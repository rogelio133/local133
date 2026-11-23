using Local133.Domain.Merchants;

namespace Local133.Web.Identity;

/// <summary>
/// Cookie del código de invitación (5.8): la escribe la liga <c>/r/{código}</c> y dura 30 días, para que el código
/// llegue al registro aunque la persona vuelva después o se registre con Google o Facebook. Se borra al crear la
/// cuenta.
/// </summary>
public static class ReferralCookie
{
    public const string Name = "l133_ref";

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>Código de la cookie, normalizado; <c>null</c> si no hay o no tiene la forma de un código.</summary>
    public static string? Read(HttpRequest request)
    {
        var code = ReferralCode.Normalize(request.Cookies[Name]);
        return ReferralCode.IsValid(code) ? code : null;
    }

    public static void Write(HttpResponse response, string code) =>
        response.Cookies.Append(Name, code, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = response.HttpContext.Request.IsHttps,
            MaxAge = Lifetime,
            Path = "/",
        });

    public static void Delete(HttpResponse response)
    {
        if (response.HttpContext.Request.Cookies.ContainsKey(Name))
        {
            response.Cookies.Delete(Name, new CookieOptions { Path = "/" });
        }
    }

    /// <summary>
    /// Código que se propone en el formulario de alta: el de la URL (<c>?codigo=</c>) o, sin él, el de la cookie.
    /// </summary>
    public static string? Proposed(HttpRequest request, string? fromQuery)
    {
        var code = ReferralCode.Normalize(fromQuery);
        return ReferralCode.IsValid(code) ? code : Read(request);
    }
}
