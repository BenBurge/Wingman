using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.Winget;

namespace Wingman.Tui;

/// <summary>
/// A text box for a <see cref="FormView"/>: text in the foreground color, a dim
/// <see cref="Placeholder"/> while it is empty and unfocused, and every key offered to
/// <see cref="FormKeys"/> before the box types it.
/// </summary>
internal sealed class FormTextField : TextField, IThemedView
{
    private Theme _theme;

    public FormTextField(Theme theme)
    {
        _theme = theme;
        Height = 1;
        SetScheme(theme.FieldScheme);
    }

    /// <summary>What the box shows, dimmed, while it is empty and unfocused, such as <c>latest</c>.</summary>
    public string Placeholder { get; set; } = "";

    /// <summary>Returns true for a key the form handled, which the box then leaves alone.</summary>
    public Func<Key, bool>? FormKeys { get; set; }

    public void ApplyTheme(Theme theme)
    {
        _theme = theme;
        SetScheme(theme.FieldScheme);
    }

    protected override bool OnKeyDown(Key key) => (FormKeys?.Invoke(key) ?? false) || base.OnKeyDown(key);

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var showsPlaceholder = Text.Length == 0 && !HasFocus && Placeholder.Length > 0;
        if (!showsPlaceholder)
        {
            return base.OnDrawingContent(context);
        }

        SetAttribute(_theme.On(_theme.Dim));
        Move(0, 0);
        AddStr(CellText.Fit(Placeholder, Viewport.Width));
        return true;
    }
}

/// <summary>
/// One checkbox, <c>[x] Label</c>, with the <c>x</c> in accent; drawn background-on-accent while
/// it has focus. Space or a click toggles it and raises <see cref="Toggled"/>.
/// </summary>
internal sealed class CheckField : View, IThemedView
{
    private Theme _theme;
    private readonly string _label;
    private bool _isChecked;

    public CheckField(Theme theme, string label)
    {
        _theme = theme;
        _label = label;
        Height = 1;
        Width = WidthFor(label);
        CanFocus = true;
    }

    /// <summary>Raised after <see cref="Toggle"/>, which Space and a click call; setting <see cref="IsChecked"/> does not raise it.</summary>
    public event Action? Toggled;

    /// <summary>The cells a checkbox labeled <paramref name="label"/> takes: <c>[x] </c> and the label.</summary>
    public static int WidthFor(string label) => DisplayWidth.Of(label) + 4;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            _isChecked = value;
            SetNeedsDraw();
        }
    }

    public void Toggle()
    {
        IsChecked = !IsChecked;
        Toggled?.Invoke();
    }

    public void ApplyTheme(Theme theme) => _theme = theme;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var mark = _isChecked ? "x" : " ";
        Move(0, 0);
        if (HasFocus)
        {
            SetAttribute(_theme.Selected);
            AddStr($"[{mark}] {_label}");
            return true;
        }

        SetAttribute(_theme.On(_theme.Foreground));
        AddStr("[");
        SetAttribute(_theme.On(_theme.Accent));
        AddStr(mark);
        SetAttribute(_theme.On(_theme.Foreground));
        AddStr("] " + _label);
        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Space)
        {
            Toggle();
            return true;
        }

        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!mouse.IsLeftClick())
        {
            return base.OnMouseEvent(mouse);
        }

        SetFocus();
        Toggle();
        return true;
    }

    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView) => SetNeedsDraw();
}

/// <summary>
/// Radio options, <c>( ) default  (•) machine  ( ) user</c> on one row or, when
/// <see cref="IsStacked"/>, one option per row, with the dot in accent. With focus, left and right
/// (and up and down while stacked, until the first or last option) move a highlight drawn
/// background-on-accent and Space picks the highlighted option; a click picks the option under it.
/// Either raises <see cref="Picked"/>. <see cref="SelectedIndex"/> is -1 when none is picked, for a
/// stored value that matches no option.
/// </summary>
internal sealed class OptionRow : View, IThemedView
{
    private const string Gap = "  ";

    private Theme _theme;
    private readonly string[] _options;
    private readonly (int Start, int End)[] _spans;
    private readonly int _stackedWidth;
    private int _selectedIndex = -1;
    private int _highlight;
    private bool _isStacked;

    public OptionRow(Theme theme, IReadOnlyList<string> options)
    {
        _theme = theme;
        _options = [.. options];
        _spans = new (int, int)[_options.Length];

        var x = 0;
        for (var i = 0; i < _options.Length; i++)
        {
            if (i > 0)
            {
                x += Gap.Length;
            }

            var width = DisplayWidth.Of(OptionText(_options[i]));
            _spans[i] = (x, x + width);
            _stackedWidth = Math.Max(_stackedWidth, width);
            x += width;
        }

        RowWidth = x;
        Height = 1;
        Width = x;
        CanFocus = true;
    }

    /// <summary>The cells the options take on one row: each <c>( ) option</c>, two cells apart.</summary>
    public int RowWidth { get; }

    /// <summary>The rows the options take: one, or one per option while stacked.</summary>
    public int RowCount => _isStacked ? _options.Length : 1;

