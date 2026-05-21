dotnet run --launch-profile https

# ✅ Verification Guide - Digital Ocean Spaces Integration

## Quick Verification (2 Minutes)

### 1️⃣ Check Environment Variables

**macOS:**
```bash
echo "Access Key: $DIGITALOCEAN_SPACES_ACCESS_KEY"
echo "Secret Key: $DIGITALOCEAN_SPACES_SECRET_KEY"
echo "Space Name: $DIGITALOCEAN_SPACES_NAME"
echo "Region: $DIGITALOCEAN_SPACES_REGION"
```

**Windows PowerShell:**
```powershell
echo "Access Key: $env:DIGITALOCEAN_SPACES_ACCESS_KEY"
echo "Secret Key: $env:DIGITALOCEAN_SPACES_SECRET_KEY"
echo "Space Name: $env:DIGITALOCEAN_SPACES_NAME"
echo "Region: $env:DIGITALOCEAN_SPACES_REGION"
```

**Expected Output:**
```
Access Key: your_key_here
Secret Key: your_secret_here
Space Name: ocr-documents
Region: nyc3
```

❌ If you see empty values → Re-run setup script

---

### 2️⃣ Verify Files Are Present

**Run this command in OCR-Backend directory:**

```bash
# Check service files exist
ls Services/IStorageService.cs
ls Services/DigitalOceanStorageService.cs
ls Services/LocalFileStorageService.cs

# Check documentation exists
ls QUICK_REFERENCE.md
ls DIGITAL_OCEAN_SETUP.md
ls DIGITAL_OCEAN_MIGRATION_GUIDE.md

# Check scripts are executable
ls -la setup-digitalocean-mac.sh
# Should show: -rwxr-xr-x
```

**Expected:** All files should exist

❌ If files are missing → Run: `git pull` to ensure all files are downloaded

---

### 3️⃣ Check NuGet Package

```bash
dotnet list package
```

**Look for:** `AWSSDK.S3`

**Expected:** Should appear in the list with version number

❌ If missing → Run: `dotnet restore`

---

### 4️⃣ Start Application

```bash
cd OCR-Backend
dotnet run
```

**Look for in logs:**
```
✓ Storage Mode: Digital Ocean Spaces (Configured)
```

OR (if env vars not set):
```
✓ Storage Mode: Local Filesystem (Development Only)
```

✅ **If you see one of these messages:** Setup is correct!

❌ If you see an error → Check the Troubleshooting section below

---

## 📋 Complete Verification Checklist

### ☑️ Documentation Verification

- [ ] INDEX.md exists and readable
- [ ] QUICK_REFERENCE.md exists and readable
- [ ] DELIVERY_SUMMARY.md exists and readable
- [ ] DIGITAL_OCEAN_SETUP.md exists and readable
- [ ] DIGITAL_OCEAN_MIGRATION_GUIDE.md exists and readable
- [ ] README_DIGITALOCEAN.md exists and readable
- [ ] IMPLEMENTATION_CHECKLIST.md exists and readable
- [ ] .env.local.example exists

### ☑️ Code Verification

- [ ] Services/IStorageService.cs exists
- [ ] Services/DigitalOceanStorageService.cs exists
- [ ] Services/LocalFileStorageService.cs exists
- [ ] Program.cs has storage service registration
- [ ] OCR-BACKEND.csproj has AWSSDK.S3 package

### ☑️ Script Verification

- [ ] setup-digitalocean-mac.sh is executable (`ls -la` shows x)
- [ ] setup-digitalocean-windows.bat exists
- [ ] .env.local.example can be read

### ☑️ Environment Verification

- [ ] DIGITALOCEAN_SPACES_ACCESS_KEY is set
- [ ] DIGITALOCEAN_SPACES_SECRET_KEY is set
- [ ] DIGITALOCEAN_SPACES_NAME is set
- [ ] DIGITALOCEAN_SPACES_REGION is set (or defaults to nyc3)

### ☑️ Application Verification

- [ ] Application starts without errors
- [ ] Logs show "Storage Mode" message
- [ ] No connection errors in logs
- [ ] No missing dependency errors

### ☑️ Digital Ocean Verification

- [ ] Can access Digital Ocean dashboard
- [ ] Space exists and accessible
- [ ] API credentials are valid
- [ ] API token has Spaces scope

---

## 🔍 Detailed Verification Tests

### Test 1: Environment Variable Detection

