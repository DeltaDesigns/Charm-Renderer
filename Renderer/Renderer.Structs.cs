using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

[StructLayout(LayoutKind.Sequential)]
public struct Matrix4x4ButGood
{
    public Vector4 X;
    public Vector4 Y;
    public Vector4 Z;
    public Vector4 W;

    public Matrix4x4ButGood(Vector4 x, Vector4 y, Vector4 z, Vector4 w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public static Matrix4x4ButGood FromScale(in Vector3 scale)
    {
        return new()
        {
            X = new(scale.X, 0, 0, 0),
            Y = new(0, scale.Y, 0, 0),
            Z = new(0, 0, scale.Z, 0),
            W = new(0, 0, 0, 1)
        };
    }

    public Matrix4x4ButGood WithW(Vector4 w)
    {
        return new Matrix4x4ButGood(X, Y, Z, w);
    }

    public Matrix4x4ButGood Transpose()
    {
        return Matrix4x4.Transpose(this);
    }

    public static Matrix4x4ButGood Identity => new Matrix4x4ButGood
    {
        X = new Vector4(1, 0, 0, 0),
        Y = new Vector4(0, 1, 0, 0),
        Z = new Vector4(0, 0, 1, 0),
        W = new Vector4(0, 0, 0, 1)
    };

    public static Matrix4x4ButGood Zero => new Matrix4x4ButGood
    {
        X = new Vector4(0, 0, 0, 0),
        Y = new Vector4(0, 0, 0, 0),
        Z = new Vector4(0, 0, 0, 0),
        W = new Vector4(0, 0, 0, 0)
    };

    public static Matrix4x4ButGood LookTo(in Vector3 eye, in Vector3 dir, in Vector3 up)
    {
        var f = Vector3.Normalize(dir);
        var s = Vector3.Normalize(Vector3.Cross(f, up));
        var u = Vector3.Cross(s, f);

        return new Matrix4x4ButGood
        {
            X = new Vector4(s.X, u.X, -f.X, 0.0f),
            Y = new Vector4(s.Y, u.Y, -f.Y, 0.0f),
            Z = new Vector4(s.Z, u.Z, -f.Z, 0.0f),
            W = new Vector4(-Vector3.Dot(eye, s), -Vector3.Dot(eye, u), Vector3.Dot(eye, f), 1.0f)
        };
    }

    public static Matrix4x4ButGood LookAt(in Vector3 eye, in Vector3 center, in Vector3 up)
    {
        return LookTo(eye, center - eye, up);
    }

    public static Matrix4x4ButGood PerspectiveInfiniteReverseRightHanded(float fov, float aspect, float zNear)
    {
        float f = 1.0f / MathF.Tan(fov / 2.0f);
        // Perspective infinite reverse rh projection matrix
        return new Matrix4x4ButGood
        {
            X = new Vector4(f / aspect, 0.0f, 0.0f, 0.0f),
            Y = new Vector4(0.0f, f, 0.0f, 0.0f),
            Z = new Vector4(0.0f, 0.0f, 0.0f, -1.0f),
            W = new Vector4(0.0f, 0.0f, zNear, 0.0f),
        };
    }

    public static Matrix4x4ButGood OrthographicRH(
        float left,
        float right,
        float bottom,
        float top,
        float near,
        float far)
    {
        float rcpWidth = 1.0f / (right - left);
        float rcpHeight = 1.0f / (top - bottom);
        float r = 1.0f / (near - far);
        return new Matrix4x4ButGood(
            new Vector4(rcpWidth + rcpWidth, 0.0f, 0.0f, 0.0f),
            new Vector4(0.0f, rcpHeight + rcpHeight, 0.0f, 0.0f),
            new Vector4(0.0f, 0.0f, r, 0.0f),
            new Vector4(-(left + right) * rcpWidth, -(top + bottom) * rcpHeight, r * near, 1.0f)
        );
    }

    public Matrix4x4ButGood Invert()
    {
        System.Numerics.Matrix4x4.Invert(this, out System.Numerics.Matrix4x4 result);
        return result;
    }

    public static Matrix4x4ButGood operator *(Matrix4x4ButGood left, Matrix4x4ButGood right)
    {
        return new Matrix4x4ButGood
        {
            X = left.X * right.X.X + left.Y * right.X.Y + left.Z * right.X.Z + left.W * right.X.W,
            Y = left.X * right.Y.X + left.Y * right.Y.Y + left.Z * right.Y.Z + left.W * right.Y.W,
            Z = left.X * right.Z.X + left.Y * right.Z.Y + left.Z * right.Z.Z + left.W * right.Z.W,
            W = left.X * right.W.X + left.Y * right.W.Y + left.Z * right.W.Z + left.W * right.W.W
        };
    }

    public static Matrix4x4ButGood operator *(Matrix4x4ButGood left, float right)
    {
        return new Matrix4x4ButGood
        {
            X = left.X * right,
            Y = left.Y * right,
            Z = left.Z * right,
            W = left.W * right
        };
    }

    public static Matrix4x4ButGood operator /(Matrix4x4ButGood left, float right)
    {
        return new Matrix4x4ButGood
        {
            X = left.X / right,
            Y = left.Y / right,
            Z = left.Z / right,
            W = left.W / right
        };
    }

    public static implicit operator Matrix4x4(Matrix4x4ButGood m)
    {
        return Unsafe.As<Matrix4x4ButGood, Matrix4x4>(ref m);
    }

    public static implicit operator Matrix4x4ButGood(Matrix4x4 m)
    {
        return Unsafe.As<Matrix4x4, Matrix4x4ButGood>(ref m);
    }
}

public enum RenderPass
{
    [Description("Final")] final,
    [Description("Final (Color Graded)")] final_color_grade,

    // GBuffer
    [Description("Albedo")] debug_source_color,
    [Description("Albedo+AO")] debug_ambient_occlusion_source_color,
    [Description("Normals")] debug_world_normal,
    [Description("Metal")] debug_metalness,
    [Description("Ambient Occlusion")] debug_texture_ao, //debug_ambient_occlusion,
    [Description("Smoothness")] debug_specular_smoothness,
    [Description("Emission")] debug_emissive,
    [Description("Emission Intensity")] debug_emissive_intensity,
    [Description("Transmission")] debug_transmission,
    [Description("Iridescense ID")] debug_colored_overcoat_id,

    // Diffuse
    [Description("Diffuse Color")] debug_diffuse_color,
    [Description("Diffuse Light")] debug_diffuse_light,
    [Description("Diffuse IBL")] debug_diffuse_ibl,
    [Description("Diffuse Only")] debug_diffuse_only,

    // Specular
    [Description("Specular Color")] debug_specular_color,
    [Description("Specular Light")] debug_specular_light,
    [Description("Specular IBL")] debug_specular_ibl,
    [Description("Specular Only")] debug_specular_only,

    // Depth
    [Description("Depth")] debug_depth,
    [Description("Depth Edges")] debug_depth_edges,

    // Misc
    [Description("Normal Edges")] debug_normal_edges,
    [Description("Grey Diffuse")] debug_grey_diffuse,
    [Description("Luminance")] debug_source_color_luminance,
    [Description("GBuffer Overdraw")] debug_gbuffer_overdraw,
    [Description("Smoothness Heatmap")] debug_valid_smoothness_heatmap,
    [Description("Metalness Heatmap")] debug_valid_layered_metalness,

    //[Description("test")] autoexposure_display,
}
