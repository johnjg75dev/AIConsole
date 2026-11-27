namespace AIConsole.Models;

public enum ChatRole { System, User, Assistant }

public sealed class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New chat";
    public string? Model { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class ChatMessage
{
    public long Id { get; set; }
    public string ConversationId { get; set; } = "";
    public ChatRole Role { get; set; }
    public string Content { get; set; } = "";
    public string? ModelId { get; set; }
    public string? Participant { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
}

/// <summary>A remote OpenAI-compatible provider.</summary>
public sealed class Endpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "OpenAI";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    /// <summary>Optional API key. Stored DPAPI-encrypted; null/empty = no auth.</summary>
    public string? ApiKey { get; set; }
    /// <summary>Periodically re-sync ALL models from this endpoint; picker mirrors the server.</summary>
    public bool ShowAllModels { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class ModelInfo
{
    public long Id { get; set; }
    public string EndpointId { get; set; } = "";
    public string ModelId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.Now;
    /// <summary>Endpoint name for display; joined at query time.</summary>
    public string EndpointName { get; set; } = "";

    /// <summary>Unique picker key; same-named models on different endpoints don't collide.</summary>
    public string Key => EndpointId + "::" + ModelId;
}

public enum SlideEdge { Top, Left, Right }

public sealed class AppSettings
{
    // Popup
    public SlideEdge SlideEdge { get; set; } = SlideEdge.Top;
    public bool HideOnFocusLoss { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+`";
    public bool StartWithWindows { get; set; }
    public bool PanelAcrylic { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;      // popup stays above other windows
    public double PanelWidth { get; set; }             // 0 = auto; Top slide defaults to full screen width
    public double PanelHeight { get; set; } = 600;     // panel height (px)
    public bool NotifyOnCompletion { get; set; } = true; // Windows notification when a reply finishes

    // Model refresh
    public int RefreshMinutes { get; set; } = 360; // 6 hours

    // Chat
    public string? LastModelKey { get; set; }
    public bool EnterToSend { get; set; } = true;

    // Appearance
    public string Theme { get; set; } = "Midnight Neon";
    public string? AccentOverride { get; set; }
    public bool FollowWindowsTheme { get; set; }
    public bool ShowLineNumbers { get; set; } = true;
    public string CodeFont { get; set; } = "Cascadia Code";
}
