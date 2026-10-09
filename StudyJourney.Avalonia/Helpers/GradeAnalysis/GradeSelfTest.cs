using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using StudyJourney.Avalonia.Models.GradeAnalysis;

namespace StudyJourney.Avalonia.Helpers.GradeAnalysis;

/// <summary>
/// 班级成绩分析自检（<c>SJ_SELFTEST=grade</c>）。
///
/// <para><b>为什么值得单独写一套</b>：这个模块最危险的地方不是崩溃，而是**算错但一切正常** ——
/// 排名算错、同分排序错、缺考的学生混进了排名、小组综合分权重不生效，都不会抛异常，
/// 老师看到的只是一个「看起来合理」的榜。所以这里对每条规则都下断言。</para>
///
/// <para>覆盖：① 总分排名与完整同分链（姓氏笔画 → 名字笔画 → 学号 → 笔画未填者垫底）；
/// ② 缺考学生总分照算但不参与排名；③ 单科排名排除特殊科目；
/// ④ 与上次考试的排名/总分变化；⑤ 年级排名与年级总人数；
/// ⑥ 小组快照解析 + 综合分权重归一化；⑦ 波动类型判定与阈值联动；
/// ⑧ **真造一个 .xlsx 走一遍导入链**（含 Excel 日期序列号与特殊状态解析）；
/// ⑨ 实例化主窗口（这是唯一能拦下 XAML/绑定写错的断言）。</para>
/// </summary>
public static class GradeSelfTest
{
    private const string P = "[GRADE]";

    public static string Run()
    {
        var sb = new StringBuilder();
        string root = Path.Combine(Path.GetTempPath(), "sj-grade-selftest");
        var dbPath = Path.Combine(root, "grades.db");

        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);

            // ══════════════════════════════════════════════════════════
            //  ① 造数据：三场对比 + 一组刻意同分的学生
            // ══════════════════════════════════════════════════════════
            var db = new Services.GradeAnalysis.GradeDatabase(dbPath);
            bool isNew = db.Initialize();
            Check(sb, isNew, "首次 Initialize 应报告新建库");
            db.Initialize(); // 幂等

            // 笔画刻意安排：王五姓 4 画 < 张三/李四 7 画；孙七刻意不填（0）→ 必须排在最后
            var students = new[]
            {
                new Student { StudentNo = "11", Name = "张三", SurnameStrokes = 7, GivenNameStrokes = 3, ClassName = "高一(1)班" },
                new Student { StudentNo = "12", Name = "李四", SurnameStrokes = 7, GivenNameStrokes = 3, ClassName = "高一(1)班" },
                new Student { StudentNo = "13", Name = "王五", SurnameStrokes = 4, GivenNameStrokes = 4, ClassName = "高一(1)班" },
                new Student { StudentNo = "14", Name = "赵六", SurnameStrokes = 9, GivenNameStrokes = 2, ClassName = "高一(1)班" },
                new Student { StudentNo = "15", Name = "孙七", SurnameStrokes = 0, GivenNameStrokes = 0, ClassName = "高一(1)班" },
            };
            var exam1 = new Exam { Name = "期中考试", ExamDate = new DateTime(2026, 5, 10) };
            var exam2 = new Exam { Name = "期末考试", ExamDate = new DateTime(2026, 6, 15), GradeTotalCount = 500 };

            var batch = new Services.GradeAnalysis.ImportBatch();
            batch.Students.AddRange(students);
            batch.Exams.Add(exam1);
            batch.Exams.Add(exam2);

            var settings = new GradeAnalysisSettings();
            var subs = settings.SubjectNames;
            double[] full = settings.Subjects.Select(x => x.FullScore).ToArray();

            // 期中：张三/李四/王五/孙七 全部 480 分（同分），赵六 420 分
            void AddScores(Exam exam, Student stu, double[] vals, ScoreStatus[]? status = null)
            {
                var key = Services.GradeAnalysis.ImportBatch.ExamKey(exam);
                for (int i = 0; i < subs.Length; i++)
                {
                    batch.Scores.Add(new Services.GradeAnalysis.PendingScore
                    {
                        StudentNo = stu.StudentNo,
                        ExamKey = key,
                        Subject = subs[i],
                        Score = vals[i],
                        FullScore = full[i],
                        Status = status?[i] ?? ScoreStatus.Normal,
                    });
                }
            }

