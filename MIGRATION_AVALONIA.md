# 学程 Avalonia 迁移规划

> ✅ 状态：已完成（2026-08-13）｜ 当前版本：**v2.5.0**（Avalonia）｜ 参考样本：ClassIsland 2.1.0.1
>
> 迁移已全部落地：Models/Services 层 1:1 复用，Views 层重写为 AXAML；倒计时 / 课表栏 / 考试模式 / 提醒 / 天气 / 更新 / 单实例 / 自启动 / 全局快捷键 / 点击穿透 / 动画 / 字体 / 调课模板 全部对齐 WPF 版。
>
> **WPF 版已废弃并归档**：旧 WPF 源码（App.xaml、Models/、Views/ 等）移入仓库根 `LegacyWPF/` 目录（本地保留、不入库，`.gitignore` 已忽略）；GitHub 仓库只维护 Avalonia 工程（`StudyJourney.Avalonia/`）。

## 一、目标

1. 将学程（WPF / .NET 8）迁移到 **Avalonia**，UI 采用 **WinUI 3 风格**（FluentAvalonia 主题），视觉与 **ClassIsland** 一致。
2. 迁移过程不丢功能：倒计时 / 课表栏 / 考试模式 / 提醒 / 天气 / 更新 / 单实例 / 自动启动 全部保留。
3. 数据（`settings.json` / `schedule.json`）格式不变，两版可共用配置。
4. 迁移完成后可选的额外收益：跨平台（Linux / macOS）。

## 二、技术选型

| 项 | 选择 | 理由 |
|---|---|---|
| UI 框架 | **Avalonia** | 跨平台；与 WPF XAML 语法最接近；ClassIsland 已证明可用 |
| 主题 | **FluentAvaloniaUI** | WinUI 3 视觉（Mica/圆角/流畅动画），ClassIsland 同款 |
| MVVM | **CommunityToolkit.Mvvm 8.3.2** | 已引入并完成三个窗口 MVVM 化，跨框架原样复用 |
| DataGrid | **Avalonia.Controls.DataGrid** | 设置窗口课表/考试表格需要 |
| 托盘 | **Avalonia 内置 TrayIcon**（NativeMenu） | 11.x 自带，替代 Hardcodet.NotifyIcon.Wpf |
| 颜色选择 | **Avalonia.Controls.ColorPicker** | 替代自绘 ColorPickerControl |
| 字体 | **Avalonia.Fonts.Inter** | ClassIsland 同款默认字体方案 |

### 版本组合（关键决策，两条路线）

| 路线 | 组合 | 优点 | 缺点 | 本机可行性 |
|---|---|---|---|---|
| **A（推荐）** | Avalonia **11.3.17** + FluentAvaloniaUI **2.4.1** | 与 ClassIsland 完全一致，社区踩坑最少，参考价值最大 | 需网络下载（本机缓存无此版本） | ⚠️ 依赖网络 |
| **B（兜底）** | Avalonia **12.0.0** + FluentAvaloniaUI **3.0.2** | 本机 `~/.nuget/packages/` 已有缓存，**离线可装** | FA 3.x 较新，社区样例少于 2.x | ✅ 离线可行 |

> **建议**：先试路线 A（网络已恢复过，8-12 实测 `dotnet add CommunityToolkit.Mvvm` 成功）；失败则切路线 B，零成本回退。

### 本机包安装链路（实测约束，2026-08-11/12）

- `api.nuget.org` 包源曾被网络拦截（FA 2.x 下载 404 / 只返回 3 个旧版本），但 8-12 实测已可正常 `dotnet add package`——**时通时断**。
- 兜底方案（网络再断时用）：
  1. 浏览器打开 nuget.org 官网下载目标 `.nupkg`（浏览器通道未被拦截）；
  2. 放入 `local-nuget/` 目录；
  3. 项目级 `nuget.config`：`<clear/>` + `<add key="local" value="./local-nuget"/>`；
  4. `dotnet restore` 从本地解析，其余依赖走缓存。
- 本机已有缓存（`~/.nuget/packages/`）：`avalonia 11.1.4 / 12.0.0`、`fluentavaloniaui 3.0.2`、`avalonia.themes.fluent`、`avalonia.controls.colorpicker`、`avalonia.controls.datagrid`、`avalonia.fonts.inter`、`communitytoolkit.mvvm`。
- 已知坑（记于项目记忆）：FA 2.2.0 依赖 `Avalonia.Controls.DataGrid`，需匹配版本（如 11.1.4），缺哪个包补哪个。

