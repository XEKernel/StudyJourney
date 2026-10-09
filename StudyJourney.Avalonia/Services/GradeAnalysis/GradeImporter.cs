using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using StudyJourney.Avalonia.Models.GradeAnalysis;

namespace StudyJourney.Avalonia.Services.GradeAnalysis;

public enum ImportSeverity { Info, Warning, Error }

/// <summary>一条导入诊断。老师的界面上要能直接照着改表，所以必须带行号与列名。</summary>
public sealed class ImportDiagnostic
{
    public ImportSeverity Severity { get; init; }
    /// <summary>Excel 里的行号（1 起，含表头行号），0 = 与具体行无关。</summary>
    public int Row { get; init; }
    public string Message { get; init; } = "";

    public string ToLine() => Row > 0 ? $"第 {Row} 行：{Message}" : Message;
}

/// <summary>导入结果。失败时 <see cref="Batch"/> 为 null，界面据此切到「手动录入」。</summary>
public sealed class ImportOutcome
{
    public bool Success { get; init; }
    public ImportBatch? Batch { get; init; }
    public List<ImportDiagnostic> Diagnostics { get; init; } = new();
    /// <summary>文件里识别到的表头 → 列下标，用于界面回显「我认成了什么」。</summary>
    public Dictionary<string, int> ColumnMap { get; init; } = new(StringComparer.Ordinal);
    /// <summary>缺少的必要列（用于给出「请补上 XX 列」的明确提示）。</summary>
    public List<string> MissingColumns { get; init; } = new();
    public string SheetName { get; init; } = "";
}

/// <summary>待入库的成绩行（还没换成数据库自增 id，靠学号 + 考试键引用）。</summary>
public sealed class PendingScore
{
    public string StudentNo { get; set; } = "";
    public string ExamKey { get; set; } = "";
    public string Subject { get; set; } = "";
    public double Score { get; set; }
    public double FullScore { get; set; }
    public ScoreStatus Status { get; set; } = ScoreStatus.Normal;
}

/// <summary>待入库的年级排名行。</summary>
public sealed class PendingGradeRank
{
    public string StudentNo { get; set; } = "";
    public string ExamKey { get; set; } = "";
    public int GradeRank { get; set; }
}

