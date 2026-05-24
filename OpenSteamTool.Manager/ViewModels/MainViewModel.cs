using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenSteamTool.Manager.Helpers;
using OpenSteamTool.Manager.Models;
using OpenSteamTool.Manager.Services;

namespace OpenSteamTool.Manager.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly SteamLocatorService _locator;
    private readonly SteamProcessService _process;
    private readonly PayloadService _payload;
    private readonly TomlConfigService _toml;
    private readonly LuaGameConfigService _lua;
    private readonly StatusService _status;
    private readonly UpdateService _updates;
    private readonly IAppControlService _appControl;
    private readonly IDialogService _dialogs;
    private readonly ITextPromptService _prompts;
    private UpdateCheckResult? _latestUpdate;

    public ObservableCollection<DllInstallStatus> DllStatuses { get; } = new();
    public ObservableCollection<GameLuaConfig> Games { get; } = new();

    [ObservableProperty]
    private string steamPath = string.Empty;

    [ObservableProperty]
    private string summaryText = string.Empty;

    [ObservableProperty]
    private string steamStateText = string.Empty;

    [ObservableProperty]
    private string steamVersionText = string.Empty;

    [ObservableProperty]
    private string appVersionText = string.Empty;

    [ObservableProperty]
    private string tomlStateText = string.Empty;

    [ObservableProperty]
    private string luaStateText = string.Empty;

    [ObservableProperty]
    private string logStateText = string.Empty;

    [ObservableProperty]
    private string dllLoadedStateText = string.Empty;

    [ObservableProperty]
    private string dllActionHintText = string.Empty;

    [ObservableProperty]
    private string updateStateText = string.Empty;

    [ObservableProperty]
    private string updateDetailText = string.Empty;

    [ObservableProperty]
    private string updateProgressText = string.Empty;

    [ObservableProperty]
    private string latestReleaseUrl = string.Empty;

    [ObservableProperty]
    private string logsText = string.Empty;

    [ObservableProperty]
    private string logModuleFilter = string.Empty;

    [ObservableProperty]
    private string tomlLogLevel = "info";

    [ObservableProperty]
    private string tomlManifestUrl = "wudrm";

    [ObservableProperty]
    private string tomlTimeoutResolveMs = "5000";

    [ObservableProperty]
    private string tomlTimeoutConnectMs = "5000";

    [ObservableProperty]
    private string tomlTimeoutSendMs = "10000";

    [ObservableProperty]
    private string tomlTimeoutRecvMs = "10000";

    [ObservableProperty]
    private string tomlLuaPathsText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveGameCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteGameCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddDepotCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddManifestCommand))]
    private GameLuaConfig? selectedGame;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveDepotCommand))]
    private DepotConfig? selectedDepot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveManifestCommand))]
    private ManifestOverrideConfig? selectedManifest;

    public MainViewModel(
        SteamLocatorService locator,
        SteamProcessService process,
        PayloadService payload,
        TomlConfigService toml,
        LuaGameConfigService lua,
        StatusService status,
        UpdateService updates,
        IAppControlService appControl,
        IDialogService dialogs,
        ITextPromptService prompts)
    {
        _locator = locator;
        _process = process;
        _payload = payload;
        _toml = toml;
        _lua = lua;
        _status = status;
        _updates = updates;
        _appControl = appControl;
        _dialogs = dialogs;
        _prompts = prompts;
    }

    public async Task InitializeAsync()
    {
        SteamPath = _locator.LoadLastPath();
        AppVersionText = _updates.CurrentVersionText;
        _ = RefreshLatestUpdateAsync();
        await ExecuteBusyAsync("正在加载状态...", () => RefreshStateAsync());
    }

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private async Task ChooseSteamPathAsync()
    {
        var chosen = _dialogs.PickFolder("选择 Steam 根目录（必须包含 steam.exe）", SteamPath);
        if (string.IsNullOrWhiteSpace(chosen))
        {
            return;
        }

        SteamPath = chosen;
        _locator.SaveLastPath(chosen);
        await ExecuteBusyAsync("正在刷新状态...", () => RefreshStateAsync());
    }

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private async Task RefreshAsync()
        => await ExecuteBusyAsync("正在刷新状态...", () => RefreshStateAsync());

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private async Task RestartSteamAsync()
        => await ExecuteBusyAsync("正在重启 Steam...", async () =>
        {
            if (!EnsureValidSteamPath())
            {
                return;
            }

            await Task.Run(() => _process.RestartSteam(SteamPath));
            await RefreshStateAsync();
        });

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private async Task InstallDllsAsync()
        => await ExecuteBusyAsync("正在安装 DLL...", async () =>
        {
            if (!EnsureValidSteamPath())
            {
                return;
            }

            await Task.Run(() => _payload.Install(SteamPath, _process.IsSteamRunning()));
            await RefreshStateAsync();
        });

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private async Task RemoveDllsAsync()
        => await ExecuteBusyAsync("正在移除 DLL...", async () =>
        {
            if (!EnsureValidSteamPath())
            {
                return;
            }

            await Task.Run(() => _payload.Remove(SteamPath, _process.IsSteamRunning()));
            await RefreshStateAsync();
        });

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private async Task SaveTomlAsync()
        => await ExecuteBusyAsync("正在保存 TOML...", async () =>
        {
            if (!EnsureValidSteamPath())
            {
                return;
            }

            var settings = BuildTomlSettings();
            await Task.Run(() => _toml.Save(SteamPath, settings));
            await RefreshStateAsync();
        });

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private async Task EnsureLuaDirAsync()
        => await ExecuteBusyAsync("正在创建 Lua 目录...", async () =>
        {
            if (!EnsureValidSteamPath())
            {
                return;
            }

            await Task.Run(() => _toml.EnsureLuaDirectory(SteamPath));
            await RefreshStateAsync();
        });

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private async Task OpenLuaFolderAsync()
        => await ExecuteBusyAsync("正在打开 Lua 文件夹...", async () =>
        {
            if (!EnsureValidSteamPath())
            {
                return;
            }

            await Task.Run(() => _toml.EnsureLuaDirectory(SteamPath));
            _dialogs.OpenFolder(Path.Combine(SteamPath, "config", "lua"));
        });

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private async Task CheckForUpdatesAsync()
        => await ExecuteBusyAsync("正在检查更新...", async () =>
        {
            var result = await _updates.CheckLatestAsync();
            _latestUpdate = result;
            ApplyUpdateResult(result);
        });

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private async Task DownloadAndUpdateAsync()
        => await ExecuteBusyAsync("正在下载并更新...", async () =>
        {
            var result = _latestUpdate ?? await _updates.CheckLatestAsync();
            _latestUpdate = result;
            ApplyUpdateResult(result);

            if (!result.HasRelease || !result.IsUpdateAvailable)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(result.AssetDownloadUrl))
            {
                UpdateStateText = "失败";
                UpdateDetailText = "该 Release 未提供可自动更新的 ZIP 发布包。";
                UpdateSummaryText();
                return;
            }

            UpdateStateText = "下载中";
            UpdateDetailText = string.IsNullOrWhiteSpace(result.AssetName) ? result.ReleaseName : result.AssetName;
            UpdateProgressText = string.Empty;
            UpdateSummaryText();

            var launched = false;
            var progress = new Progress<UpdateProgress>(value =>
            {
                UpdateStateText = value.Stage;
                UpdateProgressText = value.Percent > 0 ? $"{value.Percent}%" : string.Empty;
                UpdateDetailText = value.Message;
                UpdateSummaryText();
            });

            await _updates.StartSelfUpdateAsync(
                result,
                AppContext.BaseDirectory,
                Environment.ProcessId,
                progress);
            launched = true;

            UpdateStateText = "替换中";
            UpdateProgressText = string.Empty;
            UpdateDetailText = "更新器已启动，主程序退出后将自动替换并重启。";
            UpdateSummaryText();

            if (launched)
            {
                _appControl.Shutdown();
            }
        });

    [RelayCommand]
    private void OpenReleasePage()
    {
        var url = string.IsNullOrWhiteSpace(LatestReleaseUrl) ? _updates.ReleasesUrl : LatestReleaseUrl;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _dialogs.ShowWarning("OpenSteamTool 管理器", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private void AddGame()
    {
        var game = new GameLuaConfig { AppId = "480", Enabled = true };
        game.Depots.Add(new DepotConfig { DepotId = "480" });
        Games.Add(game);
        SelectedGame = game;
        SelectedDepot = game.Depots.FirstOrDefault();
        SelectedManifest = null;
        LuaStateText = $"{Games.Count} 个配置";
        UpdateSummaryText();
    }

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private async Task ImportGameAsync()
    {
        if (!EnsureValidSteamPath())
        {
            return;
        }

        var sourcePath = _dialogs.PickOpenFile(
            "导入 Lua 文件",
            "Lua 文件 (*.lua;*.lua.disabled)|*.lua;*.lua.disabled|所有文件 (*.*)|*.*",
            Directory.Exists(Path.Combine(SteamPath, "config", "lua"))
                ? Path.Combine(SteamPath, "config", "lua")
                : SteamPath);

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        var sourceText = await File.ReadAllTextAsync(sourcePath);
        var suggestedAppId = _lua.SuggestAppId(sourcePath, sourceText) ?? string.Empty;
        var appId = _prompts.Show(
            "确认 AppId",
            "请输入该 Lua 文件对应的 AppId。管理器会把它导入为 ost_<appid>.lua。",
            suggestedAppId);

        if (string.IsNullOrWhiteSpace(appId) || !uint.TryParse(appId, out _))
        {
            return;
        }

        var enabled = !sourcePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var targetPath = Path.Combine(SteamPath, "config", "lua", $"ost_{appId}.lua{(enabled ? string.Empty : ".disabled")}");
        if (File.Exists(targetPath) && !_dialogs.Confirm("OpenSteamTool 管理器", "目标 AppId 已存在管理器 Lua 文件，是否覆盖？"))
        {
            return;
        }

        await ExecuteBusyAsync("正在导入 Lua...", async () =>
        {
            var imported = await Task.Run(() => _lua.ImportFromFile(SteamPath, sourcePath, appId, enabled));
            await RefreshStateAsync();
            SelectedGame = Games.FirstOrDefault(x => x.AppId == imported.AppId) ?? Games.FirstOrDefault();
            SelectedDepot = SelectedGame?.Depots.FirstOrDefault();
        });
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedGame))]
    private async Task SaveGameAsync()
        => await ExecuteBusyAsync("正在保存 Lua...", async () =>
        {
            var game = SelectedGame;
            if (game is null)
            {
                return;
            }

            if (!EnsureValidSteamPath())
            {
                return;
            }

            await Task.Run(() => _lua.Save(SteamPath, game));
            await RefreshStateAsync(preserveAppId: game.AppId);
        });

    [RelayCommand(CanExecute = nameof(CanModifySelectedGame))]
    private async Task DeleteGameAsync()
        => await ExecuteBusyAsync("正在删除 Lua...", async () =>
        {
            var game = SelectedGame;
            if (game is null)
            {
                return;
            }

            await Task.Run(() => _lua.Delete(game));
            var deletedAppId = game.AppId;
            Games.Remove(game);
            SelectedGame = Games.FirstOrDefault(x => x.AppId == deletedAppId) ?? Games.FirstOrDefault();
            SelectedDepot = SelectedGame?.Depots.FirstOrDefault();
            SelectedManifest = null;
            LuaStateText = $"{Games.Count} 个配置";
            UpdateSummaryText();
        });

    [RelayCommand(CanExecute = nameof(CanModifySelectedGame))]
    private void AddDepot()
    {
        if (SelectedGame is null)
        {
            return;
        }

        var depot = new DepotConfig { DepotId = SelectedGame.AppId };
        SelectedGame.Depots.Add(depot);
        SelectedDepot = depot;
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedDepot))]
    private void RemoveDepot()
    {
        if (SelectedGame is null || SelectedDepot is null)
        {
            return;
        }

        SelectedGame.Depots.Remove(SelectedDepot);
        SelectedDepot = SelectedGame.Depots.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedGame))]
    private void AddManifest()
    {
        if (SelectedGame is null)
        {
            return;
        }

        var manifest = new ManifestOverrideConfig { DepotId = SelectedGame.AppId };
        SelectedGame.ManifestOverrides.Add(manifest);
        SelectedManifest = manifest;
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedManifest))]
    private void RemoveManifest()
    {
        if (SelectedGame is null || SelectedManifest is null)
        {
            return;
        }

        SelectedGame.ManifestOverrides.Remove(SelectedManifest);
        SelectedManifest = SelectedGame.ManifestOverrides.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanExecuteWhenIdle))]
    private async Task LoadLogsAsync()
        => await ExecuteBusyAsync("正在读取日志...", async () =>
        {
            if (!EnsureValidSteamPath())
            {
                return;
            }

            var module = LogModuleFilter.Trim();
            var snapshots = await Task.Run(() => _status.ReadLogs(SteamPath, module));
            var sb = new StringBuilder();
            foreach (var snapshot in snapshots)
            {
                sb.AppendLine($"===== {snapshot.Module} ({snapshot.Path}) =====");
                sb.AppendLine(snapshot.Tail);
                sb.AppendLine();
            }

            LogsText = sb.Length == 0 ? _status.BuildLogDiagnostic(SteamPath, module) : sb.ToString();
        });

    private async Task RefreshLatestUpdateAsync()
    {
        var result = await _updates.CheckLatestAsync();
        _latestUpdate = result;
        ApplyUpdateResult(result);
    }

    private void ApplyUpdateResult(UpdateCheckResult result)
    {
        _latestUpdate = result;
        LatestReleaseUrl = result.ReleasePageUrl;
        AppVersionText = result.CurrentVersion.ToString(3);

        if (result.ErrorMessage is not null)
        {
            UpdateStateText = result.HasRelease ? "检查失败" : "暂无公开 Release";
            UpdateDetailText = result.ErrorMessage;
            UpdateProgressText = string.Empty;
            UpdateSummaryText();
            return;
        }

        if (!result.HasRelease)
        {
            UpdateStateText = "暂无公开 Release";
            UpdateDetailText = "仓库当前没有可用于自动更新的公开 Release。";
            UpdateProgressText = string.Empty;
            UpdateSummaryText();
            return;
        }

        if (result.IsUpdateAvailable)
        {
            UpdateStateText = "可更新";
            UpdateDetailText = BuildUpdateDetail(result);
            UpdateProgressText = string.Empty;
            UpdateSummaryText();
            return;
        }

        UpdateStateText = "已是最新";
        UpdateDetailText = BuildUpdateDetail(result);
        UpdateProgressText = string.Empty;
        UpdateSummaryText();
    }

    private static string BuildUpdateDetail(UpdateCheckResult result)
    {
        var versionText = result.LatestVersion?.ToString(3) ?? result.ReleaseTag;
        var publishedText = result.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未知时间";
        var detail = $"当前版本 {result.CurrentVersion.ToString(3)}，最新版本 {versionText}，发布于 {publishedText}";

        if (!string.IsNullOrWhiteSpace(result.AssetName))
        {
            detail += $"，发布包 {result.AssetName}";
        }

        return detail;
    }

    public async Task RefreshStateAsync(string? preserveAppId = null)
    {
        var currentPath = SteamPath;
        var status = await Task.Run(() => _status.GetStatus(currentPath));

        SummaryText = status.Summary;
        SteamStateText = string.IsNullOrWhiteSpace(currentPath)
            ? "未选择"
            : status.IsValidSteamPath
                ? (status.IsSteamRunning ? "运行中" : "可用")
                : "无效";
        SteamVersionText = string.IsNullOrWhiteSpace(status.SteamVersion) ? "未知" : status.SteamVersion;
        TomlStateText = status.TomlExists ? "存在" : "缺失";
        LuaStateText = status.LuaDirExists ? $"{status.Games.Count} 个配置" : "目录缺失";
        LogStateText = status.LatestLogWriteTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "无日志";
        DllLoadedStateText = FormatDllLoadedState(status);
        DllActionHintText = BuildDllActionHint(status);

        DllStatuses.Clear();
        foreach (var dll in status.Dlls)
        {
            DllStatuses.Add(dll);
        }

        Games.Clear();
        foreach (var game in status.Games)
        {
            Games.Add(game);
        }

        var toml = status.IsValidSteamPath
            ? await Task.Run(() => _toml.Load(currentPath))
            : new TomlSettings();
        LoadTomlToFields(toml);

        SelectedGame = preserveAppId is null
            ? Games.FirstOrDefault()
            : Games.FirstOrDefault(x => x.AppId == preserveAppId) ?? Games.FirstOrDefault();
        SelectedDepot = SelectedGame?.Depots.FirstOrDefault();
        SelectedManifest = null;
        UpdateSummaryText();
    }

    private void LoadTomlToFields(TomlSettings settings)
    {
        TomlLogLevel = settings.LogLevel;
        TomlManifestUrl = settings.ManifestUrl;
        TomlTimeoutResolveMs = settings.TimeoutResolveMs.ToString();
        TomlTimeoutConnectMs = settings.TimeoutConnectMs.ToString();
        TomlTimeoutSendMs = settings.TimeoutSendMs.ToString();
        TomlTimeoutRecvMs = settings.TimeoutRecvMs.ToString();
        TomlLuaPathsText = settings.LuaPathsText;
    }

    private TomlSettings BuildTomlSettings()
    {
        return new TomlSettings
        {
            LogLevel = TomlLogLevel.Trim(),
            ManifestUrl = TomlManifestUrl.Trim(),
            TimeoutResolveMs = ParsePositiveInt(TomlTimeoutResolveMs, "解析超时"),
            TimeoutConnectMs = ParsePositiveInt(TomlTimeoutConnectMs, "连接超时"),
            TimeoutSendMs = ParsePositiveInt(TomlTimeoutSendMs, "发送超时"),
            TimeoutRecvMs = ParsePositiveInt(TomlTimeoutRecvMs, "接收超时"),
            LuaPathsText = TomlLuaPathsText
        };
    }

    private bool EnsureValidSteamPath()
    {
        if (_locator.IsValidSteamPath(SteamPath))
        {
            _locator.SaveLastPath(SteamPath);
            return true;
        }

        _dialogs.ShowWarning("OpenSteamTool 管理器", "请选择包含 steam.exe 的 Steam 根目录。");
        return false;
    }

    private async Task ExecuteBusyAsync(string busyMessage, Func<Task> action)
    {
        if (!CanExecuteWhenIdle())
        {
            return;
        }

        IsBusy = true;
        BusyMessage = busyMessage;
        NotifyCommandStates();

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _dialogs.ShowWarning("OpenSteamTool 管理器", ex.Message);
        }
        finally
        {
            BusyMessage = string.Empty;
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    private bool CanExecuteWhenIdle()
        => !IsBusy;

    private bool CanModifySelectedGame()
        => !IsBusy && SelectedGame is not null;

    private bool CanModifySelectedDepot()
        => !IsBusy && SelectedGame is not null && SelectedDepot is not null;

    private bool CanModifySelectedManifest()
        => !IsBusy && SelectedGame is not null && SelectedManifest is not null;

    private void UpdateSummaryText()
    {
        SummaryText = $"Steam {SteamStateText}，版本 {SteamVersionText}，DLL {DllStatuses.Count(x => x.Installed && x.MatchesPayload)}/{DllStatuses.Count} 已匹配，Lua {Games.Count} 个，TOML {TomlStateText}";
    }

    private string FormatDllLoadedState(SteamInstallStatus status)
    {
        if (!status.IsSteamRunning)
        {
            return "无法判断";
        }

        return status.IsOpenSteamToolLoaded switch
        {
            true => "已加载",
            false => "未加载",
            null => "无法判断"
        };
    }

    private string BuildDllActionHint(SteamInstallStatus status)
    {
        if (!status.IsSteamRunning)
        {
            return "Steam 未运行，无法判断 OpenSteamTool.dll 是否已进入 steam.exe。";
        }

        return status.IsOpenSteamToolLoaded switch
        {
            true => "OpenSteamTool.dll 已加载到 steam.exe。若功能没有变化，请检查 Lua/TOML 是否已保存。",
            false => "Steam 正在运行，但 OpenSteamTool.dll 还没有进入 steam.exe。安装或更新 DLL 后通常需要重启 Steam。",
            null => "Steam 正在运行，但当前无法枚举模块列表，无法判断 DLL 是否已加载。"
        };
    }

    partial void OnSelectedGameChanged(GameLuaConfig? value)
    {
        SelectedDepot = value?.Depots.FirstOrDefault();
        SelectedManifest = null;
        NotifyCommandStates();
    }

    partial void OnSelectedDepotChanged(DepotConfig? value)
        => NotifyCommandStates();

    partial void OnSelectedManifestChanged(ManifestOverrideConfig? value)
        => NotifyCommandStates();

    private void NotifyCommandStates()
    {
        ChooseSteamPathCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        RestartSteamCommand.NotifyCanExecuteChanged();
        InstallDllsCommand.NotifyCanExecuteChanged();
        RemoveDllsCommand.NotifyCanExecuteChanged();
        SaveTomlCommand.NotifyCanExecuteChanged();
        EnsureLuaDirCommand.NotifyCanExecuteChanged();
        OpenLuaFolderCommand.NotifyCanExecuteChanged();
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadAndUpdateCommand.NotifyCanExecuteChanged();
        OpenReleasePageCommand.NotifyCanExecuteChanged();
        AddGameCommand.NotifyCanExecuteChanged();
        ImportGameCommand.NotifyCanExecuteChanged();
        SaveGameCommand.NotifyCanExecuteChanged();
        DeleteGameCommand.NotifyCanExecuteChanged();
        AddDepotCommand.NotifyCanExecuteChanged();
        RemoveDepotCommand.NotifyCanExecuteChanged();
        AddManifestCommand.NotifyCanExecuteChanged();
        RemoveManifestCommand.NotifyCanExecuteChanged();
        LoadLogsCommand.NotifyCanExecuteChanged();
    }

    private static int ParsePositiveInt(string value, string name)
    {
        if (!int.TryParse(value.Trim(), out var result) || result <= 0)
        {
            throw new InvalidOperationException($"{name} 必须是大于 0 的整数。");
        }

        return result;
    }
}
