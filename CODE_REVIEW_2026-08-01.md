# 学程 v1.6.0 — 第二轮代码审查报告（2026-08-01）

审查日期: 2026-08-01 | 审查范围: 25 个源文件 / ~10262 行（含 XAML） | 对照: 上一轮 2026-07-18 报告（38 项）
> **🛠 修复完成（2026-08-01 22:58）：** 本轮全部 BUG（N-01~N-15）+ 上一轮遗留（PERF-1/2、Q-2/Q-4/Q-6）+ 屎山清理（S-1~S-8）+ 架构重组（Views/Models/Services/Helpers 分层，统一命名空间 GaokaoCountdown.*）已全部落地。
> 构建验证：Debug/Release 均 0 错误 0 警告；冒烟测试通过（启动/退出/单实例/日志）。详见 git 提交记录。


---

## 一、上一轮修复落实情况（38 项 → 已修复 / 部分修复 / 未修复）

### 🔴 严重 BUG（9 项）

| # | 状态 | 说明 |
|---|------|------|
| BUG-1 FillBehavior 压制 Opacity | ⚠️ 部分修复 | `ExamModeWindow` 已修复（`FillBehavior.Stop`+`Completed` 清动画）。但 **`MainWindow` 中 3 处仍持有动画未清除**：`MaximizeCheckTimer_Tick` 恢复淡入（`MainWindow.xaml.cs:701-710`）、`_classEndRestoreTimer` 下课恢复（`766-775`）、`Window_Closing` 淡出（`1331-1337`）。后果：主窗口透明度修改可能失效（被动画压制）；下课恢复时 Opacity 不会回到设置值。 |
| BUG-2 设置损坏崩溃 | ✅ 已修复 | `Settings.Load()` 损坏时备份+删除+返回默认值，不再 throw；`ScheduleData.Load()` 同。 |
| BUG-3 相对路径 | ✅ 已修复 | `SettingsPath` 改为 `AppDomain.CurrentDomain.BaseDirectory` 拼接（`Settings.cs:220`）；`ScheduleEntry.cs:182` 同。 |
| BUG-4 DialogHelper OKCancel | ✅ 已修复 | `DialogHelper.cs:94-113` 已加 `OKCancel` 分支（映射为 YesNo 主题按钮）。 |
| BUG-5 颜色选择器状态不一致 | ✅ 已修复 | `ColorPickerControl.xaml.cs:51-56` catch 块同时重置滑块/文本/预览。 |
| BUG-6 蜂鸣重复 | ✅ 已修复 | `ExamModeWindow.xaml.cs:23,343-351` 引入 `_lastBeepSecond` 去重。 |
| BUG-7 CloseWithFade 重复调用 | ✅ 已修复 | `DialogOverlayWindow.xaml.cs:9,42` 加 `_isClosing` 守卫。 |
| BUG-8 死代码三元表达式 | ✅ 已修复 | `ClassOverlayWindow` 已被删除（本轮确认目录中已不存在）。 |
| BUG-9 SoundPlayer GC 竞态 | ✅ 已修复 | `ReminderService.cs:56,232-234` 提升为字段 `_reminderPlayer`。 |

### 🟡 性能问题（5 项）

| # | 状态 | 说明 |
|---|------|------|
| PERF-1 每秒 O(n) 工作 | ⚠️ 部分修复 | 已缓存画刷（`_textBrushCache` 等），但 `UpdateCountdown()` 仍**每秒**调用 `UpdateCountdownDisplay()` 全量刷新（字体/字号/可见性/位置），静态部分未真正拆分。 |
| PERF-2 每秒 Measure() | ❌ 未修复 | `SyncProgressBarWidth()` 仍被每秒调用（`MainWindow.xaml.cs:867`），每次触发布局。 |
| PERF-3 每秒 ToList 分配 | ✅ 已修复 | `_cachedChineseTextBlocks/_cachedEnglishTextBlocks` 缓存（`MainWindow.xaml.cs:984-985`）。 |
| PERF-4 每秒重解析 HideSubjects | ✅ 已修复 | `_cachedHideSubjects/_cachedHiddenSet` 缓存（`MainWindow.xaml.cs:725-731`）。 |
| PERF-5 每秒遍历 CustomCountdowns | ✅ 已修复 | `_cachedNearestCountdown/_lastCountdownComputeDay` 缓存（`MainWindow.xaml.cs:1093-1116`）。 |

