using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using HenshinGrid.Models;
using Microsoft.Win32;

namespace HenshinGrid.Services;

// Reads only launcher-owned installation manifests. Never opens a game process or modifies a game directory.
public static class GameDiscovery
{
    private static readonly Regex VdfField = new("\"(?<key>[^\"]+)\"\\s+\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled);

    public static IEnumerable<string> SteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string? registry = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(registry)) roots.Add(registry.Replace('/', '\\'));
        }
        catch { }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        return roots.Where(Directory.Exists);
    }

    public static IEnumerable<string> SteamManifestDirectories()
    {
        foreach (var root in SteamRoots())
        {
            if (!Directory.Exists(root)) continue;
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            var pathFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(pathFile))
            {
                try
                {
                    string vdf = File.ReadAllText(pathFile);
                    foreach (Match m in VdfField.Matches(vdf))
                    {
                        if (m.Groups["key"].Value.Equals("path", StringComparison.OrdinalIgnoreCase))
                        {
                            string lib = m.Groups["value"].Value.Replace("\\\\", "\\");
                            if (Directory.Exists(lib)) libraries.Add(lib);
                        }
                    }
                }
                catch { }
            }
            foreach (string lib in libraries)
            {
                string manifests = Path.Combine(lib, "steamapps");
                if (Directory.Exists(manifests)) yield return manifests;
            }
        }
    }

    public static string EpicManifestDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public static IEnumerable<GameEntry> Discover()
    {
        foreach (string folder in SteamManifestDirectories().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string[] files;
            try { files = Directory.GetFiles(folder, "appmanifest_*.acf", SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (string file in files)
            {
                GameEntry? found = ParseSteam(file);
                if (found is not null) yield return found;
            }
        }
        if (Directory.Exists(EpicManifestDirectory))
        {
            string[] files;
            try { files = Directory.GetFiles(EpicManifestDirectory, "*.item", SearchOption.TopDirectoryOnly); }
            catch { files = Array.Empty<string>(); }
            foreach (string file in files)
            {
                GameEntry? found = ParseEpic(file);
                if (found is not null) yield return found;
            }
        }
    }

    private static GameEntry? ParseSteam(string file)
    {
        try
        {
            string raw = File.ReadAllText(file);
            var fields = VdfField.Matches(raw).Cast<Match>()
                .GroupBy(m => m.Groups["key"].Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Groups["value"].Value, StringComparer.OrdinalIgnoreCase);
            if (!fields.TryGetValue("appid", out string? id) || !Regex.IsMatch(id, @"^\d+$") ||
                !fields.TryGetValue("name", out string? name) || string.IsNullOrWhiteSpace(name)) return null;
            if (!fields.TryGetValue("installdir", out string? installDir)) return null;
            string directory = Path.Combine(Path.GetDirectoryName(file)!, "common", installDir);
            if (!Directory.Exists(directory)) return null;
            string exe = FindProbableGameExe(directory);
            return new()
            {
                Id = "steam:" + id,
                Name = name,
                Source = "Steam",
                LaunchTarget = "steam://rungameid/" + id,
                ExecutablePath = exe,
                WorkingDirectory = directory
            };
        }
        catch { return null; }
    }

    private static GameEntry? ParseEpic(string file)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var root = json.RootElement;
            string Get(string key) => root.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            string title = Get("DisplayName"), appName = Get("AppName"), folder = Get("InstallLocation");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(appName) || !Directory.Exists(folder)) return null;
            string relativeExe = Get("LaunchExecutable").TrimStart('\\', '/');
            string candidateExe = Path.GetFullPath(Path.Combine(folder, relativeExe));
            string exe = candidateExe.StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                      && File.Exists(candidateExe) ? candidateExe : "";
            if (string.IsNullOrEmpty(exe)) exe = FindProbableGameExe(folder);
            return new()
            {
                Id = "epic:" + appName,
                Name = title,
                Source = "Epic",
                LaunchTarget = "com.epicgames.launcher://apps/" + Uri.EscapeDataString(appName) + "?action=launch&silent=true",
                ExecutablePath = exe,
                WorkingDirectory = folder
            };
        }
        catch { return null; }
    }

    // Used for a local icon and for observing game-exit. Never executes an unidentified EXE.
    private static string FindProbableGameExe(string directory)
    {
        try
        {
            foreach (string exe in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
            {
                string filename = Path.GetFileName(exe).ToLowerInvariant();
                if (!new[] { "unins", "setup", "crash", "helper", "launcher", "redist", "update" }.Any(filename.Contains)) return exe;
            }
        }
        catch { }
        return "";
    }
}