using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeSessions;

public enum GameState { Ready, Running, Paused, Over }

/// <summary>What a mascot's power does in the games past the runner, which gives every power its own effect.</summary>
public enum PowerFamily { Clear, Slow, Guard, Boost }

/// <summary>
/// What the games share: a pixel field drawn at Zoom times, the render loop, ready / running / paused /
/// over, the best score, the daily course and who plays. Each game moves itself in Step and draws itself
/// in Draw, both in field pixels; the window passes keys and clicks on.
/// </summary>
public abstract class ArcadeGame : FrameworkElement
{
    // The playfield, in pixel units (drawn at Zoom times); the same for every game so the window keeps its size
    public const double FieldWidth = 380, FieldHeight = 140, Zoom = 2;
    const double BitGravity = 600, PopLife = 0.9;

    protected static readonly Brush Sky = Ink.Of(0x17, 0x18, 0x1C);
    protected static readonly Brush Veil = Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0x17, 0x18, 0x1C)));
    protected static readonly Brush GroundInk = Ink.Of(0x3A, 0x3D, 0x45);
    protected static readonly Brush PebbleInk = Ink.Of(0x2C, 0x2E, 0x34);
    protected static readonly Brush CloudInk = Ink.Of(0x2A, 0x2C, 0x32);
    protected static readonly Brush TextInk = Ink.Of(0xC9, 0xCA, 0xCF);
    protected static readonly Brush DimInk = Ink.Of(0x7D, 0x80, 0x8A);
    protected static readonly Brush MeterBack = Ink.Of(0x2C, 0x2E, 0x34);
    protected static readonly Brush MeterFill = Ink.Of(0x60, 0xA5, 0xFA);
    protected static readonly Brush MeterReady = Ink.Of(0xF5, 0xD2, 0x4A);
    protected static readonly Brush WhiteInk = Ink.Of(0xF0, 0xEE, 0xE9);
    protected static readonly Brush GoodInk = Ink.Of(0x5F, 0xC2, 0x7E);
    protected static readonly Brush BadInk = Ink.Of(0xE0, 0x60, 0x5A);
    protected static readonly Typeface Font = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    protected static readonly Typeface Label = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    protected sealed class Bit { public double X, Y, VX, VY, Age, Life; public string[] Shape = Array.Empty<string>(); public Brush Ink = Brushes.White; public bool Falls; }
    sealed class Pop { public string Text = ""; public double X, Y, Age; public Brush Ink = Brushes.White; }

    protected readonly Random rng = new(); // looks only: bits, how things fly off
    protected readonly List<Bit> bits = new();
    readonly List<Pop> pops = new();
    readonly Stopwatch clock = new();
    TimeSpan lastTick;
    bool looping, daily;
    Mascot mascot = Mascot.For(MascotKind.Dib);
    double overAt;
    /// <summary>Seconds since the game opened, paused or not.</summary>
    protected double time;

    protected ArcadeGame()
    {
        Width = FieldWidth * Zoom;
        Height = FieldHeight * Zoom;
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        Loaded += (_, _) => StartLoop();
        Unloaded += (_, _) => StopLoop();
    }

    /// <summary>The game's key in the settings and its daily seed.</summary>
    public abstract string Id { get; }
    /// <summary>Its tab in the window.</summary>
    public abstract string Title { get; }
    /// <summary>Its keys, for the line under the game and the start screen.</summary>
    public abstract string Keys { get; }
    /// <summary>How to score, shown between runs.</summary>
    protected abstract string Hint { get; }
    protected virtual string StartMessage => "Press Space to start";

    public GameState State { get; protected set; } = GameState.Ready;
    public string Message { get; protected set; } = "";
    public abstract int Score { get; }
    public int Best { get; set; }
    /// <summary>The day the course is for, when <see cref="Daily"/> is on.</summary>
    public DateTime Day { get; private set; } = DateTime.Today;

    /// <summary>Today's course, the same on every run all day, instead of a new one each run. Switching starts over.</summary>
    public bool Daily
    {
        get => daily;
        set
        {
            daily = value;
            if (State is GameState.Running or GameState.Paused) return; // the next run takes it
            NewRun();
            InvalidateVisual();
        }
    }

    /// <summary>Plays itself, for the off-screen recordings and checks.</summary>
    public bool Autopilot { get; set; }
    /// <summary>Starts over on the start screen, even mid-run (for the off-screen check).</summary>
    internal void Restart() => NewRun();
    /// <summary>Plays this course every run, for the off-screen check.</summary>
    internal int? FixedSeed { get; set; }

    public Mascot Mascot
    {
        get => mascot;
        set { mascot = value; InvalidateVisual(); }
    }

    public event Action<int>? GameOver;

    protected PowerFamily Family => mascot.Power switch
    {
        Power.Fire or Power.Chomp or Power.Honk => PowerFamily.Clear,
        Power.SlowMo or Power.Glide => PowerFamily.Slow,
        Power.Phase or Power.Burrow or Power.Shield => PowerFamily.Guard,
        _ => PowerFamily.Boost,
    };

    /// <summary>Back to the start screen with a fresh course.</summary>
    protected void NewRun()
    {
        State = GameState.Ready;
        Reset();
        Message = StartMessage;
    }

    /// <summary>Everything back to the start of a run; take the course from <see cref="CourseSeed"/>.</summary>
    protected abstract void Reset();

    /// <summary>A seed for the next course: on the daily course the day's (its own for each game), else a new one.</summary>
    protected int CourseSeed()
    {
        Day = DateTime.Today;
        if (FixedSeed is { } seed) return seed;
        if (!daily) return rng.Next();
        int salt = 17;
        foreach (char c in Id) salt = salt * 31 + c; // not string.GetHashCode, which changes every run of the app
        return (Day.Year * 10000 + Day.Month * 100 + Day.Day) ^ salt;
    }

    // ---- Control --------------------------------------------------------------

    /// <summary>A key went down; true if the game used it.</summary>
    public virtual bool Press(Key key, bool repeat) => key == Key.Space && !repeat && StartOrResume();

    public virtual void Release(Key key) { }

    /// <summary>A click, at a spot in field pixels.</summary>
    public virtual void Click(Point at, bool right)
    {
        if (!right) StartOrResume();
    }

    public virtual void ClickUp(Point at, bool right) { }

    public virtual void Hover(Point at) { }

    /// <summary>Space or a click while not playing: starts, plays again (after a moment, so a late press doesn't), or goes on. False while playing.</summary>
    protected bool StartOrResume()
    {
        switch (State)
        {
            case GameState.Ready:
                State = GameState.Running;
                Message = "";
                return true;
            case GameState.Over:
                if (time - overAt <= 0.5) return true;
                Reset();
                State = GameState.Running;
                Message = "";
                return true;
            case GameState.Paused:
                Resume();
                return true;
            default:
                return false;
        }
    }

    public void Pause(string message)
    {
        if (State == GameState.Running) State = GameState.Paused;
        if (State == GameState.Paused) Message = message;
        InvalidateVisual();
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Running;
        Message = "";
        lastTick = clock.Elapsed;
    }

    public void TogglePause()
    {
        if (State == GameState.Running) Pause("Paused · Space to go on");
        else Resume();
    }

    protected void Lose(string message = "Game over · Space to play again")
    {
        State = GameState.Over;
        overAt = time;
        Message = message;
        if (Score > Best) Best = Score;
        GameOver?.Invoke(Score);
    }

    // ---- Simulation -----------------------------------------------------------

    void StartLoop()
    {
        if (looping) return;
        looping = true;
        clock.Start();
        lastTick = clock.Elapsed;
        CompositionTarget.Rendering += OnFrame;
    }

    void StopLoop()
    {
        if (!looping) return;
        looping = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = clock.Elapsed;
        double dt = Math.Min(0.05, (now - lastTick).TotalSeconds);
        lastTick = now;
        if (dt <= 0) return;
        Tick(dt);
        InvalidateVisual();
    }

    /// <summary>Moves the game forward; the render loop calls it, the off-screen flags call it directly.</summary>
    public void Tick(double dt)
    {
        time += dt;
        if (Autopilot)
        {
            if (State == GameState.Over && time - overAt > 1.5) StartOrResume();
            Pilot();
        }
        AgeBits(dt);
        Step(dt);
    }

    /// <summary>One step of the game; it runs while paused or over too, so check <see cref="State"/>.</summary>
    protected abstract void Step(double dt);

    /// <summary>Plays a move when <see cref="Autopilot"/> is on.</summary>
    protected virtual void Pilot() { }

    protected void Burst(string[] shape, Brush ink, double x, double y, int count, bool falls)
    {
        for (int i = 0; i < count; i++)
            bits.Add(new Bit { Shape = shape, Ink = ink, X = x, Y = y, VX = (rng.NextDouble() - 0.5) * 90, VY = -40 - rng.NextDouble() * 60, Life = 0.6, Falls = falls });
    }

    /// <summary>Text that rises and fades where something was won (or lost).</summary>
    protected void Popup(string text, double x, double y, Brush? ink = null) =>
        pops.Add(new Pop { Text = text, X = x, Y = y, Ink = ink ?? MeterReady });

    void AgeBits(double dt)
    {
        foreach (var b in bits)
        {
            b.Age += dt;
            b.X += b.VX * dt;
            b.Y += b.VY * dt;
            if (b.Falls) b.VY += BitGravity * dt;
        }
        bits.RemoveAll(b => b.Age >= b.Life);
        foreach (var p in pops)
        {
            p.Age += dt;
            p.Y -= 18 * dt;
        }
        pops.RemoveAll(p => p.Age >= PopLife);
    }

    // ---- Drawing --------------------------------------------------------------

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Sky, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.PushTransform(new ScaleTransform(Zoom, Zoom));
        Draw(dc);
        DrawMessage(dc);
        dc.Pop();
    }

    /// <summary>Draws the game, in field pixels; the start, pause and game-over text goes on top by itself.</summary>
    protected abstract void Draw(DrawingContext dc);

    protected FormattedText Text(string s, Typeface face, double size, Brush ink) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip * Zoom);

    protected void DrawCentered(DrawingContext dc, FormattedText text, double y, double center = FieldWidth / 2) =>
        dc.DrawText(text, new Point(Math.Round(center - text.Width / 2), Math.Round(y)));

    protected void DrawBits(DrawingContext dc)
    {
        foreach (var b in bits)
        {
            dc.PushOpacity(Math.Min(1, (1 - b.Age / b.Life) * 2));
            PixelScene.Sprite(dc, b.Shape, Math.Round(b.X), Math.Round(b.Y), 2, c => c == 'X' ? b.Ink : null);
            dc.Pop();
        }
    }

    protected void DrawPops(DrawingContext dc)
    {
        foreach (var p in pops)
        {
            var won = Text(p.Text, Font, 6, p.Ink);
            dc.PushOpacity(Math.Min(1, (1 - p.Age / PopLife) * 2));
            dc.DrawText(won, new Point(Math.Round(Math.Clamp(p.X - won.Width / 2, 2, FieldWidth - 2 - won.Width)), Math.Round(p.Y - won.Height)));
            dc.Pop();
        }
    }

    /// <summary>The score and the best one, top right.</summary>
    protected void DrawScore(DrawingContext dc)
    {
        var score = Text($"{Score:00000}", Font, 9, TextInk);
        var best = Text($"HI {Math.Max(Best, Score):00000}", Font, 9, DimInk);
        dc.DrawText(best, new Point(FieldWidth - 8 - best.Width - 8 - score.Width, 6));
        dc.DrawText(score, new Point(FieldWidth - 8 - score.Width, 6));
    }

    /// <summary>Which course, top middle.</summary>
    protected void DrawCourse(DrawingContext dc)
    {
        var mode = Text(daily ? $"DAILY · {Day.ToString("d MMM", CultureInfo.InvariantCulture).ToUpperInvariant()}" : "FREE PLAY", Label, 7, daily ? MeterReady : DimInk);
        DrawCentered(dc, mode, 4);
    }

    /// <summary>The power meter, top left: the mascot's power, whether it's on, and how full the meter is.</summary>
    protected void DrawMeter(DrawingContext dc, double charge, string state = "")
    {
        bool ready = charge >= 1;
        if (state.Length == 0 && ready) state = "READY · X";
        var name = Text(mascot.PowerName.ToUpperInvariant() + (state.Length > 0 ? "  " + state : ""), Label, 7, ready || state.Length > 0 ? MeterReady : DimInk);
        dc.DrawText(name, new Point(8, 4));
        dc.DrawRectangle(MeterBack, null, new Rect(8, 15, 60, 3));
        dc.DrawRectangle(ready ? MeterReady : MeterFill, null, new Rect(8, 15, Math.Round(60 * Math.Clamp(charge, 0, 1)), 3));
    }

    /// <summary>The mascot, <paramref name="height"/> pixels tall, standing on <paramref name="bottom"/> with its left edge at x.</summary>
    protected void DrawMascot(DrawingContext dc, double x, double bottom, double height, int arms = 0, int legFrame = 0, EyeLook eyes = EyeLook.Open)
    {
        double scale = height / mascot.Height;
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, Math.Round(x), Math.Round(bottom - height)));
        MascotPainter.Draw(dc, mascot, 0, 0, null, arms, arms, legFrame, 1, false, 0, eyes, 0);
        dc.Pop();
    }

    protected double MascotWidthFor(double height) => mascot.Width * height / mascot.Height;

    void DrawMessage(DrawingContext dc)
    {
        if (State == GameState.Running || Message.Length == 0) return;
        var lines = new List<FormattedText> { Text(Message, Label, 9, TextInk) };
        if (State == GameState.Ready) lines.Add(Text(Keys, Label, 6, DimInk));
        if (State is GameState.Ready or GameState.Over)
            lines.Add(Text(Hint + "   D: " + (daily ? "random courses" : "today's course, the same all day"), Label, 6, DimInk));
        double y = 50;
        dc.DrawRectangle(Veil, null, new Rect(0, y - 6, FieldWidth, 16 + (lines.Count - 1) * 10 + 6));
        for (int i = 0; i < lines.Count; i++)
        {
            DrawCentered(dc, lines[i], y);
            y += i == 0 ? 14 : 10;
        }
    }

    protected static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    protected static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}

