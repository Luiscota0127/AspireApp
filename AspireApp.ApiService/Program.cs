using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using AspireApp.ApiService.Data;
using AspireApp.ApiService.Models;
using AspireApp.ApiService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Configuration
var configuration = builder.Configuration;

// EF Core - use SQLite for normal runs, but allow InMemory for tests (Environment=Testing)
if (builder.Environment.EnvironmentName == "Testing")
{
    // Use InMemory provider for tests. Requires Microsoft.EntityFrameworkCore.InMemory package.
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseInMemoryDatabase("AspireApp_Tests"));
}
else
{
    // SQLite for local development
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));
}

// Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Authentication - JWT
var jwtSection = configuration.GetSection("Jwt");
var jwtKey = jwtSection.GetValue<string>("Key") ?? "ReplaceThisWithASecretKeyForDev";
var issuer = jwtSection.GetValue<string>("Issuer") ?? "AspireApp";
var audience = jwtSection.GetValue<string>("Audience") ?? "AspireAppClients";

// derive a fixed-length signing key bytes to ensure compatibility between token creation and validation
byte[] jwtKeyRaw = Encoding.UTF8.GetBytes(jwtKey);
byte[] jwtKeyBytes;
if (jwtKeyRaw.Length < 32)
{
    using var sha = System.Security.Cryptography.SHA256.Create();
    jwtKeyBytes = sha.ComputeHash(jwtKeyRaw);
}
else
{
    jwtKeyBytes = jwtKeyRaw;
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            // Use the derived jwtKeyBytes computed earlier and set as issuer signing key
            IssuerSigningKey = new SymmetricSecurityKey(jwtKeyBytes),
        };
        // Allow JWTs to be passed to SignalR hubs via the query string (access_token)
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"].FirstOrDefault();
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<ITokenService, TokenService>();
// SignalR
builder.Services.AddSignalR();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Testing")
{
    // expose detailed errors while developing or running integration tests
    app.UseDeveloperExceptionPage();
    app.MapOpenApi();
    app.UseSwagger();   // Serves generated JSON
    app.UseSwaggerUI(); // Serves Swagger UI page
}
else
{
    app.UseExceptionHandler();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AspireApp.ApiService.Hubs.CollaborationHub>("/hubs/collab");
app.MapDefaultEndpoints();

// Ensure database is created and apply migrations at startup (development)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        // Only apply migrations for relational providers (SQLite etc.). In-memory provider doesn't support Migrate().
        try
        {
            if (db.Database.IsRelational())
            {
                db.Database.Migrate();
            }
        }
        catch (Exception ex)
        {
            // If migration fails in non-relational scenarios, log and continue. Tests use InMemory provider.
            var loggerLocal = services.GetRequiredService<ILogger<Program>>();
            loggerLocal.LogInformation(ex, "Skipping migrations (non-relational provider or migration error)");
        }
        // Seed default data (admin user etc.)
        SeedData.EnsureSeedDataAsync(services).GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred migrating or initializing the database.");
    }
}

app.Run();

