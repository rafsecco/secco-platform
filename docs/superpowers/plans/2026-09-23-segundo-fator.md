# Segundo fator no login local (entrega D) — plano de implementação

> **Para quem executa:** SUB-SKILL OBRIGATÓRIA: use `superpowers:subagent-driven-development` (recomendado) ou `superpowers:executing-plans` para implementar tarefa a tarefa. Os passos usam caixas (`- [ ]`) para acompanhamento.

**Objetivo:** dar ao login por senha um segundo fator TOTP — voluntário para qualquer pessoa, obrigatório para quem opera a instalação — sem criar um caminho que tranque alguém para fora sem volta.

**Arquitetura:** tudo sobre o que o ASP.NET Identity já traz — `fl_two_factor_enabled` em `tb_users`, chave do autenticador e códigos de recuperação em `tb_user_tokens`, `AuthenticatorTokenProvider` já registrado pelo `AddDefaultTokenProviders`. **Zero migrations.** O login vira dois passos pelo cookie de duas etapas do próprio Identity, e a exigência do operador é aplicada no `/connect/authorize`, antes de qualquer código de autorização ser emitido — por isso o AdminPortal não muda.

**Stack:** .NET 10, ASP.NET Identity, OpenIddict 7.5, Razor Pages, QRCoder (novo), xUnit + FluentAssertions 7 + NSubstitute + Testcontainers.

**Spec:** [`docs/superpowers/specs/2026-09-23-segundo-fator-design.md`](../specs/2026-09-23-segundo-fator-design.md)
**ADRs:** ADR-0022 (login), ADR-0033 (ciclo de credencial), ADR-0032 (revogação), ADR-0026 (federação), ADR-0030 (operador), ADR-0034 (idempotência), ADR-0020 (segurança).

## Restrições globais

- **Estilo:** CRLF e **tabs**; XML docs em português no que é público; `warnings = erros` em `src/`.
- **Camadas (ADR-0002):** porta em `Application`, adaptador em `Infrastructure`, composição e telas na `Api`. A `Application` não conhece `Microsoft.AspNetCore.*` nem EF Core.
- **Erros (ADR-0004):** `Result<T>`/`Error`; nunca exceção para fluxo.
- **Segredo TOTP nunca sai do servidor:** o QR é gerado localmente e embutido como `data:` URI. Nada de gerador externo (ADR-0020).
- **Nenhum segredo em log**: chave do autenticador, código digitado e códigos de recuperação jamais aparecem em log, mensagem de erro ou nome de teste.
- **Sem migration.** Se surgir necessidade de coluna, pare e reavalie — a spec assume que não há.
- **Idempotência (ADR-0034):** o reset responde igual em conta com e sem 2FA, e tem teste de repetição.
- **Testes (ADR-0012):** cada tarefa entrega teste no mesmo commit. Suíte: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release`.
- **Commits:** Conventional Commits, direto na `main`, sem branch nem PR. **Nunca `git push`** — publicação é a Tarefa 7, e só com autorização.
- **CRLF:** nunca `sed -i` em arquivo versionado; conferir `git diff --stat` depois de edição em massa.

## Estrutura de arquivos

**Application** (`src/SecureGate/Secco.SecureGate.Application/Credentials/`): `ITwoFactorSetup.cs` (porta), `EnableTwoFactorHandler.cs`, `DisableTwoFactorHandler.cs`, `ResetTwoFactorHandler.cs`; eventos novos em `ICredentialAuditor.cs`; um e-mail novo em `ICredentialMailer.cs`.

**Infrastructure** (`.../Credentials/`): `IdentityTwoFactorSetup.cs` (chave, QR, confirmação, códigos), `SeccoEmailCredentialMailer.cs` (aviso de 2FA).

**Api**: `Extensions/SecureGateIdentityExtensions.cs` (cookie de duas etapas), `Pages/Account/TwoFactor.cshtml(.cs)` (cadastro), `Pages/TwoFactorLogin.cshtml(.cs)` (segundo passo do login), `Pages/Login.cshtml.cs` (desvia quando exige 2FA), `Endpoints/InteractiveEndpoints.cs` (exigência do operador), `Endpoints/UserEndpoints.cs` (reset), `Identity/OperatorTwoFactorPolicy.cs` (regra do operador), `Pages/Account/Index.cshtml` (link).

**Testes**: `Integration/TwoFactorEnrollmentTests.cs`, `Integration/TwoFactorLoginTests.cs`, `Integration/TwoFactorOperatorTests.cs`, `Integration/TwoFactorResetTests.cs`, `Unit/OperatorTwoFactorPolicyTests.cs`.

---

### Task 1: cookie de duas etapas e a porta do 2FA

**Arquivos:**
- Modificar: `Directory.Packages.props`, `src/SecureGate/Secco.SecureGate.Infrastructure/Secco.SecureGate.Infrastructure.csproj`
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Extensions/SecureGateIdentityExtensions.cs`
- Criar: `src/SecureGate/Secco.SecureGate.Application/Credentials/ITwoFactorSetup.cs`
- Criar: `src/SecureGate/Secco.SecureGate.Infrastructure/Credentials/IdentityTwoFactorSetup.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Infrastructure/SecureGateInfrastructureExtensions.cs`
- Criar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/TwoFactorEnrollmentTests.cs`

**Interfaces:**
- Produz:

```csharp
/// <summary>Estado do segundo fator de uma conta.</summary>
/// <param name="Enabled">2FA ligado.</param>
/// <param name="HasAuthenticator">Já existe chave cadastrada.</param>
/// <param name="RecoveryCodesLeft">Códigos de recuperação ainda válidos.</param>
public sealed record TwoFactorState(bool Enabled, bool HasAuthenticator, int RecoveryCodesLeft);

