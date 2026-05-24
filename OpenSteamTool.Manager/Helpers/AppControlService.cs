using System.Windows;

namespace OpenSteamTool.Manager.Helpers;

public sealed class AppControlService : IAppControlService
{
    public void Shutdown()
        => System.Windows.Application.Current?.Shutdown();
}
