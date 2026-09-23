using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>One line of a <see cref="ContextMenu"/>: a label that runs <see cref="Action"/>, or a rule when the action is null.</summary>
internal sealed record MenuEntry(string Label, Action? Action)
{
    public static MenuEntry Rule { get; } = new("", null);

    public bool IsRule => Action is null;
}

/// <summary>
/// The row context menu from the mockup: a bordered box with the package name in bold, a rule,
/// then the entries, the selected one drawn background-on-accent. It never takes focus; the shell
/// forwards every key to <see cref="HandleKey"/> while it is open, so nothing reaches the tab
/// underneath, and closes it on a click outside.
/// </summary>
internal sealed class ContextMenu : View, IThemedView
{
    private const int MinWidth = 30;

    // The top border, the title, and the rule under it.
    private const int EntriesTop = 3;

    private Theme _theme;
    private string _title = "";
    private IReadOnlyList<MenuEntry> _entries = [];
    private int _selected;

    public ContextMenu(Theme theme)
    {
        _theme = theme;
        CanFocus = false;
        Visible = false;
    }

    /// <summary>
    /// Shows <paramref name="entries"/> under <paramref name="title"/> with its top-left corner at
    /// <paramref name="position"/>, moved up or left as needed to stay inside <paramref name="bounds"/>;
    /// both are in the superview's viewport coordinates.
    /// </summary>
    public void Open(string title, IReadOnlyList<MenuEntry> entries, Point position, Rectangle bounds)
    {
        _title = title;
        _entries = entries;
        _selected = entries.ToList().FindIndex(entry => !entry.IsRule);

        var labelWidth = DisplayWidth.Of(title);
        foreach (var entry in entries)
        {
            labelWidth = Math.Max(labelWidth, DisplayWidth.Of(entry.Label));
        }

        // A border and a space on each side of the widest line.
        var width = Math.Min(Math.Max(MinWidth, labelWidth + 4), bounds.Width);
        var height = Math.Min(EntriesTop + entries.Count + 1, bounds.Height);
        X = Math.Clamp(position.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - width));
        Y = Math.Clamp(position.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height));
        Width = width;
        Height = height;
        Visible = true;
        SetNeedsDraw();
    }

    public void Close() => Visible = false;

    /// <summary>Up and Down move the selection, Enter runs it, Esc closes the menu; every other key is ignored.</summary>
    public void HandleKey(Key key)
    {
        if (key == Key.Esc)
        {
            Close();
        }
        else if (key == Key.Enter)
        {
            Activate(_selected);
        }
        else if (key == Key.CursorDown)
        {
            MoveSelection(1);
        }
        else if (key == Key.CursorUp)
        {
            MoveSelection(-1);
        }
    }

    /// <summary>
    /// Draws the box. The shell calls this from the window's <c>DrawComplete</c>: the window renders
    /// its line canvas, the pane divider included, after its subviews, so a box drawn in the usual
    /// pass would have the divider drawn through it.
    /// </summary>
    public void Paint()
    {
        var width = Viewport.Width;
        var innerWidth = width - 2;
        var border = _theme.On(_theme.Border);

        DrawEdge(0, '┌', '┐', border);
        DrawBoxedLine(1, CellText.Fit(_title, innerWidth - 2), _theme.On(_theme.Foreground, TextStyle.Bold), border);
        DrawEdge(2, '├', '┤', border);

        for (var i = 0; i < _entries.Count; i++)
        {
            var y = EntriesTop + i;
            var entry = _entries[i];
            if (entry.IsRule)
            {
                DrawEdge(y, '├', '┤', border);
                continue;
            }

            var color = i == _selected ? _theme.Selected : _theme.On(_theme.Foreground);
            DrawBoxedLine(y, CellText.Fit(entry.Label, innerWidth - 2), color, border);
        }

        DrawEdge(EntriesTop + _entries.Count, '└', '┘', border);
    }

    public void ApplyTheme(Theme theme) => _theme = theme;

    /// <summary>Draws nothing; see <see cref="Paint"/>.</summary>
    protected override bool OnDrawingContent(DrawContext? context) => true;

    /// <summary>A click on an entry runs it; hovering selects it. Every event inside the box stops here.</summary>
    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Position is not { } position)
        {
            return true;
        }

        var index = position.Y - EntriesTop;
        var isOnEntry = index >= 0 && index < _entries.Count && !_entries[index].IsRule;
        if (!isOnEntry)
        {
            return true;
        }

        if (mouse.IsLeftClick())
        {
            Activate(index);
        }
        else if (index != _selected)
        {
            _selected = index;
            SetNeedsDraw();
        }

        return true;
    }

    /// <summary>Closes the menu before running the entry, so a question it asks is not hidden behind the menu.</summary>
    private void Activate(int index)
    {
        if (index < 0 || index >= _entries.Count || _entries[index].Action is not { } action)
        {
            return;
        }

        Close();
        action();
    }

    private void MoveSelection(int step)
    {
        if (_selected < 0)
        {
            return;
        }

        var index = _selected;
        do
        {
            index = (index + step + _entries.Count) % _entries.Count;
        }
        while (_entries[index].IsRule);

        _selected = index;
        SetNeedsDraw();
    }

    private void DrawEdge(int y, char left, char right, Attribute color)
    {
        SetAttribute(color);
        Move(0, y);
        AddStr(left + new string('─', Viewport.Width - 2) + right);
    }

    /// <summary>Draws <c>│ text │</c>, the text and its padding in <paramref name="color"/> and the sides in <paramref name="border"/>.</summary>
    private void DrawBoxedLine(int y, string text, Attribute color, Attribute border)
    {
        var innerWidth = Viewport.Width - 2;
        SetAttribute(border);
        Move(0, y);
        AddStr("│");
        SetAttribute(color);
        var padding = Math.Max(0, innerWidth - 1 - DisplayWidth.Of(text));
        AddStr(" " + text + new string(' ', padding));
        SetAttribute(border);
        AddStr("│");
    }
}