### 🟢 代码质量（8 项）

| # | 状态 | 说明 |
|---|------|------|
| Q-1 MainModule! 可能 NRE | ✅ 已修复 | 改用 `Environment.ProcessPath ?? MainModule?.FileName ?? ""`（`MainWindow.xaml.cs:280`）。 |
| Q-2 空 catch 过多 | ❌ 未修复 | 仍约 10 处空 catch（`Settings.cs:259`、`UpdateService.cs:138`、`WeatherService.cs:56`、`ColorUtils.cs` 等），静默吞异常。 |
| Q-3 CurrentVersion 每次反射 | ✅ 已修复 | 改为 `Lazy<string>`（`UpdateService.cs:32-42`）。 |
| Q-4 静态画刷未 Freeze | ⚠️ 部分修复 | `ColorUtils.ParseColor` 已 Freeze；但 `ScheduleBarWindow.xaml.cs:194-203` 的 12 个静态画刷仍未 `Freeze()`。 |
| Q-5 构造中 Dispatcher.BeginInvoke | ✅ 已修复 | 计时器引用已跟踪（字段）；`Dispatcher.BeginInvoke` 用于配置恢复提示，属合理。 |
| Q-6 时间解析静默失败 | ❌ 未修复 | `ScheduleEntry.StartTime/EndTime` 解析失败分别返回 `Zero`/`45min`（`ScheduleEntry.cs:49,61`），Duration 可为负。 |
| Q-7 null! 反模式 | ❌ 未修复 | `DialogHelper.cs:29,63` 仍有 `picker = null!`、`mb = null!`（需闭合 lambda 捕获，可接受但难看）。 |
| Q-8 Reminder.Invoke 异常阻塞 | ✅ 已修复 | `FireReminder` 用 `GetInvocationList` + 逐个 try/catch（`ReminderService.cs:216-220`）。 |

### 🗑️ 死代码（8 项）

| 项 | 状态 |
|----|------|
| `ClassOverlayWindow.xaml/.cs` | ✅ 已删除 |
| `BatchInputDialog.xaml/.cs` | ✅ 已删除 |
| `ReminderWindow.xaml/.cs` | ✅ 已删除 |
| `SettingWindow_Core.cs:982 EnableSettingsAnimationsCheck_Changed` | ✅ 已移除（本轮未发现该方法） |
| `SettingWindow_Schedule.cs:27 ScheduleBarOpacitySlider_ValueChanged` | ✅ 已移除（但滑杆 UI 仍存在，见新问题 N-08） |
| `OpenScheduleJson_Click` | ✅ 已移除（改为 ImportScheduleJson） |
| `DeleteScheduleEntry_Click` 空方法 | ✅ 已移除 |
| `MainWindow.xaml:113 ProgressBarGlow` | ✅ 已移除 |

> 上一轮 38 项中：**23 项已修复，3 项部分修复，3 项未修复（PERF-1/2、Q-2），另 Q-6/Q-7 属可接受反模式**。整体改善明显。

---

## 二、本轮新发现问题

### 🔴 严重 / 功能故障

**N-01 | 主窗口透明度在动画后可能永久失效（FillBehavior 残留）**
- 文件: `MainWindow.xaml.cs:701-710, 766-775`
- 现象: `MaximizeCheckTimer_Tick`（最大化隐藏后恢复）和 `_classEndRestoreTimer`（下课 2 分钟后恢复）都执行 `BeginAnimation(OpacityProperty, fadeIn)`，动画默认 `FillBehavior=HoldEnd`，**未在 Completed 中清除动画**。
- 后果: 动画结束后动画值仍"持有" Opacity 属性，此后 `UpdateCountdownDisplay()` 中 `this.Opacity = Math.Clamp(OverallOpacity,...)`（1057 行）的本地赋值被动画压制 → **用户在设置里调透明度不生效**，直到重启应用。
- 修复: 三个动画（含 `Window_Closing:1331` 淡出）的 Completed 回调中统一加 `BeginAnimation(OpacityProperty, null); Opacity = target;`。

