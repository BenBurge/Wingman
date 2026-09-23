using Terminal.Gui.Drawing;
using Wingman.Core.Settings;
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
    /// <summary>The setting that follows the system: Daylight in light mode, Midnight otherwise.</summary>
    public const string AutoName = "Auto";

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

    public static Theme Nord { get; } = new(
        "Nord",
        Background: new Color(0x2E, 0x34, 0x40),
        Foreground: new Color(0xD8, 0xDE, 0xE9),
        Dim: new Color(0x4C, 0x56, 0x6A),
        Border: new Color(0x43, 0x4C, 0x5E),
        Accent: new Color(0xEB, 0xCB, 0x8B),
        Header: new Color(0x81, 0xA1, 0xC1),
        Ok: new Color(0xA3, 0xBE, 0x8C),
        Error: new Color(0xBF, 0x61, 0x6A),
        Info: new Color(0x88, 0xC0, 0xD0));

    public static Theme Dracula { get; } = new(
        "Dracula",
        Background: new Color(0x28, 0x2A, 0x36),
        Foreground: new Color(0xF8, 0xF8, 0xF2),
        Dim: new Color(0x62, 0x72, 0xA4),
        Border: new Color(0x44, 0x47, 0x5A),
        Accent: new Color(0xFF, 0xB8, 0x6C),
        Header: new Color(0xBD, 0x93, 0xF9),
        Ok: new Color(0x50, 0xFA, 0x7B),
        Error: new Color(0xFF, 0x55, 0x55),
        Info: new Color(0x8B, 0xE9, 0xFD));

    /// <summary>Every theme setting in the order the Settings tab offers them, <see cref="AutoName"/> last.</summary>
    public static IReadOnlyList<string> SettingNames { get; } =
        [Midnight.Name, Daylight.Name, Nord.Name, Dracula.Name, AutoName];

    /// <summary>
    /// The theme called <paramref name="name"/>, ignoring case, or Midnight when there is none.
    /// <see cref="AutoName"/> asks <paramref name="detector"/> and gives Daylight only when it
    /// reports light mode, so an unknown mode stays dark.
    /// </summary>
    public static Theme ByName(string? name, IThemeDetector detector)
    {
        if (string.Equals(name, AutoName, StringComparison.OrdinalIgnoreCase))
        {
            return detector.IsLightMode() == true ? Daylight : Midnight;
        }

        Theme[] themes = [Midnight, Daylight, Nord, Dracula];
        foreach (var theme in themes)
        {
            if (string.Equals(name, theme.Name, StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
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

    /// <summary>Typed text in the foreground color whether or not the field has focus, for form fields whose brackets show focus instead.</summary>
    public Scheme FieldScheme => Uniform(Foreground);

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
