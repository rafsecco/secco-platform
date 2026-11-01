using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Secco.SDK.AspNetCore.SecurityHeaders;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.SecurityHeaders;

public sealed class SeccoCspNonceTagHelperTests
{
	[Theory]
	[InlineData("script")]
	[InlineData("style")]
	public void Troca_o_marcador_pelo_nonce_da_requisicao(string tagName)
	{
		var httpContext = new DefaultHttpContext();
		var feature = new SeccoCspNonceFeature(httpContext);
		httpContext.Features.Set(feature);
		var helper = new SeccoCspNonceTagHelper { ViewContext = new ViewContext { HttpContext = httpContext } };
		var output = new TagHelperOutput(
			tagName,
			[new TagHelperAttribute(SeccoCspNonceTagHelper.AttributeName)],
			(_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

		helper.Process(new TagHelperContext([], new Dictionary<object, object>(), "id"), output);

		output.Attributes.ContainsName(SeccoCspNonceTagHelper.AttributeName).Should().BeFalse();
		output.Attributes["nonce"].Value.Should().Be(feature.Value);
		feature.Used.Should().BeTrue();
	}

	[Fact]
	public void Nonce_renderizado_no_html_e_identico_ao_do_cabecalho()
	{
		// Base64 padrão tinha '+', que o Razor escreve como &#x2B;: muitas amostras cobrem o alfabeto.
		for (var i = 0; i < 500; i++)
		{
			var httpContext = new DefaultHttpContext();
			var feature = new SeccoCspNonceFeature(httpContext);
			httpContext.Features.Set(feature);
			var helper = new SeccoCspNonceTagHelper { ViewContext = new ViewContext { HttpContext = httpContext } };
			var output = new TagHelperOutput(
				"script",
				[new TagHelperAttribute(SeccoCspNonceTagHelper.AttributeName)],
				(_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

			helper.Process(new TagHelperContext([], new Dictionary<object, object>(), "id"), output);

			using var writer = new StringWriter();
			output.WriteTo(writer, HtmlEncoder.Default);
			writer.ToString().Should().Contain($"nonce=\"{feature.Value}\"");
		}
	}
}
