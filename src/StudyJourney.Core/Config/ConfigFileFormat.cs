using System.Text.Json;
using System.Text.Json.Nodes;

namespace StudyJourney.Core.Config;

/// <summary>
/// <c>settings.json</c> 的**落盘格式**：v3.3.0 写出的形态是
/// 「CRLF + 2 空格缩进 + 无 BOM + 无结尾换行 + 中文 \uXXXX 转义」。
///
/// .NET 9+ 的 <see cref="JsonSerializerOptions"/> 支持
/// <c>IndentCharacter</c> / <c>IndentSize</c> / <c>NewLine</c>，
/// 配合 STJ 默认编码器（转义非 ASCII）可以**逐字节复现**这个形态 —— 这是 A0-3 的成立基础。
/// 加载时用 <see cref="Matches"/> 实测原文件能否复现；不能复现就只在首次真实写入时告警（不静默改格式）。
/// </summary>
internal sealed class ConfigFileFormat
{
    private readonly JsonSerializerOptions _options;

    public ConfigFileFormat(bool withBom, string newLine, string trailingNewLine)
    {
        WithBom = withBom;
        TrailingNewLine = trailingNewLine;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentCharacter = ' ',
            IndentSize = 2,
            NewLine = newLine,
        };
    }

    /// <summary>原文件是否带 UTF-8 BOM（落盘时原样还原）。</summary>
    public bool WithBom { get; }

    /// <summary>原文件结尾的换行串（无结尾换行时为空串）。</summary>
    public string TrailingNewLine { get; }

    public string Serialize(JsonObject root) => JsonSerializer.Serialize(root, _options) + TrailingNewLine;

    /// <summary>序列化结果是否与原文逐字节相等（UTF-8 下字符串相等即字节相等）。</summary>
    public bool Matches(JsonObject root, string raw) => string.Equals(Serialize(root), raw, StringComparison.Ordinal);

    /// <summary>按原文件探测换行风格（出现 CRLF 即按 CRLF；否则 LF）。</summary>
    public static string DetectNewLine(string raw)
        => raw.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>探测原文件是否有结尾换行、是哪种。</summary>
    public static string DetectTrailingNewLine(string raw)
        => raw.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n"
            : raw.EndsWith("\n", StringComparison.Ordinal) ? "\n"
            : string.Empty;
}
