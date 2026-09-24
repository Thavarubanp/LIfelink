using System;
using System.Text;
using LifeLink.Data;
using LifeLink.Middleware;
using LifeLink.Services.Auth;
using LifeLink.Services.Common;
using LifeLink.Services.Inventory;
using LifeLink.Services.Emergency;
using LifeLink.Services.Transfer;
using LifeLink.Services.Hospitals;
using LifeLink.Services.Doctors;
using LifeLink.Services.Verification;
using LifeLink.Services.Matching;
using LifeLink.Services.Notification;
using LifeLink.Services.BloodCompatibility;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Acceptances;
using LifeLink.Services.Admin;
using LifeLink.Services.Complaints;
using LifeLink.Services.HospitalActivity;
using LifeLink.Services.Appeals;
using LifeLink.Services.Planning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure PostgreSQL / Neon Database Context
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    }));

// 2. Configure HttpContextAccessor & Services Injection
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPasswordHasherService, PasswordHasherService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IEmailService, MailKitEmailService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Student 3 Services Injection
builder.Services.AddScoped<IBloodInventoryService, BloodInventoryService>();
builder.Services.AddScoped<IEmergencyRequestService, EmergencyRequestService>();
builder.Services.AddScoped<ITransferRequestService, TransferRequestService>();

// Student 2 Services Injection
builder.Services.AddHttpClient<INotificationAgentService, NotificationAgentService>();
builder.Services.AddScoped<IHospitalService, HospitalService>();
builder.Services.AddScoped<IDoctorService, DoctorService>();
builder.Services.AddScoped<INotificationAgentService, NotificationAgentService>();
builder.Services.AddScoped<IVerificationService, VerificationService>();
builder.Services.AddScoped<IMatchingService, MatchingService>();

// Student 1 Services Injection
builder.Services.AddScoped<IBloodCompatibilityService, BloodCompatibilityService>();
builder.Services.AddScoped<IBloodRequestService, BloodRequestService>();
builder.Services.AddScoped<IAcceptanceService, AcceptanceService>();
builder.Services.AddScoped<IRequestExpiryService, RequestExpiryService>();
builder.Services.AddHostedService<RequestExpiryBackgroundService>();

// Student 4 Services Injection (Admin Governance, Compliance & Planning Agent)
builder.Services.AddHttpClient<IPlanningAgentService, PlanningAgentService>();
builder.Services.AddScoped<IAdminNotificationService, AdminNotificationService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IComplaintService, ComplaintService>();
builder.Services.AddScoped<IHospitalActivityService, HospitalActivityService>();
builder.Services.AddScoped<IAppealService, AppealService>();

// 3. Configure JWT Authentication & Authorization
var jwtSettings = builder.Configuration.GetSection("Jwt");
var keyString = jwtSettings["Key"] ?? throw new InvalidOperationException("Jwt:Key is missing from configuration.");
var key = Encoding.UTF8.GetBytes(keyString);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // set to true in production
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"] ?? "LifeLinkAPI",
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"] ?? "LifeLinkApp",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        // End sessions that no longer match the account: a former Admin after an ownership transfer,
        // or a permanently blocked / deleted account. (The frontend signs out on 401.)
        OnTokenValidated = async ctx =>
        {
            var sub = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? ctx.Principal?.FindFirst("sub")?.Value;
            if (!Guid.TryParse(sub, out var userId)) return;

            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var account = await db.Users.AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => new { u.AccountStatus, IsAdmin = u.UserRoles.Any(ur => ur.Role.Name == "Admin") })
                .FirstOrDefaultAsync();

            if (account != null && (account.AccountStatus == LifeLink.Entities.AccountStatus.Blocked || account.AccountStatus == LifeLink.Entities.AccountStatus.Deleted))
            {
                ctx.Fail("This account is no longer active.");
            }
            else if (ctx.Principal!.IsInRole("Admin") && account?.IsAdmin != true)
            {
                ctx.Fail("Admin ownership has changed. Please sign in again.");
            }
        }
    };
});

builder.Services.AddAuthorization();

// 4. Configure Controllers & CORS
builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 5. Configure Swagger / OpenAPI with JWT Authorization Support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LifeLink Core Authentication API",
        Version = "v1",
        Description = "Core Authentication and Authorization Foundation API for LifeLink Platform."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. \r\n\r\n Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\nExample: \"Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...\""
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Automatically apply any pending EF Core database migrations on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

// 6. Request Pipeline Configuration
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "LifeLink Core Auth API v1");
    });
}

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseMiddleware<InternalServiceAuthMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RestrictedGovernanceModeMiddleware>();

app.MapControllers();

app.Run();