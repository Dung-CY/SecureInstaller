using Installer.Core.Install;

namespace Installer.Core.Tests.Install;

public class CommandLineSplitterTests
{
    [Theory]
    [InlineData("\"C:\\P F\\u.exe\" /S", "C:\\P F\\u.exe", "/S")]
    [InlineData("C:\\Tools\\un.exe -q", "C:\\Tools\\un.exe", "-q")]
    [InlineData("C:\\Tools\\UN.EXE", "C:\\Tools\\UN.EXE", "")]
    [InlineData("\"C:\\Dự án 1\\u.exe\"  /S /v\"/qn\"", "C:\\Dự án 1\\u.exe", "/S /v\"/qn\"")]
    public void Split_cases(string line, string exe, string args)
    {
        var r = CommandLineSplitter.Split(line);
        Assert.Equal(exe, r.Exe);
        Assert.Equal(args, r.Args);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"C:\\unterminated.exe")]
    [InlineData("notanexe /S")]
    [InlineData("\"\" /S")]
    public void Split_throws_without_exe(string line) =>
        Assert.Throws<InvalidDataException>(() => CommandLineSplitter.Split(line));
}
