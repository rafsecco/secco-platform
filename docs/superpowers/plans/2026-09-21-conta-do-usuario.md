# Conta do usuário (entrega C) — plano de implementação

> **Para quem executa:** SUB-SKILL OBRIGATÓRIA: use `superpowers:subagent-driven-development` (recomendado) ou `superpowers:executing-plans` para implementar tarefa a tarefa. Os passos usam caixas (`- [ ]`) para acompanhamento.

**Objetivo:** dar ao dono da conta o controle do próprio e-mail, com confirmação no endereço novo, e ao admin a capacidade de desfazer um vínculo morto com o Entra ID sem criar conta órfã.

**Arquitetura:** a troca de e-mail reusa inteira a infraestrutura da entrega B — porta de e-mail, provedores de token do Identity, trilha best-effort, limitador e o layout das telas de conta. O endereço pendente viaja **dentro do token** (convenção `ChangeEmail:{novo}` do Identity), então não há coluna nem migration. O desvínculo é um `DELETE` idempotente sob `securegate:admin`, com uma única guarda objetiva.

**Stack:** .NET 10, ASP.NET Identity, OpenIddict 7.5, EF Core 10, Razor Pages, Blazor Server (AdminPortal), xUnit + FluentAssertions 7 + NSubstitute + Testcontainers.

**Spec:** [`docs/superpowers/specs/2026-09-21-conta-do-usuario-design.md`](../specs/2026-09-21-conta-do-usuario-design.md)
**ADRs:** ADR-0033 (ciclo de credencial), ADR-0026 (federação), ADR-0032 (revogação), ADR-0034 (idempotência), ADR-0020 (segurança).

## Restrições globais

- **Estilo:** CRLF e **tabs**; XML docs em português em tudo que é público; `warnings = erros` em `src/`.
- **Camadas (ADR-0002):** `Application` não conhece `Microsoft.AspNetCore.*` nem EF Core. Porta em `Application`, adaptador em `Infrastructure`, composição na `Api`.
- **Erros (ADR-0004):** `Result<T>`/`Error` do SharedKernel; nunca exceção para fluxo.
- **Idempotência (ADR-0034):** o `DELETE` novo é idempotente de fato; efeito colateral só na transição real; teste de repetição obrigatório.
- **Segurança:** nenhuma senha, token ou endereço completo em log; resposta idêntica para e-mail livre e em uso.
- **Sem migration nesta entrega.** Se surgir necessidade de coluna, pare e reavalie — a spec assume que não há.
- **Testes (ADR-0012):** cada tarefa entrega teste no mesmo commit. Suíte: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release`.
- **Commits:** Conventional Commits, direto na `main`, sem branch nem PR. **Nunca `git push`** — publicação é a Tarefa 7, e só com autorização.
- **CRLF:** nunca `sed -i` em arquivo versionado; conferir `git diff --stat` após edição em massa.

## Estrutura de arquivos

**Application** (`src/SecureGate/Secco.SecureGate.Application/`): `Credentials/RequestEmailChangeHandler.cs`, `Credentials/ConfirmEmailChangeHandler.cs`, `Users/RemoveExternalLoginHandler.cs`; portas ampliadas em `Credentials/ICredentialTokens.cs`, `Credentials/ICredentialMailer.cs`, `Credentials/ICredentialAuditor.cs` e `Users/IUserDirectory.cs`.

**Infrastructure**: `Credentials/IdentityCredentialTokens.cs` (troca de e-mail), `Credentials/SeccoEmailCredentialMailer.cs` (dois e-mails novos), `Users/UserAccountService.cs` (remoção do vínculo).

**Api**: `Identity/CredentialTokenProviders.cs` (provedor `SeccoEmailChange`), `Extensions/SecureGateIdentityExtensions.cs` (liga o provedor em `IdentityOptions`), `Pages/Account/ChangeEmail.cshtml(.cs)`, `Pages/Account/ConfirmEmail.cshtml(.cs)`, `Pages/Account/Index.cshtml` (link novo), `Endpoints/UserEndpoints.cs` (rota do desvínculo).

**AdminPortal**: `Services/IUserAdminService.cs`, `Components/Pages/UserManagement.razor`.

**Testes**: `Integration/EmailChangeTests.cs` (novo), `Integration/ExternalLoginUnlinkTests.cs` (novo), `Unit/CredentialTokenLifetimeTests.cs` (ampliado), `tests/AdminPortal/.../IdentityAdminServicesTests.cs`.

---

### Task 1: provedor de token da troca de e-mail

**Arquivos:**
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Identity/CredentialTokenProviders.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Extensions/SecureGateIdentityExtensions.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Extensions/SecureGateCredentialsExtensions.cs`
- Modificar: `tests/SecureGate/Secco.SecureGate.Tests/Unit/CredentialTokenLifetimeTests.cs`

**Interfaces:**
- Produz: `CredentialTokenProviders.EmailChange = "SeccoEmailChange"`; `EmailChangeTokenProviderOptions : DataProtectionTokenProviderOptions`; `EmailChangeTokenProvider : DataProtectorTokenProvider<User>`.

- [ ] **Passo 1: escrever o teste que falha**

```csharp
// tests/SecureGate/Secco.SecureGate.Tests/Unit/CredentialTokenLifetimeTests.cs (novo fato)
[Fact]
public void Provedor_DeTrocaDeEmail_UsaAJanelaCurtaDaRedefinicao()
{
	using var provider = Build(inviteHours: 72, resetMinutes: 30);

	// Link de troca de e-mail é da mesma natureza do de redefinição — curto, uso único, enviado
	// por e-mail —, então compartilha a janela em vez de ganhar mais uma chave de configuração.
	provider.GetRequiredService<IOptions<EmailChangeTokenProviderOptions>>().Value.TokenLifespan
		.Should().Be(TimeSpan.FromMinutes(30));
}
```

- [ ] **Passo 2: rodar e ver falhar**

`dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter FullyQualifiedName~CredentialTokenLifetimeTests` → não compila (`EmailChangeTokenProviderOptions` inexistente).

- [ ] **Passo 3: implementar**

Em `CredentialTokenProviders.cs`, ao lado dos dois que já existem:

```csharp
	/// <summary>Troca de e-mail — mesma janela curta da redefinição.</summary>
	public const string EmailChange = "SeccoEmailChange";
```

