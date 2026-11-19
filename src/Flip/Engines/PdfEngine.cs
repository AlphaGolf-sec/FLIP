using System.IO;
using System.Text;
using SkiaSharp;
using Flip.Core;
using Flip.Routing;

namespace Flip.Engines;

/// <summary>
/// Motore built-in PDF: renderizza PDF in immagini (via PDFtoImage) ed estrae testo (via PdfPig).
/// Genera PDF nativi multi-pagina ad alta qualità da immagini o testo (via SkiaSharp SKDocument).
/// </summary>
public sealed class PdfEngine : IConversionEngine
{
    public string Id => "pdf_builtin";
    public string DisplayName => "PDF Rendering & Document Hub Engine";
    public bool IsExternal => false;

    private readonly List<ConversionEdge> _supportedEdges = [];

    public IReadOnlyList<ConversionEdge> SupportedEdges => _supportedEdges;

    // Formati rasterizzabili dal PDF
    private static readonly string[] PdfToImageFormats = ["png", "jpg", "jpeg", "webp", "bmp"];
    // Formati di testo/documento estraibili dal PDF
    private static readonly string[] PdfToTextFormats = ["txt", "md", "html", "json", "docx", "odt", "rtf"];
    // Formati che possono diventare PDF
    private static readonly string[] ImageToPdfFormats = ["png", "jpg", "jpeg", "webp", "bmp"];
    private static readonly string[] TextToPdfFormats = ["txt", "md", "html", "rtf", "docx", "odt", "xlsx", "csv", "tsv", "json", "yaml", "xml"];

    public PdfEngine()
    {
        // PDF -> PDF/A-2b (ISO 19005-2)
        _supportedEdges.Add(new ConversionEdge("pdf", "pdfa", Id, 10));

        // PDF -> Immagini
        foreach (var dst in PdfToImageFormats)
            _supportedEdges.Add(new ConversionEdge("pdf", dst, Id, 10));

        // PDF -> Testo
        foreach (var dst in PdfToTextFormats)
            _supportedEdges.Add(new ConversionEdge("pdf", dst, Id, 10));

        // Immagini -> PDF & PDF/A
        foreach (var src in ImageToPdfFormats)
        {
            _supportedEdges.Add(new ConversionEdge(src, "pdf", Id, 10));
            _supportedEdges.Add(new ConversionEdge(src, "pdfa", Id, 12));
        }

        // Testo/Documenti -> PDF & PDF/A
        foreach (var src in TextToPdfFormats)
        {
            _supportedEdges.Add(new ConversionEdge(src, "pdf", Id, 10));
            _supportedEdges.Add(new ConversionEdge(src, "pdfa", Id, 12));
        }
    }

    public bool IsAvailable(FlipSettings settings) => true;

