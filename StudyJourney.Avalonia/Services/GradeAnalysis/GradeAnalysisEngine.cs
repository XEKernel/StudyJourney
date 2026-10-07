using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StudyJourney.Avalonia.Models.GradeAnalysis;

namespace StudyJourney.Avalonia.Services.GradeAnalysis;

/// <summary>
/// 一次分析所需的全量数据（内存视图）。**所有分析都从这里出发**，避免各页面各自查库、
/// 各自排序 —— 那样一旦排名规则改了就得改好几处，而排名算错是不会报错的，只会默默给人排错名次。
/// </summary>
public sealed class GradeDataset
{
    public GradeAnalysisSettings Settings { get; init; } = new();
    public List<Student> Students { get; init; } = new();
    /// <summary>已按 (考试日期, id) 排序 —— 「上一次考试」的语义就建立在这个顺序上。</summary>
    public List<Exam> Exams { get; init; } = new();
    public Dictionary<long, Student> StudentById { get; } = new();
    public Dictionary<long, Exam> ExamById { get; } = new();

    /// <summary>examId → studentId → subject → 成绩。</summary>
    public Dictionary<long, Dictionary<long, Dictionary<string, ScoreRecord>>> Scores { get; } = new();
    /// <summary>examId → studentId → 年级排名（录入值）。</summary>
    public Dictionary<long, Dictionary<long, int>> GradeRanks { get; } = new();

    /// <summary>
    /// 分析结果缓存。<b>必须放在数据集内</b>：<see cref="Analyze"/> 为了算「与上次对比」会递归
    /// 分析上一场，而排行榜要为每一行算一次波动类型、个人详情又要按考试遍历一遍 ——
    /// 没有缓存就是几十倍的重复分析（一个班 50 人 × 6 场 × 递归链）。
    /// 分析结果是**不可变**的（排名在构造时就定好），共享安全。
    /// </summary>
    private readonly Dictionary<long, ExamAnalysis> _analysisCache = new();

    internal bool TryGetAnalysis(long examId, out ExamAnalysis analysis)
        => _analysisCache.TryGetValue(examId, out analysis!);

    internal void StoreAnalysis(long examId, ExamAnalysis analysis) => _analysisCache[examId] = analysis;

    internal void ClearAnalysisCache() => _analysisCache.Clear();

    public static GradeDataset Build(
        GradeAnalysisSettings settings,
        IEnumerable<Student> students,
        IEnumerable<Exam> exams,
        IEnumerable<ScoreRecord> scores,
        IReadOnlyDictionary<long, Dictionary<long, int>> gradeRanks)
    {
        settings.Normalize();
        var ds = new GradeDataset
        {
            Settings = settings,
            Students = students.ToList(),
            Exams = exams.OrderBy(e => e.ExamDate).ThenBy(e => e.Id).ToList(),
        };
        foreach (var s in ds.Students) ds.StudentById[s.Id] = s;
        foreach (var e in ds.Exams) ds.ExamById[e.Id] = e;

        foreach (var sc in scores)
        {
            if (!ds.Scores.TryGetValue(sc.ExamId, out var byStudent))
                ds.Scores[sc.ExamId] = byStudent = new Dictionary<long, Dictionary<string, ScoreRecord>>();
            if (!byStudent.TryGetValue(sc.StudentId, out var bySubject))
                byStudent[sc.StudentId] = bySubject = new Dictionary<string, ScoreRecord>(StringComparer.Ordinal);
            bySubject[sc.Subject] = sc;
        }

        foreach (var kv in gradeRanks) ds.GradeRanks[kv.Key] = kv.Value;
        return ds;
    }

    /// <summary>考试顺序下标（0 起）；找不到返回 -1。</summary>
    public int OrderOf(long examId)
    {
        for (int i = 0; i < Exams.Count; i++)
            if (Exams[i].Id == examId) return i;
        return -1;
    }

    /// <summary>上一次考试（没有则 null）。</summary>
    public Exam? PreviousExam(long examId)
    {
        int i = OrderOf(examId);
        return i > 0 ? Exams[i - 1] : null;
    }