```csharp
/// <summary>Validade do token de troca de e-mail (ver <see cref="InviteTokenProviderOptions"/>).</summary>
public sealed class EmailChangeTokenProviderOptions : DataProtectionTokenProviderOptions;

/// <summary>Provedor da troca de e-mail — só existe para carregar as próprias options.</summary>
/// <param name="dataProtectionProvider">Provedor de Data Protection.</param>
/// <param name="options">Validade da troca de e-mail.</param>
/// <param name="logger">Log do provedor base.</param>
public sealed class EmailChangeTokenProvider(
	IDataProtectionProvider dataProtectionProvider,
	IOptions<EmailChangeTokenProviderOptions> options,
	ILogger<DataProtectorTokenProvider<User>> logger)
	: DataProtectorTokenProvider<User>(dataProtectionProvider, options, logger);
```

Em `SecureGateCredentialsExtensions.AddSecureGateCredentials`, junto das outras validades:

```csharp
		services.AddOptions<EmailChangeTokenProviderOptions>().Configure<CredentialOptions>((options, credentials) =>
			options.TokenLifespan = TimeSpan.FromMinutes(credentials.ResetLifetimeMinutes));
```

Em `SecureGateIdentityExtensions`, dentro do `AddIdentityCore(options => ...)`, **depois** da política de senha:

```csharp
				// O ChangeEmailAsync do Identity usa ESTE provedor; apontá-lo para o nosso é o que
				// dá ao link de troca de e-mail a validade curta (ADR-0033).
				options.Tokens.ChangeEmailTokenProvider = CredentialTokenProviders.EmailChange;
```

E no encadeamento dos provedores:

```csharp
			.AddTokenProvider<EmailChangeTokenProvider>(CredentialTokenProviders.EmailChange);
```

- [ ] **Passo 4: rodar e ver passar** — mesmo filtro, verde.

- [ ] **Passo 5: commit**

```bash
git add src/SecureGate tests/SecureGate
git commit -m "feat(securegate): provedor de token da troca de e-mail (ADR-0033)"
```

---

### Task 2: portas de troca de e-mail na Application

**Arquivos:**
- Modificar: `src/SecureGate/Secco.SecureGate.Application/Credentials/ICredentialTokens.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Application/Credentials/ICredentialMailer.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Application/Credentials/ICredentialAuditor.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Infrastructure/Credentials/IdentityCredentialTokens.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Infrastructure/Credentials/SeccoEmailCredentialMailer.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Infrastructure/Credentials/LogStreamCredentialAuditor.cs`

**Interfaces:**
- Consome: `CredentialTokenProviders.EmailChange` (Tarefa 1).
- Produz:

```csharp
// ICredentialTokens
Task<bool> IsEmailAvailableAsync(string email, CancellationToken cancellationToken = default);
Task<string> CreateEmailChangeTokenAsync(Guid userId, string newEmail, CancellationToken cancellationToken = default);
Task<CredentialTokenOutcome> ChangeEmailAsync(Guid userId, string newEmail, string token, CancellationToken cancellationToken = default);
Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);

// ICredentialMailer
Task SendEmailChangeConfirmationAsync(string newRecipient, Guid userId, string newEmail, string token, CancellationToken cancellationToken = default);
Task SendEmailChangeNoticeAsync(string currentRecipient, string newEmail, CancellationToken cancellationToken = default);

// CredentialAuditEvent — três valores novos
EmailChangeRequested, EmailChanged, ExternalLoginRemoved
```

- [ ] **Passo 1: escrever o teste que falha**

```csharp
// tests/SecureGate/Secco.SecureGate.Tests/Integration/EmailChangeTests.cs (arquivo novo)
[Fact]
public async Task Token_DeTrocaDeEmail_SoValeParaOEnderecoQueOGerou()
{
	var (userId, _) = await UserAsync();
	var destino = Email();
	var outro = Email();

	using var scope = factory.Services.CreateScope();
	var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();
	var token = await tokens.CreateEmailChangeTokenAsync(userId, destino);

	// O endereço novo faz parte do propósito do token: um link interceptado não serve para
	// apontar a conta a outro lugar.
	(await tokens.ChangeEmailAsync(userId, outro, token)).Should().Be(CredentialTokenOutcome.InvalidToken);
	(await tokens.ChangeEmailAsync(userId, destino, token)).Should().Be(CredentialTokenOutcome.Done);
}

[Fact]
public async Task TrocaDeEmail_MudaTambemOUserName()
{
	var (userId, _) = await UserAsync();
	var destino = Email();

	using var scope = factory.Services.CreateScope();
	var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();
	await tokens.ChangeEmailAsync(userId, destino, await tokens.CreateEmailChangeTokenAsync(userId, destino));

	var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
	var user = await context.Users.FindAsync(userId);
	user!.Email.Should().Be(destino);
	// Na plataforma e-mail e username são a mesma coisa (ADR-0022): deixar um para trás faria a
	// pessoa logar com o endereço antigo depois de trocá-lo.
	user.UserName.Should().Be(destino);
	user.NormalizedUserName.Should().Be(destino.ToUpperInvariant());
}

[Fact]
public async Task EmailJaEmUso_NaoEstaDisponivel()
{
	var (_, existente) = await UserAsync();

	using var scope = factory.Services.CreateScope();
	var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();

	(await tokens.IsEmailAvailableAsync(existente)).Should().BeFalse();
	(await tokens.IsEmailAvailableAsync(Email())).Should().BeTrue();
}
```

O arquivo começa assim (o mesmo esqueleto de `LocalLoginToggleTests`, na collection auto-validada):

```csharp
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class EmailChangeTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string ClientId = "troca-email-e2e";
	private const string RedirectUri = "https://localhost/callback";

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"troca-email-{Guid.NewGuid():N}@secco.test";

	private async Task<(Guid UserId, string Email)> UserAsync()
	{
		var email = Email();

		return (await IdentitySeed.UserAsync(factory, _tenantId, email), email);
	}
}
```

- [ ] **Passo 2: rodar e ver falhar** — `--filter FullyQualifiedName~EmailChangeTests`, falha de compilação.

- [ ] **Passo 3: implementar os adaptadores**

Em `IdentityCredentialTokens`:

