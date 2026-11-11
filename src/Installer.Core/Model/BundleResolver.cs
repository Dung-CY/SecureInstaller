using Installer.Core.Versioning;

namespace Installer.Core.Model;

public static class BundleResolver
{
    static bool Comparable(string v) => VersionComparer.Compare(v, v) is not null;

    static bool IsBetter(string a, string b)
    {
        bool ca = Comparable(a), cb = Comparable(b);
        if (ca != cb) return ca;
        if (ca && VersionComparer.Compare(a, b) is { } c && c != 0) return c > 0;
        return string.CompareOrdinal(a, b) > 0;
    }

    /// <summary>
    /// Resolves each bundle item to a catalog package (id matched OrdinalIgnoreCase, order follows b.Items, missing gives null).
    /// With an explicit Version the exact version is used. Without one, the "latest" among same-id entries is chosen
    /// independent of catalog order: a version with digits (comparable) beats one without; among comparable versions the
    /// highest by VersionComparer wins; ties, and the all-non-comparable case, go to the greater string by
    /// string.CompareOrdinal.
    /// </summary>
    public static List<(BundleItem Item, PackageEntry? Package)> Resolve(Catalog c, Bundle b)
    {
        var result = new List<(BundleItem, PackageEntry?)>();
        foreach (var item in b.Items)
        {
            var matches = c.Packages.Where(p => string.Equals(p.Id, item.PackageId, StringComparison.OrdinalIgnoreCase));
            PackageEntry? pick = null;
            if (item.Version is not null)
                pick = matches.FirstOrDefault(p => string.Equals(p.Version, item.Version, StringComparison.OrdinalIgnoreCase));
            else
                foreach (var p in matches)
                    if (pick is null || IsBetter(p.Version, pick.Version)) pick = p;
            result.Add((item, pick));
        }
        return result;
    }
}
