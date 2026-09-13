using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using SlingKing.Api.Controllers;
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

// Guessing a password is the only unauthenticated thing worth doing to this API, so the
// credential endpoints are the only ones that need a brake. Ten a minute per address is far
// above what a person types and far below what a guesser needs; bcrypt at work factor 12 is
// otherwise the only cost, and that is a CPU-exhaustion vector as much as a defence.
var loginAttemptsPerMinute =
    int.TryParse(Environment.GetEnvironmentVariable("SLINGKING_LOGIN_ATTEMPTS_PER_MINUTE"), out var attempts)
        ? attempts
        : 10;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthController.CredentialsRateLimitPolicy, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            // ponytail: partitions on the socket address. Behind a reverse proxy that is the
            // proxy's IP and every visitor shares one bucket — add UseForwardedHeaders when
            // this deploys behind one, or the limit becomes a self-inflicted outage.
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginAttemptsPerMinute,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    // System.Text.Json names the target CLR type when a body will not bind
    // ("could not be converted to SlingKing.Api.Contracts.Auth.LoginRequest"), which hands
    // the client our internal namespace. Field-level messages are ours and are safe to keep;
    // the deserialiser's are keyed by JSON path and get replaced wholesale.
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState.ToDictionary(
            entry => entry.Key,
            entry => entry.Key.StartsWith('$')
                ? ["The request body is not valid JSON."]
                : entry.Value!.Errors.Select(error => error.ErrorMessage).ToArray());

        return new BadRequestObjectResult(new ValidationProblemDetails(errors))
        {
            ContentTypes = { "application/problem+json" }
        };
    };
})
    // Global, not a per-property [JsonConverter]: no existing response carries an enum, so
    // nothing else changes shape today, and a per-property attribute would just leave the
    // next enum to rediscover that the wire format needs to be a name, not a number.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

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

// Render terminates TLS and proxies to this container, so every request arrives carrying the
// proxy's address. The rate limiter partitions on the client IP, so without this the whole
// internet shares one bucket of login attempts and one person's typos lock everyone else out.
//
// Clearing KnownNetworks and KnownProxies is load-bearing: by default the middleware trusts
// only loopback proxies, so behind Render it would ignore these headers and change NOTHING —
// installed, running, and silently useless. We cannot know the proxy's address in advance,
// and only Render can reach this container, so trusting the hop it adds is the available
// option.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// First in the pipeline on purpose: everything downstream that reads the client's address or
// the request scheme — the rate limiter, the request log — must see the real values rather
// than the proxy's.
app.UseForwardedHeaders();

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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Migrations run over SLINGKING_DB_MIGRATION, never the runtime connection. Applying DDL
// needs session-level advisory locks and prepared statements; the transaction pooler on 6543
// multiplexes connections per statement and provides neither. This currently appears to work
// over the runtime connection only because every migration is already applied, so MigrateAsync
// never has to take the lock — the first cold deploy with a pending migration is where that
// silence would end. The throwaway context is disposed before the app serves anything, so the
// session-pooler connection is not held for the process lifetime.
await using (var migrationDb = new AppDbContext(
    new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(
            Required("SLINGKING_DB_MIGRATION"),
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
        .UseSnakeCaseNamingConvention()
        .Options))
{
    await migrationDb.Database.MigrateAsync();
}

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

app.Run();

/// <summary>Exposed so WebApplicationFactory can boot the real host in tests.</summary>
public partial class Program
{
}
