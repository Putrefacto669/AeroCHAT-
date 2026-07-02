namespace AeroChat.Models;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Password { get; set; } = "";
    public string AvatarColor { get; set; } = "#6C63FF";
    public string? AvatarPath { get; set; }
    public string? BannerPath { get; set; }
    public string? Status { get; set; }
    public string? YoutubeSongUrl { get; set; }
}

public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SenderId { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string SenderColor { get; set; } = "#6C63FF";
    public string ReceiverId { get; set; } = "";
    public string Content { get; set; } = "";
    public MessageType Type { get; set; } = MessageType.Text;
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}

public enum MessageType { Text, Image, Audio, Document }

public class LoginViewModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Error { get; set; }
}

public class ChatViewModel
{
    public User CurrentUser { get; set; } = new();
    public User Recipient { get; set; } = new();
    public List<Message> Messages { get; set; } = new();
    public List<User> AllUsers { get; set; } = new();
}

public class EditProfileViewModel
{
    public string DisplayName { get; set; } = "";
    public string? Status { get; set; }
    public string? YoutubeSongUrl { get; set; }
    public string? Error { get; set; }
    public string? Success { get; set; }
}

public class ProfileViewModel
{
    public User ProfileUser { get; set; } = new();
    public User CurrentUser { get; set; } = new();
    public List<User> AllUsers { get; set; } = new();
    public string? YoutubeEmbedId { get; set; }
}
