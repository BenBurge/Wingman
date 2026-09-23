using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Models;

namespace Wingman.Tui;

/// <summary>
/// The Updates tab's right pane while <c>e</c> shows it: the packages with an update that Wingman
/// leaves to the app itself, one <c>⟳ Id</c> entry each with <c>self-updating</c> under it, then
/// the <c>p</c> hint. With focus the arrows select an entry, drawn background-on-accent, and
/// <c>p</c> opens the update policy for it; a click selects one too.
/// </summary>
internal sealed class ExcludedPane : View
{
    private const string Marker = "⟳";

    // The title and the blank row under it.
    private const int EntriesTop = 2;
    private const int RowsPerEntry = 2;

    private readonly Theme _theme;
    private IReadOnlyList<PackageRow> _rows = [];
    private int _selected;

    public ExcludedPane(Theme theme)
    {
        _theme = theme;
        CanFocus = true;
    }

    /// <summary>The selected entry, or null when nothing is excluded.</summary>
    public PackageRow? SelectedRow => _selected < _rows.Count ? _rows[_selected] : null;

    public void SetRows(IReadOnlyList<PackageRow> rows)
    {
        _rows = rows;
        _selected = Math.Clamp(_selected, 0, Math.Max(0, rows.Count - 1));
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var textWidth = Math.Max(0, Viewport.Width - 1);
        var dim = _theme.On(_theme.Dim);

        Move(0, 0);
        SetAttribute(_theme.On(_theme.Header));
        AddStr(CellText.Fit(" Excluded from Wingman", textWidth));

        var y = EntriesTop;
        if (_rows.Count == 0)
        {
            Move(0, y);
            SetAttribute(dim);
            AddStr(CellText.Fit(" Nothing with an update is excluded.", textWidth));
            y++;
        }

        for (var i = 0; i < _rows.Count && y + 1 < Viewport.Height; i++)
        {
            var isSelected = HasFocus && i == _selected;
            Move(0, y);
            SetAttribute(isSelected ? _theme.Selected : _theme.On(_theme.Foreground));
            AddStr(CellText.Fit($" {Marker} {_rows[i].Id}", textWidth));
            Move(0, y + 1);
            SetAttribute(dim);
            AddStr("   self-updating");
            y += RowsPerEntry;
        }

        y++;
        if (y < Viewport.Height)
        {
            Move(0, y);
            SetAttribute(_theme.On(_theme.Foreground));
            AddStr(" ");
            SetAttribute(_theme.On(_theme.Accent, TextStyle.Bold));
            AddStr("p");
            SetAttribute(dim);
            AddStr(CellText.Fit(" change policy for the row", Math.Max(0, textWidth - 2)));
        }

        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorUp || key == Key.CursorDown)
        {
            var step = key == Key.CursorUp ? -1 : 1;
            _selected = Math.Clamp(_selected + step, 0, Math.Max(0, _rows.Count - 1));
            SetNeedsDraw();
            return true;
        }

        // Left and right do nothing here, but left unhandled they would move focus out of the pane.
        var isSidewaysKey = key == Key.CursorLeft || key == Key.CursorRight;
        return isSidewaysKey || base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!mouse.IsLeftClick() || mouse.Position is not { } position)
        {
            return base.OnMouseEvent(mouse);
        }

        SetFocus();
        var index = (position.Y - EntriesTop) / RowsPerEntry;
        if (position.Y >= EntriesTop && index < _rows.Count)
        {
            _selected = index;
        }

        SetNeedsDraw();
        return true;
    }

    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView) => SetNeedsDraw();
}
