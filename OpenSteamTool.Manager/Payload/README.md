# OpenSteamTool Manager Payload

Place release payload DLLs here before publishing the manager:

- `OpenSteamTool.dll`
- `dwmapi.dll`
- `xinput1_4.dll`

The WPF project copies `Payload\*.dll` to the output directory. If these files are missing, the UI shows `Payload 缺失` and install is blocked.

OpenSteamTool is licensed under GPL-3.0. If you redistribute these payload binaries, keep the repository `LICENSE` and `THIRD_PARTY_NOTICES.md` with the distribution and provide the corresponding source code as required by GPL-3.0.
