using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Beat = ClaudeSessions.PixelScene.Beat;
using Prop = ClaudeSessions.PixelScene.Prop;
using StoryRun = ClaudeSessions.PixelScene.StoryRun;

namespace ClaudeSessions;

/// <summary>
/// Each mascot's story: a few seconds of a little scene with props, written as timed beats
/// (see PixelScene.Beat). Props go on the side of him with more room (StoryRun.Side).
/// </summary>
static class Stories
{
    static Dictionary<Mascot, Func<StoryRun, Beat[]>>? byMascot;

    public static Func<StoryRun, Beat[]>? For(Mascot m)
    {
        byMascot ??= new()
        {
            [Mascot.For(MascotKind.Dib)] = Dib,
            [Mascot.For(MascotKind.DibWhite)] = Dib,
            [Mascot.For(MascotKind.Clawd)] = Clawd,
            [Mascot.For(MascotKind.Cat)] = Cat,
            [Mascot.For(MascotKind.Dog)] = Dog,
            [Mascot.For(MascotKind.Mouse)] = Mouse,
            [Mascot.For(MascotKind.Flower)] = Flower,
            [Mascot.For(MascotKind.Chicken)] = Chicken,
            [Mascot.For(MascotKind.Goose)] = Goose,
            [Mascot.For(MascotKind.Duck)] = Duck,
            [Mascot.For(MascotKind.Bee)] = Bee,
            [Mascot.For(MascotKind.Pigeon)] = Pigeon,
            [Mascot.For(MascotKind.Goat)] = Goat,
            [Mascot.For(MascotKind.Knight)] = Knight,
            [Mascot.For(MascotKind.Dragon)] = Dragon,
            [Mascot.For(MascotKind.Robot)] = Robot,
        };
        return byMascot.GetValueOrDefault(m);
    }

    // ---- Props ----------------------------------------------------------------

    static readonly string[] Dot = { "X" };
    static readonly string[] Block = { "XX", "XX" };
    static readonly string[] Ball = { ".XX.", "XXXX", "XXXX", ".XX." };
    static readonly string[] Cheese = { "......XX", "....XXXX", "..XXOXXX", "XXXXXXOX", "XOXXXXXX" };
    static readonly string[] CloudShape = { "...XXX....", ".XXXXXXX..", "XXXXXXXXXX" };
    static readonly string[] Drop = { "X", "X" };
    static readonly string[] BeeUp = { "CC..", "BABA", ".BA." };
    static readonly string[] BeeDown = { "....", "BABA", "CBA." };
    static readonly string[] Egg = { ".XX.", "XXXX", "XXXX", ".XX." };
    static readonly string[] EggCracked = { ".XX.", "XOXX", "XXOX", ".XX." };
    static readonly string[] Shell = { "X.X", "XXX" };
    static readonly string[] Chick = { ".YY.", "YYEO", "YYYY", ".OO." };
    static readonly string[] Bell = { "..X..", ".XXX.", ".XXX.", "XXXXX", "..O.." };
    static readonly string[] Puddle =
    {
        "...XXXXXXXXXXXXXXXXXX...",
        ".XXXXXXOOOXXXXXXXXXXXXX.",
        "XXXXXXXXXXXXXXXXXXXXXXXX",
    };
    static readonly string[] Duckling = { ".YY.", "YYEO", "YYYY" };
    static readonly string[] SmallFlower = { ".P.P.", "PPYPP", ".PPP.", "..G..", "..G.." };
    static readonly string[] Hive = { "..HH..", ".HHHH.", "HDDDDH", "HHHHHH", "HDDDDH", ".HHHH." };
    static readonly string[] Scroll = { "XXXXXX", "XOOOOX", "XXXXXX" };
    static readonly string[] Rock = { ".XXXX.", "XXXXXX", "XOXXXX" };
    static readonly string[] BugA = { ".X...X.", "..X.X..", ".XXXXX.", "XXOXOXX", ".XXXXX.", "X.X.X.X" };
    static readonly string[] BugB = { ".X...X.", "..X.X..", ".XXXXX.", "XXOXOXX", ".XXXXX.", ".X.X.X." };
    static readonly string[] Coin = { ".XX.", "XOOX", ".XX." };

