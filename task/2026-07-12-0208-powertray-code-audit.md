# PowerTray 代码逻辑与安全审计

- Task ID: `2026-07-12-0208-powertray-code-audit`
- 创建时间: `2026-07-12 02:08 +09:00`
- 状态: `completed`
- 完整报告: `task/archive/2026/2026-07-12-0208-powertray-code-audit.md`
- 说明: 当前 DevSpace 文件接口不提供 rename/delete；归档报告已生成，活动文件暂保留为完成指针，未冒用 shell 修改项目文件。
- 仓库: `D:\dev\repos\logi\LGSTrayBattery-master`
- 基线: `main` / `f40068846ba9825552bf01e403f13c5bdaf21747`
- 私有远端: `sync/main` 与本地一致
- 公开远端: `origin/main` 为 `1aa11403fb108774ce6745d9c536845a63c895dc` (`v1.4.1`)

## 目标

详细审计 PowerTray 当前代码的逻辑缺陷、安全边界、并发与生命周期、隐私、更新链路、HTTP/IPC、HID、设置、告警、安装器和 CI，并输出按严重度排序的报告。

## 范围

- `LGSTrayHID`
- `LGSTrayCore`
- `LGSTrayPrimitives`
- `LGSTrayUI`
- `PowerTrayInstaller.iss`
- `build-installer.ps1`
- `.github/workflows/build.yml`
- 当前依赖与已有测试覆盖

## 不做事项

- 不修改业务代码。
- 不回复或关闭 GitHub Issue。
- 不 commit、push、发布或部署。
- 不覆盖上一轮未跟踪调查报告。
- 不执行需要真实硬件状态变化的测试。

## 检查项

- [x] 核对本地、私有远端和公开远端基线。
- [x] 审计更新下载与执行链路。
- [x] 审计 HTTP 服务与外部输入处理。
- [x] 审计 IPC、子进程和单实例边界。
- [x] 审计 HID 发现、轮询、重连和并发资源生命周期。
- [x] 审计设备身份合并、在线/离线状态机。
- [x] 审计设置持久化、告警和 UI 状态。
- [x] 审计诊断导出和隐私边界。
- [x] 审计安装器和构建供应链。
- [x] 运行必要的构建、测试、依赖漏洞与静态检查。
- [x] 记录发现、证据、优先级和修复建议。
- [x] 完整归档报告已写入 `task/archive/2026/`。

## 已知本地状态

- 用户已有未跟踪文件：`task/archive/2026/2026-07-12-0157-powertray-ci-issue-investigation.md`，保持不动。
- 本任务创建后新增本活动 task 文件。

## 最终结果

- 未发现默认配置下可由互联网直接利用的 Critical 漏洞。
- 确认 3 项高优先级可靠性缺陷：约 42 秒全量 HID Rediscover、单次失败立即离线且相同电量恢复不发 UPDATE、HID Session/句柄/线程生命周期竞态。
- 确认多项中优先级问题：helper 监督器静默停止、命令匹配/排队假超时、设备身份误合并、诊断请求缺失和隐私过采集、离线幽灵告警、HTTP 跨线程与远程无认证、更新器 TOCTOU、安装器 PATH dotnet 搜索、GDI Icon 泄漏、G Hub 本地冒充/竞态。
- Debug/Release 构建、latest-all analyzer、现有测试、30 次重复测试、NuGet 漏洞和弃用审计均通过。
- 完整证据、优先级和修复建议见归档报告。
- 未修改业务代码、未回复 Issue、未 commit/push/release。
