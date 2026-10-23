using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>
/// A one-button runner starring your mascot, to play while sessions work. He runs on his own, faster
/// and faster; jump over bugs, error signs, merge conflicts and coffee spills, hop low under the lag
/// spikes hanging over some of them, and clear the ones drifting at head height. Harder patterns join
/// every few seconds. The power meter fills slowly as you go (faster with the ✦ orbs) and lets him use
/// his power. Close calls and orbs raise a combo that multiplies the score; it drains if you play it
/// safe. The daily run is one course for the whole day, the same however you play it.
/// </summary>
public sealed class RunnerView : ArcadeGame
{
    const double GroundY = 122, MascotX = 46;
    // A snappy jump: high when held, a short hop when let go at once
    const double Gravity = 1500, JumpSpeed = 420, CutSpeed = 120;
    const double StartSpeed = 180, MaxSpeed = 480, SpeedUp = 5;
    const double ChargePerSecond = 1 / 30.0, OrbCharge = 0.3;
    const double ComboTime = 5, NearMiss = 6; // seconds a combo step lasts; pixels of room that count as a close call
    const double Grace = 1;                   // pixels of an obstacle's edge that don't count as a hit
    /// <summary>Room between his head and a lag spike hung over an obstacle: a short hop fits under it, a full jump doesn't.</summary>
    const double CeilingRoom = 46;
    const int MaxCombo = 5, CloseBonus = 20, OrbBonus = 10, KnockBonus = 10; // bonuses are times the combo

    enum Kind { Bug, Error, Tower, Coffee, Spike }

    enum Pattern { Single, HighSpike, Drone, Wide, Ceiling, Double, Scuttler, Staircase }

    sealed class Obstacle
    {
        public Kind Kind;
        public double X, Y;   // top-left
        public double W, H;
        public double Crawl;  // runs at him this much faster than the ground goes by
        public Pattern From;  // the pattern it came in
        public bool Dead;
        public double VX, VY; // once scared off or knocked away
        public double Room = double.MaxValue; // least room between him and it while passing it
        public bool Passed, Spoiled;          // spoiled: passed while a power made him untouchable
        public double Right => X + W;
    }

    sealed class Orb { public double X, Y; public bool Taken, Missed; }

    static readonly Dictionary<Kind, GameArt.Look> Looks = new()
    {
        [Kind.Bug] = GameArt.Bug,
        [Kind.Error] = GameArt.Error,
        [Kind.Tower] = GameArt.Tower,
        [Kind.Coffee] = GameArt.Coffee,
        [Kind.Spike] = GameArt.Spike,
    };
    const double Px = 2; // obstacle pixel size

    static readonly string[] Flame = { ".X.", "XXX", ".X." };
    static readonly string[] Waves = { "X..X.", ".X..X", ".X..X", "X..X." };
    static readonly string[] Slash = { "...X", "..X.", ".X..", "X..." };
    static readonly Brush FireInk = Ink.Of(0xF2, 0x7A, 0x2C);
    static readonly Brush FireCore = Ink.Of(0xF5, 0xD2, 0x4A);
    static readonly Brush SunTint = Frozen(new SolidColorBrush(Color.FromArgb(0x16, 0xF5, 0xD2, 0x4A)));
    static readonly Pen ShieldPen = FrozenPen(Color.FromArgb(0xC0, 0x6F, 0xD3, 0xE8), 1.5);
    static readonly Pen SpeedPen = FrozenPen(Color.FromArgb(0x70, 0xC9, 0xCA, 0xCF), 1);

    Random course = new(), orbCourse = new(); // what comes at him, seeded by the day on the daily run
    readonly List<Obstacle> obstacles = new();
    readonly List<Orb> orbs = new();
    readonly List<(double X, double Y, int Shape)> clouds = new();

    double speed, distance, points, nextSpawnAt, nextOrbAt;
    double feet = GroundY, vy;   // feet height and vertical speed
    bool onGround = true, jumpHeld;
    int airJumps;
    double charge, powerLeft, poseLeft;
    bool shield, chompArmed;
    int combo = 1;
    double comboLeft;
    Step? pose;
    double pilotLetGo = -1; // autopilot: when to let go of a short hop
    Pattern spawning;

    /// <summary>What ended the last run, for the off-screen check.</summary>
    internal string Death { get; private set; } = "";
    internal double Seconds { get; private set; }
    /// <summary>How far off the autopilot's timing is, up to this many seconds early or late, like a person's.</summary>
    internal double PilotSlop { get; set; }
    double slop; // this jump's

