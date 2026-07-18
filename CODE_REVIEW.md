# 学程 v1.6.0 — 全面代码审查报告

审查日期: 2026-07-18 | 文件数: 25 个源文件 | 代码行数: ~8000 行

---

## 🔴 严重 BUG (9 项)

### BUG-1 | FillBehavior.HoldEnd 导致 Opacity 设置被忽略
**文件:** `ScheduleBarWindow.xaml.cs:90-96,395-407,687-734` / `ExamModeWindow.xaml.cs:44-55` / `ClassOverlayWindow.xaml.cs:44-59`
**原因:** `DoubleAnimation` 默认 `FillBehavior=HoldEnd`，动画结束后持有属性。`Completed` 回调中设置的本地值会被动画压制。
**影响:** 调用 `ApplySettings()` 设置 Opacity 无效；PulseOpacity 闪烁后不恢复。
**修复:** `fadeIn.Completed += (_, _) => { BeginAnimation(OpacityProperty, null); Opacity = target; };`

### BUG-2 | 设置文件损坏后抛出异常导致应用崩溃
**文件:** `Settings.cs:231-243` / `ScheduleEntry.cs:211`
**原因:** 备份损坏文件后依然 `throw` 异常。调用方未 catch，App 直接崩溃。
**修复:** 备份损坏文件后静默返回 `new AppSettings()` 默认值。

### BUG-3 | Settings 使用相对路径，工作目录变化时丢失
**文件:** `Settings.cs:220`
**原因:** `private static readonly string SettingsPath = "settings.json";` — 使用相对路径。
    打开文件对话框可能修改 `Environment.CurrentDirectory`，导致读写错误位置。
**修复:** 改为 `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json")`

### BUG-4 | DialogHelper 未处理 MessageBoxButton.OKCancel
**文件:** `DialogHelper.cs:90-96`
**原因:** `Show()` 兼容方法只判断 `YesNo`，`OKCancel` / `YesNoCancel` 均降级为 OK。
**修复:** 添加 `OKCancel` 分支处理。

### BUG-5 | 颜色选择器初始值解析失败时状态不一致
**文件:** `ColorPickerControl.xaml.cs:43-51`
**原因:** `initialHex` 解析失败时：预览显示白色，但滑块保持默认值，HEX 框显示无效值。
**修复:** catch 块中同时重置滑块和 HEX 框为白色。

### BUG-6 | ExamMode 蜂鸣每秒重复两次
**文件:** `ExamModeWindow.xaml.cs:336-338`
**原因:** 定时器间隔 500ms，每秒刷新 2 次。当 `TotalSeconds` 为偶数时两次均满足 `% 2 == 0`。
**修复:** 用 `_lastBeepSecond` 字段去重。

### BUG-7 | CloseWithFade 重复调用会多次 Close
**文件:** `DialogOverlayWindow.xaml.cs:39-55`
**原因:** 重复调用启动重叠淡出动画，Completed 多次触发 `Close()`。
**修复:** 添加 `_isClosing` 守卫。

### BUG-8 | ClassOverlayWindow 三元运算符两个分支相同（死代码）
**文件:** `ClassOverlayWindow.xaml.cs:101-103,130-132`
**原因:** `remainingSeconds > 10 ? "还有 {n} 秒" : "还有 {n} 秒"` 两个分支完全相同。

### BUG-9 | SoundPlayer GC 竞态
**文件:** `ReminderService.cs:218-219`
**原因:** `SoundPlayer.Play()` 异步播放，但 `player` 立即出作用域，GC 可能提前回收。
**修复:** 将 `player` 提升为字段或使用 `PlaySync()`。

---

## 🟡 性能问题 (5 项)

### PERF-1 | UpdateCountdown 每秒 O(n) 工作
**文件:** `MainWindow.xaml.cs:703-856`
每 tick 执行：`Split+ToHashSet`、`GetCurrentEntry`、`SyncProgressBarWidth()`、设置所有 TextBlock 字体/颜色/大小。
**建议:** 拆分为 `UpdateDynamicDisplay()`（仅数字）和 `UpdateStaticDisplay()`（字体/颜色仅在设置变更时调用）。

### PERF-2 | SyncProgressBarWidth 每秒触发 Measure()
**文件:** `MainWindow.xaml.cs:642-646`
`Measure()` 触发完整布局传递——对 1 秒定时器来说太重。

