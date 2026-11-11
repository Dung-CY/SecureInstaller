using System.Security.Cryptography;
using System.Text;
using Installer.Core.Logging;
using Installer.Core.Vault;

namespace Installer.Core.Tests.Logging;

public class LogVaultFixture : IDisposable
{
    public string Tmp { get; } = Path.Combine(Path.GetTempPath(), "SI_log_" + Guid.NewGuid().ToString("N"));
    public string Root { get; }
    public Guid VaultId { get; }
    public byte[] Vk { get; }

    public LogVaultFixture()
    {
        Directory.CreateDirectory(Tmp);
        Root = Path.Combine(Tmp, "Dự án 1");
        VaultStore.Create(Root, "admin", "pw-1");
        var store = VaultStore.Open(Root);
        var s = store.Unlock("admin", "pw-1", DateTimeOffset.UtcNow);
        VaultId = store.Meta.VaultId;
        Vk = s.VaultKey;
    }

    public void Dispose() { try { Directory.Delete(Tmp, true); } catch { } }
}

public class EncryptedLogTests : IClassFixture<LogVaultFixture>
{
    readonly LogVaultFixture _f;
    public EncryptedLogTests(LogVaultFixture f) => _f = f;

    string NewDir(string n) => Path.Combine(_f.Tmp, n + "_" + Guid.NewGuid().ToString("N"));

    static LogEntry E(int i, string? detail = null) =>
        new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero).AddSeconds(i), "admin", "PC1", "SER", "install",
            "pkg", "1.0", "Success", 0, detail ?? $"d{i}");

    [Fact]
    public void Append_100_then_ReadAll_returns_100_in_order()
    {
        var root = NewDir("root"); var fb = NewDir("fb");
        var log = new EncryptedLog(root, _f.VaultId, _f.Vk, false, fb);
        for (int i = 0; i < 100; i++) log.Append(E(i));
        Assert.Equal(Path.Combine(root, "logs"), log.ActiveDir);
        var all = EncryptedLog.ReadAll(log.ActiveDir, _f.VaultId, _f.Vk).ToList();
        Assert.Equal(100, all.Count);
        for (int i = 0; i < 100; i++) Assert.Equal($"d{i}", all[i].Detail);
    }

    [Fact]
    public void Readonly_vault_writes_to_fallback()
    {
        var root = NewDir("root"); var fb = NewDir("fb");
        var log = new EncryptedLog(root, _f.VaultId, _f.Vk, true, fb);
        Assert.Equal(fb, log.ActiveDir);
        log.Append(E(1));
        Assert.Single(EncryptedLog.ReadAll(fb, _f.VaultId, _f.Vk));
        Assert.False(Directory.Exists(Path.Combine(root, "logs")));
    }

    [Fact]
    public void Write_failure_switches_to_fallback_and_does_not_throw()
    {
        var root = NewDir("root"); var fb = NewDir("fb");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "logs"), "block");
        var log = new EncryptedLog(root, _f.VaultId, _f.Vk, false, fb);
        var ex = Record.Exception(() => log.Append(E(1)));
        Assert.Null(ex);
        Assert.Equal(fb, log.ActiveDir);
        Assert.Single(EncryptedLog.ReadAll(fb, _f.VaultId, _f.Vk));
    }

    [Fact]
    public void Sync_moves_only_matching_vault_files()
    {
        var root = NewDir("root"); var fb = NewDir("fb");
        var ro = new EncryptedLog(root, _f.VaultId, _f.Vk, true, fb);
        ro.Append(E(1));
        var other = Path.Combine(fb, Guid.NewGuid().ToString("N") + "-20260101-X.log.enc");
        File.WriteAllText(other, "zzz\n");
        var rw = new EncryptedLog(root, _f.VaultId, _f.Vk, false, fb);
        rw.Append(E(2)); // same file name as fallback one, forces merge
        var n = rw.SyncFallbackIntoVault();
        Assert.Equal(1, n);
        Assert.True(File.Exists(other));
        Assert.Equal(2, EncryptedLog.ReadAll(Path.Combine(root, "logs"), _f.VaultId, _f.Vk).Count());
        Assert.Empty(EncryptedLog.ReadAll(fb, _f.VaultId, _f.Vk));
        Assert.Equal(0, new EncryptedLog(root, _f.VaultId, _f.Vk, true, fb).SyncFallbackIntoVault());
    }

    [Fact]
    public void Corrupt_line_is_skipped()
    {
        var root = NewDir("root"); var fb = NewDir("fb");
        var log = new EncryptedLog(root, _f.VaultId, _f.Vk, false, fb);
        log.Append(E(1));
        var file = Directory.GetFiles(log.ActiveDir).Single();
        File.AppendAllText(file, "not-base64!!\n" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(80)) + "\n");
        log.Append(E(2));
        var all = EncryptedLog.ReadAll(log.ActiveDir, _f.VaultId, _f.Vk).ToList();
        Assert.Equal(new[] { "d1", "d2" }, all.Select(x => x.Detail));
        Assert.Empty(EncryptedLog.ReadAll(NewDir("missing"), _f.VaultId, _f.Vk));
    }

    [Fact]
    public void Csv_has_bom_header_and_escapes_commas_and_vietnamese()
    {
        var path = Path.Combine(_f.Tmp, "out.csv");
        var e1 = E(1, "a, \"b\" ư");
        var e2 = E(0, "=cmd") with { ExitCode = -1 };
        CsvExporter.Export(new[] { e1, e2 }, path);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n");
        Assert.Equal("Thời gian,Người dùng,Máy,Serial BIOS,Hành động,Gói,Phiên bản,Trạng thái,Mã thoát,Chi tiết", lines[0]);
        Assert.EndsWith(",-1,'=cmd", lines[1]);
        Assert.EndsWith(",0,\"a, \"\"b\"\" ư\"", lines[2]);
        Assert.Equal("", lines[3]);
    }
}
