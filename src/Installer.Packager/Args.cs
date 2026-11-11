namespace Installer.Packager;

public record ParsedArgs(string Command, List<string> Positionals, Dictionary<string, string?> Options);

public static class Args
{
    public static readonly string[] Commands =
    {
        "init", "add-package", "update-package", "remove-package", "add-bundle", "remove-bundle", "add-user", "remove-user", "export-logs", "fingerprint"
    };

    public static ParsedArgs Parse(string[] argv)
    {
        if (argv == null || argv.Length == 0)
            throw new ArgumentException("Thiếu lệnh.");
        var cmd = argv[0];
        if (!Commands.Contains(cmd, StringComparer.Ordinal))
            throw new ArgumentException($"Lệnh không hợp lệ: {cmd}");

        var positionals = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < argv.Length; i++)
        {
            var t = argv[i];
            if (t.StartsWith("--", StringComparison.Ordinal))
            {
                var name = t[2..];
                string? value = null;
                var eq = name.IndexOf('=');
                if (eq >= 0) { value = name[(eq + 1)..]; name = name[..eq]; }
                else if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal))
                    value = argv[++i];
                options[name] = value;
            }
            else positionals.Add(t);
        }
        return new ParsedArgs(cmd, positionals, options);
    }
}
