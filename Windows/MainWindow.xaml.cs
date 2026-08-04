using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BrawlhallaOverlay;

public partial class MainWindow : Window
{
    // --- Win32 interop pour rendre la fenêtre "click-through" (les clics passent
    // au jeu en dessous) et invisible au alt-tab. ---
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // Ctrl+Alt+U arrive via le hook clavier bas niveau pendant que le jeu (une autre
    // fenêtre) a le focus : Window.Activate() seul ne suffit pas toujours à passer
    // au premier plan à cause du "foreground lock" de Windows (une fenêtre qui n'est
    // pas déjà au premier plan ne peut normalement pas se le voler elle-même) —
    // SetForegroundWindow explicite est nécessaire pour forcer le passage devant.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // Le hook WH_KEYBOARD_LL rapporte parfois le code générique (0x11/0x12)
    // et parfois le code spécifique gauche/droite (0xA2-0xA5) selon le contexte
    // (observé en jeu plein écran : uniquement 0xA2/0xA4) — il faut couvrir les deux.
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_O = 0x4F;
    private const int VK_P = 0x50;
    private const int VK_R = 0x52;
    private const int VK_K = 0x4B;
    private const int VK_U = 0x55;
    private const int VK_I = 0x49;
    private const int VK_H = 0x48;
    private const int VK_M = 0x4D;

    // --- Raccourcis manette (chord "Start + bouton"), voir §5.3.2 du plan UX onboarding :
    // un joueur au pad ne devrait pas avoir à lâcher la manette pour changer de combo/mode.
    // GamepadHook.SyntheticCodeBase + le flag du bouton (voir GamepadHook.Buttons) donne le
    // même genre de "code touche" qu'un vrai VK, ce qui permet de réutiliser exactement le
    // même chemin OnGlobalKeyDown/Up que le clavier et les chords Ctrl+Alt+*.
    private const int GP_START = GamepadHook.SyntheticCodeBase + 0x0010;
    private const int GP_BACK = GamepadHook.SyntheticCodeBase + 0x0020;
    private const int GP_LB = GamepadHook.SyntheticCodeBase + 0x0100;
    private const int GP_RB = GamepadHook.SyntheticCodeBase + 0x0200;
    private const int GP_X = GamepadHook.SyntheticCodeBase + 0x4000;
    private const int GP_Y = GamepadHook.SyntheticCodeBase + 0x8000;
    private bool _padStartDown;

    private static bool IsCtrl(int vkCode) => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL;
    private static bool IsAlt(int vkCode) => vkCode is VK_MENU or VK_LMENU or VK_RMENU;

    private static readonly TimeSpan MergeWindow = TimeSpan.FromMilliseconds(500);
    // Fenêtre pendant laquelle des touches enfoncées quasi en même temps sont
    // regroupées dans un seul "coup joué" ("A + B") au lieu de deux. Reste courte
    // exprès : le cas "direction tenue depuis un moment puis attaque pressée plus
    // tard" est déjà géré par QueuePendingBind qui relit l'état *actuellement*
    // enfoncé (_pressedVks) à chaque nouvel appui, donc il n'a pas besoin d'une
    // fenêtre large pour ça. Une fenêtre large ici casse le mash (appuis répétés
    // rapprochés sur l'attaque) : chaque nouvel appui relance le timer, donc un
    // mash plus rapide que la fenêtre l'empêche indéfiniment de se déclencher et
    // plus aucun coup n'est jamais transmis au ComboRunner.
    private static readonly TimeSpan ComboWindow = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan HistoryClearDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HistoryClearStep = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan HistoryClearStagger = TimeSpan.FromMilliseconds(40);

    private readonly Dictionary<int, KeyBind> _bindsByVk = new();
    private readonly Dictionary<int, List<SolidColorBrush>> _brushesByVk = new();
    private readonly Dictionary<SolidColorBrush, double> _restOpacityByBrush = new();

    // Flèches du mode 2 : au lieu de varier l'opacité, on change carrément la
    // couleur (bleu au repos → rouge quand la touche est appuyée) pour que ce
    // soit lisible d'un coup d'œil.
    private static readonly Color ArrowRestColor = (Color)ColorConverter.ConvertFromString("#3498DB");
    private static readonly Color ArrowPressedColor = (Color)ColorConverter.ConvertFromString("#E74C3C");
    private readonly Dictionary<int, List<SolidColorBrush>> _colorSwapByVk = new();
    private readonly HashSet<int> _pressedVks = new();
    private StackPanel _historyPanel = null!;
    private IntPtr _hwnd;
    private DispatcherTimer? _historyClearTimer;
    private CancellationTokenSource? _historyClearCts;

    // Regroupement des touches pressées quasi simultanément (voir ComboWindow) avant
    // de les logguer comme une seule entrée combinée dans l'historique.
    private readonly List<KeyBind> _pendingBinds = new();
    private DispatcherTimer? _comboTimer;

    private bool _ctrlDown;
    private bool _altDown;

    // Fusion des appuis répétés (spam) sur la même action/combo en une seule ligne d'historique.
    private List<KeyBind>? _lastLoggedBinds;
    private TextBlock? _lastLoggedText;
    private Border? _lastLoggedEntry;
    private int _lastLoggedCount;
    private DateTime _lastLoggedTime;

    // --- Trois modes d'affichage (voir AppState.ActiveMode) : historique+cluster,
    // grandes flèches, tutoriel de combos. Ctrl+Alt+P cycle parmi les favoris. ---
    private Border _mode1Panel = null!;
    private Grid _mode2Layer = null!;
    private Border _mode3Panel = null!;
    private TextBlock _hintText = null!;
    private Point? _dragStart;
    private double _canvasWidth;
    private double _canvasHeight;

    // --- Mode 3 : Tutoriel de combos ---
    private ComboRunner? _comboRunner;
    private StackPanel _comboStepsPanel = null!;
    private TextBlock _comboNameText = null!;
    private TextBlock _comboDamageNoteText = null!;
    private TextBlock _dexRequirementText = null!;
    private TextBlock _firstFailExplainText = null!;
    private bool _hasExplainedFirstComboFail;
    private List<string> _lastFedActionNames = new();
    private TextBlock _comboStreakText = null!;
    private Image _legendPortraitImage = null!;

    // Portrait officiel (render "Roster Pose" de brawlhalla.com/legends/) affiché en haut à
    // gauche du panneau de combo (mode Tutoriel) quand Combo.Legend est renseigné — les fichiers
    // sont embarqués dans Assets/Legends/<clé sans espace>.png (Resource dans le .csproj, chargés
    // par pack URI comme les icônes d'action). Contrairement aux icônes d'action (game-icons.net,
    // CC BY 3.0), ce sont des illustrations officielles du jeu, pas des assets sous licence libre —
    // usage en lecture seule dans un outil 100% local et non redistribué, pas une republication.
    // Nom de fichier dérivé de LegendComboPresets.Legends (source unique de la liste des légends)
    // plutôt qu'une seconde liste à maintenir en double : seuls les légends de cette liste (ceux
    // qui ont au moins un combo Signature sourcé) ont un portrait.
    private static string LegendPortraitFileName(string legend) => legend.Replace(" ", "") + ".png";
    private static readonly Dictionary<string, BitmapImage> _legendPortraitCache = new();
    private ContentControl _historySlotMode1 = null!;
    private ContentControl _historySlotMode3 = null!;
    // Contenu d'une pastille : un StackPanel horizontal (icône image et/ou glyphe
    // texte par action requise) + un "?" qui le recouvre en cacher les étapes.
    private readonly Dictionary<int, FrameworkElement> _pillContentByIndex = new();
    private readonly Dictionary<int, TextBlock> _pillMaskByIndex = new();
    // Images dont la variante de couleur (noir=défaut, vert=réussie, rouge=échec)
    // doit suivre l'état de la pastille — voir ActionIconBaseNames/SetPillIconVariant.
    private readonly Dictionary<int, List<System.Windows.Shapes.Path>> _pillIconImagesByIndex = new();
    // Icônes Gauche/Droite d'une pastille (avec le nom de l'action tel qu'écrit dans
    // le combo) : seules celles-ci sont retournées quand ComboRunner détecte un combo
    // joué en miroir (voir ComboRunner.MirrorChanged) — Haut/Bas n'y figurent jamais.
    private readonly Dictionary<int, List<(string Action, System.Windows.Shapes.Path Shape)>> _directionIconsByIndex = new();
    private readonly Dictionary<int, ProgressBar> _pillBarsByIndex = new();
    private readonly Dictionary<int, TextBlock> _pillCountdownsByIndex = new();

    // Actions du mode Tutoriel ayant une icône dédiée : Taunt n'a pas d'utilité
    // réelle dans un combo donc pas d'icône (garde son emoji). Les 4 directions
    // partagent une seule icône de flèche tournée selon l'action — voir
    // ActionIconRotationDegrees. Icônes dessinées en Geometry vectorielle (pas
    // des PNG) issues de game-icons.net (CC BY 3.0 — attribution dans l'onglet
    // À propos du panneau de contrôle) : remplace un premier pack de cliparts
    // dépareillés (police d'épées + pictogramme de sport + insigne de grade,
    // sans cohérence de style entre eux), archivé dans
    // Assets/Icons/_archive_pack1/ — voir docs/audit_features.md pour l'historique
    // de cette révision. "Sword Slice" (Lorc) a été écarté au profit de "Saber
    // Slash" (Lorc) pour l'attaque légère : sa silhouette arrondie se lisait
    // comme une parade plutôt qu'une frappe, retour direct de l'utilisateur.
    private static readonly Dictionary<string, string> ActionIconBaseNames = new()
    {
        ["Saut"] = "saut",
        ["Att. légère"] = "attaque_legere",
        ["Att. forte"] = "attaque_forte",
        ["Esquive"] = "esquive",
        ["Lancer"] = "lancer",
        ["Gauche"] = "direction",
        ["Droite"] = "direction",
        ["Haut"] = "direction",
        ["Bas"] = "direction",
    };