## 三、可复用资产盘点（学程现状 → Avalonia 可用度）

| 层 | 文件 | 复用度 | 说明 |
|---|---|---|---|
| **ViewModels** | `MainWindowViewModel` / `ScheduleBarViewModel` / `ExamModeViewModel` | 🟢 原样复用 | 纯逻辑 + CommunityToolkit.Mvvm，无 WPF 依赖 |
| **Models** | `Settings` / `ScheduleEntry` / `ScheduleManager` | 🟢 原样复用 | 纯数据 + JSON，零 UI 依赖 |
| **Services** | `ReminderService` | 🟡 微调 | `DispatcherTimer` → Avalonia.Threading（API 同名） |
| | `WeatherService` / `UpdateService` | 🟢 原样复用 | 纯 HttpClient，与框架无关 |
| **Helpers** | `AppLogger` / `DialogEnums` | 🟢 原样复用 | 无 UI 依赖 |
| | `ColorUtils` | 🟡 微调 | 颜色解析逻辑复用；返回类型 WPF Brush → Avalonia IBrush |
| | `FadeHelper` | 🔴 重写 | WPF 动画 API → Avalonia Transitions/Animation |
| **Views** | 7 个窗口 XAML + code-behind | 🔴 全部重写 | XAML → AXAML；code-behind 中"数据计算"已抽进 VM，只剩 UI 逻辑 |

> **关键结论**：上一轮 MVVM 化的最大红利在这里——**ViewModels/Models/Services 三层（约 60% 代码）跨框架直接复用**，迁移工作量集中在 Views 层（约 40%）。

## 四、WPF → Avalonia 映射表

| WPF 用法 | Avalonia 对应 | 备注 |
|---|---|---|
| `<Window x:Class>` | `<Window x:Class>` + `Avalonia.Window` | 语法兼容 |
| `AllowsTransparency=True` + `WindowStyle=None` | `TransparencyLevelHint="AcrylicBlur"` 或 `TransparencyLevel="None"` + `SystemDecorations="None"` | 无边框透明窗口做法不同 |
| `DispatcherTimer` | `Avalonia.Threading.DispatcherTimer` | 同名，直接换 using |
| `BeginAnimation(DoubleAnimation)` | `Transitions`（属性级）或 `Animation` 类 | FadeHelper 重写为扩展方法 |
| `Style x:Key` + `TargetType` + `BasedOn` | `Styles` + **Class 选择器**（`.periodCard` 等）+ `Classes.Add()` | 学程记忆里已踩过此坑（AvaloniaPort 项目） |
| `SolidColorBrush` + `Freeze()` | `SolidColorBrush`（不可变，无需 Freeze） | 删掉全部 FreezeBrush |
| `DataGrid` | `Avalonia.Controls.DataGrid` | 需包；`ItemsSource`/`SelectedItem` 用法兼容 |
| `Hardcodet.NotifyIcon.Wpf` 托盘 | `TrayIcon` + `NativeMenu` | 内置；`TrayIcon.IsVisible` 控制显隐 |
| `WindowInteropHelper` + P/Invoke（置顶/穿透） | `Topmost` 属性；穿透需平台代码（见难点 2） | Win32 部分单独抽象 |
| 自绘 `DialogOverlayWindow` / `MessageBoxControl` | FluentAvalonia **`ContentDialog`**（推荐）或自绘移植 | 用 FA 的 ContentDialog 更 WinUI 3 |
| `ColorPickerControl`（自绘） | `Avalonia.Controls.ColorPicker` | 直接替换 |
| `MouseDoubleClick` / `KeyDown` | `PointerPressed`（双击检测）/ `KeyDown` | 事件签名微调 |
| `Application.Resources` / `Styles.xaml` | `App.axaml` + `<styling:FluentAvaloniaTheme/>` | 见样式方案 |
| `FontFamily("Consolas")` | `FontFamily("Consolas")` + `Fonts.Inter` | 默认字体用 Inter |
| 注册表 `HKCU\...\Run` 自启 | 注册表逻辑保留（Windows）+ 平台判断 | 跨平台时包 `if (OperatingSystem.IsWindows())` |

## 五、样式对齐方案（"和 ClassIsland 一样"）

ClassIsland 的视觉配方（已从源码确认）：

