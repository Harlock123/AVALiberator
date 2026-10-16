using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVALiberator.Engine;
using AVALiberator.Views;
using Controls = AVALiberator.Engine.Controls;

namespace AVALiberator.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set LIBERATOR_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaliberator-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("LIBERATOR_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVALiberator-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 71400, 7);
        g.HighScores.Insert("AVA", 45250, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    /// <summary>A planet in the fight, and the computer at work on it until something worth showing.</summary>
    private static void Fight(int planet, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 60)
    {
        var (w, c, g) = Open(seed);
        g.Go(planet);
        g.SetReserve(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Battle && g.PhaseTime > 1.5 && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_fight_for_the_first_planet() =>
        Fight(1, "drevan", g => g.Threats.Any(t => t.Kind == ThreatKind.Rocket) && g.Missiles.Count > 0 && g.Blasts.Count > 0);

    [AvaloniaFact]
    public void A_citadel() =>
        Fight(3, "citadel", g => g.Bases.Any(b => b.Kind == BaseKind.Citadel && !b.Dead && g.Globe.Project(b.At).Facing > 0.6) && g.Threats.Count >= 2);

    [AvaloniaFact]
    public void Raiders() =>
        Fight(4, "raiders", g => g.Threats.Any(t => t.Kind == ThreatKind.Raider && t.T > 1.2) && g.Blasts.Count > 0);

    [AvaloniaFact]
    public void A_sentinel() =>
        Fight(5, "sentinel", g => g.Sentinels.Any(s => !s.Dead && s.Z > 0.4) && g.Threats.Any(t => t.Kind == ThreatKind.Pellet));

    [AvaloniaFact]
    public void Closing_on_a_planet()
    {
        var (w, c, g) = Open(2);
        g.StartGame();
        for (int t = 0; t < 70; t++) { g.Tick(default); c.Advance(1 / 60.0); }
        Shot(w, c, "approach");
    }

    [AvaloniaFact]
    public void A_planet_liberated()
    {
        var (w, c, g) = Open(4);
        g.Go(2);
        g.SetReserve(20);
        foreach (var b in g.Bases.Skip(1)) b.Dead = true;
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Liberated && g.PhaseTime > GameEngine.BreakAt + 0.35, 60));
        Shot(w, c, "liberated");
    }

    [AvaloniaFact]
    public void The_fleet_lost()
    {
        var (w, c, g) = Open(5);
        g.Go(3);
        g.SetReserve(0);
        foreach (var s in g.Ships) g.PutThreat(ThreatKind.Rocket, s.X, s.Y + (s.Y < Field.CY ? 60 : -60), s.Index);
        for (int t = 0; t < 60 * 4 && g.State != GameState.GameOver; t++) { g.Tick(default); c.Advance(1 / 60.0); }
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(20);
        Shot(w, c, "game-over");
    }
}
