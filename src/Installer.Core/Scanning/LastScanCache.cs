using System.Text.Json;
using Installer.Core.Crypto;
using Installer.Core.Model;

namespace Installer.Core.Scanning;

/// <summary>
/// The machine's latest scan for one vault, encrypted with that vault's key, so later sessions on the same machine
/// skip the scan. A different machine (BIOS serial or host name changed), a different vault or a damaged file means a new scan.
/// </summary>
public sealed class LastScanCache(Guid vaultId, byte[] vaultKey, string? dir = null)
{
    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SecureInstaller");

    string FilePath => Path.Combine(dir ?? DefaultDir, $"last-scan-{vaultId:N}.enc");

    public ScanReport? Load(string? biosSerial, string hostname)
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = PackageCrypto.DecryptBytes(File.ReadAllBytes(FilePath), vaultKey);
            var r = JsonSerializer.Deserialize<ScanReport>(json, Json.Options);
            if (r?.Hardware == null) return null;
            bool Same(string? a, string? b) => string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
            return Same(r.Hardware.BiosSerial, biosSerial) && Same(r.Hardware.Hostname, hostname) ? r : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Never throws: a cache that cannot be written only means the next session scans again.</summary>
    public void Save(ScanReport report)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, PackageCrypto.EncryptBytes(JsonSerializer.SerializeToUtf8Bytes(report, Json.Options), vaultKey));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception) { }
    }
}
