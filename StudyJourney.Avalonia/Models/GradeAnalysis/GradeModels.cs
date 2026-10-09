using System;
using System.Collections.Generic;
using System.Linq;

namespace StudyJourney.Avalonia.Models.GradeAnalysis;

/// <summary>
/// 单科成绩的状态。
/// <para>⚠ 本枚举**按整数落盘**（SQLite 的 status 列），因此只能**在末尾追加**，
/// 绝不能重排或删除已有项 —— 与 <c>AutomationRule</c> 的 Trigger/Action 同一纪律。</para>
/// <para>语义：<b>正常</b>参与一切排名；<b>缺考/免考/作弊</b>三者的单科不参与单科排名，
/// 且会使该生**本次考试的总分整体不参与班级排名与年级排名**（但总分仍计算并显示，
/// 界面需标注「含特殊科目，不参与排名」）。见 <c>GradeAnalysisEngine</c>。</para>
/// </summary>
public enum ScoreStatus
{
    Normal = 0,
    Absent = 1,
    Exempt = 2,
    Cheating = 3,
}

/// <summary>分数状态的显示名与中文解析（Excel 的「特殊状态」列形如「数学缺考」）。</summary>
public static class ScoreStatusText
{
    public static string ToDisplay(this ScoreStatus s) => s switch
    {
        ScoreStatus.Absent => "缺考",
        ScoreStatus.Exempt => "免考",
        ScoreStatus.Cheating => "作弊",
        _ => "正常",
    };

    /// <summary>把中文状态词解析成枚举；无法识别返回 false（调用方负责报错，不要静默当正常）。</summary>
    public static bool TryParse(string? text, out ScoreStatus status)
    {
        status = ScoreStatus.Normal;
        if (string.IsNullOrWhiteSpace(text)) return true; // 空 = 正常
        var t = text.Trim();
        switch (t)
        {
            case "正常":
            case "-":
            case "无": status = ScoreStatus.Normal; return true;
            case "缺考":
            case "缺": status = ScoreStatus.Absent; return true;
            case "免考":
            case "免": status = ScoreStatus.Exempt; return true;
            case "作弊":
            case "违纪": status = ScoreStatus.Cheating; return true;
            default: return false;
        }
    }

    /// <summary>是否为「不参与排名」的特殊状态。</summary>
    public static bool IsSpecial(this ScoreStatus s) => s != ScoreStatus.Normal;
}

/// <summary>学生。笔画用于同分时的稳定排序（姓氏笔画少者优先，再名字笔画少者优先，最后学号小者优先）。</summary>
public sealed class Student
{
    public long Id { get; set; }
    /// <summary>学号。Excel 里可能是数字也可能是字符串（如「20230101」），一律按字符串存，排序时再尝试数值化。</summary>
    public string StudentNo { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>姓氏笔画数。Excel 未提供时为 0，此时同分排序退化为按学号。</summary>
    public int SurnameStrokes { get; set; }
    /// <summary>名字笔画数（不含姓）。</summary>
    public int GivenNameStrokes { get; set; }
    public string ClassName { get; set; } = "";

    public string DisplayName => string.IsNullOrEmpty(StudentNo) ? Name : $"{Name}（{StudentNo}）";
}

/// <summary>一次考试。<see cref="GradeTotalCount"/> 是「年级总人数」，每次考试手动填写 ——
/// 本模块只掌握本班名单，无法自行算出年级排名，故年级排名是**录入值**而非计算值。</summary>
public sealed class Exam
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime ExamDate { get; set; }
    /// <summary>年级总人数（手动填写），用于展示「第 X 名 / 共 Y 人」。</summary>
    public int GradeTotalCount { get; set; }

    public string DisplayName => $"{ExamDate:yyyy-MM-dd} {Name}";
}

/// <summary>一条单科成绩（学生 × 考试 × 科目）。</summary>
public sealed class ScoreRecord
{
    public long Id { get; set; }
    public long StudentId { get; set; }
    public long ExamId { get; set; }
    public string Subject { get; set; } = "";
    /// <summary>得分。缺考/免考通常写 0，但**要不要计 0 由统计口径决定**，不要在这里偷改。</summary>
    public double Score { get; set; }
    /// <summary>满分。同学科在各次考试间可能不同（如月考 100、期中 150），所以随成绩一起存，不取全局常量。</summary>
    public double FullScore { get; set; }
    public ScoreStatus Status { get; set; } = ScoreStatus.Normal;
}

