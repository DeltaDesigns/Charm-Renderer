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
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;
using Transform = Tiger.Schema.Transform;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

// TODO: Support multiple viewports?
// Gonna need lots of reworking in here and in the renderer to remove reliance on Instance (singleton)

public partial class RendererViewport : UserControl, INotifyPropertyChanged, Shared.IRenderer
{
    public CharmRenderer Renderer;

    #region Debug Options
    public ObservableCollection<SettingItem> DebugSettings { get; set; }
    public bool ShowGrid { get; set; } = true;
    public bool CapFPS { get; set; } = true;
    #endregion

    #region Render Options
    public bool AutoOrbit { get; set; } = false;
    public ObservableCollection<SettingItem> AutoOrbitSettings { get; set; }
    public ObservableCollection<SettingItem> AtmosSettings { get; set; }
    public SliderSetting TimeOfDaySetting { get; set; }
    public SliderSetting ExposureSetting { get; set; }
    public bool RenderSky { get; set; } = true;
    public bool RenderSkyObjs { get; set; } = true;
    public float TimeOfDay { get; set; } = 0.675f;
    public float Exposure { get; set; } = 0.8f;
    public float ExposureIllum { get; set; } = 1f;
    public bool AutoExposure { get; set; } = false;
    public float FOV { get; set; } = 60f;
    public float TimeScale { get; set; } = 1f;
    public float AtmosRotation { get; set; } = 0.50f; //0.825f;
    public float AtmosIntensity { get; set; } = 0.75f;
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
            SetValue = v => RenderSky = v
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

