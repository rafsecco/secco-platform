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
	/// <summary>Permissões do recurso Sample.</summary>
	public static class Samples
	{
		/// <summary>Consultar samples.</summary>
		public const string Read = "samples:read";

		/// <summary>Criar samples.</summary>
		public const string Write = "samples:write";
	}
}
