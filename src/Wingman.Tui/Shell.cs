using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.Models;
using Wingman.Core.Winget;
using Wingman.Tui.Tabs;

namespace Wingman.Tui;

/// <summary>
/// The window chrome every tab shares: title bar, tab strip, message line, and key bar, plus the
/// key routing that sends <c>1</c>-<c>5</c> and key bar keys to the right place, the y/n prompt on
/// the message line, the one <see cref="OperationRunner"/>, and the pins every tab marks. Every
/// member must be called on the UI thread; background work marshals back with <c>App.Invoke</c>.
/// </summary>
internal sealed class Shell
{
    public const string AlreadyRunningText = "An operation is already running";

    private const string AppTitle = "Wingman";
    private static readonly TimeSpan StatusLifetime = TimeSpan.FromSeconds(5);

    private readonly IWingetClient _client;
    private readonly View _content;
    private readonly Label _message;
    private readonly KeyBar _keyBar;
    private readonly KeyHint _quitHint;
    private readonly KeyHint[] _globalHints;
    private readonly KeyHint[] _promptHints;
    private readonly HashSet<string> _installedIds = new(StringComparer.OrdinalIgnoreCase);

    private List<ShellTab> _tabs = [];
    private TabStrip? _tabStrip;
    private ShellTab? _activeTab;
    private string _wingetVersion = "";
    private object? _statusTimer;

    private IReadOnlyList<Pin> _pins = [];
    private bool _isChangingPin;

    // Set while the y/n prompt is up: what y runs.
    private Action? _onPromptYes;

    // A message posted while the prompt is up, shown once it is answered.
    private (string Text, Scheme Scheme, bool IsTransient)? _heldMessage;

    public Shell(IApplication app, Theme theme, IWingetClient client)
    {
        App = app;
        Theme = theme;
        _client = client;
        Runner = new OperationRunner(client, action => app.Invoke(action));

        Window = new Window { Title = AppTitle, BorderStyle = LineStyle.Single };
        Window.SetScheme(theme.Normal);
        Window.Border.GetOrCreateView().SetScheme(theme.BorderScheme);

        // The title and version are drawn over the top border by DrawTitleBar instead, because the
        // border's own title cannot carry right-aligned text.
        Window.Border.Settings &= ~BorderSettings.Title;
        Window.DrawComplete += (_, _) => DrawTitleBar();
        Window.KeyDown += OnKeyDown;
        Window.IsRunningChanged += (_, running) => OnRunningChanged(running.Value);

        // The application sees keys before any view does, so the prompt can take every key,
        // including Enter and the arrows that the focused table would otherwise act on.
        App.Keyboard.KeyDown += OnPromptKeyDown;

        var borderAttribute = theme.On(theme.Border);

        // X = -1 and Dim.Fill(-1) overlap the window border so the lines join it as ├ and ┤.
        var tabSeparator = new Line
        {
            X = -1,
            Y = 1,
            Width = Dim.Fill(-1),
            SuperViewRendersLineCanvas = true,
            LineAttribute = borderAttribute,
        };
        var footerSeparator = new Line
        {
            X = -1,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(-1),
            SuperViewRendersLineCanvas = true,
            LineAttribute = borderAttribute,
        };

        _content = new View
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(3),
            CanFocus = true,
            SuperViewRendersLineCanvas = true,
        };

