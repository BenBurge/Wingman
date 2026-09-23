using Terminal.Gui.Input;
using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// Searches winget with <see cref="IWingetClient.SearchAsync"/> when Enter is pressed in the
/// search box, never per keystroke, since each search is a winget process. Installed packages
/// are marked <c>✓</c>; <c>r</c> runs the last search again, and <c>i</c> installs the cursor row.
/// </summary>
internal sealed class DiscoverTab : PackageListTab
{
    private const string InstalledMarker = "✓";

    private static readonly PackageColumn[] Columns =
    [
        new("Name", row => row.Name, 23),
        new("Id", row => row.Id, 0),
        new("Version", row => row.Version, 10, VersionComparer.Instance),
    ];

    private readonly KeyHint[] _hints;
    private string? _lastQuery;

    public DiscoverTab(Shell shell, IWingetClient client)
        : base(shell, client, "Discover", Columns)
    {
        Table.Prompt = "Search:";
        Table.FiltersRows = false;
        Table.CountText = "";
        Table.EmptyText = "Type a query and press Enter";
        Table.Footer = $"{InstalledMarker} installed";
        Table.Marker = row => shell.InstalledIds.Contains(row.Id) ? InstalledMarker : "";
        Table.MarkerScheme = shell.Theme.CellScheme(shell.Theme.Ok);
        Table.QuerySubmitted += Search;
        shell.InstalledChanged += Table.RefreshMarkers;

        _hints =
        [
            new(Key.I, "Install", () => RunOperation(OperationKind.Install)),
            new(Key.Enter, "Search", () => Search(Table.Filter), "⏎"),
            new(new Key('/'), "Search box", Table.FocusFilter),
            new(Key.Tab, "Pane", SwitchPane),
        ];
    }

    protected override IReadOnlyList<KeyHint> TableHints => _hints;

    public override void OnShown()
    {
        Shell.EnsureInstalledLoaded();
        if (_lastQuery is null)
        {
            Table.FocusFilter();
        }
        else
        {
            FocusTableOrLog();
        }
    }

    /// <summary>Only the tab that ran the operation searches again; its <c>✓</c> markers follow the Installed tab by themselves.</summary>
    public override void RefreshAfterOperation(bool isOrigin)
    {
        if (isOrigin && _lastQuery is not null)
        {
            Search(_lastQuery);
        }
    }

    protected override void OnLoaded(IReadOnlyList<PackageRow> rows)
    {
        Table.CountText = rows.Count == 1 ? "1 result" : $"{rows.Count} results";
        Table.EmptyText = $"No packages match \"{_lastQuery}\"";
    }

    /// <summary>
    /// <c>r</c> reruns the last search. It is not on the key bar, so the shell does not dispatch
    /// it; this sees it only after the focused view has passed on it, so typing an r in the search
    /// box never gets here.
    /// </summary>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.R)
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
