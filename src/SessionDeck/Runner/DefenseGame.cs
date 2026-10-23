using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>
/// Context Defense: bugs march along the path toward your context window. Spend credits on tools beside
/// the path: linters fire fast, test suites hit a crowd, type checkers slow things down. Every one that
/// gets through eats context; run out and it's over. Waves grow tougher, with a merge conflict every
/// fifth that splits into bugs. Between waves there's a breather, or call the next one early for credits.
/// </summary>
public sealed class DefenseGame : ArcadeGame
{
    const int Cols = 38, Rows = 10, StartContext = 20, StartCredits = 60;
    const double T = 10, Y0 = 24, Breather = 7;
    const double ChargePerSecond = 1 / 70.0, KillCharge = 0.015;

    enum Foe { Bug, Coffee, Error, Spike, Conflict }
    enum Tool { Linter, Tests, Types }

    sealed class Enemy
    {
        public Foe Kind;
        public double Along, Hp, MaxHp, Speed, SlowLeft;
        public double X, Y;
    }

    sealed class Tower
    {
        public Tool Kind;
        public int C, R;
        public double Cool;
        public double CX => (C + 0.5) * T;
        public double CY => Y0 + (R + 0.5) * T;
    }

    sealed class Shot { public Point From, To; public Brush Ink = Brushes.White; public double Life; }

    static readonly Dictionary<Foe, (GameArt.Look Look, double Px, double Hp, double Speed, int Reward, int Leak)> Foes = new()
    {
        [Foe.Bug] = (GameArt.Bug, 1, 4, 22, 3, 1),
        [Foe.Coffee] = (GameArt.Coffee, 0.7, 3, 40, 3, 1),
        [Foe.Error] = (GameArt.Error, 1.1, 14, 14, 8, 3),
        [Foe.Spike] = (GameArt.Spike, 1.4, 6, 30, 5, 2),
        [Foe.Conflict] = (GameArt.Conflict, 1.4, 60, 12, 25, 8),
    };
    static readonly Dictionary<Tool, (string Name, int Cost, double Range, double Damage, double Every, Brush Ink)> Tools = new()
    {
        [Tool.Linter] = ("LINT", 20, 26, 1, 0.35, Ink.Of(0x60, 0xA5, 0xFA)),
        [Tool.Tests] = ("TESTS", 45, 32, 3, 1.4, Ink.Of(0x5F, 0xC2, 0x7E)),
        [Tool.Types] = ("TYPES", 35, 28, 0.5, 0.8, Ink.Of(0xB0, 0x7A, 0xE0)),
    };
    static readonly Brush PathInk = Ink.Of(0x24, 0x26, 0x2C);
    static readonly Brush PathEdge = Ink.Of(0x2C, 0x2E, 0x35);
    static readonly Brush TowerBase = Ink.Of(0x3A, 0x3D, 0x45);
    static readonly Brush HpBack = Ink.Of(0x3A, 0x3D, 0x45);
    static readonly Brush SlowTint = Frozen(new SolidColorBrush(Color.FromArgb(0x60, 0xB0, 0x7A, 0xE0)));
    static readonly Pen RangePen = FrozenPen(Color.FromArgb(0x50, 0xC9, 0xCA, 0xCF), 1);
    static readonly Pen BadRangePen = FrozenPen(Color.FromArgb(0x60, 0xE0, 0x60, 0x5A), 1);

    Random course = new();
    readonly List<(int C, int R)> path = new();
    bool[,] onPath = new bool[Cols, Rows];
    readonly List<Enemy> enemies = new();
    readonly List<Tower> towers = new();
    readonly List<Shot> shots = new();
    readonly Queue<Foe> toSend = new();
    int wave, context, credits, points;
    double sendIn, breather, charge, freezeLeft, boostLeft;
    Tool picked = Tool.Linter;
    Point? hover;
    double pilotNext;

    public override string Id => "defense";
    public override string Title => "Context Defense";
    public override string Keys => $"1 2 3: pick a tool · click: build · right-click: sell · Space: next wave · X: {Mascot.PowerName}";
    protected override string Hint => "Last as many waves as you can";
    protected override string StartMessage => "Press Space or click to start";
    public override int Score => points;