        _message = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1), Text = "" };

        _keyBar = new KeyBar(theme) { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill() };

        _quitHint = new(Key.Q, "Quit", Quit);
        _globalHints =
        [
            new(new Key('?'), "Help", () => SetStatus("Not implemented yet: help")),
            _quitHint,
        ];
        _promptHints =
        [
            new(Key.Y, "Yes", () => AnswerPrompt(true)),
            new(Key.N, "No", () => AnswerPrompt(false)),
        ];

        Window.Add(tabSeparator, _content, footerSeparator, _message, _keyBar);
    }

    public IApplication App { get; }

    public Theme Theme { get; }

    /// <summary>Runs the one install, upgrade, or uninstall allowed at a time.</summary>
    public OperationRunner Runner { get; }

    /// <summary>Raised after <see cref="InstalledIds"/> changes.</summary>
    public event Action? InstalledChanged;

    /// <summary>Raised after <see cref="Pins"/> changes.</summary>
    public event Action? PinsChanged;

    /// <summary>Ids of the installed packages as of the Installed tab's last load, ignoring case; empty before it.</summary>
    public IReadOnlySet<string> InstalledIds => _installedIds;

    /// <summary>Winget's pins as of the last load or pin change; empty until the first load finishes.</summary>
    public IReadOnlyList<Pin> Pins => _pins;

    /// <summary><c>ShowAsync</c> results by Id for the session, shared by every tab's details pane.</summary>
    public Dictionary<string, PackageDetails?> DetailsCache { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Window Window { get; }

    /// <summary>Winget's version as <c>GetVersionAsync</c> reports it, shown at the right of the title bar.</summary>
    public string WingetVersion
    {
        get => _wingetVersion;
        set
        {
            _wingetVersion = value;
            Window.SetNeedsDraw();
        }
    }

    /// <summary>Installs the tabs in strip order. The first is shown once the window starts running.</summary>
    public void SetTabs(IReadOnlyList<ShellTab> tabs)
    {
        _tabs = [.. tabs];

        var titles = new List<string>();
        foreach (var tab in _tabs)
        {
            titles.Add(tab.Title);
            tab.Visible = false;
            _content.Add(tab);
        }

        _tabStrip = new TabStrip(Theme, titles) { X = 0, Y = 0, Width = Dim.Fill() };
        _tabStrip.SelectedIndexChanged += ShowTab;
        Window.Add(_tabStrip);
    }

    public void SelectTab(int index)
    {
        if (_tabStrip is not null)
        {
            _tabStrip.SelectedIndex = index;
        }
    }

    /// <summary>Shows <paramref name="count"/> after the tab's title in the strip, or nothing when null.</summary>
    public void SetTabCount(ShellTab tab, int? count)
    {
        var index = _tabs.IndexOf(tab);
        if (_tabStrip is not null && index >= 0)
        {
            _tabStrip.SetCount(index, count);
        }
    }

    /// <summary>Replaces <see cref="InstalledIds"/> with the Ids of <paramref name="rows"/>.</summary>
    public void SetInstalled(IReadOnlyList<PackageRow> rows)
    {
        _installedIds.Clear();
        foreach (var row in rows)
        {
            _installedIds.Add(row.Id);
        }

        InstalledChanged?.Invoke();
    }

    /// <summary>Starts the Installed tab's first load, for a tab that needs <see cref="InstalledIds"/> before Installed was shown.</summary>
    public void EnsureInstalledLoaded()
    {
        foreach (var tab in _tabs)
        {
            if (tab is InstalledTab installed)
            {
                installed.EnsureLoaded();
            }
        }
    }

    /// <summary>Tells every list tab that an operation started from <paramref name="origin"/> has finished, so each reloads what it may have changed.</summary>
    public void RefreshAfterOperation(ShellTab origin)
    {
        foreach (var tab in _tabs)
        {
            if (tab is PackageListTab listTab)
            {
                listTab.RefreshAfterOperation(isOrigin: tab == origin);
            }
        }
    }

    /// <summary>The pin on the package with <paramref name="id"/>, ignoring case, or null when it has none.</summary>
    public Pin? FindPin(string id)
    {
        foreach (var pin in _pins)
        {
            if (string.Equals(pin.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return pin;
            }
        }

        return null;
    }

    public bool IsPinned(string id) => FindPin(id) is not null;

    /// <summary>Replaces <see cref="Pins"/> with winget's pin list, read on a background task.</summary>
    public void ReloadPins()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var pins = await _client.ListPinsAsync(CancellationToken.None);
                App.Invoke(() => SetPins(pins));
            }
            catch (Exception ex)
            {
                App.Invoke(() => SetError($"winget: {ex.Message}"));
            }
        });
    }

    /// <summary>
    /// Removes the package's pin, or adds a blocking one when it has none, on a background task,
    /// then reloads <see cref="Pins"/> and reports the result on the message line. Refused while an
    /// operation or another pin change is running.
    /// </summary>
    public void TogglePin(string id)
    {
        if (Runner.IsRunning || _isChangingPin)
        {
            SetStatus(AlreadyRunningText);
            return;
        }

        _isChangingPin = true;
        var wasPinned = IsPinned(id);
        _ = Task.Run(async () =>
        {
            OperationResult? result = null;
            IReadOnlyList<Pin>? pins = null;
            Exception? error = null;
            try
            {
                result = wasPinned
                    ? await _client.UnpinAsync(id, CancellationToken.None)
                    : await _client.PinAsync(id, blocking: true, version: null, CancellationToken.None);
                pins = await _client.ListPinsAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            App.Invoke(() => FinishPinChange(id, wasPinned, result, pins, error));
        });
    }

    /// <summary>
    /// Shows <paramref name="question"/> on the message line and <c>y Yes   n No</c> on the key bar,
    /// and takes every key until it is answered: <c>y</c> or Enter runs <paramref name="onYes"/>,
    /// <c>n</c> or Esc dismisses it, and any other key is ignored.
    /// </summary>
    public void AskConfirm(string question, Action onYes)
    {
        _onPromptYes = onYes;
        _heldMessage = null;
        ShowMessage(question, Theme.Normal, isTransient: false);
        ApplyHints();
    }

    /// <summary>Puts the tab's current <see cref="ShellTab.Hints"/> on the key bar if it is the active tab.</summary>
    public void RefreshHints(ShellTab tab)
    {
        if (tab == _activeTab)
        {
            ApplyHints();
        }
    }

    /// <summary>Shows <paramref name="text"/> on the message line for a few seconds.</summary>
    public void SetStatus(string text) => PostMessage(text, Theme.Normal, isTransient: true);

    /// <summary>Shows <paramref name="text"/> on the message line in the success color for a few seconds.</summary>
    public void SetSuccess(string text) => PostMessage(text, Theme.OkScheme, isTransient: true);

    /// <summary>Shows <paramref name="text"/> on the message line in the error color until the next message.</summary>
    public void SetError(string text) => PostMessage(text, Theme.ErrorScheme, isTransient: false);

    private void PostMessage(string text, Scheme scheme, bool isTransient)
    {
        if (_onPromptYes is not null)
        {
            _heldMessage = (text, scheme, isTransient);
            return;
        }

        ShowMessage(text, scheme, isTransient);
    }

    private void ShowMessage(string text, Scheme scheme, bool isTransient)
    {
        if (_statusTimer is not null)
        {
            App.RemoveTimeout(_statusTimer);
            _statusTimer = null;
        }

        _message.SetScheme(scheme);
        _message.Text = text;

        if (isTransient)
        {
            _statusTimer = App.AddTimeout(StatusLifetime, () =>
            {
                _message.Text = "";
                _statusTimer = null;
                return false;
            });
        }
    }

    private void AnswerPrompt(bool isYes)
    {
        if (_onPromptYes is not { } onYes)
        {
            return;
        }

        _onPromptYes = null;
        if (_heldMessage is { } held)
        {
            _heldMessage = null;
            ShowMessage(held.Text, held.Scheme, held.IsTransient);
        }
        else
        {
            ShowMessage("", Theme.Normal, isTransient: false);
        }

        ApplyHints();
        if (isYes)
        {
            onYes();
        }
    }

    private void OnPromptKeyDown(object? sender, Key key)
    {
        if (_onPromptYes is null)
        {
            return;
        }

        key.Handled = true;
        if (key == Key.Enter || IsPlainLetter(key, 'y'))
        {
            AnswerPrompt(true);
        }
        else if (key == Key.Esc || IsPlainLetter(key, 'n'))
        {
            AnswerPrompt(false);
        }
    }

    private static bool IsPlainLetter(Key key, char letter)
    {
        var isPlainKey = !key.IsCtrl && !key.IsAlt;
        return isPlainKey
            && key.TryGetPrintableRune(out var rune)
            && Rune.ToLowerInvariant(rune).Value == letter;
    }

    private void ApplyHints()
    {
        if (_onPromptYes is not null)
        {
            _keyBar.Hints = _promptHints;
            return;
        }

        if (_activeTab is { } tab)
        {
            IReadOnlyList<KeyHint> globalHints = tab.ShowsHelpHint ? _globalHints : [_quitHint];
            _keyBar.Hints = [.. tab.Hints, .. globalHints];
        }
    }

    private void Quit()
    {
        if (Runner.Current is not { } operation)
        {
            App.RequestStop();
            return;
        }

        AskConfirm($"Quit and cancel {operation.Verb} {operation.Id}? (y/n)", () =>
        {
            Runner.Cancel();
            App.RequestStop();
        });
    }

    private void SetPins(IReadOnlyList<Pin> pins)
    {
        _pins = pins;
        PinsChanged?.Invoke();
    }

    private void FinishPinChange(string id, bool wasPinned, OperationResult? result, IReadOnlyList<Pin>? pins, Exception? error)
    {
        _isChangingPin = false;
        if (pins is not null)
        {
            SetPins(pins);
        }

        if (error is not null)
        {
            SetError($"winget: {error.Message}");
        }
        else if (result is { Succeeded: false })
        {
            SetError($"winget: {FailureText(result)}");
        }
        else
        {
            SetStatus(wasPinned ? $"Unpinned {id}" : $"Pinned {id} (blocking)");
        }
    }

    /// <summary>Winget's last line of output, which names the problem, or the exit code when it printed nothing.</summary>
    private static string FailureText(OperationResult result)
    {
        for (var i = result.Log.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(result.Log[i]))
            {
                return result.Log[i].Trim();
            }
        }

        return $"exit code {result.ExitCode}";
    }

    private void OnRunningChanged(bool isRunning)
    {
        // Deferred to here because focus and timers need the running application.
        if (isRunning && _activeTab is null && _tabs.Count > 0)
        {
            ShowTab(0);
        }
    }

    private void ShowTab(int index)
    {
        // Only a click on the strip gets here while a prompt is up, and the question was about the tab being left.
        AnswerPrompt(false);

        _activeTab = _tabs[index];
        foreach (var tab in _tabs)
        {
            tab.Visible = tab == _activeTab;
        }

        ApplyHints();
        _activeTab.OnShown();
    }

    private void OnKeyDown(object? sender, Key key)
    {
        // Focused views see keys first, so a TextField has already taken what it types; this also
        // keeps the keys it passes on, such as F-keys, from triggering hints mid-edit.
        if (Window.MostFocused is TextField)
        {
            return;
        }

        var isPlainKey = !key.IsCtrl && !key.IsAlt;
        if (isPlainKey && key.TryGetPrintableRune(out var rune))
        {
            var tabIndex = rune.Value - '1';
            if (tabIndex >= 0 && tabIndex < _tabs.Count)
            {
                SelectTab(tabIndex);
                key.Handled = true;
                return;
            }
        }

        foreach (var hint in _keyBar.Hints)
        {
            if (hint.Matches(key))
            {
                hint.Action();
                key.Handled = true;
                return;
            }
        }
    }

    /// <summary>Draws <c>┌─ Wingman ──── winget 1.29.380 ─┐</c> over the top border.</summary>
    private void DrawTitleBar()
    {
        if (App.Driver is not { } driver)
        {
            return;
        }

        var frame = Window.FrameToScreen();
        var borderAttribute = Theme.On(Theme.Border);
        var savedClip = Window.SetClipToScreen();

        driver.Move(frame.X + 1, frame.Y);
        driver.SetAttribute(borderAttribute);
        driver.AddStr("─ ");
        driver.SetAttribute(Theme.On(Theme.Foreground, TextStyle.Bold));
        driver.AddStr(AppTitle);
        driver.SetAttribute(borderAttribute);
        driver.AddStr(" ");
        var titleEnd = frame.X + 1 + DisplayWidth.Of($"─ {AppTitle} ");

        if (_wingetVersion.Length > 0)
        {
            var versionText = $"winget {_wingetVersion.TrimStart('v')}";
            var versionWidth = DisplayWidth.Of(versionText);

            // Right-aligned as " winget 1.29.380 ─" just inside the ┐ corner.
            var versionStart = frame.X + frame.Width - 1 - versionWidth - 3;
            if (versionStart > titleEnd)
            {
                driver.Move(versionStart, frame.Y);
                driver.SetAttribute(borderAttribute);
                driver.AddStr(" ");
                driver.SetAttribute(Theme.On(Theme.Dim));
                driver.AddStr(versionText);
                driver.SetAttribute(borderAttribute);
                driver.AddStr(" ─");
            }
        }

        Window.SetClip(savedClip);
    }
}
