namespace Secco.SecureGate.Application.Tenancy;

/// <summary>Estado do escopo de acesso a dados de tenant no banco do SecureGate (ADR-0041).</summary>
public enum TenantAccessMode
{
	/// <summary>Nenhum escopo: as entidades de tenant não aparecem (fail-closed).</summary>
	None = 0,

	/// <summary>Escopo de UM tenant: só os dados dele aparecem.</summary>
	Tenant = 1,

	/// <summary>Modo plataforma: caminho cross-tenant explícito, com motivo, listado numa allowlist versionada.</summary>
	Platform = 2,
}

/// <summary>
/// Escopo de acesso por requisição (ADR-0041): fonte única do tenant que os filtros globais do EF
/// aplicam. Entrar num estado devolve um <see cref="IDisposable"/> que restaura exatamente o
/// anterior, delimitando o trecho. Preferência registrada: entrar no TENANT sempre que ele for
/// conhecido; o modo plataforma só onde o tenant não existe.
/// </summary>
public interface ITenantAccessScope
{
	/// <summary>Estado atual.</summary>
	TenantAccessMode Mode { get; }

	/// <summary>Tenant do escopo; só tem valor quando <see cref="Mode"/> é <see cref="TenantAccessMode.Tenant"/>.</summary>
	Guid? TenantId { get; }

	/// <summary>Entra no escopo de um tenant.</summary>
	/// <param name="tenantId">Tenant (nunca vazio). Vem da rota validada ou de valor emitido pelo próprio SecureGate — nunca de corpo, query ou header.</param>
	/// <returns>Handle que restaura o estado anterior ao ser descartado.</returns>
	/// <exception cref="ArgumentException">Se <paramref name="tenantId"/> for vazio.</exception>
	IDisposable EnterTenant(Guid tenantId);

	/// <summary>Entra no modo plataforma (cross-tenant).</summary>
	/// <param name="reason">Motivo obrigatório; só vai a log em Debug, nunca a resposta.</param>
	/// <returns>Handle que restaura o estado anterior ao ser descartado.</returns>
	/// <exception cref="ArgumentException">Se o motivo for vazio.</exception>
	IDisposable EnterPlatform(string reason);
}
