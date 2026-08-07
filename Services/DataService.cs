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
    private readonly string _stickerLibsFile;
    private readonly IFileStorage _storage;
    private readonly JsonSerializerOptions _opts;
    private readonly object _lock = new();

    private readonly List<User> _users;
    private readonly List<Message> _messages;
    private readonly List<Group> _groups;
    private readonly List<Status> _statuses;
    private readonly List<StickerLibrary> _stickerLibs;

    public DataService(IWebHostEnvironment env, IFileStorage storage)
    {
        _dataPath = Path.Combine(env.ContentRootPath, "Data");
        _usersFile = Path.Combine(_dataPath, "users.json");
        _messagesFile = Path.Combine(_dataPath, "messages.json");
        _groupsFile = Path.Combine(_dataPath, "groups.json");
        _statusesFile = Path.Combine(_dataPath, "statuses.json");
        _stickerLibsFile = Path.Combine(_dataPath, "stickerlibs.json");
        _storage = storage;
        _opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        _users = LoadList<User>(_usersFile);
        _messages = LoadList<Message>(_messagesFile);
        _groups = LoadList<Group>(_groupsFile);
        _statuses = LoadList<Status>(_statusesFile);
        _stickerLibs = LoadList<StickerLibrary>(_stickerLibsFile);
    }

    private List<T> LoadList<T>(string file)
    {
        try
        {
            if (!File.Exists(file)) return new();
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(file), _opts) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void SaveList<T>(string file, List<T> data)
    {
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, _opts));
        if (File.Exists(file)) File.Move(tmp, file, true);
        else File.Move(tmp, file);
    }

    // ── USERS ──────────────────────────────────────────────
    public List<User> GetUsers()
    {
        lock (_lock) return _users.ToList();
    }

    public User? GetUserById(string id)
    {
        lock (_lock) return _users.FirstOrDefault(u => u.Id == id);
    }

    public User? GetUserByUsername(string username)
    {
        lock (_lock)
            return _users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    public User? ValidateLogin(string username, string password)
    {
        lock (_lock)
            return _users.FirstOrDefault(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase) &&
                u.Password == password);
    }

    public bool UpdateUser(User updated)
    {
        lock (_lock)
        {
            var idx = _users.FindIndex(u => u.Id == updated.Id);
            if (idx < 0) return false;
            _users[idx] = updated;
            SaveList(_usersFile, _users);
            return true;
        }
    }

    public User? RegisterUser(string username, string displayName, string password)
    {
        lock (_lock)
        {
            if (_users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                return null;

            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = username.ToLower(),
                DisplayName = displayName,
                Password = password,
                AvatarColor = $"#{Random.Shared.Next(0x1000000):X6}"
            };
            _users.Add(user);
            SaveList(_usersFile, _users);
            return user;
        }
    }

    // ── FRIENDS ────────────────────────────────────────
    public FriendState GetFriendState(string userId, string otherId)
    {
        lock (_lock)
        {
            var me = _users.FirstOrDefault(u => u.Id == userId);
            var other = _users.FirstOrDefault(u => u.Id == otherId);
            if (me == null || other == null) return FriendState.None;

            if (me.FriendIds.Contains(otherId) || other.FriendIds.Contains(userId))
                return FriendState.Friends;
            if (other.FriendRequests.Any(r => r.FromUserId == userId))
                return FriendState.Outgoing;
            if (me.FriendRequests.Any(r => r.FromUserId == otherId))
                return FriendState.Incoming;
            return FriendState.None;
        }
    }

    public string? GetIncomingRequestId(string userId, string otherId)
    {
        lock (_lock)
            return _users.FirstOrDefault(u => u.Id == userId)?
                .FriendRequests.FirstOrDefault(r => r.FromUserId == otherId)?.Id;
    }

    public List<string> GetFriendIds(string userId)
    {
        lock (_lock)
        {
            var me = _users.FirstOrDefault(u => u.Id == userId);
            if (me == null) return new();
            return me.FriendIds.Where(id => _users.Any(u => u.Id == id)).ToList();
        }
    }

    public List<SidebarUserItem> GetSidebarItems(string userId)
    {
        lock (_lock)
        {
            var me = _users.FirstOrDefault(u => u.Id == userId);
            if (me == null) return new();
            var items = new List<SidebarUserItem>();
            foreach (var u in _users.Where(x => x.Id != userId))
            {
                var state = GetFriendState(userId, u.Id);
                var item = new SidebarUserItem { User = u, State = state.ToString().ToLowerInvariant() };
                if (state == FriendState.Incoming)
                    item.RequestId = me.FriendRequests.FirstOrDefault(r => r.FromUserId == u.Id)?.Id ?? "";
                items.Add(item);
            }
            return items;
        }
    }

    public FriendRequestResult SendFriendRequest(string fromId, string toId)
    {
        if (fromId == toId) return FriendRequestResult.Self;
        lock (_lock)
        {
            var from = _users.FirstOrDefault(u => u.Id == fromId);
            var to = _users.FirstOrDefault(u => u.Id == toId);
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
            SaveList(_usersFile, _users);
            return FriendRequestResult.Sent;
        }
    }

    public string? AcceptFriendRequest(string userId, string requestId)
    {
        lock (_lock)
        {
            var me = _users.FirstOrDefault(u => u.Id == userId);
            if (me == null) return null;
            var req = me.FriendRequests.FirstOrDefault(r => r.Id == requestId);
            if (req == null) return null;
            var sender = _users.FirstOrDefault(u => u.Id == req.FromUserId);
            if (sender == null) return null;

            me.FriendRequests.Remove(req);
            if (!me.FriendIds.Contains(sender.Id)) me.FriendIds.Add(sender.Id);
            if (!sender.FriendIds.Contains(me.Id)) sender.FriendIds.Add(me.Id);
            SaveList(_usersFile, _users);
            return sender.Id;
        }
    }

    public string? DeclineFriendRequest(string userId, string requestId)
    {
        lock (_lock)
        {
            var me = _users.FirstOrDefault(u => u.Id == userId);
            if (me == null) return null;
            var req = me.FriendRequests.FirstOrDefault(r => r.Id == requestId);
            if (req == null) return null;
            me.FriendRequests.Remove(req);
            SaveList(_usersFile, _users);
            return req.FromUserId;
        }
    }

    public bool CancelFriendRequest(string fromId, string toId)
    {
        lock (_lock)
        {
            var to = _users.FirstOrDefault(u => u.Id == toId);
            if (to == null) return false;
            var removed = to.FriendRequests.RemoveAll(r => r.FromUserId == fromId && r.ToUserId == toId) > 0;
            if (removed) SaveList(_usersFile, _users);
            return removed;
        }
    }

    public bool RemoveFriend(string userId, string friendId)
    {
        lock (_lock)
        {
            var me = _users.FirstOrDefault(u => u.Id == userId);
            var friend = _users.FirstOrDefault(u => u.Id == friendId);
            if (me == null || friend == null) return false;
            var removed = me.FriendIds.Remove(friendId) | friend.FriendIds.Remove(userId);
            if (removed) SaveList(_usersFile, _users);
            return removed;
        }
    }

    // ── MESSAGES ───────────────────────────────────────────
    public List<Message> GetMessages()
    {
        lock (_lock) return _messages.ToList();
    }

    public List<Message> GetConversation(string userId1, string userId2)
    {
        lock (_lock)
            return _messages
                .Where(m => !m.IsDeleted && m.Scope == MessageScope.Direct &&
                    ((m.SenderId == userId1 && m.ReceiverId == userId2) ||
                     (m.SenderId == userId2 && m.ReceiverId == userId1)))
                .OrderBy(m => m.CreatedAt)
                .ToList();
    }

    public Message AddMessage(Message message)
    {
        lock (_lock)
        {
            message.Scope = MessageScope.Direct;
            _messages.Add(message);
            SaveList(_messagesFile, _messages);
        }
        return message;
    }

    public bool EditMessage(string messageId, string userId, string newContent)
    {
        lock (_lock)
        {
            var msg = _messages.FirstOrDefault(m => m.Id == messageId && m.SenderId == userId);
            if (msg == null || msg.Type != MessageType.Text) return false;
            msg.Content = newContent;
            msg.EditedAt = DateTime.UtcNow;
            SaveList(_messagesFile, _messages);
            return true;
        }
    }

    public bool DeleteMessage(string messageId, string userId)
    {
        lock (_lock)
        {
            var msg = _messages.FirstOrDefault(m => m.Id == messageId && m.SenderId == userId);
            if (msg == null) return false;
            msg.IsDeleted = true;
            msg.Content = "Mensaje eliminado";
            if (msg.Type != MessageType.Sticker) DeleteUploadedFile(msg.FilePath);
            SaveList(_messagesFile, _messages);
            return true;
        }
    }

    // ── REACTIONS ─────────────────────────────────────────
    public (Message? Message, bool Added) ToggleReaction(string messageId, string userId, string emoji)
    {
        lock (_lock)
        {
            var msg = _messages.FirstOrDefault(m => m.Id == messageId && !m.IsDeleted);
            if (msg == null) return (null, false);
            var existing = msg.Reactions.FirstOrDefault(r => r.UserId == userId && r.Emoji == emoji);
            if (existing != null)
            {
                msg.Reactions.Remove(existing);
                SaveList(_messagesFile, _messages);
                return (msg, false);
            }
            msg.Reactions.Add(new Reaction { UserId = userId, Emoji = emoji, CreatedAt = DateTime.UtcNow });
            SaveList(_messagesFile, _messages);
            return (msg, true);
        }
    }

    // ── READ RECEIPTS / UNREAD ─────────────────────────────
    public int MarkConversationRead(string userId, string otherId)
    {
        lock (_lock)
        {
            var count = 0;
            foreach (var m in _messages.Where(m =>
                m.Scope == MessageScope.Direct && m.SenderId == otherId && m.ReceiverId == userId &&
                !m.IsDeleted && !m.ReadBy.Contains(userId)))
            {
                m.ReadBy.Add(userId);
                count++;
            }
            if (count > 0) SaveList(_messagesFile, _messages);
            return count;
        }
    }

    public int MarkGroupRead(string userId, string groupId)
    {
        lock (_lock)
        {
            var count = 0;
            foreach (var m in _messages.Where(m =>
                m.Scope == MessageScope.Group && m.ReceiverId == groupId &&
                m.SenderId != userId &&
                !m.IsDeleted && !m.ReadBy.Contains(userId)))
            {
                m.ReadBy.Add(userId);
                count++;
            }
            if (count > 0) SaveList(_messagesFile, _messages);
            return count;
        }
    }

    public Dictionary<string, int> GetUnreadCounts(string userId)
    {
        lock (_lock)
        {
            var counts = new Dictionary<string, int>();
            foreach (var m in _messages.Where(m => !m.IsDeleted && !m.ReadBy.Contains(userId)))
            {
                var key = m.Scope == MessageScope.Direct
                    ? m.SenderId
                    : m.ReceiverId;
                if (m.Scope == MessageScope.Direct && m.ReceiverId != userId) continue;
                if (m.Scope == MessageScope.Direct && m.SenderId == userId) continue;
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
            }
            return counts;
        }
    }

    // ── SEARCH ─────────────────────────────────────────────
    public List<Message> SearchConversation(string userId1, string userId2, string query)
    {
        lock (_lock)
            return _messages
                .Where(m => !m.IsDeleted && m.Scope == MessageScope.Direct &&
                    ((m.SenderId == userId1 && m.ReceiverId == userId2) ||
                     (m.SenderId == userId2 && m.ReceiverId == userId1)) &&
                    m.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.CreatedAt)
                .Take(30)
                .ToList();
    }

    public List<Message> SearchGroupMessages(string groupId, string query)
    {
        lock (_lock)
            return _messages
                .Where(m => !m.IsDeleted && m.Scope == MessageScope.Group &&
                    m.ReceiverId == groupId &&
                    m.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.CreatedAt)
                .Take(30)
                .ToList();
    }

    // ── GROUPS ────────────────────────────────────────
    public List<Group> GetGroups()
    {
        lock (_lock) return _groups.ToList();
    }

    public Group? GetGroup(string id)
    {
        lock (_lock) return _groups.FirstOrDefault(g => g.Id == id);
    }

    public List<Group> GetGroupsForUser(string userId)
    {
        lock (_lock)
            return _groups.Where(g => g.MemberIds.Contains(userId)).OrderBy(g => g.Name).ToList();
    }

    public bool IsGroupMember(string groupId, string userId)
    {
        lock (_lock) return _groups.FirstOrDefault(g => g.Id == groupId)?.MemberIds.Contains(userId) ?? false;
    }

    public Group? CreateGroup(string name, string ownerId, List<string> memberIds)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        lock (_lock)
        {
            var members = memberIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            if (!members.Contains(ownerId)) members.Add(ownerId);
            var group = new Group { Name = name.Trim(), OwnerId = ownerId, MemberIds = members };
            _groups.Add(group);
            SaveList(_groupsFile, _groups);
            return group;
        }
    }

    public bool RemoveMemberFromGroup(string groupId, string memberId)
    {
        lock (_lock)
        {
            var g = _groups.FirstOrDefault(x => x.Id == groupId);
            if (g == null || !g.MemberIds.Remove(memberId)) return false;
            if (g.MemberIds.Count == 0) _groups.Remove(g);
            SaveList(_groupsFile, _groups);
            return true;
        }
    }

    public bool AddGroupMember(string groupId, string userId)
    {
        lock (_lock)
        {
            var g = _groups.FirstOrDefault(x => x.Id == groupId);
            if (g == null || g.MemberIds.Contains(userId)) return false;
            g.MemberIds.Add(userId);
            SaveList(_groupsFile, _groups);
            return true;
        }
    }

    public bool RenameGroup(string groupId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        lock (_lock)
        {
            var g = _groups.FirstOrDefault(x => x.Id == groupId);
            if (g == null) return false;
            g.Name = newName.Trim();
            SaveList(_groupsFile, _groups);
            return true;
        }
    }

    public bool UpdateGroupAvatar(string groupId, string? path)
    {
        lock (_lock)
        {
            var g = _groups.FirstOrDefault(x => x.Id == groupId);
            if (g == null) return false;
            DeleteUploadedFile(g.AvatarPath);
            g.AvatarPath = path;
            SaveList(_groupsFile, _groups);
            return true;
        }
    }

    public List<User> GetGroupMembers(Group group)
    {
        lock (_lock)
            return group.MemberIds
                .Select(id => _users.FirstOrDefault(u => u.Id == id))
                .Where(u => u != null)
                .Select(u => u!)
                .ToList();
    }

    public List<Message> GetGroupMessages(string groupId)
    {
        lock (_lock)
            return _messages
                .Where(m => m.Scope == MessageScope.Group && m.ReceiverId == groupId && !m.IsDeleted)
                .OrderBy(m => m.CreatedAt)
                .ToList();
    }

    public Message AddGroupMessage(Message message)
    {
        lock (_lock)
        {
            message.Scope = MessageScope.Group;
            _messages.Add(message);
            SaveList(_messagesFile, _messages);
        }
        return message;
    }

    public bool EditGroupMessage(string groupId, string messageId, string userId, string newContent)
    {
        lock (_lock)
        {
            var msg = _messages.FirstOrDefault(m =>
                m.Id == messageId && m.ReceiverId == groupId && m.SenderId == userId);
            if (msg == null || msg.Type != MessageType.Text) return false;
            msg.Content = newContent;
            msg.EditedAt = DateTime.UtcNow;
            SaveList(_messagesFile, _messages);
            return true;
        }
    }

    public bool DeleteGroupMessage(string groupId, string messageId, string userId)
    {
        lock (_lock)
        {
            var msg = _messages.FirstOrDefault(m =>
                m.Id == messageId && m.ReceiverId == groupId && m.SenderId == userId);
            if (msg == null) return false;
            msg.IsDeleted = true;
            msg.Content = "Mensaje eliminado";
            if (msg.Type != MessageType.Sticker) DeleteUploadedFile(msg.FilePath);
            SaveList(_messagesFile, _messages);
            return true;
        }
    }

    // ── STATUSES ──────────────────────────────────────────
    public List<Status> GetStatuses()
    {
        lock (_lock) return _statuses.ToList();
    }

    public Status? AddStatus(Status status)
    {
        lock (_lock)
        {
            _statuses.Add(status);
            SaveList(_statusesFile, _statuses);
            return status;
        }
    }

    public bool DeleteStatus(string userId, string statusId)
    {
        lock (_lock)
        {
            var s = _statuses.FirstOrDefault(x => x.Id == statusId && x.UserId == userId);
            if (s == null) return false;
            _statuses.Remove(s);
            DeleteUploadedFile(s.FilePath);
            SaveList(_statusesFile, _statuses);
            return true;
        }
    }

    public List<Status> GetVisibleStatuses(string userId)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var friendIds = GetFriendIds(userId);
            return _statuses
                .Where(s => s.CreatedAt >= cutoff && (s.UserId == userId || friendIds.Contains(s.UserId)))
                .OrderBy(s => s.UserId)
                .ThenBy(s => s.CreatedAt)
                .ToList();
        }
    }

    public int CleanupExpiredStatuses()
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var expired = _statuses.Where(s => s.CreatedAt < cutoff).ToList();
            if (expired.Count == 0) return 0;
            foreach (var s in expired)
            {
                _statuses.Remove(s);
                DeleteUploadedFile(s.FilePath);
            }
            SaveList(_statusesFile, _statuses);
            return expired.Count;
        }
    }

    // ── FILE CLEANUP ──────────────────────────────────────
    private void DeleteUploadedFile(string? path) => _storage.Delete(path);

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

    // ── STICKER LIBRARY (favoritos, usos, nombres de paquete) ──
    public StickerLibrary GetStickerLibrary(string userId)
    {
        lock (_lock)
        {
            var lib = _stickerLibs.FirstOrDefault(s => s.UserId == userId);
            if (lib == null)
            {
                lib = new StickerLibrary { UserId = userId };
                _stickerLibs.Add(lib);
                SaveList(_stickerLibsFile, _stickerLibs);
            }
            return lib;
        }
    }

    public bool ToggleFavorite(string userId, string path)
    {
        lock (_lock)
        {
            var lib = GetStickerLibrary(userId);
            var fav = !lib.Favorites.Contains(path);
            if (fav) lib.Favorites.Add(path);
            else lib.Favorites.Remove(path);
            SaveList(_stickerLibsFile, _stickerLibs);
            return fav;
        }
    }

    public void RecordStickerUse(string userId, string path)
    {
        lock (_lock)
        {
            var lib = GetStickerLibrary(userId);
            lib.Usage.TryGetValue(path, out var n);
            lib.Usage[path] = n + 1;
            SaveList(_stickerLibsFile, _stickerLibs);
        }
    }

    public void SetPackName(string userId, string packId, string name)
    {
        lock (_lock)
        {
            var lib = GetStickerLibrary(userId);
            lib.PackNames[packId] = name;
            SaveList(_stickerLibsFile, _stickerLibs);
        }
    }
}
