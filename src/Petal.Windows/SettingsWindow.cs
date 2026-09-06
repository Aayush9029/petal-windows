using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Petal.Core;

namespace Petal.Windows;

public sealed class SettingsWindow : Window
{
    readonly AppController app;
    readonly ListBox navigation = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(12, 12, 12, 0) };
    readonly StackPanel pane = new() { Margin = new Thickness(0, 20, 0, 20) };
    readonly ContentControl pageHost = new();
    readonly TextBlock heading = new() { FontSize = 28, FontWeight = FontWeights.SemiBold };
    readonly TextBlock description = new() { Margin = new Thickness(0, 5, 0, 0) };
    readonly TextBlock status = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 40 };
    readonly string[] pages = ["General", "Recording", "Models", "History", "Advanced"];
    readonly Dictionary<string, (Button Button, ProgressBar Progress, TextBlock Detail)> modelControls = [];
    CancellationTokenSource? download;
    string? downloadingId;
    string page = "General";
    string historyQuery = "";
    string? playingId;
    MediaPlayer? playback;
    bool playbackPlaying, playbackReady; double playbackDuration; bool mutePlaybackCheck;
    DateTime playbackOpening;
    ListBox? historyList;
    TextBox? searchBox;
    StackPanel? historyDetail;
    Button? playbackButton;
    readonly System.Windows.Threading.DispatcherTimer playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    Slider? playbackPosition;
    TextBlock? playbackTime;
    bool updatingPlayback;
    bool filteringHistory;
    readonly System.Windows.Threading.DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(180) };
    static Brush Brush(string value) => value == "#0A84FF" ? SystemColors.HighlightBrush : WindowsTheme.Secondary;
    static TextBlock Text(string value, int size = 14, string? color = null)
    {
        var block = new TextBlock { Text = value, FontSize = size };
        block.SetResourceReference(TextBlock.ForegroundProperty, color == null ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        return block;
    }
    public SettingsWindow(AppController app)
    {
        this.app = app;
        Title = "Petal"; Width = 1000; Height = 740; MinWidth = 880; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/petal.png"));
        var root = new Grid(); root.SetResourceReference(Grid.BackgroundProperty, "SolidBackgroundFillColorBaseBrush"); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(216) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new DockPanel();
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 25, 12, 8) };
        brand.Children.Add(new Image { Source = Icon, Width = 32, Height = 32, Margin = new Thickness(0, 0, 10, 0) });
        brand.Children.Add(new TextBlock { Text = "Petal", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        string[] icons = ["settings", "mic", "box", "clock", "sliders-horizontal"];
        for (int i = 0; i < pages.Length; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var icon = Icons.Image(icons[i], 20); icon.Margin = new Thickness(0, 0, 14, 0); row.Children.Add(icon); row.Children.Add(Text(pages[i]));
            var item = new ListBoxItem { Content = row, Tag = pages[i], HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, Height = 36, MinHeight = 0, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 1, 0, 1) };
            System.Windows.Automation.AutomationProperties.SetName(item, pages[i]); navigation.Items.Add(item);
        }
        navigation.SelectionChanged += (_, _) => { if (navigation.SelectedItem is ListBoxItem item && page != (string)item.Tag) SelectPage((string)item.Tag); };
        sidebar.Children.Add(navigation); root.Children.Add(sidebar);
        var detail = new Grid { Margin = new Thickness(28, 24, 28, 18) };
        detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); detail.RowDefinitions.Add(new RowDefinition()); detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel(); header.Children.Add(heading); description.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush"); header.Children.Add(description); detail.Children.Add(header);
        Grid.SetRow(pageHost, 1); detail.Children.Add(pageHost);
        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.Text = app.Status; status.VerticalAlignment = VerticalAlignment.Center; status.Margin = new Thickness(0, 0, 16, 0); status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush"); footer.Children.Add(status);
        var import = ActionButton("Transcribe file…", app.ImportFile); Grid.SetColumn(import, 1); footer.Children.Add(import); Grid.SetRow(footer, 2); detail.Children.Add(footer);
        Grid.SetColumn(detail, 1); root.Children.Add(detail); Content = root;
        Closing += (_, e) => { e.Cancel = true; StopPlayback(); Hide(); };
        int historyCount = app.Storage.History.Count;
        app.Changed += () => { status.Text = app.Status; import.IsEnabled = !app.Busy; if (page == "History" && historyCount != app.Storage.History.Count) RenderHistory(); historyCount = app.Storage.History.Count; UpdateModels(); };
        playbackTimer.Tick += (_, _) => UpdatePlayback();
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); if (page == "History") PopulateHistory(); };
        AllowDrop = true;
        Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files) _ = app.Transcribe(files[0], Path.GetFileName(files[0])); else app.SetStatus("Drop one audio file at a time."); };
        SelectPage("General");
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control) { if (page != "History") SelectPage("History"); searchBox?.Focus(); searchBox?.SelectAll(); e.Handled = true; } };
    }
    public void SelectPage(string name)
    {
        searchDelay.Stop(); StopPlayback(); page = name; heading.Text = name; pane.Children.Clear(); modelControls.Clear();
        navigation.SelectedIndex = Array.IndexOf(pages, name);
        description.Text = name switch { "General" => "Shortcuts and how your dictation is inserted.", "Recording" => "Microphone and recording preferences.", "Models" => "Choose a speech recognition model.", "History" => "Find, play, and copy your previous recordings.", _ => "Storage and application settings." };
        pageHost.Content = new ScrollViewer { Content = pane, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        switch (name) { case "General": General(); break; case "Recording": Recording(); break; case "Models": Models(); break; case "History": RenderHistory(); break; case "Advanced": Advanced(); break; }
    }
    void Save() => app.Storage.SavePreferences();
    static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => action(); return button;
    }
    static CheckBox Toggle(string name, bool value, Action<bool> change)
    {
        var box = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(box, name); box.Click += (_, _) => change(box.IsChecked == true); return box;
    }
    static ComboBox Picker<T>(IEnumerable<T> items, T selected, Action<T> change)
    {
        var box = new ComboBox { ItemsSource = items, SelectedItem = selected, MinWidth = 150, MaxWidth = 220, VerticalAlignment = VerticalAlignment.Center };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is T item) change(item); }; return box;
    }
    static Grid Row(string title, string? description, UIElement control)
    {
        var row = new Grid { Margin = new Thickness(18, 16, 18, 16), MinHeight = 36 }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        label.Children.Add(Text(title)); if (description != null) { var sub = Text(description, 12, "secondary"); sub.Margin = new Thickness(0, 4, 0, 0); label.Children.Add(sub); }
        row.Children.Add(label); Grid.SetColumn(control, 1); row.Children.Add(control); return row;
    }
    static Border Surface(UIElement child)
    {
        var border = new Border { Child = child, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush"); border.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush"); return border;
    }
    void Card(string? title, params UIElement[] rows)
    {
        var group = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        if (title != null) { var label = Text(title, 14); label.FontWeight = FontWeights.SemiBold; label.Margin = new Thickness(0, 0, 0, 8); group.Children.Add(label); }
        var stack = new StackPanel(); foreach (var row in rows) { if (stack.Children.Count > 0) stack.Children.Add(new Separator { Margin = new Thickness(18, 0, 18, 0) }); stack.Children.Add(row); }
        group.Children.Add(Surface(stack)); pane.Children.Add(group);
    }
    void General()
    {
        var p = app.Storage.Preferences;
        var shortcut = ActionButton(p.Shortcut, () => { app.EditShortcut(this); SelectPage("General"); });
        Card("Dictation", Row("Recording shortcut", "Press once to start. Press again to finish.", shortcut),
            Row("Insert into active app", "Paste the finished transcript where you were typing.", Toggle("Insert into active app", p.AutoPaste, v => { p.AutoPaste = v; Save(); })),
            Row("Restore clipboard", "Restore your previous clipboard after automatic insertion.", Toggle("Restore clipboard", p.RestoreClipboard, v => { p.RestoreClipboard = v; Save(); })));
        pane.Children.Add(new TextBlock { Text = p.Shortcut == "Alt + Space" ? "Alt + Space replaces the Windows window-menu shortcut while Petal is running." : "Your chosen key is reserved for Petal while it is running. Click the shortcut above to change it.", FontSize = 12, Foreground = WindowsTheme.Secondary, Margin = new Thickness(2, -8, 2, 22) });
        Card("Get started", Row("Record your voice", "Use the shortcut from any app. Imported files are copied to the clipboard.", ActionButton("Start recording", () => _ = app.ToggleRecording())));
    }
    void Recording()
    {
        var p = app.Storage.Preferences; var devices = Recorder.Devices().ToList();
        var mic = new ComboBox { ItemsSource = devices.Select(x => x.Name).ToList(), SelectedIndex = Math.Max(0, devices.FindIndex(x => x.Id == p.Microphone)), MaxWidth = 230, MinWidth = 150 };
        mic.SelectionChanged += (_, _) => { if (mic.SelectedIndex >= 0) { p.Microphone = devices[mic.SelectedIndex].Id; Save(); } };
        Card("Input", Row("Microphone", "The device used for dictation.", mic), Row("Microphone permissions", "Manage desktop microphone access in Windows Settings.", ActionButton("Open Settings", () => Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true }))));
        Card("While recording", Row("Trim silence", "Remove quiet edges from recordings.", Toggle("Trim silence", p.TrimSilence, v => { p.TrimSilence = v; Save(); })), Row("Lower other audio", "Reduce other apps’ volume while the microphone is active.", Toggle("Lower other audio", p.LowerAudio, v => { p.LowerAudio = v; Save(); })));
        pane.Children.Add(Text("The recording bar appears above the taskbar. Hover to reveal Finish, then click to transcribe. Recordings stop after 10 minutes; imported files can be up to two hours.", 12, "secondary"));
    }
    void Models()
    {
        var rows = new List<UIElement>();
        foreach (var model in ModelCatalog.All)
        {
            var outer = new Grid();
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) }); outer.ColumnDefinitions.Add(new ColumnDefinition());
            outer.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/" + (model.Engine == "parakeet" ? "nvidia" : "openai") + ".png")), Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) });
            var body = new StackPanel { Margin = new Thickness(0, 11, 14, 11) }; Grid.SetColumn(body, 1); outer.Children.Add(body);
            var name = new Grid(); name.ColumnDefinitions.Add(new ColumnDefinition()); name.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Text(model.Name); title.FontWeight = FontWeights.SemiBold; title.VerticalAlignment = VerticalAlignment.Center; name.Children.Add(title);
            var button = ActionButton("Download", () => _ = SelectModel(model)); Grid.SetColumn(button, 1); name.Children.Add(button); body.Children.Add(name);
            var description = Text(model.Summary, 11, "#AEADB2"); description.Margin = new Thickness(0, 7, 0, 6); body.Children.Add(description);
            var meta = Text($"{model.Provider}  ·  {model.SizeLabel}  ·  INT8" + (model.Recommended ? "  ·  Recommended" : ""), 10, "#96959D"); body.Children.Add(meta);
            var progress = new ProgressBar { Height = 3, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 9, 0, 0), Visibility = Visibility.Collapsed, Foreground = Brush("#0A84FF") }; body.Children.Add(progress);
            var details = Text("", 10, "#A8A8AD"); details.Margin = new Thickness(0, 5, 0, 0); body.Children.Add(details);
            var menu = new ContextMenu(); var delete = new MenuItem { Header = "Delete downloaded model…" };
            delete.Click += async (_, _) =>
            {
                if (app.Busy || downloadingId != null) { app.SetStatus("Wait for the current operation to finish."); return; }
                if (System.Windows.MessageBox.Show($"Remove {model.Name}? You can download it again later.", "Delete Download", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                await app.Models.DeleteAsync(model); UpdateModels();
            };
            menu.Items.Add(delete); outer.ContextMenu = menu;
            modelControls[model.Id] = (button, progress, details); rows.Add(outer);
        }
        Card("Speech Model", rows.ToArray()); UpdateModels();
        pane.Children.Add(Text("Models run on your CPU. No account or NVIDIA GPU required. Downloads can be paused and resumed. Right-click a model to remove its download.", 11, "#99999F"));
    }
    async Task SelectModel(SpeechModel model)
    {
        if (downloadingId == model.Id) { download?.Cancel(); return; }
        if (app.Busy || downloadingId != null) return;
        if (app.Models.IsInstalled(model)) { app.Storage.Preferences.ModelId = model.Id; Save(); UpdateModels(); app.SetStatus(model.Name + " selected"); return; }
        download = new(); downloadingId = model.Id; UpdateModels();
        try
        {
            var progress = new Progress<double>(v =>
            {
                if (modelControls.TryGetValue(model.Id, out var controls)) { controls.Progress.Value = v; controls.Detail.Text = $"Downloading… {v:P0}"; }
            });
            await app.Models.DownloadAsync(model, progress, download.Token);
            app.Storage.Preferences.ModelId = model.Id; Save(); app.SetStatus(model.Name + " is ready");
        }
        catch (OperationCanceledException) { app.SetStatus("Download paused. Click Download to resume."); }
        catch (Exception ex) { app.SetStatus("Download failed: " + ex.Message); }
        finally { downloadingId = null; download.Dispose(); download = null; UpdateModels(); }
    }
    void UpdateModels()
    {
        foreach (var model in ModelCatalog.All)
        {
            if (!modelControls.TryGetValue(model.Id, out var controls)) continue;
            bool installed = app.Models.IsInstalled(model), active = downloadingId == model.Id;
            controls.Button.Content = active ? Icons.Label("pause", "Pause") : installed ? app.Storage.Preferences.ModelId == model.Id ? Icons.Label("check", "Selected") : "Use Model" : Icons.Label("download", "Download");
            controls.Button.IsEnabled = !app.Busy && (downloadingId == null || active);
            controls.Button.ClearValue(Button.BackgroundProperty);
            controls.Progress.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (!active) { controls.Detail.Text = installed ? "Available offline" : ""; controls.Detail.Visibility = installed ? Visibility.Visible : Visibility.Collapsed; }
            else controls.Detail.Visibility = Visibility.Visible;
        }
    }
    void RenderHistory()
    {
        var layout = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
        var search = new TextBox { Text = historyQuery, ToolTip = "Search transcripts", Margin = new Thickness(0, 0, 0, 14) };
        System.Windows.Automation.AutomationProperties.SetName(search, "Search transcripts");
        var searchRow = new DockPanel(); var searchLabel = Text("Search", 14); searchLabel.Margin = new Thickness(0, 0, 12, 14); searchLabel.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(searchLabel, Dock.Left); searchRow.Children.Add(searchLabel);
        var clear = ActionButton("Clear", () => { search.Clear(); search.Focus(); }); clear.Margin = new Thickness(8, 0, 0, 14); DockPanel.SetDock(clear, Dock.Right); searchRow.Children.Add(clear);
        search.ToolTip = "Search words, model names, or dates. Ctrl+F focuses search. Escape clears it.";
        search.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { search.Clear(); e.Handled = true; } };
        searchRow.Children.Add(search); layout.Children.Add(searchRow);
        var split = new Grid(); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) }); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); split.ColumnDefinitions.Add(new ColumnDefinition());
        historyList = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(5), Padding = new Thickness(0) };
        historyList.SelectionChanged += (_, _) => { if (!filteringHistory) ShowTranscript((historyList.SelectedItem as ListBoxItem)?.Tag as Transcript); };
        split.Children.Add(Surface(historyList));
        historyDetail = new StackPanel { Margin = new Thickness(18) };
        var detailScroll = new ScrollViewer { Content = historyDetail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var detailBorder = Surface(detailScroll); Grid.SetColumn(detailBorder, 2); split.Children.Add(detailBorder);
        Grid.SetRow(split, 1); layout.Children.Add(split); pageHost.Content = layout;
        search.TextChanged += (_, _) => { historyQuery = search.Text; clear.IsEnabled = search.Text.Length > 0; searchDelay.Stop(); searchDelay.Start(); }; clear.IsEnabled = search.Text.Length > 0;
        searchBox = search; PopulateHistory();
    }
    void PopulateHistory()
    {
        if (historyList == null) return;
        var selected = (historyList.SelectedItem as ListBoxItem)?.Tag as Transcript;
        filteringHistory = true;
        historyList.Items.Clear();
        foreach (var item in app.Storage.History.Where(x => TranscriptSearch.Matches(x, historyQuery)))
        {
            var row = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock { Text = TranscriptTitle(item), FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 174 });
            var date = Text($"{item.Timestamp.LocalDateTime:MMM d, h:mm tt}  ·  {TimeSpan.FromSeconds(item.Duration):m\\:ss}", 11, "secondary"); date.TextWrapping = TextWrapping.NoWrap; date.Margin = new Thickness(0, 4, 0, 0); row.Children.Add(date);
            var entry = new ListBoxItem { Content = row, Tag = item, Height = 58, MinHeight = 0, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Stretch, ContextMenu = TranscriptMenu(item) };
            historyList.Items.Add(entry); if (item.Id == selected?.Id) historyList.SelectedItem = entry;
        }
        if (historyList.SelectedItem == null && historyList.Items.Count > 0) historyList.SelectedIndex = 0;
        filteringHistory = false;
        var next = (historyList.SelectedItem as ListBoxItem)?.Tag as Transcript;
        if (next?.Id != selected?.Id || next == null) ShowTranscript(next);
    }
    ContextMenu TranscriptMenu(Transcript item)
    {
        var menu = new ContextMenu();
        void Add(string label, Action action, bool enabled = true) { var entry = new MenuItem { Header = label, IsEnabled = enabled }; entry.Click += (_, _) => action(); menu.Items.Add(entry); }
        Add("Copy transcript", () => app.Copy(item.Text));
        Add("Export text…", () => { var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"Petal {item.Timestamp:yyyy-MM-dd HH-mm}.txt", Filter = "Text file|*.txt" }; if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, item.Text); });
        var audio = app.Storage.AudioPath(item);
        Add("Transcribe again", () => _ = app.Transcribe(audio!, "History"), !app.Busy && audio != null && File.Exists(audio));
        menu.Items.Add(new Separator());
        Add("Delete recording", () => { if (!Confirm("Delete this transcript and its saved audio?")) return; StopPlayback(); app.Storage.Delete(item); PopulateHistory(); });
        return menu;
    }
    static string TranscriptTitle(Transcript item)
    {
        if (item.Source is not ("Microphone" or "History") && !string.IsNullOrWhiteSpace(item.Source)) return item.Source;
        return item.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Recording";
    }
    void ShowTranscript(Transcript? item)
    {
        StopPlayback(); if (historyDetail == null) return; historyDetail.Children.Clear();
        if (item == null)
        {
            var icon = Icons.Image(historyQuery.Length == 0 ? "audio-lines" : "search", 36, WindowsTheme.Secondary); icon.Margin = new Thickness(0, 70, 0, 14); historyDetail.Children.Add(icon);
            historyDetail.Children.Add(Text(historyQuery.Length == 0 ? "No recordings yet" : "No matching recordings", 18));
            historyDetail.Children.Add(Text(string.IsNullOrWhiteSpace(historyQuery) ? "Your recordings will appear here." : "Try fewer words or clear the search.", 12, "secondary")); return;
        }
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var more = ActionButton("More", () => { }); more.ContextMenu = TranscriptMenu(item); more.Click += (_, _) => { more.ContextMenu = TranscriptMenu(item); more.ContextMenu.PlacementTarget = more; more.ContextMenu.Placement = PlacementMode.Bottom; more.ContextMenu.IsOpen = true; }; DockPanel.SetDock(more, Dock.Right); toolbar.Children.Add(more);
        var copy = ActionButton("Copy", () => app.Copy(item.Text)); copy.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(copy, Dock.Right); toolbar.Children.Add(copy);
        toolbar.Children.Add(new TextBlock { Text = TranscriptTitle(item), FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) }); historyDetail.Children.Add(toolbar);
        string modelName = ModelCatalog.All.FirstOrDefault(x => x.Id == item.ModelId)?.Name ?? item.ModelId;
        historyDetail.Children.Add(Text($"{item.Timestamp.LocalDateTime:MMM d, h:mm tt} · {modelName}", 12, "secondary"));
        if (app.Storage.AudioPath(item) is { } audio && File.Exists(audio))
        {
            var player = new Grid { Margin = new Thickness(0, 16, 0, 12) }; player.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); player.ColumnDefinitions.Add(new ColumnDefinition());
            playbackButton = ActionButton("Play", () => Play(item, audio)); playbackButton.MinWidth = 65; player.Children.Add(playbackButton);
            var position = new StackPanel { Margin = new Thickness(12, 0, 0, 0) }; playbackPosition = new Slider { Minimum = 0, Maximum = Math.Max(item.Duration, 1), IsMoveToPointEnabled = true }; System.Windows.Automation.AutomationProperties.SetName(playbackPosition, "Playback position");
            playbackPosition.ValueChanged += (_, _) => { if (!updatingPlayback && playback != null && playbackReady) playback.Position = TimeSpan.FromSeconds(Math.Min(playbackPosition.Value, playbackDuration)); };
            playbackTime = Text($"0:00 / {TimeSpan.FromSeconds(item.Duration):m\\:ss}", 11, "secondary"); position.Children.Add(playbackPosition); position.Children.Add(playbackTime); Grid.SetColumn(position, 1); player.Children.Add(position); historyDetail.Children.Add(player);
        }
        historyDetail.Children.Add(new TextBox { Text = item.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), FontSize = 16, Margin = new Thickness(0, 14, 0, 0) });
    }
    void Play(Transcript item, string path)
    {
        if (playback != null && playingId == item.Id)
        {
            if (!playbackReady) return;
            if (playbackPlaying) { playback.Pause(); playbackPlaying = false; playbackButton!.Content = "Play"; }
            else { playback.Play(); playbackPlaying = true; playbackButton!.Content = "Pause"; }
            return;
        }
        StopPlayback(); playingId = item.Id;
        var current = new MediaPlayer { Volume = mutePlaybackCheck ? 0 : 1 }; playback = current;
        playbackOpening = DateTime.UtcNow; playbackTimer.Start();
        playbackButton!.Content = "Loading..."; playbackButton.IsEnabled = false;
        current.MediaOpened += (_, _) =>
        {
            if (!ReferenceEquals(playback, current)) return;
            playbackReady = true; playbackDuration = current.NaturalDuration.HasTimeSpan ? current.NaturalDuration.TimeSpan.TotalSeconds : item.Duration;
            playbackButton!.IsEnabled = true; playbackButton.Content = "Pause";
            current.Play(); playbackPlaying = true; playbackTimer.Start();
        };
        current.MediaEnded += (_, _) => { if (ReferenceEquals(playback, current)) StopPlayback(); };
        current.MediaFailed += (_, e) => { if (ReferenceEquals(playback, current)) { StopPlayback(); app.SetStatus("Couldn't play this recording. Check your audio output or try another recording."); } };
        try { current.Open(new Uri(path, UriKind.Absolute)); }
        catch (Exception) { StopPlayback(); app.SetStatus("Couldn't open this recording. The audio file may be missing or damaged."); }
    }
    void UpdatePlayback()
    {
        if (playback != null && !playbackReady && DateTime.UtcNow - playbackOpening > TimeSpan.FromSeconds(10)) { StopPlayback(); app.SetStatus("Audio took too long to open. Check your output device and try again."); return; }
        if (playback == null || !playbackReady || playbackPosition == null) return;
        updatingPlayback = true;
        try { playbackPosition.Maximum = Math.Max(playbackDuration, 1); playbackPosition.Value = playback.Position.TotalSeconds; }
        finally { updatingPlayback = false; }
        if (playbackTime != null) playbackTime.Text = $"{playback.Position:m\\:ss} / {TimeSpan.FromSeconds(playbackDuration):m\\:ss}";
    }
    void StopPlayback()
    {
        playbackTimer.Stop(); var old = playback; playback = null; playbackReady = playbackPlaying = false;
        old?.Close(); playingId = null;
        if (playbackButton != null) { playbackButton.Content = "Play"; playbackButton.IsEnabled = true; }
        if (playbackPosition != null) { updatingPlayback = true; playbackPosition.Value = 0; updatingPlayback = false; }
    }
    internal async Task CheckPlayback(string output)
    {
        SelectPage("History");
        var item = app.Storage.History.FirstOrDefault(x => app.Storage.AudioPath(x) is string path && File.Exists(path)) ?? throw new Exception("Playback check needs saved fixture audio.");
        ShowTranscript(item); mutePlaybackCheck = true;
        int ticks = 0; var heartbeat = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        heartbeat.Tick += (_, _) => ticks++; heartbeat.Start();
        try
        {
            var path = app.Storage.AudioPath(item)!; Play(item, path);
            for (int i = 0; i < 100 && !playbackReady; i++) await Task.Delay(50);
            if (!playbackReady || ticks < 1) throw new Exception("Playback failed to open or UI did not respond.");
            historyQuery = "country"; PopulateHistory();
            if (!playbackReady) throw new Exception("Search interrupted matching playback.");
            historyQuery = ""; PopulateHistory();
            await Task.Delay(250); Play(item, path);
            if (playbackPlaying) throw new Exception("Pause failed.");
            playbackPosition!.Value = Math.Min(1, playbackDuration / 2);
            Play(item, path); if (!playbackPlaying) throw new Exception("Resume failed.");
            await Task.Delay(150); SelectPage("General");
            if (playback != null) throw new Exception("Navigation did not release playback.");
            SelectPage("History"); ShowTranscript(item); Play(item, path);
            for (int i = 0; i < 100 && !playbackReady; i++) await Task.Delay(50);
            if (!playbackReady) throw new Exception("Replay failed.");
            playbackPosition!.Value = Math.Max(0, playbackDuration - .2);
            for (int i = 0; i < 100 && playback != null; i++) await Task.Delay(50);
            if (playback != null) throw new Exception("Playback did not finish.");
            Play(item, path + ".missing.wav");
            for (int i = 0; i < 100 && playback != null; i++) await Task.Delay(50);
            if (playback != null) throw new Exception("Missing audio did not recover.");
            File.WriteAllText(Path.Combine(output, "playback.txt"), $"PASS open, pause, seek, resume, search during playback, navigation cleanup, replay, end of audio, missing audio recovery. UI heartbeat: {ticks} ticks.");
        }
        finally { heartbeat.Stop(); StopPlayback(); mutePlaybackCheck = false; }
    }
    void Advanced()
    {
        var p = app.Storage.Preferences;
        Card("Storage", Row("Save history", "Applies to future recordings.", Picker(new[] { "Audio and text", "Text only", "Off" }, p.Retention, v => { p.Retention = v; Save(); })), Row("History folder", "Open saved recordings in File Explorer.", ActionButton("Open folder", () => OpenFolder(app.Storage.HistoryRoot))), Row("Models folder", "Open downloaded models in File Explorer.", ActionButton("Open folder", () => OpenFolder(app.Storage.ModelsRoot))));
        Card("Manage data", Row("Remove saved audio", "Keep transcripts and delete their recordings.", ActionButton("Remove…", () => { if (Confirm("Delete saved audio and keep transcripts?")) { StopPlayback(); app.Storage.ClearAudio(); app.SetStatus("Saved audio removed."); } })), Row("Clear history", "Delete all transcripts and saved recordings.", ActionButton("Clear…", () => { if (Confirm("Permanently delete all history and saved audio?")) { StopPlayback(); app.Storage.ClearHistory(); app.SetStatus("History cleared."); } })));
        Card("About", Row("Petal for Windows", $"Version {typeof(SettingsWindow).Assembly.GetName().Version!.ToString(3)}", ActionButton("Project website", () => Process.Start(new ProcessStartInfo("https://github.com/Aayush9029/petal") { UseShellExecute = true }))));
    }
    bool Confirm(string text) => System.Windows.MessageBox.Show(this, text, "Petal", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    static void OpenFolder(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
    internal static void Capture(FrameworkElement element, string path, int width, int height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        // DWM acrylic is composed outside WPF; render its theme fallback behind alpha controls for test images.
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle((Brush?)element.TryFindResource("SolidBackgroundFillColorBaseBrush") ?? SystemColors.WindowBrush, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); encoder.Save(output);
    }
    public async Task CapturePages(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "theme.txt"), new[] { "High contrast: " + SystemParameters.HighContrast, "System window: " + SystemColors.WindowBrush, "System text: " + SystemColors.WindowTextBrush }.Concat(new[] { "CardBackgroundFillColorDefaultBrush", "CardStrokeColorDefaultBrush", "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush", "SolidBackgroundFillColorBaseBrush", "ControlFillColorDefaultBrush" }.Select(key => key + ": " + TryFindResource(key))));
        foreach (var name in pages) { SelectPage(name); await Task.Delay(180); UpdateLayout(); Capture((FrameworkElement)Content, Path.Combine(directory, name.ToLowerInvariant() + ".png"), (int)((FrameworkElement)Content).ActualWidth, (int)((FrameworkElement)Content).ActualHeight); }
        SelectPage("History");
        var selected = (historyList?.SelectedItem as ListBoxItem)?.Tag as Transcript;
        if (selected != null) { var historyMenu = TranscriptMenu(selected); historyMenu.PlacementTarget = this; historyMenu.IsOpen = true; await Task.Delay(100); Capture(historyMenu, Path.Combine(directory, "history-menu.png"), (int)historyMenu.ActualWidth, (int)historyMenu.ActualHeight); historyMenu.IsOpen = false; }
        var menu = new TrayMenu(app).Build(); menu.IsOpen = true; await Task.Delay(100); Capture(menu, Path.Combine(directory, "tray-menu.png"), (int)menu.ActualWidth, (int)menu.ActualHeight); menu.IsOpen = false;
        app.Capsule.CaptureStates(directory);
        var palette = new TrayPalette(app); palette.Capture(directory); palette.Close();
    }
}
