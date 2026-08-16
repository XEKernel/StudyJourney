# 学程 StudyJourney — 软件架构实现

> 版本：2.5.0 ｜ 框架：.NET 10 + Avalonia 12 ｜ 最后更新：2026-08-16

## 1. 项目概览

学程是一个学生桌面伴侣应用，形态为**灵动岛式顶部状态栏小组件**（深色扁平 + 大圆角胶囊），持续显示时间、当前课程、天气与高考/自定义倒计时；上课自动收起为进度条，有窗口时自动隐藏，支持点击穿透、位置预设、托盘与全局快捷键。当前版本叠加了**远程管理模块**：班级电脑（教师机）开启 HTTP 服务后，老师可用浏览器远程查看/修改课表、投递课件并自动打开、查看班级电脑状态。

> 📦 **框架迁移已完成（v2.1.0 → v2.5.0）**：原 WPF（.NET 8）版本已废弃，源码归档在 `LegacyWPF/` 目录（本地保留，不入库）；仓库只维护 Avalonia 新框架工程（`StudyJourney.Avalonia/`）。

| 项 | 技术选型 |
|---|---|
| UI 框架 | Avalonia 12（`Avalonia.Desktop` + `Avalonia.Themes.Fluent`） |
| 主题 | FluentAvaloniaUI 3.0.2（WinUI 3 风格，强调色校园蓝 `#2B6CB0`，强制 Dark） |
| 远程服务 | ASP.NET Core Minimal API + Kestrel（`FrameworkReference Microsoft.AspNetCore.App`，后台线程运行） |
| 序列化 | System.Text.Json（全项目统一） |
| 目标框架 | net10.0，WinExe，编译绑定（`AvaloniaUseCompiledBindingsByDefault`） |
| 发布 | GitHub Releases（框架依赖版 + 自包含版），Release 自动签名 |

## 2. 总体架构

```
┌────────────────────────────────────────────────────────────────────┐
│                       进程（单实例 Mutex）                           │
│                                                                    │
│  ┌─ UI 层（Avalonia Dispatcher / UI 线程）──────────────────────┐  │
│  │  MainWindow（灵动岛）  SettingsWindow   ExamModeWindow       │  │
│  │  ScheduleEditorWindow  ColorPickerDialog                      │  │
│  └──────────────────────────────────────────────────────────────┘  │
│  ┌─ 服务层─────────────────────────────────────────────────────┐  │
│  │  ReminderService（提醒）  UpdateService（更新）              │  │
│  │  WeatherService（天气）   HttpServerService（远程 HTTP ★）    │  │
│  └──────────────────────────────────────────────────────────────┘  │
│  ┌─ 数据层（Models）───────────────────────────────────────────┐  │
│  │  AppSettings   ScheduleData/ScheduleManager/ExamEntry       │  │
│  │  CustomCountdown  TimeTemplate                               │  │
│  └──────────────────────────────────────────────────────────────┘  │
│  ┌─ 基础设施（Helpers）────────────────────────────────────────┐  │
│  │  AppLogger  DialogHelper  GlobalHotKeyManager  SystemToast  │  │
│  │  WindowLayerHelper  ColorUtils                               │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                                                                    │
│  后台线程：HttpServer 线程（Kestrel 生命周期）＋线程池（请求处理）   │
│  外部依赖：GitHub API（更新） 和风天气 API  局域网浏览器（远程管理） │
└────────────────────────────────────────────────────────────────────┘
```

**架构要点**：
- **静态全局服务入口**：`App` 静态持有 `Settings` / `Schedule`（ScheduleManager）/ `Reminders`，各窗口通过 `App.xxx` 直接访问；`App.SettingsChanged` 事件驱动 UI 刷新（设置页保存 → 广播 → 主窗口 ApplySettings）。
- **UI 线程隔离**：所有控件操作必须在 Dispatcher 线程；后台服务（HTTP）不触碰 UI，通过事件/Dispatcher.Post 回 UI。
- **远程模块低耦合**：`HttpServerService` 是静态类，只依赖 `Models`（ScheduleData）与 `AppLogger`，反向不依赖 UI。

## 3. 目录结构