```xml
<styling:FluentAvaloniaTheme CustomAccentColor="DodgerBlue"
                             TextVerticalAlignmentOverrideBehavior="AlwaysEnabled"/>
```

学程版执行：

1. **App.axaml** 引入 `FluentAvaloniaTheme`，`CustomAccentColor` 用学程品牌色（校园蓝 `#2B6CB0`，区别于 ClassIsland 的 DodgerBlue——保留学程自己的辨识度，风格同为 Fluent）。
2. **自定义资源** 沿用学程现有 WinUI 3 token 方案（记忆里的已验证做法）：
   - `SolidBackgroundFillColorSecondaryBrush`、`ControlElevationBorderBrush` 等 FA 专属键 **仅在 FA 下有效**（2.x+ 已无此问题）；
   - 兜底：自建资源键（accent / card bg / stroke / radii / BoxShadows）于 `App.axaml`，`{DynamicResource}` 引用。
3. **标题栏**：学程主窗口/设置窗口可用 FA 的 `TitleBar` 控件（自定义标题栏 = WinUI 3 标志性特征），设置窗口 6 Tab 纵向布局保留。
4. **圆角规范**：FA 主题自带 Mica/圆角卡片（CornerRadius 8），符合 WinUI 3 风格；学程现有的直角偏好（个人网站风格）不适用于桌面 Fluent 风格——**以 ClassIsland/Fluent 风格为准**（用户已明确"和 ClassIsland 样式一样"）。
5. **控件库**：设置窗口的 CheckBox/RadioButton/ComboBox/ScrollBar 全局样式 → FA 主题自带，删除 `Styles.xaml` 手写样式（减少维护面）。

## 六、关键技术难点（按风险排序）

1. **托盘图标（高）**：`Hardcodet.NotifyIcon.Wpf` 是 WPF 专属。Avalonia 11 内置 `TrayIcon` + `NativeMenu`，功能覆盖学程托盘菜单（显示/隐藏主窗、课表栏、考试模式、退出）。需要验证 11.3 的 API 细节。
2. **点击穿透（高）**：主窗口预设位置模式下用 `WS_EX_TRANSPARENT` 实现点击穿透；ScheduleBar 同样。Avalonia 无跨平台等价物：
   - 方案：`TopLevel.TryGetPlatformHandle()` 拿 HWND 后走 Win32 `SetWindowLong`（**与 WPF 版同一套 P/Invoke，代码可搬**）；
   - 抽象为 `WindowClickThroughHelper`，非 Windows 平台直接 no-op。
3. **自绘对话框（中）**：`DialogOverlayWindow`（透明遮罩 + 居中）移植可行；更优解是用 FA 的 `ContentDialog`（更 WinUI 3）。推荐后者，减少自绘代码。
4. **动画体系（中）**：FadeHelper 重写。Avalonia 推荐 `Transitions`（`Opacity` 等属性级过渡，声明式），替代 WPF 的 `BeginAnimation` 命令式写法；脉冲/闪烁动画用 `Animation` 类 + `Run`。注意记忆里的坑：动画结束后要清理（Avalonia 用 `Animation` 一次性动画天然无残留，比 WPF 的 FillBehavior 坑更少）。
5. **进度条动画（低）**：WPF 的 `ProgressBar.BeginAnimation(ValueProperty)` → Avalonia 用 `Transitions` 里 `ProgressBar.Value` 过渡（`DoubleTransition`）。
6. **多显示器/DPI（低）**：Avalonia 原生支持；`PositionToTop` 改用 `Screen` API（`window.Screen.AllScreens`），替换 WPF 的 `MonitorFromWindow` P/Invoke。
7. **更新链路（低）**：`Updater.exe` 是独立进程，与框架无关，原样保留；`UpdateService` 复用。

## 七、阶段划分（建议执行顺序）

### 阶段 0：骨架验证（先做，风险最高）
- [ ] 新建 `StudyJourney.Avalonia/` 独立工程（或新分支），最小编译链
- [ ] App.axaml + FluentAvaloniaTheme + 一个测试窗口，跑通：主题渲染、无边框透明、Topmost、托盘
- [ ] 验证包安装（路线 A 优先，失败切 B）
- [ ] 移植 FadeHelper（Avalonia 版）+ 点击穿透 Helper 的最小验证
- **退出条件**：空壳程序可运行，四件套（主题/透明置顶/托盘/穿透）全部生效 → 证明方案可行，进入阶段 1

