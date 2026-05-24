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
    public static UpdateService Updates { get; } = new("G-Yoka", "OpenSteamTool-Manager");
    public static StatusService Status { get; } = new(Locator, Process, Payload, Lua);
    public static IDialogService Dialogs { get; } = new DialogService();
    public static ITextPromptService Prompts { get; } = new TextPromptService();

    private static readonly Lazy<MainViewModel> Main = new(CreateMainViewModel);

    public static MainViewModel MainViewModel => Main.Value;

    public static MainViewModel CreateMainViewModel()
        => new(Locator, Process, Payload, Toml, Lua, Status, Updates, Dialogs, Prompts);
}