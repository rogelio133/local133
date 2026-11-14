using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Local133.Web.Hosting;

/// <summary>
/// Respeta <c>X-Forwarded-Proto</c> / <c>X-Forwarded-For</c> de un proxy confiable: dev tunnels en local (el túnel
/// entrega por HTTP al puerto 5231, pero la petición original fue HTTPS) y el proxy de producción. Sin esto,
/// <c>UseHttpsRedirection</c> responde 307 a una URL interna y el webhook de Pey nunca llega.
/// </summary>
/// <remarks>
/// Por defecto solo se confía en loopback (el proceso <c>devtunnel host</c> o un proxy en la misma máquina). Un proxy
/// en otra máquina se declara en <c>ForwardedHeaders:KnownProxies</c> (IPs) o <c>ForwardedHeaders:KnownNetworks</c>
/// (CIDR); nunca se confía en cualquiera, porque cualquiera podría falsear el esquema o la IP del cliente.
/// </remarks>
public static class ForwardedHeadersSetup
{
    public const string SectionName = "ForwardedHeaders";

    public static IServiceCollection AddAppForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var proxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var networks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var network in networks)
            {
                options.KnownNetworks.Add(Microsoft.AspNetCore.HttpOverrides.IPNetwork.Parse(network));
            }
        });
        return services;
    }
}
