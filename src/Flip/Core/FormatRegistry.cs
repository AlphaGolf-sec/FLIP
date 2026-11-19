using System.IO;

namespace Flip.Core;

/// <summary>
/// Anagrafica centralizzata ad alte prestazioni dei formati di file supportati da FLIP.
/// Mappa 280+ estensioni reali in 18 categorie con metadati, gradi di visibilità e MIME type.
/// </summary>
public static class FormatRegistry
{
    static readonly Dictionary<string, FormatDefinition> Map = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<FormatCategory, List<FormatDefinition>> ByCategoryMap = [];

    static FormatRegistry()
    {
        foreach (FormatCategory cat in Enum.GetValues<FormatCategory>())
            ByCategoryMap[cat] = [];

        // 1. RASTER & RAW (Immagini bitmap, standard moderni e formati RAW da fotocamere)
        Reg("png", "PNG", "image/png", FormatCategory.RasterAndRaw, FormatGrade.Everyday);
        Reg("jpg", "JPEG", "image/jpeg", FormatCategory.RasterAndRaw, FormatGrade.Everyday);
        Reg("jpeg", "JPEG", "image/jpeg", FormatCategory.RasterAndRaw, FormatGrade.Everyday);
        Reg("webp", "WebP", "image/webp", FormatCategory.RasterAndRaw, FormatGrade.Everyday);
        Reg("gif", "GIF", "image/gif", FormatCategory.RasterAndRaw, FormatGrade.Everyday);
        Reg("bmp", "BMP", "image/bmp", FormatCategory.RasterAndRaw, FormatGrade.Everyday);
        Reg("tiff", "TIFF", "image/tiff", FormatCategory.RasterAndRaw, FormatGrade.General);
        Reg("tif", "TIFF", "image/tiff", FormatCategory.RasterAndRaw, FormatGrade.General);
        Reg("ico", "ICO", "image/x-icon", FormatCategory.RasterAndRaw, FormatGrade.General);
        Reg("avif", "AVIF", "image/avif", FormatCategory.RasterAndRaw, FormatGrade.General);
        Reg("heic", "HEIC", "image/heic", FormatCategory.RasterAndRaw, FormatGrade.General, outSupp: false);
        Reg("heif", "HEIF", "image/heif", FormatCategory.RasterAndRaw, FormatGrade.General, outSupp: false);
        Reg("jxl", "JPEG XL", "image/jxl", FormatCategory.RasterAndRaw, FormatGrade.General);
        Reg("psd", "Photoshop", "image/vnd.adobe.photoshop", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("exr", "OpenEXR", "image/x-exr", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.ThreeDAndGameDev);
        Reg("hdr", "Radiance HDR", "image/vnd.radiance", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.ThreeDAndGameDev);
        Reg("tga", "TARGA", "image/x-tga", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.ThreeDAndGameDev);
        Reg("dds", "DirectDraw Surface", "image/x-dds", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.ThreeDAndGameDev);
        Reg("qoi", "Quite OK Image", "image/qoi", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("pcx", "Paintbrush", "image/x-pcx", FormatCategory.RasterAndRaw, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        Reg("wbmp", "Wireless BMP", "image/vnd.wap.wbmp", FormatCategory.RasterAndRaw, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        // RAW Fotocamere digitali (Input-only verso DNG, TIFF o raster moderni)
        Reg("dng", "Adobe DNG", "image/x-adobe-dng", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.RawPhotography);
        Reg("cr2", "Canon RAW 2", "image/x-canon-cr2", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.RawPhotography, outSupp: false);
        Reg("cr3", "Canon RAW 3", "image/x-canon-cr3", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.RawPhotography, outSupp: false);
        Reg("nef", "Nikon NEF", "image/x-nikon-nef", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.RawPhotography, outSupp: false);
        Reg("arw", "Sony ARW", "image/x-sony-arw", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.RawPhotography, outSupp: false);
        Reg("rw2", "Panasonic RW2", "image/x-panasonic-rw2", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.RawPhotography, outSupp: false);
        Reg("orf", "Olympus ORF", "image/x-olympus-orf", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.RawPhotography, outSupp: false);
        Reg("pef", "Pentax PEF", "image/x-pentax-pef", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.RawPhotography, outSupp: false);
        Reg("raf", "Fujifilm RAF", "image/x-fujifilm-raf", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.RawPhotography, outSupp: false);
        Reg("srw", "Samsung SRW", "image/x-samsung-srw", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.RawPhotography, outSupp: false);
        Reg("x3f", "Sigma X3F", "image/x-sigma-x3f", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.RawPhotography, outSupp: false);
        Reg("dcr", "Kodak DCR", "image/x-kodak-dcr", FormatCategory.RasterAndRaw, FormatGrade.Legacy, SpecialistField.RawPhotography, outSupp: false);
        Reg("kdc", "Kodak KDC", "image/x-kodak-kdc", FormatCategory.RasterAndRaw, FormatGrade.Legacy, SpecialistField.RawPhotography, outSupp: false);
        Reg("mrw", "Minolta MRW", "image/x-minolta-mrw", FormatCategory.RasterAndRaw, FormatGrade.Legacy, SpecialistField.RawPhotography, outSupp: false);
        Reg("raw", "Raw Image", "image/x-panasonic-raw", FormatCategory.RasterAndRaw, FormatGrade.Specialist, SpecialistField.RawPhotography, outSupp: false);

        // 2. VECTOR (Grafica vettoriale 2D)
        Reg("svg", "SVG", "image/svg+xml", FormatCategory.Vector, FormatGrade.Everyday);
        Reg("svgz", "Compressed SVG", "image/svg+xml", FormatCategory.Vector, FormatGrade.General);
        Reg("ai", "Adobe Illustrator", "application/illustrator", FormatCategory.Vector, FormatGrade.General, SpecialistField.CadAndVector);
        Reg("eps", "Encapsulated PostScript", "application/postscript", FormatCategory.Vector, FormatGrade.General, SpecialistField.CadAndVector);
        Reg("wmf", "Windows Metafile", "image/wmf", FormatCategory.Vector, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("emf", "Enhanced Metafile", "image/emf", FormatCategory.Vector, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("cdr", "CorelDraw", "application/vnd.corel-draw", FormatCategory.Vector, FormatGrade.Specialist, SpecialistField.CadAndVector, outSupp: false);
        Reg("cgm", "Computer Graphics Metafile", "image/cgm", FormatCategory.Vector, FormatGrade.Legacy, SpecialistField.CadAndVector);
        Reg("vsd", "Visio Drawing Legacy", "application/vnd.visio", FormatCategory.Vector, FormatGrade.Legacy, SpecialistField.CadAndVector, outSupp: false);
        Reg("vsdx", "Visio Drawing", "application/vnd.ms-visio.drawing.main+xml", FormatCategory.Vector, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("xps", "XML Paper Specification", "application/vnd.ms-xpsdocument", FormatCategory.Vector, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("oxps", "Open XPS", "application/oxps", FormatCategory.Vector, FormatGrade.Specialist, SpecialistField.CadAndVector);

        // 3. PDF (Formati documento a layout fisso)
        Reg("pdf", "PDF", "application/pdf", FormatCategory.Pdf, FormatGrade.Everyday);
        Reg("pdfa", "PDF/A-2b (Archivio ISO 19005 PA/Legale)", "application/pdf", FormatCategory.Pdf, FormatGrade.Everyday);
        Reg("fdf", "Forms Data Format", "application/vnd.fdf", FormatCategory.Pdf, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("xfdf", "XML Forms Data", "application/vnd.adobe.xfdf", FormatCategory.Pdf, FormatGrade.Specialist, SpecialistField.DataAndDev);

        // 4. DOCUMENT & EMAIL (Elaborazione testi ed email)
        Reg("docx", "Word Document", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", FormatCategory.DocumentAndEmail, FormatGrade.Everyday);
        Reg("odt", "OpenDocument Text", "application/vnd.oasis.opendocument.text", FormatCategory.DocumentAndEmail, FormatGrade.Everyday);
        Reg("rtf", "Rich Text Format", "application/rtf", FormatCategory.DocumentAndEmail, FormatGrade.Everyday);
        Reg("doc", "Word 97-2003", "application/msword", FormatCategory.DocumentAndEmail, FormatGrade.General);
        Reg("ott", "OpenDocument Template", "application/vnd.oasis.opendocument.text-template", FormatCategory.DocumentAndEmail, FormatGrade.General);
        Reg("dotx", "Word Template", "application/vnd.openxmlformats-officedocument.wordprocessingml.template", FormatCategory.DocumentAndEmail, FormatGrade.General);
        Reg("dot", "Word Template Legacy", "application/msword", FormatCategory.DocumentAndEmail, FormatGrade.Legacy);
        Reg("docm", "Word Macro-Enabled", "application/vnd.ms-word.document.macroenabled.12", FormatCategory.DocumentAndEmail, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("wps", "Works Document", "application/vnd.ms-works", FormatCategory.DocumentAndEmail, FormatGrade.Legacy, SpecialistField.LegacyAndRegional, outSupp: false);
        Reg("wpd", "WordPerfect", "application/wordperfect", FormatCategory.DocumentAndEmail, FormatGrade.Legacy, SpecialistField.LegacyAndRegional, outSupp: false);
        Reg("abw", "AbiWord", "application/x-abiword", FormatCategory.DocumentAndEmail, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        Reg("sxw", "OpenOffice Writer Legacy", "application/vnd.sun.xml.writer", FormatCategory.DocumentAndEmail, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        Reg("eml", "Email Message", "message/rfc822", FormatCategory.DocumentAndEmail, FormatGrade.General);
        Reg("msg", "Outlook Message", "application/vnd.ms-outlook", FormatCategory.DocumentAndEmail, FormatGrade.General);
        Reg("mbox", "Mailbox Storage", "application/mbox", FormatCategory.DocumentAndEmail, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("pst", "Outlook PST", "application/vnd.ms-outlook-pst", FormatCategory.DocumentAndEmail, FormatGrade.Specialist, SpecialistField.DataAndDev, outSupp: false);
        Reg("vcf", "vCard Contact", "text/vcard", FormatCategory.DocumentAndEmail, FormatGrade.General);

        // 5. HWP (Hangul Word Processor, standard coreano e regionale)
        Reg("hwp", "Hangul Word Processor", "application/x-hwp", FormatCategory.Hwp, FormatGrade.General, SpecialistField.LegacyAndRegional);
        Reg("hwpx", "Hangul Open Word", "application/hwp+xml", FormatCategory.Hwp, FormatGrade.General, SpecialistField.LegacyAndRegional);
        Reg("hwt", "Hangul Template", "application/x-hwt", FormatCategory.Hwp, FormatGrade.Specialist, SpecialistField.LegacyAndRegional);
        Reg("hwtx", "Hangul Open Template", "application/hwt+xml", FormatCategory.Hwp, FormatGrade.Specialist, SpecialistField.LegacyAndRegional);
        Reg("hml", "Hangul Markup", "text/x-hml", FormatCategory.Hwp, FormatGrade.Specialist, SpecialistField.LegacyAndRegional);

        // 6. SPREADSHEET (Fogli di calcolo e tabelle numeriche)
        Reg("xlsx", "Excel Workbook", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", FormatCategory.Spreadsheet, FormatGrade.Everyday);
        Reg("ods", "OpenDocument Spreadsheet", "application/vnd.oasis.opendocument.spreadsheet", FormatCategory.Spreadsheet, FormatGrade.Everyday);
        Reg("xls", "Excel 97-2003", "application/vnd.ms-excel", FormatCategory.Spreadsheet, FormatGrade.General);
        Reg("xlsm", "Excel Macro-Enabled", "application/vnd.ms-excel.sheet.macroenabled.12", FormatCategory.Spreadsheet, FormatGrade.General);
        Reg("xltx", "Excel Template", "application/vnd.openxmlformats-officedocument.spreadsheetml.template", FormatCategory.Spreadsheet, FormatGrade.General);
        Reg("xlt", "Excel Template Legacy", "application/vnd.ms-excel", FormatCategory.Spreadsheet, FormatGrade.Legacy);
        Reg("xlsb", "Excel Binary", "application/vnd.ms-excel.sheet.binary.macroenabled.12", FormatCategory.Spreadsheet, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("numbers", "Apple Numbers", "application/vnd.apple.numbers", FormatCategory.Spreadsheet, FormatGrade.Specialist, SpecialistField.LegacyAndRegional, outSupp: false);
        Reg("dif", "Data Interchange Format", "video/x-dv", FormatCategory.Spreadsheet, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("sxc", "OpenOffice Calc Legacy", "application/vnd.sun.xml.calc", FormatCategory.Spreadsheet, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        Reg("ots", "OpenDocument Spreadsheet Template", "application/vnd.oasis.opendocument.spreadsheet-template", FormatCategory.Spreadsheet, FormatGrade.General);
        Reg("fods", "Flat OpenDocument Spreadsheet", "application/vnd.oasis.opendocument.spreadsheet-flat-xml", FormatCategory.Spreadsheet, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("dbf", "dBase Database", "application/x-dbf", FormatCategory.Spreadsheet, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        Reg("prn", "Lotus Formatted Text", "text/prn", FormatCategory.Spreadsheet, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        Reg("slk", "SYLK Symbolic Link", "text/sylk", FormatCategory.Spreadsheet, FormatGrade.Legacy, SpecialistField.DataAndDev);

        // 7. PRESENTATION (Presentazioni e slide deck)
        Reg("pptx", "PowerPoint Presentation", "application/vnd.openxmlformats-officedocument.presentationml.presentation", FormatCategory.Presentation, FormatGrade.Everyday);
        Reg("odp", "OpenDocument Presentation", "application/vnd.oasis.opendocument.presentation", FormatCategory.Presentation, FormatGrade.Everyday);
        Reg("ppt", "PowerPoint 97-2003", "application/vnd.ms-powerpoint", FormatCategory.Presentation, FormatGrade.General);
        Reg("ppsx", "PowerPoint Slide Show", "application/vnd.openxmlformats-officedocument.presentationml.slideshow", FormatCategory.Presentation, FormatGrade.General);
        Reg("potx", "PowerPoint Template", "application/vnd.openxmlformats-officedocument.presentationml.template", FormatCategory.Presentation, FormatGrade.General);
        Reg("pps", "PowerPoint Show Legacy", "application/vnd.ms-powerpoint", FormatCategory.Presentation, FormatGrade.Legacy);
        Reg("pot", "PowerPoint Template Legacy", "application/vnd.ms-powerpoint", FormatCategory.Presentation, FormatGrade.Legacy);
        Reg("pptm", "PowerPoint Macro-Enabled", "application/vnd.ms-powerpoint.presentation.macroenabled.12", FormatCategory.Presentation, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("key", "Apple Keynote", "application/vnd.apple.keynote", FormatCategory.Presentation, FormatGrade.Specialist, SpecialistField.LegacyAndRegional, outSupp: false);
        Reg("sxi", "OpenOffice Impress Legacy", "application/vnd.sun.xml.impress", FormatCategory.Presentation, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        Reg("otp", "OpenDocument Presentation Template", "application/vnd.oasis.opendocument.presentation-template", FormatCategory.Presentation, FormatGrade.General);
        Reg("fodp", "Flat OpenDocument Presentation", "application/vnd.oasis.opendocument.presentation-flat-xml", FormatCategory.Presentation, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("sdd", "StarOffice Impress", "application/vnd.stardivision.impress", FormatCategory.Presentation, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        Reg("odpz", "Zipped ODP", "application/vnd.oasis.opendocument.presentation", FormatCategory.Presentation, FormatGrade.Specialist, SpecialistField.DataAndDev);

        // 8. TEXT & MARKUP (Testo semplice, documentazione e markup leggeri)
        Reg("txt", "Plain Text", "text/plain", FormatCategory.TextAndMarkup, FormatGrade.Everyday);
        Reg("md", "Markdown", "text/markdown", FormatCategory.TextAndMarkup, FormatGrade.Everyday);
        Reg("markdown", "Markdown", "text/markdown", FormatCategory.TextAndMarkup, FormatGrade.Everyday);
        Reg("html", "HTML Document", "text/html", FormatCategory.TextAndMarkup, FormatGrade.Everyday);
        Reg("htm", "HTML Document", "text/html", FormatCategory.TextAndMarkup, FormatGrade.Everyday);
        Reg("xhtml", "XHTML Document", "application/xhtml+xml", FormatCategory.TextAndMarkup, FormatGrade.General);
        Reg("rst", "reStructuredText", "text/x-rst", FormatCategory.TextAndMarkup, FormatGrade.General);
        Reg("adoc", "AsciiDoc", "text/asciidoc", FormatCategory.TextAndMarkup, FormatGrade.General);
        Reg("asciidoc", "AsciiDoc", "text/asciidoc", FormatCategory.TextAndMarkup, FormatGrade.General);
        Reg("tex", "LaTeX Source", "application/x-tex", FormatCategory.TextAndMarkup, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("latex", "LaTeX Source", "application/x-latex", FormatCategory.TextAndMarkup, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("ltx", "LaTeX File", "application/x-tex", FormatCategory.TextAndMarkup, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("org", "Emacs Org Mode", "text/x-org", FormatCategory.TextAndMarkup, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("man", "Unix Manual Page", "text/troff", FormatCategory.TextAndMarkup, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("1", "Man Section 1", "text/troff", FormatCategory.TextAndMarkup, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("log", "Log File", "text/plain", FormatCategory.TextAndMarkup, FormatGrade.General);

        // 9. DATA & CONFIG (Serializzazione, configurazioni e schema dati)
        Reg("json", "JSON", "application/json", FormatCategory.DataAndConfig, FormatGrade.Everyday);
        Reg("yaml", "YAML", "application/yaml", FormatCategory.DataAndConfig, FormatGrade.Everyday);
        Reg("yml", "YAML", "application/yaml", FormatCategory.DataAndConfig, FormatGrade.Everyday);
        Reg("xml", "XML", "application/xml", FormatCategory.DataAndConfig, FormatGrade.Everyday);
        Reg("csv", "Comma-Separated Values", "text/csv", FormatCategory.DataAndConfig, FormatGrade.Everyday);
        Reg("tsv", "Tab-Separated Values", "text/tab-separated-values", FormatCategory.DataAndConfig, FormatGrade.Everyday);
        Reg("toml", "TOML", "application/toml", FormatCategory.DataAndConfig, FormatGrade.General);
        Reg("ini", "Configuration File", "text/plain", FormatCategory.DataAndConfig, FormatGrade.General);
        Reg("env", "Environment Variables", "text/plain", FormatCategory.DataAndConfig, FormatGrade.General);
        Reg("jsonl", "JSON Lines", "application/x-jsonlines", FormatCategory.DataAndConfig, FormatGrade.General, SpecialistField.DataAndDev);
        Reg("ndjson", "Newline Delimited JSON", "application/x-ndjson", FormatCategory.DataAndConfig, FormatGrade.General, SpecialistField.DataAndDev);
        Reg("bson", "Binary JSON", "application/bson", FormatCategory.DataAndConfig, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("cbor", "CBOR", "application/cbor", FormatCategory.DataAndConfig, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("msgpack", "MessagePack", "application/x-msgpack", FormatCategory.DataAndConfig, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("proto", "Protocol Buffers", "text/x-protobuf", FormatCategory.DataAndConfig, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("properties", "Java Properties", "text/x-java-properties", FormatCategory.DataAndConfig, FormatGrade.General, SpecialistField.DataAndDev);
        Reg("plist", "Apple Property List", "application/x-plist", FormatCategory.DataAndConfig, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("reg", "Windows Registry File", "text/plain", FormatCategory.DataAndConfig, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("sql", "SQL Script / Dump", "application/sql", FormatCategory.DataAndConfig, FormatGrade.General, SpecialistField.DataAndDev);
        Reg("parquet", "Apache Parquet", "application/vnd.apache.parquet", FormatCategory.DataAndConfig, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("feather", "Apache Feather", "application/x-feather", FormatCategory.DataAndConfig, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("avro", "Apache Avro", "application/avro", FormatCategory.DataAndConfig, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("kdl", "KDL Document", "text/plain", FormatCategory.DataAndConfig, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("hcl", "HashiCorp HCL", "text/plain", FormatCategory.DataAndConfig, FormatGrade.Specialist, SpecialistField.DataAndDev);

        // 10. EBOOK & COMIC (Editoria digitale e fumetti digitali)
        Reg("epub", "EPUB", "application/epub+zip", FormatCategory.EbookAndComic, FormatGrade.Everyday);
        Reg("mobi", "Mobipocket", "application/x-mobipocket-ebook", FormatCategory.EbookAndComic, FormatGrade.Everyday);
        Reg("azw3", "Kindle Format 8", "application/vnd.amazon.ebook", FormatCategory.EbookAndComic, FormatGrade.Everyday);
        Reg("cbz", "Comic Book ZIP", "application/vnd.comicbook+zip", FormatCategory.EbookAndComic, FormatGrade.General, SpecialistField.EbookAndPublishing);
        Reg("cbr", "Comic Book RAR", "application/vnd.comicbook-rar", FormatCategory.EbookAndComic, FormatGrade.General, SpecialistField.EbookAndPublishing);
        Reg("cbt", "Comic Book TAR", "application/x-cbt", FormatCategory.EbookAndComic, FormatGrade.Specialist, SpecialistField.EbookAndPublishing);
        Reg("cb7", "Comic Book 7Z", "application/x-cb7", FormatCategory.EbookAndComic, FormatGrade.Specialist, SpecialistField.EbookAndPublishing);
        Reg("fb2", "FictionBook 2", "application/x-fictionbook+xml", FormatCategory.EbookAndComic, FormatGrade.General, SpecialistField.EbookAndPublishing);
        Reg("djvu", "DjVu Document", "image/vnd.djvu", FormatCategory.EbookAndComic, FormatGrade.Specialist, SpecialistField.EbookAndPublishing);
        Reg("azw", "Kindle AZW", "application/vnd.amazon.ebook", FormatCategory.EbookAndComic, FormatGrade.General, SpecialistField.EbookAndPublishing);
        Reg("prc", "Palm Resource", "application/x-mobipocket-ebook", FormatCategory.EbookAndComic, FormatGrade.Legacy, SpecialistField.EbookAndPublishing);
        Reg("lit", "Microsoft Reader", "application/x-ms-reader", FormatCategory.EbookAndComic, FormatGrade.Legacy, SpecialistField.EbookAndPublishing, outSupp: false);
        Reg("chm", "Compiled HTML Help", "application/vnd.ms-htmlhelp", FormatCategory.EbookAndComic, FormatGrade.Legacy, SpecialistField.LegacyAndRegional, outSupp: false);
        Reg("ibooks", "Apple iBooks", "application/x-ibooks+zip", FormatCategory.EbookAndComic, FormatGrade.Specialist, SpecialistField.EbookAndPublishing, outSupp: false);

        // 11. VIDEO (File multimediali video)
        Reg("mp4", "MP4 Video", "video/mp4", FormatCategory.Video, FormatGrade.Everyday);
        Reg("mkv", "Matroska Video", "video/x-matroska", FormatCategory.Video, FormatGrade.Everyday);
        Reg("webm", "WebM Video", "video/webm", FormatCategory.Video, FormatGrade.Everyday);
        Reg("mov", "QuickTime Video", "video/quicktime", FormatCategory.Video, FormatGrade.Everyday);
        Reg("avi", "Audio Video Interleave", "video/x-msvideo", FormatCategory.Video, FormatGrade.Everyday);
        Reg("wmv", "Windows Media Video", "video/x-ms-wmv", FormatCategory.Video, FormatGrade.General);
        Reg("flv", "Flash Video", "video/x-flv", FormatCategory.Video, FormatGrade.General);
        Reg("m4v", "Apple Video", "video/x-m4v", FormatCategory.Video, FormatGrade.General);
        Reg("mpg", "MPEG Video", "video/mpeg", FormatCategory.Video, FormatGrade.General);
        Reg("mpeg", "MPEG Video", "video/mpeg", FormatCategory.Video, FormatGrade.General);
        Reg("ts", "MPEG Transport Stream", "video/mp2t", FormatCategory.Video, FormatGrade.General);
        Reg("m2ts", "BDAV MPEG-2 TS", "video/mp2t", FormatCategory.Video, FormatGrade.Specialist);
        Reg("3gp", "3GPP Multimedia", "video/3gpp", FormatCategory.Video, FormatGrade.Legacy);
        Reg("3g2", "3GPP2 Multimedia", "video/3gpp2", FormatCategory.Video, FormatGrade.Legacy);
        Reg("ogv", "Ogg Theora Video", "video/ogg", FormatCategory.Video, FormatGrade.General);
        Reg("vob", "DVD Video Object", "video/x-ms-vob", FormatCategory.Video, FormatGrade.Legacy, outSupp: false);
        Reg("rm", "RealMedia", "application/vnd.rn-realmedia", FormatCategory.Video, FormatGrade.Legacy, outSupp: false);
        Reg("rmvb", "RealMedia Variable Bitrate", "application/vnd.rn-realmedia-vbr", FormatCategory.Video, FormatGrade.Legacy, outSupp: false);
        Reg("asf", "Advanced Systems Format", "video/x-ms-asf", FormatCategory.Video, FormatGrade.Legacy);
        Reg("mxf", "Material Exchange Format", "application/mxf", FormatCategory.Video, FormatGrade.Specialist);
        Reg("divx", "DivX Video", "video/divx", FormatCategory.Video, FormatGrade.Legacy);
        Reg("f4v", "Flash MP4 Video", "video/x-f4v", FormatCategory.Video, FormatGrade.Legacy);

        // 12. AUDIO (File sonori e musicali)
        Reg("mp3", "MP3 Audio", "audio/mpeg", FormatCategory.Audio, FormatGrade.Everyday);
        Reg("wav", "Waveform Audio", "audio/wav", FormatCategory.Audio, FormatGrade.Everyday);
        Reg("flac", "Free Lossless Audio", "audio/flac", FormatCategory.Audio, FormatGrade.Everyday);
        Reg("ogg", "Ogg Vorbis Audio", "audio/ogg", FormatCategory.Audio, FormatGrade.Everyday);
        Reg("m4a", "MPEG-4 Audio", "audio/mp4", FormatCategory.Audio, FormatGrade.Everyday);
        Reg("aac", "Advanced Audio Coding", "audio/aac", FormatCategory.Audio, FormatGrade.Everyday);
        Reg("wma", "Windows Media Audio", "audio/x-ms-wma", FormatCategory.Audio, FormatGrade.General);
        Reg("opus", "Opus Audio", "audio/opus", FormatCategory.Audio, FormatGrade.General);
        Reg("aiff", "Audio Interchange File", "audio/aiff", FormatCategory.Audio, FormatGrade.General);
        Reg("aif", "Audio Interchange File", "audio/aiff", FormatCategory.Audio, FormatGrade.General);
        Reg("alac", "Apple Lossless", "audio/alac", FormatCategory.Audio, FormatGrade.General);
        Reg("ape", "Monkey's Audio", "audio/x-ape", FormatCategory.Audio, FormatGrade.Specialist);
        Reg("ac3", "Dolby Digital AC-3", "audio/ac3", FormatCategory.Audio, FormatGrade.General);
        Reg("dts", "DTS Audio", "audio/vnd.dts", FormatCategory.Audio, FormatGrade.Specialist);
        Reg("amr", "Adaptive Multi-Rate", "audio/amr", FormatCategory.Audio, FormatGrade.Legacy);
        Reg("mka", "Matroska Audio", "audio/x-matroska", FormatCategory.Audio, FormatGrade.General);
        Reg("mid", "MIDI File", "audio/midi", FormatCategory.Audio, FormatGrade.General);
        Reg("midi", "MIDI File", "audio/midi", FormatCategory.Audio, FormatGrade.General);
        Reg("mod", "Amiga Tracker Module", "audio/x-mod", FormatCategory.Audio, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        Reg("xm", "FastTracker 2 Extended Module", "audio/x-xm", FormatCategory.Audio, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        Reg("it", "Impulse Tracker Module", "audio/x-it", FormatCategory.Audio, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        Reg("s3m", "Scream Tracker 3 Module", "audio/x-s3m", FormatCategory.Audio, FormatGrade.Legacy, SpecialistField.LegacyAndRegional);
        Reg("ra", "RealAudio", "audio/x-pn-realaudio", FormatCategory.Audio, FormatGrade.Legacy, outSupp: false);
        Reg("voc", "Creative Voice", "audio/x-voc", FormatCategory.Audio, FormatGrade.Legacy);

        // 13. SUBTITLE (Tracce sincronizzate e sottotitoli)
        Reg("srt", "SubRip Subtitle", "application/x-subrip", FormatCategory.Subtitle, FormatGrade.Everyday);
        Reg("vtt", "WebVTT", "text/vtt", FormatCategory.Subtitle, FormatGrade.Everyday);
        Reg("ass", "Advanced SubStation Alpha", "text/x-ass", FormatCategory.Subtitle, FormatGrade.General);
        Reg("ssa", "SubStation Alpha", "text/x-ssa", FormatCategory.Subtitle, FormatGrade.General);
        Reg("sub", "MicroDVD Subtitle", "text/x-microdvd", FormatCategory.Subtitle, FormatGrade.General);
        Reg("sbv", "YouTube Subtitle", "text/plain", FormatCategory.Subtitle, FormatGrade.General);
        Reg("lrc", "Timed Lyric File", "text/plain", FormatCategory.Subtitle, FormatGrade.General);
        Reg("ttml", "Timed Text Markup", "application/ttml+xml", FormatCategory.Subtitle, FormatGrade.Specialist);
        Reg("dfxp", "Distribution Format Exchange", "application/ttaf+xml", FormatCategory.Subtitle, FormatGrade.Specialist);
        Reg("smi", "SAMI Captioning", "application/smil+xml", FormatCategory.Subtitle, FormatGrade.Specialist);
        Reg("sami", "SAMI Captioning", "application/smil+xml", FormatCategory.Subtitle, FormatGrade.Specialist);
        Reg("idx", "VobSub Index", "text/plain", FormatCategory.Subtitle, FormatGrade.Specialist);
        Reg("sup", "Blu-ray PGS Subtitle", "application/x-pgs", FormatCategory.Subtitle, FormatGrade.Specialist, outSupp: false);
        Reg("usf", "Universal Subtitle Format", "application/xml", FormatCategory.Subtitle, FormatGrade.Specialist);

        // 14. ARCHIVE & DISK (Archiviazione, compressione e immagini disco)
        Reg("zip", "ZIP Archive", "application/zip", FormatCategory.ArchiveAndDisk, FormatGrade.Everyday);
        Reg("7z", "7-Zip Archive", "application/x-7z-compressed", FormatCategory.ArchiveAndDisk, FormatGrade.Everyday);
        Reg("rar", "RAR Archive", "application/vnd.rar", FormatCategory.ArchiveAndDisk, FormatGrade.Everyday, outSupp: false);
        Reg("tar", "Tape Archive", "application/x-tar", FormatCategory.ArchiveAndDisk, FormatGrade.Everyday);
        Reg("gz", "GZip Archive", "application/gzip", FormatCategory.ArchiveAndDisk, FormatGrade.Everyday);
        Reg("bz2", "BZip2 Archive", "application/x-bzip2", FormatCategory.ArchiveAndDisk, FormatGrade.General);
        Reg("xz", "XZ Archive", "application/x-xz", FormatCategory.ArchiveAndDisk, FormatGrade.General);
        Reg("tgz", "TAR GZ Archive", "application/gzip", FormatCategory.ArchiveAndDisk, FormatGrade.Everyday);
        Reg("tbz2", "TAR BZ2 Archive", "application/x-bzip2", FormatCategory.ArchiveAndDisk, FormatGrade.General);
        Reg("txz", "TAR XZ Archive", "application/x-xz", FormatCategory.ArchiveAndDisk, FormatGrade.General);
        Reg("zst", "Zstandard Archive", "application/zstd", FormatCategory.ArchiveAndDisk, FormatGrade.General);
        Reg("lzma", "LZMA Archive", "application/x-lzma", FormatCategory.ArchiveAndDisk, FormatGrade.General);
        Reg("iso", "ISO 9660 Disk Image", "application/x-iso9660-image", FormatCategory.ArchiveAndDisk, FormatGrade.Specialist);
        Reg("img", "Raw Disk Image", "application/octet-stream", FormatCategory.ArchiveAndDisk, FormatGrade.Specialist);
        Reg("vhd", "Virtual Hard Disk", "application/x-vhd", FormatCategory.ArchiveAndDisk, FormatGrade.Specialist, outSupp: false);
        Reg("vhdx", "Virtual Hard Disk v2", "application/x-vhdx", FormatCategory.ArchiveAndDisk, FormatGrade.Specialist, outSupp: false);
        Reg("cab", "Windows Cabinet", "application/vnd.ms-cab-compressed", FormatCategory.ArchiveAndDisk, FormatGrade.Specialist);
        Reg("arj", "ARJ Archive", "application/x-arj", FormatCategory.ArchiveAndDisk, FormatGrade.Legacy);
        Reg("cpio", "CPIO Archive", "application/x-cpio", FormatCategory.ArchiveAndDisk, FormatGrade.Specialist);
        Reg("rpm", "Red Hat Package", "application/x-rpm", FormatCategory.ArchiveAndDisk, FormatGrade.Specialist, outSupp: false);
        Reg("deb", "Debian Package", "application/vnd.debian.binary-package", FormatCategory.ArchiveAndDisk, FormatGrade.Specialist, outSupp: false);
        Reg("dmg", "Apple Disk Image", "application/x-apple-diskimage", FormatCategory.ArchiveAndDisk, FormatGrade.Specialist, outSupp: false);

        // 15. CAD (Disegno tecnico 2D/3D e ingegneria)
        Reg("dxf", "Drawing Exchange Format", "image/vnd.dxf", FormatCategory.Cad, FormatGrade.Everyday, SpecialistField.CadAndVector);
        Reg("dwg", "AutoCAD Drawing", "image/vnd.dwg", FormatCategory.Cad, FormatGrade.General, SpecialistField.CadAndVector);
        Reg("step", "STEP 3D CAD", "application/step", FormatCategory.Cad, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("stp", "STEP 3D CAD", "application/step", FormatCategory.Cad, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("iges", "IGES CAD", "model/iges", FormatCategory.Cad, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("igs", "IGES CAD", "model/iges", FormatCategory.Cad, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("brep", "OpenCASCADE BRep", "application/x-brep", FormatCategory.Cad, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("sat", "ACIS SAT", "application/x-sat", FormatCategory.Cad, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("x_t", "Parasolid Text", "application/x-parasolid", FormatCategory.Cad, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("x_b", "Parasolid Binary", "application/x-parasolid", FormatCategory.Cad, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("dwf", "Design Web Format", "model/vnd.dwf", FormatCategory.Cad, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("ifc", "Industry Foundation Classes (BIM)", "application/x-step", FormatCategory.Cad, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("gerber", "Gerber PCB Data", "application/vnd.gerber", FormatCategory.Cad, FormatGrade.Specialist, SpecialistField.CadAndVector);
        Reg("gbr", "Gerber PCB Data", "application/vnd.gerber", FormatCategory.Cad, FormatGrade.Specialist, SpecialistField.CadAndVector);

        // 16. MODEL 3D (Mesh poligonali, rendering e stampa 3D)
        Reg("stl", "Stereolithography (3D Print)", "model/stl", FormatCategory.Model3D, FormatGrade.Everyday, SpecialistField.ThreeDAndGameDev);
        Reg("obj", "Wavefront 3D Object", "model/obj", FormatCategory.Model3D, FormatGrade.Everyday, SpecialistField.ThreeDAndGameDev);
        Reg("glb", "Binary glTF", "model/gltf-binary", FormatCategory.Model3D, FormatGrade.Everyday, SpecialistField.ThreeDAndGameDev);
        Reg("gltf", "glTF 3D Scene", "model/gltf+json", FormatCategory.Model3D, FormatGrade.Everyday, SpecialistField.ThreeDAndGameDev);
        Reg("ply", "Polygon File Format", "model/ply", FormatCategory.Model3D, FormatGrade.Everyday, SpecialistField.ThreeDAndGameDev);
        Reg("3mf", "3D Manufacturing Format", "model/3mf", FormatCategory.Model3D, FormatGrade.Everyday, SpecialistField.ThreeDAndGameDev);
        Reg("fbx", "Autodesk Filmbox", "application/octet-stream", FormatCategory.Model3D, FormatGrade.Everyday, SpecialistField.ThreeDAndGameDev);
        Reg("dae", "Collada Digital Asset", "model/vnd.collada+xml", FormatCategory.Model3D, FormatGrade.Everyday, SpecialistField.ThreeDAndGameDev);
        Reg("blend", "Blender 3D Scene", "application/x-blender", FormatCategory.Model3D, FormatGrade.Specialist, SpecialistField.ThreeDAndGameDev, outSupp: false);
        Reg("usd", "Universal Scene Description", "model/vnd.usd", FormatCategory.Model3D, FormatGrade.Specialist, SpecialistField.ThreeDAndGameDev);
        Reg("usdz", "Zipped USD (AR)", "model/vnd.usdz+zip", FormatCategory.Model3D, FormatGrade.Specialist, SpecialistField.ThreeDAndGameDev);
        Reg("x3d", "Extensible 3D", "model/x3d+xml", FormatCategory.Model3D, FormatGrade.Specialist, SpecialistField.ThreeDAndGameDev);
        Reg("wrl", "VRML 2.0 World", "model/vrml", FormatCategory.Model3D, FormatGrade.Legacy, SpecialistField.ThreeDAndGameDev);
        Reg("vrml", "VRML World", "model/vrml", FormatCategory.Model3D, FormatGrade.Legacy, SpecialistField.ThreeDAndGameDev);
        Reg("ase", "ASCII Scene Export", "text/plain", FormatCategory.Model3D, FormatGrade.Legacy, SpecialistField.ThreeDAndGameDev);
        Reg("abc", "Alembic 3D Cache", "application/x-alembic", FormatCategory.Model3D, FormatGrade.Specialist, SpecialistField.ThreeDAndGameDev);
        Reg("lwo", "LightWave 3D Object", "image/x-lwo", FormatCategory.Model3D, FormatGrade.Legacy, SpecialistField.ThreeDAndGameDev);
        Reg("off", "Object File Format", "model/off", FormatCategory.Model3D, FormatGrade.Specialist, SpecialistField.ThreeDAndGameDev);

        // 17. FONT (Tipografia e caratteri vettoriali/bitmap)
        Reg("ttf", "TrueType Font", "font/ttf", FormatCategory.Font, FormatGrade.Everyday, SpecialistField.WebAndFonts);
        Reg("otf", "OpenType Font", "font/otf", FormatCategory.Font, FormatGrade.Everyday, SpecialistField.WebAndFonts);
        Reg("woff", "Web Open Font Format", "font/woff", FormatCategory.Font, FormatGrade.Everyday, SpecialistField.WebAndFonts);
        Reg("woff2", "Web Open Font Format 2", "font/woff2", FormatCategory.Font, FormatGrade.Everyday, SpecialistField.WebAndFonts);
        Reg("eot", "Embedded OpenType", "application/vnd.ms-fontobject", FormatCategory.Font, FormatGrade.General, SpecialistField.WebAndFonts);
        Reg("ttc", "TrueType Collection", "font/collection", FormatCategory.Font, FormatGrade.Specialist, SpecialistField.WebAndFonts);
        Reg("pfa", "PostScript Font ASCII", "application/x-font-type1", FormatCategory.Font, FormatGrade.Legacy, SpecialistField.WebAndFonts);
        Reg("pfb", "PostScript Font Binary", "application/x-font-type1", FormatCategory.Font, FormatGrade.Legacy, SpecialistField.WebAndFonts);
        Reg("afm", "Adobe Font Metrics", "application/x-font-afm", FormatCategory.Font, FormatGrade.Legacy, SpecialistField.WebAndFonts);
        Reg("dfont", "Mac OS X Data Fork Font", "application/x-dfont", FormatCategory.Font, FormatGrade.Legacy, SpecialistField.WebAndFonts);
        Reg("bdf", "Bitmap Distribution Format", "application/x-font-bdf", FormatCategory.Font, FormatGrade.Legacy, SpecialistField.WebAndFonts);
        Reg("ufo", "Unified Font Object", "application/x-ufo", FormatCategory.Font, FormatGrade.Specialist, SpecialistField.WebAndFonts);

        // 18. GEOSPATIAL (GIS, tracciati satellitari, GPS e mappe)
        Reg("gpx", "GPS Exchange Format", "application/gpx+xml", FormatCategory.Geospatial, FormatGrade.Everyday);
        Reg("kml", "Keyhole Markup Language", "application/vnd.google-earth.kml+xml", FormatCategory.Geospatial, FormatGrade.Everyday);
        Reg("kmz", "Compressed KML", "application/vnd.google-earth.kmz", FormatCategory.Geospatial, FormatGrade.Everyday);
        Reg("geojson", "GeoJSON", "application/geo+json", FormatCategory.Geospatial, FormatGrade.Everyday, SpecialistField.DataAndDev);
        Reg("shp", "ESRI Shapefile", "application/x-shapefile", FormatCategory.Geospatial, FormatGrade.Specialist);
        Reg("shx", "ESRI Shape Index", "application/x-shapefile", FormatCategory.Geospatial, FormatGrade.Specialist);
        Reg("gml", "Geography Markup Language", "application/gml+xml", FormatCategory.Geospatial, FormatGrade.Specialist);
        Reg("osm", "OpenStreetMap XML", "application/x-openstreetmap+xml", FormatCategory.Geospatial, FormatGrade.Specialist);
        Reg("pbf", "OpenStreetMap Protobuf", "application/x-protobuf", FormatCategory.Geospatial, FormatGrade.Specialist, SpecialistField.DataAndDev);
        Reg("tab", "MapInfo Table", "application/x-mapinfo-tab", FormatCategory.Geospatial, FormatGrade.Specialist);
        Reg("mif", "MapInfo Interchange Format", "application/x-mapinfo-mif", FormatCategory.Geospatial, FormatGrade.Specialist);
        Reg("dem", "Digital Elevation Model", "application/octet-stream", FormatCategory.Geospatial, FormatGrade.Specialist);
        Reg("geotiff", "GeoTIFF Dataset", "image/tiff", FormatCategory.Geospatial, FormatGrade.Specialist);
        Reg("gpkg", "OGC GeoPackage", "application/geopackage+sqlite3", FormatCategory.Geospatial, FormatGrade.Specialist);
        Reg("fit", "Garmin Flexible Training", "application/octet-stream", FormatCategory.Geospatial, FormatGrade.Specialist);
    }

    static void Reg(
        string ext,
        string displayName,
        string mimeType,
        FormatCategory category,
        FormatGrade grade,
        SpecialistField field = SpecialistField.None,
        bool inSupp = true,
        bool outSupp = true)
    {
        string normalized = ext.TrimStart('.').ToLowerInvariant();
        var def = new FormatDefinition(normalized, displayName, mimeType, category, grade, field, inSupp, outSupp);
        Map[normalized] = def;
        ByCategoryMap[category].Add(def);
    }

    /// <summary>Numero totale di formati unici registrati nel sistema.</summary>
    public static int Count => Map.Count;

    /// <summary>Tutti i formati registrati.</summary>
    public static IReadOnlyCollection<FormatDefinition> All => Map.Values;

    /// <summary>Cerca la definizione di formato per estensione o percorso di file.</summary>
    public static bool TryGet(string? extOrPath, out FormatDefinition def)
    {
        if (string.IsNullOrWhiteSpace(extOrPath))
        {
            def = default;
            return false;
        }

        string raw = extOrPath.Trim();
        string ext = Path.HasExtension(raw) ? Path.GetExtension(raw) : raw;
        string normalized = ext.TrimStart('.').ToLowerInvariant();
        return Map.TryGetValue(normalized, out def);
    }

    /// <summary>Restituisce la definizione o null se non supportata.</summary>
    public static FormatDefinition? Find(string? extOrPath) => TryGet(extOrPath, out var def) ? def : null;

    /// <summary>Restituisce tutti i formati appartenenti a una specifica categoria.</summary>
    public static IReadOnlyList<FormatDefinition> GetByCategory(FormatCategory category) =>
        ByCategoryMap.TryGetValue(category, out var list) ? list : Array.Empty<FormatDefinition>();

    /// <summary>
    /// Restituisce i formati target visibili nella ruota per una determinata categoria,
    /// filtrati per grado massimo di visibilità e per pacchetti specialistici attivi.
    /// </summary>
    public static IEnumerable<FormatDefinition> GetVisibleTargets(
        FormatCategory category,
        FormatGrade maxGrade = FormatGrade.General,
        SpecialistField enabledFields = SpecialistField.None)
    {
        if (!ByCategoryMap.TryGetValue(category, out var list))
            return Enumerable.Empty<FormatDefinition>();

        return list.Where(f => f.IsOutputSupported && f.IsVisibleInWheel(maxGrade, enabledFields));
    }

    /// <summary>Restituisce tutte le estensioni normalizzate supportate in input.</summary>
    public static IEnumerable<string> GetSupportedInputExtensions() =>
        Map.Values.Where(f => f.IsInputSupported).Select(f => f.NormalizedExtension);

    /// <summary>Restituisce tutte le estensioni normalizzate supportate in output.</summary>
    public static IEnumerable<string> GetSupportedOutputExtensions() =>
        Map.Values.Where(f => f.IsOutputSupported).Select(f => f.NormalizedExtension);

    /// <summary>
    /// Verifica se un formato target è visibile nella ruota tenendo conto dello sblocco contestuale
    /// automatico quando il file sorgente appartiene alla stessa categoria o pacchetto specialistico.
    /// </summary>
    public static bool IsVisibleInWheelForSource(
        FormatDefinition target,
        FormatGrade userMaxGrade,
        SpecialistField userEnabledFields,
        IReadOnlySet<FormatCategory> unlockedCategories,
        SpecialistField unlockedFields)
    {
        if (!target.IsOutputSupported) return false;

        bool contextuallyUnlocked = unlockedCategories.Contains(target.Category) ||
            (target.Field != SpecialistField.None && (unlockedFields & target.Field) != 0);

        if (contextuallyUnlocked)
        {
            var effectiveMaxGrade = (FormatGrade)Math.Max((int)userMaxGrade, (int)FormatGrade.Specialist);
            return target.Grade <= effectiveMaxGrade;
        }

        return target.IsVisibleInWheel(userMaxGrade, userEnabledFields);
    }

    /// <summary>Restituisce l'estensione canonica evitando duplicati alias sulla ruota.</summary>
    public static string CanonicalExtension(string? extension) =>
        (extension ?? "").Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "jpeg" => "jpg",
            "tif" => "tiff",
            "markdown" => "md",
            "htm" => "html",
            "yml" => "yaml",
            "asciidoc" => "adoc",
            var ext => ext
        };

    /// <summary>Restituisce la sigla compatta (&lt;= 6 caratteri) da mostrare sugli spicchi della ruota.</summary>
    public static string GetWheelShortLabel(string? extension)
    {
        string normalized = (extension ?? "").Trim().TrimStart('.').ToLowerInvariant();
        if (normalized.Length == 0) return "";

        return CanonicalExtension(normalized) switch
        {
            "pdfa" => "PDF/A",
            "jpg" => "JPG",
            "tiff" => "TIFF",
            "md" => "MD",
            "html" => "HTML",
            "yaml" => "YAML",
            "adoc" => "ADOC",
            "geojson" => "GJSON",
            "geotiff" => "GTIFF",
            "torrent" => "TORR",
            var canonical => canonical.Length <= 6
                ? canonical.ToUpperInvariant()
                : canonical[..6].ToUpperInvariant()
        };
    }
}

