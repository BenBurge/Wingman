using System.Globalization;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Updates;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>
/// Takes a list tab's whole content area while a batch runs and after it ends: a title with the
/// batch's progress and the elevated helper's state, one row per operation with its status, then
/// the log of the running operation, or of the selected one once the batch is over. While running,
/// the arrows scroll the log and Esc asks to cancel; afterwards the arrows select an operation,
/// <c>l</c> gives the log the whole screen, and Enter or Esc go back. A selected operation that
/// failed shows its decoded exit code in place of the log, with keys to retry it as it was,
/// interactive, or skipping the hash check, and to hold or exclude the package. The mouse wheel
/// scrolls the log and a click selects a finished batch's operation.
/// </summary>
/// <remarks>
/// <see cref="Apply"/> takes events in whatever order they come: a line that arrives after its
/// operation finished still joins its log, a state never moves backward, and an event for an
/// index outside the batch is ignored.
/// </remarks>
internal sealed class BatchRunnerScreen : View, IThemedView
{
    private const int IdWidth = 22;
    private const int ActionWidth = 30;
    private const int DoneWidth = 12;
    private const int BarWidth = 20;

    // The title, the blank row under it, the blank row under the operations, the log rule, and the saved-log row.
    private const int FixedRows = 5;
    private const int MinLogRows = 4;
    private const int WheelStep = 3;

    // " winget said     …": a space, the label padded to 15, and a space.
    private const int FailureLabelWidth = 15;
    private const int FailureValueLeft = 1 + FailureLabelWidth + 1;
    private const int FailureLogLines = 5;

    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly IApplication _app;
    private Theme _theme;
    private readonly OperationView[] _operations;
    private readonly KeyHint[] _runningHints;
    private readonly KeyHint[] _finishedHints;
    private readonly KeyHint[] _failureHints;

    private string _elevation;
    private bool _isFinished;
    private bool _isCancelRequested;
    private bool _isFullLog;

    // The operation running now, or -1; the log follows the last one that started while none is.
    private int _runningIndex = -1;
    private int _lastStartedIndex;
    private int _selected;

    private int _operationScroll;
    private int _operationRowsTop;
    private int _operationRowsShown;
    private int _logScroll;
    private int _logRowsShown;

    // True while the log shows its newest line, so new lines keep it at the bottom.
    private bool _isFollowing = true;

    private object? _spinnerTimer;
    private int _spinnerFrame;

    /// <param name="elevation">What the title shows for the elevated helper until the batch reports on it.</param>
    public BatchRunnerScreen(IApplication app, Theme theme, IReadOnlyList<QueuedOperation> operations, string elevation)
    {
        _app = app;
        _theme = theme;
        _elevation = elevation;
        _operations = [.. operations.Select(operation => new OperationView(operation))];
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        _runningHints =
        [
            new(Key.Esc, "Cancel remaining", () => CancelRequested?.Invoke()),
            new(Key.CursorUp, "Scroll log", () => SetFocus(), "↑↓"),
        ];
        _finishedHints =
        [
            new(Key.Enter, "Back", () => BackRequested?.Invoke(), "⏎"),
            new(Key.CursorUp, "Select", () => SetFocus(), "↑↓"),
            new(Key.L, "Full log", ToggleFullLog),
        ];
        _failureHints =
        [
            .. LetterHints('R', "Retry", () => Retry(request => request)),
            .. LetterHints('I', "Interactive", () => Retry(request => request with { Interactive = true })),
            .. LetterHints('S', "Skip hash check", () => Retry(request => request with { SkipHashCheck = true })),
            .. LetterHints('H', "Hold", () => RequestPolicy(UpdatePolicyKind.Hold)),
            .. LetterHints('E', "Exclude", () => RequestPolicy(UpdatePolicyKind.Exclude)),
            new(Key.L, "Log", ToggleFullLog),
            new(Key.Enter, "Back", () => BackRequested?.Invoke(), "⏎"),
        ];

        _spinnerTimer = app.AddTimeout(TimeSpan.FromMilliseconds(100), AdvanceSpinner);
    }

