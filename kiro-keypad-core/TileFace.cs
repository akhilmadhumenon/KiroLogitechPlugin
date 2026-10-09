namespace KiroKeypad;

public enum Glyph
{
    Dot,
    Check,
    Cross,
    Square,
    List,
    Play,
    Pause,
    Spark,
    Chat,
    Terminal,
    Diff,
    Alert,
    Undo,
    Gauge,
    Layers,
    Book,
}

/// <summary>
/// One LCD key: deep-purple field, icon in the top 70%, caption in the bottom 30%.
/// </summary>
public sealed class TileFace
{
    public required Rgb Field { get; init; }
    public required Rgb Ink { get; init; }
    public required Rgb IconInk { get; init; }
    public required String Caption { get; init; }
    public required Glyph Glyph { get; init; }
    public Boolean TopBar { get; init; }
    public Boolean BottomBand { get; init; }
    public Rgb? Pip { get; init; }

    public const String FontName = "Brown Logitech Pan Light";

    public static TileFace Slot(SlotPhase phase, String? title, Boolean pinned, Boolean pulseOn)
    {
        var caption = phase switch
        {
            SlotPhase.Empty => "Empty",
            SlotPhase.Done => Captions.Fit(title, "Done"),
            SlotPhase.Error => "Error",
            SlotPhase.Stopping => "Stop",
            SlotPhase.NeedsYou => Captions.Fit(title, "Needs"),
            _ => Captions.Fit(title, "Kiro"),
        };

        var iconInk = phase == SlotPhase.Tool && pulseOn ? Rgb.Accent : Rgb.Ink;
        return phase switch
        {
            SlotPhase.Empty => new TileFace
            {
                Field = Rgb.Surface,
                Ink = Rgb.InkMuted,
                IconInk = Rgb.InkMuted,
                Caption = caption,
                Glyph = Glyph.Dot,
            },
            SlotPhase.NeedsYou => new TileFace
            {
                Field = Rgb.Lift,
                Ink = Rgb.Ink,
                IconInk = Rgb.Ink,
                Caption = caption,
                Glyph = Glyph.Alert,
                BottomBand = true,
            },
            SlotPhase.Done => new TileFace
            {
                Field = Rgb.Deep,
                Ink = Rgb.Ink,
                IconInk = Rgb.Ink,
                Caption = caption,
                Glyph = Glyph.Check,
                TopBar = pinned,
                Pip = Rgb.Success,
            },
            SlotPhase.Error or SlotPhase.Stopping => new TileFace
            {
                Field = Rgb.Deep,
                Ink = Rgb.Ink,
                IconInk = Rgb.Ink,
                Caption = caption,
                Glyph = phase == SlotPhase.Stopping ? Glyph.Square : Glyph.Cross,
                Pip = Rgb.Danger,
            },
            SlotPhase.Running or SlotPhase.Tool => new TileFace
            {
                Field = Rgb.Deep,
                Ink = Rgb.Ink,
                IconInk = iconInk,
                Caption = caption,
                Glyph = Glyph.Spark,
                TopBar = true,
            },
            _ => new TileFace
            {
                Field = Rgb.Deep,
                Ink = Rgb.Ink,
                IconInk = Rgb.Ink,
                Caption = caption,
                Glyph = Glyph.Dot,
                TopBar = pinned,
            },
        };
    }

    public static TileFace Action(String caption, Glyph glyph, Boolean needsYou = false, Boolean dim = false)
    {
        if (dim)
        {
            return new TileFace
            {
                Field = Rgb.Surface,
                Ink = Rgb.InkMuted,
                IconInk = Rgb.InkMuted,
                Caption = Captions.Fit(caption, "Kiro"),
                Glyph = glyph,
            };
        }

        if (needsYou)
        {
            return new TileFace
            {
                Field = Rgb.Lift,
                Ink = Rgb.Ink,
                IconInk = Rgb.Ink,
                Caption = Captions.Fit(caption, "Allow"),
                Glyph = glyph,
                BottomBand = true,
            };
        }

        return new TileFace
        {
            Field = Rgb.Deep,
            Ink = Rgb.Ink,
            IconInk = Rgb.Ink,
            Caption = Captions.Fit(caption, "Kiro"),
            Glyph = glyph,
        };
    }

    public static IReadOnlyList<TileFace> Samples() =>
    [
        Slot(SlotPhase.Empty, null, pinned: false, pulseOn: false),
        Slot(SlotPhase.Running, "Fix login", pinned: true, pulseOn: false),
        Slot(SlotPhase.NeedsYou, "Fix login", pinned: true, pulseOn: false),
        Slot(SlotPhase.Done, "Fix login", pinned: false, pulseOn: false),
        Slot(SlotPhase.Error, "Fix login", pinned: false, pulseOn: false),
    ];

    public IReadOnlyList<String> Problems()
    {
        var problems = new List<String>();
        if (!Rgb.IsField(this.Field))
        {
            problems.Add($"field {this.Field.Hex} is outside the Kiro purple set");
        }

        if (!Rgb.IsInk(this.Ink) || !Rgb.IsInk(this.IconInk))
        {
            problems.Add("ink left the Kiro ink set");
        }

        if (this.Pip is Rgb pip && !Rgb.IsPip(pip))
        {
            problems.Add($"pip {pip.Hex} is not a status pip");
        }

        if (String.IsNullOrWhiteSpace(this.Caption))
        {
            problems.Add("caption is missing");
        }
        else if (this.Caption.Length > Captions.MaxLength)
        {
            problems.Add($"caption '{this.Caption}' is longer than {Captions.MaxLength}");
        }

        return problems;
    }
}