/// <summary>
/// Excel 成绩导入：表头识别 → 列映射 → 逐行校验 → 组装待入库批次。
/// <para><b>设计原则：宁可报错让老师改表，也不猜。</b>所有「猜」的地方（日期序列号、特殊状态词）
/// 都在诊断里明说，避免老师拿到一份数字对不上却不知道哪错了的表。</para>
/// <para><b>列名用别名表而不是精确匹配</b>：老师手上的成绩表通常来自不同的阅卷系统，
/// 「学号 / 学籍号 / 编号」「考试名称 / 考试」「年级排名 / 校排名」这些写法都得认。</para>
/// </summary>
public static class GradeImporter
{
    // 列别名（全部 trim 后按 Ordinal 比较）。键 = 规范列名，值 = 可接受的写法。
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        ["学号"] = new[] { "学号", "学籍号", "考号", "编号", "学生编号" },
        ["姓名"] = new[] { "姓名", "名字", "学生姓名", "学生" },
        ["班级"] = new[] { "班级", "班", "班级名称", "行政班" },
        ["考试名称"] = new[] { "考试名称", "考试", "考试名", "场次" },
        ["考试日期"] = new[] { "考试日期", "日期", "考试时间", "时间" },
        ["特殊状态"] = new[] { "特殊状态", "状态", "备注", "异常状态", "缺考情况" },
        ["年级排名"] = new[] { "年级排名", "校排名", "年级名次", "全校排名" },
        ["姓氏笔画"] = new[] { "姓氏笔画", "姓笔画", "姓的笔画", "姓画" },
        ["名字笔画"] = new[] { "名字笔画", "名笔画", "名的笔画", "名画" },
    };

    /// <summary>表头行扫描范围（老师表里常有标题行、制表单位、班级抬头）。</summary>
    private const int HeaderScanRows = 12;

    public static ImportOutcome Parse(string filePath, GradeAnalysisSettings settings, string? sheetName = null)
    {
        var diags = new List<ImportDiagnostic>();

        if (!XlsxReader.TryReadSheet(filePath, sheetName, out var sheet, out var error))
        {
            diags.Add(new ImportDiagnostic { Severity = ImportSeverity.Error, Message = error });
            return new ImportOutcome { Success = false, Diagnostics = diags };
        }

        var subjects = settings.Subjects.Select(s => s.Name).ToArray();

        // ── 1. 找表头行 ────────────────────────────────────────────────
        int headerRow = FindHeaderRow(sheet, subjects);
        if (headerRow < 0)
        {
            diags.Add(new ImportDiagnostic
            {
                Severity = ImportSeverity.Error,
                Message = "没有找到表头行。请确认第一行（或前几行内）是列名，且至少包含「姓名」和「学号」列。",
            });
            return new ImportOutcome { Success = false, Diagnostics = diags, SheetName = sheet.Name };
        }

        // ── 2. 列映射 ──────────────────────────────────────────────────
        var map = BuildColumnMap(sheet, headerRow, subjects);
        var missing = new List<string>();
        if (!map.ContainsKey("学号")) missing.Add("学号");
        if (!map.ContainsKey("姓名")) missing.Add("姓名");
        var mappedSubjects = subjects.Where(s => map.ContainsKey(RollCall.SubjectKey(s))).ToArray();

        if (missing.Count > 0 || mappedSubjects.Length == 0)
        {
            if (mappedSubjects.Length == 0) missing.Add($"至少一个科目列（{string.Join("、", subjects)}）");
            diags.Add(new ImportDiagnostic
            {
                Severity = ImportSeverity.Error,
                Message = $"表里缺少必要列：{string.Join("、", missing)}。请补上后重新导入，或改用「手动录入」。",
            });
            return new ImportOutcome
            {
                Success = false,
                Diagnostics = diags,
                ColumnMap = map,
                MissingColumns = missing,
                SheetName = sheet.Name,
            };
        }

        if (!map.ContainsKey("考试名称") || !map.ContainsKey("考试日期"))
        {
            diags.Add(new ImportDiagnostic
            {
                Severity = ImportSeverity.Error,
                Message = "表里没有「考试名称」或「考试日期」列 —— 没有它们就无法区分是第几次考试。请补上后重新导入。",
            });
            return new ImportOutcome
            {
                Success = false,
                Diagnostics = diags,
                ColumnMap = map,
                MissingColumns = new List<string> { "考试名称", "考试日期" },
                SheetName = sheet.Name,
            };
        }

        diags.Add(new ImportDiagnostic
        {
            Severity = ImportSeverity.Info,
            Message = $"表头在第 {sheet.ExcelRowOf(headerRow)} 行，识别到科目列：{string.Join("、", mappedSubjects)}。",
        });

        // ── 3. 逐行解析 ────────────────────────────────────────────────
        var batch = new ImportBatch();
        var batchStudents = new Dictionary<string, Student>(StringComparer.Ordinal);
        var batchExams = new Dictionary<string, Exam>(StringComparer.Ordinal);
        var batchGradeRanks = new List<PendingGradeRank>();
        var seenStudentExam = new HashSet<string>(StringComparer.Ordinal);

        int okRows = 0, skippedRows = 0;

        for (int r = headerRow + 1; r < sheet.Rows.Count; r++)
        {
            int excelRow = sheet.ExcelRowOf(r);   // 用真实 Excel 行号（Excel 省略空行，下标 ≠ 行号）

            var no = Cell(sheet, r, map, "学号");
            var name = Cell(sheet, r, map, "姓名");
            if (no.Length == 0 && name.Length == 0) continue;         // 整行空 → 静默跳过（表尾常见）
            if (no.Length == 0 || name.Length == 0)
            {
                diags.Add(new ImportDiagnostic
                {
                    Severity = ImportSeverity.Warning,
                    Row = excelRow,
                    Message = no.Length == 0 ? "缺少学号，该行已跳过。" : "缺少姓名，该行已跳过。",
                });
                skippedRows++;
                continue;
            }

            var examName = Cell(sheet, r, map, "考试名称");
            var examDate = ParseDate(Cell(sheet, r, map, "考试日期"));
            if (examName.Length == 0 || examDate is null)
            {
                diags.Add(new ImportDiagnostic
                {
                    Severity = ImportSeverity.Warning,
                    Row = excelRow,
                    Message = "考试名称或考试日期为空/无法识别，该行已跳过。",
                });
                skippedRows++;
                continue;
            }

            var exam = new Exam { Name = examName, ExamDate = examDate.Value, GradeTotalCount = 0 };
            var examKey = ImportBatch.ExamKey(exam);

            // 学生（同一学号只建一条）
            if (!batchStudents.TryGetValue(no, out var student))
            {
                student = new Student
                {
                    StudentNo = no,
                    Name = name,
                    ClassName = Cell(sheet, r, map, "班级"),
                    SurnameStrokes = ParseIntOrZero(Cell(sheet, r, map, "姓氏笔画")),
                    GivenNameStrokes = ParseIntOrZero(Cell(sheet, r, map, "名字笔画")),
                };
                batchStudents[no] = student;
            }
            else if (student.Name != name)
            {
                diags.Add(new ImportDiagnostic
                {
                    Severity = ImportSeverity.Warning,
                    Row = excelRow,
                    Message = $"学号 {no} 在表里出现了两个姓名（「{student.Name}」与「{name}」），已沿用第一个。",
                });
            }

            if (!batchExams.ContainsKey(examKey)) batchExams[examKey] = exam;

            // 同一次考试同一学生只允许一组成绩：重复行直接跳过并报警，避免后写的静默覆盖先写的
            if (!seenStudentExam.Add(no + "\u0001" + examKey))
            {
                diags.Add(new ImportDiagnostic
                {
                    Severity = ImportSeverity.Warning,
                    Row = excelRow,
                    Message = $"学号 {no} 在「{examName}」里重复出现，该行已跳过（保留先出现的）。",
                });
                skippedRows++;
                continue;
            }

            // 特殊状态列（如「数学缺考；英语作弊」）
            var specialMap = new Dictionary<string, ScoreStatus>(StringComparer.Ordinal);
            var specialText = Cell(sheet, r, map, "特殊状态");
            if (specialText.Length > 0 && !TryParseSpecialStatus(specialText, mappedSubjects, out specialMap, out var specialErr))
            {
                diags.Add(new ImportDiagnostic
                {
                    Severity = ImportSeverity.Warning,
                    Row = excelRow,
                    Message = $"特殊状态「{specialText}」无法识别（{specialErr}），已按正常处理。",
                });
            }

            foreach (var subj in mappedSubjects)
            {
                var raw = Cell(sheet, r, map, RollCall.SubjectKey(subj));
                var status = specialMap.TryGetValue(subj, out var st) ? st : ScoreStatus.Normal;
                double score = 0;

                if (raw.Length == 0)
                {
                    if (status == ScoreStatus.Normal)
                    {
                        diags.Add(new ImportDiagnostic
                        {
                            Severity = ImportSeverity.Warning,
                            Row = excelRow,
                            Message = $"{name} 的「{subj}」成绩为空，已按 0 分记。若实际是缺考，请在「特殊状态」列写明。",
                        });
                    }
                }
                else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    score = v;
                }
                else if (ScoreStatusText.TryParse(raw, out var inline) && inline != ScoreStatus.Normal)
                {
                    // 直接把「缺考」写在分数格里的表也认
                    status = inline;
                }
                else
                {
                    diags.Add(new ImportDiagnostic
                    {
                        Severity = ImportSeverity.Warning,
                        Row = excelRow,
                        Message = $"{name} 的「{subj}」成绩「{raw}」不是数字，已按 0 分记。",
                    });
                }

                batch.Scores.Add(new PendingScore
                {
                    StudentNo = no,
                    ExamKey = examKey,
                    Subject = subj,
                    Score = score,
                    FullScore = settings.FullScoreOf(subj),
                    Status = status,
                });
            }

            // 年级排名（可选列）
            var gradeRankText = Cell(sheet, r, map, "年级排名");
            if (gradeRankText.Length > 0 && int.TryParse(gradeRankText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gr) && gr > 0)
                batchGradeRanks.Add(new PendingGradeRank { StudentNo = no, ExamKey = examKey, GradeRank = gr });

            okRows++;
        }

        batch.Students.AddRange(batchStudents.Values);
        batch.Exams.AddRange(batchExams.Values);
        batch.GradeRanks.AddRange(batchGradeRanks);

        if (okRows == 0)
        {
            diags.Add(new ImportDiagnostic
            {
                Severity = ImportSeverity.Error,
                Message = "表头认得出来，但没有任何一行可用数据。请检查数据行是否为空，或学号/姓名列是否填错。",
            });
            return new ImportOutcome { Success = false, Diagnostics = diags, ColumnMap = map, SheetName = sheet.Name };
        }

        diags.Add(new ImportDiagnostic
        {
            Severity = ImportSeverity.Info,
            Message = $"解析完成：{okRows} 行成绩、{batch.Students.Count} 名学生、{batch.Exams.Count} 次考试"
                      + (skippedRows > 0 ? $"，跳过 {skippedRows} 行。" : "。")
                      + (batchGradeRanks.Count == 0 ? "（表中没有「年级排名」列，可在导入后手动补填。）" : ""),
        });

        return new ImportOutcome
        {
            Success = true,
            Batch = batch,
            Diagnostics = diags,
            ColumnMap = map,
            SheetName = sheet.Name,
        };
    }

    /// <summary>在表头扫描范围内找到「认出的列最多」的一行。</summary>
    private static int FindHeaderRow(XlsxSheet sheet, string[] subjects)
    {
        int bestRow = -1, bestScore = 0;
        int limit = Math.Min(HeaderScanRows, sheet.Rows.Count);
        for (int r = 0; r < limit; r++)
        {
            var row = sheet.Rows[r];
            int score = 0;
            foreach (var cell in row)
            {
                var t = Normalize(cell);
                if (t.Length == 0) continue;
                if (MatchesAlias(t, "姓名") || MatchesAlias(t, "学号")) score += 3;
                else if (subjects.Contains(t, StringComparer.Ordinal)) score += 2;
                else if (AnyAliasKey(t) is not null) score += 1;
            }
            if (score > bestScore) { bestScore = score; bestRow = r; }
        }
        // 至少要认出「姓名/学号」之一（3 分）＋ 一个科目（2 分）
        return bestScore >= 5 ? bestRow : -1;
    }

    private static Dictionary<string, int> BuildColumnMap(XlsxSheet sheet, int headerRow, string[] subjects)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var row = sheet.Rows[headerRow];
        for (int c = 0; c < row.Length; c++)
        {
            var t = Normalize(row[c]);
            if (t.Length == 0) continue;

            if (subjects.Contains(t, StringComparer.Ordinal))
            {
                map.TryAdd(RollCall.SubjectKey(t), c); // 同名科目只认第一列
                continue;
            }
            var key = AnyAliasKey(t);
            if (key is not null) map.TryAdd(key, c);
        }
        return map;
    }

    private static string? AnyAliasKey(string normalized)
    {
        foreach (var kv in Aliases)
            if (Array.IndexOf(kv.Value, normalized) >= 0) return kv.Key;
        return null;
    }

    private static bool MatchesAlias(string normalized, string canonical)
        => Aliases.TryGetValue(canonical, out var arr) && Array.IndexOf(arr, normalized) >= 0;

    private static string Cell(XlsxSheet sheet, int row, Dictionary<string, int> map, string key)
        => map.TryGetValue(key, out var col) ? sheet.At(row, col).Trim() : "";

    /// <summary>表头归一化：去 BOM、全角空格、空白与常见分隔符。</summary>
    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim())
        {
            if (ch == '\uFEFF' || ch == '\u3000' || char.IsWhiteSpace(ch)) continue;
            if (ch == '*' || ch == '（' || ch == '）' || ch == '(' || ch == ')') continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 解析「数学缺考；英语作弊」这类特殊状态。
    /// 规则：按分隔符切段 → 每段里找**最长**的科目名前缀 → 剩下的词必须是已知状态词。
    /// 用最长前缀是为了让「物理」不会被「物」误匹配（未来的加试科目同理）。
    /// </summary>
    public static bool TryParseSpecialStatus(string text, IReadOnlyList<string> subjects, out Dictionary<string, ScoreStatus> map, out string error)
    {
        map = new Dictionary<string, ScoreStatus>(StringComparer.Ordinal);
        error = "";
        var segments = text.Split(new[] { ';', '；', ',', '，', '|', '/', '、', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawSeg in segments)
        {
            var seg = rawSeg.Trim();
            if (seg.Length == 0) continue;

            string? hit = null;
            foreach (var subj in subjects)
            {
                if (subj.Length == 0) continue;
                if (seg.StartsWith(subj, StringComparison.Ordinal) && (hit is null || subj.Length > hit.Length))
                    hit = subj;
            }
            if (hit is null) { error = $"「{seg}」里没有科目名"; return false; }

            var tail = seg[hit.Length..].Trim();
            if (!ScoreStatusText.TryParse(tail, out var st) || st == ScoreStatus.Normal)
            {
                error = $"「{seg}」里的状态「{tail}」不是缺考/免考/作弊";
                return false;
            }
            map[hit] = st;
        }
        return true;
    }

    /// <summary>日期解析：先按 Excel 序列号，再按若干中文常见格式。</summary>
    private static DateTime? ParseDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();

        if (ExcelSerialDate.LooksLikeSerial(t, out var serial))
        {
            // 1..60 也可能是「1 分」「60 分」这种数字，但日期列里出现纯数字几乎只可能是序列号
            return ExcelSerialDate.ToDate(serial);
        }

        string[] formats =
        {
            "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd", "yyyy年M月d日", "yyyy年MM月dd日",
            "MM/dd/yyyy", "M/d/yyyy", "yyyy-M-d", "yyyy/M/d",
        };
        if (DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d.Date;
        if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) return d2.Date;
        if (DateTime.TryParse(t, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d3)) return d3.Date;
        return null;
    }

    private static int ParseIntOrZero(string s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;
}

/// <summary>列键的小工具（科目列用前缀区分，避免与固定列重名）。</summary>
public static class RollCall
{
    /// <summary>科目列的前缀。**直接复用** <see cref="RankingColumns.SubjectPrefix"/> ——
    /// 两处原本各写一份字面量 <c>"subject:"</c>，改一处忘另一处会让键静默错位。</summary>
    public const string SubjectPrefix = RankingColumns.SubjectPrefix;
    public static string SubjectKey(string subject) => SubjectPrefix + subject;
}