    /// <summary>The screen's keys in the help overlay.</summary>
    public static HelpGroup Help { get; } = new("Batch",
    [
        new("Esc", "cancel remaining"),
        new("↑↓", "scroll log; select when done"),
        new("PgUp", "scroll log"),
        new("l", "full log"),
        new("⏎", "back when done"),
        new("R", "retry a failed one"),
        new("I", "retry interactive"),
        new("S", "retry skip hash check"),
        new("H", "hold at installed"),
        new("E", "exclude from updates"),
    ]);

    /// <summary>Raised on Esc while the batch runs.</summary>
    public event Action? CancelRequested;

    /// <summary>Raised on Enter or Esc once the batch is over.</summary>
    public event Action? BackRequested;

    /// <summary>Raised on <c>R</c>, <c>I</c>, or <c>S</c> with a failed operation selected, with that operation changed as the key asks.</summary>
    public event Action<QueuedOperation>? RetryRequested;

    /// <summary>Raised on <c>H</c> or <c>E</c> with a failed operation selected, with its package and the policy to give it.</summary>
    public event Action<PackageRow, UpdatePolicyKind>? PolicyRequested;

    /// <summary>Raised when <see cref="Hints"/> changes without the batch changing state, as when a failed operation is selected.</summary>
    public event Action? HintsChanged;

    public bool IsFinished => _isFinished;

    /// <summary>The key bar entries for the batch's state, before the shell's <c>q Quit</c>.</summary>
    public IReadOnlyList<KeyHint> Hints
    {
        get
        {
            if (!_isFinished)
            {
                return _runningHints;
            }

            return SelectedFailure is null ? _finishedHints : _failureHints;
        }
    }

    public void Apply(BatchProgress progress)
    {
        switch (progress)
        {
            case OperationStarted started when IsKnown(started.Index):
                if (_operations[started.Index].State == OperationState.Waiting)
                {
                    _operations[started.Index].State = OperationState.Running;
                    _runningIndex = started.Index;
                    _lastStartedIndex = started.Index;
                    ResetLogScroll();
                }

                break;

            case OperationLine line when IsKnown(line.Index):
                _operations[line.Index].AddLine(line.Text);
                break;

            case OperationFinished finished when IsKnown(finished.Index):
                Resolve(finished.Index, FinishedState(finished), finished.Result);
                break;

            case OperationCanceled canceled when IsKnown(canceled.Index):
                Resolve(canceled.Index, OperationState.Canceled, null);
                break;

            case ElevationState elevation:
                _elevation = elevation.State;
                break;
        }

        SetNeedsDraw();
    }

    /// <summary>Marks a cancel the user asked for, so an operation it stopped shows as canceled rather than failed.</summary>
    public void MarkCancelRequested() => _isCancelRequested = true;

    /// <summary>
    /// Ends the batch: any operation still open is shown canceled, the failed operation is selected
    /// when exactly one failed and the first one otherwise, and each operation's history log name,
    /// or null, is shown under its log.
    /// </summary>
    public void Finish(IReadOnlyList<string?> logFiles)
    {
        _isFinished = true;
        _runningIndex = -1;
        for (var i = 0; i < _operations.Length; i++)
        {
            var operation = _operations[i];
            if (operation.State is OperationState.Waiting or OperationState.Running)
            {
                operation.State = OperationState.Canceled;
            }

            if (i < logFiles.Count)
            {
                operation.LogFile = logFiles[i];
            }
        }

        var failed = Array.FindAll(_operations, operation => operation.State == OperationState.Failed);
        _selected = failed.Length == 1 ? Array.IndexOf(_operations, failed[0]) : 0;
        ResetLogScroll();
        StopSpinner();
        SetNeedsDraw();
    }

    public void ToggleFullLog()
    {
        _isFullLog = !_isFullLog;
        SetNeedsDraw();
    }

    /// <summary>The percentage a progress line shows: its last <c>62%</c> token, or else the filled share of a bar of <c>█</c> and <c>░</c> or <c>▒</c>; null when it shows neither.</summary>
    internal static int? PercentIn(string line)
    {
        for (var i = line.Length - 1; i > 0; i--)
        {
            if (line[i] != '%')
            {
                continue;
            }

            var start = i;
            while (start > 0 && char.IsAsciiDigit(line[start - 1]))
            {
                start--;
            }

            var digits = i - start;
            if (digits is >= 1 and <= 3)
            {
                return Math.Min(100, int.Parse(line.AsSpan(start, digits), CultureInfo.InvariantCulture));
            }
        }

        var (filled, empty) = BarCells(line);
        return filled + empty == 0 ? null : filled * 100 / (filled + empty);
    }

