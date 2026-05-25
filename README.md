# OpenSteamTool Manager

OpenSteamTool Manager 是一个基于 `.NET 8 WPF` 的 Windows 桌面管理器，用于管理 Steam 根目录中的 OpenSteamTool 相关文件、Lua 配置、游戏资源和更新流程。

## 功能

- 选择并校验 Steam 根目录
- 安装、移除并校验 `OpenSteamTool.dll`、`dwmapi.dll`、`xinput1_4.dll`
- 使用 SHA-256 对比 Payload DLL 与 Steam 目录中的 DLL
- 检查 `OpenSteamTool.dll` 是否已加载到 `steam.exe`
- 显示 Steam 运行状态、版本、DLL 状态、TOML 状态、Lua 状态、日志状态
- 编辑 `opensteamtool.toml`
- 按每游戏一个 Lua 文件管理 `ost_<appid>.lua`
- 导入现有 Lua 文件并接管为管理器配置
- 查看 `Steam\opensteamtool\*.log`
- 快速重启 Steam
- 检查 GitHub Releases 更新
- 下载并应用管理器更新
- 按 Steam AppId 从 GitHub 资源仓库下载安装游戏资源

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

- `Steam\OpenSteamTool.dll`
- `Steam\dwmapi.dll`
- `Steam\xinput1_4.dll`
- `Steam\opensteamtool.toml`
- `Steam\config\lua\ost_<appid>.lua`
- `Steam\opensteamtool\*.log`

## 许可证

本仓库采用 GNU General Public License v3.0，详见 [LICENSE](LICENSE)。

本项目包含并分发与 OpenSteamTool 相关的 DLL 资源，OpenSteamTool 上游项目为 [OpenSteam001/OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool)，其许可证为 GPL-3.0。第三方来源、二进制 Payload 和对应说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 说明

- 安装或移除 DLL 前需要关闭 Steam。
- Lua 和 TOML 可以在 Steam 运行时编辑，但部分配置需要重启 Steam 后才会重新加载。
- “DLL 已加载”状态来自 `steam.exe` 当前模块列表，不等同于文件是否存在。
- “Steam 版本”优先读取 `Steam\logs\connection_log.txt` 中最新的 `Client version`。
