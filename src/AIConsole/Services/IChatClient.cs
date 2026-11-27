namespace AIConsole.Services;

/// <summary>Chat/model operations used by the chat view model; fakeable in tests.</summary>
public interface IChatClient
{
    Task<IReadOnlyList<string>> ListModelsAsync(string baseUrl, string? apiKey, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamChatAsync(
        string baseUrl, string? apiKey, string model, IReadOnlyList<ChatMessageDto> messages,
        CancellationToken ct = default);

    Task<string> DecideAsync(string baseUrl, string? apiKey, string model, string state,
        string type, string instructions, IReadOnlyList<string> criteria, CancellationToken ct = default);
}
