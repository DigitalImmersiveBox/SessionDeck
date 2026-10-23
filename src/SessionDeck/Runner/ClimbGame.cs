using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>
/// Ship It: bounce your mascot up the pipeline, from build to prod and beyond. He bounces on his own;
/// steer him left and right (off one side, in at the other). Green builds hold, flaky tests crumble after
/// one bounce, sliding ones move, and red builds end the run. The CI timeout rises from below, faster
/// and faster: stay above it.
/// </summary>
public sealed class ClimbGame : ArcadeGame
{
    const double AX0 = 90, AW = 200;            // the shaft, which wraps round
    const double Gravity = 700, Bounce = 300, SpringBounce = 470, RocketSpeed = 420;
    const double Accel = 1100, MaxRun = 155, Friction = 700;
    const double PlatH = 3, ManH = 14;
    const double ChargePerSecond = 1 / 40.0, SpringCharge = 0.3;

    enum Kind { Green, Flaky, Slide, Red }

    sealed class Platform
    {
        public Kind Kind;
        public double X, Alt, W;    // left edge in the shaft, height of its top
        public double VX;           // sliding ones
        public bool Spring, Broken;
        public double FallBy;       // a crumbled one drops away
    }

    static readonly string[] Stages = { "BUILD", "TEST", "REVIEW", "STAGING", "PROD" };
    static readonly Brush GreenInk = Ink.Of(0x5F, 0xC2, 0x7E);
    static readonly Brush FlakyInk = Ink.Of(0x8A, 0x8D, 0x96);
    static readonly Brush SlideInk = Ink.Of(0x60, 0xA5, 0xFA);
    static readonly Brush RedInk = Ink.Of(0xE0, 0x60, 0x5A);
    static readonly Brush ShaftInk = Ink.Of(0x14, 0x15, 0x19);
    static readonly Brush DeadlineInk = Frozen(new SolidColorBrush(Color.FromArgb(0x60, 0xE0, 0x60, 0x5A)));
    static readonly Pen NetPen = FrozenPen(Color.FromArgb(0xC0, 0x6F, 0xD3, 0xE8), 1);
    static readonly Pen StagePen = FrozenPen(Color.FromArgb(0x40, 0xC9, 0xCA, 0xCF), 1);

    Random course = new();
    readonly List<Platform> platforms = new();
    double px, alt, vx, vy;     // him: left edge in the shaft and height of his feet
    double camAlt, topAlt, best, deadline, deadlineSpeed;
    double charge, slowLeft, rocketLeft, netLeft, landed;
    int steer, mouseSteer, bonus;
    double lastGuaranteedX;

    public override string Id => "climb";
    public override string Title => "Ship It";
    public override string Keys => $"←→ / A D / hold a click: steer · X / right-click: {Mascot.PowerName}";
    protected override string Hint => "Climb as high as you can before the CI timeout catches you";
    public override int Score => (int)(best / 4) + bonus;

    public ClimbGame() => NewRun();

    double ManW => Math.Min(20, MascotWidthFor(ManH));

    protected override void Reset()
    {
        course = new Random(CourseSeed());
        platforms.Clear();
        bits.Clear();
        px = AX0 + AW / 2 - ManW / 2;
        alt = 10;
        vx = 0;
        vy = Bounce;
        camAlt = 0;
        best = 0;
        bonus = 0;
        deadline = -70;
        deadlineSpeed = 10;
        charge = slowLeft = rocketLeft = netLeft = landed = 0;
        steer = mouseSteer = 0;
        platforms.Add(new Platform { Kind = Kind.Green, X = AX0, Alt = 6, W = AW });
        topAlt = 6;
        lastGuaranteedX = AX0 + AW / 2;
        Build();
    }

    /// <summary>Platforms up to a screen above the top: one he can always reach in each row, decoys and red builds beside it.</summary>
    void Build()
    {
        while (topAlt < camAlt + FieldHeight * 2)
        {
            double h = topAlt;
            double hard = Math.Min(1, h / 4000);
            double gap = 18 + course.NextDouble() * (12 + 26 * hard); // never past what a bounce reaches
            topAlt = h + gap;
            double w = 30 - 10 * hard;
            // The one he can reach: not too far sideways from the last
            double x = Wrap(lastGuaranteedX + (course.NextDouble() * 2 - 1) * (40 + 50 * hard) - w / 2);
            lastGuaranteedX = x + w / 2;
            var kind = Kind.Green;
            double roll = course.NextDouble();
            if (h > 250 && roll < 0.15 + 0.35 * hard) kind = Kind.Flaky;
            else if (h > 600 && roll < 0.3 + 0.3 * hard) kind = Kind.Slide;
            var main = new Platform { Kind = kind, X = x, Alt = topAlt, W = w, Spring = course.NextDouble() < 0.06 };
            if (kind == Kind.Slide) main.VX = (course.Next(2) == 0 ? -1 : 1) * (30 + 40 * hard);
            platforms.Add(main);
            // A red build nearby, more often higher up
            if (h > 300 && course.NextDouble() < 0.2 + 0.4 * hard)
                platforms.Add(new Platform { Kind = Kind.Red, X = Wrap(x + w + 10 + course.NextDouble() * 60), Alt = topAlt + (course.NextDouble() - 0.5) * 10, W = 24 });
            // And a flaky decoy now and then
            if (course.NextDouble() < 0.25)
                platforms.Add(new Platform { Kind = Kind.Flaky, X = Wrap(x - 40 - course.NextDouble() * 60), Alt = topAlt - gap / 2, W = 24 });
        }
    }

