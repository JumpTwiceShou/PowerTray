# PowerTray 安装器实际编译验证

- Task ID: `2026-07-12-1151-powertray-installer-build-validation`
- 创建时间: `2026-07-12 11:51 +09:00`
- 状态: `pending`
- 来源: `task/archive/2026/2026-07-12-0726-powertray-post-remediation-audit.md`

## 目标

在安装 Inno Setup 6 后，实际编译并验证 PowerTray 轻量版与完整运行时版安装器，消除上一轮复审中“仅完成 `.iss` 静态检查、未实际调用 `ISCC.exe`”的验证缺口。

## 工作项

- [ ] 安装或定位 Inno Setup 6，并记录 `ISCC.exe` 的版本与绝对路径。
- [ ] 执行 `build-installer.ps1`，确认 locked restore、原生 DLL 哈希校验、framework-dependent/self-contained publish 全部通过。
- [ ] 实际编译 `PowerTraySetup.exe` 与 `PowerTraySetup-full.exe`。
- [ ] 确认两个安装器均生成匹配的 `.sha256` 与 `.sha256.sig`。
- [ ] 使用固定 ECDSA P-256 公钥验证 checksum 签名，并验证安装器 SHA-256。
- [ ] 在干净或可回滚的 Windows 环境执行轻量版和完整版安装、升级覆盖、优雅关闭、卸载烟雾测试。
- [ ] 检查安装目录、启动项、设置保留、进程清理及卸载残留。
- [ ] 记录证据，更新设计记忆并归档本 task。

## 明确排除

- 不执行 24 小时 Logitech 实机热插拔、句柄或 GDI 长稳测试；用户已决定不跟踪该限制。
- 不处理自定义 `hidapi.dll` 上游来源重建。
- 不修改或删除可选 HTTP API。
- 不执行发布签名私钥的离线备份。
- 不创建公开 Release，不 push，不回复 GitHub Issue。

## 当前前置进展

- `build-installer.ps1` 已补齐 checksum 的 ECDSA P-256 `.sha256.sig` 生成逻辑。
- 正式发布私钥已建立在 Windows VM102 `%USERPROFILE%\.ssh\powertray_update_ecdsa.pem`，ACL 仅当前用户 FullControl。
- 固定公钥 SPKI SHA-256 为 `9D08127794D5D85BF45DA60C8BC631CEBFE1E2D62A51140BFB6407FFC634570A`。
- 脚本会在签名前校验私钥对应公钥指纹，拒绝使用错误密钥。
- PowerShell 语法解析、Debug/Release 构建和签名验证单元测试已通过；仍需安装 Inno Setup 后执行完整产物验证。

## 完成标准

- Inno Setup 实际编译成功，两个安装器及其 checksum/signature 产物完整。
- 安装、升级、卸载烟雾测试通过。
- 无 PowerTray/PowerTrayHID 孤儿进程，无非预期安装或卸载残留。
