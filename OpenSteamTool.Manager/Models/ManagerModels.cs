using System.Collections.ObjectModel;

namespace OpenSteamTool.Manager.Models;

public sealed class DllInstallStatus
{
    public string Name { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public bool PayloadAvailable { get; set; }
    public bool Installed { get; set; }
    public bool MatchesPayload { get; set; }
    public bool HasBackup { get; set; }
    public string? TargetHash { get; set; }
    public string? PayloadHash { get; set; }

    public string StateText
        => !PayloadAvailable
            ? "Payload 缺失"
            : !Installed
                ? HasBackup ? "未安装，已有备份" : "未安装"
                : MatchesPayload ? "已安装" : "存在同名文件/哈希不匹配";
}

public sealed class SteamInstallStatus
{
    public string SteamPath { get; set; } = string.Empty;
    public string SteamVersion { get; set; } = string.Empty;
    public bool IsValidSteamPath { get; set; }
    public bool IsSteamRunning { get; set; }
    public bool? IsOpenSteamToolLoaded { get; set; }
    public bool TomlExists { get; set; }
    public bool LuaDirExists { get; set; }
    public DateTime? LatestLogWriteTime { get; set; }
    public IReadOnlyList<DllInstallStatus> Dlls { get; set; } = Array.Empty<DllInstallStatus>();
    public IReadOnlyList<GameLuaConfig> Games { get; set; } = Array.Empty<GameLuaConfig>();
    public IReadOnlyList<string> LogFiles { get; set; } = Array.Empty<string>();

    public string Summary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SteamPath))
            {
                return "请选择 Steam 根目录。";
            }

            if (!IsValidSteamPath)
            {
                return "目录无效：未找到 steam.exe。";
            }

            var installed = Dlls.Count(x => x.Installed && x.MatchesPayload);
            var loaded = !IsSteamRunning
                ? "无法判断"
                : IsOpenSteamToolLoaded switch
                {
                    true => "已加载",
                    false => "未加载",
                    null => "无法判断"
                };
            var version = string.IsNullOrWhiteSpace(SteamVersion) ? "未知" : SteamVersion;
            return $"Steam {(IsSteamRunning ? "运行中" : "未运行")}, 版本 {version}, DLL {installed}/{Dlls.Count} 已匹配, Lua {Games.Count} 个, TOML {(TomlExists ? "存在" : "缺失")}";
        }
    }
}

public sealed class TomlSettings
{
    public string LogLevel { get; set; } = "info";
    public string ManifestUrl { get; set; } = "wudrm";
    public int TimeoutResolveMs { get; set; } = 5000;
    public int TimeoutConnectMs { get; set; } = 5000;
    public int TimeoutSendMs { get; set; } = 10000;
    public int TimeoutRecvMs { get; set; } = 10000;
    public ObservableCollection<string> LuaPaths { get; } = new();

    public string LuaPathsText
    {
        get => string.Join(Environment.NewLine, LuaPaths);
        set
        {
            LuaPaths.Clear();
            foreach (var line in value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    LuaPaths.Add(trimmed);
                }
            }
        }
    }
}

public sealed class DepotConfig
{
    public string DepotId { get; set; } = string.Empty;
    public string DecryptionKey { get; set; } = string.Empty;
}

public sealed class ManifestOverrideConfig
{
    public string DepotId { get; set; } = string.Empty;
    public string Gid { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
}

public sealed class GameLuaConfig
{
    public string AppId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Note { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string AppTicketHex { get; set; } = string.Empty;
    public string ETicketHex { get; set; } = string.Empty;
    public string StatSteamId { get; set; } = string.Empty;
    public string CustomLua { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public bool Managed { get; set; } = true;
    public ObservableCollection<DepotConfig> Depots { get; } = new();
    public ObservableCollection<ManifestOverrideConfig> ManifestOverrides { get; } = new();

    public string StatusText => Enabled ? "启用" : "禁用";
}

public sealed class LogSnapshot
{
    public string Module { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Tail { get; set; } = string.Empty;
}
