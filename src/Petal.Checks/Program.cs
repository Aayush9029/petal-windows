using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Petal.Core;

var arguments = args.ToList();
var root = Path.GetFullPath(arguments.Count > 1 ? arguments[1] : "test-data");
Directory.CreateDirectory(root);
if (arguments.FirstOrDefault() == "stress")
{
    var sample = AudioFiles.Read(Path.Combine(root, "speech.wav"));
    var repeated = Enumerable.Range(0, 16).SelectMany(_ => sample.Concat(new float[8000])).ToArray();
    foreach (var id in new[] { "whisper-tiny", "parakeet-v3" })
    {
        var model = ModelCatalog.Get(id);
        using var engine = new SpeechEngine(model, Path.Combine(root, "Models", id));
        var text = engine.Transcribe(repeated);
        File.WriteAllText(Path.Combine(root, id + "-stress.txt"), text);
        int countries = System.Text.RegularExpressions.Regex.Matches(text, "country", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
        Require(countries == 32, $"{id} preserves speech across chunk boundaries ({countries}/32 country occurrences)");
        Console.WriteLine($"PASS {id}: {(double)repeated.Length / 16000:F1}s audio, {countries}/32 expected occurrences");
    }
    foreach (var extension in new[] { "mp3", "m4a" })
    {
        string encoded = Path.Combine(root, "compressed." + extension);
        using var reader = new NAudio.Wave.AudioFileReader(Path.Combine(root, "speech.wav"));
        if (extension == "mp3") NAudio.Wave.MediaFoundationEncoder.EncodeToMp3(reader, encoded);
        else NAudio.Wave.MediaFoundationEncoder.EncodeToAac(reader, encoded);
        var decoded = AudioFiles.Read(encoded);
        Require(Math.Abs(decoded.Length - sample.Length) < 8000, extension + " import duration");
        Require(decoded.Any(s => Math.Abs(s) > .1f), extension + " decoded speech");
        Console.WriteLine("PASS " + extension + " import via Windows Media Foundation");
    }
    return;
}
if (arguments.FirstOrDefault() == "models")
{
    var store = new ModelStore(Path.Combine(root, "Models"));
    var ids = arguments.Count > 2 ? arguments.Skip(2).ToArray() : ["whisper-tiny", "parakeet-v3"];
    using var http = new HttpClient();
    string sample = Path.Combine(root, "speech.wav");
    if (!File.Exists(sample))
    {
        var data = await http.GetByteArrayAsync("https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/2bda32ec70b097a55adaa07d9a7173915b43cc78/test_wavs/en.wav");
        Require(Convert.ToHexString(SHA256.HashData(data)).Equals("148b936b43ce7c546a866e64da059f0458aee2d65e617f16e9d94f06e8d99ed6", StringComparison.OrdinalIgnoreCase), "Speech fixture hash");
        await File.WriteAllBytesAsync(sample, data);
    }
    foreach (var id in ids)
    {
        var model = ModelCatalog.Get(id);
        Console.WriteLine($"Downloading {model.Name} ({model.SizeLabel})");
        int last = -1;
        await store.DownloadAsync(model, new Progress<double>(p => { int step = (int)(p * 10); if (step > last) { last = step; Console.WriteLine($"{id}: {step * 10}%"); } }));
        Require(await store.VerifyAsync(model), id + " integrity");
        var sw = Stopwatch.StartNew();
        using var engine = new SpeechEngine(model, store.DirectoryFor(model));
        var loaded = sw.Elapsed.TotalSeconds;
        var samples = AudioFiles.Read(sample);
        string text = engine.Transcribe(samples);
        Console.WriteLine($"{id}: {text}\nLoad {loaded:F2}s, inference {sw.Elapsed.TotalSeconds - loaded:F2}s, audio {(double)samples.Length / 16000:F2}s");
        Require(text.Length > 20 && text.Contains("country", StringComparison.OrdinalIgnoreCase) && text.Contains("ask", StringComparison.OrdinalIgnoreCase), id + " intelligible speech");
        Require(engine.Transcribe(new float[16000]) == "", id + " silence");
        File.WriteAllText(Path.Combine(root, id + "-result.txt"), text + $"\nLoad: {loaded:F2}s; total: {sw.Elapsed.TotalSeconds:F2}s");
    }
    return;
}

int passed = 0;
async Task Check(string name, Func<Task> test)
{
    await test(); passed++; Console.WriteLine("PASS " + name);
}
await Check("history search handles words, accents and metadata", () =>
{
    var item = new Transcript("test", DateTimeOffset.Now, "Café meeting notes for the launch", "whisper-tiny", 1, "Imported audio", null);
    Require(TranscriptSearch.Matches(item, "  LAUNCH cafe  "), "Whitespace, case, accents and word order");
    Require(TranscriptSearch.Matches(item, "whisper imported"), "Model and source metadata");
    Require(!TranscriptSearch.Matches(item, "launch missing"), "Every word must match");
    Require(TranscriptSearch.Matches(item, "   "), "Empty search matches all");
    return Task.CompletedTask;
});
await Check("custom shortcuts round trip", () =>
{
    foreach (uint key in new uint[] { 0x74, 0x24, 0xAD, 0xB3, 0x41, 0xBA })
        foreach (uint modifiers in new uint[] { 0, 1, 2, 4, 6, 8 })
        {
            var original = ShortcutBinding.FromKey(key, modifiers);
            Require(ShortcutBinding.Get(original.Label) == original, "Shortcut label persists without losing keys");
        }
    Require(ShortcutBinding.Get("F5").Key == 0x74 && ShortcutBinding.Get("Home").Key == 0x24, "F5 and Home virtual keys");
    foreach (var label in new[] { "F12", "F25", "Escape", "Ctrl", "Key 0xFF" })
    {
        bool rejected = false; try { ShortcutBinding.Get(label); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Reserved or invalid key rejected: " + label);
    }
    return Task.CompletedTask;
});
await Check("catalog pins complete model files", () =>
{
    Require(ModelCatalog.All.Select(m => m.Id).Distinct().Count() == ModelCatalog.All.Count, "Unique IDs");
    foreach (var model in ModelCatalog.All)
    {
        Require(model.Revision.Length == 40 && model.Files.Length >= 3, "Pinned revision and all model files");
        Require(model.Files.All(f => f.Size > 0 && f.Sha256.Length == 64 && Path.GetFileName(f.Name) == f.Name), "Safe file paths and hashes");
    }
    return Task.CompletedTask;
});
await Check("audio round trip, stereo conversion, trim and silence", () =>
{
    var samples = new float[16000 * 2];
    for (int i = 4000; i < 24000; i++) samples[i] = (float)Math.Sin(i * .1) * .2f;
    var path = Path.Combine(root, "roundtrip.wav"); AudioFiles.Write(path, samples);
    var read = AudioFiles.Read(path);
    Require(read.Length == samples.Length && Math.Abs(read[4100] - samples[4100]) < .0001, "PCM round trip");
    Require(AudioFiles.Trim(read).Length < read.Length, "Edge silence trimmed");
    Require(AudioFiles.Trim(new float[100]).Length == 0, "Silence is empty");
    var stereo = Path.Combine(root, "stereo.wav");
    using (var writer = new NAudio.Wave.WaveFileWriter(stereo, new NAudio.Wave.WaveFormat(48000, 16, 2))) writer.WriteSamples(new float[96000], 0, 96000);
    Require(Math.Abs(AudioFiles.Read(stereo).Length - 16000) < 5, "48k stereo resampled to mono 16k");
    return Task.CompletedTask;
});
await Check("history persistence, privacy settings, deletion and corrupt recovery", () =>
{
    var storage = new AppStorage(Path.Combine(root, Guid.NewGuid().ToString()));
    var audio = Path.Combine(root, "roundtrip.wav");
    storage.Add("Hello world", "whisper-tiny", 2, "fixture", audio);
    var restored = new AppStorage(storage.Root);
    Require(restored.History.Count == 1 && File.Exists(restored.AudioPath(restored.History[0])), "Audio and history survive restart");
    restored.ClearAudio(); Require(restored.History.Count == 1 && restored.History[0].AudioFile == null, "Clear audio keeps text");
    restored.Preferences.Retention = "Off"; restored.Add("Private", "whisper-tiny", 2, "fixture", audio); Require(restored.History.Count == 1, "History off persists nothing");
    restored.ClearHistory(); Require(new AppStorage(storage.Root).History.Count == 0, "Deletion persists");
    File.WriteAllText(Path.Combine(storage.Root, "settings.json"), "broken json");
    Require(new AppStorage(storage.Root).Warning != null, "Corrupt settings backed up with warning");
    return Task.CompletedTask;
});
await Check("verified download and no partial installation", async () =>
{
    var data = Encoding.UTF8.GetBytes("fake ONNX content to test transport");
    var model = Fixture(data);
    var handler = new FakeHandler(data);
    var store = new ModelStore(Path.Combine(root, Guid.NewGuid().ToString()), new HttpClient(handler));
    await store.DownloadAsync(model); Require(store.IsInstalled(model) && await store.VerifyAsync(model), "Valid download installs");
    await store.DownloadAsync(model); Require(handler.Calls == 1, "Verified files reused");
    await File.WriteAllTextAsync(Path.Combine(store.DirectoryFor(model), "model.onnx"), "bad"); Require(!store.IsInstalled(model), "Truncated file not ready");
});
await Check("HTTP range resume and server ignoring range", async () =>
{
    byte[] data = Encoding.UTF8.GetBytes("012345678901234567890123456789"); var model = Fixture(data);
    foreach (bool ignores in new[] { false, true })
    {
        var handler = new FakeHandler(data) { IgnoreRange = ignores };
        var store = new ModelStore(Path.Combine(root, Guid.NewGuid().ToString()), new HttpClient(handler));
        Directory.CreateDirectory(store.DirectoryFor(model));
        await File.WriteAllBytesAsync(Path.Combine(store.DirectoryFor(model), "model.onnx.partial"), data[..10]);
        await store.DownloadAsync(model); Require(handler.Range == 10 && await store.VerifyAsync(model), "Resume with correct content");
    }
});
await Check("checksum mismatch rejected", async () =>
{
    var data = Encoding.UTF8.GetBytes("valid"); var model = Fixture(data);
    var store = new ModelStore(Path.Combine(root, Guid.NewGuid().ToString()), new HttpClient(new FakeHandler(Encoding.UTF8.GetBytes("wrong"))));
    try { await store.DownloadAsync(model); throw new Exception("Corrupt download accepted"); } catch (IOException) { }
    Require(!store.IsInstalled(model), "Corrupt model is not installed");
});
await Check("canceled download cannot mark installed", async () =>
{
    var data = Encoding.UTF8.GetBytes("data"); var model = Fixture(data);
    var store = new ModelStore(Path.Combine(root, Guid.NewGuid().ToString()), new HttpClient(new FakeHandler(data)));
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    try { await store.DownloadAsync(model, ct: cancel.Token); throw new Exception("Cancel ignored"); } catch (OperationCanceledException) { }
    Require(!store.IsInstalled(model), "Canceled model not ready");
});
Console.WriteLine($"{passed} checks passed.");

static void Require(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); }
static SpeechModel Fixture(byte[] data) => new("fixture", "Fixture", "", "test/test", new string('a', 40), "test", "", false, [new("model.onnx", data.Length, Convert.ToHexString(SHA256.HashData(data)))]);
sealed class FakeHandler(byte[] data) : HttpMessageHandler
{
    public bool IgnoreRange { get; init; }
    public long? Range { get; private set; }
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++; Range = request.Headers.Range?.Ranges.First().From;
        long offset = !IgnoreRange ? Range ?? 0 : 0;
        var response = new HttpResponseMessage(offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(data[(int)offset..]) };
        if (offset > 0) response.Content.Headers.ContentRange = new(offset, data.Length - 1, data.Length);
        return Task.FromResult(response);
    }
}
