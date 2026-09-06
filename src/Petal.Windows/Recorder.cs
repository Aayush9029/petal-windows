using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.IO;

namespace Petal.Windows;

internal sealed class Recorder : IDisposable
{
    readonly WaveInEvent input;
    readonly WaveFileWriter writer;
    readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly List<(AudioSessionControl Session, float Volume)> ducked = [];
    MMDevice? outputDevice;
    bool disposed;
    public event Action<float>? Level;
    public event Action<Exception>? Failed;
    public Recorder(string path, int device, bool duck)
    {
        input = new WaveInEvent { DeviceNumber = device, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 50 };
        writer = new WaveFileWriter(path, input.WaveFormat);
        input.DataAvailable += (_, e) =>
        {
            writer.Write(e.Buffer, 0, e.BytesRecorded);
            float peak = 0;
            for (int i = 0; i < e.BytesRecorded - 1; i += 2) peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f));
            Level?.Invoke(peak);
        };
        input.RecordingStopped += (_, e) => { writer.Dispose(); if (e.Exception != null) { stopped.TrySetException(e.Exception); Failed?.Invoke(e.Exception); } else stopped.TrySetResult(); };
        try
        {
            if (duck) DuckAudio();
            input.StartRecording();
        }
        catch { Dispose(); throw; }
    }
    void DuckAudio()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            outputDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = outputDevice.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.GetProcessID == Environment.ProcessId) continue;
                float volume = session.SimpleAudioVolume.Volume;
                ducked.Add((session, volume));
                session.SimpleAudioVolume.Volume = volume * 0.25f;
            }
        }
        catch { RestoreAudio(); }
    }
    void RestoreAudio()
    {
        foreach (var (session, volume) in ducked)
        {
            try { if (Math.Abs(session.SimpleAudioVolume.Volume - volume * .25f) < .001f) session.SimpleAudioVolume.Volume = volume; } catch { }
            session.Dispose();
        }
        ducked.Clear(); outputDevice?.Dispose(); outputDevice = null;
    }
    public async Task StopAsync() { input.StopRecording(); await stopped.Task; }
    public void Dispose() { if (disposed) return; disposed = true; input.Dispose(); writer.Dispose(); RestoreAudio(); }
    public static IEnumerable<(int Id, string Name)> Devices()
    {
        yield return (-1, "Default");
        for (int i = 0; i < WaveIn.DeviceCount; i++) yield return (i, WaveIn.GetCapabilities(i).ProductName);
    }
}
