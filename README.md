# ComfyDrop 1.1

Browse, download, and delete ComfyUI outputs on a RunPod network volume from Windows, even while your pod is stopped.
Executable download link:
https://github.com/thaw1366/ComfyDrop/releases/download/v1.1.0/ComfyDrop.exe

## Update your existing installation

1. Close the current ComfyDrop window.
2. Extract the new ZIP to a separate folder.
3. Run **Update.cmd** in that new folder and select your existing ComfyDrop folder, such as `Downloads\ComfyDrop`.

The updater migrates old saved settings and encrypted credentials, replaces the program, and opens the updated app. Existing settings take priority. The previous executable is retained as `ComfyDrop.previous.exe`.

For future updates, use **Settings → Downloads & updates → Install update…** and choose the new ComfyDrop ZIP. This installs a local package; it does not check an online release service.

Settings now live in `%LOCALAPPDATA%\ComfyDrop`, separately from the program. Credentials remain encrypted for your Windows account. First launch also imports legacy settings next to the executable if no central profile exists. Use the updater against your existing installation to migrate its settings reliably.

If **Remember credentials** was not enabled in the old app, the credentials were only in memory. Save them in the old app before closing it. Updates cannot recover credentials that were never saved.

## First-time setup

Open **ComfyDrop.exe**, then **Settings**:

1. Under **Network drive**, enter the volume ID and datacenter from [RunPod Storage](https://console.runpod.io/user/storage).
2. Enter your RunPod **S3 access key and secret** from Credentials → S3 API Keys. These differ from a regular RunPod API key. Enable **Remember credentials** to retain them across restarts.
3. Under **Downloads & updates**, choose your download folder. Click **Save settings**.
4. On the main screen, set **Folder in drive** to `ComfyUI/output/` and click **Load folder**. If empty, use **Drive root** to browse. Some templates use `runpod-slim/ComfyUI/output/`.

Paths are relative to the volume: `/workspace/ComfyUI/output` usually becomes `ComfyUI/output/`. Direct access requires a datacenter with S3 support. See [RunPod's documentation](https://docs.runpod.io/storage/s3-api).

## Everyday use

- Double-click folders to open them. Search filters the current folder.
- Select files/folders with Ctrl or Shift, then choose **Download selected**, or **Download whole folder**.
- Downloads go directly to your chosen destination, preserving subfolders. No timestamped transfer directory is created. Existing filenames are kept; network downloads get names such as `image (2).png`.
- **Delete selected…** lists selected items and their folder contents. Nothing is deleted until you confirm **Delete permanently**. This affects the network drive, not local downloads. Deletion is permanent; cancellation cannot restore items already deleted.
- **Show console / Hide console** expands or collapses details. Errors open it automatically.
- **Open downloads** opens your local destination.

Downloads stream to disk. Cancellation keeps completed files and removes the current incomplete S3 download. Retrying starts again; resume and image thumbnails are not included.

## Optional runpodctl tools

The other tabs work with running pods. Settings are under **Settings → runpodctl / SSH**.

SSH browsing requires Windows OpenSSH, a verified host fingerprint, key authentication, Python 3 and runpodctl on the pod. Passphrase-protected keys need ssh-agent. Transfer-code mode lets you run the copied command on the pod and paste its code into the app.

These transfers also use your selected destination. runpodctl may overwrite matching filenames, so the app asks before receiving into a nonempty folder. Folder transfers may arrive as ZIP files; cancellation may leave partial files.

The bundled official Windows x64 runpodctl v2.14.0 was verified against its published SHA-256:
`026be0b38d0cc7e68bd2ff21042ebce71b726d0086b160772933d6cea38691f8`

## Validation and source

Built and visually checked on Windows. Local fixtures cover listings, encoding, downloads, cancellation, signed DELETE requests, deletion scope/order, duplicate filenames, profile migration and updates preserving settings. No live RunPod deletion was performed during development.

Source and `Build.ps1` are included. No Python, Node or AWS CLI is needed. Windows 10/11 with .NET Framework 4.8 is recommended. runpodctl's license is included in `runpodctl-LICENSE.txt`; upstream: https://github.com/runpod/runpodctl.
