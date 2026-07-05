using System.Windows;
using System.Windows.Media;

namespace GaokaoCountdown
{
    /// <summary>自定义对话框辅助类：颜色选择器 + 消息框（暗色主题，居中于父窗口）</summary>
    public static class DialogHelper
    {
        /// <summary>自动获取主窗口</summary>
        private static Window? GetOwner()
        {
            foreach (Window w in Application.Current.Windows)
                if (w is SettingWindow || w is MainWindow) return w;
            return Application.Current.MainWindow;
        }

        /// <summary>弹出颜色选择对话框，返回选择结果。</summary>
        /// <param name="owner">父窗口（用于居中定位）</param>
        /// <param name="initialColor">初始颜色</param>
        /// <returns>用户选择的颜色，取消返回 null</returns>
        public static Color? ShowColorPicker(Window owner, string initialColor)
        {
            var dlg = new ColorPickerDialog(initialColor) { Owner = owner };
            dlg.ShowDialog();
            return dlg.SelectedColor;
        }

        /// <summary>显示信息对话框</summary>
        public static void ShowInfo(Window owner, string message, string title = "学程")
        {
            var dlg = new ThemedMessageBox(title, message, ThemeMessageBoxButton.OK) { Owner = owner };
            dlg.ShowDialog();
        }

        /// <summary>显示警告对话框</summary>
        public static void ShowWarning(Window owner, string message, string title = "学程")
        {
            var dlg = new ThemedMessageBox(title, message, ThemeMessageBoxButton.OK, ThemeMessageBoxIcon.Warning) { Owner = owner };
            dlg.ShowDialog();
        }

        /// <summary>显示错误对话框</summary>
        public static void ShowError(Window owner, string message, string title = "学程")
        {
            var dlg = new ThemedMessageBox(title, message, ThemeMessageBoxButton.OK, ThemeMessageBoxIcon.Error) { Owner = owner };
            dlg.ShowDialog();
        }

        /// <summary>显示是/否对话框，返回用户选择</summary>
        public static bool ShowYesNo(Window owner, string message, string title = "学程")
        {
            var dlg = new ThemedMessageBox(title, message, ThemeMessageBoxButton.YesNo, ThemeMessageBoxIcon.Question) { Owner = owner };
            dlg.ShowDialog();
            return dlg.Result == true;
        }

        /// <summary>显示是/否/取消对话框，返回用户选择</summary>
        public static ThemeMessageBoxResult ShowYesNoCancel(Window owner, string message, string title = "学程")
        {
            var dlg = new ThemedMessageBox(title, message, ThemeMessageBoxButton.YesNoCancel, ThemeMessageBoxIcon.Question) { Owner = owner };
            dlg.ShowDialog();
            return dlg.Result switch { true => ThemeMessageBoxResult.Yes, false => ThemeMessageBoxResult.No, _ => ThemeMessageBoxResult.Cancel };
        }

        // ── 自动查找父窗口的重载 ──

        public static void ShowInfo(string message, string title = "学程") => ShowInfo(GetOwner()!, message, title);
        public static void ShowWarning(string message, string title = "学程") => ShowWarning(GetOwner()!, message, title);
        public static void ShowError(string message, string title = "学程") => ShowError(GetOwner()!, message, title);
        public static bool ShowYesNo(string message, string title = "学程") => ShowYesNo(GetOwner()!, message, title);
        public static Color? ShowColorPicker(string initial) => ShowColorPicker(GetOwner()!, initial);

        // ── 兼容传统 MessageBox.Show 调用签名 ──
        /// <summary>信息 / 警告：兼容 MessageBox.Show(message, caption) 签名</summary>
        public static MessageBoxResult Show(string message, string caption)
        {
            ShowInfo(GetOwner()!, message, caption);
            return MessageBoxResult.OK;
        }

        /// <summary>是/否：兼容 MessageBox.Show(message, caption, button, image) 签名</summary>
        public static MessageBoxResult Show(string message, string caption, MessageBoxButton button, MessageBoxImage image)
        {
            if (button == MessageBoxButton.YesNo)
                return ShowYesNo(GetOwner()!, message, caption) ? MessageBoxResult.Yes : MessageBoxResult.No;
            ShowInfo(GetOwner()!, message, caption);
            return MessageBoxResult.OK;
        }
    }
}
