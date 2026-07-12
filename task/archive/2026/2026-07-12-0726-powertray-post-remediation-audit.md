# PowerTray 全量修复后代码与安全复审报告

- Audit ID: `2026-07-12-0726-powertray-post-remediation-audit`
- 日期: `2026-07-12 +09:00`
- 仓库: `C:\dev\repos\logi\LGSTrayBattery-master`
- 基线: `main` / `f7f4f1246003189bf8fd8e95901a0fb113469344`
- 审计对象: 基线之上的未提交修复工作树
- 原始报告: `task/archive/2026/2026-07-12-0208-powertray-code-audit.md`
- 结论: 原报告中的业务代码缺陷均已修复或以更严格方案替代；未发现默认配置下新的 Critical/High 漏洞。保留 5 项明确的验证或环境残余风险。

## 总体结论

本轮不只是按原报告逐项修改，还对修复后的并发、生命周期、IPC、更新信任链、安装器和恢复路径重新审计。复审过程中发现并修复了多项初版修复自身的边界问题，包括：手动重发现完成竞态、SafeHandle 失效比较、读线程关闭超时仍继续替换、IPC 明文会话令牌、可预测 named event 控制面、Rediscover 异常吞没、HTTP 请求线程同步进入 WPF Dispatcher、安装器 `--shutdown` 在无实例时误启动应用、G Hub 重连后未恢复订阅等。

当前默认配置仍为本机 HTTP、原生 HID 后端和无远程写接口。Debug/Release 构建、latest-all analyzer、连续测试、依赖审计和运行时启动/关闭验证均通过。

# 原始发现逐项复审

## H1 自动 Presence Check 高频重建 HID Session

状态: **已修复**

- UI 不再每 30 秒调用全量 Rediscover。
- 自动 Presence 使用认证 IPC 发起轻量 Ping。
- 完整 Rediscover 仅用于启动、热插拔、手动请求或受控恢复。
- 手动 Rediscover 使用 requestId/response，等待真实完成结果，不再固定等待 12 秒。
- `HidppManagerContext` 按 Session 配置键复用未变化端点，只创建新增 Session、关闭消失 Session。

复审加固:

- 删除 UI 的 presence epoch/时间窗口猜测，离线状态完全由后端明确结果驱动。
- 手动请求在发现锁忙时等待自己的发现轮次，不再排队后提前回复成功。
- HID 手动刷新强制发布在线状态；G Hub 设备列表刷新会对缺失设备发送 OFFLINE。

## H2 单次通信失败立即离线，恢复事件被抑制

状态: **已修复**

- 默认连续失败阈值为 3，配置被限制在 2–10。
- 单次 Ping 或电量读取失败仅记录通信退化。
- 达到阈值后才发送 OFFLINE，并触发受控端点恢复。
- 从 offline 恢复时即使电量值完全相同，也强制发送 INIT/UPDATE。
- Centurion 路径采用相同的连续失败策略。
- 电量协议边界统一验证有限数值和 0–100 范围。

## H3 HID Session/句柄生命周期竞态

状态: **已修复**

- 原生句柄由 `SafeHidDeviceHandle` 管理，不再保存永久裸指针关闭集合。
- `HidppDevices` 实现 `IAsyncDisposable`。
- Read Thread、设备初始化任务、轮询任务和其他后台任务均被跟踪。
- Dispose 顺序为取消生命周期、完成 Channel、关闭句柄、等待读线程、等待所有后台任务、再释放 CTS/Semaphore。
- Reopen 在统一生命周期锁中执行关闭旧端点、等待旧读线程、打开新端点、启动新线程。
- 旧读线程在超时内未退出时，重开立即中止，不创建替代句柄。
- Rediscover 检测 Session 读线程关闭超时并中止同轮替换。
- 健康检查、强制电量更新和 Rediscover 使用同一管理器串行锁，避免 Dispose 与命令交叉。
- 新 Session 启动失败时从活动集合移除并完整清理。

## M1 HID helper supervisor 静默停止或孤儿进程

状态: **已修复**

- Start、Wait、Kill、ExitCode 全路径捕获和记录异常。
- 快速失败采用指数退避，不永久静默放弃。
- UI 维护 helper 状态、PID、heartbeat、最近成功命令、最近错误和重启次数。
- parent watcher 异常会请求 Host 停止，不再直接 `Environment.Exit`。
- 运行时烟雾测试确认主进程和 helper 可启动，`--shutdown` 后二者均退出，无孤儿 helper。

## M2 HID 命令匹配和排队假超时

状态: **已修复**

- 命令排队超时与设备 I/O 超时分离。
- HID++ 2.0 响应匹配 device index、feature index、function id 和 software id。
- 命令前清除明确的陈旧响应并记录数量。
- Channel 改为有界 256、`Wait` 模式，不再静默 `DropOldest`。
- Channel 拒绝写入时记录诊断错误。

