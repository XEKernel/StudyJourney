using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace StudyJourney.Avalonia.Services.GradeAnalysis;

/// <summary>一张工作表的内容：按行、按列索引存放的单元格文本（空单元格为 ""）。</summary>
public sealed class XlsxSheet
{
    public string Name { get; set; } = "";
    public List<string[]> Rows { get; } = new();
    public int MaxColumns { get; internal set; }

    /// <summary>取某行某列（越界返回 ""）。</summary>
    public string At(int row, int col)
    {
        if (row < 0 || row >= Rows.Count) return "";
        var r = Rows[row];
        return col >= 0 && col < r.Length ? r[col] : "";
    }
}

/// <summary>
/// 极简 .xlsx 读取器 —— <b>手写，零第三方依赖</b>。
///
/// <para><b>为什么不用 MiniExcel / NPOI</b>：两者把 POCO / dynamic 映射建在反射 +
/// <c>System.Dynamic</c> 之上，在 NativeAOT 下要么抛 <c>NotSupportedException</c>、
/// 要么产生 IL2026/IL3050 告警 —— 而本项目把「PublishAot 后 IL2026/IL3050 = 0」
/// 当作硬门禁。MiniExcel 官方确实提供了 NativeAOT 支持，但那是给它的**命令行工具**用的，
/// 不是给库使用者的映射 API。xlsx 本身只是一个 zip + 三段 XML，
/// 解析成本远低于为一个导入功能引入反射型依赖。</para>
///
/// <para><b>覆盖的单元格类型</b>：共享字符串（<c>t="s"</c>）、内联字符串（<c>t="inlineStr"</c>）、
/// 数值、布尔、公式结果字符串（<c>t="str"</c>）、错误值（<c>t="e"</c>）。</para>
///
/// <para>⚠ <b>不读 <c>styles.xml</c></b>，因此无法区分「单元格是日期格式」和「单元格是普通数字」——
/// 两者在 XML 里都只是一个序列号。日期列交给 <see cref="ExcelSerialDate"/> 按「列名是日期列 + 值是纯数字」
/// 的启发式还原，并在导入诊断里明确说明。</para>
/// </summary>
public static class XlsxReader
{
    private static readonly XmlReaderSettings Settings = new()
    {
        IgnoreWhitespace = true,
        IgnoreComments = true,
        DtdProcessing = DtdProcessing.Prohibit, // 防 XXE：绝不解析外部实体
        XmlResolver = null,
    };

    /// <summary>列出工作簿内所有工作表名（按工作簿里的顺序）。</summary>
    public static List<string> ListSheetNames(string path)
    {
        var names = new List<string>();
        try
        {
            using var fs = OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var wb = zip.GetEntry("xl/workbook.xml");
            if (wb is null) return names;
            using var reader = XmlReader.Create(wb.Open(), Settings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
                {
                    var n = reader.GetAttribute("name");
                    if (!string.IsNullOrEmpty(n)) names.Add(n);
                }
            }
        }
        catch { /* 列不出就返回空，调用方按「第一张表」兜底 */ }
        return names;
    }

    /// <summary>读取第一张工作表。失败时 <paramref name="error"/> 是给老师看的中文原因。</summary>
    public static bool TryReadFirstSheet(string path, out XlsxSheet sheet, out string error)
        => TryReadSheet(path, null, out sheet, out error);

