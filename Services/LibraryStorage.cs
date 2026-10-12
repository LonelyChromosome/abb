using System.IO;
using System.Text.Json;
using HenshinGrid.Models;

namespace HenshinGrid.Services;

public static class LibraryStorage
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HenshinGrid");
    private static string FilePath => Path.Combine(Root, "library.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch (Exception)
        {
            // Preserve the old file if corrupt; never overwrite it in Load.
        }
        return new();
    }

    public static void Save(AppSettings data)
    {
        Directory.CreateDirectory(Root);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}