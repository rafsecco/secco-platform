using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Secco.SDK.Logging.Tests")]

// NSubstitute (Castle DynamicProxy) precisa enxergar os tipos internos para criar substitutos
// das abstrações internas do pacote — o gateway de ingestão, em particular.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
