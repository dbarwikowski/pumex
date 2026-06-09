using Pumex.Contracts;

namespace Pumex.Daemon;

public interface IContextRepository
{
    /// <summary>
    /// Builds an agent-oriented context pack for <paramref name="query"/>:
    /// the top lexically-ranked notes, each rendered as a multi-line passage
    /// with a drill-down pointer. Sources are returned in rank order and the
    /// lowest-ranked are dropped whole once <paramref name="budgetChars"/> of
    /// passage text is reached.
    /// </summary>
    Task<List<ContextResult>> ContextAsync(
        string query,
        int limit = 5,
        int budgetChars = 6000,
        long? vaultId = null);
}
