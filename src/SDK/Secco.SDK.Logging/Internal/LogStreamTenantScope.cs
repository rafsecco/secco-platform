using Secco.SharedKernel.Constants;

namespace Secco.SDK.Logging.Internal;

/// <summary>
/// Tenant do lote que está sendo enviado no momento, para que o handler HTTP saiba qual
/// <c>X-Tenant-Id</c> anexar.
/// </summary>
/// <remarks>
/// O client é gerado pelo NSwag (ADR-0006) e não expõe headers por chamada; editá-lo à mão é
/// proibido. O caminho suportado é um <c>DelegatingHandler</c> lendo um valor ambiente — daí
/// este tipo. O token do sink é de máquina e não carrega <c>tenant_id</c>, então o header é o
/// caminho legítimo da ADR-0005 ("sem claim → header"), sem esbarrar na regra de conflito.
/// </remarks>
internal static class LogStreamTenantScope
{
	private static readonly AsyncLocal<Guid?> Current = new();

	/// <summary>Tenant do envio em andamento, quando há um.</summary>
	public static Guid? TenantId => Current.Value;

	/// <summary>Estabelece o tenant do envio até o descarte do retorno.</summary>
	/// <param name="tenantId">Tenant do lote.</param>
	public static IDisposable For(Guid tenantId)
	{
		Current.Value = tenantId;

		return new Scope();
	}

	private sealed class Scope : IDisposable
	{
		public void Dispose() => Current.Value = null;
	}
}

/// <summary>Anexa o <c>X-Tenant-Id</c> do lote em cada requisição de ingestão.</summary>
internal sealed class LogStreamTenantHeaderHandler : DelegatingHandler
{
	/// <inheritdoc />
	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (LogStreamTenantScope.TenantId is { } tenantId)
		{
			request.Headers.TryAddWithoutValidation(SeccoHeaders.TenantId, tenantId.ToString());
		}

		return base.SendAsync(request, cancellationToken);
	}
}