    public override string Id => "runner";
    public override string Title => "Runner";
    public override string Keys => $"Space / click: jump (hold for higher) · X / right-click: {Mascot.PowerName}";
    protected override string Hint => "Clear things by a hair and grab orbs to raise the combo";
    public override int Score => (int)points;

    // He's drawn with pixels the size of the obstacles' (his own art is chunkier), so he fits the field
    double Scale => Px / Mascot.P;
    double MascotW => Mascot.Width * Scale;
    double MascotH => Mascot.Height * Scale;
    /// <summary>How tall he counts for hits.</summary>
    double Reach => MascotH * 0.85;

    public RunnerView()
    {
        for (int i = 0; i < 5; i++) clouds.Add((rng.NextDouble() * FieldWidth, 10 + rng.NextDouble() * 40, rng.Next(2)));
        NewRun();
    }

    // ---- Control --------------------------------------------------------------

    public override bool Press(Key key, bool repeat)
    {
        switch (key)
        {
            case Key.Space or Key.Up or Key.W:
                if (!repeat) JumpPressed();
                return true;
            case Key.X or Key.LeftShift or Key.RightShift or Key.E:
                PowerPressed();
                return true;
        }
        return false;
    }

    public override void Release(Key key)
    {
        if (key is Key.Space or Key.Up or Key.W) JumpReleased();
    }

    public override void Click(Point at, bool right)
    {
        if (right) PowerPressed();
        else JumpPressed();
    }

    public override void ClickUp(Point at, bool right)
    {
        if (!right) JumpReleased();
    }

    /// <summary>Space, Up or a click: start, jump (hold for higher), play again or go on.</summary>
    void JumpPressed()
    {
        jumpHeld = true;
        bool starting = State == GameState.Ready;
        if (StartOrResume() && !starting) return;
        Jump();
    }

    void JumpReleased()
    {
        jumpHeld = false;
        // Let go early for a short hop
        if (!onGround && vy < -CutSpeed && !Flying) vy = -CutSpeed;
    }

    void PowerPressed()
    {
        if (State != GameState.Running || charge < 1) return;
        charge = 0;
        UsePower();
    }

