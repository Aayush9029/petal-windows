using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace Petal.Windows;

/// <summary>Renders the bundled, unmodified Lucide assets with WPF's vector drawing engine.</summary>
public static class Icons
{
    public static ImageSource ChevronDown => Source("chevron-down", Brushes.LightGray);

    public static Image Image(string name, double size = 16, Brush? color = null) => new()
    {
        Source = Source(name, color ?? WindowsTheme.Foreground), Width = size, Height = size,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        IsHitTestVisible = false
    };

    public static StackPanel Label(string name, string text, Brush? color = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Image(name, 14, color); icon.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = color ?? WindowsTheme.Foreground });
        return row;
    }

    static ImageSource Source(string name, Brush color)
    {
        using var stream = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Icons/{name}.svg")).Stream;
        var svg = XDocument.Load(stream).Root!;
        var group = new DrawingGroup();
        var pen = new Pen(color, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        // Preserve Lucide's 24×24 viewBox, including its optical padding.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        foreach (var element in svg.Elements())
        {
            double Number(string attribute) => double.Parse(element.Attribute(attribute)?.Value ?? "0", CultureInfo.InvariantCulture);
            Geometry geometry = element.Name.LocalName switch
            {
                "path" => Geometry.Parse(element.Attribute("d")!.Value),
                "circle" => new EllipseGeometry(new Point(Number("cx"), Number("cy")), Number("r"), Number("r")),
                "rect" => new RectangleGeometry(new Rect(Number("x"), Number("y"), Number("width"), Number("height")), Number("rx"), Number("rx")),
                "line" => new LineGeometry(new Point(Number("x1"), Number("y1")), new Point(Number("x2"), Number("y2"))),
                "polyline" => Geometry.Parse("M " + element.Attribute("points")!.Value),
                "polygon" => Geometry.Parse("M " + element.Attribute("points")!.Value + " Z"),
                _ => throw new InvalidOperationException($"Unsupported element in Lucide asset {name}: {element.Name}")
            };
            group.Children.Add(new GeometryDrawing(null, pen, geometry));
        }
        group.Freeze();
        return new DrawingImage(group);
    }
}
