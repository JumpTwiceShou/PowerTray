namespace LGSTrayHID;

internal static class RediscoveryPassPolicy
{
    public static IReadOnlyList<T> PrioritizeCreated<T>(
        IReadOnlyList<T> sessions,
        IReadOnlyCollection<T> createdSessions
    ) where T : notnull
    {
        if (sessions.Count < 2 || createdSessions.Count == 0)
        {
            return sessions;
        }

        HashSet<T> created = [.. createdSessions];
        return
        [
            .. sessions.Where(created.Contains),
            .. sessions.Where(session => !created.Contains(session)),
        ];
    }
}
