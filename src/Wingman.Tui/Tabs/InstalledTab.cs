using Terminal.Gui.Input;
using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// Every installed package, loaded from <see cref="IWingetClient.ListInstalledAsync"/> the first
/// time the tab is shown or another tab needs the installed set, and again on <c>r</c>.
/// </summary>
internal sealed class InstalledTab : PackageListTab
{
    private static readonly PackageColumn[] Columns =
    [
        new("Name", row => row.Name, 24),
        new("Id", row => row.Id, 0),
        new("Version", row => row.Version, 14, VersionComparer.Instance),
    ];

    private readonly KeyHint[] _hints;
    private bool _hasStartedLoading;

    public InstalledTab(Shell shell, IWingetClient client)
        : base(shell, client, "Installed", Columns)
    {
        _hints =
        [
            new(Key.U, "Upgrade", () => Shell.SetStatus("Not implemented yet: upgrade")),
            new(Key.X, "Uninstall", () => Shell.SetStatus("Not implemented yet: uninstall")),
            new(Key.P, "Pin", () => Shell.SetStatus("Not implemented yet: pin")),
            new(new Key('/'), "Filter", Table.FocusFilter),
            new(Key.S, "Sort", Table.CycleSort),
            new(Key.R, "Reload", Reload),
            new(Key.Tab, "Pane", SwitchPane),
        ];
    }

    public override IReadOnlyList<KeyHint> Hints => _hints;

    public override void OnShown()
    {
        Table.FocusTable();
        EnsureLoaded();
    }

    /// <summary>Starts the first load unless one has already started.</summary>
    public void EnsureLoaded()
    {
        if (!_hasStartedLoading)
        {
            _hasStartedLoading = true;
            Reload();
        }
    }

    protected override void OnLoaded(IReadOnlyList<PackageRow> rows)
    {
        Shell.SetTabCount(this, rows.Count);
        Shell.SetInstalled(rows);
    }

    private void Reload() => Load(Client.ListInstalledAsync);
}
