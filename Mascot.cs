using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace ClaudeSessions;

public enum MascotKind { Dib, DibWhite, Clawd, Cat, Dog, Mouse, Flower, Chicken, Goose, Duck, Bee, Pigeon, Sheep, Knight, Goat, Dragon, Robot }

/// <summary>What a mascot can do in the runner game when its power meter is full.</summary>
public enum Power
{
    Phase,      // passes through obstacles for a while
    Burrow,     // digs under them for a while
    DoubleJump, // one extra jump in the air for a while
    Dash,       // a fast sprint nothing can stop
    Chomp,      // takes out the next obstacle it reaches
    SlowMo,     // the world slows down for a while
    Glide,      // falls slowly for a while
    Honk,       // scares away everything on screen
    Shield,     // soaks up one hit
    Fly,        // flies over everything for a while
    Fire,       // burns everything just ahead
}

/// <summary>
/// One step of a mascot's signature move, held for Seconds. Distances are in the mascot's cells.
/// </summary>
public sealed record Step(double Seconds)
{
    /// <summary>A body frame to show instead of the usual one; lined up at the feet, so it can be taller.</summary>
    public string[]? Body { get; init; }
    /// <summary>Moves the whole mascot right (left when he faces left) and up.</summary>
    public int Dx { get; init; }
    public int Lift { get; init; }
    /// <summary>Lowers the body over the legs, as in a peck.</summary>
    public int Dip { get; init; }
    /// <summary>Moves the eyes (and blush) with a frame whose head moved.</summary>
    public int EyeDx { get; init; }
    public int EyeDy { get; init; }
    public int? ArmL { get; init; }
    public int? ArmR { get; init; }
    public bool Happy { get; init; }
    /// <summary>An effect popped when the step starts: h heart, n note, s spark, q sound waves, c seeds, p petals, x slash, d a bit to slash, f fire.</summary>
    public char Fx { get; init; }
    /// <summary>Where the effect starts, from the top-left of the usual body.</summary>
    public (double Col, double Row) FxAt { get; init; }
}

/// <summary>
/// A pixel mascot, split into the parts the scene animates: a still body, one arm (the right
/// one is its mirror image), legs, and where the eyes and blush go. All in cells of P DIPs.
/// </summary>
public sealed class Mascot
{
    /// <summary>Every mascot in the order the tray menu and Settings list them.</summary>
    public static readonly (MascotKind Kind, string Label)[] Choices =
    {
        (MascotKind.Dib, "dib"),
        (MascotKind.DibWhite, "dib (white)"),
        (MascotKind.Clawd, "Clawd"),
        (MascotKind.Cat, "Cat"),
        (MascotKind.Dog, "Dog"),
        (MascotKind.Mouse, "Mouse"),
        (MascotKind.Flower, "Flower"),
        (MascotKind.Chicken, "Chicken"),
        (MascotKind.Goose, "Goose"),
        (MascotKind.Duck, "Duck"),
        (MascotKind.Bee, "Bee"),
        (MascotKind.Pigeon, "Pigeon"),
        (MascotKind.Goat, "Goat"),
        (MascotKind.Knight, "Knight"),
        (MascotKind.Dragon, "Dragon"),
        (MascotKind.Robot, "Robot"),
    };

    /// <summary>One pixel of this mascot in the scene, in DIPs.</summary>
    public required double P { get; init; }
    /// <summary>Palette letters (see Palette), . empty; as wide as the whole mascot, arms and legs left out.</summary>
    public required string[] Body { get; init; }
    /// <summary>The left arm; raised arms move it up a row at a time.</summary>
    public required string[] Arm { get; init; }
    public required int ArmRow { get; init; }
    /// <summary>Where the right arm goes when the body isn't symmetric (a tail on one side); else its mirror place.</summary>
    public int? RightArmAt { get; init; }
    /// <summary>Drawn in profile facing right; the scene mirrors it to walk or look left.</summary>
    public bool FacesRight { get; init; }
    public required int[] LegColumns { get; init; }
    /// <summary>The palette letter the legs are drawn in.</summary>
    public char LegInk { get; init; } = 'B';
    public required double[] EyeColumns { get; init; }
    public required double EyeRow { get; init; }
    /// <summary>How far the eyes move when he looks left or right, in cells.</summary>
    public required double LookStep { get; init; }
    public required double[] BlushColumns { get; init; }
    public required double BlushRow { get; init; }
    /// <summary>B is the main colour; A and C are extra ones (ears, nose, petals, stem).</summary>
    public required IReadOnlyDictionary<char, Color> Palette { get; init; }
    /// <summary>Standing still for icons, with E for the eyes; left out, it's put together from the parts.</summary>
    public string[]? IconPixels { get; init; }
    /// <summary>His own move, played when clicked and now and then when idle.</summary>
    public required Step[] Signature { get; init; }

