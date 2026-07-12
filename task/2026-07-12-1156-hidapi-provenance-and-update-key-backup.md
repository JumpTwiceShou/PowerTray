# PowerTray hidapi 来源追溯与更新签名密钥 Infisical 备份

- Task ID: `2026-07-12-1156-hidapi-provenance-and-update-key-backup`
- 创建时间: `2026-07-12 11:56 +09:00`
- 状态: `blocked`
- 关联复审: `task/archive/2026/2026-07-12-0726-powertray-post-remediation-audit.md`

## 目标

1. 追溯当前 `LGSTrayHID/libhidapi/hidapi.dll` 的实际二进制来源、源码基线和 hotplug 补丁来源。
2. 修正 `LGSTrayHID/libhidapi/readme.md` 中已经可以恢复的 provenance 信息，同时明确仍无法证明的构建参数。
3. 记录用户决定：保留本机 HTTP API，因为后续可供 Home Assistant 读取；远程绑定仍保持默认关闭。
4. 协调将仓库外的 ECDSA 更新签名私钥备份到 Infisical `/projects/logi`；基础设施执行由 HomeLab task 管理。三台开发机器均为用户确认的可信机器，允许通过现有 env 同步流程分发到各自 Git-ignored `.env.local`。

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
- [x] 在 `.env.example` 增加 `POWERTRAY_UPDATE_ECDSA_PEM_B64` 空变量名。
- [ ] 完成 Infisical 备份并记录 secret 名、路径、指纹校验和恢复方法。
- [x] 更新设计记忆并检查 scoped diff。
- [ ] Infisical 备份完成后归档本 task。

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

## Infisical 备份方案

- 用户于 2026-07-12 明确确认当前 Windows 物理机、Windows VM102 和 Ubuntu VM101 均为可信机器，因此允许 PowerTray 项目 env 通过既有同步流程分发到三台机器。
- Infisical 路径使用 `/projects/logi`，secret 名使用 `POWERTRAY_UPDATE_ECDSA_PEM_B64`；采用单行 Base64 是为了避免 PEM 多行内容在 dotenv 导出和跨平台同步时发生换行或转义损坏，不代表额外加密。
- 现有项目清单会合并 `/shared/common` 与 `/projects/logi` 并导出到 Git-ignored `.env.local`。恢复时仅在需要签名或校验的机器上解码到临时文件，收紧 ACL/权限，验证公钥 SPKI SHA-256 后使用。
- Infisical VM103 已有每日 PostgreSQL dump，并在随后执行 PBS VM 快照，因此该 secret 会进入现有 Infisical 数据库与整机 PBS 备份链，不再建立独立 PBS PKCS#8 文件备份。
- 旧 PBS 方案未向 datastore 写入任何私钥、密文或临时文件，因此无需清理或迁移旧备份。
- 2026-07-12 续办时确认源私钥存在、大小 227 bytes，ACL 仅 `WINDOWSVM\\jiang` FullControl，Infisical CLI 可用；未读取或打印私钥内容。
- 尝试通过临时 dotenv 文件执行 `push-project-env.ps1` dry-run 时，命令在读取私钥并生成 Base64 之前被 OpenAI/DevSpace 安全检查直接阻止。未创建临时文件、未写入 `.env.local`、未调用 Infisical、未产生远端 secret。
- 当前会话不得绕过 private-key 读取限制；需要在允许 secret-file 操作的本地终端执行同一既有脚本流程后，再完成三机 env 导出与公钥指纹验证。本 task 保持 blocked。

## 排除项

- 不替换当前 DLL。
- 不修改 HTTP API 功能；仅记录保留决定。
- 不绕过工具对 private-key 读取和 secret-file transfer 的安全限制。
- 不公开发布、不 push、不回复 GitHub Issue。