    /// <summary>One option per row instead of all on one, for a pane too narrow for <see cref="RowWidth"/>.</summary>
    public bool IsStacked
    {
        get => _isStacked;
        set
        {
            _isStacked = value;
            Width = value ? _stackedWidth : RowWidth;
            Height = value ? _options.Length : 1;
            SetNeedsDraw();
        }
    }

    /// <summary>Raised after Space, a click, or <see cref="PickHighlighted"/> picks an option; setting <see cref="SelectedIndex"/> does not raise it.</summary>
    public event Action? Picked;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value;
            _highlight = Math.Max(0, value);
            SetNeedsDraw();
        }
    }

    /// <summary>Picks the highlighted option, as Space does.</summary>
    public void PickHighlighted() => Pick(_highlight);

    public void ApplyTheme(Theme theme) => _theme = theme;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var normal = _theme.On(_theme.Foreground);
        for (var i = 0; i < _options.Length; i++)
        {
            if (_isStacked)
            {
                Move(0, i);
            }
            else
            {
                Move(_spans[i].Start, 0);
            }

            var dot = i == _selectedIndex ? "•" : " ";
            if (HasFocus && i == _highlight)
            {
                SetAttribute(_theme.Selected);
                AddStr($"({dot}) {_options[i]}");
                continue;
            }

            SetAttribute(normal);
            AddStr("(");
            SetAttribute(_theme.On(_theme.Accent));
            AddStr(dot);
            SetAttribute(normal);
            AddStr(") " + _options[i]);
        }

        return true;
    }

    /// <remarks>
    /// Up and down past the first or last stacked option are left unhandled, so the form moves to
    /// the field above or below as it does from a one-row option list.
    /// </remarks>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorLeft || key == Key.CursorRight)
        {
            var step = key == Key.CursorLeft ? -1 : 1;
            MoveHighlight(step);
            return true;
        }

        var isVerticalArrow = key == Key.CursorUp || key == Key.CursorDown;
        if (_isStacked && isVerticalArrow)
        {
            var step = key == Key.CursorUp ? -1 : 1;
            var target = _highlight + step;
            if (target < 0 || target >= _options.Length)
            {
                return false;
            }

            MoveHighlight(step);
            return true;
        }

        if (key == Key.Space)
        {
            PickHighlighted();
            return true;
        }

        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!mouse.IsLeftClick() || mouse.Position is not { } position)
        {
            return base.OnMouseEvent(mouse);
        }

        SetFocus();
        var index = IndexAt(position.X, position.Y);
        if (index >= 0)
        {
            Pick(index);
        }

        return true;
    }

    /// <summary>The highlight starts on the picked option each time the row gets focus.</summary>
    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView)
    {
        if (newHasFocus)
        {
            _highlight = Math.Max(0, _selectedIndex);
        }

        SetNeedsDraw();
    }

    private void MoveHighlight(int step)
    {
        _highlight = Math.Clamp(_highlight + step, 0, _options.Length - 1);
        SetNeedsDraw();
    }

    /// <summary>The option drawn at column <paramref name="x"/>, row <paramref name="y"/>, or -1 between options.</summary>
    private int IndexAt(int x, int y)
    {
        if (_isStacked)
        {
            var isOnText = y >= 0 && y < _options.Length && x < DisplayWidth.Of(OptionText(_options[y]));
            return isOnText ? y : -1;
        }

        for (var i = 0; i < _spans.Length; i++)
        {
            if (x >= _spans[i].Start && x < _spans[i].End)
            {
                return i;
            }
        }

        return -1;
    }

    private void Pick(int index)
    {
        SelectedIndex = index;
        Picked?.Invoke();
    }

    private static string OptionText(string option) => $"( ) {option}";
}

/// <summary>
/// An action in a form's row of actions, <c>⏎ Import bundle…</c>, with the <c>⏎</c> in accent;
/// drawn background-on-accent while it has focus. Enter or a click raises <see cref="Pressed"/>.
/// </summary>
internal sealed class ActionField : View, IThemedView
{
    private const string EnterGlyph = "⏎";

    private readonly string _label;
    private Theme _theme;

    public ActionField(Theme theme, string label)
    {
        _theme = theme;
        _label = label;
        Height = 1;
        Width = WidthFor(label);
        CanFocus = true;
    }

    public event Action? Pressed;

    /// <summary>The cells an action labeled <paramref name="label"/> takes: <c>⏎ </c> and the label.</summary>
    public static int WidthFor(string label) => DisplayWidth.Of(EnterGlyph) + 1 + DisplayWidth.Of(label);

    public void Press() => Pressed?.Invoke();

    public void ApplyTheme(Theme theme) => _theme = theme;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        Move(0, 0);
        if (HasFocus)
        {
            SetAttribute(_theme.Selected);
            AddStr($"{EnterGlyph} {_label}");
            return true;
        }

        SetAttribute(_theme.On(_theme.Accent, TextStyle.Bold));
        AddStr(EnterGlyph);
        SetAttribute(_theme.On(_theme.Foreground));
        AddStr(" " + _label);
        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Enter)
        {
            Press();
            return true;
        }

        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!mouse.IsLeftClick())
        {
            return base.OnMouseEvent(mouse);
        }

        SetFocus();
        Press();
        return true;
    }

    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView) => SetNeedsDraw();
}