```csharp
	/// <inheritdoc />
	public async Task<bool> IsEmailAvailableAsync(string email, CancellationToken cancellationToken = default) =>
		await userManager.FindByEmailAsync(email).ConfigureAwait(false) is null;

	/// <inheritdoc />
	public async Task<string> CreateEmailChangeTokenAsync(
		Guid userId,
		string newEmail,
		CancellationToken cancellationToken = default)
	{
		var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false)
			?? throw new InvalidOperationException("Usuário inexistente ao gerar token de troca de e-mail.");

		// O endereço novo entra no PROPÓSITO do token (convenção do Identity), e é isso que
		// impede um link interceptado de apontar a conta para outro destino.
		return await userManager.GenerateChangeEmailTokenAsync(user, newEmail).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<CredentialTokenOutcome> ChangeEmailAsync(
		Guid userId,
		string newEmail,
		string token,
		CancellationToken cancellationToken = default)
	{
		var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

		if (user is null)
		{
			return CredentialTokenOutcome.InvalidToken;
		}

		if (await userManager.FindByEmailAsync(newEmail).ConfigureAwait(false) is not null)
		{
			// Alguém tomou o endereço entre o pedido e a confirmação.
			return CredentialTokenOutcome.NotAllowed;
		}

		var changed = await userManager.ChangeEmailAsync(user, newEmail, token).ConfigureAwait(false);

		if (!changed.Succeeded)
		{
			return CredentialTokenOutcome.InvalidToken;
		}

		// E-mail e username são a mesma coisa na plataforma (ADR-0022); o ChangeEmailAsync só
		// mexe no primeiro.
		var renamed = await userManager.SetUserNameAsync(user, newEmail).ConfigureAwait(false);

		return renamed.Succeeded ? CredentialTokenOutcome.Done : CredentialTokenOutcome.NotAllowed;
	}

	/// <inheritdoc />
	public async Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default) =>
		await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is { } user
			&& await userManager.CheckPasswordAsync(user, password).ConfigureAwait(false);
```

Em `SeccoEmailCredentialMailer`, o caminho da página de confirmação e os dois envios:

```csharp
	/// <summary>Caminho da página que confirma a troca de e-mail.</summary>
	public const string ConfirmEmailPath = "/conta/confirmar-email";

	/// <inheritdoc />
	public Task SendEmailChangeConfirmationAsync(
		string newRecipient,
		Guid userId,
		string newEmail,
		string token,
		CancellationToken cancellationToken = default)
	{
		var link = options.BuildLink(
			ConfirmEmailPath,
			$"userId={userId}&email={Uri.EscapeDataString(newEmail)}&token={Uri.EscapeDataString(token)}");

		return sender.SendAsync(
			newRecipient,
			"Confirme seu novo e-mail",
			$"""
			Pediram para que este endereço passe a ser o acesso de uma conta da plataforma.

			Confirme aqui: {link}

			O link vale por {options.ResetLifetimeMinutes.ToString(CultureInfo.InvariantCulture)} minutos.
			Se não foi você, ignore este e-mail: sem a confirmação, nada muda.
			""",
			cancellationToken);
	}

	/// <inheritdoc />
	public Task SendEmailChangeNoticeAsync(
		string currentRecipient,
		string newEmail,
		CancellationToken cancellationToken = default) =>
		// Sem link, de propósito: um aviso com link vira um segundo alvo de phishing.
		sender.SendAsync(
			currentRecipient,
			"Pediram a troca do e-mail da sua conta",
			$"""
			Pediram para trocar o e-mail desta conta para {Mask(newEmail)}.

			Se foi você, confirme pelo link enviado ao endereço novo. Se não foi, troque sua senha
			agora e fale com o administrador do seu tenant: quem pediu tem acesso à sua sessão.
			""",
			cancellationToken);

	/// <summary>Mascara o destino no aviso: quem não tem a caixa nova não descobre qual é.</summary>
	private static string Mask(string email)
	{
		var at = email.IndexOf('@', StringComparison.Ordinal);

		return at <= 1
			? "***"
			: string.Concat(email.AsSpan(0, 1), "***", email.AsSpan(at));
	}
```

Em `LogStreamCredentialAuditor.ToSlug`, três entradas novas:

```csharp
		CredentialAuditEvent.EmailChangeRequested => "email-troca-solicitada",
		CredentialAuditEvent.EmailChanged => "email-trocado",
		CredentialAuditEvent.ExternalLoginRemoved => "vinculo-externo-removido",
```

- [ ] **Passo 4: rodar e ver passar** — `--filter FullyQualifiedName~EmailChangeTests` verde (3 testes).

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): portas de troca de e-mail (ADR-0033)"
```

---

### Task 3: casos de uso da troca de e-mail

**Arquivos:**
- Criar: `src/SecureGate/Secco.SecureGate.Application/Credentials/RequestEmailChangeHandler.cs`
- Criar: `src/SecureGate/Secco.SecureGate.Application/Credentials/ConfirmEmailChangeHandler.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Application/SecureGateApplicationExtensions.cs`
- Modificar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/EmailChangeTests.cs`

**Interfaces:**
- Consome: as portas da Tarefa 2; `ISessionRevoker.RevokeAllAsync(userId, SessionRevocationReason.PasswordChanged, ct)`; `IPasswordResetThrottle.TryAcquire(email, remoteAddress)`.
- Produz:

```csharp
public sealed class RequestEmailChangeHandler
{
	// Não devolve resultado de negócio por endereço: livre e em uso respondem igual (ADR-0020).
	// Devolve apenas se a SENHA conferiu, porque isso a tela precisa distinguir.
	public Task<bool> HandleAsync(Guid userId, string? newEmail, string? currentPassword, string? remoteAddress, CancellationToken cancellationToken = default);
}

public sealed class ConfirmEmailChangeHandler
{
	public Task<CredentialTokenOutcome> HandleAsync(Guid userId, string newEmail, string token, CancellationToken cancellationToken = default);
}
```

- [ ] **Passo 1: escrever os testes que falham**