            AddScores(exam1, students[0], new double[] { 100, 100, 100, 60, 60, 60 });
            AddScores(exam1, students[1], new double[] { 100, 100, 100, 60, 60, 60 });
            AddScores(exam1, students[2], new double[] { 100, 100, 100, 60, 60, 60 });
            AddScores(exam1, students[3], new double[] { 90, 90, 90, 50, 50, 50 });
            AddScores(exam1, students[4], new double[] { 100, 100, 100, 60, 60, 60 });

            // 期末：张三数学缺考（总分照算，但不参与排名）
            var zhangStatus = new ScoreStatus[subs.Length];
            zhangStatus[1] = ScoreStatus.Absent;
            AddScores(exam2, students[0], new double[] { 100, 0, 100, 60, 60, 60 }, zhangStatus);
            AddScores(exam2, students[1], new double[] { 120, 120, 120, 80, 80, 80 });
            AddScores(exam2, students[2], new double[] { 110, 110, 110, 70, 70, 70 });
            AddScores(exam2, students[3], new double[] { 100, 100, 100, 60, 60, 60 });
            AddScores(exam2, students[4], new double[] { 90, 90, 90, 50, 50, 50 });

            var commit = db.CommitImport(batch);
            sb.AppendLine($"{P} 入库：{commit.StudentCount} 生 / {commit.ExamCount} 考 / {commit.ScoreCount} 条成绩");
            Check(sb, commit.StudentCount == 5 && commit.ExamCount == 2 && commit.ScoreCount == 60,
                $"CommitImport 计数应为 5/2/60，实际 {commit.StudentCount}/{commit.ExamCount}/{commit.ScoreCount}");

            var loadedStudents = db.GetStudents();
            var loadedExams = db.GetExams();
            Check(sb, loadedStudents.Count == 5 && loadedExams.Count == 2, "回读学生/考试数量不对");

            // 年级排名是录入值
            var zhangId = loadedStudents.First(s => s.Name == "张三").Id;
            var liId = loadedStudents.First(s => s.Name == "李四").Id;
            var exam2Row = loadedExams.First(e => e.Name == "期末考试");
            db.SetGradeRank(liId, exam2Row.Id, 12);

            // 小组：A = 李四 + 王五；B = 赵六 + 孙七（自期中起生效）
            var exam1Row = loadedExams.First(e => e.Name == "期中考试");
            var groupA = db.UpsertGroup(new StudentGroup { Name = "第一组", Color = "#58A6FF" });
            var groupB = db.UpsertGroup(new StudentGroup { Name = "第二组", Color = "#F0883E" });
            Check(sb, groupA > 0 && groupB > 0, "小组写入失败");
            foreach (var name in new[] { "李四", "王五" })
                db.AddGroupMember(groupA, loadedStudents.First(s => s.Name == name).Id, exam1Row.Id);
            foreach (var name in new[] { "赵六", "孙七" })
                db.AddGroupMember(groupB, loadedStudents.First(s => s.Name == name).Id, exam1Row.Id);

            // ══════════════════════════════════════════════════════════
            //  ② 分析
            // ══════════════════════════════════════════════════════════
            var gradeRanks = new Dictionary<long, Dictionary<long, int>>();
            foreach (var e in loadedExams) gradeRanks[e.Id] = db.GetGradeRanks(e.Id);

            var ds = Services.GradeAnalysis.GradeDataset.Build(
                settings, loadedStudents, loadedExams, db.GetAllScores(), gradeRanks);

            var a1 = Services.GradeAnalysis.GradeAnalysisEngine.Analyze(ds, exam1Row.Id);
            var a2 = Services.GradeAnalysis.GradeAnalysisEngine.Analyze(ds, exam2Row.Id);

            // ── 同分链：王五(姓4) < 张三(姓7,名3,学号11) < 李四(姓7,名3,学号12) < 孙七(笔画未填) ──
            var order1 = string.Join(" > ", a1.Ranked.Select(r => r.Name));
            sb.AppendLine($"{P} 期中排名：{order1}");
            Check(sb, order1 == "王五 > 张三 > 李四 > 孙七 > 赵六",
                $"同分排序不符合「姓氏笔画→名字笔画→学号→未填垫底」，实际：{order1}");

