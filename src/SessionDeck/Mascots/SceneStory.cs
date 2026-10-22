using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>
/// Story actions: a mascot's longer scene with props (a ball, an egg, a pile of coins), played
/// on a double-click and now and then when idle. A story is a list of timed beats (Stories.cs);
/// each beat can start things and steer them every frame through the StoryRun it's given.
/// </summary>
public sealed partial class PixelScene
{
    /// <summary>One beat of a story: Start runs as it begins, Tick every frame with its progress 0..1.</summary>
    public sealed record Beat(double Seconds, Action<StoryRun>? Start = null, Action<StoryRun, double>? Tick = null);

    /// <summary>A thing in a story: a sprite (or text on a slip of paper, or something drawn by hand) with simple physics.</summary>
    public sealed class Prop
    {
        public string[] Shape = { "X" };
        public Func<char, Brush?> Inks = _ => null;
        public double Px = 2;
        public double X, Y, VX, VY, Gravity;
        /// <summary>Lands on the ground and bounces back this much of its speed; below 0 it ignores the ground.</summary>
        public double Bounce = -1;
        public bool Behind;
        public double Opacity = 1;
        public double Age, Life = double.PositiveInfinity;
        public string? Text;
        public Brush? TextInk;
        public Action<DrawingContext, Prop>? Draw;
        /// <summary>Free for a story's own bookkeeping (where it came from, where it's going).</summary>
        public double HomeX, HomeY, FromX, FromY;
        public double Width => Shape[0].Length * Px;
        public double Height => Shape.Length * Px;
    }

    /// <summary>What a story steers: the mascot (walk, leap, rise, pose, eyes) and its props.</summary>
    public sealed class StoryRun
    {
        readonly PixelScene scene;
        internal Beat[] Beats = Array.Empty<Beat>();
        int index = -1;
        double at;
        double leapFromX, leapToX, leapFromRise, leapToRise, leapLeft, leapLength, leapArc;

        public readonly List<Prop> Props = new();
        public double? WalkTarget;
        public double WalkSpeed = 20;
        public double Rise, RiseTarget, RiseRate = 5;
        public Step? Pose;
        public int? ArmL, ArmR, Look;
        public EyeLook? Eyes;
        public bool Dizzy, Hidden;
        /// <summary>May leave the scene (to fetch a ball, steal a bell).</summary>
        public bool Roam;
        public bool Done { get; private set; }
        /// <summary>Seconds since the last frame, for Tick code that moves things itself.</summary>
        public double Dt { get; private set; }

        internal StoryRun(PixelScene scene) => this.scene = scene;

        public Mascot Mascot => scene.mascot;
        public double P => scene.P;
        public double X { get => scene.x; set => scene.x = value; }
        public double Width => scene.MascotWidth;
        public double Height => scene.mascot.Height;
        public double CenterX => scene.x + Width / 2;
        public double Top => scene.MascotTop(scene.ActualHeight);
        public double Ground => scene.ActualHeight;
        public double SceneWidth => scene.ActualWidth;
        public double MinX => Math.Max(scene.ActualWidth * 0.36, scene.SkyLeft + 8);
        public double Time => scene.time;
        public string Note => scene.SessionNote;
        public bool Flipped => scene.Flipped;
        /// <summary>+1 when there's more room to his right than to his left, else -1: where props go.</summary>
        public int Side => scene.ActualWidth - (scene.x + Width) >= scene.x - MinX ? 1 : -1;

        public double Random() => scene.rng.NextDouble();
        public Brush Ink(char c) => c == 'E' ? EyeBrush : MascotPainter.InksOf(scene.mascot).GetValueOrDefault(c) ?? EyeBrush;

        /// <summary>The n-th step of his signature move that has its own body frame, as a pose.</summary>
        public Step? SignaturePose(int n = 0) => scene.mascot.Signature.Where(s => s.Body != null).Skip(n).FirstOrDefault();

        /// <summary>An x on his body, counted in his cells from his left, mirrored when he faces left.</summary>
        public double At(double col) => Flipped ? scene.x + Width - col * P : scene.x + col * P;

        public void Face(int d)
        {
            scene.dir = d;
            Look = d;
        }

        public void Hop(double height, double seconds) => scene.Hop(height, seconds);

