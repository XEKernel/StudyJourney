using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Views;

namespace StudyJourney.Avalonia.ViewModels.Settings;

/// <summary>
/// 「倒计时」设置页的视图模型 —— **本项目 MVVM 化的样板（2026-10-06）**。
///
/// <para><b>为什么先拿这一页开刀</b>：它是设置页里最干净的一页（纯「数据→表单」映射、无跨页依赖），
/// 而 <c>settings</c> 自检模式已经覆盖「各设置页实例化 + Load/Apply 往返」——
/// 也就是说这次改造<b>有安全网</b>，不像改墨迹/PDF 那些渲染窗口那样只能靠人眼试。</para>
///
/// <para><b>它顺手修掉了一个结构性问题</b>：原来「自定义倒计时」列表是在
/// <c>BuildCountdownRows()</c> 里<b>命令式全量重建</b>面板的，每改一个字就重建一遍；
/// 代码里还留着一个 workaround ——「不能在按钮自己的 Click 里把它所在的那行从视觉树摘掉，
/// 得 <c>Dispatcher.Post</c> 到下一轮再重建」。改成 <see cref="ObservableCollection{T}"/>
/// + ItemsControl 之后，删除行由绑定自动完成，<b>这类"正在被遍历时移除自身"的坑从结构上消失</b>。</para>
///
/// <para><b>AOT 纪律</b>：页面 XAML 用 <c>x:DataType</c> + <b>编译绑定</b>。
/// 原页面写的是 <c>x:CompileBindings="False"</c>（即反射绑定），AOT 下不可用，顺手纠正。</para>
/// </summary>
public partial class CountdownPageViewModel : ObservableObject
{
    /// <summary>时刻未填时的默认值（09:00 是绝大多数考试的开工时间）</summary>
    public static readonly TimeSpan DefaultTime = new(9, 0, 0);

    /// <summary>Apply 时遇到「颜色无法识别」这类需要弹窗告知的问题时，由页面注入 <c>App.ShowMessageAsync</c>。
    /// VM 不直接调静态 UI API（否则它就没法在无 UI 的自检里跑）。</summary>
    public Action<string, string>? ReportError { get; set; }

    // ── 字体与字号 ────────────────────────────────────────────
    [ObservableProperty] private string _fontFamily = "";
    [ObservableProperty] private double _fontSize = 12;
    /// <summary>滑条右边的数字。绑到滑条上会与 FontSize 双向同步，逻辑放 VM，页面不写 ValueChanged。</summary>
    [ObservableProperty] private string _fontSizeText = "12";

    // ── 窗口透明度 ────────────────────────────────────────────
    [ObservableProperty] private double _overallOpacity = 1.0;
    [ObservableProperty] private string _opacityText = "100%";

    // ── 配色 ────────────────────────────────────────────────
    [ObservableProperty] private string _textColorHex = "";
    [ObservableProperty] private string _accentColorHex = "";

    // ── 可见内容 / 时间精度 ──────────────────────────────────
    [ObservableProperty] private bool _showProgressBar = true;
    [ObservableProperty] private bool _showProgressText = true;
    [ObservableProperty] private bool _showDays = true;
    [ObservableProperty] private bool _showHours = true;
    [ObservableProperty] private bool _showMinutes = true;
    [ObservableProperty] private bool _showSeconds = true;

    // ── 目标 / 起算日期 ──────────────────────────────────────
    [ObservableProperty] private DateTimeOffset? _gaokaoDate;
    [ObservableProperty] private TimeSpan? _gaokaoTime;
    [ObservableProperty] private DateTimeOffset? _startDate;
    /// <summary>「现在到底存的是什么」—— 老师不用猜选择器里那几个框是干嘛的。</summary>
    [ObservableProperty] private string _dateHintText = "";

    // ── 自定义倒计时（副本编辑，未保存不落盘）─────────────────
    public ObservableCollection<CountdownItemViewModel> Countdowns { get; } = new();
    [ObservableProperty] private bool _isCountdownListEmpty = true;

    /// <summary>只有「自定义倒计时」的增删改名会被 IsDirty 跟踪。
    /// <para>⚠ 这是<b>沿用原语义</b>，不是遗漏：其它控件的值本来就每次点「保存设置」都全量 Apply，
    /// 只有倒计时列表是"行内即时编辑副本"，所以只有它需要"切页/关窗前问一句"。</para></summary>
    public bool IsDirty { get; private set; }

    // ── Load / Apply ─────────────────────────────────────────

    public void Load(AppSettings s)
    {
        FontFamily = s.FontFamily;
        FontSize = s.FontSize;
        OverallOpacity = s.OverallOpacity;
        ShowProgressBar = s.ShowProgressBar;
        ShowProgressText = s.ShowProgressText;
        ShowDays = s.ShowDays;
        ShowHours = s.ShowHours;
        ShowMinutes = s.ShowMinutes;
        ShowSeconds = s.ShowSeconds;

        // 日期：手打格式串 → 选择器（值一律经 DateTimeStr 转换，不让 VM 自己拼串）
        var gao = DateTimeStr.ParseDateTime(s.GaokaoDateStr);
        GaokaoDate = DateTimeStr.ToOffset(gao);
        GaokaoTime = gao != null ? new TimeSpan(gao.Value.Hour, gao.Value.Minute, 0) : DefaultTime;
        StartDate = DateTimeStr.ToOffset(DateTimeStr.ParseDate(s.StartDateStr));

        // 颜色：色板值
        TextColorHex = s.TextColor.ToString();
        AccentColorHex = s.AccentColor.ToString();

        // 倒计时列表复制到副本编辑
        Countdowns.Clear();
        foreach (var c in s.CustomCountdowns ?? new())
            Countdowns.Add(CountdownItemViewModel.FromModel(this, c));
        IsCountdownListEmpty = Countdowns.Count == 0;

        IsDirty = false;
        RefreshDateHint();
    }