/// <summary>Chave nova, pronta para exibição.</summary>
/// <param name="FormattedKey">Chave em blocos de 4, para digitação manual.</param>
/// <param name="QrCodeDataUri">QR em `data:image/png;base64,...`, gerado no servidor.</param>
public sealed record TwoFactorEnrollment(string FormattedKey, string QrCodeDataUri);

public interface ITwoFactorSetup
{
	Task<TwoFactorState?> GetStateAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<TwoFactorEnrollment?> StartEnrollmentAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<bool> ConfirmAsync(Guid userId, string code, CancellationToken cancellationToken = default);
	Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken = default);
	Task DisableAsync(Guid userId, CancellationToken cancellationToken = default);
}
```

- [ ] **Passo 1: escrever o teste que falha**

```csharp
// tests/SecureGate/Secco.SecureGate.Tests/Integration/TwoFactorEnrollmentTests.cs
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class TwoFactorEnrollmentTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"dois-fatores-{Guid.NewGuid():N}@secco.test";

	private async Task<Guid> UserAsync() => await IdentitySeed.UserAsync(factory, _tenantId, Email());

	[Fact]
	public async Task Estado_ContaNova_NaoTem2FA()
	{
		var userId = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();

		var state = await setup.GetStateAsync(userId);

		state!.Enabled.Should().BeFalse();
		state.HasAuthenticator.Should().BeFalse();
	}

	[Fact]
	public async Task Cadastro_GeraChaveEQrNoProprioServidor()
	{
		var userId = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();

		var enrollment = await setup.StartEnrollmentAsync(userId);

		// O QR é embutido: um gerador externo receberia o segredo TOTP do usuário (ADR-0020).
		enrollment!.QrCodeDataUri.Should().StartWith("data:image/png;base64,");
		enrollment.FormattedKey.Should().MatchRegex("^[a-z0-9 ]+$");
	}

	[Fact]
	public async Task Cadastro_SoLigaDepoisDeConfirmarComCodigoValido()
	{
		var userId = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();
		await setup.StartEnrollmentAsync(userId);

		// Sem a confirmação, um autenticador mal configurado trancaria a conta no login seguinte.
		(await setup.ConfirmAsync(userId, "000000")).Should().BeFalse();
		(await setup.GetStateAsync(userId))!.Enabled.Should().BeFalse();

		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
		var user = await userManager.FindByIdAsync(userId.ToString());
		var codigo = TotpCalculator.Compute(await userManager.GetAuthenticatorKeyAsync(user!));

		(await setup.ConfirmAsync(userId, codigo)).Should().BeTrue();
		(await setup.GetStateAsync(userId))!.Enabled.Should().BeTrue();
	}

	[Fact]
	public async Task CodigosDeRecuperacao_SaoDezENaoSeRepetem()
	{
		var userId = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();

		var codigos = await setup.GenerateRecoveryCodesAsync(userId);

		codigos.Should().HaveCount(10).And.OnlyHaveUniqueItems();
		(await setup.GetStateAsync(userId))!.RecoveryCodesLeft.Should().Be(10);
	}
}
```

- [ ] **Passo 2: rodar e ver falhar**

`dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter FullyQualifiedName~TwoFactorEnrollmentTests` → não compila (`ITwoFactorSetup` inexistente).

- [ ] **Passo 3: dependência do QR e o cookie de duas etapas**

Em `Directory.Packages.props`, junto das demais dependências do SecureGate:

```xml
    <!-- QR do cadastro de 2FA gerado NO SERVIDOR: um gerador externo receberia o segredo
         TOTP do usuário (ADR-0020). QRCoder é MIT, sem dependência nativa. -->
    <PackageVersion Include="QRCoder" Version="1.6.0" />
```

`PackageReference Include="QRCoder"` na Infrastructure.

Em `SecureGateIdentityExtensions`, ao lado dos outros dois cookies:

```csharp
			// Estado ENTRE os dois passos do login (ADR-0022 + entrega D). Não autentica nada
			// sozinho: só diz "esta pessoa passou pela senha e falta o segundo fator".
			.AddCookie(IdentityConstants.TwoFactorUserIdScheme, options =>
			{
				options.Cookie.Name = "secco.securegate.2fa";
				options.Cookie.SameSite = SameSiteMode.Lax;
				options.Cookie.HttpOnly = true;
				options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
				options.Cookie.SecurePolicy = environment.IsProduction()
					? CookieSecurePolicy.Always
					: CookieSecurePolicy.SameAsRequest;
			})
```

- [ ] **Passo 4: implementar o adaptador**

`IdentityTwoFactorSetup` usa `UserManager<User>`:

```csharp
internal sealed class IdentityTwoFactorSetup(UserManager<User> userManager, CredentialOptions options) : ITwoFactorSetup
{
	private const int RecoveryCodeCount = 10;

