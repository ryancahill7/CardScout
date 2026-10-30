using CardScout;
using CardScout.Core.Comps;
using CardScout.Core.Data;
using CardScout.Core.Ebay;
Oracle.ManagedDataAccess.Client.OracleConfiguration.BindByName = true;
var builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration;

var oracleConnString = config.GetConnectionString("Oracle")!;

builder.Services.AddSingleton(new EbayListingsRepository(oracleConnString));
builder.Services.AddSingleton(new CompCacheRepository(oracleConnString));
builder.Services.AddSingleton(new DealsRepository(oracleConnString));
builder.Services.AddSingleton(new WatchedPlayersRepository(oracleConnString));
builder.Services.AddSingleton(new RecentListingsRepository(oracleConnString));

builder.Services.AddSingleton(new EbayAuthClient(new HttpClient(), config["Ebay:AppId"]!, config["Ebay:CertId"]!));
builder.Services.AddSingleton(sp => new EbaySearchClient(new HttpClient(), sp.GetRequiredService<EbayAuthClient>()));

builder.Services.AddSingleton(new CompsClient(new HttpClient(), config["SportsCardsPro:Token"]!));
builder.Services.AddHostedService<AvailabilityCheckWorker>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<MatcherService>();
builder.Services.AddHostedService<BroadSweepWorker>();
builder.Services.AddHostedService<RecentListingsWorker>();

var host = builder.Build();
host.Run();