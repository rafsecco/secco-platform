namespace Secco.SecureGate.Api.Requests;

/// <summary>Corpo da publicação do catálogo de permissões de um produto (ADR-0039).</summary>
/// <param name="Permissions">Permissões declaradas; lista vazia é válida (produto sem permissões).</param>
public sealed record PublishPermissionCatalogRequest(IReadOnlyList<PermissionDefinitionRequest>? Permissions);

/// <summary>Permissão declarada pelo produto.</summary>
/// <param name="Name">
/// Nome canônico <c>recurso:acao</c> (kebab-case minúsculo) ou família <c>prefixo-{param}:acao</c>
/// (ex.: <c>setor-{id}:read</c>, ADR-0043).
/// </param>
/// <param name="Description">Descrição curta, de 1 a 200 caracteres, sem caractere de controle.</param>
public sealed record PermissionDefinitionRequest(string? Name, string? Description);
