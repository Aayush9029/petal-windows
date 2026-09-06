using System.IO;
using System.Windows;
using Petal.Core;

namespace Petal.Windows;

public partial class App : System.Windows.Application
{
    Mutex? singleInstance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.FirstOrDefault() == "--worker")
        {
            try
            {
                var model = ModelCatalog.Get(e.Args[1]);
                var samples = AudioFiles.Read(e.Args[3]);
                if (e.Args.Length >= 7)
                {
                    double duration = (double)samples.Length / AudioFiles.SampleRate;
                    if (bool.Parse(e.Args[6])) samples = AudioFiles.Trim(samples);
                    await File.WriteAllTextAsync(e.Args[4] + ".duration", duration.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (samples.Length < 1600) { await File.WriteAllTextAsync(e.Args[4], ""); Shutdown(); return; }
                    AudioFiles.Write(e.Args[5], samples);
                }
                using var engine = new SpeechEngine(model, e.Args[2]);
                var text = engine.Transcribe(samples);
                await File.WriteAllTextAsync(e.Args[4], text);
                Shutdown(0);
            }
            catch (Exception ex) { await File.WriteAllTextAsync(e.Args[4] + ".error", ex.Message); Shutdown(1); }
            return;
        }
        bool smoke = e.Args.FirstOrDefault() is "--ui-smoke" or "--integration-smoke" or "--shortcut-smoke" or "--playback-smoke" or "--performance-smoke" or "--preview";
        singleInstance = new Mutex(true, "Local\\Petal.Windows.App", out var first);
        if (!first && !smoke) { System.Windows.MessageBox.Show("Petal is already running. Open it from the system tray.", "Petal"); Shutdown(); return; }
        try
        {
#pragma warning disable WPF0001 // Deterministic checks of Microsoft's Fluent themes.
            if (smoke && e.Args.Length > 3) ThemeMode = e.Args[3] == "Light" ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
            var storage = new AppStorage(smoke ? e.Args[1] : null);
            if (!smoke)
            {
                foreach (var temporary in Directory.GetFiles(storage.TempRoot))
                {
                    try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            var controller = new AppController(storage, !smoke);
            var window = new SettingsWindow(controller);
            if (e.Args.FirstOrDefault() == "--preview") window.Title = "Petal. Preview";
            MainWindow = window;
            controller.Settings = window;
            window.Show();
            if (smoke && e.Args[0] != "--preview")
            {
                try
                {
                    if (e.Args[0] == "--performance-smoke") { window = null; await PerformanceChecks.Run(controller, e.Args[2]); }
                    else if (e.Args[0] == "--playback-smoke") { Directory.CreateDirectory(e.Args[2]); await window.CheckPlayback(e.Args[2]); }
                    else if (e.Args[0] == "--shortcut-smoke") IntegrationChecks.Shortcuts(controller, e.Args[2]);
                    else if (e.Args[0] == "--integration-smoke") await IntegrationChecks.Run(controller, e.Args[2]);
                    else await window.CapturePages(e.Args[2]);
                }
                catch (Exception ex) { Directory.CreateDirectory(e.Args[2]); await File.WriteAllTextAsync(Path.Combine(e.Args[2], "failure.txt"), ex.ToString()); controller.Dispose(); Shutdown(1); return; }
                controller.Dispose();
                Shutdown();
            }
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.ToString(), "Petal could not start"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { singleInstance?.Dispose(); base.OnExit(e); }
}
