# PowerTray hidapi 0.15.0 + Hotplug 升级与可复现构建

- Task ID: `2026-07-12-1231-hidapi-015-hotplug-upgrade`
- 创建时间: `2026-07-12 12:31 +09:00`
- 状态: `in_progress`
- 优先级: `high`
- 目标应用版本: `1.4.2`
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

- [x] 获取官方 `libusb/hidapi` 仓库，确认 tag `hidapi-0.15.0` 指向 commit `d6b2a974608dec3b76fb1e36c189f22b9cf3650c`。
- [x] 检查官方 `connection-callback` 分支当前历史，列出 0.15.0 之后及与 Windows/hotplug 直接相关的 commits。
- [x] 确定最终构建基线，优先选择一个包含 0.15.0、hotplug API 和所有必要 Windows 修复的固定 commit，而不是浮动 branch HEAD。
- [x] 记录选择该 commit 的理由以及排除其他候选的理由。
- [x] 确认许可证、版权和再分发条件与 PowerTray GPL-3.0 兼容。
- [x] 保存 upstream commit、必要 patch 和来源 URL，不依赖未来可变化的远程 branch。

### 最低必须包含的修复

- [x] Windows hotplug callback mutex。
- [x] callback 内 register/deregister 的死锁修复。
- [x] `LeaveCriticalSection` 遗漏修复。
- [x] Windows device unplug handling 改进。
- [x] `hid_read_timeout()` overlapped-event 等待修复。
- [x] Windows synchronous `WriteFile` 返回长度修复。
- [x] Windows HID 字符串最大 126 wchar 修复。
- [x] 0.15.0 的参数、句柄和内存安全检查。

## 阶段 2：固定构建工具链

- [x] 安装或定位 Visual Studio Build Tools，并记录准确版本。
- [x] 记录 MSVC toolset 版本、Windows SDK 版本、CMake 版本和 generator。
- [x] 固定目标架构为 Windows x64 Release。
- [x] 确认动态 CRT/静态 CRT 选择与当前 PowerTray 分发方式兼容。
- [x] 确认 DLL 名称仍为 `hidapi.dll`，避免修改现有 P/Invoke library name。
- [x] 禁止在最终 DLL 中嵌入开发者本机绝对路径；如无法避免，记录原因并评估信息泄露。
- [x] 在仓库中加入独立 native build 目录、固定源码获取方式和一键构建脚本。
- [x] 构建脚本必须从干净目录开始，并在失败时 fail closed。

## 阶段 3：二进制与 ABI 验证

- [x] 构建 Windows x64 Release `hidapi.dll`。
- [x] 记录新 DLL SHA-256、文件版本、PE timestamp、linker version、大小和 Authenticode 状态。
- [x] 验证所有 PowerTray 当前使用的标准 hidapi exports 仍存在。
- [x] 验证以下 custom exports 存在且签名兼容：
  - [x] `hid_hotplug_register_callback`
  - [x] `hid_hotplug_deregister_callback`
- [x] 对照 `LGSTrayHID/HidApi/*.cs` 的 P/Invoke 声明逐项检查 calling convention、参数宽度、结构体布局和返回值。
- [x] 检查 `hid_device_info` 结构体在新版源码中是否变化。
- [x] 检查 `hid_winapi_get_container_id` 是否仍存在并保持兼容。
- [x] 在同一源码与同一工具链下执行至少两次 clean build，比较 SHA-256；若不一致，分析 PE timestamp、PDB、GUID 等非确定性来源。
- [x] 尽可能启用 deterministic/reproducible build；无法逐字节一致时，明确记录剩余非确定字段。

## 阶段 4：PowerTray 代码适配

- [x] 将新 DLL 暂存为测试 artifact，不立即覆盖正式文件。
- [x] 使用独立测试输出运行 `verify-hidapi.ps1` 的扩展版验证。
- [x] 如新版错误 API 可用，评估是否将读取错误诊断接入 `NativeDiagnosticsStore`；不为了使用新 API 而扩大改动范围。
- [x] 检查 `hid_read_timeout()` 行为变化是否影响当前 reader loop、取消和关闭逻辑。
- [x] 检查 hotplug 回调线程上下文是否允许当前 C# callback 只做轻量排队。
- [x] 检查 callback deregistration 和 `HidppManagerContext.DisposeAsync` 的顺序。
- [x] 修复多个托盘设备提示框未统一跟随 PowerTray 主题的问题，并在深色/浅色主题下验证。
- [x] 将托盘右键菜单全部颜色迁移到单一可变调色板，消除根菜单与 Popup 子菜单分别解析主题资源造成的背景/文字回退。
- [ ] 在深色和浅色主题下对根菜单、设备子菜单、禁用版本项、勾选图标、悬停和分隔线做最终实机目视确认。
- [ ] 仅在完成代码和 ABI 检查后替换 `LGSTrayHID/libhidapi/hidapi.dll`。
- [ ] 更新 `verify-hidapi.ps1` 中固定 SHA-256 和 export 检查。
- [ ] 更新 `LGSTrayHID/libhidapi/readme.md`，记录准确 commit、工具链、命令、flags 和产物证据。

