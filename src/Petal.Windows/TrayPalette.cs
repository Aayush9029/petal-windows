using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Petal.Windows;

// Use the built-in Fluent menu template instead of a custom popup window.
internal sealed class TrayPalette
{
    readonly AppController app;
    readonly ContextMenu menu = new() { MinWidth = 380 };
    readonly Action appChanged;
    MenuItem? record;
    IntPtr target;
    internal IntPtr RecordingTarget => target;
    public bool IsVisible => menu.IsOpen;

    public TrayPalette(AppController app)
    {
        this.app = app;
        menu.Closed += (_, _) => { menu.Items.Clear(); record = null; };
        appChanged = () => { if (menu.IsOpen) UpdateRecordingAction(); };
        app.Changed += appChanged;
    }

    void UpdateRecordingAction()
    {
        if (record == null) return;
        record.Header = app.Phase == "recording" ? "Finish recording" : "Start recording";
        record.InputGestureText = app.Storage.Preferences.Shortcut;
        record.IsEnabled = !app.Busy || app.Phase == "recording";
    }

    void Build()
    {
        menu.Items.Clear();
        record = new MenuItem { Icon = Icons.Image("mic") };
        record.Click += (_, _) => { Hide(); _ = app.ToggleRecording(target); };
        UpdateRecordingAction();
        menu.Items.Add(record);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Recent transcripts", IsEnabled = false });

        var entries = app.Storage.History.OrderByDescending(x => x.Timestamp).Take(4).ToList();
        if (entries.Count == 0) menu.Items.Add(new MenuItem { Header = "No recordings yet", IsEnabled = false });
        foreach (var entry in entries)
        {
            var content = new StackPanel { Width = 230 };
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
            var item = new MenuItem { Header = content, ToolTip = "Copy transcript", Tag = entry.Id };
            System.Windows.Automation.AutomationProperties.SetName(item, "Copy transcript: " + entry.Text);
            item.Click += (_, _) => { Hide(); app.Copy(entry.Text); };
            menu.Items.Add(item);
        }
    }

    public void Open()
    {
        target = Native.GetForegroundWindow();
        Build();
        menu.Placement = PlacementMode.MousePoint;
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
