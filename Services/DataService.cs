using System.Text.Json;
using AeroChat.Models;

namespace AeroChat.Services;

public class DataService
{
    private readonly string _dataPath;
    private readonly string _usersFile;
    private readonly string _messagesFile;
    private readonly string _groupsFile;
    private readonly string _statusesFile;
    private readonly JsonSerializerOptions _opts;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DataService(IWebHostEnvironment env)
    {
        _dataPath = Path.Combine(env.ContentRootPath, "Data");
        _usersFile = Path.Combine(_dataPath, "users.json");
        _messagesFile = Path.Combine(_dataPath, "messages.json");
        _groupsFile = Path.Combine(_dataPath, "groups.json");
        _statusesFile = Path.Combine(_dataPath, "statuses.json");
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

    public User? GetUserByUsername(string username)
        => GetUsers().FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

    public User? ValidateLogin(string username, string password)
        => GetUsers().FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase) &&
            u.Password == password);

    public bool UpdateUser(User updated)
    {
        lock (_lock)
        {
            var users = GetUsers();
            var idx = users.FindIndex(u => u.Id == updated.Id);
            if (idx < 0) return false;
            users[idx] = updated;
            SaveUsers(users);
            return true;
        }
    }

    public User? RegisterUser(string username, string displayName, string password)
    {
        lock (_lock)
        {
            var users = GetUsers();
            if (users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                return null;

            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = username.ToLower(),
                DisplayName = displayName,
                Password = password,
                AvatarColor = $"#{Random.Shared.Next(0x1000000):X6}"
            };
            users.Add(user);
            SaveUsers(users);
            return user;
        }
    }

    // ── FRIENDS ────────────────────────────────────────
    public FriendState GetFriendState(string userId, string otherId)
    {
        var users = GetUsers();
        var me = users.FirstOrDefault(u => u.Id == userId);
        var other = users.FirstOrDefault(u => u.Id == otherId);
        if (me == null || other == null) return FriendState.None;

        if (me.FriendIds.Contains(otherId) || other.FriendIds.Contains(userId))
            return FriendState.Friends;
        if (other.FriendRequests.Any(r => r.FromUserId == userId))
            return FriendState.Outgoing;
        if (me.FriendRequests.Any(r => r.FromUserId == otherId))
            return FriendState.Incoming;
        return FriendState.None;
    }

    public string? GetIncomingRequestId(string userId, string otherId)
        => GetUserById(userId)?.FriendRequests.FirstOrDefault(r => r.FromUserId == otherId)?.Id;

    public List<string> GetFriendIds(string userId)
    {
        var me = GetUserById(userId);
        if (me == null) return new();
        return me.FriendIds.Where(id => GetUserById(id) != null).ToList();
    }

    public List<SidebarUserItem> GetSidebarItems(string userId)
    {
        var me = GetUserById(userId);
        if (me == null) return new();
        var items = new List<SidebarUserItem>();
        foreach (var u in GetUsers().Where(x => x.Id != userId))
        {
            var state = GetFriendState(userId, u.Id);
            var item = new SidebarUserItem { User = u, State = state.ToString().ToLowerInvariant() };
            if (state == FriendState.Incoming)
                item.RequestId = me.FriendRequests.FirstOrDefault(r => r.FromUserId == u.Id)?.Id ?? "";
            items.Add(item);
        }
        return items;
    }

    public FriendRequestResult SendFriendRequest(string fromId, string toId)
    {
        if (fromId == toId) return FriendRequestResult.Self;
        lock (_lock)
        {
            var users = GetUsers();
            var from = users.FirstOrDefault(u => u.Id == fromId);
            var to = users.FirstOrDefault(u => u.Id == toId);
            if (from == null || to == null) return FriendRequestResult.NotFound;
            if (from.FriendIds.Contains(toId) || to.FriendIds.Contains(fromId))
                return FriendRequestResult.AlreadyFriends;
            if (to.FriendRequests.Any(r => r.FromUserId == fromId))
                return FriendRequestResult.Pending;
            if (from.FriendRequests.Any(r => r.FromUserId == toId))
                return FriendRequestResult.Pending;

            to.FriendRequests.Add(new FriendRequest
            {
                FromUserId = fromId,
                ToUserId = toId,
                CreatedAt = DateTime.UtcNow
            });
            SaveUsers(users);
            return FriendRequestResult.Sent;
        }
    }

    public string? AcceptFriendRequest(string userId, string requestId)
    {
        lock (_lock)
        {
            var users = GetUsers();
            var me = users.FirstOrDefault(u => u.Id == userId);
            if (me == null) return null;
            var req = me.FriendRequests.FirstOrDefault(r => r.Id == requestId);
            if (req == null) return null;
            var sender = users.FirstOrDefault(u => u.Id == req.FromUserId);
            if (sender == null) return null;

            me.FriendRequests.Remove(req);
            if (!me.FriendIds.Contains(sender.Id)) me.FriendIds.Add(sender.Id);
            if (!sender.FriendIds.Contains(me.Id)) sender.FriendIds.Add(me.Id);
            SaveUsers(users);
            return sender.Id;
        }
    }

    public string? DeclineFriendRequest(string userId, string requestId)
    {
        lock (_lock)
        {
            var users = GetUsers();
            var me = users.FirstOrDefault(u => u.Id == userId);
            if (me == null) return null;
            var req = me.FriendRequests.FirstOrDefault(r => r.Id == requestId);
            if (req == null) return null;
            me.FriendRequests.Remove(req);
            SaveUsers(users);
            return req.FromUserId;
        }
    }

    public bool CancelFriendRequest(string fromId, string toId)
    {
        lock (_lock)
        {
            var users = GetUsers();
            var to = users.FirstOrDefault(u => u.Id == toId);
            if (to == null) return false;
            var removed = to.FriendRequests.RemoveAll(r => r.FromUserId == fromId && r.ToUserId == toId) > 0;
            if (removed) SaveUsers(users);
            return removed;
        }
    }

    public bool RemoveFriend(string userId, string friendId)
    {
        lock (_lock)
        {
            var users = GetUsers();
            var me = users.FirstOrDefault(u => u.Id == userId);
            var friend = users.FirstOrDefault(u => u.Id == friendId);
            if (me == null || friend == null) return false;
            var removed = me.FriendIds.Remove(friendId) | friend.FriendIds.Remove(userId);
            if (removed) SaveUsers(users);
            return removed;
        }
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
        msg.EditedAt = DateTime.UtcNow;
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

    private void SaveUsers(List<User> users)
        => File.WriteAllText(_usersFile, JsonSerializer.Serialize(users, _opts));

    // ── GROUPS ────────────────────────────────────────
    public List<Group> GetGroups()
    {
        if (!File.Exists(_groupsFile)) return new();
        var json = File.ReadAllText(_groupsFile);
        return JsonSerializer.Deserialize<List<Group>>(json, _opts) ?? new();
    }

    private void SaveGroups(List<Group> groups)
        => File.WriteAllText(_groupsFile, JsonSerializer.Serialize(groups, _opts));

    public Group? GetGroup(string id) => GetGroups().FirstOrDefault(g => g.Id == id);

    public List<Group> GetGroupsForUser(string userId)
        => GetGroups().Where(g => g.MemberIds.Contains(userId)).OrderBy(g => g.Name).ToList();

    public bool IsGroupMember(string groupId, string userId)
        => GetGroup(groupId)?.MemberIds.Contains(userId) ?? false;

    public Group? CreateGroup(string name, string ownerId, List<string> memberIds)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        lock (_lock)
        {
            var groups = GetGroups();
            var members = memberIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            if (!members.Contains(ownerId)) members.Add(ownerId);
            var group = new Group { Name = name.Trim(), OwnerId = ownerId, MemberIds = members };
            groups.Add(group);
            SaveGroups(groups);
            return group;
        }
    }

    public bool RemoveMemberFromGroup(string groupId, string memberId)
    {
        lock (_lock)
        {
            var groups = GetGroups();
            var g = groups.FirstOrDefault(x => x.Id == groupId);
            if (g == null || !g.MemberIds.Remove(memberId)) return false;
            if (g.MemberIds.Count == 0) groups.Remove(g);
            SaveGroups(groups);
            return true;
        }
    }

    public List<User> GetGroupMembers(Group group)
    {
        var users = GetUsers();
        return group.MemberIds
            .Select(id => users.FirstOrDefault(u => u.Id == id))
            .Where(u => u != null)
            .Select(u => u!)
            .ToList();
    }

    public List<Message> GetGroupMessages(string groupId)
        => GetMessages()
            .Where(m => m.ReceiverId == groupId && !m.IsDeleted)
            .OrderBy(m => m.CreatedAt)
            .ToList();

    public Message AddGroupMessage(Message message)
    {
        var messages = GetMessages();
        messages.Add(message);
        SaveMessages(messages);
        return message;
    }

    public bool EditGroupMessage(string groupId, string messageId, string userId, string newContent)
    {
        var messages = GetMessages();
        var msg = messages.FirstOrDefault(m =>
            m.Id == messageId && m.ReceiverId == groupId && m.SenderId == userId);
        if (msg == null || msg.Type != MessageType.Text) return false;
        msg.Content = newContent;
        msg.EditedAt = DateTime.UtcNow;
        SaveMessages(messages);
        return true;
    }

    public bool DeleteGroupMessage(string groupId, string messageId, string userId)
    {
        var messages = GetMessages();
        var msg = messages.FirstOrDefault(m =>
            m.Id == messageId && m.ReceiverId == groupId && m.SenderId == userId);
        if (msg == null) return false;
        msg.IsDeleted = true;
        msg.Content = "Mensaje eliminado";
        SaveMessages(messages);
        return true;
    }

    // ── STATUSES ──────────────────────────────────────────
    public List<Status> GetStatuses()
    {
        if (!File.Exists(_statusesFile)) return new();
        var json = File.ReadAllText(_statusesFile);
        return JsonSerializer.Deserialize<List<Status>>(json, _opts) ?? new();
    }

    private void SaveStatuses(List<Status> statuses)
        => File.WriteAllText(_statusesFile, JsonSerializer.Serialize(statuses, _opts));

    public Status? AddStatus(Status status)
    {
        lock (_lock)
        {
            var statuses = GetStatuses();
            statuses.Add(status);
            SaveStatuses(statuses);
            return status;
        }
    }

    public bool DeleteStatus(string userId, string statusId)
    {
        lock (_lock)
        {
            var statuses = GetStatuses();
            var s = statuses.FirstOrDefault(x => x.Id == statusId && x.UserId == userId);
            if (s == null) return false;
            statuses.Remove(s);
            SaveStatuses(statuses);
            return true;
        }
    }

    public List<Status> GetVisibleStatuses(string userId)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var friendIds = GetFriendIds(userId);
        return GetStatuses()
            .Where(s => s.CreatedAt >= cutoff && (s.UserId == userId || friendIds.Contains(s.UserId)))
            .OrderBy(s => s.UserId)
            .ThenBy(s => s.CreatedAt)
            .ToList();
    }

    // ── HELPERS ────────────────────────────────────────────
    public static string? ExtractYoutubeId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        if (uri.Host.Contains("youtu.be"))
            return uri.AbsolutePath.Trim('/');

        if (uri.Host.Contains("youtube.com") || uri.Host.Contains("m.youtube.com"))
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return query["v"] ?? uri.AbsolutePath.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s));
        }

        return null;
    }
}
