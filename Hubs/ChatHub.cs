using AeroChat.Models;
using AeroChat.Services;
using Microsoft.AspNetCore.SignalR;

namespace AeroChat.Hubs;

public class ChatHub : Hub
{
    private readonly DataService _data;

    public ChatHub(DataService data) => _data = data;

    private string? UserId => Context.GetHttpContext()?.Session.GetString("UserId");

    public override async Task OnConnectedAsync()
    {
        if (UserId == null)
        {
            Context.Abort();
            return;
        }
        await base.OnConnectedAsync();
    }

    public async Task JoinConversation(string otherId)
    {
        var uid = UserId;
        if (uid == null) return;
        var group = GroupName(uid, otherId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public async Task LeaveConversation(string otherId)
    {
        var uid = UserId;
        if (uid == null) return;
        var group = GroupName(uid, otherId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    public async Task SendMessage(string receiverId, string content)
    {
        var uid = UserId;
        if (uid == null || string.IsNullOrWhiteSpace(content)) return;

        var sender = _data.GetUserById(uid);
        if (sender == null) return;

        var msg = _data.AddMessage(new Message
        {
            SenderId = uid,
            SenderName = sender.DisplayName,
            SenderColor = sender.AvatarColor,
            ReceiverId = receiverId,
            Content = content.Trim(),
            Type = MessageType.Text,
            CreatedAt = DateTime.UtcNow
        });

        var group = GroupName(uid, receiverId);
        await Clients.Group(group).SendAsync("ReceiveMessage", msg);
    }

    public async Task EditMessage(string messageId, string receiverId, string newContent)
    {
        var uid = UserId;
        if (uid == null || string.IsNullOrWhiteSpace(newContent)) return;

        var ok = _data.EditMessage(messageId, uid, newContent.Trim());
        if (!ok) return;

        var group = GroupName(uid, receiverId);
        await Clients.Group(group).SendAsync("MessageEdited", messageId, newContent.Trim(), DateTime.UtcNow);
    }

    public async Task DeleteMessage(string messageId, string receiverId)
    {
        var uid = UserId;
        if (uid == null) return;

        var ok = _data.DeleteMessage(messageId, uid);
        if (!ok) return;

        var group = GroupName(uid, receiverId);
        await Clients.Group(group).SendAsync("MessageDeleted", messageId);
    }

    public async Task Typing(string receiverId, string displayName)
    {
        var uid = UserId;
        if (uid == null) return;

        var group = GroupName(uid, receiverId);
        await Clients.OthersInGroup(group).SendAsync("UserTyping", uid, displayName);
    }

    public async Task StopTyping(string receiverId)
    {
        var uid = UserId;
        if (uid == null) return;

        var group = GroupName(uid, receiverId);
        await Clients.OthersInGroup(group).SendAsync("UserStoppedTyping", uid);
    }

    public static string GroupStatic(string a, string b)
    {
        var arr = new[] { a, b };
        Array.Sort(arr);
        return $"chat_{arr[0]}_{arr[1]}";
    }

    private static string GroupName(string a, string b) => GroupStatic(a, b);
}
