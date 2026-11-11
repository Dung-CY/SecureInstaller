using Installer.Core.Platform;
using Microsoft.Win32;

namespace Installer.Core.Tests.Fakes;

public sealed class FakeRegistry : IRegistryReader
{
    public sealed class Key
    {
        public List<string> SubKeys { get; } = new();
        public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<(RegistryHive, RegistryView, string), Key> _keys = new();

    private static string Norm(string p) => p.Trim('\\').ToLowerInvariant();

    private Key Ensure(RegistryHive hive, RegistryView view, string path)
    {
        var id = (hive, view, Norm(path));
        if (_keys.TryGetValue(id, out var k)) return k;
        k = new Key();
        _keys[id] = k;
        var trimmed = path.Trim('\\');
        var i = trimmed.LastIndexOf('\\');
        if (i > 0)
        {
            var parent = Ensure(hive, view, trimmed[..i]);
            var name = trimmed[(i + 1)..];
            if (!parent.SubKeys.Contains(name, StringComparer.OrdinalIgnoreCase)) parent.SubKeys.Add(name);
        }
        return k;
    }

    public void AddKey(RegistryHive hive, RegistryView view, string path, Dictionary<string, object?>? values = null)
    {
        var k = Ensure(hive, view, path);
        if (values != null) foreach (var kv in values) k.Values[kv.Key] = kv.Value;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string path) =>
        _keys.TryGetValue((hive, view, Norm(path)), out var k) ? k.SubKeys.ToList() : new List<string>();

    public object? GetValue(RegistryHive hive, RegistryView view, string path, string name) =>
        _keys.TryGetValue((hive, view, Norm(path)), out var k) && k.Values.TryGetValue(name, out var v) ? v : null;
}
