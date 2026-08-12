# 学程 Avalonia 重构审查报告（2026-08-12）

对比对象：
- 原版 WPF：`E:\备份\编程\C#\高考倒计时`（学程.csproj，MVVM + partial class 组织）
- 新版 Avalonia：`E:\备份\编程\C#\高考倒计时\StudyJourney.Avalonia`（Avalonia 12.0.0 + FluentAvaloniaUI 3.0.2，code-behind 组织）

结论：**数据层（Models/Services）已基本 1:1 迁移完成；View 层完成了"能看"的基础骨架，但有 40+ 项原版功能未落地，其中近半数属于完全缺失（含自动更新、单实例、全局快捷键、点击穿透、上课隐藏、动画系统、字体选择等核心特性）。另外存在明确的原型残留代码。**

---

## 一、已完成重构（对齐良好）

| 模块 | 状态 |
|---|---|
| Models/AppSettings.cs | ✅ 1:1（仅 ColorConverter→Color.Parse API 适配） |
| Models/ScheduleEntry.cs（含 ExamSubject/ExamEntry/TimeTemplate/TimetableRow/CourseSlot） | ✅ 1:1 |
| Models/ScheduleManager.cs（含 ScheduleData 持久化/导入） | ✅ 1:1（Excel 导入仍是占位，两边一致） |
| Services/WeatherService.cs | ✅ 1:1 |
| Services/ReminderService.cs | ✅ 逻辑 1:1（声音实现降级，见 P1-06） |
| MainWindow 基础倒计时（数字/进度/自定义倒计时/一言/位置预设） | ✅ 已实现 |
| ExamModeWindow 核心（倒计时/警告/蜂鸣/自动退出/ESC/F11） | ✅ 已实现 |
| 托盘图标（Avalonia TrayIcon + NativeMenu） | ✅ 已实现（菜单少了"课表栏"项） |
| Settings 基础设置（CountdownPage/PositionPage/ApiPage 大部分项） | ✅ 已实现 |
| ScheduleEditorWindow（DataGrid 编辑课表/考试） | ✅ 基础版 |

---

## 二、P0 — 完全缺失的重大功能（30 项）

### 1. 自动更新系统（原 UpdateService.cs 整文件缺失）
- 原版：`UpdateService.cs`（GitHub Release 检查 + 自包含/框架依赖匹配 + `StartUpdateAsync` 启动 Updater）+ 启动延迟 5 秒检查 + 设置页手动检查按钮。
- 新版：**无 UpdateService.cs**，`AppSettings.AutoCheckUpdate` 成为死设置，没有任何消费代码。
- AboutPage.axaml 里「立即检查更新」「GitHub 仓库」按钮是**死按钮**（无 Click 事件），「启动时自动检查更新」复选框是死控件（`IsChecked="True"` 硬编码）。

### 2. 单实例 Mutex
- 原版：`App.xaml.cs` 用 `Mutex("GaokaoCountdown_SingleInstance_XEKernel")` 保证单实例，重复启动时激活已有窗口。
- 新版：`Program.cs` 无任何单实例逻辑。

### 3. 全局快捷键
- 原版：`Ctrl+Shift+H`（显示/隐藏主窗口）、`Ctrl+Shift+B`（课表栏）、`Ctrl+Shift+E`（考试模式），RegisterHotKey + WndProc。
- 新版：完全缺失。

### 4. 点击穿透（WS_EX_TRANSPARENT）
- 原版：主窗口非自定义位置时自动穿透（`ApplyClickThrough`）；课表栏 `ScheduleBarClickThrough` 可开关。
- 新版：完全没有 P/Invoke，`ScheduleBarClickThrough` 设置无效。**这是"桌面悬浮小组件"类应用的核心交互特性。**

### 5. 上课/考试期间隐藏主窗口
- 原版：`HideDuringClass` + `HideSubjects`（科目白名单，逗号分隔）+ 下课延迟 2 分钟恢复 + 考试模式打开时隐藏 + 隐藏时连课表栏进度条也隐藏。
- 新版：MainWindow 完全没实现。**PositionPage 有这三个设置项 UI，但功能未落地（设置保存后不生效）。**

