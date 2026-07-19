using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Cryptography;

namespace Secco.SecureGate.Infrastructure.Seeding;

/// <summary>
/// Convergência de cifragem do catálogo (ADR-0025), como seed de REFERÊNCIA (ADR-0019):
/// roda em TODOS os ambientes após as migrations, é idempotente e re-cifra para a chave
/// ATIVA toda linha de <c>tb_tenant_databases</c> que ainda não esteja — legado em claro
/// (upgrade) ou cifrado por chave aposentada (rotação). Roda no startup, antes de servir
/// tráfego, como os demais seeders — não corrompe escritas concorrentes.
/// </summary>
/// <remarks>
/// A leitura é por SQL cru: o value converter do contexto decifraria o valor de forma
/// transparente e esconderia exatamente o que precisamos inspecionar (o formato armazenado).
/// A escrita também é por SQL cru — grava o texto já cifrado direto na coluna e, de
/// propósito, <b>não</b> toca <c>dt_updated_at</c>: re-cifrar não é alteração da credencial
/// (a coluna documenta "última alteração da connection string"), e ir pelo change tracking
/// do EF nem detectaria mudança (o plaintext é o mesmo, só a representação difere).
/// </remarks>
public sealed class TenantDatabaseReEncryptionSeeder(
    SecureGateDbContext context,
    IConnectionStringCipher cipher,
    ILogger<TenantDatabaseReEncryptionSeeder> logger) : IReferenceDataSeeder
{
    /// <summary>Roda após os seeders estruturais (scopes/operador) do produto.</summary>
    public int Order => 100;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Database
            .SqlQueryRaw<TenantDatabaseRawRow>(
                "SELECT id_pk_tenant_database AS \"Id\", ds_connection_string AS \"ConnectionString\" FROM tb_tenant_databases")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var converged = 0;

        foreach (var row in rows)
        {
            if (cipher.IsEncryptedWithActiveKey(row.ConnectionString))
            {
                continue;
            }

            // Decrypt cobre os dois casos a convergir: legado em claro (passthrough) e chave aposentada
            var plaintext = cipher.Decrypt(row.ConnectionString);
            var reEncrypted = cipher.Encrypt(plaintext);

            await context.Database
                .ExecuteSqlRawAsync(
                    "UPDATE tb_tenant_databases SET ds_connection_string = {0} WHERE id_pk_tenant_database = {1}",
                    [reEncrypted, row.Id],
                    cancellationToken)
                .ConfigureAwait(false);

            converged++;
        }

        if (converged > 0)
        {
            ReEncryptionLog.Converged(logger, converged);
        }
    }

    /// <summary>Projeção crua da coluna sensível — nunca logada nem exposta (ADR-0020).</summary>
    private sealed record TenantDatabaseRawRow(Guid Id, string ConnectionString);
}

/// <summary>Log estruturado da convergência (source generator — ADR-0008).</summary>
internal static partial class ReEncryptionLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Cifragem do catálogo: {Count} connection string(s) convergida(s) para a chave ativa (ADR-0025).")]
    public static partial void Converged(ILogger logger, int count);
}
