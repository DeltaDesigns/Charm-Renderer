using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

// Adapted from Alkahest

public partial class CharmRenderer
{
    private int _exposureIndex = 0;
    private float _addedDeltaTime;
    private readonly AutoExposureSystem _autoexposure = new AutoExposureSystem();

    public void RenderBloom()
    {
        var buffers = GBuffers.Bloom;
        if (!Viewport.AutoExposure && !Viewport.Bloom)
        {
            Externs.ScreenArea.Unk40 = AssetManager.Get().BlackTextureWAlpha;
            buffers.BloomFinal.Clear(Context, new RawColor4(0, 0, 0, 1));
            return;
        }

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

        void BindScope(RenderTarget2D rtIn, RenderTarget2D rtOut, ReadOnlySpan<Vector4> vals)
        {
            Context.PixelShader.SetShaderResource(0, rtIn.SRV);
            TempScopes.UpdatePostProcessScope(Context,
                new ScopePostProcess
                {
                    OutRes = rtOut.GetResolutionInverse(),
                    InRes = rtIn.GetResolutionInverse(),
                    Unk02 = Vector4.Zero,
                    Unk03 = vals[0],
                    Unk04 = vals[1],
                    Unk05 = vals[2],
                    Unk06 = vals[3],
                    Unk07 = vals[4],
                });
        }

        void Blur(RenderTarget2D rtIn,
            RenderTarget2D temp,
            BlurVariant variant,
            bool stripAlpha,
            Vector4 horzUnk03,
            Vector4 horzUnk04,
            Vector4 horzUnk05,
            Vector4 vertUnk03,
            Vector4 vertUnk04,
            Vector4 vertUnk05)
        {
            Bind(rtIn, temp, Vector4.Zero);
            BindScope(rtIn, temp, [horzUnk03, horzUnk04, horzUnk05, Vector4.One, Vector4.Zero]);

            switch (variant)
            {
                case BlurVariant.Gaussian10:
                    RenderGlobalPipeline("gaussian_10_horz");
                    break;
                case BlurVariant.Weighted6:
                    RenderGlobalPipeline("weighted_6_horz");
                    break;
            }

            Bind(temp, rtIn, Vector4.Zero);
            BindScope(temp, rtIn, [ vertUnk03, vertUnk04, vertUnk05,
                stripAlpha ? new Vector4(1,1,1,0) : Vector4.One,
                stripAlpha ? Vector4.UnitW : Vector4.Zero ]);

            switch (variant)
            {
                case BlurVariant.Gaussian10:
                    RenderGlobalPipeline("gaussian_10_vert");
                    break;
                case BlurVariant.Weighted6:
                    RenderGlobalPipeline("weighted_6_vert");
                    break;
            }
        }

        CMD.States.SetStencilRef(Context, 0);
        CMD.States.CreateStates(Context, new(0, 0, 0, 0));

        Bind(GBuffers.Shading, buffers.Bloom3rd, new(0.00f, 0.0005f, 0.016f, 0.016f));
        RenderGlobalPipeline("bloom_initial_downsample_block_2x2");

        Bind(buffers.Bloom3rd, buffers.Bloom6th, Vector4.UnitW);
        RenderGlobalPipeline("downsample_block_2x2_with_nan_kill");

        Bind(buffers.Bloom6th, buffers.Bloom12th, Vector4.Zero);
        RenderGlobalPipeline("downsample_block_2x2");

        Bind(buffers.Bloom12th, buffers.Bloom24th, Vector4.Zero);
        RenderGlobalPipeline("downsample_block_2x2");

        // Auto Exposure Sampling
        {
            RenderHelpers.Profile("Auto Exposure Sampling");
            Externs.PostProcess.UpdateAutoExposure(GBuffers);
            buffers.AutoExposureColumns.Bind(Context);

            CMD.States.SetStencilRef(Context, 0);
            CMD.States.CreateStates(Context, new(0, 0, 0, 0));

            RenderGlobalPipeline("autoexposure_sample_columns");
            //Context.CopyResource(buffers.AutoExposureColumns.Texture, buffers.AutoExposureColumnsStaging);
            Context.CopyResource(buffers.AutoExposureColumns.Texture, GBuffers.Bloom.ExposureStagings[_exposureIndex]);
            RenderHelpers.EndProfile();
        }

        // Bloom
        if (Viewport.Bloom)
        {
            {
                Bind(buffers.Bloom12th, buffers.Bloom12thHalfW, Vector4.Zero);
                BindScope(buffers.Bloom12th, buffers.Bloom12thHalfW, new Vector4[]
                {
                    new(0.12667f, 0.37333f, 0.00f, 0.00f),
                    new(0.01793f, 0.00547f, 0.00f, 0.00f),
                    Vector4.Zero,
                    new(2f, 2f, 2f, 1f),
                    Vector4.Zero,
                });
                RenderGlobalPipeline("downsample_gaussian_8x1");
            }

            {
                Bind(buffers.Bloom12thHalfW, buffers.Bloom12thQuarterW, Vector4.Zero);
                BindScope(buffers.Bloom12thHalfW, buffers.Bloom12thQuarterW, new Vector4[]
                {
                    new(0.12667f, 0.37333f, 0.00f, 0.00f),
                    new(0.03586f, 0.01094f, 0.00f, 0.00f),
                    Vector4.Zero,
                    new(2f, 2f, 2f, 1f),
                    Vector4.Zero,
                });
                RenderGlobalPipeline("downsample_gaussian_8x1");
            }


            {
                Bind(buffers.Bloom12thQuarterW, buffers.Bloom12thQuarterWTemp, Vector4.Zero);
                BindScope(buffers.Bloom12thQuarterW, buffers.Bloom12thQuarterWTemp, new Vector4[]
                {
                    new(0.04734f, 0.0858f, 0.14793f, 0.21893f),
                    new(0.17344f, 0.12284f, 0.00f, 0.00f),
                    new(0.07344f, 0.02284f, 0.00f, 0.00f),
                    new(2f, 2f, 2f, 1f),
                    Vector4.Zero,
                });
                RenderGlobalPipeline("downsample_gaussian_16x1");
            }

            {
                Bind(buffers.Bloom12thQuarterWTemp, buffers.Bloom12thQuarterW, Vector4.Zero);
                BindScope(buffers.Bloom12thQuarterWTemp, buffers.Bloom12thQuarterW, new Vector4[]
                {
                    new(0.04667f, 0.08f, 0.14f, 0.23333f),
                    new(0, 0, 0, 0),
                    new(0, 0, 0, 0),
                    new(2f, 2f, 2f, 1f),
                    Vector4.Zero,
                });
                RenderGlobalPipeline("downsample_gaussian_16x1");
            }

            Blur(buffers.Bloom24th, buffers.Bloom24thTemp, BlurVariant.Gaussian10, true,
                new Vector4(0.05882f, 0.17647f, 0.52941f, 0.00f),
                new Vector4(-0.05625f, -0.02917f, -0.00625f, 0.01111f),
                new Vector4(0.01667f, 0.04375f, 0.00f, 0.01111f),
                new Vector4(0.05882f, 0.17647f, 0.52941f, 0.00f),
                new Vector4(-0.10f, -0.05185f, -0.01111f, 0.00625f),
                new Vector4(0.02963f, 0.07778f, 0.00f, 0.00625f));

            {
                Bind(buffers.Bloom24th, buffers.Bloom12thComb, Vector4.Zero);
                Externs.PostProcess.Unk08 = buffers.Bloom12th.SRV;
                Externs.PostProcess.UnkC0 = new(0.75f, 1.30f, 2.50f, 1.00f);
                Externs.PostProcess.UnkD0 = new(0.64f, 1.07f, 2.14f, 1.00f);
                Externs.PostProcess.UnkE0 = new(1.00f, 1.00f, 0.00f, 0.00f);
                Externs.PostProcess.UnkF0 = new(1.00f, 1.00f, 0.00f, 0.00f);
                RenderGlobalPipeline("weighted_add");

                Blur(buffers.Bloom12thComb, buffers.Bloom12thTemp, BlurVariant.Gaussian10, false,
                    new Vector4(0.05882f, 0.17647f, 0.52941f, 0.00f),
                    new Vector4(-0.02813f, -0.01458f, -0.00313f, 0.00556f),
                    new Vector4(0.00833f, 0.02187f, 0.00f, 0.00556f),
                    new Vector4(0.05882f, 0.17647f, 0.52941f, 0.00f),
                    new Vector4(-0.05f, -0.02593f, -0.00556f, 0.00313f),
                    new Vector4(0.01481f, 0.03889f, 0.00f, 0.00313f));
            }

            {
                Bind(buffers.Bloom12thComb, buffers.Bloom6thComb, Vector4.Zero);
                Externs.PostProcess.Unk08 = buffers.Bloom6th.SRV;
                Externs.PostProcess.UnkC0 = new(1.00f, 1.00f, 1.00f, 1.00f);
                Externs.PostProcess.UnkD0 = new(1.80f, 2.025f, 2.40f, 1.00f);
                Externs.PostProcess.UnkE0 = new(1.00f, 1.00f, 0.00f, 0.00f);
                Externs.PostProcess.UnkF0 = new(1.00f, 1.00f, 0.00f, 0.00f);
                RenderGlobalPipeline("weighted_add");

                Blur(buffers.Bloom6thComb, buffers.Bloom6thTemp, BlurVariant.Gaussian10, false,
                    new Vector4(0.05882f, 0.17647f, 0.52941f, 0.00f),
                    new Vector4(-0.01406f, -0.00729f, -0.00156f, 0.00278f),
                    new Vector4(0.00417f, 0.01094f, 0.00f, 0.00278f),
                    new Vector4(0.05882f, 0.17647f, 0.52941f, 0.00f),
                    new Vector4(-0.025f, -0.01296f, -0.00278f, 0.00156f),
                    new Vector4(0.00741f, 0.01944f, 0.00f, 0.00156f));
            }


            {
                Bind(buffers.Bloom6thComb, buffers.Bloom3rdComb, Vector4.Zero);
                Externs.PostProcess.Unk08 = buffers.Bloom3rd.SRV;
                Externs.PostProcess.Unk10 = buffers.Bloom12thQuarterW.SRV;
                Externs.PostProcess.UnkC0 = new(1.00f, 1.00f, 1.00f, 1.00f);
                Externs.PostProcess.UnkD0 = new(2.75f, 2.75f, 2.75f, 0.00f);
                Externs.PostProcess.UnkE0 = new(0.01f, 0.01f, 0.02f, 0.00f);
                RenderGlobalPipeline("combined_bloom_line_blur");

                Blur(buffers.Bloom3rdComb, buffers.Bloom3rdTemp, BlurVariant.Weighted6, false,
                    new Vector4(0.25f, 0.50f, 0.00f, 0.00f),
                    new Vector4(0.00f, -0.00359f, -0.00078f, 0.00139f),
                    new Vector4(0.00203f, 0.00f, 0.00f, 0.00139f),
                    new Vector4(0.25f, 0.50f, 0.00f, 0.00f),
                    new Vector4(0.00f, -0.00639f, -0.00139f, 0.00078f),
                    new Vector4(0.00361f, 0.00f, 0.00f, 0.00078f));
            }

            Bind(buffers.Bloom3rdTemp, buffers.BloomFinal, Vector4.One);
            RenderGlobalPipeline("copy_texture_bilinear");
            Externs.ScreenArea.Unk40 = buffers.BloomFinal.SRV;
        }
        else
        {
            Externs.ScreenArea.Unk40 = AssetManager.Get().BlackTextureWAlpha;
            buffers.BloomFinal.Clear(Context, new RawColor4(0, 0, 0, 1));
        }

        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }

