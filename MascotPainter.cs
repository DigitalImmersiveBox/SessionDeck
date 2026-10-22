using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace ClaudeSessions;

public enum EyeLook { Open, Closed, Wide, Happy }

/// <summary>Draws a pixel mascot at a spot; the banner and widget scene and the runner game share it.</summary>
static class MascotPainter
{
    static readonly Brush EyeInk = Ink.Of(0x16, 0x16, 0x18);
    static readonly Brush BlushInk = Ink.Of(0xF0, 0xA0, 0x8C);
    static readonly ConditionalWeakTable<Mascot, Dictionary<char, Brush>> inks = new();

    /// <summary>The mascot's palette as frozen brushes, made once per mascot.</summary>
    public static Dictionary<char, Brush> InksOf(Mascot m) =>
        inks.GetValue(m, _ => m.Palette.ToDictionary(p => p.Key, p => (Brush)Ink.Of(p.Value)));

    /// <param name="left">Left edge of the mascot's box; top is the top of his usual body, legs below it.</param>
    /// <param name="step">A signature step: its body frame, dip and eye shift (the caller moves him for Dx and Lift).</param>
    /// <param name="armL">Arms: 0 resting, up to 3 raised over his head.</param>
    /// <param name="stepDir">Which way the stepping foot moves on leg frame 1, as drawn (+1 right).</param>
    /// <param name="breath">Pushes the body down a little without the legs, for sleeping.</param>
    public static void Draw(DrawingContext dc, Mascot mascot, double left, double top, Step? step = null,
                            int armL = 0, int armR = 0, int legFrame = 0, int stepDir = 1, bool sitting = false,
                            double breath = 0, EyeLook eyes = EyeLook.Open, int look = 0)
    {
        double P = mascot.P;
        var palette = InksOf(mascot);
        Func<char, Brush?> ink = c => palette.GetValueOrDefault(c);

        // Legs first, so a body dipping over them (a peck) hides their tops
        if (!sitting)
        {
            var legInk = palette[mascot.LegInk];
            double legTop = top + mascot.Body.Length * P;
            for (int i = 0; i < mascot.LegColumns.Length; i++)
            {
                int col = mascot.LegColumns[i];
                dc.DrawRectangle(legInk, null, new Rect(left + col * P, legTop, P, P));
                // Scuttle: every other foot steps forward on alternate frames
                int foot = legFrame == 1 && i % 2 == 0 ? col + stepDir : col;
                dc.DrawRectangle(legInk, null, new Rect(left + foot * P, legTop + P, P, P));
            }
        }

        // A signature frame can be taller than the body, standing on the same feet
        double bodyTop = top + breath + (step?.Dip ?? 0) * P;
        var body = step?.Body ?? mascot.Body;
        PixelScene.Sprite(dc, body, left, bodyTop - (body.Length - mascot.Body.Length) * P, P, ink);
        PixelScene.Sprite(dc, mascot.Arm, left, bodyTop + (mascot.ArmRow - armL) * P, P, ink);
        PixelScene.Sprite(dc, mascot.RightArm, left + mascot.RightArmColumn * P, bodyTop + (mascot.ArmRow - armR) * P, P, ink);

        double faceX = left + (step?.EyeDx ?? 0) * P, faceTop = bodyTop + (step?.EyeDy ?? 0) * P;
        double eyeTop = faceTop + mascot.EyeRow * P;
        foreach (double col in mascot.EyeColumns)
        {
            double ex = faceX + (col + look * mascot.LookStep) * P;
            switch (eyes)
            {
                case EyeLook.Happy:
                    // ^ ^
                    dc.DrawRectangle(EyeInk, null, new Rect(ex - P / 2, eyeTop + P / 2, P / 2, P / 2));
                    dc.DrawRectangle(EyeInk, null, new Rect(ex, eyeTop, P, P / 2));
                    dc.DrawRectangle(EyeInk, null, new Rect(ex + P, eyeTop + P / 2, P / 2, P / 2));
                    break;
                case EyeLook.Closed:
                    dc.DrawRectangle(EyeInk, null, new Rect(ex, eyeTop + 1.5 * P, P, P / 2));
                    break;
                case EyeLook.Wide:
                    dc.DrawRectangle(EyeInk, null, new Rect(ex, eyeTop - P / 2, P, 2.5 * P));
                    break;
                default:
                    dc.DrawRectangle(EyeInk, null, new Rect(ex, eyeTop, P, 2 * P));
                    break;
            }
        }
        if (eyes == EyeLook.Happy)
        {
            foreach (double col in mascot.BlushColumns)
                dc.DrawRectangle(BlushInk, null, new Rect(faceX + col * P, faceTop + mascot.BlushRow * P, P, P / 2));
        }
    }
}
