using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>
/// Merge Panic: falling blocks of code. Fill a row to commit it. Merge conflicts keep coming: a grey
/// line with one gap pushes up from the bottom every few seconds, sooner as the level rises, unless the
/// rows you commit cancel it first. It's over when the stack reaches the top.
/// </summary>
public sealed class MergeGame : ArcadeGame
{
    const int W = 10, H = 19;
    const double C = 6, X0 = 160, Y0 = 22; // cell size and where the well starts
    const double Das = 0.15, Arr = 0.035, LockDelay = 0.5;
    const int LinesPerLevel = 8;

    static readonly (int X, int Y)[][] Shapes =
    {
        new[] { (0, 1), (1, 1), (2, 1), (3, 1) }, // I
        new[] { (0, 0), (1, 0), (0, 1), (1, 1) }, // O
        new[] { (1, 0), (0, 1), (1, 1), (2, 1) }, // T
        new[] { (1, 0), (2, 0), (0, 1), (1, 1) }, // S
        new[] { (0, 0), (1, 0), (1, 1), (2, 1) }, // Z
        new[] { (0, 0), (0, 1), (1, 1), (2, 1) }, // J
        new[] { (2, 0), (0, 1), (1, 1), (2, 1) }, // L
    };
    static readonly int[] Box = { 4, 2, 3, 3, 3, 3, 3 };
    static readonly Brush[] Inks =
    {
        Ink.Of(0x60, 0xA5, 0xFA), Ink.Of(0xF5, 0xD2, 0x4A), Ink.Of(0xB0, 0x7A, 0xE0), Ink.Of(0x5F, 0xC2, 0x7E),
        Ink.Of(0xE0, 0x60, 0x5A), Ink.Of(0x7A, 0x86, 0xA8), Ink.Of(0xF2, 0x7A, 0x2C),
    };
    static readonly Brush ConflictInk = Ink.Of(0x55, 0x58, 0x62);
    static readonly Brush ConflictMark = Ink.Of(0xE8, 0x6A, 0xA8);
    static readonly Brush WellInk = Ink.Of(0x11, 0x12, 0x15);
    static readonly Pen GhostPen = FrozenPen(Color.FromArgb(0x60, 0xC9, 0xCA, 0xCF), 1);
    static readonly (int X, int Y)[] Kicks = { (0, 0), (-1, 0), (1, 0), (0, -1), (-2, 0), (2, 0), (0, 1) };

    Random course = new();
    // 0 empty, 1-7 a piece's colour, 8 a conflict line
    int[,] well = new int[W, H];
    readonly List<int> bag = new(), queue = new();
    int piece, rot, x, y, hold = -1;
    bool held;
    double fall, lockLeft, garbageIn, slowLeft, cheer, shake;
    int lockMoves, pending, guardLines, boostPieces;
    int lines, level, points;
    int dir;          // the arrow held, -1 or 1
    double dirFor;    // how long it's been held
    bool soft;
    double charge;
    double pilotNext; // when the autopilot makes its next move
    (int Rot, int X)? plan;

    public override string Id => "merge";
    public override string Title => "Merge Panic";
    public override string Keys => $"←→: move · ↑ / Z: turn · ↓: drop faster · Space: drop · C: hold · X: {Mascot.PowerName}";
    protected override string Hint => "Commit rows to cancel the conflicts coming up";
    public override int Score => points;

    public MergeGame() => NewRun();

    double GarbageEvery => Math.Max(3.5, 13 - level * 1.1);

    protected override void Reset()
    {
        course = new Random(CourseSeed());
        well = new int[W, H];
        bag.Clear();
        queue.Clear();
        hold = -1;
        held = false;
        lines = points = pending = guardLines = boostPieces = 0;
        level = 1;
        charge = slowLeft = cheer = shake = 0;
        garbageIn = GarbageEvery;
        dir = 0;
        soft = false;
        plan = null;
        NextPiece();
    }

    // ---- Pieces ---------------------------------------------------------------

    int Draw7()
    {
        if (bag.Count == 0) bag.AddRange(Enumerable.Range(0, 7).OrderBy(_ => course.Next()));
        int p = bag[0];
        bag.RemoveAt(0);
        return p;
    }

