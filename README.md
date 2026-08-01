# 学程

> 🎓 WPF 学生桌面伴侣 — 倒计时 · 课表 · 考试 · 天气 · 提醒

<p align="center">
  <img src="icon.ico" width="96" alt="图标"/>
</p>

---

## ✨ 功能

### 核心倒计时
- ⏳ **实时倒计时** — 天/时/分/秒精确刷新，支持设置小数位数
- 🌐 **中英双语** — 中文 + 英文双行倒计时显示，各行独立开关
- 📊 **高中进度条** — 可视化展示高中生活已过百分比，百分比文字独立开关
- 🎬 **数字脉冲动画** — 每秒轻量缩放脉冲，可开关

### 考试与日程
- 📝 **考试全屏倒计时** — 科目名称/剩余时间/进度条/下一场信息，最后5分钟蜂鸣提醒
- ⚠ **防误触确认** — ESC/退出按钮考试进行中弹确认框
- 🏁 **考后自动恢复** — 最后一场考试结束3秒后自动退出，恢复课表栏
- 🔄 **双击/F11 切换** — 考试窗口全屏↔窗口一键切换
- 📅 **课表悬浮栏** — 吸附屏幕顶部，课程表网格视图，自动收缩+展开
- 🌅 **明天课程预览** — 放学后/周末自动显示明天课程安排
- ⏰ **下课倒计时** — 栏内置60秒倒计时，距下课30秒自动展开+橙色高亮
- ⏱ **下课延迟展开** — 下课后等2分钟再展开栏，给老师留操作窗口
- 🔄 **调休/调课/代课** — 调休（整天复制）、调课（格子交换/移动）、代课（老师替换）
- 🚫 **按科目隐藏** — 指定老师的课自动隐藏所有窗口，下课恢复
- 🔔 **预备铃适配** — 提前2分钟自动进入上课模式（窗口隐藏+栏收缩）

### 深度自定义
- 🎨 **字体与字号** — 全部自定义，支持系统已安装字体
- 🎨 **配色方案** — 数字/文字/进度条/考试全屏颜色自由调整，内置颜色选择器
- 🔍 **透明度** — 滑杆调节窗口与课表栏透明度
- 📝 **文本定制** — 中英文标签文字可自由修改

### 屏幕定位
- 📐 **6 种预设** — 顶部 / 中上 / 居中 / 中下 / 底部 / 自定义
- 🖱 **拖动定位** — 自定义模式拖动窗口，坐标实时保存
- 📏 **Y 偏移** — 精细微调垂直位置
- 👻 **点击穿透** — 预设模式下鼠标事件穿透，不阻挡操作

### 实用功能
- ☁ **实时天气** — 支持窗口模式和文本模式，手动输入城市名
- 💬 **每日一言** — API 励志语录随机展示，可自定义 API 地址
- 📦 **系统托盘** — 任务栏托盘常驻，右键菜单快捷操作（显示/隐藏/天气/课表/考试/设置/退出）
- 🎯 **始终置顶** — 可选始终置顶或正常窗口层级
- 💾 **配置持久化** — JSON 文件存储，重启不丢失

---

## 🖥 截图

> 运行后截图替换此处

---

## 🛠 技术栈

| 技术 | 说明 |
|------|------|
| .NET 8 | 目标框架 |
| WPF | 桌面 UI 框架 |
| C# 12 | 开发语言 |
| XAML | 界面标记语言 |
| Hardcodet.NotifyIcon.Wpf | 系统托盘图标 |
| System.Drawing.Common | 颜色处理 |
| System.Text.Json | JSON 配置持久化 |
| 高德天气 API | 实时天气数据 |

---

## 🚀 快速开始

### 环境要求

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 运行

```bash
git clone https://github.com/XEKernel/StudyJourney.git
cd 学程
dotnet run
```

### 编译发布（64 位独立可执行文件）

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

发布输出位于 `bin/Release/net8.0-windows/win-x64/publish/`。

---

## 📁 项目结构

