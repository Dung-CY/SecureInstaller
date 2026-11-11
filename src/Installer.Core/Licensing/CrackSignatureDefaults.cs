using Installer.Core.Model;

namespace Installer.Core.Licensing;

public static class CrackSignatureDefaults
{
    public static IReadOnlyList<CrackSignature> All { get; } = new List<CrackSignature>
    {
        new("kms_host", @"^(127\.|localhost$|0\.0\.0\.0|::1)"),
        new("service", @"^(KMSELDI|Service_KMS|KMSpico|AutoKMS|KMSAuto.*)$"),
        new("file", @"%ProgramFiles%\KMSpico"),
        new("file", @"%SystemRoot%\SECOH-QAD.exe"),
        new("file", @"%SystemRoot%\System32\SppExtComObjHook.dll"),
        new("hosts_block", @"(?m)^\s*[\d.]+\s+(activation|validation)(-v2)?\.sls\.microsoft\.com"),
    };
}
