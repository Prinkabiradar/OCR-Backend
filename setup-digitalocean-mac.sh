#!/bin/bash
# OCR Application - Digital Ocean Spaces Setup Script for macOS
# This script helps you configure Digital Ocean Spaces credentials securely

set -e

echo "╔═══════════════════════════════════════════════════════════════════════════╗"
echo "║    OCR Application - Digital Ocean Spaces Setup                          ║"
echo "║    macOS Configuration Script                                            ║"
echo "╚═══════════════════════════════════════════════════════════════════════════╝"
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print colored output
print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if running on macOS
if [[ "$OSTYPE" != "darwin"* ]]; then
    print_error "This script is for macOS only. Use the Windows batch script on Windows."
    exit 1
fi

print_info "This script will add Digital Ocean Spaces credentials to your shell configuration."
echo ""
print_warning "IMPORTANT: Keep your credentials PRIVATE!"
print_warning "Never commit them to Git or share them."
echo ""

# Detect shell
if [[ -n "$ZSH_VERSION" ]]; then
    SHELL_RC="$HOME/.zshrc"
    print_info "Detected shell: ZSH"
elif [[ -n "$BASH_VERSION" ]]; then
    SHELL_RC="$HOME/.bash_profile"
    print_info "Detected shell: BASH"
else
    print_error "Could not detect shell. Please manually edit:"
    print_error "  ~/.zshrc (for Zsh) or ~/.bash_profile (for Bash)"
    exit 1
fi

echo ""
echo "╔─────────────────────────────────────────────────────────────────────────╗"
echo "│ Digital Ocean Spaces Credentials                                       │"
echo "╚─────────────────────────────────────────────────────────────────────────╝"
echo ""
read -sp "Enter Access Key: " ACCESS_KEY
echo ""
read -sp "Enter Secret Key: " SECRET_KEY
echo ""
read -p "Enter Space Name (e.g., ocr-documents): " SPACE_NAME
read -p "Enter Region (default: nyc3) [nyc3/sfo3/sgp1/lon1/ams3/blr1/tor1]: " REGION

# Set default region if not provided
REGION=${REGION:-nyc3}

# Validate inputs
if [[ -z "$ACCESS_KEY" ]] || [[ -z "$SECRET_KEY" ]] || [[ -z "$SPACE_NAME" ]]; then
    print_error "All fields are required!"
    exit 1
fi

echo ""
echo "Configuring..."
echo ""

# Check if variables already exist in config file
if grep -q "DIGITALOCEAN_SPACES_ACCESS_KEY" "$SHELL_RC" 2>/dev/null; then
    print_warning "Configuration already found in $SHELL_RC"
    read -p "Do you want to overwrite it? (y/n): " -n 1 -r
    echo ""
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        print_info "Skipped."
        exit 0
    fi
    # Remove old configuration
    sed -i '' '/DIGITALOCEAN_SPACES_ACCESS_KEY/d' "$SHELL_RC"
    sed -i '' '/DIGITALOCEAN_SPACES_SECRET_KEY/d' "$SHELL_RC"
    sed -i '' '/DIGITALOCEAN_SPACES_NAME/d' "$SHELL_RC"
    sed -i '' '/DIGITALOCEAN_SPACES_REGION/d' "$SHELL_RC"
fi

# Add configuration to shell rc file
{
    echo ""
    echo "# ═══════════════════════════════════════════════════════════════════"
    echo "# Digital Ocean Spaces Configuration (OCR Application)"
    echo "# ═══════════════════════════════════════════════════════════════════"
    echo "export DIGITALOCEAN_SPACES_ACCESS_KEY=\"$ACCESS_KEY\""
    echo "export DIGITALOCEAN_SPACES_SECRET_KEY=\"$SECRET_KEY\""
    echo "export DIGITALOCEAN_SPACES_NAME=\"$SPACE_NAME\""
    echo "export DIGITALOCEAN_SPACES_REGION=\"$REGION\""
    echo "# ═══════════════════════════════════════════════════════════════════"
} >> "$SHELL_RC"

print_info "Configuration added to $SHELL_RC"
echo ""

# Reload shell configuration
source "$SHELL_RC"

echo "╔─────────────────────────────────────────────────────────────────────────╗"
echo "│ Verification                                                           │"
echo "╚─────────────────────────────────────────────────────────────────────────╝"
echo ""

# Verify
if [[ "$DIGITALOCEAN_SPACES_NAME" == "$SPACE_NAME" ]]; then
    print_info "✓ Configuration verified successfully!"
    echo ""
    echo "Configured values:"
    echo "  Space Name: $DIGITALOCEAN_SPACES_NAME"
    echo "  Region: $DIGITALOCEAN_SPACES_REGION"
    echo ""
    print_info "Configuration complete!"
    print_info "Restart your terminal or development server for changes to take effect."
else
    print_warning "Could not verify configuration. You may need to restart your terminal."
    echo ""
    echo "To manually verify, run:"
    echo "  source $SHELL_RC"
    echo "  echo \$DIGITALOCEAN_SPACES_NAME"
fi

echo ""
print_info "Setup complete! Your credentials are securely stored."
echo ""
