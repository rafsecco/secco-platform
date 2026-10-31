namespace Secco.SecureGate.Application.Scheduling;

/// <summary>
/// Lease de tarefa periódica no banco de plataforma (ADR-0055, ADR-0035): exclusão mútua entre instâncias, sem
/// depender de Hangfire (SQL-Server-only) nem de relógio sincronizado além do que o banco compara. Todo método é
/// atômico e funciona em SQL Server e em PostgreSQL.
/// </summary>
public interface IBackgroundLeaseStore
{
	/// <summary>
	/// Toma o lease se ele não existe ou já expirou. Um lease ainda válido — inclusive o do próprio dono — NÃO é
	/// retomado: é isso que faz o ciclo rodar uma vez por intervalo, e não uma vez por instância por tique.
	/// </summary>
	/// <param name="taskName">Nome da tarefa.</param>
	/// <param name="owner">Identificador da instância que pede.</param>
	/// <param name="duration">Duração do lease tomado.</param>
	/// <param name="now">Instante corrente.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>true</c> se este dono passou a deter o lease.</returns>
	Task<bool> TryAcquireAsync(
		string taskName, string owner, TimeSpan duration, DateTimeOffset now, CancellationToken cancellationToken = default);

	/// <summary>Estende o lease, se ainda for deste dono.</summary>
	/// <param name="taskName">Nome da tarefa.</param>
	/// <param name="owner">Identificador da instância dona.</param>
	/// <param name="duration">Nova duração, a partir de <paramref name="now"/>.</param>
	/// <param name="now">Instante corrente.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se o lease já não é deste dono (perdido para outra instância).</returns>
	Task<bool> RenewAsync(
		string taskName, string owner, TimeSpan duration, DateTimeOffset now, CancellationToken cancellationToken = default);

	/// <summary>
	/// Encerra a execução deixando o lease válido até <paramref name="nextDueAt"/>: nenhuma instância retoma a
	/// tarefa antes disso. Passar um instante já vencido libera a tarefa na hora (retentativa). Só vale para o dono.
	/// </summary>
	/// <param name="taskName">Nome da tarefa.</param>
	/// <param name="owner">Identificador da instância dona.</param>
	/// <param name="nextDueAt">Instante em que a tarefa volta a poder rodar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task CompleteAsync(string taskName, string owner, DateTimeOffset nextDueAt, CancellationToken cancellationToken = default);
}
