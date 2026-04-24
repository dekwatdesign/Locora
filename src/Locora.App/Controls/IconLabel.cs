using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Locora.App.Controls;

public sealed class IconLabel : StackPanel
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(string),
        typeof(IconLabel),
        new PropertyMetadata(string.Empty, OnGlyphChanged));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(IconLabel),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty IconFontFamilyProperty = DependencyProperty.Register(
        nameof(IconFontFamily),
        typeof(FontFamily),
        typeof(IconLabel),
        new PropertyMetadata(null, OnIconFontFamilyChanged));

    public static readonly DependencyProperty IconFontSizeProperty = DependencyProperty.Register(
        nameof(IconFontSize),
        typeof(double),
        typeof(IconLabel),
        new PropertyMetadata(14d, OnIconFontSizeChanged));

    private readonly FontIcon _icon = new()
    {
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly TextBlock _label = new()
    {
        VerticalAlignment = VerticalAlignment.Center
    };

    public IconLabel()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_icon);
        Children.Add(_label);
        Loaded += OnLoaded;
        UpdateText();
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontFamily? IconFontFamily
    {
        get => (FontFamily?)GetValue(IconFontFamilyProperty);
        set => SetValue(IconFontFamilyProperty, value);
    }

    public double IconFontSize
    {
        get => (double)GetValue(IconFontSizeProperty);
        set => SetValue(IconFontSizeProperty, value);
    }

    private static void OnGlyphChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((IconLabel)dependencyObject)._icon.Glyph = (string)args.NewValue;
    }

    private static void OnTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((IconLabel)dependencyObject).UpdateText();
    }

    private static void OnIconFontFamilyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((IconLabel)dependencyObject)._icon.FontFamily = (FontFamily?)args.NewValue;
    }

    private static void OnIconFontSizeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((IconLabel)dependencyObject)._icon.FontSize = (double)args.NewValue;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _icon.FontSize = IconFontSize;

        if (IconFontFamily is null &&
            Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("FontAwesomeSolidFontFamily", out var resource) &&
            resource is FontFamily fontFamily)
        {
            _icon.FontFamily = fontFamily;
        }
    }

    private void UpdateText()
    {
        _label.Text = Text;
        _label.Visibility = string.IsNullOrWhiteSpace(Text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
