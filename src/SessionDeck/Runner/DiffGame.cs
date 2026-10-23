using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>
/// Daily Diff: two versions of the same code, side by side, with a few changes between them. Click each
/// change (on either side) before the clock runs out. Every find buys a little time and a miss costs
/// some; each round has more changes and subtler ones: a shade off, a token a few pixels wider. On the
/// daily course everyone gets the same puzzles all day.
/// </summary>
public sealed class DiffGame : ArcadeGame
{
    const double PanelW = 170, PanelY = 30, LineH = 8, TokenH = 4, Gutter = 14;
    const int Lines = 12;
    const double StartTime = 45, FindTime = 3, RoundTime = 10, MissTime = 5;
    static readonly double[] PanelX = { 12, 198 };
    const double ChargePerSecond = 1 / 60.0, FindCharge = 0.15;

    static readonly Brush[] Syntax =
    {
        Ink.Of(0xC5, 0x86, 0xC0), Ink.Of(0xCE, 0x91, 0x78), Ink.Of(0xB5, 0xCE, 0xA8), Ink.Of(0x9C, 0xDC, 0xFE),
        Ink.Of(0x4E, 0xC9, 0xB0), Ink.Of(0xD4, 0xD4, 0xD4), Ink.Of(0x6A, 0x99, 0x55),
        // The same, a shade off: the subtle changes
        Ink.Of(0xB0, 0x74, 0xAE), Ink.Of(0xDE, 0xA2, 0x88), Ink.Of(0xA2, 0xBC, 0x94), Ink.Of(0x86, 0xC8, 0xEE),
        Ink.Of(0x3E, 0xB4, 0x9C), Ink.Of(0xBE, 0xBE, 0xBE), Ink.Of(0x7C, 0xAC, 0x66),
    };
    static readonly Brush PanelInk = Ink.Of(0x1E, 0x1F, 0x24);
    static readonly Brush GutterInk = Ink.Of(0x2C, 0x2E, 0x34);
    static readonly Pen FoundPen = FrozenPen(Color.FromRgb(0x5F, 0xC2, 0x7E), 1);
    static readonly Pen HintPen = FrozenPen(Color.FromRgb(0xF5, 0xD2, 0x4A), 1);
    static readonly Pen MissPen = FrozenPen(Color.FromRgb(0xE0, 0x60, 0x5A), 1.5);

    sealed class Token { public double X, W; public int Ink; }
    sealed class Line { public int Indent; public List<Token> Tokens = new(); }
    sealed class Change { public Rect Area; public bool Found, Hinted; }

    Random course = new();
    List<Line> before = new(), after = new();
    readonly List<Change> changes = new();
    readonly List<(Point At, double Age)> misses = new();
    int round, found, points, freeMisses;
    double clockLeft, frozenLeft, charge, happy, roundOver;
    double pilotNext;

    public override string Id => "diff";
    public override string Title => "Daily Diff";
    public override string Keys => $"Click a change, on either side · X / right-click: {Mascot.PowerName}";
    protected override string Hint => "Every find adds time, every miss costs some";
    protected override string StartMessage => "Press Space or click to start";
    public override int Score => points;

    public DiffGame() => NewRun();

    protected override void Reset()
    {
        course = new Random(CourseSeed());
        round = 0;
        points = freeMisses = 0;
        clockLeft = StartTime;
        frozenLeft = charge = happy = roundOver = 0;
        misses.Clear();
        NextRound();
    }

    // ---- Puzzles --------------------------------------------------------------

    void NextRound()
    {
        round++;
        found = 0;
        roundOver = 0;
        changes.Clear();
        before = Write();
        after = before.Select(l => new Line { Indent = l.Indent, Tokens = l.Tokens.Select(t => new Token { X = t.X, W = t.W, Ink = t.Ink }).ToList() }).ToList();
        // A change on each of some lines that have code on them
        int count = Math.Min(9, 3 + round);
        var lines = Enumerable.Range(0, Lines).Where(i => before[i].Tokens.Count > 0).OrderBy(_ => course.Next()).Take(count).ToList();
        bool subtle = round >= 3;
        foreach (int i in lines) changes.Add(MakeChange(i, subtle && course.NextDouble() < 0.2 + round * 0.08));
    }