    /// <summary>读取指定工作表（<paramref name="sheetName"/> 为 null 时取第一张）。</summary>
    public static bool TryReadSheet(string path, string? sheetName, out XlsxSheet sheet, out string error)
    {
        sheet = new XlsxSheet();
        error = "";
        try
        {
            using var fs = OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

            var entry = ResolveSheetEntry(zip, sheetName) ?? zip.GetEntry("xl/worksheets/sheet1.xml");
            if (entry is null)
            {
                error = "这个文件里没有找到工作表。请确认它是 Excel 另存的 .xlsx（不是改后缀名来的）。";
                return false;
            }

            sheet.Name = sheetName ?? "";
            var shared = ReadSharedStrings(zip);
            ReadSheetData(entry, shared, sheet);
            if (sheet.Rows.Count == 0)
            {
                error = "工作表是空的（没有任何数据行）。";
                return false;
            }
            return true;
        }
        catch (InvalidDataException)
        {
            // xlsx 的实质是 zip；打不开 zip 基本都是旧版 .xls 或已损坏
            error = "无法读取该文件：它不是有效的 .xlsx。如果是旧版 .xls，请用 Excel / WPS「另存为」成 .xlsx 再导入。";
            return false;
        }
        catch (IOException ex)
        {
            error = $"读取文件失败（可能被杀毒软件或 Excel 占用）：{ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = $"解析 Excel 时出错：{ex.Message}";
            return false;
        }
    }

    private static FileStream OpenRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    /// <summary>按工作簿里第一张表的 r:id 经 rels 映射到真实的 sheet part 路径。</summary>
    private static ZipArchiveEntry? ResolveSheetEntry(ZipArchive zip, string? sheetName)
    {
        var wb = zip.GetEntry("xl/workbook.xml");
        if (wb is null) return null;

        string? wantRelId = null;
        string? firstName = null;
        using (var reader = XmlReader.Create(wb.Open(), Settings))
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "sheet") continue;
                firstName ??= reader.GetAttribute("name");
                var rid = reader.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                if (sheetName is null || string.Equals(reader.GetAttribute("name"), sheetName, StringComparison.Ordinal))
                {
                    wantRelId = rid;
                    if (sheetName is null) break;
                }
            }
        }
        _ = firstName;

        if (wantRelId is null) return null;

        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (rels is null) return null;

        using (var reader = XmlReader.Create(rels.Open(), Settings))
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship") continue;
                if (!string.Equals(reader.GetAttribute("Id"), wantRelId, StringComparison.Ordinal)) continue;
                var target = reader.GetAttribute("Target");
                if (string.IsNullOrEmpty(target)) return null;
                target = target.Replace('\\', '/');
                if (target.StartsWith("/", StringComparison.Ordinal)) target = target[1..];
                else if (!target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) target = "xl/" + target;
                return zip.GetEntry(target);
            }
        }
        return null;
    }

    /// <summary>读 <c>xl/sharedStrings.xml</c>。<c>&lt;si&gt;</c> 内可能拆成多个 <c>&lt;r&gt;&lt;t&gt;</c>（富文本），需要拼接。</summary>
    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return list;

        using var reader = XmlReader.Create(entry.Open(), Settings);
        var sb = new StringBuilder();
        bool inT = false;
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (reader.LocalName == "si") sb.Clear();
                    else if (reader.LocalName == "t") inT = true;
                    break;
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    if (inT) sb.Append(reader.Value);
                    break;
                case XmlNodeType.EndElement:
                    if (reader.LocalName == "t") inT = false;
                    else if (reader.LocalName == "si") list.Add(sb.ToString());
                    break;
            }
        }
        return list;
    }

    /// <summary>流式读 <c>sheetData</c>。用行列号还原位置，因此**支持跳列**（Excel 常把空单元格整个省略）。</summary>
    private static void ReadSheetData(ZipArchiveEntry entry, List<string> shared, XlsxSheet sheet)
    {
        using var reader = XmlReader.Create(entry.Open(), Settings);

        List<string>? rowAcc = null;
        int colIndex = -1;
        string cellType = "";
        var valueBuf = new StringBuilder();
        var inlineBuf = new StringBuilder();
        bool inV = false, inT = false, inFormula = false;

        void FlushCell()
        {
            if (rowAcc is null || colIndex < 0) return;
            var text = cellType switch
            {
                "s" => ResolveShared(shared, valueBuf.ToString()),
                "inlineStr" => inlineBuf.ToString(),
                _ => valueBuf.ToString(),
            };
            while (rowAcc.Count <= colIndex) rowAcc.Add("");
            rowAcc[colIndex] = text;
            if (colIndex + 1 > sheet.MaxColumns) sheet.MaxColumns = colIndex + 1;
        }

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    switch (reader.LocalName)
                    {
                        case "row":
                            rowAcc = new List<string>();
                            break;
                        case "c":
                            colIndex = ColumnIndex(reader.GetAttribute("r"));
                            cellType = reader.GetAttribute("t") ?? "";
                            valueBuf.Clear();
                            inlineBuf.Clear();
                            if (reader.IsEmptyElement) FlushCell(); // <c r="B2"/> 空单元格
                            break;
                        case "v":
                            inV = true;
                            break;
                        case "is":
                            break;
                        case "t":
                            if (!inFormula) inT = true;
                            break;
                        case "f":
                            inFormula = true;
                            break;
                    }
                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    if (inV) valueBuf.Append(reader.Value);
                    else if (inT) inlineBuf.Append(reader.Value);
                    break;

                case XmlNodeType.EndElement:
                    switch (reader.LocalName)
                    {
                        case "v": inV = false; break;
                        case "t": inT = false; break;
                        case "f": inFormula = false; break;
                        case "c": FlushCell(); break;
                        case "row":
                            // 补到统一列宽，方便调用方按下标取（列数不一致时也不越界）
                            if (rowAcc is not null) sheet.Rows.Add(rowAcc.ToArray());
                            rowAcc = null;
                            break;
                    }
                    break;
            }
        }
    }

    private static string ResolveShared(List<string> shared, string raw)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)) return "";
        return idx >= 0 && idx < shared.Count ? shared[idx] : "";
    }

    /// <summary>"AB12" → 27（0 起）。解析失败返回 -1。</summary>
    public static int ColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return -1;
        int col = 0, letters = 0;
        foreach (var ch in cellRef)
        {
            if (ch >= 'A' && ch <= 'Z') { col = col * 26 + (ch - 'A' + 1); letters++; }
            else if (ch >= 'a' && ch <= 'z') { col = col * 26 + (ch - 'a' + 1); letters++; }
            else break;
        }
        return letters == 0 ? -1 : col - 1;
    }
}