    public Exam? NextExam(long examId)
    {
        int i = OrderOf(examId);
        return i >= 0 && i < Exams.Count - 1 ? Exams[i + 1] : null;
    }
}

/// <summary>单场考试的完整分析结果。</summary>
public sealed class ExamAnalysis
{
    public long ExamId { get; init; }
    public Exam Exam { get; init; } = new();
    /// <summary>可参与排名的学生，按班级排名升序。</summary>
    public List<StudentExamResult> Ranked { get; init; } = new();
    /// <summary>含特殊科目、不参与排名的学生（按姓名排，单独放榜尾）。</summary>
    public List<StudentExamResult> NotRanked { get; init; } = new();
    /// <summary>科目 → (学生 id → 单科排名)。只有该科为「正常」的学生才在内。</summary>
    public Dictionary<string, Dictionary<long, int>> SubjectRanks { get; init; } = new(StringComparer.Ordinal);
    /// <summary>科目 → 班级平均得分率（0..1，只统计正常成绩）。</summary>
    public Dictionary<string, double> ClassAverageRate { get; init; } = new(StringComparer.Ordinal);
    /// <summary>全部学生（含不参与排名的），按总分降序 —— 界面要按行查。</summary>
    public List<StudentExamResult> All { get; init; } = new();

    public StudentExamResult? FindStudent(long studentId)
        => All.FirstOrDefault(x => x.StudentId == studentId);

    public int? SubjectRank(string subject, long studentId)
        => SubjectRanks.TryGetValue(subject, out var m) && m.TryGetValue(studentId, out var r) ? r : null;
}

/// <summary>
/// 成绩分析内核。**全部是静态纯函数**（输入数据集 → 输出结果，无副作用、不碰 UI、不碰数据库），
/// 这样同一套规则可以被排行榜 / 个人详情 / 小组 PK / 波动分析共用，
/// 也能在自检里直接喂构造数据断言 —— 排名规则是最容易「算错了但不报错」的地方。
/// </summary>
public static class GradeAnalysisEngine
{
    // ────────────────────────────────────────────────────────────────────────
    //  排名规则（本项目成绩模块的核心契约）
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 总分排序：**总分降序 → 姓氏笔画少者优先 → 名字笔画少者优先 → 学号小者优先**。
    /// <para>笔画未知（= 0）视为无穷大排在最后，而不是排在最前 —— 否则 Excel 没填笔画的学生
    /// 会莫名其妙全部跑到榜首，这是最容易被当成「软件算错了」的形态。</para>
    /// </summary>
    public static int CompareByTotal(StudentExamResult a, StudentExamResult b)
    {
        int c = b.TotalScore.CompareTo(a.TotalScore);
        if (c != 0) return c;

        c = NormStrokes(a.SurnameStrokes).CompareTo(NormStrokes(b.SurnameStrokes));
        if (c != 0) return c;

        c = NormStrokes(a.GivenNameStrokes).CompareTo(NormStrokes(b.GivenNameStrokes));
        if (c != 0) return c;

        return CompareStudentNo(a.StudentNo, b.StudentNo);
    }

    /// <summary>单科排序：分数降序，同分沿用与总分一致的比画与学号规则。</summary>
    public static int CompareBySubject(string subject, StudentExamResult a, StudentExamResult b)
    {
        var ca = a.Find(subject);
        var cb = b.Find(subject);
        double sa = ca?.Score ?? 0, sb = cb?.Score ?? 0;
        int c = sb.CompareTo(sa);
        if (c != 0) return c;

        c = NormStrokes(a.SurnameStrokes).CompareTo(NormStrokes(b.SurnameStrokes));
        if (c != 0) return c;

        c = NormStrokes(a.GivenNameStrokes).CompareTo(NormStrokes(b.GivenNameStrokes));
        if (c != 0) return c;

        return CompareStudentNo(a.StudentNo, b.StudentNo);
    }

    private static int NormStrokes(int strokes) => strokes > 0 ? strokes : int.MaxValue;

