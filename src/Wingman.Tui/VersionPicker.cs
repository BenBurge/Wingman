using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>
/// The <c>Upgrade to version…</c> and <c>Install version…</c> overlay: a bordered box like the
/// <see cref="ContextMenu"/>, with the package name, then <c>loading…</c> until the shell hands it
/// winget's versions, newest first, the installed one marked. Up and Down select, Enter or a click
/// picks, Esc closes; like the menu it never takes focus, and the shell forwards every key to
/// <see cref="HandleKey"/> while it is open.
/// </summary>
internal sealed class VersionPicker : View, IThemedView
{
    private const int MinWidth = 30;
    private const int MaxVisibleVersions = 10;
    private const int WheelStep = 3;
    private const string InstalledNote = "  installed";

    // The top border, the title, and the rule under it.
    private const int EntriesTop = 3;

    private Theme _theme;
    private string _title = "";
    private string? _installedVersion;
    private IReadOnlyList<string>? _versions;
    private int _selected;
    private int _offset;
    private Point _anchor;
    private Rectangle _bounds;
    private Action<string>? _onPicked;

    public VersionPicker(Theme theme)
    {
        _theme = theme;
        CanFocus = false;
        Visible = false;
    }

    /// <summary>
    /// Shows the box titled <paramref name="title"/> with <c>loading…</c>, its corner at
    /// <paramref name="position"/> and kept inside <paramref name="bounds"/>, both in the superview's
    /// viewport coordinates. <paramref name="onPicked"/> runs with the version picked, after the box closes.
    /// </summary>
    public void Open(string title, string? installedVersion, Point position, Rectangle bounds, Action<string> onPicked)
    {
        _title = title;
        _installedVersion = installedVersion;
        _versions = null;
        _selected = 0;
        _offset = 0;
        _anchor = position;
        _bounds = bounds;
        _onPicked = onPicked;
        Place();
        Visible = true;
        SetNeedsDraw();
    }

    /// <summary>Replaces <c>loading…</c> with <paramref name="versions"/>, newest first, and resizes the box to fit them.</summary>
    public void ShowVersions(IReadOnlyList<string> versions)
    {
        _versions = versions;
        _selected = 0;
        _offset = 0;
        Place();
        SetNeedsDraw();
    }

    public void Close()
    {
        Visible = false;
        _onPicked = null;
    }

    /// <summary>Up, Down, Page Up, Page Down, Home, and End move the selection, Enter picks, Esc closes; every other key is ignored.</summary>
    public void HandleKey(Key key)
    {
        if (key == Key.Esc)
        {
            Close();
        }
        else if (key == Key.Enter)
        {
            Pick(_selected);
        }
        else if (key == Key.CursorDown)
        {
            Select(_selected + 1);
        }
        else if (key == Key.CursorUp)
        {
            Select(_selected - 1);
        }
        else if (key == Key.PageDown)
        {
            Select(_selected + VisibleLines());
        }
        else if (key == Key.PageUp)
        {
            Select(_selected - VisibleLines());
        }
        else if (key == Key.Home || key == Key.Home.WithCtrl)
        {
            Select(0);
        }
        else if (key == Key.End || key == Key.End.WithCtrl)
        {
            Select(int.MaxValue);
        }
    }

    /// <summary>Draws the box. The shell calls this from the window's <c>DrawComplete</c>, for the reason <see cref="ContextMenu.Paint"/> gives.</summary>
    public void Paint()
    {
        var width = Viewport.Width;
        var innerWidth = width - 2;
        var border = _theme.On(_theme.Border);

        DrawEdge(0, "┌", "┐", "", border);
        DrawBoxedLine(1, CellText.Fit(_title, innerWidth - 2), "", _theme.On(_theme.Foreground, TextStyle.Bold), isSelected: false, border);
        DrawEdge(2, "├", "┤", "", border);

        var lines = VisibleLines();
        if (_versions is not { Count: > 0 } versions)
        {
            var text = _versions is null ? "loading…" : "no versions found";
            DrawBoxedLine(EntriesTop, text, "", _theme.On(_theme.Dim), isSelected: false, border);
        }
        else
        {
            for (var line = 0; line < lines; line++)
            {
                var index = _offset + line;
                var isInstalled = versions[index] == _installedVersion;
                var isSelected = index == _selected;
                var color = isSelected ? _theme.Selected : _theme.On(_theme.Foreground);
                DrawBoxedLine(EntriesTop + line, versions[index], isInstalled ? InstalledNote : "", color, isSelected, border);
            }
        }

        var count = _versions switch
        {
            { Count: 1 } => " 1 version ",
            { Count: > 1 } => $" {_versions.Count} versions ",
            _ => "",
        };
        DrawEdge(EntriesTop + lines, "└", "┘", count, border);
    }

