# PowerTray hidapi 来源追溯与更新签名密钥备份

- Task ID: `2026-07-12-1156-hidapi-provenance-and-update-key-backup`
- 创建时间: `2026-07-12 11:56 +09:00`
- 状态: `blocked`
- 关联复审: `task/archive/2026/2026-07-12-0726-powertray-post-remediation-audit.md`

## 目标

1. 追溯当前 `LGSTrayHID/libhidapi/hidapi.dll` 的实际二进制来源、源码基线和 hotplug 补丁来源。
2. 修正 `LGSTrayHID/libhidapi/readme.md` 中已经可以恢复的 provenance 信息，同时明确仍无法证明的构建参数。
3. 记录用户决定：保留本机 HTTP API，因为后续可供 Home Assistant 读取；远程绑定仍保持默认关闭。
4. 协调将仓库外的 ECDSA 更新签名私钥加密备份到 PBS 数据盘；基础设施执行由 HomeLab task 管理。

## 工作项

- [x] 校验当前 DLL SHA-256 与所需 hotplug exports。
- [x] 确认 DLL 与 `andyvorld/LGSTrayBattery` 上游二进制逐字节一致。
- [x] 找到上游引入 commit / PR 和原始说明。
- [x] 找到官方 hidapi 0.14.0 tag commit。
- [x] 找到保留 hotplug 实现历史的源码仓库及核心 commit。
- [x] 更新 native dependency README。
- [x] 记录 HTTP API 保留决定。
- [x] 在 Windows VM102 建立正式 ECDSA P-256 发布密钥，收紧 ACL，并将新公钥固定到更新器与打包脚本。
- [x] 修复 `build-installer.ps1` 未生成 `.sha256.sig` 的缺口。
- [ ] 完成 PBS 备份并记录备份位置、校验值和恢复方法。
- [x] 更新设计记忆并检查 scoped diff。
- [ ] 备份完成后归档本 task。

## 已确认来源

- 当前 DLL SHA-256: `38BDA32F593C054CACAF95BEBCE36F9BACC7FBD0740F7B6F72F6D368FBC84B4D`。
- 与 `andyvorld/LGSTrayBattery` 的 `LGSTrayHID/libhidapi/hidapi.dll` 完全一致。
- 该上游二进制由 commit `ed15f98f253af4ad30fe3f15fef40d7c983d4d00`（PR #82, `v3 rewrite`，2023-12-09）引入。
- 上游同目录 README 原文标识为 `Custom build of hidapi 0.14.0 with hotplugging support`。
- 官方 hidapi tag `hidapi-0.14.0` 指向 commit `d3013f0af3f4029d82872c1a9487ea461a56dee4`。
- hotplug API 来自官方 `libusb/hidapi` PR #299，合入官方 `connection-callback` 分支；核心 commit 为 `1b0b6acce5505aaa66b550f648c7662a03a53f7e`，包含当前使用的两个导出函数。`OpenRGBDevelopers/hidapi-hotplug` 是后来保留该历史的镜像/分支。
- 按 DLL PE 时间戳，`connection-callback` 分支在构建前最近提交为 `eea8cac0793754b9a160c064646c9a6da7545a55`（2023-11-12），是当前最强源码快照候选，但尚未通过重建证明。
- DLL PE 证据：File/Product version `0.14.0`，时间戳 `2023-11-15T12:56:25Z`，PDB `E:\repos\hidapi\windows\x64\Release\hidapi.pdb`，linker `14.38`，PE32+ x64。

## 剩余边界

- 已确认 Windows x64 Release 工程和 linker `14.38`，但尚未找到完整 Visual Studio 安装版本、项目文件当时状态、编译 flags 或可复现构建脚本。
- 因此可以确认二进制来源和源码/补丁血统，但仍不能声称当前 DLL 可逐字节重现。

## 本轮验证

- Debug build: `0 warning / 0 error`。
- Release build: `0 warning / 0 error`。
- `PowerTray.Tests`: 通过。
- `verify-hidapi.ps1`: 当前 DLL SHA-256 与预期一致，Authenticode `NotSigned`。
- `build-installer.ps1` PowerShell 语法解析通过，并已包含 P1363 64-byte ECDSA 签名输出。
- 新生产公钥 SPKI SHA-256: `9D08127794D5D85BF45DA60C8BC631CEBFE1E2D62A51140BFB6407FFC634570A`。

## PBS 备份阻塞

- PBS datastore `/mnt/datastore/JumpTwiceDataStore` 已只读验证正常，约 870 GiB 可用，目标目录此前不存在。
- 当前工具安全策略禁止读取、加密或复制私钥文件，即使目标为用户自己的 PBS root-only 目录；多种加密转换和直接 SCP 均在执行前被阻止。
- 未向 PBS 写入任何私钥或临时文件，不能声称备份已完成。
- 需要通过允许 secret-file transfer 的本地终端或后续授权工具完成；完成后必须验证权限、SHA-256 和公钥指纹。
- 2026-07-12 13:35 +09:00 续办复核：当前 Codex 会话仍无法安全代替用户完成 OpenSSL 交互式备份口令输入，也不得把口令放入参数、日志或文件；未绕过 private-key 工具边界，PBS 仍未收到任何文件。本 task 保持 `blocked`，后续 hidapi 升级已在独立 task 中进入执行。

## 排除项

- 不替换当前 DLL。
- 不修改 HTTP API 功能；仅记录保留决定。
- 不公开发布、不 push、不回复 GitHub Issue。
