using System;
using System.Collections.Generic;

namespace Traffic.Art
{
    /// <summary>
    /// Letters made of dots, five across and seven down, for painting words on the sides of
    /// trucks. Capitals, digits and a little punctuation; small letters are drawn as capitals.
    /// </summary>
    public static class DotLetters
    {
        public const int Wide = 5, High = 7;

        private static readonly Dictionary<char, string> glyphs = new Dictionary<char, string>
        {
            { 'A', "01110100011000111111100011000110001" }, { 'B', "11110100011000111110100011000111110" },
            { 'C', "01110100011000010000100001000101110" }, { 'D', "11110100011000110001100011000111110" },
            { 'E', "11111100001000011110100001000011111" }, { 'F', "11111100001000011110100001000010000" },
            { 'G', "01110100011000010111100011000101111" }, { 'H', "10001100011000111111100011000110001" },
            { 'I', "01110001000010000100001000010001110" }, { 'J', "00111000100001000010000101001001100" },
            { 'K', "10001100101010011000101001001010001" }, { 'L', "10000100001000010000100001000011111" },
            { 'M', "10001110111010110101100011000110001" }, { 'N', "10001110011010110011100011000110001" },
            { 'O', "01110100011000110001100011000101110" }, { 'P', "11110100011000111110100001000010000" },
            { 'Q', "01110100011000110001101011001001101" }, { 'R', "11110100011000111110101001001010001" },
            { 'S', "01111100001000001110000010000111110" }, { 'T', "11111001000010000100001000010000100" },
            { 'U', "10001100011000110001100011000101110" }, { 'V', "10001100011000110001100010101000100" },
            { 'W', "10001100011000110101101011101110001" }, { 'X', "10001100010101000100010101000110001" },
            { 'Y', "10001100010101000100001000010000100" }, { 'Z', "11111000010001000100010001000011111" },
            { '0', "01110100011001110101110011000101110" }, { '1', "00100011000010000100001000010001110" },
            { '2', "01110100010000100010001000100011111" }, { '3', "11110000010000101110000010000111110" },
            { '4', "00010001100101010010111110001000010" }, { '5', "11111100001111000001000011000101110" },
            { '6', "00110010001000011110100011000101110" }, { '7', "11111000010001000100010000100001000" },
            { '8', "01110100011000101110100011000101110" }, { '9', "01110100011000101111000010001001100" },
            { '&', "01100100101010001000101011001001101" }, { '.', "00000000000000000000000000110001100" },
            { ',', "00000000000000000000011000010001000" }, { '\'', "00100001000100000000000000000000000" },
            { '-', "00000000000000011111000000000000000" }, { '!', "00100001000010000100001000000000100" },
            { '?', "01110100010000100010001000000000100" },
        };

        /// <summary>How wide some words come out, in dots, at so many dots to a dot of the letters. Letters are a dot apart.</summary>
        public static int Width(string text, int scale)
        {
            return text.Length == 0 ? 0 : (text.Length * (Wide + 1) - 1) * scale;
        }

        /// <summary>
        /// Paint words into a picture held as rows of colors, the top row first. The words
        /// start at (x, y), their top left corner.
        /// </summary>
        public static void Paint(uint[] pixels, int width, int height, int x, int y, string text, int scale, uint color)
        {
            foreach (char raw in text)
            {
                string glyph;
                if (glyphs.TryGetValue(char.ToUpperInvariant(raw), out glyph))
                {
                    for (int row = 0; row < High; row++)
                    {
                        for (int col = 0; col < Wide; col++)
                        {
                            if (glyph[row * Wide + col] != '1') continue;
                            for (int dy = 0; dy < scale; dy++)
                            {
                                for (int dx = 0; dx < scale; dx++)
                                {
                                    int px = x + col * scale + dx, py = y + row * scale + dy;
                                    if (px >= 0 && px < width && py >= 0 && py < height) pixels[py * width + px] = color;
                                }
                            }
                        }
                    }
                }
                x += (Wide + 1) * scale;
            }
        }
    }

    /// <summary>
    /// The name a made-up company paints on its trucks: the name, a few words under it, and
    /// its colors. Every name here is invented, most of them a joke about traffic.
    /// </summary>
    public sealed class Livery
    {
        public string Name, Slogan;
        /// <summary>The panel's color, the color of the words, and the color of the stripe along the bottom, as 0xRRGGBB.</summary>
        public int Back, Ink, Stripe;
        /// <summary>A company that hauls fuel: its name goes on tankers, the others' on boxes.</summary>
        public bool Fuel;

