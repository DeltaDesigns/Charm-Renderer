using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Charm.Shared;
using HelixToolkit.Maths;
using Microsoft.Win32;
using Tiger;
using Tiger.Schema;
using Transform = Tiger.Schema.Transform;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

// TODO: Support multiple viewports in the same window/control?
public partial class RendererViewport : UserControl, INotifyPropertyChanged, Shared.IRenderer
{
    public CharmRenderer Renderer;

    #region Debug Options
    public ObservableCollection<SettingItem> DebugSettings { get; set; }
    public bool CapFPS { get; set; } = true;
    public bool UseSkyCopyTint_Debug { get; set; } = false;
    public int ScreenshotScale { get; set; } = 1;
    public float RenderScale { get; set; } = 1f;
    #endregion

    #region Render Options
    public ObservableCollection<SettingItem> AutoOrbitSettings { get; set; }
    public ObservableCollection<SettingItem> AtmosSettings { get; set; }
    public ObservableCollection<SettingItem> RenderSettings { get; set; }
    public SliderSetting TimeOfDaySetting { get; set; }
    public SliderSetting ExposureSetting { get; set; }

    public bool AutoOrbit { get; set; } = false;
    public bool ShowGrid { get; set; } = true;
    public bool RenderSky { get; set; } = true;
    public bool RenderSkyObjs { get; set; } = true;
    public float TimeOfDay { get; set; } = 0.635f;
    public bool AutoExposure { get; set; } = true;
    public float Exposure { get; set; } = 0.25f;
    public float ExposureIllum { get; set; } = 1f;
    public bool FXAA { get; set; } = true;
    public bool Bloom { get; set; } = true;
    public bool GodRays { get; set; } = true;
    public bool HDAO { get; set; } = true;
    public float FOV { get; set; } = 60f;
    public float TimeScale { get; set; } = 1f;
    public float AtmosRotation { get; set; } = 0.50f; //0.825f;
    public float AtmosIntensity { get; set; } = 0.8f;
    public float AutoOrbitSpeed { get; set; } = 30f;
    public float MovementSpeed { get; set; } = 1f;
    public Vector4 AutoOrbitOffset { get; set; } = Vector4.Zero;
    public RenderPass DisplayPass = RenderPass.final;
    #endregion

    #region Object Options
    public ObservableCollection<SettingItem> ObjectSettings { get; set; }
    public bool ShowSkele { get; set; } = true;
    public bool ShowBB { get; set; } = false;
    public bool ShowEntChildren { get; set; } = true;
    public SliderSetting MaterialPermutationOverride { get; set; }
    public ObservableCollection<SettingItem> GroupToggles { get; set; } = new();

    public SocketCategory AllShadersCategories { get; set; }
    public ObservableCollection<RendererShaderEntry> ItemShadersCategories { get; set; }
    #endregion

    private bool _isFullscreen = false;
    private Panel _originalParent;
    private bool _isInitialized;

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public RendererViewport()
    {
        InitializeComponent();
        CreateRenderPassOptions();
        CreateSceneWorldOptions();
        CreateViewportControls();

        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    private DispatcherTimer _uiTimer;
    private void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Initialize();
    }