    /// <summary>A screen of made-up code: indented lines of tokens in syntax colours, some blank.</summary>
    List<Line> Write()
    {
        var doc = new List<Line>();
        int indent = 0;
        for (int i = 0; i < Lines; i++)
        {
            var line = new Line();
            doc.Add(line);
            if (course.NextDouble() < 0.12) continue;
            indent = Math.Clamp(indent + course.Next(3) - 1, 0, 3);
            line.Indent = indent;
            double x = Gutter + indent * 6;
            int tokens = 1 + course.Next(6);
            for (int k = 0; k < tokens; k++)
            {
                double w = 4 + course.Next(19);
                if (x + w > PanelW - 26) break;
                line.Tokens.Add(new Token { X = x, W = w, Ink = course.Next(7) });
                x += w + 3;
            }
        }
        return doc;
    }

    static Rect TokenRect(int line, Token t) => new(t.X, 4 + line * LineH + (LineH - TokenH) / 2, t.W, TokenH);

    /// <summary>Changes line i of the right-hand side, and says where.</summary>
    Change MakeChange(int i, bool subtle)
    {
        var line = after[i];
        var token = line.Tokens[course.Next(line.Tokens.Count)];
        var area = TokenRect(i, token);
        switch (course.Next(subtle ? 2 : 5))
        {
            case 0: // another colour, or the same a shade off
                token.Ink = subtle ? (token.Ink + 7) % 14 : (token.Ink + 1 + course.Next(6)) % 7;
                break;
            case 1: // wider or narrower
            {
                int k = line.Tokens.IndexOf(token);
                double room = (k + 1 < line.Tokens.Count ? line.Tokens[k + 1].X : PanelW - 4) - (token.X + token.W) - 2;
                double by = subtle ? 3 : 6 + course.Next(5);
                if (room >= by) token.W += by;
                else token.W = Math.Max(2, token.W - by);
                area = Rect.Union(area, TokenRect(i, token));
                break;
            }
            case 2: // gone
                line.Tokens.Remove(token);
                break;
            case 3: // one more at the end
            {
                var last = line.Tokens[^1];
                double w = 6 + course.Next(9);
                if (last.X + last.W + 3 + w <= PanelW - 4)
                {
                    var extra = new Token { X = last.X + last.W + 3, W = w, Ink = course.Next(7) };
                    line.Tokens.Add(extra);
                    area = TokenRect(i, extra);
                }
                else
                {
                    line.Tokens.Remove(token);
                }
                break;
            }
            default: // indented differently: the whole line moves
            {
                double by = line.Indent > 0 && course.Next(2) == 0 ? -6 : 6;
                var span = Rect.Union(TokenRect(i, line.Tokens[0]), TokenRect(i, line.Tokens[^1]));
                foreach (var t in line.Tokens) t.X += by;
                line.Indent += by > 0 ? 1 : -1;
                area = Rect.Union(span, Rect.Offset(span, by, 0));
                break;
            }
        }
        return new Change { Area = area };
    }

    // ---- Control --------------------------------------------------------------

    public override bool Press(Key key, bool repeat)
    {
        if (State != GameState.Running) return base.Press(key, repeat);
        if (key is Key.X or Key.E or Key.LeftShift or Key.RightShift && !repeat) { UsePower(); return true; }
        return false;
    }

