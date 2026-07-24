using SharpDX;
using SharpDX.Direct3D11;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

// Adapted from Alkahest

public partial class CharmRenderer
{
    public void RenderBloom()
    {
        RenderHelpers.Profile("Render Bloom");
        Annotation.BeginEvent("Bloom");

        void Bind(RenderTarget2D rtIn, RenderTarget2D rtOut, Vector4 UnkC0)
        {
            Context.PixelShader.SetShaderResource(0, rtIn.SRV);
            Externs.PostProcess.Unk00 = rtIn.SRV;
            Externs.PostProcess.Unk60 = rtIn.GetResolutionInverse();
            Externs.PostProcess.Unk50 = rtOut.GetResolutionInverse();
            Externs.PostProcess.UnkC0 = UnkC0;
            rtOut.Bind(Context);
        }

        CMD.States.SetStencilRef(Context, 0);
        CMD.States.CreateStates(Context, new(0, 0, 0, 0));
        Externs.PostprocessInitialDownsample.Update();

        Bind(GBuffers.Shading, GBuffers.Bloom.Bloom3rd, new(0.00f, 0.0005f, 0.016f, 0.016f));
        RenderGlobalPipeline("bloom_initial_downsample_block_2x2");

        Bind(GBuffers.Bloom.Bloom3rd, GBuffers.Bloom.Bloom6th, Vector4.UnitW);
        RenderGlobalPipeline("downsample_block_2x2_with_nan_kill");

        Bind(GBuffers.Bloom.Bloom6th, GBuffers.Bloom.Bloom12th, Vector4.Zero);
        RenderGlobalPipeline("downsample_block_2x2");

        Bind(GBuffers.Bloom.Bloom12th, GBuffers.Bloom.Bloom24th, Vector4.Zero);
        RenderGlobalPipeline("downsample_block_2x2");

        // Auto Exposure Sampling
        {
            RenderHelpers.Profile("Auto Exposure Sampling");
            Externs.PostProcess.UpdateAutoExposure(GBuffers);
            GBuffers.Bloom.AutoExposureColumns.Bind(Context);

            CMD.States.SetStencilRef(Context, 0);
            CMD.States.CreateStates(Context, new(0, 0, 0, 0));

            RenderGlobalPipeline("autoexposure_sample_columns");
            Context.CopyResource(GBuffers.Bloom.AutoExposureColumns.Texture, GBuffers.Bloom.AutoExposureColumnsStaging);
            RenderHelpers.EndProfile();
        }

        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }


    private int _frameIndex;
    private readonly AutoExposureSystem _autoexposure = new AutoExposureSystem();
    private readonly object _autoexposureColumnsLock = new object();
    public void UpdateAutoexposure(float deltaTime)
    {
        if (_frameIndex > 0 && Viewport.AutoExposure && Viewport.RenderSky)
        {
            RenderHelpers.Profile("Auto Exposure Updating");
            var autoexposureColumns = GBuffers.Bloom.AutoExposureColumnsStaging;
            int columnCount = autoexposureColumns.Description.Width;

            DataBox mappedBuffer = GPU.Instance.ImmediateContext.MapSubresource(
                autoexposureColumns, 0, MapMode.Read, MapFlags.None);

            try
            {
                var data = new Vector4[columnCount];
                unsafe
                {
                    var src = (Vector4*)mappedBuffer.DataPointer;
                    for (int i = 0; i < columnCount; i++)
                    {
                        data[i] = src[i];
                    }
                }

                ExposureResult exposureResult = _autoexposure.UpdateFromRaw(data, deltaTime);
                Externs.Frame.ExposureScale = exposureResult.ExposureScale;
                //Externs.Frame.ExposureIllumRelative = exposureResult.ExposureIllumRelative;
                //Console.WriteLine(Viewport.Exposure);
            }
            finally
            {
                GPU.Instance.ImmediateContext.UnmapSubresource(autoexposureColumns, 0);
            }
            RenderHelpers.EndProfile();
        }
        else
        {
            Externs.Frame.ExposureScale = Viewport.Exposure;
        }

        _frameIndex++;
    }

}

public struct ExposureColumn
{
    public float LogSum;
    public float LinearSum;
    public float WeightSum;
}

public struct ExposureResult
{
    public float ExposureScale;
    public float ExposureIllumRelative;
    public float SceneLuminance;
}

