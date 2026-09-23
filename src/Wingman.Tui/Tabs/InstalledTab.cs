using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// Every installed package, loaded from <see cref="IWingetClient.ListInstalledAsync"/> on a
/// background task the first time the tab is shown and again on <c>r</c>, with the details pane
/// on the right.
/// </summary>
internal sealed class InstalledTab : ShellTab
{
    private const int WideLayoutWidth = 96;
    private const int WideLeftPaneWidth = 57;
    private const int NarrowLeftPanePercent = 60;

    private readonly Shell _shell;
    private readonly IWingetClient _client;
    private readonly PackageTable _table;
    private readonly Line _divider;
    private readonly View _details;
    private readonly KeyHint[] _hints;

    // Only the load this points at may touch the UI; an older one finishing late is ignored.
    private CancellationTokenSource? _loadCancellation;
    private bool _hasLoaded;
    private int _leftPaneWidth = WideLeftPaneWidth;

    public InstalledTab(Shell shell, IWingetClient client)
        : base("Installed")
    {
        _shell = shell;
        _client = client;
        CanFocus = true;

        PackageColumn[] columns =
        [
            new("Name", row => row.Name, 24),
            new("Id", row => row.Id, 0),
            new("Version", row => row.Version, 14, VersionComparer.Instance),
        ];
        _table = new PackageTable(shell.Theme, columns)
        {
            X = 0,
            Y = 0,
            Width = WideLeftPaneWidth,
            Height = Dim.Fill(),
        };
        _table.RowActivated += row => _shell.SetStatus($"Activated {row.Id}");

        // Starts one row above and ends one row below the tab so it meets the shell's separators.
        _divider = new Line
        {
            Orientation = Orientation.Vertical,
            X = WideLeftPaneWidth,
            Y = -1,
            Height = Dim.Fill(-1),
            SuperViewRendersLineCanvas = true,
            LineAttribute = shell.Theme.On(shell.Theme.Border),
        };

        _details = new View
        {
            X = WideLeftPaneWidth + 1,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        var detailsPlaceholder = new Label { X = 1, Y = 0, Text = "Details: coming in #14" };
        detailsPlaceholder.SetScheme(shell.Theme.DimScheme);
        _details.Add(detailsPlaceholder);

        Add(_table, _divider, _details);

        _hints =
        [
            new(Key.U, "Upgrade", () => _shell.SetStatus("Not implemented yet: upgrade")),
            new(Key.X, "Uninstall", () => _shell.SetStatus("Not implemented yet: uninstall")),
            new(Key.P, "Pin", () => _shell.SetStatus("Not implemented yet: pin")),
            new(new Key('/'), "Filter", _table.FocusFilter),
            new(Key.S, "Sort", _table.CycleSort),
            new(Key.R, "Reload", Reload),
            new(Key.Tab, "Pane", SwitchPane),
        ];
    }

    public override IReadOnlyList<KeyHint> Hints => _hints;

    public override void OnShown()
    {
        _table.FocusTable();
        if (!_hasLoaded)
        {
            _hasLoaded = true;
            Reload();
        }
    }

    private void Reload()
    {
        // Not disposed: a source without a timer holds no resources, and the running load may still read its token.
        _loadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        _table.IsLoading = true;

        var app = _shell.App;
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var rows = await _client.ListInstalledAsync(token);
                app.Invoke(() => ShowRows(cancellation, rows));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                app.Invoke(() => ShowFailure(cancellation, ex));
            }
        });
    }

    private void ShowRows(CancellationTokenSource cancellation, IReadOnlyList<PackageRow> rows)
    {
        if (cancellation != _loadCancellation)
        {
            return;
        }

        _table.SetRows(rows);
        _table.IsLoading = false;
        _shell.SetTabCount(this, rows.Count);
    }

    private void ShowFailure(CancellationTokenSource cancellation, Exception ex)
    {
        if (cancellation != _loadCancellation)
        {
            return;
        }

        _table.IsLoading = false;
        _shell.SetError($"winget: {ex.Message}");
    }

    private void SwitchPane()
    {
        if (_details.HasFocus)
        {
            _table.FocusTable();
        }
        else
        {
            _details.SetFocus();
        }
    }

    /// <summary>
    /// Sizes the panes from this tab's width, which is final by the time its subviews are laid
    /// out; a <c>Dim.Func</c> reading it could see the previous frame's width.
    /// </summary>
    protected override void OnSubViewLayout(LayoutEventArgs args)
    {
        base.OnSubViewLayout(args);

        var tabWidth = Viewport.Width;
        var leftWidth = tabWidth >= WideLayoutWidth ? WideLeftPaneWidth : tabWidth * NarrowLeftPanePercent / 100;
        if (leftWidth != _leftPaneWidth)
        {
            _leftPaneWidth = leftWidth;
            _table.Width = leftWidth;
            _divider.X = leftWidth;
            _details.X = leftWidth + 1;
        }
    }
}