    static readonly Brush Red = Rgb(0xE0605A);
    static readonly Brush Laser = Rgb(0xFF3B3B);
    static readonly Brush CloudInk = Rgb(0x5A5D66);
    static readonly Brush RainInk = Rgb(0x5B86B0);
    static readonly Brush Pollen = Rgb(0xF5D24A);
    static readonly Brush Honey = Rgb(0xF5B83D);
    static readonly Brush Paper = Rgb(0xF0EEE9);
    static readonly Brush PaperLine = Rgb(0xB9BCC4);
    static readonly Brush Dark = Rgb(0x1E1F23);
    static readonly Brush Green = Rgb(0x2E9E5B);
    static readonly Brush LaserGlow = Glow(0xFF, 0x3B, 0x3B);
    static readonly Brush Beam = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0x3B, 0x3B)).Frozen();
    static readonly Brush GridLine = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x3B, 0x3B)).Frozen();

    static readonly Func<char, Brush?> CheeseInks = Inks(('X', 0xF5D24A), ('O', 0xC9A23A));
    static readonly Func<char, Brush?> BeeInks = Inks(('B', 0xF5C842), ('A', 0x5B4826), ('C', 0xDDEBFA));
    static readonly Func<char, Brush?> EggInks = Inks(('X', 0xF5EEDC), ('O', 0x8A7F6A));
    static readonly Func<char, Brush?> ChickInks = Inks(('Y', 0xF5D24A), ('E', 0x161618), ('O', 0xF2A93B));
    static readonly Func<char, Brush?> BellInks = Inks(('X', 0xF5C842), ('O', 0x5B4826));
    static readonly Func<char, Brush?> PuddleInks = Inks(('X', 0x3F6A94), ('O', 0x8DB8DE));
    static readonly Func<char, Brush?> SmallFlowerInks = Inks(('P', 0xF28CA8), ('Y', 0xF5D76E), ('G', 0x6DBE6A));
    static readonly Func<char, Brush?> HiveInks = Inks(('H', 0xC8913C), ('D', 0x8A5A2A));
    static readonly Func<char, Brush?> ScrollInks = Inks(('X', 0xEDE3C8), ('O', 0xC9B48A));
    static readonly Func<char, Brush?> RockInks = Inks(('X', 0x8A8B90), ('O', 0x6E6F75));
    static readonly Func<char, Brush?> BugInks = Inks(('X', 0xE0605A), ('O', 0xF0EEE9));
    static readonly Func<char, Brush?> CoinInks = Inks(('X', 0xE0B84A), ('O', 0xF5D76E));
    static readonly Func<char, Brush?> BlockRed = Inks(('X', 0xE05050));
    static readonly Func<char, Brush?> BlockBlue = Inks(('X', 0x60A5FA));
    static readonly Func<char, Brush?> BlockYellow = Inks(('X', 0xF5D24A));

    // ---- dib: breaks into pixels, then the letters fly back together --------------------

    static Beat[] Dib(StoryRun run)
    {
        var cells = new List<Prop>();
        return new[]
        {
            new Beat(0.35, Start: r => { r.Eyes = EyeLook.Wide; r.Hop(5, 0.3); }),
            new Beat(1.2, Start: r =>
            {
                var picture = r.Mascot.Standing;
                double left = r.X, top = r.Top, p = r.P;
                for (int row = 0; row < picture.Length; row++)
                    for (int col = 0; col < picture[row].Length; col++)
                    {
                        char c = picture[row][col];
                        if (c == '.') continue;
                        var cell = r.Add(Dot, r.Ink(c), left + col * p, top + row * p, p);
                        cell.HomeX = cell.X;
                        cell.HomeY = cell.Y;
                        cell.VX = (r.Random() - 0.5) * 170;
                        cell.VY = -50 - r.Random() * 130;
                        cell.Gravity = 420;
                        cell.Bounce = 0.3;
                        cells.Add(cell);
                    }
                r.Hidden = true;
                r.Fx('s', r.CenterX, top, 0);
            }),
            new Beat(0.4),
            new Beat(0.9,
                Start: r =>
                {
                    foreach (var c in cells)
                    {
                        c.FromX = c.X;
                        c.FromY = c.Y;
                        c.VX = c.VY = c.Gravity = 0;
                        c.Bounce = -1;
                    }
                },
                Tick: (r, t) =>
                {
                    // Left to right, each pixel arcs home: d, then i, then b snap back into place
                    foreach (var c in cells)
                    {
                        double e = Ease(Math.Clamp(t * 1.4 - (c.HomeX - r.X) / r.Width * 0.4, 0, 1));
                        c.X = Lerp(c.FromX, c.HomeX, e);
                        c.Y = Lerp(c.FromY, c.HomeY, e) - Math.Sin(e * Math.PI) * 18;
                    }
                }),
            new Beat(0.1, Start: r =>
            {
                foreach (var c in cells) r.Remove(c);
                r.Hidden = false;
                r.Eyes = EyeLook.Happy;
                r.Fx('s', r.CenterX, r.Top, 0);
            }),
            new Beat(0.7, Start: r => r.Hop(8, 0.4)),
        };
    }

    // ---- Clawd: juggles three blocks, one lands on his head ------------------------------

    static Beat[] Clawd(StoryRun run)
    {
        const double G = 520, Flight = 0.62;
        var blocks = new List<Prop>();
        var launched = new bool[3];
        double lastL = -1, lastR = -1;
        bool bonked = false;

        double HandX(StoryRun r, bool left) => left ? r.X + r.P : r.X + 12 * r.P;
        double HandY(StoryRun r) => r.Top + r.P;
        void Throw(StoryRun r, Prop b, bool fromLeft)
        {
            double toX = HandX(r, !fromLeft) - b.Width / 2;
            b.X = HandX(r, fromLeft) - b.Width / 2;
            b.Y = HandY(r) - b.Height;
            b.VX = (toX - b.X) / Flight;
            b.VY = -G * Flight / 2;
            b.Opacity = 1;
            if (fromLeft) lastL = r.Time;
            else lastR = r.Time;
        }

        return new[]
        {
            new Beat(0.2, Start: r =>
            {
                foreach (var inks in new[] { BlockRed, BlockBlue, BlockYellow })
                {
                    var b = r.Add(Block, inks, 0, 0, 3);
                    b.Gravity = G;
                    b.Opacity = 0;
                    blocks.Add(b);
                }
                r.ArmL = r.ArmR = 2;
            }),
            new Beat(3.0, Tick: (r, t) =>
            {
                for (int i = 0; i < 3; i++)
                    if (!launched[i] && t * 3 >= i * 0.21)
                    {
                        launched[i] = true;
                        Throw(r, blocks[i], i % 2 == 0);
                    }
                for (int i = 0; i < 3; i++)
                {
                    var b = blocks[i];
                    if (launched[i] && b.VY > 0 && b.Y + b.Height >= HandY(r)) Throw(r, b, b.X + b.Width / 2 < r.CenterX);
                    if (!launched[i]) { b.VY = 0; b.Gravity = 0; }
                    else b.Gravity = G;
                }
                r.ArmL = r.Time - lastL < 0.15 ? 3 : 1;
                r.ArmR = r.Time - lastR < 0.15 ? 3 : 1;
            }),
            new Beat(0.8,
                Start: r =>
                {
                    // Two get away, the third comes down on his head
                    blocks[0].VX = -40; blocks[0].VY = -40; blocks[0].Bounce = 0.4;
                    blocks[1].VX = 40; blocks[1].VY = -40; blocks[1].Bounce = 0.4;
                    var b = blocks[2];
                    b.X = r.CenterX - b.Width / 2;
                    b.Y = r.Top - 50;
                    b.VX = b.VY = 0;
                    r.Eyes = EyeLook.Wide;
                    r.ArmL = r.ArmR = 2;
                },
                Tick: (r, t) =>
                {
                    var b = blocks[2];
                    if (bonked || b.Y + b.Height < r.Top) return;
                    bonked = true;
                    b.Y = r.Top - b.Height;
                    b.VY = -110;
                    b.VX = 50;
                    b.Bounce = 0.35;
                    r.Dizzy = true;
                    r.Eyes = EyeLook.Closed;
                    r.ArmL = r.ArmR = 0;
                }),
            new Beat(1.4, Start: r => { r.Dizzy = true; r.Eyes = EyeLook.Closed; }),
            new Beat(0.6, Start: r => { r.Dizzy = false; r.Eyes = null; r.ArmL = r.ArmR = null; },
                Tick: (r, t) => { foreach (var b in blocks) b.Opacity = 1 - t; }),
        };
    }

    // ---- Cat: stalks a laser dot and pounces ---------------------------------------------

    static Beat[] Cat(StoryRun run)
    {
        Prop dot = null!;
        int side = 1;
        double baseX = 0;
        var crouch = new Step(0) { Dip = 1 };
        var wiggle = new Step(0) { Dip = 1, Dx = -1 };
        return new[]
        {
            new Beat(0.2, Start: r =>
            {
                side = r.Side;
                r.Roam = true;
                baseX = r.CenterX + side * (r.Width / 2 + 40);
                dot = r.Add(Dot, Laser, baseX, r.Ground - 3, 2);
                dot.Draw = (dc, p) =>
                {
                    dc.DrawEllipse(LaserGlow, null, new Point(p.X + 1, p.Y + 1), 7, 4);
                    dc.DrawRectangle(Laser, null, new Rect(Math.Round(p.X), Math.Round(p.Y), 2, 2));
                };
            }),
            new Beat(1.6, Tick: (r, t) =>
            {
                dot.X = baseX + Math.Sin(t * 9) * 22 + Math.Sin(t * 23) * 6;
                dot.Y = r.Ground - 3 - Math.Abs(Math.Sin(t * 14)) * 10;
                r.Look = Math.Sign(dot.X - r.CenterX);
                r.Eyes = EyeLook.Wide;
            }),
            new Beat(1.2,
                Start: r =>
                {
                    dot.HomeX = dot.X;
                    dot.Y = r.Ground - 3;
                    r.Look = null;
                    r.Pose = crouch;
                    r.WalkSpeed = 12;
                    r.WalkTarget = side > 0 ? dot.X - r.Width - 6 : dot.X + 8;
                },
                Tick: (r, t) => dot.X = dot.HomeX + Math.Sin(t * 30) * 2),
            new Beat(0.6, Start: r => r.WalkTarget = null, Tick: (r, t) => r.Pose = (int)(t * 12) % 2 == 0 ? crouch : wiggle),
            new Beat(0.45, Start: r => { r.Pose = null; r.Leap(dot.X - r.Width / 2 + 1, 0, 0.45, 16); }),
            new Beat(0.1, Start: r =>
            {
                r.Remove(dot);
                r.Eyes = EyeLook.Happy;
                r.Fx('h', r.CenterX, r.Top, 0);
            }),
            new Beat(0.9),
        };
    }

    // ---- Dog: fetch -----------------------------------------------------------------------

    static Beat[] Dog(StoryRun run)
    {
        Prop ball = null!;
        int side = 1;
        double home = 0;
        return new[]
        {
            new Beat(0.4, Start: r =>
            {
                side = r.Side;
                home = r.X;
                ball = r.Add(Ball, Red, r.CenterX - 4 + side * r.Width * 0.35, r.Top + 2 * r.P, 2);
                if (side > 0) r.ArmR = 3;
                else r.ArmL = 3;
                r.Look = side;
            }),
            new Beat(0.1, Start: r =>
            {
                ball.VX = side * 150;
                ball.VY = -170;
                ball.Gravity = 420;
                ball.Bounce = 0.55;
                r.ArmL = r.ArmR = 1;
            }),
            new Beat(0.9, Start: r => r.Eyes = EyeLook.Wide),
            new Beat(1.3, Start: r =>
            {
                r.Eyes = null;
                r.ArmL = r.ArmR = null;
                r.Look = null;
                r.Roam = true;
                double away = side > 0 ? r.SceneWidth + 30 : -r.Width - 30;
                r.WalkSpeed = Math.Abs(away - r.X) / 1.1;
                r.WalkTarget = away;
            }),
            new Beat(0.5, Start: r =>
            {
                ball.VX = ball.VY = ball.Gravity = 0;
                ball.Bounce = -1;
                ball.X = -100;
            }),
            new Beat(1.8,
                Start: r =>
                {
                    r.WalkSpeed = Math.Abs(home - r.X) / 1.6;
                    r.WalkTarget = home;
                    r.Eyes = EyeLook.Happy;
                },
                Tick: (r, t) =>
                {
                    ball.X = r.CenterX - ball.Width / 2;
                    ball.Y = r.Top + 5 * r.P - 2;
                }),
            new Beat(0.3, Start: r =>
            {
                ball.VX = side * 25;
                ball.VY = -60;
                ball.Gravity = 420;
                ball.Bounce = 0.4;
            }),
            new Beat(0.45, Start: r => { r.Fx('h', r.CenterX, r.Top, 0); r.Hop(6, 0.3); }),
            new Beat(0.5, Start: r => r.Hop(6, 0.3)),
            new Beat(0.5, Tick: (r, t) => ball.Opacity = 1 - t),
        };
    }

    // ---- Mouse: nibbles a cheese wedge down to crumbs --------------------------------------

    static Beat[] Mouse(StoryRun run)
    {
        Prop cheese = null!;
        int side = 1, bites = 0;
        var beats = new List<Beat>
        {
            new Beat(0.7, Start: r =>
            {
                side = r.Side;
                r.Roam = true;
                cheese = r.Add(Cheese, CheeseInks, r.CenterX + side * (r.Width / 2 + 22) - 8, -12, 2);
                cheese.Gravity = 420;
                cheese.Bounce = 0.3;
                r.Eyes = EyeLook.Wide;
                r.Look = side;
            }),
            new Beat(0.8, Start: r =>
            {
                r.Eyes = null;
                r.Look = null;
                r.WalkSpeed = 30;
                r.WalkTarget = side > 0 ? cheese.X - r.Width + 6 : cheese.X + cheese.Width - 6;
            }),
        };
        for (int i = 0; i < 4; i++)
        {
            beats.Add(new Beat(0.28, Start: r =>
            {
                // A bite off the side facing him
                r.Pose = r.SignaturePose();
                bites++;
                int keep = Cheese[0].Length - bites * 2;
                r.Fx('c', side > 0 ? cheese.X : cheese.X + cheese.Width, cheese.Y, 0);
                if (keep <= 0) { cheese.Opacity = 0; return; }
                cheese.Shape = Cheese.Select(row => side > 0 ? row[^keep..] : row[..keep]).ToArray();
                if (side > 0) cheese.X += 2 * cheese.Px;
            }));
            beats.Add(new Beat(0.22, Start: r => r.Pose = null));
        }
        beats.Add(new Beat(0.9, Start: r =>
        {
            r.Pose = null;
            r.Eyes = EyeLook.Happy;
            r.Hop(6, 0.35);
            r.Fx('h', r.CenterX, r.Top, 0);
        }));
        return beats.ToArray();
    }

    // ---- Flower: a rain cloud, a growth spurt, a bloom, and a bee drops by -------------------

    static Beat[] Flower(StoryRun run)
    {
        Prop cloud = null!, bee = null!;
        int side = 1;
        double cx = 0;
        bool hearted = false;
        string[] Tall(string[] body, int n) => body[..^1].Concat(Enumerable.Repeat(body[^2], n)).Append(body[^1]).ToArray();
        return new[]
        {
            new Beat(0.9,
                Start: r =>
                {
                    side = r.Side;
                    cx = r.CenterX;
                    cloud = r.Add(CloudShape, CloudInk, cx + side * 140, Math.Max(2, r.Top - 30), 3);
                    cloud.FromX = cloud.X;
                    cloud.HomeX = cx - cloud.Width / 2;
                },
                Tick: (r, t) => cloud.X = Lerp(cloud.FromX, cloud.HomeX, Ease(t))),
            new Beat(1.6, Tick: (r, t) =>
            {
                if (r.Random() < r.Dt * 22)
                {
                    var d = r.Add(Drop, RainInk, cloud.X + 3 + r.Random() * (cloud.Width - 6), cloud.Y + cloud.Height, 2);
                    d.VY = 130;
                    d.Life = Math.Max(0.05, (r.Top + 6 - d.Y) / 130);
                }
                r.Eyes = t > 0.3 ? EyeLook.Happy : null;
            }),
            new Beat(0.35, Start: r => r.Pose = new Step(0) { Body = Tall(r.Mascot.Body, 1), EyeDy = -1 }),
            new Beat(0.35, Start: r => r.Pose = new Step(0) { Body = Tall(r.Mascot.Body, 2), EyeDy = -2 }),
            new Beat(0.5, Start: r =>
            {
                var open = r.SignaturePose(1)?.Body ?? r.Mascot.Body;
                r.Pose = new Step(0) { Body = Tall(open, 2), EyeDy = -2 };
                r.Eyes = EyeLook.Happy;
                r.Fx('p', r.CenterX, r.Top - 2 * r.P, 0);
                cloud.VX = side * 80;
            }),
            new Beat(2.0,
                Start: r =>
                {
                    bee = r.Add(BeeUp, BeeInks, cx - side * 120, Math.Max(2, r.Top - 30), 2);
                    bee.FromX = bee.X;
                    bee.FromY = bee.Y;
                },
                Tick: (r, t) =>
                {
                    double hx = cx - bee.Width / 2, hy = r.Top - 2 * r.P - bee.Height - 1;
                    if (t < 0.35)
                    {
                        double e = Ease(t / 0.35);
                        bee.X = Lerp(bee.FromX, hx, e);
                        bee.Y = Lerp(bee.FromY, hy, e) + Math.Sin(t * 40) * 2;
                    }
                    else if (t < 0.7)
                    {
                        bee.X = hx + Math.Sin(t * 30);
                        bee.Y = hy + Math.Sin(t * 50);
                        if (!hearted && t > 0.5) { hearted = true; r.Fx('h', cx, hy - 2, 0); }
                    }
                    else
                    {
                        double e = Ease((t - 0.7) / 0.3);
                        bee.X = Lerp(hx, cx + side * 140, e);
                        bee.Y = Lerp(hy, Math.Max(2, r.Top - 50), e);
                    }
                    bee.Shape = (int)(r.Time * 20) % 2 == 0 ? BeeUp : BeeDown;
                }),
            new Beat(0.6, Start: r => { r.Pose = null; r.Eyes = null; }),
        };
    }

    // ---- Chicken: lays an egg that hatches into a chick that follows her --------------------

    static Beat[] Chicken(StoryRun run)
    {
        Prop egg = null!, chick = null!;
        int side = 1;
        double home = 0, eggX = 0;
        void Follow(StoryRun r)
        {
            bool walkingRight = (r.WalkTarget ?? r.X) > r.X + 0.5;
            double target = walkingRight ? r.X - chick.Width - 3 : r.X + r.Width + 3;
            chick.X += (target - chick.X) * Math.Min(1, r.Dt * 3);
            if (chick.VY == 0 && Math.Abs(target - chick.X) > 3 && r.Random() < r.Dt * 5) chick.VY = -50;
        }
        return new[]
        {
            new Beat(0.6, Start: r =>
            {
                side = r.Side;
                home = r.X;
                r.Roam = true;
                r.Pose = new Step(0) { Dip = 1 };
                r.Eyes = EyeLook.Closed;
            }),
            new Beat(0.3, Start: r =>
            {
                eggX = r.CenterX - 4;
                egg = r.Add(Egg, EggInks, eggX, r.Ground - 8, 2);
                egg.Behind = true;
                r.Pose = null;
                r.Eyes = EyeLook.Wide;
                r.Hop(7, 0.3);
            }),
            new Beat(0.7, Start: r =>
            {
                r.Eyes = null;
                r.WalkSpeed = 40;
                r.WalkTarget = r.X - side * r.Width * 0.75;
            }),
            new Beat(1.3,
                Start: r => { r.Look = side; r.Eyes = EyeLook.Wide; },
                Tick: (r, t) =>
                {
                    egg.X = eggX + Math.Round(Math.Sin(t * 50) * (0.5 + t));
                    if (t > 0.55) egg.Shape = EggCracked;
                }),
            new Beat(0.15, Start: r =>
            {
                egg.Opacity = 0;
                for (int i = 0; i < 2; i++)
                {
                    var shell = r.Add(Shell, EggInks, eggX + i * 4, r.Ground - 8, 2);
                    shell.VX = (i == 0 ? -1 : 1) * 40;
                    shell.VY = -90;
                    shell.Gravity = 420;
                    shell.Bounce = 0.3;
                    shell.Life = 1.6;
                }
                chick = r.Add(Chick, ChickInks, eggX, r.Ground - 8, 2);
                chick.VY = -70;
                chick.Gravity = 420;
                chick.Bounce = 0;
                r.Fx('s', eggX + 4, r.Ground - 12, 0);
                r.Eyes = EyeLook.Happy;
            }),
            new Beat(0.6),
            new Beat(1.8,
                Start: r =>
                {
                    r.Look = null;
                    r.Eyes = null;
                    r.WalkSpeed = 30;
                    r.WalkTarget = home + side * 20;
                },
                Tick: (r, t) => Follow(r)),
            new Beat(1.2, Start: r => { r.WalkTarget = home; r.Eyes = EyeLook.Happy; }, Tick: (r, t) => Follow(r)),
            new Beat(0.6, Tick: (r, t) => { Follow(r); chick.Opacity = 1 - t; }),
        };
    }

    // ---- Goose: wanders off, comes back honking with a stolen bell ----------------------

    static Beat[] Goose(StoryRun run)
    {
        Prop bell = null!;
        int side = 1, lastHonk = -1;
        double home = 0;
        return new[]
        {
            new Beat(0.4, Start: r =>
            {
                side = r.Side;
                home = r.X;
                r.Eyes = EyeLook.Wide;
                r.Fx('q', r.CenterX, r.Top + 2 * r.P, 0);
            }),
            new Beat(1.3, Start: r =>
            {
                r.Eyes = null;
                r.Roam = true;
                double away = side > 0 ? r.SceneWidth + 30 : -r.Width - 30;
                r.WalkSpeed = Math.Abs(away - r.X) / 1.2;
                r.WalkTarget = away;
            }),
            new Beat(0.7, Start: r => bell = r.Add(Bell, BellInks, -100, 0, 2)),
            new Beat(1.8,
                Start: r =>
                {
                    r.WalkSpeed = Math.Abs(home - r.X) / 1.7;
                    r.WalkTarget = home;
                    r.Eyes = EyeLook.Happy;
                },
                Tick: (r, t) =>
                {
                    bell.X = r.CenterX - bell.Width / 2;
                    bell.Y = r.Top + 3 * r.P;
                    int honk = (int)(t * 4);
                    if (honk != lastHonk) { lastHonk = honk; r.Fx('q', r.CenterX, r.Top + r.P, 0); }
                }),
            new Beat(0.3, Start: r =>
            {
                bell.VX = side * 30;
                bell.VY = -80;
                bell.Gravity = 420;
                bell.Bounce = 0.4;
                r.Fx('n', r.CenterX, r.Top, 0);
            }),
            new Beat(1.0, Start: r => { r.Hop(7, 0.35); r.Fx('s', bell.X + 4, bell.Y, 0); }),
            new Beat(0.5, Tick: (r, t) => bell.Opacity = 1 - t),
        };
    }

    // ---- Duck: a puddle, a swim, and three ducklings ---------------------------------------

    static Beat[] Duck(StoryRun run)
    {
        Prop puddle = null!;
        var ducklings = new List<Prop>();
        int side = 1;
        double home = 0, pool = 0;
        return new[]
        {
            new Beat(0.6,
                Start: r =>
                {
                    side = r.Side;
                    home = r.X;
                    r.Roam = true;
                    pool = r.CenterX + side * (r.Width / 2 + 34);
                    puddle = r.Add(Puddle, PuddleInks, pool - 24, r.Ground - 6, 2);
                    puddle.Behind = true;
                    puddle.Opacity = 0;
                },
                Tick: (r, t) => puddle.Opacity = t),
            new Beat(0.55, Start: r => r.Leap(pool - r.Width / 2, 0, 0.55, 14)),
            new Beat(0.25, Start: r =>
            {
                r.Pose = new Step(0) { Dip = 2 };
                r.Fx('w', pool, r.Ground - 6, 0);
                r.Fx('w', pool, r.Ground - 6, 0);
                r.Eyes = EyeLook.Happy;
            }),
            new Beat(2.4,
                Start: r =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        double from = side > 0 ? home - 14 - i * 12 : home + r.Width + 6 + i * 12;
                        var d = r.Add(Duckling, ChickInks, from, r.Ground - 6, 2);
                        d.FromX = from;
                        d.HomeX = pool - side * (30 + i * 10) - 4;
                        ducklings.Add(d);
                    }
                },
                Tick: (r, t) =>
                {
                    r.X = pool - r.Width / 2 + Math.Sin(t * 2 * Math.PI) * 6;
                    if (r.Random() < r.Dt * 5)
                    {
                        var ripple = r.Add(Dot, PuddleInks('O')!, pool + (r.Random() - 0.5) * 30, r.Ground - 5, 2);
                        ripple.VX = (r.Random() < 0.5 ? -1 : 1) * 25;
                        ripple.Life = 0.5;
                    }
                    for (int i = 0; i < ducklings.Count; i++)
                    {
                        var d = ducklings[i];
                        d.X = Lerp(d.FromX, d.HomeX, Ease(Math.Clamp(t * 1.4 - i * 0.1, 0, 1)));
                        d.Y = r.Ground - d.Height - Math.Abs(Math.Sin(r.Time * 14 + i)) * 2;
                    }
                }),
            new Beat(0.5, Start: r => { r.Pose = null; r.Leap(home, 0, 0.5, 12); }),
            new Beat(0.8, Start: r =>
            {
                r.Fx('h', r.CenterX, r.Top, 0);
                foreach (var d in ducklings)
                {
                    d.VY = -60;
                    d.Gravity = 420;
                    d.Bounce = 0;
                }
            }),
            new Beat(0.6, Tick: (r, t) =>
            {
                puddle.Opacity = 1 - t;
                foreach (var d in ducklings) d.Opacity = 1 - t;
            }),
        };
    }

    // ---- Bee: pollen from a flower, honey into the hive ---------------------------------------

    static Beat[] Bee(StoryRun run)
    {
        Prop flower = null!, hive = null!;
        int side = 1;
        double home = 0;
        bool dropped = false;
        void Flap(StoryRun r) => r.ArmL = r.ArmR = (int)(r.Time * 18) % 2 == 0 ? 2 : 0;
        void FlyTo(StoryRun r, double x, double seconds)
        {
            r.WalkSpeed = Math.Max(10, Math.Abs(x - r.X) / seconds);
            r.WalkTarget = x;
        }
        return new[]
        {
            new Beat(0.5,
                Start: r =>
                {
                    side = r.Side;
                    home = r.X;
                    r.Roam = true;
                    flower = r.Add(SmallFlower, SmallFlowerInks, r.CenterX + side * (r.Width / 2 + 24) - 5, r.Ground - 10, 2);
                    hive = r.Add(Hive, HiveInks, r.CenterX + side * (r.Width / 2 + 70) - 6, r.Ground - 12, 2);
                    flower.Opacity = hive.Opacity = 0;
                },
                Tick: (r, t) => flower.Opacity = hive.Opacity = t),
            new Beat(1.0, Start: r => { r.RiseTarget = 18; FlyTo(r, flower.X + flower.Width / 2 - r.Width / 2, 0.9); }, Tick: (r, t) => Flap(r)),
            new Beat(1.1, Tick: (r, t) =>
            {
                Flap(r);
                r.Eyes = EyeLook.Happy;
                if (r.Random() < r.Dt * 8)
                {
                    var p = r.Add(Dot, Pollen, flower.X + 2 + r.Random() * 6, flower.Y, 2);
                    p.VY = -30;
                    p.Life = 0.5;
                }
            }),
            new Beat(1.0, Start: r => { r.Eyes = null; FlyTo(r, hive.X + hive.Width / 2 - r.Width / 2, 0.9); }, Tick: (r, t) => Flap(r)),
            new Beat(0.6,
                Start: r =>
                {
                    var drop = r.Add(Drop, Honey, hive.X + hive.Width / 2 - 1, r.Top + r.Height - 4, 2);
                    drop.VY = 20;
                    drop.Gravity = 300;
                    drop.Life = 0.35;
                },
                Tick: (r, t) =>
                {
                    Flap(r);
                    if (!dropped && t > 0.6) { dropped = true; r.Fx('s', hive.X + hive.Width / 2, hive.Y, 0); r.Eyes = EyeLook.Happy; }
                }),
            new Beat(1.0, Start: r => { r.RiseTarget = 0; FlyTo(r, home, 0.9); }, Tick: (r, t) => Flap(r)),
            new Beat(0.6,
                Start: r => { r.ArmL = r.ArmR = null; r.Eyes = null; },
                Tick: (r, t) => flower.Opacity = hive.Opacity = 1 - t),
        };
    }

    // ---- Pigeon: flies off and comes back with a note about your sessions ----------------------

    static Beat[] Pigeon(StoryRun run)
    {
        Prop scroll = null!, note = null!;
        int side = 1;
        double home = 0;
        return new[]
        {
            new Beat(0.4, Start: r =>
            {
                side = r.Side;
                home = r.X;
                r.Look = side;
                r.Eyes = EyeLook.Wide;
            }),
            new Beat(1.1, Start: r =>
            {
                r.Look = null;
                r.Eyes = null;
                r.Roam = true;
                r.RiseRate = 3;
                r.RiseTarget = r.Ground * 0.35;
                double away = side > 0 ? r.SceneWidth + 30 : -r.Width - 30;
                r.WalkSpeed = Math.Abs(away - r.X) / 1.0;
                r.WalkTarget = away;
            }),
            new Beat(0.6, Start: r => scroll = r.Add(Scroll, ScrollInks, -100, 0, 2)),
            new Beat(1.6,
                Start: r =>
                {
                    r.WalkSpeed = Math.Abs(home - r.X) / 1.5;
                    r.WalkTarget = home;
                },
                Tick: (r, t) =>
                {
                    scroll.X = r.CenterX - scroll.Width / 2;
                    scroll.Y = r.Top + r.Height - 2;
                }),
            new Beat(0.5, Start: r =>
            {
                scroll.Gravity = 300;
                scroll.Bounce = 0.2;
                r.RiseTarget = 0;
            }),
            new Beat(2.6, Start: r =>
            {
                scroll.Opacity = 0;
                note = r.Slip(r.Note, Paper, Dark, r.CenterX, Math.Max(2, r.Top - 20));
                r.Eyes = EyeLook.Happy;
                r.Fx('n', r.At(11), r.Top - 2, 0);
            }),
            new Beat(0.5, Tick: (r, t) => note.Opacity = 1 - t),
        };
    }

    // ---- Goat: rocks rise, he climbs to the top and bleats ---------------------------------

    static Beat[] Goat(StoryRun run)
    {
        var rocks = new List<Prop>();
        int side = 1;
        double home = 0, center = 0, spacing = 20;
        bool bleated = false;
        double ColumnX(StoryRun r, int i) => center + side * (r.Width / 2 + 4 + i * spacing) - 9;
        var beats = new List<Beat>
        {
            new Beat(0.8,
                Start: r =>
                {
                    side = r.Side;
                    home = r.X;
                    center = r.CenterX;
                    r.Roam = true;
                    double room = side > 0 ? r.SceneWidth - (r.X + r.Width) : r.X - r.MinX;
                    spacing = Math.Clamp((room - 26) / 2.2, 10, 20);
                    for (int i = 0; i < 3; i++)
                        for (int k = 0; k <= i; k++)
                        {
                            var rock = r.Add(Rock, RockInks, ColumnX(r, i), 0, 3);
                            rock.HomeY = r.Ground - (k + 1) * 9;
                            rock.FromY = rock.HomeY + (i + 1) * 9 + 2;
                            rock.Y = rock.FromY;
                            rocks.Add(rock);
                        }
                    r.Eyes = EyeLook.Wide;
                },
                Tick: (r, t) => { foreach (var rock in rocks) rock.Y = Lerp(rock.FromY, rock.HomeY, Ease(t)); }),
        };
        for (int i = 0; i < 3; i++)
        {
            int step = i;
            beats.Add(new Beat(0.45, Start: r => { r.Eyes = null; r.Leap(ColumnX(r, step) + 9 - r.Width / 2, (step + 1) * 9, 0.45, 10); }));
        }
        beats.Add(new Beat(1.3,
            Start: r =>
            {
                r.Pose = r.SignaturePose();
                r.Eyes = EyeLook.Happy;
                r.Fx('q', r.CenterX, r.Top + 6 * r.P, 0);
            },
            Tick: (r, t) =>
            {
                if (bleated || t < 0.5) return;
                bleated = true;
                r.Fx('q', r.CenterX, r.Top + 6 * r.P, 0);
            }));
        beats.Add(new Beat(0.6, Start: r => { r.Pose = null; r.Leap(home, 0, 0.6, 16); }));
        beats.Add(new Beat(0.7, Start: r => r.Eyes = null, Tick: (r, t) => { foreach (var rock in rocks) rock.Y = Lerp(rock.HomeY, rock.FromY, Ease(t)); }));
        return beats.ToArray();
    }

    // ---- Knight: a bug crawls in; sword fight; it bursts into 0s and 1s ----------------------

    static Beat[] Knight(StoryRun run)
    {
        Prop bug = null!;
        int side = 1;
        double Front(StoryRun r) => side > 0 ? r.X + r.Width : r.X;
        void Approach(StoryRun r, double speed)
        {
            if (bug.VY != 0 || bug.Y + bug.Height < r.Ground - 0.5) return; // still flying back
            double target = side > 0 ? Front(r) + 10 : Front(r) - 10 - bug.Width;
            double step = speed * r.Dt;
            bug.VX = 0;
            bug.X = Math.Abs(target - bug.X) > step ? bug.X + Math.Sign(target - bug.X) * step : target;
            bug.Shape = (int)(r.Time * 8) % 2 == 0 ? BugA : BugB;
        }
        Beat Slash(double seconds, bool last) => new(seconds, Start: r =>
        {
            r.Pose = r.SignaturePose();
            r.Fx('x', bug.X + bug.Width / 2, bug.Y - 2, side);
            if (last)
            {
                r.Fx('D', bug.X + bug.Width / 2, bug.Y + 4, side);
                bug.Opacity = 0;
                return;
            }
            bug.VX = side * 60;
            bug.VY = -110;
        });
        return new[]
        {
            new Beat(0.3, Start: r =>
            {
                side = r.Side;
                r.Look = side;
                bug = r.Add(BugA, BugInks, side > 0 ? r.SceneWidth + 4 : -18, r.Ground - 12, 2);
                bug.Gravity = 420;
                bug.Bounce = 0;
            }),
            new Beat(1.6, Start: r => r.Eyes = EyeLook.Wide, Tick: (r, t) => Approach(r, 70)),
            Slash(0.22, false),
            new Beat(0.55, Start: r => r.Pose = null, Tick: (r, t) => Approach(r, 60)),
            Slash(0.22, false),
            new Beat(0.55, Start: r => r.Pose = null, Tick: (r, t) => Approach(r, 60)),
            Slash(0.3, true),
            new Beat(0.9, Start: r =>
            {
                r.Pose = null;
                r.Eyes = EyeLook.Happy;
                r.Fx('s', r.At(11.5), r.Top - 4, 0);
            }),
            new Beat(0.1, Start: r => r.Look = null),
        };
    }

    // ---- Dragon: a pile of gold, a nap on top of it --------------------------------------------

    static Beat[] Dragon(StoryRun run)
    {
        var coins = new List<Prop>();
        int side = 1, lastZed = -1;
        double home = 0, pile = 0;
        // Twelve coins: five, four, then three on top
        (double Dx, int Row)[] slots =
        {
            (-16, 0), (-8, 0), (0, 0), (8, 0), (16, 0),
            (-12, 1), (-4, 1), (4, 1), (12, 1),
            (-8, 2), (0, 2), (8, 2),
        };
        return new[]
        {
            new Beat(0.3, Start: r =>
            {
                side = r.Side;
                home = r.X;
                r.Roam = true;
                r.Look = side;
                pile = r.CenterX + side * (r.Width / 2 + 24);
                r.Eyes = EyeLook.Wide;
            }),
            new Beat(2.0, Tick: (r, t) =>
            {
                int want = Math.Min(slots.Length, (int)(t * 12) + 1);
                while (coins.Count < want)
                {
                    var (dx, row) = slots[coins.Count];
                    var coin = r.Add(Coin, CoinInks, pile + dx - 4, -8, 2);
                    coin.HomeY = r.Ground - 6 - row * 5;
                    coin.Gravity = 500;
                    coins.Add(coin);
                }
                foreach (var coin in coins)
                    if (coin.Gravity > 0 && coin.Y >= coin.HomeY)
                    {
                        coin.Y = coin.HomeY;
                        coin.VY = coin.Gravity = 0;
                    }
                if (t > 0.5) r.Eyes = EyeLook.Happy;
            }),
            new Beat(0.6, Start: r => { r.Eyes = null; r.Leap(pile - r.Width / 2, 14, 0.6, 12); }),
            new Beat(0.4, Start: r => { r.Pose = new Step(0) { Dip = 1 }; r.Eyes = EyeLook.Closed; }),
            new Beat(2.6, Tick: (r, t) =>
            {
                r.Eyes = EyeLook.Closed;
                int zed = (int)(t * 3.25);
                if (zed != lastZed) { lastZed = zed; r.Zed(r.At(10), r.Top - 4); }
                if (r.Random() < r.Dt * 3) r.Smoke(r.At(13.5), r.Top + 2 * r.P, side * 6);
            }),
            new Beat(0.4, Start: r => { r.Pose = null; r.Eyes = EyeLook.Happy; r.Hop(6, 0.3); }),
            new Beat(0.6, Start: r => { r.Look = null; r.Eyes = null; r.Leap(home, 0, 0.6, 14); }),
            new Beat(0.6, Tick: (r, t) => { foreach (var coin in coins) coin.Opacity = 1 - t; }),
        };
    }

    // ---- Robot: scans the scene, prints a receipt: BUILD OK -------------------------------------

    static Beat[] Robot(StoryRun run)
    {
        Prop laser = null!, paper = null!, label = null!;
        int side = 1;
        double from = 0, span = 0, top = 0, ground = 0, chest = 0;
        return new[]
        {
            new Beat(0.3, Start: r =>
            {
                side = r.Side;
                r.Eyes = EyeLook.Wide;
                r.Fx('s', r.CenterX, r.Top - 2, 0);
            }),
            new Beat(2.0,
                Start: r =>
                {
                    from = side > 0 ? r.X + r.Width : r.X;
                    span = Math.Max(40, side > 0 ? r.SceneWidth - from - 6 : from - r.MinX - 6);
                    top = r.Top + 3.5 * r.P;
                    ground = r.Ground;
                    r.ArmL = r.ArmR = 2;
                    laser = r.Add(Dot, Laser, from, top, 1);
                    laser.Draw = (dc, p) =>
                    {
                        // Grid lines over what's been scanned, and the beam itself
                        double a = Math.Min(from, p.X), b = Math.Max(from, p.X);
                        for (double y = top; y < ground; y += 6) dc.DrawRectangle(GridLine, null, new Rect(Math.Round(a), Math.Round(y), Math.Round(b - a), 1));
                        dc.DrawRectangle(Beam, null, new Rect(Math.Round(p.X), Math.Round(top), 2, Math.Round(ground - top)));
                    };
                },
                Tick: (r, t) =>
                {
                    double sweep = 1 - Math.Abs(1 - 2 * (t * 2 % 1));
                    laser.X = from + side * span * sweep;
                    r.Look = side;
                }),
            new Beat(0.4, Start: r =>
            {
                r.Remove(laser);
                r.Look = null;
                r.Fx('q', r.CenterX, r.Top + 3 * r.P, 0);
                r.ArmL = r.ArmR = 1;
                r.Eyes = null;
            }),
            new Beat(1.4,
                Start: r =>
                {
                    chest = r.Top + 7 * r.P;
                    paper = r.Add(Dot, Paper, r.CenterX - 7, chest, 1);
                    paper.Draw = (dc, p) =>
                    {
                        // The receipt rolls up out of his chest, printed line by line
                        dc.DrawRectangle(Paper, null, new Rect(Math.Round(p.X), Math.Round(p.Y), 14, Math.Round(p.HomeY)));
                        for (double y = p.Y + 3; y < p.Y + p.HomeY - 2; y += 4)
                            dc.DrawRectangle(PaperLine, null, new Rect(Math.Round(p.X) + 2, Math.Round(y), 10, 1));
                    };
                },
                Tick: (r, t) =>
                {
                    paper.HomeY = 4 + t * 26;
                    paper.Y = chest - paper.HomeY;
                }),
            new Beat(1.4, Start: r =>
            {
                label = r.Slip("BUILD OK ✓", Paper, Green, r.CenterX, Math.Max(2, paper.Y - 16));
                r.Eyes = EyeLook.Happy;
                r.ArmL = r.ArmR = 3;
                r.Fx('s', r.CenterX, paper.Y, 0);
            }),
            new Beat(0.6,
                Start: r => r.ArmL = r.ArmR = null,
                Tick: (r, t) => paper.Opacity = label.Opacity = 1 - t),
        };
    }

    // ---- Helpers --------------------------------------------------------------

    static double Ease(double t) => t * t * (3 - 2 * t);
    static double Lerp(double a, double b, double t) => a + (b - a) * t;
    static Brush Rgb(uint c) => Ink.Of((byte)(c >> 16), (byte)(c >> 8), (byte)c);

    static Func<char, Brush?> Inks(params (char Letter, uint Rgb)[] inks)
    {
        var map = inks.ToDictionary(i => i.Letter, i => Rgb(i.Rgb));
        return c => map.GetValueOrDefault(c);
    }

    static Brush Glow(byte r, byte g, byte b)
    {
        var brush = new RadialGradientBrush(Color.FromArgb(0x70, r, g, b), Color.FromArgb(0, r, g, b));
        brush.Freeze();
        return brush;
    }

    static Brush Frozen(this SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
