using Installer.Core.Versioning;

namespace Installer.Core.Model;

public static class BundleResolver
{
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
                    if (pick is null || (VersionComparer.Compare(p.Version, pick.Version) ?? 0) > 0) pick = p;
            result.Add((item, pick));
        }
        return result;
    }
}
