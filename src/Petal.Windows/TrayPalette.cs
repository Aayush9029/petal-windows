using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace Petal.Windows;

// Use the built-in Fluent menu template instead of a custom popup window.
internal sealed class TrayPalette
{
    readonly AppController app;
    readonly ContextMenu menu = new() { Width = 360, StaysOpen = false };
    readonly Action appChanged;
    MenuItem? record;
    TextBlock? recordText;
    TextBlock? shortcutText;
    IntPtr target;
    internal IntPtr RecordingTarget => target;
    public bool IsVisible => menu.IsOpen;

    public TrayPalette(AppController app)
    {
        this.app = app;
        // Keep native menu behavior and Fluent colors, without the icon/gesture gutters.
        menu.Resources[typeof(MenuItem)] = (Style)XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="MenuItem">
              <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="MenuItem">
                    <Border x:Name="Row" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Padding="12,8" Background="Transparent">
                      <ContentPresenter ContentSource="Header" HorizontalAlignment="Stretch" RecognizesAccessKey="True"/>
                    </Border>
                    <ControlTemplate.Triggers>
                      <Trigger Property="IsHighlighted" Value="True">
                        <Setter TargetName="Row" Property="Background" Value="{DynamicResource ControlFillColorSecondaryBrush}"/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """);
        menu.Closed += (_, _) => { menu.Items.Clear(); record = null; };
        appChanged = () => { if (menu.IsOpen) UpdateRecordingAction(); };
        app.Changed += appChanged;
    }

    void UpdateRecordingAction()
    {
        if (record == null) return;
        recordText!.Text = app.Phase == "recording" ? "Finish recording" : "Start recording";
        shortcutText!.Text = app.Storage.Preferences.Shortcut;
        record.IsEnabled = !app.Busy || app.Phase == "recording";
    }

    void Build()
    {
        menu.Items.Clear();
        var recordingRow = new DockPanel();
        shortcutText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        shortcutText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        DockPanel.SetDock(shortcutText, Dock.Right); recordingRow.Children.Add(shortcutText);
        recordText = new TextBlock { TextWrapping = TextWrapping.NoWrap };
        recordingRow.Children.Add(recordText);
        record = new MenuItem { Header = recordingRow };
        record.Click += (_, _) => { Hide(); _ = app.ToggleRecording(target); };
        UpdateRecordingAction();
        menu.Items.Add(record);
        menu.Items.Add(new Separator());
        var recentLabel = new TextBlock { Text = "Recent transcripts", FontSize = 12, FontWeight = FontWeights.Normal };
        recentLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        menu.Items.Add(new MenuItem { Header = recentLabel, IsEnabled = false });

        var entries = app.Storage.History.OrderByDescending(x => x.Timestamp).Take(4).ToList();
        if (entries.Count == 0) menu.Items.Add(new MenuItem { Header = "No recordings yet", IsEnabled = false });
        foreach (var entry in entries)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 16, 0) };
            content.Children.Add(new TextBlock {
                Text = entry.Text.ReplaceLineEndings(" "),
                TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap
            });
            var date = new TextBlock {
                Text = entry.Timestamp.LocalDateTime.ToString("MMM d · h:mm tt"),
                FontSize = 12, Margin = new Thickness(0, 2, 0, 0)
            };
            date.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            content.Children.Add(date);
            row.Children.Add(content);
            var copy = Icons.Image("copy", 16, WindowsTheme.Secondary);
            Grid.SetColumn(copy, 1); row.Children.Add(copy);
            var item = new MenuItem { Header = row, ToolTip = "Copy transcript", Tag = entry.Id };
            System.Windows.Automation.AutomationProperties.SetName(item, "Copy transcript: " + entry.Text);
            item.Click += (_, _) => { Hide(); app.Copy(entry.Text); };
            menu.Items.Add(item);
        }
    }

    public void Open()
    {
        target = Native.GetForegroundWindow();
        Build();
        menu.Measure(new Size(menu.Width, double.PositiveInfinity));
        var area = Native.TrayWorkArea();
        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = area.Right - menu.Width - 8;
        menu.VerticalOffset = Math.Max(area.Top + 8, area.Bottom - menu.DesiredSize.Height - 8);
        menu.IsOpen = true;
    }

    public void Hide() => menu.IsOpen = false;
    public void Close() { Hide(); app.Changed -= appChanged; }

    internal void RenderPreview(string directory)
    {
        Build();
        menu.ApplyTemplate();
        menu.Measure(new Size(380, double.PositiveInfinity));
        var size = menu.DesiredSize;
        if (size.Width <= 0 || size.Height <= 0) throw new InvalidOperationException("Menu preview has no layout.");
        menu.Arrange(new Rect(size)); menu.UpdateLayout();
        System.IO.Directory.CreateDirectory(directory);
        SettingsWindow.Capture(menu, System.IO.Path.Combine(directory, "tray-palette.png"), (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
        if (menu.IsOpen) throw new InvalidOperationException("Offline rendering must not open a menu.");
    }

    internal void Capture(string directory)
    {
        Build();
        if (menu.Items.OfType<MenuItem>().Count(item => item.Tag != null) != Math.Min(4, app.Storage.History.Count))
            throw new InvalidOperationException("The tray menu must show at most four recent transcripts.");
        menu.IsOpen = true; menu.UpdateLayout();
        SettingsWindow.Capture(menu, System.IO.Path.Combine(directory, "tray-palette.png"), (int)menu.ActualWidth, (int)menu.ActualHeight);
        Hide();
    }
}