    void NextPiece(int? take = null)
    {
        while (queue.Count < 3) queue.Add(Draw7());
        if (take is { } t) piece = t;
        else if (boostPieces > 0) { piece = 0; boostPieces--; }
        else { piece = queue[0]; queue.RemoveAt(0); queue.Add(Draw7()); }
        rot = 0;
        x = (W - Box[piece]) / 2;
        y = 0;
        fall = 0;
        lockLeft = LockDelay;
        lockMoves = 0;
        plan = null;
        if (!Fits(piece, rot, x, y)) Lose($"Stack overflow at level {level} · Space to try again");
    }

    static IEnumerable<(int X, int Y)> Cells(int p, int r, int px, int py)
    {
        int n = Box[p];
        foreach (var (cx, cy) in Shapes[p])
        {
            int ax = cx, ay = cy;
            for (int i = 0; i < r; i++) (ax, ay) = (n - 1 - ay, ax);
            yield return (px + ax, py + ay);
        }
    }

    bool Fits(int p, int r, int px, int py) => Fits(well, p, r, px, py);

    static bool Fits(int[,] board, int p, int r, int px, int py) =>
        Cells(p, r, px, py).All(c => c.X >= 0 && c.X < W && c.Y < H && (c.Y < 0 || board[c.X, c.Y] == 0));

    bool Move(int dx)
    {
        if (!Fits(piece, rot, x + dx, y)) return false;
        x += dx;
        Moved();
        return true;
    }

    void Turn(int by)
    {
        int r = (rot + by + 4) % 4;
        foreach (var (kx, ky) in Kicks)
            if (Fits(piece, r, x + kx, y + ky))
            {
                rot = r;
                x += kx;
                y += ky;
                Moved();
                return;
            }
    }

    /// <summary>Moving on the ground buys a little more time before it locks, but not forever.</summary>
    void Moved()
    {
        if (Fits(piece, rot, x, y + 1) || lockMoves >= 12) return;
        lockMoves++;
        lockLeft = LockDelay;
    }

    void HardDrop()
    {
        int rows = 0;
        while (Fits(piece, rot, x, y + 1)) { y++; rows++; }
        points += rows * 2;
        Lock();
    }

    void Hold()
    {
        if (held) return;
        held = true;
        int was = hold;
        hold = piece;
        if (was < 0) NextPiece();
        else NextPiece(was);
    }

    void Lock()
    {
        foreach (var (cx, cy) in Cells(piece, rot, x, y))
        {
            if (cy < 0) { Lose($"Stack overflow at level {level} · Space to try again"); return; }
            well[cx, cy] = piece + 1;
        }
        held = false;
        int cleared = ClearRows();
        if (cleared > 0)
        {
            int won = new[] { 0, 100, 300, 500, 800 }[cleared] * level;
            points += won;
            Popup(cleared == 4 ? $"BIG MERGE +{won}" : $"+{won}", X0 + W * C / 2, Y0 + 40);
            charge = Math.Min(1, charge + (cleared == 4 ? 0.6 : 0.12 * cleared));
            cheer = 0.8;
            // Committed rows cancel conflicts on their way
            int cancel = Math.Min(pending, cleared);
            pending -= cancel;
            lines += cleared;
            level = 1 + lines / LinesPerLevel;
        }
        else if (pending > 0) Rise();
        if (State == GameState.Running) NextPiece();
    }

    int ClearRows()
    {
        int cleared = 0;
        for (int row = H - 1; row >= 0; row--)
        {
            bool full = true;
            for (int col = 0; col < W && full; col++) full = well[col, row] != 0;
            if (!full) continue;
            for (int col = 0; col < W; col++)
                bits.Add(new Bit { Shape = GameArt.Dot, Ink = well[col, row] == 8 ? ConflictMark : Inks[well[col, row] - 1], X = X0 + col * C + 3, Y = Y0 + row * C + 3, VX = (rng.NextDouble() - 0.5) * 120, VY = -40 - rng.NextDouble() * 80, Life = 0.6, Falls = true });
            for (int r = row; r > 0; r--)
                for (int col = 0; col < W; col++) well[col, r] = well[col, r - 1];
            for (int col = 0; col < W; col++) well[col, 0] = 0;
            cleared++;
            row++; // look at this row again, it's the one that fell into it
        }
        return cleared;
    }

