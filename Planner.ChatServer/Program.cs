using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Planner.Chat;
using Planner.ChatServer;

var builder = WebApplication.CreateBuilder(args);

var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
var paths = new ChatServerPaths(dataDir);
builder.Services.AddSingleton(paths);

var httpPort = HttpsSetup.HttpPortFromUrls(builder.Configuration["Urls"] ?? ChatRoutes.DefaultListen);
var httpsEnabled = builder.Configuration.GetValue("Https:Enabled", true);
var httpsPort = builder.Configuration.GetValue("Https:Port", ChatRoutes.DefaultHttpsPort);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = ChatRoutes.MaxOrgFileBytes + (1024 * 1024);
    options.ListenAnyIP(httpPort);
    if (httpsEnabled)
    {
        var cert = HttpsSetup.EnsureCertificate(paths.HttpsPfx);
        options.ListenAnyIP(httpsPort, listen => listen.UseHttps(cert));
    }
});

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = ChatRoutes.MaxOrgFileBytes;
    o.ValueLengthLimit = ChatRoutes.MaxOrgFileBytes;
});
builder.Services.Configure<KestrelServerOptions>(o =>
{
    o.Limits.MaxRequestBodySize = ChatRoutes.MaxOrgFileBytes + (1024 * 1024);
});
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var dbPath = Path.Combine(dataDir, "chat.db");
builder.Services.AddDbContextFactory<ChatServerDb>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddSingleton<AdminSessionStore>();

var app = builder.Build();
app.UseForwardedHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

await using (var db = await app.Services.GetRequiredService<IDbContextFactory<ChatServerDb>>().CreateDbContextAsync())
{
    await db.Database.EnsureCreatedAsync();
    await ChatServerMigrator.ApplyAsync(db);
    await AdminSecrets.EnsureAsync(db, app.Configuration);
}

var sessionDays = app.Configuration.GetValue("Chat:SessionDays", 30);
app.MapChatApi(sessionDays);
app.MapOrgApi();
app.MapOrgWorkflowApi();
app.MapAdminApi();
app.MapGet("/admin", () => Results.Redirect("/admin/index.html"));

app.Logger.LogInformation(
    "Yaver sunucusu hazır. HTTP :{Http}  HTTPS :{Https}  yönetim /admin  (protokol v{Version}). Üretimde reverse proxy ile TLS sonlandırın; yönetici şifresini panelden değiştirin.",
    httpPort,
    httpsEnabled ? httpsPort : 0,
    ChatRoutes.ProtocolVersion);

app.Run();
