using Flip.Core;

namespace Flip.Routing;

/// <summary>
/// Arco orientato nel grafo delle conversioni supportate.
/// Normalizza sempre le estensioni senza punto e in minuscolo.
/// </summary>
public readonly record struct ConversionEdge
{
    public string SourceExtension { get; }
    public string TargetExtension { get; }
    public string EngineId { get; }
    public int BaseCost { get; }

    public ConversionEdge(string sourceExtension, string targetExtension, string engineId, int baseCost = 10)
    {
        SourceExtension = (sourceExtension ?? "").TrimStart('.').ToLowerInvariant();
        TargetExtension = (targetExtension ?? "").TrimStart('.').ToLowerInvariant();
        EngineId = engineId ?? "";
        BaseCost = baseCost > 0 ? baseCost : 10;
    }
}

/// <summary>
/// Contesto passato al motore per l'esecuzione di una singola conversione.
/// </summary>
public sealed class ConversionContext
{
    public string InputFilePath { get; set; } = "";
    public string OutputFilePath { get; set; } = "";
    public string SourceExtension { get; set; } = "";
    public string TargetExtension { get; set; } = "";
    public FlipSettings Settings { get; set; } = new();
    public Dictionary<string, string> CustomParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public IProgress<double>? Progress { get; set; }
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}

/// <summary>
/// Risultato dell'esecuzione di una conversione da parte di un motore.
/// </summary>
public readonly record struct EngineExecutionResult(
    bool Success,
    string OutputFilePath,
    string? ErrorMessage = null,
    long OutputBytes = 0)
{
    private readonly IReadOnlyList<string>? outputFilePaths;

    /// <summary>I file prodotti in ordine, a partire da OutputFilePath. OutputBytes è il totale.</summary>
    public IReadOnlyList<string> OutputFilePaths
    {
        get => outputFilePaths ?? (string.IsNullOrEmpty(OutputFilePath) ? [] : [OutputFilePath]);
        init => outputFilePaths = value;
    }
}

/// <summary>
/// Interfaccia per i motori di conversione (nativi o esterni) registrati nel RoutePlanner.
/// </summary>
public interface IConversionEngine
{
    /// <summary>Identificativo univoco del motore (es. "image_builtin", "ffmpeg", "pandoc").</summary>
    string Id { get; }

    /// <summary>Nome visualizzabile dall'utente.</summary>
    string DisplayName { get; }

    /// <summary>Indica se il motore dipende da un binario/processo esterno.</summary>
    bool IsExternal { get; }

    /// <summary>Verifica se il motore è attualmente installato/configurato e pronto all'uso.</summary>
    bool IsAvailable(FlipSettings settings);

    /// <summary>Elenco di tutti gli archi di conversione supportati direttamente dal motore.</summary>
    IReadOnlyList<ConversionEdge> SupportedEdges { get; }

    /// <summary>Esegue la conversione specificata nel contesto.</summary>
    Task<EngineExecutionResult> ConvertAsync(ConversionContext context);
}