/// <summary>
/// Excel 日期序列号 ↔ DateTime。
///
/// <para><b>为什么不能简单写成「1899-12-30 + 天数」</b>：Excel 的 1900 日期系统里
/// 1900-02-29 这个**从来不存在的日期**被占了一个序号（60），所以序列号与真实日期在
/// 1900-03-01 处有一个**断点**：</para>
/// <list type="bullet">
/// <item><c>1</c> → 1900-01-01、<c>59</c> → 1900-02-28（这两段要 +1 天）</item>
/// <item><c>61</c> → 1900-03-01、<c>46310</c> → 2026-10-15（这两段<b>不能</b> +1）</item>
/// </list>
///
/// <para>⚠ <b>2026-10-06 踩过的坑</b>：这个断点的补偿方向一开始写反了
/// （写成 <c>serial &gt;= 61 ? serial - 1 : serial</c>），结果所有导入的考试日期**全部偏早一天**，
/// 而且自检里那条「45678 → 2025-01-20」也跟着错，两个错互相抵消、测试照样 PASS ——
/// 典型的「测试跟着 bug 一起错」。是拿 openpyxl 生成的**真表**跑导入才暴露出来的。
/// 现在自检里直接钉死 1 / 59 / 61 / 45678 四个边界值。</para>
/// </summary>
public static class ExcelSerialDate
{
    private static readonly DateTime Epoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>是否是「看起来像日期序列号」的纯数字（1 .. 2958465，即 1900-01-01 .. 9999-12-31）。</summary>
    public static bool LooksLikeSerial(string text, out double serial)
    {
        serial = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return false;
        if (v < 1 || v > 2958465) return false;
        serial = v;
        return true;
    }

    public static DateTime ToDateTime(double serial)
    {
        // 60 是那个虚构的 1900-02-29，不去管它（Excel 自己也会存这个值）
        var days = serial >= 61 ? serial : serial + 1;
        return Epoch.AddDays(days);
    }

    public static DateTime ToDate(double serial) => ToDateTime(serial).Date;
}
