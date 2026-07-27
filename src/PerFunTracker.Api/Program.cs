using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PerFunTracker.Api.BackgroundServices;
using PerFunTracker.Api.Endpoints;
using PerFunTracker.Core.Options;
using PerFunTracker.Infrastructure;
using PerFunTracker.Infrastructure.Data;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// JWT authentication
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtOptions.Key)),
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireClaim("is_admin", "true"));
});

// CORS for the React SPA
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials()
              .WithOrigins("http://localhost:5173", "http://localhost:3000"));
});

// Rate limiting: strict fixed-window limiter on auth endpoints (brute-force protection)
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddHealthChecks();
builder.Services.AddHostedService<EmailNotificationWorker>();
builder.Services.AddHostedService<RefreshTokenCleanupService>();

var app = builder.Build();

app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGet("/", () => "PerFunTracker API");
app.MapAuthEndpoints();
app.MapEventEndpoints();
app.MapUserEndpoints();
app.MapCategoryEndpoints();
app.MapAdminEndpoints();

// Auto-apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();
