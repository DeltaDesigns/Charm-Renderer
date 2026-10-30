using System.Windows;
using System.Windows.Controls;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public partial class RendererViewport
{
    public bool GlobalChannelEditor { get; set; }

    private RendererGlobalChannels _editedGlobals;
    private List<GlobalChannelItem> _globalChannelItems = new();

    private void SetGlobalChannelEditorVisible(bool visible)
    {
        GlobalChannelEditor = visible;
        GlobalChannelsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible)
            RebuildGlobalChannelEditor(Renderer?.World?.GlobalChannels);
    }

    private void UpdateGlobalChannelEditor()
    {
        if (!GlobalChannelEditor)
            return;

        var globals = Renderer?.World?.GlobalChannels;
        if (!ReferenceEquals(globals, _editedGlobals))
            RebuildGlobalChannelEditor(globals);

        foreach (var item in _globalChannelItems)
        {
            if (item.IsLocked)
                item.Refresh();
        }
    }

    private void RebuildGlobalChannelEditor(RendererGlobalChannels globals)
    {
        _editedGlobals = globals;
        _globalChannelItems = globals?.Channels
            .OrderBy(c => c.Index)
            .Select(c => new GlobalChannelItem(c, GetLockReason(c)))
            .ToList() ?? new();

        GlobalChannelsEmptyText.Visibility = _globalChannelItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyGlobalChannelFilter();
    }

    private static string GetLockReason(RendererGlobalChannels.GlobalChannel channel)
    {
        if (channel.Bytecode.Length > 0 && channel.BytecodeConstants.Length > 0)
            return "Controlled by bytecode";

        return null;
    }

    private void ResetGlobalChannels_Click(object sender, RoutedEventArgs e)
    {
        _editedGlobals?.Reset();
        foreach (var item in _globalChannelItems)
            item.Refresh();
    }

    private void GlobalChannelFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyGlobalChannelFilter();

    private void ApplyGlobalChannelFilter()
    {
        string filter = GlobalChannelFilter.Text.Trim();
        GlobalChannelsEditor.ItemsSource = filter.Length == 0
            ? _globalChannelItems
            : _globalChannelItems.Where(x => x.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}

public class GlobalChannelItem
{
    private readonly RendererGlobalChannels.GlobalChannel _channel;

    public string Label { get; }
    public string LockReason { get; }
    public bool IsLocked => LockReason is not null;
    public EditableVector4 Value { get; }

    public GlobalChannelItem(RendererGlobalChannels.GlobalChannel channel, string lockReason)
    {
        _channel = channel;
        LockReason = lockReason;
        Label = $"{channel.Index}: {channel.Name}";
        Value = new EditableVector4(channel.Value, EditableVector4.VectorInputType.Vec4, channel.Value);
        if (!IsLocked)
            Value.PropertyChanged += (_, _) => _channel.Value = Value.Vec4;
    }

    public void Refresh()
    {
        Vector4 current = _channel.Value;
        if (current != Value.Vec4)
            Value.Reset(current);
    }
}