    public void UpdateAutoexposure(float deltaTime)
    {
        if (_frameCounter > 0 && Viewport.AutoExposure && Viewport.RenderSky)
        {
            _addedDeltaTime += deltaTime;
            if (_frameCounter % 2 != 0)
                return;

            RenderHelpers.Profile("Auto Exposure Updating");

            //var autoexposureColumns = GBuffers.Bloom.AutoExposureColumnsStaging;
            //int columnCount = autoexposureColumns.Description.Width;

            //DataBox mappedBuffer = GPU.Instance.ImmediateContext.MapSubresource(
            //    autoexposureColumns, 0, MapMode.Read, MapFlags.None);

            int index = (_exposureIndex + 1) % BloomBuffers.ExposureBufferCount;
            var autoexposureColumns = GBuffers.Bloom.ExposureStagings[index];
            int columnCount = autoexposureColumns.Description.Width;

            DataBox mappedBuffer = GPU.Instance.ImmediateContext.MapSubresource(
                autoexposureColumns, 0, MapMode.Read, MapFlags.None);

            try
            {
                ReadOnlySpan<Vector4> data;
                unsafe
                {
                    data = new ReadOnlySpan<Vector4>((void*)mappedBuffer.DataPointer, columnCount);
                }

                ExposureResult exposureResult = _autoexposure.UpdateFromRaw(data, _addedDeltaTime);
                Externs.Frame.ExposureScale = exposureResult.ExposureScale;
                //Externs.Frame.ExposureIllumRelative = exposureResult.ExposureIllumRelative;
            }
            finally
            {
                GPU.Instance.ImmediateContext.UnmapSubresource(autoexposureColumns, 0);
                _addedDeltaTime = 0.0f;
            }
            _exposureIndex = (_exposureIndex + 1) % BloomBuffers.ExposureBufferCount;
            RenderHelpers.EndProfile();
        }
        else
        {
            Externs.Frame.ExposureScale = Viewport.Exposure;
            _addedDeltaTime = 0.0f;
        }
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
    public float TargetLuminance = 0.0075f;
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
    public ExposureResult UpdateFromRaw(ReadOnlySpan<Vector4> rawColumns, float deltaTime)
    {
        var columns = new List<ExposureColumn>(rawColumns.Length);
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

public enum BlurVariant
{
    Gaussian10,
    Weighted6,
}