    /// <summary>His power in the runner game, its name there, and the effect it shows (Chomp).</summary>
    public Power Power { get; init; } = Power.Shield;
    public string PowerName { get; init; } = "Shield";
    public char PowerFx { get; init; } = 's';

    /// <summary>Also plays it now and then while working, stopping his walk for it.</summary>
    public bool SignatureWhileWorking { get; init; }

    public double SignatureLength => Signature.Sum(m => m.Seconds);

    public const int LegRows = 2;

    public int Columns => Body[0].Length;
    public int Rows => Body.Length + LegRows;
    public double Width => Columns * P;
    public double Height => Rows * P;
    public Color Color => Palette['B'];
    public int RightArmColumn => RightArmAt ?? Columns - Arm[0].Length;
    public string[] RightArm => rightArm ??= Arm.Select(r => new string(r.Reverse().ToArray())).ToArray();
    string[]? rightArm;

    public string[] Icon => IconPixels ?? (icon ??= Compose());
    /// <summary>Standing still in the scene's own pixels, E for the eyes (stories break him into these).</summary>
    public string[] Standing => standing ??= Compose();
    string[]? standing;
    string[]? icon;

    /// <summary>Body, arms at rest, legs and eyes as one picture.</summary>
    string[] Compose()
    {
        var grid = Body.Select(r => r.ToCharArray()).Concat(Enumerable.Range(0, LegRows).Select(_ => new string('.', Columns).ToCharArray())).ToArray();
        void Put(string[] rows, int col, int row)
        {
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    if (rows[r][c] != '.') grid[row + r][col + c] = rows[r][c];
        }
        Put(Arm, 0, ArmRow);
        Put(RightArm, RightArmColumn, ArmRow);
        foreach (int c in LegColumns)
            for (int r = 0; r < LegRows; r++) grid[Body.Length + r][c] = LegInk;
        foreach (double c in EyeColumns)
            for (int r = 0; r < 2; r++) grid[(int)EyeRow + r][(int)Math.Round(c)] = 'E';
        return grid.Select(r => new string(r)).ToArray();
    }

    public static Mascot For(MascotKind kind) => kind switch
    {
        MascotKind.Clawd => Clawd,
        MascotKind.DibWhite => DibWhite,
        MascotKind.Cat => Cat,
        MascotKind.Dog => Dog,
        MascotKind.Mouse => Mouse,
        MascotKind.Flower => Flower,
        MascotKind.Chicken => Chicken,
        MascotKind.Goose => Goose,
        MascotKind.Duck => Duck,
        MascotKind.Bee => Bee,
        MascotKind.Pigeon => Pigeon,
        MascotKind.Sheep or MascotKind.Goat => Goat, // the goat took the sheep's place
        MascotKind.Knight => Knight,
        MascotKind.Dragon => Dragon,
        MascotKind.Robot => Robot,
        _ => Dib,
    };

    /// <summary>A copy of the body with columns c0..c1 (rows 0..lastRow) moved up n rows, for the i popping out of dib.</summary>
    static string[] Raise(string[] body, int c0, int c1, int lastRow, int n)
    {
        int w = body[0].Length;
        var grid = Enumerable.Range(0, n).Select(_ => new string('.', w).ToCharArray()).Concat(body.Select(r => r.ToCharArray())).ToArray();
        for (int r = 0; r <= lastRow; r++)
            for (int c = c0; c <= c1; c++)
            {
                grid[r][c] = grid[r + n][c];
                grid[r + n][c] = '.';
            }
        return grid.Select(r => new string(r)).ToArray();
    }

    static IReadOnlyDictionary<char, Color> Colors(params (char Letter, uint Rgb)[] inks) =>
        inks.ToDictionary(i => i.Letter, i => Color.FromRgb((byte)(i.Rgb >> 16), (byte)(i.Rgb >> 8), (byte)i.Rgb));

    // ---- Signature frames ----------------------------------------------------

    static readonly string[] CatTailMid =
    {
        "..B.......B..",
        "..BB.....BB..",
        "..BABBBBBAB.B",
        "..BBBBBBBBB.B",
        "..BBBBBBBBBBB",
        "..BBBBABBBB..",
        "..BBBBBBBBB..",
    };

    static readonly string[] CatTailLow =
    {
        "..B.......B..",
        "..BB.....BB..",
        "..BABBBBBAB..",
        "..BBBBBBBBB.B",
        "..BBBBBBBBBBB",
        "..BBBBABBBB..",
        "..BBBBBBBBB..",
    };

    static readonly string[] DogBark =
    {
        ".ABBBBBBBBBA.",
        ".ABBBBBBBBBA.",
        ".ABBBBBBBBBA.",
        "..BBBBBBBBB..",
        "..BBBBABBBB..",
        "..BBBAAABBB..",
        "..BBBBBBBBB..",
    };

