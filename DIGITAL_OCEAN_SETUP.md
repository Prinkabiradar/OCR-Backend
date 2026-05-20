# Digital Ocean Spaces Setup Guide

This guide explains how to configure your OCR application to use **Digital Ocean Spaces** for secure file storage without storing credentials in `appsettings.json`.

## Table of Contents
1. [Get Digital Ocean Credentials](#get-digital-ocean-credentials)
2. [macOS Setup](#macos-setup)
3. [Windows Setup](#windows-setup)
4. [Verify Configuration](#verify-configuration)
5. [Troubleshooting](#troubleshooting)

---

## Get Digital Ocean Credentials

### Step 1: Create Digital Ocean Account
1. Go to [https://www.digitalocean.com](https://www.digitalocean.com)
2. Sign up and create an account
3. Log in to your Digital Ocean dashboard

### Step 2: Create API Token
1. Click on **API** in the left sidebar (under Account menu)
2. Go to **Tokens/Keys** section
3. Click **Generate New Token**
4. Name it (e.g., "OCR-App-Token")
5. Select **Read and Write** scope
6. Click **Generate Token**
7. **Copy and save the token** (you won't see it again)

### Step 3: Create a Space (Bucket)
1. Navigate to **Spaces** in the left sidebar
2. Click **Create a Space**
3. Choose a name (e.g., `ocr-documents`)
4. Choose a region (e.g., `New York 3 (nyc3)`, `San Francisco (sfo3)`, etc.)
5. Click **Create Space**
6. Note the space name and region

### Step 4: Create Access Keys
1. Inside your Space, click on **Settings**
2. Scroll to **CORS** section and configure if needed
3. Go back and click your profile → **API**
4. Under **Spaces access keys**, click **Generate New Access Key**
5. Name it (e.g., "OCR-App")
6. **Copy and save both:**
   - Access Key (like your username)
   - Secret Key (like your password)

---

## macOS Setup

### Option 1: Using `.zshrc` or `.bash_profile` (Recommended)

#### Step 1: Open Terminal
```bash
nano ~/.zshrc
# or for Bash:
# nano ~/.bash_profile
```

#### Step 2: Add Environment Variables
Add these lines at the end of the file:

```bash
# Digital Ocean Spaces Configuration
export DIGITALOCEAN_SPACES_ACCESS_KEY="your_access_key_here"
export DIGITALOCEAN_SPACES_SECRET_KEY="your_secret_key_here"
export DIGITALOCEAN_SPACES_NAME="your-space-name"
export DIGITALOCEAN_SPACES_REGION="nyc3"
```

Replace with your actual credentials:
- `your_access_key_here` → Your Access Key from Step 4 above
- `your_secret_key_here` → Your Secret Key from Step 4 above
- `your-space-name` → Your Space name (e.g., `ocr-documents`)
- `nyc3` → Your region (e.g., `sfo3`, `sgp1`, etc.)

#### Step 3: Save and Exit
```bash
# Press Ctrl+X, then Y, then Enter
```

#### Step 4: Reload Configuration
```bash
source ~/.zshrc
# or for Bash:
# source ~/.bash_profile
```

#### Step 5: Verify
```bash
echo $DIGITALOCEAN_SPACES_NAME
# Should print: your-space-name
```

---

### Option 2: Using `.env.local` File (Application Level)

#### Step 1: Create `.env.local` in Project Root
```bash
cd /Users/sagarhatikat/Hare\ Krishna\ Mandir\ /OCR-without-Digital-Ocean/OCR-Backend
touch .env.local
```

#### Step 2: Add Credentials
```bash
nano .env.local
```

Add:
```
DIGITALOCEAN_SPACES_ACCESS_KEY=your_access_key_here
DIGITALOCEAN_SPACES_SECRET_KEY=your_secret_key_here
DIGITALOCEAN_SPACES_NAME=your-space-name
DIGITALOCEAN_SPACES_REGION=nyc3
```

#### Step 3: Load in Application
Add this to your `Program.cs` or startup code:
```csharp
// Load .env.local file
var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".env.local");
if (File.Exists(envFile))
{
    foreach (var line in File.ReadAllLines(envFile))
    {
        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
            }
        }
    }
}
```

⚠️ **IMPORTANT**: Add `.env.local` to `.gitignore`:
```bash
echo ".env.local" >> .gitignore
```

---

### Option 3: Using Launchd (For macOS Specific)

#### Step 1: Create LaunchAgent File
```bash
nano ~/Library/LaunchAgents/com.ocr.app.plist
```

#### Step 2: Add Configuration
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.ocr.app</string>
    <key>ProgramArguments</key>
    <array>
        <string>/usr/bin/open</string>
        <string>/Applications/YourApp.app</string>
    </array>
    <key>EnvironmentVariables</key>
    <dict>
        <key>DIGITALOCEAN_SPACES_ACCESS_KEY</key>
        <string>your_access_key_here</string>
        <key>DIGITALOCEAN_SPACES_SECRET_KEY</key>
        <string>your_secret_key_here</string>
        <key>DIGITALOCEAN_SPACES_NAME</key>
        <string>your-space-name</string>
        <key>DIGITALOCEAN_SPACES_REGION</key>
        <string>nyc3</string>
    </dict>
</dict>
</plist>
```

#### Step 3: Load Agent
```bash
launchctl load ~/Library/LaunchAgents/com.ocr.app.plist
```

---

## Windows Setup

### Option 1: Using System Environment Variables (GUI) - Recommended

#### Step 1: Open Environment Variables
1. Press `Win + X` → Click **System**
2. Click **Advanced system settings** (on the left)
3. Click **Environment Variables** button
4. Under "User variables", click **New**

#### Step 2: Add Each Variable
Create 4 new user variables:

**Variable 1:**
- Variable name: `DIGITALOCEAN_SPACES_ACCESS_KEY`
- Variable value: `your_access_key_here`
- Click **OK**

**Variable 2:**
- Variable name: `DIGITALOCEAN_SPACES_SECRET_KEY`
- Variable value: `your_secret_key_here`
- Click **OK**

**Variable 3:**
- Variable name: `DIGITALOCEAN_SPACES_NAME`
- Variable value: `your-space-name`
- Click **OK**

**Variable 4:**
- Variable name: `DIGITALOCEAN_SPACES_REGION`
- Variable value: `nyc3`
- Click **OK**

#### Step 3: Restart Application
Close and reopen your application or restart the development server.

#### Step 4: Verify
Open **Command Prompt** and type:
```cmd
echo %DIGITALOCEAN_SPACES_NAME%
```
Should print: `your-space-name`

---

### Option 2: Using Command Prompt (CMD) - Temporary

⚠️ **Note**: These changes are only for the current session.

```cmd
setx DIGITALOCEAN_SPACES_ACCESS_KEY "your_access_key_here"
setx DIGITALOCEAN_SPACES_SECRET_KEY "your_secret_key_here"
setx DIGITALOCEAN_SPACES_NAME "your-space-name"
setx DIGITALOCEAN_SPACES_REGION "nyc3"
```

Then restart your terminal/IDE.

---

### Option 3: Using PowerShell - Permanent

```powershell
[Environment]::SetEnvironmentVariable("DIGITALOCEAN_SPACES_ACCESS_KEY", "your_access_key_here", "User")
[Environment]::SetEnvironmentVariable("DIGITALOCEAN_SPACES_SECRET_KEY", "your_secret_key_here", "User")
[Environment]::SetEnvironmentVariable("DIGITALOCEAN_SPACES_NAME", "your-space-name", "User")
[Environment]::SetEnvironmentVariable("DIGITALOCEAN_SPACES_REGION", "nyc3", "User")
```

Verify:
```powershell
$env:DIGITALOCEAN_SPACES_NAME
```

---

### Option 4: Using `.env` File (Application Level)

Same as macOS Option 2 above.

---

## Verify Configuration

### Run Health Check
```bash
curl http://localhost:5000/api/OcrJob/CheckGeminiHealth
```

The application will automatically detect the environment variables and use Digital Ocean Spaces.

### Check Storage Type in Logs
When the application starts, check the logs:
```
✓ Storage Mode: Digital Ocean Spaces
```

If you see:
```
✓ Storage Mode: Local Filesystem (Development only)
```

Then the environment variables are not set.

---

## Troubleshooting

### Problem: "DIGITALOCEAN_SPACES_ACCESS_KEY not found"

**Solution:**
1. **Verify variable is set:**
   - macOS: `echo $DIGITALOCEAN_SPACES_ACCESS_KEY`
   - Windows: `echo %DIGITALOCEAN_SPACES_ACCESS_KEY%`
   - PowerShell: `$env:DIGITALOCEAN_SPACES_ACCESS_KEY`

2. **Restart your IDE/terminal** - Environment variables might not reload automatically

3. **Check the right shell:**
   - macOS: If using Zsh but edited `.bash_profile`, that won't work
   - Use `echo $SHELL` to see which shell you're using

### Problem: "Access Denied" or "Invalid Credentials"

**Solution:**
1. Double-check the **exact** Access Key and Secret Key
2. Verify the Space name is correct (case-sensitive)
3. Ensure your region is correct:
   - Available regions: `nyc3`, `sfo3`, `sgp1`, `lon1`, `ams3`, `blr1`, `tor1`

### Problem: "Connection timeout" or "Unable to reach server"

**Solution:**
1. Check your internet connection
2. Verify Digital Ocean account is in good standing
3. Check if Space is created in the correct region
4. Verify region name is spelled correctly

### Problem: Files uploading but not appearing in Space

**Solution:**
1. Go to Digital Ocean dashboard → Spaces → Your space name
2. Check if files are there with correct permissions
3. Ensure your API token has "Spaces" scope

### Problem: Running on different machine

**Solution:**
You need to set the environment variables on **each machine**:
- Each developer's machine needs these variables set
- Production server needs them set as well
- CI/CD pipeline needs them in secrets/environment configuration

---

## Security Best Practices

### ✅ DO:
- Store credentials in environment variables
- Use separate API keys for development and production
- Rotate API keys regularly
- Use read-only scope when possible
- Add `.env.local` to `.gitignore`

### ❌ DON'T:
- Commit credentials to Git
- Share API keys via email
- Use the same key for multiple environments
- Store keys in code comments
- Enable public read access on sensitive files

---

## Production Deployment

### Docker
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0

ENV DIGITALOCEAN_SPACES_ACCESS_KEY=${DIGITALOCEAN_SPACES_ACCESS_KEY}
ENV DIGITALOCEAN_SPACES_SECRET_KEY=${DIGITALOCEAN_SPACES_SECRET_KEY}
ENV DIGITALOCEAN_SPACES_NAME=${DIGITALOCEAN_SPACES_NAME}
ENV DIGITALOCEAN_SPACES_REGION=${DIGITALOCEAN_SPACES_REGION}
```

### Docker Compose
```yaml
services:
  ocr-backend:
    image: ocr-backend:latest
    environment:
      - DIGITALOCEAN_SPACES_ACCESS_KEY=${DIGITALOCEAN_SPACES_ACCESS_KEY}
      - DIGITALOCEAN_SPACES_SECRET_KEY=${DIGITALOCEAN_SPACES_SECRET_KEY}
      - DIGITALOCEAN_SPACES_NAME=${DIGITALOCEAN_SPACES_NAME}
      - DIGITALOCEAN_SPACES_REGION=${DIGITALOCEAN_SPACES_REGION}
```

### Kubernetes
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: do-spaces-credentials
type: Opaque
stringData:
  access-key: "your_access_key_here"
  secret-key: "your_secret_key_here"
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: do-spaces-config
data:
  DIGITALOCEAN_SPACES_NAME: "your-space-name"
  DIGITALOCEAN_SPACES_REGION: "nyc3"
```

---

## Quick Reference

| OS | Command to Test | File to Edit |
|---|---|---|
| macOS (Zsh) | `echo $DIGITALOCEAN_SPACES_NAME` | `~/.zshrc` |
| macOS (Bash) | `echo $DIGITALOCEAN_SPACES_NAME` | `~/.bash_profile` |
| Windows (CMD) | `echo %DIGITALOCEAN_SPACES_NAME%` | System Properties |
| Windows (PS) | `$env:DIGITALOCEAN_SPACES_NAME` | PowerShell Script |

---

## Next Steps

1. ✅ Set environment variables (macOS or Windows)
2. ✅ Restart your development server
3. ✅ Create a Digital Ocean Space
4. ✅ Get API credentials
5. ✅ Test file upload/download
6. ✅ Check logs for "Storage Mode: Digital Ocean Spaces"

---

**Need Help?**
- Digital Ocean Support: https://support.digitalocean.com
- Check Application Logs: Look for "DigitalOceanStorageService" entries
- Test Connectivity: `ping api.digitaloceanspaces.com`