        /// <summary>Jumps in an arc to x, landing at the given height above the ground.</summary>
        public void Leap(double toX, double toRise, double seconds, double arc)
        {
            WalkTarget = null;
            leapFromX = scene.x;
            leapToX = toX;
            leapFromRise = Rise;
            leapToRise = toRise;
            leapLength = leapLeft = seconds;
            leapArc = arc;
        }

        public void Fx(char kind, double x, double y, int facing = 0) => scene.Spawn(kind, x, y, facing);
        public void Zed(double x, double y) => scene.zeds.Add(new Particle { X = x, Y = y, Life = 2.4 });
        public void Smoke(double x, double y, double vx) => scene.puffs.Add(new Particle { X = x, Y = y, VX = vx, Life = 0.9 });

        public Prop Add(string[] shape, Func<char, Brush?> inks, double x, double y, double px = 2)
        {
            var p = new Prop { Shape = shape, Inks = inks, X = x, Y = y, Px = px };
            Props.Add(p);
            return p;
        }

        public Prop Add(string[] shape, Brush ink, double x, double y, double px = 2) => Add(shape, c => c == 'X' ? ink : null, x, y, px);

        /// <summary>Text on a slip of paper, centred on centerX.</summary>
        public Prop Slip(string text, Brush paper, Brush ink, double centerX, double y)
        {
            var p = new Prop { Text = text, Inks = _ => paper, TextInk = ink, X = centerX, Y = y };
            Props.Add(p);
            return p;
        }

        public void Remove(Prop p) => Props.Remove(p);

        internal void Advance(double dt)
        {
            Dt = dt;
            if (index < 0)
            {
                index = 0;
                Beats[0].Start?.Invoke(this);
            }
            at += dt;
            while (at >= Beats[index].Seconds)
            {
                Beats[index].Tick?.Invoke(this, 1);
                at -= Beats[index].Seconds;
                if (++index >= Beats.Length)
                {
                    Done = true;
                    return;
                }
                Beats[index].Start?.Invoke(this);
            }
            Beats[index].Tick?.Invoke(this, at / Beats[index].Seconds);
            Move(dt);
            foreach (var p in Props.ToList()) Physics(p, dt);
        }

        void Move(double dt)
        {
            if (leapLeft > 0)
            {
                leapLeft = Math.Max(0, leapLeft - dt);
                double t = 1 - leapLeft / leapLength;
                scene.x = leapFromX + (leapToX - leapFromX) * t;
                Rise = leapFromRise + (leapToRise - leapFromRise) * t + Math.Sin(t * Math.PI) * leapArc;
                if (Math.Abs(leapToX - leapFromX) > 1) scene.dir = Math.Sign(leapToX - leapFromX);
                scene.legFrame = 1;
                if (leapLeft <= 0) Rise = RiseTarget = leapToRise;
            }
            else
            {
                Rise += (RiseTarget - Rise) * Math.Min(1, dt * RiseRate);
                if (WalkTarget is double target)
                {
                    double step = WalkSpeed * dt, gap = target - scene.x;
                    if (Math.Abs(gap) <= step) { scene.x = target; WalkTarget = null; }
                    else
                    {
                        scene.dir = Math.Sign(gap);
                        scene.x += scene.dir * step;
                        scene.legFrame = (int)(scene.time * (WalkSpeed > 40 ? 10 : 6)) % 2;
                        scene.look = (int)scene.dir;
                    }
                }
            }
            if (Look is int look) scene.look = look;
        }