    /// <summary>The conflict lines waiting push up from the bottom, each with a gap (the same one for the batch).</summary>
    void Rise()
    {
        int gap = course.Next(W);
        for (int n = 0; n < pending; n++)
        {
            for (int col = 0; col < W; col++)
                if (well[col, 0] != 0) { Lose($"Buried in conflicts at level {level} · Space to try again"); return; }
            for (int r = 0; r < H - 1; r++)
                for (int col = 0; col < W; col++) well[col, r] = well[col, r + 1];
            for (int col = 0; col < W; col++) well[col, H - 1] = col == gap ? 0 : 8;
        }
        pending = 0;
        shake = 0.25;
    }

    // ---- Control --------------------------------------------------------------

    public override bool Press(Key key, bool repeat)
    {
        if (State != GameState.Running) return base.Press(key, repeat);
        if (repeat) return true; // held arrows repeat on their own clock
        switch (key)
        {
            case Key.Left or Key.A: dir = -1; dirFor = 0; Move(-1); return true;
            case Key.Right or Key.D: dir = 1; dirFor = 0; Move(1); return true;
            case Key.Up or Key.W or Key.Z: Turn(key == Key.Z ? -1 : 1); return true;
            case Key.Down or Key.S: soft = true; return true;
            case Key.Space: HardDrop(); return true;
            case Key.C or Key.LeftShift or Key.RightShift: Hold(); return true;
            case Key.X or Key.E: UsePower(); return true;
        }
        return false;
    }

    public override void Release(Key key)
    {
        if ((key is Key.Left or Key.A && dir < 0) || (key is Key.Right or Key.D && dir > 0)) dir = 0;
        if (key is Key.Down or Key.S) soft = false;
    }

    public override void Click(Point at, bool right)
    {
        if (State != GameState.Running) { base.Click(at, right); return; }
        if (right) Turn(1);
        else HardDrop();
    }

    void UsePower()
    {
        if (charge < 1) return;
        charge = 0;
        switch (Family)
        {
            case PowerFamily.Clear:
                // The bottom three rows go
                for (int n = 0; n < 3; n++)
                {
                    for (int col = 0; col < W; col++)
                        if (well[col, H - 1] != 0) bits.Add(new Bit { Shape = GameArt.Orb, Ink = GameArt.OrbInk, X = X0 + col * C, Y = Y0 + (H - 1) * C, VX = (rng.NextDouble() - 0.5) * 100, VY = -60 - rng.NextDouble() * 60, Life = 0.6, Falls = true });
                    for (int r = H - 1; r > 0; r--)
                        for (int col = 0; col < W; col++) well[col, r] = well[col, r - 1];
                    for (int col = 0; col < W; col++) well[col, 0] = 0;
                }
                Popup("CLEARED", X0 + W * C / 2, Y0 + H * C - 10);
                break;
            case PowerFamily.Slow:
                slowLeft = 10;
                Popup("SLOW", X0 + W * C / 2, Y0 + 30, MeterFill);
                break;
            case PowerFamily.Guard:
                pending = 0;
                guardLines = 3;
                Popup("GUARD", X0 + W * C / 2, Y0 + 30, MeterFill);
                break;
            case PowerFamily.Boost:
                boostPieces = 3;
                Popup("3 LONG ONES", X0 + W * C / 2, Y0 + 30);
                break;
        }
    }

    // ---- Simulation -----------------------------------------------------------

