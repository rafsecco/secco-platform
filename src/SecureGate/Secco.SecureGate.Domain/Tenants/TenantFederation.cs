using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.SecureGate.Domain.Tenants;

/// <summary>
/// Federação de autenticação de um tenant (ADR-0026): opt-in 1:1 com o tenant. A federação
/// é SÓ autenticação — o Entra ID prova a identidade na tela de login, mas o SecureGate segue
/// o único emissor de tokens da plataforma. O <see cref="DirectoryId"/> (tenant GUID do Entra
/// da empresa) NÃO é segredo: aparece na visão de gestão. O gate real de segurança é o pin
/// <c>tid do token == DirectoryId registrado</c>, verificado no login (ADR-0026/0020).
/// </summary>
public sealed class TenantFederation : BaseEntity
{
    /// <summary>Provedor de federação suportado na v1 (ADR-0026).</summary>
    public const string EntraProvider = "entra-id";

    /// <summary>Tamanho máximo aceito para o identificador de provedor.</summary>
    public const int ProviderMaxLength = 30;

    private TenantFederation()
    {
        // Construtor de rehidratação do EF Core
        Provider = string.Empty;
    }

    /// <summary>Habilita a federação Entra ID de um tenant (nasce habilitada).</summary>
    /// <param name="tenantId">Tenant dono da federação. Obrigatório.</param>
    /// <param name="directoryId">Directory id (tenant GUID do Entra ID da empresa). Obrigatório — não é segredo.</param>
    /// <exception cref="DomainInvariantException">Se tenant ou directory id forem vazios.</exception>
    public TenantFederation(Guid tenantId, Guid directoryId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new DomainInvariantException("Uma federação de tenant exige o tenant dono.");
        }

        if (directoryId == Guid.Empty)
        {
            throw new DomainInvariantException("Uma federação de tenant exige o directory id do Entra ID.");
        }

        TenantId = tenantId;
        DirectoryId = directoryId;
        Provider = EntraProvider;
        IsEnabled = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Tenant dono da federação (coluna <c>id_fk_tenant</c>, índice único — 1:1).</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Provedor de federação (coluna <c>ds_provider</c>); fixo em <see cref="EntraProvider"/> na v1.</summary>
    public string Provider { get; private set; }

    /// <summary>
    /// Directory id do Entra ID da empresa (coluna <c>directory_id</c>). Dado não-secreto:
    /// é o <c>tid</c> esperado do token; o login recusa qualquer diretório diferente (ADR-0026).
    /// </summary>
    public Guid DirectoryId { get; private set; }

    /// <summary>Federação habilitada (coluna <c>fl_enabled</c>). Desabilitada bloqueia o login federado.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Momento do cadastro (coluna <c>dt_created_at</c>).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Momento da última alteração (coluna <c>dt_updated_at</c>).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Substitui o directory id (empresa trocou de diretório Entra).</summary>
    /// <param name="directoryId">Novo directory id. Obrigatório.</param>
    /// <exception cref="DomainInvariantException">Se o directory id for vazio.</exception>
    public void UpdateDirectory(Guid directoryId)
    {
        if (directoryId == Guid.Empty)
        {
            throw new DomainInvariantException("Uma federação de tenant exige o directory id do Entra ID.");
        }

        DirectoryId = directoryId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Habilita ou desabilita a federação.</summary>
    /// <param name="enabled">Novo estado de habilitação.</param>
    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
