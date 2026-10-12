using System.Text.Json.Serialization;
using System.Windows.Media;

namespace HenshinGrid.Models;

public class GameEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string LaunchTarget { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string ArtPath { get; set; } = "";
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public bool Favorite { get; set; }

    [JsonIgnore] public ImageSource? Icon { get; set; }
    [JsonIgnore] public string Monogram => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..Math.Min(2, Name.Trim().Length)].ToUpperInvariant();
    [JsonIgnore] public string SourceLabel => Source.ToUpperInvariant();
}