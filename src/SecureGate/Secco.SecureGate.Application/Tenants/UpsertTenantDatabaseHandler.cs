using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Tenants;

/// <summary>Comando de cadastro/substituição do banco de um tenant em um produto.</summary>
/// <param name="TenantId">Tenant dono do banco.</param>
/// <param name="Product">Identificador do produto (kebab-case).</param>
/// <param name="ConnectionString">Connection string do banco dedicado. Nunca logar.</param>
public sealed record UpsertTenantDatabaseCommand(Guid TenantId, string? Product, string? ConnectionString);

/// <summary>
/// Upsert do banco de um tenant em um produto. A superfície é write-only para o segredo
/// (ADR-0020): a resposta não devolve a connection string — rotação de credencial é
/// simplesmente um novo PUT.
/// </summary>
public sealed class UpsertTenantDatabaseHandler(ITenantRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="command">Comando de upsert.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result> HandleAsync(
        UpsertTenantDatabaseCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = command.Product?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!TenantInputRules.IsValidSlug(product, TenantDatabase.ProductMaxLength))
        {
            return Result.Failure(SecureGateErrors.TenantDatabases.ProductInvalid);
        }

        if (string.IsNullOrWhiteSpace(command.ConnectionString))
        {
            return Result.Failure(SecureGateErrors.TenantDatabases.ConnectionStringRequired);
        }

        if (command.ConnectionString.Length > TenantDatabase.ConnectionStringMaxLength)
        {
            return Result.Failure(SecureGateErrors.TenantDatabases.ConnectionStringTooLong);
        }

        var tenant = await repository.GetByIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            return Result.Failure(SecureGateErrors.Tenants.NotFound);
        }

        var existing = await repository.GetDatabaseAsync(command.TenantId, product, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await repository.AddDatabaseAsync(
                new TenantDatabase(command.TenantId, product, command.ConnectionString),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            existing.UpdateConnectionString(command.ConnectionString);
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