## M3 Fallback 身份不稳定和同型号误合并

状态: **已修复，存在不可消除的硬件信息限制**

身份层级:

1. 有效设备序列号。
2. 有效 Unit ID。
3. 有效接收器标识（原始序列号校验后哈希，或 Windows ContainerId）+ 配对槽位。
4. 本机持久化、不透明随机 ID 映射。

其他修复:

- 全 0/全 F 接收器序列号不会被哈希后误当稳定 ID。
- model-id-only 不再作为唯一 ID。
- UI 删除同名同型号自动合并。
- 删除按设备名称迁移历史配置的危险启发式入口。
- 两个相同型号、不同配对槽位设备得到不同 ID。

## M4 诊断请求未实现

状态: **已修复**

- 实现真实 diagnostics request/response 和 requestId 匹配。
- 诊断包含 helper heartbeat、状态、PID、最后成功命令、最后错误和重启次数。
- HTTP server 状态也写入诊断。
- 超时请求被清理，不复用陈旧广播作为任意请求响应。

## M5 诊断包过度收集和明文标识

状态: **已修复**

- 仅导出 Logitech VID 端点。
- `ContainerId` 和 `GroupKey` 仅导出哈希。
- 非 Logitech 键盘、安全密钥、控制器等不会进入诊断包。
- 诊断 README 已明确字段和隐私范围。
- 测试验证无明文 ContainerId、无其他厂商设备信息。

## M6 离线设备幽灵告警

状态: **已修复**

- 离线立即清除 blinking 和通知循环状态。
- 删除设备时清理 runtime/alert state。
- 显式 ID 迁移支持状态迁移。
- 删除未使用的 `NotificationPending` 设计，改为明确 suppressed 状态。
- AlertManager 可 Dispose，解除设置和集合事件订阅。

## M7 HTTP 跨线程访问与远程无认证

状态: **已修复；显式远程模式仍为明文 HTTP**

- WPF UI 事件更新不可变设备快照。
- HTTP 请求线程只读取快照，不枚举 `ObservableCollection`，也不同步进入 Dispatcher。
- 非回环绑定必须同时满足 `AllowRemote=true` 和至少 32 字符访问令牌。
- 支持 Bearer 和 `X-PowerTray-Token`，使用固定时间比较。
- 所有端点包括 `/health` 都执行认证。
- IPv6 地址正确使用括号格式。
- 远程模式启动时显示明确风险通知。
- server 异常采用退避重启并记录健康状态。

## M8 更新器信任链和 TOCTOU

状态: **已修复**

- 更新检查使用 SemaphoreSlim 串行化。
- Release API、installer、checksum、signature 和最终 redirect URL 均限制为可信 GitHub HTTPS host。
- 安装器下载到随机独占临时文件。
- 校验后移动至目标路径。
- 执行前再次计算 SHA-256，并在启动期间以限制共享模式保持文件打开。
- SHA-256 文件必须通过内置 ECDSA P-256 公钥验证。
- Release 必须同时提供 `.exe`、`.sha256` 和原始 64-byte `.sha256.sig`。
- 私钥仅保存在开发机 `%USERPROFILE%\.ssh\powertray_update_ecdsa.pem`，未进入仓库。
- 测试覆盖合法签名、修改后的 checksum、修改后的 installer 和不可信 URL。

信任链结果:

- GitHub 仓库或 Release 账号单独被入侵时，攻击者无法生成通过验证的 checksum 签名。
- 私钥轮换和丢失处理已记录在 `docs/update-signing.md`。

## M9 安装器通过 PATH 查找 dotnet

状态: **已修复**

- 仅检查注册表、Program Files 和用户本地可信绝对路径。
- 删除 bare `dotnet` fallback。
- 找不到可信运行时时按缺失处理。

## M10 托盘 Icon/GDI 资源累积

状态: **已修复**

- 新 Icon 创建后显式销毁原始 HICON。
- 更新 TaskbarIcon 后显式 Dispose 旧 Icon。
- StringFormat、注册表键和绘图对象均使用确定性释放。
- ViewModel Dispose 时释放 TaskbarIcon。

## M11 G Hub 本地服务冒充和重连竞态

状态: **已修复**

- 连接前和重连后验证 9010 监听进程 PID、进程名和安装路径。
- 仅接受受信任 `LGHUB_agent`。
- netstat 解析不依赖本地化的 `LISTENING` 文本，使用监听远端零端点判断。
- 连接/重连通过 SemaphoreSlim 串行化。
- WebSocket 非初始重连后重新验证 owner、恢复订阅并重新加载设备列表。
- 重连恢复任务被跟踪，停止时等待。
- JSON 和 Battery 解析异常进入诊断，不再静默吞掉。
- 连接失败、owner 变化或列表缺失会明确将已知设备标记离线。