    static readonly string[] MouseNibble =
    {
        ".BBB.....BBB...",
        ".BABBBBBBBAB...",
        ".BBBBBBBBBBB...",
        "..BBBBBBBBB....",
        "..BBBBBBBBB....",
        "..BBBBABBBBAAAA",
    };

    static readonly string[] FlowerBud =
    {
        ".............",
        "....AAAAA....",
        "...ABBBBBA...",
        "...ABBBBBA...",
        "...ABBBBBA...",
        "....AAAAA....",
        ".............",
        "......C......",
        ".....CCC.....",
    };

    static readonly string[] FlowerOpen =
    {
        "...A.A.A.A...",
        "..AAAAAAAAA..",
        ".AAABBBBBAAA.",
        "AAAABBBBBAAAA",
        ".AAABBBBBAAA.",
        "..AAAAAAAAA..",
        "...A.A.A.A...",
        "......C......",
        ".....CCC.....",
    };

    static readonly string[] GooseNeck =
    {
        "...BBBBB...",
        "...BBBBB...",
        "...BBCBB...",
        "....BBB....",
        "....BBB....",
        "..BBBBBBB..",
        ".BBBBBBBBB.",
        ".AABBBBBAA.",
        "..BBBBBBB..",
    };

    static readonly string[] GooseHonk =
    {
        "...BBBBB...",
        "...BBBBB...",
        "...BBCBB...",
        "....BCB....",
        "....BBB....",
        "....BBB....",
        "..BBBBBBB..",
        ".BBBBBBBBB.",
        ".AABBBBBAA.",
        "..BBBBBBB..",
    };

    static readonly string[] DuckQuack =
    {
        ".....B.....",
        "...BBBBB...",
        "..BBBBBBB..",
        ".BBBBBBBBB.",
        ".BBBBBBBBB.",
        ".BBCCCCCBB.",
        "..BBCCCBB..",
    };

    static readonly string[] PigeonBack =
    {
        ".......BBB...",
        "......BBBBB..",
        "......BBBBBD.",
        ".......AAAB..",
        "B.....BAAB...",
        "BBBBBBBBBB...",
        ".DDBBBBBBB...",
        "..BBBBBBB....",
    };

    static readonly string[] KnightSlash =
    {
        "....CC.......",
        "...CC........",
        "..BBBBB......",
        ".BBBBBBB.....",
        ".BBBBDDB.....",
        ".BBBBDDB.....",
        ".BBBBBBB.....",
        "..AAAAA.G....",
        ".BBBBBBBGWWWW",
        ".BBBBBBBG....",
        ".BBBBBBB.....",
    };

    static readonly string[] GoatBleat =
    {
        ".AA.......AA.",
        "..AA.....AA..",
        "...BBBBBBB...",
        "BBBBBBBBBBBBB",
        "...BBBBBBB...",
        "...BBBBBBB...",
        "....BBCBB....",
        "..BBBDCDBBB..",
        "..BBBBDBBBB..",
        "..BBBBBBBBB..",
    };

    static readonly string[] DragonRoar =
    {
        "........D.....",
        "........BBBBBB",
        "........BBB...",
        "..C.....BBBBBB",
        ".CCC...BBB....",
        "CCCCC.BBB.....",
        "..BBBBBBA.....",
        ".BBBBBBAA.....",
        "BBBBBBBA......",
        "B.............",
    };

    static readonly string[] RobotBlink =
    {
        "......A......",
        "......A......",
        "...BBBBBBB...",
        "...BDDDDDB...",
        "...BDDDDDB...",
        "...BBBBBBB...",
        "....AAAAA....",
        "..BBBBBBBBB..",
        "..BBCBDBCBB..",
        "..BBBBBBBBB..",
    };

    static readonly Mascot Clawd = new()
    {
        P = 4,
        Body = new[]
        {
            "..BBBBBBBBB..",
            "..BBBBBBBBB..",
            "..BBBBBBBBB..",
            "..BBBBBBBBB..",
            "..BBBBBBBBB..",
            "..BBBBBBBBB..",
        },
        Arm = new[] { "BB", "BB" },
        ArmRow = 3,
        LegColumns = new[] { 2, 4, 8, 10 },
        EyeColumns = new[] { 4.0, 8.0 },
        EyeRow = 1,
        LookStep = 1,
        BlushColumns = new[] { 2.5, 9.5 },
        BlushRow = 3,
        // Claw claps
        Signature = new[]
        {
            new Step(0.12) { ArmL = 3, ArmR = 3 },
            new Step(0.12) { ArmL = 1, ArmR = 1, Fx = 's', FxAt = (6.5, -1) },
            new Step(0.12) { ArmL = 3, ArmR = 3 },
            new Step(0.12) { ArmL = 1, ArmR = 1, Fx = 's', FxAt = (6.5, -1) },
            new Step(0.12) { ArmL = 3, ArmR = 3 },
            new Step(0.3) { ArmL = 2, ArmR = 2, Happy = true, Fx = 'h', FxAt = (6.5, -1) },
        },
        Power = Power.Burrow,
        PowerName = "Burrow",
        Palette = Colors(('B', 0xD97757)),
    };

