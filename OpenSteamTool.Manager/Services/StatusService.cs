using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class StatusService(
    SteamLocatorService locator,
    SteamProcessService process,
    PayloadService payload,
    LuaGameConfigService lua)
{
    public SteamInstallStatus GetStatus(string steamPath)
    {
        var running = process.IsSteamRunning();
        var loaded = process.IsOpenSteamToolLoaded();
        var valid = locator.IsValidSteamPath(steamPath);
        if (!valid)
        {
            return new SteamInstallStatus
            {
                SteamPath = steamPath,
                SteamVersion = string.Empty,
                IsValidSteamPath = false,
                IsSteamRunning = running,
                IsOpenSteamToolLoaded = loaded
            };
        }

        var logDir = Path.Combine(steamPath, "opensteamtool");
        var logFiles = EnumerateLogFiles(logDir).ToList();
        var steamExe = Path.Combine(steamPath, "steam.exe");

        return new SteamInstallStatus
        {
            SteamPath = steamPath,
            SteamVersion = ReadSteamVersion(steamPath, steamExe),
            IsValidSteamPath = true,
            IsSteamRunning = running,
            IsOpenSteamToolLoaded = loaded,
            TomlExists = File.Exists(Path.Combine(steamPath, "opensteamtool.toml")),
            LuaDirExists = Directory.Exists(Path.Combine(steamPath, "config", "lua")),
            LatestLogWriteTime = logFiles.Count == 0 ? null : logFiles.Max(File.GetLastWriteTime),
            Dlls = payload.GetDllStatuses(steamPath),
            Games = lua.Load(steamPath).ToList(),
            LogFiles = logFiles
        };
    }

    public IReadOnlyList<LogSnapshot> ReadLogs(string steamPath, string module)
    {
        var logDir = Path.Combine(steamPath, "opensteamtool");
        if (!Directory.Exists(logDir))
        {
            return Array.Empty<LogSnapshot>();
        }

        var files = EnumerateLogFiles(logDir, module);

        return files.Select(path => new LogSnapshot
        {
            Module = Path.GetFileNameWithoutExtension(path),
            Path = path,
            Tail = ReadTail(path, 160)
        }).ToList();
    }

    public string BuildLogDiagnostic(string steamPath, string module)
    {
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return "请先选择 Steam 目录。";
        }

        var logDir = Path.Combine(steamPath, "opensteamtool");
        var sb = new StringBuilder();
        sb.AppendLine("未找到可显示的日志。");
        sb.AppendLine($"检查目录: {logDir}");
        sb.AppendLine($"目录存在: {Directory.Exists(logDir)}");
        sb.AppendLine($"筛选模块: {(string.IsNullOrWhiteSpace(module) ? "全部" : module)}");

        if (Directory.Exists(logDir))
        {
            sb.AppendLine($"递归 .log 数量: {EnumerateLogFiles(logDir).Count()}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string ReadTail(string path, int maxLines)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var lines = new List<string>();
            while (!reader.EndOfStream)
            {
                lines.Add(reader.ReadLine() ?? string.Empty);
            }

            return string.Join(Environment.NewLine, lines.TakeLast(maxLines));
        }
        catch (Exception ex)
        {
            return $"读取日志失败：{ex.Message}";
        }
    }

    private static IEnumerable<string> EnumerateLogFiles(string logDir, string? module = null)
    {
        if (!Directory.Exists(logDir))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(logDir, "*.log", SearchOption.AllDirectories)
            .Where(path => string.IsNullOrWhiteSpace(module)
                           || Path.GetFileNameWithoutExtension(path).Contains(module, StringComparison.OrdinalIgnoreCase)
                           || Path.GetFileName(path).Contains(module, StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName);
    }

    private static string ReadSteamVersion(string steamPath, string steamExe)
    {
        var clientVersion = ReadClientVersionFromLog(Path.Combine(steamPath, "logs", "connection_log.txt"));
        if (!string.IsNullOrWhiteSpace(clientVersion))
        {
            return clientVersion;
        }

        return ReadSteamExeVersion(steamExe);
    }

    private static string ReadClientVersionFromLog(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return string.Empty;
        }

        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            string? latest = null;
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var match = Regex.Match(line, @"Client version:\s*(?<version>\d+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    latest = match.Groups["version"].Value;
                }
            }

            return latest ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadSteamExeVersion(string steamExe)
    {
        if (!File.Exists(steamExe))
        {
            return string.Empty;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(steamExe);
            return !string.IsNullOrWhiteSpace(info.ProductVersion)
                ? info.ProductVersion
                : info.FileVersion ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