    protected override void Step(double dt)
    {
        cheer = Math.Max(0, cheer - dt);
        shake = Math.Max(0, shake - dt);
        if (State != GameState.Running) return;
        slowLeft = Math.Max(0, slowLeft - dt);

        // The next conflict line, unless slowed
        if (slowLeft <= 0) garbageIn -= dt;
        if (garbageIn <= 0)
        {
            garbageIn = GarbageEvery;
            if (guardLines > 0) guardLines--;
            else pending++;
        }

        if (dir != 0)
        {
            dirFor += dt;
            while (dirFor >= Das)
            {
                if (!Move(dir)) { dirFor = Das; break; }
                dirFor -= Arr;
            }
        }

        double perRow = Math.Pow(Math.Max(0.05, 0.8 - (level - 1) * 0.007), level) * (slowLeft > 0 ? 2 : 1);
        if (soft) perRow = Math.Min(perRow, 0.03);
        if (Fits(piece, rot, x, y + 1))
        {
            fall += dt;
            while (fall >= perRow && Fits(piece, rot, x, y + 1))
            {
                fall -= perRow;
                y++;
                if (soft) points++;
            }
        }
        else
        {
            lockLeft -= dt;
            if (lockLeft <= 0) Lock();
        }
    }

    /// <summary>Picks the best spot for each piece (low, flat, few holes) and goes there.</summary>
    protected override void Pilot()
    {
        if (State == GameState.Ready) { StartOrResume(); return; }
        if (State != GameState.Running || time < pilotNext) return;
        pilotNext = time + 0.06;
        if (charge >= 1) UsePower();
        plan ??= BestSpot();
        var (r, px) = plan.Value;
        if (rot != r) Turn(1);
        else if (x != px) { if (!Move(Math.Sign(px - x))) HardDrop(); }
        else HardDrop();
    }

    (int, int) BestSpot()
    {
        double best = double.MaxValue;
        (int, int) pick = (rot, x);
        for (int r = 0; r < 4; r++)
            for (int px = -2; px < W; px++)
            {
                if (!Fits(piece, r, px, 0)) continue;
                int py = 0;
                while (Fits(piece, r, px, py + 1)) py++;
                var board = (int[,])well.Clone();
                foreach (var (cx, cy) in Cells(piece, r, px, py)) if (cy >= 0) board[cx, cy] = 1;
                int full = 0, holes = 0, heights = 0, bumps = 0, last = -1;
                for (int row = 0; row < H; row++)
                {
                    bool all = true;
                    for (int col = 0; col < W; col++) all &= board[col, row] != 0;
                    if (all) full++;
                }
                for (int col = 0; col < W; col++)
                {
                    int top = 0;
                    while (top < H && board[col, top] == 0) top++;
                    int h = H - top;
                    for (int row = top; row < H; row++) if (board[col, row] == 0) holes++;
                    heights += h;
                    if (last >= 0) bumps += Math.Abs(h - last);
                    last = h;
                }
                double score = heights * 0.51 + holes * 3.6 + bumps * 0.18 - full * 7.6;
                if (score < best) { best = score; pick = (r, px); }
            }
        return pick;
    }

    // ---- Drawing --------------------------------------------------------------

