using AeroChat.Models;
using AeroChat.Services;
using Microsoft.AspNetCore.SignalR;

namespace AeroChat.Hubs;

public class ChatHub : Hub
{
    private readonly DataService _data;
    private static readonly object _connLock = new();
    private static readonly Dictionary<string, HashSet<string>> _connections = new();
    private static readonly object _busyLock = new();
    private static readonly HashSet<string> _busy = new();

    public ChatHub(DataService data) => _data = data;

    private string? UserId => Context.GetHttpContext()?.Session.GetString("UserId");

    public override async Task OnConnectedAsync()
    {
        var uid = UserId;
        if (uid == null)
        {
            Context.Abort();
            return;
        }
        Track(uid, Context.ConnectionId);
        await base.OnConnectedAsync();
        await Clients.Others.SendAsync("UserOnline", uid);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var uid = UserId;
        if (uid != null)
        {
            Untrack(uid, Context.ConnectionId);
            SetBusy(uid, false);
        }
        await base.OnDisconnectedAsync(exception);
        if (uid != null) await Clients.Others.SendAsync("UserOffline", uid);
    }

    // ── Presence ──────────────────────────────────────
    public Task GetOnlineUsers()
    {
        List<string> ids;
        lock (_connLock) ids = _connections.Keys.ToList();
        return Clients.Caller.SendAsync("OnlineUsers", ids);
    }

    private Task SendToUser(string userId, string method, object? arg)
    {
        List<string> conns;
        lock (_connLock)
            conns = _connections.TryGetValue(userId, out var set) ? set.ToList() : new();
        if (conns.Count == 0) return Task.CompletedTask;
        return Clients.Clients(conns).SendAsync(method, arg);
    }

    private static void Track(string userId, string connId)
    {
        lock (_connLock)
        {
            if (!_connections.TryGetValue(userId, out var set))
            {
                set = new HashSet<string>();
                _connections[userId] = set;
            }
            set.Add(connId);
        }
    }

    private static void Untrack(string userId, string connId)
    {
        lock (_connLock)
        {
            if (_connections.TryGetValue(userId, out var set))
            {
                set.Remove(connId);
                if (set.Count == 0) _connections.Remove(userId);
            }
        }
    }

    private static bool IsOnline(string userId)
    {
        lock (_connLock) return _connections.ContainsKey(userId);
    }

    private static bool IsBusy(string userId)
    {
        lock (_busyLock) return _busy.Contains(userId);
    }

    private static void SetBusy(string userId, bool busy)
    {
        lock (_busyLock)
        {
            if (busy) _busy.Add(userId); else _busy.Remove(userId);
        }
    }

    // ── Conversation groups ───────────────────────────
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

    // ── Friends ───────────────────────────────────────
    public async Task SendFriendRequest(string toUserId)
    {
        var uid = UserId;
        if (uid == null) return;

        var result = _data.SendFriendRequest(uid, toUserId);
        switch (result)
        {
            case FriendRequestResult.Sent:
                var me = _data.GetUserById(uid);
                if (me == null) return;
                await SendToUser(toUserId, "FriendRequestReceived", me);
                await Clients.Caller.SendAsync("FriendRequestSent", toUserId);
                break;
            case FriendRequestResult.Pending:
                await Clients.Caller.SendAsync("FriendRequestError", "pending");
                break;
            case FriendRequestResult.AlreadyFriends:
                await Clients.Caller.SendAsync("FriendRequestError", "friends");
                break;
        }
    }

    public async Task AcceptFriendRequest(string requestId, string fromUserId)
    {
        var uid = UserId;
        if (uid == null) return;

        var senderId = _data.AcceptFriendRequest(uid, requestId);
        if (senderId == null) return;

        var me = _data.GetUserById(uid);
        await SendToUser(senderId, "FriendRequestAccepted", me);
        await Clients.Caller.SendAsync("FriendRequestAcceptedSelf", senderId);
    }

    public async Task DeclineFriendRequest(string requestId, string fromUserId)
    {
        var uid = UserId;
        if (uid == null) return;

        var senderId = _data.DeclineFriendRequest(uid, requestId);
        if (senderId == null) return;

        var me = _data.GetUserById(uid);
        await SendToUser(senderId, "FriendRequestDeclined", me);
        await Clients.Caller.SendAsync("FriendRequestDeclinedSelf", senderId);
    }

    public async Task CancelFriendRequest(string toUserId)
    {
        var uid = UserId;
        if (uid == null) return;

        if (_data.CancelFriendRequest(uid, toUserId))
            await Clients.Caller.SendAsync("FriendRequestCancelled", toUserId);
    }

    public async Task RemoveFriend(string friendId)
    {
        var uid = UserId;
        if (uid == null) return;

        if (_data.RemoveFriend(uid, friendId))
        {
            await SendToUser(friendId, "FriendRemoved", uid);
            await Clients.Caller.SendAsync("FriendRemovedSelf", friendId);
        }
    }

