using CardScout;
using CardScout.Core.Comps;
using CardScout.Core.Data;
using CardScout.Core.Ebay;
using CardScout.Core.Flip;
using CardScout.Core.Notifications;
using CardScout.Core.Pricing;
Oracle.ManagedDataAccess.Client.OracleConfiguration.BindByName = true;
var builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration;

var oracleConnString = config.GetConnectionString("Oracle")!;

builder.Services.AddSingleton(new EbayListingsRepository(oracleConnString));
builder.Services.AddSingleton(new CompCacheRepository(oracleConnString));
builder.Services.AddSingleton(new DealsRepository(oracleConnString));
builder.Services.AddSingleton(new WatchedPlayersRepository(oracleConnString));
builder.Services.AddSingleton(new RecentListingsRepository(oracleConnString));
builder.Services.AddSingleton(new CardUsersRepository(oracleConnString));
builder.Services.AddSingleton(new CardInventoryRepository(oracleConnString));
builder.Services.AddSingleton(new WatchlistRepository(oracleConnString));
builder.Services.AddSingleton(new PushTokenRepository(oracleConnString));
builder.Services.AddSingleton(new PriceHistoryRepository(oracleConnString));

builder.Services.AddSingleton(new EbayAuthClient(new HttpClient(), config["Ebay:AppId"]!, config["Ebay:CertId"]!));
builder.Services.AddSingleton(sp => new EbaySearchClient(new HttpClient(), sp.GetRequiredService<EbayAuthClient>()));

builder.Services.AddSingleton(new CompsClient(new HttpClient(), config["SportsCardsPro:Token"]!));
builder.Services.AddSingleton<ListingEvaluator>();
builder.Services.AddSingleton<CardPricingService>();
builder.Services.AddSingleton(sp => new ExpoPushClient(
    new HttpClient(), sp.GetRequiredService<PushTokenRepository>(), sp.GetRequiredService<ILogger<ExpoPushClient>>()));

builder.Services.AddHostedService<AvailabilityCheckWorker>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<MatcherService>();
builder.Services.AddHostedService<BroadSweepWorker>();
builder.Services.AddHostedService<TcgSweepWorker>();
builder.Services.AddHostedService<RecentListingsWorker>();
builder.Services.AddHostedService<WatchlistWorker>();
builder.Services.AddHostedService<PortfolioPriceWorker>();

var host = builder.Build();
host.Run();
