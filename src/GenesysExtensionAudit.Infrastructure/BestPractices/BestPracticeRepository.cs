namespace GenesysExtensionAudit.Infrastructure.BestPractices;

public interface IBestPracticeRepository
{
    BestPracticeEntry? GetByKey(string key);
    IReadOnlyList<BestPracticeEntry> GetByDomain(string domain);
    IReadOnlyList<BestPracticeEntry> GetByTag(string tag);
    IReadOnlyList<BestPracticeMapEntry> GetMappingsForFindingType(string findingType);
    BestPracticesContentStatus GetContentStatus();
}

public sealed class BestPracticeRepository : IBestPracticeRepository
{
    private readonly IBestPracticesContentService _contentService;

    public BestPracticeRepository(IBestPracticesContentService contentService)
    {
        _contentService = contentService ?? throw new ArgumentNullException(nameof(contentService));
    }

    public BestPracticeEntry? GetByKey(string key)
        => _contentService.GetSnapshot().Catalog.Catalog
            .FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<BestPracticeEntry> GetByDomain(string domain)
        => _contentService.GetSnapshot().Catalog.Catalog
            .Where(entry => string.Equals(entry.Domain, domain, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public IReadOnlyList<BestPracticeEntry> GetByTag(string tag)
        => _contentService.GetSnapshot().Catalog.Catalog
            .Where(entry => entry.Tags.Any(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    public IReadOnlyList<BestPracticeMapEntry> GetMappingsForFindingType(string findingType)
        => _contentService.GetSnapshot().Map.Mappings
            .Where(entry => string.Equals(entry.FindingType, findingType, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public BestPracticesContentStatus GetContentStatus() => _contentService.GetStatus();
}
