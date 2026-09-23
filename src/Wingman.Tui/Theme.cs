using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>
/// A named palette and the Terminal.Gui schemes derived from it. Every view gets its colors from
/// one of these schemes, so switching themes never leaves a view on Terminal.Gui's defaults.
/// </summary>
public sealed record Theme(
    string Name,
    Color Background,
    Color Foreground,
    Color Dim,
    Color Border,
    Color Accent,
    Color Header,
    Color Ok,
    Color Error,
    Color Info)
{
    public static Theme Midnight { get; } = new(
        "Midnight",
        Background: new Color(0x17, 0x1B, 0x26),
        Foreground: new Color(0xD8, 0xDE, 0xE9),
        Dim: new Color(0x6C, 0x75, 0x90),
        Border: new Color(0x3A, 0x42, 0x58),
        Accent: new Color(0xF0, 0xB5, 0x4A),
        Header: new Color(0x8F, 0xA3, 0xC7),
        Ok: new Color(0x7F, 0xC9, 0x8A),
        Error: new Color(0xE5, 0x70, 0x7A),
        Info: new Color(0x7C, 0xB4, 0xE8));

    public static Theme Daylight { get; } = new(
        "Daylight",
        Background: new Color(0xFA, 0xF8, 0xF2),
        Foreground: new Color(0x26, 0x2A, 0x35),
        Dim: new Color(0x8A, 0x8F, 0x9E),
        Border: new Color(0xC9, 0xCB, 0xD4),
        Accent: new Color(0xB9, 0x7A, 0x10),
        Header: new Color(0x4A, 0x5F, 0x8A),
        Ok: new Color(0x2E, 0x8B, 0x4E),
        Error: new Color(0xC0, 0x42, 0x4E),
        Info: new Color(0x2F, 0x6F, 0xB0));

    /// <summary>The theme called <paramref name="name"/>, ignoring case, or Midnight when there is none.</summary>
    public static Theme ByName(string? name)
    {
        if (string.Equals(name, Daylight.Name, StringComparison.OrdinalIgnoreCase))
        {
            return Daylight;
        }

        return Midnight;
    }

    /// <summary>
    /// Foreground text on the background, with the cursor row, active tab, and focused controls
    /// drawn background-on-accent. Unfocused selections use <c>Active</c>, which is also
    /// background-on-accent so the cursor row stays visible while the filter box has focus.
    /// </summary>
    public Scheme Normal => new(On(Foreground))
    {
        Focus = Selected,
        Active = Selected,
        Highlight = On(Foreground),
        HotNormal = new Attribute(Accent, Background, TextStyle.Bold),
        HotFocus = new Attribute(Background, Accent, TextStyle.Bold),
        HotActive = new Attribute(Background, Accent, TextStyle.Bold),
        Editable = On(Foreground),
        ReadOnly = On(Dim),
        Disabled = On(Dim),
    };

    public Scheme HeaderScheme => Uniform(Header);

    public Scheme DimScheme => Uniform(Dim);

    public Scheme BorderScheme => Uniform(Border);

    public Scheme ErrorScheme => Uniform(Error);

    public Scheme OkScheme => Uniform(Ok);

    /// <summary>Typed text in accent whether or not the field has focus, as in the mockups' filter box.</summary>
    public Scheme InputScheme => Uniform(Accent);

    /// <summary>Table cell text in <paramref name="foreground"/> that turns background-on-accent on the cursor row like the rest of the row.</summary>
    public Scheme CellScheme(Color foreground) => new(On(foreground))
    {
        Focus = Selected,
        Active = Selected,
        Disabled = On(Dim),
    };

    public Attribute Selected => new(Background, Accent);

    public Attribute SelectedBold => new(Background, Accent, TextStyle.Bold);

    public Attribute On(Color foreground, TextStyle style = TextStyle.None) => new(foreground, Background, style);

    private Scheme Uniform(Color foreground)
    {
        var attribute = On(foreground);
        return new Scheme(attribute)
        {
            Focus = attribute,
            Active = attribute,
            Highlight = attribute,
            HotNormal = attribute,
            HotFocus = attribute,
            HotActive = attribute,
            Editable = attribute,
            ReadOnly = attribute,
            Disabled = attribute,
        };
    }
}