    protected override void Reset()
    {
        int seed = CourseSeed();
        course = new Random(seed);
        orbCourse = new Random(seed ^ 0x5EED); // its own, so orbs never shift the obstacles
        obstacles.Clear();
        orbs.Clear();
        bits.Clear();
        speed = StartSpeed;
        distance = points = 0;
        nextSpawnAt = StartSpeed * 1.2;
        nextOrbAt = StartSpeed * 6;
        combo = 1;
        comboLeft = 0;
        feet = GroundY;
        vy = 0;
        onGround = true;
        charge = 0;
        powerLeft = poseLeft = 0;
        shield = chompArmed = false;
        pose = null;
        pilotLetGo = -1;
        Seconds = 0;
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
            Burst(GameArt.Puff, DimInk, MascotX + MascotW / 2, feet, 3, falls: false);
        }
    }

    // ---- Powers ---------------------------------------------------------------

    bool Active(Power p) => Mascot.Power == p && powerLeft > 0;
    bool Flying => Active(Power.Fly);
    bool Burrowed => Active(Power.Burrow);
    bool Untouchable => Active(Power.Phase) || Active(Power.Burrow) || Active(Power.Dash) || Active(Power.Fly);

    void UsePower()
    {
        double front = MascotX + MascotW;
        switch (Mascot.Power)
        {
            case Power.Phase: powerLeft = 2.25; break;
            case Power.Burrow: powerLeft = 2.25; feet = GroundY; vy = 0; onGround = true; break;
            case Power.DoubleJump: powerLeft = 6; break;
            case Power.Dash: powerLeft = 2; break;
            case Power.SlowMo: powerLeft = 3; break;
            case Power.Glide: powerLeft = 4.5; break;
            case Power.Fly: powerLeft = 3; onGround = false; break;
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
                foreach (var o in obstacles.Where(o => !o.Dead && o.X < front + 130 && o.X + o.W > front - 10))
                    Knock(o, 'f');
                for (int i = 0; i < 10; i++)
                    bits.Add(new Bit { Shape = Flame, Ink = i % 2 == 0 ? FireInk : FireCore, X = front, Y = feet - MascotH * 0.75 + (rng.NextDouble() - 0.5) * 8, VX = 140 + rng.NextDouble() * 90, VY = (rng.NextDouble() - 0.5) * 50, Life = 0.6 });
                Strike(front, feet - MascotH * 0.7, 'f');
                break;
        }
        if (Mascot.Power is not (Power.Fire or Power.Honk or Power.Chomp)) ShowPose(null, 0.4);
    }

    /// <summary>Shows a pose from his signature move: the step with this effect, else a happy face.</summary>
    void Strike(double x, double y, char fx)
    {
        var step = Mascot.Signature.FirstOrDefault(s => s.Fx == fx && s.Body != null) ?? Mascot.Signature.FirstOrDefault(s => s.Body != null);
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
                    bits.Add(new Bit { Shape = GameArt.Dot, Ink = look.Ink, X = cx, Y = cy, VX = (rng.NextDouble() - 0.3) * 120, VY = -60 - rng.NextDouble() * 80, Life = 0.7, Falls = true });
                break;
            case 'c':
                for (int i = 0; i < 6; i++)
                    bits.Add(new Bit { Shape = GameArt.Dot, Ink = look.Ink, X = cx, Y = cy, VX = (rng.NextDouble() - 0.5) * 80, VY = -40 - rng.NextDouble() * 60, Life = 0.7, Falls = true });
                o.VY = 400; // eaten: nothing left to fly off
                break;
            case 'f':
                for (int i = 0; i < 4; i++)
                    bits.Add(new Bit { Shape = GameArt.Puff, Ink = DimInk, X = cx, Y = cy, VX = (rng.NextDouble() - 0.5) * 30, VY = -30 - rng.NextDouble() * 30, Life = 0.8 });
                break;
            default:
                Burst(GameArt.Orb, GameArt.OrbInk, cx, cy, 4, falls: true);
                break;
        }
    }

    // ---- Simulation -----------------------------------------------------------

    protected override void Step(double dt)
    {
        for (int i = clouds.Count - 1; i >= 0; i--)
        {
            var c = clouds[i];
            double x = c.X - (State == GameState.Running ? speed * 0.1 : 6) * dt;
            clouds[i] = x < -40 ? (FieldWidth + rng.NextDouble() * 40, 10 + rng.NextDouble() * 40, rng.Next(2)) : (x, c.Y, c.Shape);
        }
        if (State != GameState.Running) return;

        Seconds += dt;
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
            if (rng.NextDouble() < dt * 12) bits.Add(new Bit { Shape = GameArt.Dot, Ink = GroundInk, X = MascotX + rng.NextDouble() * MascotW, Y = GroundY - 1, VX = -run * 0.3, VY = -50 - rng.NextDouble() * 40, Life = 0.5, Falls = true });
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
        double top = feet - Reach, bottom = feet - 1;
        foreach (var o in obstacles)
        {
            if (o.Dead)
            {
                o.X += (o.VX - run) * dt;
                o.Y += o.VY * dt;
                o.VY += Gravity * dt;
                continue;
            }
            o.X -= (run + o.Crawl * world) * dt;
            if (o.Kind == Kind.Spike) o.Y += Math.Sin(time * 5 + o.X * 0.05) * 6 * dt;

            if (chompArmed && o.X > right - 6 && o.X < right + 26)
            {
                Knock(o, Mascot.PowerFx);
                Strike(o.X, o.Y, Mascot.PowerFx);
                chompArmed = false;
                powerLeft = 0;
                continue;
            }

            bool over = o.X + Grace < right && o.X + o.W - Grace > left;
            if (over)
            {
                if (Untouchable) o.Spoiled = true;
                o.Room = Math.Min(o.Room, Math.Max(o.Y + Grace - bottom, top - (o.Y + o.H - Grace)));
            }
            else if (!o.Passed && o.Room < double.MaxValue)
            {
                o.Passed = true;
                if (!o.Spoiled && o.Room <= NearMiss) CloseCall(o);
            }

            bool hit = over && o.Y + Grace < bottom && o.Y + o.H - Grace > top;
            if (!hit || Untouchable) continue;
            if (shield)
            {
                shield = false;
                Knock(o, 's');
                combo = 1; // it took a hit: the combo is gone
                comboLeft = 0;
                continue;
            }
            Death = $"{o.From} ({o.Kind}) at {distance:0} px, {speed:0} px/s";
            Lose("Game over · Space to run again");
            Burst(GameArt.Puff, DimInk, MascotX + MascotW / 2, feet - MascotH / 2, 6, falls: false);
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
                Burst(GameArt.Orb, GameArt.OrbInk, orb.X + 3, orb.Y + 3, 4, falls: false);
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

    /// <summary>How far into the course: a new kind of pattern joins at each step, about every ten seconds.</summary>
    static int TierAt(double distance) => distance < 1200 ? 0 : distance < 3000 ? 1 : distance < 5500 ? 2 : distance < 8500 ? 3 : 4;

    /// <summary>The next pattern on the course. Only the course and the distance decide it, so a seed always makes the same course.</summary>
    void Spawn()
    {
        double at = nextSpawnAt, x = FieldWidth + 10 - (distance - at), pace = SpeedAt(at);
        int tier = TierAt(at);
        var pool = new List<Pattern> { Pattern.Single, Pattern.Single };
        if (tier >= 1) pool.AddRange(new[] { Pattern.HighSpike, Pattern.Drone, Pattern.Wide });
        if (tier >= 2) pool.AddRange(new[] { Pattern.Ceiling, Pattern.Double });
        if (tier >= 3) pool.AddRange(new[] { Pattern.Scuttler, Pattern.Staircase });
        if (tier >= 4) pool.AddRange(new[] { Pattern.Ceiling, Pattern.Double, Pattern.Staircase, Pattern.Scuttler });
        var pattern = spawning = pool[course.Next(pool.Count)];

        Kind Low() => course.Next(3) switch { 0 => Kind.Bug, 1 => Kind.Coffee, _ => Kind.Error };
        // Land and go again: just over half a second of running between the two
        double Quick() => pace * (0.55 + course.NextDouble() * 0.1);

        double end; // right edge of the pattern
        bool hard = false;
        switch (pattern)
        {
            case Pattern.HighSpike:
                end = Add(Kind.Spike, x, GroundY - 46 - course.NextDouble() * 12 - Looks[Kind.Spike].Height(Px)).Right;
                break;
            case Pattern.Drone:
                // Drifting at head height: he has to be off the ground to clear it
                end = Add(Kind.Spike, x, GroundY - 8 - course.NextDouble() * 6 - Looks[Kind.Spike].Height(Px)).Right;
                break;
            case Pattern.Wide:
            {
                var a = Add(course.Next(2) == 0 ? Kind.Tower : Kind.Coffee, x);
                end = Add(a.Kind == Kind.Tower ? Kind.Error : Kind.Coffee, a.Right).Right;
                break;
            }
            case Pattern.Ceiling:
            {
                // A low one with lag spikes hung over it: a short hop goes under them, a full jump into them
                var low = Add(course.Next(2) == 0 ? Kind.Bug : Kind.Coffee, x);
                double sw = Looks[Kind.Spike].Width(Px), y = GroundY - Reach - CeilingRoom - course.NextDouble() * 4 - Looks[Kind.Spike].Height(Px);
                double mid = low.X + low.W / 2;
                Add(Kind.Spike, mid - sw, y);
                end = Math.Max(low.Right, Add(Kind.Spike, mid, y).Right);
                hard = true;
                break;
            }
            case Pattern.Double:
            {
                var a = Add(Low(), x);
                end = Add(Low(), a.Right + Quick()).Right;
                hard = true;
                break;
            }
            case Pattern.Scuttler:
            {
                // It runs at him, so it gets some extra room to start from
                var bug = Add(Kind.Bug, x + pace * 0.3);
                bug.Crawl = 50 + course.NextDouble() * 40;
                end = bug.Right + pace * 0.3;
                break;
            }
            case Pattern.Staircase:
            {
                var a = Add(Kind.Bug, x);
                var b = Add(Kind.Tower, a.Right + Quick());
                end = Add(Kind.Coffee, b.Right + Quick()).Right;
                hard = true;
                break;
            }
            default:
            {
                var pick = tier == 0 ? new[] { Kind.Bug, Kind.Bug, Kind.Error, Kind.Coffee } : new[] { Kind.Bug, Kind.Error, Kind.Coffee, Kind.Tower };
                end = Add(pick[course.Next(pick.Length)], x).Right;
                break;
            }
        }
        // Room before the next one, in seconds of running: it shrinks the farther in, never below a jump and a moment to react
        double ease = Math.Min(1, at / 12000);
        double lo = 0.95 - 0.25 * ease, hi = 1.7 - 0.55 * ease;
        double gap = lo + course.NextDouble() * (hi - lo) + (hard ? 0.25 : 0);
        nextSpawnAt = at + (end - x) + pace * gap;
    }

    /// <summary>An obstacle at x, on the ground unless a height is given.</summary>
    Obstacle Add(Kind kind, double x, double? y = null)
    {
        var look = Looks[kind];
        double w = look.Width(Px), h = look.Height(Px);
        var o = new Obstacle { Kind = kind, X = x, Y = y ?? GroundY - h, W = w, H = h, From = spawning };
        obstacles.Add(o);
        return o;
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
        Popup($"{label}+{won}", x, y);
    }

    /// <summary>A lag spike he can run under but not jump through.</summary>
    bool Overhead(Obstacle o) => o.Kind == Kind.Spike && o.Y + o.H < GroundY - Reach - 2;

    /// <summary>For the recordings and the check: jumps ground obstacles in time (a short hop under hung spikes), stays down under the high ones, uses the power when it can.</summary>
    protected override void Pilot()
    {
        if (State == GameState.Ready) { JumpPressed(); return; }
        if (State != GameState.Running) return;
        if (charge >= 1 && obstacles.Any(o => !o.Dead && o.X > MascotX && o.X < MascotX + 120)) PowerPressed();
        if (jumpHeld && pilotLetGo >= 0 && time >= pilotLetGo) { JumpReleased(); pilotLetGo = -1; }
        double front = MascotX + MascotW * 0.82, run = speed * (Active(Power.SlowMo) ? 0.5 : 1) * (Active(Power.Dash) ? 1.7 : 1);
        var next = obstacles.Where(o => !o.Dead && !Overhead(o) && o.X + o.W > MascotX).OrderBy(o => o.X).FirstOrDefault();
        if (next == null || !onGround) { if (jumpHeld && pilotLetGo < 0 && vy > -CutSpeed) JumpReleased(); return; }
        bool hung = obstacles.Any(o => !o.Dead && Overhead(o) && o.X < next.Right + 20 && o.Right > next.X - 20);
        double closing = run + next.Crawl, gap = next.X - front;
        if (gap > closing * ((hung ? 0.05 : 0.1) + slop) + 4 || gap < -4) return;
        JumpPressed();
        if (hung) pilotLetGo = time + 0.07 + Math.Abs(slop) / 2;
        slop = (rng.NextDouble() * 2 - 1) * PilotSlop;
    }

    // ---- Drawing --------------------------------------------------------------

    protected override void Draw(DrawingContext dc)
    {
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
            dc.DrawEllipse(GameArt.OrbGlow, null, new Point(orb.X + 3, orb.Y + 3 + bob), 9, 9);
            PixelScene.Sprite(dc, GameArt.Orb, Math.Round(orb.X), Math.Round(orb.Y + bob), 2, c => c == 'X' ? GameArt.OrbInk : null);
        }

        foreach (var o in obstacles)
        {
            if (o.Dead) dc.PushOpacity(0.7);
            Looks[o.Kind].Draw(dc, o.X, o.Y, Px);
            if (o.Dead) dc.Pop();
        }

        DrawRunner(dc);
        DrawBits(dc);
        DrawHud(dc);
    }

    void DrawRunner(DrawingContext dc)
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
        MascotPainter.Draw(dc, Mascot, 0, 0, poseLeft > 0 ? pose : null, arms, arms, legFrame, 1, false, 0, eyes, 0);
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
        DrawPops(dc);
        DrawScore(dc);
        if (combo > 1)
        {
            var times = Text($"x{combo}", Font, 9, MeterReady);
            dc.DrawText(times, new Point(FieldWidth - 8 - times.Width, 17));
            double barRight = FieldWidth - 8 - times.Width - 4;
            dc.DrawRectangle(MeterBack, null, new Rect(barRight - 30, 23, 30, 2));
            dc.DrawRectangle(MeterReady, null, new Rect(barRight - 30, 23, Math.Round(30 * comboLeft / ComboTime), 2));
        }
        DrawCourse(dc);
        DrawMeter(dc, charge, powerLeft > 0 || chompArmed ? "ON" : shield ? "UP" : "");
    }
}
