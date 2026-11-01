using FluentAssertions;
using Secco.SDK.AspNetCore.SecurityHeaders;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.SecurityHeaders;

public sealed class SeccoSecurityHeadersOptionsValidatorTests
{
	private static bool IsValid(Action<SeccoSecurityHeadersOptions> configure)
	{
		var options = new SeccoSecurityHeadersOptions();
		configure(options);
		return new SeccoSecurityHeadersOptionsValidator().Validate(null, options).Succeeded;
	}

	[Fact]
	public void Padroes_sao_validos()
		=> IsValid(_ => { }).Should().BeTrue();

	[Theory]
	[InlineData("'unsafe-inline'")]
	[InlineData("'unsafe-eval'")]
	[InlineData("'unsafe-hashes'")]
	[InlineData("'strict-dynamic'")]
	[InlineData("'none'")]
	[InlineData("*")]
	[InlineData("data:")]
	[InlineData("blob:")]
	[InlineData("https:")]
	[InlineData("http://cdn.exemplo.com")]
	[InlineData("https://*.exemplo.com")]
	[InlineData("https://cdn.exemplo.com; script-src *")]
	[InlineData("https://cdn.exemplo.com,https://outro.exemplo.com")]
	[InlineData("https://cdn.exemplo.com https://outro.exemplo.com")]
	[InlineData("https://cdn.exemplo.com\r\nX-Injetado: 1")]
	[InlineData("https://cdn.exemplo.com/lib;script-src-elem")]
	[InlineData("https://cdn.exemplo.com/lib,script-src")]
	[InlineData("https://cdn.exemplo.com/'unsafe-inline'")]
	[InlineData("https://usuario@cdn.exemplo.com")]
	[InlineData("https://cdn.exemplo.com/?q=1")]
	[InlineData("")]
	public void Script_recusa_o_que_afrouxa_ou_quebra_o_cabecalho(string source)
		=> IsValid(o => o.ScriptSources.Add(source)).Should().BeFalse();

	[Theory]
	[InlineData("'unsafe-inline'")]
	[InlineData("data:")]
	public void Estilo_recusa_inline_e_data(string source)
		=> IsValid(o => o.StyleSources.Add(source)).Should().BeFalse();

	[Theory]
	[InlineData("'self'")]
	[InlineData("https://cdn.exemplo.com")]
	[InlineData("https://cdn.exemplo.com:8443/lib/")]
	[InlineData("wss://tempo-real.exemplo.com")]
	public void Script_aceita_self_e_origem_segura(string source)
		=> IsValid(o => o.ScriptSources.Add(source)).Should().BeTrue();

	[Fact]
	public void Imagem_aceita_data_e_blob_e_fonte_aceita_data()
		=> IsValid(o =>
		{
			o.ImageSources.Add("data:");
			o.ImageSources.Add("blob:");
			o.FontSources.Add("data:");
		}).Should().BeTrue();

	[Fact]
	public void Fonte_recusa_blob()
		=> IsValid(o => o.FontSources.Add("blob:")).Should().BeFalse();

	[Fact]
	public void Frame_ancestors_vazio_e_invalido()
		=> IsValid(o => o.FrameAncestors.Clear()).Should().BeFalse();

	[Fact]
	public void Frame_ancestors_none_nao_admite_outra_origem()
		=> IsValid(o =>
		{
			o.DenyFraming();
			o.FrameAncestors.Add("'self'");
		}).Should().BeFalse();

	[Fact]
	public void Frame_ancestors_aceita_none_sozinho_e_origens_seguras()
	{
		IsValid(o => o.DenyFraming()).Should().BeTrue();
		IsValid(o => o.FrameAncestors.Add("https://portal.exemplo.com")).Should().BeTrue();
	}

	[Fact]
	public void Frame_ancestors_recusa_curinga()
		=> IsValid(o => o.FrameAncestors.Add("*")).Should().BeFalse();

	[Theory]
	[InlineData("*")]
	[InlineData("'unsafe-inline'")]
	[InlineData("https:")]
	public void Form_action_recusa_curinga(string source)
		=> IsValid(o => o.FormActions.Add(source)).Should().BeFalse();

	[Theory]
	[InlineData("Camera")]
	[InlineData("camera=()")]
	[InlineData("camera, microphone")]
	[InlineData("")]
	[InlineData("1camera")]
	public void Permissao_recusa_nome_invalido(string permission)
		=> IsValid(o => o.AllowedPermissions.Add(permission)).Should().BeFalse();

	[Theory]
	[InlineData("http://relatorios.exemplo.com/csp")]
	[InlineData("https://relatorios.exemplo.com/csp;script-src")]
	[InlineData("https://relatorios.exemplo.com/csp?a=1,2")]
	public void Report_uri_exige_https_sem_quebrar_o_cabecalho(string reportUri)
		=> IsValid(o => o.ReportUri = new Uri(reportUri)).Should().BeFalse();

	[Fact]
	public void Report_uri_relativa_e_invalida()
		=> IsValid(o => o.ReportUri = new Uri("/csp", UriKind.Relative)).Should().BeFalse();

	[Fact]
	public void Hsts_ligado_exige_prazo_positivo()
	{
		IsValid(o => o.HstsMaxAge = TimeSpan.Zero).Should().BeFalse();
		IsValid(o =>
		{
			o.HstsEnabled = false;
			o.HstsMaxAge = TimeSpan.Zero;
		}).Should().BeTrue();
	}

	[Fact]
	public void Mensagem_de_falha_nao_ecoa_a_origem_recusada()
	{
		var options = new SeccoSecurityHeadersOptions();
		options.ScriptSources.Add("https://x.exemplo.com\r\nForjado: 1");

		var result = new SeccoSecurityHeadersOptionsValidator().Validate(null, options);

		result.FailureMessage.Should().NotContain("Forjado");
	}
}