    public void ApplyTheme(Theme theme) => _theme = theme;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var height = Viewport.Height;
        var y = 0;
        _operationRowsShown = 0;

        if (!_isFullLog)
        {
            DrawTitle(width);
            _operationRowsTop = 2;
            _operationRowsShown = Math.Min(_operations.Length, Math.Max(1, height - FixedRows - MinLogRows));
            KeepOperationVisible(_isFinished ? _selected : DisplayedIndex);
            for (var row = 0; row < _operationRowsShown; row++)
            {
                DrawOperation(_operationRowsTop + row, _operationScroll + row, width);
            }

            y = _operationRowsTop + _operationRowsShown + 1;
        }

        var savedRow = height - 1;
        if (!_isFullLog && SelectedFailure is { } failure)
        {
            _logRowsShown = 0;
            DrawFailure(y, savedRow, width, failure);
        }
        else
        {
            DrawRule(y, width);
            _logRowsShown = Math.Max(0, savedRow - y - 1);
            DrawLog(y + 1, width);
        }

        DrawSavedLine(savedRow, width);
        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            if (_isFinished)
            {
                BackRequested?.Invoke();
            }
            else
            {
                CancelRequested?.Invoke();
            }

            return true;
        }

        if (key == Key.Enter)
        {
            if (_isFinished)
            {
                BackRequested?.Invoke();
            }

            return true;
        }

        if (key == Key.L)
        {
            ToggleFullLog();
            return true;
        }

        if (key == Key.CursorUp || key == Key.CursorDown)
        {
            var step = key == Key.CursorUp ? -1 : 1;
            if (_isFinished)
            {
                Select(_selected + step);
            }
            else
            {
                ScrollLog(_logScroll + step);
            }

            return true;
        }

        int? scrollTo = null;
        if (key == Key.PageUp)
        {
            scrollTo = _logScroll - Math.Max(1, _logRowsShown - 1);
        }
        else if (key == Key.PageDown)
        {
            scrollTo = _logScroll + Math.Max(1, _logRowsShown - 1);
        }
        else if (key == Key.Home)
        {
            scrollTo = 0;
        }
        else if (key == Key.End)
        {
            scrollTo = int.MaxValue;
        }

        if (scrollTo is { } target)
        {
            ScrollLog(target);
            return true;
        }

        // Left and right do nothing here, but left unhandled they would move focus off the screen.
        var isSidewaysKey = key == Key.CursorLeft || key == Key.CursorRight;
        return isSidewaysKey || base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            ScrollLog(_logScroll + WheelStep);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            ScrollLog(_logScroll - WheelStep);
            return true;
        }

        if (mouse.IsLeftClick() && mouse.Position is { } position)
        {
            SetFocus();
            var row = position.Y - _operationRowsTop;
            if (_isFinished && row >= 0 && row < _operationRowsShown)
            {
                Select(_operationScroll + row);
            }

            return true;
        }

        return base.OnMouseEvent(mouse);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopSpinner();
        }

        base.Dispose(disposing);
    }

    private int DisplayedIndex
    {
        get
        {
            if (_isFinished)
            {
                return _selected;
            }

            return _runningIndex >= 0 ? _runningIndex : _lastStartedIndex;
        }
    }

    private bool IsKnown(int index) => index >= 0 && index < _operations.Length;

    /// <summary>The selected operation once the batch is over, when it failed; null otherwise.</summary>
    private OperationView? SelectedFailure
    {
        get
        {
            var isFailure = _isFinished && IsKnown(_selected) && _operations[_selected].State == OperationState.Failed;
            return isFailure ? _operations[_selected] : null;
        }
    }

    /// <summary>
    /// A hint for the lowercase letter, shown with <paramref name="letter"/> in uppercase as the
    /// mockup draws it, and another off the bar so the letter works with Shift too.
    /// </summary>
    private static KeyHint[] LetterHints(char letter, string label, Action action) =>
    [
        new(new Key(char.ToLowerInvariant(letter)), label, action, letter.ToString()),
        new(new Key(letter), label, action, IsOnBar: false),
    ];

    private void Retry(Func<OperationRequest, OperationRequest> change)
    {
        if (SelectedFailure is not { } failure)
        {
            return;
        }

        var operation = failure.Operation;
        var plan = operation.Plan with { Request = change(operation.Plan.Request) };
        RetryRequested?.Invoke(operation with { Plan = plan });
    }

    private void RequestPolicy(UpdatePolicyKind kind)
    {
        if (SelectedFailure is { } failure)
        {
            PolicyRequested?.Invoke(failure.Operation.Row, kind);
        }
    }

    private OperationState FinishedState(OperationFinished finished)
    {
        if (finished.Skipped)
        {
            return OperationState.Skipped;
        }

        if (finished.Result.Succeeded)
        {
            return OperationState.Done;
        }

        return _isCancelRequested ? OperationState.Canceled : OperationState.Failed;
    }

    private void Resolve(int index, OperationState state, OperationResult? result)
    {
        var operation = _operations[index];
        if (operation.State is not (OperationState.Waiting or OperationState.Running))
        {
            return;
        }

        operation.State = state;
        operation.Result = result;
        if (_runningIndex == index)
        {
            _runningIndex = -1;
        }
    }

    private void DrawTitle(int width)
    {
        var dim = _theme.On(_theme.Dim);
        List<(string Text, Attribute Color)> left;
        if (_isFinished)
        {
            left =
            [
                (" Batch finished", _theme.On(_theme.Foreground, TextStyle.Bold)),
                ($"  {Count(OperationState.Done, OperationState.Failed, OperationState.Skipped)} of {_operations.Length}", dim),
            ];
            AddCountSuffix(left, OperationState.Failed, "failed", _theme.On(_theme.Error));
            AddCountSuffix(left, OperationState.Skipped, "skipped", dim);
            AddCountSuffix(left, OperationState.Canceled, "canceled", dim);
        }
        else
        {
            var position = Math.Clamp(_operations.Length - Count(OperationState.Waiting), 1, _operations.Length);
            left =
            [
                (" Running batch", _theme.On(_theme.Foreground, TextStyle.Bold)),
                ($"  {position} of {_operations.Length}", dim),
            ];
        }

        List<(string Text, Attribute Color)> right =
        [
            ("elevated helper: ", _theme.On(_theme.Foreground)),
            (_elevation, _theme.On(ElevationColor())),
            (" ", _theme.On(_theme.Foreground)),
        ];

        Move(0, 0);
        DrawSegments(left);
        var leftWidth = SegmentsWidth(left);
        var rightWidth = SegmentsWidth(right);
        if (leftWidth + 1 + rightWidth <= width)
        {
            Move(width - rightWidth, 0);
            DrawSegments(right);
        }
    }

    private void AddCountSuffix(List<(string Text, Attribute Color)> segments, OperationState state, string label, Attribute color)
    {
        var count = Count(state);
        if (count > 0)
        {
            segments.Add((" · ", _theme.On(_theme.Dim)));
            segments.Add(($"{count} {label}", color));
        }
    }

    private Color ElevationColor()
    {
        if (_elevation == "connected")
        {
            return _theme.Ok;
        }

        if (_elevation == "declined" || _elevation.StartsWith("failed", StringComparison.Ordinal))
        {
            return _theme.Error;
        }

        if (_elevation == "requesting")
        {
            return _theme.Foreground;
        }

        return _theme.Dim;
    }

    /// <summary><c> ✓ Git.Git   upgrade  2.51.0 → 2.52.0   done  14.0 s</c>, all background-on-accent when selected.</summary>
    private void DrawOperation(int y, int index, int width)
    {
        var operation = _operations[index];
        var isSelected = _isFinished && index == _selected;
        Attribute ColorOf(Color color) => isSelected ? _theme.Selected : _theme.On(color);

        if (isSelected)
        {
            SetAttribute(_theme.Selected);
            Move(0, y);
            AddStr(new string(' ', width));
        }

        var (glyph, glyphColor) = operation.State switch
        {
            OperationState.Done => ("✓", _theme.Ok),
            OperationState.Running => ("▶", _theme.Accent),
            OperationState.Failed => ("✗", _theme.Error),
            OperationState.Waiting => ("·", _theme.Dim),
            _ => ("○", _theme.Dim),
        };

        List<(string Text, Attribute Color)> segments =
        [
            (" ", ColorOf(_theme.Foreground)),
            (glyph, ColorOf(glyphColor)),
            (" " + Column(operation.Operation.Row.Id, IdWidth) + Column(ActionText(operation.Operation), ActionWidth), ColorOf(_theme.Foreground)),
        ];
        foreach (var (text, color) in StatusSegments(operation))
        {
            segments.Add((text, ColorOf(color)));
        }

        Move(0, y);
        DrawSegments(segments);
    }

    private List<(string Text, Color Color)> StatusSegments(OperationView operation)
    {
        switch (operation.State)
        {
            case OperationState.Done:
                var duration = operation.Result?.Duration ?? TimeSpan.Zero;
                return [("done".PadRight(DoneWidth), _theme.Ok), (Seconds(duration), _theme.Dim)];

            case OperationState.Running when operation.Percent is { } percent:
                var filled = (int)Math.Round(BarWidth * percent / 100.0);
                return
                [
                    (new string('█', filled), _theme.Accent),
                    (new string('░', BarWidth - filled), _theme.Dim),
                    ($"  {percent}%", _theme.Foreground),
                ];

            case OperationState.Running:
                return [(SpinnerFrames[_spinnerFrame], _theme.Accent)];

            case OperationState.Waiting:
                return [("waiting", _theme.Dim)];

            case OperationState.Failed:
                var exitCode = operation.Result is { } result ? WingetErrorCodes.Format(result.ExitCode) : "";
                return [($"failed  exit {exitCode}", _theme.Error)];

            case OperationState.Skipped:
                return [("skipped", _theme.Dim)];

            default:
                return [("canceled", _theme.Dim)];
        }
    }

    /// <summary><c> ─ Log · GitHub.cli ────…</c> across the screen in the border color.</summary>
    private void DrawRule(int y, int width)
    {
        var id = _operations.Length > 0 ? _operations[DisplayedIndex].Operation.Row.Id : "";
        var label = CellText.Fit($" ─ Log · {id} ", Math.Max(0, width - 1));
        var dashes = Math.Max(0, width - 1 - DisplayWidth.Of(label));
        SetAttribute(_theme.On(_theme.Border));
        Move(0, y);
        AddStr(label + new string('─', dashes));
    }

    private void DrawLog(int top, int width)
    {
        if (_operations.Length == 0)
        {
            return;
        }

        var lines = _operations[DisplayedIndex].Lines;
        var maxScroll = Math.Max(0, lines.Count - _logRowsShown);
        _logScroll = _isFollowing ? maxScroll : Math.Clamp(_logScroll, 0, maxScroll);

        var textWidth = Math.Max(0, width - 2);
        for (var row = 0; row < _logRowsShown; row++)
        {
            var index = _logScroll + row;
            if (index >= lines.Count)
            {
                break;
            }

            var text = CellText.Fit(lines[index], textWidth);
            Move(1, top + row);
            if (text.StartsWith("$ ", StringComparison.Ordinal))
            {
                SetAttribute(_theme.On(_theme.Dim));
                AddStr("$");
                text = text[1..];
            }

            SetAttribute(_theme.On(_theme.Foreground));
            AddStr(text);
        }
    }

    /// <summary>
    /// Draws <c> ─ Vendor.Tool failed ───…</c> at <paramref name="top"/>, then what the exit code
    /// means and the last lines of the log, stopping above <paramref name="bottom"/>.
    /// </summary>
    private void DrawFailure(int top, int bottom, int width, OperationView operation)
    {
        var border = _theme.On(_theme.Border);
        var normal = _theme.On(_theme.Foreground);
        var dim = _theme.On(_theme.Dim);

        var label = CellText.Fit($"{operation.Operation.Row.Id} failed", Math.Max(0, width - 5));
        var dashes = Math.Max(0, width - 5 - DisplayWidth.Of(label));
        Move(0, top);
        SetAttribute(border);
        AddStr(" ─ ");
        SetAttribute(_theme.On(_theme.Error));
        AddStr(label);
        SetAttribute(border);
        AddStr(" " + new string('─', dashes));

        var exitCode = operation.Result?.ExitCode ?? 0;
        var explanation = WingetErrorCodes.Explain(exitCode);
        var said = explanation.WingetSaid.Length > 0 ? explanation.WingetSaid : LastLines(operation.Lines, 1).FirstOrDefault() ?? "";
        var valueWidth = Math.Max(1, width - FailureValueLeft - 1);

        var y = top + 1;
        y = DrawFailureField(y, bottom, "winget said", CellText.Wrap(said, valueWidth), normal);
        if (y < bottom)
        {
            DrawFailureLabel(y, "Code");
            SetAttribute(normal);
            var code = WingetErrorCodes.Format(exitCode);
            AddStr(code);
            SetAttribute(dim);
            AddStr(CellText.Fit("  " + explanation.Name, Math.Max(0, valueWidth - DisplayWidth.Of(code))));
            y++;
        }

        y = DrawFailureField(y, bottom, "Usually means", CellText.Wrap(explanation.UsuallyMeans, valueWidth), normal);
        y = DrawFailureField(y, bottom, "Suggestion", CellText.Wrap(explanation.Suggestion, valueWidth), normal);

        y++;
        if (y >= bottom)
        {
            return;
        }

        Move(1, y);
        SetAttribute(_theme.On(_theme.Header));
        AddStr("Last log lines");
        y++;

        SetAttribute(dim);
        foreach (var line in LastLines(operation.Lines, FailureLogLines))
        {
            if (y >= bottom)
            {
                break;
            }

            Move(1, y);
            AddStr(CellText.Fit(line, Math.Max(0, width - 2)));
            y++;
        }
    }

    /// <summary>Draws the label on the first of <paramref name="lines"/> and the rest under the value column; returns the row after them.</summary>
    private int DrawFailureField(int y, int bottom, string label, IReadOnlyList<string> lines, Attribute color)
    {
        for (var i = 0; i < lines.Count && y < bottom; i++)
        {
            if (i == 0)
            {
                DrawFailureLabel(y, label);
            }

            Move(FailureValueLeft, y);
            SetAttribute(color);
            AddStr(lines[i]);
            y++;
        }

        return y;
    }

    /// <summary>Draws <paramref name="label"/> padded to the value column, leaving the cursor there.</summary>
    private void DrawFailureLabel(int y, string label)
    {
        Move(1, y);
        SetAttribute(_theme.On(_theme.Header));
        AddStr(label.PadRight(FailureLabelWidth));
        SetAttribute(_theme.On(_theme.Foreground));
        AddStr(" ");
    }

    /// <summary>The last <paramref name="count"/> lines with any text, oldest first.</summary>
    private static List<string> LastLines(List<string> lines, int count)
    {
        var last = new List<string>();
        for (var i = lines.Count - 1; i >= 0 && last.Count < count; i--)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                last.Insert(0, lines[i]);
            }
        }

        return last;
    }

    private void DrawSavedLine(int y, int width)
    {
        if (_operations.Length == 0 || _operations[DisplayedIndex].LogFile is not { } logFile)
        {
            return;
        }

        SetAttribute(_theme.On(_theme.Dim));
        Move(0, y);
        AddStr(CellText.Fit($" Log is saved to history/{logFile}", Math.Max(0, width - 1)));
    }

    private void DrawSegments(List<(string Text, Attribute Color)> segments)
    {
        foreach (var (text, color) in segments)
        {
            SetAttribute(color);
            AddStr(text);
        }
    }

    private static int SegmentsWidth(List<(string Text, Attribute Color)> segments) =>
        segments.Sum(segment => DisplayWidth.Of(segment.Text));

    private int Count(params OperationState[] states) =>
        _operations.Count(operation => states.Contains(operation.State));

    private void KeepOperationVisible(int index)
    {
        if (index < _operationScroll)
        {
            _operationScroll = index;
        }
        else if (index >= _operationScroll + _operationRowsShown)
        {
            _operationScroll = index - _operationRowsShown + 1;
        }

        _operationScroll = Math.Clamp(_operationScroll, 0, Math.Max(0, _operations.Length - _operationRowsShown));
    }

    private void Select(int index)
    {
        var clamped = Math.Clamp(index, 0, Math.Max(0, _operations.Length - 1));
        if (clamped == _selected)
        {
            return;
        }

        var wasFailure = SelectedFailure is not null;
        _selected = clamped;
        ResetLogScroll();
        SetNeedsDraw();
        if (wasFailure != SelectedFailure is not null)
        {
            HintsChanged?.Invoke();
        }
    }

    private void ScrollLog(int target)
    {
        if (_operations.Length == 0)
        {
            return;
        }

        var maxScroll = Math.Max(0, _operations[DisplayedIndex].Lines.Count - _logRowsShown);
        _logScroll = Math.Clamp(target, 0, maxScroll);
        _isFollowing = _logScroll == maxScroll;
        SetNeedsDraw();
    }

    private void ResetLogScroll()
    {
        _logScroll = 0;
        _isFollowing = true;
    }

    private bool AdvanceSpinner()
    {
        if (_isFinished)
        {
            _spinnerTimer = null;
            return false;
        }

        _spinnerFrame = (_spinnerFrame + 1) % SpinnerFrames.Length;
        SetNeedsDraw();
        return true;
    }

    private void StopSpinner()
    {
        if (_spinnerTimer is not null)
        {
            _app.RemoveTimeout(_spinnerTimer);
            _spinnerTimer = null;
        }
    }

    /// <summary><paramref name="text"/> fitted into <paramref name="width"/> cells with at least one blank cell after it.</summary>
    private static string Column(string text, int width)
    {
        var fitted = CellText.Fit(text, width - 1);
        return fitted + new string(' ', width - DisplayWidth.Of(fitted));
    }

    /// <summary><c>upgrade  2.51.0 → 2.52.0</c>, <c>install  → latest</c>, or <c>uninstall  2.51.0</c>.</summary>
    private static string ActionText(QueuedOperation operation)
    {
        var row = operation.Row;
        var requested = operation.Plan.Request.Version;
        switch (operation.Kind)
        {
            case OperationKind.Upgrade:
                var target = FirstNonEmpty(requested, row.AvailableVersion, "latest");
                return $"upgrade  {row.Version} → {target}";

            case OperationKind.Install:
                return $"install  → {FirstNonEmpty(requested, "latest")}";

            default:
                return $"uninstall  {row.Version}";
        }
    }

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.First(candidate => !string.IsNullOrEmpty(candidate))!;

    private static string Seconds(TimeSpan elapsed) =>
        elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    private static (int Filled, int Empty) BarCells(string line)
    {
        var filled = 0;
        var empty = 0;
        foreach (var character in line)
        {
            if (character == '█')
            {
                filled++;
            }
            else if (character is '░' or '▒')
            {
                empty++;
            }
        }

        return (filled, empty);
    }

    private enum OperationState
    {
        Waiting,
        Running,
        Done,
        Failed,
        Skipped,
        Canceled,
    }

    /// <summary>One operation as the screen shows it: its state, its log so far, and its last known progress.</summary>
    private sealed class OperationView(QueuedOperation operation)
    {
        public QueuedOperation Operation { get; } = operation;

        public OperationState State { get; set; } = OperationState.Waiting;

        public OperationResult? Result { get; set; }

        public List<string> Lines { get; } = [];

        public int? Percent { get; private set; }

        public string? LogFile { get; set; }

        /// <summary>
        /// Keeps what a terminal would leave on screen: the text after a line's last carriage
        /// return, and only the newest of consecutive progress bar lines, since winget redraws
        /// its bar in place and each redraw arrives as a line of its own.
        /// </summary>
        public void AddLine(string text)
        {
            var trimmed = text.TrimEnd('\r');
            var line = trimmed[(trimmed.LastIndexOf('\r') + 1)..];

            var isBar = BarCells(line) != (0, 0);
            var replacesBar = isBar && Lines.Count > 0 && BarCells(Lines[^1]) != (0, 0);
            if (replacesBar)
            {
                Lines[^1] = line;
            }
            else
            {
                Lines.Add(line);
            }

            if (PercentIn(line) is { } percent)
            {
                Percent = percent;
            }
        }
    }
}