        /// <summary>The picture is this many dots across and down: four times as wide as it is high, like the side of a trailer.</summary>
        public const int Width = 512, Height = 128;

        public static readonly Livery[] All =
        {
            new Livery { Name = "GRIDLOCK LOGISTICS", Slogan = "WE'LL GET THERE", Back = 0xf2f2ee, Ink = 0x1d3557, Stripe = 0xe63946 },
            new Livery { Name = "HONK & HAUL", Slogan = "MOVING SINCE THE LIGHT TURNED GREEN", Back = 0x1d3557, Ink = 0xf2f2ee, Stripe = 0xffd23f },
            new Livery { Name = "MERGE RIGHT MOVERS", Slogan = "WE FIT ANYWHERE", Back = 0xffd23f, Ink = 0x22252a, Stripe = 0x22252a },
            new Livery { Name = "YIELD FARMS", Slogan = "FRESH PRODUCE, EVENTUALLY", Back = 0x2a9d5c, Ink = 0xf7f7f2, Stripe = 0xf4e285 },
            new Livery { Name = "DEAD END DAIRY", Slogan = "UDDERLY ON TIME", Back = 0x8ecae6, Ink = 0x023047, Stripe = 0xf7f7f2 },
            new Livery { Name = "LEFT LANE LUMBER", Slogan = "IN NO HURRY", Back = 0x8d5a2b, Ink = 0xfff3d6, Stripe = 0x3b2412 },
            new Livery { Name = "FOUR-WAY FREIGHT", Slogan = "YOU GO. NO, YOU GO.", Back = 0xf77f00, Ink = 0xfffaf0, Stripe = 0x432818 },
            new Livery { Name = "ROUNDABOUT PARCEL", Slogan = "ANY EXIT NOW", Back = 0x6a4c93, Ink = 0xf7f7f2, Stripe = 0xffca3a },
            new Livery { Name = "AMBER LIGHT OIL", Slogan = "WE SPEED UP", Back = 0xdfe3e8, Ink = 0xc1121f, Stripe = 0xf4a300, Fuel = true },
            new Livery { Name = "LAST EXIT GAS", Slogan = "FUEL FOR THE LONG WAY ROUND", Back = 0xf2f2ee, Ink = 0x0b6e4f, Stripe = 0x0b6e4f, Fuel = true },
            new Livery { Name = "RUSH HOUR PETROLEUM", Slogan = "NEVER IN ONE", Back = 0x22252a, Ink = 0xffd23f, Stripe = 0xffd23f, Fuel = true },
        };

        /// <summary>The companies that haul fuel, or the ones that do not.</summary>
        public static List<Livery> Of(bool fuel)
        {
            var some = new List<Livery>();
            foreach (Livery livery in All) if (livery.Fuel == fuel) some.Add(livery);
            return some;
        }

        /// <summary>
        /// The side of the truck as a picture: rows of colors, the top row first, each 0xAARRGGBB.
        /// The name is as large as will fit, with the slogan smaller beneath it and a stripe along the bottom.
        /// </summary>
        public uint[] Paint()
        {
            var pixels = new uint[Width * Height];
            uint back = 0xFF000000u | (uint)Back, ink = 0xFF000000u | (uint)Ink, stripe = 0xFF000000u | (uint)Stripe;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = i / Width >= Height - 14 ? stripe : back;
            // The largest whole size at which the name fits, with a margin; the slogan at up to half that.
            int big = 1;
            while (big < 8 && DotLetters.Width(Name, big + 1) <= Width - 40) big++;
            int small = 1;
            while (small < Math.Max(1, big / 2) && small < 3 && DotLetters.Width(Slogan, small + 1) <= Width - 40) small++;
            int nameHigh = DotLetters.High * big, sloganHigh = DotLetters.High * small, gap = 5 * small + 4;
            int top = (Height - 14 - nameHigh - gap - sloganHigh) / 2;
            DotLetters.Paint(pixels, Width, Height, (Width - DotLetters.Width(Name, big)) / 2, top, Name, big, ink);
            // The name once more, a touch to the right, which thickens its upright strokes so that it reads from further off.
            if (big >= 3) DotLetters.Paint(pixels, Width, Height, (Width - DotLetters.Width(Name, big)) / 2 + big / 3, top, Name, big, ink);
            DotLetters.Paint(pixels, Width, Height, (Width - DotLetters.Width(Slogan, small)) / 2, top + nameHigh + gap, Slogan, small, ink);
            return pixels;
        }
    }
}
