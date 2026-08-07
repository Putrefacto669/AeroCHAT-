using AeroChat.Models;
using AeroChat.Services;
using Microsoft.AspNetCore.SignalR;

namespace AeroChat.Hubs;

public class ChatHub : Hub
{
    private readonly DataService _data;
    private readonly IWebHostEnvironment _env;
    private static readonly object _connLock = new();
    private static readonly Dictionary<string, HashSet<string>> _connections = new();
    private static readonly object _callLock = new();
    private static readonly Dictionary<string, CallRoom> _callRooms = new();

    private class CallRoom
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Type { get; init; } = "audio";
        public string? GroupId { get; init; }
        public string? GroupName { get; set; }
        public HashSet<string> Members { get; } = new();
        public HashSet<string> Invitees { get; } = new();
    }

    public ChatHub(DataService data, IWebHostEnvironment env)
    {
        _data = data;
        _env = env;
    }

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
            await LeaveAllCallRooms(uid);
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
        => SendToUsers(new[] { userId }, method, arg);

    private Task SendToUsers(IEnumerable<string> userIds, string method, object? arg)
    {
        var conns = ResolveConnections(userIds);
        if (conns.Count == 0) return Task.CompletedTask;
        return Clients.Clients(conns).SendAsync(method, arg);
    }

    private static List<string> ResolveConnections(IEnumerable<string> userIds)
    {
        var conns = new List<string>();
        lock (_connLock)
        {
            foreach (var id in userIds)
                if (_connections.TryGetValue(id, out var set) && set.Count > 0)
                    conns.AddRange(set);
        }
        return conns;
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

    public static async Task NotifyUsers(IHubContext<ChatHub> ctx, IEnumerable<string> userIds, string method, object? arg)
    {
        var conns = ResolveConnections(userIds);
        if (conns.Count > 0)
            await ctx.Clients.Clients(conns).SendAsync(method, arg);
    }

    // ── Conversation groups ───────────────────────────
    public async Task JoinConversation(string otherId)
    {
        var uid = UserId;
        if (uid == null) return;
        var group = GroupStatic(uid, otherId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public async Task LeaveConversation(string otherId)
    {
        var uid = UserId;
        if (uid == null) return;
        var group = GroupStatic(uid, otherId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    public async Task SendMessage(string receiverId, string content, string? replyToId = null)
    {
        var uid = UserId;
        if (uid == null || string.IsNullOrWhiteSpace(content)) return;

        var sender = _data.GetUserById(uid);
        if (sender == null) return;

        string? replyContent = null, replySender = null;
        if (!string.IsNullOrEmpty(replyToId))
        {
            var orig = _data.GetMessages().FirstOrDefault(m => m.Id == replyToId && !m.IsDeleted);
            if (orig != null)
            {
                replyContent = orig.Type == MessageType.Text ? orig.Content : "[" + orig.Type.ToString().ToLower() + "]";
                replySender = orig.SenderName;
            }
        }

        var msg = _data.AddMessage(new Message
        {
            SenderId = uid,
            SenderName = sender.DisplayName,
            SenderColor = sender.AvatarColor,
            ReceiverId = receiverId,
            Content = content.Trim(),
            Type = MessageType.Text,
            CreatedAt = DateTime.UtcNow,
            ReplyToId = replyToId,
            ReplyToContent = replyContent,
            ReplyToSender = replySender
        });

        var group = GroupStatic(uid, receiverId);
        await Clients.Group(group).SendAsync("ReceiveMessage", msg);
    }

    public async Task ReactToMessage(string messageId, string receiverId, string emoji)
    {
        var uid = UserId;
        if (uid == null || string.IsNullOrWhiteSpace(emoji)) return;

        var (msg, added) = _data.ToggleReaction(messageId, uid, emoji);
        if (msg == null) return;

        var count = msg.Reactions.Count(r => r.Emoji == emoji);
        var group = GroupStatic(uid, receiverId);
        await Clients.Group(group).SendAsync("MessageReacted", messageId, emoji, uid, added, count);
    }

    public async Task MarkRead(string receiverId)
    {
        var uid = UserId;
        if (uid == null) return;
        if (_data.MarkConversationRead(uid, receiverId) == 0) return;
        await Clients.Others.SendAsync("MessagesRead", uid, receiverId);
    }

    public async Task SearchMessages(string receiverId, string query)
    {
        var uid = UserId;
        if (uid == null || string.IsNullOrWhiteSpace(query)) return;
        var results = _data.SearchConversation(uid, receiverId, query.Trim());
        await Clients.Caller.SendAsync("SearchResults", results);
    }

    public async Task EditMessage(string messageId, string receiverId, string newContent)
    {
        var uid = UserId;
        if (uid == null || string.IsNullOrWhiteSpace(newContent)) return;

        var ok = _data.EditMessage(messageId, uid, newContent.Trim());
        if (!ok) return;

        var group = GroupStatic(uid, receiverId);
        await Clients.Group(group).SendAsync("MessageEdited", messageId, newContent.Trim(), DateTime.UtcNow);
    }

    public async Task DeleteMessage(string messageId, string receiverId)
    {
        var uid = UserId;
        if (uid == null) return;

        var ok = _data.DeleteMessage(messageId, uid);
        if (!ok) return;

        var group = GroupStatic(uid, receiverId);
        await Clients.Group(group).SendAsync("MessageDeleted", messageId);
    }

    public async Task Typing(string receiverId, string displayName)
    {
        var uid = UserId;
        if (uid == null) return;

        var group = GroupStatic(uid, receiverId);
        await Clients.OthersInGroup(group).SendAsync("UserTyping", uid, displayName);
    }

    public async Task StopTyping(string receiverId)
    {
        var uid = UserId;
        if (uid == null) return;

        var group = GroupStatic(uid, receiverId);
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
    public static string GroupGroupStatic(string groupId) => $"grp_{groupId}";

    public static async Task BroadcastToGroup(IHubContext<ChatHub> ctx, string groupId, string method, object? arg)
        => await ctx.Clients.Group(GroupGroupStatic(groupId)).SendAsync(method, arg);

    public async Task JoinGroup(string groupId)
    {
        var uid = UserId;
        if (uid == null || !_data.IsGroupMember(groupId, uid)) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupGroupStatic(groupId));
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

    public async Task SendGroupMessage(string groupId, string content, string? replyToId = null)
    {
        var uid = UserId;
        if (uid == null || string.IsNullOrWhiteSpace(content)) return;
        if (!_data.IsGroupMember(groupId, uid)) return;

        var sender = _data.GetUserById(uid);
        if (sender == null) return;

        string? replyContent = null, replySender = null;
        if (!string.IsNullOrEmpty(replyToId))
        {
            var orig = _data.GetMessages().FirstOrDefault(m => m.Id == replyToId && !m.IsDeleted);
            if (orig != null)
            {
                replyContent = orig.Type == MessageType.Text ? orig.Content : "[" + orig.Type.ToString().ToLower() + "]";
                replySender = orig.SenderName;
            }
        }

        var msg = _data.AddGroupMessage(new Message
        {
            SenderId = uid,
            SenderName = sender.DisplayName,
            SenderColor = sender.AvatarColor,
            ReceiverId = groupId,
            Content = content.Trim(),
            Type = MessageType.Text,
            CreatedAt = DateTime.UtcNow,
            ReplyToId = replyToId,
            ReplyToContent = replyContent,
            ReplyToSender = replySender
        });
        await Clients.Group(GroupGroupStatic(groupId)).SendAsync("ReceiveGroupMessage", msg);
    }

    // ── Stickers ──────────────────────────────────────
    public async Task SendSticker(string receiverId, string stickerPath)
    {
        var uid = UserId;
        if (uid == null || !IsValidStickerPath(stickerPath)) return;
        if (!stickerPath.StartsWith("/stickers/" + uid + "/", StringComparison.OrdinalIgnoreCase)) return;

        var sender = _data.GetUserById(uid);
        if (sender == null) return;

        var msg = _data.AddMessage(new Message
        {
            SenderId = uid,
            SenderName = sender.DisplayName,
            SenderColor = sender.AvatarColor,
            ReceiverId = receiverId,
            Content = Path.GetFileName(stickerPath),
            Type = MessageType.Sticker,
            FilePath = stickerPath,
            CreatedAt = DateTime.UtcNow
        });

        _data.RecordStickerUse(uid, stickerPath);

        var group = GroupStatic(uid, receiverId);
        await Clients.Group(group).SendAsync("ReceiveMessage", msg);
    }

    public async Task SendGroupSticker(string groupId, string stickerPath)
    {
        var uid = UserId;
        if (uid == null || !IsValidStickerPath(stickerPath)) return;
        if (!stickerPath.StartsWith("/stickers/" + uid + "/", StringComparison.OrdinalIgnoreCase)) return;
        if (!_data.IsGroupMember(groupId, uid)) return;
        var sender = _data.GetUserById(uid);
        if (sender == null) return;

        var msg = _data.AddGroupMessage(new Message
        {
            SenderId = uid,
            SenderName = sender.DisplayName,
            SenderColor = sender.AvatarColor,
            ReceiverId = groupId,
            Content = Path.GetFileName(stickerPath),
            Type = MessageType.Sticker,
            FilePath = stickerPath,
            CreatedAt = DateTime.UtcNow
        });

        _data.RecordStickerUse(uid, stickerPath);

        await Clients.Group(GroupGroupStatic(groupId)).SendAsync("ReceiveGroupMessage", msg);
    }

    public async Task ReactToGroupMessage(string groupId, string messageId, string emoji)
    {
        var uid = UserId;
        if (uid == null || string.IsNullOrWhiteSpace(emoji)) return;
        if (!_data.IsGroupMember(groupId, uid)) return;

        var (msg, added) = _data.ToggleReaction(messageId, uid, emoji);
        if (msg == null) return;

        var count = msg.Reactions.Count(r => r.Emoji == emoji);
        await Clients.Group(GroupGroupStatic(groupId)).SendAsync(
            "GroupMessageReacted", messageId, emoji, uid, added, count);
    }

    public async Task MarkGroupRead(string groupId)
    {
        var uid = UserId;
        if (uid == null || !_data.IsGroupMember(groupId, uid)) return;
        if (_data.MarkGroupRead(uid, groupId) == 0) return;
        await Clients.Group(GroupGroupStatic(groupId)).SendAsync("GroupMessagesRead", uid, groupId);
    }

    public async Task SearchGroupMessages(string groupId, string query)
    {
        var uid = UserId;
        if (uid == null || string.IsNullOrWhiteSpace(query)) return;
        if (!_data.IsGroupMember(groupId, uid)) return;
        var results = _data.SearchGroupMessages(groupId, query.Trim());
        await Clients.Caller.SendAsync("GroupSearchResults", results);
    }

    public async Task EditGroupMessage(string groupId, string messageId, string newContent)
    {
        var uid = UserId;
        if (uid == null || string.IsNullOrWhiteSpace(newContent)) return;
        if (!_data.EditGroupMessage(groupId, messageId, uid, newContent.Trim())) return;
        await Clients.Group(GroupGroupStatic(groupId)).SendAsync(
            "GroupMessageEdited", messageId, newContent.Trim(), DateTime.UtcNow);
    }

    public async Task DeleteGroupMessage(string groupId, string messageId)
    {
        var uid = UserId;
        if (uid == null) return;
        if (!_data.DeleteGroupMessage(groupId, messageId, uid)) return;
        await Clients.Group(GroupGroupStatic(groupId)).SendAsync("GroupMessageDeleted", messageId);
    }

    public async Task GroupTyping(string groupId, string displayName)
    {
        var uid = UserId;
        if (uid == null) return;
        await Clients.OthersInGroup(GroupGroupStatic(groupId)).SendAsync("GroupUserTyping", uid, displayName);
    }

    public async Task GroupStopTyping(string groupId)
    {
        var uid = UserId;
        if (uid == null) return;
        await Clients.OthersInGroup(GroupGroupStatic(groupId)).SendAsync("GroupUserStoppedTyping", uid);
    }

    public async Task LeaveGroup(string groupId)
    {
        var uid = UserId;
        if (uid == null) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupGroupStatic(groupId));
        if (_data.RemoveMemberFromGroup(groupId, uid))
        {
            await Clients.Group(GroupGroupStatic(groupId)).SendAsync("GroupMemberLeft", uid, groupId);
            await Clients.Caller.SendAsync("GroupLeft", groupId);
        }
    }

    // ── Audio/video calls (WebRTC mesh over rooms) ──────
    private static string CallGroup(string roomId) => $"call_{roomId}";

    private static CallRoom? GetCallRoom(string roomId)
    {
        lock (_callLock) return _callRooms.TryGetValue(roomId, out var r) ? r : null;
    }

    private static bool IsInCall(string userId)
    {
        lock (_callLock) return _callRooms.Values.Any(r => r.Members.Contains(userId));
    }

    private static CallRoom NewRoom(string creatorId, string type, string? groupId)
    {
        var room = new CallRoom { Type = type, GroupId = groupId };
        lock (_callLock)
        {
            _callRooms[room.Id] = room;
            room.Members.Add(creatorId);
        }
        return room;
    }

    private static object MemberInfo(User? u) => new
    {
        userId = u?.Id,
        displayName = u?.DisplayName,
        avatarColor = u?.AvatarColor,
        avatarPath = u?.AvatarPath
    };

    private async Task LeaveAllCallRooms(string userId)
    {
        var notify = new List<string>();
        var toRemove = new List<string>();
        lock (_callLock)
        {
            foreach (var room in _callRooms.Values)
            {
                if (room.Members.Remove(userId))
                {
                    notify.Add(room.Id);
                    if (room.Members.Count == 0) toRemove.Add(room.Id);
                }
                room.Invitees.Remove(userId);
            }
            foreach (var id in toRemove) _callRooms.Remove(id);
        }
        foreach (var roomId in notify)
            await Clients.OthersInGroup(CallGroup(roomId)).SendAsync("CallUserLeft", userId, roomId);
    }

    public async Task StartCall(string toUserId, string type)
    {
        var uid = UserId;
        if (uid == null || (type != "audio" && type != "video") || uid == toUserId) return;
        if (IsInCall(uid)) return;

        if (!IsOnline(toUserId))
        {
            await Clients.Caller.SendAsync("CallOffline", toUserId);
            return;
        }
        if (IsInCall(toUserId))
        {
            await Clients.Caller.SendAsync("CallBusy", toUserId);
            return;
        }

        var room = NewRoom(uid, type, null);
        lock (_callLock) room.Invitees.Add(toUserId);
        await Groups.AddToGroupAsync(Context.ConnectionId, CallGroup(room.Id));
        await Clients.Caller.SendAsync("CallCreated", room.Id, type, toUserId);

        var me = _data.GetUserById(uid);
        await SendToUser(toUserId, "IncomingCall", new
        {
            roomId = room.Id,
            type,
            groupId = (string?)null,
            groupName = (string?)null,
            fromId = uid,
            fromName = me?.DisplayName,
            fromAvatar = me?.AvatarPath,
            fromColor = me?.AvatarColor
        });
    }

    public async Task CreateGroupCall(string groupId, string type)
    {
        var uid = UserId;
        if (uid == null || (type != "audio" && type != "video")) return;
        var group = _data.GetGroup(groupId);
        if (group == null || !group.MemberIds.Contains(uid)) return;

        var room = NewRoom(uid, type, groupId);
        lock (_callLock)
        {
            room.GroupName = group.Name;
            foreach (var m in group.MemberIds)
                if (m != uid) room.Invitees.Add(m);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, CallGroup(room.Id));
        await Clients.Caller.SendAsync("CallCreated", room.Id, type, groupId);

        var me = _data.GetUserById(uid);
        var payload = new
        {
            roomId = room.Id,
            type,
            groupId,
            groupName = group.Name,
            fromId = uid,
            fromName = me?.DisplayName,
            fromAvatar = me?.AvatarPath,
            fromColor = me?.AvatarColor
        };
        foreach (var m in group.MemberIds)
        {
            if (m == uid || IsInCall(m)) continue;
            await SendToUser(m, "IncomingCall", payload);
        }
    }

    public async Task JoinCallRoom(string roomId)
    {
        var uid = UserId;
        if (uid == null) return;

        var room = GetCallRoom(roomId);
        if (room == null)
        {
            await Clients.Caller.SendAsync("CallEnded", roomId);
            return;
        }

        lock (_callLock)
        {
            if (room.Members.Contains(uid))
            {
                // already in
            }
            else if (room.Invitees.Contains(uid) || (room.GroupId != null && _data.IsGroupMember(room.GroupId, uid)))
            {
                room.Members.Add(uid);
                room.Invitees.Remove(uid);
            }
            else
            {
                return;
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, CallGroup(roomId));

        List<object> members;
        lock (_callLock)
            members = room.Members.Select(id => MemberInfo(_data.GetUserById(id))).ToList();

        await Clients.Caller.SendAsync("CallRoomJoined", roomId, members);
        await Clients.OthersInGroup(CallGroup(roomId)).SendAsync("CallUserJoined", MemberInfo(_data.GetUserById(uid)));
    }

    public async Task LeaveCallRoom(string roomId)
    {
        var uid = UserId;
        if (uid == null) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, CallGroup(roomId));

        List<string> pendingInvitees = new();
        var empty = false;
        lock (_callLock)
        {
            if (_callRooms.TryGetValue(roomId, out var room))
            {
                room.Members.Remove(uid);
                room.Invitees.Remove(uid);
                pendingInvitees = room.Invitees.ToList();
                empty = room.Members.Count == 0;
                if (empty) _callRooms.Remove(roomId);
            }
            else return;
        }

        await Clients.OthersInGroup(CallGroup(roomId)).SendAsync("CallUserLeft", uid, roomId);
        if (empty && pendingInvitees.Count > 0)
            await SendToUsers(pendingInvitees, "CallCancelled", roomId);
    }

    public async Task DeclineCall(string roomId)
    {
        var uid = UserId;
        if (uid == null) return;
        lock (_callLock)
        {
            if (!_callRooms.TryGetValue(roomId, out var room)) return;
            room.Invitees.Remove(uid);
        }
        await Clients.OthersInGroup(CallGroup(roomId)).SendAsync("CallDeclined", uid, roomId);
    }

    public async Task InviteToCall(string friendId, string roomId)
    {
        var uid = UserId;
        if (uid == null || uid == friendId) return;

        var room = GetCallRoom(roomId);
        if (room == null) return;
        lock (_callLock)
        {
            if (!room.Members.Contains(uid)) return;
            if (room.Members.Contains(friendId) || room.Invitees.Contains(friendId)) return;
        }
        if (!_data.GetFriendIds(uid).Contains(friendId)) return;

        if (!IsOnline(friendId))
        {
            await Clients.Caller.SendAsync("CallOffline", friendId);
            return;
        }
        if (IsInCall(friendId))
        {
            await Clients.Caller.SendAsync("CallBusy", friendId);
            return;
        }

        lock (_callLock) room.Invitees.Add(friendId);

        var me = _data.GetUserById(uid);
        await SendToUser(friendId, "IncomingCall", new
        {
            roomId = room.Id,
            type = room.Type,
            groupId = room.GroupId,
            groupName = room.GroupName,
            fromId = uid,
            fromName = me?.DisplayName,
            fromAvatar = me?.AvatarPath,
            fromColor = me?.AvatarColor
        });
    }

    public async Task CallSignalRoom(string roomId, string toUserId, CallMessage msg)
    {
        var uid = UserId;
        if (uid == null || msg == null) return;
        var room = GetCallRoom(roomId);
        if (room == null) return;
        lock (_callLock)
        {
            if (!room.Members.Contains(uid) || !room.Members.Contains(toUserId)) return;
        }
        await SendToUser(toUserId, "CallRoomSignal", new { roomId, from = uid, message = msg });
    }

    public static string GroupStatic(string a, string b)
    {
        var arr = new[] { a, b };
        Array.Sort(arr);
        return $"chat_{arr[0]}_{arr[1]}";
    }

    private bool IsValidStickerPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!path.StartsWith("/stickers/", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.Contains("..")) return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not ".png" and not ".webp" and not ".gif") return false;

        var full = Path.GetFullPath(Path.Combine(
            _env.WebRootPath, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(_env.WebRootPath, StringComparison.OrdinalIgnoreCase) && File.Exists(full);
    }

}
