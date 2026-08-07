using AeroChat.Hubs;
using AeroChat.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

var redis = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrEmpty(redis))
    builder.Services.AddSignalR();
else
    builder.Services.AddSignalR().AddStackExchangeRedis(redis);

builder.Services.AddSingleton<IFileStorage>(sp => new LocalFileStorage(
    sp.GetRequiredService<IWebHostEnvironment>().WebRootPath,
    builder.Configuration["Storage:PublicBaseUrl"] ?? ""));
builder.Services.AddSingleton<DataService>();
builder.Services.AddHostedService<StatusCleanupService>();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(1);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = new AeroChat.Services.SaferContentTypeProvider() });
app.UseRouting();
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<ChatHub>("/chatHub");

app.Run();
