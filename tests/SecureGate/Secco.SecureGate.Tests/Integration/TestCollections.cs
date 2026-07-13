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
