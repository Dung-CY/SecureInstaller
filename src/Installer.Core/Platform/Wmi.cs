using System.Management;

namespace Installer.Core.Platform;

public sealed class Wmi : IWmi
{
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string wql, string scope = @"root\cimv2")
    {
        var result = new List<IReadOnlyDictionary<string, object?>>();
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql));
        using var collection = searcher.Get();
        foreach (ManagementBaseObject mo in collection)
        {
            using (mo)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (PropertyData p in mo.Properties) row[p.Name] = p.Value;
                result.Add(row);
            }
        }
        return result;
    }
}