            // ── 缺考：总分照算，但不参与排名 ──
            var zhang2 = a2.FindStudent(zhangId);
            Check(sb, zhang2 is not null, "期末找不到张三");
            Check(sb, zhang2!.HasSpecial && !zhang2.RankEligible, "张三数学缺考，应标记为不参与排名");
            Check(sb, Math.Abs(zhang2.TotalScore - 380) < 0.001, $"缺考学生总分仍应计算（期望 380，实际 {zhang2.TotalScore}）");
            Check(sb, zhang2.ClassRank == 0, "缺考学生不应有班级排名");
            Check(sb, a2.NotRanked.Count == 1 && a2.NotRanked[0].Name == "张三", "不参与排名的名单应只含张三");

            var order2 = string.Join(" > ", a2.Ranked.Select(r => r.Name));
            sb.AppendLine($"{P} 期末排名：{order2}（不参与：{string.Join("、", a2.NotRanked.Select(r => r.Name))}）");
            Check(sb, order2 == "李四 > 王五 > 赵六 > 孙七", $"期末排名不对，实际：{order2}");

            // ── 单科排名必须排除特殊科目 ──
            var mathRanks = a2.SubjectRanks["数学"];
            Check(sb, !mathRanks.ContainsKey(zhangId), "数学缺考的学生不应出现在单科排名里");
            Check(sb, mathRanks[liId] == 1, "李四数学 120 应为单科第 1");

            // ── 与上次对比：李四 期中第 3 → 期末第 1，进步 2 名 ──
            var li2 = a2.FindStudent(liId);
            Check(sb, li2!.ClassRankDelta == 2, $"李四班级排名应进步 2 名，实际 {li2.ClassRankDelta}");
            Check(sb, li2.TotalScoreDelta.HasValue && Math.Abs(li2.TotalScoreDelta!.Value - 120) < 0.001,
                $"李四总分应 +120，实际 {li2.TotalScoreDelta}");

            // ── 年级排名与年级总人数 ──
            Check(sb, li2.GradeRank == 12, $"李四年级排名应回读为 12，实际 {li2.GradeRank}");
            Check(sb, li2.GradeTotalCount == 500, $"年级总人数应为 500，实际 {li2.GradeTotalCount}");
            Check(sb, zhang2.GradeRank == 0, "缺考学生不应有年级排名");

            // ── 进步榜 / 退步榜 ──
            var progress = Services.GradeAnalysis.GradeAnalysisEngine.ProgressBoard(a2);
            var regress = Services.GradeAnalysis.GradeAnalysisEngine.RegressionBoard(a2);
            sb.AppendLine($"{P} 进步榜：{string.Join("、", progress.Select(r => $"{r.Name}+{r.ClassRankDelta}"))}"
                          + $"　退步榜：{string.Join("、", regress.Select(r => $"{r.Name}{r.ClassRankDelta}"))}");
            // 期中 → 期末的班级排名：李四 3→1、赵六 5→3（都进步 2 名）、王五 1→2（退步 1）、
            // 孙七 4→4（持平，两边榜都不该出现）。同进步名次时按当前班级排名排（李四在前）。
            Check(sb, progress.Count == 2 && progress[0].Name == "李四" && progress[1].Name == "赵六",
                $"进步榜应为李四与赵六（各 +2，按进步名次降序、同分按班级排名），实际：{string.Join("、", progress.Select(r => r.Name))}");
            Check(sb, regress.Count == 1 && regress[0].Name == "王五", "退步榜应只有王五（-1）");

            // ══════════════════════════════════════════════════════════
            //  ③ 小组：快照解析 + 综合分权重
            // ══════════════════════════════════════════════════════════
            var groups = db.GetGroups();
            var members = db.GetGroupMembers();
            var resolved = Services.GradeAnalysis.GradeAnalysisEngine.ResolveMembers(ds, members, exam2Row.Id);
            Check(sb, resolved.TryGetValue(groupA, out var gA) && gA.Count == 2, "第一组在期末应有 2 名成员（快照生效）");
            Check(sb, resolved.TryGetValue(groupB, out var gB) && gB.Count == 2, "第二组在期末应有 2 名成员（快照生效）");