	public async Task<TwoFactorState?> GetStateAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is not { } user)
		{
			return null;
		}

		return new TwoFactorState(
			user.TwoFactorEnabled,
			await userManager.GetAuthenticatorKeyAsync(user).ConfigureAwait(false) is not null,
			await userManager.CountRecoveryCodesAsync(user).ConfigureAwait(false));
	}

	public async Task<TwoFactorEnrollment?> StartEnrollmentAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is not { } user)
		{
			return null;
		}

		// Chave nova a cada início: recomeçar o cadastro invalida o QR anterior.
		await userManager.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false);
		var key = await userManager.GetAuthenticatorKeyAsync(user).ConfigureAwait(false)!;

		return new TwoFactorEnrollment(FormatKey(key!), BuildQrCode(user.Email!, key!));
	}

	public async Task<bool> ConfirmAsync(Guid userId, string code, CancellationToken cancellationToken = default)
	{
		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is not { } user)
		{
			return false;
		}

		var valid = await userManager.VerifyTwoFactorTokenAsync(
			user, TokenOptions.DefaultAuthenticatorProvider, (code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal))
			.ConfigureAwait(false);

		if (!valid)
		{
			return false;
		}

		return (await userManager.SetTwoFactorEnabledAsync(user, true).ConfigureAwait(false)).Succeeded;
	}

	public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is not { } user)
		{
			return [];
		}

		// O Identity guarda só o hash; estes valores existem apenas nesta resposta.
		var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount).ConfigureAwait(false);

		return [.. codes ?? []];
	}

	public async Task DisableAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is not { } user)
		{
			return;
		}

		await userManager.SetTwoFactorEnabledAsync(user, false).ConfigureAwait(false);
		// Apagar a chave é o que faz o reset ZERAR o cadastro em vez de isentar (spec da entrega D).
		await userManager.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false);
	}

	/// <summary>Chave em blocos de 4, minúscula — o formato que se digita sem errar.</summary>
	private static string FormatKey(string key)
	{
		var lower = key.ToLowerInvariant();
		var blocks = Enumerable.Range(0, (lower.Length + 3) / 4)
			.Select(i => lower.Substring(i * 4, Math.Min(4, lower.Length - (i * 4))));

		return string.Join(' ', blocks);
	}

	/// <summary>QR gerado aqui dentro: o segredo não passa por serviço de terceiro (ADR-0020).</summary>
	private string BuildQrCode(string email, string key)
	{
		var issuer = Uri.EscapeDataString(options.TwoFactorIssuer);
		var uri = $"otpauth://totp/{issuer}:{Uri.EscapeDataString(email)}?secret={key}&issuer={issuer}&digits=6";

		using var generator = new QRCodeGenerator();
		using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
		using var png = new PngByteQRCode(data);

		return "data:image/png;base64," + Convert.ToBase64String(png.GetGraphic(6));
	}
}
```

Em `CredentialOptions`, a propriedade nova:

```csharp
	/// <summary>
	/// Nome que aparece no aplicativo autenticador da pessoa. Padrão "Secco SecureGate"; uma
	/// instalação com marca própria troca aqui.
	/// </summary>
	public string TwoFactorIssuer { get; set; } = "Secco SecureGate";
```

Registrar em `AddSecureGateInfrastructure`:

```csharp
		services.AddScoped<Application.Credentials.ITwoFactorSetup, Credentials.IdentityTwoFactorSetup>();
```

- [ ] **Passo 5: rodar e ver passar** — 4 testes verdes no filtro acima.

- [ ] **Passo 6: commit**

```bash
git add Directory.Packages.props src/SecureGate tests/SecureGate
git commit -m "feat(securegate): porta de cadastro do segundo fator, com QR gerado no servidor"
```

---

### Task 2: casos de uso de ligar, desligar e os avisos

**Arquivos:**
- Criar: `src/SecureGate/Secco.SecureGate.Application/Credentials/EnableTwoFactorHandler.cs`, `DisableTwoFactorHandler.cs`
- Modificar: `.../Credentials/ICredentialAuditor.cs`, `.../Credentials/ICredentialMailer.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Infrastructure/Credentials/{SeccoEmailCredentialMailer,LogStreamCredentialAuditor}.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Application/SecureGateApplicationExtensions.cs`
- Modificar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/TwoFactorEnrollmentTests.cs`

**Interfaces:**
- Consome: `ITwoFactorSetup` (Tarefa 1), `ISessionRevoker`, `ICredentialAuditor`, `ICredentialMailer`.
- Produz:

```csharp
public sealed class EnableTwoFactorHandler
{
	/// <returns>Os 10 códigos de recuperação, ou vazio quando o código não confere.</returns>
	public Task<IReadOnlyList<string>> HandleAsync(Guid userId, string code, CancellationToken cancellationToken = default);
}

public sealed class DisableTwoFactorHandler
{
	public Task<Result> HandleAsync(Guid userId, CancellationToken cancellationToken = default);
}

// CredentialAuditEvent — quatro valores novos
TwoFactorEnabled, TwoFactorDisabled, TwoFactorReset, TwoFactorRecoveryCodeUsed

// ICredentialMailer
Task SendTwoFactorChangedNoticeAsync(string recipient, bool enabled, CancellationToken cancellationToken = default);
```

- [ ] **Passo 1: escrever os testes que falham**

```csharp
[Fact]
public async Task Ligar_ComCodigoValido_DevolveCodigosEEncerraAsOutrasSessoes()
{
	var email = Email();
	var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
	var driver = new OidcLoginDriver(factory, ClientId, RedirectUri, IdentitySeed.Password);
	var (_, refreshToken) = await driver.LoginAsync(email, "openid offline_access logstream");

	using var scope = factory.Services.CreateScope();
	var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();
	var handler = scope.ServiceProvider.GetRequiredService<EnableTwoFactorHandler>();
	await setup.StartEnrollmentAsync(userId);

	var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
	var user = await userManager.FindByIdAsync(userId.ToString());
	var codigo = TotpCalculator.Compute(await userManager.GetAuthenticatorKeyAsync(user!));

	var codigos = await handler.HandleAsync(userId, codigo);

	codigos.Should().HaveCount(10);
	(await driver.RefreshAsync(refreshToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
	factory.Emails.For(email).Should().ContainSingle()
		.Which.Body.Should().NotContainAny(codigos);
}

[Fact]
public async Task Ligar_ComCodigoErrado_NaoLigaENaoAvisa()
{
	var email = Email();
	var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);

	using var scope = factory.Services.CreateScope();
	var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();
	var handler = scope.ServiceProvider.GetRequiredService<EnableTwoFactorHandler>();
	await setup.StartEnrollmentAsync(userId);

	(await handler.HandleAsync(userId, "000000")).Should().BeEmpty();
	(await setup.GetStateAsync(userId))!.Enabled.Should().BeFalse();
	factory.Emails.For(email).Should().BeEmpty();
}

[Fact]
public async Task Desligar_ZeraOCadastroEAvisa()
{
	var email = Email();
	var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);

	using var scope = factory.Services.CreateScope();
	var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();
	var enable = scope.ServiceProvider.GetRequiredService<EnableTwoFactorHandler>();
	var disable = scope.ServiceProvider.GetRequiredService<DisableTwoFactorHandler>();
	await setup.StartEnrollmentAsync(userId);

	var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
	var user = await userManager.FindByIdAsync(userId.ToString());
	await enable.HandleAsync(userId, TotpCalculator.Compute(await userManager.GetAuthenticatorKeyAsync(user!)));

	(await disable.HandleAsync(userId)).IsSuccess.Should().BeTrue();

	var state = await setup.GetStateAsync(userId);
	state!.Enabled.Should().BeFalse();
	// Desligar zera: religar exige cadastrar de novo, não reaproveita a chave antiga.
	state.HasAuthenticator.Should().BeFalse();
	factory.Emails.For(email).Should().HaveCount(2);
}
```