### 6. 前台最大化检测隐藏（HideWhenMaximized）
- 原版：每 500ms 检查前台窗口 `GetWindowPlacement`，最大化时隐藏、恢复时淡入。
- 新版：缺失，设置无效。

### 7. 入场动画（PlayIntroAnimation）
- 原版：启动/恢复时 1250ms 数字 0→实际值滚动 + 进度条 PowerEaseOut 动画。
- 新版：缺失。`EnableAnimations` 设置无效。

### 8. 数字脉冲动画（PulseNumber）
- 原版：数字变化时缩放 1→1.08→1 + 透明度 1→0.72→1 三段关键帧。
- 新版：缺失。

### 9. 字体选择（FontFamily）
- 原版：设置页 `PopulateFontFamilies` 系统字体下拉（主倒计时字体 + `ExamCountdownFontFamily` 考试字体），主窗口字体族/字号联动。
- 新版：**无字体 UI**，`FontFamily` 设置不生效；考试倒计时字体族也未应用。

### 10. 窗口尺寸自适应
- 原版：`FontSize / BaseFontSize` 等比缩放窗口宽高（850×175 基准）、进度条高度。
- 新版：窗口固定尺寸，字号变大时会截断。

### 11. 数字发光效果（DropShadowEffect）
- 原版：数字/进度条带发光，颜色随 NumberColor/ProgressBarColor 同步。
- 新版：无 Effect。

### 12. 关闭淡出动画
- 原版：`FadeHelper.FadeOut` 300ms 淡出后再 Hide，`Window_Closing` 取消关闭。
- 新版：直接关闭。

### 13. 拖动坐标回写
- 原版：自定义模式拖动时 `LocationChanged` 实时写回 `CustomPositionX/Y`（设置页实时可见）。
- 新版：`BeginMoveDrag` 可拖动但**不回写坐标**。

### 14. 开机自启动（注册表）
- 原版：`GetAutoStartFromRegistry` / `ApplyAutoStart` 写 `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`，启动时以注册表实际状态同步。
- 新版：PositionPage 有 AutoStart 开关 UI，但**无注册表读写实现**，设置无效。

### 15. 课表栏紧凑/展开模式（ScheduleBarAutoCollapse）
- 原版：上课自动收缩为纯进度条（CompactRow），下课/提醒展开（FullInfoRoot），交叉淡入淡出，手动展开按钮，10s 自动收缩。
- 新版：课表栏是**固定单行布局**（XAML 无 CompactRow/FullInfoRoot 结构），`ScheduleBarAutoCollapse` 设置无效。

### 16. 下课倒计时展开 + 提示音（CountdownExpandSeconds / EnableCountdownSound）
- 原版：60s 倒计时到设定秒数时展开 + 蜂鸣提示。
- 新版：只显示「还有 Ns 下课」文本，不展开、不发声。

### 17. 课表栏多显示器支持
- 原版：`MonitorFromWindow` 获取窗口所在显示器物理宽度铺满。
- 新版：`Screens.Primary` 固定主屏，副屏上位置错误。

### 18. 课表栏入场动画 + 状态脉冲动画
- 原版：FadeIn 400ms 入场；状态文本变化时 PulseOpacity。
- 新版：均缺失。

### 19. 课表栏/考试模式天气完整显示
- 原版：天气描述 + 风 + 湿度 + 5 种颜色 + 字号 + **定时刷新（StartWeatherTimer）**。
- 新版课表栏：WeatherRow 只有图标/温度/城市 3 个元素，**无天气描述/风/湿度、无颜色应用**。
- 新版考试模式：有描述但**无字号/颜色样式应用、无定时刷新**。

