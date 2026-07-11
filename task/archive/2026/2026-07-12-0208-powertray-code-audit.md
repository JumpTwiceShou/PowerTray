# PowerTray 代码逻辑与安全审计报告

- Task ID: `2026-07-12-0208-powertray-code-audit`
- 审计时间: `2026-07-12 02:08-03:05 +09:00`
- 状态: `completed`
- 审计基线: 私有 `main` / `f40068846ba9825552bf01e403f13c5bdaf21747`
- 公开版本: `origin/main` / `1aa11403fb108774ce6745d9c536845a63c895dc` (`v1.4.1`)
- 结论级别: 未发现默认配置下可被互联网直接利用的 Critical 漏洞；发现 3 项高优先级可靠性缺陷、11 项中优先级逻辑/本地安全问题及若干低优先级加固项。

## 范围

- `LGSTrayHID`
- `LGSTrayCore`
- `LGSTrayPrimitives`
- `LGSTrayUI`
- `PowerTrayInstaller.iss`
- `build-installer.ps1`
- `.github/workflows/build.yml`
- NuGet 依赖和现有测试

## 验证结果

- `dotnet build PowerTray.sln -c Debug`: 通过，0 warning / 0 error。
- `dotnet build PowerTray.sln -c Release`: 通过，0 warning / 0 error。
- 启用 latest-all .NET analyzer 后构建: 通过，0 warning / 0 error。
- `PowerTray.Tests`: 单次通过；连续 30 次运行全部通过。
- `dotnet list ... --vulnerable --include-transitive`: 未发现已知漏洞包。
- `dotnet list ... --deprecated --include-transitive`: 未发现弃用包。
- 依赖有新版本，但多数为 .NET 10 主版本或需要兼容性评估，不构成当前漏洞证据。
- 仓库内 `hidapi.dll` 为自定义 hidapi 0.14.0 构建，SHA-256 `38BDA32F593C054CACAF95BEBCE36F9BACC7FBD0740F7B6F72F6D368FBC84B4D`，无 Authenticode 签名。

# 高优先级发现

## H1 自动 Presence Check 每约 42 秒完整重建原生 HID Session

位置:

- `LGSTrayUI/NotifyIconViewModel.cs:176-199,252-259`
- `LGSTrayHID/HidppManagerContext.cs:269-323`

证据:

- 后台循环等待 30 秒后执行一次 Presence Check。
- Presence Check 调用所有 `IDeviceManager.RediscoverDevices()`，等待 12 秒再判断缺席。
- 因为一次检查本身约 12 秒，实际周期约 42 秒。
- 原生 HID 自身正常电量轮询周期却是 600 秒。
- 每次原生 Rediscover 都会创建新 Session，并 Dispose 所有旧 Session，而不是复用仍有效的 Session。

影响:

- 设备明明稳定在线，却每 42 秒经历一次端点枚举、句柄重开、Feature 探测和 Session 替换。
- 放大 LIGHTSPEED 接收器瞬时超时、独占句柄竞争和设备睡眠状态影响。
- 两次自动 Presence Check 未出现 INIT/UPDATE 后，约 84 秒即可被标记离线，与 Issue #4 的“几分钟后离线”高度吻合。
- 增加 USB/HID 负载、后台线程和 IPC 事件数量。

修复:

1. 自动 Presence Check 不应触发完整 Rediscover；只检查最后成功更新时间和后台健康状态。
2. 原生 Rediscover 只在热插拔、手动操作、后台重启或连续通信失败时执行。
3. Session 按 EndpointIdentityKey 增量复用，仅替换新增/消失/变化的端点。
4. Presence Check 应等待管理器返回明确结果，而不是固定等待 12 秒。

## H2 单次通信失败立即 OFFLINE，恢复时相同电量不发送 UPDATE

位置:

- `LGSTrayHID/HidppDevice.cs:325-339,361-378,393-407`

证据:

- 单次 150ms Ping 失败立即 `SignalOffline()`。
- 单次电量读取返回 null 也立即 `SignalOffline()`。
- 成功恢复时先将 `_offlineSignalled=false`，随后若电量/状态与上次完全相同则直接 return，不发布 UPDATE。

故障链:

```text
一次瞬时 Ping/读取失败
→ UI 收到 OFFLINE
→ 下一次读取恢复，但电量仍与上次相同
→ UPDATE 被“无变化”逻辑抑制
→ UI 持续离线，直到重启、重新发现或电量发生变化
```

