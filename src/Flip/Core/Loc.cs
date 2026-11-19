using System;
using System.Globalization;

namespace Flip.Core;

/// <summary>
/// Infrastruttura leggera di localizzazione bilingue (Italiano / English) priva di .resx o assembly satellite.
/// Rileva automaticamente la lingua da Windows quando la preferenza è "auto".
/// </summary>
public static class Loc
{
    public const string Auto = "auto";
    public const string Italian = "it";
    public const string English = "en";

    private static volatile string _preference = Auto;
    private static volatile string _currentLanguage = ResolveLanguage(Auto);

    /// <summary>Preferenza salvata dall'utente: "auto", "it" o "en".</summary>
    public static string Preference => _preference;

    /// <summary>Lingua attiva risolta: "it" o "en".</summary>
    public static string CurrentLanguage => _currentLanguage;

    /// <summary>True quando la lingua attiva dell'interfaccia è l'italiano.</summary>
    public static bool IsItalian => string.Equals(_currentLanguage, Italian, StringComparison.Ordinal);

    /// <summary>Notificato al cambio di preferenza o lingua attiva.</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>Normalizza qualsiasi valore di preferenza in "auto", "it" o "en".</summary>
    public static string NormalizePreference(string? preference)
    {
        if (string.IsNullOrWhiteSpace(preference)) return Auto;
        string trimmed = preference.Trim();
        if (trimmed.Equals(Italian, StringComparison.OrdinalIgnoreCase)) return Italian;
        if (trimmed.Equals(English, StringComparison.OrdinalIgnoreCase)) return English;
        return Auto;
    }

    /// <summary>
    /// Funzione pura di risoluzione lingua:
    /// "it" → "it"; "en" → "en"; "auto"/altro → "it" se la lingua UI di Windows è italiana, altrimenti "en".
    /// </summary>
    public static string ResolveLanguage(string? preference, CultureInfo? uiCulture = null)
    {
        string normalized = NormalizePreference(preference);
        if (normalized == Italian) return Italian;
        if (normalized == English) return English;

        CultureInfo culture = uiCulture ?? CultureInfo.CurrentUICulture;
        return culture.TwoLetterISOLanguageName.Equals(Italian, StringComparison.OrdinalIgnoreCase)
            ? Italian
            : English;
    }

    /// <summary>Imposta la preferenza lingua ("auto", "it", "en") e aggiorna subito la lingua attiva.</summary>
    public static void SetPreference(string? preference)
    {
        string nextPref = NormalizePreference(preference);
        string nextLang = ResolveLanguage(nextPref);
        bool changed = !string.Equals(_preference, nextPref, StringComparison.Ordinal) ||
                       !string.Equals(_currentLanguage, nextLang, StringComparison.Ordinal);
        _preference = nextPref;
        _currentLanguage = nextLang;
        if (changed)
            LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Restituisce la stringa localizzata nella lingua attiva.</summary>
    public static string T(string it, string en) => IsItalian ? it : en;

    /// <summary>Formatta la stringa localizzata nella lingua attiva.</summary>
    public static string F(string itFormat, string enFormat, params object?[] args) =>
        string.Format(IsItalian ? itFormat : enFormat, args);
}
