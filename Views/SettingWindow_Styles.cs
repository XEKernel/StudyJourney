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
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MessageBox = GaokaoCountdown.Views.DialogHelper;
using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
namespace GaokaoCountdown.Views
{
    public partial class SettingWindow : Window
    {
        // ── 在 C# 中构建动画控件样式（统一 1.5 秒） ──────────

        /// <summary>构建带动画的 RadioButton 样式（全部 1.5s）</summary>
        private static Style BuildAnimatedRadioStyle()
        {
            // ── 外层 Border ──────────────────────────────────────
            var radioOuter = new FrameworkElementFactory(typeof(Border));
            radioOuter.Name = "RadioOuter";
            radioOuter.SetValue(Border.WidthProperty, 18.0);
            radioOuter.SetValue(Border.HeightProperty, 18.0);
            radioOuter.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            radioOuter.SetValue(Border.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22FFFFFF")));
            radioOuter.SetValue(Border.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44FFFFFF")));
            radioOuter.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            radioOuter.SetValue(Border.MarginProperty, new Thickness(0, 0, 8, 0));
            radioOuter.SetValue(Border.SnapsToDevicePixelsProperty, true);

            // ── 内点 ────────────────────────────────────────────
            var radioDot = new FrameworkElementFactory(typeof(Ellipse));
            radioDot.Name = "RadioDot";
            radioDot.SetValue(Ellipse.WidthProperty, 8.0);
            radioDot.SetValue(Ellipse.HeightProperty, 8.0);
            radioDot.SetValue(Ellipse.FillProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6688CC")));
            radioDot.SetValue(Ellipse.OpacityProperty, 0.0);
            radioDot.SetValue(Ellipse.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            radioDot.SetValue(Ellipse.VerticalAlignmentProperty, VerticalAlignment.Center);
            radioOuter.AppendChild(radioDot);

            // ── ContentPresenter ─────────────────────────────────
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(Grid.ColumnProperty, 1);
            cp.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(2, 0, 0, 0));
            cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);

            // ── 根 Grid ─────────────────────────────────────────
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(Grid.BackgroundProperty, Brushes.Transparent);
            var cd0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            cd0.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var cd1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            cd1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            root.AppendChild(cd0);
            root.AppendChild(cd1);
            root.AppendChild(radioOuter);
            root.AppendChild(cp);

            // ── ControlTemplate ──────────────────────────────────
            var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = root };

            // ── 稳态 Trigger ────────────────────────────────────
            var isCheckedTrigger = new Trigger
            {
                Property = RadioButton.IsCheckedProperty,
                Value = true
            };
            isCheckedTrigger.Setters.Add(new Setter(Ellipse.OpacityProperty, 1.0, "RadioDot"));
            isCheckedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6688CC")), "RadioOuter"));
            isCheckedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#186688CC")), "RadioOuter"));
            template.Triggers.Add(isCheckedTrigger);

            // MouseOver + !Checked
            var hoverTrigger = new MultiTrigger();
            hoverTrigger.Conditions.Add(new Condition(RadioButton.IsMouseOverProperty, true));
            hoverTrigger.Conditions.Add(new Condition(RadioButton.IsCheckedProperty, false));
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FFFFFF")), "RadioOuter"));
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66FFFFFF")), "RadioOuter"));
            template.Triggers.Add(hoverTrigger);

            // MouseOver + Checked
            var hoverCheckedTrigger = new MultiTrigger();
            hoverCheckedTrigger.Conditions.Add(new Condition(RadioButton.IsMouseOverProperty, true));
            hoverCheckedTrigger.Conditions.Add(new Condition(RadioButton.IsCheckedProperty, true));
            hoverCheckedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#226688CC")), "RadioOuter"));
            template.Triggers.Add(hoverCheckedTrigger);

            // IsEnabled = false
            var disabledTrigger = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            template.Triggers.Add(disabledTrigger);

            // ── Checked 动画（1.5s） ─────────────────────────────
            var checkedSB = new Storyboard { FillBehavior = FillBehavior.Stop };

            var dotWAnim = new DoubleAnimation(0, 8, TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
            };
            Storyboard.SetTargetName(dotWAnim, "RadioDot");
            Storyboard.SetTargetProperty(dotWAnim, new PropertyPath(Ellipse.WidthProperty));
            checkedSB.Children.Add(dotWAnim);

            var dotHAnim = new DoubleAnimation(0, 8, TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
            };
            Storyboard.SetTargetName(dotHAnim, "RadioDot");
            Storyboard.SetTargetProperty(dotHAnim, new PropertyPath(Ellipse.HeightProperty));
            checkedSB.Children.Add(dotHAnim);

            var dotOpAnim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTargetName(dotOpAnim, "RadioDot");
            Storyboard.SetTargetProperty(dotOpAnim, new PropertyPath(Ellipse.OpacityProperty));
            checkedSB.Children.Add(dotOpAnim);

            var checkedET = new EventTrigger(RadioButton.CheckedEvent);
            checkedET.Actions.Add(new BeginStoryboard { Storyboard = checkedSB });
            template.Triggers.Add(checkedET);

            // ── Unchecked 动画（1.5s） ───────────────────────────
            var uncheckedSB = new Storyboard { FillBehavior = FillBehavior.Stop };

            var dotWOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTargetName(dotWOut, "RadioDot");
            Storyboard.SetTargetProperty(dotWOut, new PropertyPath(Ellipse.WidthProperty));
            uncheckedSB.Children.Add(dotWOut);

            var dotHOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTargetName(dotHOut, "RadioDot");
            Storyboard.SetTargetProperty(dotHOut, new PropertyPath(Ellipse.HeightProperty));
            uncheckedSB.Children.Add(dotHOut);

            var dotOpOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTargetName(dotOpOut, "RadioDot");
            Storyboard.SetTargetProperty(dotOpOut, new PropertyPath(Ellipse.OpacityProperty));
            uncheckedSB.Children.Add(dotOpOut);

            var uncheckedET = new EventTrigger(RadioButton.UncheckedEvent);
            uncheckedET.Actions.Add(new BeginStoryboard { Storyboard = uncheckedSB });
            template.Triggers.Add(uncheckedET);

            // ── Style ───────────────────────────────────────────
            var style = new Style(typeof(RadioButton));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 3, 12, 3)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(Control.ForegroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCE0E0F0"))));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            return style;
        }

        /// <summary>构建带动画的 CheckBox 样式（全部 1.5s）</summary>
        private static Style BuildAnimatedCheckStyle()
        {
            // ── 轨道 ─────────────────────────────────────────────
            var switchTrack = new FrameworkElementFactory(typeof(Border));
            switchTrack.Name = "SwitchTrack";
            switchTrack.SetValue(Border.WidthProperty, 40.0);
            switchTrack.SetValue(Border.HeightProperty, 22.0);
            switchTrack.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            switchTrack.SetValue(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22FFFFFF")));
            switchTrack.SetValue(Border.BorderBrushProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30FFFFFF")));
            switchTrack.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            switchTrack.SetValue(Border.MarginProperty, new Thickness(0, 0, 8, 0));

            // ── 滑块 ────────────────────────────────────────────
            var switchThumb = new FrameworkElementFactory(typeof(Border));
            switchThumb.Name = "SwitchThumb";
            switchThumb.SetValue(Border.WidthProperty, 18.0);
            switchThumb.SetValue(Border.HeightProperty, 18.0);
            switchThumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            switchThumb.SetValue(Border.BackgroundProperty, Brushes.White);
            switchThumb.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            switchThumb.SetValue(Border.MarginProperty, new Thickness(2, 0, 0, 0));
            var shadow = new DropShadowEffect { ShadowDepth = 0.5, BlurRadius = 3, Opacity = 0.3 };
            switchThumb.SetValue(Border.EffectProperty, shadow);

            // ── ContentPresenter ─────────────────────────────────
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(Grid.ColumnProperty, 1);
            cp.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(2, 0, 0, 0));
            cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);

            // ── 根 Grid ─────────────────────────────────────────
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(Grid.BackgroundProperty, Brushes.Transparent);
            var cd0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            cd0.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var cd1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            cd1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            root.AppendChild(cd0);
            root.AppendChild(cd1);
            root.AppendChild(switchTrack);
            root.AppendChild(switchThumb);
            root.AppendChild(cp);

            // ── ControlTemplate ──────────────────────────────────
            var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = root };

            // ── 稳态 Trigger ────────────────────────────────────
            var isCheckedTrigger = new Trigger
            {
                Property = CheckBox.IsCheckedProperty,
                Value = true
            };
            isCheckedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#446688CC")), "SwitchTrack"));
            isCheckedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6688CC")), "SwitchTrack"));
            isCheckedTrigger.Setters.Add(new Setter(Border.MarginProperty,
                new Thickness(20, 0, 0, 0), "SwitchThumb"));
            isCheckedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.White, "SwitchThumb"));
            template.Triggers.Add(isCheckedTrigger);

