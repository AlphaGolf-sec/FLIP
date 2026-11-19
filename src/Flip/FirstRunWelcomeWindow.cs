using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Flip.Core;
using Flip.SettingsHub;
using Shapes = System.Windows.Shapes;

namespace Flip;

/// <summary>
/// Tour guidato di primo avvio (680x430, dark FLIP + accento #FF7A18): 4 passi cliccabili, ognuno con
/// un titolo, al massimo due righe di testo e un'illustrazione vettoriale animata.
/// Le animazioni restano spente quando <see cref="ScreenEdgeAuraWindow.Suppressed"/> è attivo (test e anteprime).
/// </summary>
internal sealed class FirstRunWelcomeWindow : Window
{
    private static FirstRunWelcomeWindow? _instance;
    internal static Action<string>? TrayNotifier { get; set; }

    private static readonly SolidColorBrush BgDark = Frozen(0x12, 0x13, 0x18);
    private static readonly SolidColorBrush CardBg = Frozen(0x1A, 0x1C, 0x24);
    private static readonly SolidColorBrush CardBorder = Frozen(0x2A, 0x2E, 0x39);
    private static readonly SolidColorBrush IllustrationBg = Frozen(0x14, 0x16, 0x1D);
    private static readonly SolidColorBrush SectorRest = Frozen(0x23, 0x26, 0x31);
    private static readonly SolidColorBrush SectorStroke = Frozen(0x46, 0x4D, 0x5E);
    private static readonly SolidColorBrush AccentSoft = Frozen(0x3B, 0x24, 0x14);
    private static readonly SolidColorBrush Accent = Frozen(0xFF, 0x7A, 0x18);
    private static readonly SolidColorBrush AccentHover = Frozen(0xFF, 0x6B, 0x00);
    private static readonly SolidColorBrush TextPrimary = Frozen(0xF3, 0xF4, 0xF6);
    private static readonly SolidColorBrush TextSecondary = Frozen(0x9C, 0xA3, 0xAF);

    private static string[] GetStepLabels() =>
    [
        Loc.T("Trascina", "Drag"),
        Loc.T("Formati & Strumenti", "Formats & Tools"),
        Loc.T("Anteprima", "QuickLook"),
        Loc.T("Pronto", "Ready")
    ];
    private static readonly string[] FrontLabels = ["PDF", "WEBP", "JPG", "PNG", "DOCX", "MP4"];
    private static string[] GetToolLabels() =>
    [
        Loc.T("Ruota ›", "Rotate ›"),
        Loc.T("Ritaglia ›", "Crop ›"),
        Loc.T("Scala ›", "Scale ›"),
        "Palette",
        "OCR",
        "ZIP"
    ];
    private static string[] GetCropChildren() => [Loc.T("Manuale", "Manual"), "9:16", "4:5", "1:1"];
    private const int CropSector = 1;

    internal static bool IsSingletonOpen => _instance?.IsVisible == true;

    internal CheckBox GazeToggle { get; }
    internal Button StartButton { get; }
    internal int CurrentStepIndex => _currentStep;
    internal int StepTabCount => _stepContents.Length;
    internal int HoveredMiniWheelCount => _miniWheelHovers;

    private static bool Animate => !ScreenEdgeAuraWindow.Suppressed;

    private int _currentStep;
    private readonly bool[] _visitedSteps = [true, false, false, false];
    private readonly Border[] _stepDots = new Border[4];
    private readonly TextBlock[] _stepNumbers = new TextBlock[4];
    private readonly TextBlock[] _stepTitles = new TextBlock[4];
    private readonly Border[] _progressSegments = new Border[4];
    private readonly UIElement[] _stepContents = new UIElement[4];
    private readonly ContentControl _stepHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TranslateTransform _stepShift = new();
    private readonly Button _backButton;
    private readonly TextBlock _stepCounterText;
    private readonly System.Windows.Threading.DispatcherTimer _demoTicker = new() { Interval = TimeSpan.FromMilliseconds(1700) };

    // Passo 1: mini-ruota interattiva.
    private int _miniWheelHovers;

    // Passo 2: cambio faccia + sub-ring.
    private MiniWheel? _flipWheel;
    private readonly ScaleTransform _flipScale = new(1, 1);
    private Canvas? _fanLayer;
    private readonly Shapes.Path[] _fanSectors = new Shapes.Path[4];
    private bool _toolsSideShown;
    private bool _fanOpen;

    // Passo 3: QuickLook a rotazione.
    private readonly UIElement[] _quickLookFaces = new UIElement[3];
    private TextBlock? _quickLookCaption;
    private int _quickLookIndex;

    internal static void ShowSingleton()
    {
        if (_instance == null)
        {
            var win = new FirstRunWelcomeWindow();
            win.Closed += (_, _) =>
            {
                if (ReferenceEquals(_instance, win)) _instance = null;
            };
            _instance = win;
        }

        if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
        if (!_instance.IsVisible)
        {
            _instance.SelectStep(0);
            _instance.Show();
        }
        _instance.Activate();
        _instance.Focus();
    }

