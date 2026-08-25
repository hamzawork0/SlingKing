using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using SlingKing.Api.Middleware;
using SlingKing.Application.Auth;
using SlingKing.Infrastructure;
using SlingKing.Infrastructure.Auth;
using SlingKing.Infrastructure.Configuration;
using SlingKing.Infrastructure.Persistence;
using SlingKing.Infrastructure.Persistence.Seed;

// backend/.env, two directories up from src/SlingKing.Api. Absent in production, where the
// host supplies the same names as real environment variables; EnvFile never overwrites one
// that is already set, so a deployed value always wins.
EnvFile.Load(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env"));

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/slingking-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

// Every one of these is fatal when missing. There is no fallback — not even a development
// one, because a development fallback is exactly how a signing key of "secret" reaches
// production and how CORS quietly opens to the world.
string Required(string key) =>
    Environment.GetEnvironmentVariable(key)
    ?? throw new InvalidOperationException($"{key} is not set. See backend/.env.example.");

const int accessTokenMinutes = 30;
const int refreshTokenDays = 14;

builder.Services.Configure<JwtOptions>(options =>
{
    options.Secret = Required("SLINGKING_JWT_SECRET");
    options.Issuer = Required("SLINGKING_JWT_ISSUER");
    options.Audience = Required("SLINGKING_JWT_AUDIENCE");
    options.AccessTokenMinutes = accessTokenMinutes;
    options.RefreshTokenDays = refreshTokenDays;
});

builder.Services.AddInfrastructure(Required("SLINGKING_DB_RUNTIME"));

builder.Services.AddSingleton(new AuthOptions
{
    AccessTokenMinutes = accessTokenMinutes,
    RefreshTokenDays = refreshTokenDays
});
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Required("SLINGKING_JWT_ISSUER"),
            ValidAudience = Required("SLINGKING_JWT_AUDIENCE"),
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(Required("SLINGKING_JWT_SECRET"))),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

var corsOrigins = Required("SLINGKING_CORS_ORIGINS")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "SlingKing API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme, Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// First in the pipeline, so nothing downstream can hand the client an exception. In
// Development the framework also inserts its own developer exception page ahead of this
// one, but it never fires: this handler returns true, so the exception stops here.
app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await services.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

app.Run();

/// <summary>Exposed so WebApplicationFactory can boot the real host in tests.</summary>
public partial class Program
{
}
