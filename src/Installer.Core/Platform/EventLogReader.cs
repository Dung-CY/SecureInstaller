using System.Diagnostics.Eventing.Reader;

namespace Installer.Core.Platform;

public sealed class EventLogReader : IEventLogReader
{
    public IReadOnlyList<EventRecordInfo> Read(string logName, int[] ids, int max = 2000)
    {
        var result = new List<EventRecordInfo>();
        try
        {
            var xpath = "*[System[(" + string.Join(" or ", ids.Select(i => $"EventID={i}")) + ")]]";
            using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(
                new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = true });
            while (result.Count < max && reader.ReadEvent() is { } rec)
            {
                using (rec)
                {
                    try
                    {
                        if (rec.TimeCreated is not { } t) continue;
                        result.Add(new EventRecordInfo(rec.Id, t.ToLocalTime(),
                            rec.Properties.Select(p => p.Value).ToList()));
                    }
                    catch (EventLogException) { }
                }
            }
        }
        catch (Exception) { }   // missing/disabled log or no rights: return what we have
        return result;
    }
}