            // 换人必须只影响之后的考试：加一条「自期末起 孙七 转入第一组」
            var sunId = loadedStudents.First(s => s.Name == "孙七").Id;
            db.AddGroupMember(groupA, sunId, exam2Row.Id);
            var members2 = db.GetGroupMembers();
            var atExam1 = Services.GradeAnalysis.GradeAnalysisEngine.ResolveMembers(ds, members2, exam1Row.Id);
            var atExam2 = Services.GradeAnalysis.GradeAnalysisEngine.ResolveMembers(ds, members2, exam2Row.Id);
            Check(sb, atExam1[groupA].Count == 2, "换人后**历史考试**的成员不应改变（快照语义）");
            Check(sb, atExam2[groupA].Count == 3, "换人后之后考试的成员应变为 3 人");

            var (wa, wp, ws) = settings.NormalizedGroupWeights();
            Check(sb, Math.Abs(wa - 0.5) < 1e-9 && Math.Abs(wp - 0.25) < 1e-9 && Math.Abs(ws - 0.25) < 1e-9,
                $"默认权重应归一化为 0.5/0.25/0.25，实际 {wa}/{wp}/{ws}");

            var gres = Services.GradeAnalysis.GradeAnalysisEngine.BuildGroupResults(ds, a2, groups, members2);
            foreach (var g in gres)
                sb.AppendLine($"{P} 小组 {g.Name}：均分 {g.AverageTotal:0.##}　进步率 {g.ProgressRate:P0}　稳定度 {g.Stability:0.0}　综合 {g.CompositeScore:0.000}");
            Check(sb, gres.Count == 2, "应有 2 个小组");
            Check(sb, gres[0].CompositeScore >= gres[1].CompositeScore, "小组应按综合分降序");
            Check(sb, gres.All(g => g.CompositeScore >= 0 && g.CompositeScore <= 1), "综合分应落在 0..1");
            Check(sb, gres[0].Name == "第一组", "第一组（含李四/王五/孙七）综合分应最高");

            // ══════════════════════════════════════════════════════════
            //  ③-2 小组的界面化增删成员（2026-10-08）
            // ══════════════════════════════════════════════════════════
            // 关键语义：换人 = **追加**快照（只影响之后的考试）；移出 = **删掉那条快照**（退回上一条的效果）。
            // 这两条是新界面的行为基础，算错的话老师会看到"历史考试成绩被改了"。
            var recsAtExam1 = Services.GradeAnalysis.GradeAnalysisEngine.ResolveMemberRecords(ds, members2, exam1Row.Id);
            var recsAtExam2 = Services.GradeAnalysis.GradeAnalysisEngine.ResolveMemberRecords(ds, members2, exam2Row.Id);
            Check(sb, recsAtExam1[groupA].Count == 2, "期中（换人前）第一组应 2 人");
            Check(sb, recsAtExam2[groupA].Count == 3, "期末（换人后）第一组应 3 人");

            var sunRec = recsAtExam2[groupA].FirstOrDefault(r => r.StudentId == sunId);
            Check(sb, sunRec is not null, "期末成员里应能取到孙七那条**快照记录本身**（界面靠它的 id 做「移出小组」）");
            Check(sb, sunRec!.EffectiveExamId == exam2Row.Id, "孙七那条快照的生效考试应为期末");

            db.DeleteGroupMember(sunRec.Id);   // 模拟界面上的「移出小组」
            var membersAfterRemove = db.GetGroupMembers();
            var recsAfterRemove = Services.GradeAnalysis.GradeAnalysisEngine.ResolveMemberRecords(
                ds, membersAfterRemove, exam2Row.Id);
            Check(sb, recsAfterRemove[groupA].Count == 2,
                "移出成员后应退回 2 人（**退回上一条记录的效果**，而不是把人永久清空）");
            sb.AppendLine($"{P} 小组换人/移出：期中 {recsAtExam1[groupA].Count} 人 → 期末 {recsAtExam2[groupA].Count} 人 → 移出后 {recsAfterRemove[groupA].Count} 人");

