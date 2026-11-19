using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flip.Core;

namespace Flip;

internal sealed class AppPreferences(SettingsStore? store)
{
    [JsonConstructor]
    public AppPreferences() : this(null) { }

    private bool? _soundEnabled;
    private bool? _gestureEnabled;
    private string? _trigger;
    private string? _language;

    public bool SoundEnabled
    {
        get => _soundEnabled ?? store?.ReadPreferences().SoundEnabled ?? true;
        set => _soundEnabled = value;
    }

    public bool GestureEnabled
    {
        get => _gestureEnabled ?? store?.ReadPreferences().GestureEnabled ?? true;
        set => _gestureEnabled = value;
    }

    public string Trigger
    {
        get => _trigger ?? store?.ReadPreferences().TriggerKey ?? WheelShortcut.Default;
        set => _trigger = value;
    }

    public string Language
    {
        get => _language ?? store?.ReadPreferences().Language ?? "auto";
        set => _language = value;
    }

    internal static AppPreferences Load() => new(SettingsStore.Current);

    internal void Save()
    {
        if (_soundEnabled is null && _gestureEnabled is null && _trigger is null && _language is null)
            return;

        // Salva soltanto i campi modificati, così gli aggiornamenti dell'Hub restano validi.
        (store ?? SettingsStore.Current).UpdatePreferences(_soundEnabled, _gestureEnabled, _trigger, _language);
        _soundEnabled = _gestureEnabled = null;
        _trigger = _language = null;
    }

    internal static string LegacySettingsFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flip", "settings.json");

    internal static LegacyPreferences? ReadLegacy(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<LegacyPreferences>(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    internal sealed class LegacyPreferences
    {
        public bool? SoundEnabled { get; set; }
        public bool? GestureEnabled { get; set; }
        public string? Trigger { get; set; }
        public string? Language { get; set; }
    }
}