            LockTooltip = "Toggles autoexposure (Not the best)",
            IsLocked = true,
            SetLockState = locked =>
            {
                if (!locked && DisplayPass != RenderPass.final_color_grade)
                {
                    DisplayPass = RenderPass.final_color_grade;
                    RenderPassCombobox.SelectedIndex = 1;
                }

                AutoExposure = !locked;
            }
        };
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
            },
            new ToggleSetting
            {
                Text = "Cap FPS",
                GetValue = () => CapFPS,
                SetValue = v => CapFPS = v
            },
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
            if (DisplayPass != RenderPass.final_color_grade)
            {
                AutoExposure = false;
                ExposureSetting.IsLocked = true;
            }
        }
        else
            DisplayPass = RenderPass.final;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        Renderer?.OnSizeChanged();
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
        Renderer?.EntityObjectChannels?.ResetAllChannels();
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

    #region Mesh Loading (Temp?)
    public void LoadStatic(FileHash hash)
    {
        if (Renderer is null)
            Initialize();

        Renderer.Stop();
        Renderer.LoadStatic(hash);
        Renderer.Start();
    }

    private Entity _currentEntity; // temp
    public async void LoadEntity(FileHash hash)
    {
        if (Renderer is null)
            Initialize();

        var entity = FileResourcer.Get().GetFile<Entity>(hash, shouldCache: false);
        _currentEntity = entity;

        await Task.Run(() =>
        {
            Dispatcher.Invoke(() =>
            {
                CreateMaterialVariants(entity);
            });
        });

        Renderer.Stop();
        Renderer.LoadEntity(_currentEntity);
        Renderer.Start();
    }

    public void CreateMeshGroups(Entity entity)
    {
        MeshGroupsExpander.Visibility = Visibility.Visible;
        var parts = entity.Load(ExportDetailLevel.MostDetailed, LoadLevel.Minimal);
        parts.AddRange(entity.GetEntityChildren()?.SelectMany(x => x.Load(ExportDetailLevel.MostDetailed, LoadLevel.Minimal)).ToList());

        var groupIndices = parts
            .Select(m => m.GroupIndex)
            .Distinct()
            .OrderBy(i => i);

        GroupToggles.Clear();
        foreach (int idx in groupIndices)
        {
            var vm = new GroupToggleVM(idx);
            vm.VisibilityChanged += (i, visible) => Renderer.GroupVisibility.SetVisible(i, visible);

            GroupToggles.Add(vm);
        }
        MeshGroups.ItemsSource = GroupToggles;
    }

    // TODO move into own control? also support physics model? though idk if those would be the same as their regular model
    private void CreateMaterialVariants(Entity entity)
    {
        MaterialPermutationsExpander.Visibility = Visibility.Collapsed;
        MaterialVariantPanel.Children.Clear();
        //UsedMaterialsList.ItemsSource = null;

        if (entity.ModelParent is null || entity.ModelParent.Reader.ExternalMaterialsMap.Count == 0)
            return;

        int variantCount = entity.ModelParent.Reader.ExternalMaterialsMap
                        .Enumerate(entity.ModelParent.GetReader())
                        //.Where(m => m.Unk08 != 0)
                        .Select(m => (int)m.MaterialCount)
                        .Max();

        var permutations = entity.ModelParent.MaterialPermutations;
        if (permutations is null && variantCount == 0)
            return;

        if (permutations is null && variantCount != 0) // ehhh
        {
            entity.ModelParent.MaterialPermutations = new();
            permutations = entity.ModelParent.MaterialPermutations;
            MaterialPermutationsExpander.Visibility = Visibility.Visible;
        }

        // Makes some objects and combatants look like their "default" appearance, but messes with others so idk
        //if (permutations.Keys.Count != 0)
        //permutations.OverrideIndex = variantCount - 1;

        MaterialPermutationOverride = new SliderSetting()
        {
            Max = variantCount,
            Min = -1,
            Text = "Override Index",
            GetValue = () => permutations.OverrideIndex,
            SetValue = v =>
            {
                permutations.OverrideIndex = (int)Math.Floor(v);
                MaterialPermutationOverride.NotifyValueChanged();
                //ReloadEntity();
            }
        };
        PermIndexOverride.Content = MaterialPermutationOverride;

        foreach (var permutation in permutations.Keys)
        {
            ComboBoxControl matVariants = new();
            matVariants.Text = GlobalStrings.Get().GetString(permutation.Key);
            matVariants.TextFontSize = 16;
            matVariants.Margin = new Thickness(5);

            matVariants.Box.Tag = permutation.Key;

            var entries = new List<ComboBoxItem>();
            foreach (var value in permutation.Value)
            {
                if (value == 0x871AC0EA)
                    continue;

                entries.Add(new ComboBoxItem()
                {
                    Content = $"{GlobalStrings.Get().GetString(value)}",
                    Tag = value
                });
            }

            //entries.Insert(0, new()
            //{
            //    Content = "Default",
            //    Tag = (uint)0,
            //});

            //if (entries.Count != 0 && matVariants.Box.SelectedIndex == -1)
            //{
            //    matVariants.Box.SelectedIndex = 0;
            //}

            matVariants.Box.ItemsSource = entries;
            matVariants.Box.SelectionChanged += MaterialVariant_OnSelectionChanged;

            MaterialVariantPanel.Children.Add(matVariants);
        }

        if (MaterialVariantPanel.Children.Count > 0)
            MaterialPermutationsExpander.Visibility = Visibility.Visible;
    }

    private void MaterialVariant_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var permutations = _currentEntity.ModelParent.MaterialPermutations;
        var selection = (sender as ComboBox);
        var newConfig = new Dictionary<uint, uint>();
        foreach (var child in MaterialVariantPanel.Children)
        {
            if (child is ComboBoxControl comboBoxControl)
            {
                var comboBox = comboBoxControl.Box;
                if (comboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    newConfig.TryAdd((uint)comboBox.Tag, (uint)selectedItem.Tag);
                }
            }
        }

        ModelPermutation.UpdateConfiguration(permutations, newConfig);
        var permIndex = permutations.CalculatePermutationIndex();
        PermIndexDebug.Text = permIndex.HasValue ? $"Permutation Index: {permIndex}" : "Permutation Index: N/A";

        //Console.WriteLine($"\nUpdated Configuration:");
        //Console.WriteLine($"Permutation Index: {permIndex}");
        //foreach (var kvp in permutations.Configuration)
        //{
        //    var k = GlobalStrings.Get().GetString(kvp.Key);
        //    var v = GlobalStrings.Get().GetString(kvp.Value);
        //    Console.WriteLine($"Key: {k}, Value: {v}");
        //}

        //ReloadEntity();
    }

    #region Investment
    public void LoadInvestmentItem(InventoryItem item)
    {
        if (Renderer is null)
            Initialize();

        PerItemShadersPanel.Visibility = Visibility.Collapsed;
        CreateInvestmentShaders();
        Renderer.Pause();
        Renderer.LoadInvestmentItem(item);
        Renderer.Resume();
    }

    public void LoadInvestmentItems(IEnumerable<InventoryItem> items)
    {
        if (Renderer is null)
            Initialize();

        CreateInvestmentShaders();
        Renderer.Pause();
        Renderer.LoadInvestmentItems(items);
        Renderer.Resume();

        CreatePerObjectInvestmentShaders(items);
        SetArmorGenderVisibility(DestinyGenderDefinition.Masculine);
    }

    public void CreateInvestmentShaders()
    {
        InvestmentExpander.Visibility = Visibility.Visible;
        ItemShadersExpander.Visibility = Visibility.Visible;

        if (AllShadersCategories is null)
        {
            AllShadersCategories = new()
            {
                CategoryStyle = DestinySocketCategoryStyle.Consumable,
                Sockets = new List<SocketEntry>()
            };

            SocketEntry socketEntry = new();
            socketEntry.CategoryStyle = DestinySocketCategoryStyle.Consumable;

            IEnumerable<InventoryItem> inventoryItems = Investment.Get().GetInventoryItemsUnloaded();

            foreach (var invItem in inventoryItems)
            {
                if (!invItem.IsShader)
                    continue;

                var plugitem = new APIPlugItem(invItem)
                {
                    IsSelected = false,
                    Index = invItem.GetItemIndex(),
                    ParentSocket = socketEntry,
                };
                socketEntry.PlugItems.Add(plugitem);
            }

            socketEntry.PlugItems = socketEntry.PlugItems.OrderByDescending(x => x.Index).ToList();

            var initial = socketEntry.PlugItems.Last();
            socketEntry.PlugItems.Remove(initial);
            socketEntry.PlugItems.Insert(0, initial);

            socketEntry.SelectedPlug = initial;
            socketEntry.SingleInitialItem = initial;

            AllShadersCategories.Sockets.Add(socketEntry);
        }

        AllShadersCategories.Sockets[0].SelectedPlug = AllShadersCategories.Sockets[0].SingleInitialItem;
        AllItemShaders.Content = AllShadersCategories;
    }

    public void CreatePerObjectInvestmentShaders(IEnumerable<InventoryItem> items)
    {
        PerItemShadersPanel.Visibility = Visibility.Visible;

        ItemShadersCategories = new();
        IEnumerable<InventoryItem> shaders = Investment.Get().GetInventoryItemsUnloaded().Where(x => x.IsShader);

        foreach (var item in items)
        {
            RendererShaderEntry entry = new();
            entry.Item = new APIPlugItem(item);

            entry.ShadersCategory = new()
            {
                CategoryStyle = DestinySocketCategoryStyle.Consumable,
                Sockets = new List<SocketEntry>()
            };

            SocketEntry socketEntry = new();
            socketEntry.ParentItem = entry.Item;
            socketEntry.CategoryStyle = DestinySocketCategoryStyle.Consumable;

            foreach (var invItem in shaders)
            {
                if (!invItem.IsShader)
                    continue;

                var plugitem = new APIPlugItem(invItem)
                {
                    IsSelected = false,
                    Index = invItem.GetItemIndex(),
                    ParentSocket = socketEntry,
                };
                socketEntry.PlugItems.Add(plugitem);
            }

            socketEntry.PlugItems = socketEntry.PlugItems.OrderByDescending(x => x.Index).ToList();

            var initial = socketEntry.PlugItems.Last();
            socketEntry.PlugItems.Remove(initial);
            socketEntry.PlugItems.Insert(0, initial);

            socketEntry.SelectedPlug = initial;
            socketEntry.SingleInitialItem = initial;

            entry.ShadersCategory.Sockets.Add(socketEntry);
            entry.ShadersCategory.Sockets[0].SelectedPlug = entry.ShadersCategory.Sockets[0].SingleInitialItem;

            ItemShadersCategories.Add(entry);
        }

        PerItemShaders.ItemsSource = ItemShadersCategories;
    }

    private void PlugItem_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not APIPlugItem)
            return;

        APIPlugItem item = (APIPlugItem)(sender as FrameworkElement).DataContext;
        if (item.IsSelected && item.ParentSocket is not null)
        {
            item.ParentSocket.SelectedPlug = item;
            if (item.Item.Name == "Default Shader")
                ResetInvestmentShader(item.ParentSocket.ParentItem);
            else
                LoadInvestmentShader(item.Item, item.ParentSocket.ParentItem);
        }
    }

    private void LoadInvestmentShader(InventoryItem shader, APIPlugItem parent = null)
    {
        if (!Renderer.World.RenderObjects.Any() || !Renderer.World.RenderObjects.Any(x => x.Investment != null))
            return;

        if (!shader.IsLoaded())
            shader.Load(true);

        Renderer.Pause();
        var items = parent is null ? Renderer.World.RenderObjects : Renderer.World.RenderObjects.Where(x => x.Investment != null && x.Investment.BaseItem.ApiHash == parent.Item.ApiHash);
        foreach (var obj in items)
        {
            if (obj.Investment is null)
                continue;

            obj.Investment.CreateCustomDyes(Renderer.Context, shader);
        }
        Renderer.Resume();
    }

    private void ResetInvestmentShader(APIPlugItem parent = null)
    {
        if (!Renderer.World.RenderObjects.Any())
            return;

        Renderer.Pause();
        var items = parent is null ? Renderer.World.RenderObjects : Renderer.World.RenderObjects.Where(x => x.Investment != null && x.Investment.BaseItem.ApiHash == parent.Item.ApiHash);
        foreach (var obj in items)
        {
            if (obj.Investment is null)
                continue;

            obj.Investment.ResetDyes(Renderer.Context);
        }
        Renderer.Resume();
    }

    private void InvesmentShadersOrderBy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb)
            return;

        _ = int.TryParse((string)rb.Tag, out int order);

        var items = AllShadersCategories.Sockets[0].PlugItems;
        switch (order)
        {
            case 0:
                items = [.. items.OrderBy(x => x.Item.Name)];
                break;

            case 1:
                items = [.. items.OrderByDescending(x => x.Item.GetItemIndex())];
                break;
        }
        var initial = AllShadersCategories.Sockets[0].SingleInitialItem;
        items.Remove(initial);
        items.Insert(0, initial);

        AllShadersCategories.Sockets[0].PlugItems = items;

        AllItemShaders.Content = null;
        AllItemShaders.Content = AllShadersCategories;
    }

    private void ArmorGenderToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb)
            return;

        _ = int.TryParse((string)rb.Tag, out int order);

        DestinyGenderDefinition gender = order == 0 ? DestinyGenderDefinition.Masculine : DestinyGenderDefinition.Feminine;
        SetArmorGenderVisibility(gender);
    }

    private void SetArmorGenderVisibility(DestinyGenderDefinition gender)
    {
        foreach (var obj in Renderer.World.RenderObjects)
        {
            if (obj.Investment is null || obj.Entity is null || obj.Entity.Gender == DestinyGenderDefinition.None)
                continue;

            obj.Visible = obj.Entity.Gender == gender;
        }
    }
    #endregion
    #endregion

    #region Scene World
    private void SceneWorld_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Renderer is null)
            return;

        var tag = ((sender as ComboBox).SelectedItem as ComboBoxItem).Tag;
        if (tag is not null && tag is SceneWorld world)
        {
            Renderer.Pause();
            lock (Renderer.World.WorldLock)
            {
                Renderer.World.SwitchWorld(Renderer, (uint)world);
            }
            Renderer.Resume();
            AtmosRotation = Renderer.World.GlobalChannels.Get("sky_snapshot_rotation").X / 360f;
            //AtmosIntensity = Renderer.World.GlobalChannels.Get("sky_snapshot_intensity").X;

            // not ideal but forces the slider to update
            AtmosOptions.ItemsSource = null;
            AtmosOptions.ItemsSource = AtmosSettings;
        }
    }

    // TODO get from actual maps, hardcode bad
    private enum SceneWorld : uint
    {
        [Description("The Tower")] Tower = 0x81141179,
        [Description("Dreaming City")] DreamingCity = 0x80BDCF1A,
        [Description("EDZ: Trostland")] EDZTrostland = 0x80BB301E,
        [Description("The Dreadnaught")] Dreadnaught = 0x813985A0,
        [Description("Mercury Past")] MercuryPast = 0x80B1D0C4,
        [Description("The Pale Heart")] PaleHeart = 0x80E523F3,
        [Description("The Black Garden")] BlackGarden = 0x80CD96D7,
        [Description("Vesper Station")] VesperStation = 0x80EF4378,
        Cosmodrome = 0x80C86FD6,
        Mars = 0x80D44F41,
        Eternity = 0x80F2CB14,
        Kepler = 0x80DB556A,
        Neomuna = 0x81046404,
    }
    #endregion


    private void MeshGroup_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
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