影响:

- 已直接解释公开 Issue #4。
- 任何短暂接收器睡眠、命令排队或 HID 响应抖动都可能造成永久假离线。

修复:

1. 连续失败 2-3 次后才进入离线状态。
2. 失败后先做短间隔重试或触发受控 Rediscover。
3. 从 offline 恢复时必须强制发布 INIT/UPDATE，即使电量值未变化。
4. 区分 `BatteryUnavailable`、`TransportDegraded` 和真实 `Offline`，不要把一次电量查询失败等同于设备断开。

## H3 HID Session/原生句柄生命周期存在竞态与资源累积

位置:

- `LGSTrayHID/HidppDevices.cs:122-133,152-194,1170-1268`
- `LGSTrayHID/HidppManagerContext.cs:276-323`

证据:

- Dispose 只取消 CTS、完成 Channel，不等待 Read Thread 或后台 Task 退出。
- `_lifetimeCts`、`_commandSemaphore` 未 Dispose。
- Rediscover 在旧 Session 的原生读线程完全退出前即打开新端点。
- Reopen 先打开新端点，再替换 `_devShort/_devLong`，随后在另一个线程可能仍处于 native read 时关闭旧句柄。
- `_closedHandles` 永久保存原生指针数值；如果 native allocator 复用相同地址，新句柄可能被误认为已经关闭。

影响:

- 端点独占打开失败、use-after-close 风险、重复读线程、句柄泄漏和长期运行不稳定。
- 与 H1 的高频 Session 重建叠加后风险明显增大。

修复:

1. Session 实现 `IAsyncDisposable`，取消后等待所有读线程和后台 Task 退出，再关闭句柄。
2. 用 SafeHandle 封装 HID 句柄，避免裸 `nint` 和永久指针集合。
3. Reopen 在同一生命周期锁下完成“停止旧读线程→关闭旧句柄→打开新句柄→启动新线程”。
4. Session 未完成退出前禁止创建同端点替代 Session。

# 中优先级发现

## M1 HID 后台监督器可能静默永久停止或留下孤儿进程

位置:

- `LGSTrayCore/Managers/LGSTrayHIDManager.cs:61-97,141-164`
- `LGSTrayHID/Program.cs:31-48`

问题:

- `proc.Start()` 在 try 外，启动异常会直接终止 fire-and-forget supervisor Task。
- Kill 后没有保证 `WaitForExit`，随后读取 `proc.ExitCode` 仍可能抛异常。
- 连续快速失败超过 3 次后只 break，不通知 UI、不更新诊断，也不再恢复。
- Helper 的 parent watcher 中 `Process.GetProcessById(parentPid)` 抛异常时，watcher Task 结束，Helper 可能成为孤儿。

修复:

- 完整捕获 Start/Wait/Kill/ExitCode；监督器状态进入 UI 和 diagnostics。
- 使用指数退避并保留手动恢复入口，不静默永久放弃。
- Parent watcher 查询失败时立即受控停止 Host，而不是让 watcher 自身退出。

## M2 HID 命令匹配和排队会制造假超时

位置:

- `LGSTrayHID/HidppDevices.cs:989-1105,1284-1306`

问题:

- `_commandSemaphore.WaitAsync(commandTimeout)` 与实际设备响应共用同一个短超时；命令可能仅因前一命令仍在执行就失败。
- 响应匹配只校验 device index、feature index 和 software id，没有校验 function id，可能接受同 Feature 的陈旧/错误响应。
- Channel 容量 64 且 `DropOldest`，高流量时可能丢掉正在等待的命令响应。

修复:

- 分离“排队超时”和“设备 I/O 超时”。
- 匹配完整请求关联字段；优先引入 request correlation。
- Channel 满时记录明确错误，不能静默 DropOldest。

## M3 Fallback 身份并不稳定，且相同型号设备可能错误合并

位置:

- `LGSTrayHID/HidppDevices.cs:65`
- `LGSTrayHID/HidppDeviceIdentity.cs:104-121`
- `LGSTrayUI/LogiDeviceCollection.cs:224-248`
- `LGSTrayUI/UserSettingsWrapper.cs:472-493`

问题:

- Fallback ID 包含 `EndpointIdentityKey`，而该值包含 USB PathHash 和 device index；换 USB 口或接收器索引变化后 ID 会变化。
- model-id-only fallback 无法区分两台完全相同型号设备。
- UI 以“设备名称+类型+稳定度分数”迁移/删除重复项，可能把两台同型号设备的 alias、选择和告警设置合并。

