using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyJourney.Avalonia.Helpers.GradeAnalysis;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Models.GradeAnalysis;
using StudyJourney.Avalonia.Services.GradeAnalysis;
using StudyJourney.Avalonia.Views;   // ColorSwatch（小组配色）

namespace StudyJourney.Avalonia.ViewModels.GradeAnalysis;

// ═══════════════════════════════════════════════════════════════════════════
//  行模型（表格用）
//  ⚠ 这些类型直接暴露 IBrush：本模块是单程序集应用的一部分，把「刷子」放在行模型上
//    比在 XAML 里堆一堆 String→Brush 转换器更省事，也避免转换器在 AOT 下的注册问题。
//    颜色全部来自 ChartPalette 常量，不依赖主题资源查找。
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>排行榜的一行。科目固定 6 列（本项目就是六科），因此用 S1..S6 的具名属性而不用数组 ——
/// 具名属性才能走**编译绑定**，数组下标在编译绑定下无法表达。</summary>
public sealed class LeaderboardRow
{
    public string RankText { get; init; } = "";
    public string Name { get; init; } = "";
    public string StudentNo { get; init; } = "";
    public string TotalText { get; init; } = "";
    public string GradeRankText { get; init; } = "";
    public string RankDeltaText { get; init; } = "—";
    public IBrush RankDeltaBrush { get; init; } = ChartPalette.MutedBrush;
    public string TotalDeltaText { get; init; } = "—";
    public string TrendText { get; init; } = "";
    public IBrush TrendBrush { get; init; } = ChartPalette.MutedBrush;
    public string S1 { get; init; } = "";
    public string S2 { get; init; } = "";
    public string S3 { get; init; } = "";
    public string S4 { get; init; } = "";
    public string S5 { get; init; } = "";
    public string S6 { get; init; } = "";
    /// <summary>不参与排名时的原因说明（「含特殊科目，不参与排名」）。</summary>
    public string Note { get; init; } = "";
    public IBrush Background { get; init; } = Brushes.Transparent;

    // 列开关（从视图配置复制下来，让 ItemTemplate 里能直接 {Binding ShowX}）
    public bool ShowRank { get; init; } = true;
    public bool ShowName { get; init; } = true;
    public bool ShowStudentNo { get; init; }
    public bool ShowTotal { get; init; } = true;
    public bool ShowGradeRank { get; init; } = true;
    public bool ShowRankDelta { get; init; } = true;
    public bool ShowTotalDelta { get; init; }
    public bool ShowTrend { get; init; }
    public bool ShowS1 { get; init; }
    public bool ShowS2 { get; init; }
    public bool ShowS3 { get; init; }
    public bool ShowS4 { get; init; }
    public bool ShowS5 { get; init; }
    public bool ShowS6 { get; init; }
}

/// <summary>个人详情里的一行科目。</summary>
public sealed class SubjectRow
{
    public string Subject { get; init; } = "";
    public string ScoreText { get; init; } = "";
    public string FullText { get; init; } = "";
    public string RateText { get; init; } = "";
    public string RankText { get; init; } = "";
    public string ClassAvgText { get; init; } = "";
    public string StatusText { get; init; } = "";
    public IBrush StatusBrush { get; init; } = ChartPalette.MutedBrush;
}

/// <summary>与上一次考试的逐项对比行。</summary>
public sealed class CompareRow
{
    public string Label { get; init; } = "";
    public string LastText { get; init; } = "—";
    public string NowText { get; init; } = "—";
    public string DeltaText { get; init; } = "—";
    public IBrush DeltaBrush { get; init; } = ChartPalette.MutedBrush;
}

/// <summary>单人 PK 的一行。</summary>
public sealed class PkRow
{
    public string Label { get; init; } = "";
    public string ValueA { get; init; } = "";
    public string ValueB { get; init; } = "";
    public IBrush BrushA { get; init; } = Brushes.Transparent;
    public IBrush BrushB { get; init; } = Brushes.Transparent;
}

/// <summary>小组 PK 的一行。</summary>
public sealed class GroupRow
{
    public string RankText { get; init; } = "";
    public string Name { get; init; } = "";
    public IBrush ColorBrush { get; init; } = Brushes.Transparent;
    public string MemberCountText { get; init; } = "";
    public string AvgTotalText { get; init; } = "";
    public string AvgRankText { get; init; } = "";
    public string ProgressText { get; init; } = "";
    public string StabilityText { get; init; } = "";
    public string CompositeText { get; init; } = "";
    public string DetailText { get; init; } = "";
    public IBrush Background { get; init; } = Brushes.Transparent;
}

/// <summary>波动统计的一行。</summary>
public sealed class StatRow
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public IBrush ValueBrush { get; init; } = ChartPalette.TextBrush;
}

/// <summary>小组的一个**当前有效成员**（对应一条成员快照记录）。</summary>
public sealed class GroupMemberRow
{
    /// <summary>快照记录自身的 id —— 「移出小组」删的就是这一条。</summary>
    public long Id { get; init; }
    public long StudentId { get; init; }
    public string Name { get; init; } = "";
    public string StudentNo { get; init; } = "";
    /// <summary>这条记录从哪次考试起生效。老师据此判断"删了会退回什么状态"。</summary>
    public string EffectiveExamText { get; init; } = "";
    public string Display => string.IsNullOrEmpty(StudentNo) ? Name : $"{Name}（{StudentNo}）";

