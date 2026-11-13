namespace Installer.Core.Platform;

public interface IFileTextReader
{
    /// <summary>Lines of every file matching the pattern, oldest file first. Opened with FileShare.ReadWrite; never throws.</summary>
    IEnumerable<string> ReadLines(string directory, string searchPattern);
}

public sealed class FileTextReader : IFileTextReader
{
    public IEnumerable<string> ReadLines(string directory, string searchPattern)
    {
        string[] files;
        try { files = Directory.GetFiles(directory, searchPattern); }
        catch (Exception) { yield break; }
        // setupapi.dev.log is the newest; the dated ones are rotated copies
        foreach (var f in files.OrderBy(f => File.GetLastWriteTimeUtc(f)))
        {
            StreamReader? sr = null;
            try { sr = new StreamReader(new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)); }
            catch (Exception) { }
            if (sr is null) continue;
            using (sr)
            {
                while (true)
                {
                    string? line;
                    try { line = sr.ReadLine(); } catch (IOException) { break; }
                    if (line is null) break;
                    yield return line;
                }
            }
        }
    }
}
