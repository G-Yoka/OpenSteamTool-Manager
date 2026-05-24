# OpenSteamTool Manager

OpenSteamTool Manager 是一个基于 `.NET 8 WPF` 的 Windows 桌面管理器，用于管理 Steam 根目录中的 OpenSteamTool 部署、配置、Lua 脚本和运行状态。

## 功能

- 选择并校验 Steam 根目录。
- 安装、移除和校验 `OpenSteamTool.dll`、`dwmapi.dll`、`xinput1_4.dll`。
- 使用 SHA-256 对比 Payload DLL 与 Steam 目录 DLL。
- 检查 `OpenSteamTool.dll` 是否已真实加载到 `steam.exe`。
- 显示 Steam 客户端版本、DLL 状态、TOML 状态、Lua 状态和日志状态。
- 编辑 `opensteamtool.toml`。
- 按“每游戏一个 Lua 文件”管理 `<Steam>\config\lua\ost_<appid>.lua`。
- 导入现有 Lua 文件并接管为管理器配置。
- 查看 `<Steam>\opensteamtool\*.log` 日志。
- 快速重启 Steam。

## 项目结构

```text
OpenSteamTool.Manager/
  Views/
  ViewModels/
  Models/
  Services/
  Helpers/
  Converters/
  Payload/
```

当前保持单项目结构，不拆分类库。WPF 界面使用 MVVM 组织，核心第三方依赖仅为 `CommunityToolkit.Mvvm`。

## 构建

```powershell
dotnet restore OpenSteamTool.Manager\OpenSteamTool.Manager.csproj
dotnet build OpenSteamTool.Manager\OpenSteamTool.Manager.csproj -c Release
```

构建产物位于：

```text
OpenSteamTool.Manager\bin\Release\net8.0-windows\
```

## Payload DLL

应用安装 DLL 时会读取：

```text
OpenSteamTool.Manager\Payload\
```

需要包含：

- `OpenSteamTool.dll`
- `dwmapi.dll`
- `xinput1_4.dll`

如果仓库不适合分发二进制 DLL，可以删除 Payload 中的 DLL，只保留 `Payload\README.md`，由使用者自行放入对应文件。

## 管理的 Steam 文件

- DLL：`<Steam>\OpenSteamTool.dll`
- DLL：`<Steam>\dwmapi.dll`
- DLL：`<Steam>\xinput1_4.dll`
- TOML：`<Steam>\opensteamtool.toml`
- Lua：`<Steam>\config\lua\ost_<appid>.lua`
- 日志：`<Steam>\opensteamtool\*.log`

## 许可证

本仓库按 GNU General Public License v3.0 授权，详见 [LICENSE](LICENSE)。

本项目包含并分发 OpenSteamTool 相关 DLL。OpenSteamTool 上游项目为 [OpenSteam001/OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool)，其许可证为 GPL-3.0。第三方来源、二进制 Payload 和对应源码说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 注意

- 安装或移除 DLL 前需要关闭 Steam。
- Lua 和 TOML 可以在 Steam 运行时编辑，但某些配置需要重启 Steam 后才会重新加载。
- “DLL 已加载”状态来自 `steam.exe` 当前模块列表，不等同于文件是否存在。
- “Steam 版本”优先读取 `<Steam>\logs\connection_log.txt` 中最新的 `Client version`。
