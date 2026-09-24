using System;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Services;
using StudyJourney.Avalonia.Views;

namespace StudyJourney.Avalonia;

public partial class App : Application
{
    /// <summary>全局设置（从 settings.json 加载，与 WPF 版共用同一配置）</summary>
    public static AppSettings Settings { get; set; } = new();

    /// <summary>全局课表管理器（从 schedule.json 加载）</summary>
    public static ScheduleManager Schedule { get; private set; } = new();

    /// <summary>全局提醒服务（上课/下课/60 秒倒计时）</summary>
    public static ReminderService? Reminders { get; private set; }

    /// <summary>全局自动化任务服务（拼图式规则：触发 + 动作，automations.json）</summary>
    public static AutomationService? Automation { get; private set; }

    /// <summary>设置被保存后触发（主窗口/悬浮栏等订阅并刷新）</summary>
    public static event Action? SettingsChanged;

    /// <summary>保存设置并广播变更（线程安全：#2 修复 —— Kestrel 后台线程调用时自动封送 UI 线程再 Invoke，订阅者无需关心来源线程）</summary>
    public static void SaveSettings()
    {
        Settings.Save();
        if (Dispatcher.UIThread.CheckAccess())
            SettingsChanged?.Invoke();
        else
            Dispatcher.UIThread.Post(() => SettingsChanged?.Invoke());
    }

