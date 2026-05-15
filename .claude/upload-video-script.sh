#!/bin/bash

# Automated lecture video upload script for KAM server
# Usage: ./upload-video-script.sh <video-filename> <output-name> <semester>
# Example: ./upload-video-script.sh "2025-10-01 14-00-40.mkv" "prednaska-2025-10-01" "ZS-2025"

VIDEO_FILE="$1"
OUTPUT_NAME="$2"
SEMESTER="$3"
PASSWORD="Pololopo0"

if [ -z "$VIDEO_FILE" ] || [ -z "$OUTPUT_NAME" ] || [ -z "$SEMESTER" ]; then
    echo "Usage: $0 <video-filename> <output-name> <semester>"
    echo "Example: $0 '2025-10-01 14-00-40.mkv' 'prednaska-2025-10-01' 'ZS-2025'"
    exit 1
fi

echo "=== Step 1: Creating directory on server ==="
sshpass -p "$PASSWORD" ssh vasek@kam.mff.cuni.cz "mkdir -p /nfs/archive/vasek/prednasky/$SEMESTER"

echo ""
echo "=== Step 2: Converting to MP4 locally ==="
ffmpeg -i ~/Downloads/"$VIDEO_FILE" -codec copy ~/Downloads/"$OUTPUT_NAME.mp4"

echo ""
echo "=== Step 3: Uploading MP4 to server ==="
rsync -avP ~/Downloads/"$OUTPUT_NAME.mp4" --rsh="sshpass -p '$PASSWORD' ssh" vasek@kam.mff.cuni.cz:/nfs/archive/vasek/prednasky/$SEMESTER/$OUTPUT_NAME.mp4

echo ""
echo "=== Step 4: Creating symlink and setting permissions ==="
sshpass -p "$PASSWORD" ssh vasek@kam.mff.cuni.cz "
  mkdir -p ~/WWW/video/$SEMESTER &&
  ln -sf /nfs/archive/vasek/prednasky/$SEMESTER/$OUTPUT_NAME.mp4 \
         ~/WWW/video/$SEMESTER/$OUTPUT_NAME.mp4 &&
  chmod 755 ~ ~/WWW ~/WWW/video ~/WWW/video/$SEMESTER &&
  chmod 644 /nfs/archive/vasek/prednasky/$SEMESTER/$OUTPUT_NAME.mp4
"

echo ""
echo "=== Step 5: Verifying upload ==="
sshpass -p "$PASSWORD" ssh vasek@kam.mff.cuni.cz "
  ls -lh /nfs/archive/vasek/prednasky/$SEMESTER/ &&
  echo '' &&
  ls -l ~/WWW/video/$SEMESTER/
"

echo ""
echo "=== DONE ==="
echo "Video URL: https://kam.mff.cuni.cz/~vasek/video/$SEMESTER/$OUTPUT_NAME.mp4"
echo ""
echo "Add this to your course page:"
echo "- [Lecture $(date +%Y-%m-%d)](https://kam.mff.cuni.cz/~vasek/video/$SEMESTER/$OUTPUT_NAME.mp4)"