```
根目录
├─ StudyJourney.Avalonia/    ★ 当前唯一维护的新框架工程
├─ Updater/                  更新器（独立 exe，Avalonia 自动更新调用）
├─ LegacyWPF/                ⚠ 旧 WPF 版本源码（已废弃，本地归档保留，不入库）
├─ ClassIsland-2.1.0.1/      第三方参考样本（迁移风格参考，不入库）
├─ settings.json / schedule_example.json / schedule_test.json   数据与模板（两版共用格式）
├─ StudyJourney.pfx          代码签名证书（CI 自动签名）
└─ ARCHITECTURE.md / README.md / MIGRATION_AVALONIA.md / CODE_REVIEW*.md  文档

StudyJourney.Avalonia/
├─ Program.cs            入口：单实例 Mutex → BuildAvaloniaApp
├─ App.axaml(.cs)        主题、静态全局（Settings/Schedule/Reminders）、托盘/快捷键/更新/考试自动进入
├─ Views/
│  ├─ MainWindow.axaml(.cs)        灵动岛主窗口（核心 UI + 交互）
│  ├─ SettingsWindow.axaml(.cs)    设置页容器 + Settings/ 子页面
│  ├─ Settings/ServerPage.axaml(.cs)   ★ 远程服务设置（开关/上传目录/班级信息/老师账号/可选科目/日志）
│  ├─ Settings/{Countdown,Position,Api,Schedule,Exam,About}Page.axaml(.cs)
│  ├─ ExamModeWindow.axaml(.cs)    考试模式全屏窗
│  └─ ScheduleEditorWindow.axaml(.cs)  课表编辑器
├─ Models/
│  ├─ AppSettings.cs        设置模型 + TeacherAccount（多老师账号）+ 可选科目（settings.json）
│  ├─ ScheduleEntry.cs      ScheduleEntry/ExamEntry/TimeTemplate/ScheduleData（schedule.json）
│  └─ ScheduleManager.cs    课表管理器（查询/导入/事件）
├─ Services/
│  ├─ ReminderService.cs    上课/下课/考试提醒（500ms 轮询 + 声音）
│  ├─ UpdateService.cs      GitHub Releases 更新检查/下载
│  ├─ WeatherService.cs     天气（和风）
│  └─ HttpServerService.cs  ★ 远程 HTTP 服务（Minimal API + 认证 + 上传 + 日志）
├─ Helpers/
│  ├─ AppLogger.cs          文件日志（1MB 轮转）
│  ├─ DialogHelper.cs       弹窗封装
│  ├─ GlobalHotKeyManager.cs 全局快捷键（RegisterHotKey）
│  ├─ SystemToast.cs        Windows 通知
│  ├─ WindowLayerHelper.cs  前台窗口/系统外壳判断
│  └─ ColorUtils.cs         颜色/天气图标工具
└─ Assets/                 图标等 avares 资源
```

## 4. 进程与线程模型

| 线程 | 用途 | 说明 |
|---|---|---|
| UI 线程（STA） | Avalonia 渲染 + 控件 | 所有 DispatcherTimer（1s 时钟、500ms 隐藏检测、天气、提醒）在此触发 |
| HttpServer 线程 | Kestrel 生命周期 | `new Thread(RunServer)` + `ManualResetEventSlim` 阻塞等待；监听循环实际跑在 Kestrel 内部线程池 |
| 线程池 | HTTP 请求处理 | Minimal API 端点；`Process.Start` 直接调用，无需回 UI |
| 更新子进程 | 自动更新 | UpdateService 下载后拉起 `Updater` 子进程替换 exe |

**关键点**：`HttpServerService.Start()` 同步等待就绪（5s 超时，`AggregateException` 解包真实异常）；`Stop()` 置位信号让后台线程自行 `StopAsync` + `DisposeAsync`，不阻塞调用方。

## 5. 应用生命周期

```
Program.Main
 ├─ 单实例 Mutex（GaokaoCountdown_SingleInstance_XEKernel）
 │    └─ 失败 → FindWindow("学程") 激活已有实例并退出
 └─ StartWithClassicDesktopLifetime
      └─ App.OnFrameworkInitializationCompleted
           ├─ AppLogger.EnableFileLogging()
           ├─ Settings = AppSettings.Load()            // settings.json
           ├─ 创建 MainWindow（ApplyCapsuleStyle + 事件订阅）
           ├─ Reminders = new ReminderService(...)     // 500ms 轮询
           ├─ SetupTrayIcon() + SetupGlobalHotKeys()
           ├─ 延迟 5s 自动检查更新；当天有考试 → 延迟 2s 自动进考试模式
           └─ _mainWindow.Show()
退出：OnClosing 淡出 → Cleanup（快捷键/提醒/托盘）→ HttpServerService.Stop()
```