    /// <summary>行内「移出小组」。**命令挂在行模型上**，XAML 里就不需要
    /// <c>$parent[ItemsControl].DataContext</c> 那种绕路绑定（那种写法在编译绑定下又长又脆）。</summary>
    public IRelayCommand? RemoveCommand { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// 班级成绩分析的主视图模型。六个标签页共用一个数据集与一套分析缓存 ——
/// 分开查库会让「排行榜说第 3 名、个人详情说第 4 名」这种不一致有机可乘。
/// </summary>
public partial class GradeAnalysisViewModel : ObservableObject
{
    private readonly GradeDatabase _db;
    private GradeDataset? _ds;
    private List<StudentGroup> _groups = new();
    private List<GroupMember> _members = new();
    private GradeAnalysisSettings _settings = new();
    /// <summary>重建界面数据时的重入闸门（避免属性联动触发一串重复计算）。</summary>
    private bool _suspendRefresh;

    /// <summary>由视图注入的文件选择器（VM 不直接碰 Avalonia 的窗口/存储 API）。</summary>
    public Func<Task<string?>>? PickExcelFileAsync { get; set; }

    /// <summary>由视图注入的「确认」对话框。删除学生 / 移出小组成员 / 删除小组都要先问一句 ——
    /// VM 不直接调静态 UI API，否则它就没法在无 UI 的自检里跑。</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public GradeAnalysisViewModel(GradeDatabase db)
    {
        _db = db;
        _settings = LoadSettings();
        LoadFromDatabase();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  全局
    // ────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _isEmpty = true;

    public ObservableCollection<Exam> Exams { get; } = new();
    public ObservableCollection<Student> Students { get; } = new();

    [ObservableProperty] private Exam? _selectedExam;

    // 科目表头（六科，XAML 用具名属性做静态绑定）
    [ObservableProperty] private string _subHeader1 = "";
    [ObservableProperty] private string _subHeader2 = "";
    [ObservableProperty] private string _subHeader3 = "";
    [ObservableProperty] private string _subHeader4 = "";
    [ObservableProperty] private string _subHeader5 = "";
    [ObservableProperty] private string _subHeader6 = "";

    /// <summary>重新从数据库装载全部数据并刷新所有页签。</summary>
    public void LoadFromDatabase()
    {
        try
        {
            var students = _db.GetStudents();
            var exams = _db.GetExams();
            var scores = _db.GetAllScores();
            _groups = _db.GetGroups();
            _members = _db.GetGroupMembers();

            var gradeRanks = new Dictionary<long, Dictionary<long, int>>();
            foreach (var exam in exams) gradeRanks[exam.Id] = _db.GetGradeRanks(exam.Id);

            _ds = GradeDataset.Build(_settings, students, exams, scores, gradeRanks);

            _suspendRefresh = true;

            // 记住各处的当前选择（按 Id），重建列表后再恢复 ——
            // 否则老师每改一次东西（加个成员、改个颜色）界面就跳回默认项，等于没法连续操作。
            var keepExamId = SelectedExam?.Id;
            var keepGroupId = SelectedGroup?.Id;
            var keepDetailId = DetailStudent?.Id;
            var keepPkA = PkStudentA?.Id;
            var keepPkB = PkStudentB?.Id;
            var keepFluctId = FluctStudent?.Id;
            var keepEditId = SelectedStudentForEdit?.Id;

            Exams.Clear();
            foreach (var e in _ds.Exams) Exams.Add(e);
            Students.Clear();
            foreach (var s in _ds.Students.OrderBy(s => s.StudentNo, StringComparer.Ordinal)) Students.Add(s);
            Groups.Clear();
            foreach (var g in _groups) Groups.Add(g);

            var headers = _settings.SubjectNames;
            SubHeader1 = At(headers, 0); SubHeader2 = At(headers, 1); SubHeader3 = At(headers, 2);
            SubHeader4 = At(headers, 3); SubHeader5 = At(headers, 4); SubHeader6 = At(headers, 5);

            SelectedExam = Pick(Exams, keepExamId, e => e.Id) ?? _ds.Exams.LastOrDefault(); // 默认看最近一次
            SelectedGroup = Pick(Groups, keepGroupId, g => g.Id);
            DetailStudent = Pick(Students, keepDetailId, s => s.Id);
            PkStudentA = Pick(Students, keepPkA, s => s.Id);
            PkStudentB = Pick(Students, keepPkB, s => s.Id);
            FluctStudent = Pick(Students, keepFluctId, s => s.Id);
            SelectedStudentForEdit = Pick(Students, keepEditId, s => s.Id);

            SyncExamInputs();

            HasData = _ds.Students.Count > 0 && _ds.Exams.Count > 0;
            IsEmpty = !HasData;
            _suspendRefresh = false;

            StatusText = HasData
                ? $"已载入 {_ds.Students.Count} 名学生、{_ds.Exams.Count} 次考试。"
                : "还没有成绩数据。切到「数据导入」页导入 Excel，或手动录入。";

            RefreshAll();
        }
        catch (Exception ex)
        {
            ErrorText = "读取成绩库失败：" + ex.Message;
        }
    }

    private static string At(string[] arr, int i) => i < arr.Length ? arr[i] : "";

    /// <summary>重建列表后按 Id 找回原来选中的那一项；找不到返回 null（交给调用方兜底默认值）。</summary>
    private static T? Pick<T>(IEnumerable<T> items, long? id, Func<T, long> idOf) where T : class
        => id is null ? null : items.FirstOrDefault(x => idOf(x) == id.Value);

    /// <summary>把当前考试的名称 / 日期 / 年级总人数回填到编辑框。</summary>
    private void SyncExamInputs()
    {
        ExamNameInput = SelectedExam?.Name ?? "";
        ExamDateInput = SelectedExam is null ? null : new DateTimeOffset(SelectedExam.ExamDate);
        ExamGradeTotalInput = SelectedExam?.GradeTotalCount ?? 0;
    }

    partial void OnSelectedExamChanged(Exam? value)
    {
        if (_suspendRefresh) return;
        SyncExamInputs();
        RefreshAll();
    }

    /// <summary>菜单/热键进入时调用的轻量刷新：只重建缓存，不重查学生与考试列表。</summary>
    public void RefreshFromExternalChange() => LoadFromDatabase();

    /// <summary>当前考试的完整分析。缓存下沉在数据集内部，这里不再另存一份 ——
    /// 否则「排行榜看到的排名」和「个人详情看到的排名」可能来自两个不同快照。</summary>
    private ExamAnalysis? CurrentAnalysis
        => _ds is null || SelectedExam is null ? null : GradeAnalysisEngine.Analyze(_ds, SelectedExam.Id);

    private void RefreshAll()
    {
        if (_suspendRefresh) return;
        RefreshLeaderboard();
        RefreshDetail();
        RefreshPk();
        RefreshGroups();
        RefreshGroupMembers();
        RefreshFluctuation();
        RefreshImportLists();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  页签一：班级排行榜
    // ────────────────────────────────────────────────────────────────────────

    public string[] BoardKinds { get; } = { "总分榜", "单科榜", "进步榜", "退步榜" };

    [ObservableProperty] private int _selectedBoardKind;
    [ObservableProperty] private string _leaderboardSubject = "";
    [ObservableProperty] private string _leaderboardSearch = "";
    [ObservableProperty] private int _leaderboardTopN;
    [ObservableProperty] private string _leaderboardTitle = "总分榜";

    public ObservableCollection<string> SubjectChoices { get; } = new();
    public ObservableCollection<LeaderboardRow> LeaderboardRows { get; } = new();
    public ObservableCollection<LeaderboardRow> NotRankedRows { get; } = new();
    [ObservableProperty] private string _notRankedHeader = "";

    // ── 视图列开关（全班共用一套，落盘到成绩库的 app_kv）──
    [ObservableProperty] private bool _showRank = true;
    [ObservableProperty] private bool _showName = true;
    [ObservableProperty] private bool _showStudentNo;
    [ObservableProperty] private bool _showTotal = true;
    [ObservableProperty] private bool _showGradeRank = true;
    [ObservableProperty] private bool _showRankDelta = true;
    [ObservableProperty] private bool _showTotalDelta;
    [ObservableProperty] private bool _showTrend;
    [ObservableProperty] private bool _showSubject1 = true;
    [ObservableProperty] private bool _showSubject2 = true;
    [ObservableProperty] private bool _showSubject3 = true;
    [ObservableProperty] private bool _showSubject4 = true;
    [ObservableProperty] private bool _showSubject5 = true;
    [ObservableProperty] private bool _showSubject6 = true;

    partial void OnSelectedBoardKindChanged(int value) { BoardKindChanged(); }
    partial void OnLeaderboardSubjectChanged(string value) { if (!_suspendRefresh) RefreshLeaderboard(); }
    partial void OnLeaderboardSearchChanged(string value) { if (!_suspendRefresh) RefreshLeaderboard(); }
    partial void OnLeaderboardTopNChanged(int value) { if (!_suspendRefresh) RefreshLeaderboard(); }

    private void BoardKindChanged()
    {
        if (_suspendRefresh) return;
        LeaderboardTitle = At(BoardKinds, SelectedBoardKind);
        RefreshLeaderboard();
    }

    /// <summary>视图列开关变动：重画 + 落盘（大屏用的是同一套视图，所以必须存）。</summary>
    public void OnViewColumnChanged()
    {
        if (_suspendRefresh) return;
        RefreshLeaderboard();
        SaveSettings();
    }

    [RelayCommand]
    private void ResetView()
    {
        _settings.RankingVisibleColumns = RankingColumns.CreateDefault();
        _settings.RankingTopN = 0;
        _suspendRefresh = true;
        ApplyViewFlagsFromSettings();
        LeaderboardTopN = 0;
        _suspendRefresh = false;
        RefreshLeaderboard();
        SaveSettings();
        StatusText = "已恢复默认视图列。";
    }

    private void RefreshLeaderboard()
    {
        LeaderboardRows.Clear();
        NotRankedRows.Clear();
        NotRankedHeader = "";
        var analysis = CurrentAnalysis;
        if (analysis is null || _ds is null) return;

        _suspendRefresh = true;
        SubjectChoices.Clear();
        foreach (var s in _settings.SubjectNames) SubjectChoices.Add(s);
        if (string.IsNullOrEmpty(LeaderboardSubject) || !_settings.SubjectNames.Contains(LeaderboardSubject))
            LeaderboardSubject = _settings.SubjectNames.FirstOrDefault() ?? "";
        _suspendRefresh = false;

        var flags = ViewFlags();

        if (SelectedBoardKind == 0)
        {
            var ordered = analysis.Ranked.ToList();
            int i = 0;
            foreach (var r in Filter(ordered))
                LeaderboardRows.Add(BuildRow(r, ++i, flags, analysis));
            foreach (var r in analysis.NotRanked)
                NotRankedRows.Add(BuildRow(r, 0, flags, analysis));
            NotRankedHeader = NotRankedRows.Count > 0
                ? $"以下 {NotRankedRows.Count} 名学生含缺考/免考/作弊科目，总分仅供参考，不参与排名："
                : "";
        }
        else if (SelectedBoardKind == 1)
        {
            var subj = LeaderboardSubject;
            var pool = analysis.Ranked.Where(x => x.Find(subj)?.Counts == true).ToList();
            pool.Sort((a, b) => GradeAnalysisEngine.CompareBySubject(subj, a, b));
            int i = 0;
            foreach (var r in Filter(pool))
            {
                var row = BuildRow(r, ++i, flags, analysis);
                // 单科榜把「总分」列换成该科分数与得分率，避免老师来回切页看
                LeaderboardRows.Add(row);
            }
        }
        else if (SelectedBoardKind == 2)
        {
            int i = 0;
            foreach (var r in Filter(GradeAnalysisEngine.ProgressBoard(analysis)))
                LeaderboardRows.Add(BuildRow(r, ++i, flags, analysis));
        }
        else
        {
            int i = 0;
            foreach (var r in Filter(GradeAnalysisEngine.RegressionBoard(analysis)))
                LeaderboardRows.Add(BuildRow(r, ++i, flags, analysis));
        }

        if (LeaderboardRows.Count == 0)
            StatusText = $"「{LeaderboardTitle}」在当前筛选条件下没有数据。";
    }

    /// <summary>按姓名/学号搜索 + 前 N 名限制。</summary>
    private IEnumerable<StudentExamResult> Filter(IEnumerable<StudentExamResult> src)
    {
        var q = src;
        var key = LeaderboardSearch?.Trim();
        if (!string.IsNullOrEmpty(key))
            q = q.Where(x => x.Name.Contains(key, StringComparison.CurrentCultureIgnoreCase)
                             || x.StudentNo.Contains(key, StringComparison.OrdinalIgnoreCase));
        if (LeaderboardTopN > 0) q = q.Take(LeaderboardTopN);
        return q;
    }

    private static IBrush RowBrush(int rank, int index)
    {
        if (rank == 1) return new SolidColorBrush(Color.Parse(ChartPalette.Rank1), 0.55);
        if (rank == 2) return new SolidColorBrush(Color.Parse(ChartPalette.Rank2), 0.55);
        if (rank == 3) return new SolidColorBrush(Color.Parse(ChartPalette.Rank3), 0.55);
        return index % 2 == 1 ? new SolidColorBrush(Color.Parse(ChartPalette.PanelAlt), 0.7) : Brushes.Transparent;
    }

    private struct ViewFlagSet
    {
        public bool Rank, Name, No, Total, GradeRank, RankDelta, TotalDelta, Trend;
        public bool S1, S2, S3, S4, S5, S6;
    }

    private ViewFlagSet ViewFlags() => new()
    {
        Rank = ShowRank,
        Name = ShowName,
        No = ShowStudentNo,
        Total = ShowTotal,
        GradeRank = ShowGradeRank,
        RankDelta = ShowRankDelta,
        TotalDelta = ShowTotalDelta,
        Trend = ShowTrend,
        S1 = ShowSubject1,
        S2 = ShowSubject2,
        S3 = ShowSubject3,
        S4 = ShowSubject4,
        S5 = ShowSubject5,
        S6 = ShowSubject6,
    };

    private LeaderboardRow BuildRow(StudentExamResult r, int displayRank, ViewFlagSet f, ExamAnalysis analysis)
    {
        string Score(SubjectCell? c) => c is null ? "—"
            : c.Counts ? c.Score.ToString("0.##", CultureInfo.InvariantCulture) : c.Status.ToDisplay();

        var trendKind = _ds is null ? FluctuationKind.Insufficient : GradeAnalysisEngine.AnalyzeFluctuation(_ds, r.StudentId).Kind;

        return new LeaderboardRow
        {
            RankText = displayRank > 0 ? displayRank.ToString(CultureInfo.InvariantCulture) : "—",
            Name = r.Name,
            StudentNo = r.StudentNo,
            TotalText = r.TotalScore.ToString("0.##", CultureInfo.InvariantCulture),
            GradeRankText = r.GradeRank > 0
                ? (r.GradeTotalCount > 0 ? $"第 {r.GradeRank} / {r.GradeTotalCount}" : $"第 {r.GradeRank}")
                : "不参与",
            RankDeltaText = FluctuationKindText.DeltaText(r.ClassRankDelta),
            RankDeltaBrush = ChartPalette.Brush(FluctuationKindText.DeltaColor(r.ClassRankDelta)),
            TotalDeltaText = r.TotalScoreDelta.HasValue
                ? (r.TotalScoreDelta.Value >= 0 ? "+" : "") + r.TotalScoreDelta.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "—",
            TrendText = trendKind.ToDisplay(),
            TrendBrush = ChartPalette.Brush(trendKind.ToColor()),
            S1 = Score(r.Cells.Count > 0 ? r.Find(_settings.SubjectNames.ElementAtOrDefault(0) ?? "") : null),
            S2 = Score(r.Find(_settings.SubjectNames.ElementAtOrDefault(1) ?? "")),
            S3 = Score(r.Find(_settings.SubjectNames.ElementAtOrDefault(2) ?? "")),
            S4 = Score(r.Find(_settings.SubjectNames.ElementAtOrDefault(3) ?? "")),
            S5 = Score(r.Find(_settings.SubjectNames.ElementAtOrDefault(4) ?? "")),
            S6 = Score(r.Find(_settings.SubjectNames.ElementAtOrDefault(5) ?? "")),
            Note = r.HasSpecial ? "含特殊科目，不参与排名" : "",
            Background = RowBrush(displayRank, LeaderboardRows.Count),
            ShowRank = f.Rank, ShowName = f.Name, ShowStudentNo = f.No, ShowTotal = f.Total,
            ShowGradeRank = f.GradeRank, ShowRankDelta = f.RankDelta, ShowTotalDelta = f.TotalDelta,
            ShowTrend = f.Trend, ShowS1 = f.S1, ShowS2 = f.S2, ShowS3 = f.S3, ShowS4 = f.S4, ShowS5 = f.S5, ShowS6 = f.S6,
        };
    }

    // ────────────────────────────────────────────────────────────────────────
    //  页签二：个人详情
    // ────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private Student? _detailStudent;
    [ObservableProperty] private string _detailTotalText = "—";
    [ObservableProperty] private string _detailClassRankText = "—";
    [ObservableProperty] private string _detailGradeRankText = "—";
    [ObservableProperty] private string _detailDeltaText = "—";
    [ObservableProperty] private IBrush _detailDeltaBrush = ChartPalette.MutedBrush;
    [ObservableProperty] private IReadOnlyList<RadarSeries> _detailRadarSeries = Array.Empty<RadarSeries>();
    [ObservableProperty] private IReadOnlyList<string> _detailAxisLabels = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<TrendSeries> _detailTrendSeries = Array.Empty<TrendSeries>();
    [ObservableProperty] private IReadOnlyList<string> _detailTrendLabels = Array.Empty<string>();

    public ObservableCollection<SubjectRow> DetailSubjectRows { get; } = new();
    public ObservableCollection<CompareRow> DetailCompareRows { get; } = new();

    partial void OnDetailStudentChanged(Student? value) { if (!_suspendRefresh) RefreshDetail(); }

    private void RefreshDetail()
    {
        DetailSubjectRows.Clear();
        DetailCompareRows.Clear();

        var analysis = CurrentAnalysis;
        if (analysis is null || _ds is null) return;

        DetailStudent ??= _ds.Students.FirstOrDefault();
        if (DetailStudent is null) return;

        var row = analysis.FindStudent(DetailStudent.Id);
        if (row is null)
        {
            DetailTotalText = "—";
            DetailClassRankText = "—";
            DetailGradeRankText = "—";
            DetailDeltaText = "本次考试没有该生的成绩";
            DetailRadarSeries = Array.Empty<RadarSeries>();
            DetailTrendSeries = Array.Empty<TrendSeries>();
            return;
        }

        DetailTotalText = row.TotalScore.ToString("0.##", CultureInfo.InvariantCulture)
                          + (row.HasSpecial ? "（含特殊科目，不参与排名）" : "");
        DetailClassRankText = row.ClassRank > 0 ? $"第 {row.ClassRank} 名" : "不参与排名";
        DetailGradeRankText = row.GradeRank > 0
            ? (row.GradeTotalCount > 0 ? $"第 {row.GradeRank} 名 / 共 {row.GradeTotalCount} 人" : $"第 {row.GradeRank} 名")
            : "不参与";
        DetailDeltaText = $"班级排名 {FluctuationKindText.DeltaText(row.ClassRankDelta)}　"
                          + $"总分 {(row.TotalScoreDelta >= 0 ? "+" : "")}{row.TotalScoreDelta?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—"}";
        DetailDeltaBrush = ChartPalette.Brush(FluctuationKindText.DeltaColor(row.ClassRankDelta));

        // 雷达图：本人得分率 vs 班级平均得分率
        var mine = new double[_settings.Subjects.Count];
        var avg = new double[_settings.Subjects.Count];
        for (int i = 0; i < _settings.Subjects.Count; i++)
        {
            var name = _settings.Subjects[i].Name;
            var cell = row.Find(name);
            mine[i] = cell is { Counts: true } ? cell.Rate : 0;
            avg[i] = analysis.ClassAverageRate.TryGetValue(name, out var a) ? a : 0;
        }
        DetailAxisLabels = _settings.SubjectNames;
        DetailRadarSeries = new[]
        {
            new RadarSeries { Name = row.Name, Values = mine, Color = ChartPalette.Series[0] },
            new RadarSeries { Name = "班级平均", Values = avg, Color = ChartPalette.Series[7] },
        };

        for (int i = 0; i < _settings.Subjects.Count; i++)
        {
            var name = _settings.Subjects[i].Name;
            var cell = row.Find(name);
            DetailSubjectRows.Add(new SubjectRow
            {
                Subject = name,
                ScoreText = cell is null ? "—" : cell.Score.ToString("0.##", CultureInfo.InvariantCulture),
                FullText = cell?.FullScore.ToString("0.##", CultureInfo.InvariantCulture) ?? "—",
                RateText = cell is { Counts: true } ? (cell.Rate * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—",
                RankText = analysis.SubjectRank(name, row.StudentId) is { } sr ? $"第 {sr} 名" : "不参与",
                ClassAvgText = (avg[i] * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%",
                StatusText = cell is null ? "" : (cell.Counts ? "" : cell.Status.ToDisplay()),
                StatusBrush = cell is null || cell.Counts ? ChartPalette.MutedBrush : ChartPalette.Brush("#F85149"),
            });
        }

        // 与上一次对比
        var prevRow = row.PreviousExamId is { } prevId ? GetCachedAnalysis(prevId)?.FindStudent(row.StudentId) : null;
        if (prevRow is not null && row.PreviousExamId is { } prevExamId)
        {
            var prevExamName = _ds.ExamById.TryGetValue(prevExamId, out var pe) ? pe.DisplayName : "上一次考试";
            DetailCompareRows.Add(new CompareRow
            {
                Label = "总分",
                LastText = prevRow.TotalScore.ToString("0.##", CultureInfo.InvariantCulture),
                NowText = row.TotalScore.ToString("0.##", CultureInfo.InvariantCulture),
                DeltaText = ((row.TotalScoreDelta ?? 0) >= 0 ? "+" : "") + (row.TotalScoreDelta ?? 0).ToString("0.##", CultureInfo.InvariantCulture),
                DeltaBrush = ChartPalette.Brush((row.TotalScoreDelta ?? 0) >= 0 ? "#3FB950" : "#F85149"),
            });
            DetailCompareRows.Add(new CompareRow
            {
                Label = $"班级排名（上次：{prevExamName}）",
                LastText = prevRow.ClassRank > 0 ? prevRow.ClassRank.ToString(CultureInfo.InvariantCulture) : "不参与",
                NowText = row.ClassRank > 0 ? row.ClassRank.ToString(CultureInfo.InvariantCulture) : "不参与",
                DeltaText = FluctuationKindText.DeltaText(row.ClassRankDelta),
                DeltaBrush = ChartPalette.Brush(FluctuationKindText.DeltaColor(row.ClassRankDelta)),
            });

            foreach (var subj in _settings.SubjectNames)
            {
                var a = prevRow.Find(subj);
                var b = row.Find(subj);
                double da = a?.Score ?? 0, db = b?.Score ?? 0;
                double delta = db - da;
                DetailCompareRows.Add(new CompareRow
                {
                    Label = subj,
                    LastText = a is null ? "—" : a.Score.ToString("0.##", CultureInfo.InvariantCulture),
                    NowText = b is null ? "—" : b.Score.ToString("0.##", CultureInfo.InvariantCulture),
                    DeltaText = (delta >= 0 ? "+" : "") + delta.ToString("0.##", CultureInfo.InvariantCulture),
                    DeltaBrush = ChartPalette.Brush(delta > 0 ? "#3FB950" : delta < 0 ? "#F85149" : ChartPalette.TextMuted),
                });
            }
        }

        // 本学期总分趋势
        RefreshTrend(out var labels, out var totals, row.StudentId);
        DetailTrendLabels = labels;
        DetailTrendSeries = new[]
        {
            new TrendSeries { Name = row.Name, Values = totals, Color = ChartPalette.Series[0], ShowPointLabels = true },
        };
    }

    /// <summary>该生在本学期各次考试的总分走势（缺考的那次记为 null，折线断开）。</summary>
    private void RefreshTrend(out string[] labels, out double?[] totals, long studentId)
    {
        var list = new List<string>();
        var vals = new List<double?>();
        if (_ds is not null)
        {
            foreach (var exam in _ds.Exams)
            {
                var a = GetCachedAnalysis(exam.Id);
                var r = a?.FindStudent(studentId);
                if (r is null || r.Cells.Count == 0) continue;
                list.Add(exam.Name);
                vals.Add(r.HasSpecial ? null : r.TotalScore);
            }
        }
        labels = list.ToArray();
        totals = vals.ToArray();
    }

    private ExamAnalysis? GetCachedAnalysis(long examId)
        => _ds is null ? null : GradeAnalysisEngine.Analyze(_ds, examId);

    // ────────────────────────────────────────────────────────────────────────
    //  页签三：单人 PK
    // ────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private Student? _pkStudentA;
    [ObservableProperty] private Student? _pkStudentB;
    [ObservableProperty] private string _pkSummaryText = "";
    [ObservableProperty] private IReadOnlyList<RadarSeries> _pkRadarSeries = Array.Empty<RadarSeries>();
    [ObservableProperty] private IReadOnlyList<string> _pkAxisLabels = Array.Empty<string>();
    public ObservableCollection<PkRow> PkRows { get; } = new();

    partial void OnPkStudentAChanged(Student? value) { if (!_suspendRefresh) RefreshPk(); }
    partial void OnPkStudentBChanged(Student? value) { if (!_suspendRefresh) RefreshPk(); }

    [RelayCommand]
    private void SwapPk() { (PkStudentA, PkStudentB) = (PkStudentB, PkStudentA); }

    private void RefreshPk()
    {
        PkRows.Clear();
        var analysis = CurrentAnalysis;
        if (analysis is null || _ds is null) return;

        PkStudentA ??= _ds.Students.FirstOrDefault();
        PkStudentB ??= _ds.Students.Skip(1).FirstOrDefault();
        if (PkStudentA is null || PkStudentB is null) return;
        if (PkStudentA.Id == PkStudentB.Id)
        {
            PkSummaryText = "两位选手是同一人，请换一个。";
            PkRows.Clear();
            PkRadarSeries = Array.Empty<RadarSeries>();
            return;
        }

        var pk = GradeAnalysisEngine.BuildPk(_ds, analysis, PkStudentA.Id, PkStudentB.Id);
        var colorA = ChartPalette.Series[0];
        var colorB = ChartPalette.Series[2];
        foreach (var item in pk.Items)
        {
            PkRows.Add(new PkRow
            {
                Label = item.Label,
                ValueA = item.ValueA,
                ValueB = item.ValueB,
                BrushA = item.Winner == 1 ? ChartPalette.Fill("#3FB950", 0.28) : Brushes.Transparent,
                BrushB = item.Winner == 2 ? ChartPalette.Fill("#3FB950", 0.28) : Brushes.Transparent,
            });
        }
        PkSummaryText = $"{pk.NameA} {pk.WinsA} 胜 · {pk.Ties} 平 · {pk.NameB} {pk.WinsB} 胜"
                        + (pk.WinsA > pk.WinsB ? $"　→　{pk.NameA} 综合胜出" : pk.WinsB > pk.WinsA ? $"　→　{pk.NameB} 综合胜出" : "　→　打平");

        var ra = analysis.FindStudent(PkStudentA.Id);
        var rb = analysis.FindStudent(PkStudentB.Id);
        PkAxisLabels = _settings.SubjectNames;
        PkRadarSeries = new[]
        {
            new RadarSeries { Name = $"{PkStudentA.Name}", Values = RatesOf(ra), Color = colorA },
            new RadarSeries { Name = $"{PkStudentB.Name}", Values = RatesOf(rb), Color = colorB },
        };
    }

    private double[] RatesOf(StudentExamResult? r)
    {
        var arr = new double[_settings.Subjects.Count];
        if (r is null) return arr;
        for (int i = 0; i < _settings.Subjects.Count; i++)
        {
            var c = r.Find(_settings.Subjects[i].Name);
            arr[i] = c is { Counts: true } ? c.Rate : 0;
        }
        return arr;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  页签四：小组 PK
    // ────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private double _weightAvgScore = 50;
    [ObservableProperty] private double _weightProgress = 25;
    [ObservableProperty] private double _weightStability = 25;
    [ObservableProperty] private string _weightSummary = "";
    [ObservableProperty] private IReadOnlyList<RadarSeries> _groupRadarSeries = Array.Empty<RadarSeries>();
    [ObservableProperty] private IReadOnlyList<string> _groupAxisLabels = Array.Empty<string>();
    public ObservableCollection<GroupRow> GroupRows { get; } = new();

    partial void OnWeightAvgScoreChanged(double value) { WeightChanged(); }
    partial void OnWeightProgressChanged(double value) { WeightChanged(); }
    partial void OnWeightStabilityChanged(double value) { WeightChanged(); }

    private void WeightChanged()
    {
        if (_suspendRefresh) return;
        _settings.GroupWeightAverageScore = WeightAvgScore;
        _settings.GroupWeightProgressRate = WeightProgress;
        _settings.GroupWeightStability = WeightStability;
        SaveSettings();
        RefreshGroups();
    }

    private void RefreshGroups()
    {
        GroupRows.Clear();
        var analysis = CurrentAnalysis;
        if (analysis is null || _ds is null) return;

        var (wa, wp, ws) = _settings.NormalizedGroupWeights();
        WeightSummary = $"归一化后权重：平均总分 {wa * 100:0.#}%　进步率 {wp * 100:0.#}%　稳定度 {ws * 100:0.#}%"
                        + (_groups.Count == 0 ? "　（还没有小组，请先在下方新建）" : "");

        var results = GradeAnalysisEngine.BuildGroupResults(_ds, analysis, _groups, _members);
        int rank = 0;
        foreach (var g in results)
        {
            rank++;
            var members = g.MemberIds.Select(id => _ds.StudentById.TryGetValue(id, out var s) ? s.Name : "?").ToList();
            GroupRows.Add(new GroupRow
            {
                RankText = rank.ToString(CultureInfo.InvariantCulture),
                Name = g.Name,
                ColorBrush = ChartPalette.Brush(g.Color),
                MemberCountText = $"{g.MemberIds.Count} 人",
                AvgTotalText = g.AverageTotal.ToString("0.##", CultureInfo.InvariantCulture),
                AvgRankText = g.AverageClassRank > 0 ? g.AverageClassRank.ToString("0.0", CultureInfo.InvariantCulture) : "—",
                ProgressText = (g.ProgressRate * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%",
                StabilityText = g.Stability.ToString("0.0", CultureInfo.InvariantCulture),
                CompositeText = g.CompositeScore.ToString("0.000", CultureInfo.InvariantCulture),
                DetailText = $"总分项 {g.NormAverageTotal:0.00} × {wa:0.##}　进步项 {g.NormProgressRate:0.00} × {wp:0.##}　稳定项 {g.NormStability:0.00} × {ws:0.##}"
                             + (members.Count > 0 ? $"　|　{string.Join("、", members)}" : ""),
                Background = RowBrush(0, rank - 1),
            });
        }

        GroupAxisLabels = _settings.SubjectNames;
        GroupRadarSeries = results.Select((g, i) => new RadarSeries
        {
            Name = g.Name,
            Values = g.SubjectAverageRates,
            Color = g.Color,
        }).ToArray();
    }

    [ObservableProperty] private string _newGroupName = "";
    [ObservableProperty] private string _newGroupMembers = "";

    // ── 小组的界面化成员管理（2026-10-08）──────────────────────────────
    // 原来只能「建组时用逗号输一串成员」，换人得重建小组 —— 而成绩分析里换人是常态。
    // 现在：选组 → 看到该组在**当前考试**时的成员 → 加人 / 移出 / 改颜色 / 删组。
    public ObservableCollection<StudentGroup> Groups { get; } = new();
    public ObservableCollection<GroupMemberRow> GroupMemberRows { get; } = new();

    [ObservableProperty] private StudentGroup? _selectedGroup;
    [ObservableProperty] private string _selectedGroupColorHex = "";
    [ObservableProperty] private Student? _memberCandidate;
    [ObservableProperty] private string _groupHintText = "";
    [ObservableProperty] private string _groupMemberCountText = "";

    partial void OnSelectedGroupChanged(StudentGroup? value)
    {
        SelectedGroupColorHex = value?.Color ?? "";
        if (!_suspendRefresh) RefreshGroupMembers();
    }

    /// <summary>列出选中小组在**当前考试**时的成员（对应各自的快照记录）。</summary>
    private void RefreshGroupMembers()
    {
        GroupMemberRows.Clear();
        GroupHintText = "";
        GroupMemberCountText = "";
        if (_ds is null || SelectedGroup is null) return;
        if (SelectedExam is null) { GroupHintText = "请先在顶部选一次考试。"; return; }

        var resolved = GradeAnalysisEngine.ResolveMemberRecords(_ds, _members, SelectedExam.Id);
        if (!resolved.TryGetValue(SelectedGroup.Id, out var recs) || recs.Count == 0)
        {
            GroupHintText = $"「{SelectedGroup.Name}」在「{SelectedExam.DisplayName}」时还没有成员。"
                          + "用右边的下拉框把学生加进来。";
            return;
        }

        foreach (var rec in recs)
        {
            if (!_ds.StudentById.TryGetValue(rec.StudentId, out var stu)) continue;
            var effText = _ds.ExamById.TryGetValue(rec.EffectiveExamId, out var effExam)
                ? effExam.DisplayName
                : "（已删除的考试）";
            var row = new GroupMemberRow
            {
                Id = rec.Id,
                StudentId = stu.Id,
                Name = stu.Name,
                StudentNo = stu.StudentNo,
                EffectiveExamText = effText,
            };
            row.RemoveCommand = new AsyncRelayCommand(() => RemoveGroupMemberAsync(row));
            GroupMemberRows.Add(row);
        }

        GroupMemberCountText = $"{GroupMemberRows.Count} 人";
        GroupHintText = $"「{SelectedGroup.Name}」在「{SelectedExam.DisplayName}」时的成员。"
                      + "加人 = 自当前考试起生效；移出 = 删掉那条记录，会退回上一条记录的效果（历史考试不受影响）。";
    }

    [RelayCommand]
    private async Task AddGroupMemberAsync()
    {
        ErrorText = "";
        if (SelectedGroup is null) { ErrorText = "请先选择一个小组。"; return; }
        if (SelectedExam is null) { ErrorText = "请先选择一次考试。"; return; }
        if (MemberCandidate is null) { ErrorText = "请先选择要加入的学生。"; return; }
        if (GroupMemberRows.Any(r => r.StudentId == MemberCandidate.Id))
        {
            ErrorText = $"{MemberCandidate.Name} 已经在这个小组里了。";
            return;
        }

        var name = MemberCandidate.Name;
        _db.AddGroupMember(SelectedGroup.Id, MemberCandidate.Id, SelectedExam.Id);
        StatusText = $"已把 {name} 加入「{SelectedGroup.Name}」，自「{SelectedExam.DisplayName}」起生效。";
        MemberCandidate = null;
        ReloadPreserving();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RemoveGroupMemberAsync(GroupMemberRow? row)
    {
        ErrorText = "";
        if (row is null || SelectedGroup is null) return;
        var ok = ConfirmAsync is null || await ConfirmAsync("移出小组",
            $"把 {row.Name} 从「{SelectedGroup.Name}」移出？\n\n"
            + $"这会删掉「自 {row.EffectiveExamText} 起加入」这条记录 —— "
            + "该生效考试之后的考试就不再把他算进这个组；\n"
            + "如果他之前还加入过，会**退回上一条记录**的效果。\n"
            + "已发生的历史考试，小组构成与成绩都不受影响。");
        if (!ok) return;

        _db.DeleteGroupMember(row.Id);
        StatusText = $"已把 {row.Name} 移出「{SelectedGroup.Name}」。";
        ReloadPreserving();
    }

    [RelayCommand]
    private void SaveGroupColor()
    {
        ErrorText = "";
        if (SelectedGroup is null) { ErrorText = "请先选择小组。"; return; }
        if (!ColorSwatch.TryParse(SelectedGroupColorHex, out _))
        {
            ErrorText = $"颜色无法识别：{SelectedGroupColorHex}";
            return;
        }
        SelectedGroup.Color = SelectedGroupColorHex;
        _db.UpsertGroup(SelectedGroup);
        // 同步内存里的那份，让排行榜 / 雷达图立刻换色（不然要等下一次重建）
        var idx = _groups.FindIndex(g => g.Id == SelectedGroup.Id);
        if (idx >= 0) _groups[idx].Color = SelectedGroupColorHex;
        StatusText = $"已更新「{SelectedGroup.Name}」的颜色。";
        RefreshGroups();
    }

    [RelayCommand]
    private async Task DeleteGroupAsync()
    {
        ErrorText = "";
        if (SelectedGroup is null) return;
        var name = SelectedGroup.Name;
        var ok = ConfirmAsync is null || await ConfirmAsync("删除小组",
            $"删除小组「{name}」？\n\n它的成员记录会一并删除。已发生的考试成绩不受影响。");
        if (!ok) return;

        _db.DeleteGroup(SelectedGroup.Id);
        SelectedGroup = null;
        StatusText = $"已删除小组「{name}」。";
        ReloadPreserving();
    }

    /// <summary>重建界面数据并**保住当前选择**（不这样做，改一次成员界面就跳回默认项）。</summary>
    private void ReloadPreserving() => LoadFromDatabase();

    [RelayCommand]
    private void AddGroup()
    {
        var name = NewGroupName.Trim();
        if (name.Length == 0) { ErrorText = "请先填小组名称。"; return; }
        if (_ds is null || SelectedExam is null) { ErrorText = "请先导入成绩并选择考试。"; return; }

        var color = ChartPalette.SeriesAt(_groups.Count);
        var id = _db.UpsertGroup(new StudentGroup { Name = name, Color = color });

        // 成员按姓名或学号匹配（逗号/顿号/空格分隔）
        var tokens = NewGroupMembers.Split(new[] { ',', '，', '、', ' ', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
        int added = 0;
        foreach (var t in tokens)
        {
            var s = _ds.Students.FirstOrDefault(x =>
                string.Equals(x.StudentNo, t, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Name, t, StringComparison.CurrentCulture));
            if (s is null) continue;
            // 生效考试 = 当前选中的考试：从这次考试起算该成员
            _db.AddGroupMember(id, s.Id, SelectedExam.Id);
            added++;
        }

        ErrorText = "";
        StatusText = added > 0
            ? $"已新建小组「{name}」（{added} 名成员，自「{SelectedExam.DisplayName}」起生效）。"
            : $"已新建小组「{name}」，但没匹配到任何成员。请用姓名或学号，逗号分隔。";
        NewGroupName = "";
        NewGroupMembers = "";
        LoadFromDatabase();
        // 新建完直接选中它 —— 接着就能在下面的成员区继续加人，不用再去下拉框里找一遍
        SelectedGroup = Groups.FirstOrDefault(g => g.Id == id);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  页签五：波动分析
    // ────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private Student? _fluctStudent;
    [ObservableProperty] private string _fluctKindText = "—";
    [ObservableProperty] private IBrush _fluctKindBrush = ChartPalette.MutedBrush;
    [ObservableProperty] private IReadOnlyList<TrendSeries> _fluctTotalSeries = Array.Empty<TrendSeries>();
    [ObservableProperty] private IReadOnlyList<TrendSeries> _fluctRankSeries = Array.Empty<TrendSeries>();
    [ObservableProperty] private IReadOnlyList<TrendSeries> _fluctSubjectSeries = Array.Empty<TrendSeries>();
    [ObservableProperty] private IReadOnlyList<string> _fluctLabels = Array.Empty<string>();
    public ObservableCollection<StatRow> FluctStatRows { get; } = new();

    [ObservableProperty] private double _thresholdStdDev = 5;
    [ObservableProperty] private double _thresholdHalfDelta = 2;
    [ObservableProperty] private double _thresholdSubjectBias = 22;

    partial void OnFluctStudentChanged(Student? value) { if (!_suspendRefresh) RefreshFluctuation(); }
    partial void OnThresholdStdDevChanged(double value) { ThresholdChanged(); }
    partial void OnThresholdHalfDeltaChanged(double value) { ThresholdChanged(); }
    partial void OnThresholdSubjectBiasChanged(double value) { ThresholdChanged(); }

    private void ThresholdChanged()
    {
        if (_suspendRefresh) return;
        _settings.RankStdDevThreshold = ThresholdStdDev;
        _settings.HalfDeltaThreshold = ThresholdHalfDelta;
        _settings.SubjectBiasRangeThreshold = ThresholdSubjectBias;
        SaveSettings();
        RefreshFluctuation();
    }

    private void RefreshFluctuation()
    {
        FluctStatRows.Clear();
        if (_ds is null || _ds.Students.Count == 0) return;

        FluctStudent ??= _ds.Students.FirstOrDefault();
        if (FluctStudent is null) return;

        var rep = GradeAnalysisEngine.AnalyzeFluctuation(_ds, FluctStudent.Id);
        FluctKindText = rep.Kind.ToDisplay();
        FluctKindBrush = ChartPalette.Brush(rep.Kind.ToColor());

        // 趋势数据：总分 / 班级排名（都要按考试顺序对齐，缺考那次留 null 断开）
        var labels = new List<string>();
        var totals = new List<double?>();
        var ranks = new List<double?>();
        foreach (var exam in _ds.Exams)
        {
            var r = GetCachedAnalysis(exam.Id)?.FindStudent(FluctStudent.Id);
            if (r is null || r.Cells.Count == 0) continue;
            labels.Add(exam.Name);
            totals.Add(r.HasSpecial ? null : r.TotalScore);
            ranks.Add(r.ClassRank > 0 ? r.ClassRank : null);
        }
        FluctLabels = labels.ToArray();
        FluctTotalSeries = new[] { new TrendSeries { Name = "总分", Values = totals, Color = ChartPalette.Series[0], ShowPointLabels = true } };
        FluctRankSeries = new[] { new TrendSeries { Name = "班级排名", Values = ranks, Color = ChartPalette.Series[2], ShowPointLabels = true } };

        var subjSeries = new List<TrendSeries>();
        for (int i = 0; i < _settings.Subjects.Count; i++)
        {
            var name = _settings.Subjects[i].Name;
            var vals = new List<double?>();
            foreach (var exam in _ds.Exams)
            {
                var r = GetCachedAnalysis(exam.Id)?.FindStudent(FluctStudent.Id);
                if (r is null || r.Cells.Count == 0) continue;
                var c = r.Find(name);
                vals.Add(c is { Counts: true } ? Math.Round(c.Rate * 100, 1) : null);
            }
            subjSeries.Add(new TrendSeries { Name = name, Values = vals, Color = ChartPalette.SeriesAt(i) });
        }
        FluctSubjectSeries = subjSeries;

        void Stat(string label, string value, string? color = null)
            => FluctStatRows.Add(new StatRow { Label = label, Value = value, ValueBrush = color is null ? ChartPalette.TextBrush : ChartPalette.Brush(color) });

        Stat("波动类型", rep.Kind.ToDisplay(), rep.Kind.ToColor());
        Stat("参与统计的考试次数", rep.ExamCount.ToString(CultureInfo.InvariantCulture));
        Stat("其中可参与排名的次数", rep.RankedExamCount.ToString(CultureInfo.InvariantCulture)
            + (rep.RankedExamCount < 2 ? "（少于 2 次无法判断波动，结论仅供参考）" : ""),
            rep.RankedExamCount < 2 ? "#D29922" : null);
        Stat("平均总分", rep.AverageTotal.ToString("0.##", CultureInfo.InvariantCulture));
        Stat("最高 / 最低总分", $"{rep.MaxTotal:0.##} / {rep.MinTotal:0.##}");
        Stat("最好 / 最差班级排名", rep.BestRank > 0 ? $"第 {rep.BestRank} / 第 {rep.WorstRank}" : "不参与排名");
        Stat("排名标准差", rep.RankStdDev.ToString("0.00", CultureInfo.InvariantCulture)
                          + (rep.RankStdDev > _settings.RankStdDevThreshold ? $"，超过阈值 {_settings.RankStdDevThreshold:0.#}" : $"，未超过阈值 {_settings.RankStdDevThreshold:0.#}"),
            rep.RankStdDev > _settings.RankStdDevThreshold ? "#D29922" : "#3FB950");
        Stat("前后半段平均排名", rep.ExamCount >= 2
            ? $"前半 {rep.FrontHalfAvgRank:0.0} → 后半 {rep.BackHalfAvgRank:0.0}（差 {rep.HalfDelta:+0.0;-0.0;0}）"
            : "数据不足");
        Stat("科目极差（平均得分率）", rep.SubjectRateRange.ToString("0.0", CultureInfo.InvariantCulture) + " 个百分点"
            + (rep.SubjectRateRange > _settings.SubjectBiasRangeThreshold ? $"，超过阈值 {_settings.SubjectBiasRangeThreshold:0.#}" : ""),
            rep.SubjectRateRange > _settings.SubjectBiasRangeThreshold ? "#BC8CFF" : ChartPalette.Text);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  页签六：数据导入 / 手动录入
    // ────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _importFilePath = "";
    [ObservableProperty] private string _manualStudentNo = "";
    [ObservableProperty] private string _manualName = "";
    [ObservableProperty] private string _manualClassName = "";
    [ObservableProperty] private string _manualExamName = "";
    [ObservableProperty] private DateTimeOffset? _manualExamDate = DateTimeOffset.Now;
    [ObservableProperty] private string _manualSpecialStatus = "";
    [ObservableProperty] private string _manualScore1 = "";
    [ObservableProperty] private string _manualScore2 = "";
    [ObservableProperty] private string _manualScore3 = "";
    [ObservableProperty] private string _manualScore4 = "";
    [ObservableProperty] private string _manualScore5 = "";
    [ObservableProperty] private string _manualScore6 = "";
    [ObservableProperty] private Student? _selectedStudentForEdit;
    [ObservableProperty] private string _editSurnameStrokes = "";
    [ObservableProperty] private string _editGivenStrokes = "";
    [ObservableProperty] private int _examGradeTotalInput;
    // 考试的「可改属性」（2026-10-08）：以前只能改年级总人数，考试名打错了只能把整场删掉重导
    [ObservableProperty] private string _examNameInput = "";
    [ObservableProperty] private DateTimeOffset? _examDateInput;

    public ObservableCollection<string> Diagnostics { get; } = new();
    public ObservableCollection<Student> ManualStudents { get; } = new();

    private void RefreshImportLists()
    {
        ManualStudents.Clear();
        if (_ds is null) return;
        foreach (var s in _ds.Students.OrderBy(s => s.StudentNo, StringComparer.Ordinal)) ManualStudents.Add(s);
        ExamGradeTotalInput = SelectedExam?.GradeTotalCount ?? 0;
    }

    [RelayCommand]
    private async Task PickFileAsync()
    {
        if (PickExcelFileAsync is null) return;
        var path = await PickExcelFileAsync();
        if (string.IsNullOrEmpty(path)) return;
        ImportFilePath = path;
        StatusText = "已选择文件，按「开始导入」解析。";
    }

    [RelayCommand]
    private void RunImport()
    {
        Diagnostics.Clear();
        ErrorText = "";
        if (ImportFilePath.Length == 0) { ErrorText = "请先选择 .xlsx 文件。"; return; }

        var outcome = GradeImporter.Parse(ImportFilePath, _settings);
        foreach (var d in outcome.Diagnostics) Diagnostics.Add(Prefix(d));

        if (!outcome.Success || outcome.Batch is null)
        {
            ErrorText = "导入失败，已自动切到「手动录入」。请按下面的提示修表，或直接手工录入。";
            // 明确告知视图切页：OnImportFailed 事件由窗口订阅
            ImportFailed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var result = _db.CommitImport(outcome.Batch);
        StatusText = $"导入完成：写入 {result.ScoreCount} 条成绩、{result.StudentCount} 名学生、{result.ExamCount} 次考试"
                     + (result.GradeRankCount > 0 ? $"，{result.GradeRankCount} 条年级排名。" : "。");
        LoadFromDatabase();
    }

    /// <summary>导入失败 → 请求界面切到「手动录入」页。</summary>
    public event EventHandler? ImportFailed;

    private static string Prefix(ImportDiagnostic d) => d.Severity switch
    {
        ImportSeverity.Error => "✖ " + d.ToLine(),
        ImportSeverity.Warning => "△ " + d.ToLine(),
        _ => "· " + d.ToLine(),
    };

    /// <summary>手动录入一条（学生 + 考试 + 六科 + 可选特殊状态）。</summary>
    [RelayCommand]
    private void AddManual()
    {
        ErrorText = "";
        Diagnostics.Clear();
        if (ManualStudentNo.Trim().Length == 0 || ManualName.Trim().Length == 0)
        { ErrorText = "学号和姓名都必须填。"; return; }
        if (ManualExamName.Trim().Length == 0 || ManualExamDate is null)
        { ErrorText = "考试名称和考试日期都必须填。"; return; }

        var subjects = _settings.Subjects;
        var specialMap = new Dictionary<string, ScoreStatus>(StringComparer.Ordinal);
        if (ManualSpecialStatus.Trim().Length > 0 &&
            !GradeImporter.TryParseSpecialStatus(ManualSpecialStatus, _settings.SubjectNames, out specialMap, out var err))
        { ErrorText = $"特殊状态无法识别：{err}。写法示例：数学缺考；英语作弊"; return; }

        var exam = new Exam { Name = ManualExamName.Trim(), ExamDate = ManualExamDate.Value.Date };
        var batch = new ImportBatch();
        var student = new Student { StudentNo = ManualStudentNo.Trim(), Name = ManualName.Trim(), ClassName = ManualClassName.Trim() };
        batch.Students.Add(student);
        batch.Exams.Add(exam);
        var examKey = ImportBatch.ExamKey(exam);

        var raw = new[] { ManualScore1, ManualScore2, ManualScore3, ManualScore4, ManualScore5, ManualScore6 };
        for (int i = 0; i < subjects.Count && i < 6; i++)
        {
            var text = raw[i].Trim();
            var status = specialMap.TryGetValue(subjects[i].Name, out var st) ? st : ScoreStatus.Normal;
            double score = 0;
            if (text.Length > 0)
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out score))
                {
                    if (ScoreStatusText.TryParse(text, out var inline) && inline != ScoreStatus.Normal) status = inline;
                    else { ErrorText = $"「{subjects[i].Name}」的成绩「{text}」不是数字。"; return; }
                }
            }
            batch.Scores.Add(new PendingScore
            {
                StudentNo = student.StudentNo,
                ExamKey = examKey,
                Subject = subjects[i].Name,
                Score = score,
                FullScore = subjects[i].FullScore,
                Status = status,
            });
        }

        var result = _db.CommitImport(batch);
        StatusText = $"已录入 {ManualName} 在「{exam.Name}」的成绩，共 {result.ScoreCount} 条。";
        ManualStudentNo = ManualName = ManualScore1 = ManualScore2 = ManualScore3 = "";
        ManualScore4 = ManualScore5 = ManualScore6 = ManualSpecialStatus = "";
        LoadFromDatabase();
    }

    /// <summary>保存对学生的笔画/姓名的修改（笔画直接决定同分排序，必须能改）。</summary>
    [RelayCommand]
    private void SaveStudentEdit()
    {
        ErrorText = "";
        if (SelectedStudentForEdit is null) { ErrorText = "请先选择要修改的学生。"; return; }
        var s = SelectedStudentForEdit;
        s.SurnameStrokes = int.TryParse(EditSurnameStrokes, out var a) ? a : 0;
        s.GivenNameStrokes = int.TryParse(EditGivenStrokes, out var b) ? b : 0;
        _db.UpdateStudent(s);
        StatusText = $"已更新 {s.Name} 的笔画（姓 {s.SurnameStrokes}、名 {s.GivenNameStrokes}）。";
        LoadFromDatabase();
    }

    partial void OnSelectedStudentForEditChanged(Student? value)
    {
        EditSurnameStrokes = value?.SurnameStrokes.ToString(CultureInfo.InvariantCulture) ?? "";
        EditGivenStrokes = value?.GivenNameStrokes.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    /// <summary>
    /// 删除学生。**破坏性且级联**：他的成绩、年级排名、小组成员记录都会一并删除（DB 上是 ON DELETE CASCADE）。
    /// 所以必须先问一句，并把"会删掉多少条成绩"写进确认框 —— 老师才知道后果有多大。
    /// </summary>
    [RelayCommand]
    private async Task DeleteStudentAsync()
    {
        ErrorText = "";
        if (SelectedStudentForEdit is null) { ErrorText = "请先选择要删除的学生。"; return; }
        var s = SelectedStudentForEdit;

        int scoreCount = 0;
        if (_ds is not null)
            foreach (var byStudent in _ds.Scores.Values)
                if (byStudent.TryGetValue(s.Id, out var bySubject)) scoreCount += bySubject.Count;

        var ok = ConfirmAsync is null || await ConfirmAsync("删除学生",
            $"删除 {s.Name}（{s.StudentNo}）？\n\n"
            + $"会同时删除他的 {scoreCount} 条成绩与年级排名记录（小组成员记录也会一并删除）。\n"
            + "**此操作不可撤销**，只应在录错了人的时候用。\n"
            + "如果只是想让他不参与某次考试，请改用「特殊状态」标缺考。");
        if (!ok) return;

        _db.DeleteStudent(s.Id);
        SelectedStudentForEdit = null;
        StatusText = $"已删除学生 {s.Name}（{s.StudentNo}），连同 {scoreCount} 条成绩。";
        LoadFromDatabase();
    }

    /// <summary>保存考试属性：名称 / 日期 / 年级总人数（年级排名要显示「第 X / 共 Y」，Y 是手填的）。</summary>
    [RelayCommand]
    private void SaveExamMeta()
    {
        ErrorText = "";
        if (SelectedExam is null) { ErrorText = "请先选择一次考试。"; return; }

        var name = ExamNameInput.Trim();
        if (name.Length == 0) { ErrorText = "考试名称不能为空。"; return; }
        var date = (ExamDateInput ?? DateTimeOffset.Now).Date;

        // exams 表上是 UNIQUE(name, exam_date)：撞了会在 SQLite 层抛异常，老师看不懂那句英文。
        // 提前拦下并说清"和哪一场撞了"。
        if (_ds is not null)
        {
            var clash = _ds.Exams.FirstOrDefault(e =>
                e.Id != SelectedExam.Id && e.Name == name && e.ExamDate.Date == date);
            if (clash is not null)
            {
                ErrorText = $"已经有一场「{name}」（{date:yyyy-MM-dd}）了 —— 两场考试同名又同天无法区分，请改名称或日期。";
                return;
            }
        }

        _db.UpdateExamMeta(SelectedExam.Id, name, date, ExamGradeTotalInput);
        StatusText = $"已更新考试「{name}」（{date:yyyy-MM-dd}），年级总人数 {ExamGradeTotalInput}。";
        LoadFromDatabase();
    }

    [RelayCommand]
    private void DeleteExam()
    {
        if (SelectedExam is null) return;
        var name = SelectedExam.DisplayName;
        _db.DeleteExam(SelectedExam.Id);
        StatusText = $"已删除考试「{name}」及其全部成绩。";
        LoadFromDatabase();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  设置读写
    // ────────────────────────────────────────────────────────────────────────

    private const string SettingsKey = "grade_analysis_settings";

    private GradeAnalysisSettings LoadSettings()
    {
        var s = new GradeAnalysisSettings();
        try
        {
            var json = _db.GetKv(SettingsKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize(json, AppJsonContext.Default.GradeAnalysisSettings);
                if (loaded is not null) s = loaded;
            }
        }
        catch { /* 配置坏了就用默认值与默认科目，不能让模块打不开 */ }
        s.Normalize();
        _suspendRefresh = true;
        ApplyViewFlagsFromSettingsInternal(s);
        WeightAvgScore = s.GroupWeightAverageScore;
        WeightProgress = s.GroupWeightProgressRate;
        WeightStability = s.GroupWeightStability;
        ThresholdStdDev = s.RankStdDevThreshold;
        ThresholdHalfDelta = s.HalfDeltaThreshold;
        ThresholdSubjectBias = s.SubjectBiasRangeThreshold;
        LeaderboardTopN = s.RankingTopN;
        _suspendRefresh = false;
        return s;
    }

    private void SaveSettings()
    {
        try
        {
            _settings.RankingVisibleColumns = CollectVisibleColumns();
            _settings.RankingTopN = LeaderboardTopN;
            _settings.Normalize();
            _db.SetKv(SettingsKey, System.Text.Json.JsonSerializer.Serialize(_settings, AppJsonContext.Default.GradeAnalysisSettings));
        }
        catch (Exception ex) { ErrorText = "保存成绩分析设置失败：" + ex.Message; }
    }

    private void ApplyViewFlagsFromSettings() => ApplyViewFlagsFromSettingsInternal(_settings);

    private void ApplyViewFlagsFromSettingsInternal(GradeAnalysisSettings s)
    {
        var set = new HashSet<string>(s.RankingVisibleColumns, StringComparer.Ordinal);
        ShowRank = set.Contains(RankingColumns.Rank);
        ShowName = set.Contains(RankingColumns.Name);
        ShowStudentNo = set.Contains(RankingColumns.StudentNo);
        ShowTotal = set.Contains(RankingColumns.Total);
        ShowGradeRank = set.Contains(RankingColumns.GradeRank);
        ShowRankDelta = set.Contains(RankingColumns.RankDelta);
        ShowTotalDelta = set.Contains(RankingColumns.TotalDelta);
        ShowTrend = set.Contains(RankingColumns.Trend);
        ShowSubject1 = set.Contains(RankingColumns.SubjectKey(At(s.SubjectNames, 0)));
        ShowSubject2 = set.Contains(RankingColumns.SubjectKey(At(s.SubjectNames, 1)));
        ShowSubject3 = set.Contains(RankingColumns.SubjectKey(At(s.SubjectNames, 2)));
        ShowSubject4 = set.Contains(RankingColumns.SubjectKey(At(s.SubjectNames, 3)));
        ShowSubject5 = set.Contains(RankingColumns.SubjectKey(At(s.SubjectNames, 4)));
        ShowSubject6 = set.Contains(RankingColumns.SubjectKey(At(s.SubjectNames, 5)));
    }

    private List<string> CollectVisibleColumns()
    {
        var list = new List<string>();
        void Add(bool on, string key) { if (on) list.Add(key); }
        var ns = _settings.SubjectNames;
        Add(ShowRank, RankingColumns.Rank);
        Add(ShowName, RankingColumns.Name);
        Add(ShowStudentNo, RankingColumns.StudentNo);
        Add(ShowTotal, RankingColumns.Total);
        Add(ShowSubject1, RankingColumns.SubjectKey(At(ns, 0)));
        Add(ShowSubject2, RankingColumns.SubjectKey(At(ns, 1)));
        Add(ShowSubject3, RankingColumns.SubjectKey(At(ns, 2)));
        Add(ShowSubject4, RankingColumns.SubjectKey(At(ns, 3)));
        Add(ShowSubject5, RankingColumns.SubjectKey(At(ns, 4)));
        Add(ShowSubject6, RankingColumns.SubjectKey(At(ns, 5)));
        Add(ShowGradeRank, RankingColumns.GradeRank);
        Add(ShowRankDelta, RankingColumns.RankDelta);
        Add(ShowTotalDelta, RankingColumns.TotalDelta);
        Add(ShowTrend, RankingColumns.Trend);
        return list;
    }
}
