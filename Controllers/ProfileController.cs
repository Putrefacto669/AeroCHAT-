using AeroChat.Models;
using AeroChat.Services;
using Microsoft.AspNetCore.Mvc;

namespace AeroChat.Controllers;

public class ProfileController : Controller
{
    private readonly DataService _data;
    private readonly IFileStorage _storage;
    private const long MaxFileSize = 5 * 1024 * 1024;

    public ProfileController(DataService data, IFileStorage storage)
    {
        _data = data;
        _storage = storage;
    }

    private string? CurrentUserId => HttpContext.Session.GetString("UserId");

    private IActionResult? Auth()
    {
        if (CurrentUserId == null) return RedirectToAction("Index", "Home");
        return null;
    }

    // ── GET /Profile/Index/{id} ───────────────────────────
    public IActionResult Index(string id)
    {
        var a = Auth(); if (a != null) return a;
        var current = _data.GetUserById(CurrentUserId!);
        var profile = _data.GetUserById(id);
        if (current == null || profile == null) return RedirectToAction("Index", "Chat");

        var friends = _data.GetUsers().Where(u => current.FriendIds.Contains(u.Id)).ToList();
        ViewBag.SidebarUser = current;
        ViewBag.SidebarItems = _data.GetSidebarItems(current.Id);
        ViewBag.SidebarGroups = _data.GetGroupsForUser(current.Id);
        ViewBag.SidebarActiveId = null;

        var vm = new ProfileViewModel
        {
            CurrentUser = current,
            ProfileUser = profile,
            AllUsers = _data.GetUsers().Where(u => u.Id != current.Id).ToList(),
            Friends = friends,
            FriendState = _data.GetFriendState(current.Id, profile.Id).ToString().ToLowerInvariant(),
            RequestId = _data.GetIncomingRequestId(current.Id, profile.Id),
            YoutubeEmbedId = DataService.ExtractYoutubeId(profile.YoutubeSongUrl)
        };
        return View(vm);
    }

    // ── GET /Profile/Edit ─────────────────────────────────
    public IActionResult Edit()
    {
        var a = Auth(); if (a != null) return a;
        var user = _data.GetUserById(CurrentUserId!);
        if (user == null) return RedirectToAction("Index", "Home");

        ViewBag.CurrentUser = user;
        ViewBag.SidebarUser = user;
        ViewBag.SidebarItems = _data.GetSidebarItems(user.Id);
        ViewBag.SidebarGroups = _data.GetGroupsForUser(user.Id);
        ViewBag.SidebarActiveId = null;
        return View(new EditProfileViewModel
        {
            DisplayName = user.DisplayName,
            Status = user.Status,
            YoutubeSongUrl = user.YoutubeSongUrl
        });
    }

    // ── POST /Profile/Edit ────────────────────────────────
    [HttpPost]
    public IActionResult Edit(EditProfileViewModel vm)
    {
        var a = Auth(); if (a != null) return a;
        var user = _data.GetUserById(CurrentUserId!);
        if (user == null) return RedirectToAction("Index", "Home");

        if (string.IsNullOrWhiteSpace(vm.DisplayName))
        {
            vm.Error = "El nombre no puede estar vacío.";
            ViewBag.CurrentUser = user;
            ViewBag.SidebarUser = user;
            ViewBag.SidebarItems = _data.GetSidebarItems(user.Id);
            ViewBag.SidebarGroups = _data.GetGroupsForUser(user.Id);
            ViewBag.SidebarActiveId = null;
            return View(vm);
        }

        user.DisplayName = vm.DisplayName.Trim();
        user.Status = vm.Status?.Trim();
        user.YoutubeSongUrl = vm.YoutubeSongUrl?.Trim();
        _data.UpdateUser(user);
        HttpContext.Session.SetString("UserName", user.DisplayName);

        return RedirectToAction("Index", new { id = user.Id });
    }

    // ── POST /Profile/UploadAvatar ────────────────────────
    [HttpPost]
    public async Task<IActionResult> UploadAvatar(IFormFile avatarFile)
    {
        var a = Auth(); if (a != null) return a;
        var user = _data.GetUserById(CurrentUserId!);
        if (user == null) return RedirectToAction("Index", "Home");

        if (avatarFile != null && avatarFile.Length > 0 && avatarFile.Length <= MaxFileSize)
        {
            var path = await SaveImage(avatarFile, "avatars");
            if (path != null)
            {
                _storage.Delete(user.AvatarPath);
                user.AvatarPath = path;
                _data.UpdateUser(user);
            }
        }

        return RedirectToAction("Index", new { id = user.Id });
    }

    // ── POST /Profile/UploadBanner ────────────────────────
    [HttpPost]
    public async Task<IActionResult> UploadBanner(IFormFile bannerFile)
    {
        var a = Auth(); if (a != null) return a;
        var user = _data.GetUserById(CurrentUserId!);
        if (user == null) return RedirectToAction("Index", "Home");

        if (bannerFile != null && bannerFile.Length > 0 && bannerFile.Length <= MaxFileSize)
        {
            var path = await SaveImage(bannerFile, "banners");
            if (path != null)
            {
                _storage.Delete(user.BannerPath);
                user.BannerPath = path;
                _data.UpdateUser(user);
            }
        }

        return RedirectToAction("Index", new { id = user.Id });
    }

    private async Task<string?> SaveImage(IFormFile file, string folder)
    {
        var ext = Path.GetExtension(file.FileName).ToLower();
        if (UploadValidation.IsBlockedExtension(ext) || !UploadValidation.IsImageExtension(ext))
            return null;

        var head = new byte[16];
        await using (var src = file.OpenReadStream())
        {
            var n = await src.ReadAsync(head, 0, head.Length);
            if (n == 0 || !UploadValidation.HasValidImageSignature(ext, head.AsSpan(0, n).ToArray()))
                return null;
        }
        return await _storage.SaveAsync(file, folder);
    }
}