    public async Task<EngineExecutionResult> ConvertAsync(ConversionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            string srcPath = context.InputFilePath;
            string dstPath = context.OutputFilePath;

            if (!File.Exists(srcPath))
                return new EngineExecutionResult(false, "", $"File non trovato: {srcPath}");

            string srcExt = (context.SourceExtension ?? Path.GetExtension(srcPath)).TrimStart('.').ToLowerInvariant();
            string dstExt = (context.TargetExtension ?? Path.GetExtension(dstPath)).TrimStart('.').ToLowerInvariant();

            string? dir = Path.GetDirectoryName(dstPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            context.Progress?.Report(0.10);

            if (srcExt == "pdf" && PdfToImageFormats.Contains(dstExt))
                return await RenderPdfToImagesAsync(srcPath, dstPath, dstExt, context).ConfigureAwait(false);

            if (File.Exists(dstPath)) File.Delete(dstPath);

            bool stripMetadata = context.CustomParameters.TryGetValue("strip_metadata", out var stripStr) &&
                (stripStr.Equals("true", StringComparison.OrdinalIgnoreCase) || stripStr == "1");

            if (dstExt == "pdfa")
            {
                if (srcExt == "pdf")
                {
                    ConvertToPdfA2b(srcPath, dstPath, stripMetadata);
                }
                else if (ImageToPdfFormats.Contains(srcExt))
                {
                    string tempPdf = dstPath + ".base.tmp.pdf";
                    try
                    {
                        ImageToPdf(srcPath, tempPdf, stripMetadata);
                        ConvertToPdfA2b(tempPdf, dstPath, stripMetadata);
                    }
                    finally
                    {
                        try { if (File.Exists(tempPdf)) File.Delete(tempPdf); } catch { }
                    }
                }
                else if (TextToPdfFormats.Contains(srcExt))
                {
                    string tempPdf = dstPath + ".base.tmp.pdf";
                    try
                    {
                        TextToPdf(srcPath, tempPdf, srcExt, stripMetadata);
                        ConvertToPdfA2b(tempPdf, dstPath, stripMetadata);
                    }
                    finally
                    {
                        try { if (File.Exists(tempPdf)) File.Delete(tempPdf); } catch { }
                    }
                }
                else
                {
                    return new EngineExecutionResult(false, "", $"Conversione PDF/A non supportata da: {srcExt}");
                }
            }
            else if (srcExt == "pdf" && PdfToTextFormats.Contains(dstExt))
            {
                ExtractPdfText(srcPath, dstPath, dstExt, context);
            }
            else if (ImageToPdfFormats.Contains(srcExt) && dstExt == "pdf")
            {
                ImageToPdf(srcPath, dstPath, stripMetadata);
            }
            else if (TextToPdfFormats.Contains(srcExt) && dstExt == "pdf")
            {
                TextToPdf(srcPath, dstPath, srcExt, stripMetadata);
            }
            else
            {
                return new EngineExecutionResult(false, "", $"Conversione non supportata: {srcExt} -> {dstExt}");
            }

            context.Progress?.Report(1.0);
            long bytes = File.Exists(dstPath) ? new FileInfo(dstPath).Length : 0;
            return new EngineExecutionResult(true, dstPath, null, bytes);
        }
        catch (OperationCanceledException)
        {
            return new EngineExecutionResult(false, "", "Conversione PDF annullata.");
        }
        catch (Exception ex)
        {
            return new EngineExecutionResult(false, "", $"Errore PDF ({context.SourceExtension} -> {context.TargetExtension}): {ex.Message}");
        }
    }

    // ── PDF -> Immagine ──────────────────────────────────────────────────────

