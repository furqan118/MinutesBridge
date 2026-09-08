using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using MinutesBridge.Core.Http;

namespace MinutesBridge.Core.Confluence;

public sealed class ConfluenceCatalogClient(HttpClient httpClient, string cloudId) : IConfluenceCatalogClient
{
    private const int MaximumResponseBytes = 512 * 1024;
    private const int ResultLimit = 100;
    private readonly string _cloudId = ValidateCloudId(cloudId);

    public async Task<IReadOnlyList<ConfluenceSpace>> GetSpacesAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<SpaceCollection>(
            $"spaces?status=current&limit={ResultLimit}",
            accessToken,
            cancellationToken).ConfigureAwait(false);

        return (response.Results ?? [])
            .Where(item => IsSafeIdentifier(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => new ConfluenceSpace(item.Id, item.Key?.Trim() ?? string.Empty, item.Name.Trim()))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<ConfluencePageSummary>> GetRootPagesAsync(
        string accessToken,
        string spaceId,
        Uri siteUri,
        CancellationToken cancellationToken = default)
    {
        ValidateSpaceId(spaceId);
        ValidateSiteUri(siteUri);
        var response = await GetAsync<PageCollection>(
            $"pages?space-id={Uri.EscapeDataString(spaceId)}&depth=root&status=current&sort=title&limit={ResultLimit}",
            accessToken,
            cancellationToken).ConfigureAwait(false);

        return MapPages(response.Results ?? [], siteUri)
            .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<ConfluencePageSummary?> FindLatestMeetingPageAsync(
        string accessToken,
        string spaceId,
        string group,
        DateOnly beforeDate,
        Uri siteUri,
        CancellationToken cancellationToken = default)
    {
        ValidateSpaceId(spaceId);
        ValidateSiteUri(siteUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        var trimmedGroup = group.Trim();
        if (trimmedGroup.Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(group), "The meeting group must be 100 characters or fewer.");
        }

        // Newest pages are requested first and the response is capped. The title
        // date is still parsed rather than trusting remote creation order.
        var response = await GetAsync<PageCollection>(
            $"pages?space-id={Uri.EscapeDataString(spaceId)}&status=current&sort=-created-date&limit={ResultLimit}",
            accessToken,
            cancellationToken).ConfigureAwait(false);
        var suffix = $" {trimmedGroup}/Meeting Notes";

        return MapPages(response.Results ?? [], siteUri)
            .Select(page => (Page: page, Date: ParseMeetingDate(page.Title, suffix)))
            .Where(value => value.Date.HasValue && value.Date.Value < beforeDate)
            .OrderByDescending(value => value.Date)
            .Select(value => value.Page)
            .FirstOrDefault();
    }

    private async Task<T> GetAsync<T>(
        string relativePath,
        string accessToken,
        CancellationToken cancellationToken)
    {
        ValidateAccessToken(accessToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildApiUri(relativePath));
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

    private Uri BuildApiUri(string relativePath) => new(
        $"https://api.atlassian.com/ex/confluence/{Uri.EscapeDataString(_cloudId)}/wiki/api/v2/{relativePath}");

    private static IEnumerable<ConfluencePageSummary> MapPages(IEnumerable<PageResponse> pages, Uri siteUri)
    {
        foreach (var page in pages)
        {
            if (!IsSafeIdentifier(page.Id) || string.IsNullOrWhiteSpace(page.Title))
            {
                continue;
            }

            Uri? webUri = null;
            var webUi = page.Links?.WebUi;
            if (!string.IsNullOrWhiteSpace(webUi) &&
                Uri.TryCreate(siteUri, webUi, out var candidate) &&
                string.Equals(candidate.Host, siteUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                webUri = candidate;
            }

            yield return new ConfluencePageSummary(page.Id, page.Title.Trim(), webUri);
        }
    }

    private static DateOnly? ParseMeetingDate(string title, string suffix)
    {
        if (!title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || title.Length != 10 + suffix.Length)
        {
            return null;
        }

        return DateOnly.TryParseExact(
            title.AsSpan(0, 10),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date) ? date : null;
    }

    private static string ValidateCloudId(string cloudId)
    {
        if (!Guid.TryParse(cloudId, out var value))
        {
            throw new ArgumentException("The Confluence cloud ID is invalid.", nameof(cloudId));
        }

        return value.ToString("D", CultureInfo.InvariantCulture);
    }

    private static void ValidateSpaceId(string spaceId)
    {
        if (!IsSafeIdentifier(spaceId))
        {
            throw new ArgumentException("The Confluence space ID is invalid.", nameof(spaceId));
        }
    }

    private static bool IsSafeIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value.All(char.IsAsciiDigit);

    private static void ValidateAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 4096 || accessToken.Any(char.IsControl))
        {
            throw new ArgumentException("The access token is invalid.", nameof(accessToken));
        }
    }

    private static void ValidateSiteUri(Uri siteUri)
    {
        ArgumentNullException.ThrowIfNull(siteUri);
        if (siteUri.Scheme != Uri.UriSchemeHttps ||
            !siteUri.Host.EndsWith(".atlassian.net", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The Confluence site address is invalid.", nameof(siteUri));
        }
    }

    private sealed record SpaceCollection(
        [property: JsonPropertyName("results")] IReadOnlyList<SpaceResponse>? Results);

    private sealed record SpaceResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("key")] string? Key,
        [property: JsonPropertyName("name")] string Name);

    private sealed record PageCollection(
        [property: JsonPropertyName("results")] IReadOnlyList<PageResponse>? Results);

    private sealed record PageResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("_links")] PageLinks? Links);

    private sealed record PageLinks([property: JsonPropertyName("webui")] string? WebUi);
}
