namespace AVALiberator.Engine;

/// <summary>
/// The controls: the sight's movement (or, with <see cref="AimX"/>/<see cref="AimY"/>, the point it heads for,
/// as the mouse gives), the planet's spin, and the fire button.
/// </summary>
public readonly record struct Controls(double Dx = 0, double Dy = 0, bool Fire = false, double SpinX = 0, double SpinY = 0, double? AimX = null, double? AimY = null);

/// <summary>The screen, in pixels, with the planet in the middle of it and the fleet at the corners.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Top = 84;
    public const double CX = 512, CY = 420, Radius = 236;

    /// <summary>Where the sight may go.</summary>
    public const double SightLeft = 16, SightRight = Width - 16, SightTop = Top + 8, SightBottom = Height - 16;

    public static readonly (double X, double Y)[] Berths = [(78, 160), (946, 160), (78, 706), (946, 706)];
}

/// <summary>A point or direction in three dimensions.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public double Length => Math.Sqrt(Dot(this));
    public Vec3 Normal => this * (1 / Math.Max(1e-9, Length));
    public Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);

    /// <summary>The angle between two unit vectors.</summary>
    public double AngleTo(Vec3 b) => Math.Acos(Math.Clamp(Dot(b), -1, 1));
}

/// <summary>
/// Which way the planet is turned: a rotation from the planet's own frame (where its bases sit still) to the
/// view, where X is to the right, Y up and Z out of the screen towards the player.
/// </summary>
public sealed class Globe
{
    private Vec3 _r0 = new(1, 0, 0), _r1 = new(0, 1, 0), _r2 = new(0, 0, 1);

    public Vec3 ToView(Vec3 p) => new(_r0.Dot(p), _r1.Dot(p), _r2.Dot(p));
    public Vec3 ToBody(Vec3 v) => _r0 * v.X + _r1 * v.Y + _r2 * v.Z;

    /// <summary>Turns it: <paramref name="right"/> carries the near face to the right, <paramref name="down"/> down (radians).</summary>
    public void Turn(double right, double down)
    {
        if (right != 0)
        {
            double c = Math.Cos(right), s = Math.Sin(right);
            (_r0, _r2) = (_r0 * c + _r2 * s, _r2 * c - _r0 * s);
        }
        if (down != 0)
        {
            double c = Math.Cos(down), s = Math.Sin(down);
            (_r1, _r2) = (_r1 * c - _r2 * s, _r2 * c + _r1 * s);
        }
        // Keep it a true rotation as the small errors add up.
        _r0 = _r0.Normal;
        _r1 = (_r1 - _r0 * _r0.Dot(_r1)).Normal;
        _r2 = _r0.Cross(_r1);
    }

    public Globe Copy() => new() { _r0 = _r0, _r1 = _r1, _r2 = _r2 };

    /// <summary>Where a point on the surface shows on screen, at this size, and how squarely it faces the player.</summary>
    public (double X, double Y, double Facing) Project(Vec3 body, double scale = 1)
    {
        var v = ToView(body);
        return (Field.CX + v.X * Field.Radius * scale, Field.CY - v.Y * Field.Radius * scale, v.Z);
    }

    /// <summary>The point on the near face under a point on the screen, if the screen point is over the planet.</summary>
    public Vec3? Under(double x, double y, double scale = 1)
    {
        double vx = (x - Field.CX) / (Field.Radius * scale), vy = -(y - Field.CY) / (Field.Radius * scale), d = vx * vx + vy * vy;
        if (d >= 1) return null;
        return ToBody(new Vec3(vx, vy, Math.Sqrt(1 - d)));
    }
}

/// <summary>One of the occupied worlds: its name and colours.</summary>
public sealed record WorldPlan(string Name, uint Sea, uint Land, uint Peak, uint Glow);

public static class Worlds
{
    public static readonly WorldPlan[] Plans =
    [
        new("DREVAN", 0xFF143A6A, 0xFF3A7A3A, 0xFF8AB06A, 0xFF6AB0FF),
        new("MORROTH", 0xFF4A1A10, 0xFF9A4A1A, 0xFFE0A050, 0xFFFF8A4A),
        new("QUELLIS", 0xFF0A3A40, 0xFF2A8A80, 0xFFB0F0E0, 0xFF6AFFE0),
        new("SARNEK", 0xFF2A1A4A, 0xFF6A3A9A, 0xFFC09AF0, 0xFFB08AFF),
        new("VEKKOR", 0xFF3A3A1A, 0xFF8A8A3A, 0xFFE8E0A0, 0xFFFFF08A),
        new("THULON", 0xFF102030, 0xFF5A6A80, 0xFFE0E8F8, 0xFFC0E0FF),
        new("ASHGAR", 0xFF300A0A, 0xFF7A1A1A, 0xFFFF6A3A, 0xFFFF4A2A),
        new("ZEMRA", 0xFF0A2A1A, 0xFF4A9A2A, 0xFFE0FF6A, 0xFFA0FF6A),
    ];