    protected override void Draw(DrawingContext dc)
    {
        double sx = shake > 0 ? Math.Round(Math.Sin(time * 80) * 1.5) : 0;
        dc.PushTransform(new TranslateTransform(sx, 0));
        dc.DrawRectangle(WellInk, null, new Rect(X0 - 1, Y0, W * C + 2, H * C + 1));
        dc.DrawRectangle(GroundInk, null, new Rect(X0 - 2, Y0, 1, H * C + 1));
        dc.DrawRectangle(GroundInk, null, new Rect(X0 + W * C + 1, Y0, 1, H * C + 1));
        dc.DrawRectangle(GroundInk, null, new Rect(X0 - 2, Y0 + H * C + 1, W * C + 4, 1));
        for (int col = 0; col < W; col++)
            for (int row = 0; row < H; row++)
                if (well[col, row] is int v and > 0) Cell(dc, X0 + col * C, Y0 + row * C, v == 8 ? ConflictInk : Inks[v - 1], v == 8);

        if (State != GameState.Ready)
        {
            // Where it would land, then the piece
            int gy = y;
            while (Fits(piece, rot, x, gy + 1)) gy++;
            foreach (var (cx, cy) in Cells(piece, rot, x, gy))
                if (cy >= 0) dc.DrawRectangle(null, GhostPen, new Rect(X0 + cx * C + 0.5, Y0 + cy * C + 0.5, C - 1, C - 1));
            foreach (var (cx, cy) in Cells(piece, rot, x, y))
                if (cy >= 0) Cell(dc, X0 + cx * C, Y0 + cy * C, Inks[piece], false);
        }

        // The conflicts waiting to come up, a red bar beside the well that blinks as the next one nears
        if (pending > 0)
        {
            bool blink = garbageIn < 1.5 && (int)(time * 8) % 2 == 0;
            dc.DrawRectangle(blink ? WhiteInk : BadInk, null, new Rect(X0 - 6, Y0 + H * C - pending * C, 3, pending * C));
        }
        dc.DrawRectangle(MeterBack, null, new Rect(X0 - 6, Y0, 3, H * C - pending * C));
        double toNext = 1 - garbageIn / GarbageEvery;
        dc.DrawRectangle(guardLines > 0 ? MeterFill : ConflictMark, null, new Rect(X0 - 6, Y0 + (H * C - pending * C) * (1 - toNext), 3, 1));
        dc.Pop();

        // Hold and the next three
        dc.DrawText(Text("HOLD", Label, 6, DimInk), new Point(118, Y0));
        if (hold >= 0) Mini(dc, hold, 118, Y0 + 10, held ? DimInk : Inks[hold]);
        dc.DrawText(Text("NEXT", Label, 6, DimInk), new Point(232, Y0));
        var next = boostPieces > 0 ? Enumerable.Repeat(0, boostPieces).Concat(queue).Take(3) : queue.Take(3);
        int i = 0;
        foreach (int p in next) Mini(dc, p, 232, Y0 + 10 + i++ * 16, Inks[p]);

        dc.DrawText(Text($"LEVEL {level}", Label, 7, TextInk), new Point(290, Y0 + 10));
        dc.DrawText(Text($"ROWS {lines}", Label, 7, DimInk), new Point(290, Y0 + 22));
        string incoming = guardLines > 0 ? $"GUARD {guardLines}" : pending > 0 ? $"{pending} INCOMING" : $"NEXT IN {Math.Ceiling(garbageIn)} s";
        dc.DrawText(Text(incoming, Label, 7, pending > 0 ? BadInk : guardLines > 0 ? MeterFill : DimInk), new Point(290, Y0 + 34));
        if (slowLeft > 0) dc.DrawText(Text($"SLOW {Math.Ceiling(slowLeft)} s", Label, 7, MeterFill), new Point(290, Y0 + 46));

        // The mascot watches the stack: a cheer for every commit, wide eyes when it's high
        int top = H;
        for (int row = 0; row < H && top == H; row++)
            for (int col = 0; col < W; col++)
                if (well[col, row] != 0) { top = row; break; }
        var eyes = State == GameState.Over ? EyeLook.Closed : cheer > 0 ? EyeLook.Happy : top < 6 ? EyeLook.Wide : EyeLook.Open;
        DrawMascot(dc, 40, 132, 30, arms: cheer > 0 ? ((int)(time * 10) % 2 == 0 ? 3 : 1) : 0, eyes: eyes);

        DrawBits(dc);
        DrawPops(dc);
        DrawCourse(dc);
        DrawScore(dc);
        DrawMeter(dc, charge, slowLeft > 0 ? "ON" : guardLines > 0 ? "UP" : boostPieces > 0 ? "ON" : "");
    }

    static void Cell(DrawingContext dc, double cx, double cy, Brush ink, bool conflict)
    {
        dc.DrawRectangle(ink, null, new Rect(cx + 0.5, cy + 0.5, C - 1, C - 1));
        if (conflict) dc.DrawRectangle(ConflictMark, null, new Rect(cx + 2, cy + 2, 2, 2));
    }

    static void Mini(DrawingContext dc, int p, double mx, double my, Brush ink)
    {
        foreach (var (cx, cy) in Shapes[p])
            dc.DrawRectangle(ink, null, new Rect(mx + cx * 4, my + cy * 4, 3.5, 3.5));
    }
}
