using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace Petal.Windows;

internal sealed class TrayMenu(AppController app)
{
    readonly ContextMenu menu = new() { MinWidth = 280 };
    public ContextMenu Build()
    {
        menu.Items.Clear();
        MenuItem Add(string title, Action action, string? shortcut = null)
        {
            var item = new MenuItem { Header = title, InputGestureText = shortcut ?? "" };
            item.Click += (_, _) => action(); menu.Items.Add(item); return item;
        }
        Add(app.Phase == "recording" ? "Stop recording" : "Start recording", () => _ = app.ToggleRecording(), app.Storage.Preferences.Shortcut).IsEnabled = !app.Busy || app.Phase == "recording";
        Add("Transcribe file…", app.ImportFile).IsEnabled = !app.Busy;
        menu.Items.Add(new Separator());
        var recent = new MenuItem { Header = "Recent transcripts", IsEnabled = app.Storage.History.Count > 0 };
        foreach (var transcript in app.Storage.History.Take(5))
        {
            var item = new MenuItem { Header = transcript.Text.Length > 48 ? transcript.Text[..48] + "…" : transcript.Text, ToolTip = "Copy transcript", MaxWidth = 420 };
            item.Click += (_, _) => app.Copy(transcript.Text); recent.Items.Add(item);
        }
        menu.Items.Add(recent);
        Add("History", () => app.ShowSettings("History"));
        Add("Settings", () => app.ShowSettings());
        menu.Items.Add(new Separator()); Add("Exit Petal", app.Quit);
        return menu;
    }
    public void Open() { Build(); menu.Placement = PlacementMode.MousePoint; menu.IsOpen = true; }
}
