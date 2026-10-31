using System.Collections.Concurrent;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Graph falso dos testes de integração (ADR-0055): o que cada grupo contém é programado por (diretório, grupo), e
/// toda leitura fica registrada. Grupo não programado responde "não existe" — o teste precisa dizer o que o
/// diretório contém, nada vem de graça. Valida o formato da decisão do SecureGate, não o comportamento real do
/// Microsoft Graph (que exige um tenant Entra de teste, ADR-0036/0055).
/// </summary>
public sealed class FakeEntraGroupDirectory : IEntraGroupDirectory
{
	private readonly ConcurrentDictionary<(Guid Directory, Guid Group), Result<IReadOnlyList<Guid>>> _groups = new();
	private readonly ConcurrentQueue<string> _calls = new();
	private readonly ConcurrentDictionary<Guid, bool> _throwing = new();
	private readonly ConcurrentDictionary<(Guid Directory, Guid Group), IReadOnlyList<DirectoryPerson>> _people = new();
	private readonly ConcurrentDictionary<(Guid Directory, Guid Object), DirectoryPerson> _profiles = new();
	private readonly ConcurrentDictionary<Guid, bool> _profileForbidden = new();

	/// <summary>
	/// Leituras feitas, na ordem: <c>members:{dir}:{grupo}</c>, <c>profiles:{dir}:{grupo}</c>, <c>person:{dir}:{oid}</c> e
	/// <c>check:{dir}:{oid}</c>.
	/// </summary>
	public IReadOnlyList<string> Calls => [.. _calls];

	/// <summary>Programa os membros (object ids) do grupo.</summary>
	public void SetMembers(Guid directoryId, Guid groupId, params Guid[] objectIds) =>
		_groups[(directoryId, groupId)] = Result.Success<IReadOnlyList<Guid>>([.. objectIds]);

	/// <summary>
	/// Programa as pessoas (com perfil básico) do grupo: serve tanto a leitura de ids quanto a de perfil, e cada pessoa
	/// também responde à leitura individual.
	/// </summary>
	public void SetPeople(Guid directoryId, Guid groupId, params DirectoryPerson[] people)
	{
		_people[(directoryId, groupId)] = people;
		SetMembers(directoryId, groupId, [.. people.Select(person => person.ObjectId)]);

		foreach (var person in people)
		{
			_profiles[(directoryId, person.ObjectId)] = person;
		}
	}

	/// <summary>Programa o perfil de uma pessoa que não está em grupo programado (leitura individual do primeiro login).</summary>
	public void SetPerson(Guid directoryId, DirectoryPerson person) => _profiles[(directoryId, person.ObjectId)] = person;

	/// <summary>Simula o diretório SEM o consentimento <c>User.ReadBasic.All</c>: ids funcionam, perfil devolve 403.</summary>
	public void ForbidProfiles(Guid directoryId) => _profileForbidden[directoryId] = true;

	/// <summary>Programa a leitura do grupo para falhar.</summary>
	public void SetFailure(Guid directoryId, Guid groupId, Error error) =>
		_groups[(directoryId, groupId)] = Result.Failure<IReadOnlyList<Guid>>(error);

	/// <summary>Faz toda leitura do diretório LANÇAR exceção — o caso que o isolamento por tenant do ciclo tem de aguentar.</summary>
	public void ThrowFor(Guid directoryId) => _throwing[directoryId] = true;

	/// <summary>Quantas leituras o diretório recebeu.</summary>
	public int CallsFor(Guid directoryId) =>
		_calls.Count(call => call.Contains(directoryId.ToString("D"), StringComparison.Ordinal));

	/// <inheritdoc />
	public Task<Result<EntraGroupPage>> ListGroupsAsync(
		Guid directoryId, string? nameFilter, string? pageToken, int pageSize, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<EntraGroupPage>(SecureGateErrors.Federation.NotEnabled));

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<Guid>>> GetGroupMemberIdsAsync(
		Guid directoryId, Guid groupId, CancellationToken cancellationToken = default)
	{
		_calls.Enqueue($"members:{directoryId:D}:{groupId:D}");
		ThrowIfProgrammed(directoryId);

		return Task.FromResult(_groups.TryGetValue((directoryId, groupId), out var known)
			? known
			: Result.Failure<IReadOnlyList<Guid>>(SecureGateErrors.Federation.DirectoryObjectNotFound));
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<DirectoryPerson>>> GetGroupMembersWithProfileAsync(
		Guid directoryId, Guid groupId, CancellationToken cancellationToken = default)
	{
		_calls.Enqueue($"profiles:{directoryId:D}:{groupId:D}");
		ThrowIfProgrammed(directoryId);

		if (_profileForbidden.ContainsKey(directoryId))
		{
			return Task.FromResult(Result.Failure<IReadOnlyList<DirectoryPerson>>(SecureGateErrors.Federation.ConsentRequired));
		}

		if (!_groups.TryGetValue((directoryId, groupId), out var group))
		{
			return Task.FromResult(Result.Failure<IReadOnlyList<DirectoryPerson>>(SecureGateErrors.Federation.DirectoryObjectNotFound));
		}

		if (group.IsFailure)
		{
			return Task.FromResult(Result.Failure<IReadOnlyList<DirectoryPerson>>(group.Error));
		}

		return Task.FromResult(Result.Success(
			_people.TryGetValue((directoryId, groupId), out var people)
				? people
				: (IReadOnlyList<DirectoryPerson>)[.. group.Value.Select(id => new DirectoryPerson(id, null, null, null))]));
	}

	/// <inheritdoc />
	public Task<Result<DirectoryPerson>> GetPersonAsync(
		Guid directoryId, Guid objectId, CancellationToken cancellationToken = default)
	{
		_calls.Enqueue($"person:{directoryId:D}:{objectId:D}");
		ThrowIfProgrammed(directoryId);

		if (_profileForbidden.ContainsKey(directoryId))
		{
			return Task.FromResult(Result.Failure<DirectoryPerson>(SecureGateErrors.Federation.ConsentRequired));
		}

		return Task.FromResult(_profiles.TryGetValue((directoryId, objectId), out var person)
			? Result.Success(person)
			: Result.Failure<DirectoryPerson>(SecureGateErrors.Federation.DirectoryObjectNotFound));
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlySet<Guid>>> CheckMemberGroupsAsync(
		Guid directoryId, Guid objectId, IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken = default)
	{
		_calls.Enqueue($"check:{directoryId:D}:{objectId:D}");
		ThrowIfProgrammed(directoryId);
		var memberOf = new HashSet<Guid>();

		foreach (var groupId in groupIds)
		{
			if (!_groups.TryGetValue((directoryId, groupId), out var group))
			{
				return Task.FromResult(Result.Failure<IReadOnlySet<Guid>>(SecureGateErrors.Federation.DirectoryObjectNotFound));
			}

			if (group.IsFailure)
			{
				return Task.FromResult(Result.Failure<IReadOnlySet<Guid>>(group.Error));
			}

			if (group.Value.Contains(objectId))
			{
				memberOf.Add(groupId);
			}
		}

		return Task.FromResult(Result.Success<IReadOnlySet<Guid>>(memberOf));
	}

	private void ThrowIfProgrammed(Guid directoryId)
	{
		if (_throwing.ContainsKey(directoryId))
		{
			throw new InvalidOperationException("Graph falso programado para lançar.");
		}
	}
}
