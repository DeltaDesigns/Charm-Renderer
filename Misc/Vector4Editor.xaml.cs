using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Tiger;

namespace Charm.Renderer;

/// <summary>
/// Interaction logic for Vector4Editor.xaml
/// </summary>
public partial class Vector4Editor : UserControl
{
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

    private const float DragSpeed = 0.02f;
    private const double DragThreshold = 5.0;
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
            vec.Reset();
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
