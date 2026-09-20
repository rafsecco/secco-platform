using System.Threading.RateLimiting;
using Secco.SecureGate.Application.Credentials;

namespace Secco.SecureGate.Infrastructure.Credentials;

/// <summary>
/// Limite do "esqueci minha senha" por conta e por IP, em janela fixa de uma hora (ADR-0033).
/// </summary>
/// <remarks>
/// Usa o limitador nativo do .NET (<c>System.Threading.RateLimiting</c>), sem middleware: o
/// middleware responderia <c>429</c>, e uma resposta diferente ao exceder o limite conta ao
/// atacante que aquele e-mail existe. Aqui o limite só decide <b>se o e-mail sai</b> — a página
/// responde igual em todos os casos.
/// <para>
/// As partições ociosas são recolhidas pelo próprio <see cref="PartitionedRateLimiter"/>, o que
/// impede que uma enxurrada de e-mails distintos faça a memória crescer sem limite (ADR-0020).
/// </para>
/// </remarks>
internal sealed class FixedWindowPasswordResetThrottle : IPasswordResetThrottle, IDisposable
{
	private readonly PartitionedRateLimiter<string> _byAccount;
	private readonly PartitionedRateLimiter<string> _byAddress;

	/// <summary>Cria o limitador com as janelas da configuração.</summary>
	/// <param name="options">Configuração de credencial.</param>
	public FixedWindowPasswordResetThrottle(CredentialOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		_byAccount = Build(options.ForgotPerAccountPerHour);
		_byAddress = Build(options.ForgotPerIpPerHour);
	}

	/// <inheritdoc />
	public bool TryAcquire(string email, string? remoteAddress)
	{
		ArgumentNullException.ThrowIfNull(email);

		// O e-mail entra normalizado: senão "Ana@x" e "ana@x" seriam duas cotas para a mesma conta.
		using var account = _byAccount.AttemptAcquire(email.ToUpperInvariant());

		if (!account.IsAcquired)
		{
			return false;
		}

		// Sem endereço conhecido (proxy mal configurado, teste), só o limite por conta vale.
		if (string.IsNullOrWhiteSpace(remoteAddress))
		{
			return true;
		}

		using var address = _byAddress.AttemptAcquire(remoteAddress);

		return address.IsAcquired;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_byAccount.Dispose();
		_byAddress.Dispose();
	}

	private static PartitionedRateLimiter<string> Build(int permitsPerHour) =>
		PartitionedRateLimiter.Create<string, string>(key =>
			RateLimitPartition.GetFixedWindowLimiter(
				key,
				_ => new FixedWindowRateLimiterOptions
				{
					PermitLimit = permitsPerHour,
					Window = TimeSpan.FromHours(1),
					QueueLimit = 0,
					AutoReplenishment = true,
				}));
}