        void Physics(Prop p, double dt)
        {
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                Props.Remove(p);
                return;
            }
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            p.VY += p.Gravity * dt;
            if (p.Bounce >= 0 && p.Y + p.Height >= Ground)
            {
                p.Y = Ground - p.Height;
                p.VY = Math.Abs(p.VY) > 30 ? -p.VY * p.Bounce : 0;
                p.VX *= Math.Max(0, 1 - 4 * dt);
            }
        }
    }

    static readonly Brush EyeBrush = Ink.Of(0x16, 0x16, 0x18);

    StoryRun? story;
    Mood storyMood; // what was going on when it started: only a change to Waiting or Maxed stops it

    /// <summary>A line about the sessions, for the pigeon's note ("2 working · 1 needs you").</summary>
    public string SessionNote { get; set; } = "All quiet";

    public static string NoteFor(int needYou, int working) =>
        needYou == 0 && working == 0 ? "All quiet"
        : string.Join(" · ", new[]
        {
            working > 0 ? $"{working} working" : null,
            needYou > 0 ? $"{needYou} need{(needYou == 1 ? "s" : "")} you" : null,
        }.Where(s => s != null));

    public bool PlayingStory => trick == Trick.Story;

    /// <summary>Plays the mascot's story now (also for --stories).</summary>
    public void PlayStory()
    {
        strollTo = double.NaN;
        StartStory();
    }

    void StartStory()
    {
        var script = Stories.For(mascot);
        if (script == null)
        {
            StartSignature();
            return;
        }
        var run = new StoryRun(this);
        run.Beats = script(run);
        story = run;
        storyMood = Mood;
        StartTrick(Trick.Story, run.Beats.Sum(b => b.Seconds) + 1);
    }

    void AdvanceStory(double dt)
    {
        if (story == null) return;
        story.Advance(dt);
        if (story.Done) trick = Trick.None;
    }

    /// <summary>Clears what a story left behind, once it's over or interrupted.</summary>
    void EndStory()
    {
        story = null;
        hopLeft = 0;
    }

    /// <summary>An effect at a spot, for stories: facing 0 sends sound both ways.</summary>
    void Spawn(char kind, double fx0, double fy0, int facing)
    {
        switch (kind)
        {
            case 'q':
                if (facing != 0) fx.Add(new Particle { Kind = 'q', X = fx0, Y = fy0, VX = facing * 26, Life = 0.6 });
                else
                {
                    fx.Add(new Particle { Kind = 'q', X = fx0 - 12, Y = fy0, VX = -26, Life = 0.6 });
                    fx.Add(new Particle { Kind = 'q', X = fx0 + 4, Y = fy0, VX = 26, Life = 0.6 });
                }
                break;
            case 'c':
            case 'p':
                for (int i = 0; i < (kind == 'c' ? 3 : 4); i++)
                    fx.Add(new Particle { Kind = kind, X = fx0, Y = fy0, VX = (rng.NextDouble() - 0.5) * (kind == 'c' ? 30 : 50), VY = -25 - rng.NextDouble() * 20, Life = kind == 'c' ? 0.7 : 1.1 });
                break;
            case 'w':
                for (int i = 0; i < 4; i++)
                    fx.Add(new Particle { Kind = 'w', X = fx0, Y = fy0, VX = (rng.NextDouble() - 0.5) * 70, VY = -50 - rng.NextDouble() * 40, Life = 0.6 });
                break;
            case 'x':
                fx.Add(new Particle { Kind = 'x', X = fx0 - 4, Y = fy0, VX = (facing == 0 ? 1 : facing) * 14, Life = 0.3 });
                break;
            case 'D':
                // A bug bursting into bits
                for (int i = 0; i < 8; i++)
                    fx.Add(new Particle { Kind = 'D', Bit = rng.Next(2), X = fx0, Y = fy0, VX = (rng.NextDouble() - 0.5) * 110, VY = -60 - rng.NextDouble() * 70, Life = 0.9 });
                break;
            default:
                fx.Add(new Particle { Kind = kind, X = fx0 - 4, Y = fy0 - 6, VY = -22 - rng.NextDouble() * 10, Life = kind == 's' ? 0.7 : 1.3 });
                break;
        }
    }

    void DrawProps(DrawingContext dc, bool behind)
    {
        if (story == null) return;
        foreach (var p in story.Props)
        {
            if (p.Behind != behind || p.Opacity <= 0) continue;
            dc.PushOpacity(Math.Clamp(p.Opacity, 0, 1));
            if (p.Draw != null) p.Draw(dc, p);
            else if (p.Text != null)
            {
                // Text on a slip of paper, centred on X
                var text = new FormattedText(p.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NoteFace, 9,
                                             p.TextInk ?? EyeBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double w = Math.Ceiling(text.Width) + 8, left = Math.Round(Math.Clamp(p.X - w / 2, 2, Math.Max(2, ActualWidth - w - 2)));
                dc.DrawRectangle(p.Inks('X'), null, new Rect(left, Math.Round(p.Y), w, Math.Ceiling(text.Height) + 4));
                dc.DrawText(text, new Point(left + 4, Math.Round(p.Y) + 2));
            }
            else Sprite(dc, p.Shape, Math.Round(p.X), Math.Round(p.Y), p.Px, p.Inks);
            dc.Pop();
        }
    }

    static readonly Typeface NoteFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
}
