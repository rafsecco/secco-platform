using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.SecureGate.Domain.Scheduling;

/// <summary>
/// Lease de uma tarefa periódica no banco de PLATAFORMA (ADR-0055): uma linha por tarefa, com o dono atual e a
/// expiração. É o que garante que, com várias instâncias do SecureGate no ar, só uma roda o ciclo por vez
/// (ADR-0035 — estado compartilhado que todas as instâncias precisam ver). Dado de plataforma, sem tenant: fora do
/// filtro e do Row-Level Security (ADR-0041).
/// </summary>
/// <remarks>
/// A tomada e a renovação são atômicas no banco (<c>UPDATE ... WHERE</c> condicional, nos dois engines); a entidade
/// só dá forma ao schema e à primeira inserção.
/// </remarks>
public sealed class BackgroundLease : BaseEntity
{
	/// <summary>Tamanho máximo do nome da tarefa (coluna <c>ds_name</c>).</summary>
	public const int NameMaxLength = 100;

	/// <summary>Tamanho máximo do identificador do dono (coluna <c>ds_owner</c>).</summary>
	public const int OwnerMaxLength = 100;

	private BackgroundLease()
	{
		// Construtor de rehidratação do EF Core
		Name = string.Empty;
		Owner = string.Empty;
	}

	/// <summary>Cria o lease, já tomado pelo dono até a expiração.</summary>
	/// <param name="name">Nome da tarefa. Obrigatório.</param>
	/// <param name="owner">Identificador da instância dona. Obrigatório.</param>
	/// <param name="expiresAt">Instante em que o lease expira.</param>
	/// <exception cref="DomainInvariantException">Se o nome ou o dono forem vazios ou longos demais.</exception>
	public BackgroundLease(string name, string owner, DateTimeOffset expiresAt)
	{
		if (string.IsNullOrWhiteSpace(name) || name.Length > NameMaxLength)
		{
			throw new DomainInvariantException($"O lease exige o nome da tarefa, com até {NameMaxLength} caracteres.");
		}

		if (string.IsNullOrWhiteSpace(owner) || owner.Length > OwnerMaxLength)
		{
			throw new DomainInvariantException($"O lease exige o dono, com até {OwnerMaxLength} caracteres.");
		}

		Name = name;
		Owner = owner;
		ExpiresAt = expiresAt;
	}

	/// <summary>Nome da tarefa (coluna <c>ds_name</c>, índice único).</summary>
	public string Name { get; private set; }

	/// <summary>Instância que detém o lease (coluna <c>ds_owner</c>).</summary>
	public string Owner { get; private set; }

	/// <summary>Instante em que o lease expira (coluna <c>dt_expires_at</c>).</summary>
	public DateTimeOffset ExpiresAt { get; private set; }
}
