using System;
using System.Threading;

namespace BrawlhallaOverlay;

/// <summary>
/// Détecte qu'un hit a probablement eu lieu
/// en surveillant une petite zone du HUD (dégâts adverses) pour un changement de pixels — pas
/// d'OCR, pas de montant, juste "quelque chose a changé là où les dégâts s'affichent". Poll à
/// basse fréquence (même principe que ComboRunner.CheckMoveTimeout / AutoHideEnabled : un
/// DispatcherTimer, pas une boucle serrée) pour rester négligeable en CPU/GPU pendant que le
/// jeu tourne.
///
/// Purement une source de signal : ne touche jamais à ComboRunner ni à AppState. C'est
/// l'appelant (MainWindow) qui décide quoi en faire — et depuis la Version 24 il en fait
/// beaucoup : ce signal CONDITIONNE la validation d'un combo (ComboRunner.RequireHitConfirmation).
/// Le commentaire d'origine, qui décrivait encore ce signal comme un simple badge indicatif,
/// était faux depuis (audit 2026-08-07 §E3).
///
/// Le poll tourne sur un thread de pool, pas sur le thread UI (audit 2026-08-07 §M2) : la
/// capture GDI (allocation d'un Bitmap + BitBlt écran + copie ligne par ligne) était exécutée
/// 16 fois par seconde sur le thread qui sert AUSSI le hook clavier bas niveau. Un tick trop
/// long au-delà de LowLevelHooksTimeout (300 ms par défaut) fait retirer le hook par Windows
/// sans le moindre message : l'app cesse alors de capter les touches tout en ayant l'air de
/// tourner normalement. Poll() ne fait que du calcul sur byte[], il n'a aucun besoin du thread
/// UI ; les abonnés (qui touchent l'UI) marshallent déjà via Dispatcher.Invoke.
/// </summary>
public sealed class HudDamageSource : IDisposable
{
    // Abaissé de 120ms à 60ms (Version 24, retour utilisateur : encore trop lent) — ~16 Hz,
    // toujours négligeable en CPU pour une ROI de cette taille. Chaque ms gagnée ici réduit
    // d'autant le pire cas de latence de détection avant que ComboRunner.HitConfirmationWindow
    // (voir Core/ComboRunner.cs) n'expire à tort sur un vrai hit qui a mis un peu de temps à
    // s'afficher.
    private const int PollIntervalMs = 60;

    // Évite plusieurs déclenchements pour une même animation de dégâts qui s'anime sur
    // plusieurs frames. Aligné sur PollIntervalMs (un seul poll de marge) plutôt qu'un delta
    // arbitraire plus large : un debounce plus court peut laisser passer 2-3 événements pour
    // l'animation d'un seul hit, mais ce n'est plus un problème depuis la Version 24 —
    // ComboRunner.ConfirmHit ignore silencieusement un hit qui n'a rien à confirmer (file
    // d'attente vide), donc un déclenchement "en trop" ne fait rien.
    private const int DebounceMs = PollIntervalMs;

    // Fraction de pixels de la ROI qui doivent différer (au-delà d'une petite tolérance de
    // bruit vidéo) pour considérer que quelque chose a réellement changé. Premier test réel
    // (2026-08-06) : 0.08 ne détectait que les changements massifs (mort du personnage, reset
    // de jauge) et ratait les changements de chiffre normaux — un changement "23%→35%" ne
    // couvre qu'une petite fraction d'une ROI qui inclut un peu de marge autour du texte.
    // Abaissé nettement.
    private const double ChangedPixelRatioThreshold = 0.015;

    // Un lancement d'arme (action "Lancer") fait souvent 1-2 points de dégâts — un seul
    // chiffre change d'un caractère, un delta de pixels trop petit pour passer le seuil normal
    // sans risquer des faux positifs sur TOUS les hits (baisser le seuil global reviendrait à
    // sacrifier la fiabilité déjà acquise sur les hits normaux pour un cas particulier). Demande
    // explicite de l'utilisateur (2026-08-06) : accepter ce risque de faux positif, mais
    // seulement pendant la fenêtre où l'étape de combo en cours exige "Lancer" — voir
    // MainWindow.UpdateHudSensitivityForCurrentStep, seul appelant de SetBoostedSensitivity.
    private const double BoostedChangedPixelRatioThreshold = 0.004;
    private const int PerChannelNoiseTolerance = 18;

    // Cadence de repli quand la zone n'a rien montré depuis un moment (jeu pas lancé, menu,
    // alt-tab) — audit 2026-08-07 §M15 : la capture tournait à pleine cadence en permanence dès
    // le lancement de l'app, y compris lancée au démarrage de Windows sans jeu ouvert. On
    // repasse à la cadence rapide au premier signe de vie.
    private const int IdlePollIntervalMs = 500;
    private static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(30);

    /// <summary>Au-delà de ce délai sans le moindre changement observé dans la zone, on considère
    /// que le calibrage ne regarde rien de vivant (mauvaise résolution, jeu fermé, HUD ailleurs)
    /// et que ce signal n'est PAS digne de conditionner la validation d'un combo — voir
    /// <see cref="HasLiveSignal"/>.</summary>
    private static readonly TimeSpan LiveSignalWindow = TimeSpan.FromMinutes(2);

    private readonly Timer _timer;
    private int _polling; // garde de réentrance : un tick lent ne doit pas en chevaucher un autre
    private bool _running;
    private int _currentIntervalMs = PollIntervalMs;
    private byte[]? _previousFrame;
    private DateTime _lastSignal = DateTime.MinValue;
    private DateTime _lastChangeUtc = DateTime.MinValue;
    private bool _boosted;

