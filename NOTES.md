# Development Notes

This project was originally scaffolded from an AI-generated artifact. See [`/docs`](docs/) for architecture, authentication, deployment, and audit check reference documentation.

## Quick Reference

- **Build:** `dotnet build -c Release`
- **Test:** `dotnet test tests\GenesysExtensionAudit.Infrastructure.Tests\`
- **Run:** `dotnet run --project src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj`
- **Publish:** See [Deployment](docs/deployment.md)
- **Credentials:** Use .NET user secrets or environment variables — never commit to source control