## 阶段 5：自动化验证

- [x] `dotnet restore --locked-mode` 通过。
- [x] Debug build 通过，0 error。
- [x] Release build 通过，0 error。
- [x] `PowerTray.Tests` 全部通过。
- [x] 新 native build 脚本从干净目录成功构建。
- [ ] CI 中增加 native 来源/hash/export 验证；是否在 CI 内重新构建由耗时和工具链可用性决定。
- [ ] `build-installer.ps1` 能携带新 DLL 完成 publish；安装器完整编译由安装器 task 管理。
- [x] `git diff --check` 通过。

## 阶段 6：Windows 实机回归矩阵

### 基础检测

- [x] PowerTray 和 PowerTrayHID 正常启动，无 `EntryPointNotFoundException`、`BadImageFormatException` 或 native crash。
- [x] G Pro Wireless Mouse（当前设备名 `PRO X2 SUPERSTRIKE Wireless Mouse`）正常识别并读取电量。
- [x] Pro X 2 Lightspeed 正常识别并读取电量。
- [x] 设备名称、设备 ID 和持久化 identity 在本轮启动/重启验证中没有异常变化。
- [x] 鼠标与耳机同时在线时分别显示 88% 与 59%，未发生串设备或电量覆盖。

### HID 读写

- [x] 初次枚举成功。
- [ ] 电量查询成功；强制刷新仍需独立执行验证。
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
- [x] 应用关闭时 callback 正常注销，PowerTrayHID 无孤儿进程。

### 资源与稳定性

- [ ] 运行期间 native handle 数量无持续增长。
- [ ] 多次 rediscover 后线程数无持续增长。
- [ ] 快速插拔期间 CPU 不出现持续异常占用。
- [x] 完成实机读取与优雅退出的验证窗口内，Windows 事件查看器无新的 `.NET Runtime`、`Application Error` 或 `Windows Error Reporting`。
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

## 执行证据

### 2026-07-12 阶段 1

- 从 `https://github.com/libusb/hidapi.git` fetch 全部分支和 tags；annotated tag object 为 `dbff4ea89f55a572aeb0c53a7b32ea70853ec260`，解引用 `hidapi-0.15.0^{}` 后确认 commit 为 `d6b2a974608dec3b76fb1e36c189f22b9cf3650c`。
- 检查 `origin/connection-callback` 的 first-parent 与路径历史：0.15.0 后合入 `19112a2`、`ec2cd2f`、`12a30f1`、`4398a7b` 四次 master，同分支 Windows/hotplug 直接修复包括 `ce92386`（callback mutex）、`b6606ca`（遗漏 `LeaveCriticalSection`）、`da500c6`（callback 内注销死锁）、`5360e03`（Windows 拔出处理）；当前分支 head `1889e9f` 另含 libusb callback 高 CPU 修复。
- 固定最终 Windows 构建基线为官方 commit `5360e03d6edcb7820eda3dd0fa1f8706e82e2600`，不引用浮动分支名；`merge-base --is-ancestor` 已确认 0.15.0 commit、hotplug API 及 task 列出的全部 Windows 修复均在该快照内。
- 选择理由：`5360e03` 是官方分支最后一个直接修改 Windows 拔出行为的 commit，并已包含 0.15.0 master 合并与全部所需 Windows/hotplug 修复。排除标准 `hidapi-0.15.0` 是因为缺少 hotplug exports；排除旧 `eea8cac` 是因为缺少后续修复；排除后续 `8bd8ff7`（仅 macOS）和 `1889e9f`（仅 libusb/macOS）是为了避免把与 Windows DLL 无关的改动扩大进固定基线。
- 官方 `LICENSE.txt` 明确允许在 GPL-3.0、BSD-style 或 original HIDAPI license 中任选；PowerTray 选择 GPL-3.0 路径，与仓库许可证及 DLL 再分发兼容，构建快照将保留 upstream license 文件。
- 新增 `native/hidapi/source.json`，固定 commit archive URL 及 archive SHA-256 `4DC06B08B90E07BA8D146847678792C454B78CB2B4E015AE88236891E5225048`；`required-upstream-commits.txt` 保存完整上游修复范围，`patches/README.md` 明确当前没有本地 patch。已逐项验证所有列出的 commit 均为固定基线祖先，构建不依赖 branch HEAD。
- `ce923867993c55bc374ed99a4f384913af6495b4`（Windows hotplug callback mutex）为固定基线祖先。
- `da500c6ccdf0c6498b8aecd30517a53df3533c54`（callback 内安全 deregister、避免死锁）为固定基线祖先。
- `b6606ca241e2e500638669141e21249865f8cc4a`（补齐 `LeaveCriticalSection`）为固定基线祖先。
- 固定基线 commit `5360e03d6edcb7820eda3dd0fa1f8706e82e2600` 本身即 Windows device unplug handling 改进。
- `d0732cda906ad07b7e1ef93f1919035643620435`（`hid_read_timeout()` 不再额外等待 overlapped event）为固定基线祖先。
- `5c4acf88a8695e52b1d9860b4d596c5ec53d83f6`（同步 `WriteFile` 返回实际字节数）为固定基线祖先。
- `4f2e91bae80cc48e567a80bd9ae3dc53dc5b73c6`（Windows HID string buffer 最大 126 wchar）为固定基线祖先。
- `750bf201ae906eec1dc52c97087c8d0104c091ae` 与 `0ab6c14264ec76e9328a8eedb3b72b5d27dffd47`（allocation/handle/argument sanity checks）均为固定基线祖先。

