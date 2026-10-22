using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>
/// A one-button runner starring your mascot, to play while sessions work. He runs on his own;
/// jump over bugs, error signs, merge conflicts and coffee spills, and don't jump into the lag
/// spikes floating overhead. The power meter fills as you go (faster with the ✦ orbs) and lets
/// him use his power. Close calls and orbs raise a combo that multiplies the score; it drains if
/// you play it safe. The daily run is one course for the whole day, the same however you play it.
/// Drawn at twice its size in pixel units; the window adds keys and notices.
/// </summary>
public sealed class RunnerView : FrameworkElement
{
    public enum GameState { Ready, Running, Paused, Over }

    // The playfield, in pixel units (drawn at Zoom times)
    public const double FieldWidth = 380, FieldHeight = 140, Zoom = 2;
    const double GroundY = 122, MascotX = 46;
    const double Gravity = 980, JumpSpeed = 340, CutSpeed = 130;
    const double StartSpeed = 150, MaxSpeed = 330, SpeedUp = 3;
    const double ChargePerSecond = 1 / 16.0, OrbCharge = 0.5;
    const double ComboTime = 5, NearMiss = 6; // seconds a combo step lasts; pixels of room that count as a close call
    const int MaxCombo = 5, CloseBonus = 20, OrbBonus = 10, KnockBonus = 10; // bonuses are times the combo

    enum Kind { Bug, Error, Tower, Coffee, Spike }

    sealed class Obstacle
    {
        public Kind Kind;
        public double X, Y;   // top-left
        public double W, H;
        public bool Dead;
        public double VX, VY; // once scared off or knocked away
        public double Room = double.MaxValue; // least room between him and it while passing it
        public bool Passed, Spoiled;          // spoiled: passed while a power made him untouchable
    }

    sealed class Orb { public double X, Y; public bool Taken, Missed; }

    sealed class Pop { public string Text = ""; public double X, Y, Age; }

    sealed class Bit { public double X, Y, VX, VY, Age, Life; public string[] Shape = Array.Empty<string>(); public Brush Ink = Brushes.White; public bool Falls; }

    static readonly Dictionary<Kind, (string[] Rows, Brush Ink, Brush Detail)> Looks = new()
    {
        [Kind.Bug] = (new[] { ".X...X.", "..X.X..", ".XXXXX.", "XXOXOXX", ".XXXXX.", "X.X.X.X" }, Ink.Of(0xE0, 0x60, 0x5A), Ink.Of(0xF0, 0xEE, 0xE9)),
        [Kind.Error] = (new[] { "...X...", "..XXX..", "..XOX..", ".XXOXX.", ".XXXXX.", "XXXOXXX", "XXXXXXX" }, Ink.Of(0xF5, 0xB8, 0x3D), Ink.Of(0x1E, 0x1F, 0x23)),
        [Kind.Tower] = (new[] { "XXXXXX", "XOXXOX", "XXXXXX", "XXXXXX", "XOOOOX", "XXXXXX", "XXXXXX", "XOXXOX", "XXXXXX", "XXXXXX", "XXXXXX" }, Ink.Of(0x7A, 0x86, 0xA8), Ink.Of(0xF2, 0x7A, 0x2C)),
        [Kind.Coffee] = (new[] { "....OO......", "....OOX.....", "..XXXXXXX...", "XXXXXXXXXXXX" }, Ink.Of(0x8B, 0x5A, 0x3C), Ink.Of(0xE6, 0xE7, 0xEA)),
        [Kind.Spike] = (new[] { "..X..", ".XOX.", "XOOOX", ".XOX.", "..X.." }, Ink.Of(0xB0, 0x7A, 0xE0), Ink.Of(0xE2, 0xCF, 0xF5)),
    };
    const double Px = 2; // obstacle pixel size

