using System.Diagnostics;
using System.Drawing;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.Bundles;
using Wingman.Core.History;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Options;
using Wingman.Core.Settings;
using Wingman.Core.Updates;
using Wingman.Core.Winget;
using Wingman.Tui.Tabs;

namespace Wingman.Tui;

/// <summary>
/// The window chrome every tab shares: title bar, tab strip, message line, and key bar, plus the
/// key routing that sends <c>1</c>-<c>5</c> and key bar keys to the right place, the y/n prompt on
/// the message line, the row context menu and the help overlay, which take every key while open,
/// the one batch allowed to run at a time, the batch <see cref="Queue"/>, the pins every tab marks, and
/// the update policy and install option changes every tab follows. Every
/// member must be called on the UI thread; background work marshals back with <c>App.Invoke</c>.
/// </summary>
internal sealed class Shell
{
    public const string BatchRunningText = "A batch is already running";
    public const string PolicyChangingText = "A policy change is already running";
    public const string QueueClearedText = "Queue cleared";
    public const string QueueEmptyText = "The queue is empty; press Space to mark rows";

    private const string AppTitle = "Wingman";
    private static readonly TimeSpan StatusLifetime = TimeSpan.FromSeconds(5);
    private static readonly BatchOptions BatchOptions = new(ContinueOnFailure: true, AutoElevate: true);

    private static readonly HelpGroup GlobalHelp = new("Global",
    [
        new("1-5", "tabs"),
        new("Tab", "switch pane"),
        new("/", "filter or search"),
        new("s", "cycle sort"),
        new("r", "reload"),
        new("m", "context menu"),
        new("?", "help"),
        new("q", "quit"),
    ]);

    private readonly IWingetClient _client;
    private readonly BatchRunner _batchRunner;
    private readonly HistoryStore _history;
    private readonly bool _canElevate;
    private readonly View _content;
    private readonly Label _message;
    private readonly KeyBar _keyBar;
    private readonly KeyHint _quitHint;
    private readonly KeyHint[] _globalHints;
    private readonly KeyHint[] _promptHints;
    private readonly Dictionary<string, PackageRow> _installed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContextMenu _menu;
    private readonly HelpOverlay _help;

    private List<ShellTab> _tabs = [];
    private TabStrip? _tabStrip;
    private ShellTab? _activeTab;
    private string _wingetVersion = "";
    private object? _statusTimer;

    private IReadOnlyList<Pin> _pins = [];
    private bool _isApplyingPolicy;

    // The running batch, or the finished one whose screen is still up; null once that is dismissed.
    private ActiveBatch? _batch;

    // Set while the y/n prompt is up: what y runs.
    private Action? _onPromptYes;

    // A message posted while the prompt is up, shown once it is answered.
    private (string Text, Scheme Scheme, bool IsTransient)? _heldMessage;

    /// <param name="canElevate">Whether <paramref name="batchRunner"/> has an elevated helper to start.</param>
    public Shell(
        IApplication app,
        Theme theme,
        IWingetClient client,
        WingmanSettings settings,
        BatchRunner batchRunner,
        HistoryStore history,
        bool canElevate)
    {
        App = app;
        Theme = theme;
        Settings = settings;
        _client = client;
        _batchRunner = batchRunner;
        _history = history;
        _canElevate = canElevate;
        Options = WingmanApp.CreatePackageOptionsStore();

        Window = new Window { Title = AppTitle, BorderStyle = LineStyle.Single };
        Window.SetScheme(theme.Normal);
        Window.Border.GetOrCreateView().SetScheme(theme.BorderScheme);

        // The title and version are drawn over the top border by DrawTitleBar instead, because the
        // border's own title cannot carry right-aligned text.
        Window.Border.Settings &= ~BorderSettings.Title;
        Window.DrawComplete += (_, _) =>
        {
            DrawTitleBar();
            PaintOverlay();
        };
        Window.KeyDown += OnKeyDown;
        Window.IsRunningChanged += (_, running) => OnRunningChanged(running.Value);

        // The application sees keys and mouse events before any view does, so the prompt and the
        // overlays can take every key, including Enter and the arrows that the focused table would
        // otherwise act on, and an overlay can close on a click outside it before that click
        // reaches the view under it.
        App.Keyboard.KeyDown += OnAppKeyDown;
        App.Mouse.MouseEvent += OnAppMouseEvent;

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
            new(new Key('?'), "Help", ShowHelp),
            _quitHint,
        ];
        _promptHints =
        [
            new(Key.Y, "Yes", () => AnswerPrompt(true)),
            new(Key.N, "No", () => AnswerPrompt(false)),
        ];