```csharp
[Fact]
public async Task Pedido_ComSenhaErrada_NaoEnviaNada()
{
	var (userId, atual) = await UserAsync();
	var destino = Email();

	using var scope = factory.Services.CreateScope();
	var handler = scope.ServiceProvider.GetRequiredService<RequestEmailChangeHandler>();

	(await handler.HandleAsync(userId, destino, "Errada@Senha1", remoteAddress: null)).Should().BeFalse();
	factory.Emails.For(destino).Should().BeEmpty();
	factory.Emails.For(atual).Should().BeEmpty();
}

[Fact]
public async Task Pedido_Valido_MandaLinkAoNovoEAvisoAoAntigo()
{
	var (userId, atual) = await UserAsync();
	var destino = Email();

	using var scope = factory.Services.CreateScope();
	var handler = scope.ServiceProvider.GetRequiredService<RequestEmailChangeHandler>();

	(await handler.HandleAsync(userId, destino, IdentitySeed.Password, remoteAddress: null)).Should().BeTrue();

	factory.Emails.For(destino).Should().ContainSingle()
		.Which.Body.Should().Contain("/conta/confirmar-email?");
	var aviso = factory.Emails.For(atual).Should().ContainSingle().Subject;
	aviso.Body.Should().NotContain("http", "aviso não leva link — seria um segundo alvo de phishing");
	aviso.Body.Should().NotContain(destino, "o endereço novo aparece mascarado");
}

[Fact]
public async Task Pedido_ParaEmailEmUso_RespondeIgualENaoEnvia()
{
	var (userId, _) = await UserAsync();
	var (_, ocupado) = await UserAsync();

	using var scope = factory.Services.CreateScope();
	var handler = scope.ServiceProvider.GetRequiredService<RequestEmailChangeHandler>();

	// true = "a senha conferiu", não "o e-mail estava livre": a tela não pode distinguir os casos.
	(await handler.HandleAsync(userId, ocupado, IdentitySeed.Password, remoteAddress: null)).Should().BeTrue();
	factory.Emails.For(ocupado).Should().BeEmpty();
}

[Fact]
public async Task Confirmacao_TrocaOEmailEEncerraAsSessoes()
{
	var (userId, atual) = await UserAsync();
	var destino = Email();
	var driver = new OidcLoginDriver(factory, ClientId, RedirectUri, IdentitySeed.Password);
	var (_, refreshToken) = await driver.LoginAsync(atual, "openid offline_access logstream");

	using var scope = factory.Services.CreateScope();
	var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();
	var confirm = scope.ServiceProvider.GetRequiredService<ConfirmEmailChangeHandler>();
	var token = await tokens.CreateEmailChangeTokenAsync(userId, destino);

	(await confirm.HandleAsync(userId, destino, token)).Should().Be(CredentialTokenOutcome.Done);

	(await driver.RefreshAsync(refreshToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
	(await new OidcLoginDriver(factory, ClientId, RedirectUri, IdentitySeed.Password)
		.LoginAsync(destino, "openid offline_access logstream")).AccessToken.Should().NotBeNullOrEmpty();
}

[Fact]
public async Task Confirmacao_MataOConvitePendenteDaConta()
{
	var (userId, atual) = await UserAsync();
	var destino = Email();

	using var scope = factory.Services.CreateScope();
	var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();
	var confirm = scope.ServiceProvider.GetRequiredService<ConfirmEmailChangeHandler>();

	var conviteAntigo = await tokens.CreateInviteTokenAsync(userId);
	var token = await tokens.CreateEmailChangeTokenAsync(userId, destino);
	await confirm.HandleAsync(userId, destino, token);

	// A troca move o SecurityStamp, então todo link pendente morre junto — de graça.
	(await tokens.IsLinkValidAsync(userId, conviteAntigo, invite: true)).Should().BeFalse();
}
```

- [ ] **Passo 2: rodar e ver falhar.**

- [ ] **Passo 3: implementar**

`RequestEmailChangeHandler`:

```csharp
/// <summary>
/// Pedido de troca do próprio e-mail (entrega C).
/// </summary>
/// <remarks>
/// Exigir a senha atual é o que quebra a cadeia "cookie roubado → troca o e-mail → esqueci minha
/// senha na caixa nova". Conta só corporativa não tem senha a exigir, e nela o e-mail já não
/// governa o login — o vínculo <c>oid</c> governa (ADR-0026).
/// <para>
/// O retorno diz apenas se a <b>senha conferiu</b>. Endereço livre e endereço em uso produzem o
/// mesmo desfecho visível: a alternativa contaria a um usuário autenticado quais endereços
/// existem na instalação (ADR-0020).
/// </para>
/// </remarks>
public sealed class RequestEmailChangeHandler(
	ICredentialTokens tokens,
	ICredentialMailer mailer,
	ICredentialAuditor auditor,
	IPasswordResetThrottle throttle)
{
	private const int EmailMaxLength = 256;

	public async Task<bool> HandleAsync(
		Guid userId,
		string? newEmail,
		string? currentPassword,
		string? remoteAddress,
		CancellationToken cancellationToken = default)
	{
		var normalized = newEmail?.Trim() ?? string.Empty;
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		if (account is null || !account.CanReceiveCredentialMail)
		{
			return false;
		}

		if (account.HasPassword
			&& !await tokens.CheckPasswordAsync(userId, currentPassword ?? string.Empty, cancellationToken).ConfigureAwait(false))
		{
			return false;
		}

		if (normalized.Length is 0 or > EmailMaxLength
			|| string.Equals(normalized, account.Email, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		// Mesma cota do "esqueci minha senha": os dois mandam link por e-mail (ADR-0035).
		if (!throttle.TryAcquire(account.Email, remoteAddress))
		{
			return true;
		}

		if (!await tokens.IsEmailAvailableAsync(normalized, cancellationToken).ConfigureAwait(false))
		{
			return true;
		}

		var token = await tokens.CreateEmailChangeTokenAsync(userId, normalized, cancellationToken).ConfigureAwait(false);

		await mailer.SendEmailChangeConfirmationAsync(normalized, userId, normalized, token, cancellationToken)
			.ConfigureAwait(false);
		await mailer.SendEmailChangeNoticeAsync(account.Email, normalized, cancellationToken).ConfigureAwait(false);

		await auditor.RecordAsync(
			CredentialAuditEvent.EmailChangeRequested, userId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);

		return true;
	}
}
```

`ConfirmEmailChangeHandler`:

```csharp
/// <summary>
/// Confirma a troca de e-mail pelo link enviado ao endereço novo (entrega C).
/// </summary>
/// <remarks>
/// A troca move o <c>SecurityStamp</c>, então as sessões caem (ADR-0032) e os links pendentes de
/// convite e redefinição morrem junto. A sessão de quem trocou é renovada pela própria página.
/// </remarks>
public sealed class ConfirmEmailChangeHandler(
	ICredentialTokens tokens,
	ICredentialAuditor auditor,
	ISessionRevoker revoker)
{
	public async Task<CredentialTokenOutcome> HandleAsync(
		Guid userId,
		string newEmail,
		string token,
		CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		if (account is null)
		{
			return CredentialTokenOutcome.InvalidToken;
		}

		var outcome = await tokens.ChangeEmailAsync(userId, newEmail, token, cancellationToken).ConfigureAwait(false);

		if (outcome != CredentialTokenOutcome.Done)
		{
			return outcome;
		}

		await revoker.RevokeAllAsync(userId, SessionRevocationReason.PasswordChanged, cancellationToken)
			.ConfigureAwait(false);

		await auditor.RecordAsync(
			CredentialAuditEvent.EmailChanged, userId, account.TenantId, newEmail, cancellationToken)
			.ConfigureAwait(false);

		return outcome;
	}
}
```

Registrar os dois em `AddSecureGateApplication`, ao lado dos handlers de credencial:

```csharp
		services.AddScoped<Credentials.RequestEmailChangeHandler>();
		services.AddScoped<Credentials.ConfirmEmailChangeHandler>();
```

