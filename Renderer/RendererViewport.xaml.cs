using Charm.Shared;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;
using static Charm.Renderer.CharmRenderer;
using static Tiger.Schema.Entity.EntityModelParent;

namespace Charm.Renderer;

// TODO: Support multiple viewports.
// Gonna need lots of reworking in here and in the renderer to remove reliance on Instance (singleton)

public partial class RendererViewport : UserControl
{
	public CharmRenderer Renderer => CharmRenderer.Instance;
	public bool ShowGrid { get; set; } = true;
	public bool RenderSky { get; set; } = true;
	public bool UseVCEntOverride { get; set; } = false;
	public float TimeOfDay { get; set; } = 0.5f;
	public float Exposure { get; set; } = 0.8f;
	public float AtmosRotation { get; set; } = 0f;
	public float AtmosIntensity { get; set; } = 0.75f;

	private bool _isFullscreen = false;
	private Panel _originalParent;
	private bool _isInitialized;

	public RendererViewport()
	{
		InitializeComponent();
		CreateRenderPassOptions();
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

		if (_uiTimer is null)
		{
			_uiTimer = new();
			_uiTimer.Interval = TimeSpan.FromMilliseconds(10);
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
			};
			_uiTimer.Start();
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

		Combobox.ItemsSource = types;
		if (Combobox.SelectedIndex == -1)
			Combobox.SelectedIndex = 0;
	}

	private void Dropdown_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (Renderer is null)
			return;

		var tag = ((sender as ComboBox).SelectedItem as ComboBoxItem).Tag;
		if (tag is not null && tag is CharmRenderer.RenderPass pass)
			Renderer.DisplayPass = pass;
		else
			Renderer.DisplayPass = CharmRenderer.RenderPass.final_combine_no_pp;
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
			Renderer?.StopRenderLoop();
			Renderer?.Dispose();
		}
		SizeChanged -= OnSizeChanged;
		Unloaded -= OnUnloaded;
		_isInitialized = false;
		_currentEntity = null;
	}

	private void ShowGridButton_Checked(object sender, RoutedEventArgs e)
	{
		ShowGrid = !ShowGrid;
	}

	private void TimeOfDaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		TimeOfDay = (float)e.NewValue;
	}

	private void ExposureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		Exposure = (float)e.NewValue;
	}

	private void AtmosButton_Click(object sender, RoutedEventArgs e)
	{
		RenderSky = !RenderSky;
	}

	private void AtmosRotationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		AtmosRotation = (float)e.NewValue;
	}

	private void AtmosIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		AtmosIntensity = (float)e.NewValue;
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

	private void ResetObjectChannels_Click(object sender, RoutedEventArgs e)
	{
		Renderer?.EntityObjectChannels?.ResetAllChannels();
	}

	private void InvestmentDyeTest_Click(object sender, RoutedEventArgs e)
	{
		if (!Renderer.World.RenderObjects.Any())
			return;

		var shader = Investment.Get().GetInventoryItem(new TigerHash(4182403848));
		foreach (var obj in Renderer.World.RenderObjects)
		{
			if (obj.Investment is null)
				continue;

			obj.Investment.CreateCustomDyes(Renderer.Context, shader);
		}
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

	private Entity _currentEntity; // temp
	public async void LoadEntity(FileHash hash)
	{
		if (Renderer is null)
			Initialize();

		var entity = FileResourcer.Get().GetFile<Entity>(hash, shouldCache: false);
		_currentEntity = entity;

		Renderer.StopRenderLoop();
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

		Renderer.StopRenderLoop();
		Renderer.LoadEntity(_currentEntity, new MapTransform { Translation = new Vector4(0f, 0f, 0f, 1f) }, false);
		Renderer.Start();
	}

	// TODO move into own control?
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
		var selection = (sender as ComboBox);
		var permutations = _currentEntity.ModelParent.MaterialPermutations;

		//Console.WriteLine($"{(selection.SelectedItem as ComboBoxItem).Content}");

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

		ReloadEntity();
	}
}
