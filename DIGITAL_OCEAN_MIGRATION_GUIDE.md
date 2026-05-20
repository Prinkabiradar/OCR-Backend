# Digital Ocean Integration - Migration Guide

This guide explains how to integrate the new `IStorageService` abstraction into your existing OCR application.

## Overview

The application now supports both local filesystem and Digital Ocean Spaces for file storage through an abstraction layer (`IStorageService`).

### Storage Services Included:
- **LocalFileStorageService** - For development/testing (stores files locally)
- **DigitalOceanStorageService** - For production (uses Digital Ocean Spaces)

The application **automatically detects** which storage to use based on environment variables.

---

## Quick Start

### 1. Install Dependencies
```bash
cd OCR-Backend
dotnet restore
```

The `AWSSDK.S3` package has been added to your `.csproj` file.

### 2. Set Environment Variables

**macOS:**
```bash
# Option A: Interactive setup
chmod +x setup-digitalocean-mac.sh
./setup-digitalocean-mac.sh

# Option B: Manual setup
nano ~/.zshrc
# Add these lines:
export DIGITALOCEAN_SPACES_ACCESS_KEY="your_key"
export DIGITALOCEAN_SPACES_SECRET_KEY="your_secret"
export DIGITALOCEAN_SPACES_NAME="your-space"
export DIGITALOCEAN_SPACES_REGION="nyc3"

source ~/.zshrc
```

**Windows:**
```batch
REM Run as Administrator
setup-digitalocean-windows.bat

REM Or manually via PowerShell:
[Environment]::SetEnvironmentVariable("DIGITALOCEAN_SPACES_ACCESS_KEY", "your_key", "User")
[Environment]::SetEnvironmentVariable("DIGITALOCEAN_SPACES_SECRET_KEY", "your_secret", "User")
[Environment]::SetEnvironmentVariable("DIGITALOCEAN_SPACES_NAME", "your-space", "User")
[Environment]::SetEnvironmentVariable("DIGITALOCEAN_SPACES_REGION", "nyc3", "User")
```

### 3. Run the Application
```bash
dotnet run
```

Check logs for:
```
✓ Storage Mode: Digital Ocean Spaces (Configured)
```

---

## File Structure

### New Files Created:
```
Services/
├── IStorageService.cs                    # Interface defining storage operations
├── DigitalOceanStorageService.cs        # Digital Ocean implementation
└── LocalFileStorageService.cs           # Local filesystem implementation

Root/
├── DIGITAL_OCEAN_SETUP.md               # Complete setup documentation
├── setup-digitalocean-mac.sh            # macOS setup script
├── setup-digitalocean-windows.bat       # Windows setup script
└── .env.local.example                   # Example environment variables
```

---

## Integration Points

### Program.cs
Already updated to:
1. Detect environment variables
2. Register appropriate storage service
3. Log which storage mode is active

```csharp
// Automatic storage selection
var useDigitalOcean = !string.IsNullOrWhiteSpace(
    Environment.GetEnvironmentVariable("DIGITALOCEAN_SPACES_ACCESS_KEY"));

if (useDigitalOcean)
    builder.Services.AddScoped<IStorageService, DigitalOceanStorageService>();
else
    builder.Services.AddScoped<IStorageService, LocalFileStorageService>();
```

### OcrJobService Integration
The `OcrJobService` needs to be updated to use `IStorageService`. Here's how:

**Before (Local Filesystem):**
```csharp
var jobDir = Path.Combine(_config["FileStorage:Root"] ?? "uploads", dbJobId.ToString());
var originalsDir = Path.Combine(jobDir, "originals");
Directory.CreateDirectory(originalsDir);

await using var fs = File.Create(destPath);
await file.CopyToAsync(fs, ct);
```

**After (IStorageService):**
```csharp
public class OcrJobService : IOcrJobService
{
    private readonly IStorageService _storage;
    
    public OcrJobService(..., IStorageService storage, ...)
    {
        _storage = storage;
        // ... other dependencies
    }
    
    public async Task<Guid> UploadAndEnqueue(
        List<IFormFile> files, string? geminiModel = null, CancellationToken ct = default)
    {
        var dbJobId = await _ocrJobDBHelper.InsertOcrJob(null, 0);
        
        var uploadedPaths = new List<string>();
        foreach (var file in files)
        {
            var safeName = SanitizeFileName(file.FileName);
            
            // Use storage service instead of filesystem
            var key = await _storage.SaveFileAsync(
                dbJobId.ToString(),
                "originals",
                safeName,
                file.OpenReadStream(),
                ct
            );
            
            uploadedPaths.Add(key);
        }
        
        // ... rest of the method
    }
}
```

### DocumentPageController Integration

**Before:**
```csharp
var filePath = Path.Combine(storageRoot, jobId, "originals", fileName);
return File(await System.IO.File.ReadAllBytesAsync(filePath), contentType);
```

**After:**
```csharp
public class DocumentPageController : ControllerBase
{
    private readonly IStorageService _storage;
    
    public DocumentPageController(
        IDocumentPageService service,
        IConfiguration config,
        IStorageService storage)
    {
        _storage = storage;
        // ... other dependencies
    }
    
    [HttpGet("GetDocumentFile")]
    public async Task<IActionResult> GetDocumentFile(
        [FromQuery] int documentId,
        [FromQuery] int pageNumber = 1)
    {
        // Get job_id from database...
        var fileBytes = await _storage.GetFileAsyncBytes(
            jobId,
            "originals",
            fileName,
            ct
        );
        
        if (fileBytes == null)
            return NotFound();
        
        return File(fileBytes, contentType);
    }
}
```

---