    static double Wrap(double x)
    {
        double r = (x - AX0) % AW;
        return AX0 + (r < 0 ? r + AW : r);
    }

    // ---- Control --------------------------------------------------------------

    public override bool Press(Key key, bool repeat)
    {
        if (State != GameState.Running) return base.Press(key, repeat);
        switch (key)
        {
            case Key.Left or Key.A: steer = -1; return true;
            case Key.Right or Key.D: steer = 1; return true;
            case Key.X or Key.E or Key.LeftShift or Key.RightShift or Key.Space or Key.Up: if (!repeat) UsePower(); return true;
        }
        return false;
    }

    public override void Release(Key key)
    {
        if ((key is Key.Left or Key.A && steer < 0) || (key is Key.Right or Key.D && steer > 0)) steer = 0;
    }

    public override void Click(Point at, bool right)
    {
        if (State != GameState.Running) { base.Click(at, right); return; }
        if (right) UsePower();
        else SteerToward(at);
    }

    public override void ClickUp(Point at, bool right)
    {
        if (!right) mouseSteer = 0;
    }

    public override void Hover(Point at)
    {
        if (mouseSteer != 0) SteerToward(at);
    }

    void SteerToward(Point at)
    {
        // Steer toward where the pointer is, the short way round
        double d = at.X - (px + ManW / 2);
        if (Math.Abs(d) > AW / 2) d -= Math.Sign(d) * AW;
        mouseSteer = Math.Abs(d) < 4 ? 2 : Math.Sign(d); // 2: held but right over him
    }

    void UsePower()
    {
        if (State != GameState.Running || charge < 1) return;
        charge = 0;
        switch (Family)
        {
            case PowerFamily.Clear:
                foreach (var p in platforms.Where(p => p.Kind == Kind.Red && p.Alt < camAlt + FieldHeight))
                {
                    p.Kind = Kind.Green;
                    Burst(GameArt.Orb, GameArt.OrbInk, p.X + p.W / 2, ScreenY(p.Alt), 3, falls: true);
                }
                Popup("ALL GREEN", px + ManW / 2, ScreenY(alt) - 20);
                break;
            case PowerFamily.Slow:
                slowLeft = 6;
                Popup("TIMEOUT PAUSED", px + ManW / 2, ScreenY(alt) - 20, MeterFill);
                break;
            case PowerFamily.Guard:
                netLeft = 12;
                Popup("SAFETY NET", px + ManW / 2, ScreenY(alt) - 20, MeterFill);
                break;
            case PowerFamily.Boost:
                rocketLeft = 1.1;
                break;
        }
    }

    // ---- Simulation -----------------------------------------------------------

    double ScreenY(double a) => FieldHeight - (a - camAlt);

