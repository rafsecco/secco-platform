using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application.Scheduling;
using Secco.SecureGate.Domain.Scheduling;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Infrastructure.Scheduling;

/// <summary>
/// Lease no banco de plataforma (ADR-0055). A tomada, a renovação e o encerramento são UM <c>UPDATE</c>
/// condicional cada — a atomicidade é do banco, igual em SQL Server e em PostgreSQL, sem lock de aplicação nem
/// leitura-seguida-de-escrita. A primeira tomada insere a linha; o índice único por nome decide quem ganha a corrida.
/// </summary>
internal sealed class BackgroundLeaseStore(SecureGateDbContext context) : IBackgroundLeaseStore
{
	public async Task<bool> TryAcquireAsync(
		string taskName, string owner, TimeSpan duration, DateTimeOffset now, CancellationToken cancellationToken = default)
	{
		var expiresAt = now + duration;

		// Só um lease EXPIRADO é tomado — nem o do próprio dono: o lease que o ciclo deixa válido até a próxima
		// execução é justamente o que impede uma segunda passada no mesmo intervalo
		var updated = await context.BackgroundLeases
			.Where(lease => lease.Name == taskName && lease.ExpiresAt <= now)
			.ExecuteUpdateAsync(
				set => set.SetProperty(lease => lease.Owner, owner).SetProperty(lease => lease.ExpiresAt, expiresAt),
				cancellationToken).ConfigureAwait(false);

		if (updated == 1)
		{
			return true;
		}

		// Linha existe e está válida: outra instância (ou esta, no intervalo) detém o lease
		if (await context.BackgroundLeases.AsNoTracking()
			.AnyAsync(lease => lease.Name == taskName, cancellationToken).ConfigureAwait(false))
		{
			return false;
		}

		context.BackgroundLeases.Add(new BackgroundLease(taskName, owner, expiresAt));

		try
		{
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

			return true;
		}
		catch (DbUpdateException)
		{
			// Duas instâncias inseriram juntas: o índice único deu a vez a uma só
			context.ChangeTracker.Clear();

			return false;
		}
	}

	public async Task<bool> RenewAsync(
		string taskName, string owner, TimeSpan duration, DateTimeOffset now, CancellationToken cancellationToken = default)
	{
		var expiresAt = now + duration;

		return await context.BackgroundLeases
			.Where(lease => lease.Name == taskName && lease.Owner == owner)
			.ExecuteUpdateAsync(set => set.SetProperty(lease => lease.ExpiresAt, expiresAt), cancellationToken)
			.ConfigureAwait(false) == 1;
	}

	public async Task CompleteAsync(
		string taskName, string owner, DateTimeOffset nextDueAt, CancellationToken cancellationToken = default) =>
		_ = await context.BackgroundLeases
			.Where(lease => lease.Name == taskName && lease.Owner == owner)
			.ExecuteUpdateAsync(set => set.SetProperty(lease => lease.ExpiresAt, nextDueAt), cancellationToken)
			.ConfigureAwait(false);
}
