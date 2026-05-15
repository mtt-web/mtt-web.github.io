# Upload Lecture Video to KAM Server

This document contains instructions for uploading lecture videos to the KAM server and linking them from course pages.

## Configuration

- **Server**: kam.mff.cuni.cz
- **Username**: vasek
- **Archive Path**: /nfs/archive/vasek/prednasky/
- **Web Path**: ~/WWW/video/ (NOTE: WWW in uppercase, not public_html!)
- **Base URL**: https://kam.mff.cuni.cz/~vasek/video/

## Steps

### 1. Create target directory on server

```bash
sshpass -p 'PASSWORD' ssh vasek@kam.mff.cuni.cz 'mkdir -p /nfs/archive/vasek/prednasky/SEMESTER'
```

Replace `SEMESTER` with the semester code (e.g., `ZS-2025`).

### 2. Convert to MP4 locally

MKV files are not well-supported in browsers. Convert to MP4 locally before uploading:

```bash
ffmpeg -i ~/Downloads/VIDEO_FILE -codec copy ~/Downloads/OUTPUT_NAME.mp4
```

Replace:
- `VIDEO_FILE` with the source video filename
- `OUTPUT_NAME` with desired output filename (e.g., `prednaska-2025-10-01`)

This uses stream copy (no re-encoding) so it's fast and preserves quality.

### 3. Upload MP4 file

```bash
rsync -avP ~/Downloads/OUTPUT_NAME.mp4 --rsh="sshpass -p 'PASSWORD' ssh" vasek@kam.mff.cuni.cz:/nfs/archive/vasek/prednasky/SEMESTER/OUTPUT_NAME.mp4
```

Replace:
- `OUTPUT_NAME` with the output filename from step 2
- `SEMESTER` with the semester code (e.g., `ZS-2025`)

### 4. Create symlink and set permissions

```bash
sshpass -p 'PASSWORD' ssh vasek@kam.mff.cuni.cz '
  mkdir -p ~/WWW/video/SEMESTER &&
  ln -sf /nfs/archive/vasek/prednasky/SEMESTER/OUTPUT_NAME.mp4 \
         ~/WWW/video/SEMESTER/OUTPUT_NAME.mp4 &&
  chmod 755 ~ ~/WWW ~/WWW/video ~/WWW/video/SEMESTER &&
  chmod 644 /nfs/archive/vasek/prednasky/SEMESTER/OUTPUT_NAME.mp4
'
```

### 5. Configure .htaccess for inline video playback

Create or update `.htaccess` to serve videos inline instead of downloading (only needed once):

```bash
sshpass -p 'PASSWORD' ssh vasek@kam.mff.cuni.cz 'cat > ~/WWW/video/.htaccess <<EOF
# Serve video files inline in browser instead of downloading
AddType video/x-matroska .mkv
AddType video/mp4 .mp4
AddType video/webm .webm

# Force inline display
<FilesMatch "\.(mkv|mp4|webm)$">
    Header set Content-Disposition "inline"
</FilesMatch>
EOF
chmod 644 ~/WWW/video/.htaccess'
```

### 6. Verify upload and symlinks

```bash
sshpass -p 'PASSWORD' ssh vasek@kam.mff.cuni.cz '
  ls -lh /nfs/archive/vasek/prednasky/SEMESTER/ &&
  ls -l ~/WWW/video/SEMESTER/
'
```

### 7. Add link to course page

The video will be accessible at:
```
https://kam.mff.cuni.cz/~vasek/video/SEMESTER/OUTPUT_NAME.mp4
```

**Use the .mp4 version** for best browser compatibility. Add this link to the appropriate course page under a "Lecture Recordings" section.

## Example Usage

For a video `~/Downloads/2025-10-01 14-00-40.mkv` for Fall 2025 semester:

- SEMESTER: `ZS-2025`
- VIDEO_FILE: `2025-10-01 14-00-40.mkv`
- OUTPUT_NAME: `prednaska-2025-10-01`

## Requirements

- `sshpass` package must be installed: `sudo apt install sshpass`
- `ffmpeg` must be installed locally: `sudo apt install ffmpeg`