    // ── Group chat ────────────────────────────────────
    private static string GroupGroup(string groupId) => $"grp_{groupId}";

    public async Task JoinGroup(string groupId)
    {
        var uid = UserId;
        if (uid == null || !_data.IsGroupMember(groupId, uid)) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupGroup(groupId));
    }

    public async Task CreateGroup(string name, List<string> memberIds)
    {
        var uid = UserId;
        if (uid == null) return;
        var group = _data.CreateGroup(name, uid, memberIds);
        if (group == null) return;

        var me = _data.GetUserById(uid);
        foreach (var mid in group.MemberIds)
        {
            if (mid == uid) continue;
            await SendToUser(mid, "GroupCreated",
                new { group, createdBy = uid, creatorName = me?.DisplayName });
        }
        await Clients.Caller.SendAsync("GroupCreatedSelf", group);
    }

    public async Task SendGroupMessage(string groupId, string content)
    {
        var uid = UserId;
        if (uid == null || string.IsNullOrWhiteSpace(content)) return;
        if (!_data.IsGroupMember(groupId, uid)) return;

        var sender = _data.GetUserById(uid);
        if (sender == null) return;

        var msg = _data.AddGroupMessage(new Message
        {
            SenderId = uid,
            SenderName = sender.DisplayName,
            SenderColor = sender.AvatarColor,
            ReceiverId = groupId,
            Content = content.Trim(),
            Type = MessageType.Text,
            CreatedAt = DateTime.UtcNow
        });
        await Clients.Group(GroupGroup(groupId)).SendAsync("ReceiveGroupMessage", msg);
    }

    public async Task EditGroupMessage(string groupId, string messageId, string newContent)
    {
        var uid = UserId;
        if (uid == null || string.IsNullOrWhiteSpace(newContent)) return;
        if (!_data.EditGroupMessage(groupId, messageId, uid, newContent.Trim())) return;
        await Clients.Group(GroupGroup(groupId)).SendAsync(
            "GroupMessageEdited", messageId, newContent.Trim(), DateTime.UtcNow);
    }

    public async Task DeleteGroupMessage(string groupId, string messageId)
    {
        var uid = UserId;
        if (uid == null) return;
        if (!_data.DeleteGroupMessage(groupId, messageId, uid)) return;
        await Clients.Group(GroupGroup(groupId)).SendAsync("GroupMessageDeleted", messageId);
    }

    public async Task GroupTyping(string groupId, string displayName)
    {
        var uid = UserId;
        if (uid == null) return;
        await Clients.OthersInGroup(GroupGroup(groupId)).SendAsync("GroupUserTyping", uid, displayName);
    }

    public async Task GroupStopTyping(string groupId)
    {
        var uid = UserId;
        if (uid == null) return;
        await Clients.OthersInGroup(GroupGroup(groupId)).SendAsync("GroupUserStoppedTyping", uid);
    }

    public async Task LeaveGroup(string groupId)
    {
        var uid = UserId;
        if (uid == null) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupGroup(groupId));
        if (_data.RemoveMemberFromGroup(groupId, uid))
        {
            await Clients.Group(GroupGroup(groupId)).SendAsync("GroupMemberLeft", uid, groupId);
            await Clients.Caller.SendAsync("GroupLeft", groupId);
        }
    }

    // ── Audio calls (WebRTC signaling) ────────────────
    public async Task CallSignal(string toUserId, CallMessage msg)
    {
        var uid = UserId;
        if (uid == null || msg == null) return;

        if (msg.Type == "offer")
        {
            if (IsBusy(toUserId))
            {
                await Clients.Caller.SendAsync("CallBusy", toUserId);
                return;
            }
            if (!IsOnline(toUserId))
            {
                await Clients.Caller.SendAsync("CallOffline", toUserId);
                return;
            }
        }

        if (msg.Type == "answer")
        {
            SetBusy(uid, true);
            SetBusy(toUserId, true);
        }

        var sender = _data.GetUserById(uid);
        var payload = new
        {
            from = uid,
            fromName = sender?.DisplayName,
            fromAvatar = sender?.AvatarPath,
            fromColor = sender?.AvatarColor,
            message = msg
        };
        await SendToUser(toUserId, "CallSignal", payload);
    }

    public async Task CallHangup(string toUserId)
    {
        var uid = UserId;
        if (uid == null) return;
        SetBusy(uid, false);
        SetBusy(toUserId, false);
        await SendToUser(toUserId, "CallEnded", uid);
    }

    public async Task CallDecline(string toUserId)
    {
        var uid = UserId;
        if (uid == null) return;
        SetBusy(uid, false);
        SetBusy(toUserId, false);
        await SendToUser(toUserId, "CallDeclined", uid);
    }

    public static string GroupStatic(string a, string b)
    {
        var arr = new[] { a, b };
        Array.Sort(arr);
        return $"chat_{arr[0]}_{arr[1]}";
    }

    private static string GroupName(string a, string b) => GroupStatic(a, b);
}