### 20. 调课功能（交换/移动/代课）
- 原版：TimetableGrid 课程表网格（7 列周视图）选中单元格 → `SwapCourses`（交换）/`MoveCourse`（移动）/`SubstituteCourse`（代课）。
- 新版：只有 ScheduleEditorWindow 的 DataGrid 平铺增删改，**无网格视图、无调课操作**。

### 21. 时段模板管理
- 原版：`AddTimeSlot/DeleteTimeSlot/ApplyTimeTemplate/ApplyShiftRest`（模板套用到全部星期/顺延调休）。
- 新版：完全缺失。

### 22. 课表/考试 JSON 导入导出
- 原版：`ImportScheduleJson/ExportScheduleJson/ImportExamJson`（OpenFileDialog/SaveFileDialog）。
- 新版：缺失。

### 23. 数据备份/恢复
- 原版：`BackupData_Click/RestoreData_Click`（settings.json + schedule.json 打包备份/恢复）。
- 新版：缺失。

### 24. 浏览选择提醒音文件
- 原版：`BrowseReminderSound_Click`（文件选择对话框选 wav）。
- 新版：SchedulePage 只有 ReminderSoundPathBox 文本框，无浏览按钮，且 ReminderService 不消费该路径。

### 25. 中英文文本自定义
- 原版：10 个文本框（ChinesePrefix/ChineseDaysText/…/EnglishSecondsText）自定义全部文案。
- 新版：CountdownPage 无这些 UI（仅 XAML 写死默认文案）。

### 26. 考试样式完整设置 UI
- 原版：7 个字号滑块（含 `ExamWarningFontSize`/`ExamEscHintFontSize`）+ **14 个颜色输入框**（Subject/Name/CountdownNormal/Warning/Critical/Distance/Info/ProgressBar/Background/BgColor/NextSubject/Warning/ProgressPct/InfoDim）+ `ExamCountdownFontFamily` 字体下拉。
- 新版 ExamPage：7 个滑块（**缺 Warning/EscHint 两个**）+ **只有 1 个颜色框（ExamBackgroundColor）**，其余 13 色无 UI 入口。

### 27. 天气颜色完整设置 UI
- 原版：5 色（City/Info/Temp/**Time**/Icon）。
- 新版 ApiPage：只有 4 色（**缺 WeatherTimeColor**），ScheduleBarWindow 也不使用该字段。

### 28. 考试模式样式细节
- 新版 ExamModeWindow 未应用：`ExamModeFontSize`（当前时间字号）、`ProgressPctTb` 字号、信息行颜色（`ExamInfoColor`/`ExamInfoDimColor` 应用到 StartTime/EndTime/Duration/CurrentTime/EscHint）、`ExamCountdownFontFamily`、天气字号/颜色。

### 29. 考试模式交互细节
- 原版：入场缩放弹入动画、**双击切换全屏**、ESC 退出弹确认框（YesNo）。
- 新版：无入场动画、无双击全屏（仅 F11）、ESC 改为"再按一次"（功能等价但体验不同）。

### 30. 考试模式自动进入 + 与课表栏互斥联动
- 原版：`AutoEnterExamMode` 当天有考试延迟 2s 自动进入；进入考试模式时隐藏课表栏、退出时恢复；提醒服务按需启动（`EnableExamMode || RemindClassStart || ShowScheduleBar`）。
- 新版：ExamPage 只有手动进入按钮（`AutoEnterExamMode` 设置无效）；无课表栏互斥联动；`Reminders.Start()` 无条件启动。

---

## 三、P1 — 部分缺失 / 降级（5 项）

| # | 项 | 说明 |
|---|---|---|
| P1-01 | 设置窗口单例 | 原版 `_settingWindow` 单例 + 重入防护（`_isOpeningSettings`），重复点击激活已有窗口；新版每次 `new SettingsWindow()`，可无限开多个 |
| P1-02 | ReminderService 自定义提示音 | 原版支持 `ReminderSoundPath` 自定义 wav（SoundPlayer），失败降级系统提示音；新版 `PlaySound()` 忽略该路径，统一 MessageBeep |
| P1-03 | 英文行字号联动 | 原版英文行字号 = `FontSize * 0.4` 动态计算；新版 XAML 硬编码 13 |
| P1-04 | PositionOffsetY | 原版定位 `y + PositionOffsetY`；新版 `PositionToPreset` 未加偏移 |
| P1-05 | ScheduleBarWidth | 原版 0=全屏、可设自定义宽度；新版固定全屏宽度，设置无效 |

