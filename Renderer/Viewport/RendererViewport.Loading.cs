using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Charm.Shared;
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
    public async void LoadEntity(Entity entity, Dictionary<uint, uint>? state = null)
    {
        if (Renderer is null)
            Initialize();

        // entity is passed directly now so its material permutations can be modified on the "real" entity, instead of a new one being made
        _currentEntity = entity;
        await Task.Run(() =>
        {
            Dispatcher.Invoke(() =>
            {
                CreatePermutationsPanel(entity);
            });
        });

        Renderer.Stop();
        Renderer.LoadEntity(_currentEntity);
        ApplyEntityState(state);
        Renderer.Start();

        if (entity.Skeleton != null && !UseGameSkinning)
            OverrideWarning.Visibility = Visibility.Visible;
        else
            OverrideWarning.Visibility = Visibility.Collapsed;
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
            vm.VisibilityChanged += (i, visible) => Renderer.GroupVisibility.SetVisible(_currentEntity, i, visible);

            GroupToggles.Add(vm);
        }
        MeshGroups.ItemsSource = GroupToggles;
    }

    // TODO move into own control? also support physics model? though idk if those would be the same as their regular model
    private const uint AllValue = 0xFFFFFFFF;
    private const uint InheritValue = 0xFFFFFFFE;
    private void CreatePermutationsPanel(Entity entity)
    {
        PermutationsExpander.Visibility = Visibility.Collapsed;
        PermutationPanel.Children.Clear();

        if (entity.ModelParent is null)
            return;

        var variants = CharmRenderer.IsBasic ? null : entity.ModelParent.MaterialVariants;
        int variantCount = variants?.Max ?? 0;
        if (variantCount != 0)
        {
            MaterialPermutationOverride = new SliderSetting()
            {
                Max = variantCount,
                Min = -1,
                DefaultValue = -1,
                IsResetable = true,
                Text = "Override Material",
                GetValue = () => variants.OverrideIndex,
                SetValue = v =>
                {
                    variants.OverrideIndex = (int)Math.Floor(v);
                    MaterialPermutationOverride.NotifyValueChanged();
                }
            };
            PermIndexOverride.Content = MaterialPermutationOverride;
        }

        CreateVariantCombo(entity);

        var distinctValues = entity.ModelParent.GetDistinctKeyValues();
        foreach (var kv in distinctValues.Where(kv => kv.Value.Count > 0 && kv.Key != EntityModelParent.PermutationKey))
        {
            ComboBoxControl switchCombo = new();
            switchCombo.Text = GlobalStrings.Get().GetString(kv.Key);
            switchCombo.TextFontSize = 16;
            switchCombo.Margin = new Thickness(5);
            switchCombo.Box.Tag = kv.Key;

            var entries = new List<ComboBoxItem>
            {
                new() { Content = "All/Default", Tag = AllValue }
            };
            entries.AddRange(kv.Value
                .Where(v => v != 0x871AC0EA)
                .Select(v => new ComboBoxItem { Content = GlobalStrings.Get().GetString(v), Tag = v }));

            switchCombo.Box.ItemsSource = entries;
            switchCombo.Box.SelectedIndex = 0;
            switchCombo.Box.SelectionChanged += Switch_OnSelectionChanged;

            PermutationPanel.Children.Add(switchCombo);
        }
        CreatePermutationOverrides();

        if (PermutationPanel.Children.Count > 0)
            PermutationsExpander.Visibility = Visibility.Visible;
    }

    private ComboBoxControl? _variantCombo;
    private void CreateVariantCombo(Entity entity)
    {
        _variantCombo = null;
        if (entity.VariantMap is null || entity.VariantMap.Variants.Count == 0)
            return;

        _variantCombo = new();
        _variantCombo.Text = "Variant";
        _variantCombo.TextFontSize = 16;
        _variantCombo.Margin = new Thickness(5);

        var entries = new List<ComboBoxItem>
        {
            new() { Content = "None", Tag = AllValue }
        };
        var variants = entity.VariantMap.Variants;
        for (int i = 0; i < variants.Count; i++)
            entries.Add(new ComboBoxItem { Content = GlobalStrings.Get().GetString(variants[i].Name), Tag = (uint)i });

        _variantCombo.Box.ItemsSource = entries;
        _variantCombo.Box.SelectedIndex = 0;
        _variantCombo.Box.SelectionChanged += Switch_OnSelectionChanged;

        PermutationPanel.Children.Add(_variantCombo);
    }

    private void CreatePermutationOverrides()
    {
        PermutationOverridePanel.Children.Clear();

        var variants = _currentEntity.VariantMap?.Variants;

        // only the permutations a regions parts actually have so every region actually shows something
        foreach (var (region, permutations) in _currentEntity.ModelParent.GetRegionPermutations())
        {
            ComboBoxControl regionCombo = new();
            regionCombo.Text = GlobalStrings.Get().GetString(region);
            regionCombo.TextFontSize = 16;
            regionCombo.Margin = new Thickness(5);
            regionCombo.Box.Tag = region;

            var entries = new List<ComboBoxItem> { new() { Content = "Inherit", Tag = InheritValue } };
            foreach (uint permutation in permutations.Where(v => v != 0x871AC0EA))
            {
                string name = GlobalStrings.Get().GetString(permutation);

                // the variants that use this permutation for the region
                var users = variants?
                    .Where(x => x.RegionPermutations.TryGetValue(region, out uint value) && value == permutation)
                    .Select(x => GlobalStrings.Get().GetString(x.Name))
                    .ToList();

                entries.Add(new ComboBoxItem { Content = name, Tag = permutation });
            }

            regionCombo.Box.ItemsSource = entries;
            regionCombo.Box.SelectedIndex = 0; // Inherit by default
            regionCombo.Box.SelectionChanged += Switch_OnSelectionChanged;

            PermutationOverridePanel.Children.Add(regionCombo);
        }
    }

    private bool _applyingState;
    private void ApplyEntityState(Dictionary<uint, uint>? state)
    {
        var modelParent = _currentEntity.ModelParent;
        if (modelParent is null)
            return;

        if (modelParent.AppliedMeshGroups.Count != 0)
        {
            modelParent.AppliedMeshGroups = new();
            modelParent.UpdateMeshGroups++;
        }

        if (state is null || state.Count == 0)
            return;

        _applyingState = true;
        try
        {
            int variant = _currentEntity.VariantMap?.FindVariantIndex(state) ?? -1;
            if (_variantCombo is not null && variant != -1)
                _variantCombo.Box.SelectedItem = ((List<ComboBoxItem>)_variantCombo.Box.ItemsSource).FirstOrDefault(x => (uint)x.Tag == (uint)variant);

            foreach (var child in PermutationPanel.Children)
            {
                if (child is not ComboBoxControl combo || combo == _variantCombo || !state.TryGetValue((uint)combo.Box.Tag, out uint value))
                    continue;

                var entries = (List<ComboBoxItem>)combo.Box.ItemsSource;
                var entry = entries.FirstOrDefault(x => (uint)x.Tag == value);
                if (entry is null)
                {
                    entry = new ComboBoxItem { Content = GlobalStrings.Get().GetString(value), Tag = value };
                    entries.Add(entry);
                    combo.Box.Items.Refresh();
                }
                combo.Box.SelectedItem = entry;
            }
        }
        finally
        {
            _applyingState = false;
        }

        Switch_OnSelectionChanged(this, null);
    }

    private void Switch_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingState)
            return;

        var modelParent = _currentEntity.ModelParent;
        if (modelParent is null)
            return;

        EntityVariant? variant = null;
        if (_variantCombo?.Box.SelectedItem is ComboBoxItem variantItem && (uint)variantItem.Tag != AllValue)
            variant = _currentEntity.VariantMap.Variants[(int)(uint)variantItem.Tag];

        var query = new Dictionary<uint, uint>();
        foreach (var child in PermutationPanel.Children)
        {
            if (child is ComboBoxControl comboBoxControl && comboBoxControl != _variantCombo &&
                comboBoxControl.Box.SelectedItem is ComboBoxItem selectedItem &&
                (uint)selectedItem.Tag != AllValue)
            {
                query.TryAdd((uint)comboBoxControl.Box.Tag, (uint)selectedItem.Tag);
            }
        }

        var regionOverrides = new Dictionary<uint, uint>();
        foreach (var child in PermutationOverridePanel.Children)
        {
            if (child is ComboBoxControl regionCombo &&
                regionCombo.Box.SelectedItem is ComboBoxItem selected &&
                (uint)selected.Tag != InheritValue)
            {
                regionOverrides[(uint)regionCombo.Box.Tag] = (uint)selected.Tag;
            }
        }

        var activeGroups = modelParent.GetMeshGroupsWithOverrides(query, regionOverrides, variant);
        foreach (GroupToggleVM toggle in GroupToggles)
            toggle.IsChecked = activeGroups.MeshGroups.Contains(toggle.GroupIndex);
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
        [Description("Warlord's Ruin")] WarlordsRuin = 0x8112FC9E,
        [Description("Infinite Forest")] InfiniteForest = 0x811BE157,
        [Description("Neptune Orbit")] NeptuneOrbit = 0x810248AA,
        [Description("Quantum Circuit")] Eternity2 = 0x81313773,
        [Description("Tangled Shore")] TangledShore = 0x8135FD2F,
        EAZ = 0x811667E5,
        Nessus = 0x80F42561,
        Cosmodrome = 0x80C86FD6,
        Mars = 0x80D44F41,
        Eternity = 0x80F2CB14,
        Kepler = 0x80DB556A,
        Neomuna = 0x81046404,
    }
    #endregion

}



