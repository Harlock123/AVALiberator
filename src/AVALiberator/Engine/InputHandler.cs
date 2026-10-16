using Avalonia.Input;

namespace AVALiberator.Engine;

/// <summary>
/// Keyboard, mouse and gamepad boiled down to the controls: the sight, the planet's spin, and the fire button.
/// The mouse is the trackball: the sight follows the pointer, the left button fires, and dragging with the right
/// button held turns the planet. On the keyboard the arrows move the sight, and with Shift held they turn the
/// planet instead; on a gamepad the left stick moves the sight and the right stick turns the planet.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update() => _pad.Update();

    private double? _pointX, _pointY;
    private bool _mouseFire, _mouseTurn;
    private double _dragX, _dragY;

    /// <summary>The pointer has moved, to here on the screen.</summary>
    public void OnPointer(double x, double y)
    {
        if (_mouseTurn && _pointX is { } px && _pointY is { } py)
        {
            _dragX += x - px;
            _dragY += y - py;
        }
        _pointX = x;
        _pointY = y;
    }

    public void OnMouse(bool fire, bool turn)
    {
        _mouseFire = fire;
        _mouseTurn = turn;
    }

    public Controls Controls
    {
        get
        {
            bool shift = Any(Key.LeftShift, Key.RightShift);
            double dx = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            double dy = (Any(Key.Down, Key.S, Key.NumPad2) ? 1 : 0) - (Any(Key.Up, Key.W, Key.NumPad8) ? 1 : 0);
            // Keys take the sight back from the mouse until it moves again.
            if (dx != 0 || dy != 0) _pointX = _pointY = null;
            double sx = 0, sy = 0;
            if (shift) (sx, sy, dx, dy) = (dx, dy, 0, 0);
            if (dx == 0 && dy == 0)
            {
                dx = _pad.DPadLeft ? -1 : _pad.DPadRight ? 1 : Math.Abs(_pad.LeftStickX) > 0.2f ? _pad.LeftStickX : 0;
                dy = _pad.DPadUp ? -1 : _pad.DPadDown ? 1 : Math.Abs(_pad.LeftStickY) > 0.2f ? _pad.LeftStickY : 0;
                if (dx != 0 || dy != 0) _pointX = _pointY = null;
            }
            if (sx == 0 && sy == 0)
            {
                if (Math.Abs(_pad.RightStickX) > 0.2f) sx = _pad.RightStickX;
                if (Math.Abs(_pad.RightStickY) > 0.2f) sy = _pad.RightStickY;
            }
            if (_mouseTurn)
            {
                // A drag turns it like a trackball: so far for so much movement.
                sx = Math.Clamp(_dragX / 6, -3, 3);
                sy = Math.Clamp(_dragY / 6, -3, 3);
                _dragX = _dragY = 0;
            }
            bool fire = _mouseFire || Any(Key.Space, Key.Z, Key.X, Key.LeftCtrl, Key.RightCtrl) || _pad.ButtonA || _pad.ButtonB || _pad.ButtonX || _pad.RightTrigger > 0.5f;
            return new Controls(dx, dy, fire, sx, sy, _mouseTurn ? null : _pointX, _mouseTurn ? null : _pointY);
        }
    }

    public bool Start => Any(Key.Enter) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