    protected override void Step(double dt)
    {
        landed = Math.Max(0, landed - dt);
        if (State != GameState.Running) return;
        slowLeft = Math.Max(0, slowLeft - dt);
        netLeft = Math.Max(0, netLeft - dt);
        charge = Math.Min(1, charge + ChargePerSecond * dt);

        int dir = steer != 0 ? steer : mouseSteer is -1 or 1 ? mouseSteer : 0;
        if (dir != 0) vx = Math.Clamp(vx + dir * Accel * dt, -MaxRun, MaxRun);
        else vx = Math.Sign(vx) * Math.Max(0, Math.Abs(vx) - Friction * dt);
        px = Wrap(px + vx * dt);

        double was = alt;
        if (rocketLeft > 0)
        {
            rocketLeft -= dt;
            vy = RocketSpeed;
            bits.Add(new Bit { Shape = GameArt.Dot, Ink = rng.Next(2) == 0 ? MeterReady : BadInk, X = px + ManW / 2 + (rng.NextDouble() - 0.5) * 4, Y = ScreenY(alt), VY = 60, Life = 0.4 });
        }
        else vy -= Gravity * (slowLeft > 0 ? 0.7 : 1) * dt;
        alt += vy * dt;

        foreach (var p in platforms)
        {
            if (p.Kind == Kind.Slide)
            {
                p.X += p.VX * dt;
                if (p.X < AX0 || p.X + p.W > AX0 + AW) { p.VX = -p.VX; p.X = Math.Clamp(p.X, AX0, AX0 + AW - p.W); }
            }
            if (p.Broken) p.FallBy += 120 * dt;
        }

        // Landing: only on the way down, through the top of a platform
        if (vy < 0 && rocketLeft <= 0)
            foreach (var p in platforms)
            {
                if (p.Broken || was < p.Alt || alt > p.Alt) continue;
                if (!Overlaps(p)) continue;
                if (p.Kind == Kind.Red)
                {
                    Burst(GameArt.Puff, RedInk, px + ManW / 2, ScreenY(p.Alt), 6, falls: false);
                    Lose($"Broke the build at {Score} · Space to try again");
                    return;
                }
                alt = p.Alt;
                vy = p.Spring ? SpringBounce : Bounce;
                landed = 0.15;
                if (p.Spring)
                {
                    charge = Math.Min(1, charge + SpringCharge);
                    bonus += 10;
                    Popup("+10", px + ManW / 2, ScreenY(p.Alt) - 10);
                }
                if (p.Kind == Kind.Flaky)
                {
                    p.Broken = true;
                    Burst(GameArt.Dot, FlakyInk, p.X + p.W / 2, ScreenY(p.Alt), 4, falls: true);
                }
                break;
            }

        best = Math.Max(best, alt);
        // The camera keeps him in the lower half, and never goes back down
        camAlt = Math.Max(camAlt, alt - FieldHeight * 0.45);
        // The timeout rises faster and faster, and never falls far behind the screen
        // (at most a little slower than a perfect climb, so it's never out of reach)
        deadlineSpeed = Math.Min(45, 12 + best / 120);
        if (slowLeft <= 0) deadline = Math.Max(deadline + deadlineSpeed * dt, camAlt - 60);

        if (alt < camAlt - 8 || alt < deadline)
        {
            if (netLeft > 0)
            {
                netLeft = 0;
                alt = Math.Max(alt, Math.Max(camAlt, deadline) + 2);
                vy = SpringBounce;
                Popup("SAVED", px + ManW / 2, FieldHeight - 20, MeterFill);
            }
            else
            {
                Lose(alt < deadline ? $"Timed out at {Score} · Space to try again" : $"Fell out of the pipeline at {Score} · Space to try again");
                return;
            }
        }
        platforms.RemoveAll(p => p.Alt - p.FallBy < camAlt - 20);
        Build();
    }

    bool Overlaps(Platform p)
    {
        double l = px + ManW * 0.2, r = px + ManW * 0.8;
        // The shaft wraps, so check the platform where it is and one shaft-width either side
        for (int k = -1; k <= 1; k++)
            if (l < p.X + p.W + k * AW && r > p.X + k * AW) return true;
        return false;
    }

    /// <summary>Steers for the nearest safe platform it can still reach, going up.</summary>
    protected override void Pilot()
    {
        if (State == GameState.Ready) { StartOrResume(); return; }
        if (State != GameState.Running) return;
        if (charge >= 1 && (alt - deadline < 40 || vy < -200)) UsePower();
        double reach = alt + vy * vy / (2 * Gravity) * (vy > 0 ? 1 : 0);
        var target = platforms.Where(p => !p.Broken && p.Kind != Kind.Red && p.Alt <= reach - 2 && p.Alt > alt - (vy > 0 ? 200 : 0) - 1)
                              .OrderByDescending(p => p.Alt).FirstOrDefault();
        if (target == null) { steer = 0; return; }
        double d = target.X + target.W / 2 - (px + ManW / 2);
        if (Math.Abs(d) > AW / 2) d -= Math.Sign(d) * AW;
        steer = Math.Abs(d) < 3 ? 0 : Math.Sign(d);
    }

    // ---- Drawing --------------------------------------------------------------