# 低优先级发现复审

## L1 UTF-8 名称读取

状态: **已修复**。按协议字节偏移累计完整 byte 数组后统一 UTF-8 Decode。

## L2 配置数值和 disabledDevices

状态: **已修复**。Poll、Retry、Presence、失败阈值均限制范围；空过滤项被删除；匹配不区分大小写并去重。

## L3 后台 Task 异常观察

状态: **已修复**。HID、helper、IPC request、G Hub reconnect、UI presence/update/manual rediscover 和 deferred offline 均跟踪、记录或等待。

## L4 IPC 可预测名称、无应用认证

状态: **已修复为应用层认证**。

- 随机 256-bit 会话密钥只通过父子进程环境传递，不随 IPC 消息发送。
- 每条事件和请求使用随机 nonce、时间戳和 HMAC-SHA256。
- HMAC 显式区分事件域和请求域，并绑定消息类型和完整 MessagePack payload。
- 固定时间比较，拒绝过期消息和 nonce 重放。
- diagnostics、battery update、rediscover 和 health control 均迁移到认证 IPC。
- 删除可预测 named event 控制面。

说明: Windows 同用户且已具备读取其他进程内存/环境或替换本地二进制能力的恶意进程仍不属于可由应用层协议完全隔离的安全边界。

## L5 Centurion Frame 长度

状态: **已修复**。超长 BuildFrame 直接抛错；声明长度与实际帧不一致直接拒绝。

## L6 ViewModel 事件订阅

状态: **已修复**。ViewModel、AlertManager 和 LogiDeviceCollection 均解除订阅并释放持有资源。

## L7 退出方式

状态: **已修复**。菜单退出和安装器关闭请求 Host 正常停止；不再使用 `Environment.Exit`。安装器 `--shutdown` 在没有现有实例时直接退出，不会误启动常驻应用。

## L8 构建供应链

状态: **部分修复，原生 DLL 来源历史无法追溯**。

已完成:

- 固定 `windows-2022`。
- 固定 .NET SDK `8.0.422`。
- GitHub Actions 固定到提交 SHA。
- 生成并强制使用 `packages.lock.json`。
- CI locked restore。
- `hidapi.dll` 固定 SHA-256 并在 CI/打包前校验。
- 打包脚本使用 locked restore 和 `--no-restore` publish。
- 更新发布物使用独立 ECDSA 签名。

无法从现有仓库历史恢复:

- 自定义 `hidapi.dll` 的上游 source commit。
- hotplug patch 源码。
- 原始编译器、CMake 参数和可复现构建脚本。

这部分已在 `LGSTrayHID/libhidapi/readme.md` 明确记录，未伪造来源信息。

## L9 安装器按进程名 taskkill

状态: **已修复**。先通过 `--shutdown` 优雅退出；兜底仅终止路径严格等于 `{app}` 下可执行文件的进程。

## L10 电量范围

状态: **已修复**。HID++、Centurion、G Hub 和 UI 边界均拒绝非有限或越界值。

# 修复后新发现并已处理的问题

1. 手动 Rediscover 响应可能早于设备事件，造成 UI 假离线。
2. Rediscover 正在运行时，手动请求排队但提前返回成功。
3. Rediscover 内部异常被记录后吞掉，调用端无法知道失败。
4. SafeHandle 包装对象按引用比较，已关闭句柄未等价于零句柄。
5. Session Dispose 只取一次后台任务快照，可能漏等关闭过程中新增的 tracked task。
6. Reopen 读线程停止超时后仍继续打开替代端点。
7. 不同设备同时失败可能并发 Reopen 同一 Session。
8. 健康探测/强制更新可能和 Rediscover Dispose 同时运行。
9. 设备初始化异常后对象残留，后续发现无法重试。
10. 初版 IPC 将会话 token 放入每条消息，可被订阅者观察。
11. 可预测 named event 仍可触发 Rediscover/health 控制动作。
12. IPC HMAC 未区分事件/请求域。
13. 无效接收器序列号先哈希后被误认为稳定标识。
14. HTTP 请求线程仍同步进入 WPF Dispatcher，关闭阶段存在阻塞风险。
15. 安装器在无运行实例时执行 `--shutdown` 会误启动新实例。
16. updater HTTP 非成功状态异常路径未总是 Dispose response。
17. G Hub owner/连接失败后旧设备可能继续显示在线。
18. G Hub WebSocket 自动重连后未恢复订阅。
19. G Hub reconnect recovery 是未跟踪后台任务。
20. netstat 监听状态解析依赖英文 `LISTENING`。
21. 远程 IPv6 地址未统一加括号。
22. 原更新 hash 与 checksum 同源，没有独立签名信任锚。

