using AVALiberator.Engine;

namespace AVALiberator.Tests;

/// <summary>How the computer pilot gets on, planet by planet (set LIBERATOR_PROBE to run it, for tuning; LIBERATOR_PROBE=lazy for one that sweeps the sight and fires).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("LIBERATOR_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "liberator-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetReserve(99);
            var losses = new Dictionary<string, int>();
            var waves = new List<string>();
            var doing = new Dictionary<string, int>();
            int wave = 1, t = 0, lives = g.Lives;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 6 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Dx: t % 240 < 120 ? -1 : 1, Dy: t % 400 < 200 ? -0.4 : 0.4, Fire: t % 20 < 10) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Battle) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Lives < lives) losses[$"P{g.Wave} {g.Cause}"] = losses.GetValueOrDefault($"P{g.Wave} {g.Cause}") + lives - g.Lives;
                lives = g.Lives;
                if (g.Wave != wave) { waves.Add($"P{wave}:{(t - start) / 60:0}s"); wave = g.Wave; start = t; }
            }
            output.WriteLine($"seed {seed}: planet {g.Wave} after {t / 60:0}s score {g.Score} | {string.Join(" ", waves)}");
            output.WriteLine("   ships lost " + string.Join(", ", losses.Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine($"   bases left {g.BasesLeft} reserve {g.Reserve}");
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