A classe de teste ganha `ClientId`/`RedirectUri` e o `CreatePublicClientAsync` no `InitializeAsync`, como em `EmailChangeTests`.

- [ ] **Passo 2: rodar e ver falhar.**

- [ ] **Passo 3: implementar**

`EnableTwoFactorHandler`:

```csharp
/// <summary>
/// Liga o segundo fator depois de confirmar que o autenticador da pessoa está correto (entrega D).
/// </summary>
/// <remarks>
/// A confirmação não é cerimônia: ligar o 2FA com um autenticador mal configurado tranca a conta
/// no login seguinte, e o caminho de volta passaria por um admin.
/// <para>
/// Como todo evento de credencial, ligar encerra as outras sessões (ADR-0032) e avisa o dono. Os
/// códigos de recuperação voltam nesta resposta e em nenhum outro lugar: no banco há só o hash.
/// </para>
/// </remarks>
public sealed class EnableTwoFactorHandler(
	ITwoFactorSetup setup,
	ICredentialTokens tokens,
	ICredentialMailer mailer,
	ICredentialAuditor auditor,
	ISessionRevoker revoker)
{
	public async Task<IReadOnlyList<string>> HandleAsync(
		Guid userId,
		string code,
		CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		if (account is null || !await setup.ConfirmAsync(userId, code, cancellationToken).ConfigureAwait(false))
		{
			return [];
		}

		var codes = await setup.GenerateRecoveryCodesAsync(userId, cancellationToken).ConfigureAwait(false);

		await revoker.RevokeAllAsync(userId, SessionRevocationReason.PasswordChanged, cancellationToken)
			.ConfigureAwait(false);
		await mailer.SendTwoFactorChangedNoticeAsync(account.Email, enabled: true, cancellationToken)
			.ConfigureAwait(false);
		await auditor.RecordAsync(
			CredentialAuditEvent.TwoFactorEnabled, userId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);

		return codes;
	}
}
```

`DisableTwoFactorHandler` é o espelho: `setup.DisableAsync`, revogação, aviso com `enabled: false` e `CredentialAuditEvent.TwoFactorDisabled`; devolve `Result.Failure(SecureGateErrors.Users.NotFound)` quando a conta não existe.

O e-mail, em `SeccoEmailCredentialMailer`:

```csharp
	/// <inheritdoc />
	public Task SendTwoFactorChangedNoticeAsync(
		string recipient,
		bool enabled,
		CancellationToken cancellationToken = default) =>
		sender.SendAsync(
			recipient,
			enabled ? "Segundo fator ativado" : "Segundo fator desativado",
			enabled
				? """
				O segundo fator desta conta foi ativado, e as sessões abertas foram encerradas.

				Guarde os códigos de recuperação exibidos na tela: sem eles e sem o aplicativo
				autenticador, só um administrador consegue devolver o acesso.
				"""
				: """
				O segundo fator desta conta foi desativado, e as sessões abertas foram encerradas.

				Se não foi você, troque sua senha agora e fale com o administrador do seu tenant.
				""",
			cancellationToken);
```

Quatro entradas novas em `LogStreamCredentialAuditor.ToSlug`: `"2fa-ligado"`, `"2fa-desligado"`, `"2fa-resetado"`, `"2fa-codigo-recuperacao-usado"`.

Registro dos dois handlers em `AddSecureGateApplication`.

- [ ] **Passo 4: rodar e ver passar.**

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): ligar e desligar o segundo fator, com aviso e revogação (entrega D)"
```

---

### Task 3: login em dois passos

**Arquivos:**
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Pages/Login.cshtml.cs`
- Criar: `src/SecureGate/Secco.SecureGate.Api/Pages/TwoFactorLogin.cshtml(.cs)`
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Program.cs` (a página é anônima)
- Criar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/TwoFactorLoginTests.cs`

**Interfaces:**
- Consome: `SignInManager<User>.PasswordSignInAsync` (devolve `RequiresTwoFactor`), `TwoFactorAuthenticatorSignInAsync`, `TwoFactorRecoveryCodeSignInAsync`; `ITwoFactorSetup`; `ICredentialAuditor`.
- Produz: rota `/login/dois-fatores` com `returnUrl`.

- [ ] **Passo 1: escrever os testes que falham**

