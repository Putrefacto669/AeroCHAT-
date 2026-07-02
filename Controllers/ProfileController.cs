using AeroChat.Models;
using AeroChat.Services;
using Microsoft.AspNetCore.Mvc;

namespace AeroChat.Controllers;

public class ProfileController : Controller
{
    private readonly DataService _data;
    private readonly IWebHostEnvironment _env;
    private const long MaxFileSize = 5 * 1024 * 1024;

    public ProfileController(DataService data, IWebHostEnvironment env)
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

    // ── GET /Profile/Index/{id} ───────────────────────────
    public IActionResult Index(string id)
    {
        var a = Auth(); if (a != null) return a;
        var current = _data.GetUserById(CurrentUserId!);
        var profile = _data.GetUserById(id);
        if (current == null || profile == null) return RedirectToAction("Index", "Chat");

        var vm = new ProfileViewModel
        {
            CurrentUser = current,
            ProfileUser = profile,
            AllUsers = _data.GetUsers().Where(u => u.Id != current.Id).ToList(),
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
        ViewBag.AllUsers = _data.GetUsers().Where(u => u.Id != user.Id).ToList();
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
            ViewBag.AllUsers = _data.GetUsers().Where(u => u.Id != user.Id).ToList();
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
            user.AvatarPath = await SaveImage(avatarFile, "avatars");
            _data.UpdateUser(user);
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
            user.BannerPath = await SaveImage(bannerFile, "banners");
            _data.UpdateUser(user);
        }

        return RedirectToAction("Index", new { id = user.Id });
    }

    private async Task<string> SaveImage(IFormFile file, string folder)
    {
        var ext = Path.GetExtension(file.FileName).ToLower();
        var dir = Path.Combine(_env.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid()}{ext}";
        await using var stream = new FileStream(Path.Combine(dir, name), FileMode.Create);
        await file.CopyToAsync(stream);
        return $"/uploads/{folder}/{name}";
    }
}
