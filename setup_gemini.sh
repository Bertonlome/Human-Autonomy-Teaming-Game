#!/bin/bash

# Quick setup script for Gemini API key

echo "╔════════════════════════════════════════════════╗"
echo "║   Gemini API Setup for HAT Game               ║"
echo "╚════════════════════════════════════════════════╝"
echo ""

# Detect Godot user data directory
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    USER_DIR="$HOME/.local/share/HAT_Game"
elif [[ "$OSTYPE" == "darwin"* ]]; then
    USER_DIR="$HOME/Library/Application Support/HAT_Game"
elif [[ "$OSTYPE" == "msys" ]] || [[ "$OSTYPE" == "win32" ]]; then
    USER_DIR="$APPDATA/HAT_Game"
else
    echo "❌ Could not detect your operating system"
    echo "Please manually create the config file as described in GEMINI_SETUP.md"
    exit 1
fi

CONFIG_FILE="$USER_DIR/gemini_config.txt"

echo "📁 Detected config directory: $USER_DIR"
echo ""

# Check if API key is provided
if [ -z "$1" ]; then
    echo "Usage: ./setup_gemini.sh YOUR_API_KEY"
    echo ""
    echo "Get your API key from: https://makersuite.google.com/app/apikey"
    echo ""
    echo "Example:"
    echo "  ./setup_gemini.sh AIzaSyXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX"
    echo ""
    exit 1
fi

API_KEY="$1"

# Validate API key format (basic check)
if [[ ! "$API_KEY" =~ ^AIza ]]; then
    echo "⚠️  Warning: API key doesn't look valid (should start with 'AIza')"
    echo "Continuing anyway..."
    echo ""
fi

# Create directory if it doesn't exist
mkdir -p "$USER_DIR"

# Save API key
echo "$API_KEY" > "$CONFIG_FILE"

if [ $? -eq 0 ]; then
    echo "✅ API key saved successfully!"
    echo ""
    echo "Config file: $CONFIG_FILE"
    echo ""
    
    # Test the API key
    echo "🧪 Testing API key..."
    echo ""
    
    if command -v curl &> /dev/null; then
        ./test_gemini_api.sh "$API_KEY"
    else
        echo "⚠️  curl not found, skipping API test"
        echo "Your API key has been saved, but you should test it manually"
    fi
else
    echo "❌ Failed to save API key"
    echo "You may need to create the directory manually:"
    echo "  mkdir -p \"$USER_DIR\""
    echo "  echo \"YOUR_API_KEY\" > \"$CONFIG_FILE\""
    exit 1
fi

echo ""
echo "🎮 You're all set! Launch the game and try path optimization."
