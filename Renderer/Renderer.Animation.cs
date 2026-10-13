using SharpDX.DirectInput;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    private bool _animationKeyDown;
    // Debug controls for animation playback: P plays the next animation of every entity, O stops them.
    private void UpdateAnimationInput()
    {
        if (!IsAppFocused())
            return;

        bool next = KeyboardState.IsPressed(Key.P);
        if (next && !_animationKeyDown)
        {
            foreach (RenderObject renderObject in SnapshotRenderObjects())
            {
                if (renderObject.CanAnimate)
                    renderObject.PlayNextAnimation(this);
            }
        }
        _animationKeyDown = next;

        if (KeyboardState.IsPressed(Key.O))
        {
            foreach (RenderObject renderObject in SnapshotRenderObjects())
            {
                if (renderObject.IsAnimating)
                    renderObject.StopAnimation();
            }
        }
    }

    private RenderObject[] SnapshotRenderObjects()
    {
        lock (World.WorldLock)
            return World.RenderObjects.ToArray();
    }
}
