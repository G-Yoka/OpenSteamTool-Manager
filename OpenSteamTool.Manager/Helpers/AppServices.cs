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
    public static GitHubGamePackageService GamePackages { get; } = new();
    public static GamePackageInstallService GameInstaller { get; } = new();
    public static StatusService Status { get; } = new(Locator, Process, Payload, Lua);
    public static UpdateService Updates { get; } = new();
    public static IAppControlService AppControl { get; } = new AppControlService();
    public static IDialogService Dialogs { get; } = new DialogService();
    public static ITextPromptService Prompts { get; } = new TextPromptService();

    private static readonly Lazy<MainViewModel> Main = new(CreateMainViewModel);

    public static MainViewModel MainViewModel => Main.Value;

    public static MainViewModel CreateMainViewModel()
        => new(Locator, Process, Payload, Toml, Lua, Status, Updates, SteamGames, GamePackages, GameInstaller, AppControl, Dialogs, Prompts);
}
