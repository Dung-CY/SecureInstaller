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
    public void Tokens_expand_over_fake_users_root_and_drives()
    {
        var users = Path.Combine(_root, "Users");
        var drive = Path.Combine(_root, "D");
        var dirs = new[] { @"An\Desktop", @"An\OneDrive - X\Desktop", @"An\Downloads", @"Bình\Downloads",
            @"Public\Desktop", @"Default\Desktop", @"All Users\Desktop", @"Default User\Desktop" };
        foreach (var d in dirs)
        {
            Directory.CreateDirectory(Path.Combine(users, d));
            File.WriteAllText(Path.Combine(users, d, "mat.txt"), "x");
        }
        Directory.CreateDirectory(drive);
        File.WriteAllText(Path.Combine(drive, "mat.txt"), "x");
        var sc = new KeywordFileScanner(users, () => new[] { drive });

        var desk = sc.Scan(Cfg(new[] { "mat" }, new[] { "{desktop}" }), CancellationToken.None).MatchedFiles;
        Assert.Equal(3, desk.Count);
        Assert.DoesNotContain(desk, f => f.Contains("Default") || f.Contains("All Users"));
        Assert.Equal(2, sc.Scan(Cfg(new[] { "mat" }, new[] { "{downloads}" }), CancellationToken.None).MatchedFiles.Count);
        Assert.Single(sc.Scan(Cfg(new[] { "mat" }, new[] { "{data-drives}" }), CancellationToken.None).MatchedFiles);
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

    [Fact]
    public void Keyword_scan_skips_junctions_but_enters_hidden_and_normal_dirs()
    {
        var outside = _root + "_ngoai";
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "mat-ngoai.txt"), "x");
        var hidden = Path.Combine(_root, "An");
        Directory.CreateDirectory(Path.Combine(hidden, "sub"));
        File.SetAttributes(hidden, FileAttributes.Directory | FileAttributes.Hidden);
        File.WriteAllText(Path.Combine(hidden, "sub", "mat-an.txt"), "x");
        var link = Path.Combine(_root, "lien-ket");
        try
        {
            Installer.Core.Tests.Platform.SafePathTests.MakeJunction(link, outside);
            Assert.True(KeywordFileScanner.ShouldSkipDir(new DirectoryInfo(link)));
            Assert.False(KeywordFileScanner.ShouldSkipDir(new DirectoryInfo(hidden)));

            var r = new KeywordFileScanner().Scan(Cfg(new[] { "mat" }, new[] { _root }), CancellationToken.None);

            Assert.Equal(new[] { Path.Combine(hidden, "sub", "mat-an.txt") }, r.MatchedFiles);
        }
        finally
        {
            try { Directory.Delete(link, false); } catch { }
            try { Directory.Delete(outside, true); } catch { }
        }
    }
}
