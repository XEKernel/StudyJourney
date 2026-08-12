using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views;

/// <summary>课表/考试编辑窗口：DataGrid 编辑 schedule.json，保存写回</summary>
public partial class ScheduleEditorWindow : Window
{
    public ScheduleEditorWindow()
    {
        InitializeComponent();
        EntryGrid.ItemsSource = App.Schedule.Data.Entries;
        ExamGrid.ItemsSource = App.Schedule.Data.Exams;
    }

    // ── 课表 ────────────────────────────────────────────────
    private void AddBtn_Click(object? sender, RoutedEventArgs e)
    {
        App.Schedule.Data.Entries.Add(new ScheduleEntry
        {
            DayOfWeek = 1,
            Period = App.Schedule.Data.Entries.Count + 1,
            Subject = "新课程",
            StartTimeStr = "08:00",
            EndTimeStr = "08:45",
            Type = PeriodType.Normal
        });
        RefreshGrid();
    }

    private void DeleteBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (EntryGrid.SelectedItem is ScheduleEntry entry)
        {
            App.Schedule.Data.Entries.Remove(entry);
            RefreshGrid();
        }
    }

    // ── 考试 ────────────────────────────────────────────────
    private void AddExamBtn_Click(object? sender, RoutedEventArgs e)
    {
        App.Schedule.Data.Exams.Add(new ExamEntry
        {
            Name = "新考试",
            DateStr = DateTime.Today.ToString("yyyy-MM-dd"),
            Subjects = new() { new ExamSubject { Name = "科目", StartTimeStr = "09:00", EndTimeStr = "11:00" } }
        });
        RefreshExamGrid();
    }

    private void DeleteExamBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (ExamGrid.SelectedItem is ExamEntry exam)
        {
            App.Schedule.Data.Exams.Remove(exam);
            RefreshExamGrid();
        }
    }

    // ── 公共 ────────────────────────────────────────────────
    private void SaveBtn_Click(object? sender, RoutedEventArgs e)
    {
        App.Schedule.Save();
        if (sender is Button btn)
        {
            var old = btn.Content;
            btn.Content = "✓ 已保存";
            btn.IsEnabled = false;
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(1200);
                btn.Content = old;
                btn.IsEnabled = true;
            });
        }
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        App.Schedule.Reload();
        RefreshGrid();
        RefreshExamGrid();
    }

    private void RefreshGrid()
    {
        EntryGrid.ItemsSource = null;
        EntryGrid.ItemsSource = App.Schedule.Data.Entries;
    }

    private void RefreshExamGrid()
    {
        ExamGrid.ItemsSource = null;
        ExamGrid.ItemsSource = App.Schedule.Data.Exams;
    }
}
