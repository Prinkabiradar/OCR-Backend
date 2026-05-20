# Implementation Checklist - Digital Ocean Spaces Integration

## 📋 Setup Phase

### Prerequisites
- [ ] Read all documentation files
- [ ] Have Digital Ocean account
- [ ] Created a Digital Ocean Space
- [ ] Generated API access keys
- [ ] Know your Space region

### Environment Configuration

**macOS:**
- [ ] Opened terminal
- [ ] Ran `chmod +x setup-digitalocean-mac.sh`
- [ ] Ran `./setup-digitalocean-mac.sh`
- [ ] Verified with `echo $DIGITALOCEAN_SPACES_NAME`
- [ ] Restarted IDE/terminal

**Windows:**
- [ ] Ran `setup-digitalocean-windows.bat` as Administrator
- [ ] Verified with `echo %DIGITALOCEAN_SPACES_NAME%` in new CMD window
- [ ] Restarted IDE

### Application Verification
- [ ] Started application: `dotnet run`
- [ ] Saw "✓ Storage Mode: Digital Ocean Spaces" in logs
- [ ] No errors during startup

---

## 🔧 Development Phase

### Update OcrJobService
```csharp
// File: Services/OcrJobService.cs
```

**Tasks:**
- [ ] Add `IStorageService _storage` dependency
- [ ] Inject via constructor
- [ ] In `UploadAndEnqueue()` method:
  - [ ] Replace: `File.Create()` → `_storage.SaveFileAsync()`
  - [ ] Replace: `file.CopyToAsync()` → `fileStream` to service
  - [ ] Replace: `Directory.GetFiles()` → `_storage.ListFilesAsync()`
  - [ ] Replace: `File.Delete()` → `_storage.DeleteFileAsync()`

**Key Method to Update:**
```csharp
public async Task<Guid> UploadAndEnqueue(
    List<IFormFile> files,
    string? geminiModel = null,
    CancellationToken ct = default)
{
    var dbJobId = await _ocrJobDBHelper.InsertOcrJob(null, 0);
    
    foreach (var file in files)
    {
        var safeName = SanitizeFileName(file.FileName);
        
        // OLD: 
        // var fs = File.Create(destPath);
        // await file.CopyToAsync(fs, ct);
        
        // NEW:
        await _storage.SaveFileAsync(
            dbJobId.ToString(),
            "originals",
            safeName,
            file.OpenReadStream(),
            ct
        );
    }
    
    // ... rest of method
}
```

**Methods to Review:**
- [ ] `ProcessPdfAsync()` - Replace file operations
- [ ] `SplitPdfIntoPages()` - Check PDF handling
- [ ] `RetryResult()` - Update file retrieval
- [ ] Result handling - Update file paths

### Update DocumentPageController
```csharp
// File: Controllers/DocumentPageController.cs
```

**Tasks:**
- [ ] Add `IStorageService _storage` field
- [ ] Inject via constructor
- [ ] In `GetDocumentFile()` method:
  - [ ] Replace: `File.ReadAllBytesAsync()` → `_storage.GetFileAsyncBytes()`
  - [ ] Replace: `Path.Combine()` logic with storage keys
  - [ ] Update error handling

**Key Method to Update:**
```csharp
[HttpGet("GetDocumentFile")]
public async Task<IActionResult> GetDocumentFile(
    [FromQuery] int documentId,
    [FromQuery] int pageNumber = 1)
{
    try
    {
        // Get job_id from database...
        var request = new OcrDocumentRequest { /* ... */ };
        DataTable dt = await _service.GetDocumentPagesByDocument(request);
        
        var jobId = dt.Rows[0]["job_id"]?.ToString();
        
        // OLD:
        // var bytes = await System.IO.File.ReadAllBytesAsync(filePath, ct);
        
        // NEW:
        var bytes = await _storage.GetFileAsyncBytes(
            jobId,
            "originals",
            fileName,
            ct
        );
        
        if (bytes == null)
            return NotFound();
        
        return File(bytes, contentType);
    }
    catch (Exception ex)
    {
        return BadRequest(new { message = ex.Message });
    }
}
```

### Update OcrWorkerService (if file operations)
```csharp
// File: BackgroundServices/OcrWorkerService.cs
```

**Tasks:**
- [ ] Review file operations
- [ ] Identify usage patterns
- [ ] Replace with `_storage` calls
- [ ] Test background processing

### Update Other Services

**Search for file operations:**
```bash
grep -r "File\.Create\|File\.Copy\|Directory\.Create\|File\.Delete" Services/ --include="*.cs"
grep -r "System\.IO\.File\|Directory\.GetFiles" Services/ --include="*.cs"
```

**For each occurrence:**
- [ ] Identify the operation
- [ ] Map to `IStorageService` method
- [ ] Update code
- [ ] Test

---

## 🧪 Testing Phase

### Unit Testing

**Create test file:** `Tests/StorageServiceTests.cs`

- [ ] Test `LocalFileStorageService`
  - [ ] Save file locally
  - [ ] Retrieve file
  - [ ] List files
  - [ ] Delete file
  - [ ] File existence check

- [ ] Test `DigitalOceanStorageService`
  - [ ] Save file to Digital Ocean
  - [ ] Retrieve file
  - [ ] List files
  - [ ] Delete file
  - [ ] File existence check
  - [ ] Signed URL generation

### Integration Testing

