using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVALiberator.Engine;
using Effect = AVALiberator.Engine.Effect;

namespace AVALiberator.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVALiberator/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;
    private const uint Hull = 0xFFB8D8FF, Sight = 0xFF6AFFB0, Rocket = 0xFFFF6A3A, RaiderPink = 0xFFFF5AD8, Pellet = 0xFF9AF0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    /// <summary>A colour part of the way to another.</summary>
    private static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return Ch(24) << 24 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    /// <summary>A round glow: the stops run from the middle (0) to the edge (1).</summary>
    private static IBrush Radial(Point c, double r, Point? origin, params (double At, uint Colour)[] stops)
    {
        var b = new RadialGradientBrush
        {
            Center = new RelativePoint(c, RelativeUnit.Absolute),
            GradientOrigin = new RelativePoint(origin ?? c, RelativeUnit.Absolute),
            RadiusX = new RelativeScalar(r, RelativeUnit.Absolute),
            RadiusY = new RelativeScalar(r, RelativeUnit.Absolute),
        };
        foreach (var (at, col) in stops) b.GradientStops.Add(new GradientStop(Color.FromUInt32(col), at));
        return b;
    }

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF020208), new Rect(0, 0, Field.Width, Field.Height));
        DrawStars(dc);
        if (InGame)
        {
            double s = g.PlanetScale;
            foreach (var t in g.Sentinels.Where(t => !t.Dead && t.Z < 0 && !t.Hidden)) DrawSentinel(dc, t);
            if (s > 0) DrawDoomed(dc, g, s);
            else if (g.Phase == Phase.Liberated && g.PhaseTime < GameEngine.BreakAt + 0.8)
            {
                // The flash as it goes.
                double t = (g.PhaseTime - GameEngine.BreakAt) / 0.8, r = Field.Radius * (1 + t * 1.4);
                var c0 = new Point(Field.CX, Field.CY);
                dc.DrawEllipse(Radial(c0, r, null, (0, Alpha(0xFFFFFFFF, 1 - t)), (0.5, Alpha(g.World.Glow, 0.7 * (1 - t))), (1, Alpha(g.World.Glow, 0))), null, c0, r, r);
            }
            foreach (var t in g.Sentinels.Where(t => !t.Dead && t.Z >= 0)) DrawSentinel(dc, t);
            foreach (var b in g.Blasts) DrawBlast(dc, b);
            foreach (var t in g.Threats) DrawThreat(dc, t);
            foreach (var m in g.Missiles) DrawMissile(dc, m);
            foreach (var d in g.Wreckage)
            {
                double a = Math.Clamp(1 - d.Age / d.Life, 0, 1);
                dc.FillRectangle(Brush(Alpha(d.Colour, a)), new Rect(d.X - d.Size / 2, d.Y - d.Size / 2, d.Size, d.Size));
            }
            foreach (var ship in g.Ships.Where(sh => sh.Alive)) DrawShip(dc, ship.X, ship.Y, Math.Atan2(Field.CY - ship.Y, Field.CX - ship.X), 1, ship.Arriving > 0, ship.Reload > 0);
            if (g.Phase is Phase.Start or Phase.Battle && g.State != GameState.GameOver) DrawSight(dc);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    /// <summary>Stars, some twinkling, and a faint band of distant ones.</summary>
    private void DrawStars(DrawingContext dc)
    {
        for (int i = 0; i < 220; i++)
        {
            uint h = Hash(i, 7, 3);
            double x = h % 1024, y = (h >> 10) % 768;
            double tw = 0.45 + 0.55 * Math.Abs(Math.Sin(_clock * (0.5 + (h >> 20) % 7 * 0.3) + i));
            uint col = (h >> 4) % 5 == 0 ? 0xFFFFD8B0 : (h >> 4) % 5 == 1 ? 0xFFB0C8FF : 0xFFE8ECFF;
            double r = (h >> 8) % 9 == 0 ? 1.6 : 0.9;
            dc.DrawEllipse(Brush(Alpha(col, tw * (r > 1 ? 1 : 0.7))), null, new Point(x, y), r, r);
        }
    }

    // ───────────────────────────── the planet ─────────────────────────────

    /// <summary>
    /// The planet, lit from the upper left: its sea, its land and peaks (drawn smaller towards the rim, where
    /// they're seen edge on), a faint grid of latitude and longitude, its bases, and the haze of its air.
    /// </summary>
    private void DrawPlanet(DrawingContext dc, Globe globe, List<Patch> terrain, WorldPlan w, double s, List<Base> bases)
    {
        double r = Field.Radius * s;
        var c = new Point(Field.CX, Field.CY);
        dc.DrawEllipse(Radial(c, r * 1.16, null, (0.8, Alpha(w.Glow, 0.28)), (0.87, Alpha(w.Glow, 0.12)), (1, Alpha(w.Glow, 0))), null, c, r * 1.16, r * 1.16);
        var lit = new Point(c.X - r * 0.4, c.Y - r * 0.45);
        dc.DrawEllipse(Radial(c, r, lit, (0, Mix(w.Sea, 0xFFFFFFFF, 0.25)), (0.7, w.Sea), (1, Mix(w.Sea, 0xFF000000, 0.5))), null, c, r, r);
        using (dc.PushGeometryClip(new EllipseGeometry(new Rect(c.X - r, c.Y - r, r * 2, r * 2))))
        {
            // Land in one colour, so the patches run together into coasts, then the high ground on top.
            foreach (bool peak in new[] { false, true })
            {
                var brush = Brush(peak ? w.Peak : w.Land);
                foreach (var p in terrain)
                {
                    if (p.Peak != peak && peak) continue;
                    var (x, y, f) = globe.Project(p.At, s);
                    if (f <= -0.05) continue;
                    double size = p.Size * r * (peak ? 0.45 : 1) * (0.3 + 0.7 * Math.Max(0, f));
                    if (size > 0.3) dc.DrawEllipse(brush, null, new Point(x, y), size, size);
                }
            }
            DrawGrid(dc, globe, w, s);
            foreach (var b in bases.Where(b => !b.Dead))
            {
                var (x, y, f) = globe.Project(b.At, s);
                if (f > 0.02) DrawBase(dc, b, x, y, s * (0.6 + 0.7 * f));
            }
            // Night on the lower right, and a lit rim.
            dc.DrawEllipse(Radial(c, r * 1.05, lit, (0.45, 0x00000000), (0.85, 0x70000008), (1, 0xC0000008)), null, c, r, r);
        }
        dc.DrawEllipse(null, Pen(Alpha(w.Glow, 0.55), 2), c, r, r);
    }

    /// <summary>The planet, and once its last base is gone, shaking, glowing hotter and cracking open.</summary>
    private void DrawDoomed(DrawingContext dc, GameEngine g, double s)
    {
        if (g.Phase != Phase.Liberated)
        {
            DrawPlanet(dc, g.Globe, g.Terrain, g.World, s, g.Bases);
            return;
        }
        double t = Math.Min(1, g.PhaseTime / GameEngine.BreakAt), shake = 6 * t;
        double jx = Math.Sin(_clock * 53) * shake, jy = Math.Cos(_clock * 47) * shake;
        using (dc.PushTransform(Matrix.CreateTranslation(jx, jy)))
        {
            DrawPlanet(dc, g.Globe, g.Terrain, g.World, s, g.Bases);
            var c = new Point(Field.CX, Field.CY);
            for (int i = 0; i < 9; i++)
            {
                uint h = Hash(i, g.Wave, 5);
                double a = h % 628 / 100.0, len = Field.Radius * (0.3 + 0.7 * t) * (0.6 + (h >> 10) % 40 / 100.0);
                var p = c;
                for (int k = 1; k <= 4; k++)
                {
                    double bend = ((h >> (k * 3)) % 7 - 3) * 0.12;
                    var q = new Point(c.X + Math.Cos(a + bend) * len * k / 4, c.Y + Math.Sin(a + bend) * len * k / 4);
                    dc.DrawLine(Pen(Alpha(0xFFFFF0C0, t), 2 + 2 * t), p, q);
                    p = q;
                }
            }
            double r = Field.Radius;
            dc.DrawEllipse(Radial(c, r, null, (0, Alpha(0xFFFFFFFF, 0.6 * t * t)), (0.7, Alpha(g.World.Glow, 0.4 * t)), (1, Alpha(g.World.Glow, 0.1))), null, c, r, r);
        }
    }

    /// <summary>Lines of latitude and longitude, on the near side only.</summary>
    private static void DrawGrid(DrawingContext dc, Globe globe, WorldPlan w, double s)
    {
        var pen = Pen(Alpha(w.Glow, 0.16), 1);
        void Line(Func<double, Vec3> at, int steps)
        {
            Point? prev = null;
            for (int i = 0; i <= steps; i++)
            {
                var (x, y, f) = globe.Project(at(i / (double)steps), s);
                if (f <= 0) { prev = null; continue; }
                var p = new Point(x, y);
                if (prev is { } q) dc.DrawLine(pen, q, p);
                prev = p;
            }
        }
        for (int k = 0; k < 12; k++)
        {
            double lon = k * Math.PI / 6;
            Line(t => { double lat = -Math.PI / 2 + t * Math.PI; return new Vec3(Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat), Math.Cos(lat) * Math.Sin(lon)); }, 24);
        }
        for (int k = -2; k <= 2; k++)
        {
            double lat = k * Math.PI / 6;
            Line(t => { double lon = t * Math.PI * 2; return new Vec3(Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat), Math.Cos(lat) * Math.Sin(lon)); }, 48);
        }
    }

    /// <summary>
    /// A base, squashed as it turns away: a silo (a ring with a rocket standing in it), a hangar (a dome with a
    /// lit door) or a citadel (a stepped tower, its lights one for each hit it can still take).
    /// </summary>
    private void DrawBase(DrawingContext dc, Base b, double x, double y, double k)
    {
        bool flash = b.Flash > 0 && (int)(_clock * 20) % 2 == 0;
        uint blink = (int)(_clock * 3 + x) % 2 == 0 ? Red : 0xFF601010;
        // A scorched patch round it, so it stands out from the land.
        dc.DrawEllipse(Brush(0x70000000), null, new Point(x, y + 2 * k), 19 * k, 12 * k);
        switch (b.Kind)
        {
            case BaseKind.Silo:
                dc.DrawEllipse(Brush(0xFF303848), Pen(0xFF90A0B8, 1.5 * k), new Point(x, y + 4 * k), 13 * k, 7 * k);
                Poly(dc, 0xFFE0E4F0, 0xFF505868, 1, new Point(x, y - 14 * k), new Point(x + 3.5 * k, y - 6 * k), new Point(x + 3.5 * k, y + 4 * k), new Point(x - 3.5 * k, y + 4 * k), new Point(x - 3.5 * k, y - 6 * k));
                dc.DrawEllipse(Brush(Red), null, new Point(x, y - 9 * k), 1.6 * k, 1.6 * k);
                break;
            case BaseKind.Hangar:
                dc.DrawEllipse(Brush(0xFF586078), Pen(0xFFB0B8D0, 1.5 * k), new Point(x, y), 15 * k, 10 * k);
                dc.FillRectangle(Brush(0xFF586078), new Rect(x - 15 * k, y, 30 * k, 6 * k));
                dc.FillRectangle(Brush((int)(_clock * 2) % 2 == 0 ? 0xFFFF8AE8 : 0xFF8A2A70), new Rect(x - 6 * k, y - 2 * k, 12 * k, 8 * k));
                dc.DrawEllipse(Brush(blink), null, new Point(x, y - 10 * k), 1.8 * k, 1.8 * k);
                break;
            case BaseKind.Citadel:
                uint wall = flash ? 0xFFFFFFFF : 0xFF7A6A8A;
                dc.FillRectangle(Brush(wall), new Rect(x - 18 * k, y - 2 * k, 36 * k, 10 * k));
                dc.FillRectangle(Brush(flash ? 0xFFFFFFFF : 0xFF9A88AA), new Rect(x - 12 * k, y - 12 * k, 24 * k, 10 * k));
                dc.FillRectangle(Brush(flash ? 0xFFFFFFFF : 0xFFB8A8C8), new Rect(x - 5 * k, y - 24 * k, 10 * k, 12 * k));
                for (int i = 0; i < b.Hp; i++) dc.DrawEllipse(Brush(Yellow), null, new Point(x - 12 * k + i * 8 * k, y + 3 * k), 2 * k, 2 * k);
                dc.DrawEllipse(Brush(blink), null, new Point(x, y - 26 * k), 2.2 * k, 2.2 * k);
                break;
        }
    }

    // ───────────────────────────── in the sky ─────────────────────────────

    /// <summary>A sentinel: a body with two wide panels and a winking light, turning as it goes round.</summary>
    private void DrawSentinel(DrawingContext dc, Sentinel t)
    {
        double k = 0.75 + 0.25 * t.Z, spin = _clock * 1.5 + t.Lean * 4;
        double cx = Math.Cos(spin) * 22 * k, cy = Math.Sin(spin) * 6 * k;
        uint dim = t.Z < 0 ? 0xFF607088 : 0xFFC0D8FF;
        dc.DrawLine(Pen(dim, 2), new Point(t.X - cx, t.Y - cy), new Point(t.X + cx, t.Y + cy));
        foreach (int side in new[] { -1, 1 })
        {
            var p = new Point(t.X + side * cx, t.Y + side * cy);
            dc.FillRectangle(Brush(t.Z < 0 ? 0xFF203050 : 0xFF3A6AC0), new Rect(p.X - 8 * k, p.Y - 5 * k, 16 * k, 10 * k));
            dc.DrawRectangle(null, Pen(dim, 1), new Rect(p.X - 8 * k, p.Y - 5 * k, 16 * k, 10 * k));
        }
        dc.DrawEllipse(Brush(dim), Pen(0xFF404858, 1), new Point(t.X, t.Y), 8 * k, 8 * k);
        if ((int)(_clock * 4 + t.Angle) % 3 == 0) dc.DrawEllipse(Brush(Red), null, new Point(t.X, t.Y - 3 * k), 2.4 * k, 2.4 * k);
    }

    private static void DrawBlast(DrawingContext dc, Blast b)
    {
        double r = b.Radius;
        if (r < 1) return;
        double fade = b.Age < 0.3 ? 1 : Math.Max(0, 1 - (b.Age - 0.3) / (Blast.Life - 0.3));
        var c = new Point(b.X, b.Y);
        dc.DrawEllipse(Radial(c, r, null, (0, Alpha(0xFFFFFFFF, fade)), (0.35, Alpha(0xFFFFF0A0, fade)), (0.75, Alpha(0xFFFF9A3A, 0.85 * fade)), (1, Alpha(0xFFFF4A1A, 0))), null, c, r, r);
    }

    /// <summary>A rocket with its trail back to where it rose from, a raider's spinning saucer, or a pellet.</summary>
    private void DrawThreat(DrawingContext dc, Threat t)
    {
        var at = new Point(t.X, t.Y);
        switch (t.Kind)
        {
            case ThreatKind.Rocket:
            {
                double dx = t.X - t.Ox, dy = t.Y - t.Oy, len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 1)
                {
                    double back = Math.Min(len, 140);
                    var tail = new Point(t.X - dx / len * back, t.Y - dy / len * back);
                    dc.DrawLine(Pen(Alpha(Rocket, 0.25), 3), tail, at);
                    dc.DrawLine(Pen(Alpha(0xFFFFC08A, 0.6), 1.2), new Point(t.X - dx / len * back * 0.4, t.Y - dy / len * back * 0.4), at);
                }
                dc.DrawEllipse(Brush(0xFFFFF0D0), Pen(Rocket, 2), at, 4, 4);
                break;
            }
            case ThreatKind.Raider:
            {
                double a = Math.Atan2(t.Vy, t.Vx);
                dc.DrawEllipse(Brush(Alpha(RaiderPink, 0.25)), null, new Point(t.X - Math.Cos(a) * 12, t.Y - Math.Sin(a) * 12), 7, 7);
                dc.DrawEllipse(Brush(0xFF8A1A70), Pen(RaiderPink, 2), at, 15, 7);
                dc.DrawEllipse(Brush(0xFFFFB0F0), null, new Point(t.X, t.Y - 3), 6, 5);
                for (int i = 0; i < 3; i++)
                {
                    double l = _clock * 6 + i * 2.1;
                    dc.DrawEllipse(Brush(Yellow), null, new Point(t.X + Math.Cos(l) * 11, t.Y + 2), 1.6, 1.6);
                }
                break;
            }
            default:
                dc.DrawEllipse(Brush(Alpha(Pellet, 0.3)), null, at, 8, 8);
                dc.DrawEllipse(Brush(Pellet), null, at, 3.5, 3.5);
                break;
        }
    }

    private static void DrawMissile(DrawingContext dc, Missile m)
    {
        var head = new Point(m.X, m.Y);
        dc.DrawLine(Pen(Alpha(Cyan, 0.18), 3), new Point(m.Ox, m.Oy), head);
        double d = Math.Max(1, Math.Sqrt(m.Vx * m.Vx + m.Vy * m.Vy));
        dc.DrawLine(Pen(Alpha(0xFFFFFFFF, 0.8), 2), new Point(m.X - m.Vx / d * 26, m.Y - m.Vy / d * 26), head);
        dc.DrawEllipse(Brush(0xFFFFFFFF), null, head, 3, 3);
        // Where it will burst.
        dc.DrawLine(Pen(Alpha(Cyan, 0.6), 1.5), new Point(m.Tx - 5, m.Ty - 5), new Point(m.Tx + 5, m.Ty + 5));
        dc.DrawLine(Pen(Alpha(Cyan, 0.6), 1.5), new Point(m.Tx - 5, m.Ty + 5), new Point(m.Tx + 5, m.Ty - 5));
    }

    /// <summary>One of the fleet: a long arrowhead pointed at the planet, its engines lit (bright while it flies in).</summary>
    private void DrawShip(DrawingContext dc, double x, double y, double a, double k, bool arriving, bool firing)
    {
        Point At(double fx, double fy) => new(x + (Math.Cos(a) * fx - Math.Sin(a) * fy) * k, y + (Math.Sin(a) * fx + Math.Cos(a) * fy) * k);
        double flame = (arriving ? 22 : 9) + Math.Sin(_clock * 30) * 3;
        Poly(dc, Alpha(0xFF6AB0FF, 0.7), null, 0, At(-18, -6), At(-18 - flame, 0), At(-18, 6));
        Poly(dc, 0xFFFFFFFF, null, 0, At(-18, -3), At(-18 - flame * 0.5, 0), At(-18, 3));
        Poly(dc, 0xFF2A4A80, Hull, 1.6, At(-20, -16), At(-10, -14), At(-12, 14), At(-20, 16), At(-14, 0));
        Poly(dc, 0xFFD8E8FF, 0xFF4A6AA0, 1.4, At(28, 0), At(-14, -9), At(-20, 0), At(-14, 9));
        dc.DrawEllipse(Brush(firing ? 0xFFFFE08A : 0xFF6AE0FF), null, At(6, 0), 3.6 * k, 3.6 * k);
    }

    /// <summary>The sight: a ring with four ticks, and a diamond in the middle when it's over the planet.</summary>
    private void DrawSight(DrawingContext dc)
    {
        var g = Engine;
        var c = new Point(g.SightX, g.SightY);
        bool ground = g.Globe.Under(c.X, c.Y, g.PlanetScale) != null;
        uint col = ground ? Yellow : Sight;
        dc.DrawEllipse(null, Pen(col, 2), c, 14, 14);
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            dc.DrawLine(Pen(col, 2), new Point(c.X + dx * 9, c.Y + dy * 9), new Point(c.X + dx * 21, c.Y + dy * 21));
        if (ground) Poly(dc, 0, col, 1.5, new Point(c.X, c.Y - 4), new Point(c.X + 4, c.Y), new Point(c.X, c.Y + 4), new Point(c.X - 4, c.Y));
        else dc.DrawEllipse(Brush(col), null, c, 1.6, 1.6);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 34 * e.Size, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 46), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 46), 30, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"PLANET {g.Wave}: {g.World.Name}", new Point(512, 18), 28, g.World.Glow, center: true);
        // A pip for each base: lit while it stands.
        int n = g.Bases.Count;
        for (int i = 0; i < n; i++)
        {
            var b = g.Bases[i];
            double x = 512 - (n - 1) * 9 + i * 18;
            uint col = b.Kind switch { BaseKind.Citadel => 0xFFB8A8C8, BaseKind.Hangar => RaiderPink, _ => Rocket };
            if (b.Dead) dc.DrawEllipse(null, Pen(Alpha(col, 0.35), 1.2), new Point(x, 52), 5, 5);
            else dc.DrawEllipse(Brush(col), null, new Point(x, 52), b.Kind == BaseKind.Citadel ? 7 : 5, b.Kind == BaseKind.Citadel ? 7 : 5);
        }
        Text(dc, $"BASES {g.BasesLeft}", new Point(512, 74), 20, Dim, center: true);
        // The reserve: ships waiting to fly in.
        for (int i = 0; i < Math.Min(g.Reserve, 6); i++) DrawShip(dc, 210 + i * 34, 40, -Math.PI / 2, 0.55, false, false);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = g.Phase == Phase.Liberated ? 420 : 150;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xC0080A20), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 16 ? 42 : 54, g.Phase == Phase.Fallen ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private readonly Globe _titleGlobe = new();
    private List<Patch>? _titleTerrain;

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        // A small planet turning behind the title, with the fleet round it.
        _titleTerrain ??= TitleTerrain();
        _titleGlobe.Turn(0.004, 0.0006);
        using (dc.PushTransform(Matrix.CreateTranslation(-Field.CX, -Field.CY) * Matrix.CreateScale(0.5, 0.5) * Matrix.CreateTranslation(cx, 312)))
            DrawPlanet(dc, _titleGlobe, _titleTerrain, Worlds.Plans[0], 1, []);
        dc.FillRectangle(Brush(0x70020208), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "AVA LIBERATOR", new Point(cx, 110), 112, 0xFF8AD0FF, center: true);
        Text(dc, "AFTER THE 1982 ARCADE CLASSIC", new Point(cx, 178), 28, Dim, center: true);
        for (int i = 0; i < 4; i++)
        {
            var (bx, by) = Field.Berths[i];
            double x = cx + Math.Sign(bx - Field.CX) * 215, y = 312 + Math.Sign(by - Field.CY) * 80;
            DrawShip(dc, x, y, Math.Atan2(312 - y, cx - x), 1.1, false, false);
        }
        Text(dc, "FOUR SHIPS, ONE SIGHT: TURN THE PLANET AND BOMB EVERY BASE ON IT", new Point(cx, 456), 24, White, center: true);
        Text(dc, "BLAST THE ROCKETS AND RAIDERS BEFORE THEY REACH YOUR SHIPS", new Point(cx, 484), 24, White, center: true);
        (string Name, int Points, Action<double> Draw)[] legend =
        [
            ("SILO", 250, x => DrawBase(dc, new Base { Kind = BaseKind.Silo }, x, 534, 1.3)),
            ("HANGAR", 400, x => DrawBase(dc, new Base { Kind = BaseKind.Hangar }, x, 534, 1.3)),
            ("CITADEL", 1000, x => DrawBase(dc, new Base { Kind = BaseKind.Citadel, Hp = 3 }, x, 540, 1.1)),
            ("ROCKET", 50, x => DrawThreat(dc, new Threat { Kind = ThreatKind.Rocket, X = x + 6, Y = 528, Ox = x - 34, Oy = 554 })),
            ("RAIDER", 150, x => DrawThreat(dc, new Threat { Kind = ThreatKind.Raider, X = x, Y = 532, Vx = 1 })),
            ("SENTINEL", 500, x => DrawSentinel(dc, new Sentinel { X = x, Y = 532, Z = 1 })),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double x = cx - 375 + i * 150;
            legend[i].Draw(x);
            Text(dc, legend[i].Name, new Point(x, 568), 22, Dim, center: true);
            Text(dc, legend[i].Points.ToString(), new Point(x, 590), 22, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 638), 44, Yellow, center: true);
        Text(dc, "MOUSE: AIM, LEFT BUTTON FIRES, RIGHT-DRAG TURNS THE PLANET", new Point(cx, 680), 23, Dim, center: true);
        Text(dc, "KEYS: ARROWS AIM, SHIFT+ARROWS TURN, SPACE FIRES    P: PAUSE    M: SOUND", new Point(cx, 705), 23, Dim, center: true);
        Text(dc, $"NEW SHIP AT {GameEngine.FirstExtraShip} AND EVERY {GameEngine.ExtraShipEvery}", new Point(cx, 732), 22, Dim, center: true);
    }

    private static List<Patch> TitleTerrain()
    {
        var rng = new Random(5);
        var list = new List<Patch>();
        var centres = Enumerable.Range(0, 6).Select(_ => new Vec3(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1).Normal).ToList();
        while (list.Count < 400)
        {
            var v = new Vec3(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1).Normal;
            double best = centres.Max(c => 1 - v.AngleTo(c) / 0.6);
            if (best > 0 && rng.NextDouble() < best + 0.15) list.Add(new Patch(v, 0.05 + rng.NextDouble() * 0.06, best > 0.6 && rng.NextDouble() < 0.5));
        }
        return list;
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "FLEET COMMANDERS", new Point(cx, 140), 68, 0xFF8AD0FF, center: true);
        Text(dc, "RANK NAME    SCORE  PLANET", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
