using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace GaokaoCountdown
{
    public partial class BatchInputDialog : Window
    {
        public string CourseName => CourseNameBox.Text.Trim();
        public string StartTime => StartTimeBox.Text.Trim();
        public string EndTime => EndTimeBox.Text.Trim();
        public PeriodType Type
        {
            get
            {
                var sel = PeriodTypeCombo.SelectedItem as ComboBoxItem;
                return sel?.Tag?.ToString() switch
                {
                    "Morning" => PeriodType.Morning,
                    "Evening" => PeriodType.Evening,
                    "Reading" => PeriodType.Reading,
                    "Noon" => PeriodType.Noon,
                    _ => PeriodType.Normal
                };
            }
        }

        public List<int> SelectedDays
        {
            get
            {
                var days = new List<int>();
                if (ChkMon.IsChecked == true) days.Add(1);
                if (ChkTue.IsChecked == true) days.Add(2);
                if (ChkWed.IsChecked == true) days.Add(3);
                if (ChkThu.IsChecked == true) days.Add(4);
                if (ChkFri.IsChecked == true) days.Add(5);
                if (ChkSat.IsChecked == true) days.Add(6);
                if (ChkSun.IsChecked == true) days.Add(7);
                return days;
            }
        }

        public BatchInputDialog()
        {
            InitializeComponent();
            // 默认选中工作日
            ChkMon.IsChecked = true;
            ChkTue.IsChecked = true;
            ChkWed.IsChecked = true;
            ChkThu.IsChecked = true;
            ChkFri.IsChecked = true;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CourseName))
            {
                MessageBox.Show("请输入课程名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (SelectedDays.Count == 0)
            {
                MessageBox.Show("请至少选择一个星期。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