```csharp
[Fact]
public async Task Login_ComSenhaCerta_PedeOSegundoFator()
{
	var (email, _) = await UsuarioCom2FaAsync();
	using var browser = Browser();

	var senha = await SubmitLoginAsync(browser, email, IdentitySeed.Password);

	senha.StatusCode.Should().Be(HttpStatusCode.Redirect);
	senha.Headers.Location!.ToString().Should().StartWith("/login/dois-fatores");
}

[Fact]
public async Task Login_ComDigitoValido_Entra()
{
	var (email, chave) = await UsuarioCom2FaAsync();
	using var browser = Browser();
	await SubmitLoginAsync(browser, email, IdentitySeed.Password);

	var resposta = await SubmitDigitoAsync(browser, Totp(chave));

	resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
	resposta.Headers.Location!.ToString().Should().Be("/conta");
	(await browser.GetAsync("/conta")).StatusCode.Should().Be(HttpStatusCode.OK);
}

[Fact]
public async Task Login_ComDigitoErrado_NaoEntra()
{
	var (email, _) = await UsuarioCom2FaAsync();
	using var browser = Browser();
	await SubmitLoginAsync(browser, email, IdentitySeed.Password);

	var resposta = await SubmitDigitoAsync(browser, "000000");

	resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	(await resposta.Content.ReadAsStringAsync()).Should().Contain("inv");
	(await browser.GetAsync("/conta")).StatusCode.Should().Be(HttpStatusCode.Redirect);
}

[Fact]
public async Task Login_ComCodigoDeRecuperacao_EntraEOCodigoNaoServeDeNovo()
{
	var (email, _, codigos) = await UsuarioCom2FaECodigosAsync();
	using var primeira = Browser();
	await SubmitLoginAsync(primeira, email, IdentitySeed.Password);

	(await SubmitCodigoRecuperacaoAsync(primeira, codigos[0])).StatusCode.Should().Be(HttpStatusCode.Redirect);

	using var segunda = Browser();
	await SubmitLoginAsync(segunda, email, IdentitySeed.Password);

	// Cada código vale uma vez: reusar é o mesmo que não ter código.
	(await SubmitCodigoRecuperacaoAsync(segunda, codigos[0])).StatusCode.Should().Be(HttpStatusCode.OK);
}

[Fact]
public async Task SegundoPasso_SemTerPassadoPelaSenha_NaoAbre()
{
	using var browser = Browser();

	var resposta = await browser.GetAsync("/login/dois-fatores");

	// O cookie de duas etapas não autentica sozinho; sem ele, não há segundo passo.
	resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
	resposta.Headers.Location!.ToString().Should().Contain("/login");
}
```

O helper é `TotpCalculator.Compute(chaveBase32)`, em `tests/SecureGate/Secco.SecureGate.Tests/Integration/TotpCalculator.cs`, criado na Tarefa 1.

> **Correção do plano, achada na Tarefa 1:** a versão original mandava obter o código com
> `userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider)`. Isso
> **não funciona**: por design, o `AuthenticatorTokenProvider` do Identity devolve string vazia ao
> gerar — ele existe só para validar, porque o dígito nasce no aplicativo da pessoa. O cálculo no
> teste (RFC 6238, o mesmo que a validação do Identity usa) é o único jeito de simular o
> autenticador.

- [ ] **Passo 2: rodar e ver falhar.**

- [ ] **Passo 3: implementar**

Em `LoginModel.OnPostAsync`, entre o `PasswordSignInAsync` e o sucesso:

```csharp
		if (result.RequiresTwoFactor)
		{
			// O SignInManager já gravou o cookie de duas etapas; ele não autentica nada sozinho.
			return RedirectToPage("/TwoFactorLogin", new { returnUrl });
		}
```

`TwoFactorLoginModel` (`@page "/login/dois-fatores"`, `[AllowAnonymous]`):

```csharp
	public async Task<IActionResult> OnGetAsync()
	{
		// Sem o cookie de duas etapas não existe segundo passo: volta ao início.
		return await signInManager.GetTwoFactorAuthenticationUserAsync().ConfigureAwait(false) is null
			? RedirectToPage("/Login")
			: Page();
	}

	public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
	{
		var user = await signInManager.GetTwoFactorAuthenticationUserAsync().ConfigureAwait(false);

		if (user is null)
		{
			return RedirectToPage("/Login");
		}

		if (!ModelState.IsValid)
		{
			return Page();
		}

		var code = Input.Code.Replace(" ", string.Empty, StringComparison.Ordinal);

		// lockoutOnFailure: força bruta no dígito é o ataque óbvio contra TOTP (ADR-0020).
		var result = Input.IsRecoveryCode
			? await signInManager.TwoFactorRecoveryCodeSignInAsync(code).ConfigureAwait(false)
			: await signInManager.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: false)
				.ConfigureAwait(false);

		if (!result.Succeeded)
		{
			ErrorMessage = result.IsLockedOut
				? "Conta temporariamente bloqueada por excesso de tentativas. Tente novamente em alguns minutos."
				: "Código inválido.";

			return Page();
		}

		if (Input.IsRecoveryCode)
		{
			await auditor.RecordAsync(
				CredentialAuditEvent.TwoFactorRecoveryCodeUsed, user.Id, user.TenantId, user.Email, cancellationToken)
				.ConfigureAwait(false);
		}

		return LocalRedirect(ReturnUrl ?? "/conta");
	}
```

A tela tem um campo de código, uma caixa "é um código de recuperação" e, quando restam poucos, o aviso de quantos sobraram (lido de `ITwoFactorSetup.GetStateAsync`).

Em `Program.cs`: `options.Conventions.AllowAnonymousToPage("/TwoFactorLogin");`

- [ ] **Passo 4: rodar e ver passar.**

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): login em dois passos com dígito ou código de recuperação (entrega D)"
```

---

### Task 4: tela de cadastro na conta

**Arquivos:**
- Criar: `src/SecureGate/Secco.SecureGate.Api/Pages/Account/TwoFactor.cshtml(.cs)`
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Pages/Account/Index.cshtml`
- Modificar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/TwoFactorEnrollmentTests.cs`

**Interfaces:**
- Consome: `ITwoFactorSetup`, `EnableTwoFactorHandler`, `DisableTwoFactorHandler`, `OperatorTwoFactorPolicy` (Tarefa 5, só para esconder o botão de desligar — a guarda de verdade é da Tarefa 5).

- [ ] **Passo 1: escrever os testes que falham**

```csharp
[Fact]
public async Task Tela_SemSessao_RedirecionaParaOLogin()
{
	using var anonimo = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

	var resposta = await anonimo.GetAsync("/conta/dois-fatores");

	resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
	resposta.Headers.Location!.ToString().Should().Contain("/login");
}

