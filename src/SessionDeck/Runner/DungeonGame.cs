using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>
/// Debug Dungeon: a turn-based crawl down floors of bugs. Nothing moves until you do, so a session
/// can interrupt at any moment. Bump into a bug to squash it; error signs take two hits, lag spikes move
/// two tiles a turn, and merge conflicts split in two when hit. Three hearts for the whole run; reach the
/// ladder to go down, deeper floors have more of everything. The power is your mascot's.
/// </summary>
public sealed class DungeonGame : ArcadeGame
{
    const int Cols = 36, Rows = 11, MaxHearts = 3;
    const double T = 10, X0 = 10, Y0 = 24;      // tile size and where the map starts
    const double ChargePerTurn = 1 / 45.0, OrbCharge = 0.5;

    enum Foe { Bug, Error, Spike, Conflict }

    sealed class Enemy
    {
        public Foe Kind;
        public int X, Y, Hp;
        public double FX, FY;  // where it's drawn, catching up with X and Y
        public int Frozen;     // turns it sits out
        public double Flash;   // just got hit
    }

    static readonly Dictionary<Foe, (GameArt.Look Look, double Px)> Looks = new()
    {
        [Foe.Bug] = (GameArt.Bug, 1),
        [Foe.Error] = (GameArt.Error, 1),
        [Foe.Spike] = (GameArt.Spike, 1.4),
        [Foe.Conflict] = (GameArt.Conflict, 1),
    };
    static readonly Brush WallInk = Ink.Of(0x2A, 0x2C, 0x33);
    static readonly Brush WallTop = Ink.Of(0x3A, 0x3D, 0x45);
    static readonly Brush FrostInk = Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0x60, 0xA5, 0xFA)));
    static readonly Brush HurtInk = Frozen(new SolidColorBrush(Color.FromArgb(0x50, 0xE0, 0x60, 0x5A)));
    static readonly Pen AimPen = FrozenPen(Color.FromArgb(0x90, 0xF5, 0xD2, 0x4A), 1);

    Random course = new();
    bool[,] wall = new bool[Cols, Rows];
    readonly List<Enemy> enemies = new();
    readonly List<(int X, int Y, bool Heart)> items = new();
    int px, py, exitX, exitY;   // him and the ladder
    double pfx, pfy;            // where he's drawn
    int hearts, floor, kills, turn, points;
    double charge, hurt, lastMove = -1;
    bool guard, dashArmed;

    public override string Id => "dungeon";
    public override string Title => "Debug Dungeon";
    public override string Keys => $"Arrows / WASD / click: move or attack · Space: wait · X: {Mascot.PowerName}";
    protected override string Hint => "Reach the ladder; deeper floors score more";
    public override int Score => points;

    public DungeonGame() => NewRun();

    protected override void Reset()
    {
        course = new Random(CourseSeed());
        hearts = MaxHearts;
        floor = 0;
        kills = points = turn = 0;
        charge = hurt = 0;
        guard = dashArmed = false;
        NextFloor();
    }

    // ---- Floors ---------------------------------------------------------------

    void NextFloor()
    {
        floor++;
        if (floor > 1) points += 100 * floor;
        enemies.Clear();
        items.Clear();
        bits.Clear();
        wall = new bool[Cols, Rows];
        double density = Math.Min(0.34, 0.24 + floor * 0.01);
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                wall[x, y] = x == 0 || y == 0 || x == Cols - 1 || y == Rows - 1 || course.NextDouble() < density;

        // Him on the left, the ladder on the right, and a winding way between them that's always open
        px = 1 + course.Next(2);
        py = 1 + course.Next(Rows - 2);
        exitX = Cols - 2 - course.Next(2);
        exitY = 1 + course.Next(Rows - 2);
        int cx = px, cy = py;
        while (cx != exitX || cy != exitY)
        {
            wall[cx, cy] = false;
            if (course.NextDouble() < 0.6 && cx != exitX) cx += Math.Sign(exitX - cx);
            else if (course.NextDouble() < 0.5 && cy != exitY) cy += Math.Sign(exitY - cy);
            else cy = Math.Clamp(cy + (course.Next(2) == 0 ? -1 : 1), 1, Rows - 2);
        }
        wall[exitX, exitY] = false;
        pfx = px;
        pfy = py;

        var far = Distances(px, py);
        var open = new List<(int X, int Y)>();
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                if (far[x, y] >= 7 && far[x, y] < int.MaxValue && (x, y) != (exitX, exitY)) open.Add((x, y));

        (int, int) Take()
        {
            int i = course.Next(open.Count);
            var spot = open[i];
            open.RemoveAt(i);
            return spot;
        }

        // More of everything, and nastier kinds, the deeper he goes
        int count = Math.Min(16, 3 + floor + floor / 2);
        for (int i = 0; i < count && open.Count > 0; i++)
        {
            var pool = new List<Foe> { Foe.Bug, Foe.Bug };
            if (floor >= 2) pool.Add(Foe.Error);
            if (floor >= 3) pool.Add(Foe.Spike);
            if (floor >= 4) pool.Add(Foe.Conflict);
            if (floor >= 6) pool.AddRange(new[] { Foe.Spike, Foe.Error });
            var (x, y) = Take();
            Spawn(pool[course.Next(pool.Count)], x, y);
        }
        if (open.Count > 0) { var (x, y) = Take(); items.Add((x, y, false)); }
        if (floor % 3 == 0 && open.Count > 0) { var (x, y) = Take(); items.Add((x, y, true)); }
    }

    void Spawn(Foe kind, int x, int y) =>
        enemies.Add(new Enemy { Kind = kind, X = x, Y = y, FX = x, FY = y, Hp = kind == Foe.Error ? 2 : kind == Foe.Conflict ? 2 : 1 });

    /// <summary>Steps from (x, y) to every tile, walking round walls (enemies don't block).</summary>
    int[,] Distances(int fromX, int fromY)
    {
        var d = new int[Cols, Rows];
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                d[x, y] = int.MaxValue;
        var queue = new Queue<(int, int)>();
        d[fromX, fromY] = 0;
        queue.Enqueue((fromX, fromY));
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in Dirs)
            {
                int nx = x + dx, ny = y + dy;
                if (wall[nx, ny] || d[nx, ny] != int.MaxValue) continue;
                d[nx, ny] = d[x, y] + 1;
                queue.Enqueue((nx, ny));
            }
        }
        return d;
    }

    static readonly (int X, int Y)[] Dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    Enemy? At(int x, int y) => enemies.FirstOrDefault(e => e.X == x && e.Y == y);

    // ---- Control --------------------------------------------------------------

    public override bool Press(Key key, bool repeat)
    {
        (int, int)? dir = key switch
        {
            Key.Left or Key.A => (-1, 0),
            Key.Right or Key.D => (1, 0),
            Key.Up or Key.W => (0, -1),
            Key.Down or Key.S => (0, 1),
            _ => null,
        };
        if (State != GameState.Running) return base.Press(key, repeat);
        // Held keys repeat, but not so fast that he runs into something before you see it
        if (repeat && time - lastMove < 0.14) return dir != null || key == Key.Space;
        if (dir is var (dx, dy)) { Act(dx, dy); return true; }
        if (key == Key.Space) { Act(0, 0); return true; }
        if (key is Key.X or Key.E or Key.LeftShift or Key.RightShift) { UsePower(); return true; }
        return false;
    }

    public override void Click(Point at, bool right)
    {
        if (State != GameState.Running) { base.Click(at, right); return; }
        if (right) { UsePower(); return; }
        // A step toward where you clicked, along the longer way
        double dx = (at.X - X0) / T - 0.5 - px, dy = (at.Y - Y0) / T - 0.5 - py;
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) Act(0, 0);
        else if (Math.Abs(dx) >= Math.Abs(dy)) Act(Math.Sign(dx), 0);
        else Act(0, Math.Sign(dy));
    }

    // ---- Turns ----------------------------------------------------------------

    /// <summary>His turn: a step, an attack on what's there, or a wait (0, 0); then theirs.</summary>
    void Act(int dx, int dy)
    {
        lastMove = time;
        if (dashArmed && (dx, dy) != (0, 0))
        {
            Dash(dx, dy);
            EnemyTurn();
            return;
        }
        int nx = px + dx, ny = py + dy;
        if ((dx, dy) != (0, 0))
        {
            if (wall[nx, ny]) return; // a bump costs no turn
            if (At(nx, ny) is { } foe) Hit(foe);
            else
            {
                px = nx;
                py = ny;
                Pick();
                if ((px, py) == (exitX, exitY))
                {
                    NextFloor();
                    Popup($"FLOOR {floor}  +{100 * floor}", X0 + (px + 0.5) * T, Y0 + py * T, MeterReady);
                    return;
                }
            }
        }
        EnemyTurn();
    }

    void Pick()
    {
        int i = items.FindIndex(it => it.X == px && it.Y == py);
        if (i < 0) return;
        var item = items[i];
        items.RemoveAt(i);
        double cx = X0 + (px + 0.5) * T, cy = Y0 + (py + 0.5) * T;
        if (item.Heart)
        {
            hearts = Math.Min(MaxHearts, hearts + 1);
            Popup("+1 HEART", cx, cy - 4, GoodInk);
        }
        else
        {
            charge = Math.Min(1, charge + OrbCharge);
            points += 10;
            Burst(GameArt.Orb, GameArt.OrbInk, cx, cy, 4, falls: false);
        }
    }

    void Hit(Enemy foe, bool power = false)
    {
        foe.Hp--;
        foe.Flash = 0.2;
        double cx = X0 + (foe.X + 0.5) * T, cy = Y0 + (foe.Y + 0.5) * T;
        Burst(GameArt.Dot, Looks[foe.Kind].Look.Ink, cx, cy, 3, falls: true);
        if (foe.Kind == Foe.Conflict && !power)
        {
            // It splits: two bugs where there's room around it
            enemies.Remove(foe);
            Kill(foe, cx, cy);
            int split = 0;
            foreach (var (dx, dy) in Dirs.OrderBy(_ => rng.Next()))
            {
                int x = foe.X + dx, y = foe.Y + dy;
                if (split == 2 || wall[x, y] || At(x, y) != null || (x, y) == (px, py)) continue;
                Spawn(Foe.Bug, x, y);
                split++;
            }
            return;
        }
        if (foe.Hp > 0 && !power) return;
        enemies.Remove(foe);
        Kill(foe, cx, cy);
    }

    void Kill(Enemy foe, double cx, double cy)
    {
        kills++;
        int won = foe.Kind switch { Foe.Error => 20, Foe.Spike => 25, Foe.Conflict => 15, _ => 10 } * floor;
        points += won;
        Popup($"+{won}", cx, cy - 4);
    }

    void EnemyTurn()
    {
        turn++;
        charge = Math.Min(1, charge + ChargePerTurn);
        var d = Distances(px, py);
        foreach (var e in enemies.ToList())
        {
            if (!enemies.Contains(e)) continue;
            if (e.Frozen > 0) { e.Frozen--; continue; }
            int steps = e.Kind switch
            {
                Foe.Spike => 2,
                Foe.Error => turn % 2, // every other turn
                _ => 1,
            };
            for (int s = 0; s < steps && State == GameState.Running; s++)
            {
                if (Math.Abs(e.X - px) + Math.Abs(e.Y - py) == 1)
                {
                    Hurt(e);
                    break;
                }
                // Hunt him when close enough, else wander
                int sight = e.Kind == Foe.Bug ? 7 : 10;
                (int X, int Y) best = (e.X, e.Y);
                if (d[e.X, e.Y] <= sight)
                {
                    foreach (var (dx, dy) in Dirs.OrderBy(_ => rng.Next()))
                    {
                        int x = e.X + dx, y = e.Y + dy;
                        if (wall[x, y] || At(x, y) != null || (x, y) == (px, py)) continue;
                        if (d[x, y] < d[best.X, best.Y]) best = (x, y);
                    }
                }
                else if (rng.Next(2) == 0)
                {
                    var (dx, dy) = Dirs[rng.Next(4)];
                    if (!wall[e.X + dx, e.Y + dy] && At(e.X + dx, e.Y + dy) == null && (e.X + dx, e.Y + dy) != (px, py)) best = (e.X + dx, e.Y + dy);
                }
                (e.X, e.Y) = best;
            }
        }
    }

    void Hurt(Enemy by)
    {
        by.Flash = 0.15;
        if (guard)
        {
            guard = false;
            Popup("BLOCKED", X0 + (px + 0.5) * T, Y0 + py * T, MeterFill);
            return;
        }
        hearts--;
        hurt = 0.3;
        Burst(GameArt.Puff, BadInk, X0 + (px + 0.5) * T, Y0 + (py + 0.5) * T, 3, falls: false);
        if (hearts <= 0) Lose($"Lost on floor {floor} · Space to try again");
    }

    // ---- Power ----------------------------------------------------------------

    void UsePower()
    {
        if (State != GameState.Running || charge < 1) return;
        charge = 0;
        double cx = X0 + (px + 0.5) * T, cy = Y0 + (py + 0.5) * T;
        switch (Family)
        {
            case PowerFamily.Clear:
                // Everything within two tiles
                foreach (var e in enemies.Where(e => Math.Max(Math.Abs(e.X - px), Math.Abs(e.Y - py)) <= 2).ToList()) Hit(e, power: true);
                Burst(GameArt.Orb, GameArt.OrbInk, cx, cy, 10, falls: false);
                break;
            case PowerFamily.Slow:
                foreach (var e in enemies) e.Frozen = 5;
                Popup("FROZEN", cx, cy - 6, MeterFill);
                break;
            case PowerFamily.Guard:
                guard = true;
                Popup("GUARD", cx, cy - 6, MeterFill);
                break;
            case PowerFamily.Boost:
                dashArmed = true;
                Popup("DASH: PICK A WAY", cx, cy - 6, MeterReady);
                break;
        }
    }

    /// <summary>Boost: he shoots one way until a wall, squashing everything on the way.</summary>
    void Dash(int dx, int dy)
    {
        dashArmed = false;
        while (!wall[px + dx, py + dy])
        {
            px += dx;
            py += dy;
            if (At(px, py) is { } foe) Hit(foe, power: true);
            Pick();
            bits.Add(new Bit { Shape = GameArt.Dot, Ink = DimInk, X = X0 + (px + 0.5) * T, Y = Y0 + (py + 0.5) * T, Life = 0.4 });
            if ((px, py) == (exitX, exitY)) { NextFloor(); return; }
        }
    }

    // ---- Simulation -----------------------------------------------------------

    protected override void Step(double dt)
    {
        double k = Math.Min(1, dt * 20);
        pfx += (px - pfx) * k;
        pfy += (py - pfy) * k;
        foreach (var e in enemies)
        {
            e.FX += (e.X - e.FX) * k;
            e.FY += (e.Y - e.FY) * k;
            e.Flash = Math.Max(0, e.Flash - dt);
        }
        hurt = Math.Max(0, hurt - dt);
    }

    /// <summary>Heads for the ladder, squashing what's next to him and using the power when they crowd in.</summary>
    protected override void Pilot()
    {
        if (State == GameState.Ready) { StartOrResume(); return; }
        if (State != GameState.Running || time - lastMove < 0.18) return;
        if (charge >= 1 && enemies.Count(e => Math.Abs(e.X - px) + Math.Abs(e.Y - py) <= 2) >= 2) { UsePower(); if (!dashArmed) return; }
        var near = enemies.FirstOrDefault(e => Math.Abs(e.X - px) + Math.Abs(e.Y - py) == 1);
        if (near != null) { Act(near.X - px, near.Y - py); return; }
        var d = Distances(exitX, exitY);
        var step = Dirs.Where(s => !wall[px + s.X, py + s.Y]).OrderBy(s => d[px + s.X, py + s.Y]).First();
        Act(step.X, step.Y);
    }

    // ---- Drawing --------------------------------------------------------------

    protected override void Draw(DrawingContext dc)
    {
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
            {
                double sx = X0 + x * T, sy = Y0 + y * T;
                if (wall[x, y])
                {
                    dc.DrawRectangle(WallInk, null, new Rect(sx, sy, T, T));
                    if (y + 1 < Rows && !wall[x, y + 1]) dc.DrawRectangle(WallTop, null, new Rect(sx, sy + T - 2, T, 2));
                }
                else dc.DrawRectangle(PebbleInk, null, new Rect(sx + 4, sy + 4, 2, 2));
            }

        // The ladder down
        double lx = X0 + exitX * T, ly = Y0 + exitY * T;
        dc.DrawRectangle(MeterReady, null, new Rect(lx + 2, ly + 1, 1, 8));
        dc.DrawRectangle(MeterReady, null, new Rect(lx + 7, ly + 1, 1, 8));
        for (int r = 0; r < 3; r++) dc.DrawRectangle(MeterReady, null, new Rect(lx + 2, ly + 2 + r * 3, 6, 1));

        foreach (var (x, y, heart) in items)
        {
            double sx = X0 + x * T, sy = Y0 + y * T;
            if (heart) PixelScene.Sprite(dc, GameArt.Heart.Rows, sx + 1.5, sy + 1.5, 1.4, c => c == 'X' ? GameArt.Heart.Ink : null);
            else
            {
                dc.DrawEllipse(GameArt.OrbGlow, null, new Point(sx + 5, sy + 5), 7, 7);
                PixelScene.Sprite(dc, GameArt.Orb, sx + 2, sy + 2, 2, c => c == 'X' ? GameArt.OrbInk : null);
            }
        }

        foreach (var e in enemies)
        {
            var (look, lpx) = Looks[e.Kind];
            double w = look.Width(lpx), h = look.Height(lpx);
            double sx = X0 + e.FX * T + (T - w) / 2, sy = Y0 + e.FY * T + (T - h) / 2;
            if (e.Flash > 0) dc.PushOpacity(0.4);
            look.Draw(dc, sx, sy, lpx);
            if (e.Flash > 0) dc.Pop();
            if (e.Frozen > 0) dc.DrawRectangle(FrostInk, null, new Rect(X0 + e.FX * T, Y0 + e.FY * T, T, T));
            if (e.Hp > 1) dc.DrawRectangle(WhiteInk, null, new Rect(X0 + e.FX * T + 4, Y0 + e.FY * T - 1, 2, 1));
        }

        // Him, as wide as a tile and a half at most
        double mh = Math.Min(T, T * 1.6 * Mascot.Height / Mascot.Width), mw = MascotWidthFor(mh);
        double mx = X0 + pfx * T + (T - mw) / 2, mb = Y0 + (pfy + 1) * T;
        if (hurt > 0) dc.DrawRectangle(HurtInk, null, new Rect(X0 + pfx * T - 2, Y0 + pfy * T - 2, T + 4, T + 4));
        DrawMascot(dc, mx, mb, mh, arms: dashArmed ? 2 : 0, eyes: State == GameState.Over ? EyeLook.Closed : hurt > 0 ? EyeLook.Wide : EyeLook.Open);
        if (guard) dc.DrawEllipse(null, AimPen, new Point(X0 + (pfx + 0.5) * T, Y0 + (pfy + 0.5) * T), 8, 8);

        DrawBits(dc);
        DrawPops(dc);

        // Hearts and the floor, past the meter's bar
        for (int i = 0; i < MaxHearts; i++)
        {
            if (i < hearts) GameArt.Heart.Draw(dc, 76 + i * 8, 14, 1.2);
            else PixelScene.Sprite(dc, GameArt.Heart.Rows, 76 + i * 8, 14, 1.2, c => c == 'X' ? MeterBack : null);
        }
        dc.DrawText(Text($"FLOOR {floor} · {enemies.Count} LEFT", Label, 6, DimInk), new Point(104, 12.5));
        DrawCourse(dc);
        DrawScore(dc);
        DrawMeter(dc, charge, dashArmed ? "PICK A WAY" : guard ? "UP" : "");
    }
}