### 2026-07-12 阶段 2

- 当前 Codex 进程为 medium integrity，管理员组仅 `deny only`；非提升的 winget Build Tools 17.14.35 安装以 exit 1602 结束，复核确认未产生 VS instance 或 `vswhere.exe`。可见 elevated 安装需等待用户按 Windows UI 确认规则在 action time 明确确认，未把失败安装误记为完成。
- CMake 使用 Kitware 官方 4.4.0 Windows x64 portable archive，下载 SHA-256 为 `156D70EB7625A7B469444DF7D0861D2AF8D5D0A437FCE32C350372B08F5620E8`，位置 `%USERPROFILE%\.codex\tools\cmake-4.4.0-windows-x86_64`；最终工具链清单将在 VS/MSVC/SDK 安装后整体记录。
- `build-hidapi.ps1` 固定 generator `Visual Studio 17 2022`、architecture `x64`、configuration `Release`，不会根据宿主默认值选择架构或配置。
- 当前正式 DLL 的 PE import table 仅包含 `KERNEL32.dll`，没有 `VCRUNTIME`/`MSVCP` runtime 依赖；新构建固定 `CMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded`（`/MT` 静态 CRT），保持单 DLL 分发兼容性。
- 固定源码 `windows/CMakeLists.txt` 对 `hidapi_winapi` 设置 `OUTPUT_NAME hidapi`；构建脚本只接受并复制唯一的 `hidapi.dll`，现有 `LibraryImport("hidapi")` 无需改名。
- 新增独立 `native/hidapi/`：immutable source manifest、required upstream commit 清单、local patch policy、artifact ignore 规则及 `build-hidapi.ps1` 一键构建脚本；PowerShell parser 和 manifest/archive/ancestry 校验通过。
- 脚本仅允许清理 `%TEMP%` 下的 build root，每次删除并重建 source/build 目录；CMake/VS 版本、source archive hash、SDK、command exit code 和唯一 DLL 选择任一不符合即抛错停止。实际 clean build 成功证据仍由阶段 3/5 单独验证，未提前勾选。
- 用户在本轮明确同意安装；通过提权 winget 安装 Visual Studio Build Tools 2022 `17.14.35 (June 2026)`，installation version `17.14.37411.7`，绝对路径 `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools`。安装主日志记录 `Completed install`，`vswhere` 显示 `isComplete=true`、`isLaunchable=true`、`isRebootRequired=false`，且 required-component 查询同时匹配 `Microsoft.VisualStudio.Component.VC.Tools.x86.x64` 与 `Microsoft.VisualStudio.Component.Windows11SDK.26100`。
- 固定工具链实测为 MSVC toolset `14.44.35207`、`cl.exe` file version `19.44.35228.0`、Windows SDK `10.0.26100.0`、Kitware CMake `4.4.0`、generator `Visual Studio 17 2022`、architecture `x64`、configuration `Release`、static CRT `/MT`。安装器完成后遗留的 elevated winget wrapper 未自行退出；在 `vswhere` 完整状态及安装日志成功证据成立后终止该残留 wrapper，未中断任何 setup 子进程。
- 安装后首次脚本执行在下载/配置前按 fail-closed 退出：原门槛错误地用展示版本 `17.14.35` 比较 `vswhere.installationVersion=17.14.37411.7`。脚本已改为精确固定 installation version `17.14.37411.7`；此项仅修正版本字段语义，clean native build 仍需后续单独通过后才能勾选。
- 第二次脚本执行同样在 CMake 配置前 fail-closed：PowerShell 不支持原脚本使用的 `Select-Object -Single`。已改为对解压源码目录和构建出的 `hidapi.dll` 分别执行显式 `Count -eq 1` 检查，数量不唯一时仍立即抛错；前两次均未进入 native 编译，不计入两次 clean build 验证。
- 修正上述两项后首次 native 编译成功，MSVC `19.44.35228.0`、SDK `10.0.26100.0` 生成测试 DLL SHA-256 `FA2477A9D3BAB60C3CE92DE9D51319F945BFFB95B5D16ED5027739A51BF22FD1`。编译器同时报告 `/pathmap` 需要 `/experimental:deterministic` 且原参数被忽略，因此该次仅作为工具链打通证据，不计入最终两次可复现 clean build；脚本已补齐 `/experimental:deterministic`，后续以最终 flags 重建。
- 固定 commit 的 CMake configure 输出项目版本 `hidapi: v0.16.0`；这是 `connection-callback` 官方分支在包含 0.15.0 稳定提交及 task 所需后续修复后的版本元数据，不等同于、也未使用缺少 hotplug exports 的标准 0.15.0 预编译 DLL。

