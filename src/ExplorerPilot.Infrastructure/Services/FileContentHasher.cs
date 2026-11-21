using System.Buffers;
using System.Security.Cryptography;

namespace ExplorerPilot.Infrastructure.Services;

/// <summary>
/// Streaming SHA-256 helpers with fixed-size buffers for low memory use.
/// </summary>
internal static class FileContentHasher
{
    public const int PartialChunkBytes = 64 * 1024;
    public const int StreamBufferBytes = 64 * 1024;

    public static async Task<byte[]> ComputePartialSha256Async(
        string path,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: StreamBufferBytes,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length != expectedSize)
        {
            throw new IOException("File size changed during analysis.");
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferBytes);
        try
        {
            if (expectedSize <= PartialChunkBytes * 2)
            {
                await HashEntireStreamAsync(stream, sha, buffer, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await HashRangeAsync(stream, sha, buffer, offset: 0, count: PartialChunkBytes, cancellationToken)
                    .ConfigureAwait(false);
                await HashRangeAsync(
                        stream,
                        sha,
                        buffer,
                        offset: expectedSize - PartialChunkBytes,
                        count: PartialChunkBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return sha.GetHashAndReset();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task<byte[]> ComputeFullSha256Async(
        string path,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: StreamBufferBytes,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length != expectedSize)
        {
            throw new IOException("File size changed during analysis.");
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferBytes);
        try
        {
            await HashEntireStreamAsync(stream, sha, buffer, cancellationToken).ConfigureAwait(false);
            return sha.GetHashAndReset();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task HashEntireStreamAsync(
        Stream stream,
        IncrementalHash sha,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        stream.Seek(0, SeekOrigin.Begin);
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            sha.AppendData(buffer, 0, read);
        }
    }

    private static async Task HashRangeAsync(
        Stream stream,
        IncrementalHash sha,
        byte[] buffer,
        long offset,
        int count,
        CancellationToken cancellationToken)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        var remaining = count;
        while (remaining > 0)
        {
            var toRead = Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of file while hashing.");
            }

            sha.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }

    public static string ToHex(byte[] hash) => Convert.ToHexString(hash);
}