---

## 四、P2 — 原型残留代码（必须清理）

1. **App.axaml.cs 启动自动弹设置窗口**（第 57-58 行）：
   ```csharp
   // 原型验证：启动后自动弹出 WinUI 3 风格设置窗口（方便直接查看设置页效果）
   _mainWindow.Opened += (_, _) => new SettingsWindow().Show(_mainWindow!);
   ```
   正式版启动会强制弹出设置窗口，**必须删除**。
2. **AboutPage 死按钮**：「GitHub 仓库」「立即检查更新」无 Click 事件；「启动时自动检查更新」无绑定。
3. **原型文案**：「版本 1.7.0（迁移中）」「这是学程从 WPF 迁移到 Avalonia 的阶段原型」「高考倒计时伴侣 · Avalonia 迁移原型」。

---

## 五、P3 — 架构差异（非缺陷，但需知情）

| 项 | 原版 | 新版 | 影响 |
|---|---|---|---|
| 分层 | 5 个 ViewModel（MainWindow/ScheduleBar/ExamMode/Weather/PeriodCardItem）+ 数据绑定 | 全部 code-behind 直接操作控件 | 可测试性、可维护性下降；绑定失效风险低但逻辑耦合高 |
| 设置窗口 | 单窗口 6 Tab（TabControl 圆角胶囊） | FAAppWindow + NavigationView 6 页（FluentAvalonia） | 新版更接近 WinUI 3，符合预期 |
| 课表编辑 | 网格视图 + 调课 | DataGrid 平铺 | 功能降级，见 P0-20/21 |
| 服务生命周期 | MainWindow 持有并管理 | App 静态属性（App.Settings/Schedule/Reminders） | 简洁但全局状态，多窗口共享时需注意 |

---

## 六、修复优先级建议

1. **立即**：删除 App.axaml.cs 原型弹窗代码。
2. **第一优先（核心体验）**：UpdateService（含启动检查 + 手动按钮）、单实例、全局快捷键、点击穿透、上课/最大化隐藏、自启动注册表。
3. **第二优先（视觉）**：入场/脉冲动画、字体选择、发光效果、窗口自适应、考试样式细节（P0-26/28）。
4. **第三优先（课表管理）**：网格视图 + 调课 + 时段模板 + 导入导出 + 备份恢复。
5. **收尾**：托盘加"课表栏"菜单、设置窗口单例、天气完整字段、死按钮清理。

---

## 七、补充完成情况（2026-08-12 晚，按本报告优先级执行）

> 以下缺失项已在本轮修复（Debug/Release 均 0 错误构建通过），对应上文编号：

**P2 原型残留（全部清理）**
- 删除 `App.axaml.cs` 启动自动弹设置窗口代码
- AboutPage「GitHub 仓库 / 立即检查更新」按钮已绑定事件，「自动检查更新」复选框已绑定 `AutoCheckUpdate`；更新文案替换"迁移原型/迁移中"

