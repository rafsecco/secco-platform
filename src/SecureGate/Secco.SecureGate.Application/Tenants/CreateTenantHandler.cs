using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Tenants;

/// <summary>Comando de criação de tenant.</summary>
/// <param name="Name">Nome de exibição. Obrigatório.</param>
/// <param name="Slug">Identificador curto único (kebab-case). Obrigatório.</param>
public sealed record CreateTenantCommand(string? Name, string? Slug);

/// <summary>Cria um tenant no catálogo da plataforma (nasce ativo, sem bancos cadastrados).</summary>
public sealed class CreateTenantHandler(ITenantRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="command">Comando de criação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<TenantDto>> HandleAsync(
        CreateTenantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return SecureGateErrors.Tenants.NameRequired;
        }

        if (command.Name.Length > Tenant.NameMaxLength)
        {
            return SecureGateErrors.Tenants.NameTooLong;
        }

        var slug = command.Slug?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!TenantInputRules.IsValidSlug(slug, Tenant.SlugMaxLength))
        {
            return SecureGateErrors.Tenants.SlugInvalid;
        }

        if (await repository.SlugExistsAsync(slug, cancellationToken).ConfigureAwait(false))
        {
            return SecureGateErrors.Tenants.SlugAlreadyExists;
        }

        var tenant = new Tenant(command.Name.Trim(), slug);

        await repository.AddAsync(tenant, cancellationToken).ConfigureAwait(false);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TenantDto.FromEntity(tenant);
    }
}
