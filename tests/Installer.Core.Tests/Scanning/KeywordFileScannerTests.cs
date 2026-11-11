using System.Text;
using Installer.Core.Model;
using Installer.Core.Scanning;

namespace Installer.Core.Tests.Scanning;

public class KeywordFileScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SI kw Dự án " + Guid.NewGuid().ToString("N"));
    public KeywordFileScannerTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static KeywordScanConfig Cfg(string[] kw, string[] paths, int sec = 60) => new(kw.ToList(), paths.ToList(), sec);

    [Fact]
    public void Finds_files_by_name_keyword_case_insensitive()
    {
        File.WriteAllText(Path.Combine(_root, "Bao_Cao_MAT.docx"), "x");
        File.WriteAllText(Path.Combine(_root, "Tài liệu Mật.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "other.txt"), "x");
        var r = new KeywordFileScanner().Scan(Cfg(new[] { "mat", "mật" }, new[] { _root }), CancellationToken.None);
        Assert.Equal(2, r.MatchedFiles.Count);
        Assert.False(r.TimedOut);
        Assert.False(r.Truncated);
    }

    [Fact]
    public void Matches_nfd_filename_against_nfc_keyword()
    {
        var nfd = "Tài liệu Mật.txt".Normalize(NormalizationForm.FormD);
        var nfc = "mật".Normalize(NormalizationForm.FormC);
        Assert.NotEqual(nfd, nfd.Normalize(NormalizationForm.FormC));
        Assert.True(KeywordFileScanner.NameMatches(nfd, new[] { nfc }));
        Assert.True(KeywordFileScanner.NameMatches(nfc, new[] { "MẬT".Normalize(NormalizationForm.FormC) }));
    }

    [Fact]
    public void Missing_path_is_skipped()
    {
        File.WriteAllText(Path.Combine(_root, "mat.txt"), "x");
        var r = new KeywordFileScanner().Scan(Cfg(new[] { "mat" }, new[] { Path.Combine(_root, "nope"), _root, _root }), CancellationToken.None);
        Assert.Single(r.MatchedFiles);
    }

    [Fact]
    public void Stops_at_max_seconds()
    {
        File.WriteAllText(Path.Combine(_root, "mat.txt"), "x");
        var r = new KeywordFileScanner().Scan(Cfg(new[] { "mat" }, new[] { _root }, 0), CancellationToken.None);
        Assert.True(r.TimedOut);
    }

    [Fact]
    public void Truncates_at_1000()
    {
        for (int i = 0; i < 1005; i++) File.WriteAllText(Path.Combine(_root, $"mat_{i}.txt"), "");
        var r = new KeywordFileScanner().Scan(Cfg(new[] { "mat" }, new[] { _root }), CancellationToken.None);
        Assert.Equal(1000, r.MatchedFiles.Count);
        Assert.True(r.Truncated);
    }

    [Fact]
    public void Cancelled_token_returns_partial_TimedOut()
    {
        File.WriteAllText(Path.Combine(_root, "mat.txt"), "x");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = new KeywordFileScanner().Scan(Cfg(new[] { "mat" }, new[] { _root }, 60), cts.Token);
        Assert.True(r.TimedOut);
    }

    [Fact]
    public void Scan_finds_real_nfd_named_file()
    {
        var name = "Mật.txt".Normalize(NormalizationForm.FormD);
        File.WriteAllText(Path.Combine(_root, name), "x");
        var r = new KeywordFileScanner().Scan(Cfg(new[] { "mật".Normalize(NormalizationForm.FormC) }, new[] { _root }), CancellationToken.None);
        Assert.Single(r.MatchedFiles);
    }
}
