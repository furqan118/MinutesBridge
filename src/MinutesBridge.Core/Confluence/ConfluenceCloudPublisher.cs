using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json.Serialization;
using MinutesBridge.Core.Http;

namespace MinutesBridge.Core.Confluence;

public sealed class ConfluenceCloudPublisher(HttpClient httpClient, string cloudId) : IConfluencePublisher
{
    private const int MaximumResponseBytes = 64 * 1024;
    private readonly string _cloudId = ValidateCloudId(cloudId);

    public async Task<PublishedPage> CreatePageAsync(
        ConfluencePageRequest page,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        Validate(page, accessToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.atlassian.com/ex/confluence/{Uri.EscapeDataString(_cloudId)}/wiki/api/v2/pages");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            spaceId = page.SpaceId,
            status = page.SaveAsDraft ? "draft" : "current",
            title = page.Title,
            parentId = page.ParentPageId,
            body = new { representation = "storage", value = page.StorageBody }
        });

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await BoundedJsonContent.ReadAsync<CreatePageResponse>(
            response.Content,
            MaximumResponseBytes,
            cancellationToken).ConfigureAwait(false);

        var webUi = result.Links?.WebUi;
        if (!IsNumericIdentifier(result.Id) ||
            string.IsNullOrWhiteSpace(webUi) ||
            !Uri.TryCreate(webUi, UriKind.RelativeOrAbsolute, out var webUri))
        {
            throw new InvalidDataException("Confluence returned an invalid page response.");
        }

        return new PublishedPage(result.Id, webUri);
    }

    private static void Validate(ConfluencePageRequest page, string accessToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!IsNumericIdentifier(page.SpaceId) || !IsNumericIdentifier(page.ParentPageId))
        {
            throw new ArgumentException("The Confluence destination is invalid.", nameof(page));
        }

        if (string.IsNullOrWhiteSpace(page.Title) || page.Title.Length > 255 || page.Title.Any(char.IsControl))
        {
            throw new ArgumentException("The Confluence page title is invalid.", nameof(page));
        }

        if (string.IsNullOrWhiteSpace(page.StorageBody) || page.StorageBody.Length > 1_000_000)
        {
            throw new ArgumentException("The Confluence page body is empty or too large.", nameof(page));
        }

        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 4096 || accessToken.Any(char.IsControl))
        {
            throw new ArgumentException("The access token is invalid.", nameof(accessToken));
        }
    }

    private static string ValidateCloudId(string cloudId)
    {
        if (!Guid.TryParse(cloudId, out var value))
        {
            throw new ArgumentException("The Confluence cloud ID is invalid.", nameof(cloudId));
        }

        return value.ToString("D", CultureInfo.InvariantCulture);
    }

    private static bool IsNumericIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value.All(char.IsAsciiDigit);

    private sealed record CreatePageResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("_links")] ResponseLinks? Links);

    private sealed record ResponseLinks(
        [property: JsonPropertyName("webui")] string WebUi);
}