    /// <summary>学号比较：两边都是纯数字就按数值比（「9」应排在「10」前），否则按序数比。</summary>
    public static int CompareStudentNo(string? a, string? b)
    {
        var na = long.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out var va);
        var nb = long.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vb);
        if (na && nb) return va.CompareTo(vb);
        return string.CompareOrdinal(a ?? "", b ?? "");
    }

    // ────────────────────────────────────────────────────────────────────────
    //  单场考试
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>分析一场考试：算总分、班级排名、单科排名、班级平均得分率、与上次的对比。
    /// 结果会被缓存进 <see cref="GradeDataset"/>，重复调用零成本。</summary>
    public static ExamAnalysis Analyze(GradeDataset ds, long examId)
    {
        if (ds.TryGetAnalysis(examId, out var cached)) return cached;
        var built = BuildAnalysis(ds, examId);
        ds.StoreAnalysis(examId, built);
        return built;
    }

    private static ExamAnalysis BuildAnalysis(GradeDataset ds, long examId)
    {
        if (!ds.ExamById.TryGetValue(examId, out var exam))
            return new ExamAnalysis { ExamId = examId };

        var settings = ds.Settings;
        var subjects = settings.SubjectNames;
        ds.Scores.TryGetValue(examId, out var byStudent);
        ds.GradeRanks.TryGetValue(examId, out var gradeRankMap);

        var all = new List<StudentExamResult>();
        if (byStudent is not null)
        {
            foreach (var kv in byStudent)
            {
                if (!ds.StudentById.TryGetValue(kv.Key, out var student)) continue;
                var cells = new List<SubjectCell>();
                double total = 0;
                bool hasSpecial = false;

                foreach (var subj in subjects)
                {
                    if (!kv.Value.TryGetValue(subj, out var rec)) continue; // 表里没这一科就跳过，不补 0
                    cells.Add(new SubjectCell
                    {
                        Subject = subj,
                        Score = rec.Score,
                        FullScore = rec.FullScore > 0 ? rec.FullScore : settings.FullScoreOf(subj),
                        Status = rec.Status,
                    });
                    total += rec.Score;
                    if (rec.Status.IsSpecial()) hasSpecial = true;
                }

                all.Add(new StudentExamResult
                {
                    StudentId = student.Id,
                    ExamId = examId,
                    Name = student.Name,
                    StudentNo = student.StudentNo,
                    SurnameStrokes = student.SurnameStrokes,
                    GivenNameStrokes = student.GivenNameStrokes,
                    Cells = cells,
                    TotalScore = total,
                    HasSpecial = hasSpecial,
                    GradeTotalCount = exam.GradeTotalCount,
                    GradeRank = gradeRankMap is not null && gradeRankMap.TryGetValue(student.Id, out var gr) ? gr : 0,
                });
            }
        }

        var ranked = all.Where(x => x.RankEligible).OrderBy(x => x, Comparer<StudentExamResult>.Create(CompareByTotal)).ToList();
        for (int i = 0; i < ranked.Count; i++) ranked[i].ClassRank = i + 1;

        var notRanked = all.Where(x => !x.RankEligible)
            .OrderBy(x => x.Name, StringComparer.CurrentCulture)
            .ThenBy(x => x.StudentNo, StringComparer.Ordinal)
            .ToList();

        // ── 单科排名与班级平均得分率 ────────────────────────────────────
        var subjectRanks = new Dictionary<string, Dictionary<long, int>>(StringComparer.Ordinal);
        var classAvg = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var subj in subjects)
        {
            var pool = ranked.Where(x => x.Find(subj)?.Counts == true).ToList();
            pool.Sort((a, b) => CompareBySubject(subj, a, b));
            var ranks = new Dictionary<long, int>();
            for (int i = 0; i < pool.Count; i++) ranks[pool[i].StudentId] = i + 1;
            subjectRanks[subj] = ranks;
            classAvg[subj] = pool.Count == 0 ? 0 : pool.Average(x => x.Find(subj)!.Rate);
        }

        // ── 与上一次考试对比 ───────────────────────────────────────────
        var prev = ds.PreviousExam(examId);
        if (prev is not null)
        {
            var prevAnalysis = Analyze(ds, prev.Id); // 递归一层：上一场的排名由它自己算（有缓存）
            var prevMap = prevAnalysis.All.ToDictionary(x => x.StudentId);
            foreach (var row in all)
            {
                if (!prevMap.TryGetValue(row.StudentId, out var p)) continue;
                row.PreviousExamId = prev.Id;
                row.TotalScoreDelta = row.TotalScore - p.TotalScore;

                if (row.ClassRank > 0 && p.ClassRank > 0) row.ClassRankDelta = p.ClassRank - row.ClassRank;
                if (row.GradeRank > 0 && p.GradeRank > 0) row.GradeRankDelta = p.GradeRank - row.GradeRank;
            }
        }

        return new ExamAnalysis
        {
            ExamId = examId,
            Exam = exam,
            Ranked = ranked,
            NotRanked = notRanked,
            All = all,
            SubjectRanks = subjectRanks,
            ClassAverageRate = classAvg,
        };
    }

    /// <summary>进步榜：较上次班级排名有进步的学生，按进步名次降序。</summary>
    public static List<StudentExamResult> ProgressBoard(ExamAnalysis analysis)
        => analysis.Ranked
            .Where(x => x.ClassRankDelta is > 0)
            .OrderByDescending(x => x.ClassRankDelta!.Value)
            .ThenBy(x => x.ClassRank)
            .ToList();

    /// <summary>退步榜：较上次班级排名有退步的学生，按退步名次降序（退得最多的在最前）。</summary>
    public static List<StudentExamResult> RegressionBoard(ExamAnalysis analysis)
        => analysis.Ranked
            .Where(x => x.ClassRankDelta is < 0)
            .OrderBy(x => x.ClassRankDelta!.Value) // 越负越靠前
            .ThenBy(x => x.ClassRank)
            .ToList();

    // ────────────────────────────────────────────────────────────────────────
    //  小组
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 解析某场考试的小组构成。一条 <see cref="GroupMember"/> 表示「自 effective_exam_id 起该生属于该组」，
    /// 所以某场考试的成员 = 对每个 (组, 学生) 取「生效考试顺序 ≤ 目标考试顺序」里最大的那一条。
    /// <para>⚠ 不能用「最新一条」简化：那会让历史考试的小组构成随之后的换人而变化。</para>
    /// </summary>
    public static Dictionary<long, List<long>> ResolveMembers(GradeDataset ds, IReadOnlyList<GroupMember> members, long examId)
    {
        int targetOrder = ds.OrderOf(examId);
        var picked = new Dictionary<(long GroupId, long StudentId), int>(); // 已选记录的生效顺序

        foreach (var m in members)
        {
            int eff = ds.OrderOf(m.EffectiveExamId);
            // 生效考试不在数据集里（被删掉了）→ 视为「一直生效」，用 -1 参与比较而不是丢弃
            if (eff < 0) eff = -1;
            if (targetOrder >= 0 && eff > targetOrder) continue;

            var key = (m.GroupId, m.StudentId);
            if (!picked.TryGetValue(key, out var cur) || eff > cur) picked[key] = eff;
        }

        var result = new Dictionary<long, List<long>>();
        foreach (var kv in picked)
        {
            if (!result.TryGetValue(kv.Key.GroupId, out var list)) result[kv.Key.GroupId] = list = new List<long>();
            list.Add(kv.Key.StudentId);
        }
        return result;
    }

    /// <summary>
    /// 小组综合分：平均总分 / 进步率 / 稳定度 三项，**先在组间做 min-max 归一化**再加权。
    /// <para>为什么要归一化：三项量纲完全不同（总分几百分、进步率 0..1、稳定度 0..100），
    /// 直接加权会让「总分」一项吃掉全部权重，老师拖动滑块会显得毫无效果。</para>
    /// <para>归一化用 min-max 而不是「除以最大值」：除以最大值时最低分也在 0.7 以上，
    /// 组间差异被压扁；min-max 能把差距完整拉开到 0..1。全组同分时按 1 处理（都是满分，无所谓）。</para>
    /// </summary>
    public static List<GroupExamResult> BuildGroupResults(
        GradeDataset ds,
        ExamAnalysis analysis,
        IReadOnlyList<StudentGroup> groups,
        IReadOnlyList<GroupMember> members)
    {
        var resolved = ResolveMembers(ds, members, analysis.ExamId);
        var rows = new List<GroupExamResult>();
        var resultById = analysis.All.ToDictionary(x => x.StudentId);
        var subjects = ds.Settings.SubjectNames;

        foreach (var g in groups)
        {
            var memberIds = resolved.TryGetValue(g.Id, out var list) ? list : new List<long>();
            var results = memberIds
                .Select(id => resultById.TryGetValue(id, out var r) ? r : null)
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList();

            var eligible = results.Where(r => r.RankEligible && r.ClassRank > 0).ToList();

            double avgTotal = eligible.Count > 0 ? eligible.Average(r => r.TotalScore) : 0;
            double avgRank = eligible.Count > 0 ? eligible.Average(r => (double)r.ClassRank) : 0;

            // 进步率的分母是「有上次数据可比的人」，不是全组人数 ——
            // 否则第一次考试（所有人无对比）会算出 0%，看起来像全组退步。
            var comparable = eligible.Where(r => r.ClassRankDelta.HasValue).ToList();
            double progressRate = comparable.Count > 0
                ? comparable.Count(r => r.ClassRankDelta!.Value > 0) / (double)comparable.Count
                : 0;

            double stability = 100.0;
            if (eligible.Count >= 2)
            {
                double mean = eligible.Average(r => (double)r.ClassRank);
                double variance = eligible.Sum(r => Math.Pow(r.ClassRank - mean, 2)) / eligible.Count;
                stability = Math.Clamp(100.0 - Math.Sqrt(variance) * 8.0, 0, 100);
            }

            var rates = new List<double>();
            foreach (var subj in subjects)
            {
                var cells = results.Select(r => r.Find(subj)).Where(c => c?.Counts == true).Select(c => c!).ToList();
                rates.Add(cells.Count > 0 ? cells.Average(c => c.Rate) : 0);
            }

            rows.Add(new GroupExamResult
            {
                GroupId = g.Id,
                Name = g.Name,
                Color = g.Color,
                MemberIds = memberIds,
                AverageTotal = avgTotal,
                AverageClassRank = avgRank,
                ProgressRate = progressRate,
                Stability = stability,
                SubjectAverageRates = rates,
            });
        }

        if (rows.Count == 0) return rows;

        // min-max 归一化（只统计有成员的组，避免空组把上界拉到 0）
        var nonEmpty = rows.Where(r => r.MemberIds.Count > 0).ToList();
        double minTotal = nonEmpty.Count > 0 ? nonEmpty.Min(r => r.AverageTotal) : 0;
        double maxTotal = nonEmpty.Count > 0 ? nonEmpty.Max(r => r.AverageTotal) : 0;
        double minProg = nonEmpty.Count > 0 ? nonEmpty.Min(r => r.ProgressRate) : 0;
        double maxProg = nonEmpty.Count > 0 ? nonEmpty.Max(r => r.ProgressRate) : 0;

        var (wa, wp, ws) = ds.Settings.NormalizedGroupWeights();

        foreach (var r in rows)
        {
            r.NormAverageTotal = Normalize(r.AverageTotal, minTotal, maxTotal);
            r.NormProgressRate = Normalize(r.ProgressRate, minProg, maxProg);
            r.NormStability = Math.Clamp(r.Stability / 100.0, 0, 1);
            r.CompositeScore = r.NormAverageTotal * wa + r.NormProgressRate * wp + r.NormStability * ws;
        }

        return rows.OrderByDescending(r => r.CompositeScore).ThenBy(r => r.Name, StringComparer.CurrentCulture).ToList();
    }

    private static double Normalize(double v, double min, double max)
        => max - min <= 1e-9 ? 1.0 : Math.Clamp((v - min) / (max - min), 0, 1);

    // ────────────────────────────────────────────────────────────────────────
    //  波动分析
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 波动分析：看该生**本学期全部考试**的总分与排名走势。
    ///
    /// <para><b>判定优先级</b>（规格没写死先后，这里明确下来，避免同一份数据在不同页面显示不同结论）：</para>
    /// <list type="number">
    /// <item>考试不足 2 次 → 数据不足</item>
    /// <item>后半段平均排名优于前半段超过阈值 → 上升型（反之下降型）—— 趋势比波动更有信息量，优先</item>
    /// <item>排名标准差 &gt; 阈值 → 波动型</item>
    /// <item>各科平均得分率极差 &gt; 阈值 → 偏科型（规格明确要求「且属于稳定型」）</item>
    /// <item>否则 → 稳定型</item>
    /// </list>
    ///
    /// <para>⚠ 含特殊科目（缺考/免考/作弊）的考试**不参与排名统计**，否则缺考一次会让排名暴跌，
    /// 被判成「下降型」—— 那不是波动，那是没来考试。</para>
    /// </summary>
    public static FluctuationReport AnalyzeFluctuation(GradeDataset ds, long studentId)
    {
        var s = ds.Settings;
        var student = ds.StudentById.TryGetValue(studentId, out var st) ? st : null;
        var subjects = s.SubjectNames;

        // 该生参与过的考试，按考试顺序
        var perExam = new List<StudentExamResult>();
        foreach (var exam in ds.Exams)
        {
            var a = Analyze(ds, exam.Id);
            var row = a.FindStudent(studentId);
            if (row is not null && row.Cells.Count > 0) perExam.Add(row);
        }

        if (perExam.Count == 0)
        {
            return new FluctuationReport
            {
                StudentId = studentId,
                Name = student?.Name ?? "",
                Kind = FluctuationKind.Insufficient,
                SubjectAverageRates = new double[subjects.Length],
            };
        }

        var totals = perExam.Select(x => x.TotalScore).ToList();
        var ranks = perExam.Where(x => x.RankEligible && x.ClassRank > 0).Select(x => x.ClassRank).ToList();

        double stdDev = 0;
        if (ranks.Count >= 2)
        {
            double mean = ranks.Average();
            stdDev = Math.Sqrt(ranks.Sum(r => Math.Pow(r - mean, 2)) / ranks.Count);
        }

        // 前后半段：各取 floor(n/2)，奇数时中间那一次不参与（避免它被两边都算一次）
        double halfDelta = 0, frontAvg = 0, backAvg = 0;
        if (ranks.Count >= 2)
        {
            int half = ranks.Count / 2;
            frontAvg = ranks.Take(half).Average();
            backAvg = ranks.Skip(ranks.Count - half).Average();
            halfDelta = frontAvg - backAvg; // 排名数字变小 = 名次上升 = 正
        }

        var subjectRates = new List<double>();
        foreach (var subj in subjects)
        {
            var vals = perExam.Select(x => x.Find(subj)).Where(c => c?.Counts == true).Select(c => c!.Rate).ToList();
            subjectRates.Add(vals.Count > 0 ? vals.Average() : 0);
        }
        var usedRates = subjectRates.Where(r => r > 0).ToList();
        double range = usedRates.Count >= 2 ? (usedRates.Max() - usedRates.Min()) * 100 : 0;

        var kind = FluctuationKind.Stable;
        if (ranks.Count < 2) kind = FluctuationKind.Insufficient;
        else if (Math.Abs(halfDelta) > s.HalfDeltaThreshold) kind = halfDelta > 0 ? FluctuationKind.Rising : FluctuationKind.Falling;
        else if (stdDev > s.RankStdDevThreshold) kind = FluctuationKind.Fluctuant;
        else if (range > s.SubjectBiasRangeThreshold) kind = FluctuationKind.SubjectBias;
        else kind = FluctuationKind.Stable;

        return new FluctuationReport
        {
            StudentId = studentId,
            Name = student?.Name ?? "",
            Kind = kind,
            ExamCount = perExam.Count,
            RankedExamCount = ranks.Count,
            AverageTotal = totals.Average(),
            MaxTotal = totals.Max(),
            MinTotal = totals.Min(),
            BestRank = ranks.Count > 0 ? ranks.Min() : 0,
            WorstRank = ranks.Count > 0 ? ranks.Max() : 0,
            RankStdDev = stdDev,
            HalfDelta = halfDelta,
            FrontHalfAvgRank = frontAvg,
            BackHalfAvgRank = backAvg,
            SubjectRateRange = range,
            SubjectAverageRates = subjectRates,
        };
    }

    // ────────────────────────────────────────────────────────────────────────
    //  单人 PK
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>PK 对比项。</summary>
    public sealed class PkItem
    {
        public string Label { get; init; } = "";
        public string ValueA { get; init; } = "";
        public string ValueB { get; init; } = "";
        /// <summary>1 = A 胜，2 = B 胜，0 = 平。</summary>
        public int Winner { get; set; }
        public bool LowerIsBetter { get; init; }
    }

    public sealed class PkResult
    {
        public string NameA { get; init; } = "";
        public string NameB { get; init; } = "";
        public List<PkItem> Items { get; init; } = new();
        public int WinsA { get; set; }
        public int WinsB { get; set; }
        public int Ties { get; set; }
    }

    /// <summary>两人在某场考试上的对比。缺数据的一方判负，双方都缺则平。</summary>
    public static PkResult BuildPk(GradeDataset ds, ExamAnalysis analysis, long studentIdA, long studentIdB)
    {
        var a = analysis.FindStudent(studentIdA);
        var b = analysis.FindStudent(studentIdB);
        var result = new PkResult { NameA = a?.Name ?? "", NameB = b?.Name ?? "" };
        if (a is null || b is null) return result;

        void Add(string label, double? va, double? vb, bool lowerIsBetter, Func<double, string> fmt)
        {
            var item = new PkItem
            {
                Label = label,
                LowerIsBetter = lowerIsBetter,
                ValueA = va.HasValue ? fmt(va.Value) : "—",
                ValueB = vb.HasValue ? fmt(vb.Value) : "—",
            };
            if (!va.HasValue && !vb.HasValue) item.Winner = 0;
            else if (!va.HasValue) item.Winner = 2;
            else if (!vb.HasValue) item.Winner = 1;
            else if (Math.Abs(va.Value - vb.Value) < 1e-9) item.Winner = 0;
            else item.Winner = (va.Value < vb.Value) == lowerIsBetter ? 1 : 2;
            result.Items.Add(item);
        }

        string Num0(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        string Pct(double v) => (v * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

        Add("总分", a.TotalScore, b.TotalScore, false, Num0);
        Add("班级排名", a.ClassRank > 0 ? a.ClassRank : null, b.ClassRank > 0 ? b.ClassRank : null, true, v => "第 " + v.ToString("0", CultureInfo.InvariantCulture));
        Add("年级排名", a.GradeRank > 0 ? a.GradeRank : null, b.GradeRank > 0 ? b.GradeRank : null, true, v => "第 " + v.ToString("0", CultureInfo.InvariantCulture));
        Add("较上次进步名次", a.ClassRankDelta, b.ClassRankDelta, false, v => FluctuationKindText.DeltaText((int)v));
        Add("较上次总分变化", a.TotalScoreDelta, b.TotalScoreDelta, false, v => (v >= 0 ? "+" : "") + Num0(v));

        foreach (var subj in ds.Settings.SubjectNames)
        {
            var ca = a.Find(subj);
            var cb = b.Find(subj);
            Add(subj + "分数",
                ca is { Counts: true } ? ca.Score : null,
                cb is { Counts: true } ? cb.Score : null,
                false, Num0);
            Add(subj + "得分率",
                ca is { Counts: true } ? ca.Rate : null,
                cb is { Counts: true } ? cb.Rate : null,
                false, Pct);
        }

        result.WinsA = result.Items.Count(i => i.Winner == 1);
        result.WinsB = result.Items.Count(i => i.Winner == 2);
        result.Ties = result.Items.Count(i => i.Winner == 0);
        return result;
    }
}
