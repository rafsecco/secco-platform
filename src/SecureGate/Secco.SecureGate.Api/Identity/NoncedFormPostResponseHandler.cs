using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore;
using Microsoft.Extensions.Primitives;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Secco.SDK.AspNetCore.SecurityHeaders;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Página de <c>response_mode=form_post</c> com o nonce da CSP (ADR-0057). A do OpenIddict se envia
/// por um <c>&lt;script&gt;</c> inline sem nonce, que a política bloqueia — e <c>form_post</c> é o padrão
/// do handler OIDC da Microsoft, então qualquer relying party ASP.NET cairia nela. Mesma marcação,
/// mais o nonce; roda antes do handler nativo e encerra a requisição.
/// </summary>
internal sealed class NoncedFormPostResponseHandler : IOpenIddictServerHandler<ApplyAuthorizationResponseContext>
{
	public static OpenIddictServerHandlerDescriptor Descriptor { get; }
		= OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyAuthorizationResponseContext>()
			.UseSingletonHandler<NoncedFormPostResponseHandler>()
			.SetOrder(OpenIddictServerAspNetCoreHandlers.Authentication.ProcessFormPostResponse.Descriptor.Order - 1_000)
			.SetType(OpenIddictServerHandlerType.Custom)
			.Build();

	public async ValueTask HandleAsync(ApplyAuthorizationResponseContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!string.Equals(context.ResponseMode, ResponseModes.FormPost, StringComparison.Ordinal))
		{
			return;
		}

		var response = context.Transaction.GetHttpRequest()?.HttpContext.Response
			?? throw new InvalidOperationException("A resposta form_post exige o host ASP.NET Core.");

		var encoder = HtmlEncoder.Default;
		var html = new StringBuilder()
			.AppendLine("<!doctype html>")
			.AppendLine("<html>")
			.AppendLine("<body>")
			.Append("<form name=\"form\" method=\"post\" action=\"").Append(encoder.Encode(context.RedirectUri ?? string.Empty)).AppendLine("\">");

		// Cada valor sai codificado: code, state e iss acabam em HTML servido na origem do SecureGate.
		foreach (var (name, value) in context.Response.GetParameters())
		{
			foreach (var item in (StringValues)value)
			{
				html.Append("<input type=\"hidden\" name=\"").Append(encoder.Encode(name))
					.Append("\" value=\"").Append(encoder.Encode(item ?? string.Empty)).AppendLine("\" />");
			}
		}

		html.AppendLine("<noscript><button type=\"submit\">Continuar</button></noscript>")
			.AppendLine("</form>")
			.Append("<script nonce=\"").Append(response.HttpContext.GetSeccoCspNonce()).AppendLine("\">document.form.submit();</script>")
			.AppendLine("</body>")
			.AppendLine("</html>");

		var body = Encoding.UTF8.GetBytes(html.ToString());

		response.ContentType = "text/html;charset=UTF-8";
		response.ContentLength = body.Length;
		await response.Body.WriteAsync(body);

		context.HandleRequest();
	}
}