        _menu = new ContextMenu(theme);
        _help = new HelpOverlay(theme);

        Window.Add(tabSeparator, _content, footerSeparator, _message, _keyBar, _menu, _help);
    }

    public IApplication App { get; }

    public Theme Theme { get; }

    /// <summary>The settings the app started with; the theme came from these.</summary>
    public WingmanSettings Settings { get; }

    /// <summary>Per-package install options, which shape every queued operation.</summary>
    public PackageOptionsStore Options { get; }

    /// <summary>The operations marked for the batch, shared by every tab.</summary>
    public OperationQueue Queue { get; } = new();

    /// <summary>Whether a batch is running; only one may run at a time.</summary>
    public bool IsBatchRunning => _batch is { IsRunning: true };

    /// <summary>Raised after the installed set changes.</summary>
    public event Action? InstalledChanged;

    /// <summary>Raised after <see cref="Pins"/> changes.</summary>
    public event Action? PinsChanged;

    /// <summary>Raised after a package's install or update options change in <see cref="Options"/>.</summary>
    public event Action? OptionsChanged;

    /// <summary>Winget's pins as of the last load or pin change; empty until the first load finishes.</summary>
    public IReadOnlyList<Pin> Pins => _pins;

    /// <summary><c>ShowAsync</c> results by Id for the session, shared by every tab's details pane.</summary>
    public Dictionary<string, PackageDetails?> DetailsCache { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The note last saved in the update policy dialog, by Id, for the session only: UniGetUI's
    /// <c>UpdatesOptions</c> has no field to store it in.
    /// </summary>
    public Dictionary<string, string> PolicyNotes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Window Window { get; }

    /// <summary>Opens a web address in the default browser; <c>tools/TuiHarness</c> replaces it so a run never starts one.</summary>
    public Action<string> OpenUrl { get; set; } = OpenInBrowser;

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

    /// <summary>Replaces the installed set with <paramref name="rows"/>; the first row wins for an Id listed twice.</summary>
    public void SetInstalled(IReadOnlyList<PackageRow> rows)
    {
        _installed.Clear();
        foreach (var row in rows)
        {
            _installed.TryAdd(row.Id, row);
        }

        InstalledChanged?.Invoke();
    }

    /// <summary>Whether the Installed tab's last load listed <paramref name="id"/>, ignoring case; false before it.</summary>
    public bool IsInstalled(string id) => _installed.ContainsKey(id);

    /// <summary>The installed row for <paramref name="id"/> as of the Installed tab's last load, or null.</summary>
    public PackageRow? FindInstalled(string id) => _installed.GetValueOrDefault(id);

    /// <summary>
    /// A queue entry for <paramref name="kind"/> on <paramref name="row"/>, shaped by the package's
    /// saved install options and the settings, as the batch runner will run it.
    /// </summary>
    public QueuedOperation BuildOperation(OperationKind kind, PackageRow row)
    {
        var plan = OperationRequestFactory.Create(kind, row, Settings, Options.GetInstallOptions(row.Id));
        return new QueuedOperation(kind, row, plan);
    }

    public void ClearQueue()
    {
        Queue.Clear();
        SetStatus(QueueClearedText);
    }

    /// <summary>Runs every queued operation as one batch, with its screen in place of <paramref name="origin"/>'s content.</summary>
    public void RunQueue(PackageListTab origin)
    {
        if (IsBatchRunning)
        {
            SetStatus(BatchRunningText);
            return;
        }

        if (Queue.Count == 0)
        {
            SetStatus(QueueEmptyText);
            return;
        }

        StartBatch([.. Queue.Items], origin, isFromQueue: true);
    }

    /// <summary>Runs <paramref name="operation"/> as a batch of one, leaving the queue alone.</summary>
    public void RunOperation(QueuedOperation operation, PackageListTab origin)
    {
        if (IsBatchRunning)
        {
            SetStatus(BatchRunningText);
            return;
        }

        StartBatch([operation], origin, isFromQueue: false);
    }

    /// <summary>Starts the Installed tab's first load, for a tab that needs the installed set before Installed was shown.</summary>
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

    /// <summary>Tells every list tab that a batch started from <paramref name="origin"/> has finished, so each reloads what it may have changed.</summary>
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

    /// <summary>How Wingman treats <paramref name="row"/>'s updates, from winget's pins and the package's update options.</summary>
    public UpdatePolicyKind ResolvePolicy(PackageRow row) =>
        UpdatePolicyResolver.Resolve(row, _pins, Options.GetUpdatesOptions(row.Id));

    /// <summary>Whether the package is excluded from Wingman's updates because it updates itself.</summary>
    public bool IsExcluded(string id) => Options.GetUpdatesOptions(id).UpdatesIgnored;

    /// <summary>
    /// Stores both option sets for <paramref name="id"/>, raises <see cref="OptionsChanged"/>, and
    /// says so on the message line; false, with the error shown, when the file could not be written.
    /// </summary>
    public bool SaveOptions(string id, InstallOptions install, UpdatesOptions updates)
    {
        try
        {
            Options.SetInstallOptions(id, install);
            Options.SetUpdatesOptions(id, updates);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError($"Could not save options for {id}: {ex.Message}");
            return false;
        }

        OptionsChanged?.Invoke();
        SetStatus($"Saved options for {id}");
        return true;
    }

    /// <summary>
    /// Gives <paramref name="row"/> the update policy <paramref name="kind"/> through
    /// <see cref="UpdatePolicyApplier"/>, which pins or unpins it with winget as needed and stores
    /// its update options, then reloads <see cref="Pins"/> and reports the result on the message
    /// line. Refused while a batch or another policy change is running.
    /// </summary>
    /// <remarks>
    /// Awaited on the UI thread rather than run on a background task: Terminal.Gui's
    /// synchronization context brings each await back to the UI thread, so the store, which is
    /// not thread-safe, is only ever touched there.
    /// </remarks>
    public async Task ApplyPolicyAsync(PackageRow row, UpdatePolicyKind kind, string? note)
    {
        if (IsBatchRunning)
        {
            SetStatus(BatchRunningText);
            return;
        }

        if (_isApplyingPolicy)
        {
            SetStatus(PolicyChangingText);
            return;
        }

        _isApplyingPolicy = true;
        try
        {
            var applier = new UpdatePolicyApplier(_client, Options);
            var result = await applier.ApplyAsync(row, kind, note, _pins, CancellationToken.None);
            SetPins(await _client.ListPinsAsync(CancellationToken.None));
            OptionsChanged?.Invoke();
            if (result.Succeeded)
            {
                SetStatus(PolicyAppliedText(row, kind));
            }
            else
            {
                SetError($"winget: {FailureText(result)}");
            }
        }
        catch (Exception ex)
        {
            SetError($"winget: {ex.Message}");
        }
        finally
        {
            _isApplyingPolicy = false;
        }
    }

    /// <summary>
    /// Reads the history on a background task and calls <paramref name="onLoaded"/> on the UI thread
    /// with what the package's last operation's exit code means when that operation failed, or null
    /// when it succeeded or there is none.
    /// </summary>
    public void LoadLastFailure(string id, Action<ErrorExplanation?> onLoaded)
    {
        _ = Task.Run(() =>
        {
            ErrorExplanation? explanation = null;
            try
            {
                foreach (var entry in _history.List())
                {
                    var isOperation = entry.Operation != "batch"
                        && string.Equals(entry.PackageId, id, StringComparison.OrdinalIgnoreCase);
                    if (isOperation)
                    {
                        explanation = entry.Succeeded ? null : WingetErrorCodes.Explain(entry.ExitCode);
                        break;
                    }
                }
            }
            catch (IOException)
            {
            }

            App.Invoke(() => onLoaded(explanation));
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

    /// <summary>
    /// Opens the context menu titled <paramref name="title"/> with its corner at <paramref name="screenPosition"/>,
    /// kept inside the content area. Ignored while the y/n prompt is up, since the menu's entries ask questions of their own.
    /// </summary>
    public void ShowContextMenu(string title, IReadOnlyList<MenuEntry> entries, Point screenPosition)
    {
        if (_onPromptYes is not null || OpenOverlay is not null)
        {
            return;
        }

        var position = Window.ScreenToViewport(screenPosition);
        Window.MoveSubViewToEnd(_menu);
        _menu.Open(title, entries, position, _content.Frame);
    }

    /// <summary>Opens the key list for the active tab over the content.</summary>
    public void ShowHelp()
    {
        if (_onPromptYes is not null || OpenOverlay is not null)
        {
            return;
        }

        IReadOnlyList<HelpGroup> tabGroups = _activeTab?.HelpGroups ?? [];
        Window.MoveSubViewToEnd(_help);
        _help.Open([GlobalHelp, .. tabGroups], _content.Frame);
    }

    /// <summary>Puts <paramref name="id"/> on the system clipboard and says whether that worked.</summary>
    public void CopyId(string id)
    {
        var isCopied = App.Clipboard?.TrySetClipboardData(id) ?? false;
        SetStatus(isCopied ? $"Copied {id}" : "Clipboard is not available");
    }

    /// <summary>Opens the package's homepage from <c>winget show</c>, asking winget on a background task unless <see cref="DetailsCache"/> already has it.</summary>
    public void OpenHomepage(string id)
    {
        if (DetailsCache.TryGetValue(id, out var cached))
        {
            OpenHomepageFrom(id, cached);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var details = await _client.ShowAsync(id, CancellationToken.None);
                App.Invoke(() =>
                {
                    DetailsCache[id] = details;
                    OpenHomepageFrom(id, details);
                });
            }
            catch (Exception ex)
            {
                App.Invoke(() => SetError($"winget: {ex.Message}"));
            }
        });
    }

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

    /// <summary>The context menu or help overlay while one is open, or null.</summary>
    private View? OpenOverlay
    {
        get
        {
            if (_menu.Visible)
            {
                return _menu;
            }

            if (_help.Visible)
            {
                return _help;
            }

            return null;
        }
    }

    /// <summary>
    /// Opens only http and https addresses, since a manifest's homepage is whatever its author typed
    /// and the shell would run a file path or another scheme's handler just as readily.
    /// </summary>
    private void OpenHomepageFrom(string id, PackageDetails? details)
    {
        var homepage = details?.Homepage;
        if (string.IsNullOrWhiteSpace(homepage))
        {
            SetStatus($"No homepage for {id}");
            return;
        }

        var isWebAddress = Uri.TryCreate(homepage, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        if (!isWebAddress)
        {
            SetError($"Not opening {homepage}: not a web address");
            return;
        }

        try
        {
            OpenUrl(uri!.AbsoluteUri);
            SetStatus($"Opened {uri.AbsoluteUri}");
        }
        catch (Exception ex)
        {
            SetError($"Could not open {homepage}: {ex.Message}");
        }
    }

    private static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    private void OnAppKeyDown(object? sender, Key key)
    {
        if (_menu.Visible)
        {
            key.Handled = true;
            _menu.HandleKey(key);
            return;
        }

        if (_help.Visible)
        {
            key.Handled = true;
            _help.HandleKey(key);
            return;
        }

        OnPromptKeyDown(key);
    }

    /// <summary>
    /// Keeps every mouse event outside an open overlay from reaching the views behind it, and
    /// closes the overlay on a click there. The press and release before that click are taken too,
    /// or the table would move its cursor on them.
    /// </summary>
    private void OnAppMouseEvent(object? sender, Mouse mouse)
    {
        if (OpenOverlay is not { } overlay || overlay.FrameToScreen().Contains(mouse.ScreenPosition))
        {
            return;
        }

        mouse.Handled = true;
        if (mouse.IsSingleDoubleOrTripleClicked)
        {
            overlay.Visible = false;
        }
    }

    private void OnPromptKeyDown(Key key)
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
        if (_batch is not { IsRunning: true } batch)
        {
            App.RequestStop();
            return;
        }

        AskConfirm("Quit and cancel the batch? (y/n)", () =>
        {
            batch.Cancellation.Cancel();
            App.RequestStop();
        });
    }

    /// <summary>
    /// Shows a new <see cref="BatchRunnerScreen"/> on <paramref name="origin"/>, dismissing a
    /// finished one still up on any tab, and runs <paramref name="operations"/> on a background
    /// task that reports back through <c>App.Invoke</c>.
    /// </summary>
    private void StartBatch(IReadOnlyList<QueuedOperation> operations, PackageListTab origin, bool isFromQueue)
    {
        CloseBatch();

        var screen = new BatchRunnerScreen(App, Theme, operations, InitialElevationState(operations));
        var batch = new ActiveBatch(screen, origin, operations, isFromQueue);
        _batch = batch;
        screen.CancelRequested += () => AskCancelBatch(batch);
        screen.BackRequested += CloseBatch;
        screen.RetryRequested += operation => RunOperation(operation, origin);
        screen.PolicyRequested += (row, kind) => _ = ApplyPolicyAsync(row, kind, note: null);
        screen.HintsChanged += () => RefreshHints(origin);
        origin.ShowBatchScreen(screen);
        RefreshHints(origin);

        var progress = new InvokingProgress<BatchProgress>(update => App.Invoke(() => screen.Apply(update)));
        var token = batch.Cancellation.Token;
        _ = Task.Run(async () =>
        {
            Exception? error = null;
            IReadOnlyList<string?> logFiles = [];
            try
            {
                var summary = await _batchRunner.RunAsync(operations, BatchOptions, progress, token);
                logFiles = LogFilesFor(summary.BatchId, operations);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            App.Invoke(() => FinishBatch(batch, logFiles, error));
        });
    }

    /// <summary>What the batch screen shows for the elevated helper before the runner reports on it.</summary>
    private string InitialElevationState(IReadOnlyList<QueuedOperation> operations)
    {
        var needsElevation = operations.Any(operation => operation.Plan.RequiresElevation);
        if (!needsElevation)
        {
            return "not needed";
        }

        // Without a helper to start, the runner runs elevated operations in-process and says nothing.
        return _canElevate ? "requesting" : "not available";
    }

    /// <summary>
    /// Each operation's history log file name, or null for one that never ran, read from the
    /// store the runner wrote to; called on the background task, since it reads every entry.
    /// </summary>
    private IReadOnlyList<string?> LogFilesFor(string batchId, IReadOnlyList<QueuedOperation> operations)
    {
        var files = new string?[operations.Count];
        IReadOnlyList<HistoryEntry> entries;
        try
        {
            entries = _history.List();
        }
        catch (IOException)
        {
            return files;
        }

        for (var i = 0; i < operations.Count; i++)
        {
            var id = operations[i].Row.Id;
            var verb = operations[i].Kind.ToString().ToLowerInvariant();
            foreach (var entry in entries)
            {
                var isThisOperation = entry.BatchId == batchId
                    && entry.Operation == verb
                    && string.Equals(entry.PackageId, id, StringComparison.OrdinalIgnoreCase);
                if (isThisOperation)
                {
                    files[i] = entry.LogFileName;
                    break;
                }
            }
        }

        return files;
    }

    /// <summary>
    /// Ends the batch on its screen, takes what it ran out of the queue, and has every list tab
    /// reload what the batch may have changed, whether or not its screen is dismissed yet.
    /// </summary>
    private void FinishBatch(ActiveBatch batch, IReadOnlyList<string?> logFiles, Exception? error)
    {
        batch.IsRunning = false;
        batch.Screen.Finish(logFiles);
        if (error is not null)
        {
            SetError($"Batch stopped: {error.Message}");
        }

        // Taken out one by one rather than cleared, so rows marked while the batch ran stay marked.
        if (batch.IsFromQueue)
        {
            foreach (var operation in batch.Operations)
            {
                Queue.Remove(operation.Row.Id);
            }
        }

        RefreshHints(batch.Origin);
        RefreshAfterOperation(batch.Origin);
    }

    private void AskCancelBatch(ActiveBatch batch)
    {
        if (!batch.IsRunning)
        {
            return;
        }

        AskConfirm("Cancel remaining operations? (y/n)", () =>
        {
            // Checked again because the batch can finish while the question is up.
            if (batch.IsRunning)
            {
                batch.Screen.MarkCancelRequested();
                batch.Cancellation.Cancel();
            }
        });
    }

    /// <summary>Takes a finished batch's screen down and puts its tab's list back; does nothing while the batch runs.</summary>
    private void CloseBatch()
    {
        if (_batch is not { IsRunning: false } batch)
        {
            return;
        }

        _batch = null;
        batch.Origin.HideBatchScreen();
        RefreshHints(batch.Origin);
    }

    private void SetPins(IReadOnlyList<Pin> pins)
    {
        _pins = pins;
        PinsChanged?.Invoke();
    }

    private static string PolicyAppliedText(PackageRow row, UpdatePolicyKind kind) => kind switch
    {
        UpdatePolicyKind.Hold => $"Held {row.Id}",
        UpdatePolicyKind.SkipVersion => $"Skipping {row.AvailableVersion} of {row.Id}",
        UpdatePolicyKind.Exclude => $"Excluded {row.Id} from Wingman updates",
        _ => $"{row.Id} updates with Wingman",
    };

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

            // Help and the menu work on every tab and while a log shows, whether or not the key bar lists them.
            if (rune.Value == '?')
            {
                ShowHelp();
                key.Handled = true;
                return;
            }

            if (rune.Value == 'm')
            {
                _activeTab?.ShowContextMenu();
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

    /// <summary>Draws the open overlay over everything else the window drew, its line canvas included.</summary>
    private void PaintOverlay()
    {
        if (OpenOverlay is null)
        {
            return;
        }

        var savedClip = Window.SetClipToScreen();
        if (_menu.Visible)
        {
            _menu.Paint();
        }
        else
        {
            _help.Paint();
        }

        Window.SetClip(savedClip);
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

    /// <summary>A batch the shell started: its screen, the tab showing it, and what it runs.</summary>
    private sealed class ActiveBatch(
        BatchRunnerScreen screen,
        PackageListTab origin,
        IReadOnlyList<QueuedOperation> operations,
        bool isFromQueue)
    {
        public BatchRunnerScreen Screen { get; } = screen;

        public PackageListTab Origin { get; } = origin;

        public IReadOnlyList<QueuedOperation> Operations { get; } = operations;

        /// <summary>Whether <see cref="Operations"/> came from the queue, which then gives them up when the batch ends.</summary>
        public bool IsFromQueue { get; } = isFromQueue;

        // Not disposed: a source without a timer holds no resources, and a late cancel must not throw.
        public CancellationTokenSource Cancellation { get; } = new();

        public bool IsRunning { get; set; } = true;
    }

    /// <summary>
    /// Forwards each report synchronously, so the order of <c>App.Invoke</c> calls is the order the
    /// runner reported in. <see cref="Progress{T}"/> posts each report to the thread pool when there
    /// is no synchronization context instead, which can reorder them.
    /// </summary>
    private sealed class InvokingProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
