using AeroChat.Hubs;
using AeroChat.Models;
using AeroChat.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AeroChat.Controllers;

public class GroupController : Controller
{
    private readonly DataService _data;
    private readonly IFileStorage _storage;
    private readonly IHubContext<ChatHub> _hub;
    private const long MaxFileSize = 20 * 1024 * 1024;
    private const long MaxVideoSize = 100 * 1024 * 1024;
    private const long MaxAvatarSize = 5 * 1024 * 1024;

    public GroupController(DataService data, IFileStorage storage, IHubContext<ChatHub> hub)
    {
        _data = data;
        _storage = storage;
        _hub = hub;
    }

    private string? CurrentUserId => HttpContext.Session.GetString("UserId");

    private IActionResult? Auth()
    {
        if (CurrentUserId == null) return RedirectToAction("Index", "Home");
        return null;
    }

    public IActionResult Conversation(string id)
    {
        var a = Auth(); if (a != null) return a;
        var current = _data.GetUserById(CurrentUserId!);
        var group = _data.GetGroup(id);
        if (current == null || group == null || !group.MemberIds.Contains(current.Id))
            return RedirectToAction("Index", "Chat");

        ViewBag.SidebarUser = current;
        ViewBag.SidebarItems = _data.GetSidebarItems(current.Id);
        ViewBag.SidebarGroups = _data.GetGroupsForUser(current.Id);
        ViewBag.SidebarActiveId = "/Group/Conversation/" + group.Id;
        ViewBag.IsOwner = group.OwnerId == current.Id;

        var vm = new GroupViewModel
        {
            Group = group,
            CurrentUser = current,
            Members = _data.GetGroupMembers(group),
            Messages = _data.GetGroupMessages(group.Id)
        };
        return View(vm);
    }

