using System.Text.Json;

namespace Petal.Core;

public sealed class Preferences
{
    public string ModelId { get; set; } = "parakeet-v3";
    public int Microphone { get; set; } = -1;
    public bool TrimSilence { get; set; } = true;
    public bool RestoreClipboard { get; set; } = true;
    public bool AutoPaste { get; set; } = true;
    public bool LowerAudio { get; set; } = false;
    public string Retention { get; set; } = "Audio and text";
    public string Shortcut { get; set; } = "Alt + Space";
}

public record Transcript(string Id, DateTimeOffset Timestamp, string Text, string ModelId, double Duration, string Source, string? AudioFile);

public sealed class AppStorage
{
    public string Root { get; }
    public string HistoryRoot => Path.Combine(Root, "History");
    public string TempRoot => Path.Combine(Root, "Temp");
    public string ModelsRoot => Path.Combine(Root, "Models");
    public Preferences Preferences { get; private set; }
    public List<Transcript> History { get; private set; }
    public string? Warning { get; private set; }
    public AppStorage(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Petal");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(HistoryRoot);
        Directory.CreateDirectory(TempRoot);
        Preferences = Read<Preferences>("settings.json") ?? new();
        History = Read<List<Transcript>>("history.json") ?? [];
        if (!ModelCatalog.All.Any(m => m.Id == Preferences.ModelId)) Preferences.ModelId = ModelCatalog.All[0].Id;
    }
    T? Read<T>(string name)
    {
        var file = Path.Combine(Root, name);
        if (!File.Exists(file)) return default;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(file)); }
        catch (JsonException)
        {
            var backup = file + $".corrupt-{DateTime.UtcNow.Ticks}";
            File.Copy(file, backup);
            Warning = $"Could not read {name}. A recovery copy was saved beside it.";
            return default;
        }
    }
    void Write<T>(string name, T value)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
    public void SavePreferences() => Write("settings.json", Preferences);
    public Transcript? Add(string text, string model, double duration, string source, string audio)
    {
        if (Preferences.Retention == "Off") return null;
        var id = Guid.NewGuid().ToString("N");
        string? saved = null;
        if (Preferences.Retention == "Audio and text")
        {
            saved = id + ".wav";
            File.Copy(audio, Path.Combine(HistoryRoot, saved));
        }
        var item = new Transcript(id, DateTimeOffset.Now, text, model, duration, source, saved);
        History.Insert(0, item);
        Write("history.json", History);
        return item;
    }
    public string? AudioPath(Transcript item) => item.AudioFile is { } name && Path.GetFileName(name) == name ? Path.Combine(HistoryRoot, name) : null;
    public void Delete(Transcript item)
    {
        if (AudioPath(item) is { } path) File.Delete(path);
        History.Remove(item);
        Write("history.json", History);
    }
    public void ClearAudio()
    {
        foreach (var item in History) if (AudioPath(item) is { } path) File.Delete(path);
        History = History.Select(x => x with { AudioFile = null }).ToList();
        Write("history.json", History);
    }
    public void ClearHistory()
    {
        ClearAudio();
        History.Clear();
        Write("history.json", History);
    }
}
