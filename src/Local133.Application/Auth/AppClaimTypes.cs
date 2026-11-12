namespace Local133.Application.Auth;

/// <summary>Claims propios que se agregan a la cookie al iniciar sesión.</summary>
public static class AppClaimTypes
{
    /// <summary>Id del <c>Merchant</c> del usuario (solo vendedores); define el tenant de la petición.</summary>
    public const string MerchantId = "local133:merchant_id";
}
