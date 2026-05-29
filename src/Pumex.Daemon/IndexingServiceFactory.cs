using Pumex.Contracts;

namespace Pumex.Daemon;

public class IndexingServiceFactory
{
    private readonly IndexDbContext _context;
    private readonly INoteRepository _noteRepo;
    private readonly ILinkRepository _linkRepo;
    private readonly FormatParserRegistry _parser;
    private readonly ILogger<IndexingService> _logger;

    public IndexingServiceFactory(
        IndexDbContext context,
        INoteRepository noteRepo,
        ILinkRepository linkRepo,
        FormatParserRegistry parser,
        ILogger<IndexingService> logger)
    {
        _context = context;
        _noteRepo = noteRepo;
        _linkRepo = linkRepo;
        _parser = parser;
        _logger = logger;
    }

    public IndexingService Create(VaultRecord vault) =>
        new(vault, _context, _noteRepo, _linkRepo, _parser, new WikilinkResolver(), new VaultWatcher(), _logger);
}
