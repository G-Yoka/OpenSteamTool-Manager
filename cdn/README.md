# CDN 发布说明

这里放给 jsDelivr 直接读取的更新元数据。

## 更新流程

1. 先生成 `OpenSteamTool.Manager` 的发布 ZIP。
2. 将 ZIP 上传到 `releases/` 目录，命名示例：
   `OpenSteamTool.Manager-v0.1.3.zip`
3. 更新 `cdn/update.json` 中的版本号、资产路径和发布页地址。
4. 保留 GitHub Releases 作为兜底。

## 约定

- `assetPath` 指向仓库内可被 jsDelivr 读取的文件
- `assetUrl` 保留 GitHub Releases 直链，用作备用下载地址
- 更新检查优先读取 CDN，下载失败时自动切换到备用地址