    public void Initialize()
    {
        Renderer?.Start();
        if (_isInitialized)
            return;

        if (Renderer == null)
            Renderer = new CharmRenderer();

        Renderer.Viewport = this;
        Renderer.Initialize((int)ActualWidth, (int)ActualHeight);
        Renderer.Start();
        _isInitialized = true;

        Stopwatch _dayCycleStopwatch = new();
        if (_uiTimer is null)
        {
            _uiTimer = new();
            _uiTimer.Interval = TimeSpan.FromMilliseconds(33);
            _uiTimer.Tick += (s, e) =>
            {
                if (Renderer is null)
                    return;

                var cam = Renderer.Camera;
                var camPos = cam.Position;
                var camRot = cam.Rotation;

                FrameTime.Text = $"CPU Time: {Renderer.DeltaTime:F5} ms";
                CameraPosition.Text = $"Camera Position: {camPos.X:F2}, {camPos.Y:F2}, {camPos.Z:F2}";
                CameraRotation.Text = $"Camera Rotation: {camRot.X:F2}, {camRot.Y:F2}, {camRot.Z:F2}, {camRot.W:F2}\n" +
                                      $"Camera Angels: {cam.Yaw:F2}, {cam.Pitch:F2}, {cam.Roll:F2}";
                FPSCounter.Text = $"FPS: {Math.Floor(Renderer.FPS)}";

                if (AutoExposure)
                {
                    Exposure = Renderer.Externs.Frame.ExposureScale;
                    ExposureSetting.NotifyValueChanged();
                }

                if (Renderer.World.UseDayCycle)
                {
                    float dt = (float)_dayCycleStopwatch.Elapsed.TotalSeconds;
                    _dayCycleStopwatch.Restart();

                    float dayLength = 60f;
                    TimeOfDay += (dt / dayLength) * TimeScale;
                    if (TimeOfDay >= 1)
                        TimeOfDay -= 1;

                    TimeOfDaySetting.NotifyValueChanged();
                }
                else
                {
                    _dayCycleStopwatch.Stop();
                }
            };
            _uiTimer.Start();
            _dayCycleStopwatch.Start();
        }
    }

    private void CreateRenderPassOptions()
    {
        List<ComboBoxItem> types = new();

        var values = Enum.GetValues(typeof(RenderPass)).Cast<RenderPass>().ToList();
        foreach (var type in values)
        {
            types.Add(new()
            {
                Content = type.GetEnumDescription(),
                Tag = type,
            });
        }

        RenderPassCombobox.ItemsSource = types;
        if (RenderPassCombobox.SelectedIndex == -1)
            RenderPassCombobox.SelectedIndex = 0;
    }

    private void CreateSceneWorldOptions()
    {
        List<ComboBoxItem> types = new();

        var values = Enum.GetValues(typeof(SceneWorld)).Cast<SceneWorld>().ToList();
        foreach (var type in values)
        {
            types.Add(new()
            {
                Content = type.GetEnumDescription(),
                Tag = type,
            });
        }
        SceneWorldCombobox.SelectedIndex = types.IndexOf(types.First(x => (SceneWorld)x.Tag is SceneWorld.Tower));
        SceneWorldCombobox.ItemsSource = types;
    }

