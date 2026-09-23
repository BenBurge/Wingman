using Terminal.Gui.Input;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Updates;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// Installed packages with an upgrade available, loaded from
/// <see cref="IWingetClient.ListUpgradesAsync"/> the first time the tab is shown, again on
/// <c>r</c>, and after every batch once it has loaded, then run through
/// <see cref="UpdatesFilter"/>: packages excluded from Wingman's updates and skipped versions leave
/// the list, and the count label and footer say how many. The tab strip shows how many are listed
/// once a load has finished. Held (pinned) packages stay listed, dimmed and marked <c>⊘</c>. Space
/// marks a row for the batch, <c>a</c> marks every listed row an upgrade-all would take, and
/// <c>e</c> lists the excluded packages in place of the right pane.
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
        new("p", "update policy"),
        new("e", "list excluded packages"),
        new("o", "install options"),
        new("b", "export or import a bundle"),
        new("␣", "mark for batch"),
        new("a", "mark all but held"),
        new("c", "clear queue"),
        new("g", "run queue"),
    ]);

    private readonly KeyHint[] _hints;
    private readonly ExcludedPane _excludedPane;

    // Every row the last load returned, before the policy filter.
    private IReadOnlyList<PackageRow> _rows = [];
    private UpdatesView _view = new([], 0, 0, 0);
    private bool _hasStartedLoading;

    public UpdatesTab(Shell shell, IWingetClient client)
        : base(shell, client, "Updates", Columns)
    {
        Table.Marker = Marker;
        Table.MarkerColor = theme => theme.Accent;
        Table.RowColor = (row, theme) => shell.IsPinned(row.Id) ? theme.Dim : null;
        Table.Footer = "";
        Table.CountFormat = CountText;

        _excludedPane = new ExcludedPane(shell.Theme);
        AddExtraPane(_excludedPane);

        _hints = BuildHints();
    }

    protected override IReadOnlyList<KeyHint> TableHints => _hints;

    protected override HelpGroup TabHelp => Help;

    public override void OnShown()
    {
        FocusContent();
        LoadIfNeeded();
    }

    public override void LoadIfNeeded()
    {
        if (!_hasStartedLoading)
        {
            Reload();
        }
    }

    /// <summary>Reloads once the tab has loaded, since any batch can add or remove an upgrade.</summary>
    public override void RefreshAfterOperation(bool isOrigin)
    {
        if (_hasStartedLoading)
        {
            Reload();
        }
    }

    protected override IReadOnlyList<PackageRow> RowsToShow(IReadOnlyList<PackageRow> loaded)
    {
        _rows = loaded;
        return Filter();
    }

    protected override void OnLoaded(IReadOnlyList<PackageRow> rows) => ShowCounts();

    protected override void OnPinsChanged()
    {
        base.OnPinsChanged();
        ApplyPolicies();
    }

    protected override void OnOptionsChanged()
    {
        base.OnOptionsChanged();
        ApplyPolicies();
    }

    protected override void ToggleMark(PackageRow row) => ToggleQueued(OperationKind.Upgrade, row);

    protected override IReadOnlyList<MenuEntry> MenuEntries(PackageRow row) =>
    [
        new(UpgradeLabel(row), () => RunOperation(OperationKind.Upgrade, row)),
        VersionMenuEntry(OperationKind.Upgrade, row),
        MarkMenuEntry(row),
        PolicyMenuEntry(row),
        OptionsMenuEntry(row),
        MenuEntry.Rule,
        .. PackageMenuEntries(row),
    ];

    /// <summary>The policy filter over the last load's rows; also refreshes the excluded list.</summary>
    private IReadOnlyList<PackageRow> Filter()
    {
        _view = UpdatesFilter.Apply(_rows, Shell.Pins, Shell.Options);

        var excluded = new List<PackageRow>();
        foreach (var row in _rows)
        {
            if (Shell.ResolvePolicy(row) == UpdatePolicyKind.Exclude)
            {
                excluded.Add(row);
            }
        }

        _excludedPane.SetRows(excluded);
        return [.. _view.Visible.Select(update => update.Row)];
    }

    /// <summary>Filters the last load's rows again, for when a pin or a package's options changed.</summary>
    private void ApplyPolicies()
    {
        Table.SetRows(Filter());
        ShowCounts();
    }

    private void ShowCounts()
    {
        if (_hasStartedLoading && !Table.IsLoading)
        {
            Shell.SetTabCount(this, _view.Visible.Count);
        }

        Table.RefreshCount();
        UpdateFooter();
    }

    /// <summary>
    /// <c>6 available · 3 marked · 1 held · 2 excluded</c>, or <c>2 of 6 available · …</c> while the
    /// filter hides some; zero counts are left out.
    /// </summary>
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

        if (_view.ExcludedCount > 0)
        {
            parts.Add($"{_view.ExcludedCount} excluded");
        }

        if (_view.SkippedCount > 0)
        {
            parts.Add($"{_view.SkippedCount} skipped");
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

    /// <summary>Opens the policy for the selected excluded package while its list has focus, and for the cursor row otherwise.</summary>
    private void OpenPolicyForFocusedRow()
    {
        if (_excludedPane.HasFocus && _excludedPane.SelectedRow is { } excluded)
        {
            OpenPolicy(excluded);
        }
        else
        {
            OpenPolicyForCursorRow();
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

    /// <summary>The legend for each marker the rows use and the excluded count, held first, since together they are wider than the pane.</summary>
    private void UpdateFooter()
    {
        var shown = _view.Visible.Select(update => update.Row).ToList();
        var legends = new List<string>();
        if (shown.Any(row => Shell.IsPinned(row.Id)))
        {
            legends.Add(HeldFooter);
        }

        if (_view.ExcludedCount > 0)
        {
            legends.Add($"⟳ {_view.ExcludedCount} excluded · e to list them");
        }

        if (shown.Any(row => row.RequiresExplicitTargeting))
        {
            legends.Add(ExplicitFooter);
        }

        Table.Footer = string.Join(FooterSeparator, legends);
    }

    /// <remarks>
    /// <c>/ Filter</c> and <c>s Sort</c> stay off the bar so the batch keys fit at 96 columns, as do
    /// <c>e Excluded</c>, which the footer names, <c>o Options</c>, <c>b Bundle</c>, and <c>Tab Pane</c> and
    /// <c>m Menu</c>, which the tab and the shell handle by themselves.
    /// </remarks>
    private KeyHint[] BuildHints() =>
    [
        new(Key.U, "Upgrade", () => RunOperation(OperationKind.Upgrade)),
        MarkHint,
        new(Key.A, "Mark all", MarkAll),
        ClearHint,
        RunHint,
        new(Key.P, "Policy", OpenPolicyForFocusedRow),
        new(Key.R, "Refresh", Reload),
        new(Key.E, "Excluded", ToggleExtraPane, IsOnBar: false),
        new(new Key('/'), "Filter", Table.FocusFilter, IsOnBar: false),
        new(Key.S, "Sort", Table.CycleSort, IsOnBar: false),
        OptionsHint,
        BundleHint,
    ];

    private void Reload()
    {
        _hasStartedLoading = true;
        Load(Client.ListUpgradesAsync);
    }
}