    static readonly string[] DibBody =
    {
        "......BBBB.BBB.BBBB......",
        "......BBBB.BBB.BBBB......",
        "......BBBB.BBB.BBBB......",
        "......BBBB.BBB.BBBB......",
        "......BBBB.BBB.BBBB......",
        "......BBBB.BBB.BBBB......",
        "......BBBB.BBB.BBBB......",
        "......BBBB.BBB.BBBB......",
        "......BBBBBBBBBBBBB......",
    };

    // The dib logo with legs: the d and b bowls are its arms, the eyes peek out of the stems next to the i
    static readonly Mascot Dib = new()
    {
        P = 3,
        Body = DibBody,
        Arm = new[]
        {
            ".BBBBB",
            "BBBBB.",
            "BBBB..",
            "BBBB..",
            "BBBBBB",
        },
        ArmRow = 4,
        LegColumns = new[] { 6, 8, 16, 18 },
        EyeColumns = new[] { 8.0, 16.0 },
        EyeRow = 1,
        LookStep = 0.5,
        BlushColumns = new[] { 6.5, 17.5 },
        BlushRow = 3,
        // The i jumps out of the logo and drops back in
        Signature = new[]
        {
            new Step(0.1) { Body = Raise(DibBody, 11, 13, 7, 2) },
            new Step(0.1) { Body = Raise(DibBody, 11, 13, 7, 4), Fx = 's', FxAt = (12.5, -4) },
            new Step(0.3) { Body = Raise(DibBody, 11, 13, 7, 5), Happy = true },
            new Step(0.1) { Body = Raise(DibBody, 11, 13, 7, 3), Happy = true },
            new Step(0.08) { Body = Raise(DibBody, 11, 13, 7, 1) },
            new Step(0.15) { ArmL = 1, ArmR = 1 },
            new Step(0.12),
        },
        Power = Power.Phase,
        PowerName = "Glitch",
        Palette = Colors(('B', 0xD97757)),
        // The scene's 25 columns are too fine for a 16 px icon
        IconPixels = new[]
        {
            "....BBB.B.BBB....",
            "....BEB.B.BEB....",
            "....BEB.B.BEB....",
            ".BBBBBB.B.BBBBBB.",
            "BBB.BBB.B.BBB.BBB",
            "BB..BBB.B.BBB..BB",
            "BBBBBBBBBBBBBBBBB",
            "....B.B...B.B....",
            "....B.B...B.B....",
        },
    };

    static readonly Mascot DibWhite = new()
    {
        P = Dib.P,
        Body = Dib.Body,
        Arm = Dib.Arm,
        ArmRow = Dib.ArmRow,
        LegColumns = Dib.LegColumns,
        EyeColumns = Dib.EyeColumns,
        EyeRow = Dib.EyeRow,
        LookStep = Dib.LookStep,
        BlushColumns = Dib.BlushColumns,
        BlushRow = Dib.BlushRow,
        Signature = Dib.Signature,
        Power = Power.Phase,
        PowerName = "Glitch",
        Palette = Colors(('B', 0xF0EEE9)),
        IconPixels = Dib.IconPixels,
    };

    // Grey cat: pink inner ears and nose, tail up on the right
    static readonly Mascot Cat = new()
    {
        P = 4,
        Body = new[]
        {
            "..B.......B..",
            "..BB.....BB.B",
            "..BABBBBBAB.B",
            "..BBBBBBBBB.B",
            "..BBBBBBBBBBB",
            "..BBBBABBBB..",
            "..BBBBBBBBB..",
        },
        Arm = new[] { "BB", "BB" },
        ArmRow = 5,
        LegColumns = new[] { 2, 4, 8, 10 },
        EyeColumns = new[] { 4.0, 8.0 },
        EyeRow = 3,
        LookStep = 1,
        BlushColumns = new[] { 2.5, 9.5 },
        BlushRow = 5,
        // Tail swish, then a purr
        Signature = new[]
        {
            new Step(0.12) { Body = CatTailMid },
            new Step(0.15) { Body = CatTailLow },
            new Step(0.12) { Body = CatTailMid },
            new Step(0.15),
            new Step(0.12) { Body = CatTailMid },
            new Step(0.15) { Body = CatTailLow },
            new Step(0.12) { Body = CatTailMid },
            new Step(0.5) { Happy = true, Fx = 'h', FxAt = (6.5, -1) },
        },
        Power = Power.DoubleJump,
        PowerName = "Double jump",
        Palette = Colors(('B', 0xA9AEB8), ('A', 0xF0A0B0)),
    };

