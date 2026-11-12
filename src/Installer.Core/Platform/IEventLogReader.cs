namespace Installer.Core.Platform;

public record EventRecordInfo(int Id, DateTime TimeCreated, IReadOnlyList<object?> Properties);

public interface IEventLogReader
{
    /// <summary>Newest first, local times. A missing or disabled log returns an empty list; never throws.</summary>
    IReadOnlyList<EventRecordInfo> Read(string logName, int[] ids, int max = 2000);
}