[Fact]
public async Task Tela_ContaSemSenhaLocal_Responde404()
{
	var email = Email();
	var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
	using var browser = await SignedInBrowserAsync(email, IdentitySeed.Password);
	await DesligarLoginLocalAsync(userId);

	// Conta de diretório não tem senha a proteger com segundo fator: o MFA é do Entra (ADR-0026).
	(await browser.GetAsync("/conta/dois-fatores")).StatusCode.Should().Be(HttpStatusCode.NotFound);
}

[Fact]
public async Task Tela_CadastroCompleto_MostraOsCodigosUmaVez()
{
	var email = Email();
	var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
	using var browser = await SignedInBrowserAsync(email, IdentitySeed.Password);

	await browser.GetAsync("/conta/dois-fatores");
	var confirmacao = await SubmitConfirmAsync(browser, await CodigoAtualAsync(userId));

	var html = await confirmacao.Content.ReadAsStringAsync();
	html.Should().Contain("uma única vez");

	// Recarregar a página não mostra os códigos de novo.
	(await (await browser.GetAsync("/conta/dois-fatores")).Content.ReadAsStringAsync())
		.Should().NotContain("uma única vez");
}
```

- [ ] **Passo 2: rodar e ver falhar.**

- [ ] **Passo 3: implementar**

`TwoFactorModel` é autenticada pelo cookie (`[Authorize(AuthenticationSchemes = "Identity.Application")]`) e responde `NotFound()` quando a conta não usa senha local — mesma regra da tela de trocar senha. `OnGet` chama `GetStateAsync`; se não há 2FA, chama `StartEnrollmentAsync` e mostra QR e chave. `OnPostConfirmAsync` chama `EnableTwoFactorHandler` e, com sucesso, guarda os códigos numa propriedade para render **naquela resposta** (nunca em `TempData` nem em sessão). `OnPostDisableAsync` chama `DisableTwoFactorHandler` e depois `signInManager.RefreshSignInAsync`.

Em `Pages/Account/Index.cshtml`, o link novo junto dos outros, com o estado atual:

```html
<p><a href="/conta/dois-fatores">Segundo fator (@(Model.TwoFactorEnabled ? "ativado" : "desativado"))</a></p>
```

`IndexModel` ganha `TwoFactorEnabled`, lido de `ITwoFactorSetup.GetStateAsync`.

- [ ] **Passo 4: rodar e ver passar.**

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): tela de cadastro do segundo fator na conta (entrega D)"
```

---

### Task 5: exigência para o operador de instalação

**Arquivos:**
- Criar: `src/SecureGate/Secco.SecureGate.Api/Identity/OperatorTwoFactorPolicy.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Endpoints/InteractiveEndpoints.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Pages/Account/TwoFactor.cshtml(.cs)`
- Criar: `tests/SecureGate/Secco.SecureGate.Tests/Unit/OperatorTwoFactorPolicyTests.cs`, `Integration/TwoFactorOperatorTests.cs`

**Interfaces:**
- Consome: `InstallationOperatorPolicy.IsInstallationOperator(user, roles)` (já existe).
- Produz:

```csharp
public static class OperatorTwoFactorPolicy
{
	/// <summary>Caminho do cadastro, para onde o operador sem 2FA é enviado.</summary>
	public const string EnrollmentPath = "/conta/dois-fatores";

	/// <summary>Indica se a conta precisa cadastrar o segundo fator antes de receber token.</summary>
	public static bool RequiresEnrollment(User user, IReadOnlyList<string> roles, bool twoFactorEnabled) =>
		InstallationOperatorPolicy.IsInstallationOperator(user, roles) && !twoFactorEnabled;

	/// <summary>Operador não desliga o próprio segundo fator.</summary>
	public static bool CanDisable(User user, IReadOnlyList<string> roles) =>
		!InstallationOperatorPolicy.IsInstallationOperator(user, roles);
}
```

- [ ] **Passo 1: escrever os testes que falham**

```csharp
// tests/SecureGate/Secco.SecureGate.Tests/Unit/OperatorTwoFactorPolicyTests.cs
public class OperatorTwoFactorPolicyTests
{
	private static User Operador() => new() { Id = Guid.CreateVersion7(), TenantId = SecureGatePlatform.TenantId };

	[Fact]
	public void Operador_Sem2Fa_PrecisaCadastrar() =>
		OperatorTwoFactorPolicy.RequiresEnrollment(Operador(), [SecureGatePlatform.OperatorRole], twoFactorEnabled: false)
			.Should().BeTrue();

	[Fact]
	public void Operador_Com2Fa_NaoPrecisa() =>
		OperatorTwoFactorPolicy.RequiresEnrollment(Operador(), [SecureGatePlatform.OperatorRole], twoFactorEnabled: true)
			.Should().BeFalse();

	[Fact]
	public void UsuarioComum_Sem2Fa_NaoPrecisa() =>
		OperatorTwoFactorPolicy.RequiresEnrollment(
			new User { Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7() }, ["leitor"], twoFactorEnabled: false)
			.Should().BeFalse();

	[Fact]
	public void PapelHomonimoEmOutroTenant_NaoConta() =>
		// Mesma defesa da ADR-0023/0024: o nome do papel é único só por tenant.
		OperatorTwoFactorPolicy.RequiresEnrollment(
			new User { Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7() },
			[SecureGatePlatform.OperatorRole],
			twoFactorEnabled: false)
			.Should().BeFalse();
}
```

