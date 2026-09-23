using Terminal.Gui.Input;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// Installed packages with an upgrade available, loaded from
/// <see cref="IWingetClient.ListUpgradesAsync"/> the first time the tab is shown, again on
/// <c>r</c>, and after every operation once it has loaded. The tab strip shows how many there are
/// once a load has finished. Held (pinned) packages stay listed, dimmed and marked <c>⊘</c>.
/// Space marks a row for the batch, and <c>a</c> marks every listed row an upgrade-all would take.
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

    private static readonly HelpGroup Help = new("Updates",
    [
        new("u", "upgrade"),
        new("p", "hold / release"),
        new("␣", "mark for batch"),
        new("a", "mark all but held"),
        new("c", "clear queue"),
        new("g", "run queue"),
    ]);

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
        Table.CountFormat = CountText;

        _hints = BuildHints("Hold");
        _heldRowHints = BuildHints("Release");
    }

    protected override IReadOnlyList<KeyHint> TableHints => IsCursorRowPinned ? _heldRowHints : _hints;

    protected override HelpGroup TabHelp => Help;

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
        Table.RefreshCount();
        UpdateFooter();
    }

    protected override void ToggleMark(PackageRow row) => ToggleQueued(OperationKind.Upgrade, row);

    /// <summary><c>6 available · 3 marked · 1 held</c>, or <c>2 of 6 available · …</c> while the filter hides some; zero counts are left out.</summary>
    private string CountText(IReadOnlyList<PackageRow> visible, IReadOnlyList<PackageRow> all)
    {
        var parts = new List<string>
        {
            visible.Count == all.Count ? $"{all.Count} available" : $"{visible.Count} of {all.Count} available",
        };

        var marked = MarkedCount(all);
        if (marked > 0)
        {
            parts.Add($"{marked} marked");
        }

        var held = all.Count(row => Shell.IsPinned(row.Id));
        if (held > 0)
        {
            parts.Add($"{held} held");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Queues an upgrade for every row the filter shows except held ones, which winget refuses, and
    /// those needing explicit targeting, which <c>winget upgrade --all</c> skips too.
    /// </summary>
    private void MarkAll()
    {
        foreach (var row in Table.VisibleRows)
        {
            var isEligible = !Shell.IsPinned(row.Id) && !row.RequiresExplicitTargeting;
            if (isEligible && !Shell.Queue.Contains(row.Id))
            {
                Shell.Queue.Add(Shell.BuildOperation(OperationKind.Upgrade, row));
            }
        }
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

    protected override IReadOnlyList<MenuEntry> MenuEntries(PackageRow row) =>
    [
        new(UpgradeLabel(row), () => RunOperation(OperationKind.Upgrade, row)),
        MarkMenuEntry(row),
        new(Shell.IsPinned(row.Id) ? "Release" : "Hold", () => Shell.TogglePin(row.Id)),
        MenuEntry.Rule,
        .. PackageMenuEntries(row),
    ];

    /// <remarks>
    /// <c>/ Filter</c> and <c>s Sort</c> stay off the bar so the batch keys fit at 96 columns, as do
    /// <c>Tab Pane</c> and <c>m Menu</c>, which the tab and the shell handle by themselves.
    /// </remarks>
    private KeyHint[] BuildHints(string pinLabel) =>
    [
        new(Key.U, "Upgrade", () => RunOperation(OperationKind.Upgrade)),
        MarkHint,
        new(Key.A, "Mark all", MarkAll),
        ClearHint,
        RunHint,
        new(Key.P, pinLabel, TogglePin),
        new(Key.R, "Refresh", Reload),
        new(new Key('/'), "Filter", Table.FocusFilter, IsOnBar: false),
        new(Key.S, "Sort", Table.CycleSort, IsOnBar: false),
    ];

    private void Reload()
    {
        _hasStartedLoading = true;
        Load(Client.ListUpgradesAsync);
    }
}
