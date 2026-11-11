namespace Installer.Core.Errors;

/// <summary>InvalidOperationException whose message is authored by us (Vietnamese) and safe to show to the user.</summary>
public class UserMessageException : InvalidOperationException
{
    public UserMessageException(string message) : base(message) { }
}

public sealed class WorkspaceUnsafeException : Exception
{
    public WorkspaceUnsafeException(string message) : base(message) { }
}

public sealed class UnsafePathException : Exception
{
    public UnsafePathException(string message) : base(message) { }
}