    static readonly string[] OrbShape = { ".X.", "XXX", ".X." };
    static readonly string[] Puff = { "XX", "XX" };
    static readonly string[] Dot = { "X" };
    static readonly string[] Flame = { ".X.", "XXX", ".X." };
    static readonly string[] Waves = { "X..X.", ".X..X", ".X..X", "X..X." };
    static readonly string[] Slash = { "...X", "..X.", ".X..", "X..." };
    static readonly Brush Sky = Ink.Of(0x17, 0x18, 0x1C);
    static readonly Brush GroundInk = Ink.Of(0x3A, 0x3D, 0x45);
    static readonly Brush PebbleInk = Ink.Of(0x2C, 0x2E, 0x34);
    static readonly Brush CloudInk = Ink.Of(0x2A, 0x2C, 0x32);
    static readonly Brush OrbInk = Ink.Of(0xF5, 0xD2, 0x4A);
    static readonly Brush TextInk = Ink.Of(0xC9, 0xCA, 0xCF);
    static readonly Brush DimInk = Ink.Of(0x7D, 0x80, 0x8A);
    static readonly Brush MeterBack = Ink.Of(0x2C, 0x2E, 0x34);
    static readonly Brush MeterFill = Ink.Of(0x60, 0xA5, 0xFA);
    static readonly Brush MeterReady = Ink.Of(0xF5, 0xD2, 0x4A);
    static readonly Brush FireInk = Ink.Of(0xF2, 0x7A, 0x2C);
    static readonly Brush FireCore = Ink.Of(0xF5, 0xD2, 0x4A);
    static readonly Brush WhiteInk = Ink.Of(0xF0, 0xEE, 0xE9);
    static readonly Brush SunTint = Frozen(new SolidColorBrush(Color.FromArgb(0x16, 0xF5, 0xD2, 0x4A)));
    static readonly Pen ShieldPen = FrozenPen(Color.FromArgb(0xC0, 0x6F, 0xD3, 0xE8), 1.5);
    static readonly Pen SpeedPen = FrozenPen(Color.FromArgb(0x70, 0xC9, 0xCA, 0xCF), 1);
    static readonly Brush OrbGlow = Frozen(new RadialGradientBrush(Color.FromArgb(0x50, 0xF5, 0xD2, 0x4A), Color.FromArgb(0, 0xF5, 0xD2, 0x4A)));
    static readonly Typeface Font = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    static readonly Typeface Label = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    readonly Random rng = new();              // looks only: clouds, bits, how things fly off
    Random course = new(), orbCourse = new(); // what comes at him, seeded by the day on the daily run
    readonly List<Obstacle> obstacles = new();
    readonly List<Orb> orbs = new();
    readonly List<Bit> bits = new();
    readonly List<Pop> pops = new();
    readonly List<(double X, double Y, int Shape)> clouds = new();
    readonly Stopwatch clock = new();
    TimeSpan lastTick;
    bool looping;

    Mascot mascot = Mascot.For(MascotKind.Dib);
    bool daily;
    double time, speed, distance, points, nextSpawnAt, nextOrbAt, overAt;
    double feet = GroundY, vy;   // feet height and vertical speed
    bool onGround = true, jumpHeld;
    int airJumps;
    double charge, powerLeft, poseLeft;
    bool shield, chompArmed;
    int combo = 1;
    double comboLeft;
    Step? pose;

    public GameState State { get; private set; } = GameState.Ready;
    public string Message { get; private set; } = "Press Space to start";
    public int Score => (int)points;
    public int Best { get; set; }
    /// <summary>The day the course is for, when <see cref="Daily"/> is on.</summary>
    public DateTime Day { get; private set; }

    /// <summary>Today's course, the same on every run all day, instead of a new one each run. Switching starts over.</summary>
    public bool Daily
    {
        get => daily;
        set
        {
            daily = value;
            if (State == GameState.Running) return;
            State = GameState.Ready;
            Reset();
            Message = "Press Space to start";
            InvalidateVisual();
        }
    }
    /// <summary>Plays itself (for --runner): jumps what it should and uses the power when ready.</summary>
    public bool Autopilot { get; set; }

    // He's drawn with pixels the size of the obstacles' (his own art is chunkier), so he fits the field
    double Scale => Px / mascot.P;
    double MascotW => mascot.Width * Scale;
    double MascotH => mascot.Height * Scale;

    public Mascot Mascot
    {
        get => mascot;
        set { mascot = value; InvalidateVisual(); }
    }

    public event Action<int>? GameOver;

