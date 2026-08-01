using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace GaokaoCountdown.Helpers
{
    /// <summary>
    /// 统一窗口/元素淡入淡出工具。
    /// 关键点：WPF 动画默认 FillBehavior=HoldEnd，动画结束后会持续"持有"属性，
    /// 导致后续对同一属性的本地赋值（如 Opacity = x）被动画压制而失效。
    /// 本类在动画 Completed 时先 BeginAnimation(prop, null) 移除动画，再写本地值，
    /// 从根上消除该问题（历史 BUG：透明度修改不生效）。
    /// </summary>
    public static class FadeHelper
    {
        /// <summary>淡入：从 from 渐变到 to，完成后移除动画并写回最终值</summary>
        public static void FadeIn(UIElement element, double from, double to, double ms, Action? onCompleted = null)
        {
            FadeTo(element, from, to, ms, easeIn: false, onCompleted);
        }

        /// <summary>淡出：从 from 渐变到 to，完成后移除动画并写回最终值</summary>
        public static void FadeOut(UIElement element, double from, double to, double ms, Action? onCompleted = null)
        {
            FadeTo(element, from, to, ms, easeIn: true, onCompleted);
        }

        private static void FadeTo(UIElement element, double from, double to, double ms, bool easeIn, Action? onCompleted)
        {
            if (element == null) { onCompleted?.Invoke(); return; }

            // 停止可能存在的旧动画，避免叠加
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = from;

            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase
                {
                    EasingMode = easeIn ? EasingMode.EaseIn : EasingMode.EaseOut
                }
            };
            anim.Completed += (_, _) =>
            {
                // 关键：移除动画持有，使本地值恢复生效
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = to;
                onCompleted?.Invoke();
            };
            element.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }
}
