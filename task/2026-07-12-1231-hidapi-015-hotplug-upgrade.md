# PowerTray hidapi 0.15.0 + Hotplug 升级与可复现构建

- Task ID: `2026-07-12-1231-hidapi-015-hotplug-upgrade`
- 创建时间: `2026-07-12 12:31 +09:00`
- 状态: `pending`
- 优先级: `high`
- 来源: `task/2026-07-12-1156-hidapi-provenance-and-update-key-backup.md`

> 执行纪律：完成一项后立即勾选并记录证据，禁止提前或批量勾选。

## 背景

PowerTray 当前使用的 `LGSTrayHID/libhidapi/hidapi.dll` 是基于 hidapi 0.14.0 和官方 `connection-callback` hotplug 分支构建的 Windows x64 DLL。当前二进制 SHA-256 为：

```text
38BDA32F593C054CACAF95BEBCE36F9BACC7FBD0740F7B6F72F6D368FBC84B4D
```

当前 DLL 的来源和源码血统已经恢复，但完整构建参数与可复现构建脚本仍缺失。0.14.0 之后，官方稳定版和 hotplug 分支包含多项与 PowerTray 相关的 Windows 读取、写入、热插拔并发、死锁和设备拔出修复。

标准 hidapi 0.15.0 Release DLL 不包含 PowerTray 所需的两个 hotplug exports，禁止直接覆盖现有 DLL：

- `hid_hotplug_register_callback`
- `hid_hotplug_deregister_callback`

## 目标

从明确固定的官方源码构建新的 Windows x64 Release DLL：

```text
hidapi 0.15.0 稳定代码
+ 官方 connection-callback hotplug API
+ 后续 Windows hotplug 并发、死锁和拔出修复
+ 固定工具链与自动构建脚本
```

在不破坏现有 Logitech HID++ 通信、设备识别和热插拔行为的前提下，替换当前不可复现的 0.14.0 自定义 DLL。

## 预期收益

- 修复 `hid_read_timeout()` 可能产生的额外等待和读取线程异常行为。
- 修复 Windows HID 字符串缓冲长度问题，降低设备名称或制造商字符串损坏风险。
- 修复异步 `WriteFile` 实际同步完成时返回长度处理。
- 获得独立的读取错误状态和更完整的参数、内存及句柄检查。
- 合入 hotplug 回调互斥、回调内注销死锁、临界区未释放和设备拔出处理修复。
- 建立可审计、可重复执行的 native DLL 构建流程。

## 明确边界

- 不直接使用官方标准 0.15.0 预编译 DLL，因为缺少 hotplug exports。
- 不在未通过完整回归前替换仓库现有 DLL。
- 不修改 Logitech HID++ 协议实现，除非新 hidapi API/行为确实要求适配。
- 不公开发布、不 push、不创建 GitHub Release，除非用户另行明确授权。
- 不删除旧 DLL；替换前必须保存可立即回滚的副本和旧 SHA-256。

## 阶段 1：源码与补丁基线锁定

- [ ] 获取官方 `libusb/hidapi` 仓库，确认 tag `hidapi-0.15.0` 指向 commit `d6b2a974608dec3b76fb1e36c189f22b9cf3650c`。
- [ ] 检查官方 `connection-callback` 分支当前历史，列出 0.15.0 之后及与 Windows/hotplug 直接相关的 commits。
- [ ] 确定最终构建基线，优先选择一个包含 0.15.0、hotplug API 和所有必要 Windows 修复的固定 commit，而不是浮动 branch HEAD。
- [ ] 记录选择该 commit 的理由以及排除其他候选的理由。
- [ ] 确认许可证、版权和再分发条件与 PowerTray GPL-3.0 兼容。
- [ ] 保存 upstream commit、必要 patch 和来源 URL，不依赖未来可变化的远程 branch。

### 最低必须包含的修复

- [ ] Windows hotplug callback mutex。
- [ ] callback 内 register/deregister 的死锁修复。
- [ ] `LeaveCriticalSection` 遗漏修复。
- [ ] Windows device unplug handling 改进。
- [ ] `hid_read_timeout()` overlapped-event 等待修复。
- [ ] Windows synchronous `WriteFile` 返回长度修复。
- [ ] Windows HID 字符串最大 126 wchar 修复。
- [ ] 0.15.0 的参数、句柄和内存安全检查。

## 阶段 2：固定构建工具链

- [ ] 安装或定位 Visual Studio Build Tools，并记录准确版本。
- [ ] 记录 MSVC toolset 版本、Windows SDK 版本、CMake 版本和 generator。
- [ ] 固定目标架构为 Windows x64 Release。
- [ ] 确认动态 CRT/静态 CRT 选择与当前 PowerTray 分发方式兼容。
- [ ] 确认 DLL 名称仍为 `hidapi.dll`，避免修改现有 P/Invoke library name。
- [ ] 禁止在最终 DLL 中嵌入开发者本机绝对路径；如无法避免，记录原因并评估信息泄露。
- [ ] 在仓库中加入独立 native build 目录、固定源码获取方式和一键构建脚本。
- [ ] 构建脚本必须从干净目录开始，并在失败时 fail closed。

## 阶段 3：二进制与 ABI 验证

- [ ] 构建 Windows x64 Release `hidapi.dll`。
- [ ] 记录新 DLL SHA-256、文件版本、PE timestamp、linker version、大小和 Authenticode 状态。
- [ ] 验证所有 PowerTray 当前使用的标准 hidapi exports 仍存在。
- [ ] 验证以下 custom exports 存在且签名兼容：
  - [ ] `hid_hotplug_register_callback`
  - [ ] `hid_hotplug_deregister_callback`
