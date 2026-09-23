using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Wingman.Tui;

/// <summary>
/// The <see cref="TableView"/> every list uses: whole-row selection, no grid lines, one header row
/// in the header color, fixed column widths with text cut to fit, and keys that reach the window
/// when the table cannot use them.
/// </summary>
/// <remarks>
/// It lets printable keys go up to the window while it has no rows. TableView swallows them then,
/// as type-to-search with nothing to search, which would leave the tab keys, the key bar, and
/// <c>q</c> dead on an empty list. Space is unbound too: TableView toggles a cell into its
/// multi-selection on it, which no list uses, and the tabs mark the cursor row for the batch with
/// it instead. The arrow and paging keys it could not use at the first or last row are taken, or
/// they would bubble up to the window, where Terminal.Gui would move focus out of the list.
/// </remarks>
internal sealed class KeyPassingTableView : TableView, IThemedView
{
    // The header is a single row because the header overline and underline are turned off.
    public const int HeaderRows = 1;

    public KeyPassingTableView(Theme theme, ITableSource source)
        : base(source)
    {
        KeyBindings.Remove(Key.Space);
        FullRowSelect = true;
        MultiSelect = false;

        // Type-to-search would swallow the letter keys the key bar dispatches.
        CollectionNavigator = null;

        Style.ShowHorizontalHeaderOverline = false;
        Style.ShowHorizontalHeaderUnderline = false;
        Style.ShowHorizontalBottomLine = false;
        Style.ShowVerticalCellLines = false;
        Style.ShowVerticalHeaderLines = false;
        Style.InvertSelectedCellFirstCharacter = false;
        Style.ExpandLastColumn = true;
        Style.AlwaysShowHeaders = true;
        Style.HeaderScheme = theme.HeaderScheme;
    }

    public void ApplyTheme(Theme theme) => Style.HeaderScheme = theme.HeaderScheme;

    /// <summary>
    /// Pins <paramref name="tableColumn"/> to <paramref name="width"/> cells and cuts its text to fit
    /// by display width, so a wide character never pushes a row past its column. The width includes
    /// the one-cell gap TableView draws after the text; returns the text width. Call
    /// <c>Update()</c> once every column is set, since TableView caches its column layout.
    /// </summary>
    public int SetColumnWidth(int tableColumn, int width)
    {
        var textWidth = Math.Max(1, width - 1);
        var style = Style.GetOrCreateColumnStyle(tableColumn);
        style.MinWidth = textWidth;
        style.MaxWidth = textWidth;
        style.RepresentationGetter = value => CellText.Fit(value?.ToString() ?? "", textWidth);
        return textWidth;
    }

    /// <summary>Scrolls the rows by <paramref name="step"/> without moving the cursor, for the mouse wheel, which TableView ignores.</summary>
    public void ScrollRows(int step)
    {
        var rowCount = Table?.Rows ?? 0;
        var visibleRows = Math.Max(1, Viewport.Height - HeaderRows);
        var maxOffset = Math.Max(0, rowCount - visibleRows);
        RowOffset = Math.Clamp(RowOffset + step, 0, maxOffset);
        SetNeedsDraw();
    }

    protected override bool OnKeyDownNotHandled(Key key)
    {
        var isNavigationKey = key == Key.CursorUp || key == Key.CursorDown
            || key == Key.CursorLeft || key == Key.CursorRight
            || key == Key.PageUp || key == Key.PageDown
            || key == Key.Home || key == Key.End;
        if (isNavigationKey)
        {
            return true;
        }

        return Table is { Rows: > 0 } && base.OnKeyDownNotHandled(key);
    }
}