    public DefenseGame() => NewRun();

    double PathLength => (path.Count - 1) * T;

    protected override void Reset()
    {
        course = new Random(CourseSeed());
        MakePath();
        enemies.Clear();
        towers.Clear();
        shots.Clear();
        toSend.Clear();
        bits.Clear();
        wave = 0;
        context = StartContext;
        credits = StartCredits;
        points = 0;
        charge = freezeLeft = boostLeft = 0;
        breather = Breather;
    }

    /// <summary>A path from the left edge to the context window on the right, bending up and down on the way.</summary>
    void MakePath()
    {
        path.Clear();
        onPath = new bool[Cols, Rows];
        int c = 0, r = 1 + course.Next(Rows - 2);
        void Mark() { path.Add((c, r)); onPath[c, r] = true; }
        Mark();
        while (c < Cols - 2)
        {
            int run = 3 + course.Next(4);
            for (int i = 0; i < run && c < Cols - 2; i++) { c++; Mark(); }
            if (c >= Cols - 2) break;
            int to;
            do to = 1 + course.Next(Rows - 2); while (Math.Abs(to - r) < 3);
            while (r != to) { r += Math.Sign(to - r); Mark(); }
        }
        c++;
        Mark();
    }

    Point PointAt(double along)
    {
        double i = Math.Clamp(along / T, 0, path.Count - 1);
        int a = (int)Math.Floor(i), b = Math.Min(path.Count - 1, a + 1);
        double f = i - a;
        return new Point((path[a].C + 0.5 + (path[b].C - path[a].C) * f) * T, Y0 + (path[a].R + 0.5 + (path[b].R - path[a].R) * f) * T);
    }

    // ---- Waves ----------------------------------------------------------------

    void NextWave()
    {
        if (toSend.Count > 0) return;
        if (wave > 0 && breather > 0)
        {
            int early = (int)Math.Ceiling(breather) * 2;
            credits += early;
            if (early > 0) Popup($"EARLY +{early}", FieldWidth / 2, Y0 + 20);
        }
        wave++;
        breather = 0;
        int count = 6 + wave * 2;
        var pool = new List<Foe> { Foe.Bug, Foe.Bug };
        if (wave >= 2) pool.Add(Foe.Coffee);
        if (wave >= 3) pool.Add(Foe.Error);
        if (wave >= 4) pool.Add(Foe.Spike);
        if (wave >= 7) pool.AddRange(new[] { Foe.Error, Foe.Spike, Foe.Coffee });
        for (int i = 0; i < count; i++) toSend.Enqueue(pool[course.Next(pool.Count)]);
        if (wave % 5 == 0) toSend.Enqueue(Foe.Conflict);
        sendIn = 0;
    }

    void Send(Foe kind, double along = 0)
    {
        var f = Foes[kind];
        double hp = f.Hp * Math.Pow(1.17, wave - 1);
        var p = PointAt(along);
        enemies.Add(new Enemy { Kind = kind, Along = along, Hp = hp, MaxHp = hp, Speed = f.Speed * (0.9 + course.NextDouble() * 0.2), X = p.X, Y = p.Y });
    }

    // ---- Control --------------------------------------------------------------

    public override bool Press(Key key, bool repeat)
    {
        switch (key)
        {
            case Key.D1 or Key.NumPad1: picked = Tool.Linter; return true;
            case Key.D2 or Key.NumPad2: picked = Tool.Tests; return true;
            case Key.D3 or Key.NumPad3: picked = Tool.Types; return true;
        }
        if (State != GameState.Running) return base.Press(key, repeat);
        if (repeat) return true;
        switch (key)
        {
            case Key.Space: NextWave(); return true;
            case Key.X or Key.E or Key.LeftShift or Key.RightShift: UsePower(); return true;
        }
        return false;
    }

