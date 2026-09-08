using System.Text.Json;

namespace MinutesBridge.Core.Http;

internal static class BoundedJsonContent
{
    private const int BufferSize = 8192;

    public static async Task<T> ReadAsync<T>(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        if (content.Headers.ContentLength is long declaredLength && declaredLength > maximumBytes)
        {
            throw new InvalidDataException("The remote service returned an oversized response.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream(Math.Min(maximumBytes, 16 * 1024));
        var buffer = new byte[BufferSize];
        var total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > maximumBytes)
            {
                throw new InvalidDataException("The remote service returned an oversized response.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        destination.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(destination, cancellationToken: cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("The remote service returned an empty response.");
    }
}
