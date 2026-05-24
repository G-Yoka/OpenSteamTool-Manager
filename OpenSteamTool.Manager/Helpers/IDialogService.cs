namespace OpenSteamTool.Manager.Helpers;

public interface IDialogService
{
    string? PickFolder(string description, string? initialPath = null);

    string? PickOpenFile(string title, string filter, string? initialDirectory = null);

    bool Confirm(string title, string message);

    void ShowWarning(string title, string message);

    void OpenFolder(string path);

    void OpenUrl(string url);
}