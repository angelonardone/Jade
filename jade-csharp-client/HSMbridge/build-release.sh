#!/bin/bash

# HSMbridge Release Build Script
# Creates self-contained executables for Windows, macOS, and Linux

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR"
OUTPUT_DIR="$SCRIPT_DIR/releases"
VERSION="${1:-1.0.0}"

echo "=========================================="
echo "HSMbridge Release Builder"
echo "Version: $VERSION"
echo "=========================================="
echo ""

# Clean previous builds
echo "Cleaning previous builds..."
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# Define target platforms
# Format: "RID:FolderName:Description"
TARGETS=(
    "win-x64:windows-x64:Windows (64-bit)"
    "win-arm64:windows-arm64:Windows ARM64"
    "osx-x64:macos-x64:macOS Intel (64-bit)"
    "osx-arm64:macos-arm64:macOS Apple Silicon (M1/M2/M3)"
    "linux-x64:linux-x64:Linux (64-bit)"
    "linux-arm64:linux-arm64:Linux ARM64"
)

# Build for each target
for TARGET in "${TARGETS[@]}"; do
    IFS=':' read -r RID FOLDER DESC <<< "$TARGET"

    echo ""
    echo "--- Building for $DESC ---"
    echo "    Runtime: $RID"

    PUBLISH_DIR="$OUTPUT_DIR/$FOLDER"

    # Restore NuGet packages specifically for this runtime identifier
    # This ensures all platform-specific native libraries are downloaded
    echo "    Restoring packages for $RID..."
    dotnet restore "$PROJECT_DIR/HSMbridge.csproj" \
        --runtime "$RID"

    # Publish with explicit runtime and ensure native libs are bundled correctly
    dotnet publish "$PROJECT_DIR/HSMbridge.csproj" \
        --configuration Release \
        --runtime "$RID" \
        --self-contained true \
        --output "$PUBLISH_DIR" \
        --no-restore \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:IncludeAllContentForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:Version="$VERSION"

    # Copy config file
    cp "$PROJECT_DIR/appsettings.json" "$PUBLISH_DIR/"

    # Copy documentation
    cp "$PROJECT_DIR/README.md" "$PUBLISH_DIR/"

    # Create zip archive
    echo "    Creating archive..."
    ARCHIVE_NAME="HSMbridge-$VERSION-$FOLDER"
    (cd "$OUTPUT_DIR" && zip -r -q "$ARCHIVE_NAME.zip" "$FOLDER")

    # Calculate size
    if [[ "$RID" == win-* ]]; then
        EXE_SIZE=$(du -h "$PUBLISH_DIR/HSMbridge.exe" | cut -f1)
    else
        EXE_SIZE=$(du -h "$PUBLISH_DIR/HSMbridge" | cut -f1)
    fi
    ZIP_SIZE=$(du -h "$OUTPUT_DIR/$ARCHIVE_NAME.zip" | cut -f1)

    echo "    Executable size: $EXE_SIZE"
    echo "    Archive size: $ZIP_SIZE"
    echo "    Output: $OUTPUT_DIR/$ARCHIVE_NAME.zip"
done

echo ""
echo "=========================================="
echo "Build Complete!"
echo "=========================================="
echo ""
echo "Release archives created in: $OUTPUT_DIR"
echo ""
ls -lh "$OUTPUT_DIR"/*.zip
echo ""
echo "To distribute:"
echo "  1. Upload the .zip files to GitHub Releases"
echo "  2. Users download and extract for their platform"
echo "  3. Run the HSMbridge executable (no .NET required)"