### 阶段 1：核心功能移植（主窗口 + 设置）
- [ ] Models / Services / ViewModels 原样复制进新工程
- [ ] MainWindow：倒计时 UI（数据绑定已有，AXAML 重写布局）+ 入场动画 + 上课隐藏逻辑
- [ ] SettingWindow：6 Tab + 课表网格（DataGrid）+ 考试编辑（最重的一块）
- [ ] 单实例 Mutex + 自动启动 + 全局快捷键（Ctrl+Shift+H/B/E）
- **退出条件**：学程主要功能可用（倒计时/设置/课表/考试编辑）

### 阶段 2：辅助窗口
- [ ] ScheduleBarWindow（课表栏：卡片渲染逻辑保留，Win32 定位换 Screen API）
- [ ] ExamModeWindow（考试模式）
- [ ] 对话框：ContentDialog 替换自绘（MessageBoxControl / ColorPickerControl / DialogOverlayWindow 归档）
- **退出条件**：功能与 WPF 版 1:1 对齐

### 阶段 3：发布与切换
- [ ] 更新链路联调（Updater.exe + GitHub Release 检测）
- [ ] 打包：Windows（zip / MSIX 可选）
- [ ] 回归清单逐项验证（对照 WPF 版行为）
- [ ] README / 版本号 / CI 更新；稳定后 WPF 版归档为 legacy 分支
- **可选项**：Linux/macOS 打包（CI 交叉构建）

## 八、风险清单

| 风险 | 等级 | 缓解 |
|---|---|---|
| FluentAvalonia 包源再次被拦截 | 高 | 路线 B 缓存离线安装；local-nuget 兜底流程已固化 |
| 托盘在 Avalonia 11.3 的行为差异（左键/右键菜单） | 中 | 阶段 0 先行验证 |
| 点击穿透平台差异 | 中 | 只做 Windows；其他平台 no-op |
| 自绘对话框移植工作量 | 中 | 用 FA ContentDialog 替代，不移植自绘 |
| GPL 传染（ClassIsland 是 GPL-3.0） | 中 | **只参考不抄代码**；FluentAvaloniaUI 本身 MIT 可安全使用；架构/样式思想不受 GPL 约束 |
| 迁移期双版本维护成本 | 中 | 功能冻结 WPF 版，Avalonia 版功能对齐后切换 |
| XAML→AXAML 语法细节（资源/样式/转换器） | 低 | 已有 AvaloniaPort 经验（学程记忆：Class 选择器/AVLN 错误/DataGrid 依赖） |

## 九、逐文件迁移清单（Views 层）

| WPF 文件 | Avalonia 动作 |
|---|---|
| `MainWindow.xaml(.cs)` + `.Countdown/Schedule/Quote.cs` | 重写 AXAML；code-behind 只留动画/Win32/托盘 |
| `SettingWindow.xaml` + `_Core/_Styles/_Events/_Schedule.cs` | 重写 AXAML；DataGrid 换 Avalonia 版 |
| `ScheduleBarWindow.xaml(.cs)` + `.Render/.Interact.cs` | 重写 AXAML；卡片渲染逻辑保留 |
| `ExamModeWindow.xaml(.cs)` | 重写 AXAML；副作用逻辑保留 |
| `DialogOverlayWindow` / `MessageBoxControl` / `ColorPickerControl` | **删除**，用 FA `ContentDialog` + `ColorPicker` |
| `DialogHelper.cs` | 重写为 FA ContentDialog 封装 |
| `Styles.xaml` | **删除**，FA 主题自带 |
| `App.xaml(.cs)` | 重写（单实例/快捷键/日志保留） |

## 十、建议

1. **先做阶段 0**：整个迁移的最大不确定性（包安装 + 四件套）在第一天就能验证，失败成本最低。
2. **ViewModels 不动**：迁移期间严禁改动 VM 层，保证两版逻辑一致；需要调逻辑时先改 WPF 版、再同步。
3. **数据文件不动**：`settings.json` / `schedule.json` 两版共用，迁移期间用户数据零丢失。
4. **不要复制 ClassIsland 代码**（GPL-3.0）；只对齐视觉与架构思想。

---

*文档随迁移进展更新。阶段 0 启动前先确认：路线 A（联网下载 11.3.17+2.4.1）还是路线 B（缓存 12.0.0+3.0.2）。*
