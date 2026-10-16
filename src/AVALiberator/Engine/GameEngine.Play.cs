namespace AVALiberator.Engine;

public sealed partial class GameEngine
{
    public const double SightSpeed = 560, AimSpeed = 1500, SpinRate = 1.5, MissileSpeed = 720;
    public const double ShipReload = 0.45, FireGap = 0.12, ShipReach = 22;
    public const int MaxMissiles = 6, PerShip = 2;
    public const double StartSeconds = 2.6, FallenSeconds = 3, BreakAt = 1.4, LiberatedSeconds = 5.5, ArriveSeconds = 2;
    public const int FirstExtraShip = 10000, ExtraShipEvery = 20000;

    /// <summary>The planet being fought for: 1 for the first.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    /// <summary>Ships in the fight and in reserve: the game ends when the last one goes.</summary>
    public int Lives => Ships.Count(s => s.Alive) + Reserve;
    /// <summary>Replacement ships waiting to fly in when one of the four is lost.</summary>
    public int Reserve { get; private set; }
    public WorldPlan World { get; private set; } = Worlds.For(1);
    public Globe Globe { get; private set; } = new();
    public List<Patch> Terrain { get; } = [];
    public List<Base> Bases { get; } = [];
    public List<Ship> Ships { get; } = [];
    public List<Missile> Missiles { get; } = [];
    public List<Blast> Blasts { get; } = [];
    public List<Threat> Threats { get; } = [];
    public List<Sentinel> Sentinels { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public double SightX { get; private set; } = Field.CX;
    public double SightY { get; private set; } = Field.CY;
    /// <summary>The planet's own slow turning, right and down, in radians a second.</summary>
    public (double Right, double Down) Drift { get; private set; }
    public int Bonus { get; private set; }
    public string Cause { get; private set; } = "";
    /// <summary>For tests: the bases and sentinels hold their fire, and the planet keeps still.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private double _fireGap, _spinSound, _volley;

    public int BasesLeft => Bases.Count(b => !b.Dead);

    /// <summary>How big the planet shows: it swells into view as the fleet closes in.</summary>
    public double PlanetScale => Phase switch
    {
        Phase.Start => 0.15 + 0.85 * Ease(Math.Min(1, PhaseTime / (StartSeconds * 0.8))),
        Phase.Liberated when PhaseTime >= BreakAt => 0,
        _ => 1,
    };

    private static double Ease(double t) => 1 - (1 - t) * (1 - t) * (1 - t);

    public double RocketSpeed => Math.Min(140, 114 + 4 * (Wave - 1));
    public double RaiderSpeed => Math.Min(210, 125 + 9 * (Wave - 1));
    public double SiloDelay => Math.Max(3.2, 4.5 - 0.25 * (Wave - 1));
    public double HangarDelay => Math.Max(6, 11 - 0.6 * (Wave - 1));
    public double CitadelDelay => Math.Max(5, 9 - 0.4 * (Wave - 1));
    public double PelletSpeed => Math.Min(190, 140 + 5 * (Wave - 1));
    /// <summary>However many silos face the fleet, no more than one rocket goes up in this long.</summary>
    public double Volley => Math.Max(0.6, 0.9 - 0.05 * (Wave - 1));
    public int MaxRockets => Math.Min(7, 5 + Wave / 4);
    public int MaxRaiders => Math.Min(4, 1 + Wave / 2);

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Wave = 1;
        Reserve = 2;
        _nextExtra = FirstExtraShip;
        Ships.Clear();
        for (int i = 0; i < 4; i++) Ships.Add(new Ship { Index = i, Bob = i * 1.7 });
        ClearSky();
        Wreckage.Clear();
        SetPhase(Phase.Start);
    }

    private void ClearSky()
    {
        Missiles.Clear();
        Blasts.Clear();
        Threats.Clear();
        Sentinels.Clear();
    }

