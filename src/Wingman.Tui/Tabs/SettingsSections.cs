using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui.Tabs;

/// <summary>
/// The Settings tab's left pane: <c>Sections</c> in the header color, a blank row, then one row per
/// section. The selected row is drawn as the tables draw their cursor row while the list has
/// focus, and in accent while focus is in the section itself. Up, down, Home, End, and a click
/// select a section and raise <see cref="SelectionChanged"/>.
/// </summary>
internal sealed class SettingsSectionList : View, IThemedView
{
    private const string HeaderText = "Sections";

    // The header and the blank row under it.
    private const int HeaderRows = 2;

    private readonly string[] Titles;
    private Theme _theme;
    private int _selectedIndex;

    public SettingsSectionList(Theme theme, IReadOnlyList<string> titles)
    {
        _theme = theme;
        Titles = [.. titles];
        CanFocus = true;
    }

    /// <summary>Raised when a key or a click selects another section; setting <see cref="SelectedIndex"/> does not raise it.</summary>
    public event Action? SelectionChanged;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = Math.Clamp(value, 0, Titles.Length - 1);
            SetNeedsDraw();
        }
    }

    public void ApplyTheme(Theme theme) => _theme = theme;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var top = TopRow;
        if (top > 0)
        {
            DrawRow(0, " " + HeaderText, _theme.On(_theme.Header), width);
        }

        for (var i = 0; i < Titles.Length; i++)
        {
            var isSelected = i == _selectedIndex;
            Attribute color;
            if (isSelected && HasFocus)
            {
                color = _theme.Selected;
            }
            else if (isSelected)
            {
                color = _theme.On(_theme.Accent);
            }
            else
            {
                color = _theme.On(_theme.Foreground);
            }

            DrawRow(top + i, " " + Titles[i], color, width);
        }

        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        int target;
        if (key == Key.CursorUp)
        {
            target = _selectedIndex - 1;
        }
        else if (key == Key.CursorDown)
        {
            target = _selectedIndex + 1;
        }
        else if (key == Key.Home)
        {
            target = 0;
        }
        else if (key == Key.End)
        {
            target = Titles.Length - 1;
        }
        else
        {
            return base.OnKeyDown(key);
        }

        Select(target);
        return true;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!mouse.IsLeftClick() || mouse.Position is not { } position)
        {
            return base.OnMouseEvent(mouse);
        }

        SetFocus();
        var index = position.Y - TopRow;
        if (index >= 0 && index < Titles.Length)
        {
            Select(index);
        }

        return true;
    }

    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView) => SetNeedsDraw();

    /// <summary>The row of the first section: under the header when the pane is tall enough for both, at the top otherwise.</summary>
    private int TopRow => Viewport.Height >= HeaderRows + Titles.Length ? HeaderRows : 0;

    private void Select(int index)
    {
        var clamped = Math.Clamp(index, 0, Titles.Length - 1);
        if (clamped == _selectedIndex)
        {
            return;
        }

        SelectedIndex = clamped;
        SelectionChanged?.Invoke();
    }

    /// <summary>Draws <paramref name="text"/> padded to the whole row, so a selected row's color spans the pane.</summary>
    private void DrawRow(int y, string text, Attribute color, int width)
    {
        var fitted = CellText.Fit(text, width);
        Move(0, y);
        SetAttribute(color);
        AddStr(fitted + new string(' ', Math.Max(0, width - DisplayWidth.Of(fitted))));
    }
}

/// <summary>
/// One line of a setting in a <see cref="SettingsSection"/>: <paramref name="Control"/> with
/// <paramref name="Before"/> and <paramref name="After"/> drawn around it, and a text box also
/// between <c>[ </c> and <c> ]</c>. While the control is null or hidden, as an action the host
/// cannot run, the section draws <see cref="Unavailable"/> dim in its place.
/// </summary>
internal sealed record SettingLine(View? Control, string Before = "", string After = "")
{
    public string Unavailable { get; init; } = "";
}

/// <summary>
/// A page of the Settings tab's right pane: the title in bold with a dim summary, a blank row, then
/// each setting as its label padded to <see cref="LabelWidth"/>, its controls one per line, a dim
/// explanation wrapped under the controls, and a blank row. A radio list that does not fit on one
/// line is stacked one option per row. Content taller than the pane scrolls: focusing a control
/// brings it into view, the wheel scrolls, and a dim <c>▲</c> or <c>▼</c> in the right column says
/// more is above or below.
/// </summary>
internal sealed class SettingsSection : View, IThemedView
{
    private const int LabelWidth = 26;
    private const int LeftMargin = 1;
    private const int ControlLeft = LeftMargin + LabelWidth;
    private const int WheelStep = 3;
    private const string OpenBracket = "[ ";
    private const string CloseBracket = " ]";