    protected override void Draw(DrawingContext dc)
    {
        dc.DrawRectangle(ShaftInk, null, new Rect(AX0, 0, AW, FieldHeight));
        dc.PushClip(new RectangleGeometry(new Rect(AX0, 0, AW, FieldHeight)));

        // A line at each pipeline stage, with its name beside the shaft
        double stageEvery = 500;
        for (int s = (int)Math.Max(0, Math.Floor(camAlt / stageEvery)); s * stageEvery < camAlt + FieldHeight; s++)
        {
            if (s == 0) continue;
            double y = Math.Round(ScreenY(s * stageEvery)) + 0.5;
            dc.DrawLine(StagePen, new Point(AX0, y), new Point(AX0 + AW, y));
        }

        foreach (var p in platforms)
        {
            double y = Math.Round(ScreenY(p.Alt) + p.FallBy);
            if (y < -4 || y > FieldHeight + 4) continue;
            var ink = p.Kind switch { Kind.Flaky => FlakyInk, Kind.Slide => SlideInk, Kind.Red => RedInk, _ => GreenInk };
            for (int k = -1; k <= 1; k++)
            {
                double x = Math.Round(p.X + k * AW);
                if (p.Broken) dc.PushOpacity(0.5);
                if (p.Kind == Kind.Flaky)
                    for (double dx = 0; dx < p.W; dx += 5) dc.DrawRectangle(ink, null, new Rect(x + dx, y, Math.Min(3, p.W - dx), PlatH));
                else dc.DrawRectangle(ink, null, new Rect(x, y, p.W, PlatH));
                if (p.Kind == Kind.Red)
                    for (double dx = 1; dx < p.W - 2; dx += 4) dc.DrawRectangle(ink, null, new Rect(x + dx, y - 2, 2, 2));
                if (p.Spring) PixelScene.Sprite(dc, GameArt.Orb, x + p.W / 2 - 3, y - 6, 2, c => c == 'X' ? GameArt.OrbInk : null);
                if (p.Broken) dc.Pop();
            }
        }

        // Him, drawn twice when he's halfway round the wrap
        double my = ScreenY(alt);
        var eyes = State == GameState.Over ? EyeLook.Closed : vy < -250 ? EyeLook.Wide : landed > 0 ? EyeLook.Happy : EyeLook.Open;
        int arms = rocketLeft > 0 ? 3 : vy > 0 ? 2 : 0;
        double mh = ManW * Mascot.Height / Mascot.Width;
        for (int k = -1; k <= 1; k++) DrawMascot(dc, px + k * AW, my, Math.Min(ManH, mh), arms, eyes: eyes);
        if (netLeft > 0)
        {
            double y = FieldHeight - 3.5;
            dc.DrawLine(NetPen, new Point(AX0, y), new Point(AX0 + AW, y));
        }

        // The CI timeout, rising from below
        double dy = ScreenY(deadline);
        if (dy < FieldHeight) dc.DrawRectangle(DeadlineInk, null, new Rect(AX0, dy, AW, FieldHeight - dy + 1));
        if (dy < FieldHeight) dc.DrawRectangle(RedInk, null, new Rect(AX0, Math.Round(dy), AW, 1));
        DrawBits(dc);
        dc.Pop();

        dc.DrawRectangle(GroundInk, null, new Rect(AX0 - 1, 0, 1, FieldHeight));
        dc.DrawRectangle(GroundInk, null, new Rect(AX0 + AW, 0, 1, FieldHeight));

        // Stage names on the left, how close the timeout is on the right
        for (int s = (int)Math.Max(1, Math.Floor(camAlt / stageEvery)); s * stageEvery < camAlt + FieldHeight; s++)
        {
            string name = Stages[(s - 1) % Stages.Length] + (s > Stages.Length ? $" ×{(s - 1) / Stages.Length + 1}" : "");
            var label = Text(name, Label, 6, DimInk);
            dc.DrawText(label, new Point(AX0 - 6 - label.Width, Math.Round(ScreenY(s * stageEvery)) - label.Height / 2));
        }
        string stage = best < stageEvery ? "COMMIT" : Stages[((int)(best / stageEvery) - 1) % Stages.Length];
        dc.DrawText(Text($"REACHED {stage}", Label, 7, TextInk), new Point(AX0 + AW + 8, 30));
        double gapToTimeout = Math.Max(0, alt - deadline);
        dc.DrawText(Text(slowLeft > 0 ? $"TIMEOUT PAUSED {Math.Ceiling(slowLeft)} s" : $"TIMEOUT {gapToTimeout:0} BELOW", Label, 7, gapToTimeout < 40 ? BadInk : DimInk), new Point(AX0 + AW + 8, 42));
        if (netLeft > 0) dc.DrawText(Text($"NET {Math.Ceiling(netLeft)} s", Label, 7, MeterFill), new Point(AX0 + AW + 8, 54));

        DrawPops(dc);
        DrawCourse(dc);
        DrawScore(dc);
        DrawMeter(dc, charge, rocketLeft > 0 || slowLeft > 0 ? "ON" : netLeft > 0 ? "UP" : "");
    }
}
