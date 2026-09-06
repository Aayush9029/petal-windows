using System.Security.Cryptography;
using System.Text.Json;

namespace Petal.Core;

public record ModelFile(string Name, long Size, string Sha256);
public record SpeechModel(string Id, string Name, string Summary, string Repo, string Revision,
    string Engine, string Prefix, bool Recommended, ModelFile[] Files)
{
    public long Size => Files.Sum(f => f.Size);
    public string SizeLabel => Size >= 1_000_000_000 ? $"{Size / 1e9:F1} GB" : $"{Size / 1e6:F0} MB";
    public string Provider => Engine == "parakeet" ? "NVIDIA" : "OpenAI";
    public string Url(ModelFile file) => $"https://huggingface.co/{Repo}/resolve/{Revision}/{file.Name}";
}

public static class ModelCatalog
{
    public static IReadOnlyList<SpeechModel> All { get; } = Load();
    static SpeechModel[] Load()
    {
        using var stream = typeof(ModelCatalog).Assembly.GetManifestResourceStream("Petal.Core.models.json")!;
        return JsonSerializer.Deserialize<SpeechModel[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    public static SpeechModel Get(string id) => All.FirstOrDefault(m => m.Id == id)
        ?? throw new ArgumentException($"Unknown speech model: {id}");
}

public sealed class ModelStore(string root, HttpClient? client = null)
{
    static readonly HttpClient SharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    readonly HttpClient http = client ?? SharedClient;
    readonly SemaphoreSlim gate = new(1);
    public string Root { get; } = Path.GetFullPath(root);
    public string DirectoryFor(SpeechModel model) => Path.Combine(Root, model.Id);
    public bool IsInstalled(SpeechModel model) => File.Exists(Path.Combine(DirectoryFor(model), "verified.json")) &&
        model.Files.All(f => File.Exists(Path.Combine(DirectoryFor(model), f.Name)) && new FileInfo(Path.Combine(DirectoryFor(model), f.Name)).Length == f.Size);

    public async Task<bool> VerifyAsync(SpeechModel model, CancellationToken ct = default)
    {
        foreach (var f in model.Files)
            if (!await ValidAsync(Path.Combine(DirectoryFor(model), f.Name), f, ct)) return false;
        return true;
    }

    static async Task<bool> ValidAsync(string path, ModelFile file, CancellationToken ct)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != file.Size) return false;
        await using var input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public async Task DownloadAsync(SpeechModel model, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(DirectoryFor(model));
            long finished = 0;
            var progressClock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var file in model.Files)
            {
                ct.ThrowIfCancellationRequested();
                var path = Path.Combine(DirectoryFor(model), file.Name);
                if (await ValidAsync(path, file, ct)) { finished += file.Size; continue; }
                var partial = path + ".partial";
                long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                if (offset > file.Size) { File.Delete(partial); offset = 0; }
                if (offset < file.Size)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, model.Url(file));
                    if (offset > 0) request.Headers.Range = new(offset, null);
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();
                    if (response.StatusCode != System.Net.HttpStatusCode.PartialContent) offset = 0;
                    else if (response.Content.Headers.ContentRange?.From != offset) throw new IOException("Download server returned an invalid resume range.");
                    await using (var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    await using (var input = await response.Content.ReadAsStreamAsync(ct))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await input.ReadAsync(buffer, ct)) > 0)
                        {
                            offset += read;
                            if (offset > file.Size) throw new IOException("Model download exceeded the expected size.");
                            await output.WriteAsync(buffer.AsMemory(0, read), ct);
                            // Avoid flooding the UI dispatcher on fast connections.
                            if (progressClock.ElapsedMilliseconds >= 100)
                            {
                                progress?.Report((double)(finished + offset) / model.Size);
                                progressClock.Restart();
                            }
                        }
                    }
                }
                if (!await ValidAsync(partial, file, ct))
                {
                    File.Delete(partial);
                    throw new IOException($"Integrity check failed for {file.Name}. Please retry the download.");
                }
                File.Move(partial, path, true);
                finished += file.Size;
            }
            await File.WriteAllTextAsync(Path.Combine(DirectoryFor(model), "verified.json"), JsonSerializer.Serialize(model), ct);
            progress?.Report(1);
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(SpeechModel model)
    {
        await gate.WaitAsync();
        try { if (Directory.Exists(DirectoryFor(model))) Directory.Delete(DirectoryFor(model), true); }
        finally { gate.Release(); }
    }
}