## Storage Service API

### Methods Available:

```csharp
// Save a file
await _storage.SaveFileAsync(jobId, "originals", fileName, fileStream, ct);

// Retrieve as stream
var stream = await _storage.GetFileAsync(jobId, "originals", fileName, ct);

// Retrieve as bytes
var bytes = await _storage.GetFileAsyncBytes(jobId, "originals", fileName, ct);

// List files
var files = await _storage.ListFilesAsync(jobId, "originals", ct);

// Check existence
var exists = await _storage.FileExistsAsync(jobId, "originals", fileName, ct);

// Delete file
await _storage.DeleteFileAsync(jobId, "originals", fileName, ct);

// Delete entire job folder
await _storage.DeleteJobFolderAsync(jobId, ct);

// Get signed URL (for cloud storage)
var url = await _storage.GetSignedUrlAsync(jobId, "originals", fileName, 1, ct);

// Check storage type
var type = _storage.GetStorageType(); // "local" or "digitalocean"
```

---

## Key Design Decisions

### 1. Abstraction Layer (`IStorageService`)
- **Why**: Allows switching storage backends without code changes
- **Benefit**: Easy testing, future expansion (Azure, AWS S3, etc.)

### 2. Environment Variables
- **Why**: Credentials must be outside code/config files for security
- **Benefit**: Easy management across dev/staging/production

### 3. Automatic Detection
- **Why**: No configuration files needed
- **Benefit**: Zero-config for developers

### 4. S3-Compatible API
- **Why**: Digital Ocean Spaces uses AWS S3 API
- **Benefit**: Can later add support for other S3-compatible services

---

## Security Considerations

### ✅ What We Did Right:
- Credentials in environment variables, not code
- Each file type (originals/converted) in separate "folders"
- Job IDs used as folder names (no sequential IDs)
- Files served with proper content types
- Path traversal protection in file names

### ⚠️ What You Should Do:
- Rotate API keys regularly
- Use different keys for dev/prod
- Enable bucket versioning
- Set up bucket lifecycle policies
- Monitor Digital Ocean bill (bandwidth charges)
- Add access logging

### 🔒 Production Checklist:
- [ ] API keys in secrets manager (not env vars on server)
- [ ] Bucket set to private with selective public access
- [ ] CORS policy restricted to your domain
- [ ] Bucket lifecycle: delete old uploads after 30 days
- [ ] Enable bucket versioning
- [ ] Set up CloudFront/CDN for faster delivery
- [ ] Monitor costs

---

## Troubleshooting

### Problem: "DIGITALOCEAN_SPACES_ACCESS_KEY not found"

**Solution:**
```bash
# macOS
echo $DIGITALOCEAN_SPACES_ACCESS_KEY

# Windows PowerShell
$env:DIGITALOCEAN_SPACES_ACCESS_KEY

# Windows CMD
echo %DIGITALOCEAN_SPACES_ACCESS_KEY%
```

If empty, re-run setup script or manually set variables.

### Problem: "Access Denied" uploading files

**Check:**
1. API credentials are correct
2. Space is created and accessible
3. ACL is set correctly (should be public-read for user access)
4. API token has Spaces scope

### Problem: Files not visible in Space

**Check:**
1. Go to Digital Ocean dashboard → Spaces
2. Verify files are in `ocr-jobs/{jobId}/{type}/` folder
3. Check ACL settings
4. Verify region is correct

### Problem: Very slow uploads/downloads

**Optimize:**
```csharp
// Already optimized in code:
// - Default timeout: 30 seconds
// - HTTP client reuse (connection pooling)
// - Streaming (not loading entire file in memory)
// - Regional endpoint selection
```

Consider:
- Using CDN (CloudFront)
- Regional replication
- Checking network bandwidth

---

## Testing

### Local Testing (without Digital Ocean)
```bash
# Set nothing - will use local filesystem
dotnet run

# Files will be stored in: ./uploads/
```

### Integration Testing
```csharp
// Test code can mock IStorageService
var mockStorage = new Mock<IStorageService>();
mockStorage
    .Setup(x => x.SaveFileAsync(It.IsAny<string>(), It.IsAny<string>(), 
                                  It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync("test-key");

var service = new OcrJobService(..., mockStorage.Object, ...);
```

---

## Next Steps

1. **Update OcrJobService** - Refactor to use `IStorageService`
   - Replace `File.Create()` with `_storage.SaveFileAsync()`
   - Replace `File.ReadAllBytes()` with `_storage.GetFileAsyncBytes()`
   - Replace `File.Delete()` with `_storage.DeleteFileAsync()`
   - Replace `Directory.GetFiles()` with `_storage.ListFilesAsync()`

2. **Update DocumentPageController** - Use storage service for retrieval
   - Replace `System.IO.File` operations with storage service calls

3. **Update OcrWorkerService** - Handle converted files through storage

4. **Test end-to-end** - Upload, process, retrieve files

5. **Monitor costs** - Watch Digital Ocean usage

---

## Documentation Files

- [DIGITAL_OCEAN_SETUP.md](./DIGITAL_OCEAN_SETUP.md) - Complete setup guide
- [setup-digitalocean-mac.sh](./setup-digitalocean-mac.sh) - macOS automation
- [setup-digitalocean-windows.bat](./setup-digitalocean-windows.bat) - Windows automation
- [.env.local.example](./.env.local.example) - Environment variables template

---

## Getting Help

- **Digital Ocean API Docs**: https://docs.digitalocean.com/reference/api/spaces/
- **AWS S3 API Docs**: https://docs.aws.amazon.com/s3/
- **Application Logs**: Check for "DigitalOceanStorageService" entries

