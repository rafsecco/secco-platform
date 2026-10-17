using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.SecureGate.Application.Tenancy;

namespace Secco.SecureGate.Infrastructure.Tenancy;

/// <summary>
/// Implementação scoped do <see cref="ITenantAccessScope"/> (ADR-0041). Cada <c>Enter*</c> guarda o
/// estado anterior no handle; descartar o handle restaura exatamente esse estado, então
/// plataforma → tenant → volta a plataforma funciona. Começa em <see cref="TenantAccessMode.None"/>.
/// </summary>
public sealed partial class TenantAccessScope : ITenantAccessScope
{
	private readonly ILogger<TenantAccessScope> _logger;
	private State _current = new(TenantAccessMode.None, null);

	/// <summary>Cria o escopo, em <see cref="TenantAccessMode.None"/>.</summary>
	/// <param name="logger">Logger (o motivo do modo plataforma só vai em Debug).</param>
	public TenantAccessScope(ILogger<TenantAccessScope> logger)
	{
		ArgumentNullException.ThrowIfNull(logger);
		_logger = logger;
	}

	/// <summary>Escopo em <see cref="TenantAccessMode.None"/> para contextos criados fora de DI (migrations, design-time).</summary>
	public static TenantAccessScope Detached() => new(NullLogger<TenantAccessScope>.Instance);

	/// <inheritdoc />
	public TenantAccessMode Mode => _current.Mode;

	/// <inheritdoc />
	public Guid? TenantId => _current.TenantId;

	/// <inheritdoc />
	public IDisposable EnterTenant(Guid tenantId)
	{
		if (tenantId == Guid.Empty)
		{
			throw new ArgumentException("O tenant do escopo não pode ser vazio.", nameof(tenantId));
		}

		return Enter(new State(TenantAccessMode.Tenant, tenantId));
	}

	/// <inheritdoc />
	public IDisposable EnterPlatform(string reason)
	{
		if (string.IsNullOrWhiteSpace(reason))
		{
			throw new ArgumentException("O modo plataforma exige um motivo.", nameof(reason));
		}

		LogEnterPlatform(reason);

		return Enter(new State(TenantAccessMode.Platform, null));
	}

	[LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Escopo de acesso em modo plataforma: {Reason}")]
	private partial void LogEnterPlatform(string reason);

	private Handle Enter(State next)
	{
		var previous = _current;
		_current = next;

		return new Handle(this, previous);
	}

	private readonly record struct State(TenantAccessMode Mode, Guid? TenantId);

	private sealed class Handle(TenantAccessScope owner, State previous) : IDisposable
	{
		private bool _disposed;

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			owner._current = previous;
		}
	}
}