### 2026-07-12 阶段 3/4 二进制与 ABI

- 最终 flags 为 compiler `/O2 /Ob2 /DNDEBUG /Brepro /experimental:deterministic /pathmap:<clean-source>=hidapi`，linker `/INCREMENTAL:NO /OPT:REF /OPT:ICF /Brepro`，static CRT `/MT`。两个不同 `%TEMP%` clean root 分别完整下载、校验、解压、configure 和 build，均生成 SHA-256 `FA2477A9D3BAB60C3CE92DE9D51319F945BFFB95B5D16ED5027739A51BF22FD1`，大小 `173056` bytes，逐字节一致。
- 新 DLL 为 PE32+ x64，file/product version `0.16.0`，PE reproducible timestamp field `0x9DA14B5F`，linker `14.44`，Authenticode `NotSigned`；import table 仅 `KERNEL32.dll`，没有 VCRUNTIME/MSVCP 动态 CRT 依赖。
- `dumpbin /exports` 显示 30 个命名导出；PowerTray 当前验证的 12 个 required exports 全部存在，其中 `hid_hotplug_register_callback`、`hid_hotplug_deregister_callback` 与 `hid_winapi_get_container_id` 均存在。独立 artifact 运行扩展 `verify-hidapi.ps1` 通过：x64、固定 hash、12 exports、Authenticode 状态均符合门禁。
- 用 binary string scan 检查 `C:\Users\jiang`、clean build root 名和固定 commit 源目录名均未命中；最终 DLL 未嵌入开发者绝对路径。MSBuild 关于 TEMP 不适合增量构建的 `MSB8029` 只针对工程目录；脚本每次强制 clean build，不使用增量产物。
- C ABI 与 C# 声明逐项对照：Windows x64 的 `HID_API_CALL` 为空宏，所有 `LibraryImport` 和 callback delegate 显式 Cdecl；`unsigned short`→`ushort`、`size_t`→`nuint`、handle→`nint`、callback handle/flags/events/return→32-bit `int` 映射一致；`GUID*` 对应 `Guid*`。新版 `hid_device_info` 字段顺序未变化，x64 C# layout 仍为 72 bytes，既有 exact-offset 测试覆盖到 `bus_type`；`hid_winapi_get_container_id(hid_device*, GUID*) -> int` 未变化。
- 新 DLL 与 `build-evidence.json` 已暂存到 ignored `native/hidapi/artifacts/` 作为测试 artifact；正式 `LGSTrayHID/libhidapi/hidapi.dll` 仍保持旧 hash `38BDA32F...B4D`，尚未替换。
- 行为代码复核：`hid_read_timeout()` 的 `0` 仍仅继续 reader loop，`<0` 才进入既有失败/离线恢复；关闭时先取消 read token 并关闭 SafeHandle，使 reader 退出后再完成 session disposal。hotplug callback 不执行枚举或等待锁，只记录端点 hash/离线宽限并排队 rediscover；manager stop 先同步 deregister callback，再取消/等待后台任务，最后异步 dispose sessions。新版 `hid_read_error` 可提供更细错误文本，但当前错误恢复不依赖它，本轮不为可选诊断扩大 P/Invoke 与日志范围。

