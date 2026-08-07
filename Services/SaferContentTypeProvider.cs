using Microsoft.AspNetCore.StaticFiles;

namespace AeroChat.Services;

public sealed class SaferContentTypeProvider : IContentTypeProvider
{
    private readonly FileExtensionContentTypeProvider _app = new();
    private readonly FileExtensionContentTypeProvider _uploads = new();

    public SaferContentTypeProvider()
    {
        foreach (var ext in new[]
        {
            ".html", ".htm", ".shtml", ".xhtml", ".svg", ".svgz", ".xml", ".xsl", ".xslt",
            ".js", ".mjs", ".json", ".php", ".phtml", ".asp", ".aspx", ".jsp", ".cgi",
            ".pl", ".py", ".rb", ".sh", ".bash", ".zsh", ".bat", ".cmd", ".ps1", ".vbs",
            ".jsx", ".ts", ".wasm", ".jar", ".swf", ".exe", ".msi", ".dll", ".so", ".dylib", ".apk"
        })
        {
            _uploads.Mappings[ext] = "application/octet-stream";
        }
    }

    public bool TryGetContentType(string subPath, out string? contentType)
    {
        if (subPath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            return _uploads.TryGetContentType(subPath, out contentType);
        return _app.TryGetContentType(subPath, out contentType);
    }
}
