namespace Secco.SecureGate.Api.Requests;

/// <summary>Payload de criação de tenant.</summary>
/// <param name="Name">Nome de exibição. Obrigatório.</param>
/// <param name="Slug">Identificador curto único (kebab-case minúsculo). Obrigatório.</param>
public sealed record CreateTenantRequest(string? Name, string? Slug);

/// <summary>
/// Payload de cadastro/substituição do banco de um tenant em um produto.
/// Write-only por design (ADR-0020): nenhuma resposta da API devolve a connection string.
/// </summary>
/// <param name="ConnectionString">Connection string do banco dedicado. Obrigatória.</param>
public sealed record UpsertTenantDatabaseRequest(string? ConnectionString);

/// <summary>
/// Payload de provisionamento do banco de um tenant em um produto (issue #3).
/// </summary>
/// <param name="Target">
/// Alvo declarado em <c>SecureGate:Provisioning:Targets</c>; vazio usa o alvo padrão. Sem alvo
/// configurado, informe <paramref name="Server"/>.
/// </param>
/// <param name="Server">Servidor de destino, quando nenhum alvo está configurado.</param>
/// <param name="CreateDatabase">
/// <c>true</c> cria o database (tenant novo); <c>false</c> assume um banco já existente e só cria
/// o usuário de aplicação com o mínimo necessário nele — os dois cenários de adoção da issue,
/// que exigem privilégios diferentes.
/// </param>
/// <param name="DatabaseName">Nome do database; vazio deriva de produto + slug do tenant.</param>
/// <param name="LoginName">Nome do usuário de aplicação; vazio deriva do nome do database.</param>
public sealed record ProvisionTenantDatabaseRequest(
	string? Target = null,
	string? Server = null,
	bool CreateDatabase = true,
	string? DatabaseName = null,
	string? LoginName = null);

/// <summary>
/// Payload de cadastro/atualização da federação de autenticação de um tenant (ADR-0026).
/// O directory id NÃO é segredo (é o <c>tid</c> esperado do Entra ID) — aparece na leitura.
/// </summary>
/// <param name="DirectoryId">Directory id (tenant GUID do Entra ID da empresa). Obrigatório.</param>
/// <param name="Enabled">Habilita o login federado. Ausente = habilitado.</param>
public sealed record UpsertTenantFederationRequest(Guid? DirectoryId, bool? Enabled);

/// <summary>Payload de criação de role (ADR-0021).</summary>
/// <param name="Name">Nome do role (sem espaços). Obrigatório.</param>
public sealed record CreateRoleRequest(string? Name);

/// <summary>Payload de substituição das permissões de um role (PUT idempotente).</summary>
/// <param name="Permissions">Conjunto completo de permissões <c>recurso:acao</c> desejado.</param>
public sealed record SetRolePermissionsRequest(IReadOnlyList<string?>? Permissions);
