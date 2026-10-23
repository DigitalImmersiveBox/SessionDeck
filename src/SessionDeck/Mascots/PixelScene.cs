using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClaudeSessions;

public enum Mood { Idle, Working, Waiting, Maxed, Sleeping }

public enum Weather { Clear, Rain, Storm }

/// <summary>
/// The little pixel world behind the percentage: day clouds or a night sky, rain when Claude
/// is degraded, and Clawd reacting to your sessions (scuttles while working, hops when a
/// session needs you, celebrates when one finishes, flops down when you hit the limit).
/// Clawd is also a toy: click him for a trick (click fast and he gets dizzy), hover to make
/// him blush, or drag him somewhere and let go. Between jobs he strolls, stretches, waves
/// and dances, butterflies visit by day and fireflies and shooting stars come out at night.
/// </summary>
public sealed partial class PixelScene : FrameworkElement
{
    const double S = 4;          // one cloud pixel
    const double M = 3;          // one moon pixel
    const double Frame = 1 / 12.0;

    static readonly string[][] CloudShapes =
    {
        new[] { "....XXXX....", "..XXXXXXXX..", "XXXXXXXXXXXX" },
        new[] { "..XXX..", "XXXXXXX" },
        new[] { "...XXX....", ".XXXXXXX..", "XXXXXXXXXX" },
        new[] { ".....XXX.......", "..XXXXXXXXXX...", "XXXXXXXXXXXXXXX" },
    };
    static readonly string[] Moon = { ".DDLL.", "DDDLLL", "DDDLLL", "DDDLLL", ".DDLL." };
    static readonly string[] Zed = { "XXXX", "..X.", ".X..", "XXXX" };
    static readonly string[] Bang = { "X", "X", "X", ".", "X" };
    static readonly string[] Heart = { ".X.X.", "XXXXX", ".XXX.", "..X.." };
    static readonly string[] Note = { "..XX", "..X.X", "..X", "XXX", "XX" };
    static readonly string[] Spark = { ".X.", "XXX", ".X." };
    static readonly string[] Bead = { "X", "X" };
    static readonly string[] WingsOpen = { "XX.XX", "XXXXX", ".X.X." };
    static readonly string[] WingsShut = { "..X..", ".XXX.", "..X.." };
    static readonly string[] WavesRight = { "X..X.", ".X..X", ".X..X", "X..X." };
    static readonly string[] WavesLeft = { ".X..X", "X..X.", "X..X.", ".X..X" };
    static readonly string[] Seed = { "X" };
    static readonly string[] Petal = { "XX", "X." };
    static readonly string[] SlashRight = { "...X", "..X.", ".X..", "X..." };
    static readonly string[] SlashLeft = { "X...", ".X..", "..X.", "...X" };
    static readonly string[] Flame = { ".X.", "XXX", ".X." };
    // The bits the knight slices, cut between the third and fourth rows
    static readonly string[][] Bits = { new[] { ".XX.", "X..X", "X..X", "X..X", ".XX." }, new[] { ".X.", "XX.", ".X.", ".X.", "XXX" } };
    const int BitCut = 3;
    const double BitSpeed = 90;

