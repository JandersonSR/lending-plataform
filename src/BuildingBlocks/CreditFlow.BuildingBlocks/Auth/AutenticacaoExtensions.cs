using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CreditFlow.BuildingBlocks.Auth;

public static class AutenticacaoExtensions
{
    private static readonly Action<ILogger, string, string, Exception?> LogTokenRecusado =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(3001, "TokenRecusado"),
            "Token recusado: {Tipo}: {Mensagem}");

    /// <summary>
    /// JwtBearer contra o realm do Keycloak: valida assinatura (JWKS do realm), iss, aud, exp/nbf;
    /// transforma realm_access.roles em claims "role"; registra as policies e o <see cref="ICurrentUser"/>.
    /// </summary>
    public static IServiceCollection AddAutenticacaoCreditFlow(this IServiceCollection services, IConfiguration configuracao)
    {
        services.AddOptions<AuthOpcoes>()
            .Bind(configuracao.GetSection(AuthOpcoes.Secao))
            .Validate(o => Uri.TryCreate(o.Authority, UriKind.Absolute, out _), "Auth:Authority precisa ser uma URL absoluta")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Auth:Audience é obrigatório")
            // Falhar na subida com uma mensagem clara, em vez de 401 em toda requisição depois.
            .Validate(o => !o.ExigirHttps || o.Authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                "Auth:Authority é http:// mas Auth:ExigirHttps é true. Em desenvolvimento, ponha Auth:ExigirHttps=false; fora dele, use https.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configura o JwtBearer a partir de AuthOpcoes já resolvido (respeita variáveis de ambiente e testes).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOpcoes>>((jwt, auth) => Configurar(jwt, auth.Value));

        services.AddAuthorization(Politicas.Registrar);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, UsuarioAtual>();
        return services;
    }

    /// <summary>Autenticação antes de autorização. Chame depois de UseRouting (se houver) e antes de mapear endpoints.</summary>
    public static IApplicationBuilder UseAutenticacaoCreditFlow(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    internal static void Configurar(JwtBearerOptions jwt, AuthOpcoes auth)
    {
        var authority = auth.Authority.TrimEnd('/');
        jwt.Authority = authority;
        jwt.Audience = auth.Audience;
        if (!string.IsNullOrWhiteSpace(auth.MetadataAddress))
            jwt.MetadataAddress = auth.MetadataAddress;
        jwt.RequireHttpsMetadata = auth.ExigirHttps;

        // Sem isto, o handler renomeia "sub" para o URI longo do WS-Federation e "role" para ClaimTypes.Role.
        jwt.MapInboundClaims = false;
        jwt.SaveToken = false;

        var p = jwt.TokenValidationParameters;
        p.ValidateIssuer = true;
        p.ValidIssuers = [authority, .. auth.EmissoresAdicionais.Select(e => e.TrimEnd('/'))];
        p.ValidateAudience = true;
        p.ValidAudience = auth.Audience;
        p.ValidateLifetime = true;
        p.RequireExpirationTime = true;
        p.RequireSignedTokens = true;
        p.ValidAlgorithms = ["RS256"];
        p.ClockSkew = TimeSpan.FromSeconds(auth.FolgaDeRelogioSegundos);
        p.NameClaimType = ClaimsKeycloak.PreferredUsername;
        p.RoleClaimType = ClaimsKeycloak.Role;

        jwt.Events = new JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                if (ctx.Principal?.Identity is ClaimsIdentity identidade)
                {
                    var emissor = ctx.Principal.FindFirstValue("iss") ?? authority;
                    identidade.AddClaims(ClaimsKeycloak.PapeisDoRealm(ctx.Principal, emissor).ToList());
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = ctx =>
            {
                // O motivo vai para o log (iss errado, aud errado, expirado, kid desconhecido); o cliente só vê 401.
                var log = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("CreditFlow.Auth");
                LogTokenRecusado(log, ctx.Exception.GetType().Name, ctx.Exception.Message, null);
                return Task.CompletedTask;
            }
        };
    }
}
