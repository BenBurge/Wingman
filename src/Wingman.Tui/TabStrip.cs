using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Winget;

namespace Wingman.Tui;

/// <summary>
/// The one-row tab strip, <c> Installed 142 │ Discover │ Updates 6 │ …</c>, with the selected tab
/// drawn background-on-accent. Clicking a label selects that tab.
/// </summary>
internal sealed class TabStrip : View
{
    private const string Separator = "│";

    private readonly Theme _theme;
    private readonly string[] _titles;
    private readonly int?[] _counts;

    // Column span of each label as last drawn, used to hit-test clicks.
    private readonly (int Start, int End)[] _spans;

    private int _selectedIndex;

    public TabStrip(Theme theme, IReadOnlyList<string> titles)
    {
        _theme = theme;
        _titles = [.. titles];
        _counts = new int?[_titles.Length];
        _spans = new (int, int)[_titles.Length];
        Height = 1;
        CanFocus = false;
    }

    public event Action<int>? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value == _selectedIndex || value < 0 || value >= _titles.Length)
            {
                return;
            }

            _selectedIndex = value;
            SetNeedsDraw();
            SelectedIndexChanged?.Invoke(value);
        }
    }

    /// <summary>Shows <paramref name="count"/> after the tab's title, or nothing when it is null.</summary>
    public void SetCount(int index, int? count)
    {
        _counts[index] = count;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        SetAttribute(_theme.On(_theme.Foreground));
        Move(0, 0);
        AddStr(" ");
        var column = 1;

        for (var i = 0; i < _titles.Length; i++)
        {
            if (i > 0)
            {
                SetAttribute(_theme.On(_theme.Border));
                AddStr(Separator);
                column += DisplayWidth.Of(Separator);
            }

            var label = LabelFor(i);
            var isSelected = i == _selectedIndex;
            SetAttribute(isSelected ? _theme.SelectedBold : _theme.On(_theme.Foreground));
            AddStr(label);

            var width = DisplayWidth.Of(label);
            _spans[i] = (column, column + width);
            column += width;
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
                SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    private string LabelFor(int index)
    {
        var count = _counts[index];
        return count is null ? $" {_titles[index]} " : $" {_titles[index]} {count} ";
    }
}
