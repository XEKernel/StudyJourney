using System;
using System.Windows;
using System.Windows.Media;

using GaokaoCountdown.Helpers;
using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
namespace GaokaoCountdown.Views
{
    /// <summary>自定义对话框：居中于当前活动窗口，暗色主题</summary>
    public static class DialogHelper
    {
        private static Window? GetOwner()
        {
            foreach (Window w in Application.Current.Windows)
                if (w is SettingWindow && w.IsVisible) return w;
            foreach (Window w in Application.Current.Windows)
                if (w is MainWindow && w.IsVisible) return w;
            return Application.Current.MainWindow;
        }

        private static void ShowDialogCore(DialogOverlayWindow overlay)
        {
            overlay.Owner = GetOwner();
            overlay.ShowDialog();
        }

        /// <summary>弹出颜色选择，返回选择结果</summary>
        public static Color? ShowColorPicker(string initialColor)
        {
            Color? result = null;
            var overlay = new DialogOverlayWindow();
            var picker = new ColorPickerControl(initialColor, () =>
            {
                // 闭包延迟执行：点击确定时才读取（此时控件已挂载到 overlay）
                result = (overlay.ContentHost.Content as ColorPickerControl)?.SelectedColor;
                overlay.CloseWithFade();
            });
            overlay.SetContent(picker);
            ShowDialogCore(overlay);
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
            var overlay = new DialogOverlayWindow();
            var mb = new MessageBoxControl(title, message, ThemeMessageBoxButton.YesNo, ThemeMessageBoxIcon.Question, () =>
            {
                res = (overlay.ContentHost.Content as MessageBoxControl)?.Result;
                overlay.CloseWithFade();
            });
            overlay.SetContent(mb);
            ShowDialogCore(overlay);
            return res == true;
        }

        private static void ShowMsg(string title, string message, ThemeMessageBoxButton btns, ThemeMessageBoxIcon icon)
        {
            var overlay = new DialogOverlayWindow();
            var mb = new MessageBoxControl(title, message, btns, icon, () => overlay.CloseWithFade());
            overlay.SetContent(mb);
            ShowDialogCore(overlay);
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
            if (button == MessageBoxButton.OKCancel)
            {
                // OKCancel map to YesNo — Cancel = null/close returns No
                bool? okRes = null;
                var overlay = new DialogOverlayWindow();
                var themeIcon = ThemeMessageBoxIcon.Question;
                if (image == MessageBoxImage.Error) themeIcon = ThemeMessageBoxIcon.Error;
                else if (image == MessageBoxImage.Warning) themeIcon = ThemeMessageBoxIcon.Warning;
                else if (image == MessageBoxImage.Question) themeIcon = ThemeMessageBoxIcon.Question;
                else if (image == MessageBoxImage.Information) themeIcon = ThemeMessageBoxIcon.Info;
                var mb = new MessageBoxControl(caption, message, ThemeMessageBoxButton.YesNo, themeIcon, () =>
                {
                    okRes = (overlay.ContentHost.Content as MessageBoxControl)?.Result;
                    overlay.CloseWithFade();
                });
                overlay.SetContent(mb);
                ShowDialogCore(overlay);
                return okRes == true ? MessageBoxResult.OK : MessageBoxResult.Cancel;
            }
            // Map image to themed icon
            var icon = ThemeMessageBoxIcon.Info;
            if (image == MessageBoxImage.Error) icon = ThemeMessageBoxIcon.Error;
            else if (image == MessageBoxImage.Warning) icon = ThemeMessageBoxIcon.Warning;
            else if (image == MessageBoxImage.Question) icon = ThemeMessageBoxIcon.Question;
            ShowMsg(caption, message, ThemeMessageBoxButton.OK, icon);
            return MessageBoxResult.OK;
        }
    }
}