- [ ] 对照 `LGSTrayHID/HidApi/*.cs` 的 P/Invoke 声明逐项检查 calling convention、参数宽度、结构体布局和返回值。
- [ ] 检查 `hid_device_info` 结构体在新版源码中是否变化。
- [ ] 检查 `hid_winapi_get_container_id` 是否仍存在并保持兼容。
- [ ] 在同一源码与同一工具链下执行至少两次 clean build，比较 SHA-256；若不一致，分析 PE timestamp、PDB、GUID 等非确定性来源。
- [ ] 尽可能启用 deterministic/reproducible build；无法逐字节一致时，明确记录剩余非确定字段。

## 阶段 4：PowerTray 代码适配

- [ ] 将新 DLL 暂存为测试 artifact，不立即覆盖正式文件。
- [ ] 使用独立测试输出运行 `verify-hidapi.ps1` 的扩展版验证。
- [ ] 如新版错误 API 可用，评估是否将读取错误诊断接入 `NativeDiagnosticsStore`；不为了使用新 API 而扩大改动范围。
- [ ] 检查 `hid_read_timeout()` 行为变化是否影响当前 reader loop、取消和关闭逻辑。
- [ ] 检查 hotplug 回调线程上下文是否允许当前 C# callback 只做轻量排队。
- [ ] 检查 callback deregistration 和 `HidppManagerContext.DisposeAsync` 的顺序。
- [ ] 仅在完成代码和 ABI 检查后替换 `LGSTrayHID/libhidapi/hidapi.dll`。
- [ ] 更新 `verify-hidapi.ps1` 中固定 SHA-256 和 export 检查。
- [ ] 更新 `LGSTrayHID/libhidapi/readme.md`，记录准确 commit、工具链、命令、flags 和产物证据。

## 阶段 5：自动化验证

- [ ] `dotnet restore --locked-mode` 通过。
- [ ] Debug build 通过，0 error。
- [ ] Release build 通过，0 error。
- [ ] `PowerTray.Tests` 全部通过。
- [ ] 新 native build 脚本从干净目录成功构建。
- [ ] CI 中增加 native 来源/hash/export 验证；是否在 CI 内重新构建由耗时和工具链可用性决定。
- [ ] `build-installer.ps1` 能携带新 DLL 完成 publish；安装器完整编译由安装器 task 管理。
- [ ] `git diff --check` 通过。

## 阶段 6：Windows 实机回归矩阵

### 基础检测

- [ ] PowerTray 和 PowerTrayHID 正常启动，无 `EntryPointNotFoundException`、`BadImageFormatException` 或 native crash。
- [ ] G Pro Wireless Mouse 正常识别并读取电量。
- [ ] Pro X 2 Lightspeed 正常识别并读取电量。
- [ ] 设备名称、序列号、Unit ID、ContainerId 和持久化 identity 没有异常变化。
- [ ] 多设备同时在线时不发生串设备或电量覆盖。

### HID 读写

- [ ] 初次枚举成功。
- [ ] 电量查询与强制刷新成功。
- [ ] 设备休眠后唤醒可恢复更新。
- [ ] 超时读取不会造成持续高 CPU、100ms 累积卡顿或 reader loop 卡死。
- [ ] 发送失败和读取失败能正确记录并触发既有恢复逻辑。

### Hotplug

- [ ] USB 接收器拔出后设备按预期进入离线状态。
- [ ] USB 接收器重新插入后自动恢复，无需重启 PowerTray。
- [ ] 耳机连接 USB 充电线发生重新枚举时，不产生长期 OFFLINE。
- [ ] 耳机断开充电线后继续正常读取无线电量。
- [ ] 快速连续插拔至少 20 次，无死锁、崩溃、重复设备或失效 callback。
- [ ] callback 内部触发 rediscover 时不发生死锁。
- [ ] 应用关闭时 callback 正常注销，PowerTrayHID 无孤儿进程。

### 资源与稳定性

- [ ] 运行期间 native handle 数量无持续增长。
- [ ] 多次 rediscover 后线程数无持续增长。
- [ ] 快速插拔期间 CPU 不出现持续异常占用。
- [ ] Windows 事件查看器无新的 Application Error、BEX、Access Violation 或 native crash。
- [ ] 运行至少 2 小时的常规使用烟雾测试；此前用户排除的 24 小时长稳测试仍不强制。

## 阶段 7：回滚验证

- [ ] 替换前保存旧 DLL、旧 SHA-256 和旧 README 证据。
- [ ] 验证只恢复旧 DLL 与旧 hash 配置即可回退。
- [ ] 若出现设备漏检、崩溃、热插拔死锁、CPU 异常或身份映射变化，立即回滚，不带病合入。
- [ ] 回滚后重新运行基础检测，确认恢复到当前已知状态。

## 完成标准

只有同时满足以下条件才能完成并归档：

1. 新 DLL 基于固定的官方源码 commit，不依赖浮动分支。
2. 构建工具链、命令、flags 和脚本完整记录。
3. PowerTray 所需全部 exports 和 ABI 验证通过。
4. Debug/Release build 与自动测试通过。
5. 现有两台 Logitech 实机的读取、休眠恢复和 hotplug 回归通过。
6. 快速插拔、退出和 rediscover 不出现死锁、崩溃、孤儿进程或持续资源增长。
7. 新 SHA-256、来源、许可证和回滚方法已写入文档。
8. scoped diff、task 证据和设计记忆已更新。

## 失败条件

出现以下任一情况时不得替换当前 DLL：

- 缺少任一 required export。
- P/Invoke ABI 或结构体布局无法确认。
- 新 DLL 在现有 Logitech 设备上出现回归。
- 热插拔产生死锁、重复设备、持续离线或高 CPU。
- 无法固定源码和构建工具链。
- 无法提供明确回滚路径。