- [ ] **Passo 4: rodar e ver passar** — 8 testes em `EmailChangeTests`.

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): pedido e confirmação da troca de e-mail (ADR-0033)"
```

---

### Task 4: telas da troca de e-mail

**Arquivos:**
- Criar: `src/SecureGate/Secco.SecureGate.Api/Pages/Account/ChangeEmail.cshtml(.cs)`
- Criar: `src/SecureGate/Secco.SecureGate.Api/Pages/Account/ConfirmEmail.cshtml(.cs)`
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Pages/Account/Index.cshtml`
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Program.cs` (a página de confirmação é anônima)
- Modificar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/EmailChangeTests.cs`

**Interfaces:**
- Consome: `RequestEmailChangeHandler`, `ConfirmEmailChangeHandler`, `ICredentialTokens`, `SignInManager<User>`, `UserManager<User>`.

- [ ] **Passo 1: escrever os testes que falham**

```csharp
[Fact]
public async Task TelaDeTroca_SemSessao_RedirecionaParaOLogin()
{
	using var anonimo = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

	var response = await anonimo.GetAsync("/conta/trocar-email");

	response.StatusCode.Should().Be(HttpStatusCode.Redirect);
	response.Headers.Location!.ToString().Should().Contain("/login");
}

[Fact]
public async Task TelaDeTroca_PeloDono_ConfirmaEPermiteLogarComONovo()
{
	var (_, atual) = await UserAsync();
	var destino = Email();
	using var browser = await SignedInBrowserAsync(atual, IdentitySeed.Password);

	var pedido = await SubmitChangeAsync(browser, destino, IdentitySeed.Password);
	pedido.StatusCode.Should().Be(HttpStatusCode.OK);
	(await pedido.Content.ReadAsStringAsync()).Should().Contain("enviamos um link");

	var link = ConfirmLink().Match(factory.Emails.For(destino).Single().Body).Value;
	link.Should().StartWith(SecureGateApiFactory.PublicBaseUrl);

	using var novaJanela = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
	var confirmacao = await novaJanela.GetAsync(new Uri(link).PathAndQuery);
	confirmacao.StatusCode.Should().Be(HttpStatusCode.OK);
	(await confirmacao.Content.ReadAsStringAsync()).Should().Contain("E-mail confirmado");

	(await new OidcLoginDriver(factory, ClientId, RedirectUri, IdentitySeed.Password)
		.LoginAsync(destino, "openid offline_access logstream")).AccessToken.Should().NotBeNullOrEmpty();
}

[Fact]
public async Task TelaDeTroca_SenhaErrada_MostraOErroENaoEnvia()
{
	var (_, atual) = await UserAsync();
	var destino = Email();
	using var browser = await SignedInBrowserAsync(atual, IdentitySeed.Password);

	var response = await SubmitChangeAsync(browser, destino, "Errada@Senha1");

	(await response.Content.ReadAsStringAsync()).Should().Contain("Senha atual incorreta");
	factory.Emails.For(destino).Should().BeEmpty();
}

[Fact]
public async Task TelaDeTroca_EmailEmUso_RespondeExatamenteComoEmailLivre()
{
	var (_, primeiro) = await UserAsync();
	var (_, ocupado) = await UserAsync();
	using var browser = await SignedInBrowserAsync(primeiro, IdentitySeed.Password);

	var livre = await SubmitChangeAsync(browser, Email(), IdentitySeed.Password);
	var emUso = await SubmitChangeAsync(browser, ocupado, IdentitySeed.Password);

	(await emUso.Content.ReadAsStringAsync()).Should().Be(await livre.Content.ReadAsStringAsync());
}

[Fact]
public async Task Confirmacao_LinkJaUsado_RecusaNaAbertura()
{
	var (_, atual) = await UserAsync();
	var destino = Email();
	using var browser = await SignedInBrowserAsync(atual, IdentitySeed.Password);
	await SubmitChangeAsync(browser, destino, IdentitySeed.Password);
	var link = ConfirmLink().Match(factory.Emails.For(destino).Single().Body).Value;

	using var janela = factory.CreateClient();
	await janela.GetAsync(new Uri(link).PathAndQuery);
	var segunda = await janela.GetAsync(new Uri(link).PathAndQuery);

	(await segunda.Content.ReadAsStringAsync()).Should().Contain("não vale mais");
}
```

Helpers do arquivo de teste (copiar de `ChangeOwnPasswordTests`, adaptando):

```csharp
	[GeneratedRegex(@"https://\S+/conta/confirmar-email\?\S+")]
	private static partial Regex ConfirmLink();

	private async Task<HttpClient> SignedInBrowserAsync(string email, string password)
	{
		var browser = factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			HandleCookies = true,
		});

		var loginPage = await browser.GetAsync("/login");
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());
		var login = await browser.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.Email"] = email,
			["Input.Password"] = password,
		}));

		login.StatusCode.Should().Be(HttpStatusCode.Redirect);

		return browser;
	}

	private static async Task<HttpResponseMessage> SubmitChangeAsync(HttpClient browser, string novoEmail, string senha)
	{
		var form = await browser.GetAsync("/conta/trocar-email");
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await form.Content.ReadAsStringAsync());

		return await browser.PostAsync("/conta/trocar-email", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.NewEmail"] = novoEmail,
			["Input.CurrentPassword"] = senha,
		}));
	}
```

A classe passa a ser `public partial class EmailChangeTests` por causa do `[GeneratedRegex]`.

- [ ] **Passo 2: rodar e ver falhar.**

- [ ] **Passo 3: implementar as páginas**

`ChangeEmail.cshtml.cs` — autenticada pelo cookie, no mesmo molde de `ChangePasswordModel`:

```csharp
// O esquema é o cookie do login interativo, não o JwtBearer padrão da API (ADR-0007).
[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class ChangeEmailModel(
	RequestEmailChangeHandler handler,
	ICredentialTokens tokens,
	UserManager<User> userManager) : PageModel
{
	[BindProperty]
	public InputModel Input { get; set; } = new();

	/// <summary>Conta sem senha local não mostra o campo de senha (ADR-0026).</summary>
	public bool RequiresPassword { get; private set; }

	/// <summary>Verdadeiro depois do pedido — a tela passa a mostrar a confirmação genérica.</summary>
	public bool Submitted { get; private set; }

	public string? ErrorMessage { get; private set; }

	public sealed class InputModel
	{
		[Required(ErrorMessage = "Informe o novo e-mail.")]
		[EmailAddress(ErrorMessage = "E-mail inválido.")]
		[StringLength(256, ErrorMessage = "E-mail muito longo.")]
		public string NewEmail { get; set; } = string.Empty;

		// ADR-0020: teto contra amplificação de custo no hashing PBKDF2.
		[StringLength(128, ErrorMessage = "A senha deve ter no máximo 128 caracteres.")]
		[DataType(DataType.Password)]
		public string? CurrentPassword { get; set; }
	}

	public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
	{
		if (await LoadAsync(cancellationToken).ConfigureAwait(false) is not { } account)
		{
			return NotFound();
		}

		RequiresPassword = account.HasPassword;

		return Page();
	}

	public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
	{
		if (await LoadAsync(cancellationToken).ConfigureAwait(false) is not { } account)
		{
			return NotFound();
		}

		RequiresPassword = account.HasPassword;

		if (!ModelState.IsValid)
		{
			return Page();
		}

		var accepted = await handler
			.HandleAsync(account.UserId, Input.NewEmail, Input.CurrentPassword, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken)
			.ConfigureAwait(false);

		if (!accepted)
		{
			ErrorMessage = "Senha atual incorreta.";

			return Page();
		}

		Submitted = true;

		return Page();
	}

	private async Task<CredentialAccount?> LoadAsync(CancellationToken cancellationToken) =>
		userManager.GetUserId(User) is { } id && Guid.TryParse(id, out var userId)
			? await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false)
			: null;
}
```

