using AeroChat.Models;
using AeroChat.Services;
using Microsoft.AspNetCore.Mvc;

namespace AeroChat.Controllers;

public class GroupController : Controller
{
    private readonly DataService _data;

    public GroupController(DataService data) => _data = data;

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

        var vm = new GroupViewModel
        {
            Group = group,
            CurrentUser = current,
            Members = _data.GetGroupMembers(group),
            Messages = _data.GetGroupMessages(group.Id)
        };
        return View(vm);
    }
}