            // ══════════════════════════════════════════════════════════
            //  ③-3 学生删除的级联（2026-10-08）
            // ══════════════════════════════════════════════════════════
            var zhaoId = loadedStudents.First(s => s.Name == "赵六").Id;
            Check(sb, db.GetAllScores().Any(x => x.StudentId == zhaoId), "删除前赵六应有成绩");
            Check(sb, db.GetGroupMembers().Any(m => m.StudentId == zhaoId), "删除前赵六应在小组里");
            db.DeleteStudent(zhaoId);
            Check(sb, db.GetStudents().All(x => x.Id != zhaoId), "学生本身应已删除");
            Check(sb, !db.GetAllScores().Any(x => x.StudentId == zhaoId),
                "删除学生应**级联**删掉他的成绩（外键 ON DELETE CASCADE + PRAGMA foreign_keys=ON）");
            Check(sb, !db.GetGroupMembers().Any(m => m.StudentId == zhaoId), "删除学生应级联删掉他的小组成员记录");

            // ══════════════════════════════════════════════════════════
            //  ③-4 考试属性可改（原来名称打错只能把整场删掉重导）
            // ══════════════════════════════════════════════════════════
            db.UpdateExamMeta(exam1Row.Id, "期中考试（更名）", new DateTime(2026, 5, 12), 480);
            var exam1After = db.GetExams().First(e => e.Id == exam1Row.Id);
            Check(sb, exam1After.Name == "期中考试（更名）"
                      && exam1After.ExamDate == new DateTime(2026, 5, 12)
                      && exam1After.GradeTotalCount == 480,
                $"考试名称/日期/年级总人数应可修改，实际 {exam1After.Name}/{exam1After.ExamDate:yyyy-MM-dd}/{exam1After.GradeTotalCount}");

            // ══════════════════════════════════════════════════════════
            //  ④ 波动分析：阈值联动
            // ══════════════════════════════════════════════════════════
            // 李四：期中第 3 → 期末第 1，半段差 = 2，默认阈值 2 时**不**算上升（要求严格大于）
            var fluctDefault = Services.GradeAnalysis.GradeAnalysisEngine.AnalyzeFluctuation(ds, liId);
            sb.AppendLine($"{P} 李四波动（阈值 2）：{fluctDefault.Kind.ToDisplay()}，半段差 {fluctDefault.HalfDelta:0.0}，排名标准差 {fluctDefault.RankStdDev:0.00}");
            Check(sb, fluctDefault.Kind == FluctuationKind.Stable, $"默认阈值下李四应为稳定型，实际 {fluctDefault.Kind}");

            settings.HalfDeltaThreshold = 1.0;
            var fluctTight = Services.GradeAnalysis.GradeAnalysisEngine.AnalyzeFluctuation(ds, liId);
            Check(sb, fluctTight.Kind == FluctuationKind.Rising, $"阈值降到 1 后应判为上升型，实际 {fluctTight.Kind}");
            settings.HalfDeltaThreshold = 2.0;

            var fluctZhang = Services.GradeAnalysis.GradeAnalysisEngine.AnalyzeFluctuation(ds, zhangId);
            sb.AppendLine($"{P} 张三波动：{fluctZhang.Kind.ToDisplay()}，参与排名考试数 {fluctZhang.ExamCount}");
            Check(sb, fluctZhang.ExamCount == 2, "缺考的那次不应被排除在「参与过的考试」之外（总分仍算）");

            // ══════════════════════════════════════════════════════════
            //  ⑤ 真造一个 .xlsx，走一遍导入链
            // ══════════════════════════════════════════════════════════
            var xlsxPath = Path.Combine(root, "test.xlsx");
            WriteMinimalXlsx(xlsxPath);
            var outcome = Services.GradeAnalysis.GradeImporter.Parse(xlsxPath, new GradeAnalysisSettings());
            sb.AppendLine($"{P} Excel 导入：success={outcome.Success}");
            foreach (var d in outcome.Diagnostics) sb.AppendLine($"{P}   {d.Severity}: {d.ToLine()}");

