#!/bin/bash

echo "======================================"
echo "🔧 Aevatar Framework - Restore Frontend Libraries"
echo "======================================"
echo ""

# Check if we're in the right directory
if [ ! -f "DEPLOYMENT.md" ]; then
    echo "❌ Error: Please run this script from the project root directory"
    exit 1
fi

# Function to check if a command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Try ABP CLI first
if command_exists abp; then
    echo "✅ ABP CLI found, using abp install-libs..."
    echo ""
    
    echo "1. Restoring AuthServer libraries..."
    cd src/Aevatar.BusinessServer/src/Aevatar.AuthServer
    if abp install-libs; then
        echo "   ✅ AuthServer libraries restored"
    else
        echo "   ⚠️  abp install-libs failed for AuthServer"
        ABP_FAILED=1
    fi
    
    echo ""
    echo "2. Restoring BusinessServer libraries..."
    cd ../Aevatar.BusinessServer.Web
    if abp install-libs; then
        echo "   ✅ BusinessServer libraries restored"
    else
        echo "   ⚠️  abp install-libs failed for BusinessServer"
        ABP_FAILED=1
    fi
    
    if [ -z "$ABP_FAILED" ]; then
        echo ""
        echo "======================================"
        echo "✅ All frontend libraries restored!"
        echo "======================================"
        exit 0
    fi
else
    echo "⚠️  ABP CLI not found"
    echo "   To install: dotnet tool install -g Volo.Abp.Cli"
    echo ""
fi

# If ABP CLI failed or not available, use manual method
echo "======================================"
echo "📦 Using manual restoration method..."
echo "======================================"
echo ""

cd "$(dirname "$0")/src/Aevatar.BusinessServer"

# Check if BusinessServer already has libs
if [ -d "src/Aevatar.BusinessServer.Web/wwwroot/libs" ] && [ "$(ls -A src/Aevatar.BusinessServer.Web/wwwroot/libs 2>/dev/null)" ]; then
    echo "✅ BusinessServer libraries already exist"
    
    echo ""
    echo "Copying to AuthServer..."
    mkdir -p src/Aevatar.AuthServer/wwwroot/libs
    cp -r src/Aevatar.BusinessServer.Web/wwwroot/libs/* src/Aevatar.AuthServer/wwwroot/libs/
    
    echo ""
    echo "======================================"
    echo "✅ Frontend libraries restored!"
    echo "======================================"
    exit 0
fi

# If no existing libs, need to install from npm
echo "⚠️  No existing libraries found"
echo "   You need to either:"
echo "   1. Install ABP CLI: dotnet tool install -g Volo.Abp.Cli"
echo "   2. Get libs archive from team lead"
echo "   3. Manually copy from another deployment"
echo ""
echo "❌ Manual restoration failed"
exit 1

