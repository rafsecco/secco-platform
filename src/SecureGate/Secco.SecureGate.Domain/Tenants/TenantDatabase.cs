using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.SecureGate.Domain.Tenants;

/// <summary>
/// Banco dedicado de um tenant em um produto (ADR-0005: database-per-tenant é POR PRODUTO —
/// o banco do LogStream do tenant X não é o banco de outro produto). Par (tenant, produto)
/// único; a connection string é o dado mais sensível da plataforma e NUNCA aparece em logs,
/// erros ou respostas de gestão (ADR-0020) — só o endpoint de catálogo autorizado a entrega.
/// </summary>
public sealed class TenantDatabase : BaseEntity
{
    /// <summary>Tamanho máximo aceito para o identificador de produto.</summary>
    public const int ProductMaxLength = 50;

    /// <summary>Tamanho máximo aceito para a connection string.</summary>
    public const int ConnectionStringMaxLength = 2000;

    private TenantDatabase()
    {
        // Construtor de rehidratação do EF Core
        Product = string.Empty;
        ConnectionString = string.Empty;
    }

    /// <summary>Registra o banco de um tenant para um produto.</summary>
    /// <param name="tenantId">Tenant dono do banco. Obrigatório.</param>
    /// <param name="product">Identificador do produto (kebab-case, ex.: <c>logstream</c>). Obrigatório.</param>
    /// <param name="connectionString">Connection string do banco dedicado. Obrigatória.</param>
    /// <exception cref="DomainInvariantException">Se algum argumento violar os invariantes.</exception>
    public TenantDatabase(Guid tenantId, string product, string connectionString)
    {
        if (tenantId == Guid.Empty)
        {
            throw new DomainInvariantException("Um banco de tenant exige o tenant dono.");
        }

        if (string.IsNullOrWhiteSpace(product))
        {
            throw new DomainInvariantException("Um banco de tenant exige o produto.");
        }

        TenantId = tenantId;
        Product = product.ToLowerInvariant();
        ConnectionString = ValidateConnectionString(connectionString);
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Tenant dono do banco (coluna <c>id_fk_tenant</c>).</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Produto ao qual o banco pertence (coluna <c>ds_product</c>).</summary>
    public string Product { get; private set; }

    /// <summary>Connection string do banco dedicado (coluna <c>ds_connection_string</c>). Nunca logar.</summary>
    public string ConnectionString { get; private set; }

    /// <summary>Momento do cadastro (coluna <c>dt_created_at</c>).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Momento da última alteração da connection string (coluna <c>dt_updated_at</c>).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Substitui a connection string (rotação de credencial, migração de servidor).</summary>
    /// <param name="connectionString">Nova connection string. Obrigatória.</param>
    /// <exception cref="DomainInvariantException">Se a connection string for vazia ou exceder o limite.</exception>
    public void UpdateConnectionString(string connectionString)
    {
        ConnectionString = ValidateConnectionString(connectionString);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string ValidateConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new DomainInvariantException("Um banco de tenant exige connection string não vazia.");
        }

        // O valor nunca entra na mensagem — connection strings não vazam em erros (ADR-0020)
        return connectionString.Length > ConnectionStringMaxLength
            ? throw new DomainInvariantException(
                $"A connection string excede o limite de {ConnectionStringMaxLength} caracteres.")
            : connectionString;
    }
}