    public void Apply(AppSettings s)
    {
        if (!string.IsNullOrWhiteSpace(FontFamily)) s.FontFamily = FontFamily;

        s.FontSize = (int)FontSize;
        s.OverallOpacity = OverallOpacity;
        s.ShowProgressBar = ShowProgressBar;
        s.ShowProgressText = ShowProgressText;
        s.ShowDays = ShowDays;
        s.ShowHours = ShowHours;
        s.ShowMinutes = ShowMinutes;
        s.ShowSeconds = ShowSeconds;

        // 日期：选择器保证值一定合法（旧版"手打 → 格式非法 → 弹框提示"那条路径已不可能发生）；
        // 秒沿用原值（老师改别的设置时不该顺手把 09:00:30 悄悄变成 09:00:00）。
        var gaoDate = DateTimeStr.ToDate(GaokaoDate);
        var gaoTime = GaokaoTime ?? DefaultTime;
        int sec = gaoDate != null ? DateTimeStr.PreserveSecond(s.GaokaoDateStr, gaoDate.Value, gaoTime) : 0;
        s.GaokaoDateStr = DateTimeStr.ComposeDateTime(gaoDate, gaoTime, DefaultTime, sec);
        s.StartDateStr = DateTimeStr.ComposeDate(DateTimeStr.ToDate(StartDate));

        // 颜色：色板值理论上一定合法；不合法说明配置文件被外部改坏了 → 保留原值并明确提示
        if (ColorSwatch.TryParse(TextColorHex, out var tc)) s.TextColor = tc;
        else ReportError?.Invoke("倒计时", $"文字颜色无法识别：{TextColorHex}\n已保留原值。");

        if (ColorSwatch.TryParse(AccentColorHex, out var ac)) s.AccentColor = ac;
        else ReportError?.Invoke("倒计时", $"强调色无法识别：{AccentColorHex}\n已保留原值。");

        // 倒计时副本写回设置
        s.CustomCountdowns = Countdowns.Select(c => c.ToModel()).ToList();
        IsDirty = false;
    }

    // ── 联动：值变了刷新派生的展示文本 ────────────────────────
    partial void OnFontSizeChanged(double value) => FontSizeText = ((int)value).ToString();
    partial void OnOverallOpacityChanged(double value) => OpacityText = $"{value * 100:F0}%";
    partial void OnGaokaoDateChanged(DateTimeOffset? value) => RefreshDateHint();
    partial void OnGaokaoTimeChanged(TimeSpan? value) => RefreshDateHint();

    private void RefreshDateHint()
    {
        var d = DateTimeStr.ToDate(GaokaoDate);
        DateHintText = d == null
            ? "未设置日期 → 主窗口不显示高考倒计时。"
            : $"当前设定：{DateTimeStr.ComposeDateTime(d, GaokaoTime ?? DefaultTime, DefaultTime)}";
    }

    // ── 命令 ────────────────────────────────────────────────

    [RelayCommand]
    private void AddCountdown()
    {
        Countdowns.Add(new CountdownItemViewModel(this)
        {
            Name = "新倒计时",
            DateOffset = DateTimeStr.ToOffset(DateTime.Today.AddDays(30)),
        });
        IsCountdownListEmpty = false;
        IsDirty = true;
    }

    [RelayCommand]
    private void ClearGaokaoDate() => GaokaoDate = null;

    [RelayCommand]
    private void ClearStartDate() => StartDate = null;

    /// <summary>删除一行（由行内 DeleteCommand 调用）。</summary>
    internal void RemoveCountdown(CountdownItemViewModel item)
    {
        if (!Countdowns.Remove(item)) return;
        IsCountdownListEmpty = Countdowns.Count == 0;
        IsDirty = true;
    }

    /// <summary>行的名称/日期被改动时由行 VM 回调，置未保存标记。</summary>
    internal void MarkDirty() => IsDirty = true;
}

/// <summary>自定义倒计时的一行。名字与日期都是即时编辑副本，点「保存设置」才写回 settings.json。</summary>
public sealed partial class CountdownItemViewModel : ObservableObject
{
    private readonly CountdownPageViewModel _owner;

    public CountdownItemViewModel(CountdownPageViewModel owner) => _owner = owner;

    public static CountdownItemViewModel FromModel(CountdownPageViewModel owner, CustomCountdown c) => new(owner)
    {
        Name = c.Name,
        DateOffset = DateTimeStr.ToOffset(DateTimeStr.ParseDate(c.DateStr)),
    };

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private DateTimeOffset? _dateOffset;

    partial void OnNameChanged(string value) => _owner.MarkDirty();
    partial void OnDateOffsetChanged(DateTimeOffset? value) => _owner.MarkDirty();

    public CustomCountdown ToModel() => new()
    {
        Name = Name,
        DateStr = DateTimeStr.ComposeDate(DateTimeStr.ToDate(DateOffset)),
    };

    /// <summary>行内删除按钮。<b>命令挂在行 VM 上而不是页面</b> —— 这样 XAML 里不需要
    /// <c>$parent[ItemsControl].DataContext</c> 那种绕路绑定，删除参数天然就是自己。</summary>
    [RelayCommand]
    private void Delete() => _owner.RemoveCountdown(this);
}