public class AutoExposureConfig
{
    /// <summary>Target middle grey value.</summary>
    public float TargetLuminance = 0.01f;
    public float MinLuminance = 0.0001f;
    public float MaxLuminance = 65000.0f;

    /// <summary>Speed when adapting to a brighter scene (exposure going down)</summary>
    public float SpeedDarkToLight = 2.0f; // Fast reaction to bright areas

    /// <summary>Speed when adapting to a darker scene (exposure going up)</summary>
    public float SpeedLightToDark = 1.0f; // Slow reaction to dark areas

    public float HighlightProtection = 0.30f;
}

public class AutoExposureSystem
{
    public AutoExposureConfig Config;

    // Current smoothed values applied to the frame
    public float CurrentExposureScale;
    public float CurrentIllumRelative;

    public AutoExposureSystem() : this(new AutoExposureConfig())
    {
    }

    public AutoExposureSystem(AutoExposureConfig config)
    {
        Config = config;
        CurrentExposureScale = 0.8f;
        CurrentIllumRelative = 1f;
    }

    /// <summary>
    /// Feeds raw GPU columns into the system and returns the smoothed result for this frame.
    /// </summary>
    /// <param name="rawColumns">Flat array of Vector4 data representing ExposureColumn data from the GPU.</param>
    /// <param name="deltaTime">Time in seconds since the last frame.</param>
    public ExposureResult UpdateFromRaw(IReadOnlyList<Vector4> rawColumns, float deltaTime)
    {
        var columns = new List<ExposureColumn>(rawColumns.Count);
        foreach (var col in rawColumns)
        {
            columns.Add(new ExposureColumn
            {
                LogSum = col.X,
                LinearSum = col.Y,
                WeightSum = col.Z,
            });
        }

        return Update(columns, deltaTime);
    }

    /// <summary>
    /// Feeds parsed exposure columns into the system and returns the smoothed result for this frame.
    /// </summary>
    /// <param name="deltaTime">Time in seconds since the last frame.</param>
    public ExposureResult Update(IReadOnlyList<ExposureColumn> columns, float deltaTime)
    {
        ExposureResult target = CalculateInstantTarget(columns);

        bool isAdaptingToLight = target.ExposureScale < CurrentExposureScale;

        float speed = isAdaptingToLight
            ? Config.SpeedDarkToLight
            : Config.SpeedLightToDark;

        float blendFactor = 1.0f - MathF.Exp(-speed * deltaTime);

        CurrentExposureScale = Lerp(CurrentExposureScale, target.ExposureScale, blendFactor);
        CurrentIllumRelative = Lerp(CurrentIllumRelative, target.ExposureIllumRelative, blendFactor);

        return new ExposureResult
        {
            ExposureScale = CurrentExposureScale,
            ExposureIllumRelative = CurrentIllumRelative,
            SceneLuminance = target.SceneLuminance, // Pass through raw luminance for debug info
        };
    }

    private ExposureResult CalculateInstantTarget(IReadOnlyList<ExposureColumn> columns)
    {
        float totalLogSum = 0.0f;
        float totalLinSum = 0.0f;
        float totalWeight = 0.0f;

        foreach (var col in columns)
        {
            totalLogSum += col.LogSum;
            totalLinSum += col.LinearSum;
            totalWeight += col.WeightSum;
        }

        if (totalWeight <= float.Epsilon)
        {
            return new ExposureResult
            {
                ExposureScale = CurrentExposureScale,
                ExposureIllumRelative = CurrentIllumRelative,
                SceneLuminance = Config.TargetLuminance,
            };
        }

        float avgLogLum = totalLogSum / totalWeight;
        float avgLinLum = totalLinSum / totalWeight;

        // exp2(x) == 2^x
        float sceneLuminanceGeo = MathF.Pow(2.0f, avgLogLum);

        float blendedLuminance = Lerp(sceneLuminanceGeo, avgLinLum, Config.HighlightProtection);

        float clampedLuminance = Math.Clamp(blendedLuminance, Config.MinLuminance, Config.MaxLuminance);

        float targetScale = Config.TargetLuminance / clampedLuminance;
        float targetIllum = Math.Clamp(avgLinLum, 0.0f, 1.0f);

        return new ExposureResult
        {
            ExposureScale = targetScale,
            ExposureIllumRelative = targetIllum,
            SceneLuminance = clampedLuminance,
        };
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }
}