修复:

- 明确稳定身份层级：有效序列号/Unit ID > receiver pairing slot + receiver stable id > 独立持久映射。
- 不用 USB path 作为跨端口永久身份。
- 同名同型号设备存在歧义时禁止自动合并，要求用户确认。

## M4 诊断请求机制未实现，错误地报告 HID 无响应

位置:

- `LGSTrayUI/NativeDiagnosticsClient.cs:24-90`
- `LGSTrayPrimitives/IPC/MessageStructs.cs`
- `LGSTrayHID/HidppManagerService.cs`

问题:

- `RequestAsync()` 只订阅未来广播并等待 2 秒，从未发送 `NATIVE_DIAGNOSTICS_REQUEST`。
- Request enum/message 已定义，但 HID 端没有处理器。
- 如果两秒内刚好没有 INIT/UPDATE/OFFLINE 广播，就显示 `PowerTrayHID did not respond`，即使后台正常。

影响:

- Issue #4 的诊断包缺失真正的 Ping、Feature、Session 和最近事件，降低排障能力。

修复:

- 实现带 requestId 的真实 request/response IPC。
- 加入 HID helper heartbeat、最后成功命令时间、supervisor 状态和最近异常。

## M5 诊断包过度收集非 Logitech 设备和明文 ContainerId

位置:

- `LGSTrayHID/NativeDiagnosticsStore.cs:116-121,221-233`
- `LGSTrayHID/HidEndpointInfo.cs:22`

问题:

- `unsupportedHidDevices` 包含所有非 Logitech HID 端点。
- 明文导出 Windows ContainerId。
- `GroupKey` 再次包含明文 ContainerId。
- readme 声称对 HID 路径、标识和序列号做哈希，但没有说明会导出其他厂商 HID 清单和 ContainerId。

影响:

- 诊断包可能暴露安全密钥、键盘、控制器等外围设备指纹。

修复:

- 默认只导出 Logitech VID 端点。
- ContainerId、GroupKey 哈希化或移除。
- 诊断导出 UI 明确列出字段和隐私范围。

## M6 AlertManager 不检查在线状态，且设备移除/迁移后可能残留幽灵告警

位置:

- `LGSTrayUI/AlertManager.cs:64-108`
- `LGSTrayUI/LogiDeviceCollection.cs`

问题:

- Evaluate 只检查电量和暂停状态，不检查 `device.IsOnline`。
- 离线设备保留低电量值时仍可能继续闪烁或发送通知。
- ID 迁移或删除时，AlertManager `_runtime` 和 AlertStateService 的旧 key 没有清理。
- `NotificationPending` 被写入但从未读取。

修复:

- 离线立即清除 blinking/pending。
- 提供 `RemoveDeviceState` / `MigrateDeviceState`。
- 删除未使用状态或实现清晰的 suppressed→pending→delivered 状态机。

## M7 HTTP API 存在跨线程集合访问；允许远程时没有认证

位置:

- `LGSTrayUI/LogiDeviceCollection.cs:27-28`
- `LGSTrayCore/HttpServer/HttpController.cs:43-99`
- `LGSTrayPrimitives/AppSettings.cs:23-43`

问题:

- EmbedIO 请求线程直接枚举 WPF `ObservableCollection`，UI 线程可能同时 Add/Remove，存在 `Collection was modified` 和线程亲和风险。
- 默认 loopback 是安全的；但 `AllowRemote=true` 时 API 无认证、无 TLS、无访问控制，会向局域网公开设备名称和电量。
- HTTP server Task fault 只写 Debug，不恢复、不通知。

修复:

- UI 线程生成不可变快照，HTTP 只读快照。
- HTTP 默认可考虑关闭；远程模式要求显式警告、绑定具体地址，并增加 token 或操作系统 ACL。
- 增加 server health/restart 状态。

## M8 更新器校验不是独立信任链，并存在校验后替换窗口

位置:

- `LGSTrayUI/UpdateService.cs:87-104,210-245`

已有优点:

- 安装器文件名白名单严格。
- 下载后校验 SHA-256。
- 运行前有第二次用户确认。

剩余问题:

- 安装器和 `.sha256` 来自同一 GitHub Release；仓库/账号被入侵时攻击者可同时替换二者。
- 校验完成后文件移动到可预测的 Downloads 路径；用户点击 Run 前没有再次校验，存在本地 TOCTOU 替换窗口。
- 不校验 `browser_download_url` 是否属于预期 GitHub 域名。
- 自动和手动更新检查没有互斥，可能竞争同一 `.download` 文件。

