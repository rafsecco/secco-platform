namespace Secco.SecureGate.Application.Permissions;

/// <summary>Permissão do catálogo de um produto (ADR-0039).</summary>
/// <param name="Name">Nome canônico <c>recurso:acao</c>.</param>
/// <param name="Description">Descrição para quem administra perfis.</param>
/// <param name="AlsoDeclaredBy">Os OUTROS produtos que declaram o mesmo nome (colisão visível; vazio se única).</param>
public sealed record PermissionDefinitionDto(string Name, string Description, IReadOnlyList<string> AlsoDeclaredBy);

/// <summary>Catálogo de permissões de um produto.</summary>
/// <param name="Product">Identificador do produto.</param>
/// <param name="Permissions">Permissões declaradas, ordenadas por nome.</param>
/// <param name="PublishedAt">Instante da última alteração de qualquer entrada do produto.</param>
public sealed record ProductPermissionCatalogDto(
	string Product,
	IReadOnlyList<PermissionDefinitionDto> Permissions,
	DateTimeOffset PublishedAt);

/// <summary>Comando de publicação do catálogo de um produto (o <c>PUT</c> substitui o conjunto).</summary>
/// <param name="Product">Produto da rota.</param>
/// <param name="Permissions">Permissões declaradas; vazio é válido (produto sem permissões).</param>
public sealed record PublishPermissionCatalogCommand(string? Product, IReadOnlyList<PermissionDefinitionInput>? Permissions);

/// <summary>Permissão declarada no corpo da publicação, ainda não validada.</summary>
/// <param name="Name">Nome canônico <c>recurso:acao</c>.</param>
/// <param name="Description">Descrição curta.</param>
public sealed record PermissionDefinitionInput(string? Name, string? Description);