    // Tan dog: dark floppy ears and nose
    static readonly Mascot Dog = new()
    {
        P = 4,
        Body = new[]
        {
            "..BBBBBBBBB..",
            ".ABBBBBBBBBA.",
            ".ABBBBBBBBBA.",
            ".ABBBBBBBBBA.",
            "..BBBBABBBB..",
            "..BBBBBBBBB..",
            "..BBBBBBBBB..",
        },
        Arm = new[] { "BB", "BB" },
        ArmRow = 4,
        LegColumns = new[] { 2, 4, 8, 10 },
        EyeColumns = new[] { 4.0, 8.0 },
        EyeRow = 1,
        LookStep = 1,
        BlushColumns = new[] { 2.5, 9.5 },
        BlushRow = 3,
        // Ears up, woof woof
        Signature = new[]
        {
            new Step(0.22) { Body = DogBark, Fx = 'q', FxAt = (6.5, 3) },
            new Step(0.15),
            new Step(0.22) { Body = DogBark, Fx = 'q', FxAt = (6.5, 3) },
            new Step(0.35) { Happy = true },
        },
        Power = Power.Dash,
        PowerName = "Zoomies",
        Palette = Colors(('B', 0xC8955C), ('A', 0x7A5230)),
    };

    // Grey mouse: round ears, pink nose and a long tail to the right
    static readonly Mascot Mouse = new()
    {
        P = 4,
        Body = new[]
        {
            ".BBB.....BBB...",
            ".BABBBBBBBAB...",
            ".BBBBBBBBBBB...",
            "..BBBBBBBBB....",
            "..BBBBABBBB...A",
            "..BBBBBBBBBAAA.",
        },
        Arm = new[] { ".B" },
        ArmRow = 3,
        RightArmAt = 11,
        LegColumns = new[] { 3, 5, 7, 9 },
        EyeColumns = new[] { 4.0, 8.0 },
        EyeRow = 2,
        LookStep = 1,
        BlushColumns = new[] { 2.5, 9.5 },
        BlushRow = 4,
        // Nibbles crumbs, tail flicking
        Signature = new[]
        {
            new Step(0.1) { Body = MouseNibble, Fx = 'c', FxAt = (6.5, 6) },
            new Step(0.1),
            new Step(0.1) { Body = MouseNibble },
            new Step(0.1),
            new Step(0.1) { Body = MouseNibble, Fx = 'c', FxAt = (6.5, 6) },
            new Step(0.1),
            new Step(0.1) { Body = MouseNibble },
            new Step(0.35) { Happy = true },
        },
        Power = Power.Chomp,
        PowerName = "Nibble",
        PowerFx = 'c',
        Palette = Colors(('B', 0xBFC3CB), ('A', 0xF0A0B0)),
    };

    // Pink flower with a sunny face, leaves for arms and stem legs
    static readonly Mascot Flower = new()
    {
        P = 4,
        Body = new[]
        {
            "....AA.AA....",
            "...AAAAAAA...",
            "..AABBBBBAA..",
            ".AAABBBBBAAA.",
            "..AABBBBBAA..",
            "...AAAAAAA...",
            "....AA.AA....",
            "......C......",
            ".....CCC.....",
        },
        Arm = new[] { "...CC.", "....CC" },
        ArmRow = 6,
        LegColumns = new[] { 5, 7 },
        LegInk = 'C',
        EyeColumns = new[] { 5.0, 7.0 },
        EyeRow = 2,
        LookStep = 1,
        BlushColumns = new[] { 4.0, 8.0 },
        BlushRow = 4,
        // Closes to a bud, then blooms and sheds a few petals
        Signature = new[]
        {
            new Step(0.3) { Body = FlowerBud },
            new Step(0.12),
            new Step(0.5) { Body = FlowerOpen, Happy = true, Fx = 'p', FxAt = (6.5, 1) },
            new Step(0.3) { Body = FlowerOpen, Happy = true, Fx = 's', FxAt = (6.5, -1) },
            new Step(0.2),
        },
        Power = Power.SlowMo,
        PowerName = "Sunshine",
        Palette = Colors(('B', 0xF5D76E), ('A', 0xF28CA8), ('C', 0x6DBE6A)),
    };

