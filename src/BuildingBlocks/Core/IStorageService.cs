namespace Core;

/// <summary>
/// Abstraction over object storage (MinIO in dev, S3 in production).
/// All file access goes through presigned URLs — the API never proxies file bytes.
/// </summary>
public interface IStorageService
{
    /// <summary>Generates a presigned URL for uploading a file directly from the browser.</summary>
    Task<string> GenerateUploadUrlAsync(
        string objectKey,
        string contentType,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    /// <summary>Generates a presigned URL for downloading/viewing a file.</summary>
    Task<string> GenerateDownloadUrlAsync(
        string objectKey,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);
}
