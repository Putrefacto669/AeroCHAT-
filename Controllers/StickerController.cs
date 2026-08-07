using Microsoft.AspNetCore.Mvc;

namespace AeroChat.Controllers;

public class StickerController : Controller
{
    private readonly IWebHostEnvironment _env;

    public StickerController(IWebHostEnvironment env) => _env = env;

    private string? CurrentUserId => HttpContext.Session.GetString("UserId");

    [HttpGet]
    public IActionResult List()
    {
        if (CurrentUserId == null) return Unauthorized();

        var dir = Path.Combine(_env.WebRootPath, "stickers");
        var list = new List<object>();
        if (Directory.Exists(dir))
        {
            foreach (var f in Directory.GetFiles(dir)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".webp" or ".gif")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(f);
                var ext = Path.GetExtension(f).ToLowerInvariant();
                list.Add(new
                {
                    path = "/stickers/" + name,
                    name,
                    animated = ext == ".gif" || ext == ".webp"
                });
            }
        }
        return Json(list);
    }
}
