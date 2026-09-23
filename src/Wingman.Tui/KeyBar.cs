using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Winget;

namespace Wingman.Tui;

/// <summary>
/// A key the key bar shows and the shell dispatches: pressing <see cref="Key"/> runs <see cref="Action"/>.
/// <paramref name="KeyLabel"/> replaces the key's own name on the bar, such as <c>⏎</c> for Enter.
/// <paramref name="IsOnBar"/> false keeps the key working but leaves it off the bar, for a tab
/// whose keys would overflow 96 columns; the help overlay still lists it.
/// </summary>
internal sealed record KeyHint(Key Key, string Label, Action Action, string? KeyLabel = null, bool IsOnBar = true)
{
    public string KeyText => KeyLabel ?? Key.ToString();

    /// <summary>
    /// Printable keys match on the character alone, so <c>?</c> matches however the driver reports
    /// Shift; Ctrl and Alt chords never match a plain character hint.
    /// </summary>
    public bool Matches(Key pressed)
    {
        var hintIsPrintable = Key.TryGetPrintableRune(out var wanted);
        if (hintIsPrintable && pressed.TryGetPrintableRune(out var typed))
        {
            return typed == wanted && !pressed.IsCtrl && !pressed.IsAlt;
        }

        return pressed == Key;
    }
}

/// <summary>
/// The bottom row of key hints, <c> u Upgrade   x Uninstall   …</c>, keys in accent bold and labels
/// in the foreground color. Clicking a hint runs it.
/// </summary>
/// <remarks>
/// Terminal.Gui's <c>StatusBar</c> draws a separator and padding around every <c>Shortcut</c>,
/// which overflows 96 columns with the Installed tab's nine hints, so this view draws the
/// mockup's compact layout itself.
/// </remarks>
internal sealed class KeyBar : View, IThemedView
{
    private const string Gap = "   ";

    private Theme _theme;
    private IReadOnlyList<KeyHint> _hints = [];
    private KeyHint[] _shownHints = [];

    // Column span of each shown hint as last drawn, used to hit-test clicks.
    private (int Start, int End)[] _spans = [];

    public KeyBar(Theme theme)
    {
        _theme = theme;
        Height = 1;
        CanFocus = false;
    }

    /// <summary>Every key the shell dispatches for the active tab, including those not drawn on the bar.</summary>
    public IReadOnlyList<KeyHint> Hints
    {
        get => _hints;
        set
        {
            _hints = value;
            _shownHints = [.. value.Where(hint => hint.IsOnBar)];
            _spans = new (int, int)[_shownHints.Length];
            SetNeedsDraw();
        }
    }

    public void ApplyTheme(Theme theme) => _theme = theme;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var labelAttribute = _theme.On(_theme.Foreground);
        var keyAttribute = _theme.On(_theme.Accent, TextStyle.Bold);

        SetAttribute(labelAttribute);
        Move(0, 0);
        AddStr(" ");
        var column = 1;

        for (var i = 0; i < _shownHints.Length; i++)
        {
            if (i > 0)
            {
                SetAttribute(labelAttribute);
                AddStr(Gap);
                column += Gap.Length;
            }

            var hint = _shownHints[i];
            var start = column;
            SetAttribute(keyAttribute);
            AddStr(hint.KeyText);
            SetAttribute(labelAttribute);
            AddStr(" " + hint.Label);
            column += DisplayWidth.Of(hint.KeyText) + 1 + DisplayWidth.Of(hint.Label);
            _spans[i] = (start, column);
        }

        return true;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!mouse.IsLeftClick() || mouse.Position is not { } position)
        {
            return false;
        }

        for (var i = 0; i < _spans.Length; i++)
        {
            if (position.X >= _spans[i].Start && position.X < _spans[i].End)
            {
                _shownHints[i].Action();
                return true;
            }
        }

        return false;
    }
}
