using System.Diagnostics;
using System.IO;
using System.Windows;

namespace OpenSteamTool.Manager.Helpers;

public sealed class DialogService : IDialogService
{
    public string? PickFolder(string description, string? initialPath = null)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            SelectedPath = string.IsNullOrWhiteSpace(initialPath) || !Directory.Exists(initialPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                : initialPath
        };

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    public string? PickOpenFile(string title, string filter, string? initialDirectory = null)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory) || !Directory.Exists(initialDirectory)
                ? Environment.CurrentDirectory
                : initialDirectory
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string message)
        => System.Windows.MessageBox.Show(GetOwner(), message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowWarning(string title, string message)
        => System.Windows.MessageBox.Show(GetOwner(), message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void OpenFolder(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true
        });
    }

    private static Window? GetOwner()
        => System.Windows.Application.Current?.MainWindow;
}
