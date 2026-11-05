using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Framelet;

internal sealed record ReleaseVersion(Version Core, string? Prerelease) : IComparable<ReleaseVersion>
{
    internal static ReleaseVersion? Parse(string text)
    {
        var match = Regex.Match(text, @"^v?(\d+\.\d+\.\d+(?:\.\d+)?)(?:-([0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant);
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var core)) return null;
        return new(new Version(core.Major, core.Minor, core.Build, Math.Max(0, core.Revision)), match.Groups[2].Success ? match.Groups[2].Value : null);
    }
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        int main = Core.CompareTo(other.Core); if (main != 0) return main;
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;
        var left = Prerelease.Split('.'); var right = other.Prerelease.Split('.');
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            bool ln = ulong.TryParse(left[i], NumberStyles.None, CultureInfo.InvariantCulture, out ulong l);
            bool rn = ulong.TryParse(right[i], NumberStyles.None, CultureInfo.InvariantCulture, out ulong r);
            int result = ln && rn ? l.CompareTo(r) : ln != rn ? (ln ? -1 : 1) : string.CompareOrdinal(left[i], right[i]);
            if (result != 0) return result;
        }
        return left.Length.CompareTo(right.Length);
    }
}
