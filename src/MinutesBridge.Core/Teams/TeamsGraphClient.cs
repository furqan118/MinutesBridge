using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using MinutesBridge.Core.Http;

namespace MinutesBridge.Core.Teams;

public sealed class TeamsGraphClient(HttpClient httpClient)
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private const int MaximumChats = 100;
    private const int MaximumMessages = 50;
    private static readonly Uri ChatsUri = new("https://graph.microsoft.com/v1.0/me/chats?$top=50");

    public async Task<IReadOnlyList<TeamsMeetingChat>> GetMeetingChatsAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ValidateAccessToken(accessToken);
        var chats = new List<TeamsMeetingChat>();
        Uri? next = ChatsUri;

        while (next is not null && chats.Count < MaximumChats)
        {
            var page = await GetAsync<ChatCollection>(next, accessToken, cancellationToken).ConfigureAwait(false);
            foreach (var chat in page.Value ?? [])
            {
                if (chats.Count >= MaximumChats)
                {
                    break;
                }

                if (!string.Equals(chat.ChatType, "meeting", StringComparison.OrdinalIgnoreCase) ||
                    !IsSafeOpaqueIdentifier(chat.Id) ||
                    string.IsNullOrWhiteSpace(chat.Topic))
                {
                    continue;
                }

                var topic = chat.Topic.Trim();
                if (topic.Length <= 200 && !topic.Any(char.IsControl))
                {
                    chats.Add(new TeamsMeetingChat(chat.Id, topic, chat.LastUpdatedDateTime));
                }
            }

            next = ParseNextLink(page.NextLink);
        }

        return chats
            .OrderByDescending(chat => chat.LastUpdatedDateTime)
            .ToArray();
    }

    public async Task<IReadOnlyList<TeamsChatMessage>> GetRecentMessagesAsync(
        string accessToken,
        string chatId,
        CancellationToken cancellationToken = default)
    {
        ValidateAccessToken(accessToken);
        if (!IsSafeOpaqueIdentifier(chatId))
        {
            throw new ArgumentException("The Teams chat identifier is invalid.", nameof(chatId));
        }

        var uri = new Uri(
            $"https://graph.microsoft.com/v1.0/chats/{Uri.EscapeDataString(chatId)}/messages?$top={MaximumMessages}&$orderby=createdDateTime%20desc");
        var page = await GetAsync<MessageCollection>(uri, accessToken, cancellationToken).ConfigureAwait(false);

        return (page.Value ?? [])
            .Where(message => IsSafeOpaqueIdentifier(message.Id) &&
                              message.Body is not null &&
                              message.Body.Content is not null &&
                              message.Body.Content.Length <= TeamsSummaryExtractor.MaximumMessageCharacters)
            .Select(message => new TeamsChatMessage(
                message.Id,
                message.Body!.Content!,
                message.Body.ContentType ?? "text",
                message.CreatedDateTime))
            .OrderByDescending(message => message.CreatedDateTime)
            .Take(MaximumMessages)
            .ToArray();
    }

    private async Task<T> GetAsync<T>(
        Uri uri,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await BoundedJsonContent.ReadAsync<T>(
            response.Content,
            MaximumResponseBytes,
            cancellationToken).ConfigureAwait(false);
    }

    private static Uri? ParseNextLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Equals("/v1.0/me/chats", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Microsoft Graph returned an invalid continuation address.");
        }

        return uri;
    }

    private static bool IsSafeOpaqueIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 1024 &&
        !value.Any(char.IsControl);

    private static void ValidateAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) ||
            accessToken.Length > 16_384 ||
            accessToken.Any(char.IsControl))
        {
            throw new ArgumentException("The Microsoft access token is invalid.", nameof(accessToken));
        }
    }

    private sealed record ChatCollection(
        [property: JsonPropertyName("value")] IReadOnlyList<ChatResponse>? Value,
        [property: JsonPropertyName("@odata.nextLink")] string? NextLink);

    private sealed record ChatResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("topic")] string? Topic,
        [property: JsonPropertyName("chatType")] string? ChatType,
        [property: JsonPropertyName("lastUpdatedDateTime")] DateTimeOffset LastUpdatedDateTime);

    private sealed record MessageCollection(
        [property: JsonPropertyName("value")] IReadOnlyList<MessageResponse>? Value);

    private sealed record MessageResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("createdDateTime")] DateTimeOffset CreatedDateTime,
        [property: JsonPropertyName("body")] MessageBody? Body);

    private sealed record MessageBody(
        [property: JsonPropertyName("contentType")] string? ContentType,
        [property: JsonPropertyName("content")] string? Content);
}
