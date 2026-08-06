using System.Numerics;
using System.Runtime.InteropServices;
using Arithmic;
using HelixToolkit.Maths;
using SharpDX.DirectInput;
using static Charm.Renderer.Externs;
using Quaternion = System.Numerics.Quaternion;
using Vector3 = System.Numerics.Vector3;

namespace Charm.Renderer;

public class FirstPersonCamera
{
    public enum CameraType
    {
        Perspective,
        Orthographic
    }
    public CameraType ProjectionType = CameraType.Perspective;

    public float Near = 0.05f;
    public float Far = 50000f;
    public float FOV = 60f;
    public float OrthoWidth = 2f;
    public float AspectRatio => (float)Viewport.Width / (float)Viewport.Height;

    public Viewport Viewport { get; set; }
    public BoundingFrustum Frustum { get; set; }
    public float Yaw { get; set; } = -90f;
    public float Pitch { get; set; } = 0f;
    public float Roll { get; set; } = 0f;
    public float MoveSpeed { get; set; } = 0.075f;
    public float LookSensitivity { get; set; } = 0.25f;

    public Vector3 Position { get; set; } = new Vector3(0, 0, 5);
    public Vector3 Forward { get; set; }
    public Vector3 Right { get; set; }
    public Vector3 Up { get; set; }
    public Quaternion Rotation { get; private set; }

    private Vector3 worldUp = Vector3.UnitZ;

    public Matrix4x4ButGood CameraToProjective { get; set; } = Matrix4x4ButGood.Identity; // camera_to_projective
    public Matrix4x4ButGood WorldToCamera { get; set; } = Matrix4x4ButGood.Identity; // world_to_camera

    public POINT MousePos;
    private POINT currentMouse;
    private POINT lastMouse;

    public FirstPersonCamera(Viewport viewport)
    {
        Viewport = viewport;
        UpdateVectors();
    }

    public void Move(Vector3 direction)
    {
        Position += direction * MoveSpeed;
        UpdateVectors();
    }

    public void Look(float deltaX, float deltaY)
    {
        Yaw -= deltaX * LookSensitivity;
        Pitch -= deltaY * LookSensitivity;

        Pitch = Math.Clamp(Pitch, -89f, 89f);
    }

