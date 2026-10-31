namespace Secco.SecureGate.Application.Federation;

/// <summary>
/// Chave do vínculo de uma conta com o diretório federado (ADR-0026): provedor <c>EntraId</c> e chave
/// <c>{tid}:{oid}</c> em <c>tb_user_logins</c>. Centraliza o formato para o login, o vínculo e a reconciliação
/// (ADR-0055) lerem e escreverem a mesma coisa.
/// </summary>
public static class DirectoryLinkKey
{
	/// <summary>Nome do provider persistido em <c>tb_user_logins</c>.</summary>
	public const string Provider = "EntraId";

	/// <summary>Monta a chave <c>{tid}:{oid}</c>.</summary>
	/// <param name="directoryId">Directory id (<c>tid</c>).</param>
	/// <param name="objectId">Object id da conta no diretório (<c>oid</c>).</param>
	public static string Build(Guid directoryId, Guid objectId) => $"{directoryId:D}:{objectId:D}";

	/// <summary>Prefixo de todas as chaves de um diretório (<c>{tid}:</c>) — só hexadecimal e hífen, sem curinga de LIKE.</summary>
	/// <param name="directoryId">Directory id (<c>tid</c>).</param>
	public static string Prefix(Guid directoryId) => $"{directoryId:D}:";

	/// <summary>Extrai o <c>oid</c> de uma chave do diretório informado.</summary>
	/// <param name="providerKey">Chave persistida.</param>
	/// <param name="directoryId">Directory id esperado.</param>
	/// <param name="objectId">O <c>oid</c>, quando a chave é do diretório e está bem formada.</param>
	/// <returns><c>false</c> se a chave é de outro diretório ou está malformada.</returns>
	public static bool TryGetObjectId(string providerKey, Guid directoryId, out Guid objectId)
	{
		objectId = Guid.Empty;
		var prefix = Prefix(directoryId);

		return providerKey.StartsWith(prefix, StringComparison.Ordinal)
			&& Guid.TryParseExact(providerKey.AsSpan(prefix.Length), "D", out objectId)
			&& objectId != Guid.Empty;
	}
}