**N-02 | ExamMode 蜂鸣逻辑与上一轮修复冲突（回归风险）**
- 文件: `ExamModeWindow.xaml.cs:342-351`
- 现象: `_lastBeepSecond` 仅在 `remaining.TotalMinutes <= 5` 时更新；**科目切换后 `_lastBeepSecond` 未重置**（`_warnShown` 重置了但 `_lastBeepSecond` 没有）。若 A 科目剩余 300 秒蜂鸣过，切到 B 科目剩余恰好也是 300 秒 → 不蜂鸣（漏提醒）。
- 修复: 在科目切换分支（353-358 行）同时 `_lastBeepSecond = -1;`。

**N-03 | `GetCurrentEntry` 预备铃逻辑在跨天课表下出错**
- 文件: `ScheduleManager.cs:41-51`
- 现象: `tod >= e.StartTime - 2min && tod < e.EndTime`。若某课 `EndTime < StartTime`（如晚自习跨零点 22:00–00:30，或用户误填），条件恒不成立/恒成立，课程永远无法正确命中。
- 附带: `ScheduleEntry.Duration` 可为负（Q-6 未修），`GetCurrentProgress` 除零保护存在但负值未 clamp 下限（`Math.Clamp(..., 0, 1)` 有下限，OK）。

### 🟠 中等问题

**N-04 | `ReminderService.OnTick` 500ms 全量遍历 + TryFire 窗口精度**
- 文件: `ReminderService.cs:80-102`
- 现象: 每 500ms 遍历**当天所有课程**做 6 种提醒判断；`TryFire` 时间窗 `[-0.5s, +1.0s)`。当课程数量多（如 8 节 × 6 提醒）每秒做 96 次时间比较，虽开销小但设计上是 O(n) 轮询，且每 500ms 遍历与 `_lastClearDay` 无关。
- 建议: 可改为按"当前时间最近触发点"计算下一次触发时刻的调度式（一次性计算今天所有提醒点，按序触发）。低优先级。

**N-05 | `ScheduleBarWindow` 入场动画同样存在 FillBehavior 压制**
- 文件: `ScheduleBarWindow.xaml.cs:88-96`
- 现象: 与 N-01 相同模式，`fadeIn.Completed` 里 `Opacity = _settings.ScheduleBarOpacity` 是本地赋值，但动画 HoldEnd 持有 → 实际上 Completed 里的赋值被压制（动画值=目标值，恰好一致所以**当前**效果 OK）；但后续 `ApplySettings()`（153-158）再改 Opacity 时**依然被动画压制** → 用户改课表栏透明度可能不生效。
- 修复: `fadeIn.Completed` 中先 `BeginAnimation(Window.OpacityProperty, null)` 再赋本地值；`ApplySettings` 中同理先清动画。

**N-06 | 全局快捷键在窗口关闭后未注销（潜在泄漏/冲突）**
- 文件: `App.xaml.cs:75-81`
- 现象: `mainWindow.Closed` 里注销快捷键，但主窗口是"隐藏式"常驻（`Window_Closing` 拦截 + Hide），只有真正退出才触发 Closed —— 逻辑正确；但若 `RegisterHotKey` 返回值未检查（66-69 行），快捷键被占用时静默失败且无提示。
- 建议: 检查返回值，失败时提示或写入 Debug。

**N-07 | `UpdateService` 版本比较对预发布版本处理粗糙**
- 文件: `UpdateService.cs:68,146-162`
- 现象: `Regex.Replace(tagName, @"^v"...)` 只去掉 `v` 前缀；若 tag 为 `v1.6.0-beta` 则 `1.6.0-beta` vs `1.6.0` 会被 `int.TryParse("0-beta")` 失败当作 0 处理 → 比较结果可能错误（beta 版被误判）。
- 建议: 用 `System.Version.TryParse` 失败时走语义化比较或直接视为更新。

**N-08 | 课表栏透明度滑杆 UI 存在但事件被删（显示不同步）**
- 文件: `SettingWindow.xaml`（滑杆） / `SettingWindow_Schedule.cs`
- 现象: 上一轮删除了 `ScheduleBarOpacitySlider_ValueChanged`，但 XAML 滑杆仍在；`ScheduleBarOpacityLabel` 只在 `LoadSettings` 时刷新一次，拖动滑杆时百分比 Label 不实时更新（需点"应用"才看到变化）。功能可用但 UX 退化。
- 修复: 恢复一个轻量 `ValueChanged` 处理器仅更新 Label。

