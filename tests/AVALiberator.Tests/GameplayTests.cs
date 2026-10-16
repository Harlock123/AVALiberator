using AVALiberator.Engine;

namespace AVALiberator.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaliberator-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A planet in the fight, calm: the bases and sentinels hold their fire and the planet keeps still.</summary>
    private static GameEngine Game(int planet = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(planet);
        return g;
    }

    /// <summary>A real fight, with plenty of ships in reserve.</summary>
    private static GameEngine Real(int planet = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(planet);
        g.SetReserve(99);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);

    /// <summary>One missile at a point: the sight put there, the button pressed for a moment, and time for it to fly and burst.</summary>
    private static void Shoot(GameEngine g, double x, double y, int after = 60)
    {
        g.PutSight(x, y);
        g.Run(1, FireButton);
        g.Idle(after);
    }

    private static Base Only(GameEngine g, BaseKind kind)
    {
        var b = g.Bases.First(b => b.Kind == kind);
        g.Bases.RemoveAll(o => o != b);
        return b;
    }

    // ───────────────────────────── the sight and the planet ─────────────────────────────

    [Fact]
    public void The_sight_moves_with_the_keys_and_stays_on_screen()
    {
        var g = Game();
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(Field.CX + GameEngine.SightSpeed * 0.5, g.SightX, 1);
        g.Run(300, new Controls(Dx: -1, Dy: -1));
        Assert.Equal(Field.SightLeft, g.SightX);
        Assert.Equal(Field.SightTop, g.SightY);
    }

    [Fact]
    public void The_sight_follows_the_pointer_quickly()
    {
        var g = Game();
        g.Run(6, new Controls(AimX: 900, AimY: 300));
        Assert.True(g.SightX > Field.CX + 100 && g.SightX < 900, $"{g.SightX}");
        g.Run(40, new Controls(AimX: 900, AimY: 300));
        Assert.Equal((900.0, 300.0), (g.SightX, g.SightY));
    }

    [Fact]
    public void The_planet_turns_the_way_it_is_pushed()
    {
        var g = Game();
        var at = g.Bases[0].At;
        g.Face(at);
        var (x0, y0, f0) = g.Globe.Project(at);
        Assert.Equal(Field.CX, x0, 1);
        Assert.Equal(Field.CY, y0, 1);
        Assert.True(f0 > 0.99);
        g.Run(20, new Controls(SpinX: 1));
        var (x1, y1, _) = g.Globe.Project(at);
        Assert.True(x1 > x0 + 50, $"{x1}");
        Assert.Equal(y0, y1, 1);
        g.Face(at);
        g.Run(20, new Controls(SpinY: 1));
        var (x2, y2, _) = g.Globe.Project(at);
        Assert.True(y2 > y0 + 50, $"{y2}");
        Assert.Equal(x0, x2, 1);
    }

    [Fact]
    public void Left_alone_the_planet_turns_by_itself()
    {
        var g = Real();
        var at = g.Bases[0].At;
        g.Face(at);
        g.Idle(60);
        var (x, _, _) = g.Globe.Project(at);
        Assert.True(Math.Abs(x - Field.CX) > 20, $"{x}");
    }

    [Fact]
    public void A_point_on_the_planet_shows_where_it_is()
    {
        var g = Game();
        g.Globe.Turn(0.7, -0.4);
        var under = g.Globe.Under(600, 350);
        Assert.NotNull(under);
        var (x, y, f) = g.Globe.Project(under.Value);
        Assert.Equal(600, x, 6);
        Assert.Equal(350, y, 6);
        Assert.True(f > 0);
        Assert.Null(g.Globe.Under(40, 400));
    }

    // ───────────────────────────── the fleet's missiles ─────────────────────────────

    [Fact]
    public void A_missile_flies_from_the_nearest_ship_and_bursts_at_the_sight()
    {
        var g = Game();
        g.PutSight(300, 250);
        g.Run(1, FireButton);
        var m = Assert.Single(g.Missiles);
        Assert.Equal(0, m.From);
        Assert.Equal((300.0, 250.0), (m.Tx, m.Ty));
        g.Idle(40);
        Assert.Empty(g.Missiles);
        var b = Assert.Single(g.Blasts);
        Assert.Equal(300, b.X, 1);
        Assert.Null(b.Ground);
        g.Idle((int)(Blast.Life * 60) + 2);
        Assert.Empty(g.Blasts);
    }

    [Fact]
    public void Each_ship_has_two_missiles_up_at_most_and_the_fleet_six()
    {
        var g = Game();
        g.PutSight(Field.CX, 120);
        for (int i = 0; i < 80; i++)
        {
            g.Run(1, FireButton);
            Assert.True(g.Missiles.Count <= GameEngine.MaxMissiles);
            Assert.All(g.Missiles.GroupBy(m => m.From), grp => Assert.True(grp.Count() <= GameEngine.PerShip));
        }
        Assert.True(g.Missiles.Count + g.Blasts.Count > 6);
    }

    [Fact]
    public void A_blast_on_the_planet_turns_with_it()
    {
        var g = Game();
        g.Explode(Field.CX, Field.CY);
        var b = Assert.Single(g.Blasts);
        Assert.NotNull(b.Ground);
        g.Run(15, new Controls(SpinX: 1));
        Assert.True(b.X > Field.CX + 20, $"{b.X}");
    }

    // ───────────────────────────── the bases ─────────────────────────────

    [Fact]
    public void A_blast_on_a_silo_destroys_it()
    {
        var g = Game();
        var silo = g.Bases.First(b => b.Kind == BaseKind.Silo);
        g.Face(silo.At);
        Shoot(g, Field.CX, Field.CY);
        Assert.True(silo.Dead);
        Assert.Equal(250, g.Score);
        Assert.Equal(9, g.BasesLeft);
    }

    [Fact]
    public void A_near_miss_does_not()
    {
        var g = Game();
        var silo = Only(g, BaseKind.Silo);
        g.Face(silo.At);
        Shoot(g, Field.CX + 80, Field.CY);
        Assert.False(silo.Dead);
    }

    [Fact]
    public void A_citadel_takes_three_hits()
    {
        var g = Game(3);
        var citadel = g.Bases.Single(b => b.Kind == BaseKind.Citadel);
        Assert.Equal(3, citadel.Hp);
        g.Face(citadel.At);
        Shoot(g, Field.CX, Field.CY, 70);
        Shoot(g, Field.CX, Field.CY, 70);
        Assert.False(citadel.Dead);
        Assert.Equal(1, citadel.Hp);
        Shoot(g, Field.CX, Field.CY, 70);
        Assert.True(citadel.Dead);
        Assert.Equal(1000, g.Score);
    }

    [Fact]
    public void A_silo_fires_only_while_it_faces_the_fleet()
    {
        var g = Real();
        g.SetDrift(0, 0);
        var silo = Only(g, BaseKind.Silo);
        g.Face(silo.At * -1);
        g.Idle(60 * 10);
        Assert.Empty(g.Threats);
        g.Face(silo.At);
        for (int i = 0; i < 60 * 8 && g.Threats.Count == 0; i++) g.Tick(default);
        var rocket = Assert.Single(g.Threats);
        Assert.Equal(ThreatKind.Rocket, rocket.Kind);
        Assert.Equal(Field.CX, rocket.Ox, 1);
        // Straight at a ship.
        var ship = g.Ships[rocket.Target];
        double cross = (ship.HomeX - rocket.Ox) * rocket.Vy - (ship.HomeY - rocket.Oy) * rocket.Vx;
        Assert.True(Math.Abs(cross) / rocket.Speed < 10);
    }

    [Fact]
    public void A_hangar_sends_up_raiders_that_dive_on_the_fleet()
    {
        var g = Real();
        g.SetDrift(0, 0);
        var hangar = Only(g, BaseKind.Hangar);
        g.Face(hangar.At);
        g.Globe.Turn(0.25, 0.1);
        for (int i = 0; i < 60 * 12 && g.Threats.Count == 0; i++) g.Tick(default);
        var raider = Assert.Single(g.Threats);
        Assert.Equal(ThreatKind.Raider, raider.Kind);
        int lives = g.Lives;
        for (int i = 0; i < 60 * 20 && g.Lives == lives; i++) g.Tick(default);
        Assert.Equal(lives - 1, g.Lives);
        Assert.Equal("raider", g.Cause);
    }

    // ───────────────────────────── the enemy in the sky ─────────────────────────────

    [Fact]
    public void Rockets_and_raiders_can_be_blown_up()
    {
        var g = Game();
        var rocket = g.PutThreat(ThreatKind.Rocket, 300, 300, 0);
        rocket.Vx = rocket.Vy = 0;
        var raider = g.PutThreat(ThreatKind.Raider, 700, 260, 1);
        raider.Vx = raider.Vy = 0;
        raider.Speed = 0;
        Shoot(g, 300, 300);
        Shoot(g, 700, 260);
        Assert.Empty(g.Threats);
        Assert.Equal(50 + 150, g.Score);
    }

    [Fact]
    public void A_rocket_that_gets_through_destroys_a_ship_and_one_from_reserve_flies_in()
    {
        var g = Game();
        Assert.Equal(6, g.Lives);
        var ship = g.Ships[2];
        g.PutThreat(ThreatKind.Rocket, ship.X + 60, ship.Y - 60, 2);
        g.Idle(60);
        Assert.Equal("rocket", g.Cause);
        Assert.Equal(5, g.Lives);
        Assert.Equal(1, g.Reserve);
        Assert.True(ship.Alive);
        Assert.False(ship.Ready);
        g.Idle((int)((GameEngine.ArriveSeconds + 1.2) * 60));
        Assert.True(ship.Ready);
    }

    [Fact]
    public void The_last_ship_lost_ends_the_game()
    {
        var g = Game();
        g.SetReserve(0);
        foreach (var s in g.Ships) g.PutThreat(ThreatKind.Rocket, s.X, s.Y + (s.Y < Field.CY ? 40 : -40), s.Index);
        g.Idle(60);
        Assert.Equal(0, g.Lives);
        Assert.Equal(Phase.Fallen, g.Phase);
        g.Idle((int)(GameEngine.FallenSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Sentinels_go_round_out_of_reach_behind_the_planet()
    {
        var g = Game(2);
        var s = Assert.Single(g.Sentinels);
        for (int i = 0; i < 60 * 30 && !s.Hidden; i++) g.Tick(default);
        Assert.True(s.Hidden);
        g.Explode(s.X, s.Y);
        g.Idle(10);
        Assert.False(s.Dead);
        for (int i = 0; i < 60 * 30 && s.Z < 0.3; i++) g.Tick(default);
        g.Idle(60);
        g.Explode(s.X, s.Y);
        g.Idle(2);
        Assert.True(s.Dead);
        Assert.Equal(500, g.Score);
    }

    // ───────────────────────────── planets ─────────────────────────────

    [Fact]
    public void Each_planet_has_more_bases_and_more_kinds_of_enemy()
    {
        var one = Game(1);
        Assert.Equal(10, one.Bases.Count);
        Assert.Equal(1, one.Bases.Count(b => b.Kind == BaseKind.Hangar));
        Assert.DoesNotContain(one.Bases, b => b.Kind == BaseKind.Citadel);
        Assert.Empty(one.Sentinels);
        var three = Game(3);
        Assert.Equal(12, three.Bases.Count);
        Assert.Single(three.Bases, b => b.Kind == BaseKind.Citadel);
        Assert.Single(three.Sentinels);
        var eight = Game(8);
        Assert.Equal(17, eight.Bases.Count);
        Assert.Equal(2, eight.Bases.Count(b => b.Kind == BaseKind.Citadel));
        Assert.Equal(3, eight.Sentinels.Count);
        Assert.NotEqual(one.World.Name, three.World.Name);
        // No two bases on top of each other.
        foreach (var a in eight.Bases)
            Assert.All(eight.Bases.Where(b => b != a), b => Assert.True(a.At.AngleTo(b.At) >= 0.42));
    }

    [Fact]
    public void The_last_base_gone_the_planet_breaks_up_for_a_bonus_then_on_to_the_next()
    {
        var g = Game();
        var last = g.Bases[3];
        foreach (var b in g.Bases.Where(b => b != last)) b.Dead = true;
        g.Face(last.At);
        Shoot(g, Field.CX, Field.CY);
        Assert.Equal(Phase.Liberated, g.Phase);
        Assert.Equal(1000 + 4 * 500, g.Bonus);
        g.Idle((int)(GameEngine.BreakAt * 60) + 10);
        Assert.Equal(0, g.PlanetScale);
        Assert.NotEmpty(g.Wreckage);
        g.Idle((int)((GameEngine.LiberatedSeconds - GameEngine.BreakAt) * 60));
        Assert.Equal(2, g.Wave);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal(250 + 3000, g.Score);
        Assert.Equal(11, g.BasesLeft);
    }

    [Fact]
    public void A_new_ship_at_10000_fills_a_gap_or_goes_into_reserve()
    {
        var g = Game();
        g.SetScore(9990);
        var rocket = g.PutThreat(ThreatKind.Rocket, 300, 300, 0);
        rocket.Vx = rocket.Vy = 0;
        Shoot(g, 300, 300);
        Assert.Equal(3, g.Reserve);
        g.SetReserve(0);
        var ship = g.Ships[1];
        g.PutThreat(ThreatKind.Rocket, ship.X - 50, ship.Y + 50, 1);
        g.Idle(60);
        Assert.False(ship.Alive);
        g.SetScore(GameEngine.FirstExtraShip + GameEngine.ExtraShipEvery - 40);
        rocket = g.PutThreat(ThreatKind.Rocket, 300, 300, 0);
        rocket.Vx = rocket.Vy = 0;
        Shoot(g, 300, 300);
        Assert.True(ship.Alive);
        Assert.Equal(0, g.Reserve);
    }

    // ───────────────────────────── the whole game ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_player_liberates_the_first_planet()
    {
        var g = Real();
        for (int i = 0; i < 60 * 120 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still on planet {g.Wave}, {g.BasesLeft} bases left");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(2, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.SightX:0.00} {g.SightY:0.00} {g.BasesLeft} {g.Phase} {g.Threats.Count}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real();
        g.Run(30, new Controls(Dx: 1));
        double x = g.SightX;
        var (bx, _, _) = g.Globe.Project(g.Bases[0].At);
        g.Pause();
        g.Idle(120);
        Assert.Equal(x, g.SightX);
        Assert.Equal(bx, g.Globe.Project(g.Bases[0].At).X);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