    static readonly Brush DayCloud = Ink.Of(0x4A, 0x4A, 0x50);
    static readonly Brush NightCloud = Ink.Of(0x35, 0x35, 0x3B);
    static readonly Brush StormCloud = Ink.Of(0x3C, 0x3D, 0x45);
    static readonly Brush MoonLight = Ink.Of(0xC9, 0xC9, 0xCE);
    static readonly Brush MoonDark = Ink.Of(0x4A, 0x4A, 0x50);
    static readonly Brush StarInk = Ink.Of(0xE6, 0xE7, 0xEA);
    static readonly Brush RainInk = Ink.Of(0x5B, 0x86, 0xB0);
    static readonly Brush SweatInk = Ink.Of(0x8D, 0xB8, 0xDE);
    static readonly Brush PuffInk = Ink.Of(0x8A, 0x8B, 0x90);
    static readonly Brush ZedInk = Ink.Of(0x9A, 0x9C, 0xA3);
    static readonly Brush NoteInk = Ink.Of(0xC9, 0xCA, 0xCF);
    static readonly Brush HeartInk = Ink.Of(0xF2, 0x78, 0x8C);
    static readonly Brush AlertInk = Ink.Of(0xF5, 0xB8, 0x3D);
    static readonly Brush WingInk = Ink.Of(0xF2, 0xC4, 0x6D);
    static readonly Brush FireflyInk = Ink.Of(0xD8, 0xF2, 0x7A);
    static readonly Brush SeedInk = Ink.Of(0xE8, 0xC2, 0x6A);
    static readonly Brush BitInk = Ink.Of(0x7F, 0xD6, 0xA0);
    static readonly Brush FlameInk = Ink.Of(0xF2, 0x7A, 0x2C);
    static readonly Brush FlameCore = Ink.Of(0xF5, 0xD2, 0x4A);
    static readonly Brush FlashInk = Ink.Of(0xFF, 0xFF, 0xFF);
    static readonly Brush Horizon = Frozen(new RadialGradientBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF)));
    static readonly Brush WorkGlow = Frozen(new RadialGradientBrush(Color.FromArgb(0x50, 0xD9, 0x77, 0x57), Color.FromArgb(0, 0xD9, 0x77, 0x57)));
    static readonly Brush MaxedGlow = Frozen(new RadialGradientBrush(Color.FromArgb(0x38, 0xF0, 0x71, 0x71), Color.FromArgb(0, 0xF0, 0x71, 0x71)));
    static readonly Brush FireflyGlow = Frozen(new RadialGradientBrush(Color.FromArgb(0x60, 0xD8, 0xF2, 0x7A), Color.FromArgb(0, 0xD8, 0xF2, 0x7A)));

    /// <summary>A short body animation; clicks start one, and so does Clawd when he's bored.</summary>
    enum Trick { None, Spin, Squish, Wave, Dance, Signature, Stretch, Dizzy, Story }

    sealed class Cloud { public double X, Y, Speed; public int Shape; }
    // Kind for the fx list: h heart, n note, s spark, w sweat
    // Kind for the fx list also: q sound waves, c seed, p petal, x slash, d a flying bit, u/l its upper and lower halves once cut, f fire
    sealed class Particle { public double X, Y, VX, VY, Age, Life; public char Kind; public int Bit; }
    sealed class Butterfly { public double X, Y, Rest; public bool Leaving; }

    readonly Random rng = new(7);
    readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(Frame) };
    readonly List<Cloud> clouds = new();
    readonly List<(double X, double Y, double Phase)> stars = new(), fireflies = new();
    readonly List<Particle> drops = new(), puffs = new(), zeds = new(), fx = new();

    double time, x = double.NaN, dir = 1;
    double hopLeft, hopLength, hopHeight;
    int hopsQueued, look, idleLook, legFrame;
    double blinkUntil, nextBlink = 2.5, nextLook = 4, nextPuff, nextZed, nextHop, nextFlash = 3, flash;

    Trick trick, lastPoke;
    Step? step;          // the signature step showing now
    int stepIndex = -1;
    double nextBitFight = 4, nextWorkStory = 45;
    double trickLeft, trickLength, nextNote;
    double strollTo = double.NaN, nextIdle = 8, nextSweat = 2;
    double blushUntil, awakeUntil, startledUntil;
    int combo;
    double lastClick = -1;

    // Picked up with the mouse: held follows the cursor, lift is the height above the ground
    bool pressed, held;
    Point pressAt;
    double grabX, grabY, lift, fallSpeed;

    Butterfly? fly;
    double nextFly = 10, shootAge = -1, shootX, shootY, nextShoot = 6;

    Mascot mascot = Mascot.For(MascotKind.Dib);

    /// <summary>Who lives in the scene; switching keeps where he stands and what he's doing.</summary>
    public Mascot Mascot
    {
        get => mascot;
        set
        {
            if (mascot == value) return;
            mascot = value;
            if (trick == Trick.Signature) trick = Trick.None; // the old one's steps don't fit the new one
            InvalidateVisual();
        }
    }

    double P => mascot.P;
    double MascotWidth => mascot.Width;

    public Mood Mood { get; set; }
    public Weather Weather { get; set; }
    public bool Night { get; set; }

    /// <summary>Keeps the sky (clouds, stars, moon, rain) and Clawd right of this x, clear of text drawn over the scene.</summary>
    public double SkyLeft { get; set; }

    /// <summary>Just the mascot and his effects (no clouds, glow or butterflies), free to roam the whole width: the taskbar strip.</summary>
    public bool Bare { get; init; }

    /// <summary>The middle of the mascot across the scene; NaN until he's first placed.</summary>
    public double MascotCenter => x + MascotWidth / 2;

    /// <summary>After each step of the animation (the taskbar strip moves its window along with him).</summary>
    public event Action? Advanced;

    /// <summary>A double-click on him; while anyone listens, it no longer plays his story.</summary>
    public event Action? DoubleClicked;

    /// <summary>The leftmost place he goes on his own.</summary>
    double MinX => Bare ? 4 : Math.Max(ActualWidth * 0.36, SkyLeft + 8);

    public PixelScene()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand; // only over Clawd: HitTestCore ignores the rest of the scene
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        for (int i = 0; i < 24; i++) stars.Add((rng.NextDouble(), rng.NextDouble(), rng.NextDouble() * Math.PI * 2));
        for (int i = 0; i < 5; i++) fireflies.Add((rng.NextDouble(), rng.NextDouble(), rng.NextDouble() * Math.PI * 2));

        // Nothing to animate while the window is minimized
        timer.Tick += (_, _) => { if (Window.GetWindow(this)?.WindowState != WindowState.Minimized) Advance(Frame); };
        IsVisibleChanged += (_, _) => { if (IsVisible) timer.Start(); else timer.Stop(); };
    }

    /// <summary>Two happy hops; called when a session finishes.</summary>
    public void Celebrate() => hopsQueued = 2;

    /// <summary>Moves the scene forward; the timer calls this, --snapshot calls it directly.</summary>
    public void Advance(double seconds)
    {
        for (double left = seconds; left > 1e-6; left -= Frame)
            Update(Math.Min(Frame, left));
        InvalidateVisual();
        Advanced?.Invoke();
    }

    bool Asleep => Mood == Mood.Sleeping && time >= awakeUntil;
    bool Sitting => Mood == Mood.Maxed && !held && lift <= 0;
    bool Busy => held || lift > 0 || trick != Trick.None;

    void Update(double dt)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        time += dt;

        // Sky
        int wantClouds = Bare ? 0 : Weather switch { Weather.Storm => 7, Weather.Rain => 6, _ => Night ? 2 : 3 } + (Mood == Mood.Maxed ? 3 : 0);
        while (clouds.Count < wantClouds)
            clouds.Add(NewCloud(w, h, scattered: time < 1 || clouds.Count == 0));
        double windy = Weather == Weather.Storm ? 3 : 1;
        for (int i = clouds.Count - 1; i >= 0; i--)
        {
            var c = clouds[i];
            c.X += c.Speed * windy * dt;
            if (c.X <= w) continue;
            if (clouds.Count > wantClouds) clouds.RemoveAt(i);
            else clouds[i] = NewCloud(w, h, scattered: false);
        }

        int wantDrops = Weather switch { Weather.Storm => 44, Weather.Rain => 26, _ => 0 };
        while (drops.Count < wantDrops)
            drops.Add(new Particle { X = rng.NextDouble() * w, Y = -rng.NextDouble() * h });
        double fall = Weather == Weather.Storm ? 190 : 140;
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            var d = drops[i];
            d.Y += fall * dt;
            d.X -= (Weather == Weather.Storm ? 30 : 8) * dt;
            if (d.Y < h) continue;
            if (drops.Count > wantDrops) drops.RemoveAt(i);
            else { d.Y = -6; d.X = rng.NextDouble() * (w + 20); }
        }

        if (Weather == Weather.Storm && time >= nextFlash)
        {
            flash = 1;
            nextFlash = time + 4 + rng.NextDouble() * 6;
        }
        flash = Math.Max(0, flash - dt * 3);

        if (Night && Weather == Weather.Clear && shootAge < 0 && time >= nextShoot)
        {
            shootAge = 0;
            shootX = SkyLeft + rng.NextDouble() * Math.Max(1, (w - SkyLeft) * 0.6);
            shootY = 4 + rng.NextDouble() * h * 0.25;
            nextShoot = time + 14 + rng.NextDouble() * 20;
        }
        if (shootAge >= 0 && (shootAge += dt) > 0.7) shootAge = -1;

        // Clawd
        double minX = MinX, maxX = Math.Max(minX, w - MascotWidth - 4);
        if (double.IsNaN(x)) x = Math.Round(w * 0.62 - MascotWidth / 2);
        if (trick != Trick.Story || story?.Roam != true) x = Math.Clamp(x, minX, maxX);
        legFrame = 0;

        if (held)
        {
            legFrame = (int)(time * 12) % 2; // feet kick in the air
            look = 0;
        }
        else if (lift > 0)
        {
            fallSpeed += 900 * dt;
            lift -= fallSpeed * dt;
            if (lift <= 0) Land(h);
        }

        if (Mood != Mood.Idle || held) strollTo = double.NaN;
        // Something new needs him (a session waiting, the limit reached): the story can wait
        if (trick == Trick.Story && Mood != storyMood && Mood is Mood.Waiting or Mood.Maxed) trick = Trick.None;

        switch (Mood)
        {
            case Mood.Working when !Busy:
                x += dir * 24 * dt;
                if (x >= maxX) { x = maxX; dir = -1; }
                else if (x <= minX) { x = minX; dir = 1; }
                legFrame = (int)(time * 7) % 2;
                look = (int)dir;
                if (time >= nextPuff)
                {
                    puffs.Add(new Particle { X = dir > 0 ? x + 2 : x + MascotWidth - 6, Y = h - 5, VX = -dir * 6, Life = 0.6 });
                    nextPuff = time + 0.2;
                }
                if (time >= nextSweat)
                {
                    fx.Add(new Particle { Kind = 'w', X = dir > 0 ? x + 2 * P : x + MascotWidth - 3 * P, Y = MascotTop(h) + 2, VX = -dir * 22, VY = -40, Life = 0.55 });
                    nextSweat = time + 2 + rng.NextDouble() * 2.5;
                }
                // The knight stops now and then to cut down the bits coming at him
                if (mascot.SignatureWhileWorking && time >= nextBitFight)
                {
                    StartSignature();
                    nextBitFight = time + mascot.SignatureLength + 3 + rng.NextDouble() * 4;
                }
                // And every minute or two he takes a break for his story
                else if (time >= nextWorkStory)
                {
                    StartStory();
                    nextWorkStory = time + 60 + rng.NextDouble() * 60;
                }
                break;
            case Mood.Waiting when !Busy:
                look = 0;
                if (hopLeft <= 0 && time >= nextHop)
                {
                    Hop(6, 0.3);
                    nextHop = time + 1.1;
                }
                break;
            case Mood.Sleeping:
                if (!held) look = 0;
                if (Asleep && time >= nextZed)
                {
                    zeds.Add(new Particle { X = x + MascotWidth - 10, Y = MascotTop(h) - 6, Life = 2.4 });
                    nextZed = time + 1.2;
                }
                break;
            case Mood.Idle when !held:
                if (time >= nextLook)
                {
                    idleLook = rng.Next(-1, 2);
                    nextLook = time + 2 + rng.NextDouble() * 4;
                }
                look = Gaze() ?? idleLook;
                if (!double.IsNaN(strollTo)) Stroll(dt);
                else if (!Busy && time >= nextIdle) Fidget(minX, maxX);
                break;
        }

        if (hopsQueued > 0 && hopLeft <= 0 && Mood != Mood.Maxed && !held && lift <= 0)
        {
            Hop(12, 0.42);
            hopsQueued--;
            Burst('s', h, 3);
        }
        hopLeft = Math.Max(0, hopLeft - dt);

        if (trick != Trick.None)
        {
            if (trick == Trick.Dance)
            {
                if (hopLeft <= 0) Hop(4, 0.25);
                look = (int)(time * 4) % 2 * 2 - 1;
                if (time >= nextNote)
                {
                    Burst('n', h, 1);
                    nextNote = time + 0.4;
                }
            }
            else if (trick == Trick.Dizzy) look = (int)(time * 10) % 2 * 2 - 1;
            else if (trick == Trick.Signature) PlayStep(h);
            else if (trick == Trick.Story) AdvanceStory(dt);
            if ((trickLeft -= dt) <= 0) trick = Trick.None;
        }
        if (trick != Trick.Story && story != null) EndStory();

        if (time >= nextBlink)
        {
            blinkUntil = time + 0.14;
            nextBlink = time + 2.5 + rng.NextDouble() * 3;
        }

        UpdateButterfly(dt, w, h);

        foreach (var p in fx)
        {
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            if (p.Kind is 'w' or 'c' or 'u' or 'l' or 'D') p.VY += 220 * dt;
            else if (p.Kind == 'p') p.VY += 80 * dt;
        }
        Age(puffs, dt);
        Age(zeds, dt);
        Age(fx, dt);
    }

    /// <summary>Where Clawd wants to look: at the mouse while it's in the window, else at a butterfly.</summary>
    int? Gaze()
    {
        if (IsMouseOver) return 0;
        double cx = x + MascotWidth / 2;
        double? target = Window.GetWindow(this)?.IsMouseOver == true ? Mouse.GetPosition(this).X : fly?.X;
        if (target is not { } t) return null;
        return Math.Abs(t - cx) < 20 ? 0 : Math.Sign(t - cx);
    }

    /// <summary>Something to do when nothing's going on.</summary>
    void Fidget(double minX, double maxX)
    {
        nextIdle = time + 6 + rng.NextDouble() * 7;
        double r = rng.NextDouble();
        if (r < 0.4) strollTo = Math.Round(minX + rng.NextDouble() * (maxX - minX));
        else if (r < 0.55) StartSignature();
        else if (r < 0.68) StartStory();
        else if (r < 0.76) StartTrick(Trick.Stretch, 1.4);
        else if (r < 0.85) StartTrick(Trick.Wave, 1.3);
        else if (r < 0.92) { StartTrick(Trick.Spin, 0.5); Hop(6, 0.5); }
        else StartTrick(Trick.Dance, 2.4);
    }

    /// <summary>Plays the mascot's own move now (also for --signatures).</summary>
    public void PlaySignature()
    {
        strollTo = double.NaN;
        StartSignature();
    }

    void StartSignature()
    {
        step = null;
        stepIndex = -1;
        StartTrick(Trick.Signature, mascot.SignatureLength);
    }

    bool Flipped => mascot.FacesRight && (look != 0 ? look : dir) < 0;

    /// <summary>Moves to the step the signature has reached, popping its effect as it starts.</summary>
    void PlayStep(double h)
    {
        var steps = mascot.Signature;
        double at = trickLength - trickLeft, start = 0;
        int i = 0;
        while (i < steps.Length - 1 && at >= start + steps[i].Seconds) start += steps[i++].Seconds;
        if (i == stepIndex) return;
        stepIndex = i;
        step = steps[i];
        if (step.Fx != '\0') Emit(step, i, h);
    }

    /// <summary>A step's effect, from where FxAt puts it on the mascot (mirrored when he faces left).</summary>
    void Emit(Step s, int index, double h)
    {
        bool flip = Flipped;
        int facing = flip ? -1 : 1;
        double left = Math.Round(x) + s.Dx * P * facing, top = MascotTop(h) - s.Lift * P;
        double fx0 = flip ? left + MascotWidth - s.FxAt.Col * P : left + s.FxAt.Col * P;
        double fy0 = top + s.FxAt.Row * P;
        switch (s.Fx)
        {
            case 'q':
                // Sound goes out the front, or both ways from a mascot facing you
                if (mascot.FacesRight) fx.Add(new Particle { Kind = 'q', X = fx0, Y = fy0, VX = facing * 26, Life = 0.6 });
                else
                {
                    fx.Add(new Particle { Kind = 'q', X = left - 6, Y = fy0, VX = -26, Life = 0.6 });
                    fx.Add(new Particle { Kind = 'q', X = left + MascotWidth + 2, Y = fy0, VX = 26, Life = 0.6 });
                }
                break;
            case 'c':
            case 'p':
                for (int i = 0; i < (s.Fx == 'c' ? 3 : 4); i++)
                    fx.Add(new Particle { Kind = s.Fx, X = fx0, Y = fy0, VX = (rng.NextDouble() - 0.5) * (s.Fx == 'c' ? 30 : 50), VY = -25 - rng.NextDouble() * 20, Life = s.Fx == 'c' ? 0.7 : 1.1 });
                break;
            case 'x':
                fx.Add(new Particle { Kind = 'x', X = fx0 - 4, Y = fy0, VX = facing * 14, Life = 0.3 });
                // Every bit on its way is cut in two
                foreach (var bit in fx.FindAll(p => p.Kind == 'd'))
                {
                    fx.Remove(bit);
                    fx.Add(new Particle { Kind = 'u', Bit = bit.Bit, X = bit.X, Y = bit.Y, VX = facing * 18, VY = -60, Life = 0.6 });
                    fx.Add(new Particle { Kind = 'l', Bit = bit.Bit, X = bit.X, Y = bit.Y + BitCut * 2, VX = facing * 30, VY = 10, Life = 0.6 });
                }
                break;
            case 'f':
                // A puff of fire out of the mouth, orange and yellow
                for (int i = 0; i < 3; i++)
                    fx.Add(new Particle { Kind = 'f', Bit = rng.Next(2), X = fx0, Y = fy0 + (rng.NextDouble() - 0.5) * 6, VX = facing * (70 + rng.NextDouble() * 40), VY = (rng.NextDouble() - 0.5) * 24, Life = 0.45 });
                break;
            case 'd':
                // A 0 or 1 flying in from ahead, timed to reach the blade on the next slash
                double eta = 0;
                for (int i = index; i < mascot.Signature.Length && mascot.Signature[i].Fx != 'x'; i++) eta += mascot.Signature[i].Seconds;
                fx.Add(new Particle { Kind = 'd', Bit = rng.Next(2), X = fx0 + facing * BitSpeed * eta, Y = fy0, VX = -facing * BitSpeed, Life = eta + 0.4 });
                break;
            default:
                fx.Add(new Particle { Kind = s.Fx, X = fx0 - 4, Y = fy0 - 6, VY = -22 - rng.NextDouble() * 10, Life = s.Fx == 's' ? 0.7 : 1.3 });
                break;
        }
    }

    void Stroll(double dt)
    {
        double step = 16 * dt, gap = strollTo - x;
        if (Math.Abs(gap) <= step)
        {
            x = strollTo;
            strollTo = double.NaN;
            return;
        }
        dir = Math.Sign(gap);
        x += dir * step;
        legFrame = (int)(time * 5) % 2;
        look = (int)dir;
    }

    void UpdateButterfly(double dt, double w, double h)
    {
        if (fly == null)
        {
            if (!Bare && !Night && Weather == Weather.Clear && Mood != Mood.Maxed && time >= nextFly)
                fly = new Butterfly { X = w + 6, Y = Math.Round(h * 0.3) };
            return;
        }

        // Perches on Clawd's head and takes off as soon as he moves
        double headX = Math.Round(x) + MascotWidth / 2 - 5, headY = MascotTop(h) - 6;
        if (fly.Rest > 0)
        {
            fly.X = headX;
            fly.Y = headY;
            fly.Rest -= dt;
            if (fly.Rest <= 0 || hopLeft > 0 || Busy || !double.IsNaN(strollTo) || Mood == Mood.Working)
            {
                fly.Rest = 0;
                fly.Leaving = true;
            }
            return;
        }

        double tx = fly.Leaving ? w + 20 : headX, ty = fly.Leaving ? -12 : headY;
        double dx = tx - fly.X, dy = ty - fly.Y, dist = Math.Sqrt(dx * dx + dy * dy);
        if (!fly.Leaving && dist < 3)
        {
            fly.Rest = 3 + rng.NextDouble() * 3;
            return;
        }
        double speed = fly.Leaving ? 34 : 26;
        fly.X += dx / dist * speed * dt;
        fly.Y += dy / dist * speed * dt + Math.Sin(time * 9) * 22 * dt;
        if (fly.Leaving && (fly.X > w + 10 || fly.Y < -10))
        {
            fly = null;
            nextFly = time + 25 + rng.NextDouble() * 35;
        }
    }

    void Hop(double height, double length)
    {
        hopHeight = height;
        hopLength = hopLeft = length;
    }

    double HopOffset()
    {
        if (hopLeft <= 0) return 0;
        double phase = 1 - hopLeft / hopLength;
        return hopHeight * 4 * phase * (1 - phase);
    }

    void StartTrick(Trick t, double length)
    {
        trick = t;
        trickLength = trickLeft = length;
    }

    double TrickPhase => trickLength > 0 ? 1 - trickLeft / trickLength : 1;

    double MascotTop(double h) => Math.Round(h - mascot.Height - HopOffset() - lift - (story?.Rise ?? 0) + (Sitting ? Mascot.LegRows * P : 0));

    /// <summary>A few fx particles popping out of Clawd's head.</summary>
    void Burst(char kind, double h, int count)
    {
        double cx = x + MascotWidth / 2, top = MascotTop(h);
        for (int i = 0; i < count; i++)
        {
            double spread = count == 1 ? (rng.NextDouble() - 0.5) * 2 : i - (count - 1) / 2.0;
            fx.Add(new Particle
            {
                Kind = kind,
                X = cx - 4 + spread * 9,
                Y = top - 6,
                VX = spread * 10,
                VY = -22 - rng.NextDouble() * 10,
                Life = kind == 's' ? 0.7 : 1.3,
            });
        }
    }

    void Land(double h)
    {
        if (fallSpeed > 120) StartTrick(Trick.Squish, 0.45);
        lift = fallSpeed = 0;
        puffs.Add(new Particle { X = x - 2, Y = h - 5, VX = -10, Life = 0.5 });
        puffs.Add(new Particle { X = x + MascotWidth - 2, Y = h - 5, VX = 10, Life = 0.5 });
    }

    /// <summary>A click on Clawd: a different trick each time, dizzy after a flurry of clicks.</summary>
    void Poke(Point at)
    {
        double h = ActualHeight;
        fx.Add(new Particle { Kind = 's', X = at.X - 3, Y = at.Y - 3, VY = -10, Life = 0.4 });

        combo = time - lastClick < 0.6 ? combo + 1 : 1;
        lastClick = time;
        if (combo >= 5)
        {
            combo = 0;
            strollTo = double.NaN;
            StartTrick(Trick.Dizzy, 2.6);
            return;
        }
        // A double-click plays his story, unless someone else wants it (the taskbar strip opens the dashboard)
        if (combo == 2 && DoubleClicked != null)
        {
            DoubleClicked();
            return;
        }
        if (trick == Trick.Dizzy) return;
        if (combo == 2 && Mood != Mood.Maxed && !(Mood == Mood.Sleeping && Asleep))
        {
            strollTo = double.NaN;
            StartStory();
            return;
        }

        if (Mood == Mood.Sleeping && Asleep)
        {
            awakeUntil = time + 5;
            startledUntil = time + 0.9;
            Hop(10, 0.4);
            return;
        }
        if (Mood == Mood.Maxed)
        {
            StartTrick(Trick.Squish, 0.5);
            puffs.Add(new Particle { X = x + MascotWidth / 2 - 2, Y = MascotTop(h) + 3 * P, VX = dir * 8, Life = 0.9 });
            return;
        }

        strollTo = double.NaN;
        Trick next;
        // His own move most often; None stands for the heart hop
        do next = rng.NextDouble() < 0.4 ? Trick.Signature : (Trick)rng.Next(0, 5);
        while (next == lastPoke);
        lastPoke = next;
        switch (next)
        {
            case Trick.None:
                trick = Trick.None;
                Hop(11, 0.4);
                Burst('h', h, 3);
                blushUntil = time + 1.8;
                break;
            case Trick.Spin:
                StartTrick(Trick.Spin, 0.5);
                Hop(8, 0.5);
                Burst('s', h, 2);
                break;
            case Trick.Squish:
                StartTrick(Trick.Squish, 0.55);
                break;
            case Trick.Wave:
                StartTrick(Trick.Wave, 1.3);
                blushUntil = time + 1.3;
                break;
            case Trick.Signature:
                StartSignature();
                break;
            default:
                StartTrick(Trick.Dance, 2.4);
                nextNote = time;
                break;
        }
    }

    // ---- Mouse ---------------------------------------------------------------

    /// <summary>Only Clawd catches the mouse; clicks anywhere else go to whatever is underneath.</summary>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hit)
    {
        double h = ActualHeight;
        if (h <= 0 || double.IsNaN(x)) return null;
        var body = new Rect(Math.Round(x) - 4, MascotTop(h) - 6, MascotWidth + 8, mascot.Height + 10);
        return held || body.Contains(hit.HitPoint) ? new PointHitTestResult(this, hit.HitPoint) : null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        pressAt = e.GetPosition(this);
        grabX = pressAt.X - x;
        grabY = pressAt.Y - MascotTop(ActualHeight);
        pressed = CaptureMouse();
        e.Handled = true; // a click on Clawd, not a window drag
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!pressed) return;
        var at = e.GetPosition(this);
        if (!held && Math.Abs(at.X - pressAt.X) + Math.Abs(at.Y - pressAt.Y) < 5) return;
        if (!held)
        {
            held = true;
            trick = Trick.None;
            strollTo = double.NaN;
            hopLeft = 0;
            if (fly != null) { fly.Rest = 0; fly.Leaving = true; }
        }
        double h = ActualHeight;
        x = at.X - grabX;
        lift = Math.Clamp(h - mascot.Height - (at.Y - grabY), 0, h - mascot.Height);
        fallSpeed = 0;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!pressed) return;
        bool wasHeld = held;
        pressed = held = false;
        ReleaseMouseCapture();
        if (!wasHeld) Poke(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        pressed = held = false;
    }

    static void Age(List<Particle> list, double dt)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if ((list[i].Age += dt) >= list[i].Life) list.RemoveAt(i);
    }

    Cloud NewCloud(double w, double h, bool scattered)
    {
        int shape = rng.Next(CloudShapes.Length);
        double width = CloudShapes[shape][0].Length * S;
        return new Cloud
        {
            Shape = shape,
            X = scattered ? SkyLeft + rng.NextDouble() * Math.Max(1, w - SkyLeft) - width / 2 : SkyLeft - width - rng.NextDouble() * 60,
            Y = Math.Round(4 + rng.NextDouble() * Math.Max(1, h * 0.45 - 4)),
            Speed = 3 + rng.NextDouble() * 4,
        };
    }

    // ---- Drawing -------------------------------------------------------------

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        if (!Bare) dc.DrawEllipse(Horizon, null, new Point(w * 0.55, h), w * 0.6, 30);

        if (SkyLeft > 0) dc.PushClip(new RectangleGeometry(new Rect(SkyLeft, 0, Math.Max(0, w - SkyLeft), h)));
        if (Night)
        {
            foreach (var (sx, sy, phase) in stars)
            {
                dc.PushOpacity(0.3 + 0.35 * (1 + Math.Sin(time * 1.6 + phase)));
                dc.DrawRectangle(StarInk, null, new Rect(Math.Round(sx * w), Math.Round(sy * (h - 34)), 2, 2));
                dc.Pop();
            }
            Sprite(dc, Moon, Math.Round(Math.Max(w * 0.30, SkyLeft + 24)), 10, M, c => c == 'L' ? MoonLight : c == 'D' ? MoonDark : null);
            if (shootAge >= 0) DrawShootingStar(dc);
        }

        var cloudInk = Weather != Weather.Clear || Mood == Mood.Maxed ? StormCloud : Night ? NightCloud : DayCloud;
        foreach (var c in clouds)
            Sprite(dc, CloudShapes[c.Shape], Math.Round(c.X), c.Y, S, ch => ch == 'X' ? cloudInk : null);

        foreach (var d in drops)
            dc.DrawRectangle(RainInk, null, new Rect(Math.Round(d.X), Math.Round(d.Y), 2, 6));

        if (Night && Weather == Weather.Clear) DrawFireflies(dc, w, h);
        if (SkyLeft > 0) dc.Pop();

        double cx = Math.Round(x) + MascotWidth / 2;
        if (!Bare && Mood == Mood.Working) dc.DrawEllipse(WorkGlow, null, new Point(cx, h), 80, 30);
        else if (!Bare && Mood == Mood.Maxed) dc.DrawEllipse(MaxedGlow, null, new Point(cx, h), 80, 26);

        foreach (var p in puffs)
        {
            double k = p.Age / p.Life;
            dc.PushOpacity(1 - k);
            dc.DrawRectangle(PuffInk, null, new Rect(Math.Round(p.X + p.VX * k), Math.Round(p.Y - k * 6), 4, 4));
            dc.Pop();
        }

        DrawProps(dc, behind: true);
        DrawMascot(dc, h);
        DrawProps(dc, behind: false);

        foreach (var z in zeds)
        {
            double k = z.Age / z.Life;
            dc.PushOpacity(1 - k);
            Sprite(dc, Zed, Math.Round(z.X + k * 10), Math.Round(z.Y - k * 22), 2, c => c == 'X' ? ZedInk : null);
            dc.Pop();
        }

        foreach (var p in fx)
        {
            double k = p.Age / p.Life;
            var (shape, ink) = p.Kind switch
            {
                'h' => (Heart, HeartInk),
                'n' => (Note, NoteInk),
                'w' => (Bead, SweatInk),
                'q' => (p.VX < 0 ? WavesLeft : WavesRight, NoteInk),
                'c' => (Seed, SeedInk),
                'p' => (Petal, HeartInk),
                'x' => (p.VX < 0 ? SlashLeft : SlashRight, FlashInk),
                'd' or 'D' => (Bits[p.Bit], BitInk),
                'u' => (Bits[p.Bit][..BitCut], BitInk),
                'l' => (Bits[p.Bit][BitCut..], BitInk),
                'f' => (Flame, p.Bit == 0 ? FlameInk : FlameCore),
                _ => (Spark, AlertInk),
            };
            double sway = p.Kind is 'h' or 'n' ? Math.Sin(p.Age * 6 + p.X) * 2 : 0;
            dc.PushOpacity(Math.Min(1, (1 - k) * 2));
            Sprite(dc, shape, Math.Round(p.X + sway), Math.Round(p.Y), 2, c => c == 'X' ? ink : null);
            dc.Pop();
        }

        if (fly != null)
        {
            bool flap = fly.Rest > 0 ? (int)(time * 2) % 3 == 0 : (int)(time * 10) % 2 == 0;
            Sprite(dc, flap ? WingsShut : WingsOpen, Math.Round(fly.X), Math.Round(fly.Y), 2, c => c == 'X' ? WingInk : null);
        }

        if (flash > 0)
        {
            dc.PushOpacity(flash * 0.16);
            dc.DrawRectangle(FlashInk, null, new Rect(0, 0, w, h));
            dc.Pop();
        }
    }

    void DrawShootingStar(DrawingContext dc)
    {
        double fade = 1 - shootAge / 0.7;
        double hx = shootX + shootAge * 160, hy = shootY + shootAge * 50;
        for (int i = 0; i < 7; i++)
        {
            dc.PushOpacity(fade * (1 - i / 7.0));
            dc.DrawRectangle(StarInk, null, new Rect(Math.Round(hx - i * 5), Math.Round(hy - i * 1.6), 2, 2));
            dc.Pop();
        }
    }

    void DrawFireflies(DrawingContext dc, double w, double h)
    {
        foreach (var (fxp, fyp, phase) in fireflies)
        {
            double glow = Math.Sin(time * 1.7 + phase * 5);
            if (glow <= 0) continue;
            double px = Math.Round(SkyLeft + fxp * Math.Max(1, w - SkyLeft) + Math.Sin(time * 0.5 + phase) * 18);
            double py = Math.Round(h * 0.5 + fyp * (h * 0.5 - 8) + Math.Cos(time * 0.8 + phase * 2) * 5);
            dc.PushOpacity(glow);
            dc.DrawEllipse(FireflyGlow, null, new Point(px + 1, py + 1), 6, 6);
            dc.DrawRectangle(FireflyInk, null, new Rect(px, py, 2, 2));
            dc.Pop();
        }
    }

    void DrawMascot(DrawingContext dc, double h)
    {
        bool sitting = Sitting;
        // Mascots drawn in profile turn to face where they walk or look
        bool flip = Flipped;
        if (story?.Hidden == true) return;
        var step = trick == Trick.Signature ? this.step : trick == Trick.Story ? story?.Pose : null;
        double left = Math.Round(x) + (step?.Dx ?? 0) * P * (flip ? -1 : 1);
        double top = MascotTop(h) - (step?.Lift ?? 0) * P;
        double cx = left + MascotWidth / 2;
        double phase = TrickPhase;

        // Squash, stretch and spin around his feet; dizzy sways side to side
        double sx = 1, sy = 1, sway = 0;
        switch (trick)
        {
            case Trick.Spin:
                sx = Math.Cos(phase * 2 * Math.PI);
                if (Math.Abs(sx) < 0.15) sx = sx < 0 ? -0.15 : 0.15;
                break;
            case Trick.Squish:
                double squash = 0.28 * Math.Sin(phase * 3 * Math.PI) * (1 - phase);
                sx = 1 + squash;
                sy = 1 - squash;
                break;
            case Trick.Stretch:
                double reach = 0.12 * Math.Sin(phase * Math.PI);
                sx = 1 - reach / 2;
                sy = 1 + reach;
                break;
            case Trick.Dizzy:
                sway = Math.Round(Math.Sin(time * 9) * 3);
                break;
        }
        if (flip) sx = -sx;
        bool transformed = sx != 1 || sy != 1 || sway != 0;
        if (transformed)
        {
            var m = Matrix.Identity;
            m.ScaleAt(sx, sy, cx, top + mascot.Height);
            m.Translate(sway, 0);
            dc.PushTransform(new MatrixTransform(m));
        }

        // Arms: 0 resting, up to 3 raised over his head
        int armL = 0, armR = 0;
        if (held) armL = armR = 3;
        else if (trick == Trick.Wave) armR = (int)(time * 6) % 2 == 0 ? 3 : 1;
        else if (trick == Trick.Dance) { bool beat = (int)(time * 4) % 2 == 0; armL = beat ? 3 : 0; armR = beat ? 0 : 3; }
        else if (trick == Trick.Stretch) armL = armR = phase is > 0.2 and < 0.8 ? 3 : 1;
        else if (Mood == Mood.Waiting && hopLeft > 0) armL = armR = 2;
        if (step?.ArmL is int stepL) armL = stepL;
        if (step?.ArmR is int stepR) armR = stepR;
        if (story?.ArmL is int storyL) armL = storyL;
        if (story?.ArmR is int storyR) armR = storyR;

        bool happy = !Asleep && (time < blushUntil || trick == Trick.Dance || step?.Happy == true || (IsMouseOver && !held));
        bool closed = Asleep || sitting || time < blinkUntil || (trick == Trick.Stretch && phase is > 0.2 and < 0.8);
        bool wide = held || time < startledUntil;
        var eyes = story?.Eyes ?? (happy ? EyeLook.Happy : closed ? EyeLook.Closed : wide ? EyeLook.Wide : EyeLook.Open);
        // Sleeping, he breathes; his legs stay put
        MascotPainter.Draw(dc, mascot, left, top, step, armL, armR, legFrame, (int)dir * (flip ? -1 : 1), sitting,
                           Asleep && Math.Sin(time * 2.2) > 0 ? 1 : 0, eyes, look);

        if (transformed) dc.Pop();

        if ((Mood == Mood.Waiting && (int)(time * 2.5) % 2 == 0) || time < startledUntil)
            Sprite(dc, Bang, Math.Round(cx - 2), top - 19, 3, c => c == 'X' ? AlertInk : null);

        if (trick == Trick.Dizzy || story?.Dizzy == true)
        {
            // Three little stars circling his head
            for (int i = 0; i < 3; i++)
            {
                double a = time * 6 + i * 2 * Math.PI / 3;
                Sprite(dc, Spark, Math.Round(cx + sway - 3 + Math.Cos(a) * 16), Math.Round(top - 8 + Math.Sin(a) * 4), 2,
                       c => c == 'X' ? AlertInk : null);
            }
        }
    }

    /// <summary>Draws a text sprite, one rectangle per horizontal run of the same character.</summary>
    internal static void Sprite(DrawingContext dc, string[] rows, double x, double y, double px, Func<char, Brush?> ink)
    {
        for (int r = 0; r < rows.Length; r++)
        {
            string row = rows[r];
            for (int c = 0; c < row.Length;)
            {
                int start = c;
                char ch = row[c];
                while (c < row.Length && row[c] == ch) c++;
                if (ink(ch) is { } brush)
                    dc.DrawRectangle(brush, null, new Rect(x + start * px, y + r * px, (c - start) * px, px));
            }
        }
    }

    static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}

