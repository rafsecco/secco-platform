using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.Testing;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Sobe a API real (ambiente <c>Testing</c> — sem migrations/seed automáticos de DEV)
/// sobre a base da plataforma (ADR-0027), hospedando o banco de PLATAFORMA
/// <c>secco_securegate</c> (ADR-0022 — identidade não é dado de tenant).
/// Herdável: <see cref="SelfIssuedAuthSecureGateApiFactory"/> troca a chave HS256 de
/// testes pela Authority do próprio servidor.
/// </summary>
public class SecureGateApiFactory : SeccoApiFactory<Program>
{
	/// <inheritdoc />
	protected override string Audience => "secco-securegate";

	private const string PlatformDatabase = "secco_securegate";

	private string? _runtimeConnectionString;

	/// <summary>
	/// Connection string do banco de PLATAFORMA com o login de ADMIN da instância (ADR-0022): é a
	/// credencial de MIGRAÇÃO e a que os testes usam para inspecionar o banco por fora.
	/// </summary>
	public string GetPlatformConnectionString() => GetConnectionStringFor(PlatformDatabase);

	/// <summary>
	/// Connection string de RUNTIME (ADR-0041): login criado por esta factory, SEM posse do banco.
	/// Só existe depois de <c>InitializeAsync</c>.
	/// </summary>
	public string GetRuntimeConnectionString() =>
		_runtimeConnectionString
		?? throw new InvalidOperationException("O login de runtime só existe depois de InitializeAsync().");

	/// <summary>
	/// Papéis de banco do login de runtime. Padrão: leitura e escrita, sem posse (ADR-0041) — é com
	/// ele que a suíte inteira roda, então qualquer caminho de runtime que faça DDL quebra aqui.
	/// </summary>
	protected virtual string[] RuntimeDatabaseRoles => ["db_datareader", "db_datawriter"];

	/// <summary>
	/// Cria, antes de o host subir, o banco de plataforma (vazio) e o login de runtime: o usuário de
	/// banco precisa de um banco existente, então o banco nasce aqui e as migrations (credencial de
	/// admin) o populam depois. Senha aleatória por factory; identificadores gerados aqui e passados
	/// por parâmetro ao T-SQL com QUOTENAME — nunca concatenados (ADR-0020).
	/// </summary>
	protected override async Task OnInitializedAsync()
	{
		await CreateDatabaseAsync(PlatformDatabase);

		var login = $"secco_securegate_rt_{Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant()}";
		// Hex + sufixo fixo: atende à política de complexidade sem caracteres que exijam escape
		var password = $"Rt{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}a1!";

		var connectionString = new SqlConnectionStringBuilder(GetPlatformConnectionString());

		await using (var connection = new SqlConnection(connectionString.ConnectionString))
		{
			await connection.OpenAsync();

			await ExecuteAsync(
				connection,
				"DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') + N', CHECK_POLICY = OFF;' "
				+ "+ N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N';'; "
				+ "EXEC sp_executesql @sql;",
				("@login", login),
				("@password", password));

			foreach (var role in RuntimeDatabaseRoles)
			{
				// Papel por allowlist estrita: ALTER ROLE não aceita parâmetro
				if (role is not ("db_datareader" or "db_datawriter" or "db_owner"))
				{
					throw new InvalidOperationException($"Papel de banco não permitido nos testes: '{role}'.");
				}

				await ExecuteAsync(
					connection,
					"DECLARE @sql nvarchar(max) = N'ALTER ROLE ' + QUOTENAME(@role) + N' ADD MEMBER ' + QUOTENAME(@login) + N';'; EXEC sp_executesql @sql;",
					("@role", role),
					("@login", login));
			}
		}

		connectionString.UserID = login;
		connectionString.Password = password;
		connectionString.IntegratedSecurity = false;
		_runtimeConnectionString = connectionString.ConnectionString;
	}

	private static async Task ExecuteAsync(SqlConnection connection, string commandText, params (string Name, string Value)[] parameters)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = commandText;
		command.CommandType = CommandType.Text;

