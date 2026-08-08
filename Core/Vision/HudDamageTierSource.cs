using System;
using System.Threading;

namespace BrawlhallaOverlay;

/// <summary>Les 5 paliers de dégâts officiels de Brawlhalla (couleur de la barre sous l'icône
/// joueur), aux seuils 0/50/100/150/200% — voir docs/plan_improve_combo.md §3.1.1c. Ordre fixe
/// et toujours croissant pendant une vie (les dégâts ne redescendent jamais sauf mort/respawn) :
/// c'est cet ordre, pas la couleur exacte, qui porte l'essentiel de l'information — voir
/// HudDamageTierSource.</summary>
public enum DamageTier { White, Yellow, Orange, Red, Black }

/// <summary>
/// Phase 1b (variante retenue, docs/plan_improve_combo.md) : lit la couleur moyenne d'une petite
/// zone calibrée sur la barre de dégâts adverse. Repose sur une propriété du jeu confirmée par
/// l'utilisateur (2026-08-07) : les 5 paliers sont dans un **ordre fixe et monotone croissant**
/// (White→Yellow→Orange→Red→Black) pendant une vie — donc avancer d'un cran dès qu'un changement
/// de couleur est détecté suffit, pas besoin de reclasser chaque couleur dans l'absolu à chaque
/// lecture. Seul Blanc doit être reconnu directement (c'est le reset sur mort/nouvelle vie) : par
/// teinte (faible saturation + forte luminosité), PAS par comparaison à une couleur "de référence"
/// capturée au hasard du moment du calibrage — une tentative en ce sens (2026-08-07) s'est
/// révélée cassée dès que le calibrage n'a pas lieu pile à 0% de dégâts (cas courant en
/// pratique), verrouillant la mauvaise couleur comme "Blanc" pour toute la session.
/// </summary>
public sealed class HudDamageTierSource : IDisposable
{
    private const int PollIntervalMs = 200;

    // Une couleur de transition (l'anim de changement de palier) ne doit pas déclencher un
    // changement de palier à tort — on exige la même conclusion sur 2 polls consécutifs avant de
    // la considérer réelle, plutôt qu'un debounce temporel comme HudDamageSource (ici c'est un
    // état stable qu'on classe, pas un pic ponctuel).
    private const int StableReadsRequired = 2;

    // Distance de couleur (somme des écarts absolus par canal, 0-765) à partir de laquelle on
    // considère que la couleur a réellement changé. Pas calibrée sur de vraies valeurs RGB
    // capturées — à ajuster si les tests réels montrent des ratés ou des faux déclenchements. Un
    // premier test réel (2026-08-07) a montré une progression trop rapide (paliers grillés) —
    // cause la plus probable : la zone calibrée inclut le CHIFFRE de dégâts en plus de la couleur
    // de fond, et chaque changement de chiffre (même sans changement de palier) modifie assez la
    // couleur moyenne pour franchir le seuil à tort. Le vrai correctif est de recalibrer une zone
    // qui évite le chiffre (une portion de couleur pure), pas seulement de remonter ce seuil.
    private const int ChangeThreshold = 45;

    // Thread de pool plutôt que DispatcherTimer, même raison que HudDamageSource (audit
    // 2026-08-07 §M2) : la capture GDI n'a aucun besoin du thread UI, et c'est ce thread-là qui
    // sert le hook clavier bas niveau.
    private readonly Timer _timer;
    private int _polling;
    private DamageTier _currentTier = DamageTier.White;
    private (int R, int G, int B) _stableColor;
    private DamageTier? _pendingTier;
    private int _pendingCount;
    private bool _initialized;

    public DamageTier CurrentTier => _currentTier;

    public event Action<DamageTier>? TierChanged;

    /// <summary>Levé à chaque capture réussie — contrairement à TierChanged (seulement sur un
    /// vrai changement stabilisé). Le Tier rapporté ici est un indice de classification par
    /// teinte purement indicatif/diagnostique — utile pour vérifier visuellement que le calibrage
    /// vise la bonne zone (voir MainWindow/ControlPanelWindow), mais CurrentTier fait foi.</summary>
    public event Action<(int R, int G, int B, DamageTier? HueHint)>? Sampled;

    public HudDamageTierSource()
    {
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start(int x, int y, int width, int height)
    {
        _roiX = x; _roiY = y; _roiWidth = width; _roiHeight = height;
        // Blanc reste l'hypothèse de secours (voir résumé de classe) tant que la toute première
        // lecture, ci-dessous, n'a pas pu confirmer autre chose.
        _currentTier = DamageTier.White;
        _stableColor = default;
        _pendingTier = null;
        _pendingCount = 0;
        _initialized = false;
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

        if (!_initialized)
        {
            // Première lecture depuis Start() : si la teinte est confiante, elle sert de point de
            // départ réel (ex. calibrage/activation en plein combat) plutôt que de rester
            // silencieusement sur l'hypothèse "Blanc" par défaut jusqu'au prochain changement.
            // Pas de TierChanged levé ici (ce n'est pas une vraie transition détectée).
            _initialized = true;
            _currentTier = hueHint ?? DamageTier.White;
            _stableColor = sample;
            return;
        }

        var candidate = ResolveCandidateTier(sample, hueHint);

        if (candidate == _currentTier)
        {
            _pendingTier = null;
            _pendingCount = 0;
            _stableColor = sample; // référence rafraîchie tant qu'on est stable, robuste à une dérive lente (luminosité, compression)
            return;
        }

        if (candidate == _pendingTier)
        {
            _pendingCount++;
        }
        else
        {
            _pendingTier = candidate;
            _pendingCount = 1;
        }

        if (_pendingCount >= StableReadsRequired)
        {
            _currentTier = candidate;
            _stableColor = sample;
            _pendingTier = null;
            _pendingCount = 0;
            TierChanged?.Invoke(candidate);
        }
    }

    /// <summary>Cœur de la logique retenue (voir résumé de classe) : Blanc reconnu par teinte
    /// prime toujours (c'est le reset). Sinon, si la couleur s'est suffisamment éloignée de la
    /// dernière couleur stable, on avance d'un cran dans le cycle fixe — sauf si la teinte est
    /// elle-même confiante et cohérente avec un palier différent, auquel cas elle prime (permet
    /// de sauter directement au bon palier plutôt que de remonter un par un si un poll a été
    /// raté).</summary>
    private DamageTier ResolveCandidateTier((int R, int G, int B) sample, DamageTier? hueHint)
    {
        if (hueHint == DamageTier.White) return DamageTier.White;

        var changed = ColorDistance(sample, _stableColor) >= ChangeThreshold;
        if (!changed) return _currentTier;

        if (hueHint is { } confident && confident != _currentTier) return confident;

        return Next(_currentTier);
    }

    private static DamageTier Next(DamageTier tier) => tier switch
    {
        DamageTier.White => DamageTier.Yellow,
        DamageTier.Yellow => DamageTier.Orange,
        DamageTier.Orange => DamageTier.Red,
        DamageTier.Red => DamageTier.Black,
        _ => DamageTier.Black, // déjà au max (200%+), rien au-delà
    };

    private static int ColorDistance((int R, int G, int B) a, (int R, int G, int B) b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

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

    /// <summary>Classification par teinte — sert de fois pour Blanc (voir ResolveCandidateTier),
    /// purement indicative pour le reste. Seuils choisis à partir des couleurs nommées connues,
    /// pas de valeurs RGB exactes capturées sur le jeu.</summary>
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
        if (hue < 45) return DamageTier.Orange;
        if (hue < 75) return DamageTier.Yellow;

        return null;
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