    public void LookAt(Vector3 target)
    {
        Vector3 dir = Vector3.Normalize(target - Position);
        Pitch = MathF.Asin(dir.Z) * (180f / MathF.PI);
        Yaw = MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI);
        Pitch = Math.Clamp(Pitch, -89f, 89f);
        UpdateVectors();
    }

    public void UpdateViewMatrix()
    {
        WorldToCamera = Matrix4x4ButGood.LookAt(Position, Position + Forward, Up);
    }

    public void UpdateProjectionMatrix()
    {
        switch (ProjectionType)
        {
            case CameraType.Perspective:
                CameraToProjective = Matrix4x4ButGood.PerspectiveInfiniteReverseRightHanded(
                    float.DegreesToRadians(FOV),
                    AspectRatio,
                    Near
                );
                break;

            case CameraType.Orthographic:
                Vector2 extents = new Vector2(OrthoWidth, OrthoWidth / AspectRatio) * 0.5f;
                CameraToProjective = Matrix4x4ButGood.OrthographicRH(
                    -extents.X, extents.X, -extents.Y, extents.Y, Far, Near
                );
                break;
        }

        Frustum = new(CameraToProjective * WorldToCamera);
    }

    private OrbitMode CurrentOrbitMode = OrbitMode.None;
    private float OrbitDistance = 5f;
    private Vector3 OrbitPivot;
    public bool AutoOrbit = false;
    private float AutoOrbitAngle = 0f;
    public float AutoOrbitSpeed = 30f;
    public Vector3 AutoOrbitOffset = Vector3.Zero;
    public void Update(RenderWorld world, KeyboardState keyboard, MouseState mouse, RendererViewport viewport)
    {
        FOV = viewport.FOV;
        UpdateProjectionMatrix();
        GetCursorPos(out MousePos);

        AutoOrbit = viewport.AutoOrbit;
        AutoOrbitSpeed = viewport.AutoOrbitSpeed;
        AutoOrbitOffset = viewport.AutoOrbitOffset.ToVector3();

        lastMouse = currentMouse;
        currentMouse = MousePos;
        float mouseDeltaX = (float)(currentMouse.X - lastMouse.X);
        float mouseDeltaY = (float)(currentMouse.Y - lastMouse.Y);
        float scrollDelta = mouse.Z;
        float zoomSpeed = 0.0075f;

        Vector3 moveDir = Vector3.Zero;
        if (keyboard.IsPressed(SharpDX.DirectInput.Key.W)) moveDir += Forward;
        if (keyboard.IsPressed(SharpDX.DirectInput.Key.S)) moveDir -= Forward;
        if (keyboard.IsPressed(SharpDX.DirectInput.Key.D)) moveDir += Right;
        if (keyboard.IsPressed(SharpDX.DirectInput.Key.A)) moveDir -= Right;

        if (keyboard.IsPressed(SharpDX.DirectInput.Key.LeftControl))
        {
            zoomSpeed *= 0.1f;
            moveDir *= 0.1f;
        }
        if (keyboard.IsPressed(SharpDX.DirectInput.Key.LeftShift))
        {
            zoomSpeed *= 5f;
            moveDir *= 5f;
        }

        moveDir *= viewport.MovementSpeed;

        if (AutoOrbit)
        {
            var bbox = world.OverrideMainBB != null
                ? world.OverrideMainBB.Value
                : world.RenderObjects.FirstOrDefault()?.BoundingBox ?? new BoundingBox();

            var center = (bbox.Minimum + bbox.Maximum) / 2f;
            var extents = (bbox.Maximum - bbox.Minimum) / 2f;
            var target = center + extents * AutoOrbitOffset;

            if (CurrentOrbitMode != OrbitMode.Auto)
            {
                CurrentOrbitMode = OrbitMode.Auto;
                OrbitDistance = Vector3.Distance(Position, target);
                AutoOrbitAngle = Yaw;
            }

            if (viewport.ViewportContainer.IsMouseOver)
            {
                if (scrollDelta != 0)
                {
                    OrbitDistance -= scrollDelta * zoomSpeed;
                    OrbitDistance = MathF.Max(0.1f, OrbitDistance);
                }

                if ((mouse.Buttons[0] || mouse.Buttons[1] || mouse.Buttons[2]))
                {
                    Pitch += mouseDeltaY * -0.4f;
                }
            }

            Pitch = Math.Clamp(Pitch, -89f, 89f);
            AutoOrbitAngle += AutoOrbitSpeed * viewport.Renderer.Externs.Frame.DeltaTime;

            float yawRad = AutoOrbitAngle * MathF.PI / 180f;
            float pitchRad = Pitch * MathF.PI / 180f;

            Vector3 orbitDir = new Vector3(
                MathF.Cos(pitchRad) * MathF.Cos(yawRad),
                MathF.Cos(pitchRad) * MathF.Sin(yawRad),
                MathF.Sin(pitchRad)
            );

            Position = target - orbitDir * OrbitDistance;
            LookAt(target);
            return;
        }

        if (CurrentOrbitMode == OrbitMode.Auto)
            CurrentOrbitMode = OrbitMode.None;

        if (!viewport.ViewportContainer.IsMouseOver)
            return; // doesnt process input if mouse is over any ui or off screen, but still allows auto orbiting to work

        if (!mouse.Buttons[0])
            Move(moveDir);

        if (scrollDelta != 0)
        {
            float zoomAmount = scrollDelta * zoomSpeed;
            if (CurrentOrbitMode != OrbitMode.None)
            {
                OrbitDistance -= zoomAmount;
                OrbitDistance = MathF.Max(0.1f, OrbitDistance);
            }
            else
            {
                Position += Forward * zoomAmount;
            }
        }

        // Left click — free-pivot orbit
        if (mouse.Buttons[0])
        {
            if (CurrentOrbitMode != OrbitMode.FreePivot)
            {
                CurrentOrbitMode = OrbitMode.FreePivot;
                OrbitDistance = 10f;
                OrbitPivot = Position + Forward * OrbitDistance;
            }

            Yaw += mouseDeltaX * -0.4f;
            Pitch += mouseDeltaY * -0.4f;
            Pitch = Math.Clamp(Pitch, -89f, 89f);

            float yawRad = Yaw * MathF.PI / 180f;
            float pitchRad = Pitch * MathF.PI / 180f;

            Vector3 orbitDir = new(
                MathF.Cos(pitchRad) * MathF.Cos(yawRad),
                MathF.Cos(pitchRad) * MathF.Sin(yawRad),
                MathF.Sin(pitchRad)
            );

            Position = OrbitPivot - orbitDir * OrbitDistance;
            LookAt(OrbitPivot);
        }
        // Right click — free look
        else if (mouse.Buttons[1])
        {
            CurrentOrbitMode = OrbitMode.None;
            Look(mouseDeltaX, mouseDeltaY);
        }
        // Middle click — BBox orbit
        else if (mouse.Buttons[2])
        {
            var bbox = world.OverrideMainBB != null
                ? world.OverrideMainBB.Value
                : world.RenderObjects.FirstOrDefault()?.BoundingBox ?? new BoundingBox();

            var target = (bbox.Minimum + bbox.Maximum) / 2f;

            if (CurrentOrbitMode != OrbitMode.BBox)
            {
                CurrentOrbitMode = OrbitMode.BBox;
                OrbitDistance = Vector3.Distance(Position, target);
            }

            Yaw += mouseDeltaX * -0.4f;
            Pitch += mouseDeltaY * -0.4f;
            Pitch = Math.Clamp(Pitch, -89f, 89f);

            float yawRad = Yaw * MathF.PI / 180f;
            float pitchRad = Pitch * MathF.PI / 180f;

            Vector3 orbitDir = new Vector3(
                MathF.Cos(pitchRad) * MathF.Cos(yawRad),
                MathF.Cos(pitchRad) * MathF.Sin(yawRad),
                MathF.Sin(pitchRad)
            );
            Position = target - orbitDir * OrbitDistance;
            LookAt(target);
        }
        else
        {
            CurrentOrbitMode = OrbitMode.None;
        }

        if (keyboard.IsPressed(SharpDX.DirectInput.Key.R))
            ResetCameraTransform();
    }

    public void UpdateVectors()
    {
        //if (!MainWindow.Current.IsActive)
        //    return;

        float yawRad = MathF.PI * Yaw / 180f;
        float pitchRad = MathF.PI * Pitch / 180f;

        Forward = Vector3.Normalize(new Vector3(
            MathF.Cos(pitchRad) * MathF.Cos(yawRad),
            MathF.Cos(pitchRad) * MathF.Sin(yawRad),
            MathF.Sin(pitchRad)
        ));

        Right = Vector3.Normalize(Vector3.Cross(Forward, worldUp));
        Up = Vector3.Normalize(Vector3.Cross(Right, Forward));

        Rotation = Quaternion.CreateFromYawPitchRoll(
            MathF.PI * Yaw / 180f,
            MathF.PI * Pitch / 180f,
            0f
        );

        UpdateViewMatrix();
    }

    public void RotateAround(Vector3 pivot, float yawDegrees, float pitchDegrees)
    {
        Vector3 offset = Position - pivot;

        float yaw = MathF.PI / 180f * yawDegrees;
        float pitch = MathF.PI / 180f * pitchDegrees;

        float radius = offset.Length();
        float currentYaw = MathF.Atan2(offset.Y, offset.X);
        float currentPitch = MathF.Asin(offset.Z / radius);

        float newYaw = currentYaw + yaw;
        float newPitch = Math.Clamp(currentPitch + pitch, -MathF.PI / 2 + 0.01f, MathF.PI / 2 - 0.01f);

        float x = radius * MathF.Cos(newPitch) * MathF.Cos(newYaw);
        float y = radius * MathF.Cos(newPitch) * MathF.Sin(newYaw);
        float z = radius * MathF.Sin(newPitch);

        Position = pivot + new Vector3(x, y, z);

        LookAt(pivot);
    }

    public void ResetCameraTransform()
    {
        Position = new(-2.50f, 2.5f, 2f);
        Yaw = -45;
        Pitch = -20;
        UpdateVectors();
    }

    public Ray GetMouseRay(Viewport viewport, ExternView view)
    {
        int localX = MousePos.X - viewport.X;
        int localY = MousePos.Y - viewport.Y;
        Matrix4x4ButGood viewProj = view.WorldToCamera * view.ProjToCamera;
        var ray = Ray.GetPickRay(localX, localY, viewport, viewProj);

        Log.Debug($"{ray.Direction}");
        return ray;
    }

    public void Pick(Ray ray, RenderWorld world)
    {
        RenderObject picked = null;
        float closest = float.MaxValue;

        foreach (var obj in world.RenderObjects)
        {
            var boundingBox = obj.BoundingBox;
            if (!ray.Intersects(ref boundingBox, out float distance))
                continue;

            if (distance < closest)
            {
                closest = distance;
                picked = obj;
            }
        }

        Log.Debug($"Picked Object: {picked?.Hash}");
    }

    public Vector4 GetResolutionInverse()
    {
        int width = Viewport.Width;
        int height = Viewport.Height;
        return new(width, height, 1f / width, 1f / height);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    private enum OrbitMode
    {
        None,
        FreePivot,
        BBox,
        Auto
    }
}
