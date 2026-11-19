namespace Flip.Core;

/// <summary>
/// Le 18 macro-categorie di formati gestite da FLIP.
/// </summary>
public enum FormatCategory
{
    RasterAndRaw,
    Vector,
    Pdf,
    DocumentAndEmail,
    Hwp,
    Spreadsheet,
    Presentation,
    TextAndMarkup,
    DataAndConfig,
    EbookAndComic,
    Video,
    Audio,
    Subtitle,
    ArchiveAndDisk,
    Cad,
    Model3D,
    Font,
    Geospatial
}

/// <summary>
/// Grado di frequenza d'uso e visibilità per evitare l'affollamento della ruota radiale.
/// </summary>
public enum FormatGrade
{
    /// <summary>Formati di uso quotidiano mostrati sempre nello spicchio primario (es. PNG, JPG, PDF, MP4, MP3, DOCX).</summary>
    Everyday = 0,

    /// <summary>Formati standard diffusi mostrati nei sub-ring o all'espansione (es. WebP, AVIF, FLAC, ODS, MKV).</summary>
    General = 1,

    /// <summary>Formati specialistici visibili quando il pacchetto di settore è attivo (es. RAW, 3D, CAD, GIS).</summary>
    Specialist = 2,

    /// <summary>Formati storici, legacy o regionali abilitabili su richiesta (es. HWP, WPD, DBF, MOD).</summary>
    Legacy = 3
}

/// <summary>
/// Settori specialistici verticali attivabili singolarmente dall'Hub delle Impostazioni.
/// </summary>
[Flags]
public enum SpecialistField
{
    None = 0,
    RawPhotography = 1 << 0,
    DataAndDev = 1 << 1,
    ThreeDAndGameDev = 1 << 2,
    EbookAndPublishing = 1 << 3,
    CadAndVector = 1 << 4,
    WebAndFonts = 1 << 5,
    LegacyAndRegional = 1 << 6
}

/// <summary>
/// Definizione immutabile dei metadati di un formato di file.
/// </summary>
public readonly record struct FormatDefinition(
    string Extension,
    string DisplayName,
    string MimeType,
    FormatCategory Category,
    FormatGrade Grade,
    SpecialistField Field = SpecialistField.None,
    bool IsInputSupported = true,
    bool IsOutputSupported = true)
{
    /// <summary>Estensione normalizzata in minuscolo senza punto iniziale (es. "png").</summary>
    public string NormalizedExtension => Extension.TrimStart('.').ToLowerInvariant();

    /// <summary>Verifica se il formato rientra nei criteri di visibilità della ruota.</summary>
    public bool IsVisibleInWheel(FormatGrade maxGrade, SpecialistField enabledFields)
    {
        if (Grade > maxGrade) return false;
        if (Field == SpecialistField.None) return true;
        return (enabledFields & Field) != 0;
    }
}