    public override void Click(Point at, bool right)
    {
        if (State != GameState.Running) { base.Click(at, right); return; }
        if (right) { UsePower(); return; }
        if (roundOver > 0) return;
        // Which panel, and where in it
        int side = at.X >= PanelX[1] ? 1 : 0;
        var local = new Point(at.X - PanelX[side], at.Y - PanelY);
        if (local.X < 0 || local.X > PanelW || local.Y < 0 || local.Y > Lines * LineH + 8) return;
        var hit = changes.FirstOrDefault(c => !c.Found && Inflate(c.Area).Contains(local));
        if (hit != null)
        {
            Find(hit, full: true);
            return;
        }
        if (changes.Any(c => c.Found && Inflate(c.Area).Contains(local))) return; // already found: no harm
        misses.Add((at, 0));
        if (freeMisses > 0)
        {
            freeMisses--;
            Popup("FREE MISS", at.X, at.Y - 4, MeterFill);
            return;
        }
        clockLeft -= MissTime;
        Popup($"-{MissTime:0} s", at.X, at.Y - 4, BadInk);
        if (clockLeft <= 0) TimeUp();
    }

    static Rect Inflate(Rect r)
    {
        r.Inflate(3, 3);
        return r;
    }

    void Find(Change c, bool full)
    {
        c.Found = true;
        found++;
        happy = 0.6;
        int won = (full ? 50 : 25) * round;
        points += won;
        clockLeft += FindTime;
        charge = Math.Min(1, charge + FindCharge);
        foreach (double px in PanelX)
            Burst(GameArt.Orb, GameArt.OrbInk, px + c.Area.X + c.Area.Width / 2, PanelY + c.Area.Y, 3, falls: false);
        Popup($"+{won}", PanelX[1] + c.Area.X + c.Area.Width / 2, PanelY + c.Area.Y - 2);
        if (found < changes.Count) return;
        int bonus = 200 * round;
        points += bonus;
        clockLeft += RoundTime;
        roundOver = 1.2;
        Popup($"ROUND {round} CLEAR +{bonus}", FieldWidth / 2, PanelY + 40, GoodInk);
    }

    void UsePower()
    {
        if (charge < 1) return;
        charge = 0;
        switch (Family)
        {
            case PowerFamily.Clear:
                var left = changes.Where(c => !c.Found).OrderBy(_ => rng.Next()).FirstOrDefault();
                if (left != null) { left.Hinted = true; Find(left, full: false); }
                break;
            case PowerFamily.Slow:
                frozenLeft = 8;
                Popup("CLOCK STOPPED", FieldWidth / 2, 26, MeterFill);
                break;
            case PowerFamily.Guard:
                freeMisses = 3;
                Popup("3 FREE MISSES", FieldWidth / 2, 26, MeterFill);
                break;
            case PowerFamily.Boost:
                clockLeft += 10;
                Popup("+10 s", FieldWidth / 2, 26, GoodInk);
                break;
        }
    }

    void TimeUp()
    {
        clockLeft = 0;
        foreach (var c in changes.Where(c => !c.Found)) c.Hinted = true; // show what was left
        Lose($"Time's up in round {round} · Space to try again");
    }

    // ---- Simulation -----------------------------------------------------------

    protected override void Step(double dt)
    {
        happy = Math.Max(0, happy - dt);
        for (int i = misses.Count - 1; i >= 0; i--)
        {
            misses[i] = (misses[i].At, misses[i].Age + dt);
            if (misses[i].Age > 0.6) misses.RemoveAt(i);
        }
        if (State != GameState.Running) return;
        charge = Math.Min(1, charge + ChargePerSecond * dt);
        if (roundOver > 0)
        {
            roundOver -= dt;
            if (roundOver <= 0) NextRound();
            return;
        }
        if (frozenLeft > 0) frozenLeft = Math.Max(0, frozenLeft - dt);
        else clockLeft -= dt;
        if (clockLeft <= 0) TimeUp();
    }

    /// <summary>Clicks a change every so often, and misses now and then.</summary>
    protected override void Pilot()
    {
        if (State == GameState.Ready) { StartOrResume(); return; }
        if (State != GameState.Running || time < pilotNext) return;
        pilotNext = time + 1.4;
        if (charge >= 1) UsePower();
        var c = changes.FirstOrDefault(c => !c.Found);
        if (c == null) return;
        int side = rng.Next(2);
        var at = rng.NextDouble() < 0.15
            ? new Point(PanelX[side] + 20 + rng.NextDouble() * 140, PanelY + 10 + rng.NextDouble() * 80)
            : new Point(PanelX[side] + c.Area.X + c.Area.Width / 2, PanelY + c.Area.Y + c.Area.Height / 2);
        Click(at, false);
    }

