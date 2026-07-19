using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Application;
using Secco.SecureGate.Tests.Integration.Helpers;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Cifragem em repouso da connection string do catálogo (ADR-0025): o valor persistido
/// NUNCA é plaintext (asserção direta na coluna por SQL cru), o endpoint de catálogo segue
/// entregando o plaintext funcional, e o legado em claro converge no startup (seed de
/// referência) — idempotente.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class ConnectionStringEncryptionTests(SecureGateApiFactory factory) : IAsyncLifetime
{
    private const string EncryptedPrefix = "secco-enc:v1:";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        await factory.EnsureDatabaseMigratedAsync();
        _admin = CreateClientWithScopes(SecureGateScopes.Admin);
    }

    public Task DisposeAsync()
    {
        _admin?.Dispose();

        return Task.CompletedTask;
    }

    [Fact]
    public async Task UpsertDatabase_StoresCiphertext_NeverPlaintext()
    {
        const string connectionString = "Server=cifrado-em-repouso;Database=segredo-nunca-em-claro;User Id=svc;Password=p@ss;";
        var tenantId = await CreateTenantAsync();

        (await _admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/databases/logstream", new { connectionString }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await ReadRawConnectionStringAsync(tenantId);

        stored.Should().StartWith(EncryptedPrefix, "o value converter cifra no write (ADR-0025)");
        stored.Should().NotContain("segredo-nunca-em-claro", "o plaintext jamais repousa na coluna");
    }

    [Fact]
    public async Task Catalog_StillReturnsWorkingPlaintext_AfterEncryption()
    {
        const string connectionString = "Server=catalogo-decifra;Database=tenant-x;";
        var tenantId = await CreateTenantAsync();

        (await _admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/databases/logstream", new { connectionString }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reader = CreateClientWithScopes(SecureGateScopes.CatalogFor("logstream"));
        var entry = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/v1/catalog/logstream/tenants/{tenantId}", Json);

        entry.GetProperty("connectionString").GetString().Should().Be(connectionString,
            "o caminho de leitura do catálogo decifra transparentemente (ADR-0025)");
    }

    [Fact]
    public async Task LegacyPlaintextRow_ConvergesToCiphertext_OnSeeding_AndIsIdempotent()
    {
        const string legacyPlaintext = "Server=legado-em-claro;Database=upgrade;Password=antigo;";
        var tenantId = await CreateTenantAsync();

        // Simula uma linha anterior ao ADR-0025: plaintext gravado direto na coluna, sem prefixo
        await InsertRawDatabaseRowAsync(tenantId, "logstream", legacyPlaintext);
        (await ReadRawConnectionStringAsync(tenantId)).Should().Be(legacyPlaintext, "pré-condição: repousa em claro");

        // Convergência no mesmo pipeline do seed de referência (ADR-0019)
        await factory.Services.SeedSeccoDataAsync();

        var convergedStored = await ReadRawConnectionStringAsync(tenantId);
        convergedStored.Should().StartWith(EncryptedPrefix, "o legado converge para cifrado no startup");
        convergedStored.Should().NotContain("legado-em-claro");

        // Segundo passo = no-op: já cifrado com a chave ativa, o valor não muda (idempotência)
        await factory.Services.SeedSeccoDataAsync();
        (await ReadRawConnectionStringAsync(tenantId)).Should().Be(convergedStored, "reexecutar não re-cifra");

        // E o catálogo continua entregando o plaintext original
        var reader = CreateClientWithScopes(SecureGateScopes.CatalogFor("logstream"));
        var entry = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/v1/catalog/logstream/tenants/{tenantId}", Json);
        entry.GetProperty("connectionString").GetString().Should().Be(legacyPlaintext);
    }

    private HttpClient CreateClientWithScopes(params string[] scopes)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestTokenFactory.CreateToken(scopes));

        return client;
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var created = await _admin.PostAsJsonAsync("/api/v1/tenants",
            new { name = "Tenant cifragem", slug = $"t-{Guid.NewGuid():N}" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
    }

    private async Task<string> ReadRawConnectionStringAsync(Guid tenantId)
    {
        await using var connection = new SqlConnection(factory.GetPlatformConnectionString());
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            "SELECT ds_connection_string FROM tb_tenant_databases WHERE id_fk_tenant = @tenant", connection);
        command.Parameters.AddWithValue("@tenant", tenantId);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task InsertRawDatabaseRowAsync(Guid tenantId, string product, string plaintext)
    {
        await using var connection = new SqlConnection(factory.GetPlatformConnectionString());
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            INSERT INTO tb_tenant_databases
                (id_pk_tenant_database, id_fk_tenant, ds_product, ds_connection_string, dt_created_at, dt_updated_at)
            VALUES (@id, @tenant, @product, @connectionString, @createdAt, @updatedAt)
            """,
            connection);

        command.Parameters.AddWithValue("@id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@product", product);
        command.Parameters.AddWithValue("@connectionString", plaintext);
        command.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("@updatedAt", DateTimeOffset.UtcNow);

        await command.ExecuteNonQueryAsync();
    }
}