    /// <summary>Vrai si la zone surveillée a montré au moins un vrai changement récemment.
    ///
    /// Garde-fou central de l'audit 2026-08-07 §C1 : la détection de hit est activée par défaut
    /// sur une ROI codée en dur (celle d'un seul setup). Dès que cette zone ne correspond pas —
    /// autre résolution, autre mise à l'échelle, jeu pas au premier plan, jeu pas lancé, simple
    /// entraînement au clavier sur le bureau — aucun hit n'arrive jamais, et comme le signal
    /// conditionne la validation, CHAQUE étape d'attaque cassait la tentative au bout de la
    /// fenêtre de confirmation. L'app devenait inutilisable sans qu'aucun message n'explique
    /// pourquoi.
    ///
    /// Tant que la zone n'a rien montré, on considère le signal non fiable et le gate est levé :
    /// l'app se comporte exactement comme avant la Version 24. C'est le bon sens de repli — un
    /// gate désactivé à tort ne fait que valider un combo qu'on aurait peut-être dû refuser,
    /// alors qu'un gate actif à tort rend l'entraînement impossible.</summary>
    public bool HasLiveSignal => DateTime.UtcNow - _lastChangeUtc <= LiveSignalWindow;

    /// <summary>Vrai si la source tourne mais n'a encore jamais rien vu bouger — permet à l'UI
    /// de le dire explicitement plutôt que de laisser croire que la détection fonctionne.</summary>
    public bool IsRunningWithoutSignal => _running && !HasLiveSignal;

    public event Action? HitDetected;

    /// <summary>Levé à chaque capture réussie, changement détecté ou non — contrairement à
    /// HitDetected (débounced, seulement sur détection). Sert de retour visuel en direct côté
    /// panneau de contrôle (voir ControlPanelWindow) : sans ça, la seule façon de vérifier que le
    /// calibrage capture la bonne zone était d'attendre un vrai hit en jeu.</summary>
    public event Action<double>? Sampled; // ratio de pixels changés lors de la dernière capture

    public HudDamageSource()
    {
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start(int x, int y, int width, int height)
    {
        _roiX = x; _roiY = y; _roiWidth = width; _roiHeight = height;
        _previousFrame = null;
        _lastChangeUtc = DateTime.MinValue;
        _running = true;
        _currentIntervalMs = PollIntervalMs;
        _timer.Change(PollIntervalMs, PollIntervalMs);
    }

    public void Stop()
    {
        _running = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _previousFrame = null;
        _lastChangeUtc = DateTime.MinValue;
    }

    /// <summary>Bascule vers un seuil de détection beaucoup plus sensible (donc plus sujet aux
    /// faux positifs) — à n'activer que pendant une fenêtre courte où le gain le justifie, voir
    /// commentaire de BoostedChangedPixelRatioThreshold. Sans effet sur le poll lui-même, juste
    /// sur le seuil de comparaison utilisé au prochain tick.</summary>
    public void SetBoostedSensitivity(bool boosted) => _boosted = boosted;

    private int _roiX, _roiY, _roiWidth, _roiHeight;

    private void Poll()
    {
        // Un tick ne doit jamais chevaucher le précédent : System.Threading.Timer, contrairement
        // à DispatcherTimer, peut relancer le callback sur un autre thread du pool avant que le
        // précédent soit fini si la capture prend plus longtemps que l'intervalle.
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var frame = ScreenRegionCapture.Capture(_roiX, _roiY, _roiWidth, _roiHeight);
            if (frame is null) return;

            if (_previousFrame is not null && _previousFrame.Length == frame.Length)
            {
                var ratio = ChangedPixelRatio(_previousFrame, frame);
                Sampled?.Invoke(ratio);

                var threshold = _boosted ? BoostedChangedPixelRatioThreshold : ChangedPixelRatioThreshold;
                var now = DateTime.UtcNow;
                if (ratio >= threshold)
                {
                    // Marque la zone comme "vivante" même si le debounce avale l'événement :
                    // c'est bien la preuve que le calibrage regarde quelque chose qui bouge.
                    _lastChangeUtc = now;
                    if ((now - _lastSignal).TotalMilliseconds > DebounceMs)
                    {
                        _lastSignal = now;
                        HitDetected?.Invoke();
                    }
                }

                AdjustPollRate(now);
            }

            _previousFrame = frame;
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    /// <summary>Ralentit le poll quand la zone est inerte depuis longtemps, et le réaccélère dès
    /// le premier changement (§M15). Sans effet sur la logique de détection elle-même.</summary>
    private void AdjustPollRate(DateTime now)
    {
        if (!_running) return;
        var idle = now - _lastChangeUtc > IdleAfter;
        var wanted = idle ? IdlePollIntervalMs : PollIntervalMs;
        if (wanted == _currentIntervalMs) return;
        _currentIntervalMs = wanted;
        _timer.Change(wanted, wanted);
    }

    private static double ChangedPixelRatio(byte[] previous, byte[] current)
    {
        var pixelCount = previous.Length / 3;
        if (pixelCount == 0) return 0;

        var changed = 0;
        for (var i = 0; i < previous.Length; i += 3)
        {
            var db = Math.Abs(previous[i] - current[i]);
            var dg = Math.Abs(previous[i + 1] - current[i + 1]);
            var dr = Math.Abs(previous[i + 2] - current[i + 2]);
            if (db > PerChannelNoiseTolerance || dg > PerChannelNoiseTolerance || dr > PerChannelNoiseTolerance)
            {
                changed++;
            }
        }

        return (double)changed / pixelCount;
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
