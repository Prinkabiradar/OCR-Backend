using System.Text.RegularExpressions;

namespace OCR_BACKEND.Services
{
    /// <summary>
    /// Local filesystem storage implementation.
    /// Used for development or when Digital Ocean is not configured.
    /// </summary>
    public class LocalFileStorageService : IStorageService
    {
        private readonly string _rootPath;
        private readonly ILogger<LocalFileStorageService> _logger;

        public LocalFileStorageService(IConfiguration config, ILogger<LocalFileStorageService> logger)
        {
            _logger = logger;
            _rootPath = config["FileStorage:Root"] ?? "uploads";

            // Ensure root directory exists
            Directory.CreateDirectory(_rootPath);

            _logger.LogInformation("Local file storage initialized at: {RootPath}", _rootPath);
        }

        public async Task<string> SaveFileAsync(string jobId, string fileType, string fileName, Stream fileStream, CancellationToken ct = default)
        {
            try
            {
                var jobDir = Path.Combine(_rootPath, jobId);
                var typeDir = Path.Combine(jobDir, fileType);
                Directory.CreateDirectory(typeDir);

                var filePath = Path.Combine(typeDir, SanitizeFileName(fileName));

                // Ensure unique file name
                filePath = BuildUniqueDestinationPath(typeDir, fileName);

                await using var fs = File.Create(filePath);
                await fileStream.CopyToAsync(fs, ct);

                _logger.LogInformation("File saved locally: {FilePath}", filePath);

                // Return relative path from root
                return Path.GetRelativePath(_rootPath, filePath).Replace("\\", "/");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving file locally: {FileName}", fileName);
                throw;
            }
        }

        public async Task<Stream?> GetFileAsync(string jobId, string fileType, string fileName, CancellationToken ct = default)
        {
            try
            {
                var filePath = ResolveFilePath(jobId, fileType, fileName);

                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("File not found: {FilePath}", filePath);
                    return null;
                }

                var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return fileStream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving file: {FileName}", fileName);
                throw;
            }
        }

        public async Task<byte[]?> GetFileAsyncBytes(string jobId, string fileType, string fileName, CancellationToken ct = default)
        {
            try
            {
                var filePath = ResolveFilePath(jobId, fileType, fileName);

                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("File not found: {FilePath}", filePath);
                    return null;
                }

                return await File.ReadAllBytesAsync(filePath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving file bytes: {FileName}", fileName);
                throw;
            }
        }

        public async Task<List<string>> ListFilesAsync(string jobId, string fileType, CancellationToken ct = default)
        {
            try
            {
                var typeDir = Path.Combine(_rootPath, jobId, fileType);

                if (!Directory.Exists(typeDir))
                    return new List<string>();

                var files = Directory.GetFiles(typeDir)
                    .Select(f => Path.GetFileName(f))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return files;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing files for job: {JobId}", jobId);
                throw;
            }
        }

        public async Task DeleteFileAsync(string jobId, string fileType, string fileName, CancellationToken ct = default)
        {
            try
            {
                var filePath = ResolveFilePath(jobId, fileType, fileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogInformation("File deleted: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file: {FileName}", fileName);
                throw;
            }
        }

        public async Task DeleteJobFolderAsync(string jobId, CancellationToken ct = default)
        {
            try
            {
                var jobDir = Path.Combine(_rootPath, jobId);

                if (Directory.Exists(jobDir))
                {
                    Directory.Delete(jobDir, recursive: true);
                    _logger.LogInformation("Job folder deleted: {JobDir}", jobDir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting job folder: {JobId}", jobId);
                throw;
            }
        }

        public async Task<bool> FileExistsAsync(string jobId, string fileType, string fileName, CancellationToken ct = default)
        {
            try
            {
                var filePath = ResolveFilePath(jobId, fileType, fileName);
                return File.Exists(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking file existence: {FileName}", fileName);
                throw;
            }
        }

        public async Task<string?> GetSignedUrlAsync(string jobId, string fileType, string fileName, int expiryHours = 1, CancellationToken ct = default)
        {
            // Local storage doesn't have signed URLs; return relative path instead
            return $"/api/DocumentPage/GetDocumentFile?jobId={jobId}&pageType={fileType}&fileName={SanitizeFileName(fileName)}";
        }

        public string GetStorageType()
        {
            return "local";
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Private helpers
        // ═══════════════════════════════════════════════════════════════════════

        private string ResolveFilePath(string jobId, string fileType, string fileName)
        {
            var basePath = Path.Combine(_rootPath, jobId, fileType);
            var filePath = Path.Combine(basePath, SanitizeFileName(fileName));

            // Security: ensure the resolved path is within the expected directory
            var fullBasePath = Path.GetFullPath(basePath);
            var fullFilePath = Path.GetFullPath(filePath);

            if (!fullFilePath.StartsWith(fullBasePath))
                throw new UnauthorizedAccessException("Invalid file path");

            return fullFilePath;
        }

        private string SanitizeFileName(string fileName)
        {
            // Remove any path traversal attempts
            var sanitized = Path.GetFileName(fileName);
            // Replace unsafe characters
            sanitized = Regex.Replace(sanitized, @"[^\w\s._-]", "");
            return string.IsNullOrEmpty(sanitized) ? "unnamed_file" : sanitized;
        }

        private string BuildUniqueDestinationPath(string directory, string fileName)
        {
            var sanitized = SanitizeFileName(fileName);
            var filePath = Path.Combine(directory, sanitized);

            if (!File.Exists(filePath))
                return filePath;

            var nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
            var extension = Path.GetExtension(sanitized);
            var counter = 1;

            while (File.Exists(filePath))
            {
                var newFileName = $"{nameWithoutExt}_{counter}{extension}";
                filePath = Path.Combine(directory, newFileName);
                counter++;
            }

            return filePath;
        }
    }
}
