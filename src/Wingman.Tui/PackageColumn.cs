using Wingman.Core.Models;

namespace Wingman.Tui;

/// <summary>
/// One column of a <see cref="PackageTable"/>. <paramref name="Width"/> counts the one-cell gap
/// after the text; zero or less makes the column fill whatever the fixed columns leave.
/// <paramref name="Comparer"/> orders the column when it is sorted, ordinal ignoring case when null.
/// </summary>
internal sealed record PackageColumn(
    string Header,
    Func<PackageRow, string> Value,
    int Width,
    IComparer<string>? Comparer = null);
