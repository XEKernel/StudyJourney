using System;
using System.Collections.Generic;
using System.Linq;

namespace StudyJourney.Avalonia.Models.GradeAnalysis;

/// <summary>一个科目的定义（名称 + 满分）。做成可配置项而不是硬编码常量，
/// 因为不同学校的满分/科目组合会变（如英语听力另算、技术课 100 分）。</summary>
public sealed class SubjectDefinition
{
    public string Name { get; set; } = "";
    public double FullScore { get; set; } = 100;
}

/// <summary>
/// 成绩分析模块的配置。**落盘位置是成绩库自己的 <c>app_kv</c> 表（JSON）**，
/// 不写进 <c>settings.json</c> —— 理由有两条：
/// ① 设置页的「恢复默认设置」会整份替换 AppSettings，成绩分析这类领域配置不该被它波及；
/// ② 排行榜视图是「全班共用一套」的展示配置，应该跟着成绩数据走（拷贝成绩库就带走了视图），
///    而不是跟着这台机器的软件设置走。
/// </summary>
public sealed class GradeAnalysisSettings
{
    /// <summary>科目表。默认六科：语数英 150 分，物化生 100 分。</summary>
    public List<SubjectDefinition> Subjects { get; set; } = CreateDefaultSubjects();

    /// <summary>波动分析：稳定型 / 波动型的排名标准差阈值（默认 5）。</summary>
    public double RankStdDevThreshold { get; set; } = 5.0;

    /// <summary>波动分析：上升型 / 下降型的「后半段平均排名优于前半段」阈值，单位「名」（默认 2）。</summary>
    public double HalfDeltaThreshold { get; set; } = 2.0;

    /// <summary>波动分析：偏科判定阈值 —— 各科平均得分率的极差（百分点，默认 22）。</summary>
    public double SubjectBiasRangeThreshold { get; set; } = 22.0;

    /// <summary>小组综合分权重：平均总分（默认 50%）。三项会自动归一化，不必凑成 100。</summary>
    public double GroupWeightAverageScore { get; set; } = 50;
    /// <summary>小组综合分权重：进步率（默认 25%）。</summary>
    public double GroupWeightProgressRate { get; set; } = 25;
    /// <summary>小组综合分权重：稳定度（默认 25%）。</summary>
    public double GroupWeightStability { get; set; } = 25;

    /// <summary>总分榜显示的列（键名见 <see cref="RankingColumns"/>）。全班共用一套。</summary>
    public List<string> RankingVisibleColumns { get; set; } = RankingColumns.CreateDefault();
    /// <summary>总分榜限制显示前 N 名；0 = 不限制。</summary>
    public int RankingTopN { get; set; }

    public static List<SubjectDefinition> CreateDefaultSubjects() => new()
    {
        new SubjectDefinition { Name = "语文", FullScore = 150 },
        new SubjectDefinition { Name = "数学", FullScore = 150 },
        new SubjectDefinition { Name = "英语", FullScore = 150 },
        new SubjectDefinition { Name = "物理", FullScore = 100 },
        new SubjectDefinition { Name = "化学", FullScore = 100 },
        new SubjectDefinition { Name = "生物", FullScore = 100 },
    };

    public string[] SubjectNames => Subjects.Select(s => s.Name).ToArray();

    public double FullScoreOf(string subject)
    {
        foreach (var s in Subjects)
            if (s.Name == subject) return s.FullScore;
        return 0;
    }

    /// <summary>三项权重归一化成和为 1。全为 0（老师把滑块全拖到底）时退化为「平均总分占 100%」，
    /// 避免除零，也避免小组排名变成随机顺序。</summary>
    public (double Avg, double Progress, double Stability) NormalizedGroupWeights()
    {
        double a = Math.Max(0, GroupWeightAverageScore);
        double p = Math.Max(0, GroupWeightProgressRate);
        double s = Math.Max(0, GroupWeightStability);
        double sum = a + p + s;
        if (sum <= 0) return (1, 0, 0);
        return (a / sum, p / sum, s / sum);
    }

    /// <summary>把配置规范化（去重科目、剔除非法权重），用于手工录入/外部导入后的兜底。</summary>
    public void Normalize()
    {
        if (Subjects == null || Subjects.Count == 0) Subjects = CreateDefaultSubjects();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cleaned = new List<SubjectDefinition>();
        foreach (var s in Subjects)
        {
            if (string.IsNullOrWhiteSpace(s.Name)) continue;
            s.Name = s.Name.Trim();
            if (!seen.Add(s.Name)) continue;
            if (s.FullScore <= 0) s.FullScore = 100;
            cleaned.Add(s);
        }
        if (cleaned.Count == 0) cleaned = CreateDefaultSubjects();
        Subjects = cleaned;

        if (RankStdDevThreshold <= 0) RankStdDevThreshold = 5;
        if (HalfDeltaThreshold <= 0) HalfDeltaThreshold = 2;
        if (SubjectBiasRangeThreshold <= 0) SubjectBiasRangeThreshold = 22;
        if (RankingTopN < 0) RankingTopN = 0;

        var valid = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in RankingColumns.FixedColumns) valid.Add(k);
        foreach (var s in Subjects) valid.Add(RankingColumns.SubjectKey(s.Name));
        RankingVisibleColumns ??= RankingColumns.CreateDefault();
        RankingVisibleColumns = RankingVisibleColumns.Where(valid.Contains).Distinct().ToList();
        if (RankingVisibleColumns.Count == 0) RankingVisibleColumns = RankingColumns.CreateDefault();
    }
}

/// <summary>
/// 总分榜的列键。用字符串键而不是枚举，是为了让「每个科目一列」这种动态列也能统一表示，
/// 并且能直接序列化进 JSON（枚举名一旦重命名就会让老配置文件失效）。
/// </summary>
public static class RankingColumns
{
    public const string Rank = "rank";                    // 班级排名
    public const string Name = "name";                    // 姓名
    public const string StudentNo = "no";                 // 学号
    public const string Total = "total";                  // 总分
    public const string GradeRank = "graderank";          // 年级排名
    public const string RankDelta = "rankdelta";          // 较上次班级排名变化
    public const string TotalDelta = "totaldelta";        // 较上次总分变化
    public const string Trend = "trend";                  // 波动标签

    /// <summary>科目列的键前缀：「subject:语文」。</summary>
    public const string SubjectPrefix = "subject:";

    public static string SubjectKey(string subject) => SubjectPrefix + subject;

    public static readonly string[] FixedColumns =
        { Rank, Name, StudentNo, Total, GradeRank, RankDelta, TotalDelta, Trend };

    /// <summary>总分榜的默认列：名次 + 姓名 + 总分 + 全科 + 年级排名 + 排名变化。</summary>
    public static List<string> CreateDefault()
    {
        var list = new List<string> { Rank, Name, Total };
        foreach (var s in GradeAnalysisSettings.CreateDefaultSubjects())
            list.Add(SubjectKey(s.Name));
        list.Add(GradeRank);
        list.Add(RankDelta);
        return list;
    }
}
