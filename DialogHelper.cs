using System;
using System.Windows;
using System.Windows.Media;

namespace GaokaoCountdown
{
    /// <summary>自定义对话框：使用全屏遮罩窗口，内容居中显示（暗色主题）</summary>
    public static class DialogHelper
    {
        /// <summary>弹出颜色选择，返回选择结果</summary>
        public static Color? ShowColorPicker(string initialColor)
        {
            Color? result = null;
            ColorPickerControl picker = null!;
            var overlay = new DialogOverlayWindow();
            picker = new ColorPickerControl(initialColor, () =>
            {
                result = picker.SelectedColor;
                overlay.CloseWithFade();
            });
            overlay.SetContent(picker);
            overlay.ShowDialog();
            return result;
        }

        /// <summary>显示信息对话框</summary>
        public static void ShowInfo(string message, string title = "学程")
        {
            ShowMsg(title, message, ThemeMessageBoxButton.OK, ThemeMessageBoxIcon.Info);
        }

        /// <summary>显示警告对话框</summary>
        public static void ShowWarning(string message, string title = "学程")
        {
            ShowMsg(title, message, ThemeMessageBoxButton.OK, ThemeMessageBoxIcon.Warning);
        }

        /// <summary>显示错误对话框</summary>
        public static void ShowError(string message, string title = "学程")
        {
            ShowMsg(title, message, ThemeMessageBoxButton.OK, ThemeMessageBoxIcon.Error);
        }

        /// <summary>显示是/否对话框，返回 true=是</summary>
        public static bool ShowYesNo(string message, string title = "学程")
        {
            bool? res = null;
            MessageBoxControl mb = null!;
            var overlay = new DialogOverlayWindow();
            mb = new MessageBoxControl(title, message, ThemeMessageBoxButton.YesNo, ThemeMessageBoxIcon.Question, () =>
            {
                res = mb.Result;
                overlay.CloseWithFade();
            });
            overlay.SetContent(mb);
            overlay.ShowDialog();
            return res == true;
        }

        private static void ShowMsg(string title, string message, ThemeMessageBoxButton btns, ThemeMessageBoxIcon icon)
        {
            var overlay = new DialogOverlayWindow();
            var mb = new MessageBoxControl(title, message, btns, icon, () => overlay.CloseWithFade());
            overlay.SetContent(mb);
            overlay.ShowDialog();
        }

        // ── 兼容传统 MessageBox.Show 签名 ──
        public static MessageBoxResult Show(string message, string caption)
        {
            ShowInfo(message, caption);
            return MessageBoxResult.OK;
        }

        public static MessageBoxResult Show(string message, string caption, MessageBoxButton button, MessageBoxImage image)
        {
            if (button == MessageBoxButton.YesNo)
                return ShowYesNo(message, caption) ? MessageBoxResult.Yes : MessageBoxResult.No;
            ShowInfo(message, caption);
            return MessageBoxResult.OK;
        }
    }
}
