# OpenSteamTool Manager

OpenSteamTool Manager 是一个基于 `.NET 8 WPF` 的 Windows 桌面管理器，用来管理 Steam 根目录里的 OpenSteamTool 相关文件、Lua 配置、日志、游戏资源包，以及应用内更新。

当前版本：`v0.1.5`

## 主要功能

- 选择并校验 Steam 根目录
- 安装、移除并校验 `OpenSteamTool.dll`、`dwmapi.dll`、`xinput1_4.dll`
- 通过 SHA-256 校验 Payload 与 Steam 目录中的 DLL 是否一致
- 检查 `OpenSteamTool.dll` 是否真的已加载进 `steam.exe`
- 编辑 `opensteamtool.toml`
- 按每个游戏一个 Lua 文件管理 `ost_<appid>.lua`
- 导入现有 Lua 文件并接管为管理器配置
- 查看 `Steam\\opensteamtool\\*.log`
- 快速重启 Steam
- 在应用内检查更新、下载并替换程序
- 按 Steam AppId 从 GitHub 游戏资源仓库下载并安装资源
- 切换更新/资源来源优先级：`jsDelivr CDN` 或 `GitHub Releases`
- 测试 jsDelivr、GitHub API、GitHub Raw 的连通性

## 更新方式

管理器优先使用 `jsDelivr CDN` 获取更新元数据和游戏资源清单，GitHub 作为兜底来源。

- 更新元数据：`cdn/update.json`
- 更新包：`releases/OpenSteamTool.Manager-vX.Y.Z.zip`
- 游戏资源清单：`GameResources/manifest.json`

如果你要发布新版本，请同步更新：

- `OpenSteamTool.Manager/OpenSteamTool.Manager.csproj`
- `cdn/update.json`
- `releases/OpenSteamTool.Manager-vX.Y.Z.zip`

## 构建

```powershell
dotnet restore OpenSteamTool.Manager\OpenSteamTool.Manager.csproj
dotnet build OpenSteamTool.Manager\OpenSteamTool.Manager.csproj -c Release
```

输出目录：

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

## 许可

本仓库采用 `GPL-3.0-only` 许可证。OpenSteamTool 相关上游项目同样遵循 GPL-3.0，第三方资源与说明见 `THIRD_PARTY_NOTICES.md`。
