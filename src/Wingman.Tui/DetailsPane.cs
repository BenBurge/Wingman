using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Models;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>
/// The right-hand pane of every list tab. It draws the cursor row's own fields at once, then the
/// rest from <c>winget show</c>, which it asks for on a background task once the cursor has rested
/// for a moment and keeps in a cache shared for the session. With focus, the arrow and paging keys
/// scroll the release notes or description; the mouse wheel scrolls them whenever it is over the pane.
/// </summary>
internal sealed class DetailsPane : View
{
    private const int LabelWidth = 11;
    private const int WheelStep = 3;
    private const string LoadingText = "loading…";
    private const string NoValue = "—";

    // Long enough that holding an arrow key down never starts a winget process per row.
    private static readonly TimeSpan FetchDelay = TimeSpan.FromMilliseconds(150);

    private readonly Theme _theme;
    private readonly IApplication _app;
    private readonly IWingetClient _client;
    private readonly Dictionary<string, PackageDetails?> _cache;
    private readonly HashSet<string> _fetching = new(StringComparer.OrdinalIgnoreCase);

    private PackageRow? _row;
    private string? _error;
    private object? _fetchTimer;

    private int _textScroll;
    private int _textRowsShown;
    private string? _wrappedSource;
    private int _wrappedWidth;
    private List<string> _wrappedLines = [];

    /// <param name="cache">
    /// Results of <c>ShowAsync</c> by Id, null where winget found nothing. Shared by every pane and
    /// only touched on the UI thread.
    /// </param>
    public DetailsPane(Theme theme, IApplication app, IWingetClient client, Dictionary<string, PackageDetails?> cache)
    {
        _theme = theme;
        _app = app;
        _client = client;
        _cache = cache;
        CanFocus = true;
    }

    /// <summary>The <c>Policy</c> row's text for a row, such as <c>hold (blocking)</c>, or null to show <c>—</c>; <c>—</c> for every row when unset.</summary>
    public Func<PackageRow, string?>? PolicyFor { get; set; }

    /// <summary>Whether a package Id has saved install options, for the <c>Options</c> row; none has when unset.</summary>
    public Func<string, bool>? HasCustomOptions { get; set; }

    /// <summary>Shows <paramref name="row"/>, or nothing when it is null. Call on every cursor move.</summary>
    public void Show(PackageRow? row)
    {
        if (Equals(row, _row))
        {
            return;
        }

        var isSamePackage = row is not null && IsCurrent(row.Id);
        _row = row;
        if (!isSamePackage)
        {
            _error = null;
            _textScroll = 0;
        }

        SetNeedsDraw();
        RestartFetchTimer();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (_row is not { } row)
        {
            return true;
        }

        var width = Viewport.Width;
        var valueWidth = width - 1 - LabelWidth - 1;
        var hasResult = _cache.TryGetValue(row.Id, out var details);
        var isLoading = !hasResult && _error is null;

        var normal = _theme.On(_theme.Foreground);
        var dim = _theme.On(_theme.Dim);

        (string Text, Attribute Color) FromDetails(string? value)
        {
            if (isLoading)
            {
                return (LoadingText, dim);
            }

            return string.IsNullOrEmpty(value) ? (NoValue, dim) : (value, normal);
        }

        (string Text, Attribute Color) FromRow(string? value, Attribute color) =>
            string.IsNullOrEmpty(value) ? (NoValue, dim) : (value, color);

        var name = string.IsNullOrEmpty(details?.Name) ? row.Name : details.Name;
        DrawText(0, " " + CellText.Fit(name, width - 2), _theme.On(_theme.Foreground, TextStyle.Bold));

        string? scope = null;
        details?.AdditionalFields.TryGetValue("Installer.Scope", out scope);
        var hasCustomOptions = HasCustomOptions?.Invoke(row.Id) ?? false;
        var options = hasCustomOptions ? ("custom (o to edit)", _theme.On(_theme.Accent)) : ("default", dim);

        (string Label, (string Text, Attribute Color) Value)[] fields =
        [
            ("Id", FromRow(row.Id, normal)),
            ("Version", FromRow(row.Version, normal)),
            ("Available", FromRow(row.AvailableVersion, _theme.On(_theme.Ok))),
            ("Publisher", FromDetails(details?.Publisher)),
            ("License", FromDetails(details?.License)),
            ("Homepage", FromDetails(details?.Homepage)),
            ("Source", FromRow(row.Source, normal)),
            ("Scope", FromDetails(scope)),
            ("Policy", FromRow(PolicyFor?.Invoke(row), normal)),
            ("Options", options),
        ];

        var y = 2;
        foreach (var (label, value) in fields)
        {
            DrawText(y, " " + label.PadRight(LabelWidth), normal);
            SetAttribute(value.Color);
            AddStr(CellText.Fit(value.Text, valueWidth));
            y++;
        }

        y++;
        if (isLoading)
        {
            return true;
        }

        if (_error is not null)
        {
            DrawBody(y, $"winget: {_error}", _theme.On(_theme.Error));
        }
        else if (details is null)
        {
            DrawText(y, " " + CellText.Fit("No details from winget", width - 2), dim);
        }
        else
        {
            var hasReleaseNotes = details.ReleaseNotes.Length > 0;
            DrawText(y, hasReleaseNotes ? " Release notes" : " Description", _theme.On(_theme.Header));
            DrawBody(y + 1, hasReleaseNotes ? details.ReleaseNotes : details.Description, normal);
        }

        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        int? scrollTo = null;
        if (key == Key.CursorDown)
        {
            scrollTo = _textScroll + 1;
        }
        else if (key == Key.CursorUp)
        {
            scrollTo = _textScroll - 1;
        }
        else if (key == Key.PageDown)
        {
            scrollTo = _textScroll + Math.Max(1, _textRowsShown - 1);
        }
        else if (key == Key.PageUp)
        {
            scrollTo = _textScroll - Math.Max(1, _textRowsShown - 1);
        }
        else if (key == Key.Home)
        {
            scrollTo = 0;
        }
        else if (key == Key.End)
        {
            scrollTo = int.MaxValue;
        }

        // Left and right do nothing here, but left unhandled they would move focus out of the pane.
        var isSidewaysKey = key == Key.CursorLeft || key == Key.CursorRight;
        if (scrollTo is { } target)
        {
            ScrollTextTo(target);
            return true;
        }

        return isSidewaysKey || base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            ScrollTextTo(_textScroll + WheelStep);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            ScrollTextTo(_textScroll - WheelStep);
            return true;
        }

