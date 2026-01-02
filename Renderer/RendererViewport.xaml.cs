using Charm.Shared;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;
using static Charm.Renderer.CharmRenderer;

namespace Charm.Renderer;

// TODO: Support multiple viewports?
// Gonna need lots of reworking in here and in the renderer to remove reliance on Instance (singleton)

public partial class RendererViewport : UserControl, INotifyPropertyChanged, Shared.IRenderer
{
	public CharmRenderer Renderer => CharmRenderer.Instance;

	#region Debug Options
	public ObservableCollection<SettingItem> DebugSettings { get; set; }
	public bool ShowGrid { get; set; } = true;
	public bool CapFPS { get; set; } = true;
	#endregion

	#region Render Options
	public ObservableCollection<SettingItem> AtmosSettings { get; set; }
	public SliderSetting TimeOfDaySetting { get; set; }
	public bool RenderSky { get; set; } = true;
	public bool RenderSkyObjs { get; set; } = true;
	public float TimeOfDay { get; set; } = 0.675f;
	public float Exposure { get; set; } = 0.8f;
	public float FOV { get; set; } = 60f;
	public float TimeScale { get; set; } = 1f;
	public float AtmosRotation { get; set; } = 0.825f;
	public float AtmosIntensity { get; set; } = 0.75f;
	#endregion

