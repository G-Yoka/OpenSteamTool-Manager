using OpenSteamTool.Manager.Services;
using OpenSteamTool.Manager.ViewModels;

namespace OpenSteamTool.Manager.Helpers;

public static class AppServices
{
    public static SteamLocatorService Locator { get; } = new();
    public static SteamProcessService Process { get; } = new();
    public static PayloadService Payload { get; } = new();
    public static TomlConfigService Toml { get; } = new();
    public static LuaGameConfigService Lua { get; } = new();
    public static SteamGameLibraryService SteamGames { get; } = new();
    public static ManagerSettingsService Settings { get; } = new();
    public static GitHubTokenService GitHubToken { get; } = new();
    public static GitHubDnsService GitHubDns { get; } = new(Settings);
    public static GitHubHttpService GitHub { get; } = new(GitHubToken, GitHubDns);
    public static GitHubGamePackageService GamePackages { get; } = new(GitHub, Settings);
    public static GamePackageInstallService GameInstaller { get; } = new(GitHub);
    public static StatusService Status { get; } = new(Locator, Process, Payload, Lua);
    public static UpdateService Updates { get; } = new(GitHub);
    public static ConnectivityTestService Connectivity { get; } = new(GitHubDns, GitHub, Settings);
    public static IAppControlService AppControl { get; } = new AppControlService();
    public static IDialogService Dialogs { get; } = new DialogService();
    public static ITextPromptService Prompts { get; } = new TextPromptService();
    public static ISecretPromptService SecretPrompts { get; } = new SecretPromptService();

    private static readonly Lazy<MainViewModel> Main = new(CreateMainViewModel);

    public static MainViewModel MainViewModel => Main.Value;

    public static MainViewModel CreateMainViewModel()
        => new(Locator, Process, Payload, Toml, Lua, Status, Updates, SteamGames, GamePackages, GameInstaller, AppControl, Dialogs, Prompts, SecretPrompts, GitHubToken, Settings, Connectivity);
}