    // The title row and the blank row under it.
    private const int HeaderRows = 2;

    private readonly string _summary;
    private readonly List<Setting> _settings = [];
    private Theme _theme;

    // The first content row on screen, and the content's height as last laid out.
    private int _scroll;
    private int _contentHeight;

    // A control that took focus since the last layout, to bring into view once rows are known.
    private View? _focusTarget;

    public SettingsSection(Theme theme, string title, string summary)
    {
        _theme = theme;
        Title = title;
        _summary = summary;
        CanFocus = true;
    }

    /// <summary>The controls that can take focus now, in the order they are drawn.</summary>
    public IReadOnlyList<View> Fields
    {
        get
        {
            var fields = new List<View>();
            foreach (var setting in _settings)
            {
                foreach (var line in setting.Lines)
                {
                    if (IsLive(line))
                    {
                        fields.Add(line.Control!);
                    }
                }
            }

            return fields;
        }
    }

    public void AddSetting(string label, IReadOnlyList<SettingLine> lines, string explanation) =>
        AddSetting(label, lines, () => explanation);

    /// <summary>Adds a setting whose explanation, such as a status, is read again at every layout.</summary>
    public void AddSetting(string label, IReadOnlyList<SettingLine> lines, Func<string> explanation)
    {
        var setting = new Setting(label, [.. lines], explanation);
        _settings.Add(setting);
        foreach (var line in lines)
        {
            if (line.Control is { } control)
            {
                AddControl(control);
            }
        }
    }

    /// <summary>Lays the section out again, for when a control was shown or an explanation changed.</summary>
    public void Refresh()
    {
        SetNeedsLayout();
        SetNeedsDraw();
    }

    public void ApplyTheme(Theme theme) => _theme = theme;

