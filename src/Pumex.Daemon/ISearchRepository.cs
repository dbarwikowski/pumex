using Pumex.Contracts;

namespace Pumex.Daemon;

public interface ISearchRepository
{
    Task<List<SearchResult>> SearchAsync(
        string? query,
        int limit = 50,
        long? vaultId = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<KeyValuePair<string, string>>? properties = null);
}
