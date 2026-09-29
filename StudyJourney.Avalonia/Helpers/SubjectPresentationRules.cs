using System;
using System.Collections.Generic;
using System.Linq;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 「按科目的课上表现」规则（v2.26.0，用户需求：*"指定课程下不要有提示音但保留进度条，
/// 或者是不显示进度条只有声音，或者是什么都没有"*）。
///
/// 两个维度各一个开关，正好 4 种组合（与 UI 的 4 档下拉一一对应）：
///   ① 正常      提示音✓ 显示✓   —— 不配置时的默认表现
///   ② 静音      提示音✗ 显示✓   —— "不要有提示音但保留进度条"
///   ③ 只提示音  提示音✓ 显示✗   —— "不显示进度条，只有声音"
///   ④ 完全安静  提示音✗ 显示✗   —— "什么都没有"
///
/// ⚠ 这里是**纯函数**：运行期（ReminderService 判静音 / MainWindow 判隐藏）与设置页共用同一份映射，
///   免得"界面写着一个档、运行时按另一个档执行"这种不会报错的分歧。
/// ⚠ 档位顺序即 UI 下拉索引，**只能末尾追加**（与枚举落盘同理）。
/// </summary>
public static class SubjectPresentationRules
{
    /// <summary>4 档的显示名（索引 = UI 下拉 SelectedIndex）</summary>
    public static readonly string[] PresetNames =
    {
        "正常（有提示音 + 上课显示）",
        "静音（不出声，但保留上课显示）",
        "只提示音（上课不显示）",
        "完全安静（不出声，上课也不显示）",
    };

    /// <summary>档位 → (是否静音, 是否上课隐藏)。越界按「正常」处理（宁可多响一声，也别静默消失）</summary>
    public static (bool MuteSound, bool HideWindow) Preset(int index) => index switch
    {
        1 => (true, false),
        2 => (false, true),
        3 => (true, true),
        _ => (false, false),
    };

    /// <summary>反向：两个开关 → 档位索引（用于把已存设置回显到下拉）</summary>
    public static int PresetIndexOf(bool muteSound, bool hideWindow)
        => (muteSound, hideWindow) switch
        {
            (true, false) => 1,
            (false, true) => 2,
            (true, true) => 3,
            _ => 0,
        };

    /// <summary>科目名归一化（去掉首尾空白、统一大写比较用）</summary>
    private static string Norm(string? subject) => (subject ?? "").Trim();

    /// <summary>找出该科目的策略；没配过返回 null</summary>
    public static Models.SubjectPresentation? Find(
        IEnumerable<Models.SubjectPresentation>? list, string? subject)
    {
        var key = Norm(subject);
        if (key.Length == 0 || list == null) return null;
        return list.FirstOrDefault(p =>
            p != null && string.Equals(Norm(p.Subject), key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>该科目上课时是否**不播提示音**（提醒弹窗照常 —— 老师不会错过提醒）</summary>
    public static bool IsMuted(IEnumerable<Models.SubjectPresentation>? list, string? subject)
        => Find(list, subject)?.MuteSound == true;

    /// <summary>该科目上课时是否**整个窗口不显示**（托盘 / Ctrl+Shift+H 仍可唤回）</summary>
    public static bool IsHidden(IEnumerable<Models.SubjectPresentation>? list, string? subject)
        => Find(list, subject)?.HideWindow == true;

    /// <summary>
    /// 就地把一条策略写入列表（同科目覆盖，科目名按归一化后比较）。
    /// 档位为「正常」时视为**删除该科目的配置** —— 免得列表里堆一堆"其实什么都没改"的条目。
    /// 返回 true = 发生了变化（供设置页标脏）。
    /// </summary>
    public static bool Upsert(List<Models.SubjectPresentation> list, string? subject, int presetIndex)
    {
        var key = Norm(subject);
        if (key.Length == 0) return false;

        var (mute, hide) = Preset(presetIndex);
        var exist = Find(list, key);

        if (!mute && !hide)
        {
            if (exist == null) return false;
            list.Remove(exist);
            return true;
        }
        if (exist != null)
        {
            if (exist.MuteSound == mute && exist.HideWindow == hide) return false;
            exist.MuteSound = mute;
            exist.HideWindow = hide;
            return true;
        }
        list.Add(new Models.SubjectPresentation { Subject = key, MuteSound = mute, HideWindow = hide });
        return true;
    }

    /// <summary>该科目的档位索引（没配过 → 0 = 正常）</summary>
    public static int PresetOf(IEnumerable<Models.SubjectPresentation>? list, string? subject)
    {
        var p = Find(list, subject);
        return p == null ? 0 : PresetIndexOf(p.MuteSound, p.HideWindow);
    }
}
