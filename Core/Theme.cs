using System.Windows;
using System.Windows.Media;

namespace BrawlhallaOverlay;

/// <summary>
/// Source unique de la palette/typo/forme "DA Brawlhalla-like" (bleu-nuit/violet
/// + accent doré, motif "écusson" à coins asymétriques). Remplace les blocs de
/// brushes privés autrefois dupliqués dans ControlPanelWindow/DashboardWindow/
/// ParcoursWindow/OverlayControlBarWindow/MainWindow. La quasi-totalité de l'UI
/// est construite en C# (chaque fenêtre n'a que 11-13 lignes de XAML de chrome),
/// donc une classe statique sert la même fonction qu'un ResourceDictionary sans
/// exiger un FindResource() partout dans le code-behind.
/// </summary>
public static class Theme
{
    // Fond
    public static readonly Brush BgPanel = MakeBrush("#181722"); // fenêtres bordées
    public static readonly Brush BgCard = MakeBrush("#2A2735"); // cartes/sections
    public static readonly Brush BorderCard = MakeBrush("#3A3548"); // liseré de carte

    // Overlay (alpha bas déjà éprouvé en jeu, ne pas re-designer — légibilité prioritaire)
    public static readonly Brush OverlayPanelBg = MakeBrush("#401B1B24");
    public static readonly Brush OverlayPanelBorder = MakeBrush("#55E8C44A");

    // Texte
    public static readonly Brush TextPrimary = Brushes.White;
    public static readonly Brush TextSubtle = MakeBrush("#AAAAAA");

    // Accent de marque
    public static readonly Brush AccentGold = MakeBrush("#E8C44A");

    // États fonctionnels (consolidés depuis les valeurs déjà en usage, pas de nouvelle teinte)
    public static readonly Brush StateSuccess = MakeBrush("#55D98B");
    public static readonly Brush StateFail = MakeBrush("#E4574C");
    public static readonly Brush StateWarning = MakeBrush("#F0A030"); // hit non confirmé
    public static readonly Brush StateSlow = MakeBrush("#7FA6FF"); // timeout / trop lent

    // Typo d'accent (titres/labels courts uniquement, jamais le corps de texte)
    public static readonly FontFamily AccentFontFamily = new("Segoe UI Semibold");
    public static readonly FontFamily AccentFontFamilyHeavy = new("Segoe UI Black");

    // Motif "écusson" : coins hauts plus prononcés que les coins bas.
    // Implémenté avec les 4 valeurs indépendantes natives de CornerRadius,
    // pas de géométrie Path/Polygon custom.
    public static readonly CornerRadius CrestMain = new(16, 16, 4, 4); // panneaux/cartes principaux
    public static readonly CornerRadius CrestBadge = new(10, 10, 3, 3); // badges de mode/série

    private static Brush MakeBrush(string hex)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