`ChangeEmail.cshtml` segue o padrão das outras (layout `_AccountLayout`, `<h1>Trocar meu e-mail</h1>`); quando `Submitted`, mostra **sempre a mesma mensagem**:

```html
@if (Model.Submitted)
{
    <p class="sub">Se o endereço estiver disponível, enviamos um link de confirmação para ele.</p>
    <div class="notice">O e-mail atual continua valendo até você confirmar pelo link.</div>
    <p style="margin-top:1rem"><a href="/conta">Voltar</a></p>
}
```

O campo de senha aparece dentro de `@if (Model.RequiresPassword)`.

`ConfirmEmail.cshtml.cs` — anônima (quem confirma pode estar em outro navegador), valida na abertura e confirma no mesmo GET:

```csharp
[AllowAnonymous]
public sealed class ConfirmEmailModel(ConfirmEmailChangeHandler handler) : PageModel
{
	[BindProperty(SupportsGet = true)]
	public Guid UserId { get; set; }

	[BindProperty(SupportsGet = true, Name = "email")]
	public string NewEmail { get; set; } = string.Empty;

	[BindProperty(SupportsGet = true)]
	public string Token { get; set; } = string.Empty;

	public bool Confirmed { get; private set; }

	public async Task OnGetAsync(CancellationToken cancellationToken)
	{
		if (UserId == Guid.Empty || string.IsNullOrWhiteSpace(NewEmail) || string.IsNullOrWhiteSpace(Token))
		{
			return;
		}

		// A confirmação acontece no GET: o link É a ação, e o token só serve para este endereço.
		Confirmed = await handler.HandleAsync(UserId, NewEmail, Token, cancellationToken).ConfigureAwait(false)
			== CredentialTokenOutcome.Done;
	}
}
```

`ConfirmEmail.cshtml` mostra "E-mail confirmado" com link para `/login` quando `Confirmed`, e "Este link não vale mais" com "pedir outro" apontando para `/conta/trocar-email` caso contrário.

Em `Program.cs`, a confirmação entra na lista de páginas anônimas:

```csharp
	options.Conventions.AllowAnonymousToPage("/Account/ConfirmEmail");
```

Em `Pages/Account/Index.cshtml`, o link novo, logo abaixo do de trocar senha:

```html
<p style="margin-top:.5rem"><a href="/conta/trocar-email">Trocar meu e-mail</a></p>
```

- [ ] **Passo 4: rodar e ver passar** — `--filter FullyQualifiedName~EmailChangeTests`, 13 testes.

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): telas de troca e confirmação de e-mail (ADR-0033)"
```

---

### Task 5: desvínculo do Entra ID

**Arquivos:**
- Modificar: `src/SecureGate/Secco.SecureGate.Application/Users/IUserDirectory.cs`
- Criar: `src/SecureGate/Secco.SecureGate.Application/Users/RemoveExternalLoginHandler.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Infrastructure/Users/UserAccountService.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Application/SecureGateErrors.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Application/SecureGateApplicationExtensions.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Endpoints/UserEndpoints.cs`
- Criar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/ExternalLoginUnlinkTests.cs`

**Interfaces:**
- Produz: `IUserDirectory.RemoveExternalLoginAsync(Guid tenantId, Guid userId, string provider, CancellationToken)` → `bool` (achou o usuário no tenant); `SecureGateErrors.Credentials.LastSignInPath`; rota `DELETE /api/v1/tenants/{tenantId}/users/{userId}/external-logins/{provider}` (`RemoveUserExternalLogin`).

- [ ] **Passo 1: escrever os testes que falham**

```csharp
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class ExternalLoginUnlinkTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string AdminClientId = "unlink-admin";
	private const string AdminSecret = "unlink-admin-secret-32-chars-minimo!";
	private const string Provider = "EntraId";

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreateClientAsync(AdminClientId, AdminSecret, "securegate:admin");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task Desvincular_RemoveOVinculoEResponde204()
	{
		var userId = await UserWithLinkAsync();
		using var admin = await AdminAsync();

		var response = await admin.DeleteAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/external-logins/{Provider}");

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await LinksAsync(userId)).Should().BeEmpty();
	}

	[Fact]
	public async Task Desvincular_Repetido_ContinuaRespondendo204()
	{
		var userId = await UserWithLinkAsync();
		using var admin = await AdminAsync();
		var rota = $"/api/v1/tenants/{_tenantId}/users/{userId}/external-logins/{Provider}";

		await admin.DeleteAsync(rota);

		// ADR-0034: DELETE é idempotente de fato — o SDK repete esse método sozinho.
		(await admin.DeleteAsync(rota)).StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task Desvincular_ContaSoCorporativa_Responde409()
	{
		var userId = await UserWithLinkAsync(localLogin: false);
		using var admin = await AdminAsync();

		var response = await admin.DeleteAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/external-logins/{Provider}");

		// O vínculo é o único caminho de entrada: removê-lo criaria uma conta órfã.
		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await LinksAsync(userId)).Should().ContainSingle();
	}

	[Fact]
	public async Task Desvincular_DeOutroTenant_Responde404()
	{
		var userId = await UserWithLinkAsync();
		var outroTenant = await IdentitySeed.TenantAsync(factory);
		using var admin = await AdminAsync();

		var response = await admin.DeleteAsync($"/api/v1/tenants/{outroTenant}/users/{userId}/external-logins/{Provider}");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Desvincular_SemEscopoDeAdmin_Responde403()
	{
		var userId = await UserWithLinkAsync();
		using var semEscopo = factory.CreateClient();

		var response = await semEscopo.DeleteAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/external-logins/{Provider}");

		response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
	}
}
```