    // Chemins Geometry (mini-langage WPF, syntaxe compatible avec le "d" SVG
    // d'origine) sur un viewBox natif 512x512 — Path.Stretch="Uniform" fait la
    // mise à l'échelle, pas besoin de convertir les coordonnées à la main.
    private static readonly Dictionary<string, string> IconGeometryByBaseName = new()
    {
        // "Jump Across" — Delapouite. https://game-icons.net/1x1/delapouite/jump-across.html
        ["saut"] = "M295.883 20.338c-14.656-.098-30.21 16.152-37.057 29.625-8.19 16.117-14.16 43.37-5.826 58.734l-13.63 6.483c-5.76-3.823-46.376-13.28-63.386-10.748-27.583 6.662-52.99 20.944-78.793 33.84l12.165 26.667c23.13-10.42 42.92-28.464 69.89-30.424 21.533-1.566 34.608 11.535 50.786 18.552-1.066 68.896-16.84 101.175-54.03 160.44-26.528 16.792-61.213 17.727-94.11 22.693l12.62 28.323c40.826-5.42 80.217-10.064 108.947-26.65 58.103-41.767 85.666-62.308 148.543-92.38 30.3 9.43 41.237 39.108 55.03 61.048l24.163-22.63c-12.5-27.36-44.15-61.68-79.193-84.066-22.694 7.043-44.088 17.01-64.133 30.01 6.64-24.67 6.65-44.777-1.678-69.448 18.79 6.873 36.892 10.287 54.28 10.137 27.537-20.4 42.684-46.306 62.66-70.066L384 84.564c-16.46 18.927-25.97 37.853-49.404 56.78-16.322-1.3-32.255-8.444-48.114-16.69l-2.732-7.615c15.41-6.64 30.163-24.084 35.334-38.8 6.553-18.647 1.573-50.056-17.004-56.804a18.37 18.37 0 0 0-6.197-1.098zM18 384v110h142V384H18zm334 0v110h142V384H352z",
        // "Saber Slash" — Lorc. https://game-icons.net/1x1/lorc/saber-slash.html
        ["attaque_legere"] = "M275.03 20c35.223 49.563 53.59 113.64 55.69 173.47C315.154 143 289.092 88.423 250.81 48.75c40.294 79.527 51.15 172.312 37.938 256.094-12.287-75.777-40.564-159.524-92.375-227.156 29.6 70.937 36.64 149.785 24.813 221.843-8.745-51.804-25.41-107.4-52.594-158.81 13.023 54.315 12.854 107.64 3.437 159.28l21.657 6.813 15 4.718-11.28 10.908c-10.68 10.332-19.868 21.905-27.345 34.343 93.614 35.486 232.952 64.53 298.032 41.376-41.02 56.466-210.332 13.822-309.313-18.687-1.514 3.775-2.918 7.594-4.124 11.467a152.536 152.536 0 0 0-6.062 29.657l176.47 66.375c98.5 31.095 150.5-24.62 158.655-81.72C505.253 254.472 485.016 105.66 426.06 20h-22.187c40.092 65.52 66.67 154.216 60.47 255.344-8.154-79.833-42.8-157.214-98.44-219.5 38.676 85.094 56.566 185.746 34.376 288.625.057-118.816-33.1-225.865-105.092-324.47H275.03zm-110.186 1.594c41.255 29.176 74.328 74.093 97.5 120.656-7.702-46.15-21.3-86.79-44-120.656h-53.5zm176.375 0c28.882 15.143 52.096 36.614 71.28 66.78-7.14-27.79-17.217-49.85-31.438-66.78H341.22zM123.686 304.406a179.344 179.344 0 0 1-4.062 64L18.812 336.344V366l91.938 29.094a178.602 178.602 0 0 1-30.313 48.28l50.094 15.75c-3.038-24.898-1.136-49.885 6.282-73.718 7.446-23.92 20.223-46.108 37.032-65.22l-50.156-15.78z",
        // "Sword Clash" — Lorc. https://game-icons.net/1x1/lorc/sword-clash.html
        ["attaque_forte"] = "m311.313 25.625-23 10.656-29.532 123.032 60.814-111.968-8.28-21.72zM59.625 50.03c11.448 76.937 48.43 141.423 100.188 195.75a3267.323 3267.323 0 0 0 42.718-29.405c-22.156-27.314-37.85-56.204-43.593-86.28-34.214-26.492-67.613-53.376-99.312-80.064zm390.47.032C419.178 76.1 386.64 102.33 353.31 128.22c-10.333 58.234-58.087 112.074-118.218 158.624-65.433 50.654-146.56 92.934-215.28 121.406l-.002 32.78c93.65-34.132 195.55-81.378 276.875-146.592 79.035-63.378 138.329-143.063 153.41-244.375zm-236.158 9.344-8.5 27.813 40.688 73.06-6.875-85.31-25.313-15.564zm114.688 87.813C223.39 227.47 112.257 302.862 19.812 355.905V388c65.917-27.914 142.58-68.51 203.844-115.938 49.83-38.574 88.822-81.513 104.97-124.843zm-144.563 2.155c7.35 18.89 19.03 37.68 34 56.063 7.03-4.98 14.056-10.03 21.094-15.094-18.444-13.456-36.863-27.12-55.094-40.97zM352.656 269.72c-9.573 9.472-19.58 18.588-29.906 27.405 54.914 37.294 117.228 69.156 171.906 92.156V358.19c-43.86-24.988-92.103-55.13-142-88.47zm-44.906 39.81c-11.65 9.32-23.696 18.253-36.03 26.845 70.326 45.135 149.33 79.775 222.935 106.375v-33.22c-58.858-24.223-127.1-58.727-186.906-100zm-58.625 52.033l-46.188 78.25 7.813 23.593 27.75-11.344 10.625-90.5zm15.844.812L316.343 467l36.47 10.28-3.533-31.967-84.31-82.938z",
        // "Dodging" — Lorc. https://game-icons.net/1x1/lorc/dodging.html
        ["esquive"] = "M396.082 17.326c-.166-.025-1.922.108-4.977.108-21.975 0-42.158 18.904-49.437 46.595l75.713 12.61-78.526 13.085c.564 16.248 5.55 30.99 13.062 42.367l54.39 9.603-41.277 7.29.484.607-15.91 2.47c-15.262 2.366-25.866 9.63-34.46 21.165-2.534 3.4-4.848 7.198-6.962 11.328l90.798 13.2-100.976 14.684a197.818 197.818 0 0 0-1.627 6.874c-1.662 7.613-2.953 15.622-3.982 23.854l115.275 14.107-117.81 14.418c-.525 9.083-.84 18.236-1.022 27.31l114.07 16.407-113.304 16.3h40.826l2.144 32.532 82.026 11.38-80.54 11.173 2.512 38.14 75.582 10.897-74.158 10.69 2.938 44.59h96.306l11.875-159.403h43.983c-.228-36.033-1.914-77.32-10.137-111.194-4.462-18.384-10.84-34.42-19.314-46.063-8.472-11.642-18.583-18.958-32.248-21.53l-15.59-2.933 10.124-12.213c10.435-12.587 17.49-30.688 17.49-51.127 0-37.056-22.084-66.04-47.127-69.295l-.106-.013-.108-.016zm-53.535 5.055L16.785 45.968l304.93 22.082c3.073-17.672 10.43-33.57 20.832-45.67zm-22.402 62.114L16.783 106.46l312.28 22.612c-5.686-12.618-8.96-27.047-8.96-42.422 0-.722.027-1.437.042-2.156zm-2.612 60.688L16.783 166.96l269.96 19.546c3.583-8.906 7.975-17.144 13.415-24.445 4.868-6.532 10.676-12.254 17.375-16.878zm-37.79 63.228-262.96 19.04L273.19 246.02c1.18-10.497 2.77-20.808 4.927-30.69.51-2.33 1.05-4.635 1.625-6.918zm-8.327 57.803L16.783 284.65l253.225 18.336c.18-12.057.585-24.438 1.408-36.773zm-1.562 60.605-253.07 18.325 297.22 21.52-1.072-16.267H269.86v-9.343c0-4.62-.01-9.38-.006-14.235zm45.294 57.22L16.783 405.64l301.227 21.81-2.862-43.413zm3.97 60.202L16.782 466.13l305.233 22.102-2.9-43.992z",
        // "Thrown Daggers" — Lorc. https://game-icons.net/1x1/lorc/thrown-daggers.html
        ["lancer"] = "M167 18.813c-20.39-.002-36.813 16.92-36.813 37.312 0 20.39 16.423 36.813 36.813 36.813 12.06 0 22.896-5.747 29.75-14.657l73.094 19.595L305.5 145.75l186.844-.094-161.75-93.5-53.906 23.25L204.344 56c-.07-20.335-16.996-37.19-37.344-37.188zm0 18.656c10.29 0 18.656 8.365 18.656 18.655 0 10.288-8.366 18.156-18.656 18.156s-18.125-7.867-18.125-18.155c0-10.29 7.835-18.658 18.125-18.656zM64.062 69.874c-3.547.035-7.133.54-10.718 1.5C30.4 77.523 16.79 101.088 22.937 124.03c4.89 18.253 20.803 30.59 38.657 31.782l22.78 84.907-27.56 63.874 109.03 188.625.125-217.876-54.876-40.844-22.97-85.72c15.04-9.912 22.795-28.642 17.876-47-5.187-19.357-22.783-32.096-41.938-31.905zm.25 19.22c10.707-.108 20.57 6.99 23.47 17.81 3.435 12.825-4.177 26.003-17 29.44-12.825 3.435-26.002-4.177-29.438-17-3.436-12.825 4.144-26.003 16.968-29.44a24.125 24.125 0 0 1 6-.81zm112.438 44.28c-12.127.323-24.084 5.554-32.625 15.47-16.078 18.662-13.976 46.827 4.688 62.905 14.85 12.794 35.712 14.094 51.718 4.688l69.032 59.5 13.688 70.843 203.594 98.033L359.75 257.969 288.844 255l-69.656-60.063c7.095-17.28 2.774-37.888-12.157-50.75-8.747-7.536-19.58-11.097-30.28-10.812zm.72 19.813a24.78 24.78 0 0 1 16.905 6.03c10.432 8.988 11.612 24.756 2.625 35.188-8.987 10.432-24.724 11.58-35.156 2.594-10.432-8.987-11.612-24.724-2.625-35.156 4.773-5.542 11.47-8.476 18.25-8.656z",
        // "Plain Arrow" — Delapouite. https://game-icons.net/1x1/delapouite/plain-arrow.html
        ["direction"] = "M130.81 21.785v245.95H43.84L256 489.382l212.158-221.644H381.19V21.786H130.81z",
    };

    // "Plain Arrow" pointe vers le bas par défaut (contrairement à l'ancienne
    // icône du pack archivé, qui pointait à droite) — rotations recalculées en
    // conséquence.
    private static readonly Dictionary<string, double> ActionIconRotationDegrees = new()
    {
        ["Bas"] = 0,
        ["Gauche"] = 90,
        ["Haut"] = 180,
        ["Droite"] = 270,
    };

    // Recoloration par simple changement de Fill (ces icônes sont des Geometry à
    // une seule couleur, pas des PNG) : "black" = état par défaut (à venir/courante),
    // recoloré en accent doré de la marque plutôt qu'en noir — un remplissage noir
    // plein était quasi invisible sur le panneau bleu-nuit translucide réel du mode
    // Tutoriel (bug de contraste relevé en même temps que ce changement de pack,
    // voir docs/audit_features.md). "green" = étape réussie, "red" = flash d'échec.
    private static readonly SolidColorBrush IconBrushDefault = new((Color)ColorConverter.ConvertFromString("#E8C44A"));
    private static readonly SolidColorBrush IconBrushSuccess = new((Color)ColorConverter.ConvertFromString("#55D98B"));
    private static readonly SolidColorBrush IconBrushFail = new((Color)ColorConverter.ConvertFromString("#E4574C"));

    private static Brush IconBrushForVariant(string variant) => variant switch
    {
        "green" => IconBrushSuccess,
        "red" => IconBrushFail,
        _ => IconBrushDefault,
    };

