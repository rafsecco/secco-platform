namespace Secco.SecureGate.Application.Federation;

/// <summary>
/// Atributos de uma pessoa do diretório federado, lidos do Microsoft Graph. Todo texto aqui é INPUT EXTERNO — vem
/// do diretório do cliente, não do operador: quem o usa valida tamanho e caracteres de controle antes de persistir
/// ou registrar (<see cref="DirectoryAttributeRules"/>).
/// </summary>
/// <param name="ObjectId">Object id (<c>oid</c>) da pessoa no diretório.</param>
/// <param name="JobTitle">Cargo (<c>jobTitle</c>), cru; pode ser nulo.</param>
/// <param name="Department">Departamento (<c>department</c>), cru; pode ser nulo.</param>
/// <param name="BusinessPhone">Primeiro telefone comercial (<c>businessPhones</c>), cru; pode ser nulo.</param>
/// <param name="ManagerObjectId">Object id do gestor no diretório; nulo quando a pessoa não tem gestor.</param>
public sealed record DirectoryAttributes(
	Guid ObjectId,
	string? JobTitle,
	string? Department,
	string? BusinessPhone,
	Guid? ManagerObjectId);