    public override void Click(Point at, bool right)
    {
        if (State != GameState.Running) { base.Click(at, right); return; }
        // A click on the tool row picks one
        if (at.Y >= Y0 + Rows * T)
        {
            foreach (var (tool, x, w) in Palette())
                if (at.X >= x && at.X < x + w) picked = tool;
            return;
        }
        var cell = CellAt(at);
        if (cell is not var (c, r)) return;
        var there = towers.FirstOrDefault(t => t.C == c && t.R == r);
        if (right)
        {
            if (there == null) return;
            int back = Tools[there.Kind].Cost * 6 / 10;
            credits += back;
            towers.Remove(there);
            Popup($"+{back}", there.CX, there.CY - 4, GoodInk);
            return;
        }
        Build(picked, c, r);
    }

    public override void Hover(Point at) => hover = at;

    (int, int)? CellAt(Point at)
    {
        int c = (int)Math.Floor(at.X / T), r = (int)Math.Floor((at.Y - Y0) / T);
        return c >= 0 && c < Cols && r >= 0 && r < Rows ? (c, r) : null;
    }

    bool CanBuild(int c, int r) => !onPath[c, r] && !towers.Any(t => t.C == c && t.R == r) && c < Cols - 1;

    void Build(Tool tool, int c, int r)
    {
        int cost = Tools[tool].Cost;
        if (!CanBuild(c, r)) return;
        if (credits < cost)
        {
            Popup("NOT ENOUGH", (c + 0.5) * T, Y0 + r * T, BadInk);
            return;
        }
        credits -= cost;
        towers.Add(new Tower { Kind = tool, C = c, R = r });
    }

    void UsePower()
    {
        if (charge < 1) return;
        charge = 0;
        switch (Family)
        {
            case PowerFamily.Clear:
                // Half of everyone's health, all at once
                foreach (var e in enemies.ToList()) Damage(e, e.MaxHp * 0.5);
                foreach (var e in enemies) Burst(GameArt.Orb, GameArt.OrbInk, e.X, e.Y, 2, falls: true);
                Popup("FIREWALL", FieldWidth / 2, Y0 + 20);
                break;
            case PowerFamily.Slow:
                freezeLeft = 4;
                Popup("FROZEN", FieldWidth / 2, Y0 + 20, MeterFill);
                break;
            case PowerFamily.Guard:
                context = Math.Min(StartContext, context + 5);
                Popup("+5 CONTEXT", FieldWidth - 30, Y0 + 20, GoodInk);
                break;
            case PowerFamily.Boost:
                boostLeft = 8;
                Popup("OVERCLOCK", FieldWidth / 2, Y0 + 20);
                break;
        }
    }

    // ---- Simulation -----------------------------------------------------------

    protected override void Step(double dt)
    {
        foreach (var s in shots) s.Life -= dt;
        shots.RemoveAll(s => s.Life <= 0);
        if (State != GameState.Running) return;
        charge = Math.Min(1, charge + ChargePerSecond * dt);
        freezeLeft = Math.Max(0, freezeLeft - dt);
        boostLeft = Math.Max(0, boostLeft - dt);

        // Sending the wave, then the breather before the next
        if (toSend.Count > 0)
        {
            sendIn -= dt;
            if (sendIn <= 0)
            {
                Send(toSend.Dequeue());
                sendIn = Math.Max(0.35, 0.9 - wave * 0.03);
            }
        }
        else if (enemies.Count == 0)
        {
            if (wave > 0 && breather <= 0)
            {
                breather = Breather;
                credits += 10 + wave;
                points += 100 * wave;
                Popup($"WAVE {wave} CLEARED", FieldWidth / 2, Y0 + 20, GoodInk);
            }
            breather -= dt;
            if (breather <= 0) NextWave();
        }

        foreach (var e in enemies.ToList())
        {
            if (freezeLeft > 0) continue;
            e.SlowLeft = Math.Max(0, e.SlowLeft - dt);
            double speed = e.Speed * (e.SlowLeft > 0 && e.Kind != Foe.Spike ? 0.5 : 1);
            e.Along += speed * dt;
            var p = PointAt(e.Along);
            (e.X, e.Y) = (p.X, p.Y);
            if (e.Along >= PathLength)
            {
                enemies.Remove(e);
                int leak = Foes[e.Kind].Leak;
                context -= leak;
                Popup($"-{leak} CONTEXT", e.X - 20, e.Y - 4, BadInk);
                if (context <= 0)
                {
                    context = 0;
                    Lose($"Context full at wave {wave} · Space to try again");
                    return;
                }
            }
        }

        foreach (var t in towers)
        {
            var spec = Tools[t.Kind];
            t.Cool -= dt * (boostLeft > 0 ? 2 : 1);
            if (t.Cool > 0) continue;
            // The one in range that's farthest along
            var target = enemies.Where(e => Dist(e.X, e.Y, t.CX, t.CY) <= spec.Range).OrderByDescending(e => e.Along).FirstOrDefault();
            if (target == null) continue;
            t.Cool = spec.Every;
            shots.Add(new Shot { From = new Point(t.CX, t.CY - 2), To = new Point(target.X, target.Y), Ink = spec.Ink, Life = 0.08 });
            switch (t.Kind)
            {
                case Tool.Tests:
                    foreach (var e in enemies.Where(e => Dist(e.X, e.Y, target.X, target.Y) <= 12).ToList()) Damage(e, spec.Damage);
                    Burst(GameArt.Dot, spec.Ink, target.X, target.Y, 3, falls: false);
                    break;
                case Tool.Types:
                    target.SlowLeft = 1.2;
                    Damage(target, spec.Damage);
                    break;
                default:
                    Damage(target, spec.Damage);
                    break;
            }
        }
    }