### 2026-07-12 阶段 5 自动化验证（替换前）

- `dotnet restore PowerTray.sln --locked-mode` 通过；Debug 和 Release solution build 均为 `0 warning / 0 error`；Release `PowerTray.Tests` 通过，其中 x64 `hid_device_info` 72-byte exact-offset ABI 测试通过。
- native clean build、独立 artifact hash/export/architecture 验证已通过；正式 DLL 尚未替换，因此 installer publish 与最终 `verify-hidapi.ps1` 固定 hash 更新留到实机门禁之后。
- 隔离 Debug runtime 用新 DLL 启动 `PowerTray` 与 `PowerTrayHID` 成功，HTTP 仅监听 `localhost:12321`，未出现 `EntryPointNotFoundException`、`BadImageFormatException` 或 native crash；但实时 PnP/CIM 中没有任何 `VID_046D` 设备，HTTP `/devices` 为空，因此两台 Logitech 的读取与 hotplug 项保持未勾选。
- 第一次通过 `PowerTray.exe --shutdown` 退出时两个进程均消失且无孤儿，但 Application log 记录 UI `.NET Runtime 1026`：MessagePipe named-pipe receive loop 在 host stop 时把预期 `OperationCanceledException` 投递到 WPF Dispatcher。新增窄范围 shutdown guard：仅当 `IHostApplicationLifetime.ApplicationStopping` 已触发时，将 Dispatcher 上的 `OperationCanceledException` 标记 handled；其他时段和其他异常仍保持原错误处理。复测通过前不勾选优雅退出门禁。
- shutdown guard 后重新执行 Debug build（`0 warning / 0 error`）与 `PowerTray.Tests` 均通过。隔离 UI runtime 携带新 DLL 后 `/health` 返回 HTTP 200；随后用同一 runtime 的 `PowerTray.exe --shutdown` 触发正式停止路径，`PowerTray`/`PowerTrayHID` 在 10 秒门限内全部退出、无孤儿进程，且该运行窗口内 Application log 没有新的 `.NET Runtime`、`Application Error` 或 `Windows Error Reporting` 事件。callback deregistration/host stop 门禁通过。
- 当前 Windows VM 的 `Get-PnpDevice -PresentOnly` 与 `Win32_PnPEntity` 都没有 `VID_046D`，因此设备识别、电量、休眠、插拔、资源增长与 2 小时 smoke 仍等待 Logitech USB 设备接入；正式 DLL 保持旧稳定版本，不在硬件门禁缺失时替换。
- 最终 scoped validation 再次通过：locked restore、Debug build、Release build 均成功且 `0 warning / 0 error`，`PowerTray.Tests`、正式旧 DLL x64/hash/12-export 验证与 `git diff --check` 通过。新 DLL 只存在 ignored test artifact；tracked tree 未替换正式二进制，installer task 使用旧稳定 DLL 完成并已归档。

## 版本准备

- 2026-07-12 已将 `LGSTrayUI`、`LGSTrayHID`、Inno Setup 默认版本和 `build-installer.ps1` 默认版本统一从 `1.4.1` 提升至 `1.4.2`，并通过重新 restore 更新项目引用 lock file。
- 此版本号仅表示下一版候选；在完整硬件门禁、安装器验证、发布说明确认和用户明确授权前，不创建公共 tag 或 GitHub Release。
- `dotnet restore --locked-mode`、Debug/Release build、Release `PowerTray.Tests`、`build-installer.ps1` PowerShell 语法解析和 `git diff --check` 均通过；构建产物报告 `ProductVersion 1.4.2+2dd2582...`、`FileVersion 1.4.2.0`。

### 2026-07-12 主 Windows 物理机启动检查与 SDK 复核