**Scenario 1: Local Storage**
```bash
# Unset environment variable
unset DIGITALOCEAN_SPACES_ACCESS_KEY  # macOS
# OR
set DIGITALOCEAN_SPACES_ACCESS_KEY=  # Windows

# Start app
dotnet run

# Upload test file
# Verify it's in ./uploads/{jobId}/originals/

# Download test file
# Verify it opens correctly
```

- [ ] File upload works
- [ ] File appears in uploads folder
- [ ] File retrieval works
- [ ] File content is correct
- [ ] Logs show "Local Filesystem" mode

**Scenario 2: Digital Ocean Storage**
```bash
# Set environment variables
export DIGITALOCEAN_SPACES_ACCESS_KEY="..."  # macOS
# OR via GUI (Windows)

# Start app
dotnet run

# Upload test file
# Verify it's in Digital Ocean Space

# Download test file
# Verify it opens correctly
```

- [ ] File upload works
- [ ] File appears in Digital Ocean
- [ ] File is in `ocr-jobs/{jobId}/originals/` folder
- [ ] File retrieval works
- [ ] File content is correct
- [ ] Logs show "Digital Ocean Spaces" mode

### End-to-End Testing

**Full Upload & Processing Workflow:**
- [ ] Upload multi-page document
- [ ] Verify files stored correctly
- [ ] Process through OCR pipeline
- [ ] Verify converted files stored
- [ ] Download and view results
- [ ] Verify file integrity
- [ ] Check database records match

**Error Scenarios:**
- [ ] Invalid credentials
- [ ] Space doesn't exist
- [ ] Network timeout
- [ ] Corrupted file
- [ ] Very large file
- [ ] Concurrent uploads

---

## 📊 Code Review Checklist

**Before committing:**
- [ ] No hardcoded paths or credentials
- [ ] All file operations use `IStorageService`
- [ ] Error handling is consistent
- [ ] Logging is comprehensive
- [ ] Comments explain non-obvious code
- [ ] No debug code left behind
- [ ] Tests pass locally
- [ ] Tests pass in CI/CD pipeline

**Security review:**
- [ ] No credentials in code
- [ ] Path traversal protection in place
- [ ] File names are sanitized
- [ ] Proper exception handling
- [ ] No sensitive data in logs

---

## 🚀 Deployment Phase

### Pre-Production

- [ ] All tests passing
- [ ] Code reviewed
- [ ] Documentation updated
- [ ] Created separate Digital Ocean Space for prod
- [ ] Generated new API keys for prod
- [ ] Tested with production environment

### Production Deployment

**Environment Setup:**
- [ ] Set production credentials on server
- [ ] Verified credentials via logs
- [ ] Tested file operations

**Migration of Existing Files (if needed):**
- [ ] Identify existing files in local storage
- [ ] Write migration script (if necessary)
- [ ] Backup original files
- [ ] Run migration script
- [ ] Verify all files transferred
- [ ] Update database references
- [ ] Test file retrieval

**Monitoring:**
- [ ] Monitor error logs for storage failures
- [ ] Check Digital Ocean usage/costs
- [ ] Verify upload/download speeds
- [ ] Monitor API rate limits
- [ ] Set up alerts for failures

### Rollback Plan

- [ ] If Digital Ocean fails, automatically fallback? (Can implement)
- [ ] Keep local storage as backup?
- [ ] Have database backup ready
- [ ] Know how to restore from backup
- [ ] Document rollback procedure

---

## 📝 Documentation Update

After implementation, update:

- [ ] README.md - Add Digital Ocean section
- [ ] API documentation - Update file endpoints
- [ ] Architecture docs - Explain storage layer
- [ ] Setup guide - Verify it's still accurate
- [ ] Deployment guide - Add environment variables
- [ ] Troubleshooting - Add common issues

---

## 🎯 Success Criteria

### Functional Requirements
- [ ] Files upload to Digital Ocean Spaces
- [ ] Files can be retrieved from Digital Ocean
- [ ] Local storage fallback works
- [ ] Storage mode detected correctly
- [ ] No credentials in config files

### Non-Functional Requirements
- [ ] Upload performance acceptable (< 5 sec for 10MB)
- [ ] Download performance acceptable (< 2 sec for 10MB)
- [ ] Error handling robust
- [ ] Security best practices followed
- [ ] No breaking changes to API

### Testing Requirements
- [ ] Unit tests passing
- [ ] Integration tests passing
- [ ] End-to-end scenarios verified
- [ ] Error scenarios handled
- [ ] Load testing completed (optional)

---

## 📋 Sign-off

When everything is complete:

- [ ] All checklist items completed
- [ ] All tests passing
- [ ] Documentation updated
- [ ] Team notified
- [ ] Monitoring alerts configured
- [ ] Ready for production deployment

---

## 📞 Quick Reference

### Command to check storage mode:
```bash
dotnet run 2>&1 | grep "Storage Mode"
```

### File paths during development:
```
Local: ./uploads/{jobId}/{type}/{fileName}
DO: ocr-jobs/{jobId}/{type}/{fileName}
```

### Common methods to update:
```csharp
File.Create() → _storage.SaveFileAsync()
File.ReadAllBytesAsync() → _storage.GetFileAsyncBytes()
File.Exists() → _storage.FileExistsAsync()
File.Delete() → _storage.DeleteFileAsync()
Directory.GetFiles() → _storage.ListFilesAsync()
Directory.Delete() → _storage.DeleteJobFolderAsync()
```

---