**P0 核心体验（已实现）**
- #1 自动更新：`Services/UpdateService.cs` 移植完成；启动延迟 5 秒检查 + AboutPage 手动检查；`AutoCheckUpdate` 已生效
- #2 单实例：`Program.cs` Mutex（激活已有实例）
- #3 全局快捷键：`Helpers/GlobalHotKeyManager.cs`（隐藏 Win32 消息窗口）注册 Ctrl+Shift+H/B/E
- #4 点击穿透：MainWindow（非自定义位置自动穿透）+ ScheduleBarWindow（`ScheduleBarClickThrough`）均实现
- #5 上课/考试隐藏：MainWindow.Tick 检查 `HideDuringClass`/`HideSubjects`（科目白名单）+ 下课延迟 2 分钟恢复 + 考试模式互斥
- #6 最大化隐藏：`HideWhenMaximized` 500ms 轮询前台窗口
- #7/#8 动画：入场动画（数字滚动 + 进度条）+ 数字脉冲动画，`EnableAnimations` 生效
- #9 字体：CountdownPage/ExamPage 系统字体下拉 + 主窗口/考试倒计时字体族应用
- #11 发光：`DropShadowDirectionEffect`（Avalonia 12 替代 WPF DropShadowEffect）
- #14 自启动：注册表 HKCU\Run 读写（`AutoStart` 生效）
- #15-#18 课表栏：紧凑/展开双模式（`ScheduleBarAutoCollapse`）、下课倒计时展开 + 提示音（`CountdownExpandSeconds`/`EnableCountdownSound`）、多显示器（`Screens.ScreenFromWindow`）、入场淡入
- #19 天气完整字段：课表栏显示描述/风/湿度 + 全部颜色；考试模式天气样式 + 定时刷新
- #22-#25 课表管理：ScheduleEditorWindow 补 JSON 导入/导出、数据备份/恢复
- #26 中英文文本：CountdownPage 10 个文本输入框已绑定（`ChinesePrefix`…`EnglishSecondsText`）
- #28/#29 考试样式 UI：ExamPage 补齐 14 色 + 2 字号滑块（Warning/EscHint）+ 字体下拉；ApiPage 补 `WeatherTimeColor`
- #30-#32 考试模式细节：入场动画、双击全屏、`ExamModeFontSize`/信息行颜色/字体族全部应用
- #33 托盘：加「课表栏」菜单项（带 ✓ 状态）

**P1 降级（已修复）**
- P1-01 设置窗口单例（MainWindow.OpenSettings 防重复）
- P1-02 `ReminderSoundPath` 自定义 wav 生效（winmm `PlaySoundW`）+ SchedulePage「浏览…」按钮
- P1-03 英文行字号联动 `FontSize * 0.4`
- P1-04 `PositionOffsetY` 应用
- P1-05 `ScheduleBarWidth` 应用（>0 时居中，0=全屏）

**架构补充**
- App 新增 `EnterExamModeGlobal()`/`OpenSettingsGlobal()` 统一入口（托盘/快捷键/设置页共用）
- MainWindow 新增公开接口 `ToggleVisibility`/`ToggleScheduleBarViaHotkey`/`EnterExamMode`/`IsScheduleBarVisible`/`OpenSettings`

**仍未实现（待后续）**
- ~~调课功能（交换/移动/代课）与时段模板管理~~ → **已于 21:05 轮完成**：ScheduleEditorWindow 新增「周视图 · 调课」Tab，含代码构建周视图网格（时段+周一~周日）、单击选源/目标（高亮）、交换/移动/代课（含确认弹窗）、时段模板行编辑（节次/开始/结束/类型 ComboBox/删除）、调休顺延（从/到下拉复制）、保存写回 Entries 并落盘
  - 技术说明：Avalonia 12 的 DataGrid **无 DataGridComboBoxColumn**（仅 Text/CheckBox/Template 列），时段模板改用代码构建行列表（ScrollViewer+StackPanel），ComboBox 直接写回 TimeTemplate.Type
- 考试模式 ESC 确认弹窗（现为"再按一次"二次确认，可接受）
- `EnableSettingsAnimationsCheck`（设置窗口自身动画开关，原版即为窗口级非持久设置）

*本轮涉及文件：App.axaml.cs / Program.cs / MainWindow.axaml(.cs) / ScheduleBarWindow.axaml(.cs) / ExamModeWindow.axaml(.cs) / SettingsWindow.axaml(.cs) / Settings/{CountdownPage,ExamPage,ApiPage,SchedulePage,AboutPage} / ScheduleEditorWindow / Services/UpdateService.cs / Services/ReminderService.cs / Helpers/GlobalHotKeyManager.cs*
