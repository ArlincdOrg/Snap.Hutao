﻿// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.
// Some part of this file came from:
// https://github.com/xunkong/desktop/tree/main/src/Desktop/Desktop/Pages/CharacterInfoPage.xaml.cs

using System.Buffers.Binary;
using Windows.UI;

namespace Snap.Hutao.Control.Media;

[HighQuality]
internal struct Rgba32
{
    public byte R;
    public byte G;
    public byte B;
    public byte A;

    public Rgba32(string hex)
        : this(hex.Length == 6 ? Convert.ToUInt32($"{hex}FF", 16) : Convert.ToUInt32(hex, 16))
    {
    }

    public unsafe Rgba32(uint xrgbaCode)
    {
        // uint layout: 0xRRGGBBAA is AABBGGRR
        // AABBGGRR -> RRGGBBAA
        fixed (Rgba32* pSelf = &this)
        {
            *(uint*)pSelf = BinaryPrimitives.ReverseEndianness(xrgbaCode);
        }
    }

    private Rgba32(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static unsafe implicit operator Color(Rgba32 hexColor)
    {
        // Goal : Rgba32:RRGGBBAA(0xAABBGGRR) -> Color: AARRGGBB(0xBBGGRRAA)
        // Step1: Rgba32:RRGGBBAA(0xAABBGGRR) -> UInt32:AA000000(0x000000AA)
        uint a = ((*(uint*)&hexColor) >> 24) & 0x000000FF;

        // Step2: Rgba32:RRGGBBAA(0xAABBGGRR) -> UInt32:00RRGGBB(0xRRGGBB00)
        uint rgb = ((*(uint*)&hexColor) << 8) & 0xFFFFFF00;

        // Step2: UInt32:00RRGGBB(0xRRGGBB00) + UInt32:AA000000(0x000000AA) -> UInt32:AARRGGBB(0xRRGGBBAA)
        uint rgba = rgb + a;

        return *(Color*)&rgba;
    }

    public static Rgba32 FromHsl(Hsla32 hsl)
    {
        double chroma = (1 - Math.Abs((2 * hsl.L) - 1)) * hsl.S;
        double h1 = hsl.H / 60;
        double x = chroma * (1 - Math.Abs((h1 % 2) - 1));
        double m = hsl.L - (0.5 * chroma);
        double r1, g1, b1;

        if (h1 < 1)
        {
            r1 = chroma;
            g1 = x;
            b1 = 0;
        }
        else if (h1 < 2)
        {
            r1 = x;
            g1 = chroma;
            b1 = 0;
        }
        else if (h1 < 3)
        {
            r1 = 0;
            g1 = chroma;
            b1 = x;
        }
        else if (h1 < 4)
        {
            r1 = 0;
            g1 = x;
            b1 = chroma;
        }
        else if (h1 < 5)
        {
            r1 = x;
            g1 = 0;
            b1 = chroma;
        }
        else
        {
            r1 = chroma;
            g1 = 0;
            b1 = x;
        }

        byte r = (byte)(255 * (r1 + m));
        byte g = (byte)(255 * (g1 + m));
        byte b = (byte)(255 * (b1 + m));
        byte a = (byte)(255 * hsl.A);

        return new(r, g, b, a);
    }

    public readonly Hsla32 ToHsl()
    {
        const double toDouble = 1.0 / 255;
        double r = toDouble * R;
        double g = toDouble * G;
        double b = toDouble * B;
        double max = Math.Max(Math.Max(r, g), b);
        double min = Math.Min(Math.Min(r, g), b);
        double chroma = max - min;
        double h1;

        if (chroma == 0)
        {
            h1 = 0;
        }
        else if (max == r)
        {
            // The % operator doesn't do proper modulo on negative
            // numbers, so we'll add 6 before using it
            h1 = (((g - b) / chroma) + 6) % 6;
        }
        else if (max == g)
        {
            h1 = 2 + ((b - r) / chroma);
        }
        else
        {
            h1 = 4 + ((r - g) / chroma);
        }

        double lightness = 0.5 * (max + min);
        double saturation = chroma == 0 ? 0 : chroma / (1 - Math.Abs((2 * lightness) - 1));

        Hsla32 ret;
        ret.H = 60 * h1;
        ret.S = saturation;
        ret.L = lightness;
        ret.A = toDouble * A;
        return ret;
    }
}