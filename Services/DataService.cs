using System.Text.Json;
using AeroChat.Models;

namespace AeroChat.Services;

public class DataService
{
    private readonly string _dataPath;
    private readonly string _usersFile;
    private readonly string _messagesFile;
    private readonly JsonSerializerOptions _opts;

    public DataService(IWebHostEnvironment env)
    {
        _dataPath = Path.Combine(env.ContentRootPath, "Data");
        _usersFile = Path.Combine(_dataPath, "users.json");
        _messagesFile = Path.Combine(_dataPath, "messages.json");
        _opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
    }

    // ── USERS ──────────────────────────────────────────────
    public List<User> GetUsers()
    {
        if (!File.Exists(_usersFile)) return new();
        var json = File.ReadAllText(_usersFile);
        return JsonSerializer.Deserialize<List<User>>(json, _opts) ?? new();
    }

    public User? GetUserById(string id) => GetUsers().FirstOrDefault(u => u.Id == id);

    public User? ValidateLogin(string username, string password)
        => GetUsers().FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase) &&
            u.Password == password);

    public bool UpdateUser(User updated)
    {
        var users = GetUsers();
        var idx = users.FindIndex(u => u.Id == updated.Id);
        if (idx < 0) return false;
        users[idx] = updated;
        File.WriteAllText(_usersFile, JsonSerializer.Serialize(users, _opts));
        return true;
    }

    // ── MESSAGES ───────────────────────────────────────────
    public List<Message> GetMessages()
    {
        if (!File.Exists(_messagesFile)) return new();
        var json = File.ReadAllText(_messagesFile);
        return JsonSerializer.Deserialize<List<Message>>(json, _opts) ?? new();
    }

    public List<Message> GetConversation(string userId1, string userId2)
        => GetMessages()
            .Where(m => !m.IsDeleted &&
                ((m.SenderId == userId1 && m.ReceiverId == userId2) ||
                 (m.SenderId == userId2 && m.ReceiverId == userId1)))
            .OrderBy(m => m.CreatedAt)
            .ToList();

    public Message AddMessage(Message message)
    {
        var messages = GetMessages();
        messages.Add(message);
        SaveMessages(messages);
        return message;
    }

    public bool EditMessage(string messageId, string userId, string newContent)
    {
        var messages = GetMessages();
        var msg = messages.FirstOrDefault(m => m.Id == messageId && m.SenderId == userId);
        if (msg == null || msg.Type != MessageType.Text) return false;
        msg.Content = newContent;
        msg.EditedAt = DateTime.Now;
        SaveMessages(messages);
        return true;
    }

    public bool DeleteMessage(string messageId, string userId)
    {
        var messages = GetMessages();
        var msg = messages.FirstOrDefault(m => m.Id == messageId && m.SenderId == userId);
        if (msg == null) return false;
        msg.IsDeleted = true;
        msg.Content = "Mensaje eliminado";
        SaveMessages(messages);
        return true;
    }

    private void SaveMessages(List<Message> messages)
        => File.WriteAllText(_messagesFile, JsonSerializer.Serialize(messages, _opts));

    // ── HELPERS ────────────────────────────────────────────
    public static string? ExtractYoutubeId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        // Handles: youtu.be/ID, youtube.com/watch?v=ID, youtube.com/embed/ID
        var uri = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : null;
        if (uri == null) return null;

        if (uri.Host.Contains("youtu.be"))
            return uri.AbsolutePath.Trim('/');

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return query["v"] ?? uri.AbsolutePath.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s));
    }
}
