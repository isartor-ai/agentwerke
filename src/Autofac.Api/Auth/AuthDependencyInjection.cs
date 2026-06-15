using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Autofac.Api.Auth;

public static class AuthDependencyInjection
{
    public static IServiceCollection AddAutofacAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var opts = new JwtOptions();
        configuration.GetSection(JwtOptions.Section).Bind(opts);
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));

        var authBuilder = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

        if (!string.IsNullOrWhiteSpace(opts.SecretKey))
        {
            authBuilder.AddJwtBearer(o =>
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SecretKey));
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = opts.Issuer is not null,
                    ValidIssuer = opts.Issuer,
                    ValidateAudience = opts.Audience is not null,
                    ValidAudience = opts.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        }
        else if (!string.IsNullOrWhiteSpace(opts.Authority))
        {
            authBuilder.AddJwtBearer(o =>
            {
                o.Authority = opts.Authority;
                o.Audience = opts.Audience;
            });
        }
        else
        {
            // No auth configured — allow anonymous; protect endpoints via policy fallback
            authBuilder.AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = false,
                    ValidateIssuerSigningKey = false,
                    SignatureValidator = (token, _) =>
                    {
                        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                        return handler.ReadJwtToken(token);
                    }
                };
            });
        }

        services.AddAuthorization(o =>
        {
            o.AddPolicy(AutofacPolicies.Operator, p =>
                p.RequireRole(AutofacRoles.Operator, AutofacRoles.Admin));

            o.AddPolicy(AutofacPolicies.Approver, p =>
                p.RequireRole(AutofacRoles.Approver, AutofacRoles.Admin));

            o.AddPolicy(AutofacPolicies.Admin, p =>
                p.RequireRole(AutofacRoles.Admin));
        });

        return services;
    }
}