            Check(sb, outcome.Success && outcome.Batch is not null, "导入应成功");
            var ib = outcome.Batch!;
            Check(sb, ib.Students.Count == 1 && ib.Students[0].StudentNo == "20260101", "应认出 1 名学生且学号保真（含前导零）");
            Check(sb, ib.Scores.Count == 6, $"应导入 6 条成绩，实际 {ib.Scores.Count}");

            var math = ib.Scores.FirstOrDefault(s => s.Subject == "数学");
            Check(sb, math is not null && math.Status == ScoreStatus.Absent && Math.Abs(math.Score) < 0.001,
                "分数格里写「缺考」应被识别为缺考状态");
            var physics = ib.Scores.FirstOrDefault(s => s.Subject == "物理");
            Check(sb, physics is not null && physics.Status == ScoreStatus.Cheating,
                "特殊状态列写「物理作弊」应作用到物理这一科");
            var english = ib.Scores.FirstOrDefault(s => s.Subject == "英语");
            Check(sb, english is not null && Math.Abs(english.Score - 110) < 0.001, "英语应为 110");
            // ⚠ 期望值要用 openpyxl 的**正向**映射（`to_excel`）来定，不能拿"1899-12-31 + 天数"
            //   线性反推 —— 那条路在 1900-03-01 之后整体差一天（2026-10-06 实测：正是这么算错了期望值）。
            Check(sb, ib.Exams.Count == 1 && ib.Exams[0].ExamDate == new DateTime(2025, 1, 21),
                $"Excel 日期序列号 45678 应还原为 2025-01-21，实际 {(ib.Exams.Count > 0 ? ib.Exams[0].ExamDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "无")}");

            // 1900 日期系统断点：直接钉死四个边界值。
            // ⚠ 这组断言是 2026-10-06 补的 —— 之前只有一条 45678 的断言，而它和实现**一起错**，
            //   互相抵消所以照样 PASS。断点两侧必须各有钉子，否则改反了看不出来。
            Check(sb, Services.GradeAnalysis.ExcelSerialDate.ToDate(1) == new DateTime(1900, 1, 1),
                $"序列号 1 应为 1900-01-01，实际 {Services.GradeAnalysis.ExcelSerialDate.ToDate(1):yyyy-MM-dd}");
            Check(sb, Services.GradeAnalysis.ExcelSerialDate.ToDate(59) == new DateTime(1900, 2, 28),
                $"序列号 59 应为 1900-02-28，实际 {Services.GradeAnalysis.ExcelSerialDate.ToDate(59):yyyy-MM-dd}");
            Check(sb, Services.GradeAnalysis.ExcelSerialDate.ToDate(61) == new DateTime(1900, 3, 1),
                $"序列号 61 应为 1900-03-01（断点另一侧），实际 {Services.GradeAnalysis.ExcelSerialDate.ToDate(61):yyyy-MM-dd}");
            Check(sb, Services.GradeAnalysis.ExcelSerialDate.ToDate(46310) == new DateTime(2026, 10, 15),
                $"序列号 46310 应为 2026-10-15，实际 {Services.GradeAnalysis.ExcelSerialDate.ToDate(46310):yyyy-MM-dd}");

            // 表头缺列 → 必须失败并给出「缺什么列」，界面据此切手动录入
            var badPath = Path.Combine(root, "bad.xlsx");
            WriteMinimalXlsx(badPath, omitSubjectHeader: true);
            var bad = Services.GradeAnalysis.GradeImporter.Parse(badPath, new GradeAnalysisSettings());
            Check(sb, !bad.Success && bad.MissingColumns.Count > 0,
                "缺科目列时必须失败并回报 MissingColumns（界面要据此切到手动录入）");
            sb.AppendLine($"{P} 缺列场景回报：{string.Join("、", bad.MissingColumns)}");

