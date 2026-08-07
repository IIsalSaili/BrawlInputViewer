using System;
using System.Windows.Threading;

namespace BrawlhallaOverlay;

/// <summary>
/// Phase 1a de docs/plan_improve_combo.md §3.1.1a : détecte qu'un hit a probablement eu lieu
/// en surveillant une petite zone du HUD (dégâts adverses) pour un changement de pixels — pas
/// d'OCR, pas de montant, juste "quelque chose a changé là où les dégâts s'affichent". Poll à
/// basse fréquence (même principe que ComboRunner.CheckAbandon / AutoHideEnabled : un
/// DispatcherTimer, pas une boucle serrée) pour rester négligeable en CPU/GPU pendant que le
/// jeu tourne.
///
/// Purement une source de signal : ne touche jamais à ComboRunner ni à AppState. L'appelant
/// (MainWindow) décide quoi faire du signal — actuellement rien d'autre qu'un badge indicatif,
/// voir §6 du plan ("le silence est le comportement par défaut d'un signal incertain").
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
    // Abaissé nettement ; si ça produit des faux positifs qu'un seuil simple ne filtre pas
    // (voir docs/plan_improve_combo.md, critère d'abandon 1a), on arrête là plutôt que d'aller
    // vers l'OCR sur une base instable.
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

    private readonly DispatcherTimer _timer;
    private byte[]? _previousFrame;
    private DateTime _lastSignal = DateTime.MinValue;
    private bool _boosted;

    public event Action? HitDetected;

    /// <summary>Levé à chaque capture réussie, changement détecté ou non — contrairement à
    /// HitDetected (débounced, seulement sur détection). Sert de retour visuel en direct côté
    /// panneau de contrôle (voir ControlPanelWindow) : sans ça, la seule façon de vérifier que le
    /// calibrage capture la bonne zone était d'attendre un vrai hit en jeu.</summary>
    public event Action<double>? Sampled; // ratio de pixels changés lors de la dernière capture

    public HudDamageSource()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start(int x, int y, int width, int height)
    {
        _roiX = x; _roiY = y; _roiWidth = width; _roiHeight = height;
        _previousFrame = null;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _previousFrame = null;
    }

    /// <summary>Bascule vers un seuil de détection beaucoup plus sensible (donc plus sujet aux
    /// faux positifs) — à n'activer que pendant une fenêtre courte où le gain le justifie, voir
    /// commentaire de BoostedChangedPixelRatioThreshold. Sans effet sur le poll lui-même, juste
    /// sur le seuil de comparaison utilisé au prochain tick.</summary>
    public void SetBoostedSensitivity(bool boosted) => _boosted = boosted;

    private int _roiX, _roiY, _roiWidth, _roiHeight;

    private void Poll()
    {
        var frame = ScreenRegionCapture.Capture(_roiX, _roiY, _roiWidth, _roiHeight);
        if (frame is null) return;

        if (_previousFrame is not null && _previousFrame.Length == frame.Length)
        {
            var ratio = ChangedPixelRatio(_previousFrame, frame);
            Sampled?.Invoke(ratio);

            var threshold = _boosted ? BoostedChangedPixelRatioThreshold : ChangedPixelRatioThreshold;
            if (ratio >= threshold && (DateTime.UtcNow - _lastSignal).TotalMilliseconds > DebounceMs)
            {
                _lastSignal = DateTime.UtcNow;
                HitDetected?.Invoke();
            }
        }

        _previousFrame = frame;
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

    public void Dispose() => Stop();
}