```
学程/
├── App.xaml / App.xaml.cs           # 应用入口（单实例/全局快捷键）
├── Styles.xaml                      # 全局暗色样式（CheckBox/RadioButton/ComboBox/ScrollBar/…）
├── icon.ico                         # 软件图标
├── 学程.csproj                      # 项目文件
│
├── Views/                           # 窗口与控件（UI 层）
│   ├── MainWindow.xaml(.cs)         # 主窗口核心（字段/托盘/事件/退出）
│   ├── MainWindow.Countdown.cs      # 主窗口：倒计时/动画/进度
│   ├── MainWindow.Schedule.cs       # 主窗口：课表/考试/课表栏联动
│   ├── MainWindow.Quote.cs          # 主窗口：每日一言
│   ├── SettingWindow.xaml           # 设置窗口 UI（6 个纵向侧边栏 Tab）
│   ├── SettingWindow_Core.cs        # 设置窗口：初始化/加载/应用/按钮
│   ├── SettingWindow_Styles.cs      # 设置窗口：动画控件样式构建
│   ├── SettingWindow_Events.cs      # 设置窗口：控件事件处理器
│   ├── SettingWindow_Schedule.cs    # 设置窗口：课表/考试
│   ├── ExamModeWindow.xaml(.cs)     # 考试全屏倒计时
│   ├── ScheduleBarWindow.xaml(.cs)  # 课表悬浮栏核心（构造/定位/设置）
│   ├── ScheduleBarWindow.Render.cs  # 课表悬浮栏：刷新/卡片渲染
│   ├── ScheduleBarWindow.Interact.cs# 课表悬浮栏：倒计时/紧凑展开/天气
│   ├── DialogHelper.cs              # 自定义对话框辅助（居中/颜色选择/消息框）
│   ├── DialogOverlayWindow.xaml(.cs)# 对话框遮罩层
│   ├── MessageBoxControl.xaml(.cs)  # 主题消息框控件
│   └── ColorPickerControl.xaml(.cs) # 颜色选择控件
│
├── Models/                          # 数据模型（纯数据，无 UI 依赖）
│   ├── Settings.cs                  # 应用设置（JSON 持久化）
│   ├── ScheduleEntry.cs             # 课表/考试条目/时段模板/课程表网格行
│   └── ScheduleManager.cs           # 课表与考试数据管理（加载/保存/查询）
│
├── Services/                        # 服务层（业务逻辑，可独立测试）
│   ├── ReminderService.cs           # 提醒调度服务（上课/下课/考试提醒）
│   ├── WeatherService.cs            # 天气服务（HTTP + JSON 解析）
│   └── UpdateService.cs             # GitHub Release 更新检查/下载
│
├── Helpers/                         # 公共工具
│   ├── ColorUtils.cs                # 颜色解析/天气表情符号
│   ├── DialogEnums.cs               # 对话框枚举
│   ├── FadeHelper.cs                # 统一淡入淡出（解决动画 FillBehavior 残留）
│   └── AppLogger.cs                 # 轻量日志（Debug + 文件）
│
└── Updater/                         # 独立更新程序（StudyJourney.Updater）
```

> 架构约定：`Views → Models/Services/Helpers` 单向依赖；`Services → Models/Helpers`；Models/Helpers 不依赖上层。

---
## 📝 设置 Tab 说明

| Tab | 功能 |
|-----|------|
| ⏳ 倒计时 | 字体、字号、颜色、透明度、可见内容、时间精度/小数位、动画、中英文文本、目标/起算日期、自定义倒计时 |
| 📐 位置 | 6 种预设 + 自定义坐标 + Y 偏移 + 始终置顶 + 上课隐藏科目 |
| 🔌 API | 每日一言 API + 实时天气（城市/模式/窗口位置/颜色/刷新间隔） |
| 📋 课表 | 时段模板、课程表网格、调休、调课（交换/移动/代课）、JSON 导入导出 |
| 📝 考试 | 考试列表+科目编辑、导入导出 |
| ℹ 关于 | 版本信息、技术栈、功能亮点、更新日志、数据备份还原 |

---

## 🌤 天气功能说明

1. 进入设置 → 🔌 API → 天气
2. 输入城市名称（如"深圳"）和行政区划代码（可选）
3. 选择**文本模式**（主窗口内显示）或**窗口模式**（独立悬浮窗）
4. 窗口模式下可在设置中调整位置预设和颜色
5. 天气数据来源于高德地图天气 API

---

## 📅 课表与考试

1. **课表编辑**：设置 → 📋 课表 → 时段模板（定义每天节次时间）→ 课程表网格填课程名
2. **调休**：在课程表下方选来源星期→目标星期→应用，一键复制课表
3. **调课**：点击课程表格子选源→再点击选目标→点交换/移动/代课按钮，支持跨节次跨星期操作
4. **课表栏**：托盘菜单 → 课表栏，悬浮屏幕顶部。上课自动收缩；距下课1分钟显示倒计时，30秒自动展开；下课后2分钟再展开
5. **考试模式**：设置 → 📝 考试 → 添加考试+科目 → 托盘菜单 → 考试模式进入全屏
6. 空闲时双击考试窗口或按 F11 切换窗口模式
7. 考试最后5分钟有蜂鸣提醒，考试结束自动恢复课表栏

---

## 📄 更新日志

详见 GitHub Releases: https://github.com/XEKernel/StudyJourney/releases

---

## 📄 许可证

MIT License

Copyright (c) 2025 XEKernel
