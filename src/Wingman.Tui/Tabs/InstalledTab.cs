using Terminal.Gui.Input;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// Every installed package, loaded from <see cref="IWingetClient.ListInstalledAsync"/> the first
/// time the tab is shown or another tab needs the installed set, again on <c>r</c>, and after
/// every batch. Pinned packages are marked <c>⊘</c>. Space marks a row with an upgrade
/// available for the batch.
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

    private static readonly HelpGroup Help = new("Installed",
    [
        new("u", "upgrade"),
        new("x", "uninstall"),
        new("p", "pin / unpin"),
        new("␣", "mark for batch"),
        new("c", "clear queue"),
        new("g", "run queue"),
        new("⏎", "details"),
    ]);

    private readonly KeyHint[] _hints;
    private readonly KeyHint[] _pinnedRowHints;
    private bool _hasStartedLoading;

    public InstalledTab(Shell shell, IWingetClient client)
        : base(shell, client, "Installed", Columns)
    {
        Table.Marker = row => shell.IsPinned(row.Id) ? PinnedMarker : "";
        Table.MarkerScheme = shell.Theme.CellScheme(shell.Theme.Dim);
        Table.CountFormat = (visible, all) => $"{visible.Count} of {all.Count}" + MarkedSuffix(all);

        _hints = BuildHints("Pin");
        _pinnedRowHints = BuildHints("Unpin");
    }

    protected override IReadOnlyList<KeyHint> TableHints => IsCursorRowPinned ? _pinnedRowHints : _hints;

    protected override HelpGroup TabHelp => Help;

    public override void OnShown()
    {
        FocusTableOrBatch();
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

    /// <summary>Reloads after every batch from any tab, since each can change what is installed and Discover's <c>✓</c> markers come from this list.</summary>
    public override void RefreshAfterOperation(bool isOrigin) => Reload();

    protected override void OnLoaded(IReadOnlyList<PackageRow> rows)
    {
        Shell.SetTabCount(this, rows.Count);
        Shell.SetInstalled(rows);
    }

    /// <summary>Queues an upgrade; a row with nothing newer has nothing to queue, since uninstall stays a one-package action.</summary>
    protected override void ToggleMark(PackageRow row)
    {
        var hasUpgrade = !string.IsNullOrEmpty(row.AvailableVersion);
        if (!hasUpgrade && !Shell.Queue.Contains(row.Id))
        {
            Shell.SetStatus($"Nothing to upgrade for {row.Id}; use x to uninstall");
            return;
        }

        ToggleQueued(OperationKind.Upgrade, row);
    }

    protected override IReadOnlyList<MenuEntry> MenuEntries(PackageRow row)
    {
        var entries = new List<MenuEntry>();
        var hasUpgrade = !string.IsNullOrEmpty(row.AvailableVersion);
        if (hasUpgrade)
        {
            entries.Add(new(UpgradeLabel(row), () => RunOperation(OperationKind.Upgrade, row)));
        }

        if (hasUpgrade || Shell.Queue.Contains(row.Id))
        {
            entries.Add(MarkMenuEntry(row));
        }

        entries.Add(new("Uninstall", () => RunOperation(OperationKind.Uninstall, row)));
        entries.Add(new(Shell.IsPinned(row.Id) ? "Unpin" : "Pin", () => Shell.TogglePin(row.Id)));
        entries.Add(MenuEntry.Rule);
        entries.AddRange(PackageMenuEntries(row));
        return entries;
    }

    /// <remarks>
    /// <c>s Sort</c> and <c>r Reload</c> stay off the bar so the batch keys fit at 96 columns, but
    /// still work; <c>m Menu</c> and <c>Tab Pane</c> are left out too, since the shell handles
    /// <c>m</c> and the tab handles Tab by themselves.
    /// </remarks>
    private KeyHint[] BuildHints(string pinLabel) =>
    [
        new(Key.U, "Upgrade", () => RunOperation(OperationKind.Upgrade)),
        new(Key.X, "Uninstall", () => RunOperation(OperationKind.Uninstall)),
        new(Key.P, pinLabel, TogglePin),
        MarkHint,
        ClearHint,
        RunHint,
        new(new Key('/'), "Filter", Table.FocusFilter),
        new(Key.S, "Sort", Table.CycleSort, IsOnBar: false),
        new(Key.R, "Reload", Reload, IsOnBar: false),
    ];

    private void Reload()
    {
        _hasStartedLoading = true;
        Load(Client.ListInstalledAsync);
    }
}