修复:

- 发布物使用 Authenticode 签名，并在执行前验证 signer thumbprint。
- 执行前重新计算 hash；下载到独占临时文件并保持安全句柄。
- 限定下载 host；更新检查加入 SemaphoreSlim。

## M9 安装器通过 PATH 查找 `dotnet`，存在搜索路径劫持

位置:

- `PowerTrayInstaller.iss:126-172`

问题:

- 轻量安装器运行 `cmd.exe /C "dotnet" --list-runtimes`。
- `dotnet` 未使用绝对路径，可能解析到安装器当前目录或 PATH 中非预期的 `dotnet.exe`。
- 安装器为 per-user，因此不是提权，但可能执行用户原本未打算运行的旁加载程序。

修复:

- 只使用注册表和 `%ProgramFiles%\dotnet\dotnet.exe` 等可信绝对路径。
- 删除 bare-name fallback；找不到可信 dotnet 时直接判定 runtime 缺失。

## M10 托盘图标反复创建 Icon 但旧 Icon 未释放

位置:

- `LGSTrayUI/BatteryIconDrawing.cs:85-179`

证据:

- 每次绘制都创建并 Clone 一个新 `System.Drawing.Icon` 后赋值给 `TaskbarIcon.Icon`。
- 对 Hardcodet.NotifyIcon.Wpf 1.1.0 反汇编确认 `set_Icon` 仅覆盖字段并更新 Shell，不 Dispose 旧 Icon。
- 该库 `TaskbarIcon.Dispose()` 也不 Dispose icon 字段。
- 低电量闪烁时每 500ms 重绘，长期可能累积 GDI Handle，依赖 GC Finalizer 回收。

修复:

- 保存旧 Icon，在 Shell 更新后显式 Dispose。
- 更好方案是按主题/电量档位缓存 Icon，闪烁只在两个缓存 Icon 间切换。
- 增加长时间闪烁 GDI handle 稳定性测试。

## M11 G Hub 模式可被本地服务冒充，重连逻辑存在竞态

位置:

- `LGSTrayCore/Managers/GHubManager.cs:66-238`

问题:

- 无认证连接 `ws://localhost:9010`，任何占用该端口的本地进程可伪造设备数据。
- `ParseSocketMsg` 顶层 JSON 反序列化未保护；异常可能终止消息订阅。
- Battery parse 吞掉所有异常，无诊断。
- Rediscover fire-and-forget，Dispose/Start 可能并发作用于 `_ws`。

修复:

- 验证 G Hub 进程和端口所有者；能用官方本地认证/握手时必须使用。
- 序列化重连，取消并等待旧连接彻底停止。
- 解析错误进入 diagnostics，不能静默吞掉。

# 低优先级与加固项

## L1 非 ASCII 设备名称读取按字符长度推进字节偏移

`HidppDevice.cs:120-126` 使用 `name.Length` 作为 HID 协议字节 offset，并逐块单独 UTF-8 解码。非 ASCII 名称可能截断、重复或出现替换字符。应先累计 byte，再统一 UTF-8 Decode。

## L2 配置数值和 disabledDevices 未验证

- 负数/极大 `PollPeriod`、`RetryTime` 可能导致 `Task.Delay` 异常并永久结束 fire-and-forget poll Task。
- `disabledDevices` 中空字符串会匹配所有名称；匹配区分大小写。

## L3 Background Task 普遍只捕获取消异常

多个 HID poll/discovery Task 对非取消异常没有统一观察、重启和日志，容易静默失效。

## L4 IPC 只有可预测名称，没有应用级认证

MessagePipe named pipe 名称是机器/域/用户名哈希，属于命名隔离而不是 Secret。MessagePipe 1.8.2 使用默认 `NamedPipeServerStream` 构造器，项目没有额外 ACL、握手或消息认证。同用户本地进程可能注入 INIT/UPDATE/OFFLINE 或 DoS；不是远程或提权漏洞。

## L5 Centurion Frame 长度处理过于宽松

- BuildFrame 对超长 payload 在 header 中写原长度后再截断正文。
- TryExtractPayload 对声明长度超过实际帧时静默截断。

应对超长/畸形帧直接拒绝。

## L6 设备 ViewModel 事件订阅未解除

