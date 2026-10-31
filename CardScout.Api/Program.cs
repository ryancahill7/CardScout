using System.Threading.RateLimiting;
using CardScout.Api;
using CardScout.Api.Auth;
using CardScout.Api.Endpoints;
using CardScout.Core.Billing;
using CardScout.Core.Comps;
using CardScout.Core.Data;
using CardScout.Core.Ebay;
using CardScout.Core.Flip;
using CardScout.Core.Identify;
using CardScout.Core.Images;
using CardScout.Core.Notifications;
using CardScout.Core.Pricing;
using CardScout.Core.Usage;
Oracle.ManagedDataAccess.Client.OracleConfiguration.BindByName = true;
var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

var oracleConnString = config.GetConnectionString("Oracle")!;

// dotnet run --project CardScout.Api -- create-user --name "Jane" --email jane@example.com
// Creates a user and prints their API key once (only its hash is stored).
if (args.Length > 0 && args[0] == "create-user")
{
    string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();

    var apiKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var userId = await new CardUsersRepository(oracleConnString).CreateAsync(Arg("--name"), Arg("--email"), apiKey);

    Console.WriteLine($"Created user {userId}.");
    Console.WriteLine($"API key (shown once, put it in mobile/.env as EXPO_PUBLIC_API_KEY): {apiKey}");
    return;
}

builder.Services.AddSingleton(new CardUsersRepository(oracleConnString));
builder.Services.AddSingleton(new CardInventoryRepository(oracleConnString));
builder.Services.AddSingleton(new CompCacheRepository(oracleConnString));
builder.Services.AddSingleton(new WatchlistRepository(oracleConnString));
builder.Services.AddSingleton(new PushTokenRepository(oracleConnString));
builder.Services.AddSingleton(new PriceHistoryRepository(oracleConnString));
builder.Services.AddSingleton(new DealsRepository(oracleConnString));
builder.Services.AddSingleton(new SubscriptionRepository(oracleConnString));

// Stripe billing. Works without keys (everyone is on the free plan and checkout returns 503).
builder.Services.AddSingleton(config.GetSection("Stripe").Get<StripeOptions>() ?? new StripeOptions());
builder.Services.AddSingleton<ISubscriptionService, SubscriptionService>();
builder.Services.AddSingleton<StripeBillingService>();
builder.Services.AddSingleton(new AiUsageRepository(oracleConnString));

builder.Services.AddSingleton(new EbayAuthClient(new HttpClient(), config["Ebay:AppId"]!, config["Ebay:CertId"]!));
builder.Services.AddSingleton(sp => new EbaySearchClient(new HttpClient(), sp.GetRequiredService<EbayAuthClient>()));
builder.Services.AddSingleton<MarketSnapshotService>();
builder.Services.AddSingleton(sp => new ExpoPushClient(
    new HttpClient(), sp.GetRequiredService<PushTokenRepository>(), sp.GetRequiredService<ILogger<ExpoPushClient>>()));

builder.Services.AddSingleton(new CompsClient(new HttpClient(), config["SportsCardsPro:Token"]!));
builder.Services.AddSingleton<CardPricingService>();
builder.Services.AddSingleton(new CardIdentifier(
    config["Anthropic:ApiKey"]!,
    config["Anthropic:Model"]!,
    config["Anthropic:Effort"] ?? "medium",
    config["Anthropic:FreeModel"]));

// Plans, AI metering and the free plan's monthly cap (Billing:*, AnthropicPricing:*)
builder.Services.AddSingleton(PlanCatalog.FromConfiguration(config));
builder.Services.AddSingleton(AiCostCalculator.FromConfiguration(config));
builder.Services.AddSingleton(config.GetSection("Billing").Get<UsageOptions>() ?? new UsageOptions());
builder.Services.AddSingleton<UsageMeter>();

var imageRoot = Path.Combine(builder.Environment.ContentRootPath, config["Images:Root"] ?? "card-images");
builder.Services.AddSingleton<IImageStorage>(new LocalDiskImageStorage(imageRoot));

// Two ~1600px photos are well under this; the cap just stops runaway uploads
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 25 * 1024 * 1024);

builder.Services.AddMemoryCache();
builder.Services.AddScoped<CurrentUser>();

// ---- Sign-in (see CardScout.Api/Auth and Endpoints/AuthEndpoints.cs) ----
builder.Services.Configure<AuthOptions>(config.GetSection("Auth"));
builder.Services.AddSingleton(new UserAuthRepository(oracleConnString));
builder.Services.AddSingleton(new RefreshTokenRepository(oracleConnString));
builder.Services.AddSingleton(new PasswordResetRepository(oracleConnString));
builder.Services.AddSingleton(new AuthFlowRepository(oracleConnString));
builder.Services.AddSingleton(new AccountDeletionRepository(oracleConnString));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<PasswordResetService>();
// Resend when Email:Resend:ApiKey is set; otherwise emails (reset links) are only logged in Development
if (!string.IsNullOrWhiteSpace(config["Email:Resend:ApiKey"]))
    builder.Services.AddSingleton<IEmailSender>(sp => new ResendEmailSender(new HttpClient(), config["Email:Resend:ApiKey"]!,
        config["Email:From"] ?? "CardScout <onboarding@resend.dev>", sp.GetRequiredService<ILogger<ResendEmailSender>>()));
else
    builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
var authHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
builder.Services.AddSingleton(new ExternalTokenValidator(authHttp));
builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<BrokeredSignInService>(sp, authHttp));

// Per-IP limits on the anonymous auth endpoints; per-account lockout is in AuthService
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many attempts. Wait a minute and try again." }, ct);
    };
    string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    o.AddPolicy(AuthEndpoints.RateLimitPolicy, c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Sign-up and password reset: fewer, slower (they create accounts / send email)
    o.AddPolicy(AuthEndpoints.StrictRateLimitPolicy, c => RateLimitPartition.GetTokenBucketLimiter(Ip(c),
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

builder.Services.AddCors(o => o.AddPolicy("Development", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseCors("Development");

// Fail at startup, not on the first request, when the JWT signing key is missing or weak
app.Services.GetRequiredService<TokenService>();

app.UseRateLimiter();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/", () => Results.Ok(new { service = "CardScout.Api", status = "ok" }));
app.MapAuthEndpoints();
app.MapCardEndpoints();
app.MapLotEndpoints();
app.MapWatchlistEndpoints();
app.MapPortfolioEndpoints();
app.MapBillingEndpoints();
app.MapUsageEndpoints();

app.Run();