	#region Object Options
	public ObservableCollection<SettingItem> ObjectSettings { get; set; }
	public bool ShowSkele { get; set; } = true;
	public bool ShowBB { get; set; } = false;
	public SliderSetting MaterialPermutationOverride { get; set; }
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
	}

	private DispatcherTimer _uiTimer;
	private void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
	{
		Initialize();
	}

	public void Initialize()
	{
		Unloaded -= OnUnloaded;
		Unloaded += OnUnloaded;

		if (_isInitialized)
			return;

		if (Renderer == null)
			CharmRenderer.Instance = new CharmRenderer();

		Renderer.Viewport = this;
		Renderer.Initialize((int)ActualWidth, (int)ActualHeight);
		Renderer.Start();

		SizeChanged += OnSizeChanged;

		Stopwatch _dayCycleStopwatch = new();
		if (_uiTimer is null)
		{
			_uiTimer = new();
			_uiTimer.Interval = TimeSpan.FromMilliseconds(33);
			_uiTimer.Tick += (s, e) =>
			{
				if (Renderer is null)
					return;

				var camPos = Renderer.Camera.Position;
				var camRot = Renderer.Camera.Rotation;

				FrameTime.Text = $"CPU Time: {Renderer.DeltaTime:F5} ms";
				CameraPosition.Text = $"Camera Position: {camPos.X:F2}, {camPos.Y:F2}, {camPos.Z:F2}";
				CameraRotation.Text = $"Camera Rotation: {camRot.X:F2}, {camRot.Y:F2}, {camRot.Z:F2}, {camRot.W:F2}";
				FPSCounter.Text = $"FPS: {Math.Ceiling(Renderer.FPS)}";

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
		_isInitialized = true;
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
		SceneWorldCombobox.SelectionChanged += SceneWorld_OnSelectionChanged;
		SceneWorldCombobox.ItemsSource = types;
	}

	private void CreateViewportControls()
	{
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

		ShowDebugSettings.Content = new ToggleSetting { Text = "Debug Options" };
		DebugSettings = new ObservableCollection<SettingItem>
		{
			new SliderSetting
			{
				Text = "Exposure",
				Max = 2f,
				GetValue = () => Exposure,
				SetValue = v => Exposure = v
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
				Text = "Cap FPS",
				GetValue = () => CapFPS,
				SetValue = v => CapFPS = v
			},
		};
		DebugOptions.ItemsSource = DebugSettings;

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
		};
		ObjectOptions.ItemsSource = ObjectSettings;
	}

	private void Dropdown_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (Renderer is null)
			return;

		var tag = ((sender as ComboBox).SelectedItem as ComboBoxItem).Tag;
		if (tag is not null && tag is CharmRenderer.RenderPass pass)
			Renderer.DisplayPass = pass;
		else
			Renderer.DisplayPass = CharmRenderer.RenderPass.final;
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

		Unloaded -= OnUnloaded;
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
		if (Renderer != null)
		{
			Renderer?.Stop();
			Renderer?.Dispose();
		}
		SizeChanged -= OnSizeChanged;
		Unloaded -= OnUnloaded;
		_isInitialized = false;
		_currentEntity = null;
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

	private void InvesmentShadersOrderBy_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not RadioButton rb)
			return;

		_ = int.TryParse((string)rb.Tag, out int order);

		var items = InvestmentShaders.ItemsSource.Cast<ToggleSetting>();
		switch (order)
		{
			case 0:
				items = items.OrderBy(x => ((ToggleSetting)x).Text);
				break;

			case 1:
				items = items.OrderByDescending(x => (((ToggleSetting)x).Tag as InventoryItem).GetItemIndex());
				break;
		}
		InvestmentShaders.ItemsSource = items;
	}
	#endregion

	#region Mesh Loading (Temp?)
	public void LoadStatic(FileHash hash, MapTransform transform)
	{
		if (Renderer is null)
			Initialize();

		Renderer.Stop();
		Renderer.LoadStatic(hash, new MapTransform { Translation = new Vector4(0f, 0f, 0f, 1f) });
		Renderer.Start();
	}

	public void LoadInvestmentItem(InventoryItem item)
	{
		if (Renderer is null)
			Initialize();

		CreateInvestmentShaders();
		Renderer.Pause();
		Renderer.LoadInvestmentItem(item);
		Renderer.Resume();
	}

	private Entity _currentEntity; // temp
	public async void LoadEntity(FileHash hash)
	{
		if (Renderer is null)
			Initialize();

		var entity = FileResourcer.Get().GetFile<Entity>(hash, shouldCache: false);
		_currentEntity = entity;

		Renderer.Stop();
		Renderer.LoadEntity(_currentEntity, new MapTransform { Translation = new Vector4(0f, 0f, 0f, 1f) });
		Renderer.Start();

		await Task.Run(() =>
		{
			Dispatcher.Invoke(() =>
			{
				CreateMaterialVariants(entity);
			});
		});
	}

	public void ReloadEntity()
	{
		if (_currentEntity is null)
			return;

		Renderer.Stop();
		Renderer.LoadEntity(_currentEntity, new MapTransform { Translation = new Vector4(0f, 0f, 0f, 1f) }, false);
		Renderer.Start();
	}

	public ObservableCollection<SettingItem> GroupToggles { get; set; } = new();
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

		if (entity.ModelParent is null)
			return;

		var permutations = entity.ModelParent.MaterialPermutations;
		if (permutations is null)
			return;

		int variantCount = entity.ModelParent.Reader.ExternalMaterialsMap
							.Enumerate(entity.ModelParent.GetReader())
							//.Where(m => m.Unk08 != 0)
							.Select(m => (int)m.MaterialCount)
							.Max();

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

		if (MaterialVariantPanel.Children.Count != 0)
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

	public void CreateInvestmentShaders()
	{
		InvestmentShadersExpander.Visibility = Visibility.Visible;

		var shaders = new ObservableCollection<SettingItem>();
		IEnumerable<InventoryItem> inventoryItems = Investment.Get().GetInventoryItemsUnloaded();

		foreach (var invItem in inventoryItems)
		{
			if (!invItem.IsShader)
				continue;

			shaders.Add(new ToggleSetting
			{
				Text = invItem.Name,
				Tag = invItem,
			});
		}

		InvestmentShaders.ItemsSource = shaders.OrderBy(x => ((ToggleSetting)x).Text);
	}

	private void LoadInvestmentShader(object sender, RoutedEventArgs e)
	{
		if ((sender as RadioButton).Tag is not InventoryItem shader)
			return;

		if (!Renderer.World.RenderObjects.Any() || !Renderer.World.RenderObjects.Any(x => x.Investment != null))
			return;

		if (!shader.IsLoaded())
			shader.Load(true);

		Renderer.Pause();
		foreach (var obj in Renderer.World.RenderObjects)
		{
			if (obj.Investment is null)
				continue;

			obj.Investment.CreateCustomDyes(Renderer.Context, shader);
		}
		Renderer.Resume();
	}

	private void ResetInvestmentShader(object sender, RoutedEventArgs e)
	{
		if (!Renderer.World.RenderObjects.Any())
			return;

		foreach (var obj in Renderer.World.RenderObjects)
		{
			if (obj.Investment is null)
				continue;

			obj.Investment.ResetDyes(Renderer.Context);
		}
	}
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

	// TODO maybe, get from actual maps
	private enum SceneWorld : uint
	{
		[Description("The Tower")] Tower = 0x81141179,
		Mars = 0x80D44F41,
		[Description("Dreaming City")] DreamingCity = 0x80BDCF1A,
		[Description("EDZ: Trostland")] EDZTrostland = 0x80BB301E,
		Eternity = 0x80F2CB14,
		[Description("Europa: Cadmus Ridge")] Europa = 0x810E94BF,
		Kepler = 0x80DB556A,
		Neomuna = 0x81046404,
		[Description("Mercury Past")] MercuryPast = 0x80B1D0C4,
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
