# OpenSteamTool Manager

OpenSteamTool Manager 是一个基于 `.NET 8 WPF` 的 Windows 桌面管理器，用于简化 OpenSteamTool 的部署、配置和状态查看。

它不会修改 OpenSteamTool 的 C++ Hook 逻辑，只负责管理 Steam 根目录下的 DLL、`opensteamtool.toml`、Lua 配置文件和日志。

## 功能

- 选择并校验 Steam 根目录，要求目录中存在 `steam.exe`。
- 安装或移除 OpenSteamTool 所需 DLL。
- 安装前自动备份 Steam 根目录中已有的同名 DLL。
- 移除时优先恢复备份；没有备份时只删除与内置 Payload 匹配的 DLL。
- Steam 正在运行时禁止 DLL 安装/移除，但允许编辑 Lua 和 TOML。
- 使用表单编辑 `opensteamtool.toml`。
- 按“每个游戏一个文件”的方式管理 Lua 配置。
- 支持启用/禁用单个游戏 Lua，禁用时改名为 `.lua.disabled`。
- 查看 DLL、TOML、Lua、日志和 Steam 进程状态。
- 读取 `<Steam>\opensteamtool\*.log`，支持按日志模块筛选。

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
