using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using Tiger.Schema.Animation;
using Animation = Tiger.Schema.Animation.Animation;

namespace Charm.Renderer;

// Animation playback controls of the object panel
public partial class RendererViewport
{
    private RenderObject? _animationObject;
    private bool _updatingAnimationUI;
    private int _animationRequest;
    private int _layerRequest;

    private RetargetMode AnimationMode
    {
        get
        {
            return AnimationModeCombo.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse(tag, out RetargetMode mode) ? mode : RetargetMode.Auto;
        }
    }

    private RenderObject? FindAnimatableObject()
    {
        if (Renderer?.World is null)
            return null;

        lock (Renderer.World.WorldLock)
        {
            foreach (RenderObject renderObject in Renderer.World.RenderObjects)
            {
                if (!renderObject.IsChild && renderObject.MeshType == MeshType.Normal && renderObject.CanAnimate)
                    return renderObject;
            }
        }

        return null;
    }

    private void UpdateAnimationPanel()
    {
        RenderObject? target = FindAnimatableObject();
        if (!ReferenceEquals(target, _animationObject))
        {
            _animationObject = target;
            FillAnimationLists();
        }

        AnimationExpander.Visibility = target is null || target.GetAvailableAnimations().Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (target is null || !AnimationExpander.IsExpanded)
            return;

        _updatingAnimationUI = true;
        try
        {
            double duration = target.AnimationDuration;
            AnimationTimeSlider.IsEnabled = target.IsAnimating;
            AnimationTimeSlider.Maximum = Math.Max(duration, 0.0001);
            if (!AnimationTimeSlider.IsMouseCaptureWithin)
                AnimationTimeSlider.Value = Math.Min(target.AnimationTime, AnimationTimeSlider.Maximum);

            AnimationCurTimeText.Text = target.IsAnimating ? $"{target.AnimationTime:0.00} s" : "";
            AnimationDurationText.Text = target.IsAnimating ? $"{duration:0.00} s" : "";
            AnimationPlayToggle.IsChecked = target.IsPlaying;
            AnimationLayerWeightSlider.IsEnabled = target.HasLayer;
        }
        finally
        {
            _updatingAnimationUI = false;
        }
    }

    private void FillAnimationLists()
    {
        _updatingAnimationUI = true;
        try
        {
            AnimationCombo.ItemsSource = CreateAnimationItems();
            AnimationCombo.SelectedIndex = 0;
            AnimationLayerCombo.ItemsSource = CreateAnimationItems();
            AnimationLayerCombo.SelectedIndex = 0;
            AnimationStatusText.Text = "";
        }
        finally
        {
            _updatingAnimationUI = false;
        }
    }

    private List<ComboBoxItem> CreateAnimationItems()
    {
        var items = new List<ComboBoxItem> { new() { Content = "None", Tag = null } };
        if (_animationObject is null)
            return items;

        var animations = _animationObject.GetAvailableAnimations();
        for (int i = 0; i < animations.Count; i++)
            items.Add(new ComboBoxItem { Content = $"{i + 1}: {animations[i].Hash}-{animations[i].TagData.LayerType}", Tag = animations[i] });

        return items;
    }

    private void AnimationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingAnimationUI)
            return;

        LoadSelectedAnimation();
    }

    private void AnimationLayerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingAnimationUI)
            return;

        LoadSelectedLayer();
    }

    private void AnimationModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingAnimationUI || _animationObject is null)
            return;

        LoadSelectedAnimation();
        LoadSelectedLayer();
    }

    private async void LoadSelectedAnimation()
    {
        RenderObject? target = _animationObject;
        if (target is null)
            return;

        RetargetMode mode = AnimationMode;
        int request = ++_animationRequest;
        if (AnimationCombo.SelectedItem is not ComboBoxItem { Tag: Animation animation })
        {
            target.SetAnimation(null);
            AnimationStatusText.Text = "";
            return;
        }

        var frames = await Task.Run(() =>
        {
            SkinningFrames? built = target.BuildAnimation(animation, mode);
            return built;
        });

        if (request != _animationRequest)
            return;

        if (frames is null)
            return;

        target.SetAnimation(frames);
        AnimationStatusText.Text = $"{frames.FrameCount} frames, {frames.Duration:0.00} s";
    }

    private async void LoadSelectedLayer()
    {
        RenderObject? target = _animationObject;
        if (target is null)
            return;

        RetargetMode mode = AnimationMode;
        int request = ++_layerRequest;
        if (AnimationLayerCombo.SelectedItem is not ComboBoxItem { Tag: Animation animation })
        {
            target.SetLayer(null);
            return;
        }

        var frames = await Task.Run(() =>
        {
            SkinningFrames? built = target.BuildAnimation(animation, mode);
            return built;
        });

        if (request != _layerRequest)
            return;

        if (frames is null)
            return;

        target.SetLayer(frames);
    }

    private void AnimationPlayNext(object sender, RoutedEventArgs e) => StepAnimation(previous: false);

    private void AnimationPlayPrevious(object sender, RoutedEventArgs e) => StepAnimation(previous: true);

    private void StepAnimation(bool previous)
    {
        if (_updatingAnimationUI || _animationObject is null)
            return;

        int index = _animationObject.PlayNextAnimation(Renderer, previous, AnimationCombo.SelectedIndex - 1);
        if (index < 0)
            return;

        _updatingAnimationUI = true;
        try
        {
            AnimationCombo.SelectedIndex = index + 1;
        }
        finally
        {
            _updatingAnimationUI = false;
        }

        _animationRequest++;
        _animationObject.IsPlaying = true;
    }

    private void ResetAnimation_Click(object sender, RoutedEventArgs e)
    {
        if (_animationObject is null)
            return;

        AnimationCombo.SelectedIndex = 0;
        AnimationLayerCombo.SelectedIndex = 0;

        _animationObject.IsPlaying = false;
    }

    private void AnimationPlayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingAnimationUI || _animationObject is null)
            return;

        if (_animationObject.AnimationTime >= _animationObject.AnimationDuration)
            _animationObject.AnimationTime = 0;

        _animationObject.IsPlaying = AnimationPlayToggle.IsChecked == true;
    }

    private void AnimationLoopToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingAnimationUI || _animationObject is null)
            return;

        _animationObject.LoopAnimation = AnimationLoopToggle.IsChecked == true;
        _animationObject.MarkPoseDirty();
    }

    private void AnimationTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingAnimationUI || _animationObject is null)
            return;

        _animationObject.IsPlaying = false;
        _animationObject.AnimationTime = e.NewValue;
    }

    private void UpdateAnimationSpeed(Vector4 v)
    {
        if (_animationObject is not null)
            _animationObject.AnimationSpeed = v.X;
    }

    private void AnimationLayerWeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AnimationLayerWeightText is null)
            return;

        AnimationLayerWeightText.Text = $"{e.NewValue:0.00}";
        if (_animationObject is not null)
        {
            _animationObject.LayerWeight = (float)e.NewValue;
            _animationObject.MarkPoseDirty();
        }
    }
}
