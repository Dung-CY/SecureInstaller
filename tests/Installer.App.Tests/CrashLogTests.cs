using System.IO;
using Installer.App;

namespace Installer.App.Tests;

public class CrashLogTests
{
    [Fact]
    public void Append_writes_line_rotates_when_large_and_bad_path_does_not_throw()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SI_crash_" + Guid.NewGuid().ToString("N"), "Dự án 1");
        try
        {
            var p = Path.Combine(dir, "crash.log");
            CrashLog.Append(new InvalidOperationException("boom"), p);
            Assert.Contains("boom", File.ReadAllText(p));

            File.WriteAllBytes(p, new byte[1024 * 1024 + 1]);
            CrashLog.Append(new InvalidOperationException("second"), p);
            Assert.True(File.Exists(Path.Combine(dir, "crash.old.log")));
            var text = File.ReadAllText(p);
            Assert.Contains("second", text);
            Assert.True(new FileInfo(p).Length < 100_000);

            CrashLog.Append(new Exception("x"), "Z:/no/such|drive/crash.log");
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(dir)!, true); } catch { } }
    }
}