    public RunnerView()
    {
        Width = FieldWidth * Zoom;
        Height = FieldHeight * Zoom;
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        for (int i = 0; i < 5; i++) clouds.Add((rng.NextDouble() * FieldWidth, 10 + rng.NextDouble() * 40, rng.Next(2)));
        Reset();
        Loaded += (_, _) => StartLoop();
        Unloaded += (_, _) => StopLoop();
    }

    // ---- Control --------------------------------------------------------------

    /// <summary>Space, Up or a click: start, jump (hold for higher), restart or resume.</summary>
    public void JumpPressed()
    {
        jumpHeld = true;
        switch (State)
        {
            case GameState.Ready:
                State = GameState.Running;
                Jump();
                break;
            case GameState.Over when time - overAt > 0.5:
                Reset();
                State = GameState.Running;
                break;
            case GameState.Paused:
                Resume();
                break;
            case GameState.Running:
                Jump();
                break;
        }
    }

    public void JumpReleased()
    {
        jumpHeld = false;
        // Let go early for a short hop
        if (!onGround && vy < -CutSpeed && !Flying) vy = -CutSpeed;
    }

    public void PowerPressed()
    {
        if (State != GameState.Running || charge < 1) return;
        charge = 0;
        UsePower();
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
        lastTick = clock.Elapsed;
    }

    public void TogglePause()
    {
        if (State == GameState.Running) Pause("Paused · Space to go on");
        else Resume();
    }

    void Reset()
    {
        Day = DateTime.Today;
        int seed = daily ? Day.Year * 10000 + Day.Month * 100 + Day.Day : rng.Next();
        course = new Random(seed);
        orbCourse = new Random(seed ^ 0x5EED); // its own, so orbs never shift the obstacles
        obstacles.Clear();
        orbs.Clear();
        bits.Clear();
        pops.Clear();
        speed = StartSpeed;
        distance = points = 0;
        nextSpawnAt = StartSpeed * 1.2;
        nextOrbAt = StartSpeed * 6;
        combo = 1;
        comboLeft = 0;
        feet = GroundY;
        vy = 0;
        onGround = true;
        charge = 0.25;
        powerLeft = poseLeft = 0;
        shield = chompArmed = false;
        pose = null;
        Message = "";
    }

    void Jump()
    {
        if (Burrowed || Flying) return;
        if (onGround)
        {
            vy = -JumpSpeed;
            onGround = false;
        }
        else if (airJumps > 0)
        {
            airJumps--;
            vy = -JumpSpeed * 0.9;
            Burst(Puff, DimInk, MascotX + MascotW / 2, feet, 3, falls: false);
        }
    }

    // ---- Powers ---------------------------------------------------------------

    bool Active(Power p) => mascot.Power == p && powerLeft > 0;
    bool Flying => Active(Power.Fly);
    bool Burrowed => Active(Power.Burrow);
    bool Untouchable => Active(Power.Phase) || Active(Power.Burrow) || Active(Power.Dash) || Active(Power.Fly);

    void UsePower()
    {
        double front = MascotX + MascotW;
        switch (mascot.Power)
        {
            case Power.Phase: powerLeft = 3; break;
            case Power.Burrow: powerLeft = 3; feet = GroundY; vy = 0; onGround = true; break;
            case Power.DoubleJump: powerLeft = 8; break;
            case Power.Dash: powerLeft = 2.5; break;
            case Power.SlowMo: powerLeft = 4; break;
            case Power.Glide: powerLeft = 6; break;
            case Power.Fly: powerLeft = 4; onGround = false; break;
            case Power.Shield:
                shield = true;
                powerLeft = 0;
                break;
            case Power.Chomp:
                chompArmed = true;
                powerLeft = 3;
                break;
            case Power.Honk:
                // Everything on screen runs off
                foreach (var o in obstacles.Where(o => !o.Dead))
                {
                    o.Dead = true;
                    o.VX = 60 + rng.NextDouble() * 80;
                    o.VY = -160 - rng.NextDouble() * 80;
                    if (o.X < FieldWidth) Bonus(KnockBonus, o.X + o.W / 2, o.Y - 6);
                }
                Strike(front, feet - MascotH * 0.7, 'q');
                break;
            case Power.Fire:
                foreach (var o in obstacles.Where(o => !o.Dead && o.X < front + 170 && o.X + o.W > front - 10))
                    Knock(o, 'f');
                for (int i = 0; i < 10; i++)
                    bits.Add(new Bit { Shape = Flame, Ink = i % 2 == 0 ? FireInk : FireCore, X = front, Y = feet - MascotH * 0.75 + (rng.NextDouble() - 0.5) * 8, VX = 160 + rng.NextDouble() * 120, VY = (rng.NextDouble() - 0.5) * 50, Life = 0.6 });
                Strike(front, feet - MascotH * 0.7, 'f');
                break;
        }
        if (mascot.Power is not (Power.Fire or Power.Honk or Power.Chomp)) ShowPose(null, 0.4);
    }