    /// <summary>On to a planet: its bases scattered over it, its sentinels in orbit, turning its own way.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        World = Worlds.For(wave);
        var rng = new Random(_seed * 31 + wave * 7919);
        Globe = new Globe();
        Globe.Turn(rng.NextDouble() * Math.PI * 2, (rng.NextDouble() - 0.5) * 0.6);
        MakeTerrain(rng);
        Bases.Clear();
        int citadels = wave >= 8 ? 2 : wave >= 3 ? 1 : 0, hangars = Math.Min(4, 1 + wave / 3);
        int total = Math.Min(20, 9 + wave);
        var spots = new List<Vec3>();
        while (spots.Count < total)
        {
            var v = RandomDirection(rng);
            if (Math.Abs(v.Y) > 0.75 || spots.Any(s => s.AngleTo(v) < 0.42)) continue;
            spots.Add(v);
        }
        for (int i = 0; i < total; i++)
        {
            var kind = i < citadels ? BaseKind.Citadel : i < citadels + hangars ? BaseKind.Hangar : BaseKind.Silo;
            Bases.Add(new Base
            {
                Kind = kind, At = spots[i], Hp = kind == BaseKind.Citadel ? (wave >= 8 ? 4 : 3) : 1,
                FireIn = 0.5 + rng.NextDouble() * (kind == BaseKind.Silo ? SiloDelay : HangarDelay) * 0.6,
            });
        }
        ClearSky();
        _volley = 0;
        int sentinels = Math.Min(3, (wave + 1) / 3);
        for (int i = 0; i < sentinels; i++)
            Sentinels.Add(new Sentinel
            {
                Angle = i * Math.PI * 2 / Math.Max(1, sentinels), Tilt = 0.35 + 0.25 * i, Lean = -0.4 + 0.5 * i,
                Speed = (0.32 + 0.03 * wave) * (i % 2 == 0 ? 1 : -1), FireIn = 3 + i,
            });
        foreach (var s in Sentinels) s.Place();
        double dir = wave % 2 == 1 ? 1 : -1;
        Drift = (dir * Math.Min(0.34, 0.16 + 0.02 * (wave - 1)), 0.035 * -dir);
        SightX = Field.CX;
        SightY = Field.CY;
        foreach (var s in Ships) s.Reload = 0;
        SetPhase(Phase.Start);
        Say($"PLANET {World.Name}", $"{total} BASES TO DESTROY", StartSeconds + 0.4);
        Play(Sfx.PlanetStart);
    }

    private static Vec3 RandomDirection(Random rng)
    {
        while (true)
        {
            var v = new Vec3(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
            double l = v.Length;
            if (l > 0.1 && l <= 1) return v * (1 / l);
        }
    }

    /// <summary>Continents: a few centres, with patches of land scattered round each, and peaks near the middles.</summary>
    private void MakeTerrain(Random rng)
    {
        Terrain.Clear();
        var centres = Enumerable.Range(0, 7).Select(_ => (At: RandomDirection(rng), Size: 0.35 + rng.NextDouble() * 0.4)).ToList();
        int tries = 0;
        while (Terrain.Count < 520 && tries++ < 20000)
        {
            var v = RandomDirection(rng);
            double best = centres.Max(c => 1 - v.AngleTo(c.At) / c.Size);
            if (best <= 0 || rng.NextDouble() > best + 0.15) continue;
            Terrain.Add(new Patch(v, 0.05 + rng.NextDouble() * 0.06, best > 0.62 && rng.NextDouble() < 0.5));
        }
    }

    /// <summary>One step of the fight.</summary>
    private void Simulate(double dt, Controls c)
    {
        foreach (var s in Ships) s.Bob += dt * 2;
        MoveDebris(dt);
        switch (Phase)
        {
            case Phase.Start:
                Sight(dt, c);
                if (PhaseTime >= StartSeconds)
                {
                    SetPhase(Phase.Battle);
                    Music(true);
                }
                break;
            case Phase.Battle:
                Sight(dt, c);
                Spin(dt, c);
                Fire(dt, c);
                Fly(dt);
                Bombard(dt);
                Strike();
                Arrivals(dt);
                if (BasesLeft == 0) Liberate();
                else if (Lives == 0) Fall();
                break;
            case Phase.Liberated:
                Fly(dt);
                Strike();
                Arrivals(dt);
                if (PhaseTime >= BreakAt && PhaseTime - dt < BreakAt) Break();
                if (PhaseTime >= BreakAt + 0.9 && PhaseTime - dt < BreakAt + 0.9)
                {
                    Score += Bonus;
                    Say($"{World.Name} LIBERATED", $"BONUS {Bonus}", 3);
                    Play(Sfx.Liberated);
                    CheckExtra();
                }
                if (PhaseTime >= LiberatedSeconds) BeginWave(Wave + 1);
                break;
            case Phase.Fallen:
                Fly(dt);
                if (PhaseTime >= FallenSeconds) GameOver();
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the fleet ─────────────────────────────

    private void Sight(double dt, Controls c)
    {
        double x = SightX, y = SightY;
        if (c.AimX is { } ax && c.AimY is { } ay)
        {
            double dx = ax - x, dy = ay - y, d = Math.Sqrt(dx * dx + dy * dy), step = AimSpeed * dt;
            if (d <= step) (x, y) = (ax, ay);
            else (x, y) = (x + dx / d * step, y + dy / d * step);
        }
        else
        {
            x += Math.Clamp(c.Dx, -1, 1) * SightSpeed * dt;
            y += Math.Clamp(c.Dy, -1, 1) * SightSpeed * dt;
        }
        SightX = Math.Clamp(x, Field.SightLeft, Field.SightRight);
        SightY = Math.Clamp(y, Field.SightTop, Field.SightBottom);
    }

    /// <summary>The planet turns on its own, and the player can turn it faster, or back.</summary>
    private void Spin(double dt, Controls c)
    {
        double right = Math.Clamp(c.SpinX, -3, 3) * SpinRate, down = Math.Clamp(c.SpinY, -3, 3) * SpinRate;
        if (!Calm) { right += Drift.Right; down += Drift.Down; }
        Globe.Turn(right * dt, down * dt);
        if (Math.Abs(c.SpinX) + Math.Abs(c.SpinY) > 0.3 && (_spinSound -= dt) <= 0)
        {
            _spinSound = 0.35;
            Play(Sfx.Spin);
        }
    }

    /// <summary>A missile at the sight, from the nearest ship that's ready.</summary>
    private void Fire(double dt, Controls c)
    {
        foreach (var s in Ships) s.Reload -= dt;
        _fireGap -= dt;
        if (!c.Fire || _fireGap > 0 || Missiles.Count >= MaxMissiles) return;
        var ship = Ships.Where(s => s.Ready && s.Reload <= 0 && Missiles.Count(m => m.From == s.Index) < PerShip)
            .OrderBy(s => Dist(s.X, s.Y, SightX, SightY)).FirstOrDefault();
        if (ship == null) return;
        double d = Math.Max(1, Dist(ship.X, ship.Y, SightX, SightY));
        Missiles.Add(new Missile
        {
            X = ship.X, Y = ship.Y, Ox = ship.X, Oy = ship.Y, Tx = SightX, Ty = SightY, From = ship.Index,
            Vx = (SightX - ship.X) / d * MissileSpeed, Vy = (SightY - ship.Y) / d * MissileSpeed,
        });
        ship.Reload = ShipReload;
        _fireGap = FireGap;
        Play(Sfx.Launch);
    }

    private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>Missiles to their marks, blasts growing, and the enemy's rockets, raiders and pellets on their way.</summary>
    private void Fly(double dt)
    {
        foreach (var m in Missiles)
        {
            double left = Dist(m.X, m.Y, m.Tx, m.Ty);
            if (left <= MissileSpeed * dt)
            {
                m.Dead = true;
                Detonate(m.Tx, m.Ty);
            }
            else { m.X += m.Vx * dt; m.Y += m.Vy * dt; }
        }
        Missiles.RemoveAll(m => m.Dead);
        foreach (var b in Blasts)
        {
            b.Age += dt;
            if (b.Ground is { } at)
            {
                var (x, y, _) = Globe.Project(at, PlanetScale);
                (b.X, b.Y) = (x, y);
            }
        }
        Blasts.RemoveAll(b => b.Done);
        foreach (var t in Threats) Steer(t, dt);
        Threats.RemoveAll(t => t.Dead);
        foreach (var s in Sentinels.Where(s => !s.Dead))
        {
            s.Angle += s.Speed * dt;
            s.Place(PlanetScale);
        }
    }

    /// <summary>A blast where the missile stopped: on the planet's face if it's over it, so it turns with it.</summary>
    private void Detonate(double x, double y)
    {
        var ground = Phase == Phase.Battle ? Globe.Under(x, y, PlanetScale) : null;
        Blasts.Add(new Blast { X = x, Y = y, Ground = ground });
        Play(Sfx.Blast);
    }

    private void Steer(Threat t, double dt)
    {
        t.T += dt;
        var ship = Ships[t.Target];
        if (!ship.Alive)
        {
            // Its ship is gone: on to another, or away.
            var other = Ships.Where(s => s.Alive).OrderBy(s => Dist(s.X, s.Y, t.X, t.Y)).FirstOrDefault();
            if (other != null) { t.Target = other.Index; ship = other; }
        }
        double dx = ship.X - t.X, dy = ship.Y - t.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        if (t.Kind == ThreatKind.Raider)
        {
            // Out from the hangar first, then a curving dive that closes on the ship.
            if (t.T > 0.8)
            {
                double want = Math.Atan2(dy, dx), have = Math.Atan2(t.Vy, t.Vx);
                double turn = Math.IEEERemainder(want - have, Math.PI * 2);
                have += Math.Clamp(turn, -2.2 * dt, 2.2 * dt) + Math.Sin(t.T * 3) * 0.9 * dt;
                t.Vx = Math.Cos(have) * t.Speed;
                t.Vy = Math.Sin(have) * t.Speed;
            }
        }
        t.X += t.Vx * dt;
        t.Y += t.Vy * dt;
        if (t.X < -60 || t.X > Field.Width + 60 || t.Y < -60 || t.Y > Field.Height + 60) t.Dead = true;
        if (ship.Ready && Dist(t.X, t.Y, ship.X, ship.Y) < ShipReach + t.Radius * 0.5)
        {
            t.Dead = true;
            Lose(ship, t.Kind switch { ThreatKind.Raider => "raider", ThreatKind.Pellet => "sentinel", _ => "rocket" });
        }
    }

    private void Lose(Ship ship, string cause)
    {
        ship.Alive = false;
        Cause = cause;
        Burst(ship.X, ship.Y, 0xFF8AD0FF, 40, 1.6);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = ship.X, Y = ship.Y, Life = 0.8, Size = 2, Colour = 0xFFFFE08A });
        Play(Sfx.ShipLost);
        if (Reserve > 0)
        {
            Reserve--;
            ship.Alive = true;
            ship.Arriving = ArriveSeconds + 1.2;
        }
    }

    private void Arrivals(double dt)
    {
        foreach (var s in Ships.Where(s => s.Alive && s.Arriving > 0))
        {
            bool waiting = s.Arriving > ArriveSeconds;
            s.Arriving = Math.Max(0, s.Arriving - dt);
            if (waiting && s.Arriving <= ArriveSeconds) Play(Sfx.NewShip);
        }
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    /// <summary>Bases on the near face send up rockets and raiders; sentinels in front fire pellets.</summary>
    private void Bombard(double dt)
    {
        if (Calm) return;
        var targets = Ships.Where(s => s.Ready).ToList();
        if (targets.Count == 0) return;
        _volley -= dt;
        foreach (var b in Bases.Where(b => !b.Dead))
        {
            b.Flash = Math.Max(0, b.Flash - dt);
            var (x, y, facing) = Globe.Project(b.At);
            if (facing < 0.3) continue;
            b.FireIn -= dt;
            if (b.FireIn > 0) continue;
            switch (b.Kind)
            {
                case BaseKind.Silo when _volley <= 0 && Threats.Count(t => t.Kind == ThreatKind.Rocket) < MaxRockets:
                    Launch(ThreatKind.Rocket, x, y, Pick(targets, x, y), RocketSpeed);
                    _volley = Volley;
                    b.FireIn = SiloDelay * (0.6 + _rng.NextDouble() * 0.8);
                    Play(Sfx.EnemyLaunch);
                    break;
                case BaseKind.Hangar when Threats.Count(t => t.Kind == ThreatKind.Raider) < MaxRaiders:
                    Launch(ThreatKind.Raider, x, y, Pick(targets, x, y), RaiderSpeed);
                    b.FireIn = HangarDelay * (0.7 + _rng.NextDouble() * 0.6);
                    Play(Sfx.RaiderLaunch);
                    break;
                case BaseKind.Citadel:
                    // A salvo: one at each of two ships, or three from the eighth planet on.
                    foreach (var s in targets.OrderBy(_ => _rng.Next()).Take(Wave >= 8 ? 3 : 2))
                        Launch(ThreatKind.Rocket, x, y, s, RocketSpeed * 0.9);
                    b.FireIn = CitadelDelay;
                    Play(Sfx.EnemyLaunch);
                    break;
                default:
                    b.FireIn = 0.3 + _rng.NextDouble() * 0.7;
                    break;
            }
        }
        foreach (var s in Sentinels.Where(s => !s.Dead && s.Z > 0.2))
        {
            s.FireIn -= dt;
            if (s.FireIn > 0) continue;
            s.FireIn = Math.Max(3, 4.6 - 0.15 * Wave);
            var ship = targets.OrderBy(t => Dist(t.X, t.Y, s.X, s.Y)).First();
            Launch(ThreatKind.Pellet, s.X, s.Y, ship, PelletSpeed);
            Play(Sfx.SentinelShot);
        }
    }

    /// <summary>The ship a base goes for: any of the fleet, the nearer ones more often.</summary>
    private Ship Pick(List<Ship> ships, double x, double y)
    {
        var near = ships.OrderBy(s => Dist(s.X, s.Y, x, y)).ToList();
        return near[Math.Min(near.Count - 1, _rng.Next(3) == 0 ? _rng.Next(near.Count) : 0)];
    }

    private void Launch(ThreatKind kind, double x, double y, Ship ship, double speed)
    {
        double dx = ship.X - x, dy = ship.Y - y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        var t = new Threat { Kind = kind, X = x, Y = y, Ox = x, Oy = y, Target = ship.Index, Speed = speed };
        if (kind == ThreatKind.Raider)
        {
            // Straight up off the surface first.
            double ux = x - Field.CX, uy = y - Field.CY, u = Math.Max(1, Math.Sqrt(ux * ux + uy * uy));
            (t.Vx, t.Vy) = (ux / u * speed, uy / u * speed);
        }
        else (t.Vx, t.Vy) = (dx / d * speed, dy / d * speed);
        Threats.Add(t);
    }

    /// <summary>Blasts against everything: bases under them, and whatever flies into them.</summary>
    private void Strike()
    {
        foreach (var b in Blasts)
        {
            double r = b.Radius;
            if (r <= 0) continue;
            if (b.Ground is { } at && Phase == Phase.Battle)
            {
                foreach (var e in Bases.Where(e => !e.Dead && !b.Struck.Contains(e)))
                {
                    if (e.At.AngleTo(at) * Field.Radius > r + 4) continue;
                    b.Struck.Add(e);
                    HitBase(e);
                }
            }
            foreach (var t in Threats.Where(t => !t.Dead && Dist(t.X, t.Y, b.X, b.Y) < r + t.Radius))
            {
                t.Dead = true;
                Award(t.Points, t.X, t.Y);
                Burst(t.X, t.Y, t.Kind == ThreatKind.Raider ? 0xFFFF6AE0 : 0xFFFFB04A, 10, 0.7);
            }
            foreach (var s in Sentinels.Where(s => !s.Dead && !s.Hidden && Dist(s.X, s.Y, b.X, b.Y) < r + 16))
            {
                s.Dead = true;
                Award(500, s.X, s.Y);
                Burst(s.X, s.Y, 0xFFC0E0FF, 18, 1);
                Play(Sfx.BaseDown);
            }
        }
        Threats.RemoveAll(t => t.Dead);
    }

    private void HitBase(Base e)
    {
        var (x, y, _) = Globe.Project(e.At);
        if (--e.Hp > 0)
        {
            e.Flash = 0.4;
            Play(Sfx.CitadelHit);
            return;
        }
        e.Dead = true;
        Award(e.Points, x, y);
        Burst(x, y, World.Peak, 22, 1.1);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Life = 0.6, Size = 1.4, Colour = 0xFFFFD06A });
        Play(Sfx.BaseDown);
    }

    // ───────────────────────────── a planet won or lost ─────────────────────────────

    /// <summary>The last base gone: everything still flying blows up, and the planet starts to break.</summary>
    private void Liberate()
    {
        foreach (var t in Threats) Burst(t.X, t.Y, 0xFFFFB04A, 6, 0.6);
        Threats.Clear();
        foreach (var s in Sentinels.Where(s => !s.Dead && !s.Hidden)) Burst(s.X, s.Y, 0xFFC0E0FF, 10, 0.8);
        Sentinels.Clear();
        Bonus = 1000 * Wave + 500 * Ships.Count(s => s.Alive);
        Music(false);
        SetPhase(Phase.Liberated);
    }

    private void Break()
    {
        Play(Sfx.PlanetBreak);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = Field.CX, Y = Field.CY, Life = 1.2, Size = 9, Colour = World.Glow });
        for (int i = 0; i < 220; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, r = Math.Sqrt(_rng.NextDouble()) * Field.Radius, v = 60 + _rng.NextDouble() * 320;
            Wreckage.Add(new Debris
            {
                X = Field.CX + Math.Cos(a) * r, Y = Field.CY + Math.Sin(a) * r, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v,
                Life = 1.5 + _rng.NextDouble() * 2, Size = 3 + _rng.NextDouble() * 9,
                Colour = _rng.Next(4) switch { 0 => World.Land, 1 => World.Peak, 2 => World.Sea, _ => World.Glow },
            });
        }
    }

    private void Fall()
    {
        Music(false);
        Cause = Cause.Length > 0 ? Cause : "the fleet";
        SetPhase(Phase.Fallen);
        Say("THE FLEET IS LOST", null, FallenSeconds);
    }

    private void Award(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y - 10, Text = points.ToString(), Life = 0.9, Colour = 0xFFFFE14A });
        CheckExtra();
    }

    /// <summary>A new ship at 10000 and every 15000 after: straight into a gap in the fleet, or into reserve.</summary>
    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraShipEvery;
            Play(Sfx.NewShip);
            var gap = Ships.FirstOrDefault(s => !s.Alive);
            if (gap != null)
            {
                gap.Alive = true;
                gap.Arriving = ArriveSeconds;
            }
            else Reserve++;
            Effects.Add(new Effect { Kind = EffectKind.Word, X = Field.CX, Y = 130, Text = "NEW SHIP", Life = 1.6, Size = 1.4, Colour = 0xFF8AE0FF });
        }
    }

    private void Burst(double x, double y, uint colour, int n, double life)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 40 + _rng.NextDouble() * 200;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = life * (0.5 + _rng.NextDouble() * 0.5), Colour = colour });
        }
    }

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Vx *= 1 - dt * 0.6;
            d.Vy *= 1 - dt * 0.6;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the fight for a planet, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Battle);
        Message = SubMessage = null;
    }

    /// <summary>Turns the planet so that a point on it faces the player square on.</summary>
    internal void Face(Vec3 at)
    {
        for (int i = 0; i < 400; i++)
        {
            var v = Globe.ToView(at);
            if (v.Z > 1 - 1e-12) return;
            Globe.Turn(-Math.Atan2(v.X, v.Z), Math.Atan2(v.Y, Math.Sqrt(v.X * v.X + v.Z * v.Z)));
        }
    }

    internal void PutSight(double x, double y) { SightX = x; SightY = y; }
    internal Threat PutThreat(ThreatKind kind, double x, double y, int ship)
    {
        Launch(kind, x, y, Ships[ship], kind == ThreatKind.Raider ? RaiderSpeed : kind == ThreatKind.Pellet ? PelletSpeed : RocketSpeed);
        return Threats[^1];
    }

    /// <summary>A blast right here, as if a missile had just got here.</summary>
    internal void Explode(double x, double y) => Detonate(x, y);
    internal void SetDrift(double right, double down) => Drift = (right, down);
    internal void SetReserve(int n) => Reserve = n;
    internal void SetScore(int n) => Score = n;
}
