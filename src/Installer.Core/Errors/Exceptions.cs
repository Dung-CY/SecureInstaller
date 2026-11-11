namespace Installer.Core.Errors;

public sealed class WorkspaceUnsafeException : Exception
{
    public WorkspaceUnsafeException(string message) : base(message) { }
}

public sealed class UnsafePathException : Exception
{
    public UnsafePathException(string message) : base(message) { }
}