    public static WorldPlan For(int wave) => Plans[(wave - 1) % Plans.Length];
}

/// <summary>A patch of high ground, for drawing: where it is on the planet, and how big.</summary>
public readonly record struct Patch(Vec3 At, double Size, bool Peak);

public enum BaseKind { Silo, Hangar, Citadel }

/// <summary>An enemy base on the planet's surface, sitting still in the planet's own frame.</summary>
public sealed class Base
{
    public BaseKind Kind;
    public Vec3 At;
    public int Hp = 1;
    public double FireIn, Flash;
    public bool Dead;

    public int Points => Kind switch { BaseKind.Citadel => 1000, BaseKind.Hangar => 400, _ => 250 };
}

/// <summary>One of the fleet's four ships, holding its corner.</summary>
public sealed class Ship
{
    public int Index;
    public bool Alive = true;
    public double Reload, Bob;
    /// <summary>A replacement on its way in: how long until it's in place.</summary>
    public double Arriving;
    public double HomeX => Field.Berths[Index].X;
    public double HomeY => Field.Berths[Index].Y;
    public double X => HomeX + (HomeX < Field.CX ? -1 : 1) * Arriving * 140;
    public double Y => HomeY + Math.Sin(Bob) * 4 + (HomeY < Field.CY ? -1 : 1) * Arriving * 90;
    public bool Ready => Alive && Arriving <= 0;
}

/// <summary>A missile from the fleet, flying to the point the sight was on when it was fired.</summary>
public sealed class Missile
{
    public double X, Y, Tx, Ty, Vx, Vy, Ox, Oy;
    public int From;
    public bool Dead;
}

/// <summary>A missile's blast, growing then dying away. One on the planet turns with it.</summary>
public sealed class Blast
{
    public double X, Y, Age;
    public Vec3? Ground;
    public readonly HashSet<Base> Struck = [];
    public const double Life = 0.9, Peak = 42;

    public double Radius => Age < 0.3 ? Peak * Age / 0.3 : Peak * Math.Max(0, 1 - (Age - 0.3) / (Life - 0.3));
    public bool Done => Age >= Life;
}

public enum ThreatKind { Rocket, Raider, Pellet }

/// <summary>
/// Something coming for the fleet: a rocket from a silo (straight at a ship), a raider from a hangar (swinging
/// round to dive on one), or a sentinel's pellet.
/// </summary>
public sealed class Threat
{
    public ThreatKind Kind;
    public double X, Y, Vx, Vy, Ox, Oy, Speed, T;
    public int Target;
    public bool Dead;

    public double Radius => Kind switch { ThreatKind.Raider => 14, ThreatKind.Pellet => 5, _ => 7 };
    public int Points => Kind switch { ThreatKind.Raider => 150, ThreatKind.Pellet => 20, _ => 50 };
}

/// <summary>
/// A sentinel in orbit round the planet: out of reach while it's behind, and firing at the fleet while it's in
/// front.
/// </summary>
public sealed class Sentinel
{
    public double Angle, Tilt, Lean, Speed, FireIn;
    public double X, Y, Z;
    public bool Dead;
    public const double Orbit = 1.3;

    /// <summary>Behind the planet, where it can't be seen or hit.</summary>
    public bool Hidden => Z < 0 && Math.Sqrt((X - Field.CX) * (X - Field.CX) + (Y - Field.CY) * (Y - Field.CY)) < Field.Radius;

    public void Place(double scale = 1)
    {
        double ox = Math.Cos(Angle), oy = Math.Sin(Angle) * Math.Sin(Tilt), oz = Math.Sin(Angle) * Math.Cos(Tilt);
        double cl = Math.Cos(Lean), sl = Math.Sin(Lean);
        double vx = ox * cl - oy * sl, vy = ox * sl + oy * cl;
        X = Field.CX + vx * Field.Radius * Orbit * scale;
        Y = Field.CY - vy * Field.Radius * Orbit * scale;
        Z = oz;
    }
}

/// <summary>A fleck of something blown up, flying out.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Age, Life = 1, Size = 4;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
