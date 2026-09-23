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
/// key routing that sends <c>1</c>-<c>5</c> and key bar keys to the right place. Every member
/// must be called on the UI thread; background work marshals back with <c>App.Invoke</c>.
/// </summary>
internal sealed class Shell
{
    private const string AppTitle = "Wingman";
    private static readonly TimeSpan StatusLifetime = TimeSpan.FromSeconds(5);

    private readonly View _content;
    private readonly Label _message;
    private readonly KeyBar _keyBar;
    private readonly KeyHint[] _globalHints;
    private readonly HashSet<string> _installedIds = new(StringComparer.OrdinalIgnoreCase);

    private List<ShellTab> _tabs = [];
    private TabStrip? _tabStrip;
    private ShellTab? _activeTab;
    private string _wingetVersion = "";
    private object? _statusTimer;

    public Shell(IApplication app, Theme theme)
    {
        App = app;
        Theme = theme;

        Window = new Window { Title = AppTitle, BorderStyle = LineStyle.Single };
        Window.SetScheme(theme.Normal);
        Window.Border.GetOrCreateView().SetScheme(theme.BorderScheme);

        // The title and version are drawn over the top border by DrawTitleBar instead, because the
        // border's own title cannot carry right-aligned text.
        Window.Border.Settings &= ~BorderSettings.Title;
        Window.DrawComplete += (_, _) => DrawTitleBar();
        Window.KeyDown += OnKeyDown;
        Window.IsRunningChanged += (_, running) => OnRunningChanged(running.Value);

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

        _globalHints =
        [
            new(new Key('?'), "Help", () => SetStatus("Not implemented yet: help")),
            new(Key.Q, "Quit", () => App.RequestStop()),
        ];

        Window.Add(tabSeparator, _content, footerSeparator, _message, _keyBar);
    }

    public IApplication App { get; }

    public Theme Theme { get; }

    /// <summary>Raised after <see cref="InstalledIds"/> changes.</summary>
    public event Action? InstalledChanged;

    /// <summary>Ids of the installed packages as of the Installed tab's last load, ignoring case; empty before it.</summary>
    public IReadOnlySet<string> InstalledIds => _installedIds;

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

    /// <summary>Shows <paramref name="text"/> on the message line for a few seconds.</summary>
    public void SetStatus(string text)
    {
        ShowMessage(text, Theme.Normal);
        _statusTimer = App.AddTimeout(StatusLifetime, () =>
        {
            _message.Text = "";
            _statusTimer = null;
            return false;
        });
    }

    /// <summary>Shows <paramref name="text"/> on the message line in the error color until the next message.</summary>
    public void SetError(string text) => ShowMessage(text, Theme.ErrorScheme);

    private void ShowMessage(string text, Scheme scheme)
    {
        if (_statusTimer is not null)
        {
            App.RemoveTimeout(_statusTimer);
            _statusTimer = null;
        }

        _message.SetScheme(scheme);
        _message.Text = text;
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
        _activeTab = _tabs[index];
        foreach (var tab in _tabs)
        {
            tab.Visible = tab == _activeTab;
        }

        _keyBar.Hints = [.. _activeTab.Hints, .. _globalHints];
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