**Code to run in application startup:**
```csharp
var accessKey = Environment.GetEnvironmentVariable("DIGITALOCEAN_SPACES_ACCESS_KEY");
var spaceName = Environment.GetEnvironmentVariable("DIGITALOCEAN_SPACES_NAME");

if (string.IsNullOrWhiteSpace(accessKey))
    Console.WriteLine("❌ DIGITALOCEAN_SPACES_ACCESS_KEY not set");
else
    Console.WriteLine("✅ DIGITALOCEAN_SPACES_ACCESS_KEY is set");

if (string.IsNullOrWhiteSpace(spaceName))
    Console.WriteLine("❌ DIGITALOCEAN_SPACES_NAME not set");
else
    Console.WriteLine($"✅ DIGITALOCEAN_SPACES_NAME = {spaceName}");
```

**Expected Output:**
```
✅ DIGITALOCEAN_SPACES_ACCESS_KEY is set
✅ DIGITALOCEAN_SPACES_NAME = ocr-documents
```

---

### Test 2: Storage Service Registration

**Add this test to your startup:**
```csharp
var app = builder.Build();
var storageService = app.Services.GetRequiredService<IStorageService>();

Console.WriteLine($"Storage Service Type: {storageService.GetType().Name}");
Console.WriteLine($"Storage Mode: {storageService.GetStorageType()}");
```

**Expected Output (with env vars set):**
```
Storage Service Type: DigitalOceanStorageService
Storage Mode: digitalocean
```

**Expected Output (without env vars):**
```
Storage Service Type: LocalFileStorageService
Storage Mode: local
```

---

### Test 3: Digital Ocean Connection

**Add this test to verify connection:**
```csharp
var storage = app.Services.GetRequiredService<IStorageService>();

// Try to list files from a test job
try
{
    var files = await storage.ListFilesAsync("test-job", "originals", CancellationToken.None);
    Console.WriteLine($"✅ Connected to storage: Found {files.Count} files in test-job");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Connection failed: {ex.Message}");
}
```

**Expected Output:**
```
✅ Connected to storage: Found X files in test-job
```

Or for first run:
```
✅ Connected to storage: Found 0 files in test-job
```

---

## 🚨 Troubleshooting

### Issue: "DIGITALOCEAN_SPACES_ACCESS_KEY not found"

**Symptoms:**
- Application starts but shows "Local Filesystem" mode
- Logs don't show Digital Ocean initialization

**Solutions:**
1. **Verify variable is set:**
   ```bash
   # macOS
   echo $DIGITALOCEAN_SPACES_ACCESS_KEY
   
   # Windows
   echo %DIGITALOCEAN_SPACES_ACCESS_KEY%
   ```

2. **If empty, re-run setup script:**
   ```bash
   ./setup-digitalocean-mac.sh  # macOS
   # or
   setup-digitalocean-windows.bat  # Windows (as Admin)
   ```

3. **Restart terminal/IDE completely**

4. **If still not working, manually check:**
   ```bash
   # macOS - check .zshrc
   grep "DIGITALOCEAN_SPACES" ~/.zshrc
   
   # Windows - check System Properties
   # Control Panel → System → Advanced → Environment Variables
   ```

---

### Issue: "Cannot connect to Digital Ocean"

**Symptoms:**
```
Amazon.S3Exception: Unable to connect to the remote server
```

**Solutions:**
1. **Check internet connection:**
   ```bash
   ping google.com
   ```

2. **Verify credentials are correct:**
   - Go to https://cloud.digitalocean.com/account/api/tokens
   - Compare with your environment variables
   - They should match exactly (case-sensitive)

3. **Check region is valid:**
   Valid regions: nyc3, sfo3, sgp1, lon1, ams3, blr1, tor1

4. **Verify Space exists:**
   - Go to https://cloud.digitalocean.com/spaces
   - Confirm your space is listed
   - Confirm it's in the correct region

5. **Check API permissions:**
   - Space name should be visible in Spaces list
   - Token should have "Spaces" scope

---

### Issue: "Access Denied" or "Invalid Credentials"

**Symptoms:**
```
Access Denied error when trying to upload/download files
```

**Solutions:**
1. **Verify exact credentials:**
   ```bash
   # Copy from Digital Ocean:
   # https://cloud.digitalocean.com/account/api/spaces
   # Check: Access Keys section
   ```