- PowerTray 主工作树为 `main...sync/main`，`HEAD` 与 `sync/main` 均为 `3ee0a92efc102877a60eaae4c4b16bbe8a5f24cd`，无未提交改动；公共 `origin/main` 仍停在不同提交，按本任务边界不向其 push。
- HomeLab 主工作树存在用户未提交的 `PROGRESS.md`、两份 runbook、一个 task 和未跟踪 `NUL`；本任务不改动、暂存或清理它们。
- Windows SDK 的 winget/Windows Installer 注册显示 `Windows Software Development Kit - Windows 10.0.26100.7705`；实际检查确认 `Include`、`Lib`、`bin` 的 `10.0.26100.0` 目录和 `Lib\\10.0.26100.0\\um\\x64\\hid.lib`（12,004 bytes）均存在。最新官方 SDK setup 日志为 2026-07-12 15:43--15:44，bundle 与 Desktop Libs/Headers/Tools x64 均以 exit code `0x0` 结束。因此此前 SDK 缺件现象目前已消失，仍需用主机 native clean build 完成工具链验收。
- `winget` 仍注册 `Microsoft.VisualStudio.2022.BuildTools` 17.14.35，但 `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools` 缺失，Visual Studio setup 实例注册表也为空；Visual Studio Installer 目录存在但其中缺少 `vswhere.exe`。这与 SDK 已完整但 Build Tools 注册残留相矛盾，尚不能视为主机完整工具链修复；下一步通过可见管理员 Visual Studio Build Tools 安装/修复补齐固定组件，不删除其他 SDK。
- 已通过正常 UAC 与可见 Microsoft Visual Studio Installer 执行 `modify --installPath C:\BuildTools --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 --add Microsoft.VisualStudio.Component.Windows11SDK.26100 --passive --norestart`；installer exit code 为 `0`。此前 15:37 的错误为无效 `--wait` 参数（exit code `87`），本次不再包含该参数。修复后 `vswhere -requires` 对 VC x64 tools 和 Windows11SDK.26100 的联合查询均返回 `C:\BuildTools`，installation version `17.14.37411.7`、`isComplete=true`、`isLaunchable=true`、`isRebootRequired=false`。实际 VS instance 一直位于 `C:\BuildTools`，而不是默认 Program Files 路径；此前路径检查的假阴性已更正。
- 主机使用已修复的固定工具链从全新 `%TEMP%` root 成功执行 `native/hidapi/build-hidapi.ps1`：源码 commit `5360e03d6edcb7820eda3dd0fa1f8706e82e2600`、archive SHA-256 `4DC06B08...5048`、Build Tools `17.14.37411.7`、MSVC toolset `14.44.35207`（`cl.exe` `19.44.35228.0`）、SDK `10.0.26100.0`、CMake `4.4.0`、VS 2022 x64 Release、`/MT`。产物 `native/hidapi/artifacts/host-20260712-1832/hidapi.dll` 为 x64 PE、173,056 bytes、SHA-256 `FA2477A9D3BAB60C3CE92DE9D51319F945BFFB95B5D16ED5027739A51BF22FD1`，与 VM102 固定产物一致；`dumpbin` 确认 linker `14.44`、三项关键 custom/Windows exports 存在、Authenticode 为预期 `NotSigned`。此前 ignored artifact 为不同的 170,496-byte hash `AF1C19...50FB`，未被覆盖或用作验证证据。
- 实测发现 `LGSTrayHID/libhidapi/verify-hidapi.ps1` 的 `[Runtime.InteropServices.NativeLibrary]` 仅适用于 .NET Core，在本机默认 Windows PowerShell 5 中无法运行，阻断 installer gate。已改为使用受控 `LoadLibrary`/`GetProcAddress`/`FreeLibrary` P/Invoke probe；同时把依赖 `$PSScriptRoot` 的默认 DLL 路径移到参数绑定后解析，避免 Windows PowerShell 5 在默认参数表达式中得到空路径。PowerShell parser、显式候选 DLL（新 hash）和无参数正式 DLL（旧 hash）均通过 12 required exports、x64 PE 与 Authenticode `NotSigned` 验证。固定正式 hash 尚未改动。
- `dotnet restore PowerTray.sln --locked-mode` 与全 solution Debug build 均在主机通过（`0 warning / 0 error`）。已创建隔离 Debug runtime `C:\Users\jiang\AppData\Local\Temp\PowerTray-1.4.2-hardware-20260712-1835`，其 `PowerTray.exe`/`PowerTrayHID.exe` 为 `1.4.2+3ee0a92...`，唯一 `hidapi.dll` 已替换为候选新 hash；当前正式 1.4.1 light 安装和 `%APPDATA%\PowerTray` 已完整备份至 `C:\Users\jiang\AppData\Local\Temp\PowerTray-1.4.2-validation-20260712-1835`。升级前正式状态为 version `1.4.1+1aa11403...`、FileVersion `1.4.1.0`、light、旧 DLL hash、autostart disabled。
- 通过 PnP/CIM 实测到 C547、C54D 两个 LIGHTSPEED receiver 及 `0AF7` headset USB composite/interfaces；其中 receiver 下还暴露 keyboard/HID child interfaces，尚未将任一接收器作为热插拔目标，避免依产品名/PID猜测或误禁主输入。当前 1.4.1 UI 和 helper 仍正常运行，`/health` 返回 404（该旧安装尚未包含 1.4.2 health route）。
- 尝试用已安装 1.4.1 的 `PowerTray.exe --shutdown` 正常退出，15 秒内 UI/helper 仍存活；检查对应 `origin/main` 源码确认 1.4.1 根本没有 `ShutdownEventName`/`--shutdown`，仅在托盘 Exit command 中调用 `Environment.Exit(0)`。为避免强杀 UI/helper 或留下 orphan，等待用户从托盘菜单正常选择 Exit；在此之前不启动会与旧实例共享 single-instance mutex 的隔离 1.4.2 runtime，也不进行 PnP disable/enable、DLL 正式替换或安装升级。
- 用户随后报告已关闭，但实时复核仍显示旧 `PowerTray.exe` PID `31632`、`PowerTrayHID.exe` PID `27844` 均在运行，且 `127.0.0.1:12321` 仍由 PID `31632` 监听。因此并未执行托盘应用级 Exit（可能只关闭了 Settings 窗口）；继续保持 blocker，不强制结束进程。
- 用户再次从托盘退出后，实时复核确认 `PowerTray.exe`、`PowerTrayHID.exe` 与端口 `12321` 监听均已消失，旧实例退出 blocker 已解除。用户同时提供同一深色主题下两个设备提示框一黑一白的截图，新增托盘提示框主题一致性修复与实机验证范围。
- 托盘提示框不一致的根因是 Hardcodet 为每个设备单独创建外层 `ToolTip`，原实现只让内部内容使用 `DynamicResource`，因此外层系统 chrome 和不同创建时机的内容可能保留 Windows/旧主题。`LogiDeviceIcon` 现在在每次打开前从 `Application.Current` 读取 PowerTray 当前 `TooltipBackgroundBrush`、`BorderBrushSoft`、`TextBrush`，显式同步到命名的内容元素，并把外层 wrapper 设为透明、无边框、无 padding；PowerTray 主题变化时也会在 dispatcher 上刷新各设备实例并关闭旧 popup。
- 主题修复后 `LGSTrayUI` Debug build 与 `PowerTray.Tests` Debug build 均为 `0 warning / 0 error`，`PowerTray.Tests passed`，`git diff --check` 无内容错误。修复后的隔离 `1.4.2+3ee0a92...` runtime 使用候选 hidapi SHA-256 `FA2477A9...22FD1` 启动，UI/helper 均从 `%LOCALAPPDATA%\Temp\PowerTray-1.4.2-hardware-20260712-1835` 运行；维护者随后实机检查并明确确认提示框主题问题已修复，按其要求停止额外视觉检查。

