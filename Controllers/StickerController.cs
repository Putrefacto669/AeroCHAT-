using System.Text.Json;
using System.Text.RegularExpressions;
using AeroChat.Services;
using Microsoft.AspNetCore.Mvc;

namespace AeroChat.Controllers;

public class StickerController : Controller
{
    private readonly IWebHostEnvironment _env;
    private readonly HttpClient _http;
    private readonly DataService _data;
    private const string PackApi = "https://api.sticker.ly/v3.1/stickerPack/{0}";
    private const string ApiUserAgent = "androidapp.stickerly/1.13.3 (G011A; U; Android 22; pt-BR; br;)";
    private const int MaxStickers = 100;
    private const int MaxStickerBytes = 3 * 1024 * 1024;

    public StickerController(IWebHostEnvironment env, HttpClient http, DataService data)
    {
        _env = env;
        _http = http;
        _data = data;
    }

    private string? CurrentUserId => HttpContext.Session.GetString("UserId");

    private IActionResult? Auth()
    {
        if (CurrentUserId == null) return Json(new { ok = false, message = "No autorizado" });
        return null;
    }

    [HttpGet]
    public IActionResult List()
    {
        var a = Auth(); if (a != null) return a;
        var uid = CurrentUserId!;
        var lib = _data.GetStickerLibrary(uid);
        var baseDir = Path.Combine(_env.WebRootPath, "stickers", uid);
        var packs = new List<object>();

        if (Directory.Exists(baseDir))
        {
            foreach (var packDir in Directory.GetDirectories(baseDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var packId = Path.GetFileName(packDir);
                var stickers = new List<object>();
                foreach (var f in Directory.GetFiles(packDir)
                    .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".webp" or ".gif")
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var rel = "/" + Path.GetRelativePath(_env.WebRootPath, f).Replace('\\', '/');
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    stickers.Add(new
                    {
                        path = rel,
                        name = Path.GetFileName(f),
                        animated = ext is ".gif" or ".webp",
                        fav = lib.Favorites.Contains(rel),
                        uses = lib.Usage.TryGetValue(rel, out var u) ? u : 0
                    });
                }
                if (stickers.Count > 0)
                {
                    packs.Add(new
                    {
                        packId,
                        name = lib.PackNames.TryGetValue(packId, out var pn) && !string.IsNullOrEmpty(pn) ? pn : packId,
                        stickers
                    });
                }
            }
        }
        return Json(packs);
    }

    [HttpPost]
    public IActionResult Favorite(string path)
    {
        var a = Auth(); if (a != null) return a;
        var uid = CurrentUserId!;
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/stickers/" + uid + "/"))
            return Json(new { ok = false });
        var fav = _data.ToggleFavorite(uid, path);
        return Json(new { ok = true, fav });
    }

    [HttpPost]
    public async Task<IActionResult> Import(string url)
    {
        var a = Auth(); if (a != null) return a;
        var uid = CurrentUserId!;

        var packId = ExtractPackId(url);
        if (packId == null)
            return Json(new { ok = false, message = "Link de sticker.ly no reconocido" });

        using var req = new HttpRequestMessage(HttpMethod.Get, string.Format(PackApi, packId));
        req.Headers.Add("User-Agent", ApiUserAgent);
        req.Headers.Add("Accept", "application/json");

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req);
        }
        catch
        {
            return Json(new { ok = false, message = "No se pudo contactar sticker.ly" });
        }

        using (res)
        {
            if (!res.IsSuccessStatusCode)
                return Json(new { ok = false, message = "El paquete no existe (" + (int)res.StatusCode + ")" });

            JsonDocument doc;
            try
            {
                using var stream = await res.Content.ReadAsStreamAsync();
                doc = JsonDocument.Parse(stream);
            }
            catch
            {
                return Json(new { ok = false, message = "Respuesta de sticker.ly inválida" });
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("result", out var result))
                    return Json(new { ok = false, message = "Respuesta de sticker.ly sin datos" });

                var name = result.TryGetProperty("name", out var n) ? n.GetString() ?? packId : packId;
                var author = result.TryGetProperty("authorName", out var au) ? au.GetString() ?? "" : "";
                var prefix = result.TryGetProperty("resourceUrlPrefix", out var p) ? p.GetString() : null;
                if (string.IsNullOrEmpty(prefix))
                    return Json(new { ok = false, message = "Paquete sin archivos" });

                if (!result.TryGetProperty("stickers", out var stickers) || stickers.ValueKind != JsonValueKind.Array)
                    return Json(new { ok = false, message = "Paquete sin stickers" });

                if (stickers.GetArrayLength() == 0 || stickers.GetArrayLength() > MaxStickers)
                    return Json(new { ok = false, message = "El paquete no tiene stickers válidos" });

                var target = Path.Combine(UserStickerDir(uid), packId);
                Directory.CreateDirectory(target);

                var count = 0;
                var errors = 0;
                foreach (var s in stickers.EnumerateArray())
                {
                    if (count >= MaxStickers) break;
                    if (!s.TryGetProperty("fileName", out var fname)) continue;
                    var fileName = fname.GetString();
                    if (string.IsNullOrEmpty(fileName) || !Regex.IsMatch(fileName, @"^[A-Za-z0-9][A-Za-z0-9._-]{1,63}$"))
                        continue;

                    byte[] bytes;
                    try
                    {
                        using var sr = await _http.GetStreamAsync(prefix + fileName);
                        using var ms = new MemoryStream();
                        await sr.CopyToAsync(ms);
                        bytes = ms.ToArray();
                    }
                    catch
                    {
                        errors++;
                        continue;
                    }

                    if (bytes.Length == 0 || bytes.Length > MaxStickerBytes || !LooksLikeImage(bytes))
                    {
                        errors++;
                        continue;
                    }

                    await System.IO.File.WriteAllBytesAsync(Path.Combine(target, fileName), bytes);
                    count++;
                }

                if (count == 0)
                    return Json(new { ok = false, message = "No se pudo descargar ningún sticker" });

                _data.SetPackName(uid, packId, name);
                return Json(new { ok = true, name, author, count });
            }
        }
    }

    private string UserStickerDir(string userId)
    {
        var dir = Path.Combine(_env.WebRootPath, "stickers", userId);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }

    private static bool LooksLikeImage(byte[] b)
    {
        if (b.Length < 12) return false;
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return true; // PNG
        if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return true; // GIF8
        if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
            b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return true; // RIFF..WEBP
        return false;
    }

    private static string? ExtractPackId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        var m = Regex.Match(url, @"/s/([A-Za-z0-9]{4,12})(?:[?#/]|$)", RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(url, @"/pack/([A-Za-z0-9]{4,12})(?:[?#/]|$)", RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(url, @"^stickerly://[^/\s]*/?([A-Za-z0-9]{4,12})(?:[?#]|$)", RegexOptions.IgnoreCase);
        if (!m.Success && Regex.IsMatch(url, @"^[A-Za-z0-9]{4,12}$"))
            m = Regex.Match(url, @"^([A-Za-z0-9]{4,12})$");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }
}
