using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Petal.Core;
using Forms = System.Windows.Forms;

namespace Petal.Windows;

public sealed class AppController : IDisposable
{
    public AppStorage Storage { get; }
    public ModelStore Models { get; }
    public SettingsWindow? Settings { get; set; }
    public string Status { get; private set; } = "Ready to transcribe";
    public string Phase { get; private set; } = "idle";
    public bool Busy => Phase != "idle";
    public event Action? Changed;
    internal CapsuleWindow Capsule { get; }
    readonly Forms.NotifyIcon? tray;
    readonly bool integration;
    Shortcut? shortcut;
    string? registeredShortcut;
    TrayMenu? trayMenu;
    TrayPalette? palette;
    Recorder? recorder;
    string? recordingPath;
    IntPtr pasteTarget;
    CancellationTokenSource? operation;
    internal int? ActiveWorkerId { get; private set; }

    readonly DispatcherTimer maxRecording = new() { Interval = TimeSpan.FromMinutes(10) };
    public AppController(AppStorage storage, bool integration)
    {
        Storage = storage; Models = new(storage.ModelsRoot); this.integration = integration;
        Capsule = new(this);
        if (integration)
        {
            var iconUri = new Uri("pack://application:,,,/Assets/petal.ico");
            using var iconStream = System.Windows.Application.GetResourceStream(iconUri).Stream;
            tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(iconStream), Text = "Petal", Visible = true };
            tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowPalette(); else if (e.Button == Forms.MouseButtons.Right) ShowTray(); };
            RegisterShortcut();
        }
        maxRecording.Tick += async (_, _) => { maxRecording.Stop(); await StopRecording(); };
        if (storage.Warning != null) SetStatus(storage.Warning);
    }
    public void RegisterShortcut()
    {
        if (!integration) return;
        if (!TrySetShortcut(Storage.Preferences.Shortcut) && shortcut == null && Storage.Preferences.Shortcut != "Ctrl + Alt + Space")
        {
            string desired = Storage.Preferences.Shortcut;
            if (TrySetShortcut("Ctrl + Alt + Space")) SetStatus($"{desired} is unavailable. Using Ctrl + Alt + Space; change it in General.");
        }
    }
    public bool TrySetShortcut(string binding)
    {
        if (registeredShortcut == binding) return true;
        if (Busy) { SetStatus("Finish the current recording before changing its shortcut."); return false; }
        try
        {
            var replacement = new Shortcut(() => _ = ToggleRecording(), binding);
            shortcut?.Dispose(); shortcut = replacement; registeredShortcut = binding;
            Storage.Preferences.Shortcut = binding; Storage.SavePreferences();
            SetStatus($"Recording shortcut: {binding}"); return true;
        }
        catch (Exception ex) { SetStatus(ex.Message); return false; }
    }
    public void EditShortcut(Window owner)
    {
        if (Busy) { SetStatus("Finish recording before changing the shortcut."); return; }
        shortcut?.Dispose(); shortcut = null; registeredShortcut = null;
        try { new ShortcutDialog(this) { Owner = owner }.ShowDialog(); }
        finally { if (shortcut == null) TrySetShortcut(Storage.Preferences.Shortcut); }
    }
    public void SetStatus(string value) { Status = value; Changed?.Invoke(); }
    void SetPhase(string phase, string text)
    {
        Phase = phase; SetStatus(text);
        Capsule.SetPhase(phase, text);
        if (tray != null) tray.Text = "Petal: " + (phase == "recording" ? "Recording" : phase == "processing" ? "Transcribing" : "Ready");
    }
    public void ShowSettings(string? page = null)
    {
        Settings ??= new SettingsWindow(this);
        Settings.Show();
        if (Settings != null) { Settings.WindowState = WindowState.Normal; Settings.Activate(); if (page != null) Settings.SelectPage(page); }
    }
    void ShowTray() { trayMenu ??= new TrayMenu(this); trayMenu.Open(); }
    internal void ShowPalette() { palette ??= new TrayPalette(this); palette.Open(); }
    public async Task ToggleRecording(IntPtr target = default)
    {
        if (palette?.IsVisible == true) { target = palette.RecordingTarget; palette.Hide(); }
        if (Phase == "recording") { await StopRecording(); return; }
        if (Busy) return;
        if (!Models.IsInstalled(ModelCatalog.Get(Storage.Preferences.ModelId)))
        { SetStatus("Download a speech model to start recording."); ShowSettings("Models"); return; }
        try
        {
            pasteTarget = target != IntPtr.Zero ? target : Native.GetForegroundWindow();
            recordingPath = Path.Combine(Storage.TempRoot, Guid.NewGuid() + ".wav");
            recorder = new Recorder(recordingPath, Storage.Preferences.Microphone, Storage.Preferences.LowerAudio);
            recorder.Level += level => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Capsule.SetLevel(level));
            recorder.Failed += error => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (Phase == "recording") { Cancel(); SetStatus("Microphone stopped: " + error.Message); }
            });
            SetPhase("recording", "Listening…");
            maxRecording.Start();
        }
        catch (Exception ex)
        {
            recorder?.Dispose(); recorder = null;
            if (recordingPath != null) File.Delete(recordingPath);
            SetPhase("idle", "Microphone unavailable: " + ex.Message);
        }
    }
    public async Task StopRecording()
    {
        if (recorder == null || Phase != "recording") return;
        maxRecording.Stop();
        var current = recorder; recorder = null;
        SetPhase("processing", "Preparing audio…");
        try { await current.StopAsync(); current.Dispose(); await Transcribe(recordingPath!, "Microphone", Storage.Preferences.AutoPaste, true); }
        catch (Exception ex) { SetPhase("idle", ex.Message); }
        finally { current.Dispose(); if (recordingPath != null) File.Delete(recordingPath); recordingPath = null; }
    }
    public void ImportFile()
    {
        if (Busy) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Transcribe Audio File", Filter = "Audio files|*.wav;*.mp3;*.m4a;*.aac;*.mp4;*.wma;*.flac" };
        if (dialog.ShowDialog(Settings) == true) _ = Transcribe(dialog.FileName, Path.GetFileName(dialog.FileName), false);
    }
    public async Task Transcribe(string path, string source, bool paste = false, bool alreadyBusy = false)
    {
        if (Busy && !alreadyBusy) return;
        var model = ModelCatalog.Get(Storage.Preferences.ModelId);
        if (!Models.IsInstalled(model)) { SetPhase("idle", "Download a speech model first."); ShowSettings("Models"); return; }
        operation = new();
        var ct = operation.Token;
        string prepared = Path.Combine(Storage.TempRoot, Guid.NewGuid() + ".wav");
        string result = prepared + ".txt";
        SetPhase("processing", "Transcribing with " + model.Name + "…");
        try
        {
            if (!await Models.VerifyAsync(model, ct)) throw new IOException("The model files are damaged. Delete the download from Models and download it again.");
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            // Audio decoding and its large buffers live only in the disposable worker.
            foreach (var arg in new[] { "--worker", model.Id, Models.DirectoryFor(model), path, result, prepared, Storage.Preferences.TrimSilence.ToString() }) info.ArgumentList.Add(arg);
            using var worker = Process.Start(info) ?? throw new IOException("Could not start the speech engine.");
            ActiveWorkerId = worker.Id;
            using var registration = ct.Register(() => { try { if (!worker.HasExited) worker.Kill(true); } catch (InvalidOperationException) { } });
            await worker.WaitForExitAsync(CancellationToken.None);
            ct.ThrowIfCancellationRequested();
            if (worker.ExitCode != 0) throw new IOException(File.Exists(result + ".error") ? await File.ReadAllTextAsync(result + ".error") : "The speech engine stopped unexpectedly. Try downloading the model again.");
            string text = (await File.ReadAllTextAsync(result, ct)).Trim();
            if (text.Length == 0) { SetPhase("idle", "No speech detected."); return; }
            double duration = double.Parse(await File.ReadAllTextAsync(result + ".duration", ct), System.Globalization.CultureInfo.InvariantCulture);
            Storage.Add(text, model.Id, duration, source, prepared);
            string delivered = await Deliver(text, paste);
            SetPhase("idle", delivered);
            Capsule.ShowResult(delivered);
        }
        catch (OperationCanceledException) { SetPhase("idle", "Transcription canceled."); }
        catch (Exception ex) { SetPhase("idle", "Transcription failed: " + ex.Message); }
        finally
        {
            ActiveWorkerId = null;
            File.Delete(prepared); File.Delete(result); File.Delete(result + ".error"); File.Delete(result + ".duration");
            operation?.Dispose(); operation = null;
        }
    }
    async Task<string> Deliver(string text, bool paste)
    {
        System.Windows.IDataObject? previous = null;
        try
        {
            if (paste && Storage.Preferences.RestoreClipboard) previous = System.Windows.Clipboard.GetDataObject();
            System.Windows.Clipboard.SetText(text);
            uint sequence = Native.GetClipboardSequenceNumber();
            if (!paste || pasteTarget == IntPtr.Zero) return "Copied to clipboard";
            if (!Native.SetForegroundWindow(pasteTarget)) return "Copied. Switch to your app and paste.";
            await Task.Delay(150);
            // Never inject into an unrelated foreground window or while modifiers remain held.
            for (int i = 0; i < 40 && ModifiersHeld(); i++) await Task.Delay(50);
            if (Native.GetForegroundWindow() != pasteTarget || ModifiersHeld() || !Native.Paste()) return "Copied. Press Ctrl+V to paste.";
            await Task.Delay(600);
            if (previous != null && sequence == Native.GetClipboardSequenceNumber()) System.Windows.Clipboard.SetDataObject(previous, true);
            return "Pasted into your app";
        }
        catch (Exception)
        {
            var fallback = new Window { Title = "Petal transcript", Width = 520, Height = 280, Owner = Settings,
                Content = new System.Windows.Controls.TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto } };
            fallback.Show();
            return "Could not copy. Your transcript is open in a new window.";
        }
    }
    static bool ModifiersHeld() => new[] { 0x11, 0x12, 0x10, 0x5B, 0x5C }.Any(k => (Native.GetAsyncKeyState(k) & 0x8000) != 0);
    public async void Cancel()
    {
        if (Phase == "recording" && recorder != null)
        {
            maxRecording.Stop(); var current = recorder; recorder = null;
            try { await current.StopAsync(); } catch { }
            finally { current.Dispose(); if (recordingPath != null) File.Delete(recordingPath); recordingPath = null; SetPhase("idle", "Recording canceled."); }
        }
        else operation?.Cancel();
    }
    public void Copy(string text)
    {
        try { System.Windows.Clipboard.SetText(text); SetStatus("Copied to clipboard"); }
        catch { SetStatus("Clipboard is busy. Please try again."); }
    }
    public void Quit()
    {
        if (Busy && System.Windows.MessageBox.Show("Cancel the current recording or transcription and quit?", "Quit Petal", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Dispose(); System.Windows.Application.Current.Shutdown();
    }
    public void Dispose()
    {
        maxRecording.Stop(); operation?.Cancel(); recorder?.Dispose(); recorder = null; shortcut?.Dispose();
        tray?.Dispose(); palette?.Close(); Capsule.Close();
    }
}
