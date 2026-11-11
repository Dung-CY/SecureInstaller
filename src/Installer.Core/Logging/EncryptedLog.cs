using System.Text;
using System.Text.Json;
using Installer.Core.Crypto;
using Installer.Core.Model;
using Installer.Core.Platform;

namespace Installer.Core.Logging;

public sealed class EncryptedLog
{
    private readonly string _root;
    private readonly Guid _vaultId;
    private readonly byte[] _vk;
    private readonly bool _readOnly;
    private readonly string _fallback;
    private readonly object _lock = new();

    public string ActiveDir { get; private set; }

    public static string DefaultFallbackDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SecureInstaller", "logs");

    public EncryptedLog(string vaultRoot, Guid vaultId, byte[] vaultKey, bool vaultReadOnly, string? fallbackDir = null)
    {
        _root = vaultRoot;
        _vaultId = vaultId;
        _vk = vaultKey;
        _readOnly = vaultReadOnly;
        _fallback = fallbackDir ?? DefaultFallbackDir();
        ActiveDir = vaultReadOnly ? _fallback : Path.Combine(vaultRoot, "logs");
    }

    private string FileName(LogEntry e)
    {
        var host = e.Hostname ?? "";
        foreach (var c in Path.GetInvalidFileNameChars()) host = host.Replace(c, '_');
        return $"{_vaultId:N}-{e.Ts.ToLocalTime():yyyyMMdd}-{host}.log.enc";
    }

    public void Append(LogEntry e)
    {
        try
        {
            lock (_lock)
            {
                var line = Convert.ToBase64String(PackageCrypto.EncryptBytes(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(e, Json.Options)), _vk)) + "\n";
                var bytes = Encoding.ASCII.GetBytes(line);
                var name = FileName(e);
                try { WriteTo(ActiveDir, name, bytes); return; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Log write failed: " + ex.Message);
                    if (string.Equals(ActiveDir, _fallback, StringComparison.OrdinalIgnoreCase)) return;
                    ActiveDir = _fallback;
                }
                WriteTo(ActiveDir, name, bytes);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Log append failed: " + ex.Message);
        }
    }

    private void WriteTo(string dir, string name, byte[] bytes)
    {
        if (string.Equals(dir, _fallback, StringComparison.OrdinalIgnoreCase))
            SecureDirectory.CreateProtected(dir, SecureDirectory.IsElevated());
        else Directory.CreateDirectory(dir);
        AppendBytes(Path.Combine(dir, name), bytes);
    }

    // Appends bytes, isolating any torn last line (no trailing '\n') left by an unplug.
    private static void AppendBytes(string path, byte[] bytes)
    {
        using var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var buf = bytes;
        if (fs.Length > 0)
        {
            fs.Seek(-1, SeekOrigin.End);
            var last = fs.ReadByte();
            if (last != '\n')
            {
                buf = new byte[bytes.Length + 1];
                buf[0] = (byte)'\n';
                Buffer.BlockCopy(bytes, 0, buf, 1, bytes.Length);
            }
        }
        fs.Seek(0, SeekOrigin.End);
        fs.Write(buf, 0, buf.Length);
        fs.Flush(true);
    }

    public int SyncFallbackIntoVault()
    {
        try
        {
            if (_readOnly || !Directory.Exists(_fallback)) return 0;
            var dest = Path.Combine(_root, "logs");
            Directory.CreateDirectory(dest);
            int n = 0;
            foreach (var src in Directory.GetFiles(_fallback, $"{_vaultId:N}-*"))
            {
                try
                {
                    lock (_lock)
                    {
                        var target = Path.Combine(dest, Path.GetFileName(src));
                        if (File.Exists(target))
                        {
                            var data = File.ReadAllBytes(src);
                            if (data.Length > 0 && data[^1] != (byte)'\n')
                                data = data.Concat(new[] { (byte)'\n' }).ToArray();
                            AppendBytes(target, data);
                            File.Delete(src);
                        }
                        else File.Move(src, target);
                        n++;
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Log sync failed: " + ex.Message); }
            }
            return n;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Log sync failed: " + ex.Message);
            return 0;
        }
    }

    public static IEnumerable<LogEntry> ReadAll(string dir, Guid vaultId, byte[] vaultKey)
    {
        if (!Directory.Exists(dir)) yield break;
        var files = Directory.GetFiles(dir, $"{vaultId:N}-*")
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
        foreach (var f in files)
        {
            string[] lines;
            try
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                lines = sr.ReadToEnd().Split('\n');
            }
            catch { continue; }
            foreach (var l in lines)
            {
                if (string.IsNullOrWhiteSpace(l)) continue;
                LogEntry? e = null;
                try
                {
                    var plain = PackageCrypto.DecryptBytes(Convert.FromBase64String(l.Trim()), vaultKey);
                    e = JsonSerializer.Deserialize<LogEntry>(plain, Json.Options);
                }
                catch { }
                if (e != null) yield return e;
            }
        }
    }
}
