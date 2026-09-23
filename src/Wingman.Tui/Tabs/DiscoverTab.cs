using Terminal.Gui.Input;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// Searches winget with <see cref="IWingetClient.SearchAsync"/> when Enter is pressed in the
/// search box, never per keystroke, since each search is a winget process. Installed packages
/// are marked <c>✓</c>; <c>r</c> runs the last search again, <c>i</c> installs the cursor row, and
/// Space marks it for the batch.
/// </summary>
internal sealed class DiscoverTab : PackageListTab
{
    private const string InstalledMarker = "✓";
    private const string Legend = "✓ installed   ● marked for batch";

    private static readonly PackageColumn[] Columns =
    [
        new("Name", row => row.Name, 23),
        new("Id", row => row.Id, 0),
        new("Version", row => row.Version, 10, VersionComparer.Instance),
    ];

    private static readonly HelpGroup Help = new("Discover",
    [
        new("⏎", "search"),
        new("i", "install"),
        new("␣", "mark for batch"),
        new("c", "clear queue"),
        new("g", "run queue"),
        new("o", "install options"),
        new("⏎", "on a row: details"),
    ]);

    private readonly KeyHint[] _hints;
    private string? _lastQuery;

    public DiscoverTab(Shell shell, IWingetClient client)
        : base(shell, client, "Discover", Columns)
    {
        Table.Prompt = "Search:";
        Table.FiltersRows = false;
        Table.CountText = "";
        Table.EmptyText = "Type a query and press Enter";
        Table.Footer = Legend;
        Table.Marker = row => shell.IsInstalled(row.Id) ? InstalledMarker : "";
        Table.MarkerScheme = shell.Theme.CellScheme(shell.Theme.Ok);
        Table.QuerySubmitted += Search;
        shell.InstalledChanged += Table.RefreshMarkers;

        // Tab Pane is left off so the batch keys fit at 96 columns; Tab still switches panes.
        _hints =
        [
            new(Key.I, "Install", () => RunOperation(OperationKind.Install)),
            MarkHint,
            ClearHint,
            RunHint,
            new(Key.Enter, "Search", () => Search(Table.Filter), "⏎"),
            new(new Key('/'), "Search box", Table.FocusFilter),
            new(Key.M, "Menu", ShowContextMenu),
            OptionsHint,
        ];
    }

    protected override IReadOnlyList<KeyHint> TableHints => _hints;

    protected override HelpGroup TabHelp => Help;

    public override void OnShown()
    {
        Shell.EnsureInstalledLoaded();
        if (_lastQuery is null)
        {
            Table.FocusFilter();
        }
        else
        {
            FocusContent();
        }
    }

    /// <summary>Only the tab that ran the batch searches again; its <c>✓</c> markers follow the Installed tab by themselves.</summary>
    public override void RefreshAfterOperation(bool isOrigin)
    {
        if (isOrigin && _lastQuery is not null)
        {
            Search(_lastQuery);
        }
    }

    /// <summary>
    /// Queues an upgrade, from the installed row so the queue shows its version, when the package
    /// is installed and has one; an install otherwise.
    /// </summary>
    protected override void ToggleMark(PackageRow row)
    {
        var installed = Shell.FindInstalled(row.Id);
        var hasUpgrade = !string.IsNullOrEmpty(installed?.AvailableVersion);
        if (hasUpgrade)
        {
            ToggleQueued(OperationKind.Upgrade, installed!);
        }
        else
        {
            ToggleQueued(OperationKind.Install, row);
        }
    }

    protected override IReadOnlyList<MenuEntry> MenuEntries(PackageRow row)
    {
        var entries = new List<MenuEntry>
        {
            new("Install", () => RunOperation(OperationKind.Install, row)),
            MarkMenuEntry(row),
        };
        if (Shell.IsInstalled(row.Id))
        {
            entries.Add(new("Uninstall", () => RunOperation(OperationKind.Uninstall, row)));
        }

        entries.Add(OptionsMenuEntry(row));
        entries.Add(MenuEntry.Rule);
        entries.AddRange(PackageMenuEntries(row));
        return entries;
    }

    /// <summary>A search result has an update policy only once it is installed, and then the installed row carries it.</summary>
    protected override PackageRow? PolicyRow(PackageRow row) => Shell.FindInstalled(row.Id);

    protected override void OnLoaded(IReadOnlyList<PackageRow> rows)
    {
        Table.CountText = rows.Count == 1 ? "1 result" : $"{rows.Count} results";
        Table.EmptyText = $"No packages match \"{_lastQuery}\"";
    }

    /// <summary>
    /// <c>r</c> reruns the last search. It is not on the key bar, so the shell does not dispatch
    /// it; this sees it only after the focused view has passed on it, so typing an r in the search
    /// box never gets here, nor while the batch screen hides the results.
    /// </summary>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.R && !IsBatchShown)
        {
            if (_lastQuery is not null)
            {
                Search(_lastQuery);
            }

            return true;
        }

        return base.OnKeyDown(key);
    }

    private void Search(string query)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        _lastQuery = trimmed;
        Load(ct => Client.SearchAsync(trimmed, ct));
    }
}
