namespace Secco.SecureGate.Application.Users;

/// <summary>
/// Situação de uma conta como o admin a vê. Strings, e não enum, de propósito: o contrato OpenAPI
/// sai sem <c>type: string</c> para enum e o client NSwag quebra na desserialização (Fase 7.3).
/// </summary>
public static class UserStatuses
{
	/// <summary>Conta utilizável.</summary>
	public const string Active = "Active";

	/// <summary>Desativada pelo admin (lockout sem fim).</summary>
	public const string Deactivated = "Deactivated";

	/// <summary>Bloqueada temporariamente por tentativas de senha.</summary>
	public const string LockedOut = "LockedOut";

	/// <summary>Ano a partir do qual o lockout é tratado como desativação.</summary>
	private const int DeactivatedFromYear = 9999;

	/// <summary>Deriva a situação a partir do lockout do Identity.</summary>
	/// <param name="lockoutEnabled">Se o lockout está habilitado — desligado, o Identity ignora a data.</param>
	/// <param name="lockoutEnd">Fim do bloqueio.</param>
	/// <param name="now">Instante de referência.</param>
	public static string From(bool lockoutEnabled, DateTimeOffset? lockoutEnd, DateTimeOffset now)
	{
		if (!lockoutEnabled || lockoutEnd is not { } end || end <= now)
		{
			return Active;
		}

		// A desativação grava DateTimeOffset.MaxValue. Compara pelo ano, e não por igualdade: o
		// PostgreSQL guarda microssegundos e devolve o máximo truncado, que deixaria de ser igual.
		return end.Year >= DeactivatedFromYear ? Deactivated : LockedOut;
	}
}
