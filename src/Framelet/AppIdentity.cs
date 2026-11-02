using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Framelet;

internal static class AppIdentity
{
    internal const string Name = "Snipsera";
    internal const string LegacyName = "Framelet";
    internal const string ProjectExtension = ".snipsera";

    internal static string DataRoot(string localAppData)
    {
        string current = Path.Combine(localAppData, Name);
        string legacy = Path.Combine(localAppData, LegacyName);
        return Directory.Exists(current) || !Directory.Exists(legacy) ? current : legacy;
    }

    internal static bool IsProject(string path) =>
        Path.GetExtension(path).Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".framelet", StringComparison.OrdinalIgnoreCase);

    // Keep the original mutex scheme: two brands must never write the same history concurrently.
    internal static string InstanceName(string root)
    {
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant())))[..20];
        return "Local\\Framelet-" + identity;
    }
}
