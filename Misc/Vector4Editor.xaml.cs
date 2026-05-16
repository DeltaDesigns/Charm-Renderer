using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Tiger;
using static Charm.Renderer.EditableVector4;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

/// <summary>
/// Interaction logic for Vector4Editor.xaml
/// </summary>
public partial class Vector4Editor : UserControl
{
	public static readonly DependencyProperty DragSpeedProperty =
		DependencyProperty.Register(
			nameof(DragSpeed),
			typeof(float),
			typeof(Vector4Editor),
			new PropertyMetadata(0.02f));

	public float DragSpeed
	{
		get => (float)GetValue(DragSpeedProperty);
		set => SetValue(DragSpeedProperty, value);
	}

	public static readonly DependencyProperty DragThresholdProperty =
		DependencyProperty.Register(
			nameof(DragThreshold),
			typeof(float),
			typeof(Vector4Editor),
			new PropertyMetadata(5.0f));

	public float DragThreshold
	{
		get => (float)GetValue(DragThresholdProperty);
		set => SetValue(DragThresholdProperty, value);
	}


	public Vector4Editor()
	{
		InitializeComponent();
	}

	private static readonly Regex FloatRegex = new Regex("[0-9.-]+");
	protected void NumericOnly(object sender, System.Windows.Input.TextCompositionEventArgs e)
	{
		e.Handled = !FloatRegex.IsMatch(e.Text);
	}

	private void TextBox_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Command == ApplicationCommands.Paste)
			e.Handled = true;
	}

	private bool _isDragging = false;
	private bool _isMouseDown = false;
	private Point _mouseDownPos;
	private float _startValue;

	private TextBox _target;
	private void DragEdit_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (_isDragging)
			return;

		_isMouseDown = true;
		_mouseDownPos = e.GetPosition(null);

		var box = (TextBox)sender;
		_target = box;
		float.TryParse(_target.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _startValue);
		_target.Focus();
	}

	private void DragEdit_MouseUp(object sender, MouseButtonEventArgs e)
	{
		if (_target != null)
		{
			if (_target.IsMouseCaptured)
				_target.ReleaseMouseCapture();

			_target = null;
			_isDragging = false;
		}

		e.Handled = true;
	}

	private void DragEdit_MouseMove(object sender, MouseEventArgs e)
	{
		if (_target == null || !_isMouseDown)
			return;

		Point current = e.GetPosition(null);
		double dx = current.X - _mouseDownPos.X;
		if (!_isDragging)
		{
			if (Math.Abs(dx) < DragThreshold)
				return;

			_isDragging = true;
			_target.CaptureMouse();
		}

		float newValue = (float)(_startValue + dx * DragSpeed);
		_target.Text = newValue.ToString("0.###", CultureInfo.InvariantCulture);

		var binding = _target.GetBindingExpression(TextBox.TextProperty);
		binding?.UpdateSource();
	}

	private void Reset_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is EditableVector4 vec)
			vec.Reset(vec.DefaultVec);
	}
}

public class ChannelHashToString : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is null || value is not uint)
			return "";

		var hash = new StringHash(uint.Parse(value.ToString()));
		return GlobalStrings.Get().GetString(hash);
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}


public class EditableVector4 : INotifyPropertyChanged
{
	public Vector4 DefaultVec { get; set; }
	private float x, y, z, w;

	public EditableVector4(Vector4 value, VectorInputType type, Vector4? defaultVec = null)
	{
		DefaultVec = defaultVec ?? Vector4.Zero;
		X = value.X;
		Y = value.Y;
		Z = value.Z;
		W = value.W;
		VectorType = type;
	}

	public Vector4 Vec4 => new Vector4(X, Y, Z, W);

	private VectorInputType _vectorType;
	public VectorInputType VectorType
	{
		get => _vectorType;
		set
		{
			if (_vectorType == value)
				return;

			_vectorType = value;
			OnPropertyChanged(nameof(VectorType));
		}
	}

	public float X { get => x; set { x = value; OnPropertyChanged(nameof(X)); } }
	public float Y { get => y; set { y = value; OnPropertyChanged(nameof(Y)); } }
	public float Z { get => z; set { z = value; OnPropertyChanged(nameof(Z)); } }
	public float W { get => w; set { w = value; OnPropertyChanged(nameof(W)); } }

	public event PropertyChangedEventHandler PropertyChanged;
	private void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	public void Reset(Vector4 vec)
	{
		X = vec.X;
		Y = vec.Y;
		Z = vec.Z;
		W = vec.W;
	}

	public enum VectorInputType
	{
		Float,
		Vec2,
		Vec3,
		Vec4
	}
}

public class FloatConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return value;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		// return an invalid value in case of the value ends with a point
		//if (value.ToString() == string.Empty)
		//    return 0.0f;

		return value.ToString().EndsWith(".") ? "." : value;
	}
}

public class VectorComponentVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is not VectorInputType type || parameter is null)
			return Visibility.Collapsed;

		int componentIndex = System.Convert.ToInt32(parameter);

		int visibleComponents = type switch
		{
			VectorInputType.Float => 1,
			VectorInputType.Vec2 => 2,
			VectorInputType.Vec3 => 3,
			VectorInputType.Vec4 => 4,
			_ => 0
		};

		return componentIndex < visibleComponents
			? Visibility.Visible
			: Visibility.Collapsed;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}