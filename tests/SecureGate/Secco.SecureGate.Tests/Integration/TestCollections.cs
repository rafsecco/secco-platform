using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Collection dos testes sobre a factory base (HS256 de testes): as classes compartilham
/// um único container/API — tokens forjados exercitam a autorização por scope isolada.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SharedApiCollectionDefinition : ICollectionFixture<SecureGateApiFactory>
{
	/// <summary>Nome da collection.</summary>
	public const string Name = "SecureGate API compartilhada";
}

/// <summary>
/// Collection dos testes com o SecureGate validando os próprios tokens (Authority = ele
/// mesmo): o fluxo de produção completo — client credentials, catálogo remoto e E2E.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SelfIssuedApiCollectionDefinition : ICollectionFixture<SelfIssuedAuthSecureGateApiFactory>
{
	/// <summary>Nome da collection.</summary>
	public const string Name = "SecureGate auto-validado";
}

/// <summary>
/// Testes que RE-EXECUTAM o seed de referência. Isolados da collection compartilhada: desde a
/// ADR-0037 o seed reconcilia os clients de plataforma e remove os não declarados — inclusive os
/// que os helpers de teste criam para as outras classes.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ReseedApiCollectionDefinition : ICollectionFixture<SecureGateApiFactory>
{
	/// <summary>Nome da collection.</summary>
	public const string Name = "SecureGate com seed re-executado";
}