/// <summary>小组（老师手动维护的固定分组）。组内成员允许换人，成员变动见 <see cref="GroupMember"/>。</summary>
public sealed class StudentGroup
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>颜色，形如 <c>#RRGGBB</c>。沿用项目的 <c>ColorSwatch</c> 约定，不让老师手打 hex。</summary>
    public string Color { get; set; } = "#2B6CB0";
}

/// <summary>
/// 小组成员**快照**。一条记录表示「自 <see cref="EffectiveExamId"/> 这次考试起，
/// <see cref="StudentId"/> 属于 <see cref="GroupId"/>」。
/// <para>解析某次考试的成员 = 对每个 (组, 学生) 取「考试顺序 ≤ 目标考试」中最大的一条。
/// 换人时**追加一条新记录**，绝不修改旧记录 —— 否则会篡改历史考试的小组构成。</para>
/// </summary>
public sealed class GroupMember
{
    public long Id { get; set; }
    public long GroupId { get; set; }
    public long StudentId { get; set; }
    /// <summary>生效考试 Id。指向的考试顺序由 <c>GradeDataset.OrderOf</c> 统一折算。</summary>
    public long EffectiveExamId { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  分析结果 DTO（只读投影，供 ViewModel / 图表消费）
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>某学生某场考试的单科表现。</summary>
public sealed class SubjectCell
{
    public string Subject { get; init; } = "";
    public double Score { get; init; }
    public double FullScore { get; init; }
    public ScoreStatus Status { get; init; } = ScoreStatus.Normal;

    /// <summary>得分率 0..1；满分为 0 时返回 0（防除零）。</summary>
    public double Rate => FullScore > 0 ? Score / FullScore : 0;
    public bool Counts => Status == ScoreStatus.Normal;
}

/// <summary>某学生某场考试的整体结果（总分 / 排名 / 与上次对比）。</summary>
public sealed class StudentExamResult
{
    public long StudentId { get; init; }
    public long ExamId { get; init; }
    public string Name { get; init; } = "";
    public string StudentNo { get; init; } = "";
    public int SurnameStrokes { get; init; }
    public int GivenNameStrokes { get; init; }

    /// <summary>该次考试该生各科成绩（按 settings 的科目顺序）。</summary>
    public IReadOnlyList<SubjectCell> Cells { get; init; } = Array.Empty<SubjectCell>();

    /// <summary>总分。**始终计算并显示**，即使含特殊科目。</summary>
    public double TotalScore { get; init; }

    /// <summary>是否存在缺考/免考/作弊的单科。</summary>
    public bool HasSpecial { get; init; }
    /// <summary>总分是否可参与排名（= 无特殊科目）。</summary>
    public bool RankEligible => !HasSpecial;

    /// <summary>班级排名（1 起）。0 = 不参与排名。</summary>
    public int ClassRank { get; set; }
    /// <summary>年级排名（录入值）。0 = 未录入或不参与。</summary>
    public int GradeRank { get; set; }
    /// <summary>年级总人数（来自 <see cref="Exam.GradeTotalCount"/>）。</summary>
    public int GradeTotalCount { get; set; }

    /// <summary>较上一次考试的班级排名变化。正数 = 进步（名次数字变小）。null = 无上次数据。</summary>
    public int? ClassRankDelta { get; set; }
    /// <summary>较上一次考试的年级排名变化。正数 = 进步。</summary>
    public int? GradeRankDelta { get; set; }
    /// <summary>较上一次考试的总分变化。</summary>
    public double? TotalScoreDelta { get; set; }
    /// <summary>上一次的考试 Id（用于「与上次对比」跳转）。</summary>
    public long? PreviousExamId { get; set; }

    public SubjectCell? Find(string subject)
    {
        for (int i = 0; i < Cells.Count; i++)
            if (Cells[i].Subject == subject) return Cells[i];
        return null;
    }
}

/// <summary>波动类型（波动分析页的结论标签）。</summary>
public enum FluctuationKind
{
    Insufficient = 0, // 数据不足（只有 1 次考试）
    Stable = 1,       // 稳定型
    Fluctuant = 2,    // 波动型
    Rising = 3,       // 上升型
    Falling = 4,      // 下降型
    SubjectBias = 5,  // 偏科型
}

public static class FluctuationKindText
{
    public static string ToDisplay(this FluctuationKind k) => k switch
    {
        FluctuationKind.Stable => "稳定型",
        FluctuationKind.Fluctuant => "波动型",
        FluctuationKind.Rising => "上升型",
        FluctuationKind.Falling => "下降型",
        FluctuationKind.SubjectBias => "偏科型",
        _ => "数据不足",
    };

    /// <summary>标签配色（GitHub Dark 取向，低饱和）。返回 #RRGGBB。</summary>
    public static string ToColor(this FluctuationKind k) => k switch
    {
        FluctuationKind.Stable => "#3FB950",   // 绿
        FluctuationKind.Fluctuant => "#D29922", // 黄
        FluctuationKind.Rising => "#58A6FF",    // 蓝
        FluctuationKind.Falling => "#F85149",   // 红
        FluctuationKind.SubjectBias => "#BC8CFF", // 紫
        _ => "#8B949E",                          // 灰
    };

    /// <summary>排名变化的小箭头/标签。</summary>
    public static string DeltaText(int? delta) => delta switch
    {
        null => "—",
        > 0 => $"↑{delta}",
        < 0 => $"↓{-delta}",
        _ => "—",
    };

    public static string DeltaColor(int? delta) => delta switch
    {
        null => "#8B949E",
        > 0 => "#3FB950",
        < 0 => "#F85149",
        _ => "#8B949E",
    };
}

/// <summary>波动分析结论。</summary>
public sealed class FluctuationReport
{
    public long StudentId { get; init; }
    public string Name { get; init; } = "";
    public FluctuationKind Kind { get; init; }
    public int ExamCount { get; init; }
    /// <summary>其中**可参与排名**的考试次数（缺考/免考/作弊的那几次不计入）。
    /// 波动结论只由这些点得出 —— 所以「考了 3 次但有 2 次缺考」时结论只能是「数据不足」，
    /// 界面上必须把这两个数分开显示，否则老师会以为软件算错了。</summary>
    public int RankedExamCount { get; init; }
    public double AverageTotal { get; init; }
    public double MaxTotal { get; init; }
    public double MinTotal { get; init; }
    public int BestRank { get; init; }
    public int WorstRank { get; init; }
    /// <summary>班级排名的标准差（越小越稳）。</summary>
    public double RankStdDev { get; init; }
    /// <summary>前半段 vs 后半段的平均排名差（正 = 后半段名次更小 = 进步）。</summary>
    public double HalfDelta { get; init; }
    public double FrontHalfAvgRank { get; init; }
    public double BackHalfAvgRank { get; init; }
    /// <summary>各科平均得分率的极差（百分点，0..100）。</summary>
    public double SubjectRateRange { get; init; }
    /// <summary>各科平均得分率（按科目顺序）。</summary>
    public IReadOnlyList<double> SubjectAverageRates { get; init; } = Array.Empty<double>();
}

/// <summary>小组在某场考试上的成绩单。</summary>
public sealed class GroupExamResult
{
    public long GroupId { get; init; }
    public string Name { get; init; } = "";
    public string Color { get; init; } = "#2B6CB0";
    public IReadOnlyList<long> MemberIds { get; init; } = Array.Empty<long>();

    /// <summary>小组平均总分（只统计该场考试可参与排名的成员）。</summary>
    public double AverageTotal { get; init; }
    /// <summary>小组平均班级排名（0 = 无可统计成员）。</summary>
    public double AverageClassRank { get; init; }
    /// <summary>进步率 = 较上次班级排名有进步的人数 / 可比人数（0..1）。</summary>
    public double ProgressRate { get; init; }
    /// <summary>稳定度 = clamp(100 - 班级排名标准差 × 8, 0, 100)。</summary>
    public double Stability { get; init; }
    /// <summary>综合分（三项归一化后加权）。</summary>
    public double CompositeScore { get; set; }
    /// <summary>各科平均得分率（按科目顺序，0..1）。</summary>
    public IReadOnlyList<double> SubjectAverageRates { get; init; } = Array.Empty<double>();

    // 归一化后的三项（0..1），用于界面说明「为什么这个组分高」
    public double NormAverageTotal { get; set; }
    public double NormProgressRate { get; set; }
    public double NormStability { get; set; }
}