    // ---- Drawing --------------------------------------------------------------

    protected override void Draw(DrawingContext dc)
    {
        double panelH = Lines * LineH + 8;
        for (int side = 0; side < 2; side++)
        {
            double x0 = PanelX[side];
            dc.DrawRectangle(PanelInk, null, new Rect(x0, PanelY, PanelW, panelH));
            dc.DrawRectangle(GutterInk, null, new Rect(x0 + Gutter - 4, PanelY, 1, panelH));
            var doc = side == 0 ? before : after;
            for (int i = 0; i < Lines; i++)
            {
                dc.DrawRectangle(GutterInk, null, new Rect(x0 + 3, PanelY + 4 + i * LineH + 3, 4, 2));
                if (State == GameState.Ready) continue; // no peeking before the clock starts
                foreach (var t in doc[i].Tokens)
                {
                    var r = TokenRect(i, t);
                    dc.DrawRectangle(Syntax[t.Ink], null, new Rect(x0 + r.X, PanelY + r.Y, r.Width, r.Height));
                }
            }
            foreach (var c in changes.Where(c => c.Found || c.Hinted))
            {
                var r = Inflate(c.Area);
                dc.DrawRectangle(null, c.Hinted ? HintPen : FoundPen, new Rect(Math.Round(x0 + r.X) + 0.5, Math.Round(PanelY + r.Y) + 0.5, Math.Round(r.Width), Math.Round(r.Height)));
            }
        }
        dc.DrawText(Text("BEFORE", Label, 6, DimInk), new Point(PanelX[0], PanelY - 8));
        dc.DrawText(Text("AFTER", Label, 6, DimInk), new Point(PanelX[1], PanelY - 8));

        foreach (var (at, age) in misses)
        {
            dc.PushOpacity(1 - age / 0.6);
            dc.DrawLine(MissPen, new Point(at.X - 3, at.Y - 3), new Point(at.X + 3, at.Y + 3));
            dc.DrawLine(MissPen, new Point(at.X - 3, at.Y + 3), new Point(at.X + 3, at.Y - 3));
            dc.Pop();
        }

        // The clock: a bar between the panels' labels and the HUD
        double frac = Math.Clamp(clockLeft / StartTime, 0, 1);
        dc.DrawRectangle(MeterBack, null, new Rect(12, 22, 356, 2));
        dc.DrawRectangle(frozenLeft > 0 ? MeterFill : clockLeft < 10 ? BadInk : TextInk, null, new Rect(12, 22, Math.Round(356 * frac), 2));
        var clock = Text($"{Math.Max(0, Math.Ceiling(clockLeft))} s", Font, 7, clockLeft < 10 ? BadInk : TextInk);
        dc.DrawText(clock, new Point(368 - clock.Width, PanelY - 9));
        var progress = Text($"ROUND {round} · {found}/{changes.Count} FOUND" + (freeMisses > 0 ? $" · {freeMisses} FREE" : ""), Label, 6, DimInk);
        DrawCentered(dc, progress, 13);

        // The mascot, between the two sides
        double mh = Math.Min(14, 14 * Mascot.Height / Mascot.Width), mw = MascotWidthFor(mh);
        DrawMascot(dc, FieldWidth / 2 - mw / 2, PanelY + panelH, mh, arms: happy > 0 ? 3 : 0,
                   eyes: State == GameState.Over ? EyeLook.Closed : happy > 0 ? EyeLook.Happy : clockLeft < 10 ? EyeLook.Wide : EyeLook.Open);

        DrawBits(dc);
        DrawPops(dc);
        DrawCourse(dc);
        DrawScore(dc);
        DrawMeter(dc, charge, frozenLeft > 0 ? "ON" : freeMisses > 0 ? "UP" : "");
    }
}