            // ══════════════════════════════════════════════════════════
            //  ⑥ 可选：再验一份**外部真实**成绩表（SJ_GRADE_XLSX=<路径>）
            // ══════════════════════════════════════════════════════════
            // 自检里用的是现场拼的最小 xlsx，只覆盖最朴素形态。真实阅卷系统导出的表带标题行、
            // 合并单元格、**真日期单元格**（落盘是序列号而不是文本）—— 结构完全不同，
            // 必须拿真表再过一遍。这个环境变量就是为此留的口子：
            //   SJ_GRADE_XLSX=".workbuddy/samples/成绩样例-期中考试.xlsx" python .workbuddy/tools/run_selftests.py grade
            var ext = Environment.GetEnvironmentVariable("SJ_GRADE_XLSX");
            if (!string.IsNullOrWhiteSpace(ext))
            {
                Check(sb, File.Exists(ext), $"SJ_GRADE_XLSX 指向的文件不存在：{ext}");
                var extOutcome = Services.GradeAnalysis.GradeImporter.Parse(ext, new GradeAnalysisSettings());
                sb.AppendLine($"{P} 外部表「{Path.GetFileName(ext)}」：success={extOutcome.Success}");
                foreach (var d in extOutcome.Diagnostics) sb.AppendLine($"{P}   {d.Severity}: {d.ToLine()}");
                Check(sb, extOutcome.Success, "外部成绩表导入失败");
                var eb = extOutcome.Batch!;
                Check(sb, eb.Students.Count > 0, "外部表没解析出学生");
                Check(sb, eb.Exams.Count > 0, "外部表没解析出考试");
                Check(sb, eb.Scores.Count == eb.Students.Count * eb.Exams.Count * new GradeAnalysisSettings().Subjects.Count,
                    $"外部表成绩行数应为 学生×考试×科目 = {eb.Students.Count}×{eb.Exams.Count}×"
                    + $"{new GradeAnalysisSettings().Subjects.Count}，实际 {eb.Scores.Count}");
                var anySpecial = eb.Scores.Count(x => x.Status != ScoreStatus.Normal);
                Check(sb, anySpecial > 0, "外部表应至少解析出一条特殊状态（缺考/免考/作弊）");
                sb.AppendLine($"{P} 外部表解析结果：{eb.Students.Count} 生 / {eb.Exams.Count} 考 / "
                              + $"{eb.Scores.Count} 条成绩，其中 {anySpecial} 条特殊状态；"
                              + $"考试日期 {string.Join("、", eb.Exams.Select(e => e.ExamDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))}");
            }

            // ══════════════════════════════════════════════════════════
            //  ⑦ 窗口实例化（唯一能拦下 XAML / 绑定写错的断言）
            // ══════════════════════════════════════════════════════════
            var win = new Views.GradeAnalysis.GradeAnalysisWindow(db);
            win.Show();

            // ⑦-1 删除考试**必须二次确认**（2026-10-09 补：此前它是唯一漏网、点了即删的破坏性操作）
            // 语义与「删学生 / 移出成员 / 删小组」一致：取消 → 一条都不删；确认 → 才真正级联删除。
            var vm = (ViewModels.GradeAnalysis.GradeAnalysisViewModel)win.DataContext!;
            vm.SelectedExam = vm.Exams.FirstOrDefault();
            Check(sb, vm.SelectedExam is not null, "自检窗口应能选中一场考试");
            long examIdBefore = vm.SelectedExam!.Id;
            int examsBefore = db.GetExams().Count;

            bool confirmAsked = false;
            vm.ConfirmAsync = (_, _) => { confirmAsked = true; return System.Threading.Tasks.Task.FromResult(false); };
            vm.DeleteExamCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Check(sb, confirmAsked, "删除考试**必须**先弹二次确认框（与删学生/移出成员/删小组一致）");
            Check(sb, db.GetExams().Any(e => e.Id == examIdBefore), "确认框选择「取消」时不应删除考试");

            vm.ConfirmAsync = (_, _) => System.Threading.Tasks.Task.FromResult(true);
            vm.DeleteExamCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Check(sb, !db.GetExams().Any(e => e.Id == examIdBefore), "确认后应真正删除该场考试");
            sb.AppendLine($"{P} 删除考试确认：考试数 {examsBefore} → {db.GetExams().Count}（取消不删 / 确认才删）");

            win.Close();
            sb.AppendLine($"{P} 主窗口实例化 + 显示 + 关闭：OK");

