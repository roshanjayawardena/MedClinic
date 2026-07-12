using Core;
using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;

namespace MedClinic.Api.Infrastructure;

/// <summary>
/// Object storage backed by MinIO (dev) or AWS S3 (production via the same API).
/// Never returns raw file bytes — callers get a presigned URL valid for the given expiry.
/// </summary>
public sealed class MinioStorageService : IStorageService
{
    private readonly IMinioClient _minio;
    private readonly string _bucket;

    public MinioStorageService(IConfiguration configuration)
    {
        var endpoint  = configuration["Storage:Endpoint"]!;
        var accessKey = configuration["Storage:AccessKey"]!;
        var secretKey = configuration["Storage:SecretKey"]!;
        _bucket       = configuration["Storage:BucketName"] ?? "mediclinic";

        _minio = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(configuration.GetValue<bool>("Storage:UseSsl", false))
            .Build();
    }

    public async Task<string> GenerateUploadUrlAsync(
        string objectKey,
        string contentType,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        var args = new PresignedPutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(objectKey)
            .WithExpiry((int)expiry.TotalSeconds);

        return await _minio.PresignedPutObjectAsync(args).ConfigureAwait(false);
    }

    public async Task<string> GenerateDownloadUrlAsync(
        string objectKey,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        var args = new PresignedGetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(objectKey)
            .WithExpiry((int)expiry.TotalSeconds);

        return await _minio.PresignedGetObjectAsync(args).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var args = new RemoveObjectArgs()
            .WithBucket(_bucket)
            .WithObject(objectKey);

        await _minio.RemoveObjectAsync(args, cancellationToken).ConfigureAwait(false);
    }
}
