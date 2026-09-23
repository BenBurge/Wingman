using Terminal.Gui.Input;
using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// Every installed package, loaded from <see cref="IWingetClient.ListInstalledAsync"/> the first
/// time the tab is shown or another tab needs the installed set, again on <c>r</c>, and after
/// every operation. Pinned packages are marked <c>⊘</c>.
/// </summary>
internal sealed class InstalledTab : PackageListTab
{
    private const string PinnedMarker = "⊘";

    private static readonly PackageColumn[] Columns =
    [
        new("Name", row => row.Name, 24),
        new("Id", row => row.Id, 0),
        new("Version", row => row.Version, 14, VersionComparer.Instance),
    ];

    private readonly KeyHint[] _hints;
    private readonly KeyHint[] _pinnedRowHints;
    private bool _hasStartedLoading;

    public InstalledTab(Shell shell, IWingetClient client)
        : base(shell, client, "Installed", Columns)
    {
        Table.Marker = row => shell.IsPinned(row.Id) ? PinnedMarker : "";
        Table.MarkerScheme = shell.Theme.CellScheme(shell.Theme.Dim);

        _hints = BuildHints("Pin");
        _pinnedRowHints = BuildHints("Unpin");
    }

    protected override IReadOnlyList<KeyHint> TableHints => IsCursorRowPinned ? _pinnedRowHints : _hints;

    public override void OnShown()
    {
        FocusTableOrLog();
        EnsureLoaded();
    }

    /// <summary>Starts the first load unless one has already started.</summary>
    public void EnsureLoaded()
    {
        if (!_hasStartedLoading)
        {
            Reload();
        }
    }

    /// <summary>Reloads after every operation from any tab, since each changes what is installed and Discover's <c>✓</c> markers come from this list.</summary>
    public override void RefreshAfterOperation(bool isOrigin) => Reload();

    protected override void OnLoaded(IReadOnlyList<PackageRow> rows)
    {
        Shell.SetTabCount(this, rows.Count);
        Shell.SetInstalled(rows);
    }

    private KeyHint[] BuildHints(string pinLabel) =>
    [
        new(Key.U, "Upgrade", () => RunOperation(OperationKind.Upgrade)),
        new(Key.X, "Uninstall", () => RunOperation(OperationKind.Uninstall)),
        new(Key.P, pinLabel, TogglePin),
        new(new Key('/'), "Filter", Table.FocusFilter),
        new(Key.S, "Sort", Table.CycleSort),
        new(Key.R, "Reload", Reload),
        new(Key.Tab, "Pane", SwitchPane),
    ];

    private void Reload()
    {
        _hasStartedLoading = true;
        Load(Client.ListInstalledAsync);
    }
}