### 🟡 轻微 / 代码卫生

| # | 问题 | 位置 |
|---|------|------|
| N-09 | `MainWindow.UpdateCountdownDisplay()` 内 `PositionWindow()` 每秒调用，重复触发 `LocationChanged` → 已有 `_isPositioning` 抑制，但每秒布局开销仍在 | MainWindow.xaml.cs:1066 |
| N-10 | `UpdateCountdown` 每秒重新设置所有 TextBlock 的 FontSize/Foreground（静态属性），与字体/颜色缓存策略不一致 | MainWindow.xaml.cs:921-1070 |
| N-11 | `ScheduleBarWindow` 静态画刷 `BrOrange/BrRed/...` 未 `Freeze()`（Q-4 遗留） | ScheduleBarWindow.xaml.cs:194-203 |
| N-12 | `ScheduleData.Save()` 与 `AppSettings.Save()` 空 catch 无日志；损坏备份文件无限堆积（每次启动损坏都会生成 .corrupted 副本） | Settings.cs:231-243 / ScheduleEntry.cs:203-211 |
| N-13 | `ExamModeWindow.ApplyAllSettings` 从 `MainWindow` 逐字段拷贝 20+ 项，容易漏同步（如 `ExamInfoDimColor` 在 ApplyStaticStyles 中未使用） | ExamModeWindow.xaml.cs:66-94 |
| N-14 | `App.xaml.cs` 单实例 Mutex 若前实例异常退出，`AbandonedMutexException` 未处理 → 新实例无法启动且无提示 | App.xaml.cs:39 |
| N-15 | `MainWindow` 构造函数 catch 后 `Dispatcher.BeginInvoke` 弹窗，但此时窗口尚未加载，`MessageBox.Show` 的 Owner 解析（`DialogHelper.GetOwner`）可能拿到 null | MainWindow.xaml.cs:305-312 |

---

## 三、屎山代码（架构与维护性）

| # | 类型 | 说明 |
|---|------|------|
| S-1 | 巨型文件 | `SettingWindow_Core.cs` 1779 行、`MainWindow.xaml.cs` 1459 行、`ScheduleBarWindow.xaml.cs` 860 行。逻辑+UI 事件+动画+服务全揉一起。 |
| S-2 | 手动属性搬运 | `MainWindow` 有 ~60 个"代理属性"（`ChinesePrefix` 等）逐个转发到 `settings`；`SettingWindow.ApplySettings()` 手动搬运 100+ 项（586-837 行）。改一个字段要动三处。 |
| S-3 | 重复动画样板 | 入场淡入/淡出模式在 4 个窗口重复编写（`BeginAnimation + Completed + 清动画`），且**清动画这一步有的写了有的没写**（N-01 根因）。建议抽 `FadeHelper`。 |
| S-4 | 魔法数字/字符串 | `PositionPreset 0-5` 魔法数字；`2 分钟`、`30 秒`、`10 秒`等硬编码时间；颜色 hex 散落 XAML 与代码。 |
| S-5 | 空 catch 泛滥 | ~10 处空 catch 静默吞异常，排障只能靠 Debug.WriteLine（发布版无日志）。建议引入 `Serilog`/`NLog` 或统一 `DebugLogger`。 |
| S-6 | 无数据绑定 | 全项目几乎无 `INotifyPropertyChanged`/`DependencyProperty` 绑定，全手动赋值刷新。可接受（小工具）但加剧 S-2。 |
| S-7 | 状态标志多而散 | `_hiddenByMaximize/_hiddenByScheduleOrExam/_isPositioning/_isCompact/_countdownExpanded/_tomorrowChecked...` 十余个布尔互相耦合，易漏重置（N-02 就是漏重置）。 |
| S-8 | 更新链路脆弱 | 主程序下载 zip → 唤起 `StudyJourney.Updater.exe`（csproj 中 `Compile Remove="Updater\**"` 排除了该子项目，发布是否带 Updater.exe 依赖 CI 手动处理）；签名 Target 无失败中断。 |

---

## 四、新功能建议（按优先级排序）