    /// <summary>
    /// Places every control from the pane's width: a radio list is stacked when its one-row form
    /// would pass the right edge, explanations wrap to the space right of the label column, and
    /// the scroll is kept in range and moved to show a control that just took focus.
    /// </summary>
    protected override void OnSubViewLayout(LayoutEventArgs args)
    {
        base.OnSubViewLayout(args);

        var width = Viewport.Width;
        var explanationWidth = Math.Max(1, width - ControlLeft - 1);
        var row = HeaderRows;
        for (var i = 0; i < _settings.Count; i++)
        {
            var setting = _settings[i];
            if (i > 0)
            {
                row++;
            }

            setting.Top = row;
            for (var j = 0; j < setting.Lines.Length; j++)
            {
                setting.LineTops[j] = row;
                row += PlaceLine(setting.Lines[j], width);
            }

            setting.WrappedExplanation = CellText.Wrap(setting.Explanation(), explanationWidth);
            setting.ExplanationTop = row;
            row += setting.WrappedExplanation.Count;
            setting.Bottom = row;
        }

        _contentHeight = row;
        if (_focusTarget is { } target)
        {
            _focusTarget = null;
            ScrollToShow(target);
        }

        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _contentHeight - Viewport.Height));
        foreach (var setting in _settings)
        {
            for (var j = 0; j < setting.Lines.Length; j++)
            {
                if (setting.Lines[j].Control is { } control)
                {
                    control.Y = setting.LineTops[j] - _scroll;
                }
            }
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var normal = _theme.On(_theme.Foreground);
        var dim = _theme.On(_theme.Dim);

        var titleWidth = DisplayWidth.Of(Title);
        DrawText(LeftMargin, 0, Title, _theme.On(_theme.Foreground, TextStyle.Bold), width);
        DrawText(LeftMargin + titleWidth + 2, 0, _summary, dim, width);

        foreach (var setting in _settings)
        {
            DrawText(LeftMargin, setting.Top, setting.Label, normal, width);
            for (var j = 0; j < setting.Lines.Length; j++)
            {
                DrawLine(setting.Lines[j], setting.LineTops[j], normal, dim, width);
            }

            for (var i = 0; i < setting.WrappedExplanation.Count; i++)
            {
                DrawText(ControlLeft, setting.ExplanationTop + i, setting.WrappedExplanation[i], dim, width);
            }
        }

        DrawScrollMarkers(width, dim);
        return true;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (TryScrollWith(mouse))
        {
            return true;
        }

        return base.OnMouseEvent(mouse);
    }

    private bool HasMoreAbove => _scroll > 0;

    private bool HasMoreBelow => _scroll + Viewport.Height < _contentHeight;

    private static bool IsLive(SettingLine line) => line.Control is { Visible: true, CanFocus: true };

    /// <summary>Puts the line's control in place for this width and returns the rows the line takes.</summary>
    private static int PlaceLine(SettingLine line, int width)
    {
        if (line.Control is not { } control)
        {
            return 1;
        }

        var left = ControlLeft + DisplayWidth.Of(line.Before);
        if (control is FormTextField)
        {
            left += OpenBracket.Length;
        }

        control.X = left;
        if (control is OptionRow options)
        {
            options.IsStacked = left + options.RowWidth > width - 1;
            return options.RowCount;
        }

        return 1;
    }

    private void AddControl(View control)
    {
        Add(control);
        control.HasFocusChanged += (_, focus) =>
        {
            if (focus.NewValue)
            {
                _focusTarget = control;
            }

            Refresh();
        };

        // Controls take clicks only, so the wheel scrolls the section wherever the pointer is.
        control.MouseEvent += (_, mouse) =>
        {
            if (TryScrollWith(mouse))
            {
                mouse.Handled = true;
            }
        };
    }

    /// <summary>
    /// Scrolls as little as needed to show the setting <paramref name="control"/> belongs to, from
    /// its label to the end of its explanation, or at least the control's own rows when the whole
    /// setting is taller than the pane; the first setting also brings the title back.
    /// </summary>
    private void ScrollToShow(View control)
    {
        var height = Viewport.Height;
        for (var i = 0; i < _settings.Count; i++)
        {
            var setting = _settings[i];
            var lineIndex = Array.FindIndex(setting.Lines, candidate => candidate.Control == control);
            if (lineIndex < 0)
            {
                continue;
            }

            var top = i == 0 ? 0 : setting.Top;
            var bottom = setting.Bottom;
            if (bottom - top > height)
            {
                top = setting.LineTops[lineIndex];
                bottom = top + ((control as OptionRow)?.RowCount ?? 1);
            }

            if (top < _scroll)
            {
                _scroll = top;
            }
            else if (bottom > _scroll + height)
            {
                _scroll = bottom - height;
            }

            return;
        }
    }

    private bool TryScrollWith(Mouse mouse)
    {
        int step;
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            step = WheelStep;
        }
        else if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            step = -WheelStep;
        }
        else
        {
            return false;
        }

        _scroll = Math.Clamp(_scroll + step, 0, Math.Max(0, _contentHeight - Viewport.Height));
        Refresh();
        return true;
    }

    private void DrawLine(SettingLine line, int top, Attribute normal, Attribute dim, int width)
    {
        if (line.Control is not { Visible: true } control)
        {
            DrawText(ControlLeft, top, line.Unavailable, dim, width);
            return;
        }

        DrawText(ControlLeft, top, line.Before, normal, width);
        var controlLeft = control.Frame.X;
        var controlRight = controlLeft + control.Frame.Width;
        if (control is FormTextField)
        {
            var bracket = control.HasFocus ? _theme.On(_theme.Accent, TextStyle.Bold) : normal;
            DrawText(controlLeft - OpenBracket.Length, top, OpenBracket, bracket, width);
            DrawText(controlRight, top, CloseBracket, bracket, width);
            controlRight += CloseBracket.Length;
        }

        DrawText(controlRight, top, line.After, normal, width);
    }

    private void DrawScrollMarkers(int width, Attribute dim)
    {
        var markerX = width - 1;
        if (HasMoreAbove)
        {
            Move(markerX, 0);
            SetAttribute(dim);
            AddStr("▲");
        }

        if (HasMoreBelow)
        {
            Move(markerX, Viewport.Height - 1);
            SetAttribute(dim);
            AddStr("▼");
        }
    }

    /// <summary>Draws <paramref name="text"/> at content row <paramref name="contentRow"/>, shifted by the scroll and kept off the marker column.</summary>
    private void DrawText(int x, int contentRow, string text, Attribute color, int width)
    {
        var y = contentRow - _scroll;
        if (text.Length == 0 || y < 0 || y >= Viewport.Height)
        {
            return;
        }

        Move(x, y);
        SetAttribute(color);
        AddStr(CellText.Fit(text, Math.Max(0, width - x - 1)));
    }

    /// <summary>A setting and, once laid out, the content rows its parts take.</summary>
    private sealed class Setting(string label, SettingLine[] lines, Func<string> explanation)
    {
        public string Label { get; } = label;

        public SettingLine[] Lines { get; } = lines;

        public Func<string> Explanation { get; } = explanation;

        public int Top { get; set; }

        public int ExplanationTop { get; set; }

        public int Bottom { get; set; }

        public int[] LineTops { get; } = new int[lines.Length];

        public List<string> WrappedExplanation { get; set; } = [];
    }
}
