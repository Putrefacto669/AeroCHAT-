using AeroChat.Models;
using AeroChat.Services;
using Microsoft.AspNetCore.Mvc;

namespace AeroChat.Controllers;

public class ChatController : Controller
{
    private readonly DataService _data;
    private readonly IWebHostEnvironment _env;
    private const long MaxFileSize = 20 * 1024 * 1024;

    public ChatController(DataService data, IWebHostEnvironment env)
    {
        _data = data;
        _env = env;
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
        ViewBag.AllUsers = _data.GetUsers().Where(u => u.Id != user.Id).ToList();
        return View();
    }

    public IActionResult Conversation(string id)
    {
        var a = Auth(); if (a != null) return a;
        var current = _data.GetUserById(CurrentUserId!);
        var recipient = _data.GetUserById(id);
        if (current == null || recipient == null) return RedirectToAction("Index");

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
    public IActionResult Send(string receiverId, string content)
    {
        var a = Auth(); if (a != null) return a;
        var sender = _data.GetUserById(CurrentUserId!);
        if (sender == null || string.IsNullOrWhiteSpace(content))
            return RedirectToAction("Conversation", new { id = receiverId });

        _data.AddMessage(new Message
        {
            SenderId = sender.Id, SenderName = sender.DisplayName,
            SenderColor = sender.AvatarColor, ReceiverId = receiverId,
            Content = content.Trim(), Type = MessageType.Text
        });
        return RedirectToAction("Conversation", new { id = receiverId });
    }

    [HttpPost]
    public async Task<IActionResult> SendFile(string receiverId, IFormFile file)
    {
        var a = Auth(); if (a != null) return a;
        var sender = _data.GetUserById(CurrentUserId!);
        if (sender == null || file == null || file.Length == 0)
            return RedirectToAction("Conversation", new { id = receiverId });

        if (file.Length > MaxFileSize)
        {
            TempData["Error"] = "El archivo supera el límite de 20 MB.";
            return RedirectToAction("Conversation", new { id = receiverId });
        }

        var ext = Path.GetExtension(file.FileName).ToLower();
        var type = GetMessageType(ext);
        var folder = type switch { MessageType.Image => "images", MessageType.Audio => "audios", _ => "documents" };
        var dir = Path.Combine(_env.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(dir);
        var safeName = $"{Guid.NewGuid()}{ext}";
        await using (var stream = new FileStream(Path.Combine(dir, safeName), FileMode.Create))
            await file.CopyToAsync(stream);

        _data.AddMessage(new Message
        {
            SenderId = sender.Id, SenderName = sender.DisplayName,
            SenderColor = sender.AvatarColor, ReceiverId = receiverId,
            Content = file.FileName, Type = type,
            FileName = file.FileName, FilePath = $"/uploads/{folder}/{safeName}",
            FileSize = file.Length
        });
        return RedirectToAction("Conversation", new { id = receiverId });
    }

    [HttpPost]
    public IActionResult Edit(string messageId, string receiverId, string newContent)
    {
        var a = Auth(); if (a != null) return a;
        if (!string.IsNullOrWhiteSpace(newContent))
            _data.EditMessage(messageId, CurrentUserId!, newContent.Trim());
        return RedirectToAction("Conversation", new { id = receiverId });
    }

    [HttpPost]
    public IActionResult Delete(string messageId, string receiverId)
    {
        var a = Auth(); if (a != null) return a;
        _data.DeleteMessage(messageId, CurrentUserId!);
        return RedirectToAction("Conversation", new { id = receiverId });
    }

    private static MessageType GetMessageType(string ext) => ext switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => MessageType.Image,
        ".mp3" or ".ogg" or ".wav" or ".m4a" or ".aac" => MessageType.Audio,
        _ => MessageType.Document
    };
}