            // MouseOver + !Checked
            var hoverTrigger = new MultiTrigger();
            hoverTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hoverTrigger.Conditions.Add(new Condition(CheckBox.IsCheckedProperty, false));
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FFFFFF")), "SwitchTrack"));
            template.Triggers.Add(hoverTrigger);

            // MouseOver + Checked
            var hoverCheckedTrigger = new MultiTrigger();
            hoverCheckedTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hoverCheckedTrigger.Conditions.Add(new Condition(CheckBox.IsCheckedProperty, true));
            hoverCheckedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#556688CC")), "SwitchTrack"));
            template.Triggers.Add(hoverCheckedTrigger);

            // IsEnabled = false
            var disabledTrigger = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            template.Triggers.Add(disabledTrigger);

            // ── Checked 动画（1.5s） ─────────────────────────────
            var checkedSB = new Storyboard { FillBehavior = FillBehavior.Stop };
            var thumbInAnim = new ThicknessAnimation(
                new Thickness(2, 0, 0, 0),
                new Thickness(20, 0, 0, 0),
                TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTargetName(thumbInAnim, "SwitchThumb");
            Storyboard.SetTargetProperty(thumbInAnim, new PropertyPath(Border.MarginProperty));
            checkedSB.Children.Add(thumbInAnim);

            var checkedET = new EventTrigger(CheckBox.CheckedEvent);
            checkedET.Actions.Add(new BeginStoryboard { Storyboard = checkedSB });
            template.Triggers.Add(checkedET);

            // ── Unchecked 动画（1.5s） ───────────────────────────
            var uncheckedSB = new Storyboard { FillBehavior = FillBehavior.Stop };
            var thumbOutAnim = new ThicknessAnimation(
                new Thickness(20, 0, 0, 0),
                new Thickness(2, 0, 0, 0),
                TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTargetName(thumbOutAnim, "SwitchThumb");
            Storyboard.SetTargetProperty(thumbOutAnim, new PropertyPath(Border.MarginProperty));
            uncheckedSB.Children.Add(thumbOutAnim);

            var uncheckedET = new EventTrigger(CheckBox.UncheckedEvent);
            uncheckedET.Actions.Add(new BeginStoryboard { Storyboard = uncheckedSB });
            template.Triggers.Add(uncheckedET);

            // ── Style ───────────────────────────────────────────
            var style = new Style(typeof(CheckBox));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 7)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            style.Setters.Add(new Setter(Control.ForegroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCE0E0F0"))));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            return style;
        }
    }
}