Helpers: `UserWithLinkAsync` cria o usuário por `IdentitySeed.UserAsync`, ajusta `LocalLoginEnabled` quando pedido e insere o vínculo direto no banco:

```csharp
	private async Task<Guid> UserWithLinkAsync(bool localLogin = true)
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, $"unlink-{Guid.NewGuid():N}@secco.test");

		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var user = await context.Users.FindAsync(userId);
		user!.LocalLoginEnabled = localLogin;
		context.UserLogins.Add(new UserLogin
		{
			UserId = userId,
			LoginProvider = Provider,
			ProviderKey = $"{Guid.NewGuid():D}:{Guid.NewGuid():D}",
			ProviderDisplayName = "Microsoft Entra ID",
		});
		await context.SaveChangesAsync();

		return userId;
	}

	private async Task<IReadOnlyList<string>> LinksAsync(Guid userId)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await context.UserLogins.Where(l => l.UserId == userId).Select(l => l.LoginProvider).ToListAsync();
	}
```

`AdminAsync()` é o mesmo de `LocalLoginToggleTests` (client credentials com `securegate:admin`).

- [ ] **Passo 2: rodar e ver falhar.**

- [ ] **Passo 3: implementar**

Erro novo em `SecureGateErrors.Credentials`:

```csharp
		/// <summary>
		/// Desvincular deixaria a conta sem caminho de entrada: só corporativa, o vínculo é o
		/// único acesso. Ligar a senha local primeiro dispara convite e resolve.
		/// </summary>
		public static readonly Error LastSignInPath =
			Error.Conflict(
				"SecureGate.Credential.LastSignInPath",
				"Esta conta entra apenas pelo diretório corporativo. Ligue a senha local antes de desvincular.");
```

Porta em `IUserDirectory`:

```csharp
	/// <summary>Remove um login externo do usuário do tenant. Idempotente: sem vínculo, nada acontece.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="provider">Provedor (ex.: <c>EntraId</c>).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se o usuário não existe ou é de outro tenant.</returns>
	Task<bool> RemoveExternalLoginAsync(Guid tenantId, Guid userId, string provider, CancellationToken cancellationToken = default);
```

Adaptador em `UserAccountService`:

```csharp
	public async Task<bool> RemoveExternalLoginAsync(
		Guid tenantId,
		Guid userId,
		string provider,
		CancellationToken cancellationToken = default)
	{
		var user = await context.Users
			.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken).ConfigureAwait(false);

		if (user is null || user.TenantId != tenantId)
		{
			return false;
		}

		var links = await context.UserLogins
			.Where(login => login.UserId == userId && login.LoginProvider == provider)
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		if (links.Count == 0)
		{
			return true;
		}

		context.UserLogins.RemoveRange(links);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return true;
	}
```

`RemoveExternalLoginHandler`:

```csharp
/// <summary>
/// Remove o vínculo de login externo de uma conta (entrega C).
/// </summary>
/// <remarks>
/// <b>Não bloqueia ninguém:</b> enquanto a pessoa seguir no diretório e a federação do tenant
/// estiver ligada, o próximo login casa por e-mail e vincula de novo (ADR-0026). Serve para
/// vínculo morto — conta recriada no diretório, pessoa que saiu de lá. Quem quer barrar acesso
/// desativa a conta ou desliga a federação.
/// <para>
/// A guarda é uma só: conta com login local DESLIGADO não pode perder o vínculo, porque ele é o
/// único caminho de entrada. "Sem senha ainda" não impede — o convite e o "esqueci minha senha"
/// levam a pessoa até a senha (ADR-0033).
/// </para>
/// </remarks>
public sealed class RemoveExternalLoginHandler(
	IUserDirectory directory,
	Credentials.ICredentialTokens tokens,
	Credentials.ICredentialAuditor auditor)
{
	public async Task<Result> HandleAsync(
		Guid tenantId,
		Guid userId,
		string provider,
		CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		if (account is null || account.TenantId != tenantId)
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		if (!account.LocalLoginEnabled)
		{
			return Result.Failure(SecureGateErrors.Credentials.LastSignInPath);
		}

		if (!await directory.RemoveExternalLoginAsync(tenantId, userId, provider, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		await auditor.RecordAsync(
			Credentials.CredentialAuditEvent.ExternalLoginRemoved, userId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
```

Registro em `AddSecureGateApplication`: `services.AddScoped<Users.RemoveExternalLoginHandler>();`

Rota em `UserEndpoints`, junto das outras do grupo:

```csharp
		group.MapDelete("/{userId:guid}/external-logins/{provider}", async (
				Guid tenantId,
				Guid userId,
				string provider,
				RemoveExternalLoginHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, provider, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("RemoveUserExternalLogin")
			.WithSummary("Remove o vínculo com um provedor externo (idempotente). Não bloqueia acesso: a federação revincula no próximo login.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);
```

- [ ] **Passo 4: rodar e ver passar** — `--filter FullyQualifiedName~ExternalLoginUnlinkTests`, 5 testes.

- [ ] **Passo 5: regenerar o contrato e commitar**

```bash
SECCO_UPDATE_OPENAPI=true dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter FullyQualifiedName~OpenApiContractTests
git add -A
git commit -m "feat(securegate): desvincula login externo com guarda de acesso (ADR-0026)"
```

---

### Task 6: AdminPortal — desvincular na página do usuário

**Arquivos:**
- Modificar: `src/AdminPortal/Secco.AdminPortal/Services/IUserAdminService.cs`
- Modificar: `src/AdminPortal/Secco.AdminPortal/Components/Pages/UserManagement.razor`
- Modificar: `tests/AdminPortal/Secco.AdminPortal.Tests/IdentityAdminServicesTests.cs`

**Interfaces:**
- Consome: `RemoveUserExternalLogin` do client gerado (`client.RemoveUserExternalLoginAsync(tenantId, userId, provider, ct)`).
- Produz: `IUserAdminService.RemoveExternalLoginAsync(Guid tenantId, Guid userId, string provider, CancellationToken cancellationToken = default)`.

- [ ] **Passo 1: escrever o teste que falha**

```csharp
	[Fact]
	public async Task DesvincularLoginExterno_ChamaOClient()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();

		await new SecureGateUserAdminService(factory).RemoveExternalLoginAsync(tenantId, userId, "EntraId");

		await client.Received(1).RemoveUserExternalLoginAsync(tenantId, userId, "EntraId", Arg.Any<CancellationToken>());
	}
```

- [ ] **Passo 2: rodar e ver falhar** — `dotnet test tests/AdminPortal/Secco.AdminPortal.Tests/Secco.AdminPortal.Tests.csproj -c Release`.