## 6. 核心模块实现

### 6.1 主窗口（灵动岛小组件）
- **双视图**：`OuterCapsule`（完整视图：时间 | 课表 | 天气 → 倒计时 → 每日一言 → 远程管理行）与 `CompactCapsule`（上课时只剩"科目 剩余时间 + 进度条"），切换带 200ms 淡入上移动画。
- **胶囊样式**：`IslandSeparated` 决定"单条大胶囊"还是"多块分离胶囊"，圆角由 `MainWindowCornerRadius` 统一驱动，代码内 `SetCapsule` 批量应用。
- **点击穿透**：`WS_EX_TRANSPARENT + WS_EX_LAYERED + WS_EX_NOACTIVATE`，并 WndProc 子类化——Avalonia 12 的 WndProc 会自行处理 WM_NCHITTEST 返回 HT_CLIENT 覆盖穿透，必须主动返回 HT_TRANSPARENT（委托需持有引用防 GC）。
- **位置预设**：顶部/中上/居中/中下/底部/自定义，`PositionChanged` 回写自定义坐标；视图切换用 `_pendingReposition` 等布局完成再定位。
- **隐藏策略**：`MaximizeCheckTimer`（500ms）检测前台窗口，非系统外壳时自动隐藏；考试模式隐藏；`_suppressAutoHide` 豁免用户主动显示。
- **每秒 Tick**：刷新时间、课程状态、倒计时环；`GetCurrentEntry`（预备铃提前 2 分钟）与跨天课逻辑由 ScheduleManager 提供。

### 6.2 设置系统
`AppSettings`（POCO，`JsonIgnore` 颜色属性 + Hex 序列化代理）→ `settings.json`（exe 目录，与 WPF 版共用）。`App.SaveSettings()` 保存并触发 `SettingsChanged` 广播，主窗口订阅后统一 `ApplySettings()`（透明度/强调色/字体/胶囊样式/穿透/位置）。

### 6.3 课表系统
- **模型**：`ScheduleData { Entries, Exams, TimeTemplates }`；`ScheduleEntry` 含 `StartTimeStr/EndTimeStr`（运行时解析 TimeSpan）与跨天判定（EndTime < StartTime）。
- **ScheduleManager**：加载/保存/JSON 导入（防 `Entries/Exams` 为 null）；查询当前课（含预备铃）、下一节、跨天课凌晨延续、考试科目；`DataChanged` 事件通知提醒服务刷新缓存。
- **文件路径**：`Documents\StudyJourney\schedule.json`（第 4 轮迁移统一，Load 时从 exe 目录旧文件自动复制）。

### 6.4 倒计时系统
高考倒计时（固定）+ 自定义倒计时（动态 `RingHost`），环形/条形两种样式，时间精度（天/时/分/秒）可配；`CheckCountdownExpiry` 按 `name|date` 去重触发一次性提醒。

### 6.5 提醒服务
500ms DispatcherTimer 轮询课表，按时间窗（±0.5s~1s）触发 13 类提醒（上课/课中 20min/下课/放学前 10min·1min/下节 5min/早读/晚自习/晚读/考试结束前 15min），`_firedKeys` 按天去重；声音用 `user32.MessageBeep` / `winmm.PlaySoundW`（自定义 wav）；呈现方式由 `ReminderStyle` 二选一：胶囊弹窗（3s 淡出）/ Windows 通知。

### 6.6 天气与每日一言
WeatherService（和风 API，按详情级别渲染）+ 每日一言（用户配置 API，字段名可配），均有独立 DispatcherTimer 定时刷新。

### 6.7 考试模式
ExamModeWindow 全屏窗，`GetCurrentExamSubject/GetNextExamSubject` 驱动；开考自动进入（设置开关）。

### 6.8 自动更新
UpdateService：`AssemblyInformationalVersion` 取版本（含 beta，去 `+hash`）→ 请求 GitHub Releases（XEKernel/StudyJourney）→ 对比版本 → 提示 → 下载（自包含检测）→ 拉起 Updater 子进程替换并退出。

### 6.9 托盘与全局快捷键
Avalonia 内置 `TrayIcon + NativeMenu`（显示/隐藏、考试模式、设置、退出）；`GlobalHotKeyManager`（RegisterHotKey）注册 Ctrl+Shift+H / Ctrl+Shift+E。

