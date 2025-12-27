namespace Charm.Renderer;

// Move into Charm.Shared maybee?

public abstract class SettingItem { }

public class ToggleSetting : SettingItem
{
	public string Text { get; set; }

	public Func<bool> GetValue { get; set; }
	public Action<bool> SetValue { get; set; }

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

public class SliderSetting : SettingItem
{
	public string Text { get; set; }
	public double Min { get; set; } = 0;
	public double Max { get; set; } = 1;

	public Func<float> GetValue { get; set; }
	public Action<float> SetValue { get; set; }

	public float Value
	{
		get => GetValue();
		set => SetValue(value);
	}
}