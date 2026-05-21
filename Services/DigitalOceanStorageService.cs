using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace OCR_BACKEND.Services
{
    /// <summary>
    /// Digital Ocean Spaces storage implementation (S3-compatible).
    /// Uses AWS SDK for S3 to interact with Digital Ocean Spaces.
    /// </summary>
    public class DigitalOceanStorageService : IStorageService
    {
        private const string CompressionFlagMetadataKey = "x-amz-meta-content-compressed";
        private const string OriginalSizeMetadataKey = "x-amz-meta-original-size";
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;
        private readonly string _spaceName;
        private readonly string _region;
        private readonly ILogger<DigitalOceanStorageService> _logger;

        public DigitalOceanStorageService(IConfiguration config, ILogger<DigitalOceanStorageService> logger)
        {
            _logger = logger;
            
            // Read from environment variables (NOT from appsettings.json for security)
            var accessKey = Environment.GetEnvironmentVariable("DIGITALOCEAN_SPACES_ACCESS_KEY");
            var secretKey = Environment.GetEnvironmentVariable("DIGITALOCEAN_SPACES_SECRET_KEY");
            _spaceName = Environment.GetEnvironmentVariable("DIGITALOCEAN_SPACES_NAME") ?? "";
            _region = Environment.GetEnvironmentVariable("DIGITALOCEAN_SPACES_REGION") ?? "nyc3";

            if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(_spaceName))
            {
                throw new InvalidOperationException(
                    "Digital Ocean Spaces credentials not configured. " +
                    "Set environment variables: DIGITALOCEAN_SPACES_ACCESS_KEY, DIGITALOCEAN_SPACES_SECRET_KEY, DIGITALOCEAN_SPACES_NAME");
            }

            _bucketName = _spaceName;

            // Configure AWS S3 client for Digital Ocean Spaces
            var s3Config = new AmazonS3Config
            {
                ServiceURL = $"https://{_region}.digitaloceanspaces.com",
                ForcePathStyle = false,
                SignatureVersion = "v4",
                MaxErrorRetry = 5,
                HttpClientCacheSize = 10,
                Timeout = TimeSpan.FromSeconds(30)
            };

            var credentials = new Amazon.Runtime.BasicAWSCredentials(accessKey, secretKey);
            _s3Client = new AmazonS3Client(credentials, s3Config);

            _logger.LogInformation("Digital Ocean Spaces initialized: {SpaceName} in {Region}", _spaceName, _region);
        }

        public async Task<string> SaveFileAsync(string jobId, string fileType, string fileName, Stream fileStream, CancellationToken ct = default)
        {
            try
            {
                var key = BuildS3Key(jobId, fileType, fileName);
                await using var preparedStream = await PrepareLosslessCompressedStreamAsync(fileStream, ct);

                var uploadRequest = new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key,
                    InputStream = preparedStream.Stream,
                    ContentType = GetContentType(fileName),
                    StorageClass = S3StorageClass.Standard
                };
                
                foreach (var kvp in preparedStream.Metadata)
                {
                    uploadRequest.Metadata.Add(kvp.Key, kvp.Value);
                }

                // Set public-read ACL for files that need to be accessed by users
                uploadRequest.CannedACL = S3CannedACL.PublicRead;

                var response = await _s3Client.PutObjectAsync(uploadRequest, ct);

                _logger.LogInformation("File saved to Digital Ocean: {Key}", key);

                return key;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving file to Digital Ocean: {FileName}", fileName);
                throw;
            }
        }

        public async Task<Stream?> GetFileAsync(string jobId, string fileType, string fileName, CancellationToken ct = default)
        {
            try
            {
                var key = BuildS3Key(jobId, fileType, fileName);

                var getRequest = new GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key
                };

                var response = await _s3Client.GetObjectAsync(getRequest, ct);

                var memoryStream = new MemoryStream();
                await response.ResponseStream.CopyToAsync(memoryStream, ct);
                memoryStream.Position = 0;

                if (IsCompressedObject(response.Metadata))
                {
                    var decompressed = new MemoryStream();
                    using (var gzip = new GZipStream(memoryStream, CompressionMode.Decompress, leaveOpen: true))
                    {
                        await gzip.CopyToAsync(decompressed, ct);
                    }

                    decompressed.Position = 0;
                    memoryStream.Dispose();
                    return decompressed;
                }

                return memoryStream;
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
            {
                _logger.LogWarning("File not found in Digital Ocean: {FileName}", fileName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving file from Digital Ocean: {FileName}", fileName);
                throw;
            }
        }

        public async Task<byte[]?> GetFileAsyncBytes(string jobId, string fileType, string fileName, CancellationToken ct = default)
        {
            try
            {
                var stream = await GetFileAsync(jobId, fileType, fileName, ct);
                if (stream == null)
                    return null;

                using (stream)
                {
                    var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, ct);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving file bytes from Digital Ocean: {FileName}", fileName);
                throw;
            }
        }

        public async Task<List<string>> ListFilesAsync(string jobId, string fileType, CancellationToken ct = default)
        {
            try
            {
                var prefix = BuildS3KeyPrefix(jobId, fileType);
                var files = new List<string>();

                var listRequest = new ListObjectsV2Request
                {
                    BucketName = _bucketName,
                    Prefix = prefix,
                    MaxKeys = 1000
                };

                ListObjectsV2Response response;
                do
                {
                    response = await _s3Client.ListObjectsV2Async(listRequest, ct);

                    if (response.S3Objects != null)
                    {
                        foreach (var obj in response.S3Objects)
                        {
                            // Extract just the file name, not the full S3 key
                            var fileName = Path.GetFileName(obj.Key);
                            if (!string.IsNullOrEmpty(fileName))
                                files.Add(fileName);
                        }
                    }

                    listRequest.ContinuationToken = response.NextContinuationToken;
                } while (response.IsTruncated);

                return files;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing files from Digital Ocean for job: {JobId}", jobId);
                throw;
            }
        }

        public async Task DeleteFileAsync(string jobId, string fileType, string fileName, CancellationToken ct = default)
        {
            try
            {
                var key = BuildS3Key(jobId, fileType, fileName);

                var deleteRequest = new DeleteObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key
                };

                await _s3Client.DeleteObjectAsync(deleteRequest, ct);

                _logger.LogInformation("File deleted from Digital Ocean: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file from Digital Ocean: {FileName}", fileName);
                throw;
            }
        }

        public async Task DeleteJobFolderAsync(string jobId, CancellationToken ct = default)
        {
            try
            {
                var filesToDelete = await ListFilesAsync(jobId, "originals", ct);
                filesToDelete.AddRange(await ListFilesAsync(jobId, "converted", ct));

                if (filesToDelete.Count == 0)
                    return;

                var deleteRequest = new DeleteObjectsRequest
                {
                    BucketName = _bucketName,
                    Objects = filesToDelete
                        .Distinct()
                        .Select(f => new KeyVersion { Key = BuildS3Key(jobId, "originals", f) })
                        .ToList()
                };

                await _s3Client.DeleteObjectsAsync(deleteRequest, ct);

                _logger.LogInformation("Job folder deleted from Digital Ocean: {JobId}", jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting job folder from Digital Ocean: {JobId}", jobId);
                throw;
            }
        }

        public async Task<bool> FileExistsAsync(string jobId, string fileType, string fileName, CancellationToken ct = default)
        {
            try
            {
                var key = BuildS3Key(jobId, fileType, fileName);

                var metadataRequest = new GetObjectMetadataRequest
                {
                    BucketName = _bucketName,
                    Key = key
                };

                await _s3Client.GetObjectMetadataAsync(metadataRequest, ct);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey" || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking file existence in Digital Ocean: {FileName}", fileName);
                throw;
            }
        }

        public async Task<string?> GetSignedUrlAsync(string jobId, string fileType, string fileName, int expiryHours = 1, CancellationToken ct = default)
        {
            try
            {
                var key = BuildS3Key(jobId, fileType, fileName);
                var expiration = DateTime.UtcNow.AddHours(expiryHours);

                var urlRequest = new GetPreSignedUrlRequest
                {
                    BucketName = _bucketName,
                    Key = key,
                    Expires = expiration,
                    Verb = HttpVerb.GET
                };

                var url = _s3Client.GetPreSignedURL(urlRequest);
                return url;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating signed URL from Digital Ocean: {FileName}", fileName);
                throw;
            }
        }

        public string GetStorageType()
        {
            return "digitalocean";
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Private helpers
        // ═══════════════════════════════════════════════════════════════════════

        private string BuildS3Key(string jobId, string fileType, string fileName)
        {
            // S3 key format: ocr-jobs/{jobId}/{fileType}/{fileName}
            return $"ocr-jobs/{jobId}/{fileType}/{SanitizeFileName(fileName)}";
        }

        private string BuildS3KeyPrefix(string jobId, string fileType)
        {
            return $"ocr-jobs/{jobId}/{fileType}/";
        }

        private string SanitizeFileName(string fileName)
        {
            // Remove any path traversal attempts
            var sanitized = Path.GetFileName(fileName);
            // Replace unsafe characters
            sanitized = Regex.Replace(sanitized, @"[^\w\s._-]", "");
            return string.IsNullOrEmpty(sanitized) ? "unnamed_file" : sanitized;
        }

        private string GetContentType(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "application/pdf",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".tif" => "image/tiff",
                ".tiff" => "image/tiff",
                ".txt" => "text/plain",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                _ => "application/octet-stream"
            };
        }

        private static bool IsCompressedObject(MetadataCollection metadata)
        {
            var value = metadata[CompressionFlagMetadataKey];
            return string.Equals(value, "gzip", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<PreparedUploadStream> PrepareLosslessCompressedStreamAsync(Stream sourceStream, CancellationToken ct)
        {
            var sourceBytes = await ReadAllBytesAsync(sourceStream, ct);
            var compressedBytes = await CompressGzipAsync(sourceBytes, ct);

            // Keep original bytes when compression does not help.
            if (compressedBytes.Length >= sourceBytes.Length)
            {
                return new PreparedUploadStream(new MemoryStream(sourceBytes), new Dictionary<string, string>());
            }

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CompressionFlagMetadataKey] = "gzip",
                [OriginalSizeMetadataKey] = sourceBytes.Length.ToString()
            };

            return new PreparedUploadStream(new MemoryStream(compressedBytes), metadata);
        }

        private static async Task<byte[]> CompressGzipAsync(byte[] sourceBytes, CancellationToken ct)
        {
            await using var output = new MemoryStream();
            await using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                await gzip.WriteAsync(sourceBytes, ct);
            }

            return output.ToArray();
        }

        private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            await using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }

        private sealed record PreparedUploadStream(
            MemoryStream Stream,
            Dictionary<string, string> Metadata) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                Stream.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
