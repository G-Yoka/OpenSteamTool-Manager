using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class SteamGameLibraryService
{
    private static readonly Regex ManifestValueRegex = new(@"^\s*""(?<key>appid|name|installdir)""\s*""(?<value>.*)""\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LibraryPathRegex = new(@"^\s*""(?<key>\d+|path)""\s*""(?<path>(?:[A-Za-z]:\\|\\\\|/)[^""]+)""\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IReadOnlyList<SteamGameInfo> ScanInstalledGames(string steamPath)
    {
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
        {
            return Array.Empty<SteamGameInfo>();
        }

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(steamPath)
        };

        var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFoldersPath))
        {
            foreach (var library in ReadLibraryFolders(libraryFoldersPath))
            {
                libraries.Add(library);
            }
        }

        var games = new Dictionary<string, SteamGameInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries)
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
            {
                continue;
            }

            foreach (var manifestPath in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                var game = ReadGameFromManifest(library, manifestPath);
                if (game is null || string.IsNullOrWhiteSpace(game.AppId) || games.ContainsKey(game.AppId))
                {
                    continue;
                }

                games.Add(game.AppId, game);
            }
        }

        return games.Values
            .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(game => game.AppId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SteamGameInfo? ReadGameFromManifest(string libraryPath, string manifestPath)
    {
        try
        {
            var text = File.ReadAllText(manifestPath);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var match = ManifestValueRegex.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                values[match.Groups["key"].Value] = Unescape(match.Groups["value"].Value);
            }

            if (!values.TryGetValue("appid", out var appId) || !values.TryGetValue("name", out var name) || !values.TryGetValue("installdir", out var installDir))
            {
                return null;
            }

            var installPath = Path.Combine(libraryPath, "steamapps", "common", installDir);
            if (!Directory.Exists(installPath))
            {
                return null;
            }

            return new SteamGameInfo
            {
                AppId = appId,
                Name = name,
                LibraryPath = libraryPath,
                InstallDir = installDir,
                InstallPath = installPath,
                ManifestPath = manifestPath
            };
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> ReadLibraryFolders(string libraryFoldersPath)
    {
        var result = new List<string>();

        try
        {
            var text = File.ReadAllText(libraryFoldersPath);
            foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var match = LibraryPathRegex.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var path = Unescape(match.Groups["path"].Value);
                if (Directory.Exists(path))
                {
                    result.Add(Path.GetFullPath(path));
                }
            }
        }
        catch
        {
            // Fall back to only the root Steam folder.
        }

        return result;
    }

    private static string Unescape(string value)
        => value.Replace(@"\\", @"\").Replace(@"\/", "/");
}
