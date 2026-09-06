using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Petal.Core;

namespace Petal.Windows;

// Materialize only visible rows, including their context menus.
internal sealed class HistoryList(Func<Transcript, string> title, Func<Transcript, ContextMenu> menu) : ListBox
{
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is not ListBoxItem container || item is not Transcript transcript) return;
        var row = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock { Text = title(transcript), FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 174 });
        var date = new TextBlock { Text = $"{transcript.Timestamp.LocalDateTime:MMM d, h:mm tt}  ·  {TimeSpan.FromSeconds(transcript.Duration):m\\:ss}", FontSize = 11, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 4, 0, 0) };
        date.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        row.Children.Add(date);
        container.Content = row;
        container.Height = 58; container.MinHeight = 0;
        container.Padding = new Thickness(10, 4, 10, 4); container.Margin = new Thickness(1);
        container.VerticalContentAlignment = VerticalAlignment.Center;
        container.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        container.ContextMenu = menu(transcript);
        System.Windows.Automation.AutomationProperties.SetName(container, title(transcript));
    }
    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is ListBoxItem container) { container.ContextMenu = null; container.Content = null; }
        base.ClearContainerForItemOverride(element, item);
    }
}
