using Microsoft.Extensions.Options;
using Secco.SecureGate.Application.InstallationOperators;

namespace Secco.SecureGate.Infrastructure.InstallationOperators;

/// <summary>
/// Designações lidas da configuração a cada consulta (<see cref="IOptionsMonitor{TOptions}"/>, ADR-0049): com o
/// provedor de configuração recarregável, tirar a conta da lista revoga sem reiniciar o processo.
/// </summary>
internal sealed class ConfiguredInstallationOperatorDesignations(IOptionsMonitor<InstallationOperatorOptions> options)
	: IInstallationOperatorDesignations
{
	public IReadOnlyCollection<Guid> Current => [.. options.CurrentValue.Accounts.Where(id => id != Guid.Empty).Distinct()];

	public bool IsDesignated(Guid userId) =>
		userId != Guid.Empty && options.CurrentValue.Accounts.Contains(userId);
}
