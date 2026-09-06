using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Petal.Windows;

internal sealed class TrayPalette : Window
{
    readonly AppController app;
    IntPtr target;
    internal IntPtr RecordingTarget => target;
    readonly StackPanel results = new();
    readonly TextBox search = new() { Margin = new Thickness(0, 16, 0, 12) };
    readonly Button record = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock feedback = new() { FontSize = 12, Margin = new Thickness(0, 10, 0, 0) };
    public TrayPalette(AppController app)
    {
        this.app = app;
        Title = "Petal palette"; Width = 380; Height = 520; WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(-1) });
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Petal", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        record.Click += (_, _) => { Hide(); _ = app.ToggleRecording(target); }; header.Children.Add(record);
        search.ToolTip = "Search transcripts"; System.Windows.Automation.AutomationProperties.SetName(search, "Search transcripts");
        header.Children.Add(new TextBlock { Text = "Search history", FontSize = 12, Margin = new Thickness(0, 12, 0, 0) });
        search.Margin = new Thickness(0, 6, 0, 12);
        var searchRow = new DockPanel(); var clear = new Button { Content = "Clear", Margin = new Thickness(8, 6, 0, 12), IsEnabled = false };
        clear.Click += (_, _) => { search.Clear(); search.Focus(); }; search.TextChanged += (_, _) => clear.IsEnabled = search.Text.Length > 0;
        DockPanel.SetDock(clear, Dock.Right); searchRow.Children.Add(clear); searchRow.Children.Add(search); header.Children.Add(searchRow); root.Children.Add(header);
        var scroll = new ScrollViewer { Content = results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        void Add(string label, Action action) { var b = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0) }; b.Click += (_, _) => { Hide(); action(); }; actions.Children.Add(b); }
        Add("History", () => app.ShowSettings("History")); Add("Import…", app.ImportFile); Add("Settings", () => app.ShowSettings());
        footer.Children.Add(actions); footer.Children.Add(feedback); Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        SourceInitialized += (_, _) => Native.Acrylic(this);
        Deactivated += (_, _) => Hide();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
        search.TextChanged += (_, _) => Refresh();
        app.Changed += () => { if (IsVisible) Refresh(); };
    }
    void Refresh()
    {
        record.Content = (app.Phase == "recording" ? "Finish recording" : "Start recording") + "   ·   " + app.Storage.Preferences.Shortcut;
        record.IsEnabled = !app.Busy || app.Phase == "recording";
        feedback.Text = app.Status; results.Children.Clear();
        var matches = app.Storage.History.Where(x => Petal.Core.TranscriptSearch.Matches(x, search.Text)).ToList();
        var entries = matches.Take(20).ToList();
        results.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(search.Text) ? "Recent transcripts" : $"{matches.Count} results" + (matches.Count > 20 ? " (first 20 shown)" : ""), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        if (entries.Count == 0) results.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(search.Text) ? "No recordings yet." : "No matches. Try fewer words.", Margin = new Thickness(0, 16, 0, 16) });
        foreach (var entry in entries)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = entry.Text, MaxHeight = 42, TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(new TextBlock { Text = entry.Timestamp.ToLocalTime().ToString("MMM d · h:mm tt") + "  ·  Copy", FontSize = 11, Margin = new Thickness(0, 6, 0, 0), Opacity = .65 });
            var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12) };
            System.Windows.Automation.AutomationProperties.SetName(button, "Copy transcript: " + entry.Text);
            button.Click += (_, _) => app.Copy(entry.Text); results.Children.Add(button);
        }
    }
    public void Open()
    {
        target = Native.GetForegroundWindow(); Refresh(); var area = SystemParameters.WorkArea; Left = area.Right - Width - 12; Top = area.Bottom - Height - 12;
        Show(); Activate(); search.Focus();
    }
    internal void Capture(string directory)
    {
        Refresh(); Show(); UpdateLayout();
        SettingsWindow.Capture((FrameworkElement)Content, System.IO.Path.Combine(directory, "tray-palette.png"), 380, 520);
        search.Text = "__petal_no_matching_transcript__";
        if (results.Children.OfType<Button>().Any()) throw new InvalidOperationException("Palette search failed to filter transcripts.");
        search.Clear(); Hide();
    }
}