    // Round white chicken: red comb and wattle, yellow beak and legs
    static readonly Mascot Chicken = new()
    {
        P = 4,
        Body = new[]
        {
            "....AAA....",
            "...BBBBB...",
            "..BBBBBBB..",
            ".BBBBBBBBB.",
            ".BBBBBBBBB.",
            ".BBBBCBBBB.",
            ".BBBBABBBB.",
            "..BBBBBBB..",
        },
        Arm = new[] { "." },
        ArmRow = 0,
        LegColumns = new[] { 4, 6 },
        LegInk = 'C',
        EyeColumns = new[] { 3.0, 7.0 },
        EyeRow = 3,
        LookStep = 1,
        BlushColumns = new[] { 1.5, 8.5 },
        BlushRow = 5,
        // Pecks at seeds
        Signature = new[]
        {
            new Step(0.08) { Dip = 1 },
            new Step(0.14) { Dip = 2, Fx = 'c', FxAt = (5.5, 9) },
            new Step(0.08) { Dip = 1 },
            new Step(0.15),
            new Step(0.08) { Dip = 1 },
            new Step(0.14) { Dip = 2, Fx = 'c', FxAt = (5.5, 9) },
            new Step(0.08) { Dip = 1 },
            new Step(0.3) { Happy = true },
        },
        Power = Power.Glide,
        PowerName = "Flutter",
        Palette = Colors(('B', 0xF2F0EA), ('A', 0xE05050), ('C', 0xF2A93B)),
    };

    // Round white goose: short neck, orange beak and feet, grey wing tips
    static readonly Mascot Goose = new()
    {
        P = 4,
        Body = new[]
        {
            "...BBBBB...",
            "...BBBBB...",
            "...BBCBB...",
            "....BBB....",
            "..BBBBBBB..",
            ".BBBBBBBBB.",
            ".AABBBBBAA.",
            "..BBBBBBB..",
        },
        Arm = new[] { "." },
        ArmRow = 0,
        LegColumns = new[] { 4, 6 },
        LegInk = 'C',
        EyeColumns = new[] { 4.0, 6.0 },
        EyeRow = 1,
        LookStep = 0.5,
        BlushColumns = new[] { 3.0, 7.0 },
        BlushRow = 2.5,
        // Neck up: HONK
        Signature = new[]
        {
            new Step(0.1) { Body = GooseNeck, EyeDy = -1 },
            new Step(0.35) { Body = GooseHonk, EyeDy = -2, Fx = 'q', FxAt = (5.5, 0) },
            new Step(0.2) { Body = GooseHonk, EyeDy = -2, Fx = 'q', FxAt = (5.5, 0) },
            new Step(0.1) { Body = GooseNeck, EyeDy = -1 },
            new Step(0.25),
        },
        Power = Power.Honk,
        PowerName = "HONK",
        Palette = Colors(('B', 0xEDEBE6), ('A', 0x9EA2AA), ('C', 0xF28C28)),
    };

    // Round yellow duckling: a tuft on top, wide orange bill
    static readonly Mascot Duck = new()
    {
        P = 4,
        Body = new[]
        {
            ".....B.....",
            "...BBBBB...",
            "..BBBBBBB..",
            ".BBBBBBBBB.",
            ".BBBBBBBBB.",
            ".BBBCCCBBB.",
            "..BBBBBBB..",
        },
        Arm = new[] { "." },
        ArmRow = 0,
        LegColumns = new[] { 4, 6 },
        LegInk = 'C',
        EyeColumns = new[] { 3.0, 7.0 },
        EyeRow = 3,
        LookStep = 1,
        BlushColumns = new[] { 1.5, 8.5 },
        BlushRow = 5,
        // Hop, quack, quack
        Signature = new[]
        {
            new Step(0.18) { Body = DuckQuack, Lift = 1, Fx = 'q', FxAt = (5.5, 5) },
            new Step(0.14),
            new Step(0.18) { Body = DuckQuack, Lift = 1, Fx = 'q', FxAt = (5.5, 5) },
            new Step(0.14),
            new Step(0.3) { Body = DuckQuack, Happy = true, Fx = 'q', FxAt = (5.5, 5) },
            new Step(0.15),
        },
        Power = Power.Shield,
        PowerName = "Bubble",
        Palette = Colors(('B', 0xF5D24A), ('C', 0xF28C28)),
    };

