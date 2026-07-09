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

    public IActionResult Register()
    {
        if (HttpContext.Session.GetString("UserId") != null)
            return RedirectToAction("Index", "Chat");
        return View(new RegisterViewModel());
    }

    [HttpPost]
    public IActionResult Register(RegisterViewModel model)
    {
        if (HttpContext.Session.GetString("UserId") != null)
            return RedirectToAction("Index", "Chat");

        if (string.IsNullOrWhiteSpace(model.Username) ||
            string.IsNullOrWhiteSpace(model.DisplayName) ||
            string.IsNullOrWhiteSpace(model.Password))
        {
            model.Error = "Todos los campos son obligatorios.";
            return View(model);
        }

        if (model.Username.Length < 3)
        {
            model.Error = "El usuario debe tener al menos 3 caracteres.";
            return View(model);
        }

        if (_data.GetUserByUsername(model.Username) != null)
        {
            model.Error = "Ese nombre de usuario ya está en uso.";
            return View(model);
        }

        if (model.Password != model.ConfirmPassword)
        {
            model.Error = "Las contraseñas no coinciden.";
            return View(model);
        }

        if (model.Password.Length < 4)
        {
            model.Error = "La contraseña debe tener al menos 4 caracteres.";
            return View(model);
        }

        var user = _data.RegisterUser(model.Username, model.DisplayName.Trim(), model.Password);
        if (user == null)
        {
            model.Error = "Error al crear el usuario. Intentalo de nuevo.";
            return View(model);
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
