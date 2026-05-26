# OpenSteamTool Manager

OpenSteamTool Manager 是一个基于 `.NET 8 WPF` 的 Windows 桌面管理器，用于管理 Steam 根目录中的 OpenSteamTool 相关文件、Lua 配置、日志、游戏资源包以及应用内更新。

当前版本：`v0.1.6`

## 主要功能

- 选择并校验 Steam 根目录
- 安装、移除并校验 `OpenSteamTool.dll`、`dwmapi.dll`、`xinput1_4.dll`
- 通过 SHA-256 校验 Payload 与 Steam 目录中的 DLL 是否一致
- 检查 `OpenSteamTool.dll` 是否真的已加载进 `steam.exe`
- 编辑 `opensteamtool.toml`
- 按 AppId 管理单个游戏的 Lua 配置
- 导入已有 Lua 文件并接管为管理器配置
- 查看 `Steam\opensteamtool\*.log`
- 管理游戏资源清单源，并按 AppId 安装游戏资源包
- 通过 GitHub Releases 进行更新检查与下载更新
- 使用应用内 GitHub DNS 优化，仅对 GitHub 相关域名启用 DoH 解析

## 网络与资源

- 更新检查使用 GitHub Releases API
- 游戏资源支持多个清单源，按启用顺序合并
- 自定义清单源支持 GitHub `blob` / `raw` 地址自动规范化
- 如果 GitHub 网络不稳定，可在设置中启用 GitHub DNS 优化

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
  Assets/
```

## 许可

本仓库采用 `GPL-3.0-only` 许可证。OpenSteamTool 相关上游项目同样遵循 GPL-3.0，第三方资源说明见仓库内相关文件。
