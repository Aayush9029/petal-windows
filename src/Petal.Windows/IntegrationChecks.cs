using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Petal.Core;

namespace Petal.Windows;

internal static class IntegrationChecks
{
    public static void Shortcuts(AppController app, string output)
    {
        Directory.CreateDirectory(output);
        var dialog = new ShortcutDialog(app); dialog.Show();
        foreach (var key in new[] { System.Windows.Input.Key.F5, System.Windows.Input.Key.Home, System.Windows.Input.Key.VolumeMute })
        {
            dialog.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), 0, key) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            if (ShortcutBinding.Get(dialog.CapturedBinding!).Key != (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key)) throw new Exception("Shortcut capture lost the pressed key.");
        }
        SettingsWindow.Capture((FrameworkElement)dialog.Content, Path.Combine(output, "shortcut-capture.png"), 460, 290);
        dialog.Close();
        if (!app.TrySetShortcut("F5")) throw new Exception(app.Status);
        using (var blocker = new Shortcut(() => { }, "Home"))
        {
            if (app.TrySetShortcut("Home")) throw new Exception("Conflicting shortcut was accepted.");
            if (app.Storage.Preferences.Shortcut != "F5") throw new Exception("Conflict changed the saved shortcut.");
            bool retained = false;
            try { using var duplicate = new Shortcut(() => { }, "F5"); }
            catch (InvalidOperationException) { retained = true; }
            if (!retained) throw new Exception("Conflict lost the previous registration.");
        }
        if (!app.TrySetShortcut("Home")) throw new Exception("Released shortcut cannot be selected.");
        using (var released = new Shortcut(() => { }, "F5")) { }
        if (!app.TrySetShortcut("F5")) throw new Exception(app.Status);
        File.WriteAllText(Path.Combine(output, "shortcuts.txt"), "PASS F5 registers; conflicts preserve previous binding; switching releases previous binding; Home registers; F5 restored.");
    }
    public static async Task Run(AppController app, string output)
    {
        Directory.CreateDirectory(output);
        var report = new List<string>();
        void Pass(string value) { report.Add("PASS " + value); File.WriteAllLines(Path.Combine(output, "integration.txt"), report); }
        void Require(bool value, string message) { if (!value) throw new Exception(message); Pass(message); }
        // Exercise the real child-process path and all downstream storage/clipboard behavior.
        app.Storage.Preferences.ModelId = "whisper-tiny";
        app.Storage.Preferences.Retention = "Audio and text";
        app.Storage.SavePreferences();
        var previous = System.Windows.Clipboard.GetDataObject();
        uint ownClipboard = 0;
        try
        {
            await app.Transcribe(Path.Combine(app.Storage.Root, "speech.wav"), "Integration fixture");
            Require(app.Phase == "idle" && app.Storage.History.Count > 0, "worker completes and history persists");
            Require(app.Storage.History[0].Text.Contains("country", StringComparison.OrdinalIgnoreCase), "worker returns expected transcript");
            Require(System.Windows.Clipboard.GetText().Contains("country", StringComparison.OrdinalIgnoreCase), "file transcription copies to clipboard");
            ownClipboard = Native.GetClipboardSequenceNumber();
            var restored = new AppStorage(app.Storage.Root);
            Require(restored.History.Count > 0 && File.Exists(restored.AudioPath(restored.History[0])), "saved recording survives restart");
            var targetText = new TextBox { AcceptsReturn = true };
            var target = new Window { Title = "Petal automated paste test", Width = 300, Height = 150, Content = targetText, Topmost = true };
            target.Show(); target.Activate(); targetText.Focus();
            await Task.Delay(200);
            var targetHandle = new WindowInteropHelper(target).Handle;
            Native.SetForegroundWindow(targetHandle);
            await Task.Delay(150);
            if (Native.GetForegroundWindow() == targetHandle)
            {
                Require(Native.Paste(), "Windows SendInput succeeds");
                await Task.Delay(250);
                Require(targetText.Text.Contains("country", StringComparison.OrdinalIgnoreCase), "Ctrl+V inserts transcript into focused text field");
            }
            else { report.Add("SKIP SendInput paste: Windows denied foreground activation of the test window; no keys injected."); }
            target.Close();
            using (var shortcut = new Shortcut(() => { }, "Alt + Space")) Pass("Alt + Space global shortcut registers");
            var capture = Path.Combine(app.Storage.TempRoot, "microphone-check.wav");
            try
            {
                using var recorder = new Recorder(capture, -1, false);
                await Task.Delay(400); await recorder.StopAsync();
                Require(new FileInfo(capture).Length > 44, "default microphone captures PCM audio");
            }
            finally { File.Delete(capture); }
            app.Storage.Preferences.ModelId = "parakeet-v3";
            await app.Transcribe(Path.Combine(app.Storage.Root, "speech.wav"), "Integration Parakeet");
            ownClipboard = Native.GetClipboardSequenceNumber();
            Require(app.Storage.History[0].ModelId == "parakeet-v3", "Parakeet worker completes through app pipeline");
            var pending = app.Transcribe(Path.Combine(app.Storage.Root, "speech.wav"), "Cancellation fixture");
            for (int i = 0; i < 100 && app.ActiveWorkerId == null && !pending.IsCompleted; i++) await Task.Delay(50);
            Require(app.ActiveWorkerId != null, "cancellation test reaches running native worker");
            int workerId = app.ActiveWorkerId!.Value;
            app.Cancel(); await pending;
            Require(app.Phase == "idle" && app.Status.Contains("canceled"), "app transcription cancellation returns to idle");
            bool gone;
            try { using var worker = System.Diagnostics.Process.GetProcessById(workerId); gone = worker.HasExited; } catch (ArgumentException) { gone = true; }
            Require(gone, "canceled native worker exits");
            Require(Directory.GetFiles(app.Storage.TempRoot).Length == 0, "temporary audio and worker outputs cleaned");
            await app.Settings!.CapturePages(output);
            Pass("all settings pages render with real history");
        }
        finally
        {
            if (ownClipboard != 0 && ownClipboard == Native.GetClipboardSequenceNumber())
            {
                if (previous != null) System.Windows.Clipboard.SetDataObject(previous, true);
                else System.Windows.Clipboard.Clear();
            }
        }
    }
}