    static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    void Damage(Enemy e, double by)
    {
        if (!enemies.Contains(e)) return;
        e.Hp -= by;
        if (e.Hp > 0) return;
        enemies.Remove(e);
        var f = Foes[e.Kind];
        credits += f.Reward;
        points += f.Reward * 5;
        charge = Math.Min(1, charge + KillCharge);
        Burst(GameArt.Dot, f.Look.Ink, e.X, e.Y, 4, falls: true);
        if (e.Kind == Foe.Conflict)
            for (int i = 0; i < 4; i++) Send(Foe.Bug, Math.Max(0, e.Along - i * 6));
    }

    /// <summary>Builds where the path passes most, a mix of tools, and calls waves early once it's set up.</summary>
    protected override void Pilot()
    {
        if (State == GameState.Ready) { StartOrResume(); return; }
        if (State != GameState.Running || time < pilotNext) return;
        pilotNext = time + 0.4;
        if (charge >= 1 && enemies.Count > 6) UsePower();
        var tool = towers.Count % 3 == 1 ? Tool.Tests : towers.Count % 3 == 2 ? Tool.Types : Tool.Linter;
        if (credits >= Tools[tool].Cost)
        {
            (int C, int R, int N) best = (-1, -1, 0);
            for (int c = 0; c < Cols - 1; c++)
                for (int r = 0; r < Rows; r++)
                {
                    if (!CanBuild(c, r)) continue;
                    double cx = (c + 0.5) * T, cy = Y0 + (r + 0.5) * T;
                    int n = path.Count(p => Dist((p.C + 0.5) * T, Y0 + (p.R + 0.5) * T, cx, cy) <= Tools[tool].Range);
                    if (n > best.N) best = (c, r, n);
                }
            if (best.C >= 0) Build(tool, best.C, best.R);
        }
        if (toSend.Count == 0 && enemies.Count == 0 && towers.Count >= 2 + wave / 2) NextWave();
    }

    // ---- Drawing --------------------------------------------------------------

    IEnumerable<(Tool Tool, double X, double W)> Palette()
    {
        double x = 8;
        foreach (var (tool, spec) in Tools)
        {
            double w = 52;
            yield return (tool, x, w);
            x += w + 4;
        }
    }

