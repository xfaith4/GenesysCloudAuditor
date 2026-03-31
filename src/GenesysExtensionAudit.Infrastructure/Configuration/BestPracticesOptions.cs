namespace GenesysExtensionAudit.Infrastructure.Configuration;

public sealed class BestPracticesOptions
{
    public string RootPath { get; set; } = @"..\..\..\..\shared\Genesys.BestPractices";
    public string CatalogPath { get; set; } = @"best-practices\best-practices.catalog.json";
    public string CatalogSchemaPath { get; set; } = @"best-practices\best-practices.schema.json";
    public string MapPath { get; set; } = @"best-practices\best-practices-map.json";
    public string MapSchemaPath { get; set; } = @"best-practices\best-practices-map.schema.json";
    public string GlossaryPath { get; set; } = @"best-practices\glossary.json";
    public bool FailOnValidationError { get; set; }
}
