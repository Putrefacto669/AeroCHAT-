using AeroChat.Hubs;
using AeroChat.Models;
using AeroChat.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AeroChat.Controllers;

public class StatusController : Controller
{
    private readonly DataService _data;
    private readonly IHubContext<ChatHub> _hub;
    private readonly IFileStorage _storage;
    private const long MaxImageSize = 20 * 1024 * 1024;

    public StatusController(DataService data, IHubContext<ChatHub> hub, IFileStorage storage)
    {
        _data = data;
        _hub = hub;
        _storage = storage;
    }

    private string? CurrentUserId => HttpContext.Session.GetString("UserId");

    private IActionResult? Auth()
    {
        if (CurrentUserId == null) return RedirectToAction("Index", "Home");
        return null;
    }

    public IActionResult Index(string? u)
    {
        var a = Auth(); if (a != null) return a;
        var current = _data.GetUserById(CurrentUserId!);
        if (current == null) return RedirectToAction("Logout", "Home");

        ViewBag.SidebarUser = current;
        ViewBag.SidebarItems = _data.GetSidebarItems(current.Id);
        ViewBag.SidebarGroups = _data.GetGroupsForUser(current.Id);
        ViewBag.SidebarActiveId = null;

        var statuses = _data.GetVisibleStatuses(current.Id);
        var friends = _data.GetFriendIds(current.Id)
            .Select(id => _data.GetUserById(id))
            .Where(x => x != null)
            .Select(x => x!)
            .ToList();

        var startUser = string.IsNullOrEmpty(u) ? statuses.FirstOrDefault()?.UserId : u;
        if (string.IsNullOrEmpty(startUser)) startUser = current.Id;

        ViewBag.StatusJson = System.Text.Json.JsonSerializer.Serialize(statuses.Select(s => new
        {
            id = s.Id,
            userId = s.UserId,
            userName = s.UserName,
            userColor = s.UserColor,
            userAvatar = s.UserAvatar,
            content = s.Content,
            type = s.Type.ToString().ToLower(),
            filePath = s.FilePath,
            createdAt = s.CreatedAt
        }));
        ViewBag.StartUser = startUser;
        ViewBag.FriendsJson = System.Text.Json.JsonSerializer.Serialize(friends.Select(f => new
        {
            id = f.Id,
            displayName = f.DisplayName,
            avatarColor = f.AvatarColor,
            avatarPath = f.AvatarPath
        }));
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create(string content, IFormFile? image)
    {
        var a = Auth(); if (a != null) return a;
        var me = _data.GetUserById(CurrentUserId!);
        if (me == null) return RedirectToAction("Logout", "Home");

        var status = new Status
        {
            UserId = me.Id,
            UserName = me.DisplayName,
            UserColor = me.AvatarColor,
            UserAvatar = me.AvatarPath,
            Content = content?.Trim() ?? ""
        };

        if (image != null && image.Length > 0)
        {
            if (image.Length > MaxImageSize)
            {
                TempData["Error"] = "La imagen supera el límite de 20 MB.";
                return RedirectToAction("Index");
            }
            var ext = Path.GetExtension(image.FileName).ToLower();
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".webp"))
            {
                TempData["Error"] = "Formato de imagen no válido.";
                return RedirectToAction("Index");
            }

            var head = new byte[16];
            await using (var src = image.OpenReadStream())
            {
                var n = await src.ReadAsync(head, 0, head.Length);
                if (n == 0 || !UploadValidation.HasValidImageSignature(ext, head.AsSpan(0, n).ToArray()))
                {
                    TempData["Error"] = "El archivo no es una imagen válida.";
                    return RedirectToAction("Index");
                }
            }

            var filePath = await _storage.SaveAsync(image, "statuses");
            if (filePath == null)
            {
                TempData["Error"] = "No se pudo guardar la imagen.";
                return RedirectToAction("Index");
            }

            status.Type = StatusType.Image;
            status.FilePath = filePath;
            status.FileName = image.FileName;
        }

        if (status.Type == StatusType.Text && string.IsNullOrEmpty(status.Content))
        {
            TempData["Error"] = "Escribí algo para tu estado.";
            return RedirectToAction("Index");
        }

        _data.AddStatus(status);
        await ChatHub.NotifyUsers(_hub, _data.GetFriendIds(me.Id), "StatusChanged", me.DisplayName);

        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Delete(string id)
    {
        var a = Auth(); if (a != null) return a;
        var me = _data.GetUserById(CurrentUserId!);
        if (me == null) return RedirectToAction("Logout", "Home");
        _data.DeleteStatus(me.Id, id);
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Summary()
    {
        var uid = CurrentUserId;
        if (uid == null) return Unauthorized();
        var me = _data.GetUserById(uid);
        if (me == null) return Unauthorized();

        var visible = _data.GetVisibleStatuses(uid);
        var mine = visible.Where(s => s.UserId == uid).ToList();
        var friendStatuses = visible.Where(s => s.UserId != uid)
            .GroupBy(s => s.UserId)
            .Select(g =>
            {
                var last = g.OrderByDescending(s => s.CreatedAt).First();
                var user = _data.GetUserById(g.Key);
                return new
                {
                    userId = g.Key,
                    name = last.UserName,
                    color = last.UserColor,
                    avatar = last.UserAvatar,
                    count = g.Count(),
                    lastTime = last.CreatedAt,
                    preview = last.Type == StatusType.Text ? last.Content : "📷 Foto"
                };
            })
            .OrderByDescending(x => x.lastTime)
            .ToList();

        return Json(new
        {
            me = new
            {
                hasStatus = mine.Any(),
                name = me.DisplayName,
                color = me.AvatarColor,
                avatar = me.AvatarPath
            },
            friends = friendStatuses
        });
    }
}
