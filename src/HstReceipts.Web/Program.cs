using HstReceipts.Core.Interfaces;
using HstReceipts.Infrastructure;
using HstReceipts.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Local secrets (gitignored). Copy from appsettings.Development.local.json.example.
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddInfrastructure(builder.Configuration);

// Keys live only in process memory, so auth cookies become invalid when the app restarts.
builder.Services.AddDataProtection()
    .UseEphemeralDataProtectionProvider();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/client/login";
        options.AccessDeniedPath = "/client/login";
        options.LogoutPath = "/api/auth/logout";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 104_857_600;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        db.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully.");

        var auth = scope.ServiceProvider.GetRequiredService<IUserAuthService>();
        await auth.EnsureSeedUsersAsync();
        logger.LogInformation("Seed users checked from configuration.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(
            ex,
            "Could not apply database migrations or seed users. Fix Database:Provider / DefaultConnection before using login or save.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.Redirect("/client/");
            await Task.CompletedTask;
        });
    });
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Receipts}/{action=Index}/{id?}");

// React SPA (built into wwwroot/client). Deep links fall back to index.html.
app.MapFallbackToFile("/client/{**path}", "client/index.html");

// Root URL starts a fresh sign-in (clears cookie + session preview).
app.MapGet("/", async (HttpContext context) =>
{
    await context.Session.LoadAsync();
    context.Session.Clear();
    await context.Session.CommitAsync();
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/client/login");
});

app.Run();
