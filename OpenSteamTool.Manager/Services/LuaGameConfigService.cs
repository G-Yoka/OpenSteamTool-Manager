using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class LuaGameConfigService
{
    private const string Marker = "-- OpenSteamTool Manager";
    private static readonly Regex AddAppId = new("addappid\\((?<id>\\d+)(?:\\s*,\\s*0\\s*,\\s*\"(?<key>[0-9a-fA-F]{64})\")?\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AddToken = new("addtoken\\((?<id>\\d+)\\s*,\\s*\"(?<value>\\d+)\"\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SetManifest = new("setmanifestid\\((?<depot>\\d+)\\s*,\\s*\"(?<gid>\\d+)\"(?:\\s*,\\s*(?<size>\\d+))?\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SetTicket = new("setappticket\\((?<id>\\d+)\\s*,\\s*\"(?<value>[0-9a-fA-F]+)\"\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SetETicket = new("seteticket\\((?<id>\\d+)\\s*,\\s*\"(?<value>[0-9a-fA-F]+)\"\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SetStat = new("setstat\\((?<id>\\d+)\\s*,\\s*\"(?<value>\\d+)\"\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ManagedHeader = new(@"^\s*--\s*OpenSteamTool Manager\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AppIdHeader = new(@"^\s*--\s*AppId:\s*(?<id>\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NoteHeader = new(@"^\s*--\s*澶囨敞:\s*(?<value>.*)$", RegexOptions.Compiled);
    private static readonly Regex ManagedNoteHeader = new(@"^\s*--\s*(?:备注|Note|澶囨敞)\s*:\s*(?<value>.*)$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ManagedNotice = new(@"^\s*--\s*This file is managed by OpenSteamTool Manager\.\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ObservableCollection<GameLuaConfig> Load(string steamPath)
    {
        var result = new ObservableCollection<GameLuaConfig>();
        var luaDir = GetLuaDir(steamPath);
        if (!Directory.Exists(luaDir))
        {
            return result;
        }

        foreach (var path in Directory.EnumerateFiles(luaDir, "ost_*.lua*").OrderBy(x => x))
        {
            var text = File.ReadAllText(path);
            if (!text.StartsWith(Marker, StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(ParseManagedFile(path, text, !path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)));
        }

        return result;
    }

    public void Save(string steamPath, GameLuaConfig config)
    {
        Validate(config);
        Directory.CreateDirectory(GetLuaDir(steamPath));

        var appId = config.AppId.Trim();
        var enabledPath = GetGamePath(steamPath, appId, enabled: true);
        var disabledPath = GetGamePath(steamPath, appId, enabled: false);
        var targetPath = config.Enabled ? enabledPath : disabledPath;
        var previousPath = config.FilePath;

        if (!string.IsNullOrWhiteSpace(previousPath)
            && !string.Equals(previousPath, targetPath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(previousPath))
        {
            File.Delete(previousPath);
        }

        File.WriteAllText(targetPath, Render(config), new UTF8Encoding(false));
        config.FilePath = targetPath;
        config.Managed = true;
    }

    public void Delete(GameLuaConfig config)
    {
        if (config.Managed && File.Exists(config.FilePath))
        {
            File.Delete(config.FilePath);
        }
    }

    public string? SuggestAppId(string sourcePath, string text)
    {
        var fileMatch = Regex.Match(Path.GetFileName(sourcePath), @"(?:ost_)?(?<id>\d+)", RegexOptions.IgnoreCase);
        if (fileMatch.Success)
        {
            return fileMatch.Groups["id"].Value;
        }

        var appIds = AddAppId.Matches(text)
            .Select(m => m.Groups["id"].Value)
            .Distinct()
            .ToList();
        if (appIds.Count == 1)
        {
            return appIds[0];
        }

        if (appIds.Count > 1)
        {
            return appIds[0];
        }

        return null;
    }

    public GameLuaConfig ImportFromFile(string steamPath, string sourcePath, string appId, bool enabled)
    {
        var text = File.ReadAllText(sourcePath);
        var config = ParseImportedFile(sourcePath, text, appId, enabled);
        Directory.CreateDirectory(GetLuaDir(steamPath));

        var targetPath = GetGamePath(steamPath, config.AppId, config.Enabled);
        File.WriteAllText(targetPath, Render(config), new UTF8Encoding(false));
        config.FilePath = targetPath;
        config.Managed = true;
        return config;
    }

    private static GameLuaConfig ParseManagedFile(string path, string text, bool enabled)
    {
        var config = new GameLuaConfig
        {
            Enabled = enabled,
            FilePath = path,
            Managed = true
        };

        var firstApp = AddAppId.Match(text);
        if (firstApp.Success)
        {
            config.AppId = firstApp.Groups["id"].Value;
        }
        else
        {
            var fileName = Path.GetFileName(path);
            var idMatch = Regex.Match(fileName, "ost_(?<id>\\d+)\\.lua", RegexOptions.IgnoreCase);
            config.AppId = idMatch.Success ? idMatch.Groups["id"].Value : string.Empty;
        }

        foreach (Match match in AddAppId.Matches(text))
        {
            config.Depots.Add(new DepotConfig
            {
                DepotId = match.Groups["id"].Value,
                DecryptionKey = match.Groups["key"].Value
            });
        }

        foreach (Match match in SetManifest.Matches(text))
        {
            config.ManifestOverrides.Add(new ManifestOverrideConfig
            {
                DepotId = match.Groups["depot"].Value,
                Gid = match.Groups["gid"].Value,
                Size = match.Groups["size"].Value
            });
        }

        config.AccessToken = MatchValue(AddToken, text);
        config.AppTicketHex = MatchValue(SetTicket, text);
        config.ETicketHex = MatchValue(SetETicket, text);
        config.StatSteamId = MatchValue(SetStat, text);
        config.Note = MatchValue(ManagedNoteHeader, text, "value");

        var customIndex = text.IndexOf("-- Custom Lua", StringComparison.OrdinalIgnoreCase);
        if (customIndex >= 0)
        {
            config.CustomLua = text[(customIndex + "-- Custom Lua".Length)..].Trim();
        }

        return config;
    }

    private static GameLuaConfig ParseImportedFile(string sourcePath, string text, string appId, bool enabled)
    {
        var config = new GameLuaConfig
        {
            AppId = appId,
            Enabled = enabled,
            FilePath = sourcePath,
            Managed = false
        };
        var inferredNote = Path.GetFileName(sourcePath);
        if (inferredNote.EndsWith(".lua.disabled", StringComparison.OrdinalIgnoreCase))
        {
            inferredNote = inferredNote[..^".lua.disabled".Length];
        }
        else if (inferredNote.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            inferredNote = inferredNote[..^".lua".Length];
        }

        if (Regex.IsMatch(inferredNote, @"^(ost_)?\d+$", RegexOptions.IgnoreCase))
        {
            inferredNote = string.Empty;
        }

        foreach (Match match in AddAppId.Matches(text))
        {
            config.Depots.Add(new DepotConfig
            {
                DepotId = match.Groups["id"].Value,
                DecryptionKey = match.Groups["key"].Value
            });
        }

        foreach (Match match in SetManifest.Matches(text))
        {
            config.ManifestOverrides.Add(new ManifestOverrideConfig
            {
                DepotId = match.Groups["depot"].Value,
                Gid = match.Groups["gid"].Value,
                Size = match.Groups["size"].Value
            });
        }

        config.AccessToken = MatchValue(AddToken, text);
        config.AppTicketHex = MatchValue(SetTicket, text);
        config.ETicketHex = MatchValue(SetETicket, text);
        config.StatSteamId = MatchValue(SetStat, text);
        config.Note = MatchValue(ManagedNoteHeader, text, "value");
        if (string.IsNullOrWhiteSpace(config.Note))
        {
            config.Note = inferredNote;
        }
        config.CustomLua = StripManagedLines(text);
        return config;
    }

    private static string MatchValue(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success ? match.Groups["value"].Value : string.Empty;
    }

    private static string MatchValue(Regex regex, string text, string groupName)
    {
        var match = regex.Match(text);
        return match.Success ? match.Groups[groupName].Value.Trim() : string.Empty;
    }

    private static string Render(GameLuaConfig config)
    {
        var appId = config.AppId.Trim();
        var sb = new StringBuilder();
        sb.AppendLine(Marker);
        sb.AppendLine($"-- AppId: {appId}");
        sb.AppendLine("-- This file is managed by OpenSteamTool Manager.");
        if (!string.IsNullOrWhiteSpace(config.Note))
        {
            sb.AppendLine($"-- 备注: {config.Note.Trim()}");
        }
        sb.AppendLine();

        var depots = config.Depots.Count > 0
            ? config.Depots
            : new ObservableCollection<DepotConfig> { new() { DepotId = appId } };

        foreach (var depot in depots)
        {
            var depotId = depot.DepotId.Trim();
            var key = depot.DecryptionKey.Trim();
            if (string.IsNullOrWhiteSpace(depotId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(key))
            {
                sb.AppendLine($"addappid({depotId}, 0, \"{key}\")");
            }
            else
            {
                sb.AppendLine($"addappid({depotId})");
            }
        }

        if (!string.IsNullOrWhiteSpace(config.AccessToken))
        {
            sb.AppendLine($"addtoken({appId}, \"{config.AccessToken.Trim()}\")");
        }

        foreach (var manifest in config.ManifestOverrides)
        {
            if (string.IsNullOrWhiteSpace(manifest.DepotId) || string.IsNullOrWhiteSpace(manifest.Gid))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(manifest.Size))
            {
                sb.AppendLine($"setManifestid({manifest.DepotId.Trim()}, \"{manifest.Gid.Trim()}\")");
            }
            else
            {
                sb.AppendLine($"setManifestid({manifest.DepotId.Trim()}, \"{manifest.Gid.Trim()}\", {manifest.Size.Trim()})");
            }
        }

        if (!string.IsNullOrWhiteSpace(config.AppTicketHex))
        {
            sb.AppendLine($"setAppTicket({appId}, \"{config.AppTicketHex.Trim()}\")");
        }

        if (!string.IsNullOrWhiteSpace(config.ETicketHex))
        {
            sb.AppendLine($"setETicket({appId}, \"{config.ETicketHex.Trim()}\")");
        }

        if (!string.IsNullOrWhiteSpace(config.StatSteamId))
        {
            sb.AppendLine($"setStat({appId}, \"{config.StatSteamId.Trim()}\")");
        }

        if (!string.IsNullOrWhiteSpace(config.CustomLua))
        {
            sb.AppendLine();
            sb.AppendLine("-- Custom Lua");
            sb.AppendLine(config.CustomLua.Trim());
        }

        return sb.ToString();
    }

    private static void Validate(GameLuaConfig config)
    {
        if (!uint.TryParse(config.AppId, out _))
        {
            throw new InvalidOperationException("AppId 必须是 uint32 数字。");
        }

        foreach (var depot in config.Depots)
        {
            if (!string.IsNullOrWhiteSpace(depot.DepotId) && !uint.TryParse(depot.DepotId, out _))
            {
                throw new InvalidOperationException("DepotId 必须是 uint32 数字。");
            }

            if (!string.IsNullOrWhiteSpace(depot.DecryptionKey)
                && !Regex.IsMatch(depot.DecryptionKey.Trim(), "^[0-9a-fA-F]{64}$"))
            {
                throw new InvalidOperationException("Depot decryption key 必须是 64 位十六进制字符串。");
            }
        }
    }

    private static string GetLuaDir(string steamPath)
        => Path.Combine(steamPath, "config", "lua");

    private static string GetGamePath(string steamPath, string appId, bool enabled)
        => Path.Combine(GetLuaDir(steamPath), $"ost_{appId}.lua{(enabled ? string.Empty : ".disabled")}");

    private static string StripManagedLines(string text)
    {
        var lines = new List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (ManagedHeader.IsMatch(line) || ManagedNotice.IsMatch(line) || AppIdHeader.IsMatch(line))
            {
                continue;
            }

            if (ManagedNoteHeader.IsMatch(line))
            {
                continue;
            }

            if (AddAppId.IsMatch(line)
                || AddToken.IsMatch(line)
                || SetManifest.IsMatch(line)
                || SetTicket.IsMatch(line)
                || SetETicket.IsMatch(line)
                || SetStat.IsMatch(line))
            {
                continue;
            }

            lines.Add(rawLine);
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }
}