`LogiDeviceViewModel` 订阅 UserSettings 和 Localization，但没有 Dispose；删除历史设备后对象仍可能被事件持有，长期热插拔会泄漏 ViewModel。

## L7 进程和应用退出方式不够优雅

`Environment.Exit(0)` 绕过 Host Stop 和正常 Dispose，依赖 HID 子进程 parent watcher 清理。应先请求 Host 停止并设置超时，最后才强制退出。

## L8 构建供应链可进一步固定

- Actions 使用 `actions/checkout@v4`、`setup-dotnet@v4` 可变 tag。
- runner 使用 `windows-latest`，SDK 使用 `8.0.x`。
- 没有 `packages.lock.json`。
- 自定义 unsigned `hidapi.dll` 只有说明版本，没有源码 commit、构建脚本和可复现证据。

建议 pin action commit、固定 runner/SDK、启用 lock file，并记录 hidapi 源 commit/补丁/构建命令/hash。

## L9 安装器按进程名 taskkill

会结束所有同名 `PowerTray.exe` / `PowerTrayHID.exe`，包括其他路径的同名程序。应核对进程路径后终止。

## L10 Battery 百分比未统一限定 0-100

HID++ 1000/1004 和 Centurion 数据直接进入 UI。畸形响应可能显示 >100 或错误触发告警；应在协议层校验并记录 invalid payload。

# 没有发现的问题

- 默认 HTTP 配置限制为 loopback；没有发现默认向局域网公开的行为。
- HTTP HTML 输出已编码，XML 输出已转义，未发现明显 XSS/XML 注入。
- 更新器不会选择任意 EXE，且会验证匹配文件名的 SHA-256。
- 当前 NuGet 依赖扫描未发现已知漏洞或弃用包。
- Debug/Release 构建和现有测试均通过。
- 未发现默认配置下的互联网远程代码执行、远程未授权写操作或权限提升路径。

# 修复优先级

## P0：先修 Issue #4 和运行时稳定性

1. 删除 42 秒全量 Rediscover；改为轻量健康检查和事件驱动 Rediscover。
2. 连续失败阈值 + offline recovery 强制 UPDATE。
3. Session async dispose、SafeHandle 和线程/Task join。
4. 增加状态机回归测试。

## P1：诊断、监督和资源泄漏

5. 实现真正 diagnostics request/response + heartbeat。
6. 修复 HID supervisor 静默终止与 orphan helper。
7. 修复 Icon/GDI 资源释放。
8. 修复离线告警和 ID 迁移后的状态清理。

## P2：安全和隐私加固

9. 安装器移除 PATH `dotnet` fallback。
10. 诊断只导出 Logitech，哈希 ContainerId/GroupKey。
11. 更新安装器签名并在执行前重新校验。
12. IPC/GHub 本地端点增加身份验证或至少进程所有者校验。
13. 远程 HTTP 模式增加认证和明确风险提示。

## P3：协议和维护性

14. 修复 UTF-8 名称读取、Frame 长度验证、响应 function 匹配。
15. 配置 schema 验证和 Background Task 统一监督。
16. pin CI action/SDK/依赖锁和 native binary provenance。

# 建议新增测试

- offline 后相同电量恢复必须重新在线。
- 连续失败阈值和单次抖动不离线。
- Presence Check 不触发完整 Session 重建。
- Session Dispose 后线程、Task、handle 数量回到基线。
- 相同型号两台设备不会错误合并。
- 非 ASCII 名称分片读取。
- diagnostics request/response 和 helper heartbeat。
- diagnostics 不包含非 Logitech 设备、明文 ContainerId 或 GroupKey。
- 离线/删除/ID 迁移时告警状态清理。
- GDI handle 长时间闪烁稳定。
- updater 校验后文件被替换时拒绝执行。
- 安装器绝不通过当前目录/PATH 执行 dotnet。

# 最终结论

PowerTray 当前没有发现默认配置下可由互联网直接利用的严重漏洞，编译、现有测试和依赖审计也均正常。但运行时设计存在明确的高优先级可靠性问题：自动全量 Rediscover、单次失败即离线、恢复事件被抑制，以及 HID 句柄/线程生命周期竞态。这些问题互相放大，是 Issue #4 和未来随机离线、后台失效、长期资源累积的主要风险。应先完成 P0，再发布下一版本。

本次仅审计和记录，不修改业务代码、不回复 Issue、不 commit、不 push、不发布。
