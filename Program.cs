using AeroChat.Hubs;
using AeroChat.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

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

var safeContentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
foreach (var ext in new[]
{
    ".html", ".htm", ".shtml", ".xhtml", ".svg", ".svgz", ".xml", ".xsl", ".xslt",
    ".js", ".mjs", ".json", ".php", ".phtml", ".asp", ".aspx", ".jsp", ".cgi",
    ".pl", ".py", ".rb", ".sh", ".bash", ".zsh", ".bat", ".cmd", ".ps1", ".vbs",
    ".jsx", ".ts", ".wasm", ".jar", ".swf", ".exe", ".msi", ".dll", ".so", ".dylib", ".apk"
})
{
    safeContentTypes.Mappings[ext] = "application/octet-stream";
}

app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = safeContentTypes });
app.UseRouting();
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<ChatHub>("/chatHub");

app.Run();
