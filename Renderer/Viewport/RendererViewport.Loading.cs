using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Charm.Shared;
using HelixToolkit.Maths;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;

namespace Charm.Renderer;

public partial class RendererViewport
{
    public void LoadStatic(FileHash hash)
    {
        if (Renderer is null)
            Initialize();

        Renderer.Stop();
        Renderer.LoadStatic(hash);
        Renderer.Start();
    }

    private Entity _currentEntity; // temp
    public async void LoadEntity(Entity entity)
    {
        if (Renderer is null)
            Initialize();

        // entity is passed directly now so its material permutations can be modified on the "real" entity, instead of a new one being made
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
        GroupToggles.Clear();
        var parts = entity.Load(ExportDetailLevel.MostDetailed, LoadLevel.Minimal);
        parts.AddRange(entity.GetEntityChildren()?.SelectMany(x => x.Load(ExportDetailLevel.MostDetailed, LoadLevel.Minimal)).ToList());

        var groupIndices = parts
            .Select(m => m.GroupIndex)
            .Distinct()
            .OrderBy(i => i);

        if (groupIndices.Count() <= 1)
        {
            MeshGroupsExpander.Visibility = Visibility.Collapsed;
            return;
        }

        MeshGroupsExpander.Visibility = Visibility.Visible;
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
            GetValue = () => permutations?.OverrideIndex ?? -1,
            SetValue = v =>
            {
                if (permutations is not null)
                {
                    permutations.OverrideIndex = (int)Math.Floor(v);
                    MaterialPermutationOverride.NotifyValueChanged();
                    PermIndexDebug.Text = $"Permutation Index: {Math.Max(0, permutations.OverrideIndex)}";
                }
            }
        };
        PermIndexDebug.Text = "Permutation Index: 0";
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
        if (permIndex.HasValue)
        {
            MaterialPermutationOverride.Value = permIndex.Value;
            MaterialPermutationOverride.NotifyValueChanged();
        }

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

        if (item.IsArmor || item.IsArmorOrnament)
            SetArmorGenderVisibility(DestinyGenderDefinition.Masculine);
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
        if (items.Any(item => item.IsArmor || item.IsArmorOrnament))
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

    private string _shaderSearch = string.Empty;
    public string ShaderSearch
    {
        get => _shaderSearch;
        set
        {
            _shaderSearch = value;
            OnPropertyChanged(nameof(ShaderSearch));
            SearchShaders();
        }
    }

    private int _currentOrder = 0;
    private readonly Dictionary<SocketCategory, List<APIPlugItem>> _shaderItems = new();
    private DispatcherTimer _searchDebounceTimer;
    private void SearchShaders()
    {
        if (_searchDebounceTimer == null)
        {
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _searchDebounceTimer.Tick += (s, args) =>
            {
                _searchDebounceTimer.Stop();
                FilterInvestmentShaders();
            };
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void InvestmentShadersOrderBy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || !int.TryParse((string)rb.Tag, out int order))
            return;

        _currentOrder = order;
        FilterInvestmentShaders();
    }

    private void FilterInvestmentShaders()
    {
        FilterSocket(AllShadersCategories, _currentOrder);
        AllItemShaders.Content = null;
        AllItemShaders.Content = AllShadersCategories;

        if (ItemShadersCategories != null && ItemShadersCategories.Count > 0)
        {
            foreach (var entry in ItemShadersCategories)
                FilterSocket(entry.ShadersCategory, _currentOrder);

            PerItemShaders.ItemsSource = null;
            PerItemShaders.ItemsSource = ItemShadersCategories;
        }
    }

    private void FilterSocket(SocketCategory category, int order)
    {
        var socket = category.Sockets[0];

        if (!_shaderItems.TryGetValue(category, out var shaders))
        {
            shaders = [.. socket.PlugItems];
            _shaderItems[category] = shaders;
        }

        var items = order switch
        {
            0 => shaders.OrderBy(x => x.Item.Name).ToList(),
            1 => shaders.OrderByDescending(x => x.Item.GetItemIndex()).ToList(),
            _ => [.. shaders]
        };

        var initial = socket.SingleInitialItem;
        if (initial != null)
        {
            items.Remove(initial);
            items.Insert(0, initial);
        }

        var filtered = string.IsNullOrWhiteSpace(ShaderSearch)
            ? items
            : items.Where(x => x.Item.Name.Contains(ShaderSearch, StringComparison.InvariantCultureIgnoreCase)).ToList();

        socket.PlugItems = filtered;
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
        ArmorGenderToggles.Visibility = Visibility.Visible;
        foreach (var obj in Renderer.World.RenderObjects)
        {
            if (obj.Investment is null || obj.Entity is null || obj.Entity.Gender == DestinyGenderDefinition.None)
                continue;

            obj.Visible = obj.Entity.Gender == gender;
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

    // TODO get from actual maps, hardcode bad
    private enum SceneWorld : uint
    {
        [Description("The Tower")] Tower = 0x81141169,
        [Description("EDZ: Trostland")] EDZTrostland = 0x80BB301E,
        [Description("The Dreadnaught")] Dreadnaught = 0x8143C58C,
        [Description("Mercury Past")] MercuryPast = 0x80B1D0C4,
        [Description("The Pale Heart")] PaleHeart = 0x80E523F3,
        [Description("The Black Garden")] BlackGarden = 0x80CD96D7,
        [Description("Vesper Station")] VesperStation = 0x80EF4378,
        [Description("Botza Ruins")] BotzaRuins = 0x813E57CA,
        [Description("Dreaming City")] DreamingCity = 0x813E57D4, // Keep of Voices
        [Description("Twilight Gap")] SeraphShield = 0x8118D1D7, // Seraphs Shield: Depot
        [Description("Warlord's Ruin")] WarlordsRuin = 0x8112FC9E,
        [Description("Infinite Forest")] InfiniteForest = 0x811BE157,
        Cosmodrome = 0x80C86FD6,
        Mars = 0x80D44F41,
        Eternity = 0x80F2CB14,
        Kepler = 0x80DB556A,
        Neomuna = 0x81046404,
    }
    #endregion

}



