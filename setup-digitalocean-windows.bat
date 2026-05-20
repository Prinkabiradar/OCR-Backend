@echo off
REM OCR Application - Digital Ocean Spaces Setup Script for Windows
REM This script helps you configure Digital Ocean Spaces credentials securely

REM Check for admin privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo.
    echo [ERROR] This script must be run as Administrator!
    echo Right-click cmd.exe and select "Run as administrator"
    echo.
    pause
    exit /b 1
)

cls
echo.
echo ════════════════════════════════════════════════════════════════════════════
echo     OCR Application - Digital Ocean Spaces Setup
echo     Windows Configuration Script
echo ════════════════════════════════════════════════════════════════════════════
echo.
echo [WARNING] This script will set system environment variables!
echo [WARNING] Keep your credentials PRIVATE!
echo [WARNING] Never commit them to Git or share them.
echo.
pause

cls

REM Prompt for credentials
set /p ACCESS_KEY="Enter Access Key: "
if "%ACCESS_KEY%"=="" (
    echo.
    echo [ERROR] Access Key is required!
    pause
    exit /b 1
)

set /p SECRET_KEY="Enter Secret Key: "
if "%SECRET_KEY%"=="" (
    echo.
    echo [ERROR] Secret Key is required!
    pause
    exit /b 1
)

set /p SPACE_NAME="Enter Space Name (e.g., ocr-documents): "
if "%SPACE_NAME%"=="" (
    echo.
    echo [ERROR] Space Name is required!
    pause
    exit /b 1
)

set /p REGION="Enter Region (default: nyc3) [nyc3/sfo3/sgp1/lon1/ams3/blr1/tor1]: "
if "%REGION%"=="" (
    set REGION=nyc3
)

cls
echo.
echo Configuring environment variables...
echo.

REM Set environment variables for current user
setx DIGITALOCEAN_SPACES_ACCESS_KEY "%ACCESS_KEY%"
if errorlevel 1 (
    echo [ERROR] Failed to set DIGITALOCEAN_SPACES_ACCESS_KEY
    pause
    exit /b 1
)
echo [OK] DIGITALOCEAN_SPACES_ACCESS_KEY set

setx DIGITALOCEAN_SPACES_SECRET_KEY "%SECRET_KEY%"
if errorlevel 1 (
    echo [ERROR] Failed to set DIGITALOCEAN_SPACES_SECRET_KEY
    pause
    exit /b 1
)
echo [OK] DIGITALOCEAN_SPACES_SECRET_KEY set

setx DIGITALOCEAN_SPACES_NAME "%SPACE_NAME%"
if errorlevel 1 (
    echo [ERROR] Failed to set DIGITALOCEAN_SPACES_NAME
    pause
    exit /b 1
)
echo [OK] DIGITALOCEAN_SPACES_NAME set

setx DIGITALOCEAN_SPACES_REGION "%REGION%"
if errorlevel 1 (
    echo [ERROR] Failed to set DIGITALOCEAN_SPACES_REGION
    pause
    exit /b 1
)
echo [OK] DIGITALOCEAN_SPACES_REGION set

echo.
echo ════════════════════════════════════════════════════════════════════════════
echo Configuration Complete!
echo ════════════════════════════════════════════════════════════════════════════
echo.
echo Configured values:
echo   Space Name: %SPACE_NAME%
echo   Region: %REGION%
echo.
echo [IMPORTANT] You must restart your application/IDE for changes to take effect!
echo.
echo To verify configuration manually:
echo   - Open a new Command Prompt window
echo   - Type: echo %%DIGITALOCEAN_SPACES_NAME%%
echo   - It should print: %SPACE_NAME%
echo.
pause