## 7. 远程管理模块（HttpServerService）

### 7.1 设计
静态类；`Start(url="http://*:8080")` 在**独立后台线程**构建 `WebApplication`（`UseUrls` 通配符 = 全接口绑定，局域网任意设备可访问），`app.Start()` 同步就绪后 `ManualResetEventSlim.Wait()` 阻塞线程；`Stop()` 置位信号 → 线程 `StopAsync + DisposeAsync`。Kestrel 通配符绑定无需管理员权限/URL ACL。

### 7.2 API 一览

| 方法 | 路径 | 认证 | 说明 |
|---|---|---|---|
| GET | `/api/health` | 否 | 连通性测试 `{status:"ok"}` |
| GET | `/api/teachers` | 否 | 老师账号列表（用户名/显示名/科目，**不含密码**），登录页下拉 |
| POST | `/api/login` | 否 | 多老师账号密码换 Token（rememberMe=true → 1 年 + 落盘） |
| POST | `/api/logout` | Token | 使 Token 失效并移出 tokens.json（踢下线） |
| GET | `/api/config` | Token | 班级名 / 当前登录老师显示名 / 可选科目（选科） |
| PUT | `/api/config` | Token | 改班级名 + 当前登录老师自己的显示名（账号表持久化 + 即时生效） |
| GET | `/api/schedule` | Token | 返回完整课表（schedule=ScheduleData，camelCase，与 PUT 同构） |
| PUT | `/api/schedule` | Token | 覆盖写课表（无效 JSON → 400，写失败 → 500） |
| GET | `/api/status` | Token | 班级状态（IP/磁盘/最近上传/运行时长） |
| GET | `/api/upload-dir` | Token | 上传目录 + 预设列表（默认/桌面/桌面\课件/文档/下载） |
| PUT | `/api/upload-dir` | Token | 改上传目录（写 settings + **立即生效**） |
| GET | `/api/logs` | Token | 操作日志（最近 100 条，按老师显示名记名） |
| POST | `/api/upload` | Token | multipart 课件上传 + 自动打开 |
| GET | 静态文件 | 否 | WebRoot（index.html 控制台、upload-test.html） |

### 7.3 认证体系
- **Token**：`Guid.NewGuid()`，内存 `Dictionary<string, DateTime>`；rememberMe=true 过期 `UtcNow.AddYears(1)` 并持久化到 **exe 目录 `tokens.json`**（启动 `LoadTokens` 恢复，跳过过期项 → 重启免登录）；false 为 8h 内存临时会话。
- **中间件**：`app.Use` 统一处理——`/api/*`（除 login/health）必须携带有效 Token（支持 `Authorization: Bearer`、`X-Token` 头、`?token=`），无效 401；过期 Token 惰性删除。
- **凭据**：常量 `Teacher01 / Study@2026`（改密码改常量即可）。

### 7.4 IP 白名单（灵活开关）
`EnableIpWhitelist` 静态开关，**默认 false 放行所有内网**（老师连不同 WiFi 也能访问）。开启后读 `Documents\StudyJourney\whitelist.txt`（一行一前缀、`#` 注释、空/缺文件放行），回环永远放行，`::ffff:` IPv4 映射先归一化再前缀匹配，命中返回 403。

### 7.5 课件上传
- 500 MB 上限需**双放宽**：`Kestrel Limits.MaxRequestBodySize`（默认 30MB）+ `FormOptions.MultipartBodyLengthLimit`（默认 128MB），超限 `ReadFormAsync` 抛 `InvalidDataException` → 413。
- 后缀白名单（忽略大小写）：`.pptx/.ppt/.docx/.doc/.pdf/.mp4/.avi/.mkv/.zip/.rar`。
- 安全：`Path.GetFileName` 清路径遍历成分 + `yyyyMMdd_HHmmss_` 时间戳前缀防重名；流式写入。
- 自动打开：`Process.Start(UseShellExecute=true)` 直接调用（工作线程无需回 UI），失败仅追加 `openWarning`，不影响保存成功。

### 7.6 课表远程读写
GET/PUT 共用 `ScheduleData` 格式（前端表格数据源即接口格式）；PUT 反序列化 → `SortEntries()` → `Save()`。路径统一为 `Documents\StudyJourney\schedule.json`，与主程序共用（远程改的课表主程序下次加载即生效）。

