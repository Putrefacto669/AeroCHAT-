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
    public List<string> FriendIds { get; set; } = new();
    public List<FriendRequest> FriendRequests { get; set; } = new();
}

public class FriendRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FromUserId { get; set; } = "";
    public string ToUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum FriendState { None, Friends, Outgoing, Incoming }

public enum FriendRequestResult { Sent, Pending, AlreadyFriends, NotFound, Self }

public class SidebarUserItem
{
    public User User { get; set; } = new();
    public string State { get; set; } = "none";
    public string RequestId { get; set; } = "";
}

public class Reaction
{
    public string UserId { get; set; } = "";
    public string Emoji { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public MessageScope Scope { get; set; } = MessageScope.Direct;
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
    public string? ReplyToId { get; set; }
    public string? ReplyToContent { get; set; }
    public string? ReplyToSender { get; set; }
    public List<string> ReadBy { get; set; } = new();
    public List<Reaction> Reactions { get; set; } = new();
}

public enum MessageType { Text, Image, Audio, Document, Video, Sticker }

public enum MessageScope { Direct, Group }

public class Group
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public List<string> MemberIds { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? AvatarPath { get; set; }
    public string? Description { get; set; }
}

public class GroupViewModel
{
    public Group Group { get; set; } = new();
    public User CurrentUser { get; set; } = new();
    public List<User> Members { get; set; } = new();
    public List<Message> Messages { get; set; } = new();
}

public class CallMessage
{
    public string Type { get; set; } = "";
    public string? Sdp { get; set; }
    public string? Candidate { get; set; }
    public string? SdpMid { get; set; }
    public int? SdpMLineIndex { get; set; }
}

public enum StatusType { Text, Image }

public class Status
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string UserColor { get; set; } = "#6C63FF";
    public string? UserAvatar { get; set; }
    public string Content { get; set; } = "";
    public StatusType Type { get; set; } = StatusType.Text;
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

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

public class RegisterViewModel
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Password { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
    public string? Error { get; set; }
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
    public List<User> Friends { get; set; } = new();
    public string FriendState { get; set; } = "none";
    public string? RequestId { get; set; }
    public string? YoutubeEmbedId { get; set; }
}

public class StickerLibrary
{
    public string UserId { get; set; } = "";
    public List<string> Favorites { get; set; } = new();
    public Dictionary<string, int> Usage { get; set; } = new();
    public Dictionary<string, string> PackNames { get; set; } = new();
}
