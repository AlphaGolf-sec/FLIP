using System.Globalization;
using System.IO;
using System.Text.Json;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Flip.Core;
using Flip.Routing;

namespace Flip.Engines;

/// <summary>
/// Motore built-in per documenti Office Open XML (DOCX, XLSX, PPTX, ODT).
/// Manipola direttamente i contenitori ZIP e XML senza richiedere Office o LibreOffice.
/// </summary>
public sealed class OfficeOpenXmlEngine : IConversionEngine
{
    public string Id => "openxml_builtin";
    public string DisplayName => "Native Office OpenXML Engine (No Office Required)";
    public bool IsExternal => false;

    private readonly List<ConversionEdge> _supportedEdges = [];

    public IReadOnlyList<ConversionEdge> SupportedEdges => _supportedEdges;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace OdtText = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace OdtOffice = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace OdtTable = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const int MaxSpreadsheetColumns = 16_384;
    private static readonly XNamespace Ssml = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Pml = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static readonly string[] DocAndTextFormats = ["txt", "md", "html", "docx", "odt", "rtf"];
    private static readonly string[] TabularFormats = ["csv", "tsv", "json", "xml", "yaml", "yml"];

    public OfficeOpenXmlEngine()
    {
        // Full 1-hop mesh among document & text/markup formats (TXT <-> MD <-> HTML <-> DOCX <-> ODT <-> RTF)
        foreach (var src in DocAndTextFormats)
            foreach (var dst in DocAndTextFormats)
                if (!string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
                    _supportedEdges.Add(new ConversionEdge(src, dst, Id, 10));

        // Direct 1-hop text/markdown -> JSON & CSV
        foreach (var src in new[] { "txt", "md" })
            foreach (var dst in new[] { "json", "csv" })
                _supportedEdges.Add(new ConversionEdge(src, dst, Id, 12));

        // XLSX -> tabular & markup formats
        foreach (var dst in new[] { "csv", "tsv", "json", "xml", "yaml", "html", "md" })
            _supportedEdges.Add(new ConversionEdge("xlsx", dst, Id, 10));

        // Tabular formats -> XLSX
        foreach (var src in TabularFormats)
            _supportedEdges.Add(new ConversionEdge(src, "xlsx", Id, 12));

        // PPTX -> text
        foreach (var dst in new[] { "txt", "md", "html" })
            _supportedEdges.Add(new ConversionEdge("pptx", dst, Id, 10));
    }

    public bool IsAvailable(FlipSettings settings) => true;

    public Task<EngineExecutionResult> ConvertAsync(ConversionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            string srcPath = context.InputFilePath;
            string dstPath = context.OutputFilePath;

            if (!File.Exists(srcPath))
                return Task.FromResult(new EngineExecutionResult(false, "", $"File non trovato: {srcPath}"));

            string srcExt = (context.SourceExtension ?? Path.GetExtension(srcPath)).TrimStart('.').ToLowerInvariant();
            string dstExt = (context.TargetExtension ?? Path.GetExtension(dstPath)).TrimStart('.').ToLowerInvariant();

            string? dir = Path.GetDirectoryName(dstPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(dstPath)) File.Delete(dstPath);

            context.Progress?.Report(0.10);

            if (srcExt is "docx" && dstExt is "txt" or "md" or "html")
                ExtractDocx(srcPath, dstPath, dstExt);
            else if (srcExt is "odt" && dstExt is "txt" or "md" or "html")
                ExtractOdt(srcPath, dstPath, dstExt);
            else if (srcExt is "txt" or "md" or "html" && dstExt == "docx")
                GenerateDocx(srcPath, dstPath, srcExt);
            else if (DocAndTextFormats.Contains(srcExt) && DocAndTextFormats.Contains(dstExt))
                ConvertDocOrText(srcPath, srcExt, dstPath, dstExt);
            else if (srcExt is "txt" or "md" && dstExt is "json" or "csv")
                ConvertTextToData(srcPath, dstPath, dstExt);
            else if (srcExt == "xlsx" && dstExt is "csv" or "tsv" or "json" or "xml" or "yaml" or "html" or "md")
                ExtractXlsx(srcPath, dstPath, dstExt);
            else if (TabularFormats.Contains(srcExt) && dstExt == "xlsx")
                GenerateXlsx(srcPath, dstPath, srcExt);
            else if (srcExt == "pptx" && dstExt is "txt" or "md" or "html")
                ExtractPptx(srcPath, dstPath, dstExt);
            else
                return Task.FromResult(new EngineExecutionResult(false, "", $"Conversione OpenXML non supportata: {srcExt} -> {dstExt}"));

            context.Progress?.Report(1.0);
            long bytes = File.Exists(dstPath) ? new FileInfo(dstPath).Length : 0;
            return Task.FromResult(new EngineExecutionResult(true, dstPath, null, bytes));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(new EngineExecutionResult(false, "", "Conversione OpenXML annullata."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new EngineExecutionResult(false, "", $"Errore OpenXML: {ex.Message}"));
        }
    }

    private static void ExtractDocx(string docxPath, string outputPath, string format)
    {
        using var zip = ZipFile.OpenRead(docxPath);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new IOException("document.xml non trovato nel DOCX.");

        using var stream = entry.Open();
        var doc = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        var body = doc.Root?.Element(doc.Root.Name.Namespace + "body")
            ?? throw new IOException("Struttura DOCX non valida: body non trovato.");
        WriteDocument(outputPath, format, body, word: true);
    }

    private static void ExtractOdt(string odtPath, string outputPath, string format)
    {
        using var zip = ZipFile.OpenRead(odtPath);
        var entry = zip.GetEntry("content.xml")
            ?? throw new IOException("content.xml non trovato nel file ODT.");

        using var stream = entry.Open();
        var doc = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        var body = doc.Root?.Element(OdtOffice + "body")?.Element(OdtOffice + "text")
            ?? throw new IOException("Struttura ODT non valida: corpo del documento non trovato.");
        WriteDocument(outputPath, format, body, word: false);
    }

    private static void WriteDocument(string outputPath, string format, XElement body, bool word)
    {
        XNamespace textNamespace = word ? body.Name.Namespace : OdtText;
        XName paragraphName = textNamespace + "p";
        XName headingName = textNamespace + "h";
        XName tableName = word ? textNamespace + "tbl" : OdtTable + "table";
        var output = new StringBuilder();
        if (format == "html")
            output.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Documento</title></head><body>");

        AppendBlocks(body);
        if (format == "html") output.AppendLine("</body></html>");
        File.WriteAllText(outputPath, output.ToString(), new UTF8Encoding(false));

        void AppendBlocks(XElement container)
        {
            foreach (var block in container.Elements())
            {
                if (block.Name == tableName)
                {
                    var rows = ReadDocumentTable(block, textNamespace, word);
                    if (format == "html") AppendHtmlTable(output, rows);
                    else if (format == "md") AppendMarkdownTable(output, rows);
                    else
                    {
                        foreach (var row in rows)
                            output.AppendLine(string.Join('\t', row.Select(cell => cell.Replace("\n", " / ").Replace('\t', ' '))));
                        output.AppendLine();
                    }
                }
                else if (block.Name == paragraphName || block.Name == headingName)
                {
                    string text = ReadParagraph(block, textNamespace, word);
                    string style = (string?)block.Element(textNamespace + "pPr")?
                        .Element(textNamespace + "pStyle")?.Attribute(textNamespace + "val") ?? "";
                    bool heading = block.Name == headingName || style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
                        || style.StartsWith("Titolo", StringComparison.OrdinalIgnoreCase);
                    if (format == "html")
                    {
                        string tag = heading ? "h2" : "p";
                        output.AppendLine($"<{tag}>{HtmlText(text)}</{tag}>");
                    }
                    else
                    {
                        output.AppendLine(format == "md" && heading ? $"## {text}" : text);
                        if (format == "md") output.AppendLine();
                    }
                }
                else
                {
                    AppendBlocks(block);
                }
            }
        }
    }

    private static List<List<string>> ReadDocumentTable(XElement table, XNamespace textNamespace, bool word)
    {
        XNamespace tableNamespace = table.Name.Namespace;
        XName rowName = tableNamespace + (word ? "tr" : "table-row");
        XName cellName = tableNamespace + (word ? "tc" : "table-cell");
        var rows = new List<List<string>>();
        long cellCount = 0;
        foreach (var row in table.Descendants(rowName).Where(row => row.Ancestors(table.Name).First() == table))
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements())
            {
                if (cell.Name != cellName && (word || cell.Name != tableNamespace + "covered-table-cell"))
                    continue;
                string text = string.Join('\n', cell.Descendants().Where(element =>
                    element.Name == textNamespace + "p" || (!word && element.Name == textNamespace + "h"))
                    .Select(paragraph => ReadParagraph(paragraph, textNamespace, word)));
                int repeat = word ? 1 : ReadRepeat(cell, tableNamespace + "number-columns-repeated", MaxSpreadsheetColumns);
                if (cells.Count + repeat > MaxSpreadsheetColumns)
                    throw new IOException("La tabella contiene troppe colonne.");
                cells.AddRange(Enumerable.Repeat(text, repeat));
            }
            if (cells.Count == 0) continue;

            int repetitions = word ? 1 : ReadRepeat(row, tableNamespace + "number-rows-repeated", 1_000_000);
            cellCount += (long)cells.Count * repetitions;
            if (cellCount > 1_000_000) throw new IOException("La tabella contiene troppe celle.");
            rows.AddRange(Enumerable.Repeat(cells, repetitions));
        }
        return rows;
    }

    private static int ReadRepeat(XElement element, XName attributeName, int limit)
    {
        string? raw = (string?)element.Attribute(attributeName);
        if (raw is null) return 1;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1 || count > limit)
            throw new IOException($"Valore non valido per {attributeName.LocalName}.");
        return count;
    }

    private static string ReadParagraph(XElement paragraph, XNamespace ns, bool word)
    {
        var text = new StringBuilder();
        foreach (var node in paragraph.DescendantNodes())
        {
            if (node is XText run && (!word || run.Parent?.Name == ns + "t"))
                text.Append(run.Value);
            else if (node is XElement element)
            {
                if (element.Name == ns + "tab") text.Append('\t');
                else if (element.Name == ns + (word ? "br" : "line-break") || (word && element.Name == ns + "cr"))
                    text.Append('\n');
                else if (!word && element.Name == ns + "s")
                    text.Append(' ', ReadRepeat(element, ns + "c", 1_000_000));
            }
        }
        return text.ToString();
    }

    private static void AppendHtmlTable(StringBuilder output, List<List<string>> rows)
    {
        if (rows.Count == 0) return;
        output.AppendLine("<table>");
        foreach (var row in rows)
        {
            output.Append("<tr>");
            foreach (string cell in row)
                output.Append("<td>").Append(HtmlText(cell)).Append("</td>");
            output.AppendLine("</tr>");
        }
        output.AppendLine("</table>");
    }

    private static string HtmlText(string text) => System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>");

    private static void AppendMarkdownTable(StringBuilder output, List<List<string>> rows)
    {
        if (rows.Count == 0) return;
        int columns = rows.Max(row => row.Count);
        output.AppendLine();
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var cells = Enumerable.Range(0, columns).Select(i => i < row.Count ? EscapeMarkdownCell(row[i]) : "");
            output.AppendLine("| " + string.Join(" | ", cells) + " |");
            if (r == 0)
                output.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", columns)) + " |");
        }
        output.AppendLine();
    }

    private static string EscapeMarkdownCell(string value) => value
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\\", "\\\\").Replace("|", "\\|").Replace("\t", " ").Replace("\n", "<br>");

    // ── Text -> DOCX ──────────────────────────────────────────────────────────

    private static void GenerateDocx(string srcPath, string docxPath, string srcExt)
    {
        string raw = File.ReadAllText(srcPath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(raw)) throw new IOException("File sorgente vuoto.");

        string[] lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // Genera paragrafi Word
        var paragraphs = new List<XElement>();
        foreach (string line in lines)
        {
            bool isHeading = srcExt == "md" && line.StartsWith("# ");
            string text = isHeading ? line.TrimStart('#').Trim() : (srcExt == "html" ? StripHtmlTags(line) : line);

            var pProps = new List<XElement>();
            if (isHeading)
            {
                pProps.Add(new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", "Heading1"))));
            }

            var run = new XElement(W + "r", new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));
            var para = new XElement(W + "p");
            foreach (var p in pProps) para.Add(p);
            para.Add(run);
            paragraphs.Add(para);
        }

        // Assembla il pacchetto DOCX
        var documentXml = new XDocument(
            new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XElement(W + "body", paragraphs)));

        using var fs = new FileStream(docxPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        // [Content_Types].xml
        WriteZipEntry(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "</Types>");

        // _rels/.rels
        WriteZipEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "</Relationships>");

        // word/document.xml
        WriteZipEntry(zip, "word/document.xml", documentXml.ToString());
    }

    // ── XLSX -> Tabular ──────────────────────────────────────────────────────

    private static void ExtractXlsx(string xlsxPath, string outputPath, string format)
    {
        using var zip = ZipFile.OpenRead(xlsxPath);

        // Leggi shared strings
        var sharedStrings = new List<string>();
        var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (ssEntry != null)
        {
            using var ssStream = ssEntry.Open();
            var ssDoc = XDocument.Load(ssStream);
            foreach (var si in ssDoc.Descendants(Ssml + "si"))
            {
                sharedStrings.Add(string.Join("", si.Descendants(Ssml + "t").Where(t => t.Parent?.Name != Ssml + "rPh").Select(t => t.Value)));
            }
        }

        // Leggi il primo foglio
        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new IOException("Nessun foglio trovato nel file XLSX.");

        using var sheetStream = sheetEntry.Open();
        var sheetDoc = XDocument.Load(sheetStream);

        var rows = new List<List<string>>();
        foreach (var rowEl in sheetDoc.Descendants(Ssml + "row"))
        {
            var cells = new List<string>();
            var occupied = new HashSet<int>();
            int nextColumn = 0;
            foreach (var cell in rowEl.Elements(Ssml + "c"))
            {
                string type = cell.Attribute("t")?.Value ?? "";
                string rawVal = cell.Element(Ssml + "v")?.Value ?? "";

                string resolved;
                if (type == "s")
                {
                    if (!int.TryParse(rawVal, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                        || index >= sharedStrings.Count)
                        throw new IOException("Indice di sharedStrings non valido.");
                    resolved = sharedStrings[index];
                }
                else if (type == "inlineStr")
                    resolved = string.Concat(cell.Element(Ssml + "is")?.Descendants(Ssml + "t")
                        .Where(t => t.Parent?.Name != Ssml + "rPh").Select(t => t.Value) ?? []);
                else
                    resolved = rawVal;

                string? reference = (string?)cell.Attribute("r");
                int column = reference is null ? nextColumn : ReadColumnIndex(reference);
                if (column >= MaxSpreadsheetColumns || !occupied.Add(column))
                    throw new IOException($"Colonna XLSX duplicata o fuori limite: {reference ?? (column + 1).ToString()}.");
                while (cells.Count <= column)
                    cells.Add("");
                cells[column] = resolved;
                nextColumn = column + 1;
            }
            rows.Add(cells);
        }

        if (rows.Count == 0) throw new IOException("Foglio Excel vuoto.");

        var utf8 = new UTF8Encoding(false);
        var sb = new StringBuilder();

        switch (format)
        {
            case "csv":
                foreach (var row in rows)
                    sb.AppendLine(string.Join(',', row.Select(cell => EscapeDelimited(cell, ','))));
                break;

            case "tsv":
                foreach (var row in rows)
                    sb.AppendLine(string.Join('\t', row.Select(cell => EscapeDelimited(cell, '\t'))));
                break;

            case "json":
                var headers = new List<string>();
                var used = new HashSet<string>(StringComparer.Ordinal);
                int columns = rows.Max(row => row.Count);
                for (int c = 0; c < columns; c++)
                {
                    string heading = c < rows[0].Count && !string.IsNullOrWhiteSpace(rows[0][c])
                        ? rows[0][c] : $"col_{c + 1}";
                    string uniqueHeading = heading;
                    for (int suffix = 2; !used.Add(uniqueHeading); suffix++)
                        uniqueHeading = $"{heading}_{suffix}";
                    headers.Add(uniqueHeading);
                }

                var records = rows.Skip(1).Select(row => headers.Select((header, index) => (header, index))
                    .ToDictionary(item => item.header, item => item.index < row.Count ? row[item.index] : ""));
                sb.AppendLine(JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
                break;

            case "md":
                AppendMarkdownTable(sb, rows);
                break;

            case "xml":
                {
                    var headersXml = BuildSheetHeaders(rows);
                    var rootXml = new XElement("rows");
                    foreach (var row in rows.Skip(1))
                    {
                        var rowEl = new XElement("row");
                        for (int c = 0; c < headersXml.Count; c++)
                        {
                            string tag = Regex.Replace(headersXml[c], @"[^a-zA-Z0-9_\-]", "_");
                            if (string.IsNullOrEmpty(tag) || char.IsDigit(tag[0])) tag = "c_" + tag;
                            rowEl.Add(new XElement(tag, c < row.Count ? row[c] : ""));
                        }
                        rootXml.Add(rowEl);
                    }
                    sb.AppendLine(new XDocument(new XDeclaration("1.0", "utf-8", null), rootXml).ToString());
                }
                break;

            case "yaml":
                {
                    var headersYaml = BuildSheetHeaders(rows);
                    foreach (var row in rows.Skip(1))
                    {
                        for (int c = 0; c < headersYaml.Count; c++)
                        {
                            string prefix = c == 0 ? "- " : "  ";
                            string val = c < row.Count ? row[c] : "";
                            string escaped = val.Replace("\\", "\\\\").Replace("\"", "\\\"");
                            sb.AppendLine($"{prefix}{headersYaml[c]}: \"{escaped}\"");
                        }
                    }
                }
                break;

            case "html":
                sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>XLSX</title>");
                sb.AppendLine("<style>body{font-family:sans-serif;margin:24px;background:#18181b;color:#f4f4f5;} table{border-collapse:collapse;width:100%;} th,td{border:1px solid #3f3f46;padding:8px;text-align:left;} th{background:#27272a;}</style>");
                sb.AppendLine("</head><body><table><thead><tr>");
                if (rows.Count > 0)
                {
                    foreach (var h in rows[0])
                        sb.Append($"<th>{System.Net.WebUtility.HtmlEncode(h)}</th>");
                    sb.AppendLine("</tr></thead><tbody>");
                    for (int r = 1; r < rows.Count; r++)
                    {
                        sb.Append("<tr>");
                        foreach (var c in rows[r]) sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(c)}</td>");
                        sb.AppendLine("</tr>");
                    }
                }
                sb.AppendLine("</tbody></table></body></html>");
                break;
        }

        File.WriteAllText(outputPath, sb.ToString(), utf8);
    }

    // ── Tabular -> XLSX ──────────────────────────────────────────────────────

    private static void GenerateXlsx(string srcPath, string xlsxPath, string srcExt)
    {
        string raw = File.ReadAllText(srcPath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(raw)) throw new IOException("File sorgente vuoto.");

        // Parsing rows
        List<List<string>> rows;
        if (srcExt == "json")
        {
            rows = ParseJsonToRows(raw);
        }
        else if (srcExt is "yaml" or "yml" or "xml")
        {
            string tempJson = DataSerializationEngine.ConvertRawToJson(raw, srcExt);
            rows = ParseJsonToRows(tempJson);
        }
        else
        {
            char delim = srcExt == "tsv" ? '\t' : ',';
            rows = ParseDelimitedRows(raw, delim);
        }

        if (rows.Count == 0) throw new IOException("Nessun dato da convertire in XLSX.");

        // Shared Strings
        var allStrings = rows.SelectMany(r => r).Distinct().ToList();
        var stringIndex = new Dictionary<string, int>();
        for (int i = 0; i < allStrings.Count; i++) stringIndex[allStrings[i]] = i;

        // Build sheet XML
        var sheetRows = new List<XElement>();
        for (int r = 0; r < rows.Count; r++)
        {
            var cells = new List<XElement>();
            for (int c = 0; c < rows[r].Count; c++)
            {
                string cellRef = $"{ColumnName(c)}{r + 1}";
                int si = stringIndex[rows[r][c]];
                cells.Add(new XElement(Ssml + "c",
                    new XAttribute("r", cellRef),
                    new XAttribute("t", "s"),
                    new XElement(Ssml + "v", si)));
            }
            sheetRows.Add(new XElement(Ssml + "row", new XAttribute("r", r + 1), cells));
        }

        var worksheetDoc = new XDocument(
            new XElement(Ssml + "worksheet",
                new XElement(Ssml + "sheetData", sheetRows)));

        var sharedStringsDoc = new XDocument(
            new XElement(Ssml + "sst",
                new XAttribute("count", allStrings.Count),
                new XAttribute("uniqueCount", allStrings.Count),
                allStrings.Select(s => new XElement(Ssml + "si", new XElement(Ssml + "t", s)))));

        using var fs = new FileStream(xlsxPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        WriteZipEntry(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
            "</Types>");

        WriteZipEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");

        WriteZipEntry(zip, "xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");

        WriteZipEntry(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>" +
            "</Relationships>");

        WriteZipEntry(zip, "xl/worksheets/sheet1.xml", worksheetDoc.ToString());
        WriteZipEntry(zip, "xl/sharedStrings.xml", sharedStringsDoc.ToString());
    }

    // ── PPTX -> Text ──────────────────────────────────────────────────────────

    private static void ExtractPptx(string pptxPath, string outputPath, string format)
    {
        using var zip = ZipFile.OpenRead(pptxPath);

        // Trova tutte le slide ordinate per numero
        var slideEntries = zip.Entries
            .Where(e => Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$", RegexOptions.IgnoreCase))
            .OrderBy(e =>
            {
                var m = Regex.Match(e.Name, @"slide(\d+)\.xml", RegexOptions.IgnoreCase);
                return m.Success ? int.Parse(m.Groups[1].Value) : 0;
            })
            .ToList();

        if (slideEntries.Count == 0) throw new IOException("Nessuna slide trovata nel PPTX.");

        var slides = new List<(int Num, string Text)>();
        int num = 0;
        foreach (var entry in slideEntries)
        {
            num++;
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);

            // Estrai tutti i testi dagli shape delle slide
            var texts = doc.Descendants(A + "t").Select(t => t.Value);
            string slideText = string.Join(" ", texts).Trim();
            slides.Add((num, slideText));
        }

        var utf8 = new UTF8Encoding(false);
        var sb = new StringBuilder();

        switch (format)
        {
            case "txt":
                foreach (var (n, text) in slides)
                {
                    sb.AppendLine($"--- Slide {n} ---");
                    sb.AppendLine(text);
                    sb.AppendLine();
                }
                break;

            case "md":
                foreach (var (n, text) in slides)
                {
                    sb.AppendLine($"## Slide {n}");
                    sb.AppendLine();
                    sb.AppendLine(text);
                    sb.AppendLine();
                }
                break;

            case "html":
                sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Presentazione</title></head><body>");
                foreach (var (n, text) in slides)
                {
                    sb.AppendLine($"<h2>Slide {n}</h2>");
                    sb.AppendLine($"<p>{System.Net.WebUtility.HtmlEncode(text)}</p>");
                }
                sb.AppendLine("</body></html>");
                break;
        }

        File.WriteAllText(outputPath, sb.ToString(), utf8);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void WriteZipEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string EscapeDelimited(string s, char delimiter)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(delimiter) || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    private static int ReadColumnIndex(string reference)
    {
        int index = 0;
        int letters = 0;
        while (letters < reference.Length && reference[letters] is >= 'A' and <= 'Z')
        {
            index = index * 26 + reference[letters++] - 'A' + 1;
            if (index > MaxSpreadsheetColumns) break;
        }
        if (letters == 0 || index > MaxSpreadsheetColumns ||
            !int.TryParse(reference.AsSpan(letters), NumberStyles.None, CultureInfo.InvariantCulture, out int row) ||
            row is < 1 or > 1_048_576)
            throw new IOException($"Riferimento cella XLSX non valido: {reference}.");
        return index - 1;
    }

    private static string ColumnName(int index)
    {
        if (index >= MaxSpreadsheetColumns)
            throw new IOException("Il foglio supera il limite di 16.384 colonne.");
        Span<char> letters = stackalloc char[3];
        int cursor = letters.Length;
        for (int column = index + 1; column > 0; column = (column - 1) / 26)
            letters[--cursor] = (char)('A' + (column - 1) % 26);
        return new string(letters[cursor..]);
    }

    private static string StripHtmlTags(string html) =>
        Regex.Replace(html, @"<[^>]+>", "").Trim();

    private static List<List<string>> ParseDelimitedRows(string text, char delimiter)
    {
        var result = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cell.Append(ch);
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == delimiter) { row.Add(cell.ToString()); cell.Clear(); }
                else if (ch is '\r' or '\n')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Clear(); result.Add(row); row = [];
                }
                else cell.Append(ch);
            }
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); result.Add(row); }
        return result;
    }

    private static List<List<string>> ParseJsonToRows(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new IOException("JSON deve essere un array di oggetti per la conversione in XLSX.");

        var rows = new List<List<string>>();
        var headers = new List<string>();
        bool headersSet = false;

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

            if (!headersSet)
            {
                foreach (var prop in item.EnumerateObject())
                    headers.Add(prop.Name);
                rows.Add([.. headers]);
                headersSet = true;
            }

            var row = new List<string>();
            foreach (var h in headers)
            {
                if (item.TryGetProperty(h, out var val))
                    row.Add(val.ValueKind == System.Text.Json.JsonValueKind.String ? (val.GetString() ?? "") : val.GetRawText());
                else
                    row.Add("");
            }
            rows.Add(row);
        }

        return rows;
    }

    private static List<string> BuildSheetHeaders(List<List<string>> rows)
    {
        var headers = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        int columns = rows.Max(row => row.Count);
        for (int c = 0; c < columns; c++)
        {
            string heading = c < rows[0].Count && !string.IsNullOrWhiteSpace(rows[0][c])
                ? rows[0][c] : $"col_{c + 1}";
            string uniqueHeading = heading;
            for (int suffix = 2; !used.Add(uniqueHeading); suffix++)
                uniqueHeading = $"{heading}_{suffix}";
            headers.Add(uniqueHeading);
        }
        return headers;
    }

    // ── Conversione universale Document & Text (TXT/MD/HTML/DOCX/ODT/RTF) ────

    internal static string ExtractPlainTextForPdf(string srcPath, string srcExt)
    {
        string ext = srcExt.TrimStart('.').ToLowerInvariant();
        if (ext is "docx" or "odt" or "rtf" or "html" or "md" or "txt")
        {
            var blocks = ReadStructuredBlocks(srcPath, ext);
            return string.Join("\n", blocks.Select(b => b.IsHeading ? b.Text.ToUpperInvariant() : b.Text));
        }
        if (ext == "xlsx")
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"flip-xlsx-txt-{Guid.NewGuid():N}.tsv");
            try
            {
                ExtractXlsx(srcPath, tmp, "tsv");
                return File.ReadAllText(tmp, Encoding.UTF8);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }
        return File.ReadAllText(srcPath, Encoding.UTF8);
    }

    internal static void WriteFromPlainText(string fullText, string dstPath, string dstExt)
    {
        string[] lines = fullText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = lines.Select(line => (IsHeading: false, Text: line)).ToList();
        WriteStructuredBlocks(blocks, dstPath, dstExt.TrimStart('.').ToLowerInvariant());
    }

    private static void ConvertDocOrText(string srcPath, string srcExt, string dstPath, string dstExt)
    {
        var blocks = ReadStructuredBlocks(srcPath, srcExt);
        if (blocks.Count == 0 || blocks.All(b => string.IsNullOrWhiteSpace(b.Text)))
            throw new IOException("File sorgente vuoto o privo di testo.");
        WriteStructuredBlocks(blocks, dstPath, dstExt);
    }

    private static List<(bool IsHeading, string Text)> ReadStructuredBlocks(string srcPath, string srcExt)
    {
        if (srcExt is "docx" or "odt")
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"flip-doc-md-{Guid.NewGuid():N}.md");
            try
            {
                if (srcExt == "docx") ExtractDocx(srcPath, tmp, "md");
                else ExtractOdt(srcPath, tmp, "md");
                return ReadMarkdownBlocks(File.ReadAllText(tmp, Encoding.UTF8));
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        string raw = File.ReadAllText(srcPath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(raw))
            throw new IOException("File sorgente vuoto.");

        return srcExt switch
        {
            "md" => ReadMarkdownBlocks(raw),
            "html" => ReadHtmlBlocks(raw),
            "rtf" => ReadRtfBlocks(raw),
            _ => raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Select(l => (IsHeading: false, Text: l)).ToList()
        };
    }

    private static List<(bool IsHeading, string Text)> ReadMarkdownBlocks(string raw)
    {
        var list = new List<(bool IsHeading, string Text)>();
        foreach (string line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string trimmed = line.TrimEnd();
            if (trimmed.StartsWith('#'))
            {
                string heading = trimmed.TrimStart('#').Trim();
                if (heading.Length > 0) list.Add((true, heading));
            }
            else
            {
                list.Add((false, trimmed));
            }
        }
        return list;
    }

    private static List<(bool IsHeading, string Text)> ReadHtmlBlocks(string raw)
    {
        string cleaned = Regex.Replace(raw, @"<(script|style|head)\b[^>]*>.*?</\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var list = new List<(bool IsHeading, string Text)>();
        var blockMatches = Regex.Matches(cleaned, @"<(h[1-6]|p|li|div|tr|pre)\b[^>]*>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (blockMatches.Count > 0)
        {
            foreach (Match m in blockMatches)
            {
                string tag = m.Groups[1].Value.ToLowerInvariant();
                string inner = Regex.Replace(m.Groups[2].Value, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
                string text = System.Net.WebUtility.HtmlDecode(StripHtmlTags(inner)).Trim();
                if (text.Length > 0)
                    list.Add((tag.StartsWith('h'), text));
            }
        }
        if (list.Count == 0)
        {
            string fallback = System.Net.WebUtility.HtmlDecode(StripHtmlTags(cleaned)).Trim();
            foreach (string line in fallback.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                list.Add((false, line));
        }
        return list;
    }

    private static List<(bool IsHeading, string Text)> ReadRtfBlocks(string rtf)
    {
        // Rimuovi i gruppi RTF di intestazione (\fonttbl, \colortbl, \stylesheet, \info, \*\...)
        string text = Regex.Replace(rtf, @"\{\\\*\\[^{}]*(?:\{[^{}]*\}[^{}]*)*\}", "");
        text = Regex.Replace(text, @"\{\\(?:fonttbl|colortbl|stylesheet|info|pict)\b[^{}]*(?:\{[^{}]*\}[^{}]*)*\}", "");
        text = Regex.Replace(text, @"\\(?:par|line|row)\b\s?", "\n");
        text = Regex.Replace(text, @"\\tab\b\s?", "\t");
        text = Regex.Replace(text, @"\\'([0-9a-fA-F]{2})", m =>
        {
            byte b = Convert.ToByte(m.Groups[1].Value, 16);
            return ((char)b).ToString();
        });
        text = Regex.Replace(text, @"\\u(-?\d+)\??", m =>
        {
            int code = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (code < 0) code += 65536;
            return char.ConvertFromUtf32(Math.Clamp(code, 0, 0x10FFFF));
        });
        text = Regex.Replace(text, @"\\[a-zA-Z]+-?\d*\s?", "");
        text = text.Replace("{", "").Replace("}", "");
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(l => (IsHeading: false, Text: l.TrimEnd())).ToList();
    }

    private static void WriteStructuredBlocks(List<(bool IsHeading, string Text)> blocks, string dstPath, string dstExt)
    {
        var utf8 = new UTF8Encoding(false);
        switch (dstExt)
        {
            case "txt":
                File.WriteAllText(dstPath, string.Join("\n", blocks.Select(b => b.Text)), utf8);
                break;

            case "md":
                {
                    var sb = new StringBuilder();
                    bool wroteTitle = false;
                    foreach (var (isHeading, text) in blocks)
                    {
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            sb.AppendLine();
                            continue;
                        }
                        if (isHeading || (!wroteTitle && blocks.Count > 1 && !text.StartsWith('#') && text.Length < 80))
                        {
                            sb.AppendLine($"## {text.TrimStart('#').Trim()}");
                            sb.AppendLine();
                            wroteTitle = true;
                        }
                        else
                        {
                            sb.AppendLine(text);
                            wroteTitle = true;
                        }
                    }
                    File.WriteAllText(dstPath, sb.ToString().TrimEnd() + "\n", utf8);
                }
                break;

            case "html":
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Documento</title></head><body>");
                    foreach (var (isHeading, text) in blocks)
                    {
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        string tag = isHeading ? "h2" : "p";
                        sb.AppendLine($"<{tag}>{HtmlText(text)}</{tag}>");
                    }
                    sb.AppendLine("</body></html>");
                    File.WriteAllText(dstPath, sb.ToString(), utf8);
                }
                break;

            case "docx":
                {
                    string tmp = Path.Combine(Path.GetTempPath(), $"flip-gen-docx-{Guid.NewGuid():N}.md");
                    try
                    {
                        WriteStructuredBlocks(blocks, tmp, "md");
                        GenerateDocx(tmp, dstPath, "md");
                    }
                    finally
                    {
                        if (File.Exists(tmp)) File.Delete(tmp);
                    }
                }
                break;

            case "odt":
                GenerateOdt(blocks, dstPath);
                break;

            case "rtf":
                GenerateRtf(blocks, dstPath);
                break;

            default:
                throw new NotSupportedException($"Formato documento non supportato: {dstExt}");
        }
    }

    private static void GenerateOdt(List<(bool IsHeading, string Text)> blocks, string odtPath)
    {
        XNamespace manifestNs = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
        var bodyElements = new List<XElement>();
        foreach (var (isHeading, text) in blocks)
        {
            if (isHeading)
                bodyElements.Add(new XElement(OdtText + "h", new XAttribute(OdtText + "outline-level", "2"), text));
            else
                bodyElements.Add(new XElement(OdtText + "p", text));
        }

        var contentDoc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(OdtOffice + "document-content",
                new XAttribute(XNamespace.Xmlns + "office", OdtOffice.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "text", OdtText.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "table", OdtTable.NamespaceName),
                new XAttribute(OdtOffice + "version", "1.2"),
                new XElement(OdtOffice + "body",
                    new XElement(OdtOffice + "text", bodyElements))));

        var manifestDoc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(manifestNs + "manifest",
                new XAttribute(XNamespace.Xmlns + "manifest", manifestNs.NamespaceName),
                new XAttribute(manifestNs + "version", "1.2"),
                new XElement(manifestNs + "file-entry",
                    new XAttribute(manifestNs + "full-path", "/"),
                    new XAttribute(manifestNs + "media-type", "application/vnd.oasis.opendocument.text")),
                new XElement(manifestNs + "file-entry",
                    new XAttribute(manifestNs + "full-path", "content.xml"),
                    new XAttribute(manifestNs + "media-type", "text/xml"))));

        using var fs = new FileStream(odtPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var mimeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var s = mimeEntry.Open())
        using (var w = new StreamWriter(s, Encoding.ASCII))
            w.Write("application/vnd.oasis.opendocument.text");

        WriteZipEntry(zip, "META-INF/manifest.xml", manifestDoc.ToString());
        WriteZipEntry(zip, "content.xml", contentDoc.ToString());
    }

    private static void GenerateRtf(List<(bool IsHeading, string Text)> blocks, string rtfPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\viewkind4\uc1\pard\f0\fs22");
        foreach (var (isHeading, text) in blocks)
        {
            if (isHeading) sb.Append(@"\b\fs28 ");
            foreach (char c in text)
            {
                if (c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c);
                else if (c <= 0x7F) sb.Append(c);
                else sb.Append($@"\u{(short)c}?");
            }
            if (isHeading) sb.Append(@"\b0\fs22");
            sb.AppendLine(@"\par");
        }
        sb.Append('}');
        File.WriteAllText(rtfPath, sb.ToString(), Encoding.ASCII);
    }

    private static void ConvertTextToData(string srcPath, string dstPath, string dstExt)
    {
        string raw = File.ReadAllText(srcPath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(raw)) throw new IOException("File di testo vuoto.");
        string[] lines = raw.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var utf8 = new UTF8Encoding(false);
        if (dstExt == "json")
        {
            var payload = lines.Select((line, index) => new { line = index + 1, text = line }).ToArray();
            File.WriteAllText(dstPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), utf8);
        }
        else if (dstExt == "csv")
        {
            var sb = new StringBuilder();
            sb.AppendLine("line,text");
            for (int i = 0; i < lines.Length; i++)
                sb.AppendLine($"{i + 1},{EscapeDelimited(lines[i], ',')}");
            File.WriteAllText(dstPath, sb.ToString(), utf8);
        }
    }
}

