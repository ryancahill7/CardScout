using CardScout.Core.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

var oracleConnString = builder.Configuration.GetConnectionString("Oracle")!;
builder.Services.AddSingleton(new DealsRepository(oracleConnString));
builder.Services.AddSingleton(new RecentListingsRepository(oracleConnString));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/Deals")); // add this line
app.MapRazorPages();

app.Run();