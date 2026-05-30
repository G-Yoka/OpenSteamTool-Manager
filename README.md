# G-OpenSteamTool

G-OpenSteamTool 是一个面向 Windows 的 OpenSteamTool 可视化管理器，使用 Rust + Tauri v2 + React 构建。它用于管理 Steam 根目录中的 OpenSteamTool DLL、`opensteamtool.toml` 设置，以及按游戏拆分的 Lua 配置文件。

> 本项目只提供桌面管理器和资源管理能力，不在应用内编译 OpenSteamTool 上游源码。

## 功能特性

- 自动检测或手动选择 Steam 根目录。
- 安装、移除、扫描 `OpenSteamTool.dll`、`dwmapi.dll`、`xinput1_4.dll`。
- 使用 SHA-256 对比应用 `dlls` 资源与 Steam 目标目录 DLL，显示一致、不一致、未安装、资源缺失。
- 创建和保存 `opensteamtool.toml`。
- 每个游戏独立管理 Lua：`G-<appid>.lua`。
- 支持禁用 Lua：`G-<appid>.lua.disabled`。
- 导入已有 Lua，并解析 `addappid`、`addtoken`、`setManifestid` 等常见 OpenSteamTool Lua 调用。
- 管理 Depot 与解密密钥。
- 查看 OpenSteamTool 日志。
- 基础 Steam 关闭与快速重启。

## 使用方法

1. 安装或运行 G-OpenSteamTool。
2. 在概览页选择 Steam 根目录，目录中应包含 `steam.exe`。
3. 点击 **初始化**，应用会安装 DLL 并写入默认 `opensteamtool.toml`。
4. 在 Lua 页面新增或导入游戏配置。
5. 重启 Steam，使 DLL 和 TOML 配置生效。

## DLL 资源说明

应用会把打包资源目录中的 DLL 复制到 Steam 根目录：

```text
<Steam>\OpenSteamTool.dll
<Steam>\dwmapi.dll
<Steam>\xinput1_4.dll
```

DLL 页面使用 SHA-256 判断文件状态：

- **一致**：资源 DLL 与 Steam 目标 DLL 完全相同。
- **不一致**：目标 DLL 存在，但 SHA-256 与资源 DLL 不同。
- **未安装**：目标 DLL 不存在。
- **资源缺失**：应用资源目录中缺少对应 DLL。

移除 DLL 时，应用只会移除由本工具安装且哈希匹配的 DLL，避免误删用户已有文件。

## Lua 管理规则

托管 Lua 文件位于：

```text
<Steam>\config\lua
```

命名规则：

- 启用：`G-<appid>.lua`
- 禁用：`G-<appid>.lua.disabled`

示例：

```lua
addappid(1962700)
addappid(1962701, 0, "depot_key_here")
addtoken(1962700, "access_token_here")
setManifestid(1962700, "manifest_gid_here")
```

导入 Lua 时，应用会尽量从文件名、注释、`addappid(...)` 和元数据中识别 AppId，并写入托管文件 `G-<appid>.lua`。

## TOML 设置

配置文件写入 Steam 根目录：

```text
<Steam>\opensteamtool.toml
```

当前界面支持快速编辑：

- 日志等级
- Manifest 来源：`wudrm` / `steamrun`
- HTTP timeout
- 额外 Lua 路径
- Pattern mirror

## 目录结构

```text
src/                         React 前端
src-tauri/                   Tauri / Rust 后端
src-tauri/resources/dlls/    打包 DLL 资源
src-tauri/tests/             Rust 集成测试
```

## 注意事项

- 应用不会在内部构建 OpenSteamTool DLL。
- Lua 管理只操作 `G-*.lua` 和 `G-*.lua.disabled`，不会主动修改普通 `.lua` 文件。
- 如果 Steam 正在运行，安装或修改 DLL/TOML 后建议重启 Steam。
- Git 仓库不提交 `node_modules/`、`dist/`、`target/`、MSI 或 exe 构建产物。

## 项目信息

- 作者：G-Yoka
- 版本：0.2.0
- 项目地址：https://github.com/G-Yoka/G-OpenSteamTool

