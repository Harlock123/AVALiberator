namespace AVALiberator.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). It works out where each rocket, raider and
/// pellet will be by the time a missile could get there and puts a blast in its way, the one that would hit a
/// ship soonest first. With the sky clear it bombs the base that faces it most squarely, leading it as the
/// planet turns, and when none faces it, it turns the planet to bring the nearest round.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>How long it takes to see something new: it only looks again every so often.</summary>
    public const int LookEvery = 5;
    /// <summary>How close the sight must be to the mark before it fires.</summary>
    public const double Steady = 9;
    /// <summary>How fast it can swing the sight: quick, but not as quick as a flick of the mouse.</summary>
    public const double Swing = 820;
    /// <summary>A breath between shots.</summary>
    public const double Pause = 0.22;

    private int _tick;
    private (double X, double Y)? _mark;
    /// <summary>Where it means to fire next, for the probe.</summary>
    public (double X, double Y)? Mark => _mark;
    private bool _fire;
    private double _spinX, _spinY, _wait;
    private Threat? _chasing;

    /// <summary>A threat this close to its ship comes before any base.</summary>
    public const double Urgent = 3;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        if (g.Phase != Phase.Battle) { Doing = "waiting"; _mark = null; _chasing = null; return default; }
        _wait -= Field.Step;
        if (_wait > 0) return new Controls(SpinX: _spinX, SpinY: _spinY);
        if (_tick % LookEvery == 0 || _mark == null) Look(g);
        if (_mark is not { } m) return new Controls(SpinX: _spinX, SpinY: _spinY);
        bool on = Math.Abs(g.SightX - m.X) < Steady && Math.Abs(g.SightY - m.Y) < Steady;
        double dx = m.X - g.SightX, dy = m.Y - g.SightY, d = Math.Sqrt(dx * dx + dy * dy), step = Swing * Field.Step;
        (double X, double Y) aim = d <= step ? m : (g.SightX + dx / d * step, g.SightY + dy / d * step);
        var c = new Controls(Fire: on && _fire, SpinX: _spinX, SpinY: _spinY, AimX: aim.X, AimY: aim.Y);
        if (on && _fire) { _fire = false; _mark = null; _chasing = null; _wait = Pause; }
        return c;
    }

    private void Look(GameEngine g)
    {
        _spinX = _spinY = 0;
        _fire = true;
        // Stick with the threat it's after until it fires: switching every look, it would never settle.
        if (_chasing is { Dead: false } t && Lead(g, t) is { } again) { _mark = again.At; return; }
        _chasing = null;
        if (Intercept(g, Urgent) is { } shot) { (_mark, _chasing) = (shot.At, shot.Threat); Doing = "intercepting"; return; }
        if (Bomb(g) is { } bomb) { _mark = bomb; Doing = "bombing"; return; }
        if (Intercept(g, double.MaxValue) is { } any) { (_mark, _chasing) = (any.At, any.Threat); Doing = "intercepting"; return; }
        _fire = false;
        _mark = null;
        Seek(g);
    }

    /// <summary>The ship that would fire at a point: the nearest one that's ready.</summary>
    private static Ship? Launcher(GameEngine g, double x, double y) =>
        g.Ships.Where(s => s.Ready).OrderBy(s => (s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y)).FirstOrDefault();

    /// <summary>A missile already on its way to somewhere near, or a blast there still big enough.</summary>
    private static bool Covered(GameEngine g, double x, double y, double within) =>
        g.Missiles.Any(m => Near(m.Tx, m.Ty, x, y, within)) || g.Blasts.Any(b => b.Age < 0.45 && Near(b.X, b.Y, x, y, within));

    private static bool Near(double ax, double ay, double bx, double by, double d) => (ax - bx) * (ax - bx) + (ay - by) * (ay - by) < d * d;

    /// <summary>Where to burst a missile in a threat's way, and how long it has before it reaches its ship; nothing if it can't be caught.</summary>
    private static ((double X, double Y) At, double Eta)? Lead(GameEngine g, Threat t)
    {
        var ship = g.Ships[t.Target];
        double eta = Math.Sqrt((ship.X - t.X) * (ship.X - t.X) + (ship.Y - t.Y) * (ship.Y - t.Y)) / Math.Max(1, t.Speed);
        double lead = 0, px = t.X, py = t.Y;
        for (int i = 0; i < 4; i++)
        {
            var from = Launcher(g, px, py);
            if (from == null) return null;
            double fly = Math.Sqrt((px - from.X) * (px - from.X) + (py - from.Y) * (py - from.Y)) / GameEngine.MissileSpeed;
            double aim = Math.Sqrt((px - g.SightX) * (px - g.SightX) + (py - g.SightY) * (py - g.SightY)) / Swing;
            lead = fly + aim + 0.12;
            px = t.X + t.Vx * lead;
            py = t.Y + t.Vy * lead;
        }
        if (lead > eta + 0.3) return null;
        if (px < Field.SightLeft || px > Field.SightRight || py < Field.SightTop || py > Field.SightBottom) return null;
        return ((px, py), eta);
    }

    /// <summary>The threat that would reach its ship soonest, within so long, and where to burst a missile in its way.</summary>
    private static ((double X, double Y) At, Threat Threat)? Intercept(GameEngine g, double within)
    {
        ((double X, double Y) At, Threat Threat)? best = null;
        double soonest = within;
        foreach (var t in g.Threats)
        {
            if (Lead(g, t) is not { } l || l.Eta >= soonest || Covered(g, l.At.X, l.At.Y, 40)) continue;
            soonest = l.Eta;
            best = (l.At, t);
        }
        return best;
    }

    /// <summary>The base facing most squarely, where it will be when the missile lands.</summary>
    private (double, double)? Bomb(GameEngine g)
    {
        var sentinel = g.Sentinels.Where(s => !s.Dead && !s.Hidden && s.Z > 0.3).FirstOrDefault();
        Base? best = null;
        double facing = 0.3;
        foreach (var b in g.Bases.Where(b => !b.Dead))
        {
            var (_, _, f) = g.Globe.Project(b.At);
            if (f <= facing) continue;
            var (x, y, _) = g.Globe.Project(b.At);
            if (Covered(g, x, y, 34)) continue;
            facing = f;
            best = b;
        }
        if (best == null)
        {
            if (sentinel != null && !Covered(g, sentinel.X, sentinel.Y, 40)) return Ahead(sentinel);
            return null;
        }
        // Lead it: where it will be once the sight is there and the missile has flown.
        var (bx, by, _) = g.Globe.Project(best.At);
        var from = Launcher(g, bx, by);
        if (from == null) return null;
        double t = Math.Sqrt((bx - from.X) * (bx - from.X) + (by - from.Y) * (by - from.Y)) / GameEngine.MissileSpeed
                 + Math.Sqrt((bx - g.SightX) * (bx - g.SightX) + (by - g.SightY) * (by - g.SightY)) / Swing + 0.1;
        var ahead = g.Globe.Copy();
        ahead.Turn(g.Drift.Right * t, g.Drift.Down * t);
        var (ax, ay, _) = ahead.Project(best.At);
        return (ax, ay);
    }

    private static (double, double) Ahead(Sentinel s)
    {
        var copy = new Sentinel { Angle = s.Angle + s.Speed * 0.5, Tilt = s.Tilt, Lean = s.Lean };
        copy.Place();
        return (copy.X, copy.Y);
    }

    /// <summary>No base in view: turn the planet to bring the nearest one round.</summary>
    private void Seek(GameEngine g)
    {
        Base? near = null;
        double facing = double.MinValue;
        foreach (var b in g.Bases.Where(b => !b.Dead))
        {
            var v = g.Globe.ToView(b.At);
            if (v.Z > facing) { facing = v.Z; near = b; }
        }
        if (near == null) { Doing = "idle"; return; }
        var at = g.Globe.ToView(near.At);
        // Turning right raises Z by X's worth, turning down by Y's: push the way that brings it to the front.
        _spinX = Math.Clamp(-at.X * 3, -1, 1);
        _spinY = Math.Clamp(at.Y * 3, -1, 1);
        if (at.Z < 0 && Math.Abs(at.X) < 0.2 && Math.Abs(at.Y) < 0.2) _spinX = 1;
        Doing = "turning";
    }
}
