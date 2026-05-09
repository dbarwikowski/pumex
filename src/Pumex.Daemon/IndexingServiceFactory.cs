using Pumex.Contracts;

namespace Pumex.Daemon;

public class IndexingServiceFactory
{
    private readonly IndexDb _db;
    private readonly NoteParser _parser;
    private readonly ILogger<IndexingService> _logger;

    public IndexingServiceFactory(
        IndexDb db,
        NoteParser parser,
        ILogger<IndexingService> logger)
    {
        _db = db;
        _parser = parser;
        _logger = logger;
    }

    public IndexingService Create(VaultRecord vault) =>
        new(vault, _db, _parser, new WikilinkResolver(), new VaultWatcher(), _logger);
}
