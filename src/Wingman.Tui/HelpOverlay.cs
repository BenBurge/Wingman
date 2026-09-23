using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>A key and what it does, as the help overlay lists it: <c>u</c>, <c>upgrade</c>.</summary>
internal sealed record HelpKey(string Key, string Label);

/// <summary>A titled list of keys in the help overlay, such as <c>Global</c> or <c>Installed</c>.</summary>
internal sealed record HelpGroup(string Title, IReadOnlyList<HelpKey> Keys);

/// <summary>
/// The <c>?</c> overlay: a bordered box titled <c>Keys</c> centered over the content, listing
/// each group's keys in accent and what they do in the foreground color. The groups stack in one
/// column, or the first goes on the left and the rest on the right when one column would not fit
/// the height. Like <see cref="ContextMenu"/> it never takes focus; the shell forwards every key to
/// <see cref="HandleKey"/> while it is open.
/// </summary>
internal sealed class HelpOverlay : View
{
    private const string BoxTitle = "Keys";
    private const int PreferredWidth = 60;

    // The border and a blank row inside it, at the top and at the bottom.
    private const int FrameRows = 4;
    private const int KeyGap = 2;

    private readonly Theme _theme;
    private List<List<HelpLine>> _columns = [];
    private int _keyWidth;

    public HelpOverlay(Theme theme)
    {
        _theme = theme;
        CanFocus = false;
        Visible = false;
    }

    /// <summary>Shows <paramref name="groups"/> centered in <paramref name="bounds"/>, in the superview's viewport coordinates.</summary>
    public void Open(IReadOnlyList<HelpGroup> groups, Rectangle bounds)
    {
        _keyWidth = 0;
        foreach (var group in groups)
        {
            foreach (var key in group.Keys)
            {
                _keyWidth = Math.Max(_keyWidth, DisplayWidth.Of(key.Key));
            }
        }

        var oneColumn = LinesFor(groups);
        var fitsOneColumn = oneColumn.Count + FrameRows <= bounds.Height;
        _columns = fitsOneColumn || groups.Count < 2
            ? [oneColumn]
            : [LinesFor(groups.Take(1)), LinesFor(groups.Skip(1))];

        var width = Math.Min(PreferredWidth, bounds.Width);
        var height = Math.Min(_columns.Max(column => column.Count) + FrameRows, bounds.Height);
        X = bounds.Left + (bounds.Width - width) / 2;
        Y = bounds.Top + (bounds.Height - height) / 2;
        Width = width;
        Height = height;
        Visible = true;
        SetNeedsDraw();
    }

    public void Close() => Visible = false;

    /// <summary>Esc, <c>?</c>, <c>q</c>, and Enter close the overlay; every other key is ignored.</summary>
    public void HandleKey(Key key)
    {
        var isPlainKey = !key.IsCtrl && !key.IsAlt;
        var isCloseCharacter = isPlainKey
            && key.TryGetPrintableRune(out var rune)
            && (rune.Value == '?' || rune.Value == 'q');
        if (key == Key.Esc || key == Key.Enter || isCloseCharacter)
        {
            Close();
        }
    }

    /// <summary>Draws the box. The shell calls this from the window's <c>DrawComplete</c>, for the reason <see cref="ContextMenu.Paint"/> gives.</summary>
    public void Paint()
    {
        var width = Viewport.Width;
        var height = Viewport.Height;
        var border = _theme.On(_theme.Border);

        SetAttribute(border);
        Move(0, 0);
        AddStr("┌─ ");
        SetAttribute(_theme.On(_theme.Foreground, TextStyle.Bold));
        AddStr(BoxTitle);
        SetAttribute(border);
        AddStr(" " + new string('─', Math.Max(0, width - 5 - BoxTitle.Length)) + "┐");

        for (var y = 1; y < height - 1; y++)
        {
            SetAttribute(border);
            Move(0, y);
            AddStr("│");
            SetAttribute(_theme.On(_theme.Foreground));
            AddStr(new string(' ', width - 2));
            SetAttribute(border);
            AddStr("│");
        }

        Move(0, height - 1);
        AddStr("└" + new string('─', width - 2) + "┘");

        // Two columns split the inside evenly; one column uses all of it.
        var innerWidth = width - 4;
        var columnWidth = _columns.Count == 1 ? innerWidth : (innerWidth - KeyGap) / 2;
        for (var c = 0; c < _columns.Count; c++)
        {
            var left = 2 + c * (columnWidth + KeyGap);
            DrawColumn(_columns[c], left, columnWidth, height - FrameRows);
        }
    }

    /// <summary>Draws nothing; see <see cref="Paint"/>.</summary>
    protected override bool OnDrawingContent(DrawContext? context) => true;

    /// <summary>Eats every mouse event inside the box, so a click there neither closes it nor reaches the view behind it.</summary>
    protected override bool OnMouseEvent(Mouse mouse) => true;

    private static List<HelpLine> LinesFor(IEnumerable<HelpGroup> groups)
    {
        var lines = new List<HelpLine>();
        foreach (var group in groups)
        {
            if (lines.Count > 0)
            {
                lines.Add(new HelpLine(null, ""));
            }

            lines.Add(new HelpLine(null, group.Title));
            foreach (var key in group.Keys)
            {
                lines.Add(new HelpLine(key.Key, key.Label));
            }
        }

        return lines;
    }

    private void DrawColumn(List<HelpLine> lines, int left, int columnWidth, int rows)
    {
        var labelLeft = left + _keyWidth + KeyGap;
        var labelWidth = Math.Max(0, columnWidth - _keyWidth - KeyGap);
        var count = Math.Min(lines.Count, rows);
        for (var i = 0; i < count; i++)
        {
            var y = 2 + i;
            var line = lines[i];
            if (line.KeyText is null)
            {
                DrawText(left, y, CellText.Fit(line.Text, columnWidth), _theme.On(_theme.Header));
                continue;
            }

            DrawText(left, y, line.KeyText, _theme.On(_theme.Accent, TextStyle.Bold));
            DrawText(labelLeft, y, CellText.Fit(line.Text, labelWidth), _theme.On(_theme.Foreground));
        }
    }

    private void DrawText(int x, int y, string text, Attribute color)
    {
        SetAttribute(color);
        Move(x, y);
        AddStr(text);
    }

    /// <summary>A key and its label, or a group title or blank separator when <see cref="KeyText"/> is null.</summary>
    private sealed record HelpLine(string? KeyText, string Text);
}
