using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Credentials;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// A trilha de credencial é <b>best-effort</b> (ADR-0033): LogStream fora do ar não pode impedir
/// ninguém de recuperar a conta nem de encerrar sessões — que é justamente o que se faz durante
/// um incidente. A elevação (ADR-0031) segue no extremo oposto, fail-closed, por atravessar a
/// fronteira de tenant.
/// </summary>
public class CredentialAuditorBestEffortTests
{
	private static readonly Guid UserId = Guid.Parse("018f0000-0000-7000-8000-0000000000aa");
	private static readonly Guid TenantId = Guid.Parse("018f0000-0000-7000-8000-0000000000bb");

	private static LogStreamCredentialAuditor Build(IHttpClientFactory httpClientFactory) =>
		new(httpClientFactory, NullLogger<LogStreamCredentialAuditor>.Instance);

	[Fact]
	public async Task Auditoria_QuandoOClientNemSobe_NaoLanca()
	{
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(Arg.Any<string>())
			.Returns(_ => throw new InvalidOperationException("LogStream indisponível"));

		var act = async () => await Build(httpClientFactory)
			.RecordAsync(CredentialAuditEvent.PasswordReset, UserId, TenantId, "ana@acme.test");

		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task Auditoria_QuandoOLogStreamRecusa_NaoLanca()
	{
		using var client = new HttpClient(new AlwaysFailsHandler())
		{
			BaseAddress = new Uri("https://logs.exemplo"),
		};

		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(Arg.Any<string>()).Returns(client);

		var act = async () => await Build(httpClientFactory)
			.RecordAsync(CredentialAuditEvent.PasswordSet, UserId, TenantId, "ana@acme.test");

		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task Auditoria_ComCancelamentoDoChamador_PropagaOCancelamento()
	{
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		using var client = new HttpClient(new AlwaysFailsHandler()) { BaseAddress = new Uri("https://logs.exemplo") };
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(Arg.Any<string>()).Returns(client);

		// Requisição abortada pelo chamador não é falha de trilha: não há o que engolir.
		var act = async () => await Build(httpClientFactory)
			.RecordAsync(CredentialAuditEvent.LinkRejected, UserId, TenantId, "ana@acme.test", cancellation.Token);

		await act.Should().ThrowAsync<OperationCanceledException>();
	}

	private sealed class AlwaysFailsHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
			{
				Content = new StringContent("erro interno"),
			});
		}
	}
}