    private void CreateViewportControls()
    {
        AutoOrbitButton.Content = new ToggleSetting
        {
            Text = "Auto Orbit",
            GetValue = () => AutoOrbit,
            SetValue = v => AutoOrbit = v
        };
        AutoOrbitSettings = new ObservableCollection<SettingItem>
        {
            new SliderSetting
            {
                Text = "Orbit Speed",
                Min = 1f,
                Max = 200f,
                GetValue = () => AutoOrbitSpeed,
                SetValue = v => AutoOrbitSpeed = v
            },
            new VectorSetting
            {
                Text = "Orbit Offset",
                Value = new EditableVector4(Vector4.Zero, EditableVector4.VectorInputType.Vec3),
                SetValue = v => AutoOrbitOffset = v.Vec4
            },
        };
        AutoOrbitOptions.ItemsSource = AutoOrbitSettings;

        ShowGridButton.Content = new ToggleSetting
        {
            Text = "Show Grid",
            GetValue = () => ShowGrid,
            SetValue = v => ShowGrid = v
        };

        ShowAtmosSettings.Content = new ToggleSetting
        {
            Text = "Render Sky",
            GetValue = () => RenderSky,
            SetValue = v =>
            {
                if (!v)
                {
                    Exposure = 1f;
                    ExposureSetting.NotifyValueChanged();
                }

                RenderSky = v;
            }
        };
        TimeOfDaySetting = new SliderSetting
        {
            Text = "Time Of Day",
            GetValue = () => TimeOfDay,
            SetValue = v => TimeOfDay = v,

            LockTooltip = "Toggles automatic day cycle.",
            IsLocked = true,
            SetLockState = locked =>
            {
                Renderer.World.UseDayCycle = !locked;
            }
        };
        AtmosSettings = new ObservableCollection<SettingItem>
        {
            new ToggleSetting
            {
                Text = "Render Sky Objects",
                GetValue = () => RenderSkyObjs,
                SetValue = v => RenderSkyObjs = v
            },
            TimeOfDaySetting,
            new SliderSetting
            {
                Text = "Sky Rotation",
                GetValue = () => AtmosRotation,
                SetValue = v => AtmosRotation = v
            },
            new SliderSetting
            {
                Text = "Sky Intensity",
                GetValue = () => AtmosIntensity,
                SetValue = v => AtmosIntensity = v
            }
        };
        AtmosOptions.ItemsSource = AtmosSettings;

        ExposureSetting = new SliderSetting
        {
            Text = "Exposure",
            Max = 5f,
            GetValue = () => Exposure,
            SetValue = v => Exposure = v,

            LockTooltip = "Toggles Autoexposure",
            IsLocked = false,
            SetLockState = locked =>
            {
                //if (!locked && DisplayPass != RenderPass.final_color_grade)
                //{
                //    DisplayPass = RenderPass.final_color_grade;
                //    RenderPassCombobox.SelectedIndex = 1;
                //}

                AutoExposure = !locked;
            }
        };

        RenderSettings = new ObservableCollection<SettingItem>
        {
            new ToggleSetting
            {
                Text = "Anti-aliasing",
                GetValue = () => FXAA,
                SetValue = v => FXAA = v
            },
            new ToggleSetting
            {
                Text = "Bloom",
                GetValue = () => Bloom,
                SetValue = v => Bloom = v
            },
            new ToggleSetting
            {
                Text = "God Rays",
                GetValue = () => GodRays,
                SetValue = v => GodRays = v
            },
            new ToggleSetting
            {
                Text = "HDAO",
                GetValue = () => HDAO,
                SetValue = v => HDAO = v
            },
            ExposureSetting,
            new SliderSetting
            {
                Text = "Exposure Illum",
                Max = 5f,
                GetValue = () => ExposureIllum,
                SetValue = v => ExposureIllum = v
            },
            new SliderSetting
            {
                Text = "Time Scale",
                Max = 25f,
                GetValue = () => TimeScale,
                SetValue = v => TimeScale = v
            },
            new SliderSetting
            {
                Text = "FOV",
                Min = 30f,
                Max = 110f,
                GetValue = () => FOV,
                SetValue = v => FOV = v
            }
        };
        RenderOptions.ItemsSource = RenderSettings;

        DebugSettings = new ObservableCollection<SettingItem>
        {
            new SliderSetting
            {
                Text = "Movement Speed",
                Min = 0.1f,
                Max = 5f,
                GetValue = () => MovementSpeed,
                SetValue = v => MovementSpeed = v
            },
            new SliderSetting
            {
                Text = "Render Scale",
                Min = 0.25f,
                Max = 2f,
                GetValue = () => RenderScale,
                SetValue = async v => RenderScale = await ChangeRenderScale(v)
            },
            new SliderSetting
            {
                Text = "Screenshot Scale",
                Min = 1f,
                Max = 4f,
                IsInt = true,
                GetValue = () => ScreenshotScale,
                SetValue = v => ScreenshotScale = (int)v
            },
            new ToggleSetting
            {
                Text = "Cap FPS",
                GetValue = () => CapFPS,
                SetValue = v => CapFPS = v
            },
            new ToggleSetting
            {
                Text = "Use Sky Copy Tint",
                GetValue = () => UseSkyCopyTint_Debug,
                SetValue = v => UseSkyCopyTint_Debug = v
            }
        };
        DebugOptions.ItemsSource = DebugSettings;

        // Object Options
        ObjectSettings = new ObservableCollection<SettingItem>
        {
            new ToggleSetting
            {
                Text = "Show Skeleton",
                GetValue = () => ShowSkele,
                SetValue = v => ShowSkele = v
            },
            new ToggleSetting
            {
                Text = "Show Bounding Box",
                GetValue = () => ShowBB,
                SetValue = v => ShowBB = v
            },
            new ToggleSetting
            {
                Text = "Show Entity Children",
                GetValue = () => ShowEntChildren,
                SetValue = v => ShowEntChildren = v
            },
        };
        ObjectOptions.ItemsSource = ObjectSettings;

        List<VectorSetting> transforms = new()
        {
            new VectorSetting
            {
                Text = "Translation",
                Value = new EditableVector4(Vector4.Zero, EditableVector4.VectorInputType.Vec3),
                SetValue = v => UpdateTranslation(v.Vec4)
            },
            new VectorSetting
            {
                Text = "Rotation",
                DragSpeed = 1f,
                Value = new EditableVector4(Vector4.Zero, EditableVector4.VectorInputType.Vec3),
                SetValue = v => UpdateRotation(v.Vec4)
            },
            new VectorSetting
            {
                Text = "Scale",
                Value = new EditableVector4(Vector4.One, EditableVector4.VectorInputType.Vec3, Vector4.One),
                SetValue = v => UpdateScale(v.Vec4),
            },
        };
        ObjectTransforms.ItemsSource = transforms;
    }