### 🥇 高价值（贴合"备考学生"场景）

| # | 功能 | 说明 | 依赖 |
|---|------|------|------|
| F-1 | **番茄钟 / 专注计时** | 25/45 分钟专注 + 5 分钟休息，支持跳过上课时段；可在课表栏显示倒计时。现有 `ReminderService` + `Countdown60Tick` 基础设施可直接复用 | 低 |
| F-2 | **作业/待办清单** | 简单任务列表（科目+截止日期），课表栏显示"下一项待办"，到期提醒。数据存独立 `tasks.json` | 中 |
| F-3 | **成绩追踪器** | 手动录入各科成绩，折线图趋势 + "距高考 X 天，较上次提高 Y 分" 激励文案 | 中 |
| F-4 | **重要日期列表增强** | 现有 `CustomCountdown` 只显示"最近一个"；升级为可滚动/轮播显示多个（一模/二模/联考/报名） | 低 |

### 🥈 中价值（体验提升）

| # | 功能 | 说明 |
|---|------|------|
| F-5 | **课程信息悬停** | 课表卡片 ToolTip 显示老师/教室/备注（`ScheduleEntry.Remark` 已存在但 UI 未展示） |
| F-6 | **主题/皮肤系统** | 预设暗色/亮色/护眼绿一键切换，颜色 hex 集中到主题资源字典 |
| F-7 | **休息提醒（护眼）** | 连续使用 45 分钟弹提醒"站起来看看远处"，复用 `ReminderService` |
| F-8 | **考试自动切换** | 考试模式科目间自动切换（当前需手动/等待下一场） |
| F-9 | **语音播报** | 上课/下课/考试倒计时用 `System.Speech` 播报，可选开关 |

### 🥉 锦上添花

| # | 功能 | 说明 |
|---|------|------|
| F-10 | **数据云同步** | 可选 WebDAV / GitHub Gist 同步 settings.json + schedule.json |
| F-11 | **多显示器支持** | `PositionWindow` 目前只认主屏（`SystemParameters.PrimaryScreen*`），支持选屏 |
| F-12 | **导出为桌面小组件** | 生成迷你置顶卡片（类似 Windows 小组件），或支持启动参数无托盘模式 |
| F-13 | **倒计时结束后的"已过 X 天"** | 高考后自动切换为"高考已过去 X 天"而非停在 0 |

---

## 五、建议修复顺序（性价比排序）

1. **N-01 + N-05（FillBehavior 清动画）** — 抽一个 `FadeHelper.FadeOut/FadeIn(window, target, ms, onDone)`，一次性修 4 个窗口。**这是最影响用户体验的隐藏 bug。**
2. **N-02（蜂鸣漏重置）** — 一行修复。
3. **Q-6 / N-03（时间解析健壮性）** — `ScheduleEntry` 校验 `EndTime > StartTime`，解析失败返回可辨识状态而非静默兜底。
4. **N-08（滑杆 Label 实时刷新）** — 补回轻量事件。
5. **N-07（版本比较）** — 换 `Version.TryParse`。
6. **S-8（更新链路）** — 确认发布流水线是否真的带上 `StudyJourney.Updater.exe`，否则自动更新形同虚设。
7. **N-14（AbandonedMutex）** — try/catch 提示"检测到异常退出，是否重置"。
8. **Q-2 / S-5（日志）** — 空 catch 至少 `Debug.WriteLine(ex)`，或引入文件日志。

---

## 六、结论

- **上一轮 38 项已修复 23 项**，主要架构性 BUG（路径、崩溃、GC、死代码）已全部落地，代码质量有实质提升。
- **本轮新发现 15 项**：3 项严重（透明度过动画失效、蜂鸣漏重置、跨天课表），6 项中等，6 项轻微。核心风险集中在 **WPF 动画 FillBehavior 未清**这一模式性问题上（N-01/N-05），建议优先系统性修复。
- 屎山主要体现为**巨型文件 + 手动属性搬运 + 重复动画样板**，短期可接受，长期建议以 `FadeHelper`、设置数据绑定（简化 ApplySettings）两项重构起步。
- 功能层面，番茄钟（F-1）与成绩追踪（F-3）最贴合目标用户（高中生备考）且可复用现有计时/存储设施。