    /// <summary>Même Geometry/rotation que les pastilles du mode Tutoriel (ActionIconBaseNames/
    /// IconGeometryByBaseName/ActionIconRotationDegrees), réutilisée pour les modes 1 et 2 — un
    /// seul jeu d'icônes cohérent au lieu du glyphe Symbol (emoji) d'origine sur ces deux modes.
    /// Renvoie null si l'action n'a pas d'icône dédiée (Taunt) : l'appelant garde alors son ancien
    /// rendu texte/Symbol en repli.</summary>
    private static System.Windows.Shapes.Path? BuildActionIconShape(string action, double size, Brush fill)
    {
        if (!ActionIconBaseNames.TryGetValue(action, out var baseName) || !IconGeometryByBaseName.TryGetValue(baseName, out var geometryData))
            return null;

        var shape = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(geometryData),
            Fill = fill,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
        };
        if (ActionIconRotationDegrees.TryGetValue(action, out var rotation))
        {
            shape.RenderTransformOrigin = new Point(0.5, 0.5);
            shape.RenderTransform = new RotateTransform(rotation);
        }
        return shape;
    }

    /// <summary>Icône dédiée pour <paramref name="bind"/>, ou repli sur son glyphe <c>Symbol</c>
    /// (emoji d'origine) si l'action n'en a pas (Taunt) — factorise le repli identique répété dans
    /// les modes 1/2 (BuildBigArrow, BuildBigKeycap).</summary>
    private static UIElement BuildActionIconOrSymbolFallback(KeyBind bind, double iconSize, double fallbackFontSize, bool centerVertical = false)
    {
        var icon = BuildActionIconShape(bind.Action, iconSize, Brushes.White);
        if (icon is not null) return icon;

        var fallback = new TextBlock
        {
            Text = bind.Symbol,
            FontSize = fallbackFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (centerVertical) fallback.VerticalAlignment = VerticalAlignment.Center;
        return fallback;
    }

    private bool _quizRevealed;
    private DispatcherTimer? _quizRevealTimer;
    private DispatcherTimer? _chainComboTimer;
    private DispatcherTimer? _toleranceCountdownTimer;
    private DispatcherTimer? _comboCompletedResetTimer;
    private DispatcherTimer? _comboAbandonPollTimer;
    private static readonly TimeSpan ComboAbandonTimeout = TimeSpan.FromSeconds(3);

    // --- Enregistrement de combo (Ctrl+Alt+R) ---
    private readonly List<(List<KeyBind> Binds, DateTime Time)> _recordedMoves = new();

    // --- Badge de changement de mode (auto-fade) ---
    private TextBlock _modeBadge = null!;
    private DispatcherTimer? _modeBadgeTimer;
    private TextBlock _suspendedBadge = null!;

    // --- Icône dans la zone de notification (tray) ---
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    // --- Panneau de contrôle (fenêtre séparée, ouverte à la demande) ---
    private ControlPanelWindow? _controlPanel;

    // --- Barre de contrôle overlay (§5.3.1 du plan UX onboarding) ---
    private OverlayControlBarWindow? _controlBar;

    public MainWindow()
    {
        InitializeComponent();

        RebuildBindMaps();
        BuildLayout();

        AppState.LockChanged += OnLockChanged;
        AppState.ModeChanged += OnModeChanged;
        AppState.BindsChanged += OnBindsChanged;
        AppState.CombosChanged += OnCombosOrActiveComboChanged;
        AppState.ActiveComboChanged += _ => OnCombosOrActiveComboChanged();
        AppState.SettingsChanged += OnSettingsChanged;
        AppState.RecordingChanged += OnRecordingChanged;
        AppState.OverlayHiddenChanged += OnOverlayHiddenChanged;

        Loaded += MainWindow_Loaded;
        Closed += (_, _) =>
        {
            AppState.Hook.KeyDown -= OnGlobalKeyDown;
            AppState.Hook.KeyUp -= OnGlobalKeyUp;
            AppState.Hook.Dispose();
            AppState.Gamepad.ButtonDown -= OnGlobalKeyDown;
            AppState.Gamepad.ButtonUp -= OnGlobalKeyUp;
            AppState.Gamepad.Dispose();
            AppState.QuizRevealRequested -= RevealQuizStepsTemporarily;
            AppState.OverlayHiddenChanged -= OnOverlayHiddenChanged;
            if (_trayIcon is not null) _trayIcon.Visible = false;
            _trayIcon?.Dispose();
            _controlPanel?.Close();
            _controlBar?.Close();
        };
    }

    private void RebuildBindMaps()
    {
        _bindsByVk.Clear();
        foreach (var bind in AppState.Binds)
        {
            foreach (var vk in bind.VirtualKeyCodes)
            {
                _bindsByVk[vk] = bind;
            }
        }
    }

    private void BuildLayout()
    {
        _brushesByVk.Clear();
        _restOpacityByBrush.Clear();
        _colorSwapByVk.Clear();
        RootCanvas.Children.Clear();

        _hintText = new TextBlock
        {
            Text = "Ctrl+Alt+O : verrouiller/déverrouiller · Ctrl+Alt+P : changer de mode · Ctrl+Alt+K : changer de combo · Ctrl+Alt+R : enregistrer un combo · Ctrl+Alt+U : panneau de contrôle · Ctrl+Alt+I : révéler le combo (cacher les étapes) · Ctrl+Alt+H : suspendre/reprendre la capture · Ctrl+Alt+M : masquer/afficher l'overlay · glisser pour déplacer (mode 1)",
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
            Padding = new Thickness(6, 3, 6, 3),
            TextWrapping = TextWrapping.Wrap,
            Width = 280,
            Visibility = Visibility.Collapsed,
        };
        Canvas.SetLeft(_hintText, 10);
        Canvas.SetTop(_hintText, 10);
        RootCanvas.Children.Add(_hintText);

        _mode1Panel = BuildMode1Panel();
        Canvas.SetLeft(_mode1Panel, 24);
        Canvas.SetTop(_mode1Panel, 24);
        RootCanvas.Children.Add(_mode1Panel);

        _mode2Layer = BuildMode2Layer();
        _mode2Layer.Visibility = Visibility.Collapsed;
        Canvas.SetLeft(_mode2Layer, 0);
        Canvas.SetTop(_mode2Layer, 0);
        RootCanvas.Children.Add(_mode2Layer);

        _mode3Panel = BuildMode3Panel();
        _mode3Panel.Visibility = Visibility.Collapsed;
        Canvas.SetLeft(_mode3Panel, 24);
        Canvas.SetTop(_mode3Panel, 24);
        RootCanvas.Children.Add(_mode3Panel);

        _modeBadge = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00)),
            Padding = new Thickness(10, 5, 10, 5),
            Opacity = 0,
        };
        Canvas.SetTop(_modeBadge, 10);
        RootCanvas.Children.Add(_modeBadge);

        _suspendedBadge = new TextBlock
        {
            Text = "⏸ Capture suspendue (Ctrl+Alt+H pour reprendre)",
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x8A, 0x2B, 0x2B)),
            Padding = new Thickness(8, 4, 8, 4),
            Opacity = 0,
        };
        Canvas.SetTop(_suspendedBadge, 40);
        RootCanvas.Children.Add(_suspendedBadge);
        AppState.CaptureSuspendedChanged += OnCaptureSuspendedChanged;

        ApplyScale();
        SetActiveComboRunner();
        ApplyModeVisuals(AppState.ActiveMode, showBadge: false);
        ApplyLockVisuals(AppState.Locked);
    }

    private Border BuildMode1Panel()
    {
        var mainRow = new StackPanel { Orientation = Orientation.Horizontal };
        mainRow.Children.Add(BuildMovementCluster());
        mainRow.Children.Add(BuildActionButtons());
        mainRow.Children.Add(new Border
        {
            Width = 1,
            Margin = new Thickness(12, 2, 12, 2),
            // Séparateur en accent doré (signature de marque reprise de la tray icon,
            // voir Brawhl.md) plutôt qu'un simple liseré blanc générique.
            Background = new SolidColorBrush(Color.FromArgb(0x80, 0xE8, 0xC4, 0x4A)),
        });
        mainRow.Children.Add(BuildHistoryContainer());

        var panel = new Border
        {
            // Fond bleu-nuit teinté plutôt que noir neutre (cohérent avec le panneau
            // de contrôle, voir Brawhl.md section 4) — même niveau de transparence
            // qu'avant, seule la teinte change.
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0x1B, 0x1B, 0x24)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Child = mainRow,
        };

        panel.MouseLeftButtonDown += Mode1Panel_MouseLeftButtonDown;
        panel.MouseMove += Mode1Panel_MouseMove;
        panel.MouseLeftButtonUp += Mode1Panel_MouseLeftButtonUp;
        panel.SizeChanged += (_, _) =>
        {
            if (AppState.Settings.Position != OverlayPosition.Free) RepositionPanel(panel);
        };

        return panel;
    }

    private UIElement BuildMovementCluster()
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        foreach (var bind in AppState.Binds)
        {
            if (bind.Group != "Movement") continue;

            var (row, col) = bind.Slot switch
            {
                "Up" => (0, 1),
                "Left" => (1, 0),
                "Down" => (1, 1),
                "Right" => (1, 2),
                _ => (0, 0),
            };

            var keycap = BuildKeycap(bind, width: 42, height: 42, fontSize: 15);
            Grid.SetRow(keycap, row);
            Grid.SetColumn(keycap, col);
            grid.Children.Add(keycap);
        }

        return grid;
    }

    private UIElement BuildActionButtons()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

        foreach (var bind in AppState.Binds)
        {
            if (bind.Group != "Action") continue;
            panel.Children.Add(BuildKeycap(bind, width: 130, height: 30, fontSize: 12));
        }

        return panel;
    }

    private void RegisterBrush(KeyBind bind, SolidColorBrush brush)
    {
        _restOpacityByBrush[brush] = brush.Opacity;
        foreach (var vk in bind.VirtualKeyCodes)
        {
            if (!_brushesByVk.TryGetValue(vk, out var list))
            {
                list = new List<SolidColorBrush>();
                _brushesByVk[vk] = list;
            }
            list.Add(brush);
        }
    }

    private void RegisterColorSwapBrush(KeyBind bind, SolidColorBrush brush)
    {
        foreach (var vk in bind.VirtualKeyCodes)
        {
            if (!_colorSwapByVk.TryGetValue(vk, out var list))
            {
                list = new List<SolidColorBrush>();
                _colorSwapByVk[vk] = list;
            }
            list.Add(brush);
        }
    }

    private Border BuildKeycap(KeyBind bind, double width, double height, double fontSize)
    {
        var color = (Color)ColorConverter.ConvertFromString(bind.Color);
        var brush = new SolidColorBrush(color) { Opacity = 0.25 };
        RegisterBrush(bind, brush);

        // Le cluster ZQSD n'affiche que la lettre physique de la touche — sans
        // légende, rien n'indique que ce sont des directions (surtout pour qui
        // ne connaît pas la convention clavier AZERTY). On affiche donc la
        // même icône de direction (tournée) que le mode Tutoriel sous la
        // lettre pour ce groupe, à la place de l'ancien nom d'action en texte
        // — cohérent avec les autres modes maintenant que ces icônes
        // existent, plus tout de suite lisible qu'un mot. Tooltip conservé
        // sur tous les boutons pour la touche complète associée.
        var movementIcon = bind.Group == "Movement" ? BuildActionIconShape(bind.Action, fontSize * 0.85, Brushes.White) : null;
        UIElement content = bind.Group == "Movement"
            ? new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = bind.Label,
                        FontSize = fontSize,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    movementIcon is not null
                        ? (UIElement)movementIcon
                        : new TextBlock
                        {
                            Text = bind.Action,
                            FontSize = fontSize * 0.45,
                            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                },
            }
            : BuildActionKeycapContent(bind, fontSize);

        return new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(6),
            Width = width,
            Height = height,
            Margin = new Thickness(3),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            ToolTip = $"{bind.Action} ({string.Join(" / ", bind.Keys)})",
            Child = content,
        };
    }

    /// <summary>Contenu d'un bouton d'action du mode 1 (Saut/Att. légère/Att. forte/Esquive/
    /// Lancer) : icône dédiée (voir ActionIconBaseNames) suivie du nom de la touche physique
    /// (bind.Label, ex. "Espace"), au lieu du seul texte d'origine — même jeu d'icônes que le
    /// mode Tutoriel, pour une identité visuelle cohérente entre les modes. Repli en texte seul
    /// pour une action sans icône dédiée (Taunt).</summary>
    private UIElement BuildActionKeycapContent(KeyBind bind, double fontSize)
    {
        var icon = BuildActionIconShape(bind.Action, fontSize * 1.3, Brushes.White);
        if (icon is null)
        {
            return new TextBlock
            {
                Text = bind.Label,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        icon.Margin = new Thickness(0, 0, 6, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon);
        row.Children.Add(new TextBlock
        {
            Text = bind.Label,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private UIElement BuildHistoryContainer()
    {
        _historyPanel = new StackPanel { Orientation = Orientation.Vertical, Width = 200, VerticalAlignment = VerticalAlignment.Bottom };
        _historySlotMode1 = new ContentControl { Content = _historyPanel };
        return _historySlotMode1;
    }

    // --- Mode 3 : Tutoriel de combos, en gros bandeau centré en haut de l'écran
    // (façon "notation de combo" des jeux de baston) : contrairement au mode 1,
    // ce panneau ignore le réglage de position général et reste toujours ancré
    // en haut, pour rester lisible d'un coup d'œil pendant l'action. ---
    private Border BuildMode3Panel()
    {
        _comboNameText = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };

        _comboStreakText = new TextBlock
        {
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };

        // Affiche le rappel "true combo jusqu'à X%" saisi dans l'éditeur (Combo.DamageNote),
        // quand renseigné — sinon masqué, absence de note ≠ "marche à tout %".
        _comboDamageNoteText = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xC1, 0x4D)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };

        // Seuil de Dex requis + faisabilité pour le personnage entraîné (voir Combo.MinDex,
        // LegendStats) — demande explicite de l'utilisateur : un combo qui exige plus de Dex que ce
        // que le personnage peut atteindre (même avec stance) ne devrait même pas apparaître dans les
        // listes (filtré dans AppState.FilteredComboIndices), mais celui qui EST montré doit quand
        // même dire clairement le seuil, et si la marge est confortable ou tout juste suffisante.
        _dexRequirementText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };

        // Explication du tout premier échec de combo (§5.6 du plan UX onboarding) : un simple
        // flash rouge sans contexte oblige à deviner ce qui a raté. Affichée une seule fois par
        // session (voir _hasExplainedFirstComboFail) pour ne pas polluer l'affichage une fois le
        // mécanisme compris.
        _firstFailExplainText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xE7, 0x4C, 0x3C)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 360,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        _comboStepsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        // L'historique de coups n'est plus affiché en mode Tutoriel : les pastilles de
        // combo suffisent à suivre la progression, et l'historique en dessous ajoutait
        // du bruit visuel jugé gênant par l'utilisateur. _historySlotMode3 reste un champ
        // non nul mais n'est jamais ajouté à l'arbre visuel ni alimenté (voir ApplyModeVisuals).
        _historySlotMode3 = new ContentControl();

        var textContent = new StackPanel { Orientation = Orientation.Vertical };
        textContent.Children.Add(_comboNameText);
        textContent.Children.Add(_comboDamageNoteText);
        textContent.Children.Add(_dexRequirementText);
        textContent.Children.Add(_comboStepsPanel);
        textContent.Children.Add(_comboStreakText);
        textContent.Children.Add(_firstFailExplainText);

        // Portrait en colonne 0 (Auto, aligné en haut) : la colonne 1 (le reste du contenu,
        // toujours centré comme avant) occupe l'espace restant, ce qui place naturellement le
        // portrait en haut à gauche du panneau sans toucher au centrage existant.
        _legendPortraitImage = new Image
        {
            Width = 48,
            Height = 48,
            Stretch = Stretch.UniformToFill,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 12, 0),
            Visibility = Visibility.Collapsed,
            Clip = new RectangleGeometry(new Rect(0, 0, 48, 48), 8, 8),
        };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_legendPortraitImage, 0);
        Grid.SetColumn(textContent, 1);
        content.Children.Add(_legendPortraitImage);
        content.Children.Add(textContent);

        var panel = new Border
        {
            // Même teinte bleu-nuit que le mode 1 (cohérence de marque, Brawhl.md
            // section 4). Bordure basse plus épaisse en accent doré : évoque le motif
            // "nameplate" (écusson) récurrent de l'UI Brawlhalla sans reproduire une
            // forme complexe qui risquerait de casser le layout existant.
            Background = new SolidColorBrush(Color.FromArgb(0x77, 0x1B, 0x1B, 0x24)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24, 18, 24, 18),
            BorderThickness = new Thickness(1, 1, 1, 3),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xE8, 0xC4, 0x4A)),
            Child = content,
        };

        panel.SizeChanged += (_, _) =>
        {
            if (AppState.ActiveMode == 2) RepositionTopCenter(panel);
        };

        return panel;
    }

    /// <summary>Affiche le portrait du personnage sur lequel on s'entraîne (AppState.Settings.
    /// TrainingLegendFilter), indépendamment du combo actif — depuis le retrait des combos par
    /// légende (voir LegendComboPresets.cs), Combo.Legend n'est presque plus jamais renseigné, mais
    /// l'utilisateur veut quand même voir le portrait tant qu'un personnage est choisi, combo
    /// générique ou pas, voire sans combo du tout. Appelée depuis RenderComboSteps (les deux
    /// branches) et OnSettingsChanged (changer de personnage sans changer de combo actif ne lève
    /// sinon jamais ActiveComboChanged) — jamais depuis un endroit qui reconstruit tout le panneau,
    /// pour ne pas désynchroniser un combo en cours.</summary>
    private void UpdateLegendPortrait()
    {
        var trainingLegend = AppState.Settings.TrainingLegendFilter;
        if (string.IsNullOrEmpty(trainingLegend) || !LegendComboPresets.Legends.Contains(trainingLegend))
        {
            _legendPortraitImage.Visibility = Visibility.Collapsed;
            return;
        }

        var portraitFile = LegendPortraitFileName(trainingLegend);
        if (!_legendPortraitCache.TryGetValue(portraitFile, out var portrait))
        {
            try
            {
                portrait = new BitmapImage(new Uri($"pack://application:,,,/Assets/Legends/{portraitFile}", UriKind.Absolute));
                _legendPortraitCache[portraitFile] = portrait;
            }
            catch (IOException)
            {
                // Portrait pas encore récupéré pour ce personnage (roster étendu à 69 légendes,
                // seuls 35 ont un fichier pour l'instant) — masquer plutôt que planter.
                portrait = null;
            }
        }

        if (portrait is not null)
        {
            _legendPortraitImage.Source = portrait;
            _legendPortraitImage.Visibility = Visibility.Visible;
        }
        else
        {
            _legendPortraitImage.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Affiche le seuil de Dex requis par le combo actif (Combo.MinDex) et si le
    /// personnage entraîné peut l'atteindre — demande explicite de l'utilisateur : "faudrait même
    /// préciser vu que t'as les stats du perso sélectionné si il a les stats pour ou si il faut une
    /// stance". FilteredComboIndices exclut déjà les combos hors de portée même avec stance, mais un
    /// combo peut rester actif après un changement de personnage tant qu'on ne l'a pas changé —
    /// cette méthode reste donc honnête même dans ce cas plutôt que de supposer que "affiché" veut
    /// dire "jouable".</summary>
    private void UpdateDexRequirement(Combo combo)
    {
        if (combo.MinDex is not int minDex)
        {
            _dexRequirementText.Visibility = Visibility.Collapsed;
            return;
        }

        var legend = AppState.Settings.TrainingLegendFilter;
        if (string.IsNullOrEmpty(legend) || !LegendStats.Table.TryGetValue(legend, out var stats))
        {
            _dexRequirementText.Text = $"Dex requis : {minDex}+";
            _dexRequirementText.Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
            _dexRequirementText.Visibility = Visibility.Visible;
            return;
        }

        _dexRequirementText.Visibility = Visibility.Visible;
        if (minDex <= stats.Dex)
        {
            _dexRequirementText.Text = $"Dex requis : {minDex}+ — {legend} l'a déjà (Dex {stats.Dex})";
            _dexRequirementText.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
        }
        else if (minDex == stats.Dex + 1)
        {
            _dexRequirementText.Text = $"Dex requis : {minDex}+ — jouable avec une stance (+1 Dex, {legend} est à {stats.Dex} de base)";
            _dexRequirementText.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));
        }
        else
        {
            _dexRequirementText.Text = $"Dex requis : {minDex}+ — impossible sur {legend} même avec une stance (max {stats.Dex + 1})";
            _dexRequirementText.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
        }
    }

    private void RenderComboSteps()
    {
        _comboStepsPanel.Children.Clear();
        _pillContentByIndex.Clear();
        _pillMaskByIndex.Clear();
        _pillIconImagesByIndex.Clear();
        _directionIconsByIndex.Clear();
        _pillBarsByIndex.Clear();
        _pillCountdownsByIndex.Clear();
        _quizRevealed = false;
        _quizRevealTimer?.Stop();
        _toleranceCountdownTimer?.Stop();

        var combos = AppState.Combos;
        var activeIndex = AppState.ActiveComboIndex;

        if (activeIndex < 0 || combos.Count == 0)
        {
            // État vide explicite (§5.6 du plan UX onboarding) : "Aucun combo" tout court
            // n'indique aucune action à faire — pointer vers le panneau de contrôle (l'icône
            // dorée ≡ en bas à droite de l'overlay, ou Ctrl+Alt+U) est le chemin le plus direct
            // pour en importer une, avant même de penser à en enregistrer une soi-même.
            _comboNameText.Text = "Aucun combo sélectionné → ouvre le panneau de contrôle (icône ≡ en bas à droite, ou Ctrl+Alt+U) pour en importer un";
            _comboStreakText.Text = "";
            _comboDamageNoteText.Visibility = Visibility.Collapsed;
            _dexRequirementText.Visibility = Visibility.Collapsed;
            UpdateLegendPortrait();
            _firstFailExplainText.Visibility = Visibility.Collapsed;
            return;
        }

        var combo = combos[activeIndex];
        var (filteredPos, filteredCount) = AppState.ActiveComboFilteredPosition();
        var weaponTag = string.IsNullOrEmpty(combo.Weapon) ? "" : $"[{combo.Weapon}] ";
        _comboNameText.Text = $"{weaponTag}{combo.Name}  ({filteredPos + 1}/{filteredCount} · Ctrl+Alt+K pour changer)";
        _comboStreakText.Text = $"Série réussie : {_comboRunner?.Streak ?? 0}";

        UpdateLegendPortrait();

        if (string.IsNullOrEmpty(combo.DamageNote))
        {
            _comboDamageNoteText.Visibility = Visibility.Collapsed;
        }
        else
        {
            _comboDamageNoteText.Text = $"⚠ {combo.DamageNote}";
            _comboDamageNoteText.Visibility = Visibility.Visible;
        }

        UpdateDexRequirement(combo);

        for (int i = 0; i < combo.Steps.Count; i++)
        {
            if (i > 0)
            {
                _comboStepsPanel.Children.Add(new TextBlock
                {
                    Text = "→",
                    Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                    FontSize = 30,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0),
                });
            }

            var step = combo.Steps[i];

            // Une action avec icône dédiée (voir ActionIconBaseNames) affiche l'icône
            // vectorielle *seule*, sans carré/fond derrière : les silhouettes
            // game-icons.net sont déjà des badges pleins, un carré gris translucide
            // derrière les écraserait visuellement. Une action sans icône dédiée
            // (Taunt) garde son glyphe texte existant (Symbol du KeyBind). Quand une
            // étape combine plusieurs actions (ex. direction + attaque), chacune
            // reste un élément séparé avec un espacement net entre les deux, pas
            // fusionnées dans un même bloc.
            var iconShapes = new List<System.Windows.Shapes.Path>();
            var contentPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var actionCount = step.RequiredActions.Count;
            for (int a = 0; a < actionCount; a++)
            {
                var action = step.RequiredActions[a];
                var gap = a < actionCount - 1 ? 10.0 : 0.0;

                if (ActionIconBaseNames.TryGetValue(action, out var baseName) && IconGeometryByBaseName.TryGetValue(baseName, out var geometryData))
                {
                    const double size = 58;
                    var shape = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse(geometryData),
                        Fill = IconBrushDefault,
                        Stretch = Stretch.Uniform,
                        Width = size,
                        Height = size,
                        Margin = new Thickness(0, 0, gap, 0),
                    };
                    if (ActionIconRotationDegrees.TryGetValue(action, out var rotation))
                    {
                        shape.RenderTransformOrigin = new Point(0.5, 0.5);
                        shape.RenderTransform = new RotateTransform(rotation);
                    }
                    if (action is "Gauche" or "Droite")
                    {
                        if (!_directionIconsByIndex.TryGetValue(i, out var directionIcons))
                        {
                            directionIcons = new List<(string, System.Windows.Shapes.Path)>();
                            _directionIconsByIndex[i] = directionIcons;
                        }
                        directionIcons.Add((action, shape));
                    }
                    iconShapes.Add(shape);
                    contentPanel.Children.Add(shape);
                }
                else
                {
                    var bind = AppState.Binds.FirstOrDefault(b => b.Action == action);
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = bind?.Symbol ?? "?",
                        FontSize = 34,
                        Foreground = Brushes.White,
                        Width = 58,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, gap, 0),
                    });
                }
            }
            _pillContentByIndex[i] = contentPanel;
            _pillIconImagesByIndex[i] = iconShapes;

            var mask = new TextBlock
            {
                Text = "?",
                FontSize = 34,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            _pillMaskByIndex[i] = mask;

            // Pas de carré/fond englobant : juste un conteneur transparent (pour le
            // masque quiz et l'opacité à venir/courante/flash) autour des icônes/glyphes.
            var pill = new Grid
            {
                MinWidth = 72,
                Opacity = i == 0 ? 1.0 : 0.4,
                Tag = i,
            };
            pill.Children.Add(contentPanel);
            pill.Children.Add(mask);

            // Barre fine sous la pastille : visible seulement pour l'étape courante
            // quand elle a une fenêtre de tolérance (pas la 1ère étape), se vide au
            // fil du temps restant avant que l'input soit jugé "trop lent".
            var bar = new ProgressBar
            {
                Width = 72,
                Height = 4,
                Margin = new Thickness(0, 4, 0, 0),
                Minimum = 0,
                Visibility = Visibility.Collapsed,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xD0, 0x3F)),
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0),
            };
            _pillBarsByIndex[i] = bar;

            var countdown = new TextBlock
            {
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
                Visibility = Visibility.Collapsed,
            };
            _pillCountdownsByIndex[i] = countdown;

            var stepColumn = new StackPanel { Orientation = Orientation.Vertical };
            stepColumn.Children.Add(pill);
            stepColumn.Children.Add(bar);
            stepColumn.Children.Add(countdown);
            _comboStepsPanel.Children.Add(stepColumn);
        }

        ApplyMirrorDisplay(_comboRunner?.IsMirrored ?? false);
        UpdateComboStepVisuals();
    }

    /// <summary>Tourne les icônes Gauche/Droite du panneau de combo pour refléter
    /// l'orientation détectée par ComboRunner (voir ComboRunner.MirrorChanged) : en
    /// miroir, chaque pastille Gauche/Droite affiche la flèche opposée à celle écrite
    /// dans Combo.Steps, pour montrer visuellement ce qui est réellement attendu
    /// (pas ce que Combo.Steps décrit dans son sens d'origine).</summary>
    private void ApplyMirrorDisplay(bool mirrored)
    {
        foreach (var icons in _directionIconsByIndex.Values)
        {
            foreach (var (action, shape) in icons)
            {
                var displayedAction = mirrored ? OppositeDirection(action) : action;
                if (ActionIconRotationDegrees.TryGetValue(displayedAction, out var rotation)
                    && shape.RenderTransform is RotateTransform rt)
                {
                    rt.Angle = rotation;
                }
            }
        }
    }

    private static string OppositeDirection(string action) => action switch
    {
        "Gauche" => "Droite",
        "Droite" => "Gauche",
        _ => action,
    };

    private void OnComboMirrorChanged(bool mirrored)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyMirrorDisplay(mirrored);
            if (mirrored) ShowModeBadge("Direction inversée détectée — combo joué en miroir");
        });
    }

    private Grid? PillAt(int index)
    {
        foreach (var child in _comboStepsPanel.Children)
        {
            if (child is not StackPanel column) continue;
            if (column.Children.Count > 0 && column.Children[0] is Grid g && g.Tag is int i && i == index) return g;
        }
        return null;
    }

    private void ApplyQuizMask()
    {
        if (_comboRunner is null) return;

        foreach (var (index, content) in _pillContentByIndex)
        {
            var hidden = AppState.Settings.QuizMode && !_quizRevealed && index >= _comboRunner.CurrentStepIndex;
            content.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            if (_pillMaskByIndex.TryGetValue(index, out var mask)) mask.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Bascule la variante de couleur (voir IconBrushForVariant) des icônes d'une
    // pastille : "black" = état par défaut (à venir/courante, accent doré — pas de
    // variante jaune), "green" = étape déjà réussie, "red" = flash d'échec (voir
    // FlashAllStepsRed). Un seul changement de Fill par forme, plus de fichier à
    // charger (ces icônes sont des Geometry, pas des PNG).
    private void SetPillIconVariant(int index, string variant)
    {
        if (!_pillIconImagesByIndex.TryGetValue(index, out var shapes)) return;
        var brush = IconBrushForVariant(variant);
        foreach (var shape in shapes)
        {
            shape.Fill = brush;
        }
    }

    private void SetAllPillIconVariant(string variant)
    {
        foreach (var index in _pillIconImagesByIndex.Keys) SetPillIconVariant(index, variant);
    }

    private void RevealQuizStepsTemporarily()
    {
        if (!AppState.Settings.QuizMode || _comboRunner is null) return;

        _quizRevealed = true;
        ApplyQuizMask();
        ShowModeBadge("Combo révélée (3s)");

        _quizRevealTimer?.Stop();
        _quizRevealTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _quizRevealTimer.Tick += (_, _) =>
        {
            _quizRevealTimer!.Stop();
            _quizRevealed = false;
            ApplyQuizMask();
        };
        _quizRevealTimer.Start();
    }

    private void UpdateComboStepVisuals()
    {
        if (_comboRunner is null) return;

        foreach (var child in _comboStepsPanel.Children)
        {
            if (child is not StackPanel column) continue;
            if (column.Children.Count == 0 || column.Children[0] is not Grid pill || pill.Tag is not int index) continue;

            if (index < _comboRunner.CurrentStepIndex)
            {
                pill.Opacity = 1.0;
                SetPillIconVariant(index, "green"); // étape déjà réussie
            }
            else if (index == _comboRunner.CurrentStepIndex)
            {
                pill.Opacity = 1.0;
                SetPillIconVariant(index, "black"); // pas de variante jaune pour les icônes
            }
            else
            {
                pill.Opacity = 0.4; // à venir
                SetPillIconVariant(index, "black");
            }
        }

        ApplyQuizMask();
    }

    private void HideAllToleranceBars()
    {
        _toleranceCountdownTimer?.Stop();
        foreach (var bar in _pillBarsByIndex.Values)
        {
            bar.BeginAnimation(RangeBase.ValueProperty, null);
            bar.Visibility = Visibility.Collapsed;
        }
        foreach (var countdown in _pillCountdownsByIndex.Values)
        {
            countdown.Visibility = Visibility.Collapsed;
        }
    }

    // Démarre le compte à rebours visuel (barre + texte en secondes) de la
    // fenêtre de tolérance pour l'étape qui vient de devenir courante (pas
    // d'effet sur la 1ère étape, qui n'a pas de délai à respecter — même
    // règle que ComboRunner.Feed). Le texte lit la valeur *animée* de la
    // barre à intervalle régulier plutôt que de dupliquer le calcul de temps
    // écoulé, pour rester strictement synchronisé avec ce que l'œil voit.
    private void StartToleranceBar(int newCurrentIndex)
    {
        HideAllToleranceBars();
        if (_comboRunner is null || newCurrentIndex <= 0 || newCurrentIndex >= _comboRunner.Combo.Steps.Count) return;
        if (!_pillBarsByIndex.TryGetValue(newCurrentIndex, out var bar)) return;
        if (!_pillCountdownsByIndex.TryGetValue(newCurrentIndex, out var countdown)) return;

        var step = _comboRunner.Combo.Steps[newCurrentIndex];
        var maxDelay = step.MaxDelayMs ?? _comboRunner.Combo.DefaultToleranceMs;
        if (maxDelay <= 0) return;

        bar.Maximum = maxDelay;
        bar.Value = maxDelay;
        bar.Visibility = Visibility.Visible;
        bar.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(maxDelay, 0, TimeSpan.FromMilliseconds(maxDelay)));

        countdown.Visibility = Visibility.Visible;
        countdown.Text = $"{maxDelay / 1000.0:0.0}s";

        _toleranceCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _toleranceCountdownTimer.Tick += (_, _) =>
        {
            var remaining = Math.Max(0, bar.Value);
            countdown.Text = $"{remaining / 1000.0:0.0}s";
            if (remaining <= 0) _toleranceCountdownTimer!.Stop();
        };
        _toleranceCountdownTimer.Start();
    }

    private void FlashComboStepSuccess(int index)
    {
        var pill = PillAt(index);
        if (pill is null) return;

        SetPillIconVariant(index, "green");
        var flash = new DoubleAnimation(1.0, 0.4, TimeSpan.FromMilliseconds(120)) { AutoReverse = true };
        pill.BeginAnimation(OpacityProperty, flash);
    }

    private static readonly TimeSpan FailBlinkStep = TimeSpan.FromMilliseconds(110);
    private const int FailBlinkCount = 3;

    // Sur un échec : toute la rangée clignote puis se réinitialise. Couleur
    // différente selon la cause (mauvaise touche = rouge, trop lent = orange)
    // pour un diagnostic immédiat sans devoir recouper avec l'historique.
    private void FlashAllStepsRed(ComboFailReason reason)
    {
        HideAllToleranceBars();
        SetAllPillIconVariant("red"); // pas de variante orange dédiée : Timeout n'est de toute façon jamais levé (voir ComboFailReason)

        foreach (var child in _comboStepsPanel.Children)
        {
            if (child is not StackPanel column || column.Children.Count == 0 || column.Children[0] is not Grid pill) continue;

            pill.Opacity = 1.0;

            var blink = new DoubleAnimation(1.0, 0.15, FailBlinkStep)
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(FailBlinkCount),
            };
            pill.BeginAnimation(OpacityProperty, blink);
        }

        var resetDelay = TimeSpan.FromMilliseconds(FailBlinkStep.TotalMilliseconds * 2 * FailBlinkCount + 40);
        var resetTimer = new DispatcherTimer { Interval = resetDelay };
        resetTimer.Tick += (_, _) =>
        {
            resetTimer.Stop();
            UpdateComboStepVisuals();
        };
        resetTimer.Start();
    }

    private void SetActiveComboRunner()
    {
        _chainComboTimer?.Stop();

        if (_comboRunner is not null)
        {
            _comboRunner.StepSucceeded -= OnComboStepSucceeded;
            _comboRunner.StepFailed -= OnComboStepFailed;
            _comboRunner.ComboCompleted -= OnComboCompleted;
            _comboRunner.ComboReset -= OnComboReset;
            _comboRunner.ComboAbandoned -= OnComboAbandoned;
            _comboRunner.MirrorChanged -= OnComboMirrorChanged;
        }

        var index = AppState.ActiveComboIndex;
        var combos = AppState.Combos;
        _comboRunner = index >= 0 && index < combos.Count
            ? new ComboRunner(combos[index], AppState.Settings.KeepStreakOnFail)
            : null;

        if (_comboRunner is not null)
        {
            _comboRunner.StepSucceeded += OnComboStepSucceeded;
            _comboRunner.StepFailed += OnComboStepFailed;
            _comboRunner.ComboCompleted += OnComboCompleted;
            _comboRunner.ComboReset += OnComboReset;
            _comboRunner.ComboAbandoned += OnComboAbandoned;
            _comboRunner.MirrorChanged += OnComboMirrorChanged;
        }

        RenderComboSteps();
    }

    private void OnCombosOrActiveComboChanged()
    {
        Dispatcher.Invoke(SetActiveComboRunner);
    }

    private void OnComboStepSucceeded(int index)
    {
        Dispatcher.Invoke(() =>
        {
            if (AppState.Settings.SoundEnabled) SystemSounds.Asterisk.Play();

            var actions = _comboRunner?.Combo.Steps[index].RequiredActions ?? new List<string>();
            AppState.RecordStepResult(actions, success: true);

            FlashComboStepSuccess(index);
            UpdateComboStepVisuals();
            StartToleranceBar(index + 1);
        });
    }

    private void OnComboStepFailed(int index, ComboFailReason reason)
    {
        Dispatcher.Invoke(() =>
        {
            if (AppState.Settings.SoundEnabled) SystemSounds.Hand.Play();

            var actions = _comboRunner?.Combo.Steps[index].RequiredActions ?? new List<string>();
            AppState.RecordStepResult(actions, success: false);

            ExplainFirstComboFailIfNeeded(actions);
            FlashAllStepsRed(reason);
        });
    }

    /// <summary>Un simple flash rouge ne dit pas ce qui a raté — la 1ère fois qu'un combo casse
    /// dans la session, affiche une ligne explicite (§5.6 du plan UX onboarding) plutôt que de
    /// laisser deviner. Une seule fois : une fois le mécanisme compris, répéter le message à
    /// chaque échec ajouterait juste du bruit.</summary>
    private void ExplainFirstComboFailIfNeeded(List<string> expectedActions)
    {
        if (_hasExplainedFirstComboFail) return;
        _hasExplainedFirstComboFail = true;

        var expected = expectedActions.Count > 0 ? string.Join(" + ", expectedActions) : "?";
        var actual = _lastFedActionNames.Count > 0 ? string.Join(" + ", _lastFedActionNames) : "(rien)";

        _firstFailExplainText.Text = $"Mauvaise touche : tu as fait « {actual} », l'étape demandait « {expected} ».";
        _firstFailExplainText.Visibility = Visibility.Visible;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _firstFailExplainText.Visibility = Visibility.Collapsed;
        };
        timer.Start();
    }

    private void OnComboCompleted()
    {
        Dispatcher.Invoke(() =>
        {
            if (AppState.Settings.SoundEnabled) SystemSounds.Exclamation.Play();

            HideAllToleranceBars();
            var streak = _comboRunner?.Streak ?? 0;
            _comboStreakText.Text = $"Série réussie : {streak}";
            UpdateComboStepVisuals();

            // Reste tout vert 1s après une réussite (ComboRunner.Feed remet déjà
            // CurrentStepIndex à 0 en interne juste après avoir levé cet event, donc
            // rien ne raffiche l'état "en attente" tant qu'aucun nouvel input n'arrive) :
            // si on rejoue la 1ère étape entre-temps, StepSucceeded rafraîchit l'affichage
            // immédiatement de toute façon, ce timer sert juste de filet en cas d'inaction.
            _comboCompletedResetTimer?.Stop();
            _comboCompletedResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _comboCompletedResetTimer.Tick += (_, _) =>
            {
                _comboCompletedResetTimer!.Stop();
                UpdateComboStepVisuals();
            };
            _comboCompletedResetTimer.Start();

            // Historique de performance persisté par combo (survit au redémarrage,
            // contrairement à Streak qui vit dans le ComboRunner en mémoire) :
            // sauvegarde "silencieuse" pour ne pas déclencher CombosChanged (qui
            // reconstruirait le ComboRunner actif et perdrait la série en cours).
            var combo = _comboRunner?.Combo;
            if (combo is not null)
            {
                combo.TotalCompletions++;
                combo.TotalAttempts++;
                if (streak > combo.BestStreak) combo.BestStreak = streak;

                var threshold = Math.Max(1, AppState.Settings.ChainStreakThreshold);
                var justMastered = !combo.Mastered && streak >= threshold;
                if (justMastered)
                {
                    combo.Mastered = true;
                    ShowModeBadge($"Combo maîtrisé : {combo.Name} !");
                }
                AppState.SaveCombosQuiet();

                // Enchaînement façon "session guidée" : ne passe au combo suivant
                // de la liste qu'une fois le seuil de réussites consécutives atteint
                // (pas juste après la 1ère réussite), pour forcer une vraie répétition
                // avant de progresser — cf. section "progression multi-combos" de
                // docs/audit_features.md.
                if (AppState.Settings.ChainCombos && AppState.Combos.Count > 1 && streak >= threshold)
                {
                    _chainComboTimer?.Stop();
                    _chainComboTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                    _chainComboTimer.Tick += (_, _) =>
                    {
                        _chainComboTimer!.Stop();
                        AppState.CycleCombo();
                    };
                    _chainComboTimer.Start();
                }
            }
        });
    }

    // Ne remet pas l'affichage à zéro tout de suite : FlashAllStepsRed (déclenché
    // juste avant par StepFailed) s'en charge une fois son clignotement terminé,
    // sinon le reset immédiat couperait l'animation rouge.
    private void OnComboReset()
    {
        Dispatcher.Invoke(() =>
        {
            _comboStreakText.Text = $"Série réussie : {_comboRunner?.Streak ?? 0}";

            var combo = _comboRunner?.Combo;
            if (combo is not null)
            {
                combo.TotalAttempts++;
                AppState.SaveCombosQuiet();
            }
        });
    }

    // Contrairement à OnComboReset (déclenché par une mauvaise touche, dont l'affichage
    // est remis à zéro par FlashAllStepsRed une fois son clignotement terminé — voir le
    // commentaire au-dessus d'OnComboReset), un abandon par inactivité n'a aucune
    // animation de faute à attendre : on remet l'affichage à l'état d'attente ici, tout
    // de suite, sans flash rouge ni son (ce n'est pas une faute de frappe, juste un
    // "il a arrêté").
    private void OnComboAbandoned()
    {
        Dispatcher.Invoke(() =>
        {
            HideAllToleranceBars();
            UpdateComboStepVisuals();
            _comboStreakText.Text = $"Série réussie : {_comboRunner?.Streak ?? 0}";

            var combo = _comboRunner?.Combo;
            if (combo is not null)
            {
                combo.TotalAttempts++;
                AppState.SaveCombosQuiet();
            }
        });
    }

    // --- Mode 2 : grosses flèches collées aux bords de l'écran (gauche/droite/
    // haut/bas) + attaques légère/forte bien visibles au centre en haut. Pensé
    // pour être lu d'un coup d'œil, sans avoir à lire du texte. ---
    private Grid BuildMode2Layer()
    {
        var grid = new Grid();

        var left = AppState.Binds.First(b => b.Group == "Movement" && b.Slot == "Left");
        var right = AppState.Binds.First(b => b.Group == "Movement" && b.Slot == "Right");
        var up = AppState.Binds.First(b => b.Group == "Movement" && b.Slot == "Up");
        var down = AppState.Binds.First(b => b.Group == "Movement" && b.Slot == "Down");

        grid.Children.Add(BuildBigArrow(left, HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(180, 0, 0, 0)));
        grid.Children.Add(BuildBigArrow(right, HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0, 0, 180, 0)));
        grid.Children.Add(BuildBigArrow(up, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, 60, 0, 0), fontSize: 110));
        grid.Children.Add(BuildBigArrow(down, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, 160)));

        var attackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 190, 0, 0),
        };
        var light = AppState.Binds.First(b => b.Action == "Att. légère");
        var heavy = AppState.Binds.First(b => b.Action == "Att. forte");
        attackPanel.Children.Add(BuildBigKeycap(light, 170, 170, 58));
        attackPanel.Children.Add(BuildBigKeycap(heavy, 170, 170, 58));
        grid.Children.Add(attackPanel);

        var extrasPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 380, 0, 0),
        };
        // Toutes les actions du groupe "Action" sauf les deux déjà affichées en
        // gros au centre (attaques légère/forte) — pas de liste de noms en dur,
        // pour ne pas casser silencieusement si l'utilisateur renomme/ajoute une
        // action dans l'onglet Touches.
        foreach (var bind in AppState.Binds.Where(b => b.Group == "Action" && b != light && b != heavy))
        {
            extrasPanel.Children.Add(BuildBigKeycap(bind, 70, 70, 30, showLabel: false));
        }
        grid.Children.Add(extrasPanel);

        return grid;
    }

    private FrameworkElement BuildBigArrow(KeyBind bind, HorizontalAlignment h, VerticalAlignment v, Thickness margin, double fontSize = 150)
    {
        var badgeBrush = new SolidColorBrush(ArrowRestColor) { Opacity = 0.75 };
        RegisterColorSwapBrush(bind, badgeBrush);

        double size = fontSize * 1.55;

        // Même icône de direction (tournée selon l'action) que le mode Tutoriel, à la place de
        // l'ancien glyphe Symbol — les 4 badges sont toujours des directions (Gauche/Droite/Haut/
        // Bas), donc BuildActionIconShape trouve toujours une icône ici (jamais de repli texte).
        UIElement child = BuildActionIconOrSymbolFallback(bind, fontSize * 0.75, fontSize, centerVertical: true);

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = badgeBrush,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(3),
            HorizontalAlignment = h,
            VerticalAlignment = v,
            Margin = margin,
            Child = child,
        };
    }

    private Border BuildBigKeycap(KeyBind bind, double width, double height, double fontSize, bool showLabel = true)
    {
        var color = (Color)ColorConverter.ConvertFromString(bind.Color);
        var brush = new SolidColorBrush(color) { Opacity = 0.25 };
        RegisterBrush(bind, brush);

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Icône dédiée (mêmes formes que le mode Tutoriel) à la place du glyphe Symbol d'origine ;
        // repli en Symbol pour Taunt, qui n'a pas d'icône (voir ActionIconBaseNames).
        content.Children.Add(BuildActionIconOrSymbolFallback(bind, fontSize * 1.05, fontSize));
        if (showLabel)
        {
            content.Children.Add(new TextBlock
            {
                Text = bind.Action,
                FontSize = fontSize * 0.28,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        return new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(10),
            Width = width,
            Height = height,
            Margin = new Thickness(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(2),
            Child = content,
        };
    }

    /// <summary>Zone de travail de l'écran ciblé par Settings.MonitorIndex (-1 = écran
    /// principal, comportement historique). SystemParameters.WorkArea seul renvoie
    /// toujours l'écran principal Windows, ce qui laissait l'overlay mal placé sur un
    /// setup multi-écran où le jeu tourne sur un moniteur secondaire — voir
    /// docs/audit_features.md §1.4.</summary>
    private static Rect GetTargetWorkArea()
    {
        var index = AppState.Settings.MonitorIndex;
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (index < 0 || index >= screens.Length) return SystemParameters.WorkArea;

        var primary = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var target = screens[index].WorkingArea;

        // WPF exprime Left/Top/Width/Height en unités indépendantes de la résolution
        // (96 DPI de base), alors que Screen.WorkingArea est en pixels physiques. L'app
        // ne déclare pas de per-monitor DPI awareness, donc on suppose ici la même
        // échelle que l'écran principal (ratio SystemParameters.WorkArea / écran
        // principal en pixels) — correct sur l'immense majorité des setups multi-écrans
        // (même mise à l'échelle partout), seulement approximatif si les écrans ont des
        // échelles DPI différentes.
        var scaleX = SystemParameters.WorkArea.Width / primary.Width;
        var scaleY = SystemParameters.WorkArea.Height / primary.Height;

        return new Rect(target.Left * scaleX, target.Top * scaleY, target.Width * scaleX, target.Height * scaleY);
    }

    private void ApplyWorkArea()
    {
        _lastAppliedMonitorIndex = AppState.Settings.MonitorIndex;
        var workArea = GetTargetWorkArea();
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;

        _canvasWidth = workArea.Width;
        _canvasHeight = workArea.Height;
        RootCanvas.Width = _canvasWidth;
        RootCanvas.Height = _canvasHeight;

        _mode2Layer.Width = _canvasWidth;
        _mode2Layer.Height = _canvasHeight;

        _controlBar?.Reposition(workArea);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        ApplyClickThrough(AppState.Locked);

        _controlBar = new OverlayControlBarWindow(OpenControlPanel);
        _controlBar.Show();

        ApplyWorkArea();

        RepositionPanel(_mode1Panel);
        // BuildLayout() (appelé depuis le constructeur, avant que la fenêtre soit chargée)
        // positionne déjà _mode3Panel via ApplyModeVisuals, mais _canvasWidth valait encore 0
        // à ce moment-là (ApplyWorkArea() n'a pas encore tourné) : si Tutoriel est le mode actif
        // au démarrage, le panneau restait coincé en haut-gauche au lieu d'être recentré une
        // fois la vraie largeur d'écran connue.
        _mode3Panel.UpdateLayout();
        RepositionTopCenter(_mode3Panel);

        _historyClearTimer = new DispatcherTimer { Interval = HistoryClearDelay };
        _historyClearTimer.Tick += (_, _) =>
        {
            _historyClearTimer.Stop();
            _ = ClearHistoryGraduallyAsync();
        };

        _comboTimer = new DispatcherTimer { Interval = ComboWindow };
        _comboTimer.Tick += (_, _) =>
        {
            _comboTimer!.Stop();
            FlushPendingBinds();
        };

        // Poll indépendant du clavier (contrairement à _comboTimer, jamais redémarré à
        // chaque appui) : c'est justement l'absence d'appui qu'on veut détecter, pour
        // abandonner un combo en cours si le joueur ne l'a pas poursuivi depuis
        // ComboAbandonTimeout (voir ComboRunner.CheckAbandon).
        _comboAbandonPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _comboAbandonPollTimer.Tick += (_, _) => _comboRunner?.CheckAbandon(DateTime.UtcNow, ComboAbandonTimeout);
        _comboAbandonPollTimer.Start();

        AppState.Hook.KeyDown += OnGlobalKeyDown;
        AppState.Hook.KeyUp += OnGlobalKeyUp;
        AppState.Hook.Start();

        // Manette : mêmes handlers que le clavier (codes synthétiques hors de la
        // plage VK réelle, voir GamepadHook), donc l'historique/les combos/les
        // raccourcis Ctrl+Alt+* fonctionnent à l'identique quelle que soit la
        // source d'input.
        AppState.Gamepad.ButtonDown += OnGlobalKeyDown;
        AppState.Gamepad.ButtonUp += OnGlobalKeyUp;
        AppState.Gamepad.Start();

        AppState.QuizRevealRequested += RevealQuizStepsTemporarily;

        BuildTrayIcon();

        // Applique le mode de démarrage configuré (par défaut 0, déjà affiché par
        // BuildLayout, donc pas de badge parasite si rien ne change).
        if (AppState.Settings.DefaultMode != AppState.ActiveMode)
        {
            AppState.SetMode(AppState.Settings.DefaultMode);
        }

        ApplyOpacity();
        ShowFirstRunHintIfNeeded();
    }

    /// <summary>Tout premier lancement (settings.json absent avant chargement) :
    /// affiche temporairement le bandeau de raccourcis (normalement réservé au
    /// mode déverrouillé) pour que l'utilisateur découvre qu'un panneau de
    /// contrôle existe, sans avoir à deviner Ctrl+Alt+O au préalable.</summary>
    private void ShowFirstRunHintIfNeeded()
    {
        if (!OverlaySettingsConfig.WasFirstRun) return;

        _hintText.Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            // Ne masque que si toujours verrouillé : si l'utilisateur a lui-même
            // déverrouillé entre-temps, on ne casse pas ce qu'il a déclenché.
            if (AppState.Locked) _hintText.Visibility = Visibility.Collapsed;
        };
        timer.Start();
    }

    private void BuildTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        var lockItem = new System.Windows.Forms.ToolStripMenuItem("Verrouiller / Déverrouiller (Ctrl+Alt+O)");
        lockItem.Click += (_, _) => Dispatcher.Invoke(AppState.ToggleLock);
        menu.Items.Add(lockItem);

        var modeItem = new System.Windows.Forms.ToolStripMenuItem("Changer de mode (Ctrl+Alt+P)");
        modeItem.Click += (_, _) => Dispatcher.Invoke(AppState.CycleMode);
        menu.Items.Add(modeItem);

        var comboItem = new System.Windows.Forms.ToolStripMenuItem("Changer de combo (Ctrl+Alt+K)");
        comboItem.Click += (_, _) => Dispatcher.Invoke(AppState.CycleCombo);
        menu.Items.Add(comboItem);

        var recordItem = new System.Windows.Forms.ToolStripMenuItem("Démarrer/arrêter l'enregistrement d'un combo (Ctrl+Alt+R)");
        recordItem.Click += (_, _) => Dispatcher.Invoke(() => AppState.SetRecording(!AppState.Recording));
        menu.Items.Add(recordItem);

        var revealItem = new System.Windows.Forms.ToolStripMenuItem("Révéler le combo (cacher les étapes, Ctrl+Alt+I)");
        revealItem.Click += (_, _) => Dispatcher.Invoke(RevealQuizStepsTemporarily);
        menu.Items.Add(revealItem);

        var suspendItem = new System.Windows.Forms.ToolStripMenuItem("Suspendre la capture (Ctrl+Alt+H)") { CheckOnClick = true };
        suspendItem.Click += (_, _) => Dispatcher.Invoke(AppState.ToggleCaptureSuspended);
        AppState.CaptureSuspendedChanged += suspended => suspendItem.Checked = suspended;
        menu.Items.Add(suspendItem);

        var hideItem = new System.Windows.Forms.ToolStripMenuItem("Masquer/afficher l'overlay (Ctrl+Alt+M)") { CheckOnClick = true };
        hideItem.Click += (_, _) => Dispatcher.Invoke(AppState.ToggleOverlayHidden);
        AppState.OverlayHiddenChanged += hidden => hideItem.Checked = hidden;
        menu.Items.Add(hideItem);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var panelItem = new System.Windows.Forms.ToolStripMenuItem("Ouvrir le panneau de contrôle (Ctrl+Alt+U)");
        panelItem.Click += (_, _) => Dispatcher.Invoke(OpenControlPanel);
        menu.Items.Add(panelItem);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var quitItem = new System.Windows.Forms.ToolStripMenuItem("Quitter");
        quitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(quitItem);

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Brawlhalla Input Overlay",
            Visible = true,
            ContextMenuStrip = menu,
        };
        if (OverlaySettingsConfig.WasFirstRun)
        {
            // Un premier lancement sans historique/combo n'a aucun autre moyen de
            // découvrir que l'app tourne ici et que c'est la porte d'entrée vers le
            // panneau de contrôle (voir aussi le badge d'accueil affiché sur l'overlay).
            _trayIcon.ShowBalloonTip(6000, "Brawlhalla Input Overlay", "L'overlay tourne ici. Clic gauche sur cette icône (ou Ctrl+Alt+U) ouvre le panneau de contrôle.", System.Windows.Forms.ToolTipIcon.Info);
        }
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left)
            {
                Dispatcher.Invoke(OpenControlPanel);
            }
        };
    }

    /// <summary>Icône de tray à partir du logo de l'app (Assets/AppLogo.png, voir le .csproj) —
    /// remplace l'ancien "B" dessiné au runtime. Redimensionné en 32×32 ici (System.Drawing.Bitmap,
    /// pas BitmapImage WPF, puisque NotifyIcon attend un System.Drawing.Icon).</summary>
    private static System.Drawing.Icon CreateTrayIcon()
    {
        const int size = 32;
        using var stream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/AppLogo.png", UriKind.Absolute))!.Stream;
        using var source = new System.Drawing.Bitmap(stream);
        using var bmp = new System.Drawing.Bitmap(size, size);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.Clear(System.Drawing.Color.Transparent);
            g.DrawImage(source, 0, 0, size, size);
        }

        var hIcon = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(hIcon);
    }

    private void OpenControlPanel()
    {
        if (_controlPanel is null || !_controlPanel.IsLoaded)
        {
            _controlPanel = new ControlPanelWindow();
            _controlPanel.Closed += (_, _) => _controlPanel = null;
            _controlPanel.Show();
        }
        else
        {
            if (_controlPanel.WindowState == WindowState.Minimized)
                _controlPanel.WindowState = WindowState.Normal;
            _controlPanel.Activate();
        }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(_controlPanel).EnsureHandle();
        SetForegroundWindow(hwnd);
    }

    private void RepositionPanel(Border panel)
    {
        double left, top;
        switch (AppState.Settings.Position)
        {
            case OverlayPosition.BottomRight:
                left = _canvasWidth - panel.ActualWidth - 24;
                top = _canvasHeight - panel.ActualHeight - 24;
                break;
            case OverlayPosition.TopLeft:
                left = 24;
                top = 24;
                break;
            case OverlayPosition.TopRight:
                left = _canvasWidth - panel.ActualWidth - 24;
                top = 24;
                break;
            case OverlayPosition.Free:
                left = AppState.Settings.FreeLeft;
                top = AppState.Settings.FreeTop;
                break;
            case OverlayPosition.BottomLeft:
            default:
                left = 24;
                top = _canvasHeight - panel.ActualHeight - 24;
                break;
        }

        Canvas.SetLeft(panel, left);
        Canvas.SetTop(panel, top);
    }

    // Le mode Tutoriel (mode 3) ignore le réglage de position général : il reste
    // toujours un gros bandeau centré en haut de l'écran, pour rester lisible
    // pendant l'action sans dépendre d'où l'utilisateur a placé le panneau 1.
    private void RepositionTopCenter(Border panel)
    {
        Canvas.SetLeft(panel, (_canvasWidth - panel.ActualWidth) / 2);
        Canvas.SetTop(panel, 30);
    }

    private void ApplyScale()
    {
        var scale = AppState.Settings.Scale;
        var transform = new ScaleTransform(scale, scale);
        _mode1Panel.LayoutTransform = transform;
        _mode3Panel.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void ApplyOpacity()
    {
        Opacity = AppState.Settings.Opacity;
    }

    private void OnGlobalKeyDown(int vkCode)
    {
        ResetHistoryClearTimer();

        if (IsCtrl(vkCode)) _ctrlDown = true;
        if (IsAlt(vkCode)) _altDown = true;

        if (_ctrlDown && _altDown && vkCode == VK_O)
        {
            Dispatcher.Invoke(AppState.ToggleLock);
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_P)
        {
            Dispatcher.Invoke(AppState.CycleMode);
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_K)
        {
            Dispatcher.Invoke(AppState.CycleCombo);
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_R)
        {
            Dispatcher.Invoke(() => AppState.SetRecording(!AppState.Recording));
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_U)
        {
            Dispatcher.Invoke(OpenControlPanel);
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_I)
        {
            Dispatcher.Invoke(RevealQuizStepsTemporarily);
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_H)
        {
            Dispatcher.Invoke(AppState.ToggleCaptureSuspended);
            return;
        }

        if (_ctrlDown && _altDown && vkCode == VK_M)
        {
            Dispatcher.Invoke(AppState.ToggleOverlayHidden);
            return;
        }

        if (vkCode == GP_START) _padStartDown = true;

        if (_padStartDown && vkCode == GP_RB)
        {
            Dispatcher.Invoke(AppState.CycleCombo);
            return;
        }

        if (_padStartDown && vkCode == GP_LB)
        {
            Dispatcher.Invoke(AppState.CyclePreviousCombo);
            return;
        }

        if (_padStartDown && vkCode == GP_Y)
        {
            Dispatcher.Invoke(AppState.CycleMode);
            return;
        }

        if (_padStartDown && vkCode == GP_BACK)
        {
            Dispatcher.Invoke(AppState.ToggleCaptureSuspended);
            return;
        }

        if (_padStartDown && vkCode == GP_X)
        {
            Dispatcher.Invoke(OpenControlPanel);
            return;
        }

        // Suspendu : on garde le suivi Ctrl/Alt et les raccourcis ci-dessus actifs
        // (pour pouvoir se réactiver), mais on n'allume plus les touches, n'ajoute
        // plus à l'historique et ne nourrit plus le ComboRunner — sinon n'importe
        // quelle frappe faite ailleurs sur le PC (hors jeu) continue de faire
        // avancer/rater silencieusement le combo en cours (voir AppState.CaptureSuspended).
        if (AppState.CaptureSuspended) return;

        var hasBrushes = _brushesByVk.TryGetValue(vkCode, out var brushes);
        var hasSwapBrushes = _colorSwapByVk.TryGetValue(vkCode, out var swapBrushes);

        // Ignore l'auto-répétition OS : un seul événement d'historique par appui,
        // pas une rafale tant que la touche reste enfoncée.
        KeyBind? bind = null;
        var shouldQueueBind = _pressedVks.Add(vkCode) && _bindsByVk.TryGetValue(vkCode, out bind);

        if (!hasBrushes && !hasSwapBrushes && !shouldQueueBind) return;

        // Un seul aller-retour vers le thread UI pour les trois mutations, plutôt
        // que trois Dispatcher.Invoke séparés sur le chemin le plus chaud de l'app
        // (appelé pour chaque frappe, clavier comme manette).
        Dispatcher.Invoke(() =>
        {
            if (hasBrushes) foreach (var brush in brushes!) brush.Opacity = 1.0;
            if (hasSwapBrushes) foreach (var brush in swapBrushes!) brush.Color = ArrowPressedColor;
            if (shouldQueueBind) QueuePendingBind(bind!);
        });
    }

    // Recalcule à chaque nouvel appui l'ensemble des touches *actuellement* enfoncées
    // (pas seulement celles pressées pendant la fenêtre) : une direction déjà tenue
    // avant que l'attaque soit pressée doit quand même compter comme tenue au moment
    // du coup — sinon "se placer puis attaquer" ne matcherait jamais une étape combinée
    // du mode Tutoriel. Le ComboRunner est nourri ICI, tout de suite, à chaque appui :
    // aucun délai d'attente n'est introduit avant de juger un coup (voir ComboRunner
    // pour le détail de la tolérance mouvement/mash qui remplace l'ancien système de
    // fenêtre de regroupement, qui rendait certaines combos injouables). Seul
    // l'affichage dans l'historique (regroupement visuel "A + B", voir FlushPendingBinds)
    // attend encore un court instant, sans impact sur la validation du combo.
    private void QueuePendingBind(KeyBind bind)
    {
        _pendingBinds.Clear();
        foreach (var vk in _pressedVks)
        {
            if (_bindsByVk.TryGetValue(vk, out var heldBind) && !_pendingBinds.Contains(heldBind))
            {
                _pendingBinds.Add(heldBind);
            }
        }

        _lastFedActionNames = _pendingBinds.Select(b => b.Action).ToList();
        _comboRunner?.Feed(new List<KeyBind>(_pendingBinds), DateTime.UtcNow);

        _comboTimer?.Stop();
        _comboTimer?.Start();
    }

    private void FlushPendingBinds()
    {
        if (_pendingBinds.Count == 0) return;

        var binds = new List<KeyBind>(_pendingBinds);
        _pendingBinds.Clear();
        RegisterMove(binds);
    }

    private void OnGlobalKeyUp(int vkCode)
    {
        ResetHistoryClearTimer();

        if (IsCtrl(vkCode)) _ctrlDown = false;
        if (IsAlt(vkCode)) _altDown = false;
        if (vkCode == GP_START) _padStartDown = false;

        _pressedVks.Remove(vkCode);

        if (AppState.CaptureSuspended) return;

        var hasBrushes = _brushesByVk.TryGetValue(vkCode, out var brushes);
        var hasSwapBrushes = _colorSwapByVk.TryGetValue(vkCode, out var swapBrushes);
        if (!hasBrushes && !hasSwapBrushes) return;

        Dispatcher.Invoke(() =>
        {
            if (hasBrushes) foreach (var brush in brushes!) brush.Opacity = _restOpacityByBrush[brush];
            if (hasSwapBrushes) foreach (var brush in swapBrushes!) brush.Color = ArrowRestColor;
        });
    }

    private void RegisterMove(List<KeyBind> binds)
    {
        var now = DateTime.UtcNow;

        if (AppState.Recording) _recordedMoves.Add((binds, now));
        AppState.LogSessionMove(HistoryText(binds));

        // Spam de la même action/combo (ex: attaque légère martelée) : on ne veut
        // qu'une seule ligne dans l'historique, avec un compteur, puisque seul le
        // premier appui compte vraiment dans le combo.
        if (_lastLoggedBinds is not null && BindsEqual(_lastLoggedBinds, binds) && _lastLoggedText is not null && (now - _lastLoggedTime) <= MergeWindow)
        {
            _lastLoggedCount++;
            _lastLoggedText.Text = $"{HistoryText(binds)} ×{_lastLoggedCount}";
            Pulse(_lastLoggedEntry!);
            _lastLoggedTime = now;
            return;
        }

        var (entry, text) = CreateHistoryEntry(binds);
        _historyPanel.Children.Add(entry);
        entry.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));

        while (_historyPanel.Children.Count > AppState.Settings.MaxHistoryEntries)
        {
            _historyPanel.Children.RemoveAt(0);
        }

        _lastLoggedBinds = binds;
        _lastLoggedEntry = entry;
        _lastLoggedText = text;
        _lastLoggedCount = 1;
        _lastLoggedTime = now;
    }

    private static bool BindsEqual(List<KeyBind> a, List<KeyBind> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    private static (Border entry, TextBlock text) CreateHistoryEntry(List<KeyBind> binds)
    {
        var color = (Color)ColorConverter.ConvertFromString(binds[0].Color);

        var text = new TextBlock
        {
            Text = HistoryText(binds),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        };

        var entry = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(8, 4, 8, 4),
            Opacity = 0,
            Child = text,
        };

        return (entry, text);
    }

    private static string HistoryText(List<KeyBind> binds) =>
        string.Join(" + ", binds.Select(HistoryTextSingle));

    private static string HistoryTextSingle(KeyBind bind) =>
        string.IsNullOrEmpty(bind.Symbol) ? bind.Action : $"{bind.Symbol} ({bind.Action})";

    private static void Pulse(Border entry)
    {
        var flash = new DoubleAnimation(1.0, 0.55, TimeSpan.FromMilliseconds(80))
        {
            AutoReverse = true,
        };
        entry.BeginAnimation(OpacityProperty, flash);
    }

    private void ResetHistoryClearTimer()
    {
        _historyClearTimer?.Stop();
        _historyClearCts?.Cancel();
        _historyClearTimer?.Start();
    }

    private async Task ClearHistoryGraduallyAsync()
    {
        _historyClearCts?.Cancel();
        var cts = new CancellationTokenSource();
        _historyClearCts = cts;

        try
        {
            while (_historyPanel.Children.Count > 0)
            {
                cts.Token.ThrowIfCancellationRequested();

                if (_historyPanel.Children[0] is Border entry)
                {
                    var fade = new DoubleAnimation(1.0, 0.0, HistoryClearStep);
                    entry.BeginAnimation(OpacityProperty, fade);
                    await Task.Delay(HistoryClearStep + HistoryClearStagger, cts.Token);
                }
                else
                {
                    await Task.Delay(HistoryClearStagger, cts.Token);
                }

                if (cts.Token.IsCancellationRequested)
                    break;

                if (_historyPanel.Children.Count > 0)
                    _historyPanel.Children.RemoveAt(0);
            }
        }
        catch (OperationCanceledException)
        {
            // Le timer a été réinitialisé par une nouvelle entrée : ne pas toucher
            // à _lastLoggedBinds/Entry/Text, RegisterMove vient de les fixer pour
            // cette nouvelle touche (sinon son mash-merge serait cassé).
            return;
        }
        finally
        {
            if (_historyClearCts == cts)
                _historyClearCts = null;
        }

        _lastLoggedBinds = null;
        _lastLoggedEntry = null;
        _lastLoggedText = null;
        _lastLoggedCount = 0;
    }

    private void ApplyLockVisuals(bool locked)
    {
        ApplyClickThrough(locked);
        _hintText.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnLockChanged(bool locked) => Dispatcher.Invoke(() => ApplyLockVisuals(locked));

    private static readonly string[] ModeNames = { "Historique", "Grand affichage", "Tutoriel" };

    private void ApplyModeVisuals(int mode, bool showBadge)
    {
        _mode1Panel.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        _mode2Layer.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        _mode3Panel.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;

        // L'historique n'est affiché qu'en mode Historique (mode 1) : en mode Tutoriel
        // (mode 3), il ajoutait du bruit visuel jugé gênant en jeu par l'utilisateur —
        // les pastilles de combo suffisent à suivre la progression.
        _historySlotMode1.Content = mode == 0 ? _historyPanel : null;

        if (mode == 2)
        {
            _mode3Panel.UpdateLayout();
            RepositionTopCenter(_mode3Panel);
        }
        else if (mode == 0)
        {
            _mode1Panel.UpdateLayout();
            RepositionPanel(_mode1Panel);
        }

        if (showBadge) ShowModeBadge(ModeNames[mode]);
    }

    private void OnModeChanged(int mode) => Dispatcher.Invoke(() => ApplyModeVisuals(mode, showBadge: true));

    /// <summary>Sans indicateur permanent, un utilisateur qui active la
    /// suspension (ou la retrouve active à la relance) ne comprend pas pourquoi
    /// plus rien ne s'allume/s'enregistre — contrairement au badge de mode
    /// (1.5s), celui-ci reste affiché tant que la capture est suspendue.</summary>
    private void OnCaptureSuspendedChanged(bool suspended) => Dispatcher.Invoke(() =>
    {
        _suspendedBadge.Opacity = suspended ? 1.0 : 0.0;
        Canvas.SetLeft(_suspendedBadge, (_canvasWidth - _suspendedBadge.ActualWidth) / 2);
    });

    /// <summary>Masquage complet (distinct du verrouillage, qui laisse l'overlay affiché mais
    /// non interactif) : cache la fenêtre principale et la barre de contrôle overlay sans les
    /// fermer — les hooks et le tray restent actifs, donc Ctrl+Alt+M ou l'icône de tray
    /// permettent de la réafficher instantanément.</summary>
    private void OnOverlayHiddenChanged(bool hidden) => Dispatcher.Invoke(() =>
    {
        Visibility = hidden ? Visibility.Hidden : Visibility.Visible;
        if (_controlBar is not null) _controlBar.Visibility = hidden ? Visibility.Hidden : Visibility.Visible;
    });

    private void ShowModeBadge(string text)
    {
        _modeBadge.Text = $"Mode : {text}";
        Canvas.SetLeft(_modeBadge, (_canvasWidth - _modeBadge.ActualWidth) / 2);

        _modeBadgeTimer?.Stop();
        _modeBadge.BeginAnimation(OpacityProperty, null);
        _modeBadge.Opacity = 1.0;

        _modeBadgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _modeBadgeTimer.Tick += (_, _) =>
        {
            _modeBadgeTimer!.Stop();
            _modeBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(400)));
        };
        _modeBadgeTimer.Start();
    }

    private void OnRecordingChanged(bool recording)
    {
        Dispatcher.Invoke(() =>
        {
            if (recording)
            {
                _recordedMoves.Clear();
                ShowModeBadge("Enregistrement combo… (Ctrl+Alt+R pour arrêter)");
            }
            else
            {
                SaveRecordedCombo();
            }
        });
    }

    private void SaveRecordedCombo()
    {
        if (_recordedMoves.Count == 0)
        {
            ShowModeBadge("Enregistrement annulé (aucun coup capturé)");
            return;
        }

        var steps = new List<ComboStep>();
        for (int i = 0; i < _recordedMoves.Count; i++)
        {
            var (binds, time) = _recordedMoves[i];
            int? maxDelay = null;
            if (i > 0)
            {
                var gapMs = (time - _recordedMoves[i - 1].Time).TotalMilliseconds;
                maxDelay = (int)Math.Max(150, gapMs * 1.6);
            }

            steps.Add(new ComboStep
            {
                RequiredActions = binds.Select(b => b.Action).ToList(),
                MaxDelayMs = maxDelay,
            });
        }

        var draft = new Combo
        {
            Name = $"Combo {AppState.Combos.Count + 1}",
            Steps = steps,
        };

        // Écran de relecture/édition avant sauvegarde (docs/plan.md §1.3.A, jamais fait
        // jusqu'ici — cette méthode sauvegardait auparavant direct dans AppState.Combos
        // sans passer par un éditeur, voir docs/audit_features.md §1.5). Réutilise
        // ComboEditorWindow pré-rempli avec les étapes capturées, pour pouvoir supprimer
        // une étape parasite, ajuster une tolérance ou renommer avant de valider — sinon
        // la seule façon de corriger un combo mal enregistré était de rouvrir l'éditeur
        // après coup depuis la liste.
        var editor = new ComboEditorWindow(draft, isRecordingReview: true) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is not null)
        {
            AppState.Combos.Add(editor.Result);
            AppState.NotifyCombosMutated();
            ShowModeBadge($"Combo enregistré : {editor.Result.Name} ({editor.Result.Steps.Count} étapes)");
            AppState.SetActiveCombo(AppState.Combos.Count - 1);
        }
        else
        {
            ShowModeBadge("Enregistrement rejeté");
        }
    }

    private void OnBindsChanged()
    {
        Dispatcher.Invoke(() =>
        {
            RebuildBindMaps();
            BuildLayout();
        });
    }

    private int _lastAppliedMonitorIndex = -2; // -2 = jamais appliqué, distinct de -1 (écran principal)

    private void OnSettingsChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (AppState.Settings.MonitorIndex != _lastAppliedMonitorIndex)
            {
                _lastAppliedMonitorIndex = AppState.Settings.MonitorIndex;
                ApplyWorkArea();
            }

            ApplyScale();
            ApplyOpacity();
            RepositionPanel(_mode1Panel);
            RepositionTopCenter(_mode3Panel);
            if (_comboRunner is not null) _comboRunner.KeepStreakOnFail = AppState.Settings.KeepStreakOnFail;

            // Le portrait et la faisabilité Dex suivent TrainingLegendFilter — un changement de
            // personnage sans changement de combo actif (déjà filtré pareil) ne lèverait sinon
            // jamais ActiveComboChanged pour les rafraîchir. Juste ces deux-là, pas
            // RenderComboSteps() au complet : ça reconstruirait les pastilles à l'état "step 0" et
            // désynchroniserait l'affichage d'un combo en cours.
            UpdateLegendPortrait();
            var idx = AppState.ActiveComboIndex;
            if (idx >= 0 && idx < AppState.Combos.Count) UpdateDexRequirement(AppState.Combos[idx]);
        });
    }

    private void ApplyClickThrough(bool clickThrough)
    {
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
        exStyle = clickThrough ? (exStyle | WS_EX_TRANSPARENT) : (exStyle & ~WS_EX_TRANSPARENT);
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
    }

    private void Mode1Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (AppState.Locked) return;
        _dragStart = e.GetPosition(RootCanvas);
        _mode1Panel.CaptureMouse();
    }

    private void Mode1Panel_MouseMove(object sender, MouseEventArgs e)
    {
        if (AppState.Locked || _dragStart is null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(RootCanvas);
        var delta = pos - _dragStart.Value;
        Canvas.SetLeft(_mode1Panel, Canvas.GetLeft(_mode1Panel) + delta.X);
        Canvas.SetTop(_mode1Panel, Canvas.GetTop(_mode1Panel) + delta.Y);
        _dragStart = pos;
    }

    private void Mode1Panel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not null)
        {
            AppState.Settings.Position = OverlayPosition.Free;
            AppState.Settings.FreeLeft = Canvas.GetLeft(_mode1Panel);
            AppState.Settings.FreeTop = Canvas.GetTop(_mode1Panel);
            AppState.SaveSettings();
        }

        _dragStart = null;
        _mode1Panel.ReleaseMouseCapture();
    }
}