    protected override void Draw(DrawingContext dc)
    {
        foreach (var (c, r) in path)
        {
            dc.DrawRectangle(PathInk, null, new Rect(c * T, Y0 + r * T, T, T));
        }
        for (int i = 0; i < path.Count; i += 3)
        {
            var (c, r) = path[i];
            dc.DrawRectangle(PathEdge, null, new Rect(c * T + 4, Y0 + r * T + 4, 2, 2));
        }

        // The context window at the end of the path
        var (ec, er) = path[^1];
        double fill = 1 - context / (double)StartContext;
        dc.DrawRectangle(TowerBase, null, new Rect(ec * T, Y0 + er * T - 6, T, 22));
        dc.DrawRectangle(BadInk, null, new Rect(ec * T + 2, Y0 + er * T - 4 + 18 * (1 - fill), T - 4, 18 * fill));

        foreach (var t in towers)
        {
            var spec = Tools[t.Kind];
            double x = t.C * T, y = Y0 + t.R * T;
            dc.DrawRectangle(TowerBase, null, new Rect(x + 1, y + 2, 8, 7));
            dc.DrawRectangle(spec.Ink, null, new Rect(x + 3, y + 1, 4, 4));
            if (boostLeft > 0) dc.DrawRectangle(MeterReady, null, new Rect(x + 4, y, 2, 1));
        }

        foreach (var e in enemies)
        {
            var f = Foes[e.Kind];
            double w = f.Look.Width(f.Px), h = f.Look.Height(f.Px);
            f.Look.Draw(dc, e.X - w / 2, e.Y - h / 2, f.Px);
            if (e.SlowLeft > 0 && e.Kind != Foe.Spike || freezeLeft > 0) dc.DrawRectangle(SlowTint, null, new Rect(e.X - w / 2, e.Y - h / 2, w, h));
            if (e.Hp < e.MaxHp)
            {
                dc.DrawRectangle(HpBack, null, new Rect(Math.Round(e.X - 4), Math.Round(e.Y - h / 2 - 3), 8, 1));
                dc.DrawRectangle(GoodInk, null, new Rect(Math.Round(e.X - 4), Math.Round(e.Y - h / 2 - 3), Math.Round(8 * e.Hp / e.MaxHp), 1));
            }
        }

        foreach (var s in shots) dc.DrawLine(new Pen(s.Ink, 1), s.From, s.To);

        // Where a tool would go, with its reach
        if (State == GameState.Running && hover is { } at && CellAt(at) is var (hc, hr))
        {
            double cx = (hc + 0.5) * T, cy = Y0 + (hr + 0.5) * T;
            var there = towers.FirstOrDefault(t => t.C == hc && t.R == hr);
            double range = there != null ? Tools[there.Kind].Range : Tools[picked].Range;
            dc.DrawEllipse(null, there != null || CanBuild(hc, hr) && credits >= Tools[picked].Cost ? RangePen : BadRangePen, new Point(cx, cy), range, range);
        }

        DrawBits(dc);
        DrawPops(dc);

        // The tool row along the bottom, with credits and context
        double by = Y0 + Rows * T + 2;
        foreach (var (tool, x, w) in Palette())
        {
            var spec = Tools[tool];
            bool on = tool == picked, afford = credits >= spec.Cost;
            if (on) dc.DrawRectangle(MeterBack, null, new Rect(x - 2, by - 1, w, 12));
            dc.DrawRectangle(spec.Ink, null, new Rect(x, by + 3, 4, 4));
            dc.DrawText(Text($"{(int)tool + 1} {spec.Name} {spec.Cost}", Label, 6, afford ? (on ? TextInk : DimInk) : BadInk), new Point(x + 7, by + 0.5));
        }
        dc.DrawText(Text($"CREDITS {credits}", Font, 7, MeterReady), new Point(184, by));
        dc.DrawText(Text($"CONTEXT {context}/{StartContext}", Font, 7, context <= 5 ? BadInk : TextInk), new Point(262, by));

        string status = (Daily ? "DAILY · " : "") + (toSend.Count > 0 || enemies.Count > 0
            ? $"WAVE {wave}"
            : wave == 0 ? $"FIRST WAVE IN {Math.Ceiling(breather)} s · SPACE TO CALL IT" : $"WAVE {wave + 1} IN {Math.Ceiling(breather)} s · SPACE: NOW");
        dc.DrawText(Text(status, Label, 7, TextInk), new Point(96, 4));
        if (freezeLeft > 0 || boostLeft > 0) dc.DrawText(Text(freezeLeft > 0 ? "FROZEN" : "OVERCLOCK", Label, 6, MeterFill), new Point(96, 13));
        DrawScore(dc);
        DrawMeter(dc, charge, freezeLeft > 0 || boostLeft > 0 ? "ON" : "");
    }
}