		foreach (var (name, value) in parameters)
		{
			command.Parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, 256) { Value = value });
		}

		await command.ExecuteNonQueryAsync();
	}

	/// <summary>Base pública dos links de credencial nos testes (ADR-0033: nunca vem do header Host).</summary>
	public const string PublicBaseUrl = "https://id.testes.local";

	/// <summary>E-mails que a aplicação tentou enviar — nenhum sai de verdade em teste.</summary>
	public FakeEmailSender Emails { get; } = new();

	/// <inheritdoc />
	protected override void ConfigureTestServices(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		// Registrado DEPOIS do AddSeccoEmail da aplicação: a última inscrição vence na resolução.
		services.AddScoped<Secco.SDK.Email.ISeccoEmailSender>(_ => Emails);
	}

	/// <summary>Aplica migrations + seed de referência (scopes) — a base garante a chamada única.</summary>
	protected override async Task MigrateAsync(IServiceProvider services)
	{
		await Secco.SecureGate.Infrastructure.SecureGateInfrastructureExtensions
			.MigrateSecureGateDatabaseAsync(services);

		// Seed de referência (scopes de produto); o de DEV não roda em Testing (guarda dupla)
		await Secco.SDK.EntityFrameworkCore.Seeding.SeccoSeedingExtensions
			.SeedSeccoDataAsync(services);
	}

	/// <summary>Registra um client OIDC de teste (client credentials) com os scopes informados.</summary>
	public Task CreateClientAsync(string clientId, string clientSecret, params string[] scopes) =>
		CreateClientWithRolesAsync(clientId, clientSecret, roles: null, scopes);

	/// <summary>
	/// Registra um client OIDC de teste com scopes e roles (Fase 6.4, ADR-0021 —
	/// máquinas carregam a claim curta <c>role</c> como os usuários). Nome distinto por
	/// design: um overload posicional confundiria scope com roles.
	/// </summary>
	public async Task CreateClientWithRolesAsync(string clientId, string clientSecret, string? roles, params string[] scopes)
	{
		using var scope = Services.CreatePlatformScope();
		var applications = scope.ServiceProvider.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>();

		if (await applications.FindByClientIdAsync(clientId) is not null)
		{
			return;
		}

		var descriptor = new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
		{
			ClientId = clientId,
			ClientSecret = clientSecret,
			DisplayName = $"Client de teste {clientId}",
		};

		descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token);
		descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);

		foreach (var scopeName in scopes)
		{
			descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + scopeName);
		}

		await applications.CreateAsync(descriptor);

		if (!string.IsNullOrWhiteSpace(roles)
			&& await applications.FindByClientIdAsync(clientId)
				is Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication application)
		{
			application.Roles = roles;
			await applications.UpdateAsync(application);
		}
	}

	/// <summary>
	/// Registra direto no banco um client de PRODUTO vinculado ao tenant (ADR-0037) — atalho de
	/// teste para exercitar a emissão sem depender da API de gestão. Permissões gravadas como
	/// vierem, inclusive escopo proibido: é assim que os testes provam a checagem na emissão.
	/// </summary>
	public async Task CreateProductClientAsync(
		Guid tenantId, string clientId, string clientSecret, string? roles, params string[] scopes)
	{
		using var scope = Services.CreatePlatformScope();
		var applications = scope.ServiceProvider
			.GetRequiredService<OpenIddict.Core.OpenIddictApplicationManager<Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication>>();

		var application = new Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication
		{
			TenantId = tenantId,
			Origin = Secco.SecureGate.Application.Clients.ClientOrigin.Api,
			Name = clientId,
			Roles = roles,
		};

		var descriptor = new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
		{
			ClientId = clientId,
			ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Confidential,
			DisplayName = clientId,
		};
		descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token);
		descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);

		foreach (var scopeName in scopes)
		{
			descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + scopeName);
		}

		await applications.PopulateAsync(application, descriptor);
		await applications.CreateAsync(application, clientSecret);
	}

	/// <summary>
	/// Registra um client PÚBLICO de teste (authorization code + PKCE + refresh, sem secret) —
	/// o modelo de uma aplicação web/SPA (Fase 6.5). Consent implícito (first-party).
	/// </summary>
	public async Task CreatePublicClientAsync(string clientId, string redirectUri, params string[] scopes)
	{
		using var scope = Services.CreatePlatformScope();
		var applications = scope.ServiceProvider.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>();

		if (await applications.FindByClientIdAsync(clientId) is not null)
		{
			return;
		}

		var descriptor = new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
		{
			ClientId = clientId,
			ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Public,
			ConsentType = OpenIddict.Abstractions.OpenIddictConstants.ConsentTypes.Implicit,
			DisplayName = $"Client público de teste {clientId}",
			RedirectUris = { new Uri(redirectUri) },
			Permissions =
			{
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Authorization,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.EndSession,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.ResponseTypes.Code,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Email,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Profile,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Roles,
			},
		};

		foreach (var scopeName in scopes)
		{
			descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + scopeName);
		}

		await applications.CreateAsync(descriptor);
	}

	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		// Duas credenciais (ADR-0041): runtime SEM posse e migração com o admin da instância
		settings["SecureGate:Database:ConnectionString"] = GetRuntimeConnectionString();
		settings["SecureGate:Database:MigrationConnectionString"] = GetPlatformConnectionString();

		// O host de teste sobe ANTES de a factory migrar o banco (ADR-0027): a recusa com migration
		// pendente (ADR-0038) tem teste próprio em SecureGateMigrationTests, que a liga.
		settings["SecureGate:Database:VerifyMigrationsOnStartup"] = "false";

		// Credenciais (ADR-0033): a seção de e-mail e a base pública são validadas no startup,
		// então sem estas chaves nenhuma suíte sobe. O envio em si é trocado por um dublê nos
		// testes que o exercitam; aqui só se satisfaz o fail-fast.
		settings["SecureGate:PublicBaseUrl"] = PublicBaseUrl;
		settings["SecureGate:Email:Host"] = "smtp.testes.local";
		settings["SecureGate:Email:FromAddress"] = "no-reply@testes.local";

		// Piso de tempo da resposta do "esqueci minha senha" zerado: em teste ele só deixaria a
		// suíte lenta — a resposta idêntica, que é a proteção de verdade, continua valendo.
		settings["SecureGate:Credentials:ResponseFloorMilliseconds"] = "0";
	}
}