- [ ] **Passo 3: implementar**

No serviço:

```csharp
	/// <summary>
	/// Remove o vínculo da conta com um provedor externo. Não bloqueia acesso: enquanto a pessoa
	/// seguir no diretório, o próximo login federado vincula de novo (ADR-0026).
	/// </summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="userId">Identificador do usuário.</param>
	/// <param name="provider">Nome do provedor (ex.: <c>EntraId</c>).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RemoveExternalLoginAsync(Guid tenantId, Guid userId, string provider, CancellationToken cancellationToken = default);
```

```csharp
	public async Task RemoveExternalLoginAsync(
		Guid tenantId, Guid userId, string provider, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.RemoveUserExternalLoginAsync(tenantId, userId, provider, cancellationToken).ConfigureAwait(false);
	}
```

Na página, a lista de logins externos deixa de ser texto solto e vira seção própria:

```html
    <section>
        <h2>Login corporativo</h2>
        @if (_user.ExternalLogins.Count == 0)
        {
            <p>Nenhum vínculo com diretório externo.</p>
        }
        else
        {
            <table class="grid">
                <tbody>
                    @foreach (var provider in _user.ExternalLogins)
                    {
                        <tr>
                            <td>@provider</td>
                            <td>
                                <button @onclick="() => AskUnlink(provider)"
                                        disabled="@(_busy || !_user.LocalLoginEnabled)">Desvincular</button>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
            @if (!_user.LocalLoginEnabled)
            {
                <p class="muted">
                    Esta conta entra apenas pelo diretório: o vínculo é o único acesso. Ligue a senha
                    local antes de desvincular.
                </p>
            }
            else
            {
                <p class="muted">
                    Desvincular não bloqueia o acesso — enquanto a pessoa seguir no diretório, o próximo
                    login vincula de novo. Para barrar acesso, desative a conta.
                </p>
            }
        }
    </section>
```

```csharp
    private void AskUnlink(string provider) =>
        _pending = new PendingAction($"Remover o vínculo com {provider}?", async () =>
        {
            await UserAdminService.RemoveExternalLoginAsync(TenantId, UserId, provider);
            await ReloadAsync();
            _message = $"Vínculo com {provider} removido.";
        });
```

O trecho `· login externo: …` que hoje fica no parágrafo de situação, no topo da página, sai — a informação passou a ter seção própria.

- [ ] **Passo 4: rodar e ver passar** — suíte do AdminPortal verde.

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(adminportal): desvincular login corporativo na página do usuário (ADR-0026)"
```

---

### Task 7: mutação, documentação e publicação

**Arquivos:**
- Criar: `<scratchpad>/mutate_conta.py` (fora do repositório)
- Modificar: `CHANGELOG.md`, `docs/roadmap.md`, `docs/design-decisions-log.md`, `src/SecureGate/README.md`

- [ ] **Passo 1: bateria de mutação**

Nos moldes de `mutate_credenciais.py`: uma mutação por vez, restaurar e conferir byte a byte, e **falha de build não conta como detecção** (regex `: error [A-Z]+\d+`).

| # | Mutação | Teste esperado |
|---|---|---|
| M1 | link de confirmação vai para o e-mail **atual** em vez do novo | `TelaDeTroca_PeloDono_ConfirmaEPermiteLogarComONovo` |
| M2 | pedido não confere a senha atual | `TelaDeTroca_SenhaErrada_MostraOErroENaoEnvia` |
| M3 | `ChangeEmailAsync` aceita token emitido para outro endereço | `Token_DeTrocaDeEmail_SoValeParaOEnderecoQueOGerou` |
| M4 | e-mail em uso passa a ser aceito | `Pedido_ParaEmailEmUso_RespondeIgualENaoEnvia` |
| M5 | `UserName` não acompanha o `Email` | `TrocaDeEmail_MudaTambemOUserName` |
| M6 | confirmação não revoga sessões | `Confirmacao_TrocaOEmailEEncerraAsSessoes` |
| M7 | desvincular sem a guarda de conta órfã | `Desvincular_ContaSoCorporativa_Responde409` |
| M8 | desvincular atravessa tenant | `Desvincular_DeOutroTenant_Responde404` |

Mutação sobrevivente = teste faltando; escrever o teste e repetir.

- [ ] **Passo 2: documentação**

- `CHANGELOG.md`, em "Não publicado": `Secco.SecureGate.Client` 0.10.0 — rota nova `RemoveUserExternalLogin`, aditiva.
- `docs/roadmap.md`: entrada da entrega C com as decisões e o limite honesto do desvínculo.
- `docs/design-decisions-log.md`: por que só o dono troca o e-mail, por que a senha atual é exigida, por que a resposta é genérica para e-mail em uso, e por que a guarda olha `LocalLoginEnabled` e não "tem senha".
- `src/SecureGate/README.md`: as duas telas novas na tabela do ciclo de credencial e a rota do desvínculo.

- [ ] **Passo 3: checklist da skill `secco-platform-standards`**, item a item.

- [ ] **Passo 4: verificação final**

```bash
dotnet build Secco.Platform.slnx --configuration Release
dotnet test Secco.Platform.slnx
```

- [ ] **Passo 5: publicação (só com autorização explícita do usuário)**

```bash
python scripts/check-release-chain.py securegate-client/v
```

Taguear no HEAD as dependências apontadas, **uma tag por push**, esperando cada workflow (`gh run list --workflow=publish-packages.yml --limit 5`). Depois, mover o CHANGELOG para "Publicado" e atualizar README e `docs/getting-started.md`.

---

## Auto-revisão

**Cobertura da spec:** provedor de token (T1), portas e adaptadores (T2), casos de uso com senha, limite e resposta genérica (T3), telas de troca e confirmação (T4), desvínculo com guarda e contrato (T5), AdminPortal (T6), mutação/documentação/publicação (T7). As seções "Auditoria" e "Segurança" da spec estão distribuídas por T2, T3 e T5.

**Tipos consistentes:** `CredentialTokenOutcome` é o mesmo da entrega B nas três novas operações; `CredentialAccount` é o tipo devolvido por `tokens.FindAsync` usado em T3 e T5; `RemoveExternalLoginAsync` tem a mesma assinatura na porta (T5) e no serviço do AdminPortal (T6), com o `provider` como `string` nos dois.

**Ponto que o executor deve conferir antes de seguir:** o nome gerado pelo NSwag para a rota nova. O plano assume `RemoveUserExternalLoginAsync(tenantId, userId, provider, ct)`, derivado do `WithName("RemoveUserExternalLogin")` — se o gerador produzir outra assinatura, vale a do gerador, e só a chamada em T6 muda.
