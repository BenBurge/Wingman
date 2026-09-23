using Terminal.Gui.Input;
using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// Installed packages with an upgrade available, loaded from
/// <see cref="IWingetClient.ListUpgradesAsync"/> the first time the tab is shown, again on
/// <c>r</c>, and after every operation once it has loaded. The tab strip shows how many there are
/// once a load has finished. Held (pinned) packages stay listed, dimmed and marked <c>⊘</c>.
/// </summary>
internal sealed class UpdatesTab : PackageListTab
{
    private const string HeldMarker = "⊘";
    private const string HeldFooter = "⊘ held (winget pin --blocking)";
    private const string ExplicitMarker = "!";
    private const string ExplicitFooter = "! needs explicit targeting (winget upgrade --id)";
    private const string FooterSeparator = "  ";

    private static readonly PackageColumn[] Columns =
    [
        new("Name", row => row.Name, 16),
        new("Id", row => row.Id, 0),
        new("Version", row => row.Version, 11, VersionComparer.Instance),
        new("Available", row => row.AvailableVersion ?? "", 11, VersionComparer.Instance),
    ];

    private readonly KeyHint[] _hints;
    private readonly KeyHint[] _heldRowHints;
    private IReadOnlyList<PackageRow> _rows = [];
    private bool _hasStartedLoading;

    public UpdatesTab(Shell shell, IWingetClient client)
        : base(shell, client, "Updates", Columns)
    {
        var heldScheme = shell.Theme.CellScheme(shell.Theme.Dim);
        Table.Marker = Marker;
        Table.MarkerScheme = shell.Theme.CellScheme(shell.Theme.Accent);
        Table.RowScheme = row => shell.IsPinned(row.Id) ? heldScheme : null;
        Table.Footer = "";

        _hints = BuildHints("Hold");
        _heldRowHints = BuildHints("Release");
    }

    protected override IReadOnlyList<KeyHint> TableHints => IsCursorRowPinned ? _heldRowHints : _hints;

    public override void OnShown()
    {
        FocusTableOrLog();
        if (!_hasStartedLoading)
        {
            Reload();
        }
    }

    /// <summary>Reloads once the tab has loaded, since any operation can add or remove an upgrade.</summary>
    public override void RefreshAfterOperation(bool isOrigin)
    {
        if (_hasStartedLoading)
        {
            Reload();
        }
    }

    protected override void OnLoaded(IReadOnlyList<PackageRow> rows)
    {
        _rows = rows;
        Shell.SetTabCount(this, rows.Count);
        UpdateFooter();
    }

    protected override void OnPinsChanged()
    {
        base.OnPinsChanged();
        UpdateFooter();
    }

    private string Marker(PackageRow row)
    {
        if (Shell.IsPinned(row.Id))
        {
            return HeldMarker;
        }

        return row.RequiresExplicitTargeting ? ExplicitMarker : "";
    }

    /// <summary>The legend for each marker the rows use, held first, since the pair is wider than the pane.</summary>
    private void UpdateFooter()
    {
        var legends = new List<string>();
        if (_rows.Any(row => Shell.IsPinned(row.Id)))
        {
            legends.Add(HeldFooter);
        }

        if (_rows.Any(row => row.RequiresExplicitTargeting))
        {
            legends.Add(ExplicitFooter);
        }

        Table.Footer = string.Join(FooterSeparator, legends);
    }

    private KeyHint[] BuildHints(string pinLabel) =>
    [
        new(Key.U, "Upgrade", () => RunOperation(OperationKind.Upgrade)),
        new(Key.P, pinLabel, TogglePin),
        new(Key.R, "Refresh", Reload),
        new(new Key('/'), "Filter", Table.FocusFilter),
        new(Key.S, "Sort", Table.CycleSort),
        new(Key.Tab, "Pane", SwitchPane),
    ];

    private void Reload()
    {
        _hasStartedLoading = true;
        Load(Client.ListUpgradesAsync);
    }
}
