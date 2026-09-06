using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Petal.Windows;

internal static class PerformanceChecks
{
    public static async Task Run(AppController app, string output)
    {
        Directory.CreateDirectory(output);
        for (int i = 0; i < 1000; i++)
            app.Storage.History.Add(new Petal.Core.Transcript(i.ToString(), DateTimeOffset.Now.AddMinutes(-i), "Performance fixture transcript " + i, "whisper-tiny", 3, "Microphone", null));
        var windows = new List<WeakReference>();
        for (int i = 0; i < 5; i++)
        {
            app.ShowSettings("History");
            await Task.Delay(100);
            app.Settings!.CheckHistoryVirtualization();
            windows.Add(new WeakReference(app.Settings));
            app.Settings!.Close();
        }
        await Task.Delay(1000);
        // Measurement only: collect unreachable objects to compare retained UI memory.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        if (windows.Any(w => w.IsAlive)) throw new InvalidOperationException("Closed settings windows are still retained.");
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpu = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        await Task.Delay(4000);
        process.Refresh();
        var report = new {
            PrivateMiB = process.PrivateMemorySize64 / 1048576.0,
            WorkingSetMiB = process.WorkingSet64 / 1048576.0,
            ManagedMiB = GC.GetTotalMemory(false) / 1048576.0,
            IdleCpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
            IdleWallMs = clock.Elapsed.TotalMilliseconds,
            RetainedWindowCount = windows.Count(w => w.IsAlive)
        };
        await File.WriteAllTextAsync(Path.Combine(output, "performance.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
