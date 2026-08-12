using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MessageBox = GaokaoCountdown.Views.DialogHelper;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;


using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
namespace GaokaoCountdown.Views
{
    public partial class SettingWindow
    {
        // ══════════════════════════════════════════════════════
        //  课表 Tab 事件处理
        // ══════════════════════════════════════════════════════

        private void BrowseReminderSound_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "音频文件|*.wav;*.mp3|所有文件|*.*",
                Title = "选择提醒声音文件"
            };
            if (dlg.ShowDialog() == true)
                ReminderSoundPathBox.Text = dlg.FileName;
        }

        /// <summary>课表栏透明度滑杆：拖动时实时刷新百分比标签</summary>
        private void ScheduleBarOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ScheduleBarOpacityLabel != null)
                ScheduleBarOpacityLabel.Text = $"{ScheduleBarOpacitySlider.Value * 100:F0}%";
        }

        private void ImportScheduleJson_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON 文件|*.json|所有文件|*.*",
                Title = "导入课表 JSON"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var json = System.IO.File.ReadAllText(dlg.FileName);
                var sm = _mainWindow.GetScheduleManager();
                if (sm == null) { ScheduleStatusTb.Text = "课表服务未初始化"; return; }
                var (ok, msg) = sm.ImportFromJson(json);
                ScheduleStatusTb.Text = msg;
                if (ok)
                {
                    RefreshTimeTemplate();
                    RefreshTimetable();
                    var today = sm.GetTodayEntries();
                    ExamStatusTb.Text = $"考试记录：{sm.Data.Exams.Count} 场";
                }
            }
            catch (Exception ex)
            {
                ScheduleStatusTb.Text = $"读取文件失败：{ex.Message}";
            }
        }

        private void ExportScheduleJson_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON 文件|*.json",
                FileName = "schedule.json",
                Title = "导出课表 JSON"
            };
            if (dlg.ShowDialog() != true) return;
            var sm = _mainWindow.GetScheduleManager();
            if (sm == null) return;
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            var json = System.Text.Json.JsonSerializer.Serialize(sm.Data, opts);
            System.IO.File.WriteAllText(dlg.FileName, json);
            ScheduleStatusTb.Text = $"已导出到 {dlg.FileName}";
        }

        // ══════════════════════════════════════════════════════
        //  考试模式 Tab 事件处理
        // ══════════════════════════════════════════════════════

        private void ImportExamJson_Click(object sender, RoutedEventArgs e)
        {
            // 复用 ImportScheduleJson（考试数据在同一 schedule.json 中）
            ImportScheduleJson_Click(sender, e);
            var sm = _mainWindow.GetScheduleManager();
            if (sm != null)
                ExamStatusTb.Text = $"已加载 {sm.Data.Exams.Count} 场考试";
        }

        private void EnterExamMode_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.EnterExamMode();
        }

        private void ExitExamMode_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.ExitExamMode();
        }

        private static readonly Dictionary<string, PeriodType> _periodTypes = new()
        {
            { "普通课", PeriodType.Normal },
            { "早自习", PeriodType.Morning },
            { "晚自习", PeriodType.Evening },
            { "晚读",   PeriodType.Reading },
            { "午休",   PeriodType.Noon },
        };

        private static readonly string[] _dayNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

        /// <summary>从 Entries 构建课程表网格行列表</summary>
        private List<TimetableRow> BuildTimetableRows()
        {
            var sm = _mainWindow.GetScheduleManager();
            var entries = sm?.Data?.Entries ?? new();
            var temps   = sm?.Data?.TimeTemplates ?? new();

            // 如果有时段模板，用它；否则从 entries 推算
            var slots = temps.Count > 0
                ? temps.Select(t => (Period: t.Period, Start: t.StartTime, End: t.EndTime, Type: t.Type)).ToList()
                : entries.GroupBy(e => (e.Period, e.StartTimeStr, e.EndTimeStr, e.Type))
                         .Select(g => (Period: g.Key.Period, Start: g.Key.StartTimeStr, End: g.Key.EndTimeStr, Type: g.Key.Type))
                         .OrderBy(x => x.Period).ToList();

            var rows = new List<TimetableRow>();
            foreach (var (period, start, end, type) in slots)
            {
                var row = new TimetableRow
                {
                    TimeLabel = type switch
                    {
                        PeriodType.Morning => $"早 {start}-{end}",
                        PeriodType.Evening => $"晚 {start}-{end}",
                        PeriodType.Reading => $"读 {start}-{end}",
                        PeriodType.Noon    => $"午 {start}-{end}",
                        _                  => $"第{period}节 {start}-{end}"
                    }
                };
                for (int d = 0; d < 7; d++)
                    row[d] = entries.FirstOrDefault(e => e.DayOfWeek == d + 1 && e.Period == period)?.Subject ?? "";
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>从课程表网格回写 Entries</summary>
        private void SaveTimetableToEntries(List<TimetableRow> rows)
        {
            var sm = _mainWindow.GetScheduleManager();
            if (sm?.Data == null) return;
            sm.Data.Entries.Clear();

            var temps = sm.Data.TimeTemplates;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                // 解析时段信息
                var slot = i < temps.Count
                    ? (Period: temps[i].Period, StartTime: temps[i].StartTime, EndTime: temps[i].EndTime, Type: temps[i].Type)
                    : (Period: i + 1, StartTime: "08:00", EndTime: "08:45", Type: PeriodType.Normal);

                for (int d = 0; d < 7; d++)
                {
                    var subj = row[d]?.Trim();
                    if (string.IsNullOrEmpty(subj)) continue;
                    sm.Data.Entries.Add(new ScheduleEntry
                    {
                        DayOfWeek = d + 1,
                        Period = slot.Period,
                        Subject = subj,
                        StartTimeStr = slot.StartTime,
                        EndTimeStr = slot.EndTime,
                        Type = slot.Type
                    });
                }
            }
            sm.Data.SortEntries();
            sm.Save();
        }

        private void RefreshTimetable()
        {
            TimetableGrid.ItemsSource = null;
            TimetableGrid.ItemsSource = BuildTimetableRows();
            RefreshTimetableStatus();
        }

        private void RefreshTimetableStatus()
        {
            var sm = _mainWindow.GetScheduleManager();
            if (sm != null)
            {
                var today = sm.GetTodayEntries();
                ScheduleStatusTb.Text = $"已加载 {sm.Data.Entries.Count} 节课 / 今日 {today.Count} 节课 / {sm.Data.Exams.Count} 场考试";
            }
        }

        /// <summary>填充时段模板 DataGrid 的 ComboBox 列</summary>
        private void PopulateTimeTemplateCombo()
        {
            var typeCol = TimeTemplateGrid.Columns[3] as DataGridComboBoxColumn;
            if (typeCol != null) typeCol.ItemsSource = _periodTypes.ToList();
        }

        private void RefreshTimeTemplate()
        {
            TimeTemplateGrid.ItemsSource = null;
            var sm = _mainWindow.GetScheduleManager();
            if (sm?.Data?.TimeTemplates == null) return;
            TimeTemplateGrid.ItemsSource = sm.Data.TimeTemplates;
        }

        private void AddTimeSlot_Click(object sender, RoutedEventArgs e)
        {
            var sm = _mainWindow.GetScheduleManager();
            if (sm?.Data == null) return;
            int nextP = sm.Data.TimeTemplates.Count > 0
                ? sm.Data.TimeTemplates[^1].Period + 1 : 1;
            string start = "08:00", end = "08:45";
            if (sm.Data.TimeTemplates.Count > 0)
            {
                if (TimeSpan.TryParse(sm.Data.TimeTemplates[^1].EndTime, out var lastEnd))
                {
                    var ns = lastEnd.Add(TimeSpan.FromMinutes(5));
                    start = $"{ns.Hours:D2}:{ns.Minutes:D2}";
                    end   = $"{ns.Add(TimeSpan.FromMinutes(40)).Hours:D2}:{ns.Add(TimeSpan.FromMinutes(40)).Minutes:D2}";
                }
            }
            sm.Data.TimeTemplates.Add(new TimeTemplate { Period = nextP, StartTime = start, EndTime = end });
            sm.Save();
            RefreshTimeTemplate();
        }

        private void DeleteTimeSlot_Click(object sender, RoutedEventArgs e)
        {
            if (TimeTemplateGrid.SelectedItem is not TimeTemplate t) return;
            var r = MessageBox.Show($"确定删除「{t.Label}」吗？", "删除时段", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            var sm = _mainWindow.GetScheduleManager();
            sm?.Data?.TimeTemplates.Remove(t);
            sm?.Save();
            RefreshTimeTemplate();
        }

        private void ApplyTimeTemplate_Click(object sender, RoutedEventArgs e)
        {
            var sm = _mainWindow.GetScheduleManager();
            if (sm?.Data?.TimeTemplates == null || sm.Data.TimeTemplates.Count == 0) return;
            sm.Save();
            RefreshTimetable();
            ScheduleStatusTb.Text += "  ✅ 已应用时段模板";
        }

        private void ApplyShiftRest_Click(object sender, RoutedEventArgs e)
        {
            int from = AdjustFromDay.SelectedIndex; // 0=周一..6=周日
            int to   = AdjustToDay.SelectedIndex;
            if (from == to) return;

            var r = MessageBox.Show(
                $"确定将{_dayNames[from]}的课程复制到{_dayNames[to]}吗？",
                "调休确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            if (TimetableGrid.ItemsSource is not List<TimetableRow> rows) return;
            foreach (var row in rows)
                row[to] = row[from];
            TimetableGrid.ItemsSource = null;
            TimetableGrid.ItemsSource = rows;
            ScheduleStatusTb.Text = $"  ✅ 已从{_dayNames[from]}调休至{_dayNames[to]}";
        }

        // ══════════════════════════════════════════════════════
        //  调课 — 点击课程表格子选择源/目标
        // ══════════════════════════════════════════════════════

        private CourseSlot? _swapSource;
        private CourseSlot? _swapTarget;

        /// <summary>获取指定单元格的课程槽位</summary>
        private CourseSlot? GetSlotAt(int rowIndex, int colIndex)
        {
            if (TimetableGrid.ItemsSource is not List<TimetableRow> rows) return null;
            if (rowIndex < 0 || rowIndex >= rows.Count) return null;
            int dayIndex = colIndex - 1; // col 0 = 时段, col 1..7 = 周一..周日
            if (dayIndex < 0 || dayIndex >= 7) return null;
            return new CourseSlot
            {
                RowIndex = rowIndex,
                DayIndex = dayIndex,
                Subject = rows[rowIndex][dayIndex],
                TimeLabel = rows[rowIndex].TimeLabel,
                DayName = _dayNames[dayIndex]
            };
        }

        /// <summary>点击表格格子选择源或目标</summary>
        private void TimetableGrid_SelectedCellsChanged(object sender, System.Windows.Controls.SelectedCellsChangedEventArgs e)
        {
            if (TimetableGrid.SelectedCells.Count == 0) return;
            var cell = TimetableGrid.SelectedCells[0];
            var slot = GetSlotAt(
                TimetableGrid.Items.IndexOf(cell.Item),
                cell.Column?.DisplayIndex ?? 0);
            if (slot == null) return;

            if (_swapSource == null)
            {
                _swapSource = slot;
                SwapSourceLb.Text = $"源：{slot.Display}";
                SwapTargetLb.Text = "目标：未选择";
                SwapHintTb.Text = $"已选源「{slot.Subject}」→ 再点一个格子选目标";
            }
            else
            {
                _swapTarget = slot;
                SwapTargetLb.Text = $"目标：{slot.Display}";
                SwapHintTb.Text = !slot.IsEmpty
                    ? $"源「{_swapSource.Subject}」→ 目标「{slot.Subject}」— 点按钮执行"
                    : $"源「{_swapSource.Subject}」→ 目标空位 — 点按钮执行";
            }
        }

        private void ClearSwapSelection_Click(object sender, RoutedEventArgs e)
        {
            _swapSource = null;
            _swapTarget = null;
            SwapSourceLb.Text = "源：未选择";
            SwapTargetLb.Text = "目标：未选择";
            SwapHintTb.Text = "已清除 — 点击格子重新选择";
        }

        /// <summary>交换两节课</summary>
        private void SwapCourses_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateSwapSelection()) return;

            if (_swapSource!.IsEmpty && _swapTarget!.IsEmpty)
            {
                SwapHintTb.Text = "⚠ 两个位置都是空的，无需交换";
                return;
            }

            var r = MessageBox.Show(
                $"交换「{_swapSource!.Display}」↔「{_swapTarget!.Display}」？",
                "调课·交换", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            if (TimetableGrid.ItemsSource is not List<TimetableRow> rows) return;
            string tmp = rows[_swapSource.RowIndex][_swapSource.DayIndex];
            rows[_swapSource.RowIndex][_swapSource.DayIndex] = rows[_swapTarget.RowIndex][_swapTarget.DayIndex];
            rows[_swapTarget.RowIndex][_swapTarget.DayIndex] = tmp;
            RefreshTimetable();
            SaveTimetableToEntries(rows);
            ClearSwapSelection();
            ScheduleStatusTb.Text = $"  ✅ 已交换";
        }

        /// <summary>移动课程到目标位置（源清空，目标原有内容丢失）</summary>
        private void MoveCourse_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateSwapSelection()) return;

            if (_swapSource!.IsEmpty)
            {
                SwapHintTb.Text = "⚠ 源位置是空的，请选有课程的位置";
                return;
            }

            string warn = !_swapTarget!.IsEmpty
                ? $"\n\n目标「{_swapTarget.Display}」将被覆盖！"
                : "";

            var r = MessageBox.Show(
                $"将「{_swapSource.Display}」移动到「{_swapTarget.Display}」？{warn}",
                "调课·移动", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            if (TimetableGrid.ItemsSource is not List<TimetableRow> rows) return;
            rows[_swapTarget.RowIndex][_swapTarget.DayIndex] = rows[_swapSource.RowIndex][_swapSource.DayIndex];
            rows[_swapSource.RowIndex][_swapSource.DayIndex] = "";
            RefreshTimetable();
            SaveTimetableToEntries(rows);
            ClearSwapSelection();
            ScheduleStatusTb.Text = $"  ✅ 已移动";
        }

        /// <summary>代课/替换：目标被源课程覆盖，源不变</summary>
        private void SubstituteCourse_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateSwapSelection()) return;

            if (_swapSource!.IsEmpty)
            {
                SwapHintTb.Text = "⚠ 请选有课程的位置作为来源";
                return;
            }

            string info = _swapTarget!.IsEmpty
                ? $"由「{_swapSource.Subject}」代课"
                : $"「{_swapSource.Subject}」代课，原「{_swapTarget.Subject}」取消";

            var r = MessageBox.Show(
                $"{_swapSource.DayName} {_swapSource.TimeLabel} 的「{_swapSource.Subject}」老师\n到 {_swapTarget.DayName} {_swapTarget.TimeLabel} 代课？\n\n{info}",
                "调课·代课", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            if (TimetableGrid.ItemsSource is not List<TimetableRow> rows) return;
            rows[_swapTarget.RowIndex][_swapTarget.DayIndex] = _swapSource.Subject;
            RefreshTimetable();
            SaveTimetableToEntries(rows);
            ClearSwapSelection();
            ScheduleStatusTb.Text = $"  ✅ 「{_swapSource.Subject}」代课到「{_swapTarget.DayName} {_swapTarget.TimeLabel}」";
        }

        private bool ValidateSwapSelection()
        {
            if (_swapSource == null || _swapTarget == null)
            {
                SwapHintTb.Text = "⚠ 先在课程表上点一个格子选源，再点一个格子选目标";
                return false;
            }
            if (_swapSource.RowIndex == _swapTarget.RowIndex && _swapSource.DayIndex == _swapTarget.DayIndex)
            {
                SwapHintTb.Text = "⚠ 源和目标不能相同";
                return false;
            }
            return true;
        }

        private void ClearSwapSelection()
        {
            _swapSource = null;
            _swapTarget = null;
            SwapSourceLb.Text = "源：未选择";
            SwapTargetLb.Text = "目标：未选择";
        }

        private void SaveSchedule_Click(object sender, RoutedEventArgs e)
        {
            TimetableGrid.CommitEdit(DataGridEditingUnit.Row, true);
            if (TimetableGrid.ItemsSource is List<TimetableRow> rows)
                SaveTimetableToEntries(rows);
            RefreshTimetableStatus();
            ScheduleStatusTb.Text += "  ✅ 已保存";
        }

        // ══════════════════════════════════════════════════════
        //  考试 DataGrid 直编辑
        // ══════════════════════════════════════════════════════

        /// <summary>刷新考试 DataGrid</summary>
        private void RefreshExamGrid()
        {
            try
            {
                // 提交所有待编辑，防止状态不一致崩溃
                ExamDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
                ExamSubjectGrid.CommitEdit(DataGridEditingUnit.Row, true);

                var sm = _mainWindow.GetScheduleManager();
                if (sm?.Data?.Exams == null)
                {
                    ExamDataGrid.ItemsSource = null;
                    ExamSubjectGrid.ItemsSource = null;
                    return;
                }
                ExamDataGrid.ItemsSource = null;
                ExamDataGrid.ItemsSource = sm.Data.Exams;
                // 自动选中第一场考试，避免科目表空白
                if (sm.Data.Exams.Count > 0 && ExamDataGrid.SelectedIndex < 0)
                {
                    ExamDataGrid.SelectedIndex = 0;
                    ExamSubjectGrid.ItemsSource = sm.Data.Exams[0].Subjects;
                    ExamSubjectGrid.Visibility = System.Windows.Visibility.Visible;
                    NoExamSelectedHint.Visibility = System.Windows.Visibility.Collapsed;
                }
                else if (sm.Data.Exams.Count == 0)
                {
                    ExamSubjectGrid.ItemsSource = null;
                    ExamSubjectGrid.Visibility = System.Windows.Visibility.Collapsed;
                    NoExamSelectedHint.Visibility = System.Windows.Visibility.Visible;
                }
                else
                {
                    ExamSubjectGrid.ItemsSource = null;
                }
                RefreshExamStatus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshExamGrid error: {ex.Message}");
            }
        }

        private void RefreshExamStatus()
        {
            var sm = _mainWindow.GetScheduleManager();
            if (sm != null)
                ExamStatusTb.Text = $"已加载 {sm.Data.Exams.Count} 场考试";
        }

        /// <summary>选中考试时联动展示其科目列表</summary>
        private void ExamDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ExamDataGrid.SelectedItem is ExamEntry exam)
            {
                ExamSubjectGrid.ItemsSource = exam.Subjects;
                ExamSubjectGrid.Visibility = System.Windows.Visibility.Visible;
                NoExamSelectedHint.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                ExamSubjectGrid.ItemsSource = null;
                ExamSubjectGrid.Visibility = System.Windows.Visibility.Collapsed;
                NoExamSelectedHint.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void AddExam_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 提交可能存在的待编辑
                ExamDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

                var sm = _mainWindow.GetScheduleManager();
                if (sm?.Data == null) return;
                sm.Data.Exams.Add(new ExamEntry { Name = "新考试", DateStr = DateTime.Today.ToString("yyyy-MM-dd") });
                RefreshExamGrid();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddExam error: {ex.Message}");
            }
        }

        private void DeleteExam_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ExamDataGrid.SelectedItem is not ExamEntry exam) return;

                var r = MessageBox.Show(
                    $"确定要删除考试「{exam.Name}」及其所有科目吗？",
                    "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;

                ExamDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

                var sm = _mainWindow.GetScheduleManager();
                sm?.Data?.Exams.Remove(exam);
                RefreshExamGrid();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DeleteExam error: {ex.Message}");
            }
        }

        private void AddExamSubject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ExamDataGrid.SelectedItem is not ExamEntry exam) return;

                // 提交可能存在的待编辑，防止崩溃
                ExamSubjectGrid.CommitEdit(DataGridEditingUnit.Row, true);

                // ObservableCollection 自动通知 DataGrid 刷新，无需手动重置 ItemsSource
                exam.Subjects.Add(new ExamSubject { Name = "新科目", StartTimeStr = "09:00", EndTimeStr = "11:00" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddExamSubject error: {ex.Message}");
            }
        }

        private void DeleteExamSubject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ExamDataGrid.SelectedItem is not ExamEntry exam) return;
                if (ExamSubjectGrid.SelectedItem is not ExamSubject sub) return;

                var r = MessageBox.Show(
                    $"确定要删除科目「{sub.Name}」吗？",
                    "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;

                ExamSubjectGrid.CommitEdit(DataGridEditingUnit.Row, true);

                exam.Subjects.Remove(sub);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DeleteExamSubject error: {ex.Message}");
            }
        }

        private void SaveExams_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 提交所有待编辑，确保最新修改被保存
                ExamDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
                ExamSubjectGrid.CommitEdit(DataGridEditingUnit.Row, true);

                var sm = _mainWindow.GetScheduleManager();
                sm?.Save();
                RefreshExamStatus();
                ExamStatusTb.Text += "  ✅ 已保存";
            }
            catch (Exception ex)
            {
                ExamStatusTb.Text = $"保存失败：{ex.Message}";
            }
        }
    }

    public class FontFamilyItem
    {
        public FontFamily FontFamily { get; }
        public FontFamilyItem(FontFamily ff) => FontFamily = ff;
        public override string ToString() => FontFamily.Source;
    }
}
