using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views;

/// <summary>课表编辑窗口：DataGrid 编辑 ScheduleData.Entries，保存写回 schedule.json</summary>
public partial class ScheduleEditorWindow : Window
{
    public ScheduleEditorWindow()
    {
        InitializeComponent();
        EntryGrid.ItemsSource = App.Schedule.Data.Entries;
    }

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
        // 撤销：重新从文件加载
        App.Schedule.Reload();
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        EntryGrid.ItemsSource = null;
        EntryGrid.ItemsSource = App.Schedule.Data.Entries;
    }
}