```csharp
// tests/SecureGate/Secco.SecureGate.Tests/Integration/TwoFactorOperatorTests.cs
[Fact]
public async Task Operador_Sem2Fa_NaoObtemCodigoDeAutorizacao()
{
	var email = await OperadorAsync();
	using var browser = await SignedInBrowserAsync(email, IdentitySeed.Password);

	var authorize = await browser.GetAsync(AuthorizeUrl());

	// A exigência age ANTES de o código sair, então o AdminPortal não precisa saber de nada.
	authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
	authorize.Headers.Location!.ToString().Should().Contain("/conta/dois-fatores");
}

[Fact]
public async Task Operador_ComCadastroFeito_ObtemCodigo()
{
	var email = await OperadorCom2FaAsync();
	using var browser = await SignedInBrowserAsync(email, IdentitySeed.Password);

	var authorize = await browser.GetAsync(AuthorizeUrl());

	authorize.Headers.Location!.ToString().Should().StartWith(RedirectUri);
}

[Fact]
public async Task Operador_NaoDesligaOProprioSegundoFator()
{
	var email = await OperadorCom2FaAsync();
	using var browser = await SignedInBrowserAsync(email, IdentitySeed.Password);

	var resposta = await SubmitDisableAsync(browser);

	// Mesma lógica de ninguém desativar a própria conta nem se remover do papel de operador:
	// senão a exigência vira decoração.
	resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	(await resposta.Content.ReadAsStringAsync()).Should().Contain("operador");
}

[Fact]
public async Task UsuarioComum_Sem2Fa_ObtemCodigoNormalmente()
{
	var email = Email();
	await IdentitySeed.UserAsync(factory, _tenantId, email);
	using var browser = await SignedInBrowserAsync(email, IdentitySeed.Password);

	(await browser.GetAsync(AuthorizeUrl())).Headers.Location!.ToString().Should().StartWith(RedirectUri);
}
```

- [ ] **Passo 2: rodar e ver falhar.**

- [ ] **Passo 3: implementar**

Em `AuthorizeAsync`, logo depois do `var roles = await userManager.GetRolesAsync(user);` e **antes** do `FilterScopes`:

```csharp
		// Operador de instalação sem segundo fator não recebe código de autorização (entrega D):
		// é a conta que lê log de todos os tenants por elevação (ADR-0031). O desvio vai para o
		// cadastro e volta a esta mesma requisição depois.
		if (OperatorTwoFactorPolicy.RequiresEnrollment(user, roles, user.TwoFactorEnabled))
		{
			var retorno = context.Request.PathBase + context.Request.Path + context.Request.QueryString;

			return Results.Redirect(
				OperatorTwoFactorPolicy.EnrollmentPath + "?returnUrl=" + Uri.EscapeDataString(retorno));
		}
```

A política ganha um segundo método, explícito:

```csharp
	/// <summary>
	/// Operador não desliga o próprio segundo fator. Mesma lógica de ninguém desativar a própria
	/// conta nem se remover do papel de operador: senão a exigência vira decoração.
	/// </summary>
	/// <param name="user">Conta.</param>
	/// <param name="roles">Papéis da conta no tenant dela.</param>
	public static bool CanDisable(User user, IReadOnlyList<string> roles) =>
		!InstallationOperatorPolicy.IsInstallationOperator(user, roles);
```

Na tela de cadastro, o botão de desligar não aparece para operador, e o `OnPostDisableAsync` recusa de novo no servidor — o botão escondido é conveniência, a guarda é esta:

```csharp
		if (!OperatorTwoFactorPolicy.CanDisable(user, roles))
		{
			ErrorMessage = "O segundo fator é obrigatório para operadores da instalação e não pode ser desativado.";

			return Page();
		}
```

- [ ] **Passo 4: rodar e ver passar.**

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): segundo fator obrigatório para o operador de instalação (ADR-0030)"
```

---

### Task 6: reset pelo admin

**Arquivos:**
- Criar: `src/SecureGate/Secco.SecureGate.Application/Credentials/ResetTwoFactorHandler.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Endpoints/UserEndpoints.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Application/SecureGateApplicationExtensions.cs`
- Criar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/TwoFactorResetTests.cs`
- Modificar: `src/AdminPortal/Secco.AdminPortal/Services/IUserAdminService.cs`, `Components/Pages/UserManagement.razor`, `tests/AdminPortal/.../IdentityAdminServicesTests.cs`

**Interfaces:**
- Produz: rota `POST /api/v1/tenants/{tenantId}/users/{userId}/two-factor/reset` (`ResetUserTwoFactor`); `IUserAdminService.ResetTwoFactorAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)`.

- [ ] **Passo 1: escrever os testes que falham**

```csharp
[Fact]
public async Task Reset_ZeraOCadastroEAvisaODono()
{
	var (userId, email) = await UsuarioCom2FaAsync();
	using var admin = await AdminAsync();

	var resposta = await admin.PostAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/two-factor/reset", null);

	resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);

	using var scope = factory.Services.CreateScope();
	var state = await scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>().GetStateAsync(userId);
	state!.Enabled.Should().BeFalse();
	// Reset ZERA o cadastro; não isenta ninguém.
	state.HasAuthenticator.Should().BeFalse();
	factory.Emails.For(email).Last().Subject.Should().Contain("desativado");
}

[Fact]
public async Task Reset_Repetido_ContinuaRespondendo204()
{
	var (userId, _) = await UsuarioCom2FaAsync();
	using var admin = await AdminAsync();
	var rota = $"/api/v1/tenants/{_tenantId}/users/{userId}/two-factor/reset";

	await admin.PostAsync(rota, null);

	(await admin.PostAsync(rota, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
}

[Fact]
public async Task Reset_DeOutroTenant_Responde404()
{
	var (userId, _) = await UsuarioCom2FaAsync();
	var outroTenant = await IdentitySeed.TenantAsync(factory);
	using var admin = await AdminAsync();

	var resposta = await admin.PostAsync($"/api/v1/tenants/{outroTenant}/users/{userId}/two-factor/reset", null);

	resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);

	using var scope = factory.Services.CreateScope();
	(await scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>().GetStateAsync(userId))!
		.Enabled.Should().BeTrue("a rota de outro tenant não pode mexer na conta");
}

[Fact]
public async Task Reset_DeOperador_ExigeCadastroNoProximoLogin()
{
	var (userId, email) = await OperadorCom2FaAsync();
	using var admin = await AdminAsync();

	await admin.PostAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/users/{userId}/two-factor/reset", null);

	using var browser = await SignedInBrowserAsync(email, IdentitySeed.Password);
	var authorize = await browser.GetAsync(AuthorizeUrl());

	authorize.Headers.Location!.ToString().Should().Contain("/conta/dois-fatores");
}
```