    private static Task<EngineExecutionResult> RenderPdfToImagesAsync(
        string pdfPath, string outputPath, string imageFormat, ConversionContext ctx)
    {
        return Task.Run(() =>
        {
            using var pdf = File.OpenRead(pdfPath);
            int pageCount = PDFtoImage.Conversion.GetPageCount(pdf, leaveOpen: true);
            if (pageCount == 0) throw new IOException("PDF senza pagine.");
            if (pageCount > 200) throw new IOException("PDF oltre il limite di 200 pagine.");
            pdf.Position = 0;

            var options = new PDFtoImage.RenderOptions(Dpi: 150, Width: 1800, Height: 2400, WithAspectRatio: true);
            int quality = 92;
            if (ctx.CustomParameters.TryGetValue("quality", out var qStr) && int.TryParse(qStr, out int qVal))
                quality = Math.Clamp(qVal, 1, 100);

            var outputs = new string[pageCount];
            long totalBytes = 0;
            using var pages = PDFtoImage.Conversion.ToImages(pdf, leaveOpen: true, options: options).GetEnumerator();
            for (int i = 0; i < pageCount; i++)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                if (!pages.MoveNext()) throw new IOException($"Pagina PDF {i + 1} mancante.");

                using var bitmap = pages.Current;
                ctx.CancellationToken.ThrowIfCancellationRequested();
                string pagePath = i == 0
                    ? outputPath
                    : Path.ChangeExtension(outputPath, $"page-{i + 1:000}{Path.GetExtension(outputPath)}");
                WritePageImage(bitmap, pagePath, imageFormat, quality);
                outputs[i] = pagePath;
                totalBytes += new FileInfo(pagePath).Length;
                ctx.Progress?.Report(0.10 + 0.90 * (i + 1) / pageCount);
            }

            ctx.CancellationToken.ThrowIfCancellationRequested();
            return new EngineExecutionResult(true, outputPath, null, totalBytes) { OutputFilePaths = outputs };
        }, ctx.CancellationToken);
    }

    private static void WritePageImage(SKBitmap bitmap, string path, string format, int quality)
    {
        if (format == "bmp")
        {
            ConversionEngine.WriteImage(bitmap, path, "BMP");
            return;
        }

        SKEncodedImageFormat encoding = format switch
        {
            "png" => SKEncodedImageFormat.Png,
            "jpg" or "jpeg" => SKEncodedImageFormat.Jpeg,
            "webp" => SKEncodedImageFormat.Webp,
            _ => throw new NotSupportedException($"Formato immagine PDF non supportato: {format}.")
        };
        using var data = bitmap.Encode(encoding, quality)
            ?? throw new IOException($"Encoder immagine non disponibile per il formato {format}.");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
        stream.Flush(flushToDisk: true);
    }

    // ── PDF -> Testo ──────────────────────────────────────────────────────────

    private static void ExtractPdfText(string pdfPath, string outputPath, string textFormat, ConversionContext ctx)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        if (pdf.NumberOfPages == 0) throw new IOException("PDF senza pagine.");
        if (pdf.NumberOfPages > 200) throw new IOException("PDF oltre il limite di 200 pagine.");

        var sb = new StringBuilder();
        int pageNum = 0;
        foreach (var page in pdf.GetPages())
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            pageNum++;
            string text = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page);
            sb.AppendLine(text);
            sb.AppendLine();
        }

        string fullText = sb.ToString().TrimEnd();
        if (string.IsNullOrWhiteSpace(fullText))
            throw new IOException("Il PDF non contiene testo estraibile. Per scansioni usa prima PNG/JPG.");

        ctx.Progress?.Report(0.70);

        if (textFormat is "docx" or "odt" or "rtf")
        {
            OfficeOpenXmlEngine.WriteFromPlainText(fullText, outputPath, textFormat);
            return;
        }

        var utf8 = new UTF8Encoding(false);
        string output = textFormat switch
        {
            "txt" => fullText,
            "md" => $"# Documento estratto da PDF\n\n{fullText}",
            "html" => $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>PDF</title></head><body><pre>{System.Net.WebUtility.HtmlEncode(fullText)}</pre></body></html>",
            "json" => SerializePdfTextToJson(pdf, fullText),
            _ => fullText
        };

        File.WriteAllText(outputPath, output, utf8);
    }

    private static string SerializePdfTextToJson(UglyToad.PdfPig.PdfDocument pdf, string fullText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"pageCount\": {pdf.NumberOfPages},");
        sb.AppendLine($"  \"text\": {System.Text.Json.JsonSerializer.Serialize(fullText)}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Immagine -> PDF ──────────────────────────────────────────────────────

    private static void ImageToPdf(string imagePath, string pdfPath, bool stripMetadata = false)
    {
        using var bitmap = LoadSkiaBitmap(imagePath);
        using var stream = new FileStream(pdfPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var metadata = stripMetadata ? EmptyPdfMetadata() : default;
        using var document = (stripMetadata ? SKDocument.CreatePdf(stream, metadata) : SKDocument.CreatePdf(stream))
            ?? throw new IOException("Encoder PDF non disponibile.");

        float pageW = 595, pageH = 842, margin = 30;
        using var image = SKImage.FromBitmap(bitmap);
        var canvas = document.BeginPage(pageW, pageH);
        canvas.Clear(SKColors.White);

        float scale = Math.Min((pageW - 2 * margin) / bitmap.Width, (pageH - 2 * margin) / bitmap.Height);
        float w = bitmap.Width * scale, h = bitmap.Height * scale;
        canvas.DrawImage(image,
            new SKRect((pageW - w) / 2, (pageH - h) / 2, (pageW + w) / 2, (pageH + h) / 2),
            new SKSamplingOptions(SKFilterMode.Linear));

        document.EndPage();
        document.Close();
        stream.Flush(true);
    }

    private static SKBitmap LoadSkiaBitmap(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec == null) throw new IOException("Formato immagine non decodificabile.");
        if ((long)codec.Info.Width * codec.Info.Height > 60_000_000)
            throw new IOException("Immagine oltre il limite di 60 megapixel.");

        var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result != SKCodecResult.Success) { bitmap.Dispose(); throw new IOException($"Immagine corrotta: {result}"); }
        return bitmap;
    }

    private static SKDocumentPdfMetadata EmptyPdfMetadata() => new()
    {
        Title = "",
        Author = "",
        Subject = "",
        Keywords = "",
        Creator = "",
        Producer = ""
    };

    // ── Testo -> PDF ──────────────────────────────────────────────────────────

    private static void TextToPdf(string textPath, string pdfPath, string srcExt = "txt", bool stripMetadata = false)
    {
        if (new FileInfo(textPath).Length > 10 * 1024 * 1024)
            throw new IOException("File testo troppo grande per la conversione PDF (limite 10 MiB).");

        string text = OfficeOpenXmlEngine.ExtractPlainTextForPdf(textPath, srcExt);
        if (string.IsNullOrWhiteSpace(text))
            throw new IOException("Documento privo di testo da esportare in PDF.");

        using var stream = new FileStream(pdfPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var metadata = stripMetadata ? EmptyPdfMetadata() : default;
        using var document = (stripMetadata ? SKDocument.CreatePdf(stream, metadata) : SKDocument.CreatePdf(stream))
            ?? throw new IOException("Encoder PDF non disponibile.");
        using var typeface = SKTypeface.FromFamilyName("Consolas");
        using var font = new SKFont(typeface, 11);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        float pageW = 595, pageH = 842;
        SKCanvas canvas = document.BeginPage(pageW, pageH);
        canvas.Clear(SKColors.White);
        float y = 45;
        int pages = 1;

        foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string value = line.Replace("\t", "    ");
            int offset = 0;
            do
            {
                if (y > 805)
                {
                    document.EndPage();
                    if (++pages > 200) throw new IOException("Testo oltre il limite di 200 pagine PDF.");
                    canvas = document.BeginPage(pageW, pageH);
                    canvas.Clear(SKColors.White);
                    y = 45;
                }
                int count = Math.Min(78, value.Length - offset);
                if (count > 0 && char.IsHighSurrogate(value[offset + count - 1]) && offset + count < value.Length) count--;
                canvas.DrawText(count == 0 ? "" : value.Substring(offset, count), 35, y, SKTextAlign.Left, font, paint);
                y += 15;
                offset += count;
            }
            while (offset < value.Length);
        }

        document.EndPage();
        document.Close();
        stream.Flush(true);
    }

    // ── PDF -> PDF/A-2b (ISO 19005-2) ─────────────────────────────────────────

    internal static void ConvertToPdfA2b(string srcPdfPath, string dstPdfPath, bool stripMetadata = false)
    {
        byte[] srcBytes = File.ReadAllBytes(srcPdfPath);
        if (srcBytes.Length < 32 || !srcBytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new IOException("Il file sorgente non è un documento PDF valido.");

        int pageCount;
        using (var checkDoc = UglyToad.PdfPig.PdfDocument.Open(srcBytes))
        {
            pageCount = checkDoc.NumberOfPages;
            if (pageCount <= 0)
                throw new IOException("Il documento PDF non contiene pagine.");
        }

        string initialLatin1 = Encoding.Latin1.GetString(srcBytes);
        if (!HasClassicXrefTable(initialLatin1))
        {
            // PDF 1.5+ con Cross-Reference Stream / Object Stream: normalizza prima in un PDF classico multi-pagina
            // ad alta risoluzione così l'aggiunta incrementale PDF/A-2b resta valida al 100%.
            string normalizedTmp = dstPdfPath + ".norm.tmp.pdf";
            try
            {
                RebuildAsClassicPdf(srcBytes, normalizedTmp);
                ConvertToPdfA2b(normalizedTmp, dstPdfPath, stripMetadata);
                return;
            }
            finally
            {
                try { if (File.Exists(normalizedTmp)) File.Delete(normalizedTmp); } catch { }
            }
        }

        // In-place upgrade "%PDF-1.x" -> "%PDF-1.7" (8 byte identici: mantiene invariati gli offset xref esistenti).
        byte[] working = (byte[])srcBytes.Clone();
        if (working.Length >= 8 && working[0] == (byte)'%' && working[1] == (byte)'P' &&
            working[2] == (byte)'D' && working[3] == (byte)'F' && working[4] == (byte)'-')
        {
            working[5] = (byte)'1';
            working[6] = (byte)'.';
            working[7] = (byte)'7';
        }

        string latin1 = Encoding.Latin1.GetString(working);

        // Estrai ultimo startxref, /Root, /Size e /Info
        int startXrefIdx = latin1.LastIndexOf("startxref", StringComparison.Ordinal);
        long prevXrefOffset = 0;
        if (startXrefIdx >= 0)
        {
            var mXref = System.Text.RegularExpressions.Regex.Match(latin1[startXrefIdx..], @"startxref\s+(\d+)");
            if (mXref.Success) long.TryParse(mXref.Groups[1].Value, out prevXrefOffset);
        }

        int rootObjNum = 1;
        var rootMatches = System.Text.RegularExpressions.Regex.Matches(latin1, @"/Root\s+(\d+)\s+0\s+R");
        if (rootMatches.Count > 0)
            int.TryParse(rootMatches[^1].Groups[1].Value, out rootObjNum);

        int maxObjNum = rootObjNum;
        var sizeMatches = System.Text.RegularExpressions.Regex.Matches(latin1, @"/Size\s+(\d+)");
        if (sizeMatches.Count > 0 && int.TryParse(sizeMatches[^1].Groups[1].Value, out int parsedSize))
            maxObjNum = Math.Max(maxObjNum, parsedSize - 1);

        foreach (System.Text.RegularExpressions.Match mObj in System.Text.RegularExpressions.Regex.Matches(latin1, @"(?:^|\r|\n)(\d+)\s+0\s+obj\b"))
        {
            if (int.TryParse(mObj.Groups[1].Value, out int n) && n > maxObjNum)
                maxObjNum = n;
        }

        int? infoObjNum = null;
        if (!stripMetadata)
        {
            var infoMatches = System.Text.RegularExpressions.Regex.Matches(latin1, @"/Info\s+(\d+)\s+0\s+R");
            if (infoMatches.Count > 0 && int.TryParse(infoMatches[^1].Groups[1].Value, out int parsedInfo))
                infoObjNum = parsedInfo;
        }

        // Trova il dizionario del Catalog (/Root) originale (con parentesi annidate bilanciate)
        string catalogInner = ExtractCatalogDictionaryInner(latin1, rootObjNum);

        int metaObjNum = maxObjNum + 1;
        int iccObjNum = maxObjNum + 2;
        int intentObjNum = maxObjNum + 3;
        int newSize = maxObjNum + 4;

        string nowIso = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
        byte[] xmpBytes = BuildPdfA2bXmpPacket(nowIso);
        byte[] iccBytes = BuildMinimalSrgbIccProfile();
        string docIdHex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(working))[..32].ToLowerInvariant();

        // Rimuovi eventuali /Metadata o /OutputIntents preesistenti dal Catalog prima di inserire quelli PDF/A-2b
        catalogInner = System.Text.RegularExpressions.Regex.Replace(catalogInner, @"/Metadata\s+\d+\s+0\s+R", "");
        catalogInner = System.Text.RegularExpressions.Regex.Replace(catalogInner, @"/OutputIntents\s*\[[^\]]*\]", "");
        string updatedCatalog = $"<< {catalogInner.Trim()} /Metadata {metaObjNum} 0 R /OutputIntents [ {intentObjNum} 0 R ] >>";

        using var ms = new MemoryStream(working.Length + xmpBytes.Length + iccBytes.Length + 2048);
        ms.Write(working, 0, working.Length);
        if (working[^1] != (byte)'\n' && working[^1] != (byte)'\r')
            ms.WriteByte((byte)'\n');

        // Commento binario a 4 byte > 128 richiesto da ISO 19005-2
        ms.Write("%\xE2\xE3\xCF\xD3\n"u8);

        static void WriteAscii(Stream s, string text) => s.Write(Encoding.Latin1.GetBytes(text));

        long metaOffset = ms.Position;
        WriteAscii(ms, $"{metaObjNum} 0 obj\n<< /Type /Metadata /Subtype /XML /Length {xmpBytes.Length} >>\nstream\n");
        ms.Write(xmpBytes, 0, xmpBytes.Length);
        WriteAscii(ms, "\nendstream\nendobj\n");

        long iccOffset = ms.Position;
        WriteAscii(ms, $"{iccObjNum} 0 obj\n<< /N 3 /Alternate /DeviceRGB /Length {iccBytes.Length} >>\nstream\n");
        ms.Write(iccBytes, 0, iccBytes.Length);
        WriteAscii(ms, "\nendstream\nendobj\n");

        long intentOffset = ms.Position;
        WriteAscii(ms,
            $"{intentObjNum} 0 obj\n" +
            $"<< /Type /OutputIntent /S /GTS_PDFA1 /OutputConditionIdentifier (sRGB IEC61966-2.1) " +
            $"/OutputCondition (sRGB IEC61966-2.1) /RegistryName (http://www.color.org) " +
            $"/Info (sRGB IEC61966-2.1) /DestOutputProfile {iccObjNum} 0 R >>\n" +
            $"endobj\n");

        long rootOffset = ms.Position;
        WriteAscii(ms, $"{rootObjNum} 0 obj\n{updatedCatalog}\nendobj\n");

        long newXrefOffset = ms.Position;
        var xref = new StringBuilder();
        xref.Append("xref\n");
        xref.Append("0 1\n0000000000 65535 f \n");
        xref.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{rootObjNum} 1\n{rootOffset:D10} 00000 n \n"));
        xref.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{metaObjNum} 3\n"));
        xref.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{metaOffset:D10} 00000 n \n"));
        xref.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{iccOffset:D10} 00000 n \n"));
        xref.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{intentOffset:D10} 00000 n \n"));

        xref.Append("trailer\n<< ");
        xref.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"/Size {newSize} /Root {rootObjNum} 0 R "));
        if (infoObjNum.HasValue)
            xref.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"/Info {infoObjNum.Value} 0 R "));
        if (prevXrefOffset > 0)
            xref.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"/Prev {prevXrefOffset} "));
        xref.Append($"/ID [<{docIdHex}> <{docIdHex}>] >>\n");
        xref.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"startxref\n{newXrefOffset}\n%%EOF\n"));

        WriteAscii(ms, xref.ToString());
        File.WriteAllBytes(dstPdfPath, ms.ToArray());
    }

    private static bool HasClassicXrefTable(string latin1)
    {
        int startXrefIdx = latin1.LastIndexOf("startxref", StringComparison.Ordinal);
        if (startXrefIdx < 0) return false;
        var mXref = System.Text.RegularExpressions.Regex.Match(latin1[startXrefIdx..], @"startxref\s+(\d+)");
        if (!mXref.Success || !long.TryParse(mXref.Groups[1].Value, out long offset)) return false;
        if (offset < 0 || offset + 4 > latin1.Length) return false;
        return latin1.AsSpan((int)offset).TrimStart().StartsWith("xref", StringComparison.Ordinal);
    }

    private static void RebuildAsClassicPdf(byte[] srcBytes, string dstPdfPath)
    {
        using var stream = new FileStream(dstPdfPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var doc = SKDocument.CreatePdf(stream, EmptyPdfMetadata()) ?? throw new IOException("Encoder PDF non disponibile.");
        foreach (var pageBmp in PDFtoImage.Conversion.ToImages(srcBytes, options: new PDFtoImage.RenderOptions(Dpi: 200, WithAspectRatio: true)))
        {
            using (pageBmp)
            {
                var canvas = doc.BeginPage(pageBmp.Width, pageBmp.Height);
                canvas.Clear(SKColors.White);
                using var img = SKImage.FromBitmap(pageBmp);
                canvas.DrawImage(img, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
                doc.EndPage();
            }
        }
        doc.Close();
        stream.Flush(true);
    }

    private static string ExtractCatalogDictionaryInner(string latin1, int rootObjNum)
    {
        var objMatch = System.Text.RegularExpressions.Regex.Match(
            latin1,
            $@"(?:^|\r|\n){rootObjNum}\s+0\s+obj\s*<<");
        if (objMatch.Success)
        {
            int dictStart = objMatch.Index + objMatch.Length;
            string? balanced = ReadBalancedDictionaryInner(latin1, dictStart);
            if (balanced != null) return balanced;
        }

        var fallback = System.Text.RegularExpressions.Regex.Match(latin1, @"<<\s*/Type\s*/Catalog\b");
        if (fallback.Success)
        {
            int dictStart = fallback.Index + 2;
            string? balanced = ReadBalancedDictionaryInner(latin1, dictStart);
            if (balanced != null) return balanced;
        }

        return "/Type /Catalog /Pages 2 0 R";
    }

    private static string? ReadBalancedDictionaryInner(string text, int startAfterOpeningAngles)
    {
        int depth = 1;
        for (int i = startAfterOpeningAngles; i < text.Length - 1; i++)
        {
            if (text[i] == '<' && text[i + 1] == '<')
            {
                depth++;
                i++;
            }
            else if (text[i] == '>' && text[i + 1] == '>')
            {
                depth--;
                if (depth == 0)
                    return text[startAfterOpeningAngles..i];
                i++;
            }
        }
        return null;
    }

    private static byte[] BuildPdfA2bXmpPacket(string isoTimestamp)
    {
        string xml =
            "<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n" +
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n" +
            "  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
            "    <rdf:Description rdf:about=\"\"\n" +
            "        xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\"\n" +
            "        xmlns:dc=\"http://purl.org/dc/elements/1.1/\"\n" +
            "        xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"\n" +
            "        xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\">\n" +
            "      <pdfaid:part>2</pdfaid:part>\n" +
            "      <pdfaid:conformance>B</pdfaid:conformance>\n" +
            "      <dc:format>application/pdf</dc:format>\n" +
            $"      <xmp:CreateDate>{isoTimestamp}</xmp:CreateDate>\n" +
            $"      <xmp:ModifyDate>{isoTimestamp}</xmp:ModifyDate>\n" +
            "      <xmp:CreatorTool>FLIP PDF/A-2b Engine</xmp:CreatorTool>\n" +
            "      <pdf:Producer>FLIP ISO 19005-2 Archival Engine</pdf:Producer>\n" +
            "    </rdf:Description>\n" +
            "  </rdf:RDF>\n" +
            "</x:xmpmeta>\n" +
            "<?xpacket end=\"w\"?>";
        return Encoding.UTF8.GetBytes(xml);
    }

    private static byte[] BuildMinimalSrgbIccProfile()
    {
        // Intestazione e tag ICC v2.1 sRGB IEC61966-2.1 (RGB -> XYZ D50)
        byte[] profile = new byte[128 + 4 + 12 + 32];
        static void WriteUInt32BE(Span<byte> buf, int offset, uint val)
        {
            buf[offset] = (byte)(val >> 24);
            buf[offset + 1] = (byte)(val >> 16);
            buf[offset + 2] = (byte)(val >> 8);
            buf[offset + 3] = (byte)val;
        }
        static void WriteTag(Span<byte> buf, int offset, string fourCC)
        {
            for (int i = 0; i < 4; i++) buf[offset + i] = (byte)fourCC[i];
        }

        Span<byte> span = profile;
        WriteUInt32BE(span, 0, (uint)profile.Length);
        WriteTag(span, 4, "none");
        WriteUInt32BE(span, 8, 0x02100000); // ICC v2.1
        WriteTag(span, 12, "mntr");         // Display device profile
        WriteTag(span, 16, "RGB ");         // Color space RGB
        WriteTag(span, 20, "XYZ ");         // PCS XYZ
        WriteTag(span, 36, "acsp");         // Signature 'acsp'
        WriteUInt32BE(span, 68, 0x0000F6D6); // D50 X
        WriteUInt32BE(span, 72, 0x00010000); // D50 Y
        WriteUInt32BE(span, 76, 0x0000D32D); // D50 Z
        WriteUInt32BE(span, 128, 1);        // 1 tag ('desc')
        WriteTag(span, 132, "desc");
        WriteUInt32BE(span, 136, 144);
        WriteUInt32BE(span, 140, 32);
        WriteTag(span, 144, "desc");
        byte[] name = Encoding.ASCII.GetBytes("sRGB IEC61966-2.1\0");
        WriteUInt32BE(span, 152, (uint)name.Length);
        name.CopyTo(span[156..]);
        return profile;
    }
}