    // Round bumblebee: dark stripes and antennae, see-through wings for arms
    static readonly Mascot Bee = new()
    {
        P = 4,
        Body = new[]
        {
            "...A.....A...",
            "....A...A....",
            "...BBBBBBB...",
            "..BBBBBBBBB..",
            "..BBBBBBBBB..",
            "..AAAAAAAAA..",
            "..BBBBBBBBB..",
            "...AAAAAAA...",
        },
        Arm = new[] { "CC", "CC" },
        ArmRow = 2,
        LegColumns = new[] { 5, 7 },
        LegInk = 'A',
        EyeColumns = new[] { 4.0, 8.0 },
        EyeRow = 3,
        LookStep = 1,
        BlushColumns = new[] { 2.5, 9.5 },
        BlushRow = 4.5,
        // Buzzes a little loop, wings going
        Signature = new[]
        {
            new Step(0.1) { Dx = 1, Lift = 2, ArmL = 2, ArmR = 2 },
            new Step(0.1) { Dx = 2, Lift = 4, ArmL = 0, ArmR = 0 },
            new Step(0.1) { Dx = 2, Lift = 6, ArmL = 2, ArmR = 2 },
            new Step(0.12) { Dx = 0, Lift = 7, ArmL = 0, ArmR = 0, Happy = true, Fx = 's', FxAt = (6.5, -1) },
            new Step(0.1) { Dx = -2, Lift = 6, ArmL = 2, ArmR = 2 },
            new Step(0.1) { Dx = -2, Lift = 4, ArmL = 0, ArmR = 0 },
            new Step(0.1) { Dx = -1, Lift = 2, ArmL = 2, ArmR = 2 },
            new Step(0.12),
        },
        Power = Power.Fly,
        PowerName = "Buzz",
        Palette = Colors(('B', 0xF5C842), ('A', 0x5B4826), ('C', 0xDDEBFA)),
    };

    // City pigeon in profile: blue-grey, green neck sheen, dark beak and wing bars, pink feet
    static readonly Mascot Pigeon = new()
    {
        P = 4,
        FacesRight = true,
        Body = new[]
        {
            "........BBB..",
            ".......BBBBB.",
            ".......BBBBBD",
            ".......AAAB..",
            "B.....BAAB...",
            "BBBBBBBBBB...",
            ".DDBBBBBBB...",
            "..BBBBBBB....",
        },
        Arm = new[] { "." },
        ArmRow = 0,
        LegColumns = new[] { 5, 7 },
        LegInk = 'C',
        EyeColumns = new[] { 10.0 },
        EyeRow = 1,
        LookStep = 0,
        BlushColumns = new[] { 8.0 },
        BlushRow = 2.5,
        // Head bob, then a coo
        Signature = new[]
        {
            new Step(0.14) { Body = PigeonBack, EyeDx = -1 },
            new Step(0.14),
            new Step(0.14) { Body = PigeonBack, EyeDx = -1 },
            new Step(0.14),
            new Step(0.14) { Body = PigeonBack, EyeDx = -1 },
            new Step(0.45) { Happy = true, Fx = 'n', FxAt = (11, -1) },
        },
        Power = Power.Fly,
        PowerName = "Fly",
        Palette = Colors(('B', 0x9AA3B4), ('A', 0x6FB59A), ('C', 0xE58A8A), ('D', 0x4A4E58)),
    };

    // Knight in profile: steel armour, red plume, face in the helm, sword raised in front
    static readonly Mascot Knight = new()
    {
        P = 4,
        FacesRight = true,
        Body = new[]
        {
            "....CC.....W.",
            "...CC......W.",
            "..BBBBB....W.",
            ".BBBBBBB...W.",
            ".BBBBDDB...W.",
            ".BBBBDDB...W.",
            ".BBBBBBB...W.",
            "..AAAAA...GGG",
            ".BBBBBBBBBBG.",
            ".BBBBBBB.....",
            ".BBBBBBB.....",
        },
        Arm = new[] { "." },
        ArmRow = 0,
        LegColumns = new[] { 3, 5 },
        LegInk = 'A',
        EyeColumns = new[] { 6.0 },
        EyeRow = 4,
        LookStep = 0,
        BlushColumns = new[] { 5.0 },
        BlushRow = 5.5,
        // Cuts down the 0s and 1s flying at him from where he's heading
        Signature = new[]
        {
            new Step(0.55) { Fx = 'd', FxAt = (13, 7.5) },
            new Step(0.1) { Body = KnightSlash, Fx = 'x', FxAt = (12, 6) },
            new Step(0.3) { Body = KnightSlash },
            new Step(0.45) { Fx = 'd', FxAt = (13, 7.5) },
            new Step(0.1) { Body = KnightSlash, Fx = 'x', FxAt = (12, 6) },
            new Step(0.25) { Body = KnightSlash },
            new Step(0.3) { Happy = true, Fx = 's', FxAt = (11.5, -1) },
        },
        SignatureWhileWorking = true,
        Power = Power.Chomp,
        PowerName = "Slash",
        PowerFx = 'x',
        Palette = Colors(('B', 0xB8BEC8), ('A', 0x6E7681), ('C', 0xD84A4A), ('D', 0xE8C4A0), ('G', 0xE0B84A), ('W', 0xE4E8EE)),
    };

