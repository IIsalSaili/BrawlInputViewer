using System;
using System.Threading;

namespace BrawlhallaOverlay;

/// <summary>Les 5 paliers de dégâts officiels de Brawlhalla (couleur de la barre sous l'icône
/// joueur), aux seuils 0/50/100/150/200%.</summary>
public enum DamageTier { White, Yellow, Orange, Red, Black }

/// <summary>
/// Lit la couleur moyenne d'une petite
/// zone calibrée sur la barre de dégâts adverse et la classe en palier, par teinte (voir Classify).
///
/// **Réécrit le 2026-08-08 — la machine à états séquentielle a été supprimée.** L'ancienne version
/// partait du principe que classer chaque couleur de façon fiable était trop dur, et se contentait
/// donc de détecter QU'UN changement avait eu lieu pour avancer d'un cran dans le cycle fixe
/// White→Yellow→Orange→Red→Black. Deux constats de l'utilisateur en jeu ont fait tomber ça :
///   1. la classification par teinte, elle, est juste et « plutôt précise » — l'hypothèse de
///      départ était fausse, il n'y avait pas besoin de contourner le problème ;
///   2. la valeur produite par la machine à états, elle, ne bougeait JAMAIS. Cause : Blanc avait
///      un droit de veto immédiat sur une seule image (`if (hueHint == White) return White;`)
///      alors que tous les autres paliers exigeaient 2 lectures consécutives. Il suffisait donc
///      d'une capture sur trois tirant vers le blanc (animation de transition, chiffre de dégâts
///      dans la zone, reflet clair) pour ramener la machine à Blanc ET remettre son compteur de
///      stabilité à zéro — elle ne pouvait littéralement plus jamais en sortir.
///
/// Ne reste donc que ce qui marchait : classification par teinte à chaque capture, avec une
/// stabilisation sur 2 lectures consécutives avant de lever TierChanged (une couleur de transition
/// entre deux paliers ne doit pas compter comme un palier). Plus de cycle fixe, plus de couleur de
/// référence à faire dériver, plus de veto. Corollaire assumé : si la teinte est non concluante,
/// CurrentTier reste sur sa dernière valeur connue et vaut null tant qu'il n'y en a jamais eu —
/// on préfère ne rien affirmer plutôt que de supposer Blanc par défaut (c'est cette supposition
/// qui avait déjà cassé une version antérieure quand le calibrage n'avait pas lieu à 0%).
///
/// Reste purement informatif : rien dans ComboRunner ni dans la validation des combos ne dépend
/// du palier (contrairement à HudDamageSource, qui conditionne la validation via
/// ComboRunner.RequireHitConfirmation).
/// </summary>
public sealed class HudDamageTierSource : IDisposable
{
    private const int PollIntervalMs = 200;

    // Une couleur de transition (l'anim de changement de palier) ne doit pas déclencher un
    // changement de palier à tort — on exige la même conclusion sur 2 polls consécutifs avant de
    // la considérer réelle, plutôt qu'un debounce temporel comme HudDamageSource (ici c'est un
    // état stable qu'on classe, pas un pic ponctuel). S'applique à TOUS les paliers, Blanc
    // compris — c'est l'exception faite à Blanc qui figeait la valeur, voir résumé de classe.
    private const int StableReadsRequired = 2;

    // Thread de pool plutôt que DispatcherTimer, même raison que HudDamageSource (audit
    // 2026-08-07 §M2) : la capture GDI n'a aucun besoin du thread UI, et c'est ce thread-là qui
    // sert le hook clavier bas niveau.
    private readonly Timer _timer;
    private int _polling;
    private DamageTier? _currentTier;
    private DamageTier? _pendingTier;
    private int _pendingCount;

    /// <summary>Dernier palier classé de façon stable, ou null tant qu'aucune lecture concluante
    /// n'a eu lieu depuis Start() (zone mal calibrée, jeu fermé, teinte ambiguë).</summary>
    public DamageTier? CurrentTier => _currentTier;

    public event Action<DamageTier>? TierChanged;

