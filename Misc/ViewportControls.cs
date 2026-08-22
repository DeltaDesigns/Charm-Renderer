using System.ComponentModel;

namespace Charm.Renderer;

// Move into Charm.Shared maybee?

public abstract class SettingItem { }

public class ToggleSetting : SettingItem
{
    public string Text { get; set; }

    public Func<bool> GetValue { get; set; }
    public Action<bool> SetValue { get; set; }
    public dynamic Tag { get; set; }

    private bool _isChecked;
    public bool IsChecked
    {
        get => GetValue != null ? GetValue() : _isChecked;
        set
        {
            if (SetValue != null)
                SetValue(value);
            else
                _isChecked = value;
        }
    }
}

public class SliderSetting : SettingItem, INotifyPropertyChanged
{
    public string Text { get; set; }
    public double Min { get; set; } = 0;
    public double Max { get; set; } = 1;
    public bool IsInt { get; set; } = false;

    public Func<float> GetValue { get; set; }
    public Action<float> SetValue { get; set; }

    private float _value;
    public float Value
    {
        get => GetValue != null ? GetValue() : _value;
        set
        {
            if (IsInt)
                value = MathF.Round(value);

            if (SetValue != null)
                SetValue(value);
            else
                _value = value;

            OnPropertyChanged(nameof(Value));
        }
    }

    public bool IsLockable => SetLockState != null;

    private bool _isLocked;
    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (_isLocked == value)
                return;

            _isLocked = value;
            SetLockState?.Invoke(value);
            OnPropertyChanged(nameof(IsLocked));
        }
    }

    public string LockTooltip { get; set; }
    public Action<bool> SetLockState { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public void NotifyValueChanged()
    {
        OnPropertyChanged(nameof(Value));
    }
}

public class GroupToggleVM : SettingItem, INotifyPropertyChanged
{
    public GroupToggleVM(int groupIndex)
    {
        GroupIndex = groupIndex;
    }

    public int GroupIndex { get; }

    private bool _isChecked = true;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;

            _isChecked = value;
            PropertyChanged?.Invoke(this, new(nameof(IsChecked)));
            VisibilityChanged?.Invoke(GroupIndex, value);
        }
    }

    public string Text => $"Group {GroupIndex}";
    public event Action<int, bool>? VisibilityChanged;

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}

public class VectorSetting : SettingItem
{
    public string Text { get; set; }

    public Func<EditableVector4> GetValue { get; set; }
    public Action<EditableVector4> SetValue { get; set; }

    public float DragSpeed { get; set; } = 0.02f;

    private EditableVector4 _value;
    public EditableVector4 Value
    {
        get => GetValue != null ? GetValue() : _value;
        set
        {
            if (ReferenceEquals(_value, value))
                return;

            if (_value != null)
                _value.PropertyChanged -= OnVectorPropertyChanged;

            _value = value;

            if (_value != null)
                _value.PropertyChanged += OnVectorPropertyChanged;

            SetValue?.Invoke(_value);
        }
    }

    private void OnVectorPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        SetValue?.Invoke(_value);
    }
}

public class SeparatorItem : SettingItem, INotifyPropertyChanged
{
    public SeparatorItem(string text)
    {
        Text = text;
    }
    public string Text { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
