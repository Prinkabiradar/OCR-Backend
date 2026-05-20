# 🚀 Quick Reference Card - Digital Ocean Spaces Setup

## 5-Minute Quick Start

### Step 1: Get Credentials (5 min)
```
1. Go to: https://www.digitalocean.com
2. Create account → Create Space (e.g., "ocr-documents")
3. Get API keys from: Account → API → Spaces
```

### Step 2: Set Environment Variables

**macOS (1 command):**
```bash
chmod +x OCR-Backend/setup-digitalocean-mac.sh
./OCR-Backend/setup-digitalocean-mac.sh
```

**Windows (1 command, as Administrator):**
```batch
OCR-Backend\setup-digitalocean-windows.bat
```

### Step 3: Verify
```bash
# macOS
echo $DIGITALOCEAN_SPACES_NAME

# Windows
echo %DIGITALOCEAN_SPACES_NAME%
```

### Step 4: Run App
```bash
cd OCR-Backend
dotnet run
```

### Step 5: Check Logs
```
Look for: ✓ Storage Mode: Digital Ocean Spaces
```

---

## Environment Variables

```bash
DIGITALOCEAN_SPACES_ACCESS_KEY="your_key_here"
DIGITALOCEAN_SPACES_SECRET_KEY="your_secret_here"
DIGITALOCEAN_SPACES_NAME="ocr-documents"
DIGITALOCEAN_SPACES_REGION="nyc3"
```

**Available regions:** nyc3, sfo3, sgp1, lon1, ams3, blr1, tor1

---

## File Storage Structure

```
In Digital Ocean:
ocr-jobs/
├── {jobId}/
│   ├── originals/      ← Original files
│   └── converted/      ← Processed files

On Local Disk (dev):
./uploads/
├── {jobId}/
│   ├── originals/
│   └── converted/
```

---

## Storage Service API

```csharp
// Inject in constructor
public MyService(IStorageService storage) { _storage = storage; }

// Save file
await _storage.SaveFileAsync(jobId, "originals", fileName, stream, ct);

// Get file bytes
var bytes = await _storage.GetFileAsyncBytes(jobId, "originals", fileName, ct);

// List files
var files = await _storage.ListFilesAsync(jobId, "originals", ct);

// Check exists
var exists = await _storage.FileExistsAsync(jobId, "originals", fileName, ct);

// Delete file
await _storage.DeleteFileAsync(jobId, "originals", fileName, ct);

// Get signed URL (DO only)
var url = await _storage.GetSignedUrlAsync(jobId, "originals", fileName, 1, ct);
```

---

## Common Tasks

### Check Storage Mode
```bash
dotnet run 2>&1 | grep "Storage Mode"
```

### Verify Credentials are Set
```bash
# macOS
env | grep DIGITALOCEAN

# Windows
set | grep DIGITALOCEAN
```

### Switch to Local Storage (dev)
```bash
# Unset the access key variable
unset DIGITALOCEAN_SPACES_ACCESS_KEY  # macOS
set DIGITALOCEAN_SPACES_ACCESS_KEY=  # Windows
```

### Debug Connection
```csharp
// In your code
var type = _storage.GetStorageType();
Console.WriteLine($"Using: {type}");  // "digitalocean" or "local"
```

---

## Code Changes Needed

Replace these patterns throughout your code:

```csharp
// OLD → NEW

File.Create(path)
→ _storage.SaveFileAsync(jobId, type, fileName, stream, ct)

File.ReadAllBytesAsync(path)
→ _storage.GetFileAsyncBytes(jobId, type, fileName, ct)

File.Exists(path)
→ _storage.FileExistsAsync(jobId, type, fileName, ct)

File.Delete(path)
→ _storage.DeleteFileAsync(jobId, type, fileName, ct)

Directory.GetFiles(dir)
→ _storage.ListFilesAsync(jobId, type, ct)

Directory.Delete(dir)
→ _storage.DeleteJobFolderAsync(jobId, ct)
```

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| "Not found" error | Restart terminal/IDE after setting env vars |
| Still using local | `echo $DIGITALOCEAN_SPACES_NAME` - should show space name |
| "Access Denied" | Check credentials in Digital Ocean dashboard |
| Connection timeout | Verify internet connection, check Digital Ocean status |
| Files not in Space | Go to Digital Ocean → Spaces → Your Space → Check files |

---

## Files to Know

| File | Purpose |
|------|---------|
| `DELIVERY_SUMMARY.md` | What was delivered (START HERE!) |
| `DIGITAL_OCEAN_SETUP.md` | Complete setup guide |
| `DIGITAL_OCEAN_MIGRATION_GUIDE.md` | How to integrate into code |
| `README_DIGITALOCEAN.md` | Overview and quick start |
| `IMPLEMENTATION_CHECKLIST.md` | Step-by-step checklist |
| `.env.local.example` | Example env vars file |

---

## Next Steps

1. ✅ Run setup script (5 min)
2. ✅ Verify configuration (2 min)
3. 📖 Read DIGITAL_OCEAN_MIGRATION_GUIDE.md (20 min)
4. 💻 Update OcrJobService to use IStorageService (1-2 hours)
5. 💻 Update DocumentPageController (30 min)
6. ✅ Run tests (1 hour)
7. 🚀 Deploy to production

---

## Useful Links

- Digital Ocean API: https://docs.digitalocean.com/reference/api/spaces/
- AWS S3 SDK: https://github.com/aws/aws-sdk-net
- Spaces Documentation: https://docs.digitalocean.com/products/spaces/

---

## Emergency Fallback

If Digital Ocean fails, application automatically uses **local filesystem**:

```bash
unset DIGITALOCEAN_SPACES_ACCESS_KEY  # macOS
# Files will be saved to ./uploads/
```

---

## Security Reminders

✅ **DO:**
- Store credentials in environment variables
- Use different keys for dev/prod
- Rotate keys monthly
- Add `.env.local` to `.gitignore`

❌ **DON'T:**
- Put credentials in code
- Commit credentials to Git
- Share API keys via email
- Use same key for multiple environments

---

**Ready to start?** Run the setup script for your OS! 🎉

