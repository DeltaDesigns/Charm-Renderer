using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Arithmic;
using Tiger;
using Tiger.Schema.Investment;

namespace Charm.Renderer;

public partial class RendererViewport
{
    public SocketCategory? AllEmotesCategory { get; set; }
    private SocketEntry? _emoteSocket;
    private InventoryItem? _activeEmote;
    private DestinyGenderDefinition _armorGender = DestinyGenderDefinition.Masculine;

    public void CreateInvestmentEmotes(IEnumerable<InventoryItem> items)
    {
        _activeEmote = null;
        Renderer.ClearEmote();

        bool hasArmor = items.Any(CharmRenderer.IsArmor);
        ItemEmotesExpander.Visibility = hasArmor ? Visibility.Visible : Visibility.Collapsed;
        if (!hasArmor)
            return;

        if (AllEmotesCategory is null)
        {
            AllEmotesCategory = new()
            {
                CategoryStyle = DestinySocketCategoryStyle.Consumable,
                Sockets = new List<SocketEntry>()
            };

            SocketEntry socketEntry = new();
            socketEntry.CategoryStyle = DestinySocketCategoryStyle.Consumable;

            foreach (var invItem in Investment.Get().GetInventoryItemsUnloaded())
            {
                if (!invItem.IsEmote)
                    continue;

                var plugitem = new EmotePlugItem(invItem)
                {
                    IsSelected = false,
                    Index = invItem.GetItemIndex(),
                    ParentSocket = socketEntry,
                };
                socketEntry.PlugItems.Add(plugitem);
            }

            var none = new EmotePlugItem
            {
                IsSelected = false,
                ParentSocket = socketEntry,
            };
            socketEntry.PlugItems.Insert(0, none);

            socketEntry.SelectedPlug = none;
            socketEntry.SingleInitialItem = none;

            AllEmotesCategory.Sockets.Add(socketEntry);
            _emoteSocket = socketEntry;
            FilterSocket(AllEmotesCategory, _emoteOrder, EmoteSearch, groupByRarity: true);
        }

        _emoteSocket!.SelectedPlug = _emoteSocket.SingleInitialItem;
        AllItemEmotes.Content = AllEmotesCategory;
    }

    private void PlugItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EmotePlugItem item || Renderer is null)
            return;

        if (item.ParentSocket is not null)
            item.ParentSocket.SelectedPlug = item;

        if (item.Item is null)
        {
            _activeEmote = null;
            Renderer.ClearEmote();
            return;
        }

        _ = StartEmote(item.Item);
    }

    private void PlugItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EmotePlugItem { Item: not null } item || Renderer is null)
            return;

        e.Handled = true;
        if (_activeEmote?.ApiHash == item.Item.ApiHash)
            _ = Renderer.StopEmote();
    }

    private async Task StartEmote(InventoryItem emote)
    {
        _activeEmote = emote;

        List<EmoteAnimations> emotes;
        try
        {
            emotes = await Task.Run(() =>
            {
                if (!emote.IsLoaded())
                    emote.Load(true);

                return Investment.Get().GetEmoteAnimations(emote);
            });
        }
        catch (Exception e)
        {
            Log.Error($"Couldnt read emote {emote.Name}: {e.Message}");
            return;
        }

        if (_activeEmote != emote)
            return;

        var picked = emotes.FirstOrDefault(x => x.Gender == _armorGender) ?? emotes.FirstOrDefault();
        if (picked is null || !EmoteAnimations.IsValid(picked.Loop))
        {
            Log.Error($"{emote.Name} has no animations.");
            return;
        }

        await Renderer.PlayEmote(picked);
    }

    private void OnEmoteGenderChanged()
    {
        if (_activeEmote is not null && Renderer?.CurrentEmote is not null)
            _ = StartEmote(_activeEmote);
    }

    private string _emoteSearch = string.Empty;
    public string EmoteSearch
    {
        get => _emoteSearch;
        set
        {
            _emoteSearch = value;
            OnPropertyChanged(nameof(EmoteSearch));
            SearchEmotes();
        }
    }

    private int _emoteOrder = 1;
    private DispatcherTimer _emoteSearchDebounceTimer;
    private void SearchEmotes()
    {
        if (_emoteSearchDebounceTimer == null)
        {
            _emoteSearchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _emoteSearchDebounceTimer.Tick += (s, args) =>
            {
                _emoteSearchDebounceTimer.Stop();
                FilterInvestmentEmotes();
            };
        }

        _emoteSearchDebounceTimer.Stop();
        _emoteSearchDebounceTimer.Start();
    }

    private void InvestmentEmotesOrderBy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || !int.TryParse((string)rb.Tag, out int order))
            return;

        _emoteOrder = order;
        FilterInvestmentEmotes();
    }

    private void FilterInvestmentEmotes()
    {
        if (AllEmotesCategory is null)
            return;

        FilterSocket(AllEmotesCategory, _emoteOrder, EmoteSearch, groupByRarity: true);
        AllItemEmotes.Content = null;
        AllItemEmotes.Content = AllEmotesCategory;
    }
}

public class EmotePlugItem : APIPlugItem
{
    public EmotePlugItem() { }

    public EmotePlugItem(InventoryItem item) : base(item) { }

    public override IEnumerable<InputBlock>? GetTooltipInputs()
    {
        if (Item is null)
            return [];

        try
        {
            if (!Investment.Get().GetEmoteAnimations(Item).Any(x => x.HasIntroLoopOutro))
                return [];
        }
        catch (Exception e)
        {
            Log.Warning($"Couldnt read emote {Item.Name}: {e.Message}");
            return [];
        }

        return
        [
            new InputBlock { Order = 0, Key = "", KeyPress = "", Action = "Start" },
            new InputBlock { Order = 1, Key = "", KeyPress = "", Action = "Stop" },
        ];
    }
}