    /// <summary>Shows a pose from his signature move: the step with this effect, else a happy face.</summary>
    void Strike(double x, double y, char fx)
    {
        var step = mascot.Signature.FirstOrDefault(s => s.Fx == fx && s.Body != null) ?? mascot.Signature.FirstOrDefault(s => s.Body != null);
        ShowPose(step, 0.4);
        if (fx == 'q')
        {
            bits.Add(new Bit { Shape = Waves, Ink = TextInk, X = x + 2, Y = y, VX = 120, Life = 0.6 });
            bits.Add(new Bit { Shape = Waves, Ink = TextInk, X = x + 2, Y = y + 8, VX = 100, Life = 0.6 });
        }
    }

    void ShowPose(Step? step, double seconds)
    {
        pose = step;
        poseLeft = seconds;
    }

    /// <summary>An obstacle taken out: it flies off, with bits to show how.</summary>
    void Knock(Obstacle o, char how)
    {
        o.Dead = true;
        o.VX = 90 + rng.NextDouble() * 60;
        o.VY = -150 - rng.NextDouble() * 60;
        var look = Looks[o.Kind];
        double cx = o.X + o.W / 2, cy = o.Y + o.H / 2;
        if (how != 's') Bonus(KnockBonus, cx, o.Y - 6); // taken out by his power; the shield only saved him
        switch (how)
        {
            case 'x':
                bits.Add(new Bit { Shape = Slash, Ink = WhiteInk, X = cx - 4, Y = cy - 4, VX = 20, Life = 0.25 });
                for (int i = 0; i < 4; i++)
                    bits.Add(new Bit { Shape = Dot, Ink = look.Ink, X = cx, Y = cy, VX = (rng.NextDouble() - 0.3) * 120, VY = -60 - rng.NextDouble() * 80, Life = 0.7, Falls = true });
                break;
            case 'c':
                for (int i = 0; i < 6; i++)
                    bits.Add(new Bit { Shape = Dot, Ink = look.Ink, X = cx, Y = cy, VX = (rng.NextDouble() - 0.5) * 80, VY = -40 - rng.NextDouble() * 60, Life = 0.7, Falls = true });
                o.VY = 400; // eaten: nothing left to fly off
                break;
            case 'f':
                for (int i = 0; i < 4; i++)
                    bits.Add(new Bit { Shape = Puff, Ink = DimInk, X = cx, Y = cy, VX = (rng.NextDouble() - 0.5) * 30, VY = -30 - rng.NextDouble() * 30, Life = 0.8 });
                break;
            default:
                Burst(OrbShape, OrbInk, cx, cy, 4, falls: true);
                break;
        }
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

    /// <summary>Moves the game forward; the render loop calls it, --runner calls it directly.</summary>
    public void Tick(double dt)
    {
        time += dt;
        if (Autopilot) Pilot();
        for (int i = clouds.Count - 1; i >= 0; i--)
        {
            var c = clouds[i];
            double x = c.X - (State == GameState.Running ? speed * 0.1 : 6) * dt;
            clouds[i] = x < -40 ? (FieldWidth + rng.NextDouble() * 40, 10 + rng.NextDouble() * 40, rng.Next(2)) : (x, c.Y, c.Shape);
        }
        AgeBits(dt);
        if (State != GameState.Running) return;

        powerLeft = Math.Max(0, powerLeft - dt);
        poseLeft = Math.Max(0, poseLeft - dt);
        if (powerLeft <= 0) chompArmed = false;
        double world = Active(Power.SlowMo) ? 0.5 : 1;
        speed = SpeedAt(distance);
        double run = speed * world * (Active(Power.Dash) ? 1.7 : 1);
        distance += run * dt;
        points += run * dt / 10 * combo;
        charge = Math.Min(1, charge + ChargePerSecond * dt);
        comboLeft = Math.Max(0, comboLeft - dt);
        if (combo > 1 && comboLeft <= 0) StepDown();

        // Him
        if (Flying)
        {
            feet += (GroundY - 74 - feet) * Math.Min(1, dt * 6);
            vy = 0;
        }
        else if (Burrowed)
        {
            feet = GroundY;
            if (rng.NextDouble() < dt * 12) bits.Add(new Bit { Shape = Dot, Ink = GroundInk, X = MascotX + rng.NextDouble() * MascotW, Y = GroundY - 1, VX = -run * 0.3, VY = -50 - rng.NextDouble() * 40, Life = 0.5, Falls = true });
        }
        else if (!onGround || feet < GroundY)
        {
            vy += Gravity * dt;
            if (Active(Power.Glide) && vy > 60) vy = 60;
            feet += vy * dt;
            if (feet >= GroundY)
            {
                feet = GroundY;
                vy = 0;
                onGround = true;
                airJumps = Active(Power.DoubleJump) ? 1 : 0;
            }
        }
        if (onGround) airJumps = Active(Power.DoubleJump) ? 1 : 0;

        // Obstacles and orbs come at him, placed by distance so the course doesn't depend on the frame rate or his powers
        while (distance >= nextSpawnAt) Spawn();
        while (distance >= nextOrbAt)
        {
            orbs.Add(new Orb { X = FieldWidth + 10 - (distance - nextOrbAt), Y = GroundY - 24 - orbCourse.NextDouble() * 44 });
            nextOrbAt += SpeedAt(nextOrbAt) * (7 + orbCourse.NextDouble() * 5);
        }

        double left = MascotX + MascotW * 0.18, right = MascotX + MascotW * 0.82;
        double top = feet - MascotH * 0.85, bottom = feet - 1;
        foreach (var o in obstacles)
        {
            if (o.Dead)
            {
                o.X += (o.VX - run) * dt;
                o.Y += o.VY * dt;
                o.VY += Gravity * dt;
                continue;
            }
            o.X -= run * dt;
            if (o.Kind == Kind.Spike) o.Y += Math.Sin(time * 5 + o.X * 0.05) * 6 * dt;

            if (chompArmed && o.X > right - 6 && o.X < right + 26)
            {
                Knock(o, mascot.PowerFx);
                Strike(o.X, o.Y, mascot.PowerFx);
                chompArmed = false;
                powerLeft = 0;
                continue;
            }

            bool over = o.X + 2 < right && o.X + o.W - 2 > left;
            if (over)
            {
                if (Untouchable) o.Spoiled = true;
                o.Room = Math.Min(o.Room, Math.Max(o.Y + 2 - bottom, top - (o.Y + o.H - 2)));
            }
            else if (!o.Passed && o.Room < double.MaxValue)
            {
                o.Passed = true;
                if (!o.Spoiled && o.Room <= NearMiss) CloseCall(o);
            }

            bool hit = over && o.Y + 2 < bottom && o.Y + o.H - 2 > top;
            if (!hit || Untouchable) continue;
            if (shield)
            {
                shield = false;
                Knock(o, 's');
                combo = 1; // it took a hit: the combo is gone
                comboLeft = 0;
                continue;
            }
            Lose();
            return;
        }
        obstacles.RemoveAll(o => o.X + o.W < -20 || o.Y > FieldHeight + 20);

        foreach (var orb in orbs)
        {
            orb.X -= run * dt;
            if (!orb.Taken && orb.X < right && orb.X + 6 > left && orb.Y < bottom && orb.Y + 6 > top)
            {
                orb.Taken = true;
                charge = Math.Min(1, charge + OrbCharge);
                Burst(OrbShape, OrbInk, orb.X + 3, orb.Y + 3, 4, falls: false);
                StepUp();
                Bonus(OrbBonus, orb.X + 3, orb.Y - 4);
            }
            else if (!orb.Taken && !orb.Missed && orb.X + 6 < left)
            {
                orb.Missed = true;
                if (combo > 1) StepDown();
            }
        }
        orbs.RemoveAll(o => o.Taken || o.X < -10);
    }

    /// <summary>His base pace this far in: gaining SpeedUp a second as if at his normal pace, whatever his powers did.</summary>
    static double SpeedAt(double distance) => Math.Min(MaxSpeed, Math.Sqrt(StartSpeed * StartSpeed + 2 * SpeedUp * distance));

    /// <summary>The next obstacle on the course. Only the course and the distance decide it, so a seed always makes the same course.</summary>
    void Spawn()
    {
        double at = nextSpawnAt, x = FieldWidth + 10 - (distance - at);
        int far = (int)(at / 10);
        // Harder mixes farther in
        var pool = new List<Kind> { Kind.Bug, Kind.Bug, Kind.Error, Kind.Coffee };
        if (far > 60) pool.Add(Kind.Tower);
        if (far > 120) pool.Add(Kind.Spike);
        var kind = pool[course.Next(pool.Count)];
        bool pair = course.NextDouble() < 0.35;
        Add(kind, x, course.NextDouble());
        if (kind == Kind.Bug && far > 200 && pair) Add(Kind.Bug, x + 16, 0);
        // Room to land and jump again: more at higher speed
        nextSpawnAt = at + SpeedAt(at) * (0.85 + course.NextDouble() * 0.9) + 50;
    }

    /// <summary>An obstacle at x; a lag spike floats at a height set by <paramref name="lift"/> (0 to 1).</summary>
    void Add(Kind kind, double x, double lift)
    {
        var rows = Looks[kind].Rows;
        double w = rows[0].Length * Px, h = rows.Length * Px;
        double y = kind == Kind.Spike ? GroundY - 46 - lift * 12 - h : GroundY - h;
        obstacles.Add(new Obstacle { Kind = kind, X = x, Y = y, W = w, H = h });
    }

    // ---- Combo ----------------------------------------------------------------

    /// <summary>He cleared it by a hair, with no power to help.</summary>
    void CloseCall(Obstacle o)
    {
        StepUp();
        Bonus(CloseBonus, o.X + o.W / 2, o.Y - 6, "CLOSE ");
    }

    void StepUp()
    {
        combo = Math.Min(MaxCombo, combo + 1);
        comboLeft = ComboTime;
    }

    void StepDown()
    {
        combo = Math.Max(1, combo - 1);
        comboLeft = combo > 1 ? ComboTime : 0;
    }

    void Bonus(int basePoints, double x, double y, string label = "")
    {
        int won = basePoints * combo;
        points += won;
        pops.Add(new Pop { Text = $"{label}+{won}", X = x, Y = y });
    }

    void Lose()
    {
        State = GameState.Over;
        overAt = time;
        Message = "Game over · Space to run again";
        if (Score > Best) Best = Score;
        Burst(Puff, DimInk, MascotX + MascotW / 2, feet - MascotH / 2, 6, falls: false);
        GameOver?.Invoke(Score);
    }

    void Burst(string[] shape, Brush ink, double x, double y, int count, bool falls)
    {
        for (int i = 0; i < count; i++)
            bits.Add(new Bit { Shape = shape, Ink = ink, X = x, Y = y, VX = (rng.NextDouble() - 0.5) * 90, VY = -40 - rng.NextDouble() * 60, Life = 0.6, Falls = falls });
    }

    void AgeBits(double dt)
    {
        foreach (var b in bits)
        {
            b.Age += dt;
            b.X += b.VX * dt;
            b.Y += b.VY * dt;
            if (b.Falls) b.VY += Gravity * 0.6 * dt;
        }
        bits.RemoveAll(b => b.Age >= b.Life);
        foreach (var p in pops)
        {
            p.Age += dt;
            p.Y -= 18 * dt;
        }
        pops.RemoveAll(p => p.Age >= PopLife);
    }

    const double PopLife = 0.9;

    /// <summary>For --runner: jumps ground obstacles in time, stays down under lag spikes, uses the power when it can.</summary>
    void Pilot()
    {
        if (State == GameState.Ready) { JumpPressed(); return; }
        if (State != GameState.Running) return;
        if (charge >= 1 && obstacles.Any(o => !o.Dead && o.X > MascotX && o.X < MascotX + 160)) PowerPressed();
        double front = MascotX + MascotW * 0.82, run = speed * (Active(Power.SlowMo) ? 0.5 : 1) * (Active(Power.Dash) ? 1.7 : 1);
        var next = obstacles.Where(o => !o.Dead && o.X + o.W > MascotX).OrderBy(o => o.X).FirstOrDefault();
        if (next == null || !onGround) { if (jumpHeld && vy > -CutSpeed) JumpReleased(); return; }
        double gap = next.X - front;
        if (next.Kind != Kind.Spike && gap < run * 0.2 + 6 && gap > -4) JumpPressed();
    }

    // ---- Drawing --------------------------------------------------------------

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Sky, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.PushTransform(new ScaleTransform(Zoom, Zoom));

        foreach (var (cx, cy, shape) in clouds)
            PixelScene.Sprite(dc, shape == 0 ? new[] { "..XXX..", "XXXXXXX" } : new[] { "...XXX....", ".XXXXXXX..", "XXXXXXXXXX" }, Math.Round(cx), Math.Round(cy), 3, c => c == 'X' ? CloudInk : null);
        if (Active(Power.SlowMo)) dc.DrawRectangle(SunTint, null, new Rect(0, 0, FieldWidth, FieldHeight));

        // Ground, with pebbles scrolling by
        dc.DrawRectangle(GroundInk, null, new Rect(0, GroundY, FieldWidth, 1));
        double scroll = distance % 40;
        for (double x = -scroll; x < FieldWidth; x += 40)
        {
            dc.DrawRectangle(PebbleInk, null, new Rect(Math.Round(x + 7), GroundY + 5, 3, 1));
            dc.DrawRectangle(PebbleInk, null, new Rect(Math.Round(x + 26), GroundY + 10, 2, 1));
        }

        foreach (var orb in orbs)
        {
            double bob = Math.Round(Math.Sin(time * 4 + orb.X * 0.1) * 2);
            dc.DrawEllipse(OrbGlow, null, new Point(orb.X + 3, orb.Y + 3 + bob), 9, 9);
            PixelScene.Sprite(dc, OrbShape, Math.Round(orb.X), Math.Round(orb.Y + bob), 2, c => c == 'X' ? OrbInk : null);
        }

        foreach (var o in obstacles)
        {
            var (rows, ink, detail) = Looks[o.Kind];
            if (o.Dead) dc.PushOpacity(0.7);
            PixelScene.Sprite(dc, rows, Math.Round(o.X), Math.Round(o.Y), Px, c => c == 'X' ? ink : c == 'O' ? detail : null);
            if (o.Dead) dc.Pop();
        }

        DrawMascot(dc);

        foreach (var b in bits)
        {
            dc.PushOpacity(Math.Min(1, (1 - b.Age / b.Life) * 2));
            PixelScene.Sprite(dc, b.Shape, Math.Round(b.X), Math.Round(b.Y), 2, c => c == 'X' ? b.Ink : null);
            dc.Pop();
        }

        DrawHud(dc);
        dc.Pop();
    }