    /// <summary>Levé à chaque capture réussie — contrairement à TierChanged (seulement sur un
    /// changement stabilisé sur 2 lectures). HueHint est la classification brute de CETTE capture,
    /// sans mémoire : c'est ce qui permet de vérifier visuellement que le calibrage vise la bonne
    /// zone (voir MainWindow/ControlPanelWindow).</summary>
    public event Action<(int R, int G, int B, DamageTier? HueHint)>? Sampled;

    public HudDamageTierSource()
    {
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start(int x, int y, int width, int height)
    {
        _roiX = x; _roiY = y; _roiWidth = width; _roiHeight = height;
        _currentTier = null;
        _pendingTier = null;
        _pendingCount = 0;
        _timer.Change(PollIntervalMs, PollIntervalMs);
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private int _roiX, _roiY, _roiWidth, _roiHeight;

    private void Poll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try { PollCore(); }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }

    private void PollCore()
    {
        var frame = ScreenRegionCapture.Capture(_roiX, _roiY, _roiWidth, _roiHeight);
        if (frame is null || frame.Length < 3) return;

        var sample = AverageColor(frame);
        var hueHint = Classify(sample.R, sample.G, sample.B);
        Sampled?.Invoke((sample.R, sample.G, sample.B, hueHint));

        // Lecture non concluante : on ne conclut rien et on ne casse pas non plus une série de
        // confirmations en cours par un seul poll ambigu (typiquement l'image de transition).
        if (hueHint is not { } hint) return;

        if (hint == _currentTier)
        {
            _pendingTier = null;
            _pendingCount = 0;
            return;
        }

        if (hint == _pendingTier)
        {
            _pendingCount++;
        }
        else
        {
            _pendingTier = hint;
            _pendingCount = 1;
        }

        if (_pendingCount < StableReadsRequired) return;

        _currentTier = hint;
        _pendingTier = null;
        _pendingCount = 0;
        TierChanged?.Invoke(hint);
    }

    private static (int R, int G, int B) AverageColor(byte[] bgrFrame)
    {
        long sumB = 0, sumG = 0, sumR = 0;
        var pixelCount = bgrFrame.Length / 3;
        for (var i = 0; i < bgrFrame.Length; i += 3)
        {
            sumB += bgrFrame[i];
            sumG += bgrFrame[i + 1];
            sumR += bgrFrame[i + 2];
        }
        return ((int)(sumR / pixelCount), (int)(sumG / pixelCount), (int)(sumB / pixelCount));
    }

    /// <summary>Classification par teinte — c'est désormais la SEULE source de vérité du palier
    /// (voir résumé de classe). Seuils Blanc/Rouge/Noir choisis à partir des couleurs nommées
    /// connues, pas de valeurs RGB exactes capturées sur le jeu. La frontière Orange/Jaune, elle,
    /// a été affinée le 2026-08-08 sur deux captures d'écran réelles fournies par l'utilisateur
    /// (Jaune ≈ rgb(238,228,10), hue 57,4° ; Orange ≈ rgb(238,148,10), hue 36,3° — pixels
    /// dominants de la barre/texte de tier, isolés par saturation+luminosité pour exclure le
    /// cadre HUD et le contour) : Jaune apparaissait trop tôt avec l'ancien seuil de 45°, plus
    /// proche du côté Orange (36,3°) que du milieu réel des deux teintes mesurées (46,9°). Seuil
    /// remonté à 47° pour laisser sa vraie place à l'Orange. Renvoie null quand aucune plage ne
    /// correspond — un silence assumé, jamais un palier supposé par défaut.</summary>
    internal static DamageTier? Classify(int r, int g, int b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        var value = max / 255.0;
        var saturation = max == 0 ? 0 : delta / (double)max;
        if (saturation < 0.25 && value > 0.6) return DamageTier.White;

        double hue;
        if (delta == 0) hue = 0;
        else if (max == r) hue = 60.0 * (((g - b) / (double)delta) % 6);
        else if (max == g) hue = 60.0 * (((b - r) / (double)delta) + 2);
        else hue = 60.0 * (((r - g) / (double)delta) + 4);
        if (hue < 0) hue += 360;

        if ((hue < 20 || hue >= 345) && value < 0.45) return DamageTier.Black;
        if (hue < 20 || hue >= 345) return DamageTier.Red;
        if (hue < 47) return DamageTier.Orange;
        if (hue < 75) return DamageTier.Yellow;

        return null;
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
