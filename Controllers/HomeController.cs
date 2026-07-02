using AeroChat.Models;
using AeroChat.Services;
using Microsoft.AspNetCore.Mvc;

namespace AeroChat.Controllers;

public class HomeController : Controller
{
    private readonly DataService _data;

    public HomeController(DataService data) => _data = data;

    public IActionResult Index()
    {
        if (HttpContext.Session.GetString("UserId") != null)
            return RedirectToAction("Index", "Chat");

        return View(new LoginViewModel());
    }

    [HttpPost]
    public IActionResult Login(LoginViewModel model)
    {
        var user = _data.ValidateLogin(model.Username, model.Password);
        if (user == null)
        {
            model.Error = "Usuario o contraseña incorrectos.";
            return View("Index", model);
        }

        HttpContext.Session.SetString("UserId", user.Id);
        HttpContext.Session.SetString("UserName", user.DisplayName);
        return RedirectToAction("Index", "Chat");
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Index");
    }
}
