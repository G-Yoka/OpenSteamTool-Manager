# OpenSteamTool Manager

OpenSteamTool Manager 是一个基于 `.NET 8 WPF` 的 Windows 桌面管理器，用来管理 Steam 根目录中的 OpenSteamTool 相关文件、Lua 配置、日志、游戏资源包，以及应用自身更新。

当前版本：`v0.1.4`

## 主要功能

- 选择并校验 Steam 根目录
- 安装、移除并校验 `OpenSteamTool.dll`、`dwmapi.dll`、`xinput1_4.dll`
- 通过 SHA-256 校验 Payload 与 Steam 目录中的 DLL 是否一致
- 检查 `OpenSteamTool.dll` 是否已真正加载进 `steam.exe`
- 编辑 `opensteamtool.toml`
- 按每个游戏一个 Lua 文件管理 `ost_<appid>.lua`
- 导入现有 Lua 文件并接管到管理器
- 查看 `Steam\opensteamtool\*.log`
- 快速重启 Steam
- 检查更新并在应用内完成更新
- 按 Steam AppId 从 GitHub 游戏资源仓库下载并安装资源

## CDN 更新

为减少 GitHub 网络问题带来的影响，更新与资源下载优先走 jsDelivr CDN：

- 更新检查优先读取 `cdn/update.json`
- 更新包优先从 `releases/` 目录的 CDN 地址下载
- 游戏资源清单优先读取 `GameResources` 仓库的 CDN 地址
- CDN 不可用时自动回退到 GitHub 原始地址或 Releases 兜底

如果要发布一个可被 CDN 直接读取的更新包，请同步更新：

- `cdn/update.json`
- `releases/OpenSteamTool.Manager-vX.Y.Z.zip`

## 构建

```powershell
dotnet restore OpenSteamTool.Manager\OpenSteamTool.Manager.csproj
dotnet build OpenSteamTool.Manager\OpenSteamTool.Manager.csproj -c Release
```

构建输出位于：

```text
OpenSteamTool.Manager\bin\Release\net8.0-windows\
```

## 目录结构

```text
OpenSteamTool.Manager/
  Views/
  ViewModels/
  Models/
  Services/
  Helpers/
  Converters/
  Payload/
  cdn/
  releases/
```

## 许可证

本仓库采用 `GPL-3.0-only` 许可证。OpenSteamTool 相关上游项目同样遵循 GPL-3.0，第三方资源与说明见 `THIRD_PARTY_NOTICES.md`。
