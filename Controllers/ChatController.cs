using AeroChat.Hubs;
using AeroChat.Models;
using AeroChat.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AeroChat.Controllers;

public class ChatController : Controller
{
    private readonly DataService _data;
    private readonly IFileStorage _storage;
    private readonly IHubContext<ChatHub> _hub;
    private const long MaxFileSize = 20 * 1024 * 1024;
    private const long MaxVideoSize = 100 * 1024 * 1024;

    public ChatController(DataService data, IFileStorage storage, IHubContext<ChatHub> hub)
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

    public IActionResult Index()
    {
        var a = Auth(); if (a != null) return a;
        var user = _data.GetUserById(CurrentUserId!);
        if (user == null) return RedirectToAction("Logout", "Home");

        ViewBag.CurrentUser = user;
        ViewBag.SidebarUser = user;
        ViewBag.SidebarItems = _data.GetSidebarItems(user.Id);
        ViewBag.SidebarGroups = _data.GetGroupsForUser(user.Id);
        ViewBag.SidebarActiveId = null;
        return View();
    }

    public IActionResult Conversation(string id)
    {
        var a = Auth(); if (a != null) return a;
        var current = _data.GetUserById(CurrentUserId!);
        var recipient = _data.GetUserById(id);
        if (current == null || recipient == null) return RedirectToAction("Index");

        ViewBag.SidebarUser = current;
        ViewBag.SidebarItems = _data.GetSidebarItems(current.Id);
        ViewBag.SidebarGroups = _data.GetGroupsForUser(current.Id);
        ViewBag.SidebarActiveId = recipient.Id;

        var vm = new ChatViewModel
        {
            CurrentUser = current,
            Recipient = recipient,
            Messages = _data.GetConversation(current.Id, recipient.Id),
            AllUsers = _data.GetUsers().Where(u => u.Id != current.Id).ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [RequestSizeLimit(128 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 128 * 1024 * 1024)]
    public async Task<IActionResult> SendFile(string receiverId, IFormFile file)
    {
        var a = Auth(); if (a != null) return a;
        var sender = _data.GetUserById(CurrentUserId!);
        if (sender == null || file == null || file.Length == 0)
            return RedirectToAction("Conversation", new { id = receiverId });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (string.IsNullOrEmpty(ext)) ext = ".file";

        if (UploadValidation.IsBlockedExtension(ext) || !UploadValidation.IsAllowedExtension(ext))
        {
            TempData["Error"] = "Tipo de archivo no permitido.";
            return RedirectToAction("Conversation", new { id = receiverId });
        }
        var type = UploadValidation.GetMessageType(ext);

        var limit = type == MessageType.Video ? MaxVideoSize : MaxFileSize;
        if (file.Length > limit)
        {
            TempData["Error"] = type == MessageType.Video
                ? "El video supera el límite de 100 MB."
                : "El archivo supera el límite de 20 MB.";
            return RedirectToAction("Conversation", new { id = receiverId });
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
                    return RedirectToAction("Conversation", new { id = receiverId });
                }
            }
        }

        var filePath = await _storage.SaveAsync(file, folder);
        if (filePath == null)
        {
            TempData["Error"] = "No se pudo guardar el archivo.";
            return RedirectToAction("Conversation", new { id = receiverId });
        }

        var msg = _data.AddMessage(new Message
        {
            SenderId = sender.Id, SenderName = sender.DisplayName,
            SenderColor = sender.AvatarColor, ReceiverId = receiverId,
            Content = file.FileName, Type = type,
            FileName = file.FileName, FilePath = filePath,
            FileSize = file.Length,
            CreatedAt = DateTime.UtcNow
        });

        var group = ChatHub.GroupStatic(sender.Id, receiverId);
        await _hub.Clients.Group(group).SendAsync("ReceiveMessage", msg);

        return RedirectToAction("Conversation", new { id = receiverId });
    }
}
