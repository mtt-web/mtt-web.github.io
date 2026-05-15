# Video Upload Automation Instructions

When the user provides a video filename from ~/Downloads/, automatically execute the upload workflow.

## Process

When user says something like:
- "upload video X"
- "upload the lecture recording X"
- Just provides a filename

### Steps to execute:

1. **Extract information:**
   - VIDEO_FILE: from user's message (e.g., "2025-10-08 14-00-40.mkv")
   - OUTPUT_NAME: generate from date in filename (e.g., "prednaska-2025-10-08")
   - SEMESTER: determine from current date or ask if unclear (e.g., "ZS-2025" for fall semester)

2. **Run the upload script:**
   ```bash
   /home/vasek/vaclavrozhon.github.io/.claude/upload-video-script.sh "VIDEO_FILE" "OUTPUT_NAME" "SEMESTER"
   ```

3. **After upload completes, update the course page:**
   - Find the relevant course page in `teaching/2025/`
   - Add the video link under "Lecture Recordings" section
   - Format: `- [Lecture YYYY-MM-DD](https://kam.mff.cuni.cz/~vasek/video/SEMESTER/OUTPUT_NAME.mp4)`

4. **Commit and push:**
   - Commit the course page update
   - Push to GitHub

## Semester codes

- ZS-YYYY = Fall/Winter semester (zimní semestr)
- LS-YYYY = Spring semester (letní semestr)

Current semester (October 2025): ZS-2025

## Example

User: "upload 2025-10-08 14-00-40.mkv"

Execute:
```bash
/home/vasek/vaclavrozhon.github.io/.claude/upload-video-script.sh "2025-10-08 14-00-40.mkv" "prednaska-2025-10-08" "ZS-2025"
```

Then update teaching/2025/probability-2.md and commit/push.