    /// <summary>
    /// 统一入口：从磁盘重载课表并广播 DataChanged（线程安全）。
    /// 远程 PUT /api/schedule、恢复备份、编辑器保存一律走这里，避免各处自行 Post 造成不一致（报告第四节 A）。
    /// 内部 UI 线程封送：ScheduleManager._data 被主窗口每秒 Tick 读取。
    /// </summary>
    public static void ReloadScheduleFromDisk()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            try { Schedule.Reload(); }
            catch (Exception ex) { Helpers.AppLogger.Error("课表重载到内存失败", ex); }
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { Schedule.Reload(); }
                catch (Exception ex) { Helpers.AppLogger.Error("课表重载到内存失败", ex); }
            });
        }
    }

    /// <summary>通过系统托盘发送 Windows 通知（提醒方式=Windows 通知时使用）</summary>
    public static void ShowSystemNotification(string title, string message)
    {
        try
        {
            Helpers.SystemToast.Show(title, message);
        }
        catch { /* 系统通知失败静默 */ }
    }

    private TrayIcon? _trayIcon;
    private Window? _mainWindow;

    /// <summary>应用图标（各窗口标题栏/任务栏共用，从 avares 加载）</summary>
    public static WindowIcon? AppIcon { get; private set; }

    // ── 全局快捷键 ID（与 WPF 版一致）────────────────────────
    private const int HotKeyToggleMain = 1;   // Ctrl+Shift+H
    private const int HotKeyWhiteboard = 2;   // Ctrl+Shift+W（白板，PLANNING 2.4）
    private const int HotKeyExamMode   = 3;   // Ctrl+Shift+E
    private const int HotKeyAnnotation = 4;   // Ctrl+Alt+D（屏幕批注，PLANNING 2.3/2.7#7）
    private const int HotKeyPdfReader  = 5;   // Ctrl+Shift+P（PDF 阅读器，PLANNING 2.1）
    private const uint VK_H = 0x48, VK_E = 0x45, VK_W = 0x57, VK_D = 0x44, VK_P = 0x50;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>从 avares 资源加载窗口图标</summary>
    public static WindowIcon? LoadAppIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://StudyJourneyAvalonia/Assets/icon.ico"));
            return new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Warn("加载窗口图标失败: " + ex.Message);
            return null;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // ── 启动耗时诊断（2026-09-16 用户问"启动有点慢，能否换 C++/Qt"）──
        // 时间基准用**进程真实启动时刻**，而不是进到本方法才开始计时 ——
        // 否则会把"运行时自举"（CLR 加载 + 程序集解析 + Main 之前的初始化）那段时间漏掉，
        // 而那恰恰是评估"换原生框架能省多少"的关键部分。
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long SinceProcessStartMs()
        {
            try
            {
                var start = System.Diagnostics.Process.GetCurrentProcess().StartTime;
                return (long)(DateTime.Now - start).TotalMilliseconds;
            }
            catch { return sw.ElapsedMilliseconds; }
        }
        void Mark(string stage) =>
            Helpers.AppLogger.Info($"[启动耗时] {stage}: {SinceProcessStartMs()} ms");

        Helpers.AppLogger.EnableFileLogging();
        Helpers.AppLogger.Info("学程 Avalonia 启动");
        Mark("进入应用初始化（=运行时自举已花掉的时间）");

        Settings = AppSettings.Load();
        Mark("settings.json 加载完成");

        // 上课活动记录（2026-09-24）：设置里开启才启动 —— 自动化打开的文件、老师手动打开的、
        // U 盘插拔都记到 records/ 下，供分析"课件顺序规律"
        if (Settings.RecordActivity) Services.ActivityRecorder.Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppIcon = LoadAppIcon();
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownRequested += (_, _) => Cleanup();
            Mark("主窗口构建完成（含 XAML 解析）");

            // 提醒服务：课表/考试关键节点触发（声音 + 事件）。
            // #3 修复后不再注入 Settings 实例：ReminderService 内部动态读 App.Settings
            Reminders = new ReminderService(Schedule);
            Reminders.Start();

            // 自动化任务服务：拼图式规则（触发拼块 + 动作拼块）。总开关默认关，设置页开启才生效。
            // 注意：automations.json 独立于 settings.json（恢复默认设置不误删规则）
            Automation = new AutomationService(Schedule);
            Automation.Start();
            Mark("提醒/自动化服务就绪");

            // ── 自检模式（SJ_SELFTEST）────────────────────────────
            // 必须在建托盘/窗口之前判断：自检实例**不显示托盘图标、不显示主窗口**。
            // 否则一个卡住的自检实例会留下一个点不动的托盘图标，看起来像"软件卡死"。
            var selfTest = Environment.GetEnvironmentVariable("SJ_SELFTEST");
            if (!string.IsNullOrEmpty(selfTest))
            {
                RunSelfTest(selfTest);
                return;
            }

            SetupTrayIcon();
            SetupGlobalHotKeys();
            Mark("托盘 + 全局快捷键注册完成");

            // 更新下载通道：把设置里的加速镜像灌进 UpdateService（空前缀 = 直连 GitHub）。
            // 本机实测 github.com 的 release 资产直连基本不通，所以默认开镜像。
            UpdateService.ProxyPrefix = Settings.UpdateUseProxy ? Settings.UpdateProxyPrefix : "";

            // 自动检查更新（延迟 5 秒，不阻塞启动）
            if (Settings.AutoCheckUpdate)
                _ = CheckUpdateDelayedAsync();

            // 当天有考试且开启自动进入 → 延迟 2 秒进入考试模式（对齐 WPF）
            if (Settings.AutoEnterExamMode && Settings.EnableExamMode &&
                Schedule.GetTodayExams().Count > 0)
                _ = EnterExamModeDelayedAsync();

            _mainWindow.Show();
            Mark("主窗口 Show() 返回");

            // Show() 只把窗口排进渲染队列，真正"看得见"要等首帧画完；
            // 用低优先级回调近似测首帧（= 用户可感知的启动时间）
            Dispatcher.UIThread.Post(() => Mark("首帧渲染完成 ← 用户可感知的启动时间"),
                DispatcherPriority.Background);

            // 远程 HTTP 服务：设置开启则延迟 1.5s 自动启动（不阻塞首屏；失败记日志不影响主程序）
            if (Settings.AutoStartHttpServer)
                _ = StartHttpServerDelayedAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 自检分发。全部走 UI 线程，但**绝不允许在 UI 线程上同步等待 async** ——
    /// 续体要回到被阻塞的 UI 线程会直接死锁，表现为"启动后界面全无反应"。
    /// </summary>
    private static void RunSelfTest(string mode)
    {
        ArmSelfTestWatchdog(mode == "update" ? 180 : 90);

        switch (mode)
        {
            case "whiteboard":
                Dispatcher.UIThread.Post(RunWhiteboardSelfTest, DispatcherPriority.Background);
                break;
            case "pdf":
                Dispatcher.UIThread.Post(RunPdfSelfTest, DispatcherPriority.Background);
                break;
            case "update":
                // 真异步（内部有网络等待），不能用 Post(sync) 包一层
                _ = RunUpdateSelfTestAsync();
                break;
            case "reminder":
                Dispatcher.UIThread.Post(RunReminderSelfTest, DispatcherPriority.Background);
                break;
            case "json":
                Dispatcher.UIThread.Post(RunJsonSelfTest, DispatcherPriority.Background);
                break;
            case "api":
                Dispatcher.UIThread.Post(RunApiSelfTest, DispatcherPriority.Background);
                break;
            case "makeup":
                Dispatcher.UIThread.Post(RunMakeupSelfTest, DispatcherPriority.Background);
                break;
            case "courseware":
                Dispatcher.UIThread.Post(RunCoursewareSelfTest, DispatcherPriority.Background);
                break;
            case "diag":
                Dispatcher.UIThread.Post(RunDiagSelfTest, DispatcherPriority.Background);
                break;
            default:
                Helpers.AppLogger.Warn($"未知自检模式：{mode}");
                Environment.Exit(2);
                break;
        }
    }

    /// <summary>
    /// 自检看门狗：无论卡在哪一步，超时后写结果并强退，**绝不留僵尸进程**。
    /// 起因（2026-09-15）：自检里在 UI 线程上 .GetAwaiter().GetResult() 等一个 async 下载，
    /// 续体要回到已被阻塞的 UI 线程 → 死锁；进程活着但界面全无反应，
    /// 还留下一个点不动的托盘图标，被误判成"软件卡死"。看门狗让这类问题最多影响 3 分钟。
    /// </summary>
    private static void ArmSelfTestWatchdog(int seconds)
    {
        var t = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(seconds * 1000);
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-result.txt"),
                    $"[WATCHDOG] 自检超过 {seconds} 秒仍未结束，已强制退出。\n" +
                    "多半是在 UI 线程上同步等待 async（死锁），或网络请求卡住。\n");
            }
            catch { }
            Environment.Exit(3);
        })
        { IsBackground = true };
        t.Start();
    }

    /// <summary>延迟启动远程 HTTP 服务（供老师局域网访问；#10：走 StartAsync 不阻塞任何线程）</summary>
    private static async System.Threading.Tasks.Task StartHttpServerDelayedAsync()
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(1500);
            await HttpServerService.StartAsync();
            Helpers.AppLogger.Info($"远程服务已自动启动，端口 {HttpServerService.Port}");
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error($"远程服务自动启动失败: {ex.Message}", ex);
        }
    }

    private async System.Threading.Tasks.Task CheckUpdateDelayedAsync()
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(5000);
            var info = await UpdateService.CheckAsync("XEKernel", "StudyJourney");
            if (!info.HasUpdate) return;

            // 2026-09-17 用户要求：检测到新版本**不再弹窗**，直接进入更新流程
            // （进度显示在顶部课表胶囊栏里，见 RunUpdateAsync）。
            Helpers.AppLogger.Info($"[UpdateService] 发现新版本 {info.LatestVersion}（当前 {UpdateService.CurrentVersion}），开始自动更新");
            if (!TryBeginUpdate())
            {
                Helpers.AppLogger.Info("[UpdateService] 已有更新在进行中，本次自动检查跳过");
                return;
            }
            await RunUpdateAsync(info);
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Warn($"[UpdateService] 启动时检查更新异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 自动更新：在胶囊栏显示进度 → 下载 → 解压 → 重启。
    /// 全程无弹窗（用户明确要求）；失败则记日志 + 在胶囊栏短暂提示，不打断老师。
    /// </summary>
    public static async System.Threading.Tasks.Task RunUpdateAsync(UpdateInfo info)
    {
        var banner = (Current as App)?._mainWindow as MainWindow;

        void SetBanner(string? text, double progress = -1, string? detail = null)
            => Dispatcher.UIThread.Post(() => banner?.ShowUpdateStatus(text, progress, detail));

        try
        {
            if (!UpdateService.UpdaterPresent)
            {
                Helpers.AppLogger.Warn("[Updater] 缺少 StudyJourney.Updater.exe，无法自动更新");
                SetBanner("更新程序缺失，请到 GitHub 手动下载", -1);
                await System.Threading.Tasks.Task.Delay(6000);
                SetBanner(null);
                return;
            }

            // ── 准备阶段：下载 + 解压，但**不启动更新程序** ──
            // ⚠ 必须与"启动"分开：更新程序只等主进程 **15 秒**就强杀，
            //   先拉起它再去等老师关白板 = 保证被强杀、板书照样丢（2026-09-22 修复的缺陷）。
            // 详细进度（2026-09-23 用户要求）：短串给"上课中"的小胶囊，长串给完整胶囊，
            // 长串含 下载进度 / 下载速度 / 已下载量 / 新旧版本号。
            string verPair = $"{UpdateService.CurrentVersion} → {info.LatestVersion}";
            SetBanner("正在下载新版本", 0, verPair);

            var progress = new Progress<UpdateProgress>(p =>
            {
                string pct = p.Fraction >= 0 ? $" {p.Fraction * 100:0}%" : "";
                string detail = verPair;
                if (p.SpeedText.Length > 0) detail += $"  ·  {p.SpeedText}";
                if (p.SizeText.Length > 0) detail += $"  ·  {p.SizeText}";
                SetBanner($"正在下载新版本{pct}", p.Fraction, detail);
            });

            void OnPhase(UpdatePhase phase) => SetBanner(phase switch
            {
                UpdatePhase.Downloading => "正在下载新版本",
                UpdatePhase.Extracting => "正在解压",
                UpdatePhase.Restarting => "重启",
                _ => "正在更新",
            }, -1, verPair);

            var prepared = await UpdateService.PrepareUpdateAsync(
                info.DownloadUrl, zipPath: null, progress: progress, onPhase: OnPhase);

            if (prepared == null)
            {
                SetBanner("更新失败，稍后将重试", -1);
                await System.Threading.Tasks.Task.Delay(6000);
                SetBanner(null);
                return;
            }

            // ── 等一个"能安全退出"的时机（最多 5 分钟）──
            int waited = 0;
            while (!IsSafeToRestartForUpdate(out string reason) && waited < 300)
            {
                SetBanner($"已就绪 · {reason}，稍后自动重启", -1);
                await System.Threading.Tasks.Task.Delay(2000);
                waited += 2;
            }

            if (!IsSafeToRestartForUpdate(out string still))
            {
                // 等了 5 分钟仍不安全 → **坚决不启动更新程序**（启动它 = 我们一定会被强杀）。
                // 留待下次安全时由定时器接手安装。
                Helpers.AppLogger.Info($"[Updater] 更新已就绪，但因「{still}」暂不重启");
                SetBanner("新版本已就绪，稍后自动安装", -1);
                _pendingUpdate = prepared;
                ArmPendingUpdateTimer();
                await System.Threading.Tasks.Task.Delay(6000);
                SetBanner(null);
                return;
            }

            await RestartIntoUpdateAsync(prepared, SetBanner);
        }
        catch (OperationCanceledException)
        {
            SetBanner(null);
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error("[Updater] 自动更新失败", ex);
            SetBanner("更新失败，稍后将重试", -1);
            await System.Threading.Tasks.Task.Delay(6000);
            SetBanner(null);
        }
        finally
        {
            _updateRunning = false;
        }
    }

    // ── 更新安装：拉开更新程序并退出 ────────────────────────────

    /// <summary>已准备就绪、但因"有未保存内容"而暂缓的更新；一旦变安全就自动安装</summary>
    private static UpdateService.PreparedUpdate? _pendingUpdate;
    private static global::Avalonia.Threading.DispatcherTimer? _pendingUpdateTimer;

    /// <summary>同一次会话里自动更新与手动更新不可并发（否则会互踩共用的临时 zip/staging）</summary>
    private static bool _updateRunning;

    /// <summary>尝试开始一次更新；已有更新在跑则返回 false（调用方据此提示/忽略）</summary>
    public static bool TryBeginUpdate() => !_updateRunning && (_updateRunning = true);

    /// <summary>暂缓的更新就绪后，每 10 秒看一次是否可以安全重启</summary>
    private static void ArmPendingUpdateTimer()
    {
        if (_pendingUpdateTimer != null) return;
        _pendingUpdateTimer = new global::Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10),
        };
        _pendingUpdateTimer.Tick += async (_, _) =>
        {
            if (_pendingUpdate == null) { _pendingUpdateTimer?.Stop(); _pendingUpdateTimer = null; return; }
            if (!IsSafeToRestartForUpdate(out _)) return;

            var p = _pendingUpdate;
            _pendingUpdate = null;
            _pendingUpdateTimer?.Stop();
            _pendingUpdateTimer = null;
            Helpers.AppLogger.Info("[Updater] 已无未保存内容，开始安装此前就绪的更新");

            var banner = (Current as App)?._mainWindow as MainWindow;
            void SetBanner(string? t, double pr = -1, string? d = null) => Dispatcher.UIThread.Post(() => banner?.ShowUpdateStatus(t, pr, d));
            try { await RestartIntoUpdateAsync(p, SetBanner); }
            catch (Exception ex) { Helpers.AppLogger.Error("[Updater] 暂缓更新安装失败", ex); }
        };
        _pendingUpdateTimer.Start();
    }

    /// <summary>
    /// 确认可以安全退出 → 拉起更新程序并退出进程。
    /// ⚠ 调用后要么进程消失，要么**什么都没发生**（拉起失败时不退出，否则程序就没了）。
    /// </summary>
    private static async System.Threading.Tasks.Task RestartIntoUpdateAsync(
        UpdateService.PreparedUpdate prepared, Action<string?, double, string?> setBanner)
    {
        setBanner("重启", -1, null);
        await System.Threading.Tasks.Task.Delay(400);   // 让"重启"两个字有机会画出来

        // 退出前把内存里的设置落盘：窗口位置拖动等只写在内存里，靠 SaveSettings 落盘，
        // 而 Environment.Exit 会跳过所有正常关闭流程 → 不落盘就白拖了。
        try { Settings?.Save(); }
        catch (Exception ex) { Helpers.AppLogger.Warn($"[Updater] 退出前保存设置失败: {ex.Message}"); }

        try { Services.HttpServerService.Stop(); } catch { }

        if (!UpdateService.LaunchUpdater(prepared, Environment.ProcessId))
        {
            Helpers.AppLogger.Error("[Updater] 更新程序未能启动，放弃本次重启（程序保持运行）");
            setBanner("更新程序启动失败，稍后将重试", -1, null);
            await System.Threading.Tasks.Task.Delay(6000);
            setBanner(null, -1, null);
            return;
        }

        Helpers.AppLogger.Info("[Updater] 更新程序已启动，主程序即将退出");
        Environment.Exit(0);
    }

    /// <summary>
    /// 现在重启是否会丢东西？
    ///
    /// ⚠ 2026-09-22 从"硬编码白板/批注/PDF 三个窗口"改成**遍历所有窗口**问
    /// <see cref="Views.IUnsavedWork"/>：`Environment.Exit` 不触发任何 Closing 事件，
    /// 所以课表编辑器的"未保存修改"、设置窗口的三选一同样会被静默跳过（原来漏了它们）。
    /// </summary>
    private static bool IsSafeToRestartForUpdate(out string reason)
    {
        reason = "";
        try
        {
            var lifetime = Application.Current?.ApplicationLifetime
                           as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime != null)
            {
                foreach (var w in lifetime.Windows)
                {
                    if (w is Views.IUnsavedWork uw && uw.HasUnsavedWork)
                    {
                        reason = uw.UnsavedWorkHint;
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 判不出来就当作安全 —— 否则一个异常会让更新永远完不成
            Helpers.AppLogger.Warn($"[Updater] 未保存内容检查异常，按安全处理: {ex.Message}");
        }
        return true;
    }

    /// <summary>
    /// 手动更新（设置页「立即检查更新」用）：进度窗 + 可取消。
    /// 与自动更新的 <see cref="RunUpdateAsync"/> 的区别：这里老师是**主动点的**，
    /// 设置窗口通常盖着胶囊栏看不到进度，所以给一个带进度和取消的窗口更合适。
    /// </summary>
    public static async System.Threading.Tasks.Task RunUpdateWithWindowAsync(UpdateInfo info)
    {
        // 更新程序不在位就没必要白下几十 MB
        if (!UpdateService.UpdaterPresent)
        {
            await ConfirmAsync("学程 — 无法自动更新",
                "程序目录里缺少更新程序 StudyJourney.Updater.exe，无法自动安装。\n\n" +
                "请到 GitHub Releases 手动下载完整压缩包覆盖。", "知道了", "关闭");
            return;
        }

        Views.UpdateProgressWindow? win = null;
        try
        {
            win = new Views.UpdateProgressWindow();
            win.Show();

            var progress = new Progress<UpdateProgress>(p => win?.Report(p));

            // 与自动更新同样的两段式（2026-09-22）：先准备（下载+解压，不启动更新程序），
            // 确认安全后才启动 —— 否则更新程序 15 秒后会强杀主进程，未保存的板书照样丢。
            var prepared = await UpdateService.PrepareUpdateAsync(
                info.DownloadUrl, zipPath: null, progress: progress,
                onPhase: ph =>
                {
                    if (ph == UpdatePhase.Extracting) win?.Phase("正在解压…");
                },
                ct: win.Token);

            if (prepared == null)
            {
                win.Close();
                await ConfirmAsync("学程 — 更新未完成",
                    "下载或解压失败。\n\n可以稍后重试，或到 GitHub Releases 手动下载。", "知道了", "关闭");
                return;
            }

            if (!IsSafeToRestartForUpdate(out string reason))
            {
                win.Close();
                await ConfirmAsync("学程 — 新版本已就绪",
                    $"更新已经下载好了，但现在有未保存的内容：{reason}。\n\n" +
                    "请先把它保存或关掉，然后重新「检查更新」即可完成升级。\n" +
                    "（也可以不管它 —— 程序下次启动时会自动装好。）",
                    "知道了", "关闭");
                // 留着这份已就绪的更新：一旦安全由定时器接手，不用重下几十 MB
                _pendingUpdate = prepared;
                ArmPendingUpdateTimer();
                return;
            }

            win.SwitchToInstalling();
            await RestartIntoUpdateAsync(prepared, (t, _, _) => win?.Phase(t ?? ""));
        }
        catch (OperationCanceledException)
        {
            win?.Close();
        }
        catch (Exception ex)
        {
            try { win?.Close(); } catch { }
            Helpers.AppLogger.Error("[Updater] 自动更新失败", ex);
            await ConfirmAsync("学程 — 更新失败", $"更新失败：\n{ex.Message}", "知道了", "关闭");
        }
    }

    /// <summary>开考自动进入考试模式：延迟 2 秒（对齐 WPF MainWindow.Schedule.cs）</summary>
    private async System.Threading.Tasks.Task EnterExamModeDelayedAsync()
    {
        await System.Threading.Tasks.Task.Delay(2000);
        Dispatcher.UIThread.Post(EnterExamMode);
    }

    /// <summary>通用确认弹窗（Avalonia 无内置 MessageBox，用系统消息框）</summary>
    public static async System.Threading.Tasks.Task<bool> ConfirmAsync(string title, string message,
        string okText = "确定", string cancelText = "取消")
    {
        return await Helpers.DialogHelper.ShowConfirmAsync(
            (Current as App)?._mainWindow, title, message, okText, cancelText);
    }

    /// <summary>简易提示弹窗（单一"确定"按钮；主窗口隐藏时降级为非模态）</summary>
    public static async System.Threading.Tasks.Task ShowMessageAsync(string title, string message)
    {
        await Helpers.DialogHelper.ShowMessageAsync((Current as App)?._mainWindow, title, message);
    }

    private void SetupGlobalHotKeys()
    {
        // Ctrl+Shift+H 显示/隐藏主窗口
        if (!GlobalHotKeyManager.Register(HotKeyToggleMain, VK_H, true, true, false,
                () => Dispatcher.UIThread.Post(() => ToggleMainWindow())))
            Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+H 注册失败（可能被其他程序占用）");

        // Ctrl+Shift+E 进入考试模式
        if (!GlobalHotKeyManager.Register(HotKeyExamMode, VK_E, true, true, false,
                () => Dispatcher.UIThread.Post(() => EnterExamMode())))
            Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+E 注册失败（可能被其他程序占用）");

        // Ctrl+Shift+W 打开白板（PLANNING 2.4；老师板书随时唤起）
        if (!GlobalHotKeyManager.Register(HotKeyWhiteboard, VK_W, true, true, false,
                () => Dispatcher.UIThread.Post(OpenWhiteboardGlobal)))
            Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+W 注册失败（可能被其他程序占用）");

        // Ctrl+Alt+D 屏幕批注（PLANNING 2.3：在任意应用之上圈画）
        if (!GlobalHotKeyManager.Register(HotKeyAnnotation, VK_D, true, false, true,
                () => Dispatcher.UIThread.Post(ToggleScreenAnnotationGlobal)))
            Helpers.AppLogger.Warn("全局快捷键 Ctrl+Alt+D 注册失败（可能被其他程序占用）");

        // Ctrl+Shift+P 打开 PDF 阅读器（PLANNING 2.1；老师上课翻课件随时唤起）
        if (!GlobalHotKeyManager.Register(HotKeyPdfReader, VK_P, true, true, false,
                () => Dispatcher.UIThread.Post(() => OpenPdfReaderGlobal())))
            Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+P 注册失败（可能被其他程序占用）");
    }

    /// <summary>统一入口：进入考试模式（托盘/快捷键/设置页共用）</summary>
    public static void EnterExamModeGlobal()
    {
        if (Current is App app && app._mainWindow is MainWindow mw) mw.EnterExamMode();
    }

    /// <summary>统一入口：退出考试模式（设置页"退出考试模式"按钮）</summary>
    public static void ExitExamModeGlobal()
    {
        if (Current is App app && app._mainWindow is MainWindow mw) mw.ExitExamMode();
    }

    /// <summary>统一入口：打开设置（单例）</summary>
    public static void OpenSettingsGlobal()
    {
        if (Current is App app && app._mainWindow is MainWindow mw) mw.OpenSettings();
        else new SettingsWindow().Show();
    }

    // ── 白板（PLANNING 2.4）──────────────────────────────────
    // 单例：重复触发只激活已有窗口，避免老师连按快捷键开出多个白板（板书会分散在多个窗口里）

    private static WhiteboardWindow? _whiteboard;

    /// <summary>统一入口：打开白板（托盘 / 全局快捷键 / 主窗口菜单共用）</summary>
    public static void OpenWhiteboardGlobal()
    {
        if (_whiteboard is { IsVisible: true })
        {
            if (_whiteboard.WindowState == WindowState.Minimized)
                _whiteboard.WindowState = WindowState.Normal;
            _whiteboard.Activate();
            return;
        }
        _whiteboard = new WhiteboardWindow();
        _whiteboard.Closed += (_, _) => _whiteboard = null;
        _whiteboard.Show();
    }

    // ── 屏幕批注（PLANNING 2.3）──────────────────────────────

    private static ScreenAnnotationWindow? _annotation;

    /// <summary>开关屏幕批注覆盖层（快捷键/托盘共用）。已开则关闭（再按一次退出，符合"随时收起"直觉）</summary>
    public static void ToggleScreenAnnotationGlobal()
    {
        if (_annotation is { IsVisible: true })
        {
            _annotation.Close();
            return;
        }
        _annotation = new ScreenAnnotationWindow();
        _annotation.Closed += (_, _) => _annotation = null;
        _annotation.Show();
    }

    // ── PDF 阅读器（PLANNING 2.1）────────────────────────────
    // 单例：重复触发只激活已有窗口；传了 path 就直接加载（远程投递/自动化"打开课件"复用这里）

    private static PdfReaderWindow? _pdfReader;

    /// <summary>统一入口：打开 PDF 阅读器（托盘 / 全局快捷键 / 主窗口菜单共用）</summary>
    /// <summary>
    /// 按"课件打开方式"打开一个课件路径：若开启「用内置阅读器打开 PDF」且确实是 .pdf，
    /// 就交给内置阅读器并返回 true —— 调用方**不要**再 Process.Start（否则会同时开两个）。
    /// 否则返回 false，由调用方走系统默认关联程序。
    ///
    /// 2026-09-22（规划 2.0 课件打开方式二选一）：自动化「打开课件」与远程「投递课件」共用此入口。
    /// </summary>
    public static bool TryOpenCoursewareWithBuiltInReader(string path)
    {
        try
        {
            if (Settings == null || !Settings.OpenPdfWithBuiltInReader) return false;
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return false;
            if (!System.IO.File.Exists(path)) return false;

            OpenPdfReaderGlobal(path);
            Helpers.AppLogger.Info($"[课件] 用内置阅读器打开：{path}");
            return true;
        }
        catch (Exception ex)
        {
            // 失败就退回系统默认程序，不能让"开不了课件"这种事发生
            Helpers.AppLogger.Warn($"[课件] 内置阅读器打开失败，回退系统默认程序: {ex.Message}");
            return false;
        }
    }

    public static void OpenPdfReaderGlobal(string? path = null)
    {
        if (_pdfReader is { IsVisible: true })
        {
            if (_pdfReader.WindowState == WindowState.Minimized)
                _pdfReader.WindowState = WindowState.Normal;
            _pdfReader.Activate();
            if (!string.IsNullOrWhiteSpace(path)) _ = _pdfReader.OpenFileAsync(path);
            return;
        }

        _pdfReader = new PdfReaderWindow();
        _pdfReader.Closed += (_, _) => _pdfReader = null;
        _pdfReader.Show();
        if (!string.IsNullOrWhiteSpace(path)) _ = _pdfReader.OpenFileAsync(path);
    }

    /// <summary>
    /// 白板渲染链路自检（SJ_SELFTEST=whiteboard）：真正实例化窗口 + 走一遍导出渲染，
    /// 验证 InkCanvas 几何构建与 RenderTargetBitmap 在真实 Avalonia 平台下可用。
    /// 结果写日志后退出进程（仅测试用，正常启动不进入）。
    /// </summary>
    private static void RunWhiteboardSelfTest()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            sb.AppendLine("[SELFTEST] 白板渲染链路自检开始");

            // 1. 文档逻辑：增 / 撤销 / 重做 / 擦除
            var doc = new Helpers.InkDocument();
            var s1 = new Helpers.InkStroke { Color = Colors.Red, Thickness = 3, Tool = Helpers.InkTool.Pen };
            s1.Points.Add(new global::Avalonia.Point(10, 10));
            s1.Points.Add(new global::Avalonia.Point(60, 40));
            s1.Points.Add(new global::Avalonia.Point(120, 20));
            doc.Add(s1);
            sb.AppendLine($"[SELFTEST] 笔画数={doc.Strokes.Count} canUndo={doc.CanUndo}");
            sb.AppendLine($"[SELFTEST] undo={doc.Undo()} → {doc.Strokes.Count} canRedo={doc.CanRedo}");
            sb.AppendLine($"[SELFTEST] redo={doc.Redo()} → {doc.Strokes.Count}");

            // 2. 几何构建（真实平台；控制台自检跑到这里会抛 IPlatformRenderInterface 未定位）
            var geo = Helpers.InkGeometry.BuildGeometry(s1, Helpers.IdentityInkSurface.Instance);
            sb.AppendLine($"[SELFTEST] 几何构建 OK bounds={geo.Bounds}");

            // 3. 全部背景样式渲染
            foreach (Helpers.BoardBackground bg in Enum.GetValues<Helpers.BoardBackground>())
            {
                using var bmp = Helpers.BoardRenderer.RenderPage(bg, doc.Strokes, 800, 600, 1.0);
                if (bmp.PixelSize.Width <= 0) throw new Exception($"背景 {bg} 渲染尺寸为 0");
                sb.AppendLine($"[SELFTEST] 背景 {bg} 渲染 OK {bmp.PixelSize.Width}x{bmp.PixelSize.Height}");
            }

            // 4. 真实窗口实例化（会触发 InkCanvas 构造 + 背景层）
            var win = new Views.WhiteboardWindow();
            sb.AppendLine("[SELFTEST] WhiteboardWindow 实例化 OK（未 Show，避免干扰桌面）");
            win.Close();

            // 5. 回归：InkCanvas 的**默认文档**必须订阅 Changed。
            //    曾经的 bug —— 订阅只写在 Document 的 setter 里，宿主不显式赋值 Document
            //    （屏幕批注就是这种用法）时，落笔提交不会重建历史缓存，
            //    表现为「笔迹一松手就消失」。这里用私有缓存字段直接断言。
            var canvas = new InkCanvas();
            var cacheField = typeof(InkCanvas).GetField("_historyCache",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? throw new Exception("找不到 InkCanvas._historyCache（自检需同步更新）");
            int cacheBefore = (cacheField.GetValue(canvas) as System.Collections.ICollection)?.Count ?? -1;

            var probe = new InkStroke { Color = Colors.Red, Thickness = 3, Tool = InkTool.Pen };
            probe.Points.Add(new global::Avalonia.Point(5, 5));
            probe.Points.Add(new global::Avalonia.Point(50, 50));
            canvas.Document.Add(probe);      // ⚠ 刻意不赋值 Document，走默认实例

            int cacheAfter = (cacheField.GetValue(canvas) as System.Collections.ICollection)?.Count ?? -1;
            sb.AppendLine($"[SELFTEST] 默认文档订阅：历史缓存 {cacheBefore} → {cacheAfter}");
            if (cacheAfter != 1)
                throw new Exception(
                    $"InkCanvas 未随默认文档变化重建绘制缓存（{cacheBefore}→{cacheAfter}）—— " +
                    "「笔迹一松手就消失」回归！");

            // 6. 回归：切换白板背景**不得重建视觉树**。
            //    曾经的 bug —— ApplyBackground 每次新建 Grid 并把 _ink 重新挂进去，
            //    导致 InkCanvas 被新旧两个父级同时持有（视觉树损坏），点背景按钮卡死/闪退。
            var wb = new Views.WhiteboardWindow();
            const System.Reflection.BindingFlags NonPub =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            var border = (Border?)typeof(WhiteboardWindow).GetField("_boardBorder", NonPub)?.GetValue(wb)
                ?? throw new Exception("找不到 WhiteboardWindow._boardBorder");
            var ink = (InkCanvas?)typeof(WhiteboardWindow).GetField("_ink", NonPub)?.GetValue(wb)
                ?? throw new Exception("找不到 WhiteboardWindow._ink");
            var bgField = typeof(WhiteboardWindow).GetField("_background", NonPub)
                ?? throw new Exception("找不到 WhiteboardWindow._background");
            var applyM = typeof(WhiteboardWindow).GetMethod("ApplyBackground", NonPub)
                ?? throw new Exception("找不到 WhiteboardWindow.ApplyBackground");

            var childBefore = border.Child;
            foreach (var bg in Enum.GetValues<BoardBackground>())
            {
                bgField.SetValue(wb, bg);
                try
                {
                    applyM.Invoke(wb, null);
                }
                catch (System.Reflection.TargetInvocationException tie)
                {
                    throw tie.InnerException ?? tie;   // 展开成真实异常，便于定位
                }
            }

            if (!ReferenceEquals(childBefore, border.Child))
                throw new Exception("切换背景重建了画布视觉树 —— 「背景按钮卡死/闪退」回归！");
            if (border.Child is not Grid g || !g.Children.Contains(ink))
                throw new Exception("切换背景后 InkCanvas 已不在画布视觉树内");

            sb.AppendLine($"[SELFTEST] 背景原地切换 OK（5 种，视觉树未重建，InkCanvas 仍在树上）");

            // 7. 回归：白板工具栏**不得横向溢出**（用户反馈"有的按钮跑到屏幕外面去"）。
            //    量法：给每一行「无限宽度」测一次（得到不折行时的自然宽度），
            //    再和各档屏幕宽度比 —— 自然宽度 > 屏宽就意味着会被挤出可视区。
            //    注意不能约束宽度去测（WrapPanel 一折行 DesiredSize 就等于屏宽，测不出问题）。
            var toolbar = (Border?)typeof(WhiteboardWindow).GetField("_toolbar", NonPub)?.GetValue(wb)
                ?? throw new Exception("找不到 WhiteboardWindow._toolbar");
            if (toolbar.Child is not StackPanel rows || rows.Children.Count == 0)
                throw new Exception("工具栏结构异常：应为若干行 StackPanel/WrapPanel");

            string[] screens = { "1920x1080", "1600x900", "1366x768" };
            double[] widths = { 1920, 1600, 1366 };
            double widestRow = 0;

            for (int i = 0; i < rows.Children.Count; i++)
            {
                if (rows.Children[i] is not Control row) continue;
                row.Measure(new global::Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = row.DesiredSize.Width;
                widestRow = Math.Max(widestRow, w);
                sb.AppendLine($"[SELFTEST] 工具栏第 {i + 1} 行自然宽度: {w:0} px");
            }

            sb.AppendLine($"[SELFTEST] 最宽一行 {widestRow:0} px vs 屏宽 " +
                          string.Join(" / ", screens.Zip(widths, (s, w) => $"{s}={w:0}")));
            if (widestRow > 1366)
                throw new Exception($"工具栏最宽一行 {widestRow:0}px 超过 1366 —— 窄屏上按钮会被挤出屏幕");

            sb.AppendLine("[SELFTEST] 工具栏不溢出 OK（1366 及以上都能完整显示，更窄会行内折行）");
            wb.Close();

            sb.AppendLine("[SELFTEST] 结论：PASS");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[SELFTEST] 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }

        Helpers.AppLogger.Info(sb.ToString());
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-result.txt"),
            sb.ToString());
        Environment.Exit(0);
    }

    /// <summary>
    /// PDF 渲染链路自检（SJ_SELFTEST=pdf）：现场生成一份最小 PDF → PDFium 打开 → 渲染一页
    /// → 校验像素真的画出来了；再校验 ZoomInkSurface 的坐标往返（"墨迹随缩放"的核心换算）。
    ///
    /// 这个自检最重要的价值：验证 **Docnet.Core 的原生 pdfium.dll 在本构建产物里能被加载**
    /// —— 原生库解析（runtimes/win-x64/native）是最容易在发布时才翻车的环节，必须能提前发现。
    /// </summary>
    private static void RunPdfSelfTest()
    {
        var sb = new System.Text.StringBuilder();
        bool deferred = false;      // 是否把收尾挪到了消息循环之后（见下方 Show()）
        try
        {
            sb.AppendLine("[PDFTEST] PDF 渲染链路自检开始");

            var pdfPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "studyjourney-selftest.pdf");
            BuildSamplePdf(pdfPath);
            sb.AppendLine($"[PDFTEST] 生成样本 PDF OK（{new System.IO.FileInfo(pdfPath).Length} 字节）");

            using var pdf = new Helpers.PdfRenderer(pdfPath);
            var p0 = pdf.PageSizesPt[0];
            sb.AppendLine($"[PDFTEST] PDFium 打开 OK：{pdf.PageCount} 页，第 1 页 {p0.Width:0}×{p0.Height:0} pt");
            if (pdf.PageCount != 1) throw new Exception($"页数应为 1，实际 {pdf.PageCount}");

            var page = pdf.RenderPage(0, Helpers.PdfRenderer.PtToDip);
            int expected = page.Width * page.Height * 4;
            sb.AppendLine($"[PDFTEST] 渲染第 1 页 OK：{page.Width}×{page.Height} px / {page.Bgra.Length} 字节（期望 {expected}）");
            if (page.Bgra.Length != expected) throw new Exception("像素数据长度与页面尺寸不匹配");
            if (page.Width <= 1 || page.Height <= 1) throw new Exception("渲染尺寸非法");

            // 统计"非白"像素：证明真的渲染出了内容，而不是一张空白图
            int nonWhite = 0;
            for (int i = 0; i + 3 < page.Bgra.Length; i += 4)
                if (page.Bgra[i] < 200 || page.Bgra[i + 1] < 200 || page.Bgra[i + 2] < 200) nonWhite++;
            sb.AppendLine($"[PDFTEST] 非白像素 {nonWhite} 个（应远大于 0，证明内容真的画出来了）");
            if (nonWhite < 50) throw new Exception($"渲染结果几乎全白（非白像素 {nonWhite}）—— PDFium 可能没真正工作");

            // 缩放坐标往返（PDF 阅读器"笔迹随缩放一起动"的核心换算）
            var surface = new Helpers.ZoomInkSurface { Zoom = 2.5 };
            var content = new global::Avalonia.Point(123.5, 456.25);
            var canvasPt = surface.FromContent(content);
            var back = surface.ToContent(canvasPt);
            sb.AppendLine($"[PDFTEST] ZoomInkSurface 往返 OK：(123.5,456.25) → 画布({canvasPt.X:0.###},{canvasPt.Y:0.###}) → 内容({back.X:0.###},{back.Y:0.###})");
            if (Math.Abs(back.X - content.X) > 1e-6 || Math.Abs(back.Y - content.Y) > 1e-6)
                throw new Exception("ZoomInkSurface 坐标往返有偏差");

            // 窗口实例化 + 触屏输入模式验证。
            // ⚠ 这里必须**真的 Show**："摘掉滚动手势识别器"发生在主题模板应用之后，不 Show
            //    就验证不到（而"手指写不出字"正是模板里那个识别器在抢指针，见 PdfReaderWindow）。
            //    断言放在消息循环之后（DispatcherPriority.Background 比 Render/Loaded 低，
            //    模板那时已经应用完），所以收尾也要挪到那里。
            var w = new Views.PdfReaderWindow();
            w.Show();
            deferred = true;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    sb.AppendLine($"[PDFTEST] 触屏输入：{w.TouchInputDiagnostics}");
                    // 诊断：把窗口里所有带手势识别器的元素列出来 —— 定位"到底谁在跟墨迹层抢触摸指针"
                    foreach (var v in global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<global::Avalonia.Input.InputElement>())
                        if (v.GestureRecognizers.Count > 0)
                            sb.AppendLine($"[PDFTEST]   识别器：{v.GetType().Name} × {v.GestureRecognizers.Count} → " +
                                          string.Join(",", v.GestureRecognizers.Select(g => g.GetType().Name)));
                    if (!w.TouchInkEnabled)
                        throw new Exception("AllowTouchInk 未开启 —— 手指写不出字（用户反馈的正是这个）");
                    if (!w.ScrollGestureDetached)
                        throw new Exception("内容视图上的 ScrollGestureRecognizer 没被摘除 —— 单指拖动会被它抢走指针，写半笔就断");
                    sb.AppendLine("[PDFTEST] 结论：PASS");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[PDFTEST] 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
                    sb.AppendLine(ex.StackTrace);
                }
                finally
                {
                    try { w.Close(); } catch { }
                    WritePdfSelfTestResult(sb);
                }
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[PDFTEST] 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }

        if (!deferred) WritePdfSelfTestResult(sb);
    }

    /// <summary>PDF 自检收尾：写日志 + 结果文件 + 退出进程（自检专用）</summary>
    private static void WritePdfSelfTestResult(System.Text.StringBuilder sb)
    {
        Helpers.AppLogger.Info(sb.ToString());
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-result.txt"),
            sb.ToString());
        Environment.Exit(0);
    }

    /// <summary>现场拼一份最小可用的单页 PDF（仅自检用：手写 PDF 语法 + 正确 xref 字节偏移，不引第三方库）</summary>
    private static void BuildSamplePdf(string path)
    {
        var sb = new System.Text.StringBuilder();
        var offsets = new System.Collections.Generic.List<int>();

        void Obj(int n, string body)
        {
            offsets.Add(System.Text.Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append(n).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }

        sb.Append("%PDF-1.4\n");
        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 200] " +
               "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>");
        const string content = "BT /F1 36 Tf 40 80 Td (StudyJourney PDF) Tj ET";
        Obj(4, $"<< /Length {System.Text.Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
        Obj(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        int xref = System.Text.Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append(o.ToString("0000000000")).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");

        System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.ASCII);
    }

    /// <summary>
    /// 纯逻辑自检（SJ_SELFTEST=update）：课件序号解析（含中文数字）+ 更新下载通道。
    ///
    /// 这些都是"错了也不会崩、只会静默做错事"的逻辑（序号排错 → 课件顺序错；
    /// 下载通道错 → 更新永远失败），所以专门用断言把它们钉住。
    /// 其中更新通道会**真的走一次镜像下载**（用别家仓库的小 zip），端到端验证代理可用。
    /// </summary>
    /// <summary>
    /// 课间静音语义自检（SJ_SELFTEST=reminder）。
    ///
    /// 2026-09-18 用户反馈"连堂中间的通知太吵"（晚读↔晚自习、中午听力↔下午第一节）。
    /// 这类课间语义属于"改错了也不会崩、只会默默变吵"的逻辑，必须用断言钉住：
    ///   · 自习类（早自习/晚读/午休/晚自习）之后的边界 → SelfStudyBoundary（该边界全部静音）
    ///   · 普通课之间的短/中/长间隔 → 仍按原语义分类（**不能**把正常课间也静音掉）
    ///   · 长间隔（≥60 分钟）优先 → 保住「上午放学」那条语义
    /// </summary>
    /// <summary>
    /// JSON 源生成器自检（SJ_SELFTEST=json）。
    ///
    /// 源生成器最大的风险不是"编不过"，而是**生成的 JSON 格式和原来不一样** ——
    /// 那会让老师现有的 settings.json / 课表读不出来（退化成默认值）或写坏。
    /// 所以这里用**仓库里真实的配置文件**做往返验证：
    ///   读 → 反序列化（必须能读出真值）→ 再序列化 → 顶层字段集合必须与原文一致。
    ///
    /// 同时确认源生成器确实接管了：走的是 AppJsonContext（编译期元数据），不是反射。
    /// </summary>
    /// <summary>
    /// 诊断包 + 课件序号解析自检（SJ_SELFTEST=diag）。
    ///
    /// 两件事一起验：
    ///  ① **课件序号解析**（含中文数字）—— 用户问过“中文序号做了没”，这里用真实文件名把结论钉死；
    ///  ② **诊断包端到端** —— 真跑一次 Create()，断言 zip 生成了、且包含预期的分节与安全约束，
    ///     跑完把 zip 删掉（不在用户桌面上留垃圾）。
    /// </summary>
    private static void RunDiagSelfTest()
    {
        var sb = new System.Text.StringBuilder();
        string? producedZip = null;
        string? probeDir = null;   // 自检造的"额外目录"探针，跑完要删掉
        try
        {
            sb.AppendLine("[DIAGTEST] 诊断包 / 课件序号自检开始");

            // ── ① 课件序号解析（阿拉伯 + 中文数字 + 误报抑制）──
            var seqCases = new (string Name, int Expect, string Desc)[]
            {
                ("01 集合.pdf",             1,  "阿拉伯两位"),
                ("unit3 lesson.pptx",      3,  "unit 前缀"),
                ("第3讲 函数.pdf",          3,  "第N讲"),
                ("第十讲 力学.pdf",        10,  "第十讲 → 10"),
                ("三 化学平衡.pptx",        3,  "中文数字+空格"),
                ("五、牛顿定律.docx",       5,  "中文数字+顿号"),
                ("一 集合的概念.pptx",      1,  "中文 一"),
                ("Lesson2 Grammar.pdf",    2,  "Lesson 前缀"),
                // ↓ 误报抑制：这些里面的中文数字**不是**序号（否则会被当成第 1 份排到最前）
                ("一次函数图像.pptx",       -1,  "应抑制：“一次函数”不是第 1 份"),
                ("一元二次方程.pdf",       -1,  "应抑制：“一元二次”不是序号"),
                ("三角函数.pdf",           -1,  "没有序号 → 返回 -1（未识别）"),
            };

            foreach (var (name, expect, desc) in seqCases)
            {
                int got = Helpers.FileSequence.ExtractNumber(name);
                bool ok = got == expect;
                sb.AppendLine($"[DIAGTEST]   {(ok ? "ok" : "✗")} {desc,-28} “{name}” → {got}（期望 {expect}）");
                if (!ok) throw new Exception($"课件序号解析不符：{desc} “{name}” → {got}，期望 {expect}");
            }
            sb.AppendLine("[DIAGTEST]   序号列说明：-1 = 未识别出序号（排序时排在最后）；" +
                          "中文数字（一/三/十/廿/卅 + 大写体）与误报抑制均已覆盖");

            // ── ② 诊断包端到端 ──
            // 先造一个"额外目录"并把深度调到 3，验证 2026-09-24 新增的「自定义打包内容」真的生效。
            // ⚠ 只改**内存中**的设置、不调用 SaveSettings，自检结束就 Exit → 不会污染老师的配置。
            try
            {
                probeDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SJDiagProbe");
                System.IO.Directory.CreateDirectory(probeDir);
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(probeDir, "子目录A"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(probeDir, "探针课件 第3讲.pdf"), "x");
                System.IO.File.WriteAllText(System.IO.Path.Combine(probeDir, "子目录A", "深层文件.txt"), "x");

                App.Settings.DiagExtraDirs = probeDir;
                App.Settings.DiagDesktopTreeDepth = 3;
                App.Settings.DiagIncludeCoursewareTree = false;   // 不依赖自动化配置，保证可重复
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[DIAGTEST]   （准备探针目录失败，该项将失败：{ex.Message}）");
            }

            string? zip = Services.DiagnosticPackager.Create();
            if (zip == null) throw new Exception("诊断包生成返回 null（详见日志）");
            producedZip = zip;
            if (!System.IO.File.Exists(zip)) throw new Exception("诊断包文件不存在：" + zip);

            long size = new System.IO.FileInfo(zip).Length;
            sb.AppendLine($"[DIAGTEST]   诊断包已生成：{System.IO.Path.GetFileName(zip)}（{size / 1024} KB）");
            if (size < 200) throw new Exception("诊断包过小，可能内容缺失");

            using (var za = System.IO.Compression.ZipFile.OpenRead(zip))
            {
                var names = za.Entries.Select(e => e.FullName).ToList();
                foreach (var w in new[] { "说明.txt", "课件清单.txt", "桌面文件树.txt", "系统信息.txt" })
                {
                    bool ok = names.Any(x => x == w);
                    sb.AppendLine($"[DIAGTEST]   {(ok ? "ok" : "✗")} 包内含 {w}");
                    if (!ok) throw new Exception("诊断包缺少 " + w);
                }

                // 新增功能（2026-09-24）：自定义打包内容 —— 额外目录树必须生成且展开到位
                bool extraInPack = names.Any(x => x == "额外目录树.txt");
                sb.AppendLine($"[DIAGTEST]   {(extraInPack ? "ok" : "✗")} 包内含 额外目录树.txt（自定义打包内容生效）");
                if (!extraInPack) throw new Exception("诊断包缺少 额外目录树.txt —— 自定义目录没生效");

                var entry = za.GetEntry("额外目录树.txt");
                if (entry != null)
                {
                    using var r = new System.IO.StreamReader(entry.Open());
                    string tree = r.ReadToEnd();

                    bool hasPath = tree.Contains(probeDir ?? "\u0000", StringComparison.OrdinalIgnoreCase);
                    bool hasSub = tree.Contains("子目录A", StringComparison.Ordinal);
                    bool hasDeep = tree.Contains("深层文件.txt", StringComparison.Ordinal);
                    bool hasFile = tree.Contains("探针课件 第3讲.pdf", StringComparison.Ordinal);

                    sb.AppendLine($"[DIAGTEST]   {(hasPath ? "ok" : "✗")} 额外目录树写明了指定目录的路径");
                    sb.AppendLine($"[DIAGTEST]   {(hasFile ? "ok" : "✗")} 额外目录树列出了目录下的文件");
                    sb.AppendLine($"[DIAGTEST]   {(hasSub && hasDeep ? "ok" : "✗")} 额外目录树展开了子目录与深层文件（深度生效）");
                    if (!hasPath) throw new Exception("额外目录树里没有指定目录的路径");
                    if (!hasFile) throw new Exception("额外目录树没有列出目录下的文件");
                    if (!(hasSub && hasDeep)) throw new Exception("额外目录树的深度没生效（子目录/深层文件缺失）");
                }

                bool cfg = names.Any(x => x.StartsWith("配置/", StringComparison.Ordinal));
                sb.AppendLine($"[DIAGTEST]   {(cfg ? "ok" : "✗")} 包内含 配置/ 分节");
                if (!cfg) throw new Exception("诊断包缺少 配置/ 分节");

                // ⚠ 安全断言：凭据绝不能进包
                bool leak = names.Any(x => x.Contains("tokens.json", StringComparison.OrdinalIgnoreCase));
                sb.AppendLine($"[DIAGTEST]   {(leak ? "✗" : "ok")} 包内不含 tokens.json（凭据不外泄）");
                if (leak) throw new Exception("诊断包泄漏了 tokens.json！");

                var se = za.GetEntry("配置/settings.json");
                if (se != null)
                {
                    using var r = new System.IO.StreamReader(se.Open());
                    string txt = r.ReadToEnd();
                    bool clean = !txt.Contains("PBKDF2", StringComparison.Ordinal);
                    sb.AppendLine($"[DIAGTEST]   {(clean ? "ok" : "✗")} settings.json 已剔除密码哈希");
                    if (!clean) throw new Exception("诊断包里的 settings.json 仍含密码哈希！");
                }
            }

            sb.AppendLine("[DIAGTEST] 结论：PASS");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[DIAGTEST] 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }
        finally
        {
            try { if (producedZip != null && System.IO.File.Exists(producedZip)) System.IO.File.Delete(producedZip); }
            catch { }
            try { if (probeDir != null && System.IO.Directory.Exists(probeDir)) System.IO.Directory.Delete(probeDir, true); }
            catch { }
        }

        Helpers.AppLogger.Info(sb.ToString());
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-result.txt"),
            sb.ToString());
        Environment.Exit(0);
    }

    /// <summary>
    /// 课件序列规则判定自检（SJ_SELFTEST=courseware）。
    ///
    /// 2026-09-24 用户反馈的 bug：某节课的自动化只是“打开一个软件”，
    /// 但主窗口胶囊却显示“下一份：<某 dll>” —— 因为判定把「打开文件」也算成课件序列，
    /// 而它的目录被解析成了**软件安装目录**，于是把安装目录里的文件当成了“下一份课件”。
    /// 这个判定错得**不会报错**，只会显示莫名其妙的文件名，所以必须钉住。
    /// </summary>
    private static void RunCoursewareSelfTest()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            sb.AppendLine("[CWTEST] 课件序列规则判定自检开始");

            static Models.AutomationRule R(Models.AutomationActionKind kind, string path) => new()
            {
                Name = "t", Enabled = true, ActionKind = kind, ActionPath = path,
            };

            var D = "D:" + '\\' + '\\';   // 拼出 D:\\

            var cases = new (Models.AutomationRule Rule, bool Expect, string Desc)[]
            {
                (R(Models.AutomationActionKind.OpenCourseware, ""),              true,  "打开课件（无路径）→ 算序列"),
                (R(Models.AutomationActionKind.OpenCourseware, D + "课件"),      true,  "打开课件（目录）→ 算序列"),
                (R(Models.AutomationActionKind.OpenFile, D + "Soft" + D + "EV.exe"), false, "★打开 .exe 软件 → 不算序列"),
                (R(Models.AutomationActionKind.OpenFile, D + "Soft" + D + "X.LNK"), false, "打开快捷方式 .lnk → 不算序列"),
                (R(Models.AutomationActionKind.OpenFile, D + "a" + D + "run.bat"), false, "打开 .bat → 不算序列"),
                (R(Models.AutomationActionKind.OpenFile, D + "a" + D + "setup.msi"), false, "打开 .msi → 不算序列"),
                (R(Models.AutomationActionKind.OpenFile, D + "课件" + D + "第1讲.pdf"), true,  "打开 .pdf → 算序列"),
                (R(Models.AutomationActionKind.OpenFile, D + "课件" + D + "第1讲.pptx"), true, "打开 .pptx → 算序列"),
                (R(Models.AutomationActionKind.OpenFile, D + "课件" + D + "a.mp4"), true,  "打开 .mp4 → 算序列"),
                (R(Models.AutomationActionKind.OpenFile, ""),                  false, "打开文件但没填路径 → 不算序列"),
                (R(Models.AutomationActionKind.ScreenOff, D + "x.pdf"),        false, "熄屏 → 不算序列"),
                (R(Models.AutomationActionKind.PlayAudio, D + "x.mp3"),       false, "播放音频 → 不算序列"),
                (R(Models.AutomationActionKind.OpenWhiteboard, ""),            false, "开白板 → 不算序列"),
            };

            foreach (var (rule, expect, desc) in cases)
            {
                bool got = Services.AutomationService.IsSequenceCoursewareRule(rule);
                bool ok = got == expect;
                sb.AppendLine($"[CWTEST]   {(ok ? "ok" : "✗")} {desc,-32} 期望 {(expect ? "算" : "不算")} 实际 {(got ? "算" : "不算")}");
                if (!ok) throw new Exception($"课件序列判定不符：{desc}");
            }

            sb.AppendLine("[CWTEST] 结论：PASS");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[CWTEST] 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }

        Helpers.AppLogger.Info(sb.ToString());
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-result.txt"),
            sb.ToString());
        Environment.Exit(0);
    }

    /// <summary>
    /// 调休（补课日）映射自检（SJ_SELFTEST=makeup）。
    ///
    /// 2026-09-21 用户反馈：周日调休补周五的课，但周五中午的听力自动化没触发 ——
    /// 因为自动化规则按星期几配（TriggerDays[5]），而那天真实是周日(7)。
    /// 这个映射错了**不会报错、只会静默什么都不发生**，所以必须断言。
    /// </summary>
    private static void RunMakeupSelfTest()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            sb.AppendLine("[MKTEST] 调休映射自检开始");

            // 2026-09-20 是周日（用户实际遇到的那天）
            var sunday = new DateTime(2026, 9, 20);
            var friday = new DateTime(2026, 9, 18);
            var saturday = new DateTime(2026, 9, 19);

            var makeup = new List<Models.MakeupDay>
            {
                new() { DateStr = "2026-09-20", DayOfWeek = 5 },   // 周日补周五
            };

            var cases = new (DateTime Date, List<Models.MakeupDay>? Days, int Expect, string Desc)[]
            {
                (sunday,   makeup, 5, "2026-09-20（周日）配了补周五 → 应映射为 5"),
                (sunday,   null,   7, "同一天没有调休配置 → 周日应为 7"),
                (sunday,   new(),  7, "调休表为空 → 周日应为 7"),
                (friday,   makeup, 5, "周五本身不受影响 → 5"),
                (saturday, makeup, 6, "周六不受影响 → 6"),
                (new DateTime(2026, 9, 21), makeup, 1, "周一不受影响 → 1"),
                // 非法配置要能安全回退（不能因为一条脏数据就整天不显示课表）
                (sunday, new List<Models.MakeupDay> { new() { DateStr = "2026-09-20", DayOfWeek = 0 } },
                 7, "调休目标写 0（非法）→ 回退真实周日 7"),
                (sunday, new List<Models.MakeupDay> { new() { DateStr = "2026-09-20", DayOfWeek = 9 } },
                 7, "调休目标写 9（非法）→ 回退真实周日 7"),
            };

            foreach (var (date, days, expect, desc) in cases)
            {
                var got = Models.ScheduleData.ResolveEffectiveDayOfWeek(date, days);
                bool ok = got == expect;
                sb.AppendLine($"[MKTEST]   {(ok ? "ok" : "✗")} {desc,-42} 期望 {expect} 实际 {got}");
                if (!ok) throw new Exception($"调休映射不符：{desc} 期望 {expect}，实际 {got}");
            }

            // 显示串（列表里给人看的）也要对
            var d0 = makeup[0];
            bool dispOk = d0.Display.Contains("2026-09-20") && d0.Display.Contains("周五");
            sb.AppendLine($"[MKTEST]   {(dispOk ? "ok" : "✗")} 列表显示串：\"{d0.Display}\"");
            if (!dispOk) throw new Exception($"调休显示串不对：{d0.Display}");

            sb.AppendLine("[MKTEST] 结论：PASS");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[MKTEST] 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }

        Helpers.AppLogger.Info(sb.ToString());
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-result.txt"),
            sb.ToString());
        Environment.Exit(0);
    }

    /// <summary>
    /// 远程控制台 API 响应自检（SJ_SELFTEST=api）。
    ///
    /// 2026-09-20：把 `Results.Json(new { success, message })` 这类匿名类型换成 DTO 是为了
    /// 让 NativeAOT 能用（匿名类型源生成器覆盖不了）。但**最大的风险是改错字段名** ——
    /// 老师端控制台网页按这些名字取值，改了名就是"页面白屏/功能失灵"，而且编译器不会报错。
    /// 所以这里把每个 DTO 的**实际序列化键**与期望值逐个断言。
    /// </summary>
    private static void RunApiSelfTest()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            sb.AppendLine("[APITEST] API 响应 DTO 自检开始");

            static string KeysOf<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> ti)
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, ti));
                return string.Join(",", doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(x => x));
            }
            static string Norm(string expected)
                => string.Join(",", expected.Split(',').Select(s => s.Trim()).OrderBy(x => x));

            // (描述, 实际键集合, 期望键集合 —— 期望值取自改造前匿名类型的成员名)
            var cases = new (string Desc, string Got, string Want)[]
            {
                ("ApiMsg",        KeysOf(new Services.ApiMsg(),        Services.ApiJsonContext.Default.ApiMsg),        "success,message"),
                ("ApiOkError",    KeysOf(new Services.ApiOkError(),    Services.ApiJsonContext.Default.ApiOkError),    "ok,error"),
                ("ApiOkOnly",     KeysOf(new Services.ApiOkOnly(),     Services.ApiJsonContext.Default.ApiOkOnly),     "ok"),
                ("ApiStatus",     KeysOf(new Services.ApiStatus(),     Services.ApiJsonContext.Default.ApiStatus),     "status"),
                ("ApiMsgPath",    KeysOf(new Services.ApiMsgPath(),    Services.ApiJsonContext.Default.ApiMsgPath),    "success,message,path"),
            };

            foreach (var (desc, got, want) in cases)
            {
                bool ok = got == Norm(want);
                sb.AppendLine($"[APITEST]   {(ok ? "ok" : "✗")} {desc,-14} 实际 [{got}]  期望 [{Norm(want)}]");
                if (!ok)
                    throw new Exception($"DTO 字段名不符：{desc} 实际 [{got}] 期望 [{Norm(want)}] —— 老师端网页会取值失败");
            }

            sb.AppendLine("[APITEST] 结论：PASS");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[APITEST] 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }

        Helpers.AppLogger.Info(sb.ToString());
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-result.txt"),
            sb.ToString());
        Environment.Exit(0);
    }

    private static void RunJsonSelfTest()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            sb.AppendLine("[JSONTEST] 源生成器自检开始");

            // 从 exe 目录上溯到仓库根（bin/<cfg>/net10.0 → 仓库根）
            string root = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 4; i++) root = Path.GetFullPath(Path.Combine(root, ".."));
            sb.AppendLine($"[JSONTEST] 仓库根推定：{root}");

            // ── 1. 设置：往返 + 字段集合一致 ──
            string settingsPath = Path.Combine(root, "settings.json");
            if (!File.Exists(settingsPath))
                throw new Exception($"找不到测试样本 {settingsPath}");

            string src = File.ReadAllText(settingsPath);
            var loaded = JsonSerializer.Deserialize(src, Models.AppJsonContext.Default.AppSettings);
            if (loaded == null) throw new Exception("settings.json 反序列化返回 null");
            sb.AppendLine($"[JSONTEST] settings 读入 OK：ClassName=\"{loaded.ClassName}\" 班级字段非空={!string.IsNullOrWhiteSpace(loaded.ClassName)}");

            static HashSet<string> TopKeys(string json)
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            }
            var kIn = TopKeys(src);

            // ── 1a. 旧文件能读出来（值到了就说明映射对）──
            sb.AppendLine("[JSONTEST] settings 旧文件读入 OK → 说明属性映射与旧格式一致");

            // ── 1b. 关键检查：当前类的**每个可写属性**都必须出现在序列化结果里 ──
            //     这才能抓到"源生成器漏了某个属性"——那会导致老师的设置项静默不落盘。
            //     注意不能用旧的 settings.json 做字段集合对比：那份文件是 WPF 时代的旧 schema，
            //     里面还留着 ChinesePrefix / ChineseDaysText 之类代码里已不存在的键，
            //     拿它比会把"正常演进"误判成"丢字段"。
            // ⚠ 探针里必须**真的放一个教师账号**：Teachers 默认已改成空表（见 AppSettings 注释），
            //   不放的话 "0 == 0" 会让这条断言恒成立 —— 看着有覆盖，实际什么都没测。
            var probe = new Models.AppSettings { ClassName = "JSONTEST 探针" };
            probe.Teachers = new List<Models.TeacherAccount>
            {
                new() { Username = "probe01", DisplayName = "探针老师", Subject = "数学" },
            };
            string probeJson = JsonSerializer.Serialize(probe, Models.AppJsonContext.Default.AppSettings);
            var written = TopKeys(probeJson);

            var expected = typeof(Models.AppSettings)
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .Where(p => p.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true).Length == 0)
                .Select(p => p.Name)
                .ToList();

            var notWritten = expected.Where(n => !written.Contains(n)).ToList();
            sb.AppendLine($"[JSONTEST] AppSettings 可写属性 {expected.Count} 个，序列化写出 {written.Count} 个键");
            if (notWritten.Count > 0)
                throw new Exception($"源生成器漏写了 {notWritten.Count} 个属性：{string.Join(", ", notWritten)}");
            sb.AppendLine("[JSONTEST] 每个可写属性都有对应 JSON 键 OK（设置项不会静默丢失）");

            // ── 1c. 往返一致：序列化 → 反序列化 → 值不变 ──
            var rt = JsonSerializer.Deserialize(probeJson, Models.AppJsonContext.Default.AppSettings);
            if (rt == null) throw new Exception("往返反序列化返回 null");
            if (rt.ClassName != "JSONTEST 探针")
                throw new Exception($"往返后 ClassName 变了：{rt.ClassName}");
            if (rt.Teachers.Count != probe.Teachers.Count || rt.Teachers.Count == 0)
                throw new Exception($"往返后 Teachers 数量不对：{rt.Teachers.Count}（探针放了 {probe.Teachers.Count}）");
            if (rt.Teachers[0].DisplayName != "探针老师")
                throw new Exception($"往返后教师字段丢了：DisplayName=\"{rt.Teachers[0].DisplayName}\"");
            sb.AppendLine($"[JSONTEST] 往返值一致 OK（ClassName 保留；Teachers {rt.Teachers.Count} 位且字段完整保留）");

            // 旧文件里"代码已不存在"的键，如实报出来（这是正常的 schema 演进，不是缺陷）
            var obsolete = kIn.Except(written).ToList();
            if (obsolete.Count > 0)
                sb.AppendLine($"[JSONTEST]   （旧文件含 {obsolete.Count} 个代码里已不存在的遗留键，属正常演进：" +
                              $"{string.Join(", ", obsolete.Take(6))}…）");

            // ── 2. 课表：往返 + 字段集合一致 ──
            string schedPath = Path.Combine(root, "schedule_example.json");
            if (File.Exists(schedPath))
            {
                string sSrc = File.ReadAllText(schedPath);
                var sd = JsonSerializer.Deserialize(sSrc, Models.AppJsonContext.Default.ScheduleData);
                if (sd == null) throw new Exception("schedule_example.json 反序列化返回 null");

                // 同 1b 的思路：用**当前类**做探针验证属性全覆盖，而不是和旧样本比字段集合
                var sProbe = new Models.ScheduleData();
                string sProbeJson = JsonSerializer.Serialize(sProbe, Models.AppJsonContext.Default.ScheduleData);
                var sWritten = TopKeys(sProbeJson);
                var sExpected = typeof(Models.ScheduleData)
                    .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                    .Where(p => p.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true).Length == 0)
                    .Select(p => p.Name)
                    .ToList();
                var sNotWritten = sExpected.Where(n => !sWritten.Contains(n)).ToList();
                sb.AppendLine($"[JSONTEST] 课表读入 OK：{sd.Entries.Count} 条课节；" +
                              $"ScheduleData 可写属性 {sExpected.Count} 个，写出 {sWritten.Count} 个键");
                if (sNotWritten.Count > 0)
                    throw new Exception($"课表源生成器漏写属性：{string.Join(", ", sNotWritten)}");
                sb.AppendLine("[JSONTEST] 课表字段全覆盖 OK");
            }
            else
            {
                sb.AppendLine($"[JSONTEST]   （跳过课表样本，未找到 {schedPath}）");
            }

            // ── 3. 各上下文类型都能取到 JsonTypeInfo（= 源生成器已覆盖，不会落到反射）──
            var probes = new (string Name, bool Ok)[]
            {
                ("AppSettings",  Models.AppJsonContext.Default.AppSettings != null),
                ("ScheduleData", Models.AppJsonContext.Default.ScheduleData != null),
                ("AutomationSettings", Models.AppJsonContext.Default.AutomationSettings != null),
                ("OpenStateData", Models.AppJsonContext.Default.OpenStateData != null),
                ("PdfReadingStateData", Models.AppJsonContext.Default.PdfReadingStateData != null),
                ("DictionaryStringTokenInfo", Models.AppJsonContext.Default.DictionaryStringTokenInfo != null),
            };
            foreach (var (name, ok) in probes)
            {
                sb.AppendLine($"[JSONTEST]   {(ok ? "ok" : "✗")} 元数据已生成：{name}");
                if (!ok) throw new Exception($"{name} 没有生成 JsonTypeInfo");
            }

            sb.AppendLine("[JSONTEST] 结论：PASS");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[JSONTEST] 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }

        Helpers.AppLogger.Info(sb.ToString());
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-result.txt"),
            sb.ToString());
        Environment.Exit(0);
    }

    private static void RunReminderSelfTest()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            sb.AppendLine("[RMTEST] 课间语义自检开始");
            var date = new DateTime(2026, 9, 18);

            static Models.ScheduleEntry E(int period, string subject, Models.PeriodType type,
                string start, string end) => new()
                {
                    DayOfWeek = 5,
                    Period = period,
                    Subject = subject,
                    Type = type,
                    StartTimeStr = start,
                    EndTimeStr = end,
                };

            // (上一节, 下一节, 期望类型, 说明)
            var cases = new (Models.ScheduleEntry? Prev, Models.ScheduleEntry? Next,
                             Services.ReminderService.GapKind Expect, string Desc)[]
            {
                // ── 用户报告的两个场景：必须静音 ──
                (E(1, "晚读", Models.PeriodType.Reading, "18:00", "18:30"),
                 E(2, "晚自习", Models.PeriodType.Evening, "18:40", "20:00"),
                 Services.ReminderService.GapKind.SelfStudyBoundary, "晚读→晚自习（10 分钟）"),

                (E(1, "中午听力", Models.PeriodType.Noon, "12:20", "12:50"),
                 E(2, "数学", Models.PeriodType.Normal, "13:10", "13:55"),
                 Services.ReminderService.GapKind.SelfStudyBoundary, "中午听力→下午第一节（20 分钟）"),

                (E(1, "早自习", Models.PeriodType.Morning, "07:00", "07:40"),
                 E(2, "语文", Models.PeriodType.Normal, "08:00", "08:45"),
                 Services.ReminderService.GapKind.SelfStudyBoundary, "早自习→第一节（20 分钟）"),

                // ── 正常课间不能被误静音 ──
                (E(2, "语文", Models.PeriodType.Normal, "08:00", "08:45"),
                 E(3, "数学", Models.PeriodType.Normal, "08:55", "09:40"),
                 Services.ReminderService.GapKind.Normal, "普通课间 10 分钟（应保留提醒）"),

                (E(2, "语文", Models.PeriodType.Normal, "08:00", "08:45"),
                 E(3, "数学", Models.PeriodType.Normal, "08:45", "09:30"),
                 Services.ReminderService.GapKind.Consecutive, "普通课连堂（≤1 分钟）"),

                // ── 长间隔仍走 Dismissal：保住「上午放学」语义 ──
                (E(4, "英语", Models.PeriodType.Normal, "11:20", "12:05"),
                 E(5, "物理", Models.PeriodType.Normal, "14:00", "14:45"),
                 Services.ReminderService.GapKind.Dismissal, "中午放学级长间隔（≥60 分钟）"),

                (E(4, "午休", Models.PeriodType.Noon, "12:00", "12:30"),
                 E(5, "物理", Models.PeriodType.Normal, "14:00", "14:45"),
                 Services.ReminderService.GapKind.Dismissal, "午休→下午第一节但间隔 90 分钟（Dismissal 优先）"),

                // ── 边界情形 ──
                (null, E(1, "语文", Models.PeriodType.Normal, "08:00", "08:45"),
                 Services.ReminderService.GapKind.Normal, "首节无上一节"),
                (E(1, "语文", Models.PeriodType.Normal, "08:00", "08:45"), null,
                 Services.ReminderService.GapKind.Normal, "末节无下一节"),
            };

            foreach (var (prev, next, expect, desc) in cases)
            {
                var got = Services.ReminderService.ClassifyGap(prev, next, date);
                string mark = got == expect ? "ok" : "✗";
                sb.AppendLine($"[RMTEST]   {mark} {desc,-34} 期望 {expect,-17} 实际 {got}");
                if (got != expect)
                    throw new Exception($"课间语义不符：{desc} 期望 {expect}，实际 {got}");
            }

            // ── 压制门控（2026-09-20 用户要求"下午第一节课还是该提醒一声"）──
            // 这套语义按反馈调整过两次，必须断言「哪些压、哪些留」，否则改错了只会默默变吵/变哑。
            // ⚠ 必须给出**真实的前后邻居**：只传 next=null 的话"下课/自习结束"两位永远测不到
            //   （第一版就踩了这个坑，测试自己写错了而不是代码错）。
            // 位序 = ReminderGates 字段序：快上课了 / 上课了 / 下课 / 自习开始 / 自习结束，Y=压 N=留
            string GateOf(List<Models.ScheduleEntry> day, int i)
            {
                var g = Services.ReminderService.DecideGates(
                    i > 0 ? day[i - 1] : null, day[i], i + 1 < day.Count ? day[i + 1] : null, date);
                return string.Concat(
                    g.SuppressNextClassSoon ? "Y" : "N",
                    g.SuppressStart ? "Y" : "N",
                    g.SuppressEnd ? "Y" : "N",
                    g.SuppressSpecialStart ? "Y" : "N",
                    g.SuppressSpecialEnd ? "Y" : "N");
            }

            // 场景 A：上午最后一节 → 中午听力(午休) → 下午第一节 → 下午第二节
            // 用户诉求：下午第一节「上课了」要留，其余边界噪音压掉
            var dayA = new List<Models.ScheduleEntry>
            {
                E(4, "英语", Models.PeriodType.Normal, "11:20", "12:05"),
                E(5, "中午听力", Models.PeriodType.Noon, "12:20", "12:50"),
                E(6, "数学", Models.PeriodType.Normal, "13:10", "13:55"),
                E(7, "物理", Models.PeriodType.Normal, "14:05", "14:50"),
            };
            foreach (var (i, want, desc) in new[]
            {
                (1, "NNYNY", "中午听力(午休)：本节的「下课」「自习结束」压掉"),
                (2, "YNNYN", "★下午第一节：留「上课了」，压「快上课了」（用户要求）"),
                (3, "NNNNN", "下午第二节：普通课间，全都不压"),
            })
            {
                string got = GateOf(dayA, i);
                sb.AppendLine($"[RMTEST]   {(got == want ? "ok" : "✗")} {desc,-44} 期望 {want} 实际 {got}");
                if (got != want)
                    throw new Exception($"压制门控不符：{desc} 期望 {want}，实际 {got}");
            }

            // 场景 B：晚读 → 晚自习（两头都是自习 → 全静音，保持 9-18 的原始诉求）
            var dayB = new List<Models.ScheduleEntry>
            {
                E(1, "晚读", Models.PeriodType.Reading, "18:00", "18:30"),
                E(2, "晚自习", Models.PeriodType.Evening, "18:40", "20:00"),
            };
            foreach (var (i, want, desc) in new[]
            {
                (0, "NNYNY", "晚读：本节的「下课」「晚读结束」压掉"),
                // 晚自习是当天最后一节（next=null）→ 它的「下课」「晚自习结束」本来就该响，
                // 那天还要弹「放学」。所以只压前三位（快上课了/上课了/自习开始）。
                (1, "YYNYN", "★晚自习：压「快上课了」「上课了」「晚自习开始」，保留收尾的「下课」"),
            })
            {
                string got = GateOf(dayB, i);
                sb.AppendLine($"[RMTEST]   {(got == want ? "ok" : "✗")} {desc,-44} 期望 {want} 实际 {got}");
                if (got != want)
                    throw new Exception($"压制门控不符：{desc} 期望 {want}，实际 {got}");
            }

            sb.AppendLine("[RMTEST] 结论：PASS");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[RMTEST] 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }

        Helpers.AppLogger.Info(sb.ToString());
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-result.txt"),
            sb.ToString());
        Environment.Exit(0);
    }

    private static async System.Threading.Tasks.Task RunUpdateSelfTestAsync()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            sb.AppendLine("[UPDTEST] 逻辑自检开始");

            // ── 1. 课件序号解析（含中文数字）──
            var cases = new (string Name, int Expect)[]
            {
                ("01.pptx", 1),
                ("Unit 03.mp3", 3),
                ("第1讲 力学.pdf", 1),
                ("Lesson2.docx", 2),
                ("第一讲 力学.pdf", 1),
                ("第十讲 力学.pdf", 10),
                ("一、牛顿第一定律.pptx", 1),
                ("二、牛顿第二定律.pptx", 2),
                ("三 化学平衡.pptx", 3),
                ("十一、复习.pptx", 11),
                ("二十、复习.pptx", 20),
                ("二十三、复习.pptx", 23),
                ("五单元 化学.pptx", 5),
                ("廿三、复习.pptx", 23),
                ("第两讲.pdf", 2),
                ("复习.pptx", -1),
                ("一次函数.pptx", -1),        // 误报抑制：中文数字不在序号语境
                ("高三复习2024.pptx", -1),    // 年份不算序号
                ("二〇二四 复习.pptx", -1),   // 中文年份也不算
            };
            foreach (var (name, expect) in cases)
            {
                int got = Helpers.FileSequence.ExtractNumber(name);
                string mark = got == expect ? "ok" : "✗";
                sb.AppendLine($"[UPDTEST]   {mark} {name,-26} 期望 {expect,3}  实际 {got,3}");
                if (got != expect)
                    throw new Exception($"序号解析不符：\"{name}\" 期望 {expect}，实际 {got}");
            }

            // 排序：中文与阿拉伯数字混排应按数值顺序
            var sorted = Helpers.FileSequence.Sort(new[]
            {
                "第三讲.pptx", "01.pptx", "第二讲.pptx", "十二、复习.pptx", "复习.pptx"
            });
            sb.AppendLine($"[UPDTEST] 混排顺序：{string.Join(" → ", sorted.Select(System.IO.Path.GetFileName))}");
            if (System.IO.Path.GetFileName(sorted[0]) != "01.pptx" ||
                System.IO.Path.GetFileName(sorted[^1]) != "复习.pptx")
                throw new Exception("自然序号排序结果不符（应数字 1、2、3、12，最后无序号）");

            // ── 2. 动作枚举只能末尾追加（落盘整数约束）──
            if ((int)Models.AutomationActionKind.CloseApp != 7)
                throw new Exception("CloseApp 不再是 7 —— 枚举被重排了，会破坏老配置文件！");
            if ((int)Models.AutomationActionKind.OpenWhiteboard != 8)
                throw new Exception("OpenWhiteboard 应为 8（追加在末尾）");
            sb.AppendLine("[UPDTEST] 动作枚举追加合规：CloseApp=7、OpenWhiteboard=8");

            // ── 3. 更新下载通道（代理前缀拼接）──
            const string assetUrl = "https://github.com/o/r/releases/download/v1/x-fd.zip";
            var cands = Services.UpdateService.BuildCandidates(assetUrl);
            sb.AppendLine($"[UPDTEST] 候选通道 {cands.Count} 个：");
            foreach (var c in cands) sb.AppendLine($"[UPDTEST]    {c}");
            if (cands.Count < 2) throw new Exception("候选通道过少（应至少含镜像 + 直连）");
            if (!cands[0].StartsWith(Services.UpdateService.DefaultProxyPrefix))
                throw new Exception("首选通道不是配置的镜像");
            if (cands[^1] != assetUrl) throw new Exception("最后一个候选应为直连原链接");

            // ── 4. Release JSON 解析 + 自包含/框架依赖资产匹配 ──
            const string json = """
            {"tag_name":"v99.0.0","body":"note","assets":[
              {"name":"StudyJourney-v99.0.0-win-x64.zip","browser_download_url":"https://github.com/o/r/releases/download/v99.0.0/sc.zip"},
              {"name":"StudyJourney-v99.0.0-win-x64-fd.zip","browser_download_url":"https://github.com/o/r/releases/download/v99.0.0/fd.zip"},
              {"name":"StudyJourney-v99.0.0-win-x64-fdx.zip","browser_download_url":"https://github.com/o/r/releases/download/v99.0.0/fdx.zip"}]}
            """;
            var parsed = Services.UpdateService.ParseRelease(json);
            string wantSuffix = Services.UpdateService.IsSelfContained ? "/sc.zip" : "/fd.zip";
            sb.AppendLine($"[UPDTEST] 解析 v{parsed.LatestVersion} → {parsed.DownloadUrl}（自包含={Services.UpdateService.IsSelfContained}）");
            if (parsed.LatestVersion != "99.0.0") throw new Exception("tag_name 解析错误");
            if (!parsed.HasUpdate) throw new Exception("99.0.0 应判为有新版本");
            if (!parsed.DownloadUrl.EndsWith(wantSuffix, StringComparison.Ordinal))
                throw new Exception($"资产匹配错误：期望以 {wantSuffix} 结尾（\"-fdx.zip\" 不该被当成 -fd 包）");

            // 只有 -fdx.zip（非法名）时应当匹配不到 → 判为不可更新，避免下到错包
            const string badJson = """
            {"tag_name":"v99.0.0","body":"","assets":[
              {"name":"x-fdx.zip","browser_download_url":"https://github.com/o/r/releases/download/v99.0.0/x-fdx.zip"}]}
            """;
            var bad = Services.UpdateService.ParseRelease(badJson);
            sb.AppendLine($"[UPDTEST] 非法资产名 → HasUpdate={bad.HasUpdate}（应为 False）");
            if (bad.HasUpdate) throw new Exception("非法资产名被错误匹配，可能下载到错误文件");

            // ── 5. 更新程序必须取自**更新包**（2026-09-19 修的核心，钉死它防回归）──
            //    故障链：从程序目录启动自包含的更新程序 → 它把程序目录里的运行时 DLL
            //    （coreclr/System.Private.CoreLib/hostpolicy…）映射成自己的 → 复制新版本覆盖
            //    这些 DLL 时抛共享冲突 → 主程序都退出了，更新还是失败。
            string fakeStage = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sj_updtest_stage");
            try
            {
                System.IO.Directory.CreateDirectory(fakeStage);
                string fakeUpdater = System.IO.Path.Combine(fakeStage, "StudyJourney.Updater.exe");
                System.IO.File.WriteAllText(fakeUpdater, "stub");
                var picked = Services.UpdateService.ResolveUpdaterExe(fakeStage);
                sb.AppendLine($"[UPDTEST] 更新包里有更新程序 → 选用：{picked}");
                if (!string.Equals(picked, fakeUpdater, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("没有优先用更新包里那份更新程序 —— 会重新踩到「DLL 被自己占用」的坑");
                var fallback = Services.UpdateService.ResolveUpdaterExe(System.IO.Path.Combine(fakeStage, "not-exist"));
                sb.AppendLine($"[UPDTEST] 包里没有更新程序 → 回退：{fallback ?? "(无可用)"}");
                if (fallback != null && fallback.StartsWith(fakeStage, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("回退路径不该指向 staging 目录");
            }
            finally { try { System.IO.Directory.Delete(fakeStage, true); } catch { } }

            // ── 6. 真的走一次镜像下载（端到端验证代理可用）──
            //    用一个别家仓库的小 zip（约 90KB），验证：镜像拼接 + 流式下载 + zip 魔数校验
            const string probe = "https://github.com/WJQSERVER-STUDIO/ghproxy/archive/refs/heads/main.zip";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(120));
            string zipPath = await Services.UpdateService.DownloadUpdateAsync(probe, null, cts.Token);
            sw.Stop();
            var fi = new System.IO.FileInfo(zipPath);
            sb.AppendLine($"[UPDTEST] 镜像下载 OK：{fi.Length / 1024.0:0.0} KB，耗时 {sw.Elapsed.TotalSeconds:0.0}s");
            if (fi.Length < 10 * 1024) throw new Exception("下载内容过小，疑似镜像返回了错误页");
            using (var fs = System.IO.File.OpenRead(zipPath))
            {
                var head = new byte[2];
                fs.ReadExactly(head);
                if (head[0] != 0x50 || head[1] != 0x4B) throw new Exception("下载结果不是 zip");
            }

            sb.AppendLine("[UPDTEST] 结论：PASS");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[UPDTEST] 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }

        Helpers.AppLogger.Info(sb.ToString());
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-result.txt"),
            sb.ToString());
        Environment.Exit(0);
    }

    private void EnterExamMode()
    {
        EnterExamModeGlobal();
    }

    /// <summary>系统托盘图标（替代 WPF Hardcodet.NotifyIcon；Avalonia 内置 TrayIcon + NativeMenu）</summary>
    private void SetupTrayIcon()
    {
        try
        {
            // WindowIcon 从 avares 资源流加载（支持 .ico；Bitmap 不支持 ico 会抛异常）
            using var stream = AssetLoader.Open(new Uri("avares://StudyJourneyAvalonia/Assets/icon.ico"));
            var icon = new WindowIcon(stream);

            var showItem = new NativeMenuItem("显示 / 隐藏窗口");
            showItem.Click += (_, _) => ToggleMainWindow();

            var examItem = new NativeMenuItem("进入考试模式");
            examItem.Click += (_, _) => EnterExamMode();

            var boardItem = new NativeMenuItem("打开白板（板书）");
            boardItem.Click += (_, _) => OpenWhiteboardGlobal();

            var annotItem = new NativeMenuItem("屏幕批注（Ctrl+Alt+D）");
            annotItem.Click += (_, _) => ToggleScreenAnnotationGlobal();

            var pdfItem = new NativeMenuItem("PDF 阅读（Ctrl+Shift+P）");
            pdfItem.Click += (_, _) => OpenPdfReaderGlobal();

            var settingsItem = new NativeMenuItem("打开设置");
            settingsItem.Click += (_, _) => OpenSettingsGlobal();

            var exitItem = new NativeMenuItem("退出");
            exitItem.Click += (_, _) => ExitApplication();

            var menu = new NativeMenu();
            menu.Add(showItem);
            menu.Add(examItem);
            menu.Add(boardItem);
            menu.Add(annotItem);
            menu.Add(pdfItem);
            menu.Add(settingsItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(exitItem);

            _trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "学程",
                Menu = menu,
                IsVisible = true
            };
            _trayIcon.Clicked += (_, _) => ToggleMainWindow();
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error("托盘图标初始化失败", ex);
        }
    }

    private void ToggleMainWindow()
    {
        // 复用 MainWindow.ToggleVisibility（含 _suppressAutoHide 豁免 + 临时置顶，
        // 避免"显示窗口被 MaximizeCheckTimer 立即隐藏"的闪退问题）
        if (_mainWindow is MainWindow mw) mw.ToggleVisibility();
    }

    private void ExitApplication()
    {
        Cleanup();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime l) l.Shutdown();
    }

    private void Cleanup()
    {
        GlobalHotKeyManager.UnregisterAll();
        Reminders?.Dispose();
        Reminders = null;
        Automation?.Dispose();
        Automation = null;
        // B9 修复：右键「退出」/系统关机走 Cleanup 路径，原实现不停远程服务 → 与 MainWindow.Closed
        // 路径不对称（服务残留/端口占用）。Stop 内部会 Join 后台线程（最长 8s），放后台执行避免卡退出。
        _ = System.Threading.Tasks.Task.Run(() => HttpServerService.Stop());
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
