using Secco.SecureGate.Application.Users;

namespace Secco.SecureGate.Application.Federation;

/// <summary>Conta dona do tenant vinculada ao diretório, com o estado atual dos atributos de pessoa.</summary>
/// <param name="UserId">Conta no SecureGate.</param>
/// <param name="ObjectId">Object id da pessoa no diretório (o <c>oid</c> do vínculo).</param>
/// <param name="DirectoryManaged">Conta gerida pelo diretório (ADR-0055).</param>
/// <param name="IsActive">Conta ativa (nem desativada nem bloqueada).</param>
/// <param name="JobTitle">Cargo gravado.</param>
/// <param name="Department">Departamento gravado.</param>
/// <param name="BusinessPhone">Telefone comercial gravado.</param>
/// <param name="ManagerId">Conta do gestor no SecureGate.</param>
/// <param name="Origin">Origem dos atributos.</param>
public sealed record DirectoryAttributeAccount(
	Guid UserId,
	Guid ObjectId,
	bool DirectoryManaged,
	bool IsActive,
	string? JobTitle,
	string? Department,
	string? BusinessPhone,
	Guid? ManagerId,
	DirectoryAttributesOrigin Origin);

/// <summary>Novo valor dos atributos de uma conta.</summary>
/// <param name="UserId">Conta no SecureGate.</param>
/// <param name="JobTitle">Cargo já normalizado.</param>
/// <param name="Department">Departamento já normalizado.</param>
/// <param name="BusinessPhone">Telefone já normalizado.</param>
/// <param name="ManagerId">Conta do gestor no SecureGate.</param>
public sealed record DirectoryAttributeUpdate(
	Guid UserId, string? JobTitle, string? Department, string? BusinessPhone, Guid? ManagerId);

/// <summary>
/// Porta de persistência dos atributos de pessoa vindos do diretório (ADR-0055). Roda no escopo do tenant que o
/// chamador já entrou (ADR-0041).
/// </summary>
public interface IDirectoryAttributeStore
{
	/// <summary>
	/// Contas DONAS do tenant vinculadas a <c>{tid}:{oid}</c> do diretório, com o mesmo critério de vínculo da
	/// reconciliação de perfis. Membro por vínculo (ADR-0040) nunca entra.
	/// </summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="directoryId">Diretório (tid) da federação do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<DirectoryAttributeAccount>> ListLinkedAsync(
		Guid tenantId, Guid directoryId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Grava os quatro campos e marca a origem como diretório, só em conta dona do tenant. Não toca no nome.
	/// </summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="updates">Valores novos por conta.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Quantas contas foram gravadas.</returns>
	Task<int> ApplyAsync(
		Guid tenantId, IReadOnlyList<DirectoryAttributeUpdate> updates, CancellationToken cancellationToken = default);
}
