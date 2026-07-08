using System.Text;
using Core;
using FluentValidation;
using Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Identity.Features.GetCurrentUser;
using Identity.Features.Login;
using Identity.Features.RegisterUser;
using Identity.Persistence;
using Identity.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

[assembly: MedClinicModule(typeof(Identity.IdentityModule), order: 50)]

namespace Identity;

public sealed class IdentityModule : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityModuleDbContext>((sp, options) =>
            options.UseNpgsql(
                configuration["ConnectionStrings:DefaultConnection"],
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "identity")));

        services.AddIdentityCore<ClinicUser>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
        })
        .AddRoles<ClinicRole>()
        .AddEntityFrameworkStores<IdentityModuleDbContext>()
        .AddDefaultTokenProviders();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = configuration["Jwt:Audience"],
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(configuration["Jwt:Secret"]!)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission, policy =>
                    policy.RequireClaim("permissions", permission));
            }
        });

        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("auth", window =>
            {
                window.Window = TimeSpan.FromMinutes(1);
                window.PermitLimit = 10;
                window.QueueLimit = 0;
            });
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddValidatorsFromAssemblyContaining<LoginValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        LoginEndpoint.Map(app);
        RegisterUserEndpoint.Map(app);
        GetCurrentUserEndpoint.Map(app);
    }
}