以上问题均已在当前工作树中修复。

# 验证证据

## 构建与分析

- `dotnet restore PowerTray.sln --locked-mode`: 通过。
- `dotnet build PowerTray.sln -c Debug --no-restore`: 通过，0 warning / 0 error。
- `dotnet build PowerTray.sln -c Release --no-restore`: 通过，0 warning / 0 error。
- `dotnet build ... -p:EnableNETAnalyzers=true -p:AnalysisLevel=latest-all`: 通过，0 warning / 0 error。

## 测试

- `PowerTray.Tests` Release 单次: 通过。
- `PowerTray.Tests` Release 连续 30 次: 30/30 通过。
- 新增覆盖:
  - 单次抖动不离线。
  - 连续失败阈值。
  - offline 后相同电量恢复强制发布。
  - 配置边界和 disabledDevices。
  - Centurion 正常/畸形帧。
  - 诊断隐私范围。
  - IPC HMAC、篡改、重放、类型绑定、全部 request union、null envelope。
  - updater ECDSA detached signature。
  - updater 文件校验后替换。
  - trusted GitHub URL 和 lookalike host 拒绝。
  - HTTP 远程 token 和 IPv6 prefix。
  - receiver pairing slot 稳定身份与无效序列号处理。

## 依赖和供应链

- `dotnet list ... --vulnerable --include-transitive`: 未发现已知漏洞包。
- `dotnet list ... --deprecated --include-transitive`: 未发现弃用包。
- `hidapi.dll` SHA-256 校验: 通过。
- `hidapi.dll` Authenticode: `NotSigned`。
- build-installer PowerShell parser: 通过。
- build-installer ECDSA checksum/signature helper: 生成并验证通过，签名长度 64 bytes。
- 仓库私钥内容扫描: 通过；私钥未被 Git 跟踪。
- `git diff --check`: 通过。

## 运行时

Release 构建烟雾验证:

- PowerTray UI 启动成功。
- PowerTrayHID helper 启动成功。
- 第二进程 `PowerTray.exe --shutdown` 成功请求优雅关闭。
- 主进程在超时内退出。
- helper 随父进程退出。
- 无孤儿 PowerTrayHID 进程。

# 剩余风险与未验证范围

## R1 自定义 hidapi 可复现来源

严重度: **中等供应链残余**

现有 DLL 的 hash 已固定，但原始上游 commit、patch 和构建参数不在仓库历史中，无法真实恢复。未来替换 DLL 时必须提交完整 source provenance 和可复现构建流程。

## R2 安装器实际编译

严重度: **验证缺口**

当前设备未安装 Inno Setup `ISCC.exe`，因此 `PowerTrayInstaller.iss` 已静态审查，但未在本机执行最终 installer compile。PowerShell 打包脚本语法和签名 helper 已验证。

## R3 实机长期稳定性

严重度: **验证缺口**

没有在本轮自动审计中执行以下长时间实机测试:

- 多只 Logitech 设备连续热插拔。
- 接收器睡眠/唤醒和异常 USB 抖动。
- 24 小时以上 Session/handle/thread/GDI 曲线。
- G Hub 实际重连和 owner 变化。

代码路径、状态机和单元测试已覆盖，但仍建议发布前在实际设备上进行长时间观察。

## R4 显式远程 HTTP 为明文

严重度: **低，非默认配置**

默认仅 loopback。用户显式开启远程模式后有 token 认证和风险提示，但传输仍是 HTTP，不提供 TLS。应仅用于可信局域网并通过防火墙限制；不应直接暴露到互联网。

## R5 无硬件稳定标识设备跨端口身份

严重度: **低，功能限制**

若设备和接收器均没有有效序列号/Unit ID/ContainerId，只能使用本地端点映射。移动 USB 端口后无法在不冒错误合并风险的前提下自动证明是同一物理设备。当前策略选择不自动合并。

## R6 更新签名私钥运维

严重度: **运维要求**

私钥已生成并限制在开发机用户目录，但离线加密备份需要由发布者完成。丢失密钥会阻断现有版本对新签名的自动信任；轮换必须遵循 `docs/update-signing.md` 的过渡版本流程。

# 最终评级

- 默认互联网远程攻击面: 未发现 Critical/High。
- 本地可靠性: 原 H1/H2/H3 故障链已拆除。
- 本地 IPC: 从命名隔离升级为防篡改、防重放的应用层认证。
- 更新供应链: 从同源 SHA-256 升级为内置 ECDSA 公钥的独立签名链。
- 代码质量: Debug/Release/latest-all analyzer 0 warning。
- 发布建议: 在完成一次真实 Inno Setup 编译和 Logitech 实机长时间测试后再发布下一版本。

本轮未 commit、未 push、未创建 GitHub Release、未回复公开 Issue。
