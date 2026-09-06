using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Petal.Windows;

internal sealed class CapsuleWindow : Window
{
    readonly AppController controller;
    readonly Button surface = new() { Padding = new Thickness(16, 8, 16, 8), Focusable = false, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly StackPanel waveform = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 22, Margin = new Thickness(0, 0, 12, 0) };
    readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    readonly DispatcherTimer dismiss = new() { Interval = TimeSpan.FromSeconds(2) };
    string phase = "idle";
    float level;
    int frame;
    bool hovering;
    public CapsuleWindow(AppController controller)
    {
        this.controller = controller;
        Width = 244; Height = 48; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        for (int i = 0; i < 18; i++) waveform.Children.Add(new Rectangle { Width = 2, Height = 3, RadiusX = 1, RadiusY = 1, Fill = SystemColors.HighlightBrush, Margin = new Thickness(1), VerticalAlignment = VerticalAlignment.Center });
        surface.Background = Brushes.Transparent; surface.BorderThickness = new Thickness(0); Content = surface; SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush"); System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(-1) });
        SourceInitialized += (_, _) => { Native.NoActivate(this); Native.Acrylic(this); };
        surface.MouseEnter += (_, _) => { hovering = true; RefreshContent(); };
        surface.MouseLeave += (_, _) => { hovering = false; RefreshContent(); };
        surface.Click += async (_, _) => { if (phase == "recording") await controller.StopRecording(); };
        var menu = new ContextMenu(); var cancel = new MenuItem { Header = "Cancel" }; cancel.Click += (_, _) => controller.Cancel(); menu.Items.Add(cancel); surface.ContextMenu = menu;
        timer.Tick += (_, _) =>
        {
            frame++;
            for (int i = 0; i < waveform.Children.Count; i++)
            {
                var bar = (Rectangle)waveform.Children[i];
                bar.Height = phase == "recording" ? 3 + Math.Min(18, level * 70) * (.3 + .7 * Math.Abs(Math.Sin(i * .7 + frame * .2))) : 3 + 12 * Math.Abs(Math.Sin(i * .4 - frame * .25));
                bar.Fill = phase == "recording" ? SystemColors.HighlightBrush : WindowsTheme.Secondary;
            }
        };
        dismiss.Tick += (_, _) => { dismiss.Stop(); Hide(); };
    }
    void RefreshContent()
    {
        // A single native Fluent button changes content on hover; no extra floating controls.
        if (surface.Content is Panel old) old.Children.Clear();
        if (phase == "recording" && hovering) surface.Content = new StackPanel { Children = { new TextBlock { Text = "Finish", FontSize = 13 } } };
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            if (phase is "recording" or "processing") row.Children.Add(waveform);
            row.Children.Add(status); surface.Content = row;
        }
        System.Windows.Automation.AutomationProperties.SetName(surface, phase == "recording" ? "Finish recording" : status.Text);
    }
    public void SetLevel(float value) => level = value;
    public void SetPhase(string phase, string text)
    {
        this.phase = phase; dismiss.Stop();
        if (phase == "idle") { timer.Stop(); Hide(); return; }
        Width = 244; status.Text = phase == "recording" ? "Listening" : "Transcribing";
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2; Top = area.Bottom - Height - 16;
        RefreshContent(); Show(); timer.Start();
    }
    public void ShowResult(string text)
    {
        phase = "result"; status.Text = text; Width = Math.Clamp(text.Length * 7 + 40, 244, 420);
        var area = SystemParameters.WorkArea; Left = area.Left + (area.Width - Width) / 2; Top = area.Bottom - Height - 16;
        RefreshContent(); Show(); dismiss.Start();
    }
    internal void CaptureStates(string directory)
    {
        Width = 244; hovering = false; SetPhase("recording", "Listening"); UpdateLayout();
        SettingsWindow.Capture((FrameworkElement)Content, System.IO.Path.Combine(directory, "recording-bar.png"), 244, 48);
        surface.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = MouseEnterEvent });
        if (surface.Content is not StackPanel hover || !hover.Children.OfType<TextBlock>().Any(x => x.Text == "Finish")) throw new InvalidOperationException("Recording bar did not show its stop action on hover.");
        SettingsWindow.Capture((FrameworkElement)Content, System.IO.Path.Combine(directory, "recording-bar-hover.png"), 244, 48);
        surface.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = MouseLeaveEvent });
        if (surface.Content is not StackPanel rest || !rest.Children.Contains(waveform)) throw new InvalidOperationException("Recording bar did not restore its waveform after hover.");
        SetPhase("processing", "Transcribing"); SettingsWindow.Capture((FrameworkElement)Content, System.IO.Path.Combine(directory, "processing-bar.png"), 244, 48);
        SetPhase("idle", "");
    }
}
