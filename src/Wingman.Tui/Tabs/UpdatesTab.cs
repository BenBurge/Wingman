using Terminal.Gui.Input;
using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// Installed packages with an upgrade available, loaded from
/// <see cref="IWingetClient.ListUpgradesAsync"/> the first time the tab is shown and again on
/// <c>r</c>. The tab strip shows how many there are once a load has finished.
/// </summary>
internal sealed class UpdatesTab : PackageListTab
{
    private const string ExplicitMarker = "!";
    private const string ExplicitFooter = "! needs explicit targeting (winget upgrade --id)";

    private static readonly PackageColumn[] Columns =
    [
        new("Name", row => row.Name, 16),
        new("Id", row => row.Id, 0),
        new("Version", row => row.Version, 12, VersionComparer.Instance),
        new("Available", row => row.AvailableVersion ?? "", 12, VersionComparer.Instance),
        new("Source", row => row.Source, 8),
    ];

    private readonly KeyHint[] _hints;
    private bool _hasStartedLoading;

    public UpdatesTab(Shell shell, IWingetClient client)
        : base(shell, client, "Updates", Columns)
    {
        Table.Marker = row => row.RequiresExplicitTargeting ? ExplicitMarker : "";
        Table.MarkerScheme = shell.Theme.CellScheme(shell.Theme.Accent);
        Table.Footer = "";

        _hints =
        [
            new(Key.U, "Upgrade", () => SetStubStatus("upgrade")),
            new(Key.P, "Hold", () => SetStubStatus("hold")),
            new(Key.R, "Refresh", Reload),
            new(new Key('/'), "Filter", Table.FocusFilter),
            new(Key.S, "Sort", Table.CycleSort),
            new(Key.Tab, "Pane", SwitchPane),
        ];
    }

    public override IReadOnlyList<KeyHint> Hints => _hints;

    public override void OnShown()
    {
        Table.FocusTable();
        if (!_hasStartedLoading)
        {
            _hasStartedLoading = true;
            Reload();
        }
    }

    protected override void OnLoaded(IReadOnlyList<PackageRow> rows)
    {
        Shell.SetTabCount(this, rows.Count);

        var anyNeedExplicitTargeting = rows.Any(row => row.RequiresExplicitTargeting);
        Table.Footer = anyNeedExplicitTargeting ? ExplicitFooter : "";
    }

    private void Reload() => Load(Client.ListUpgradesAsync);

    private void SetStubStatus(string action)
    {
        if (Table.CurrentRow is { } row)
        {
            Shell.SetStatus($"Not implemented yet: {action} {row.Id}");
        }
    }
}