- [ ] **Passo 2: rodar e ver falhar.**

- [ ] **Passo 3: implementar**

`ResetTwoFactorHandler` segue o molde do `SetLocalLoginHandler`: `tokens.FindAsync`, `404` para outro tenant, `setup.DisableAsync`, revogação de sessões, e-mail `SendTwoFactorChangedNoticeAsync(enabled: false)` e `CredentialAuditEvent.TwoFactorReset`. Idempotente: conta sem 2FA percorre o mesmo caminho e responde igual.

A rota, no grupo existente:

```csharp
		group.MapPost("/{userId:guid}/two-factor/reset", async (
				Guid tenantId,
				Guid userId,
				ResetTwoFactorHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("ResetUserTwoFactor")
			.WithSummary("Zera o cadastro do segundo fator do usuário (não isenta: o próximo login cadastra de novo).")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound);
```

No AdminPortal, o serviço chama `client.ResetUserTwoFactorAsync(tenantId, userId, ct)` e a página do usuário ganha, na seção **Credencial**, o botão **Resetar segundo fator** com confirmação, mostrando o estado atual (`Segundo fator: ativado/desativado`) — o `UserDetailDto` precisa ganhar `twoFactorEnabled`, o que **muda o contrato** e entra nesta mesma tarefa, com `openapi.json` regenerado.

- [ ] **Passo 4: rodar e ver passar**, regenerar contrato e rodar a suíte do AdminPortal.

- [ ] **Passo 5: commit**

```bash
SECCO_UPDATE_OPENAPI=true dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter FullyQualifiedName~OpenApiContractTests
git add -A
git commit -m "feat(securegate): reset do segundo fator pelo admin, com aviso ao dono (entrega D)"
```

---

### Task 7: mutação, documentação e publicação

**Arquivos:**
- Criar: `<scratchpad>/mutate_2fa.py` (fora do repositório)
- Modificar: `CHANGELOG.md`, `docs/roadmap.md`, `docs/design-decisions-log.md`, `src/SecureGate/README.md`

- [ ] **Passo 1: bateria de mutação**

| # | Mutação | Teste esperado |
|---|---|---|
| M1 | cadastro liga o 2FA sem conferir o código | `Cadastro_SoLigaDepoisDeConfirmarComCodigoValido` |
| M2 | código de recuperação não é consumido | `Login_ComCodigoDeRecuperacao_EntraEOCodigoNaoServeDeNovo` |
| M3 | dígito errado não alimenta o lockout | `Login_ComDigitoErrado_NaoEntra` (com a variante de lockout) |
| M4 | operador sem 2FA recebe código de autorização | `Operador_Sem2Fa_NaoObtemCodigoDeAutorizacao` |
| M5 | operador consegue desligar o próprio | `Operador_NaoDesligaOProprioSegundoFator` |
| M6 | reset isenta em vez de zerar (não apaga a chave) | `Reset_ZeraOCadastroEAvisaODono` |
| M7 | ligar/desligar não revoga sessões | `Ligar_ComCodigoValido_DevolveCodigosEEncerraAsOutrasSessoes` |
| M8 | reset atravessa tenant | `Reset_DeOutroTenant_Responde404` |

Mutação sobrevivente = teste faltando. Falha de build **não** conta como detecção.

- [ ] **Passo 2: documentação**

- `CHANGELOG.md`: `Secco.SecureGate.Client` 0.11.0 — rota nova `ResetUserTwoFactor` e `twoFactorEnabled` no `UserDetailDto`.
- `docs/roadmap.md`: entrada da entrega D, fechando a série de identidade (A–D).
- `docs/design-decisions-log.md`: por que TOTP e não SMS/e-mail, por que obrigatório só para operador, por que sem "lembrar dispositivo", por que o QR sai do servidor, e por que reset zera em vez de isentar.
- `src/SecureGate/README.md`: as duas telas novas e a rota do reset.

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

Taguear no HEAD as dependências apontadas, **uma tag por push**, esperando cada workflow. Depois mover o CHANGELOG para "Publicado" e atualizar `README.md` e `docs/getting-started.md`.

---

## Auto-revisão

**Cobertura da spec:** cadastro com QR local e confirmação (T1, T4), códigos de recuperação (T1, T2, T3), login em dois passos (T3), federado sem dígito (T3 — o caminho do Entra não passa pelo `PasswordSignInAsync`, e o teste `UsuarioComum_Sem2Fa_ObtemCodigoNormalmente` mais o E2E federado existente cobrem a ausência de regressão), obrigatoriedade e não-desligamento do operador (T5), reset que zera e avisa (T6), auditoria dos quatro eventos (T2, T3, T6), mutação e docs (T7).

**Tipos consistentes:** `ITwoFactorSetup` tem a mesma assinatura em T1, T2, T4 e T6; `TwoFactorState` é o retorno usado nos testes de todas elas; `OperatorTwoFactorPolicy.RequiresEnrollment(user, roles, twoFactorEnabled)` e `CanDisable(user, roles)` aparecem com a mesma forma em T5 e na tela da T4.

**Pontos que o executor deve confirmar antes de seguir:** (1) o nome gerado pelo NSwag para a rota nova — o plano assume `ResetUserTwoFactorAsync(tenantId, userId, ct)`, derivado do `WithName`; se o gerador divergir, vale o gerador. (2) `QRCoder` 1.6.0 no feed — se a versão não existir, use a mais recente estável e registre no commit.
