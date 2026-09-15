using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 课件/音频「自然序号」工具（2.5.8 顺序记忆的地基）。
///
/// 老师命名习惯多样，需要能识别其中携带的序号：
///   阿拉伯数字：01.pptx / unit1.mp3 / Unit 03.mp3 / 第1讲.pdf / Lesson2.docx / 1-2 复习.pptx / L05.wav
///   中文数字（2026-09-15 新增）：一、牛顿第一定律.pptx / 第十讲 力学.pdf / 三 化学平衡.pptx
/// 解析规则：取文件名里**第一段**序号（阿拉伯与中文数字谁先出现取谁）；
///   - 无序号 → 视为无序号（排在有序号之后，按名称排）
///   - 数字过大（>999，如年份 2024、"二〇二四"）→ 视为无序号，避免"高三复习2024"排在最后
///   - 同名序号（如 "1-1" 与 "1-2" 都解析为 1）→ 用完整文件名做次级排序，保证稳定
///
/// 中文数字的**误报抑制**：中文里数字用字会出现在普通词中（"一次函数" 的"一"、"一元二次方程"），
/// 所以中文数字只有满足以下之一才算序号：
///   ① 紧跟在"第"后面（第一讲 / 第二课）
///   ② 后面紧跟分隔符或数量单位（一、xxx / 三 化学 / 五单元 / 十课时）
///   ③ 就是文件名的结尾（文件就叫"三"）
/// 阿拉伯数字沿用原有宽松规则（第一个 \d+ 即序号），保持既有行为不变。
/// </summary>
public static class FileSequence
{
    /// <summary>序号上限：超过视为"年份/页码"而非课次编号，按无序号处理</summary>
    private const int MaxSequenceNumber = 999;

    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);

    /// <summary>中文数字连续段（含大写体与常见异体）</summary>
    private static readonly Regex ChineseNumberRegex =
        new(@"[零〇○一二三四五六七八九十百千两廿卅壹贰叁肆伍陆柒捌玖拾佰仟]+", RegexOptions.Compiled);

    /// <summary>中文数字后面紧跟这些字符 → 说明它在当"序号"用（"、3" / "三 化学" / "五.单元"）</summary>
    private static readonly HashSet<char> NumberFollowers = new()
    {
        '、', '．', '.', '。', '，', ',', '：', ':', '；', ';', '-', '_', '—', '–', '～', '~',
        '/', '\\', '|', '+', ' ', '\t', '　',
        '（', '(', '）', ')', '「', '」', '『', '』', '【', '】', '[', ']', '《', '》', '"', '\'', '“', '”',
    };

    /// <summary>中文数字后面紧跟这些**单位词** → 也是在当序号用（第十讲 / 五单元 / 三课时）</summary>
    private static readonly string[] NumberUnits =
    {
        "讲", "课", "课时", "单元", "小节", "章", "节", "卷", "篇", "册", "组", "辑", "集", "期", "轮", "部", "套", "模块", "专题",
    };

    /// <summary>提取文件名中的序号；无有效序号返回 -1</summary>
    public static int ExtractNumber(string pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName)) return -1;
        var name = Path.GetFileNameWithoutExtension(pathOrName);
        if (string.IsNullOrEmpty(name)) return -1;

        // 阿拉伯与中文各找第一处，谁先出现用谁
        var arabic = NumberRegex.Match(name);
        var chinese = ChineseNumberRegex.Match(name);

        int best = -1;
        int bestIndex = int.MaxValue;

        if (arabic.Success && arabic.Index < bestIndex)
        {
            if (int.TryParse(arabic.Value, out int n) && n is >= 0 and <= MaxSequenceNumber)
            {
                best = n;
                bestIndex = arabic.Index;
            }
        }

        if (chinese.Success && chinese.Index < bestIndex
            && IsChineseSequenceContext(name, chinese.Index, chinese.Index + chinese.Length))
        {
            int n = ParseChineseNumber(chinese.Value);
            if (n is >= 0 and <= MaxSequenceNumber) best = n;   // 索引更小，覆盖阿拉伯结果
        }

        return best;
    }

    /// <summary>中文数字是否处在"序号"语境（见类注释的三条判据）</summary>
    private static bool IsChineseSequenceContext(string name, int start, int end)
    {
        // ① "第" + 数字
        if (start > 0 && name[start - 1] == '第') return true;

        // ③ 数字就是名字结尾
        if (end >= name.Length) return true;

        // ② 后面是分隔符
        if (NumberFollowers.Contains(name[end])) return true;

        // ② 后面是数量单位词
        var rest = name.AsSpan(end);
        foreach (var unit in NumberUnits)
            if (rest.StartsWith(unit)) return true;

        return false;
    }

    /// <summary>中文数字转整数（支持 一~九百九十九 / 十一 / 二十 / 二十三 / 一百零一 / 两 / 廿 等）</summary>
    private static int ParseChineseNumber(string s)
    {
        if (string.IsNullOrEmpty(s)) return -1;
        if (s.Length > 12) return -1;      // 超长串不可能是序号（多半是误匹配）

        // 先归一化：把"隐含单位"的字展开成规范写法，后面的解析逻辑才统一。
        // （廿/卅 既是数字又隐含"十"，直接在循环里判会算错："廿三"会变成 3）
        var sb = new StringBuilder(s.Length + 4);
        foreach (var c in s)
        {
            switch (c)
            {
                case '廿': sb.Append("二十"); break;
                case '卅': sb.Append("三十"); break;
                case '两': sb.Append('二'); break;
                case '〇' or '○': sb.Append('零'); break;
                default: sb.Append(c); break;
            }
        }
        var t = sb.ToString();

        // 纯数字连写（二〇二四 = 2024）：没有"十/百/千"这类单位词时按逐位拼接
        bool hasUnit = false;
        foreach (var c in t)
            if (ChineseUnit(c) > 0) { hasUnit = true; break; }

        if (!hasUnit)
        {
            var digits = new StringBuilder(t.Length);
            foreach (var c in t)
            {
                int d = ChineseDigit(c);
                if (d < 0) return -1;
                digits.Append((char)('0' + d));
            }
            return int.TryParse(digits.ToString(), out int v) ? v : -1;
        }

        int section = 0, current = 0;
        foreach (var c in t)
        {
            int d = ChineseDigit(c);
            if (d >= 0) { current = d; continue; }

            int unit = ChineseUnit(c);
            if (unit <= 0) continue;

            // "十一" 的"十"前面没有数字 → 视作 1
            if (current == 0) current = 1;
            section += current * unit;
            current = 0;
        }
        return section + current;
    }

    private static int ChineseDigit(char c) => c switch
    {
        '零' => 0,
        '一' or '壹' => 1,
        '二' or '贰' => 2,
        '三' or '叁' => 3,
        '四' or '肆' => 4,
        '五' or '伍' => 5,
        '六' or '陆' => 6,
        '七' or '柒' => 7,
        '八' or '捌' => 8,
        '九' or '玖' => 9,
        _ => -1,
    };

    private static int ChineseUnit(char c) => c switch
    {
        '十' or '拾' => 10,
        '百' or '佰' => 100,
        '千' or '仟' => 1000,
        _ => 0,
    };

    /// <summary>自然序号排序（有序号在前、按序号；同号或无号按文件名不区分大小写）</summary>
    public static List<string> Sort(IEnumerable<string> paths)
    {
        return paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .OrderBy(p => ExtractNumber(p) is var n && n >= 0 ? 0 : 1)
            .ThenBy(p => { var n = ExtractNumber(p); return n >= 0 ? n : int.MaxValue; })
            .ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>序列第一份（编号最小的；全无编号则取名称最小的一份）</summary>
    public static string? First(IEnumerable<string> paths)
        => Sort(paths).FirstOrDefault();

    /// <summary>
    /// 序列中 current 的下一份；current 不在序列里/已是最后一份 → 退回"名称最小的一份"（形成循环，避免听力播完卡死）。
    /// 单份文件时返回它自己。
    /// </summary>
    public static string? Next(string? current, IEnumerable<string> paths)
    {
        var list = Sort(paths);
        if (list.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(current)) return list[0];

        int idx = list.FindIndex(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return list[0];
        return list[(idx + 1) % list.Count];
    }

    /// <summary>列出目录下的候选文件（排除占位文件与隐藏文件），按自然序号排序</summary>
    public static List<string> ListCandidates(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return new List<string>();
            return Sort(Directory.GetFiles(directory)
                .Where(f => !f.EndsWith(".placeholder", StringComparison.OrdinalIgnoreCase))
                .Where(f => !Path.GetFileName(f).StartsWith('~'))     // Office 临时文件 ~$xxx
                .Where(f => (File.GetAttributes(f) & FileAttributes.Hidden) == 0));
        }
        catch
        {
            return new List<string>();
        }
    }
}
