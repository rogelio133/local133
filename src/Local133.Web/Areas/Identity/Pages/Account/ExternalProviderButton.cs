using Microsoft.AspNetCore.Authentication;

namespace Local133.Web.Areas.Identity.Pages.Account;

/// <summary>Modelo del parcial <c>_ExternalProviderButton</c>: "Entrar con Google", "Ligar Facebook"…</summary>
public sealed record ExternalProviderButton(AuthenticationScheme Scheme, string Verb, string? Id = null);