/// <summary>Three pixel hearts showing how much of the 5-hour limit is left, in half-heart steps.</summary>
public sealed class PixelHearts : FrameworkElement
{
    const double Px = 2;
    const double Gap = 4;

    static readonly string[] Heart =
    {
        ".XX.XX.",
        "XXXXXXX",
        "XXXXXXX",
        ".XXXXX.",
        "..XXX..",
        "...X...",
    };
    static readonly Brush Full = Ink.Of(0xE0, 0x7A, 0x5F);
    static readonly Brush Empty = Ink.Of(0x45, 0x46, 0x4C);

    double value = 3;

    public PixelHearts() => RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

    public double Value
    {
        get => value;
        set
        {
            if (this.value == value) return;
            this.value = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size available) =>
        new(3 * Heart[0].Length * Px + 2 * Gap, Heart.Length * Px);

    protected override void OnRender(DrawingContext dc)
    {
        for (int i = 0; i < 3; i++)
        {
            double fill = Math.Clamp(value - i, 0, 1);
            double left = i * (Heart[0].Length * Px + Gap);
            for (int r = 0; r < Heart.Length; r++)
                for (int c = 0; c < Heart[r].Length; c++)
                {
                    if (Heart[r][c] != 'X') continue;
                    bool lit = fill >= 1 || (fill >= 0.5 && c <= Heart[r].Length / 2);
                    dc.DrawRectangle(lit ? Full : Empty, null, new Rect(left + c * Px, r * Px, Px, Px));
                }
        }
    }
}

static class Ink
{
    public static SolidColorBrush Of(byte r, byte g, byte b) => Of(Color.FromRgb(r, g, b));

    public static SolidColorBrush Of(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
