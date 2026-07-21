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

    public event Action<int>? ButtonDown;
    public event Action<int>? ButtonUp;
    public event Action<bool>? ConnectionChanged;

    public bool Connected => _connected;

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
        XInputState state;
        int result;
        try
        {
            result = XInputGetState(0, out state);
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

        var connectedNow = result == 0;
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
    }

    public void Dispose() => _timer.Stop();
}
