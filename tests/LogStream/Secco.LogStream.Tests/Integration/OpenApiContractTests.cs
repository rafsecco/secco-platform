using System.Net;
using FluentAssertions;
using Xunit;

namespace Secco.LogStream.Tests.Integration;

/// <summary>
/// Teste de contrato (ADR-0006/0012): o openapi.json versionado no repositório É o contrato.
/// Divergência entre o documento gerado pela API e o snapshot commitado falha o CI.
/// Para atualizar o snapshot intencionalmente: rodar com SECCO_UPDATE_OPENAPI=true e
/// commitar o diff no mesmo PR da mudança de contrato.
/// </summary>
public class OpenApiContractTests(LogStreamApiFactory factory) : IClassFixture<LogStreamApiFactory>
{
    private const string UpdateSnapshotVariable = "SECCO_UPDATE_OPENAPI";

    private static readonly string SnapshotPath = Path.Combine(
        FindRepositoryRoot(), "src", "LogStream", "Secco.LogStream.Api", "openapi", "openapi.json");

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
        content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Secco.Platform.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Raiz do repositório (Secco.Platform.slnx) não encontrada.");
    }
}
