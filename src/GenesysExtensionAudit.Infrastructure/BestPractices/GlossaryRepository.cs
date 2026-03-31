namespace GenesysExtensionAudit.Infrastructure.BestPractices;

public interface IGlossaryRepository
{
    GlossaryEntry? GetGlossaryTerm(string term);
    IReadOnlyList<GlossaryEntry> GetByDomain(string domain);
}

public sealed class GlossaryRepository : IGlossaryRepository
{
    private readonly IBestPracticesContentService _contentService;

    public GlossaryRepository(IBestPracticesContentService contentService)
    {
        _contentService = contentService ?? throw new ArgumentNullException(nameof(contentService));
    }

    public GlossaryEntry? GetGlossaryTerm(string term)
        => _contentService.GetSnapshot().Glossary.Terms
            .FirstOrDefault(entry => string.Equals(entry.Term, term, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<GlossaryEntry> GetByDomain(string domain)
        => _contentService.GetSnapshot().Glossary.Terms
            .Where(entry => string.Equals(entry.Domain, domain, StringComparison.OrdinalIgnoreCase))
            .ToList();
}