    void DrawMascot(DrawingContext dc)
    {
        double left = MascotX, top = Math.Round(feet - MascotH);
        bool running = State == GameState.Running;
        int legFrame = running && onGround && !Burrowed ? (int)(distance / 14) % 2 : 0;
        int arms = Flying ? ((int)(time * 12) % 2 == 0 ? 2 : 0) : !onGround && running ? 1 : 0;
        if (Active(Power.Glide) && vy > 0) arms = 2;
        var eyes = State == GameState.Over ? EyeLook.Closed : poseLeft > 0 || powerLeft > 0 ? EyeLook.Happy : EyeLook.Open;

        if (Active(Power.Dash))
            for (int i = 0; i < 3; i++)
            {
                double y = top + MascotH * (0.25 + i * 0.25);
                dc.DrawLine(SpeedPen, new Point(left - 30 + i * 6, y), new Point(left - 6, y));
            }

        bool burrowed = Burrowed;
        bool ghost = Active(Power.Phase);
        if (burrowed)
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, FieldWidth, GroundY)));
            top += Math.Round(MascotH * 0.6);
        }
        if (ghost) dc.PushOpacity(0.35 + 0.25 * Math.Abs(Math.Sin(time * 14)));
        dc.PushTransform(new MatrixTransform(Scale, 0, 0, Scale, left, top));
        MascotPainter.Draw(dc, mascot, 0, 0, poseLeft > 0 ? pose : null, arms, arms, legFrame, 1, false, 0, eyes, 0);
        dc.Pop();
        if (ghost) dc.Pop();
        if (burrowed) dc.Pop();

        if (shield)
        {
            double r = Math.Max(MascotW, MascotH) * 0.62;
            dc.DrawEllipse(null, ShieldPen, new Point(left + MascotW / 2, top + MascotH / 2), r, r);
        }
    }

    void DrawHud(DrawingContext dc)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Text(string s, Typeface face, double size, Brush ink) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, ink, dpi * Zoom);

        // Bonuses where they were won, rising and fading
        foreach (var p in pops)
        {
            var won = Text(p.Text, Font, 6, OrbInk);
            dc.PushOpacity(Math.Min(1, (1 - p.Age / PopLife) * 2));
            dc.DrawText(won, new Point(Math.Round(Math.Clamp(p.X - won.Width / 2, 2, FieldWidth - 2 - won.Width)), Math.Round(p.Y - won.Height)));
            dc.Pop();
        }

        // Score, top right, and the combo under it with the time it has left
        var score = Text($"{Score:00000}", Font, 9, TextInk);
        var best = Text($"HI {Math.Max(Best, Score):00000}", Font, 9, DimInk);
        dc.DrawText(best, new Point(FieldWidth - 8 - best.Width - 8 - score.Width, 6));
        dc.DrawText(score, new Point(FieldWidth - 8 - score.Width, 6));
        if (combo > 1)
        {
            var times = Text($"x{combo}", Font, 9, MeterReady);
            dc.DrawText(times, new Point(FieldWidth - 8 - times.Width, 17));
            double barRight = FieldWidth - 8 - times.Width - 4;
            dc.DrawRectangle(MeterBack, null, new Rect(barRight - 30, 23, 30, 2));
            dc.DrawRectangle(MeterReady, null, new Rect(barRight - 30, 23, Math.Round(30 * comboLeft / ComboTime), 2));
        }

        // Which course, top middle
        var mode = Text(daily ? $"DAILY RUN · {Day.ToString("d MMM", CultureInfo.InvariantCulture).ToUpperInvariant()}" : "FREE RUN", Label, 7, daily ? MeterReady : DimInk);
        dc.DrawText(mode, new Point(Math.Round((FieldWidth - mode.Width) / 2), 4));

        // Power meter, top left
        bool ready = charge >= 1;
        string state = powerLeft > 0 || chompArmed ? "ON" : shield ? "UP" : ready ? "READY · X" : "";
        var name = Text(mascot.PowerName.ToUpperInvariant() + (state.Length > 0 ? "  " + state : ""), Label, 7, ready || powerLeft > 0 ? MeterReady : DimInk);
        dc.DrawText(name, new Point(8, 4));
        dc.DrawRectangle(MeterBack, null, new Rect(8, 15, 60, 3));
        dc.DrawRectangle(ready ? MeterReady : MeterFill, null, new Rect(8, 15, Math.Round(60 * charge), 3));

        if (State != GameState.Running && Message.Length > 0)
        {
            var big = Text(Message, Label, 9, TextInk);
            dc.DrawText(big, new Point(Math.Round((FieldWidth - big.Width) / 2), 50));
            if (State == GameState.Ready)
            {
                var keys = Text($"Space or click: jump (hold for higher)   X or right-click: {mascot.PowerName}", Label, 6, DimInk);
                dc.DrawText(keys, new Point(Math.Round((FieldWidth - keys.Width) / 2), 64));
            }
            if (State is GameState.Ready or GameState.Over)
            {
                var hint = Text("Clear things by a hair and grab orbs to raise the combo   D: " + (daily ? "random courses" : "today's course, the same all day"), Label, 6, DimInk);
                dc.DrawText(hint, new Point(Math.Round((FieldWidth - hint.Width) / 2), State == GameState.Ready ? 74 : 64));
            }
        }
    }

    static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
