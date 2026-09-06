using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;

namespace Petal.Core;

public static class AudioFiles
{
    public const int SampleRate = 16000;
    public const int MaxSeconds = 7200;
    public static readonly string[] Extensions = [".wav", ".mp3", ".m4a", ".aac", ".mp4", ".wma", ".flac"];
    public static float[] Read(string path)
    {
        if (!Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) throw new InvalidDataException("Choose a WAV, MP3, M4A, AAC, MP4, WMA, or FLAC file.");
        using var reader = new AudioFileReader(path);
        if (reader.TotalTime.TotalSeconds > MaxSeconds) throw new InvalidDataException("Audio files must be two hours or shorter.");
        ISampleProvider sample = reader;
        if (sample.WaveFormat.Channels == 2) sample = new StereoToMonoSampleProvider(sample);
        if (sample.WaveFormat.Channels != 1) throw new InvalidDataException("Only mono and stereo audio are supported.");
        if (sample.WaveFormat.SampleRate != SampleRate) sample = new WdlResamplingSampleProvider(sample, SampleRate);
        var result = new List<float>();
        float[] buffer = new float[SampleRate];
        int count;
        while ((count = sample.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (result.Count + count > SampleRate * MaxSeconds) throw new InvalidDataException("Audio is too long.");
            result.AddRange(buffer.AsSpan(0, count).ToArray());
        }
        if (result.Count == 0) throw new InvalidDataException("The audio file is empty.");
        return result.ToArray();
    }
    public static float[] Trim(float[] samples)
    {
        // Trim edges only; preserve interior pauses and timing.
        const float threshold = 0.006f;
        int first = Array.FindIndex(samples, x => Math.Abs(x) > threshold);
        if (first < 0) return [];
        int last = Array.FindLastIndex(samples, x => Math.Abs(x) > threshold);
        first = Math.Max(0, first - SampleRate / 5);
        last = Math.Min(samples.Length - 1, last + SampleRate / 5);
        return samples[first..(last + 1)];
    }
    public static void Write(string path, float[] samples)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(SampleRate, 16, 1));
        writer.WriteSamples(samples, 0, samples.Length);
    }
}

public sealed class SpeechEngine : IDisposable
{
    readonly OfflineRecognizer recognizer;
    readonly bool whisper;
    public SpeechEngine(SpeechModel model, string directory)
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = AudioFiles.SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        string FilePath(string name) => Path.Combine(directory, name);
        whisper = model.Engine == "whisper";
        if (whisper)
        {
            config.ModelConfig.Whisper.Encoder = FilePath(model.Prefix + "-encoder.int8.onnx");
            config.ModelConfig.Whisper.Decoder = FilePath(model.Prefix + "-decoder.int8.onnx");
            config.ModelConfig.Whisper.Task = "transcribe";
            config.ModelConfig.Tokens = FilePath(model.Prefix + "-tokens.txt");
        }
        else
        {
            config.ModelConfig.Transducer.Encoder = FilePath("encoder.int8.onnx");
            config.ModelConfig.Transducer.Decoder = FilePath("decoder.int8.onnx");
            config.ModelConfig.Transducer.Joiner = FilePath("joiner.int8.onnx");
            config.ModelConfig.ModelType = "nemo_transducer";
            config.ModelConfig.Tokens = FilePath("tokens.txt");
        }
        recognizer = new OfflineRecognizer(config);
    }
    public string Transcribe(float[] samples, CancellationToken ct = default, IProgress<double>? progress = null)
    {
        if (samples.Length == 0 || samples.All(s => Math.Abs(s) < 0.0001f)) return "";
        var results = new List<string>();
        // Whisper has a 30-second context. Prefer a quiet boundary near each limit.
        int max = AudioFiles.SampleRate * (whisper ? 28 : 60);
        for (int start = 0; start < samples.Length;)
        {
            ct.ThrowIfCancellationRequested();
            int end = Math.Min(samples.Length, start + max);
            if (end - start > AudioFiles.SampleRate * 2)
            {
                // Decode complete utterances separately. Recognizers can collapse repeated
                // phrases when many short utterances share one context window.
                int quiet = 0;
                for (int p = start + AudioFiles.SampleRate * 2; p + 160 <= end; p += 160)
                {
                    double energy = 0;
                    for (int i = p; i < p + 160; i++) energy += samples[i] * samples[i];
                    quiet = energy / 160 < 0.00002 ? quiet + 160 : 0;
                    if (quiet >= 5600) { end = p + 160; break; }
                }
            }
            if (end == start + max && end < samples.Length)
            {
                double best = double.MaxValue;
                int boundary = end;
                for (int p = end - AudioFiles.SampleRate * 3; p < end; p += 160)
                {
                    double energy = 0;
                    for (int i = p; i < Math.Min(p + 160, end); i++) energy += samples[i] * samples[i];
                    if (energy < best) { best = energy; boundary = p + 80; }
                }
                end = boundary;
            }
            using var stream = recognizer.CreateStream();
            stream.AcceptWaveform(AudioFiles.SampleRate, samples[start..end]);
            recognizer.Decode(stream);
            var text = stream.Result.Text.Trim();
            if (text.Length > 0) results.Add(text);
            start = end;
            progress?.Report((double)start / samples.Length);
        }
        return string.Join(" ", results);
    }
    public void Dispose() => recognizer.Dispose();
}
