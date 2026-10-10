using Secco.SharedKernel.Entities;

namespace Secco.SecureGate.Domain.Permissions;

/// <summary>
/// Permissão declarada por um produto no catálogo da plataforma (ADR-0039). Dado de plataforma,
/// sem tenant: é o produto quem publica, no <c>migrate</c>. Identidade natural: (produto, nome).
/// As regras de formato e de limite de quantidade vivem na Application; aqui só o mínimo para
/// a entidade não nascer vazia.
/// </summary>
public sealed class PermissionCatalogEntry : BaseEntity
{
	/// <summary>Tamanho máximo do identificador do produto (mesma regra de <c>TenantDatabase</c>).</summary>
	public const int ProductMaxLength = 50;

	/// <summary>Tamanho máximo do nome da permissão (coluna).</summary>
	public const int NameMaxLength = 200;

	/// <summary>Tamanho máximo da descrição.</summary>
	public const int DescriptionMaxLength = 200;

	private PermissionCatalogEntry()
	{
		// Construtor de rehidratação do EF Core
		Product = string.Empty;
		Name = string.Empty;
		Description = string.Empty;
	}

	/// <summary>Cria uma entrada de catálogo.</summary>
	/// <param name="product">Produto que declara a permissão. Obrigatório.</param>
	/// <param name="name">Nome canônico <c>recurso:acao</c>. Obrigatório.</param>
	/// <param name="description">Descrição para quem administra perfis. Obrigatória.</param>
	/// <param name="publishedAt">Instante da publicação.</param>
	/// <exception cref="ArgumentException">Se produto, nome ou descrição forem vazios.</exception>
	public PermissionCatalogEntry(string product, string name, string description, DateTimeOffset publishedAt)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(product);
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(description);

		Product = product;
		Name = name;
		Description = description;
		PublishedAt = publishedAt;
	}

	/// <summary>Produto declarante (coluna <c>ds_product</c>).</summary>
	public string Product { get; private set; }

	/// <summary>Nome canônico da permissão (coluna <c>ds_name</c>).</summary>
	public string Name { get; private set; }

	/// <summary>Descrição (coluna <c>ds_description</c>).</summary>
	public string Description { get; private set; }

	/// <summary>Instante da última alteração da entrada (coluna <c>dt_published_at</c>).</summary>
	public DateTimeOffset PublishedAt { get; private set; }

	/// <summary>Atualiza a descrição e o instante de publicação.</summary>
	/// <param name="description">Nova descrição. Obrigatória.</param>
	/// <param name="publishedAt">Instante da publicação.</param>
	public void UpdateDescription(string description, DateTimeOffset publishedAt)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(description);

		Description = description;
		PublishedAt = publishedAt;
	}
}
