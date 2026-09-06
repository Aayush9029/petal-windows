using System.Globalization;

namespace Petal.Core;

public static class TranscriptSearch
{
    public static bool Matches(Transcript item, string query)
    {
        string searchable = string.Join(" ", item.Text, item.Source, item.ModelId, item.Timestamp.LocalDateTime.ToString("D", CultureInfo.CurrentCulture));
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(word =>
            CultureInfo.CurrentCulture.CompareInfo.IndexOf(searchable, word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    }
}
