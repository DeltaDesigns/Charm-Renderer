using System.Numerics;
using HelixToolkit.Maths;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using Tiger;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    public const int NumSunShadowCascades = 4;
    public const float MaxCascadeDistance = 600.0f;
    public static readonly float[] CascadeDistances = { 10.0f, 30.0f, 100.0f, MaxCascadeDistance };

    private readonly (Matrix4x4ButGood WorldToCascade, ShaderResourceView CascadeSRV)?[] _cascadeData = new (Matrix4x4ButGood, ShaderResourceView)?[NumSunShadowCascades];

    public static (float ZNear, float ZFar) GetCascadeDistanceRange(int index)
    {
        float zNear = index == 0 ? 0.05f : CascadeDistances[index - 1];
        float zFar = CascadeDistances[index];
        return (zNear, zFar);
    }

    public void RenderShadowMask()
    {
        if (!Viewport.SunShadows)
        {
            GBuffers.ShadowMask.Clear(Context, new RawColor4(1, 1, 1, 1));
            return;
        }
        RenderHelpers.Profile("Render Shadow Mask");
        Annotation.BeginEvent("Shadow Mask");

        GBuffers.ShadowMask.Bind(Context);
        GBuffers.ShadowMask.Clear(Context, new RawColor4(1, 1, 1, 1));
        CMD.States.SetDefaultState(Context, new(0, 0, 0, 0));
        var res = GBuffers.ShadowMask.GetResolution();
        var scaleX = 2f / res.width;
        var scaleY = -2f / res.height;
        var viewportToProj = new Matrix4x4ButGood(
            new Vector4(scaleX, 0, 0, 0),
            new Vector4(0, scaleY, 0, 0),
            new Vector4(0, 0, 1, 0),
            new Vector4(-1, 1, 0, 1)
        );
        var targetPixelToWorld = Externs.View.ProjToWorld * viewportToProj;

        Vector3 sunDir = GetSunDirection();

        for (int cascadeIndex = NumSunShadowCascades - 1; cascadeIndex >= 0; cascadeIndex--)
        {
            if (_cascadeData[cascadeIndex] is not (var worldToCascade, var cascadeSRV))
                continue;

            TempScopes.UpdateCascadeScope(Context, new()
            {
                TargetPixelToWorld = targetPixelToWorld,
                CameraToProj = Externs.View.CameraToProj,
                WorldToCamera = Externs.View.WorldToCamera,
                WorldToCascade = worldToCascade,
                LightDir = sunDir.ToVector4(1),
                PlaneDistance = GetCascadeDistanceRange(cascadeIndex).ZFar
            });

            Context.VertexShader.Set(AssetManager.ShadowMapVS);
            Context.PixelShader.Set(AssetManager.ShadowMapPS);
            Context.PixelShader.SetShaderResource(0, GBuffers.Depth_Clone.DepthSRV);
            Context.PixelShader.SetShaderResource(1, cascadeSRV);
            Context.PixelShader.SetSampler(1, _linearSampler);
            DrawScreenQuad();
        }

        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }

    private void PrepareSunShadows()
    {
        if (!Viewport.SunShadows)
            return;

        RenderHelpers.Profile("Prepare Sun Shadow Cascades");
        Annotation.BeginEvent("Sun Shadow Cascades");

        Vector3 sunDir = GetSunDirection();
        CMD.States.SetDepthMode(Context, DepthMode.Forward);
        try
        {
            for (int cascadeIndex = 0; cascadeIndex < NumSunShadowCascades; cascadeIndex++)
            {
                (float ZNear, float ZFar) = GetCascadeDistanceRange(cascadeIndex);
                Camera.BuildShadowCascade(sunDir, ZNear, ZFar);
                var buffer = GBuffers.SunShadowCascades[cascadeIndex];

                buffer.Clear(Context, 1, 0);
                buffer.Set(Context);
                Context.Rasterizer.SetViewport(buffer.GetViewport());

                Externs.View.WorldToCamera = Camera.WorldToCamera;
                Externs.View.CameraToProj = Camera.CameraToProjective;
                Externs.View.UpdateMatrices(buffer.Width, buffer.Height);
                TfxScopes[Tiger.TfxScope.VIEW].Bind(this);

                CMD.States.SetDefaultState(Context, new(0, 2, 0, 6));
                Context.VertexShader.SetShaderResource(2, AssetManager.BlueTexture);
                RenderMesh(TfxRenderStage.ShadowGenerate, "Shadow Mesh");

                _cascadeData[cascadeIndex] = (Camera.CameraToProjective * Camera.WorldToCamera, buffer.DepthSRV);
            }
        }
        finally
        {
            CMD.States.SetDepthMode(Context, DepthMode.Reverse);
            Camera.UpdateViewMatrix();
            Camera.UpdateProjectionMatrix();
            Externs.View.Update(this);
            TfxScopes[Tiger.TfxScope.VIEW].Bind(this);
        }

        RenderHelpers.EndProfile();
    }

    private Vector3 GetSunDirection()
    {
        Vector3 sunDir = Externs.Atmosphere.AtmosSunDir.ToVector3();
        if (sunDir.LengthSquared() < 0.0001f)
            sunDir = Vector3.UnitZ;
        return -Vector3.Normalize(sunDir);
    }
}
