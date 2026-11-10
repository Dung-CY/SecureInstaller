namespace Installer.Core.Platform;

public interface IWmi
{
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string wql, string scope = @"root\cimv2");
}
