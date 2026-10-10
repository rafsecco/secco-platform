using Secco.SharedKernel.Authorization;

namespace Secco.SampleService.Application;

/// <summary>
/// Permissões do recurso Sample de EXEMPLO (ADR-0021, formato canônico <c>recurso:acao</c>
/// do kernel). As constantes vivem no PRODUTO — não no SharedKernel — pela regra de admissão
/// da ADR-0003. Usadas direto como policy:
/// <c>RequireAuthorization(SampleServicePermissions.Samples.Write)</c>.
/// Este arquivo faz parte do recurso Sample de exemplo — apague junto com o restante quando
/// o domínio real começar.
/// </summary>
public static class SampleServicePermissions
{
	/// <summary>
	/// Identificador do produto no catálogo de permissões do SecureGate (ADR-0039): kebab-case, o mesmo
	/// do escopo <c>permissions:&lt;produto&gt;</c> do client de plataforma. Nasce em minúsculas
	/// (<c>sampleservice</c>) — ajuste junto do restante do scaffold (item 6 do README).
	/// </summary>
	public const string Product = "sampleservice";

	/// <summary>Permissões do recurso Sample.</summary>
	public static class Samples
	{
		/// <summary>Consultar samples.</summary>
		public const string Read = "samples:read";

		/// <summary>Criar samples.</summary>
		public const string Write = "samples:write";
	}

	/// <summary>
	/// Catálogo de permissões do produto, publicado no SecureGate pelo <c>migrate</c> (ADR-0039).
	/// Toda constante acima precisa de uma entrada aqui — o teste de reflexão garante.
	/// </summary>
	public static IReadOnlyList<SeccoPermissionDefinition> Catalog { get; } =
	[
		new(Samples.Read, "Consultar samples"),
		new(Samples.Write, "Criar samples"),
	];
}
