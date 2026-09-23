using System.Globalization;

namespace Wingman.Tui;

/// <summary>
/// Orders version strings so <c>2.10.0</c> sorts after <c>2.9.1</c>: segments split on <c>.</c>
/// compare numerically when both are whole numbers and ordinally, ignoring case, otherwise. Winget
/// prints versions such as <c>Unknown</c> or <c>&lt; 1.2</c>, which fall back to the ordinal rule.
/// </summary>
internal sealed class VersionComparer : IComparer<string>
{
    public static VersionComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        var left = (x ?? "").Split('.');
        var right = (y ?? "").Split('.');
        var shared = Math.Min(left.Length, right.Length);

        for (var i = 0; i < shared; i++)
        {
            var result = CompareSegment(left[i], right[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int CompareSegment(string left, string right)
    {
        var leftIsNumber = ulong.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
        var rightIsNumber = ulong.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
        if (leftIsNumber && rightIsNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }
}
