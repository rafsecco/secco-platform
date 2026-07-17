using System.Net;
using FluentAssertions;
using Xunit;

namespace Secco.NotificationHub.Tests.Integration;

/// <summary>
/// Teste de contrato (ADR-0006/0012): o openapi.json versionado É o contrato.
/// Divergência entre o documento gerado pela API e o snapshot commitado falha o CI.
/// Para atualizar o snapshot intencionalmente: rodar com SECCO_UPDATE_OPENAPI=true e
/// commitar o diff junto do client regenerado, no mesmo PR.
/// </summary>
public class OpenApiContractTests(NotificationHubApiFactory factory) : IClassFixture<NotificationHubApiFactory>
{
    private const string UpdateSnapshotVariable = "SECCO_UPDATE_OPENAPI";

    private static readonly string SnapshotPath = Path.Combine(
        FindProjectRoot(), "Secco.NotificationHub.Api", "openapi", "openapi.json");

    [Fact]
    public async Task OpenApiDocument_Always_IsExposedByTheApi()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApiDocument_Always_MatchesCommittedSnapshot()
    {
        var generated = Normalize(await factory.CreateClient().GetStringAsync("/openapi/v1.json"));

        if (Environment.GetEnvironmentVariable(UpdateSnapshotVariable) == "true")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            await File.WriteAllTextAsync(SnapshotPath, generated);
            return;
        }

        File.Exists(SnapshotPath).Should().BeTrue(
            $"o contrato versionado deve existir — gere-o com {UpdateSnapshotVariable}=true");

        var committed = Normalize(await File.ReadAllTextAsync(SnapshotPath));

        generated.Should().Be(committed,
            $"mudança de contrato exige regenerar o snapshot ({UpdateSnapshotVariable}=true) e o client no mesmo PR (ADR-0006)");
    }

    private static string Normalize(string content) =>
        content
            // Fins de linha reais do arquivo (checkout Windows vs Linux)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            // Fins de linha ESCAPADOS dentro de strings JSON (comentários XML seguem o SO do build)
            .Replace("\\r\\n", "\\n", StringComparison.Ordinal)
            .TrimEnd('\n') + "\n";

    private static string FindProjectRoot()
    {
        // Sobe até a pasta do produto (testes dentro do produto) OU até a raiz do monorepo
        // (testes já movidos para tests/<Produto>) e localiza a pasta do Api
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Secco.NotificationHub.Api")))
            {
                return directory.FullName;
            }

            if (File.Exists(Path.Combine(directory.FullName, "Secco.Platform.slnx")))
            {
                return Directory.GetDirectories(directory.FullName, "Secco.NotificationHub.Api", SearchOption.AllDirectories)
                    .Select(Path.GetDirectoryName)
                    .First(parent => parent is not null)!;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Raiz do produto (Secco.NotificationHub.Api) não encontrada.");
    }
}
