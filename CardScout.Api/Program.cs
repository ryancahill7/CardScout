using CardScout.Api;
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
    config["Anthropic:Effort"] ?? "medium"));

var imageRoot = Path.Combine(builder.Environment.ContentRootPath, config["Images:Root"] ?? "card-images");
builder.Services.AddSingleton<IImageStorage>(new LocalDiskImageStorage(imageRoot));

// Two ~1600px photos are well under this; the cap just stops runaway uploads
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 25 * 1024 * 1024);

builder.Services.AddMemoryCache();
builder.Services.AddScoped<CurrentUser>();

builder.Services.AddCors(o => o.AddPolicy("Development", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseCors("Development");

app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/", () => Results.Ok(new { service = "CardScout.Api", status = "ok" }));
app.MapCardEndpoints();
app.MapLotEndpoints();
app.MapWatchlistEndpoints();
app.MapPortfolioEndpoints();
app.MapBillingEndpoints();

app.Run();