### PERF-3 | Children.OfType<TextBlock>().ToList() 每秒分配
**文件:** `MainWindow.xaml.cs:963-966`
每次 tick 分配新 `List<TextBlock>`。字体族只在设置变更时需更新。

### PERF-4 | 每秒重新解析 HideSubjects
**文件:** `MainWindow.xaml.cs:711-714`
`Split + ToHashSet` 每次 tick 重新分配。应缓存并仅在值变化时重建。

### PERF-5 | 每秒循环遍历 CustomCountdowns 并解析日期
**文件:** `MainWindow.xaml.cs:1065-1078`
应缓存最近倒计时，仅在日期变更时重新计算。

---

## 🟢 代码质量 (8 项)

| # | 问题 | 文件 | 行号 |
|---|------|------|------|
| Q-1 | `MainModule!` null-forgiving 可能 NRE | MainWindow.xaml.cs | 273 |
| Q-2 | 空 catch 块过多（共 8+ 处）静默吞下异常 | ColorPickerControl, UpdateService, WeatherService | 多处 |
| Q-3 | `UpdateService.CurrentVersion` 每次调用反射 | UpdateService.cs | 34 |
| Q-4 | 静态画刷 `BrOrange/BrRed` 未 Freeze() | ScheduleBarWindow.xaml.cs | 194-203 |
| Q-5 | Dispatcher.BeginInvoke 在构造函数中，Timer 引用未跟踪 | MainWindow.xaml.cs | 302,356 |
| Q-6 | ScheduleEntry 时间解析静默失败，Duration 可为负 | ScheduleEntry.cs | 46-61 |
| Q-7 | `null!` 反模式 | ClassOverlayWindow.xaml.cs, DialogHelper.cs | 23-24, 29 |
| Q-8 | Reminder.Invoke 一个订阅者异常阻塞后续 | ReminderService.cs | 207 |

---

## 🗑️ 死代码 — 应立即删除

| 文件 | 原因 |
|------|------|
| `ClassOverlayWindow.xaml` + `.xaml.cs` | 完全未被引用，功能已被 ScheduleBarWindow 替代 |
| `BatchInputDialog.xaml` + `.xaml.cs` | 从未被实例化 |
| `ReminderWindow.xaml` + `.xaml.cs` | 仅自引用，外部无调用 |
| `SettingWindow_Core.cs:982` — `EnableSettingsAnimationsCheck_Changed` | XAML 中无事件连线 |
| `SettingWindow_Schedule.cs:27` — `ScheduleBarOpacitySlider_ValueChanged` | XAML 中无事件连线 |
| `SettingWindow_Schedule.cs:90` — `OpenScheduleJson_Click` | XAML 中无按钮连线 |
| `SettingWindow_Schedule.cs:477` — `DeleteScheduleEntry_Click` | 空函数体，注释"不再需要" |
| `MainWindow.xaml:113` — `ProgressBarGlow` x:Name | 代码中通过 `ProgressBar.Effect as DropShadowEffect` 访问 |

---

## 💡 功能建议 (8 项)

| # | 功能 | 说明 |
|---|------|------|
| F-1 | **番茄钟/专注计时** | 可配置 25/45 分钟专注时段，利用现有计时器基础设施，跳过上课时段 |
| F-2 | **作业/待办事项** | 简单任务列表+截止日期，课表栏显示下一项待办，数据存 JSON |
| F-3 | **成绩追踪器** | 手动录入成绩，迷你统计+趋势折线图，"距高考 X 天，已提高 Y 分" |
| F-4 | **重要日期列表** | 将单一 CustomCountdown 展开为可滚动多倒计时列表（一模/二模/联考等） |
| F-5 | **课程信息提示** | 课程新增教师/教室/教材字段，鼠标悬停显示 |
| F-6 | **主题/皮肤系统** | 预设暗色/亮色/暖色/护眼绿一键切换 |
| F-7 | **眼保健操/休息提醒** | 基于时段或连续使用 45 分钟的休息提示 |
| F-8 | **数据云同步** | GitHub Gist/WebDAV 同步课表+设置，多台电脑共享 |

---

## 📊 统计

| 类别 | 数量 |
|------|------|
| 严重 BUG | 9 |
| 性能问题 | 5 |
| 代码质量 | 8 |
| 死代码文件/方法 | 8 |
| 功能建议 | 8 |
| **合计** | **38** |
