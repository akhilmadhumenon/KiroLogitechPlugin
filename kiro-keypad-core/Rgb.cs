namespace KiroKeypad;

/// <summary>Kiro.dev palette used on the keypad. Fields stay inside this set.</summary>
public readonly record struct Rgb(Byte R, Byte G, Byte B)
{
    public static readonly Rgb Surface = new(0x19, 0x16, 0x1D);
    public static readonly Rgb Deep = new(0x4C, 0x1D, 0x95);
    public static readonly Rgb Lift = new(0x6D, 0x28, 0xD9);
    public static readonly Rgb Ink = new(0xFA, 0xFA, 0xFA);
    public static readonly Rgb InkMuted = new(0xA1, 0xA1, 0xAA);
    public static readonly Rgb Accent = new(0xC6, 0xA0, 0xFF);
    public static readonly Rgb Brand = new(0x90, 0x46, 0xFF);
    public static readonly Rgb Success = new(0x3D, 0xDC, 0x97);
    public static readonly Rgb Danger = new(0xF8, 0x71, 0x71);

    public String Hex => $"#{this.R:X2}{this.G:X2}{this.B:X2}";

    public static Boolean IsField(Rgb color) =>
        color == Surface || color == Deep || color == Lift;

    public static Boolean IsInk(Rgb color) =>
        color == Ink || color == InkMuted || color == Accent;

    public static Boolean IsPip(Rgb color) =>
        color == Success || color == Danger || color == Accent;
}