    private void UpdateTranslation(Vector4 loc)
    {
        var pos = loc.ToVector3();

        UpdateTransforms((ref Transform t) =>
        {
            t.Position = pos;
        });
    }

    private void UpdateRotation(Vector4 rot)
    {
        var quat = HelixToolkit.Maths.QuaternionHelper.RotationYawPitchRoll(
            float.DegreesToRadians(rot.X),
            float.DegreesToRadians(rot.Y),
            float.DegreesToRadians(rot.Z));

        UpdateTransforms((ref Transform t) =>
        {
            t.Quaternion = new(quat);
        });
    }

    private void UpdateScale(Vector4 scale)
    {
        var s = new Vector3(scale.X, scale.Y, scale.Z);

        UpdateTransforms((ref Transform t) =>
        {
            t.Scale = s;
        });
    }

    private void UpdateTransforms(ActionRef<Tiger.Schema.Transform> applyChange)
    {
        var objects = Renderer.World.RenderObjects;
        if (objects.Count == 0)
            return;

        foreach (var obj in objects)
        {
            ref var transform = ref obj.GlobalTransforms[0];
            applyChange(ref transform);

            obj.BoundingBox = RenderHelpers.TransformBoundingBox(
                obj.LocalBoundingBox,
                transform.Position + obj.TransformOffset.Position,
                transform.Quaternion.ToQuat() * obj.TransformOffset.Quaternion.ToQuat(),
                transform.Scale);
        }

        if (Renderer.World.OverrideMainBB is not null)
        {
            var first = objects.First().GlobalTransforms[0];

            Renderer.World.OverrideMainBB = RenderHelpers.TransformBoundingBox(
                Renderer.World.LocalOverrideMainBB.Value,
                first.Position,
                first.Quaternion.ToQuat(),
                first.Scale);
        }
    }

