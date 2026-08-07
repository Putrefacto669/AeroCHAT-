using AeroChat.Models;

namespace AeroChat.Services;

public static class UploadValidation
{
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".shtml", ".xhtml", ".svg", ".svgz", ".xml", ".xsl", ".xslt",
        ".js", ".mjs", ".json", ".php", ".phtml", ".asp", ".aspx", ".jsp", ".cgi",
        ".pl", ".py", ".rb", ".sh", ".bash", ".zsh", ".bat", ".cmd", ".ps1", ".vbs",
        ".jsx", ".ts", ".wasm", ".jar", ".swf", ".exe", ".msi", ".dll", ".so", ".dylib", ".apk"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".ogg", ".wav", ".m4a", ".aac", ".flac", ".opus"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".ogv", ".mov", ".mkv", ".avi", ".m4v"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".txt", ".md", ".csv", ".zip", ".rar", ".7z", ".tar", ".gz",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".rtf"
    };

    public static bool IsBlockedExtension(string ext) => BlockedExtensions.Contains(ext);

    public static bool IsImageExtension(string ext)
        => ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp";

    public static bool IsAllowedExtension(string ext)
        => IsImageExtension(ext) ||
           AudioExtensions.Contains(ext) ||
           VideoExtensions.Contains(ext) ||
           DocumentExtensions.Contains(ext);

    public static bool HasValidImageSignature(string ext, byte[] head)
    {
        switch (ext.ToLowerInvariant())
        {
            case ".jpg":
            case ".jpeg":
                return head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF;
            case ".png":
                return head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 &&
                       head[2] == 0x4E && head[3] == 0x47;
            case ".gif":
                return head.Length >= 4 && head[0] == (byte)'G' && head[1] == (byte)'I' &&
                       head[2] == (byte)'F' && head[3] == (byte)'8';
            case ".webp":
                return head.Length >= 12 && head[0] == (byte)'R' && head[1] == (byte)'I' &&
                       head[2] == (byte)'F' && head[3] == (byte)'F' &&
                       head[8] == (byte)'W' && head[9] == (byte)'E' &&
                       head[10] == (byte)'B' && head[11] == (byte)'P';
            default:
                return false;
        }
    }

    public static MessageType GetMessageType(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => MessageType.Image,
        ".mp3" or ".ogg" or ".wav" or ".m4a" or ".aac" or ".flac" or ".opus" => MessageType.Audio,
        ".mp4" or ".webm" or ".ogv" or ".mov" or ".mkv" or ".avi" or ".m4v" => MessageType.Video,
        _ => MessageType.Document
    };
}
