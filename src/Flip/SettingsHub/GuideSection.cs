using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Flip.Core;

namespace Flip.SettingsHub;

/// <summary>
/// Sezione 7: scorciatoie essenziali come keycap, sei capacità chiave di FLIP e accesso al tour iniziale.
/// </summary>
public static class GuideSection
{
    private static readonly SolidColorBrush KeyBg = Frozen(0x1E, 0x21, 0x2B);
    private static readonly SolidColorBrush KeyBorder = Frozen(0x3A, 0x3F, 0x4E);

    public static UIElement Build()
    {
        var settings = SettingsStore.Current.CreateSnapshot();
        var root = new StackPanel { Margin = new Thickness(0, 4, 0, 12) };

        root.Children.Add(new TextBlock
        {
            Text = Loc.T("Guida & Scorciatoie", "Guide & Shortcuts"),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = HubUi.TextPrimary,
            Padding = new Thickness(0, 2, 0, 0),
            Margin = new Thickness(0, 4, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("Gesti essenziali e cosa può fare FLIP.", "Core gestures and what FLIP can do."),
            FontSize = 13,
            Foreground = HubUi.TextSecondary,
            Margin = new Thickness(0, 0, 0, 18)
        });

        // ── Scorciatoie ─────────────────────────────────────────────────────
        var cardKeys = HubUi.CreateCard(Loc.T("Scorciatoie", "Shortcuts"));
        var stackKeys = (StackPanel)cardKeys.Child;
        string trigger = KeyChoice.Describe(string.IsNullOrWhiteSpace(settings.TriggerKey) ? "Down" : settings.TriggerKey);
        string clipboard = string.IsNullOrWhiteSpace(settings.EnableClipboardFlipShortcut) ? "Ctrl+Shift+F" : settings.EnableClipboardFlipShortcut.Trim();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        (string[] Keys, string Separator, string Action)[] shortcuts =
        [
            ([Loc.T("Trascina", "Drag"), trigger], "+", Loc.T("Apri la ruota", "Open the wheel")),
            (["Tab", Loc.T("Rotellina", "Scroll")], "/", Loc.T("Passa da Formati a Strumenti", "Flip between Formats and Tools")),
            ([Loc.T("Spazio", "Space")], "", Loc.T("Anteprima rapida · 3D, PDF, immagini, audio", "QuickLook · 3D, PDF, images, audio")),
            ([clipboard], "", Loc.T("Converti dagli appunti", "Convert from clipboard")),
            (["A"], "", Loc.T("Salva o richiama l'inquadratura 3D", "Save or recall 3D camera angle")),
            (["Ctrl+S", "Ctrl+C", "P"], "·", Loc.T("Salva, copia o oscura nel ritaglio", "Save, copy, or blur in crop editor")),
            (["Esc"], "", Loc.T("Chiudi ruota o editor", "Close wheel or editor"))
        ];
        for (int i = 0; i < shortcuts.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var keys = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
            for (int k = 0; k < shortcuts[i].Keys.Length; k++)
            {
                if (k > 0 && shortcuts[i].Separator.Length > 0)
                {
                    keys.Children.Add(new TextBlock
                    {
                        Text = shortcuts[i].Separator,
                        FontSize = 11,
                        Foreground = HubUi.TextSecondary,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 6, 0)
                    });
                }
                keys.Children.Add(KeyCap(shortcuts[i].Keys[k]));
            }
            Grid.SetRow(keys, i);
            grid.Children.Add(keys);

            var action = new TextBlock
            {
                Text = shortcuts[i].Action,
                FontSize = 13,
                Foreground = HubUi.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(action, i);
            Grid.SetColumn(action, 1);
            grid.Children.Add(action);

            if (i < shortcuts.Length - 1)
            {
                var divider = new Border
                {
                    Height = 1,
                    Background = HubUi.CardBorder,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Opacity = 0.6
                };
                Grid.SetRow(divider, i);
                Grid.SetColumnSpan(divider, 2);
                grid.Children.Add(divider);
            }
        }
        stackKeys.Children.Add(grid);
        root.Children.Add(cardKeys);

        // ── Cosa può fare FLIP (2 × 3) ──────────────────────────────────────
        var cardFeatures = HubUi.CreateCard(Loc.T("Cosa può fare FLIP", "What FLIP can do"));
        var stackFeatures = (StackPanel)cardFeatures.Child;
        var features = new UniformGrid { Columns = 2, Rows = 3 };
        (string Glyph, string Title, string Line)[] items =
        [
            ("⇄", Loc.T("Conversioni & PDF/A-2b", "Conversions & PDF/A-2b"),
                Loc.T("Immagini, audio, video, 3D, documenti, archivi e PDF/A-2b.", "Images, audio, video, 3D, docs, archives, and PDF/A-2b.")),
            ("✂", Loc.T("Immagini & Privacy", "Images & Privacy"),
                Loc.T("Ritaglio 9:16/manuale, ruota, scala, sfondo AI, oscura aree, EXIF.", "9:16 or free crop, rotate, scale, AI cutout, privacy blur, EXIF.")),
            ("◈", Loc.T("QuickLook 3D · PDF · Palette", "QuickLook 3D · PDF · Palette"),
                Loc.T("Vista 3D con Turntable 360°, copia testo PDF, palette CSS e \"Aa\".", "3D viewer & 360° Turntable, PDF text copy, CSS palette & \"Aa\" contrast.")),
            ("◉", Loc.T("Audio AI & OCR locali", "Local Audio AI & OCR"),
                Loc.T("Trascrizione Whisper (SRT/TXT) e OCR immagini, 100% offline.", "Whisper transcription (SRT/TXT) and image OCR, 100% offline.")),
            ("⚡", Loc.T("Automazione & Batch", "Automation & Batch"),
                Loc.T("Cartelle monitorate, Preset riutilizzabili, Unisci PDF e batch.", "Watch folders, reusable Presets, PDF merge, and batch jobs.")),
            ("◎", Loc.T("Browser, Appunti & Tobii", "Browser, Clipboard & Tobii"),
                Loc.T("Drag da browser, conversione appunti e controllo con lo sguardo.", "Browser drag-drop, clipboard flip, and hands-free eye tracking."))
        ];
        for (int i = 0; i < items.Length; i++)
        {
            int row = i / 2;
            int col = i % 2;
            var tile = new Border
            {
                Background = HubUi.InputBg,
                BorderBrush = HubUi.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(col == 0 ? 0 : 5, row == 0 ? 0 : 5, col == 0 ? 5 : 0, row == 2 ? 0 : 5)
            };
            var tileGrid = new Grid();
            tileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            tileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tileGrid.Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Background = Frozen(0x3B, 0x24, 0x14),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = items[i].Glyph,
                    FontSize = 12,
                    Foreground = HubUi.Accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = items[i].Title,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = HubUi.TextPrimary
            });
            text.Children.Add(new TextBlock
            {
                Text = items[i].Line,
                FontSize = 11.2,
                Foreground = HubUi.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 15,
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetColumn(text, 1);
            tileGrid.Children.Add(text);
            tile.Child = tileGrid;
            features.Children.Add(tile);
        }
        stackFeatures.Children.Add(features);
        root.Children.Add(cardFeatures);

        var tour = HubUi.CreateButton(Loc.T("Riapri tour iniziale", "Replay welcome tour"), false, FirstRunWelcomeWindow.ShowSingleton);
        tour.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(tour);
        return root;
    }

    private static Border KeyCap(string text) => new()
    {
        Background = KeyBg,
        BorderBrush = KeyBorder,
        BorderThickness = new Thickness(1, 1, 1, 2),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(8, 2, 8, 3),
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = HubUi.TextPrimary,
            FontFamily = new FontFamily("Segoe UI")
        }
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
