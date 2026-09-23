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
internal sealed class FormTextField : TextField
{
    private readonly Theme _theme;

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
/// it has focus. Space or a click toggles it.
/// </summary>
internal sealed class CheckField : View
{
    private readonly Theme _theme;
    private readonly string _label;
    private bool _isChecked;

    public CheckField(Theme theme, string label)
    {
        _theme = theme;
        _label = label;
        Height = 1;
        Width = DisplayWidth.Of(label) + 4;
        CanFocus = true;
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            _isChecked = value;
            SetNeedsDraw();
        }
    }

    public void Toggle() => IsChecked = !IsChecked;

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
/// A row of radio options, <c>( ) default  (•) machine  ( ) user</c>, with the dot in accent. With
/// focus, left and right move a highlight drawn background-on-accent and Space picks the
/// highlighted option; a click picks the option under it. <see cref="SelectedIndex"/> is -1 when
/// none is picked, for a stored value that matches no option.
/// </summary>
internal sealed class OptionRow : View
{
    private const string Gap = "  ";

    private readonly Theme _theme;
    private readonly string[] _options;
    private readonly (int Start, int End)[] _spans;
    private int _selectedIndex = -1;
    private int _highlight;

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

            var width = DisplayWidth.Of(OptionText(i));
            _spans[i] = (x, x + width);
            x += width;
        }

        Height = 1;
        Width = x;
        CanFocus = true;
    }

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
    public void PickHighlighted() => SelectedIndex = _highlight;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var normal = _theme.On(_theme.Foreground);
        for (var i = 0; i < _options.Length; i++)
        {
            Move(_spans[i].Start, 0);
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

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorLeft || key == Key.CursorRight)
        {
            var step = key == Key.CursorLeft ? -1 : 1;
            _highlight = Math.Clamp(_highlight + step, 0, _options.Length - 1);
            SetNeedsDraw();
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
        for (var i = 0; i < _spans.Length; i++)
        {
            if (position.X >= _spans[i].Start && position.X < _spans[i].End)
            {
                SelectedIndex = i;
                break;
            }
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

    private string OptionText(int index) => $"( ) {_options[index]}";
}