        return base.OnMouseEvent(mouse);
    }

    private bool IsCurrent(string id) => string.Equals(_row?.Id, id, StringComparison.OrdinalIgnoreCase);

    private void RestartFetchTimer()
    {
        if (_fetchTimer is not null)
        {
            _app.RemoveTimeout(_fetchTimer);
            _fetchTimer = null;
        }

        if (_row is null || _cache.ContainsKey(_row.Id))
        {
            return;
        }

        _fetchTimer = _app.AddTimeout(FetchDelay, () =>
        {
            _fetchTimer = null;
            FetchCurrent();
            return false;
        });
    }

    private void FetchCurrent()
    {
        if (_row is not { } row || _cache.ContainsKey(row.Id) || !_fetching.Add(row.Id))
        {
            return;
        }

        var id = row.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                var details = await _client.ShowAsync(id, CancellationToken.None);
                _app.Invoke(() => OnFetched(id, details));
            }
            catch (Exception ex)
            {
                _app.Invoke(() => OnFetchFailed(id, ex.Message));
            }
        });
    }

    private void OnFetched(string id, PackageDetails? details)
    {
        _fetching.Remove(id);
        _cache[id] = details;
        if (IsCurrent(id))
        {
            SetNeedsDraw();
        }
    }

    private void OnFetchFailed(string id, string message)
    {
        // Not cached, so coming back to the row asks winget again.
        _fetching.Remove(id);
        if (IsCurrent(id))
        {
            _error = message;
            SetNeedsDraw();
        }
    }

    private void DrawText(int y, string text, Attribute color)
    {
        SetAttribute(color);
        Move(0, y);
        AddStr(text);
    }

    /// <summary>Draws <paramref name="text"/> word-wrapped from row <paramref name="top"/> down, scrolled by <see cref="_textScroll"/>.</summary>
    private void DrawBody(int top, string text, Attribute color)
    {
        var textWidth = Math.Max(1, Viewport.Width - 2);
        if (!ReferenceEquals(text, _wrappedSource) || textWidth != _wrappedWidth)
        {
            _wrappedSource = text;
            _wrappedWidth = textWidth;
            _wrappedLines = CellText.Wrap(text, textWidth);
        }

        _textRowsShown = Math.Max(0, Viewport.Height - top);
        _textScroll = Math.Clamp(_textScroll, 0, MaxTextScroll());

        SetAttribute(color);
        for (var row = 0; row < _textRowsShown; row++)
        {
            var index = _textScroll + row;
            if (index >= _wrappedLines.Count)
            {
                break;
            }

            Move(1, top + row);
            AddStr(_wrappedLines[index]);
        }
    }

    private int MaxTextScroll() => Math.Max(0, _wrappedLines.Count - _textRowsShown);

    private void ScrollTextTo(int target)
    {
        var clamped = Math.Clamp(target, 0, MaxTextScroll());
        if (clamped != _textScroll)
        {
            _textScroll = clamped;
            SetNeedsDraw();
        }
    }
}
