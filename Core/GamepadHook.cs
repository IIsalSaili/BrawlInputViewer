using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace BrawlhallaOverlay;

/// <summary>
/// Passive polling of gamepad button state via XInput (XInputGetState), same
/// security posture as KeyboardHook: user-mode reads of input device state,
/// no injection into any process, no automation of input. Buttons are
/// reported through the same style of ButtonDown/ButtonUp events as
/// KeyboardHook, but using synthetic codes offset well above the keyboard
/// virtual-key range (0-255) so both sources can share one lookup table in
/// MainWindow (KeyBind.VirtualKeyCodes mixes real VKs and these codes).
/// </summary>
public sealed class GamepadHook : IDisposable
{
    public const int SyntheticCodeBase = 0x10000;

    // 16ms (~60Hz) une fois une manette détectée, pour ne pas rater d'appui ;
    // 250ms tant qu'aucune manette n'est connectée (cas courant, clavier seul)
    // pour ne pas spammer XInputGetState ~60 fois/sec pour rien.
    private static readonly TimeSpan PollIntervalConnected = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan PollIntervalDisconnected = TimeSpan.FromMilliseconds(250);

    public static readonly (string Name, ushort Flag)[] Buttons =
    {
        ("DPadUp", 0x0001), ("DPadDown", 0x0002), ("DPadLeft", 0x0004), ("DPadRight", 0x0008),
        ("Start", 0x0010), ("Back", 0x0020), ("LeftStick", 0x0040), ("RightStick", 0x0080),
        ("LeftShoulder", 0x0100), ("RightShoulder", 0x0200),
        ("A", 0x1000), ("B", 0x2000), ("X", 0x4000), ("Y", 0x8000),
    };

    public static bool TryResolveSyntheticCode(string buttonName, out int code)
    {
        foreach (var (name, flag) in Buttons)
        {
            if (string.Equals(name, buttonName, StringComparison.OrdinalIgnoreCase))
            {
                code = SyntheticCodeBase + flag;
                return true;
            }
        }
        code = 0;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState(int dwUserIndex, out XInputState pState);

    private readonly DispatcherTimer _timer;
    private ushort _lastButtons;
    private bool _connected;
    private bool _unavailable;

    // Index XInput (0-3) de la manette actuellement suivie, -1 si aucune. Une manette
    // Bluetooth/branchée après une autre ne s'enregistre pas forcément sur l'index 0 —
    // interroger uniquement l'index 0 (comportement d'origine) pouvait donc ne jamais
    // détecter une manette pourtant bien reconnue par Windows. On reste "collé" au même
    // index tant qu'il répond, pour ne pas sauter d'une manette à l'autre en multi-manette.
    private int _activeIndex = -1;

    public event Action<int>? ButtonDown;
    public event Action<int>? ButtonUp;
    public event Action<bool>? ConnectionChanged;

    /// <summary>Dernier état brut lu (boutons + triggers + sticks), pour un panneau de diagnostic ; ne déclenche aucun event.</summary>
    public event Action<GamepadSnapshot>? RawStateChanged;

    public bool Connected => _connected;
    public int ActiveIndex => _activeIndex;

    public GamepadHook()
    {
        _timer = new DispatcherTimer { Interval = PollIntervalDisconnected };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        if (!_unavailable) _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        XInputState state = default;
        int result = 1; // ERROR_DEVICE_NOT_CONNECTED par défaut si aucun index ne répond
        int respondingIndex = -1;

        // Sonde l'index déjà actif en premier (chemin chaud le plus courant), puis les 4
        // index XInput si besoin (première connexion, ou manette débranchée/rebranchée sur
        // un autre index).
        foreach (var index in CandidateIndices())
        {
            try
            {
                result = XInputGetState(index, out state);
            }
            catch (DllNotFoundException)
            {
                // Pas de runtime XInput sur cette machine (rare, Windows sans DirectX
                // à jour) : on abandonne le polling plutôt que de spammer l'exception.
                _unavailable = true;
                _timer.Stop();
                return;
            }
            catch (EntryPointNotFoundException)
            {
                _unavailable = true;
                _timer.Stop();
                return;
            }

            if (result == 0)
            {
                respondingIndex = index;
                break;
            }
        }

        var connectedNow = respondingIndex >= 0;
        _activeIndex = connectedNow ? respondingIndex : -1;

        if (connectedNow != _connected)
        {
            _connected = connectedNow;
            _timer.Interval = connectedNow ? PollIntervalConnected : PollIntervalDisconnected;
            ConnectionChanged?.Invoke(_connected);
        }

        if (!connectedNow)
        {
            _lastButtons = 0;
            return;
        }

        var current = state.Gamepad.wButtons;
        var pressed = (ushort)(current & ~_lastButtons);
        var released = (ushort)(_lastButtons & ~current);

        if (pressed != 0 || released != 0)
        {
            foreach (var (_, flag) in Buttons)
            {
                if ((pressed & flag) != 0) ButtonDown?.Invoke(SyntheticCodeBase + flag);
                if ((released & flag) != 0) ButtonUp?.Invoke(SyntheticCodeBase + flag);
            }
        }

        _lastButtons = current;

        if (RawStateChanged is not null)
        {
            RawStateChanged.Invoke(new GamepadSnapshot(
                _activeIndex,
                current,
                state.Gamepad.bLeftTrigger,
                state.Gamepad.bRightTrigger,
                state.Gamepad.sThumbLX,
                state.Gamepad.sThumbLY,
                state.Gamepad.sThumbRX,
                state.Gamepad.sThumbRY));
        }
    }

    private System.Collections.Generic.IEnumerable<int> CandidateIndices()
    {
        if (_activeIndex >= 0) yield return _activeIndex;
        for (var i = 0; i < 4; i++)
        {
            if (i != _activeIndex) yield return i;
        }
    }

    public void Dispose() => _timer.Stop();
}

/// <summary>Instantané en lecture seule de l'état d'une manette, pour l'affichage (panneau de diagnostic périphériques).</summary>
public readonly record struct GamepadSnapshot(
    int Index,
    ushort Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftStickX,
    short LeftStickY,
    short RightStickX,
    short RightStickY);
