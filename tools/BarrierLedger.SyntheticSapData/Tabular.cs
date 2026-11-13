using System.IO.Compression;
using System.Text;
using System.Xml;

namespace BarrierLedger.SyntheticSapData;

/// <summary>Column headers as an SAP list export in English would label them.</summary>
internal static class Headers
{
    public static readonly string[] Fl =
    {
        "Functional Loc.", "Description", "SupFunctLoc.", "Object Type", "Sort Field",
        "Plant Section", "Planner Group", "Main WorkCtr", "System Status", "ABC Indic.",
    };

    public static readonly string[] FlLogical =
    {
        "fl_code", "fl_description", "superior_fl", "object_type", "sort_field",
        "plant_section", "planner_group", "main_work_center", "system_status", "abc_indicator",
    };

    public static readonly string[] Equipment =
    {
        "Equipment", "Description", "Functional Loc.", "Object Type", "Sort Field", "Manufacturer",
        "Model number", "ManufSerialNo.", "System Status", "ABC Indic.", "Planner Group", "Main WorkCtr",
    };

    public static readonly string[] EquipmentLogical =
    {
        "equipment_no", "equipment_description", "installed_at_fl", "object_type", "sort_field", "manufacturer",
        "model", "serial_no", "system_status", "abc_indicator", "planner_group", "main_work_center",
    };
}

internal static class Encodings
{
    static Encodings()
    {
        // The provider must be registered before code page 1256 is requested, so
        // both happen here, in order, and not in field initializers.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1256 = Encoding.GetEncoding(1256, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    public static Encoding Utf8Strict { get; } =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Windows-1256, throwing instead of substituting when a character cannot be encoded.</summary>
    public static Encoding Windows1256 { get; }
}

internal static class Csv
{
    private const string LineEnd = "\r\n";

    public static string Format(string[] header, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        AppendLine(sb, header);
        foreach (string[] row in rows)
        {
            AppendLine(sb, row);
        }

        return sb.ToString();
    }

    public static byte[] ToUtf8WithBom(string text)
    {
        byte[] body = Encodings.Utf8Strict.GetBytes(text);
        byte[] result = new byte[body.Length + 3];
        result[0] = 0xEF;
        result[1] = 0xBB;
        result[2] = 0xBF;
        body.CopyTo(result, 3);
        return result;
    }

    public static string FromUtf8WithBom(byte[] bytes)
    {
        bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return hasBom
            ? Encodings.Utf8Strict.GetString(bytes, 3, bytes.Length - 3)
            : Encodings.Utf8Strict.GetString(bytes);
    }

    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        bool any = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    any = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    any = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add(fields.ToArray());
                    fields.Clear();
                    any = false;
                    break;
                default:
                    field.Append(c);
                    any = true;
                    break;
            }
        }

        if (any || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add(fields.ToArray());
        }

        return rows;
    }

    private static void AppendLine(StringBuilder sb, string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            string value = fields[i];
            bool mustQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                || (value.Length > 0 && (value[0] == ' ' || value[^1] == ' '));
            if (mustQuote)
            {
                sb.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
            }
            else
            {
                sb.Append(value);
            }
        }

        sb.Append(LineEnd);
    }
}

/// <summary>
/// Minimal single-sheet workbook with every cell stored as an inline string,
/// so equipment numbers and codes keep their exact text.
/// </summary>
internal static class Xlsx
{
    private static readonly DateTimeOffset FixedTimestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static byte[] Write(string[] header, IEnumerable<string[]> rows)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                + "</Types>");
            AddEntry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                + "</Relationships>");
            AddEntry(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                + "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            AddEntry(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
                + "</Relationships>");
            AddEntry(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
                + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
                + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
                + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
                + "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>"
                + "</styleSheet>");
            AddEntry(zip, "xl/worksheets/sheet1.xml", SheetXml(header, rows));
        }

        return stream.ToArray();
    }

    public static List<string[]> Read(byte[] bytes, int width)
    {
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        ZipArchiveEntry entry = zip.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidDataException("Workbook has no xl/worksheets/sheet1.xml.");
        using Stream sheet = entry.Open();
        using var reader = XmlReader.Create(sheet, new XmlReaderSettings { IgnoreWhitespace = false });

        var rows = new List<string[]>();
        string[]? current = null;
        int column = -1;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
            {
                current = Enumerable.Repeat("", width).ToArray();
                rows.Add(current);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c")
            {
                string reference = reader.GetAttribute("r") ?? throw new InvalidDataException("Cell without reference.");
                column = ColumnIndex(reference);
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t" && current is not null)
            {
                if (column < 0 || column >= width)
                {
                    throw new InvalidDataException("Cell outside the expected width.");
                }

                current[column] = reader.ReadElementContentAsString();
            }
        }

        return rows;
    }

    private static int ColumnIndex(string reference)
    {
        int index = 0;
        foreach (char c in reference)
        {
            if (c < 'A' || c > 'Z')
            {
                break;
            }

            index = (index * 26) + (c - 'A' + 1);
        }

        return index - 1;
    }

    private static string ColumnName(int index)
    {
        if (index >= 26)
        {
            throw new NotSupportedException("Only 26 columns are supported.");
        }

        return ((char)('A' + index)).ToString();
    }

    private static string SheetXml(string[] header, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        int rowNumber = 1;
        AppendRow(sb, header, rowNumber++);
        foreach (string[] row in rows)
        {
            AppendRow(sb, row, rowNumber++);
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string[] fields, int rowNumber)
    {
        sb.Append("<row r=\"").Append(rowNumber).Append("\">");
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].Length == 0)
            {
                continue;
            }

            sb.Append("<c r=\"").Append(ColumnName(i)).Append(rowNumber)
              .Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
              .Append(Escape(fields[i]))
              .Append("</t></is></c>");
        }

        sb.Append("</row>");
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTimestamp;
        using Stream stream = entry.Open();
        byte[] bytes = Encodings.Utf8Strict.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
