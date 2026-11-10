using System.Text.RegularExpressions;
using Installer.Core.Platform;

namespace Installer.Core.Tests.Fakes;

public sealed class FakeWmi : IWmi
{
    public Dictionary<string, List<Dictionary<string, object?>>> Rows { get; }
    public HashSet<string> Throws { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Queries { get; } = new();

    public FakeWmi(Dictionary<string, List<Dictionary<string, object?>>>? rows = null)
    {
        Rows = new(rows ?? new(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string wql, string scope = @"root\cimv2")
    {
        Queries.Add(wql);
        var m = Regex.Match(wql, @"\bFROM\s+(\w+)", RegexOptions.IgnoreCase);
        var cls = m.Success ? m.Groups[1].Value : "";
        if (Throws.Contains(cls)) throw new InvalidOperationException($"Simulated WMI failure for {cls}");
        return Rows.TryGetValue(cls, out var list)
            ? list.Select(d => (IReadOnlyDictionary<string, object?>)d).ToList()
            : new List<IReadOnlyDictionary<string, object?>>();
    }
}
