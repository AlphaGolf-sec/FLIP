using Flip.Core;

namespace Flip.Routing;

/// <summary>
/// Contesto passato a uno strumento rapido per l'esecuzione sul retro della ruota.
/// </summary>
public sealed class QuickToolContext
{
    public IReadOnlyList<string> InputFilePaths { get; set; } = [];
    public FlipSettings Settings { get; set; } = new();
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public IProgress<double>? Progress { get; set; }
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}

/// <summary>
/// Risultato dell'operazione di uno strumento rapido.
/// </summary>
public readonly record struct QuickToolResult(
    bool Success,
    IReadOnlyList<string> OutputFilePaths,
    string? ErrorMessage = null);

/// <summary>
/// Interfaccia per gli strumenti rapidi (Quick Tools) accessibili sul retro della ruota
/// (es. Crop, Rotate, Unisci PDF, Impacchetta ZIP, Target Size, Remove BG, OCR).
/// </summary>
public interface IQuickTool
{
    /// <summary>Identificativo univoco dello strumento (es. "crop", "rotate", "merge_pdf").</summary>
    string Id { get; }

    /// <summary>Nome visualizzabile dall'utente.</summary>
    string DisplayName { get; }

    /// <summary>Glifo o icona dello strumento (es. Segoe Fluent Icons o simbolo Unicode).</summary>
    string IconGlyph { get; }

    /// <summary>Indica se lo strumento supporta l'elaborazione combinata di più file contemporaneamente.</summary>
    bool SupportsMultipleFiles { get; }

    /// <summary>Verifica se lo strumento può operare sull'insieme di estensioni fornite e con le impostazioni attuali.</summary>
    bool CanHandle(IReadOnlyList<string> inputExtensions, FlipSettings settings);

    /// <summary>Esegue l'operazione sui file specificati nel contesto.</summary>
    Task<QuickToolResult> ExecuteAsync(QuickToolContext context);
}