    internal static void CloseSingleton()
    {
        _instance?.Close();
        _instance = null;
    }

    internal FirstRunWelcomeWindow()
    {
        Title = Loc.T("FLIP · Benvenuto", "FLIP · Welcome");
        Width = 680;
        Height = 430;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        Resources.MergedDictionaries.Add(HubUi.DarkControls.Value);

        var currentSettings = SettingsStore.Current.CreateSnapshot();
        GazeToggle = new CheckBox
        {
            IsChecked = currentSettings.GazeAccessibilityMode,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = Loc.T("Accessibilità Tobii", "Tobii Accessibility"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary },
                    new TextBlock { Text = Loc.T("Usa FLIP con lo sguardo, senza mani.", "Hands-free control with eye tracking."), FontSize = 11, Foreground = TextSecondary, Margin = new Thickness(0, 1, 0, 0) }
                }
            }
        };
        GazeToggle.Checked += (_, _) => SettingsStore.Current.Update(s => s.GazeAccessibilityMode = true);
        GazeToggle.Unchecked += (_, _) => SettingsStore.Current.Update(s => s.GazeAccessibilityMode = false);

        var outerBorder = new Border
        {
            Background = BgDark,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22, 16, 22, 16)
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── Intestazione ────────────────────────────────────────────────────
        var header = new Grid { Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch (InvalidOperationException) { }
            }
        };

        var badge = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = Accent,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "F",
                FontSize = 17,
                FontWeight = FontWeights.Black,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        header.Children.Add(badge);

        var titleStack = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock { Text = Loc.T("Benvenuto in FLIP", "Welcome to FLIP"), FontSize = 16, FontWeight = FontWeights.Bold, Foreground = TextPrimary });
        titleStack.Children.Add(new TextBlock
        {
            Text = Loc.F("v{0} · 4 passi, un minuto", "v{0} · 4 quick steps", UpdateChecker.CurrentVersion),
            FontSize = 11,
            Foreground = TextSecondary,
            Margin = new Thickness(0, 1, 0, 0)
        });
        Grid.SetColumn(titleStack, 1);
        header.Children.Add(titleStack);

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 28,
            Height = 28,
            Background = CardBg,
            Foreground = TextSecondary,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            FontSize = 12,
            Cursor = Cursors.Hand,
            ToolTip = Loc.T("Chiudi", "Close"),
            VerticalAlignment = VerticalAlignment.Center
        };
        closeBtn.Click += (_, _) => CompleteOnboardingAndClose(showWelcomeNotification: false);
        Grid.SetColumn(closeBtn, 2);
        header.Children.Add(closeBtn);
        root.Children.Add(header);

        // ── Indicatori di passo cliccabili ──────────────────────────────────
        var stepsBar = new StackPanel { Margin = new Thickness(0, 14, 0, 10) };
        var stepsGrid = new Grid();
        var progressTrack = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        string[] stepLabels = GetStepLabels();
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            stepsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressTrack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _stepNumbers[i] = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _stepDots[i] = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Child = _stepNumbers[i],
                VerticalAlignment = VerticalAlignment.Center
            };
            _stepTitles[i] = new TextBlock
            {
                Text = stepLabels[i],
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var item = new StackPanel { Orientation = Orientation.Horizontal, Children = { _stepDots[i], _stepTitles[i] } };
            var hit = new Border
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Padding = new Thickness(0, 2, 6, 2),
                Child = item,
                ToolTip = Loc.F("Passo {0}", "Step {0}", i + 1)
            };
            hit.MouseLeftButtonUp += (_, _) => SelectStep(idx);
            Grid.SetColumn(hit, i);
            stepsGrid.Children.Add(hit);

            var seg = new Border
            {
                Height = 2,
                CornerRadius = new CornerRadius(1),
                Background = CardBorder,
                Margin = new Thickness(i == 0 ? 0 : 3, 0, i == 3 ? 0 : 3, 0)
            };
            _progressSegments[i] = seg;
            Grid.SetColumn(seg, i);
            progressTrack.Children.Add(seg);
        }
        stepsBar.Children.Add(stepsGrid);
        stepsBar.Children.Add(progressTrack);
        Grid.SetRow(stepsBar, 1);
        root.Children.Add(stepsBar);

        // ── Contenuto del passo ─────────────────────────────────────────────
        string keyName = KeyChoice.Describe(string.IsNullOrWhiteSpace(currentSettings.TriggerKey) ? "Down" : currentSettings.TriggerKey);
        _stepContents[0] = BuildStep(1,
            Loc.T("Trascina & Converti", "Drag & Convert"),
            Loc.F("Trascina un file e premi {0}.", "Drag any file and press {0}.", keyName),
            Loc.T("Rilascia su un formato: il nuovo file appare accanto all'originale.", "Drop onto a format — the output saves right next to the original."),
            BuildDragVisual());
        _stepContents[1] = BuildStep(2,
            Loc.T("Formati & Strumenti", "Formats & Tools"),
            Loc.T("Tab o rotellina girano la ruota.", "Press Tab or scroll to flip the wheel."),
            Loc.T("Ruota, Ritaglia e Scala aprono altre opzioni.", "Rotate, Crop, and Scale expand into quick presets."),
            BuildFlipVisual());
        _stepContents[2] = BuildStep(3,
            Loc.T("QuickLook & Editor", "QuickLook & Editor"),
            Loc.T("Spazio apre l'anteprima: 3D, PDF, palette.", "Press Space to preview 3D models, PDFs, audio, and palettes."),
            Loc.T("Ritaglia & Oscura: Ctrl+S salva, Ctrl+C copia, P oscura.", "Crop & Blur: Ctrl+S saves, Ctrl+C copies, P blurs sensitive areas."),
            BuildQuickLookVisual());
        _stepContents[3] = BuildStep(4,
            Loc.T("Pronto al via", "Ready to go"),
            Loc.T("Motori opzionali e accessibilità, se ti servono.", "Optional local engines and accessibility, whenever you need them."),
            Loc.T("Puoi cambiarli quando vuoi nelle Impostazioni.", "You can adjust these anytime in Settings."),
            BuildReadyVisual());

        _stepHost.RenderTransform = _stepShift;
        var stepCard = new Border
        {
            Background = CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 14, 16, 14),
            ClipToBounds = true,
            Child = _stepHost
        };
        Grid.SetRow(stepCard, 2);
        root.Children.Add(stepCard);

        // ── Navigazione ─────────────────────────────────────────────────────
        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var skipBtn = new Button
        {
            Content = Loc.T("Salta", "Skip"),
            Background = Brushes.Transparent,
            Foreground = TextSecondary,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 7, 12, 7),
            FontSize = 12,
            Cursor = Cursors.Hand
        };
        skipBtn.Click += (_, _) => CompleteOnboardingAndClose(showWelcomeNotification: true);
        footer.Children.Add(skipBtn);

        _stepCounterText = new TextBlock
        {
            Foreground = TextSecondary,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(_stepCounterText, 1);
        footer.Children.Add(_stepCounterText);

        _backButton = new Button
        {
            Content = Loc.T("Indietro", "Back"),
            Background = CardBg,
            Foreground = TextPrimary,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand
        };
        _backButton.Click += (_, _) =>
        {
            if (_currentStep > 0) SelectStep(_currentStep - 1);
        };
        Grid.SetColumn(_backButton, 2);
        footer.Children.Add(_backButton);

        StartButton = new Button
        {
            Content = Loc.T("Avanti →", "Next →"),
            Tag = "Primary",
            Background = Accent,
            Foreground = Brushes.White,
            BorderBrush = AccentHover,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 8, 18, 8),
            FontSize = 12.8,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand,
            IsDefault = true
        };
        StartButton.Click += (_, _) =>
        {
            if (_currentStep < _stepContents.Length - 1)
                SelectStep(_currentStep + 1);
            else
                CompleteOnboardingAndClose(showWelcomeNotification: true);
        };
        Grid.SetColumn(StartButton, 3);
        footer.Children.Add(StartButton);

        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        outerBorder.Child = root;
        Content = outerBorder;

        PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    CompleteOnboardingAndClose(showWelcomeNotification: false);
                    break;
                case Key.Tab when _currentStep == 1:
                    e.Handled = true;
                    ToggleStep2FlipDemo();
                    break;
                case Key.Right:
                    e.Handled = true;
                    SelectStep(_currentStep + 1);
                    break;
                case Key.Left:
                    e.Handled = true;
                    SelectStep(_currentStep - 1);
                    break;
            }
        };
        PreviewMouseWheel += (_, e) =>
        {
            if (_currentStep != 1) return;
            e.Handled = true;
            ToggleStep2FlipDemo();
        };

        _demoTicker.Tick += (_, _) => AdvanceDemo();
        Loaded += (_, _) => { if (Animate) _demoTicker.Start(); };
        Closed += (_, _) => _demoTicker.Stop();

        SelectStep(0);
    }

    internal void SelectStep(int stepIndex)
    {
        int next = Math.Clamp(stepIndex, 0, _stepContents.Length - 1);
        bool changed = next != _currentStep || _stepHost.Content == null;
        int direction = Math.Sign(next - _currentStep);
        _currentStep = next;
        _visitedSteps[_currentStep] = true;
        _stepHost.Content = _stepContents[_currentStep];
        _stepCounterText.Text = $"{_currentStep + 1} / {_stepContents.Length}";
        _backButton.Visibility = _currentStep == 0 ? Visibility.Collapsed : Visibility.Visible;
        StartButton.Content = _currentStep == _stepContents.Length - 1 ? Loc.T("Inizia a usare FLIP", "Start using FLIP") : Loc.T("Avanti →", "Next →");

        for (int i = 0; i < _stepDots.Length; i++)
        {
            bool active = i == _currentStep;
            bool done = _visitedSteps[i] && !active;
            _stepDots[i].Background = active ? Accent : done ? AccentSoft : CardBg;
            _stepDots[i].BorderBrush = active || done ? Accent : CardBorder;
            _stepNumbers[i].Text = done ? "✓" : (i + 1).ToString();
            _stepNumbers[i].Foreground = active ? Brushes.White : done ? Accent : TextSecondary;
            _stepTitles[i].Foreground = active ? TextPrimary : TextSecondary;
            _progressSegments[i].Background = i <= _currentStep ? Accent : CardBorder;
        }

        if (_currentStep == 1) ResetFlipDemo();
        if (!changed || !Animate) return;

        // Transizione morbida: dissolvenza + scorrimento di 16 px nella direzione del passo.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _stepHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        _stepShift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(direction < 0 ? -16 : 16, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
    }

    internal void InvokeTabFlipPreviewForTest() => ToggleStep2FlipDemo();

    internal void CompleteOnboardingAndClose(bool showWelcomeNotification = false)
    {
        SettingsStore.Current.Update(s => s.OnboardingCompleted = true);
        if (showWelcomeNotification && !ScreenEdgeAuraWindow.Suppressed)
            TrayNotifier?.Invoke(Loc.T(
                "FLIP è attivo nell'area di notifica. Trascina un file e premi il tasto di attivazione.",
                "FLIP is running in the system tray. Drag any file and press your trigger key."));
        Close();
    }

    private void AdvanceDemo()
    {
        if (_currentStep == 1)
        {
            // Ciclo: Formati → Strumenti → sub-ring Ritaglia → Formati.
            if (!_toolsSideShown) ToggleStep2FlipDemo();
            else if (!_fanOpen) SetFanOpen(true);
            else ToggleStep2FlipDemo();
        }
        else if (_currentStep == 2)
        {
            ShowQuickLookFace((_quickLookIndex + 1) % _quickLookFaces.Length);
        }
    }

    // ── Struttura comune di un passo ────────────────────────────────────────

    private static Grid BuildStep(int number, string title, string line1, string line2, UIElement visual)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.12, GridUnitType.Star) });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        left.Children.Add(new TextBlock
        {
            Text = Loc.F("PASSO {0}", "STEP {0}", number),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Accent
        });
        left.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = TextPrimary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 10)
        });
        left.Children.Add(new TextBlock
        {
            Text = line1 + "\n" + line2,
            FontSize = 12.5,
            Foreground = TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19
        });
        grid.Children.Add(left);

        Grid.SetColumn(visual, 1);
        grid.Children.Add(visual);
        return grid;
    }

    private static Border IllustrationFrame(UIElement child) => new()
    {
        Background = IllustrationBg,
        BorderBrush = CardBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Child = new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(8), Child = child }
    };

    // ── Passo 1: trascina un file sulla mini-ruota ──────────────────────────

    private Border BuildDragVisual()
    {
        var canvas = new Canvas { Width = 280, Height = 210 };
        var wheel = new MiniWheel(new Point(170, 105), 30, 82, FrontLabels, 10);
        wheel.SectorHovered += _ => _miniWheelHovers++;
        canvas.Children.Add(wheel.Root);

        // File che scivola verso il centro della ruota, poi la ruota "risponde" evidenziando WEBP.
        var fileShift = new TranslateTransform();
        var file = new Border
        {
            Width = 34,
            Height = 42,
            CornerRadius = new CornerRadius(4),
            Background = Frozen(0x2A, 0x2E, 0x39),
            BorderBrush = TextSecondary,
            BorderThickness = new Thickness(1),
            RenderTransform = fileShift,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "PNG",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = TextPrimary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 5)
            }
        };
        Canvas.SetLeft(file, 8);
        Canvas.SetTop(file, 140);
        canvas.Children.Add(file);

        var cursor = new Shapes.Path
        {
            Data = Geometry.Parse("M 0,0 L 0,16 L 4.5,12 L 7.5,18 L 10,17 L 7,11 L 12,11 Z"),
            Fill = Brushes.White,
            Stroke = BgDark,
            StrokeThickness = 1,
            RenderTransform = fileShift,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(cursor, 30);
        Canvas.SetTop(cursor, 160);
        canvas.Children.Add(cursor);

        var keyCap = CreateKeyCap("↓");
        keyCap.Opacity = Animate ? 0 : 1;
        Canvas.SetLeft(keyCap, 8);
        Canvas.SetTop(keyCap, 12);
        canvas.Children.Add(keyCap);

        if (Animate)
        {
            var period = TimeSpan.FromMilliseconds(2600);
            fileShift.BeginAnimation(TranslateTransform.XProperty, KeyFrames(period, (0, 0), (0.9, 145), (2.6, 145)));
            fileShift.BeginAnimation(TranslateTransform.YProperty, KeyFrames(period, (0, 0), (0.9, -56), (2.6, -56)));
            file.BeginAnimation(OpacityProperty, KeyFrames(period, (0, 0), (0.2, 1), (1.6, 1), (1.9, 0), (2.6, 0)));
            cursor.BeginAnimation(OpacityProperty, KeyFrames(period, (0, 0), (0.2, 1), (2.2, 1), (2.5, 0), (2.6, 0)));
            keyCap.BeginAnimation(OpacityProperty, KeyFrames(period, (0, 0), (0.85, 0), (1.0, 1), (2.2, 1), (2.5, 0), (2.6, 0)));
            wheel.Root.BeginAnimation(OpacityProperty, KeyFrames(period, (0, 0.25), (0.9, 0.25), (1.1, 1), (2.3, 1), (2.6, 0.25)));
            wheel.PlayDemoHighlight(1, period, 1.35, 2.3);
        }
        return IllustrationFrame(canvas);
    }

    // ── Passo 2: cambio faccia + apertura del sub-ring ──────────────────────

    private Border BuildFlipVisual()
    {
        var canvas = new Canvas { Width = 280, Height = 210 };
        var center = new Point(120, 110);
        _flipWheel = new MiniWheel(center, 28, 70, FrontLabels, 9.5);
        _flipWheel.Root.RenderTransformOrigin = new Point(center.X / 280, center.Y / 210);
        _flipWheel.Root.Width = 280;
        _flipWheel.Root.Height = 210;
        _flipWheel.Root.RenderTransform = _flipScale;
        _flipWheel.CenterText.Text = Loc.T("Formati", "Formats");

        _fanLayer = new Canvas { Width = 280, Height = 210, Opacity = 0, IsHitTestVisible = false };
        string[] cropChildren = GetCropChildren();
        double parentAngle = CropSector * 360.0 / FrontLabels.Length;
        double sweep = 92, step = sweep / cropChildren.Length, start = parentAngle - sweep / 2;
        for (int i = 0; i < cropChildren.Length; i++)
        {
            double a0 = start + i * step + 1.2, a1 = start + (i + 1) * step - 1.2;
            var sector = new Shapes.Path
            {
                Data = CreateAnnularSector(center, 76, 108, a0, a1),
                Fill = SectorRest,
                Stroke = SectorStroke,
                StrokeThickness = 1
            };
            _fanSectors[i] = sector;
            _fanLayer.Children.Add(sector);
            var mid = PolarPoint(center, 92, (a0 + a1) / 2);
            var label = new TextBlock { Text = cropChildren[i], FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, mid.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, mid.Y - label.DesiredSize.Height / 2);
            _fanLayer.Children.Add(label);
        }

        canvas.Children.Add(_flipWheel.Root);
        canvas.Children.Add(_fanLayer);

        var pill = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = BgDark,
            BorderBrush = Frozen(0x7A, 0x3D, 0x15),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 2, 10, 3),
            Child = new TextBlock { Text = Loc.T("Tab · rotellina", "Tab · scroll"), FontSize = 9.5, Foreground = TextSecondary }
        };
        Canvas.SetLeft(pill, 8);
        Canvas.SetTop(pill, 8);
        canvas.Children.Add(pill);
        return IllustrationFrame(canvas);
    }

    private void ResetFlipDemo()
    {
        _toolsSideShown = false;
        _flipScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _flipScale.ScaleY = 1;
        ApplyFlipFace();
        SetFanOpen(false);
    }

    private void ToggleStep2FlipDemo()
    {
        _toolsSideShown = !_toolsSideShown;
        SetFanOpen(false);
        if (!Animate)
        {
            ApplyFlipFace();
            return;
        }

        // Flip verticale come la ruota vera: ScaleY 1 → 0 → 1 con cambio faccia a metà.
        var down = new DoubleAnimation(1, 0.04, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        down.Completed += (_, _) =>
        {
            ApplyFlipFace();
            _flipScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.04, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        };
        _flipScale.BeginAnimation(ScaleTransform.ScaleYProperty, down);
    }

    private void ApplyFlipFace()
    {
        if (_flipWheel == null) return;
        _flipWheel.SetLabels(_toolsSideShown ? GetToolLabels() : FrontLabels);
        _flipWheel.CenterText.Text = _toolsSideShown ? Loc.T("Strumenti", "Tools") : Loc.T("Formati", "Formats");
        _flipWheel.Highlight(-1);
    }

    private void SetFanOpen(bool open)
    {
        _fanOpen = open;
        if (_fanLayer == null || _flipWheel == null) return;
        _flipWheel.Highlight(open ? CropSector : -1);
        _flipWheel.CenterText.Text = open ? "9:16" : _toolsSideShown ? Loc.T("Strumenti", "Tools") : Loc.T("Formati", "Formats");
        for (int i = 0; i < _fanSectors.Length; i++)
            _fanSectors[i].Fill = open && i == 1 ? Accent : SectorRest;
        if (!Animate)
        {
            _fanLayer.BeginAnimation(OpacityProperty, null);
            _fanLayer.Opacity = open ? 1 : 0;
            return;
        }
        _fanLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, TimeSpan.FromMilliseconds(open ? 220 : 120)));
    }

    // ── Passo 3: QuickLook (Spazio) + editor Ritaglia & Oscura ──────────────

    private Border BuildQuickLookVisual()
    {
        var canvas = new Canvas { Width = 280, Height = 210 };

        // Pannello QuickLook a sinistra.
        var qlPanel = MockPanel(8, 8, 128, 194, Loc.T("Spazio", "Space"));
        canvas.Children.Add(qlPanel);
        var faceHost = new Grid { Width = 112, Height = 120 };
        Canvas.SetLeft(faceHost, 16);
        Canvas.SetTop(faceHost, 44);
        canvas.Children.Add(faceHost);

        _quickLookFaces[0] = BuildCubeFace();
        _quickLookFaces[1] = BuildPdfFace();
        _quickLookFaces[2] = BuildPaletteFace();
        foreach (var face in _quickLookFaces)
        {
            face.Opacity = 0;
            faceHost.Children.Add(face);
        }
        _quickLookCaption = new TextBlock { Width = 112, TextAlignment = TextAlignment.Center, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = TextSecondary };
        Canvas.SetLeft(_quickLookCaption, 16);
        Canvas.SetTop(_quickLookCaption, 176);
        canvas.Children.Add(_quickLookCaption);
        ShowQuickLookFace(0);

        // Editor di ritaglio a destra.
        canvas.Children.Add(MockPanel(144, 8, 128, 194, Loc.T("Ritaglia & Oscura", "Crop & Blur")));
        var photo = new Shapes.Rectangle
        {
            Width = 112,
            Height = 112,
            RadiusX = 3,
            RadiusY = 3,
            Fill = new LinearGradientBrush(Color.FromRgb(0x3B, 0x4A, 0x66), Color.FromRgb(0x8A, 0x5A, 0x3C), 45)
        };
        Canvas.SetLeft(photo, 152);
        Canvas.SetTop(photo, 44);
        canvas.Children.Add(photo);

        var privacy = new Shapes.Rectangle
        {
            Width = 30,
            Height = 14,
            Fill = Frozen(0x2A, 0x2E, 0x39),
            Stroke = Frozen(0xFF, 0xB0, 0x20),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 }
        };
        Canvas.SetLeft(privacy, 210);
        Canvas.SetTop(privacy, 118);
        canvas.Children.Add(privacy);

        var frame = new Shapes.Rectangle { Width = 63, Height = 112, Stroke = Accent, StrokeThickness = 1.6 };
        Canvas.SetLeft(frame, 176);
        Canvas.SetTop(frame, 44);
        canvas.Children.Add(frame);
        if (Animate)
        {
            // Il riquadro passa da 9:16 a 1:1 e torna indietro, come cambiando preset nell'editor.
            var span = TimeSpan.FromMilliseconds(1500);
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            frame.BeginAnimation(WidthProperty, new DoubleAnimation(63, 104, span) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            frame.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(176, 156, span) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            frame.BeginAnimation(HeightProperty, new DoubleAnimation(112, 104, span) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        }

        var keys = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (string key in new[] { "Ctrl+S", "Ctrl+C", "P" })
        {
            var cap = CreateKeyCap(key);
            cap.Margin = new Thickness(0, 0, 4, 0);
            keys.Children.Add(cap);
        }
        Canvas.SetLeft(keys, 152);
        Canvas.SetTop(keys, 172);
        canvas.Children.Add(keys);
        return IllustrationFrame(canvas);
    }

    private void ShowQuickLookFace(int index)
    {
        _quickLookIndex = index;
        string[] captions = [Loc.T("Modello 3D", "3D Model"), "PDF", "Palette · Aa"];
        if (_quickLookCaption != null) _quickLookCaption.Text = captions[index];
        for (int i = 0; i < _quickLookFaces.Length; i++)
        {
            var face = _quickLookFaces[i];
            if (face == null) continue;
            double target = i == index ? 1 : 0;
            if (Animate) face.BeginAnimation(OpacityProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(260)));
            else face.Opacity = target;
        }
    }

    private static Canvas MockPanel(double left, double top, double width, double height, string title)
    {
        var panel = new Canvas();
        var body = new Shapes.Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = 7,
            RadiusY = 7,
            Fill = BgDark,
            Stroke = CardBorder,
            StrokeThickness = 1
        };
        Canvas.SetLeft(body, left);
        Canvas.SetTop(body, top);
        panel.Children.Add(body);
        var caption = new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Accent };
        Canvas.SetLeft(caption, left + 10);
        Canvas.SetTop(caption, top + 9);
        panel.Children.Add(caption);
        return panel;
    }

    private static UIElement BuildCubeFace()
    {
        // Cubo isometrico in fil di ferro che ruota lentamente.
        var cube = new Shapes.Path
        {
            Data = Geometry.Parse("M 0,-34 L 30,-17 L 30,17 L 0,34 L -30,17 L -30,-17 Z M 0,-34 L 0,0 M 30,17 L 0,0 M -30,17 L 0,0"),
            Stroke = Accent,
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = AccentSoft
        };
        var spin = new RotateTransform(0);
        cube.RenderTransform = spin;
        var host = new Canvas { Width = 112, Height = 120 };
        Canvas.SetLeft(cube, 56);
        Canvas.SetTop(cube, 60);
        host.Children.Add(cube);
        if (Animate) spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(9)) { RepeatBehavior = RepeatBehavior.Forever });
        return host;
    }

    private static UIElement BuildPdfFace()
    {
        var page = new StackPanel();
        var paper = new Border
        {
            Width = 70,
            Height = 92,
            Background = Frozen(0xE5, 0xE7, 0xEB),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(8, 9, 8, 8),
            Child = page
        };
        page.Children.Add(new Border { Height = 5, Width = 34, HorizontalAlignment = HorizontalAlignment.Left, Background = Accent, Margin = new Thickness(0, 0, 0, 6) });
        foreach (double w in new[] { 54.0, 48, 54, 36, 54, 44 })
            page.Children.Add(new Border { Height = 3, Width = w, HorizontalAlignment = HorizontalAlignment.Left, Background = Frozen(0x9C, 0xA3, 0xAF), Margin = new Thickness(0, 0, 0, 5) });
        return new Grid { Width = 112, Height = 120, Children = { paper } };
    }

    private static UIElement BuildPaletteFace()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var c in new[] { (0xFF, 0x7A, 0x18), (0x1F, 0x2A, 0x44), (0x3B, 0x82, 0xF6), (0xE5, 0xE7, 0xEB), (0x10, 0xB9, 0x81) })
            row.Children.Add(new Border { Width = 18, Height = 30, Margin = new Thickness(1, 0, 1, 0), CornerRadius = new CornerRadius(2), Background = Frozen((byte)c.Item1, (byte)c.Item2, (byte)c.Item3) });
        stack.Children.Add(row);
        stack.Children.Add(new TextBlock
        {
            Text = "Aa",
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            Foreground = Accent,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        });
        return new Grid { Width = 112, Height = 120, Children = { stack } };
    }

    // ── Passo 4: scelte rapide finali ───────────────────────────────────────

    private Border BuildReadyVisual()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var enginesRow = new Grid();
        enginesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        enginesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var enginesText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        enginesText.Children.Add(new TextBlock { Text = Loc.T("Motori & GPU", "Engines & GPU"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary });
        enginesText.Children.Add(new TextBlock { Text = Loc.T("FFmpeg, Pandoc, Whisper e altri.", "FFmpeg, Pandoc, Whisper, and more."), FontSize = 11, Foreground = TextSecondary, Margin = new Thickness(0, 1, 0, 0) });
        enginesRow.Children.Add(enginesText);
        var openEngines = HubUi.CreateButton(Loc.T("Apri", "Open"), false, () => SettingsHubWindow.ShowSingleton(3, null, null, null));
        openEngines.VerticalAlignment = VerticalAlignment.Center;
        openEngines.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(openEngines, 1);
        enginesRow.Children.Add(openEngines);

        stack.Children.Add(OptionCard(enginesRow));
        var gazeCard = OptionCard(GazeToggle);
        gazeCard.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(gazeCard);
        stack.Children.Add(new TextBlock
        {
            Text = Loc.T("Riapri il tour da Impostazioni → 7. Guida & Scorciatoie.", "Replay this tour anytime in Settings → 7. Guide & Shortcuts."),
            FontSize = 10.5,
            Foreground = TextSecondary,
            Margin = new Thickness(2, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        return new Border { Child = stack, Padding = new Thickness(0) };

        static Border OptionCard(UIElement child) => new()
        {
            Background = IllustrationBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Child = child
        };
    }

    // ── Helper grafici ──────────────────────────────────────────────────────

    private static Border CreateKeyCap(string text) => new()
    {
        Background = Frozen(0x1E, 0x21, 0x2B),
        BorderBrush = Frozen(0x3A, 0x3F, 0x4E),
        BorderThickness = new Thickness(1, 1, 1, 2),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 1, 6, 2),
        IsHitTestVisible = false,
        Child = new TextBlock { Text = text, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary }
    };

    private static DoubleAnimationUsingKeyFrames KeyFrames(TimeSpan period, params (double Seconds, double Value)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = period, RepeatBehavior = RepeatBehavior.Forever };
        foreach (var (seconds, value) in frames)
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
        return animation;
    }

    private static Point PolarPoint(Point center, double radius, double degrees) => new(
        center.X + radius * Math.Cos((degrees - 90) * Math.PI / 180.0),
        center.Y + radius * Math.Sin((degrees - 90) * Math.PI / 180.0));

    private static Geometry CreateAnnularSector(Point center, double innerR, double outerR, double startDeg, double endDeg)
    {
        bool largeArc = endDeg - startDeg > 180.0;
        var fig = new PathFigure { StartPoint = PolarPoint(center, outerR, startDeg), IsClosed = true };
        fig.Segments.Add(new ArcSegment(PolarPoint(center, outerR, endDeg), new Size(outerR, outerR), 0, largeArc, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(PolarPoint(center, innerR, endDeg), true));
        fig.Segments.Add(new ArcSegment(PolarPoint(center, innerR, startDeg), new Size(innerR, innerR), 0, largeArc, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([fig]);
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Mini-ruota vettoriale riutilizzata dai passi 1 e 2: spicchi, etichette, testo centrale e hover.</summary>
    private sealed class MiniWheel
    {
        internal Canvas Root { get; } = new();
        internal TextBlock CenterText { get; }
        internal event Action<string>? SectorHovered;

        private readonly Shapes.Path[] _sectors;
        private readonly TextBlock[] _labels;
        private readonly Point _center;
        private readonly double _labelRadius;
        private string[] _texts;

        internal MiniWheel(Point center, double innerR, double outerR, string[] labels, double fontSize)
        {
            _center = center;
            _texts = labels;
            _labelRadius = (innerR + outerR) / 2;
            _sectors = new Shapes.Path[labels.Length];
            _labels = new TextBlock[labels.Length];
            CenterText = new TextBlock
            {
                Width = innerR * 2 - 6,
                TextAlignment = TextAlignment.Center,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = Accent,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center
            };
            double span = 360.0 / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var path = new Shapes.Path
                {
                    Data = CreateAnnularSector(center, innerR, outerR, i * span - span / 2 + 2.5, (i + 1) * span - span / 2 - 2.5),
                    Fill = SectorRest,
                    Stroke = SectorStroke,
                    StrokeThickness = 1.1,
                    Cursor = Cursors.Hand
                };
                path.MouseEnter += (_, _) =>
                {
                    Highlight(idx);
                    CenterText.Text = "→ " + _texts[idx].TrimEnd(' ', '›');
                    SectorHovered?.Invoke(_texts[idx]);
                };
                _sectors[i] = path;
                Root.Children.Add(path);

                var label = new TextBlock { FontSize = fontSize, FontWeight = FontWeights.Bold, Foreground = Brushes.White, IsHitTestVisible = false };
                _labels[i] = label;
                Root.Children.Add(label);
            }

            var hub = new Border
            {
                Width = innerR * 2 - 4,
                Height = innerR * 2 - 4,
                CornerRadius = new CornerRadius(innerR - 2),
                Background = BgDark,
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Child = new Grid { Children = { CenterText } }
            };
            Canvas.SetLeft(hub, center.X - hub.Width / 2);
            Canvas.SetTop(hub, center.Y - hub.Height / 2);
            Root.Children.Add(hub);
            SetLabels(labels);
        }

        internal void SetLabels(string[] labels)
        {
            _texts = labels;
            double span = 360.0 / _labels.Length;
            for (int i = 0; i < _labels.Length; i++)
            {
                var label = _labels[i];
                label.Text = i < labels.Length ? labels[i] : "";
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var at = PolarPoint(_center, _labelRadius, i * span);
                Canvas.SetLeft(label, at.X - label.DesiredSize.Width / 2);
                Canvas.SetTop(label, at.Y - label.DesiredSize.Height / 2);
            }
        }

        internal void Highlight(int index)
        {
            for (int i = 0; i < _sectors.Length; i++)
            {
                _sectors[i].Fill = i == index ? Accent : SectorRest;
                _sectors[i].Stroke = i == index ? AccentHover : SectorStroke;
            }
        }

        /// <summary>Accende lo spicchio <paramref name="index"/> tra <paramref name="onSeconds"/> e <paramref name="offSeconds"/> di ogni ciclo.</summary>
        internal void PlayDemoHighlight(int index, TimeSpan period, double onSeconds, double offSeconds)
        {
            var brush = new SolidColorBrush(SectorRest.Color);
            _sectors[index].Fill = brush;
            var colors = new ColorAnimationUsingKeyFrames { Duration = period, RepeatBehavior = RepeatBehavior.Forever };
            colors.KeyFrames.Add(new DiscreteColorKeyFrame(SectorRest.Color, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            colors.KeyFrames.Add(new LinearColorKeyFrame(SectorRest.Color, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(onSeconds))));
            colors.KeyFrames.Add(new LinearColorKeyFrame(Accent.Color, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(onSeconds + 0.15))));
            colors.KeyFrames.Add(new LinearColorKeyFrame(Accent.Color, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(offSeconds))));
            colors.KeyFrames.Add(new LinearColorKeyFrame(SectorRest.Color, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(offSeconds + 0.2))));
            brush.BeginAnimation(SolidColorBrush.ColorProperty, colors);
        }
    }
}