            sb.AppendLine($"{P} 结论：PASS");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{P} 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { /* 清理失败不影响结论 */ }
        }

        return sb.ToString();
    }

    private static void Check(StringBuilder sb, bool ok, string what)
    {
        if (ok) return;
        throw new Exception("断言失败：" + what);
    }

    /// <summary>
    /// 现场拼一个最小可用的 .xlsx（zip + 5 个 XML part）。
    /// 刻意**不用**第三方库 —— 被测的正是「不依赖反射的 xlsx 解析」这条路本身。
    /// </summary>
    private static void WriteMinimalXlsx(string path, bool omitSubjectHeader = false)
    {
        string[] shared =
        {
            "学号", "姓名", "班级", "考试名称", "考试日期", "语文", "数学", "英语", "物理", "化学", "生物", "特殊状态",
            "20260101", "张三", "高一(1)班", "期中考试", "缺考", "物理作弊",
        };

        var sst = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sst.Append($"<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"{shared.Length}\" uniqueCount=\"{shared.Length}\">");
        foreach (var s in shared) sst.Append("<si><t>").Append(Escape(s)).Append("</t></si>");
        sst.Append("</sst>");

        var headerCells = new StringBuilder();
        for (int i = 0; i < 12; i++)
        {
            if (omitSubjectHeader && i >= 5 && i <= 10) continue; // 故意挖掉六科列，验证「缺必要列」分支
            headerCells.Append($"<c r=\"{Col(i)}1\" t=\"s\"><v>{i}</v></c>");
        }

        var row2 = new StringBuilder("<row r=\"2\">");
        row2.Append($"<c r=\"A2\" t=\"s\"><v>12</v></c>");      // 学号（字符串，保前导零）
        row2.Append($"<c r=\"B2\" t=\"s\"><v>13</v></c>");      // 姓名
        row2.Append($"<c r=\"C2\" t=\"s\"><v>14</v></c>");      // 班级
        row2.Append($"<c r=\"D2\" t=\"s\"><v>15</v></c>");      // 考试名称
        row2.Append("<c r=\"E2\"><v>45678</v></c>");            // 日期 = Excel 序列号（不是文本！）
        row2.Append("<c r=\"F2\"><v>120</v></c>");              // 语文
        row2.Append($"<c r=\"G2\" t=\"s\"><v>16</v></c>");      // 数学：分数格里直接写「缺考」
        row2.Append("<c r=\"H2\"><v>110</v></c>");              // 英语
        row2.Append("<c r=\"I2\"><v>80</v></c>");               // 物理（分数正常，状态在特殊状态列里）
        row2.Append("<c r=\"J2\"><v>70</v></c>");               // 化学
        row2.Append("<c r=\"K2\"><v>60</v></c>");               // 生物
        row2.Append($"<c r=\"L2\" t=\"s\"><v>17</v></c>");      // 特殊状态：物理作弊
        row2.Append("</row>");

        // 故意把 G2 的「缺考」写成 inlineStr 形式，覆盖第二种字符串编码
        row2.Replace("<c r=\"G2\" t=\"s\"><v>16</v></c>", "<c r=\"G2\" t=\"inlineStr\"><is><t>缺考</t></is></c>");

        string sheet = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            $"<sheetData><row r=\"1\">{headerCells}</row>{row2}</sheetData></worksheet>";

        string workbook = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"成绩\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

        string wbRels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "</Relationships>";

        string rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        string types = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
            "</Types>";

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            using var s = entry.Open();
            using var w = new StreamWriter(s, new UTF8Encoding(false));
            w.Write(content);
        }
        Add("[Content_Types].xml", types);
        Add("_rels/.rels", rels);
        Add("xl/workbook.xml", workbook);
        Add("xl/_rels/workbook.xml.rels", wbRels);
        Add("xl/sharedStrings.xml", sst.ToString());
        Add("xl/worksheets/sheet1.xml", sheet);
    }

    private static string Col(int index)
    {
        // 0 → A, 25 → Z, 26 → AA
        var s = "";
        int n = index;
        while (true)
        {
            s = (char)('A' + n % 26) + s;
            n = n / 26 - 1;
            if (n < 0) break;
        }
        return s;
    }

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