2. **Ensure no extra spaces:**
   ```bash
   # Wrong (has spaces)
   export DIGITALOCEAN_SPACES_ACCESS_KEY=" your_key "
   
   # Correct
   export DIGITALOCEAN_SPACES_ACCESS_KEY="your_key"
   ```

3. **Check API token expiration:**
   - Regenerate if older than 1 year
   - Visit: https://cloud.digitalocean.com/account/api/tokens

4. **Verify token has correct scope:**
   - Should include: "Spaces" access
   - Go to: API → Tokens → Check scopes

---

### Issue: "File not found" after upload

**Symptoms:**
- Upload succeeds but file can't be retrieved
- Error when trying to download

**Solutions:**
1. **Check Digital Ocean Space:**
   ```
   Go to: https://cloud.digitalocean.com/spaces
   Your Space → Files
   Look for: ocr-jobs/{jobId}/originals/{fileName}
   ```

2. **Verify ACL permissions:**
   - Files should be accessible
   - Check space public/private settings

3. **Confirm space has files:**
   ```csharp
   var files = await _storage.ListFilesAsync(jobId, "originals", ct);
   Console.WriteLine($"Files found: {files.Count}");
   foreach(var f in files)
       Console.WriteLine($"  - {f}");
   ```

---

### Issue: Slow uploads/downloads

**Symptoms:**
- Uploads take > 30 seconds for 10MB
- Downloads are very slow

**Solutions:**
1. **Check internet bandwidth:**
   - Run speedtest: https://speedtest.net/
   - Should have at least 5 Mbps

2. **Check region latency:**
   - Ping your region's endpoint:
   ```bash
   ping nyc3.digitaloceanspaces.com
   # Should show < 100ms latency
   ```

3. **Optimize settings in code:**
   - Already done in DigitalOceanStorageService
   - Connection pooling, streaming, etc. are enabled

4. **Consider using CDN:**
   - For frequently accessed files
   - Set up CloudFront or similar

---

### Issue: Files in wrong location

**Symptoms:**
- Files appear in Space root instead of ocr-jobs folder
- File paths don't match expected structure

**Solutions:**
1. **Check folder structure:**
   ```
   Expected: ocr-jobs/{jobId}/originals/
   Actual: Check in Space → Files
   ```

2. **Verify code is using correct storage:**
   ```csharp
   var storage = app.Services.GetRequiredService<IStorageService>();
   var type = storage.GetStorageType();
   Console.WriteLine($"Storage type: {type}");
   // Should print: digitalocean
   ```

3. **Check key building logic:**
   - Keys should be: `ocr-jobs/{jobId}/originals/{fileName}`
   - Verify jobId is not empty or "null"

---

## 📊 Health Check Report

### Run This Command:
```bash
cd OCR-Backend
dotnet run 2>&1 | tee health-check.log
```

### What to Look For:

**✅ Good Signs:**
```
✓ Storage Mode: Digital Ocean Spaces (Configured)
Listening on http://...
Application started successfully
```

**⚠️ Warning Signs:**
```
✓ Storage Mode: Local Filesystem (Development Only)
Amazon.S3Exception...
Failed to initialize storage...
```

### After checking, send me:
If you see errors, collect:
1. The log output (health-check.log)
2. Environment variable values (sanitize secrets)
3. Error messages
4. Expected vs actual behavior

---

## ✅ Final Verification Checklist

- [ ] All documentation files exist
- [ ] All source files exist and compiled
- [ ] Environment variables are set correctly
- [ ] Application starts without errors
- [ ] Logs show correct storage mode
- [ ] Can list files using storage service
- [ ] Digital Ocean Space is accessible
- [ ] Can upload test file (if implementing)
- [ ] Can download test file (if implementing)
- [ ] Storage mode can be toggled (test both)

---

## 🎉 Verification Complete!

If you've checked all items above ✅ then:

**Your setup is complete and ready for integration!**

Next steps:
1. Read: DIGITAL_OCEAN_MIGRATION_GUIDE.md
2. Update: OcrJobService
3. Update: DocumentPageController
4. Test: End-to-end file operations
5. Deploy: To production

---

## 📞 Still Having Issues?

1. **Check troubleshooting section above** (most issues covered)
2. **Review DIGITAL_OCEAN_SETUP.md** troubleshooting
3. **Check application logs** for specific error messages
4. **Verify credentials** at: https://cloud.digitalocean.com/account/api
5. **Test connectivity** with: `ping nyc3.digitaloceanspaces.com`