    // White goat: brown horns, ears out to the sides, a beard, dark hooves
    static readonly Mascot Goat = new()
    {
        P = 4,
        Body = new[]
        {
            ".AA.......AA.",
            "..AA.....AA..",
            "...BBBBBBB...",
            "BBBBBBBBBBBBB",
            "...BBBBBBB...",
            "...BBBBBBB...",
            "....BBCBB....",
            "..BBBDDDBBB..",
            "..BBBBDBBBB..",
            "..BBBBBBBBB..",
        },
        Arm = new[] { "." },
        ArmRow = 0,
        LegColumns = new[] { 3, 5, 7, 9 },
        LegInk = 'C',
        EyeColumns = new[] { 4.0, 8.0 },
        EyeRow = 3,
        LookStep = 1,
        BlushColumns = new[] { 3.5, 8.5 },
        BlushRow = 5,
        // Bleating hops
        Signature = new[]
        {
            new Step(0.1) { Dip = 1 },
            new Step(0.15) { Body = GoatBleat, Lift = 2, Fx = 'q', FxAt = (6.5, 6) },
            new Step(0.12) { Body = GoatBleat, Lift = 1 },
            new Step(0.1) { Dip = 1 },
            new Step(0.18) { Body = GoatBleat, Lift = 3, Fx = 'q', FxAt = (6.5, 6) },
            new Step(0.12) { Lift = 1 },
            new Step(0.3) { Happy = true },
        },
        Power = Power.Chomp,
        PowerName = "Ram",
        PowerFx = 's',
        Palette = Colors(('B', 0xEDE8DE), ('A', 0xB89A6E), ('C', 0x5A5048), ('D', 0xD6CDBB)),
    };

    // Green dragon in profile: cream horn, dark wing on its back, yellow belly, tail behind
    static readonly Mascot Dragon = new()
    {
        P = 4,
        FacesRight = true,
        Body = new[]
        {
            "........D.....",
            "........BBBB..",
            "........BBBBBB",
            "..C.....BBBBB.",
            ".CCC...BBB....",
            "CCCCC.BBB.....",
            "..BBBBBBA.....",
            ".BBBBBBAA.....",
            "BBBBBBBA......",
            "B.............",
        },
        Arm = new[] { "." },
        ArmRow = 0,
        LegColumns = new[] { 3, 6 },
        EyeColumns = new[] { 10.0 },
        EyeRow = 2,
        LookStep = 0,
        BlushColumns = new[] { 9.0 },
        BlushRow = 3.5,
        // Rears back, then breathes fire
        Signature = new[]
        {
            new Step(0.3) { Dx = -1 },
            new Step(0.12) { Body = DragonRoar, Fx = 'f', FxAt = (14, 2) },
            new Step(0.12) { Body = DragonRoar, Fx = 'f', FxAt = (14, 2) },
            new Step(0.12) { Body = DragonRoar, Fx = 'f', FxAt = (14, 2) },
            new Step(0.12) { Body = DragonRoar, Fx = 'f', FxAt = (14, 2) },
            new Step(0.35) { Happy = true },
        },
        Power = Power.Fire,
        PowerName = "Fire breath",
        Palette = Colors(('B', 0x5FB85A), ('A', 0xF2D06B), ('C', 0x3E8A45), ('D', 0xEDE3C8)),
    };

    // Little robot: red antenna light, cyan visor, chest lights, grey limbs
    static readonly Mascot Robot = new()
    {
        P = 4,
        Body = new[]
        {
            "......C......",
            "......A......",
            "...BBBBBBB...",
            "...BDDDDDB...",
            "...BDDDDDB...",
            "...BBBBBBB...",
            "....AAAAA....",
            "..BBBBBBBBB..",
            "..BBDBCBDBB..",
            "..BBBBBBBBB..",
        },
        Arm = new[] { "AA", "BB" },
        ArmRow = 7,
        LegColumns = new[] { 4, 8 },
        LegInk = 'A',
        EyeColumns = new[] { 5.0, 7.0 },
        EyeRow = 3,
        LookStep = 1,
        BlushColumns = new[] { 4.0, 8.0 },
        BlushRow = 5,
        // Blinks and computes, then arms up: beep beep
        Signature = new[]
        {
            new Step(0.14) { Body = RobotBlink, ArmL = 1 },
            new Step(0.14) { ArmR = 1, Fx = 's', FxAt = (6.5, -1) },
            new Step(0.14) { Body = RobotBlink, ArmL = 1 },
            new Step(0.14) { ArmR = 1, Fx = 's', FxAt = (6.5, -1) },
            new Step(0.4) { ArmL = 3, ArmR = 3, Happy = true, Fx = 'q', FxAt = (6.5, 3) },
            new Step(0.15),
        },
        Power = Power.Shield,
        PowerName = "Force field",
        Palette = Colors(('B', 0x9AABBD), ('A', 0x4A5562), ('C', 0xE05050), ('D', 0x6FD3E8)),
    };
}