### 7.7 教师端控制台（index.html）
内联字符串嵌入 C#（`EnsureWebRoot` 每次启动覆盖写，零外部文件）：深色渐变 + 毛玻璃、校园蓝、直角；三 Tab——课表管理（周课表 行=节次列=周一~周日，双击编辑，保存 PUT）、课件投递（拖拽/点击 + XHR 进度 + 前端后缀预校验 + "班级电脑已自动打开课件！"）、班级状态（运行指示灯 15s 轮询 / IP / 磁盘 / 最近 5 文件）；localStorage 恢复 Token；fetch 统一封装自动带 Bearer、401 自动登出、Toast 提示。

## 8. 数据文件布局

| 文件/目录 | 位置 | 用途 |
|---|---|---|
| `settings.json` | exe 目录 | 应用设置（与 WPF 版共用） |
| `schedule.json` | `Documents\StudyJourney\` | 课表（远程 + 主程序共用，含旧文件迁移） |
| `tokens.json` | exe 目录 | 持久化登录 Token（rememberMe） |
| `whitelist.txt` | `Documents\StudyJourney\` | IP 白名单前缀（可选） |
| `WebRoot/` | `Documents\StudyJourney\` | 静态页（index.html 控制台 / upload-test.html） |
| `Uploads/` | `Documents\StudyJourney\` | 课件上传目录（含 `.placeholder`） |
| `logs/app.log` | exe 目录 | 应用日志（1MB 轮转） |

## 9. 线程安全与并发

- **锁**：`HttpServerService` 内部 `Gate`（app 生命周期）、`TokenGate`（Token 字典）、`WhitelistGate`（白名单列表）——请求并发下的最小临界区。
- **令牌快照**：写 tokens.json 前在锁内拷贝字典，避免长 IO 持锁。
- **UI 隔离**：`Dispatcher.UIThread.Post/InvokeAsync` 是后台→UI 的唯一通道（提醒、更新弹窗）。
- **数据一致性**：课表远程写入与主程序读取共用同一文件，写入走 `WriteAllText`（原子性足够班级场景）。

## 10. 安全设计

1. 认证：随机 Token + 过期时间 + 持久化；401 统一拦截。
2. IP 白名单（可选，默认开放）+ 回环豁免。
3. 上传：后缀白名单、500MB 上限、路径遍历防护、时间戳防重名。
4. 宏病毒提醒：类头注释明确要求班级电脑装杀毒软件、开启 Office「受保护的视图」。
5. 单实例 Mutex 防多开；Token 明文存 exe 目录（班级场景可接受，注释已说明）。

## 11. 构建、签名与发布

- `dotnet build/publish` 标准流程；Release 自动签名（`StudyJourney.pfx` + `SIGN_PASSWORD` 环境变量/CI Secrets + DigiCert 时间戳），本地无密码时跳过由 CI 签名。
- 版本号：csproj `Version` 同时驱动 `AssemblyVersion`/`InformationalVersion`（更新检查解析用）。
- CI：GitHub Actions 产出框架依赖版 + 自包含版，Release 上传 → UpdateService 检查下载。

## 12. 关键设计决策与踩坑记录

| 决策/坑 | 结论 |
|---|---|
| 后台 HTTP 服务 | 独立 Thread + ManualResetEventSlim 生命周期，`app.Start()` 同步就绪，杜绝阻塞 UI |
| `UseStaticFiles` 不提供默认文档 | 必须先 `UseDefaultFiles(FileProvider=...)`，否则 `/` 404 |
| Kestrel/FormOptions 默认限制 | 30MB / 128MB → 上传接口必须显式放宽到 500MB |
| FluentAvalonia 3.0.2 无 ToggleSwitch | 用 Avalonia 内置 `<ToggleSwitch>` |
| `WebApplication` 无同步 Dispose | `DisposeAsync().AsTask().Wait()`；`app.Start()` 需 `using Microsoft.Extensions.Hosting` |
| schedule.json 路径统一 | 从 exe 目录迁到 Documents\StudyJourney\，Load 自动迁移旧数据 |
| 点击穿透被 Avalonia 覆盖 | WndProc 子类化主动返回 HT_TRANSPARENT，委托持引用防 GC |
| 登录持久化 | 内存字典 + tokens.json，`LoadTokens` 跳过过期项，重启免登录 |
