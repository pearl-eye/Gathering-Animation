# Gathering Animation

## 简介

pearleye 自制的 FF14 国服采集动画插件，当前版本为 **1.3.1.0**，适用于游戏资源版本 `2026.11.03.0000.0000`、Dalamud API 15 和 .NET 10。

插件通过替换指定动画资源，缩短采矿工、园艺工和捕鱼人的动画等待时间。采矿工和园艺工支持主工具、副工具的普通采集动作；捕鱼人支持站立和坐姿钓鱼，保留鱼咬钩的提示动画。三个职业可分别开关。

附近有其他已加载玩家时可自动暂停，默认检测范围为 50 米，可调整为 1 至 200 米。检测仅覆盖游戏当前已加载的玩家。实际采集间隔仍受服务器处理和网络延迟影响。

## 安装方式

在国服卫月设置的自定义插件仓库中添加并启用：

```text
https://raw.githubusercontent.com/pearl-eye/Dalamud-Plugins-CN/main/repo.json
```

保存后打开插件安装器，搜索 **Gathering Animation** 并安装。若已启用本地开发副本，请先禁用该副本，避免重复加载同一插件。插件显示名为 `Gathering Animation`；为兼容现有配置，内部名称、程序集名和安装文件名仍使用 `Gather animation`。

已安装旧显示名版本时，需重新安装一次，现有设置继续沿用。

- `/gatheranim`：打开设置。
- `/gatheranim on`：启用总开关。
- `/gatheranim off`：关闭总开关。
- `/gatheranim status`：查看当前开关及暂停状态。

设置会自动保存。也可从 [Releases](https://github.com/pearl-eye/Gathering-Animation/releases/latest) 下载安装包。

从源码构建需要 .NET 10 SDK、PowerShell 7.2 及当前国服 Dalamud API 15 的程序集目录：

```powershell
./build.ps1 -DalamudPath "$env:APPDATA/XIVLauncherCN/addon/Hooks/dev"
```

脚本输出 `artifacts/latest.zip`、`artifacts/repo.json` 和 `artifacts/SHA256SUMS`。安装包根目录仅包含插件 DLL、清单和依赖描述，不包含卫月框架 DLL、调试文件或个人配置。

## 反馈与贡献

请在 [Issues](https://github.com/pearl-eye/Gathering-Animation/issues) 反馈问题，提供插件版本、游戏版本、涉及职业及复现步骤。提交日志前请移除角色名等个人信息。代码修改建议可通过 Issue 或 Pull Request 提交。

## 许可证与归属

版权所有 © 2026 pearleye。本仓库未提供开源许可证，也未授予额外的复制、修改或再分发许可；相关授权请联系作者。引用的 Dalamud、FFXIVClientStructs 和 Lumina 等项目分别遵循其自身许可证，安装包不附带这些框架程序集。
