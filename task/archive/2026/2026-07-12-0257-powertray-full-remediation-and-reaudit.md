# PowerTray 全量修复与复审

- Task ID: `2026-07-12-0257-powertray-full-remediation-and-reaudit`
- 状态: `completed-with-residual-validation`
- 基线: `main` / `f7f4f1246003189bf8fd8e95901a0fb113469344`
- 用户要求: 修复原审计报告全部项目，使用 task 管理，完成后重新详细审计。
- 原始报告: `task/archive/2026/2026-07-12-0208-powertray-code-audit.md`
- 修复后报告: `task/archive/2026/2026-07-12-0726-powertray-post-remediation-audit.md`

## 范围

- `LGSTrayHID`
- `LGSTrayCore`
- `LGSTrayPrimitives`
- `LGSTrayUI`
- `PowerTray.Tests`
- `PowerTrayInstaller.iss`
- `build-installer.ps1`
- `.github/workflows/build.yml`
- NuGet lock files、SDK 固定、native binary integrity、update signing

## 排除

- 不 commit。
- 不 push。
- 不创建 Release。
- 不回复 GitHub Issue。
- 不安装 Inno Setup 或其他系统软件。

## 执行结果

### P0 HID 可靠性

- [x] 自动 Presence 不再触发全量 Rediscover。
- [x] 手动 Rediscover 使用认证 request/response 并等待真实完成。
- [x] 单次失败不离线，连续失败阈值默认 3。
- [x] offline 后相同电量恢复强制 INIT/UPDATE。
- [x] Centurion 使用相同失败阈值。
- [x] Session 增量复用。
- [x] SafeHandle 管理 HID 句柄。
- [x] Session async dispose、读线程 join、后台 Task drain。
- [x] Reopen 串行执行，旧线程退出失败时中止替换。
- [x] Rediscover/health/forced update 生命周期串行化。

### P1 监督、诊断和资源

- [x] helper supervisor 异常捕获、退避重启、状态上报。
- [x] parent watcher 优雅停止 Host。
- [x] diagnostics request/response、heartbeat、最近成功命令和错误。
- [x] Alert 状态离线/删除/迁移清理。
- [x] Icon/HICON/GDI 资源确定性释放。
- [x] ViewModel、AlertManager、device collection 事件解除和 Dispose。

### P2 安全和隐私

- [x] 诊断只导出 Logitech，ContainerId/GroupKey 哈希。
- [x] HTTP 使用不可变快照，不跨线程枚举 WPF collection。
- [x] 远程 HTTP 必须强 token，全部端点认证并显示风险提示。
- [x] updater trusted host/final redirect、随机独占 temp、双重 hash、执行锁定。
- [x] updater 独立 ECDSA P-256 checksum 签名信任链。
- [x] 私钥保存在仓库外，仓库防误提交和内容扫描。
- [x] 安装器删除 PATH bare `dotnet`。
- [x] G Hub 端口 owner/路径校验、串行重连、恢复订阅、解析诊断。
- [x] IPC 使用 nonce/timestamp/HMAC、防篡改、防重放、事件/请求域分离。
- [x] 删除 named event Rediscover/health 控制面。

### P3 协议和维护性

- [x] 排队超时与 I/O 超时分离。
- [x] HID++ function id 完整匹配。
- [x] Channel 不再 DropOldest。
- [x] 稳定身份层级和本地持久映射。
- [x] 删除同名同型号自动合并和名称迁移启发式。
- [x] UTF-8 名称按字节累计解码。
- [x] 配置范围验证、空 disabledDevices 清理、不区分大小写。
- [x] Centurion Frame 严格长度验证。
- [x] 电量边界统一 0–100。
- [x] Host 优雅退出和安装器精确路径进程停止。
- [x] runner、SDK、Actions、NuGet lock 固定。
- [x] hidapi 固定 hash 和来源边界说明。

## 修复中追加发现

- [x] 手动 Rediscover 完成信号早于设备事件的假离线竞态。
- [x] 手动请求在活跃 discovery 时提前返回成功。
- [x] Rediscover 异常被吞没。
- [x] SafeHandle 包装引用比较导致关闭句柄未等价于零。
- [x] Dispose 单次任务快照漏等任务。
- [x] Reopen 线程停止超时仍打开替代句柄。
- [x] 多设备并发恢复同一 Session。
- [x] 设备 init 失败后无法重试。
- [x] 初版 IPC token 随消息泄露。
- [x] IPC HMAC 未区分事件/请求域。
- [x] 无效 receiver serial 被哈希后误判稳定。
- [x] HTTP 请求同步 Dispatcher 的关闭风险。
- [x] 安装器无实例 `--shutdown` 误启动应用。
- [x] updater response 异常释放。
- [x] G Hub 失败后旧设备仍在线。
- [x] G Hub 自动重连后未恢复订阅。
- [x] G Hub reconnect task 未跟踪。
- [x] netstat 解析依赖英文状态。
- [x] updater checksum 和 installer 同源信任。

## 验证

- [x] `dotnet restore PowerTray.sln --locked-mode`
- [x] Debug build: 0 warning / 0 error
- [x] Release build: 0 warning / 0 error
- [x] latest-all analyzer: 0 warning / 0 error
- [x] PowerTray.Tests Release 单次通过
- [x] PowerTray.Tests Release 30/30 连续通过
- [x] NuGet vulnerable scan: 无已知漏洞
- [x] NuGet deprecated scan: 无弃用包
- [x] hidapi SHA-256 校验通过
- [x] build-installer PowerShell parser 通过
- [x] build-installer ECDSA signing helper 生成/验证通过
- [x] 私钥仓库扫描通过，未跟踪私钥
- [x] `git diff --check` 通过
- [x] Release UI/helper 启动与 `--shutdown` 优雅退出烟雾测试通过
- [x] helper 无孤儿残留

## 残余风险/验证缺口

- `hidapi.dll` 原上游 commit、hotplug patch、编译参数无法从仓库历史恢复；已固定 hash 并明确记录，不能伪造 provenance。
- 当前设备没有 Inno Setup `ISCC.exe`，`.iss` 未实际编译；脚本语法和签名 helper 已验证。
- 未进行真实 Logitech 多设备 24 小时热插拔/句柄/GDI 长稳测试。
- 显式远程 HTTP 有 token 认证但仍为明文 HTTP；默认仅 loopback。
- 无 serial/Unit ID/ContainerId 的设备跨 USB 端口无法安全自动证明同一身份。
- ECDSA 私钥需要发布者另行制作离线加密备份。

## 产物

- 修复后审计: `task/archive/2026/2026-07-12-0726-powertray-post-remediation-audit.md`
- 更新签名说明: `docs/update-signing.md`
- native provenance/integrity: `LGSTrayHID/libhidapi/readme.md`, `verify-hidapi.ps1`
- 本地设计记忆: `memory/powertray-design-019ead2d-ea09-7620-a6f2-7a45c88d7199.md`

## Git/发布

- Commit: 未执行。
- Push: 未执行。
- Release: 未执行。
- Issue 回复: 未执行。