    private void Dropdown_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Renderer is null)
            return;

        var tag = ((sender as ComboBox).SelectedItem as ComboBoxItem).Tag;
        if (tag is not null && tag is RenderPass pass)
        {
            DisplayPass = pass;
            //if (DisplayPass != RenderPass.final_color_grade)
            //{
            //    AutoExposure = false;
            //    ExposureSetting.IsLocked = true;
            //}
        }
        else
            DisplayPass = RenderPass.final;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        Renderer?.ResizeViewport((int)(ActualWidth * RenderScale), (int)(ActualHeight * RenderScale));
    }

    // this sucks but helps with the black flickering caused by the back buffer becoming null for a frame
    private CancellationTokenSource _debounce;
    private async Task<float> ChangeRenderScale(float scale)
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(100, _debounce.Token);
            scale = Math.Clamp(scale, 0.25f, 2f);
            Renderer?.ResizeViewport((int)(ActualWidth * scale), (int)(ActualHeight * scale));
        }
        catch (TaskCanceledException) { return scale; }

        return scale;
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (Parent is not Panel)
            return;

        if (_originalParent is null)
            _originalParent = (Panel)this.Parent;

        if (!_isFullscreen)
        {
            var mainParent = FindParentGridByName(this, "MainContainer");
            if (mainParent is null)
                throw new NullReferenceException($"Renderer viewport requires a \"MainContainer\" named grid to attach to. Yes I know, this is dumb.");

            ((Panel)Parent).Children.Remove(this);
            mainParent.Children.Add(this);

            Grid.SetRow(this, 0);
            Grid.SetColumn(this, 0);
            Grid.SetColumnSpan(this, mainParent.ColumnDefinitions.Count);
        }
        else
        {

            ((Panel)Parent).Children.Remove(this);
            _originalParent.Children.Add(this);
        }

        _isFullscreen = !_isFullscreen;
    }

    private void OnRender(object sender, EventArgs args)
    {
        if (Renderer is null)
            return;

        var camPos = Renderer.Camera.Position;
        var camRot = Renderer.Camera.Rotation;

        FrameTime.Text = $"CPU Time: {Renderer.DeltaTime:F2} ms";
        CameraPosition.Text = $"Camera Position: {camPos.X:F2}, {camPos.Y:F2}, {camPos.Z:F2}";
        CameraRotation.Text = $"Camera Rotation: {camRot.X:F2}, {camRot.Y:F2}, {camRot.Z:F2}, {camRot.W:F2}";
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        Renderer?.Stop();
    }

    public void Destroy(bool fullyDestroy = false)
    {
        if (Renderer != null)
        {
            Renderer.Destroy(fullyDestroy);
            _isInitialized = false;

            SizeChanged -= OnSizeChanged;
            Unloaded -= OnUnloaded;
            Renderer = null;
        }
    }

    #region Render/debug options
    private void ResetObjectChannels_Click(object sender, RoutedEventArgs e)
    {
        Vector4 vec = ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) ? Vector4.Zero : Vector4.One;
        Renderer?.EntityObjectChannels?.ResetAllChannels(vec);
    }

    private void PrintGlobalChannels_Click(object sender, RoutedEventArgs e)
    {
        if (Renderer.World.GlobalChannels is null)
            return;

        foreach (var gc in Renderer.World.GlobalChannels.Channels)
        {
            Console.WriteLine($"Global Channel {gc.Index} ({gc.Name}) : {gc.Value}");
        }
    }
    #endregion


    private void MeshGroup_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (sender is ToggleButton tb && tb.DataContext is GroupToggleVM selected)
            {
                foreach (var item in GroupToggles)
                {
                    if (item is GroupToggleVM toggle)
                        toggle.IsChecked = ReferenceEquals(toggle, selected);
                }

                e.Handled = true;
            }
        }
    }

    private void ResetMeshGroups_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in GroupToggles)
        {
            if (item is GroupToggleVM toggle)
                toggle.IsChecked = true;
        }
    }

    private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PNG|*.png",
            FileName = $"Charm_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (dialog.ShowDialog() == true)
            Renderer.RequestScreenshot(dialog.FileName, Math.Clamp(ScreenshotScale / RenderScale, 0.25f, 2f));
    }

    public static Grid FindParentGridByName(DependencyObject start, string gridName)
    {
        DependencyObject current = start;

        while (current != null)
        {
            // Look at parent
            current = VisualTreeHelper.GetParent(current);

            // If it's a Grid, check the name
            if (current is Grid grid && grid.Name == gridName)
            {
                return grid;
            }

            // If we hit a Window or the root, bail out
            if (current is Window)
                return null;
        }

        return null;
    }
}

public class RendererShaderEntry : CharmUIElement
{
    public APIPlugItem Item { get; set; }
    public SocketCategory ShadersCategory { get; set; }
}
