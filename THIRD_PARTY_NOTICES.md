# Third Party Notices

This repository contains OpenSteamTool Manager and may include binary payload files used by the manager to install OpenSteamTool into a local Steam directory.

## OpenSteamTool

- Upstream project: https://github.com/OpenSteam001/OpenSteamTool
- License: GNU General Public License v3.0
- Payload files in this repository:
  - `OpenSteamTool.Manager/Payload/OpenSteamTool.dll`
  - `OpenSteamTool.Manager/Payload/dwmapi.dll`
  - `OpenSteamTool.Manager/Payload/xinput1_4.dll`

These payload files are redistributed for the manager's install workflow. The corresponding source code for OpenSteamTool is available from the upstream repository above.

If these payload files are replaced with a modified build, the distributor should make the corresponding modified source available under the terms of GPL-3.0.

## CommunityToolkit.Mvvm

- Package: `CommunityToolkit.Mvvm`
- Version: `8.4.0`
- Project: https://github.com/CommunityToolkit/dotnet

This package is referenced by the WPF manager project through NuGet.
