namespace OCR_BACKEND.Services
{
    /// <summary>
    /// Abstraction for file storage operations.
    /// Supports both local filesystem and cloud storage (Digital Ocean Spaces).
    /// </summary>
    public interface IStorageService
    {
        /// <summary>
        /// Save a file to storage (local or cloud)
        /// </summary>
        /// <param name="jobId">Unique job identifier</param>
        /// <param name="fileType">Type: 'originals' or 'converted'</param>
        /// <param name="fileName">Name of the file</param>
        /// <param name="fileStream">File content stream</param>
        /// <param name="ct">Cancellation token</param>
        Task<string> SaveFileAsync(string jobId, string fileType, string fileName, Stream fileStream, CancellationToken ct = default);

        /// <summary>
        /// Get file as stream from storage
        /// </summary>
        Task<Stream?> GetFileAsync(string jobId, string fileType, string fileName, CancellationToken ct = default);

        /// <summary>
        /// Get file content as bytes
        /// </summary>
        Task<byte[]?> GetFileAsyncBytes(string jobId, string fileType, string fileName, CancellationToken ct = default);

        /// <summary>
        /// Get list of files in a folder
        /// </summary>
        Task<List<string>> ListFilesAsync(string jobId, string fileType, CancellationToken ct = default);

        /// <summary>
        /// Delete a file from storage
        /// </summary>
        Task DeleteFileAsync(string jobId, string fileType, string fileName, CancellationToken ct = default);

        /// <summary>
        /// Delete entire job folder
        /// </summary>
        Task DeleteJobFolderAsync(string jobId, CancellationToken ct = default);

        /// <summary>
        /// Check if file exists
        /// </summary>
        Task<bool> FileExistsAsync(string jobId, string fileType, string fileName, CancellationToken ct = default);

        /// <summary>
        /// Get a signed URL for direct file access (for cloud storage)
        /// </summary>
        Task<string?> GetSignedUrlAsync(string jobId, string fileType, string fileName, int expiryHours = 1, CancellationToken ct = default);

        /// <summary>
        /// Get storage type (local or digitalocean)
        /// </summary>
        string GetStorageType();
    }
}