## 当前阻塞

- 2026-07-12 已在 Windows 物理机使用固定候选 DLL `FA2477A9...22FD1` 完成真实设备枚举和电量读取：`PRO X2 SUPERSTRIKE Wireless Mouse` 88%，`PRO X 2 Lightspeed Gaming Headset` 59%，二者同时在线且 identity 稳定。
- 实机验证同时发现并修复独立阻断：MessagePipe 1.8.2 的 subscriber 会无条件创建 server；原 UI/helper 共用一个双向管道时会各自连回自身，造成 helper 有进程但无心跳和设备事件。提交 `f7bafb5` 改为 `HidToUi` 与 `UiToHid` 两条单向管道，并增加双向 IPC 集成测试。
- 当前工具会话不能自动执行真实 USB 拔插；中等完整性会话无权 `Disable-PnpDevice`，Windows VM 到物理机的 SSH/WinRM 端口均未开放，而包含设备禁用/启用的提权命令被平台安全检查阻止。未发生任何半完成的设备禁用。
- 因此接收器拔插、充电线重枚举、20 次快速插拔、休眠唤醒、强制刷新、资源增长和 2 小时普通使用 smoke 仍未验证。此前用户排除的 24 小时测试没有重新加入。
- Windows VM 使用固定 SDK 10.0.26100.0 再次重建出预期 `FA2477A9...22FD1`，但遵守 fail-closed 门禁，在上述测试完成前已将未提交的正式 DLL 替换回滚；当前正式 DLL 继续为 `38BDA32F...B4D`。本 task 保持 active/blocked。

## 提交