/// <summary>The sprites the games share: the things that go wrong in a session, and a few pickups.</summary>
static class GameArt
{
    public sealed record Look(string[] Rows, Brush Ink, Brush Detail)
    {
        public double Width(double px) => Rows[0].Length * px;
        public double Height(double px) => Rows.Length * px;
        public void Draw(DrawingContext dc, double x, double y, double px) =>
            PixelScene.Sprite(dc, Rows, Math.Round(x), Math.Round(y), px, c => c == 'X' ? Ink : c == 'O' ? Detail : null);
    }

    public static readonly Look Bug = new(new[] { ".X...X.", "..X.X..", ".XXXXX.", "XXOXOXX", ".XXXXX.", "X.X.X.X" }, Ink.Of(0xE0, 0x60, 0x5A), Ink.Of(0xF0, 0xEE, 0xE9));
    public static readonly Look Error = new(new[] { "...X...", "..XXX..", "..XOX..", ".XXOXX.", ".XXXXX.", "XXXOXXX", "XXXXXXX" }, Ink.Of(0xF5, 0xB8, 0x3D), Ink.Of(0x1E, 0x1F, 0x23));
    public static readonly Look Tower = new(new[] { "XXXXXX", "XOXXOX", "XXXXXX", "XXXXXX", "XOOOOX", "XXXXXX", "XXXXXX", "XOXXOX", "XXXXXX", "XXXXXX", "XXXXXX" }, Ink.Of(0x7A, 0x86, 0xA8), Ink.Of(0xF2, 0x7A, 0x2C));
    public static readonly Look Coffee = new(new[] { "....OO......", "....OOX.....", "..XXXXXXX...", "XXXXXXXXXXXX" }, Ink.Of(0x8B, 0x5A, 0x3C), Ink.Of(0xE6, 0xE7, 0xEA));
    public static readonly Look Spike = new(new[] { "..X..", ".XOX.", "XOOOX", ".XOX.", "..X.." }, Ink.Of(0xB0, 0x7A, 0xE0), Ink.Of(0xE2, 0xCF, 0xF5));
    /// <summary>A merge conflict: two arrows running into each other.</summary>
    public static readonly Look Conflict = new(new[] { "X.....X", "XX...XX", "XOX.XOX", "XOOXOOX", "XOX.XOX", "XX...XX", "X.....X" }, Ink.Of(0xE8, 0x6A, 0xA8), Ink.Of(0xF6, 0xC8, 0xDF));
    public static readonly Look Heart = new(new[] { ".X.X.", "XXXXX", "XXXXX", ".XXX.", "..X.." }, Ink.Of(0xE0, 0x60, 0x5A), Ink.Of(0xF0, 0xEE, 0xE9));

    public static readonly string[] Orb = { ".X.", "XXX", ".X." };
    public static readonly string[] Puff = { "XX", "XX" };
    public static readonly string[] Dot = { "X" };
    public static readonly Brush OrbInk = Ink.Of(0xF5, 0xD2, 0x4A);
    public static readonly Brush OrbGlow = Freeze(new RadialGradientBrush(Color.FromArgb(0x50, 0xF5, 0xD2, 0x4A), Color.FromArgb(0, 0xF5, 0xD2, 0x4A)));

    static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}