    // ── ADMIN ────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> AddMember(string id, List<string> memberIds)
    {
        var a = Auth(); if (a != null) return a;
        var group = _data.GetGroup(id);
        if (group == null || group.OwnerId != CurrentUserId) return RedirectToAction("Conversation", new { id });

        var added = new List<string>();
        foreach (var mid in (memberIds ?? new List<string>()).Distinct())
        {
            if (string.IsNullOrWhiteSpace(mid) || mid == CurrentUserId) continue;
            if (_data.AddGroupMember(id, mid)) added.Add(mid);
        }

        group = _data.GetGroup(id);
        if (added.Count > 0)
        {
            await ChatHub.BroadcastToGroup(_hub, id, "GroupUpdated", group);
            await ChatHub.NotifyUsers(_hub, added, "GroupUpdated", group);
        }
        return RedirectToAction("Conversation", new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveMember(string id, string memberId)
    {
        var a = Auth(); if (a != null) return a;
        var group = _data.GetGroup(id);
        if (group == null) return RedirectToAction("Conversation", new { id });
        if (memberId != CurrentUserId && group.OwnerId != CurrentUserId)
            return RedirectToAction("Conversation", new { id });

        if (_data.RemoveMemberFromGroup(id, memberId))
        {
            group = _data.GetGroup(id);
            if (group != null)
            {
                await ChatHub.BroadcastToGroup(_hub, id, "GroupUpdated", group);
                await ChatHub.NotifyUsers(_hub, new[] { memberId }, "GroupLeft", id);
            }
        }
        return RedirectToAction("Conversation", new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Rename(string id, string name)
    {
        var a = Auth(); if (a != null) return a;
        var group = _data.GetGroup(id);
        if (group == null || group.OwnerId != CurrentUserId) return RedirectToAction("Conversation", new { id });

        if (_data.RenameGroup(id, name))
        {
            group = _data.GetGroup(id);
            await ChatHub.BroadcastToGroup(_hub, id, "GroupUpdated", group);
        }
        return RedirectToAction("Conversation", new { id });
    }

    [HttpPost]
    public async Task<IActionResult> UploadAvatar(string id, IFormFile avatarFile)
    {
        var a = Auth(); if (a != null) return a;
        var group = _data.GetGroup(id);
        if (group == null || group.OwnerId != CurrentUserId) return RedirectToAction("Conversation", new { id });

        if (avatarFile != null && avatarFile.Length > 0 && avatarFile.Length <= MaxAvatarSize)
        {
            var ext = Path.GetExtension(avatarFile.FileName).ToLower();
            if (!UploadValidation.IsBlockedExtension(ext) && UploadValidation.IsImageExtension(ext))
            {
                var head = new byte[16];
                await using (var src = avatarFile.OpenReadStream())
                {
                    var n = await src.ReadAsync(head, 0, head.Length);
                    if (n > 0 && UploadValidation.HasValidImageSignature(ext, head.AsSpan(0, n).ToArray()))
                    {
                        var path = await _storage.SaveAsync(avatarFile, "groups");
                        if (path != null && _data.UpdateGroupAvatar(id, path))
                        {
                            group = _data.GetGroup(id);
                            await ChatHub.BroadcastToGroup(_hub, id, "GroupUpdated", group);
                        }
                    }
                }
            }
        }
        return RedirectToAction("Conversation", new { id });
    }

    // ── ARCHIVOS EN GRUPOS ───────────────────────────
    [HttpPost]
    [RequestSizeLimit(128 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 128 * 1024 * 1024)]
    public async Task<IActionResult> SendFile(string id, IFormFile file)
    {
        var a = Auth(); if (a != null) return a;
        var sender = _data.GetUserById(CurrentUserId!);
        if (sender == null || file == null || file.Length == 0)
            return RedirectToAction("Conversation", new { id });
        if (!_data.IsGroupMember(id, sender.Id))
            return RedirectToAction("Conversation", new { id });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (string.IsNullOrEmpty(ext)) ext = ".file";

        if (UploadValidation.IsBlockedExtension(ext) || !UploadValidation.IsAllowedExtension(ext))
        {
            TempData["Error"] = "Tipo de archivo no permitido.";
            return RedirectToAction("Conversation", new { id });
        }
        var type = UploadValidation.GetMessageType(ext);

        var limit = type == MessageType.Video ? MaxVideoSize : MaxFileSize;
        if (file.Length > limit)
        {
            TempData["Error"] = type == MessageType.Video
                ? "El video supera el límite de 100 MB."
                : "El archivo supera el límite de 20 MB.";
            return RedirectToAction("Conversation", new { id });
        }

        var folder = type switch
        {
            MessageType.Image => "images",
            MessageType.Audio => "audios",
            MessageType.Video => "videos",
            _ => "documents"
        };

        if (type == MessageType.Image)
        {
            var head = new byte[16];
            await using (var src = file.OpenReadStream())
            {
                var n = await src.ReadAsync(head, 0, head.Length);
                if (n == 0 || !UploadValidation.HasValidImageSignature(ext, head.AsSpan(0, n).ToArray()))
                {
                    TempData["Error"] = "El archivo no es una imagen válida.";
                    return RedirectToAction("Conversation", new { id });
                }
            }
        }

        var filePath = await _storage.SaveAsync(file, folder);
        if (filePath == null)
        {
            TempData["Error"] = "No se pudo guardar el archivo.";
            return RedirectToAction("Conversation", new { id });
        }

        var msg = _data.AddGroupMessage(new Message
        {
            SenderId = sender.Id, SenderName = sender.DisplayName,
            SenderColor = sender.AvatarColor, ReceiverId = id,
            Content = file.FileName, Type = type,
            FileName = file.FileName, FilePath = filePath,
            FileSize = file.Length,
            CreatedAt = DateTime.UtcNow
        });

        await ChatHub.BroadcastToGroup(_hub, id, "ReceiveGroupMessage", msg);
        return RedirectToAction("Conversation", new { id });
    }
}