- `77097bb` (`Build reproducible hidapi and validate installers`)：固定源码/工具链/native build、ABI 门禁、shutdown 修复、RID lock、安装器编译与 smoke 结果；正式 hidapi DLL 未替换。
- `9e3248f` (`Record PowerTray validation status`)：记录 blocked 硬件门禁、安装器 task 归档与实现提交。
- `f7bafb5` (`Fix bidirectional native IPC`)：修复 UI/helper 单管道自连接问题，增加双向命名管道集成测试，并让 native 构建脚本在 PowerShell 5 下兼容且固定 SDK 10.0.26100.0。
- `efa1ed2` (`Fix hidapi verification on Windows PowerShell`)：让 native export probe 与默认 DLL 路径在 Windows PowerShell 5 下可用。
- `b6333b1` (`Fix tray tooltip theme consistency`)：每次打开前同步 PowerTray 当前调色板到每个设备 tooltip，并记录主机修复、测试与维护者实机确认。

## 设备同步

- Windows VM102 `C:\dev\repos\logi\LGSTrayBattery-master` 已推送并验证为私有 `origin/main` `9e3248f`。
- Ubuntu VM101 `~/src/repos/logi/LGSTrayBattery-master` tracked tree 干净，执行 `pull --ff-only` 后已验证 local/origin 均为 `9e3248f`。
- 当前 Windows 物理机清单路径 `D:\dev\repos\logi\LGSTrayBattery-master` 在 VM102 不存在，无法从本设备检查或同步，记录为 `pending-device-sync`，不冒充已同步。
- 当前 Windows 物理机已将 `efa1ed2` 与 `b6333b1` 推送到私有 `sync/main`；`git ls-remote` 复核 local HEAD 与 `sync/main` 均为 `b6333b1516aa61cbb426b8eb38e851eea5f83b66`，tracked/untracked worktree 均干净。公共 `origin` 未改动。
- 托盘主题修复完成后继续原 1.4.2 总任务；隔离候选 UI/helper 自 2026-07-12 18:49 起保持运行，恢复两小时资源与设备状态采样，并并行完成无需物理操作的接口、事件日志和文档门禁。只有再次到达确实需要接收器拔插、耳机充电线或系统休眠的步骤时才请求维护者动作。
- 深色托盘右键菜单第一次修复后的实机截图仍显示两类回归：自定义 Header 中的禁用文字继续被 `ContextMenu.Resources` 内隐式 `TextBlock.Foreground` 覆盖，设备子菜单的独立 `Popup` 仍无法可靠解析父菜单动态背景资源。第二轮修复删除冲突的 `TextBlock.Foreground`，让所有标题继承 `MenuItem.Foreground`，并把子菜单 Border 绑定到 `PART_Popup.PlacementTarget.Tag`；每次打开前仍由 `TrayContextMenuPlacement.ApplyCurrentTheme` 显式写入当前调色板。
- 维护者随后提供的最新截图确认第二轮仍有设备子菜单背景透明/回退问题。第三轮改为让 Popup Border 通过 `TemplateBinding Tag` 直接读取其模板宿主 `MenuItem` 上已解析的当前主题背景，不再跨 Popup 名称作用域取 `PlacementTarget`；子菜单边框同样通过 `TemplateBinding BorderBrush` 获取，打开菜单前显式刷新到所有已实现菜单项。设备名称 `TextBlock` 也显式绑定所属 `MenuItem.Foreground`，避免独立 Popup 中的文字继承链失效。`LGSTrayUI` Debug build 为 `0 warning / 0 error`，`PowerTray.Tests passed`，`git diff --check` 通过；新 `PowerTray.dll` SHA-256 为 `BFBC1550...EDE9`，已通过 `--shutdown` 优雅替换进隔离 runtime 并重启，PowerTray/PowerTrayHID 均响应且无新 PowerTray runtime error。
- 最新实机截图又确认设备名称仍回退为黑色，说明继续依赖 `MenuItem.Foreground`、`DynamicResource` 或 Popup 生成时机都不可靠。最终修复不再逐项补丁：`NotifyIconResources.xaml` 定义 8 个 `po:Freeze="False"` 的共享 `SolidColorBrush`，覆盖背景、边框、正文、次要文字、悬停、分隔线、禁用文字和强调色；根菜单、所有 Header、动态设备 `DataTemplate` 与独立 Popup 全部只引用这些静态共享实例。每次菜单打开前，`TrayContextMenuPlacement` 仅把当前 PowerTray 主题颜色复制到共享实例，不再遍历未生成的 `ItemsSource` 容器，也不再依赖跨 Popup 的资源继承。Debug build 为 `0 warning / 0 error`，`PowerTray.Tests passed`；新隔离 runtime `PowerTray.dll` SHA-256 为 `67BC0CF0...BCC1A`，UI/helper 已优雅重启并保持响应。剩余只需维护者做一次深色/浅色最终目视确认。
