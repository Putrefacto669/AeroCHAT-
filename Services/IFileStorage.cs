namespace AeroChat.Services;

public interface IFileStorage
{
    Task<string?> SaveAsync(IFormFile file, string folder);
    void Delete(string? publicUrl);
}

public class LocalFileStorage : IFileStorage
{
    private readonly string _webRoot;
    private readonly string _publicBaseUrl;

    public LocalFileStorage(string webRoot, string publicBaseUrl)
    {
        _webRoot = webRoot;
        _publicBaseUrl = publicBaseUrl?.TrimEnd('/') ?? "";
    }

    public async Task<string?> SaveAsync(IFormFile file, string folder)
    {
        if (file == null || file.Length == 0) return null;
        var ext = Path.GetExtension(file.FileName).ToLower();
        if (string.IsNullOrEmpty(ext)) ext = ".file";
        var name = $"{Guid.NewGuid()}{ext}";
        var dir = Path.Combine(_webRoot, "uploads", folder);
        Directory.CreateDirectory(dir);
        var full = Path.Combine(dir, name);
        await using var stream = new FileStream(full, FileMode.Create);
        await file.CopyToAsync(stream);
        return $"{_publicBaseUrl}/uploads/{folder}/{name}";
    }

    public void Delete(string? publicUrl)
    {
        if (string.IsNullOrWhiteSpace(publicUrl)) return;
        try
        {
            var path = publicUrl;
            if (!string.IsNullOrEmpty(_publicBaseUrl) && path.StartsWith(_publicBaseUrl, StringComparison.OrdinalIgnoreCase))
                path = path.Substring(_publicBaseUrl.Length);

            var full = Path.GetFullPath(Path.Combine(_webRoot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            if (full.StartsWith(_webRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                File.Delete(full);
        }
        catch
        {
            // best effort
        }
    }
}
