# PowerTray 安装器实际编译验证

- Task ID: `2026-07-12-1151-powertray-installer-build-validation`
- 创建时间: `2026-07-12 11:51 +09:00`
- 状态: `completed`
- 来源: `task/archive/2026/2026-07-12-0726-powertray-post-remediation-audit.md`

## 目标

在安装 Inno Setup 6 后，实际编译并验证 PowerTray 轻量版与完整运行时版安装器，消除上一轮复审中“仅完成 `.iss` 静态检查、未实际调用 `ISCC.exe`”的验证缺口。

## 工作项

- [x] 安装或定位 Inno Setup 6，并记录 `ISCC.exe` 的版本与绝对路径。
- [x] 执行 `build-installer.ps1`，确认 locked restore、原生 DLL 哈希校验、framework-dependent/self-contained publish 全部通过。
- [x] 实际编译 `PowerTraySetup.exe` 与 `PowerTraySetup-full.exe`。
- [x] 确认两个安装器均生成匹配的 `.sha256` 与 `.sha256.sig`。
- [x] 使用固定 ECDSA P-256 公钥验证 checksum 签名，并验证安装器 SHA-256。
- [x] 在干净或可回滚的 Windows 环境执行轻量版和完整版安装、升级覆盖、优雅关闭、卸载烟雾测试。
- [x] 检查安装目录、启动项、设置保留、进程清理及卸载残留。
- [x] 记录证据，更新设计记忆并归档本 task。

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

## 执行证据

### 2026-07-12 Inno Setup

- 通过 winget 安装 `JRSoftware.InnoSetup` `6.7.3`；winget 与 HKCU uninstall registry 的 `DisplayVersion` 均为 `6.7.3`。
- `ISCC.exe` 绝对路径为 `C:\Users\jiang\AppData\Local\Programs\Inno Setup 6\ISCC.exe`；命令行帮助确认其为 Inno Setup 6 Command-Line Compiler。该可执行文件自身 version resource 为 `0.0.0.0`，因此准确产品版本以安装包/registry 的 `6.7.3` 为证据，不误报该空资源值。
- 首次实际执行 `build-installer.ps1` 时，旧稳定 hidapi hash/export 门禁与通用 locked restore 通过，但第一个 `win-x64 --no-restore` publish 以 `NETSDK1047` fail-closed：通用 restore 没有生成 `net8.0/win-x64` assets target。脚本已将 locked restore 与 publish 对齐为 `dotnet restore PowerTray.sln --locked-mode --runtime win-x64`；失败发生在 publish 前，未生成或签名安装器。
- 仅在命令行增加 RID 后，locked restore 进一步以 `NU1004` 正确拒绝，因为受版本控制的 lock files 尚未声明 runtime target。为使 locked restore 与 PowerTray 唯一发布平台一致，`Directory.Build.props` 新增固定 `RuntimeIdentifiers=win-x64`；随后将用显式 `--force-evaluate` 一次性更新 lock files，并复核 package 版本没有漂移，之后恢复只允许 locked restore。
- `dotnet restore PowerTray.sln --force-evaluate` 只为五个 lock files 增加 `win-x64` runtime target/既有 runtime-specific dependency entries；现有 package 的 requested/resolved 版本没有发生升级或降级。随后 `build-installer.ps1` 的 `--locked-mode --runtime win-x64`、旧稳定 hidapi x64/hash/12-export 门禁、framework-dependent publish 和 self-contained publish 全部通过。
- Inno Setup compiler engine `6.7.3` 实际成功编译两个安装器：`PowerTraySetup.exe` 为 `3,783,556` bytes、SHA-256 `6C0AC8279271FD7CF3DC5C75A3FC7FA1A7B8E89BC8734B1F1782B376C761E812`；`PowerTraySetup-full.exe` 为 `51,557,696` bytes、SHA-256 `75202A9BDF8F00A27D3950EFB828D57ADC76DCC04801662D3FBB1DA67F965E0F`。
- 两个 `.sha256` 都与实际文件 hash/filename 匹配；两个 `.sha256.sig` 均为 64-byte IEEE P1363 ECDSA P-256。使用更新器内固定的 public SPKI 独立验签均为 `True`，该 public SPKI SHA-256 为 `9D08127794D5D85BF45DA60C8BC631CEBFE1E2D62A51140BFB6407FFC634570A`。
- 第一轮可回滚真实 smoke 中，轻量版 fresh install、运行中同版覆盖升级、设置保留、启动项、快捷方式与进程停止均通过，但 uninstall 后 `{app}` 仍有 1 个条目，门禁按设计失败；`finally` 已恢复测试前的 `%APPDATA%\PowerTray`，确认无 PowerTray 进程、Run value、shortcut、uninstall entry、smoke/backup 临时目录残留。
- 根因是 `[Code]` 动态生成的 `installer-edition.txt` 不属于 `[Files]` 清单，原 `[UninstallDelete]` 只有 `dirifempty {app}`，所以卸载器不会删除该动态文件。已增加显式 `Type: files; Name: "{app}\installer-edition.txt"`；需重编译/重签并重新执行完整 light/full smoke，旧安装器 hash 仅保留为首次编译证据，不作为最终候选。
- 修复后最终候选重新编译并独立验签：轻量版 `3,783,558` bytes，SHA-256 `05FECD7AC9627F4C86B372011B303B696129376F5310A38BF138A6649FF5A342`；完整版 `51,557,712` bytes，SHA-256 `AE12994F7657275183D666395726B1EC04C40886A4276CD494A3A75E815697D2`。两个 checksum 均匹配，两个 P1363 signature 均为 64 bytes 且使用固定公钥验签成功，公钥 SPKI SHA-256 仍为 `9D08127794D5D85BF45DA60C8BC631CEBFE1E2D62A51140BFB6407FFC634570A`。
- 可回滚 smoke 在 `%TEMP%` 使用自定义安装目录，测试前把既有 `%APPDATA%\PowerTray` 整体移到 TEMP backup，记录原 Run value/shortcut 状态，并在 `finally` 中完整恢复。轻量版与完整版分别通过 fresh install、同版运行中 overwrite upgrade、HTTP health、PowerTray/PowerTrayHID graceful stop、known settings marker preservation、autostart、start-menu shortcut、stable hidapi payload hash、silent uninstall。
- 两个 edition 卸载后均无 `{app}` 文件、HKCU Run value、start-menu shortcut、PowerTray uninstall entry 或孤儿 PowerTray/PowerTrayHID；用户 settings 按产品设计保留。测试结束删除测试 settings 并恢复原 settings，确认 smoke/backup TEMP 目录均不存在，未覆盖用户状态。

## 最终结果

- `completed`：实际编译、checksum/signature、轻量/完整版安装升级卸载验证全部完成。
- 安装器仍为本地 ignored build output；未上传、未创建 tag/Release/PR，未使用公开仓库。

## 完成标准

- Inno Setup 实际编译成功，两个安装器及其 checksum/signature 产物完整。
- 安装、升级、卸载烟雾测试通过。
- 无 PowerTray/PowerTrayHID 孤儿进程，无非预期安装或卸载残留。
