using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;   // ⚠ 必须用 using：本文件在 StudyJourney.Avalonia.Views 里，
                                    //    写全名 Avalonia.Platform.* 会被相对解析成 StudyJourney.Avalonia.Platform（本仓老坑）
using Avalonia.Threading;
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Services;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 听力播放器窗口（v2.27.0，规划 2.9）。
///
/// 解决的原问题（用户 2026-09-29）：*"自动化能够打开系统的播放器（UWP 应用），但播完后还需要手动关闭"*
///   → 内置播放器：**整份播完后延时 N 秒自动关窗**，并且因为能确切知道"播完了没"，
///     可以据此**只在整份播完时推进听力进度**（听一半关掉 → 下次续听同一份）。
///
/// 单例：听力不该同时开两个窗口（第二个窗口会抢同一个声卡设备、进度也会打架）。
/// </summary>
public sealed class ListeningPlayerWindow : Window
{
    // ── 单例 ────────────────────────────────────────────────
    private static ListeningPlayerWindow? _instance;

    /// <summary>只打开窗口（不改变播放内容）</summary>
    public static void ShowOrActivate()
    {
        if (_instance == null)
        {
            _instance = new ListeningPlayerWindow();
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        else
        {
            _instance.Show();
            _instance.Activate();
        }
    }

    /// <summary>自动化入口：解析"这次该播哪一份" → 打开窗口并播放。返回是否真的开播（供临时任务判定）。</summary>
    public static bool PlayAuto(DateTime now, out string reason)
    {
        var svc = App.Listening;
        if (svc == null) { reason = "听力服务未启动"; return false; }

        var file = svc.ResolveAuto(now, out reason);
        if (file == null) return false;

        var src = svc.ActiveSource;
        ShowOrActivate();
        _instance?.StartTrack(file, src?.Id ?? "");
        return true;
    }

    /// <summary>手动入口：播放指定文件（可带来源 Id；老师手动播 → 该来源成为活跃）</summary>
    public static void PlayFile(string path, string sourceId = "")
    {
        ShowOrActivate();
        _instance?.StartTrack(path, sourceId);
    }

    // ── 控件 ────────────────────────────────────────────────
    private readonly ComboBox _sourceCombo = new() { FontSize = 12, MinWidth = 190 };
    private readonly TextBlock _titleTb = new() { FontSize = 16, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _progressTb = new()
    {
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
    };
    private readonly TextBlock _statusTb = new()
    {
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x8A, 0xC8, 0xFF)),
    };
    private readonly ProgressBar _bar = new()
    {
        Height = 4, Minimum = 0, Maximum = 100, Value = 0,
        Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x2B, 0x6C, 0xB0)),
        Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
    };
    private readonly TextBlock _posTb = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _totalTb = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _playBtn = new() { Content = "▶ 播放", MinWidth = 92 };
    private readonly Button _prevBtn = new() { Content = "⏮ 上一首" };
    private readonly Button _nextBtn = new() { Content = "⏭ 下一首" };
    private readonly Button _stopBtn = new() { Content = "⏹ 停止" };
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, Width = 130 };
    private readonly TextBlock _volumeTb = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };

    // ── 状态 ────────────────────────────────────────────────
    private readonly AudioPlayer _player = new();
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _closeTimer;
    private bool _loadingUi;              // 回填控件时不触发事件
    private bool _seeking;                // 拖动进度条时不要被 tick 覆盖
    private string _sourceId = "";
    private int _closeLeft;
    private bool _autoCloseArmed;

    /// <summary>internal 供自检实例化（校验整棵可视树能建起来）——对外仍只走上面的单例入口</summary>
    internal ListeningPlayerWindow()
    {
        Title = "听力播放";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = true;
        Topmost = true;                          // 上课/中午放听力时不该被别的窗口挡住
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x22));

        Content = BuildUi();

        _player.Finished += OnTrackFinished;

        // 每 250ms 刷新进度（NAudio 播放线程不碰 UI）
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tick.Tick += (_, _) => RefreshProgress();
        _tick.Start();

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _closeTimer.Tick += (_, _) => CloseCountdown();

        LoadSourcesIntoCombo();
        LoadVolume();
        UpdateButtons();
    }

    // ── UI 构建 ─────────────────────────────────────────────

    private Control BuildUi()
    {
        var root = new StackPanel { Margin = new Thickness(18, 14, 18, 16), Spacing = 10 };

        // 来源选择
        var srcRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        srcRow.Children.Add(new TextBlock
        {
            Text = "来源", FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
        });
        srcRow.Children.Add(_sourceCombo);
        var openBtn = new Button { Content = "播放文件…", FontSize = 12 };
        openBtn.Click += (_, _) => _ = PickFileAsync();
        srcRow.Children.Add(openBtn);
        var openDirBtn = new Button { Content = "播放文件夹…", FontSize = 12 };
        openDirBtn.Click += (_, _) => _ = PickFolderAsync();
        srcRow.Children.Add(openDirBtn);
        root.Children.Add(srcRow);

        root.Children.Add(_titleTb);
        root.Children.Add(_progressTb);

        // 进度条（可点/拖跳转）
        _bar.PointerPressed += (_, e) =>
        {
            _seeking = true;
            SeekFromPointer(e.GetPosition(_bar).X);
        };
        _bar.PointerMoved += (_, e) => { if (_seeking) SeekFromPointer(e.GetPosition(_bar).X); };
        _bar.PointerReleased += (_, _) => _seeking = false;
        root.Children.Add(_bar);

        var timeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        timeRow.Children.Add(_posTb);
        Grid.SetColumn(_totalTb, 2);
        timeRow.Children.Add(_totalTb);
        root.Children.Add(timeRow);

        // 控制按钮
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _prevBtn.Click += (_, _) => Step(-1);
        _playBtn.Click += (_, _) => TogglePlay();
        _stopBtn.Click += (_, _) => { _player.Stop(); UpdateButtons(); };
        _nextBtn.Click += (_, _) => Step(1);
        btnRow.Children.Add(_prevBtn);
        btnRow.Children.Add(_playBtn);
        btnRow.Children.Add(_stopBtn);
        btnRow.Children.Add(_nextBtn);
        root.Children.Add(btnRow);

        // 音量
        var volRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        volRow.Children.Add(new TextBlock { Text = "🔊", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        _volume.ValueChanged += (_, _) =>
        {
            if (_loadingUi) return;
            _player.Volume = (float)(_volume.Value / 100.0);
            _volumeTb.Text = $"{(int)_volume.Value}%";
            var d = App.Listening?.Data;
            if (d != null) { d.Volume = (int)_volume.Value; App.Listening!.Save(); }
        };
        volRow.Children.Add(_volume);
        volRow.Children.Add(_volumeTb);
        root.Children.Add(volRow);

        root.Children.Add(_statusTb);

        var hint = new TextBlock
        {
            Text = "播完会自动关闭（秒数在「设置 → 课表 → 听力播放器」里改）。听一半关掉不会记成「听过」，下次会续播这一份。",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
        };
        root.Children.Add(hint);

        _sourceCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi) return;
            if (_sourceCombo.SelectedItem is SourceItem it)
            {
                App.Listening?.SetActive(it.Id);
                // 切来源 = 播这一套"该播的那一份"
                var svc = App.Listening;
                var src = svc?.FindSource(it.Id);
                var cands = ListeningRules.CandidatesOf(src);
                var file = ListeningRules.CurrentToPlay(src, cands);
                if (file != null) StartTrack(file, it.Id);
                else _statusTb.Text = cands.Count == 0 ? "这一套里没有音频文件" : "这一套已经听完了";
            }
        };

        return root;
    }

    private sealed record SourceItem(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    // ── 载入数据 ────────────────────────────────────────────

    private void LoadSourcesIntoCombo()
    {
        _loadingUi = true;
        try
        {
            var svc = App.Listening;
            var items = new List<SourceItem>();
            if (svc != null)
                foreach (var s in svc.Sources)
                    items.Add(new SourceItem(s.Id, s.Once ? $"{s.Name}（一次性）" : s.Name));

            _sourceCombo.ItemsSource = items;
            var active = svc?.ActiveSource;
            _sourceCombo.SelectedIndex = active == null
                ? (items.Count > 0 ? 0 : -1)
                : Math.Max(items.FindIndex(i => i.Id == active.Id), 0);

            if (items.Count == 0)
                _statusTb.Text = "还没配置听力来源 —— 可在「播放文件…」里直接选一份录音，或到设置里添加来源";
        }
        finally { _loadingUi = false; }
    }

    private void LoadVolume()
    {
        _loadingUi = true;
        try
        {
            int v = Math.Clamp(App.Listening?.Data.Volume ?? 80, 0, 100);
            _volume.Value = v;
            _volumeTb.Text = $"{v}%";
            if (_player.HasTrack) _player.Volume = (float)(v / 100.0);
        }
        finally { _loadingUi = false; }
    }

    // ── 播放控制 ────────────────────────────────────────────

    /// <summary>开始播一份（会把它记为"正在播"，但**不**算听过）</summary>
    private void StartTrack(string path, string sourceId)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            _player.Load(path);
            _player.Volume = (float)(Math.Clamp(App.Listening?.Data.Volume ?? 80, 0, 100) / 100.0);
            _autoCloseArmed = false;
            _closeTimer.Stop();

            _sourceId = sourceId;
            if (!string.IsNullOrEmpty(sourceId))
            {
                App.Listening?.MarkPlaying(sourceId, path);   // 指针指向它，但 Finished = false
                PromoteComboSelection(sourceId);
            }

            _player.Play();
            RefreshTrackText();
            UpdateButtons();
            _statusTb.Text = "正在播放…";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"听力：打开失败 {path}", ex);
            _statusTb.Text = "打不开这份音频：" + ex.Message;
            // 可能是缺 Media Foundation（m4a/wma）。给老师一条能行动的提示。
            if (path.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".wma", StringComparison.OrdinalIgnoreCase))
                _statusTb.Text += "（m4a/wma 需要系统 Media Foundation；可先转成 mp3）";
        }
    }

    private void PromoteComboSelection(string sourceId)
    {
        if (_sourceCombo.ItemsSource is not IEnumerable<SourceItem> items) return;
        int idx = items.ToList().FindIndex(i => i.Id == sourceId);
        if (idx >= 0 && _sourceCombo.SelectedIndex != idx)
        {
            _loadingUi = true;
            try { _sourceCombo.SelectedIndex = idx; }
            finally { _loadingUi = false; }
        }
    }

    private List<string> CurrentCandidates()
    {
        var svc = App.Listening;
        var src = svc?.FindSource(_sourceId) ?? svc?.ActiveSource;
        return ListeningRules.CandidatesOf(src);
    }

    private void Step(int delta)
    {
        var cands = CurrentCandidates();
        var cur = _player.CurrentFile;
        if (cands.Count == 0 || string.IsNullOrEmpty(cur)) return;

        var next = delta > 0
            ? ListeningRules.NextStrict(cur, cands)
            : ListeningRules.PreviousStrict(cur, cands);
        if (next == null)
        {
            _statusTb.Text = delta > 0 ? "这一套已经是最后一份了" : "这已经是第一份了";
            return;
        }
        StartTrack(next, _sourceId);
    }

    private void TogglePlay()
    {
        if (!_player.HasTrack) { _statusTb.Text = "先选一份录音（上面的「播放文件…」或来源下拉）"; return; }
        if (_player.IsPlaying) { _player.Pause(); _statusTb.Text = "已暂停"; }
        else { _player.Play(); _statusTb.Text = "正在播放…"; _autoCloseArmed = false; _closeTimer.Stop(); }
        UpdateButtons();
    }

    private void SeekFromPointer(double x)
    {
        if (!_player.HasTrack || _bar.Bounds.Width <= 0) return;
        double pct = Math.Clamp(x / _bar.Bounds.Width * 100.0, 0, 100);
        _bar.Value = pct;
        _player.Seek(TimeSpan.FromTicks((long)(_player.Total.Ticks * pct / 100.0)));
        RefreshTimeText();
    }

    private void RefreshProgress()
    {
        if (_seeking || !_player.HasTrack) return;
        var total = _player.Total;
        var pos = _player.Position;
        _bar.Value = total.TotalMilliseconds > 0
            ? Math.Clamp(pos.TotalMilliseconds / total.TotalMilliseconds * 100.0, 0, 100)
            : 0;
        RefreshTimeText();
        UpdateButtons();
    }

    private void RefreshTimeText()
    {
        _posTb.Text = AudioPlayer.FormatTime(_player.Position);
        _totalTb.Text = AudioPlayer.FormatTime(_player.Total);
    }

    private void RefreshTrackText()
    {
        var path = _player.CurrentFile;
        _titleTb.Text = string.IsNullOrEmpty(path) ? "（未选择录音）" : Path.GetFileName(path);
        var cands = CurrentCandidates();
        _progressTb.Text = ListeningRules.ProgressText(path, cands) +
                           (string.IsNullOrEmpty(path) ? "" : "　·　" + (Path.GetDirectoryName(path) ?? ""));
    }

    private void UpdateButtons()
    {
        _playBtn.Content = _player.IsPlaying ? "⏸ 暂停" : "▶ 播放";
        var cands = CurrentCandidates();
        _nextBtn.IsEnabled = ListeningRules.HasNext(_player.CurrentFile, cands);
        _prevBtn.IsEnabled = ListeningRules.PreviousStrict(_player.CurrentFile, cands) != null;
    }

    // ── 播完 ────────────────────────────────────────────────

    /// <summary>整份**自然播完**（NAudio 线程回调 → 封送回 UI 线程）</summary>
    private void OnTrackFinished()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var path = _player.CurrentFile;
            var svc = App.Listening;
            if (svc != null && !string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(_sourceId))
            {
                svc.MarkFinished(_sourceId, path);            // 到这里才推进进度（下次播下一份）

                var src = svc.FindSource(_sourceId);
                if (src != null && ListeningRules.IsAtEnd(src, ListeningRules.CandidatesOf(src)))
                {
                    _statusTb.Text = $"「{src.Name}」这套已经听完了";
                    svc.FallbackFromOnceSource(src);            // 一次性来源（临时文件夹/桌面）→ 回退
                }
                else
                {
                    _statusTb.Text = "这一份播完了";
                }
            }
            UpdateButtons();
            RefreshProgress();

            int wait = svc?.Data.AutoCloseSeconds ?? 3;
            if (wait < 0)
            {
                _statusTb.Text += "（设置为不自动关闭）";
                return;
            }
            _closeLeft = wait;
            _autoCloseArmed = true;
            if (wait == 0) { Close(); return; }
            _statusTb.Text += $"　{_closeLeft} 秒后自动关闭";
            _closeTimer.Start();
        });
    }

    private void CloseCountdown()
    {
        if (!_autoCloseArmed) { _closeTimer.Stop(); return; }
        _closeLeft--;
        if (_closeLeft <= 0)
        {
            _closeTimer.Stop();
            Close();
            return;
        }
        _statusTb.Text = $"这一份播完了　{_closeLeft} 秒后自动关闭";
    }

    protected override void OnClosed(EventArgs e)
    {
        _tick.Stop();
        _closeTimer.Stop();
        _player.Stop();      // 主动停 → 不算"听过"，进度指针保持在上次那份（下次续播）
        _player.Dispose();
        base.OnClosed(e);
    }

    // ── 选文件 / 选文件夹 ────────────────────────────────────

    private async System.Threading.Tasks.Task PickFileAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择听力录音",
                AllowMultiple = false,
            });
            var path = files?.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;
            PlayFile(path, _sourceId);
        }
        catch (Exception ex) { AppLogger.Error("听力：选文件失败", ex); }
    }

    private async System.Threading.Tasks.Task PickFolderAsync()
    {
        try
        {
            var dirs = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择听力录音所在的文件夹",
                AllowMultiple = false,
            });
            var dir = dirs?.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrEmpty(dir)) return;

            // 临时来源：老师随手指的文件夹（含桌面）→ 播完自动回退到常规来源
            var svc = App.Listening;
            if (svc == null) return;
            var src = svc.AddSource(Path.GetFileName(dir.TrimEnd('\\', '/')), dir, once: true);
            LoadSourcesIntoCombo();
            var cands = ListeningRules.CandidatesOf(src);
            var file = ListeningRules.CurrentToPlay(src, cands);
            if (file == null) { _statusTb.Text = "这个文件夹里没有音频文件"; return; }
            StartTrack(file, src.Id);
        }
        catch (Exception ex) { AppLogger.Error("听力：选文件夹失败", ex); }
    }
}