    public void ApplyTheme(Theme theme) => _theme = theme;

    /// <summary>Draws nothing; see <see cref="Paint"/>.</summary>
    protected override bool OnDrawingContent(DrawContext? context) => true;

    /// <summary>A click on a version picks it, hovering selects it, and the wheel scrolls. Every event inside the box stops here.</summary>
    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Position is not { } position || _versions is not { Count: > 0 } versions)
        {
            return true;
        }

        if (mouse.IsWheel)
        {
            var step = mouse.Flags.HasFlag(MouseFlags.WheeledDown) ? WheelStep : -WheelStep;
            _offset = Math.Clamp(_offset + step, 0, Math.Max(0, versions.Count - VisibleLines()));
            _selected = Math.Clamp(_selected, _offset, _offset + VisibleLines() - 1);
            SetNeedsDraw();
            return true;
        }

        var line = position.Y - EntriesTop;
        if (line < 0 || line >= VisibleLines())
        {
            return true;
        }

        var index = _offset + line;
        if (mouse.IsLeftClick())
        {
            Pick(index);
        }
        else if (index != _selected)
        {
            _selected = index;
            SetNeedsDraw();
        }

        return true;
    }

    /// <summary>Lines between the rule and the bottom border: one for <c>loading…</c> or an empty list, else up to ten versions, fewer when the content area is short.</summary>
    private int VisibleLines()
    {
        if (_versions is not { Count: > 0 } versions)
        {
            return 1;
        }

        var room = Math.Max(1, _bounds.Height - EntriesTop - 1);
        return Math.Min(Math.Min(versions.Count, MaxVisibleVersions), room);
    }

    /// <summary>Sizes the box to its lines and moves it up or left as needed to stay inside the bounds it opened in.</summary>
    private void Place()
    {
        var labelWidth = DisplayWidth.Of(_title);
        foreach (var version in _versions ?? [])
        {
            labelWidth = Math.Max(labelWidth, DisplayWidth.Of(version + InstalledNote));
        }

        var width = Math.Min(Math.Max(MinWidth, labelWidth + 4), _bounds.Width);
        var height = Math.Min(EntriesTop + VisibleLines() + 1, _bounds.Height);
        X = Math.Clamp(_anchor.X, _bounds.Left, Math.Max(_bounds.Left, _bounds.Right - width));
        Y = Math.Clamp(_anchor.Y, _bounds.Top, Math.Max(_bounds.Top, _bounds.Bottom - height));
        Width = width;
        Height = height;
    }

    private void Select(int index)
    {
        if (_versions is not { Count: > 0 } versions)
        {
            return;
        }

        _selected = Math.Clamp(index, 0, versions.Count - 1);
        var lines = VisibleLines();
        if (_selected < _offset)
        {
            _offset = _selected;
        }
        else if (_selected >= _offset + lines)
        {
            _offset = _selected - lines + 1;
        }

        SetNeedsDraw();
    }

    /// <summary>Closes the box before running the pick, so the question it asks is not hidden behind the box.</summary>
    private void Pick(int index)
    {
        if (_versions is not { Count: > 0 } versions || index < 0 || index >= versions.Count)
        {
            return;
        }

        var onPicked = _onPicked;
        Close();
        onPicked?.Invoke(versions[index]);
    }

    /// <summary>Draws a border row, with <paramref name="label"/> after its first <c>─</c> when there is one.</summary>
    private void DrawEdge(int y, string left, string right, string label, Attribute color)
    {
        var innerWidth = Viewport.Width - 2;
        var middle = label.Length == 0 || DisplayWidth.Of(label) + 2 > innerWidth
            ? new string('─', innerWidth)
            : "─" + label + new string('─', innerWidth - 1 - DisplayWidth.Of(label));
        SetAttribute(color);
        Move(0, y);
        AddStr(left + middle + right);
    }

    /// <summary>Draws <c>│ text note │</c>, the text and its padding in <paramref name="color"/>, the note dim unless selected, and the sides in <paramref name="border"/>.</summary>
    private void DrawBoxedLine(int y, string text, string note, Attribute color, bool isSelected, Attribute border)
    {
        var innerWidth = Viewport.Width - 2;
        SetAttribute(border);
        Move(0, y);
        AddStr("│");
        SetAttribute(color);
        AddStr(" " + text);
        SetAttribute(isSelected ? color : _theme.On(_theme.Dim));
        AddStr(note);
        SetAttribute(color);
        var padding = Math.Max(0, innerWidth - 1 - DisplayWidth.Of(text) - DisplayWidth.Of(note));
        AddStr(new string(' ', padding));
        SetAttribute(border);
        AddStr("│");
    }
}
