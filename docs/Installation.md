# Installing EmoTracker

EmoTracker is available for Windows, macOS, and Linux. Download the latest release from the
[Releases page](https://github.com/EmoTracker-Community/EmoTracker/releases) and follow the
instructions for your platform below.

---

## Windows

### 1. Download

From the Releases page, download **`EmoTracker-VERSION-win-x64.zip`**.

### 2. Choose an install location

Extract the zip to a permanent folder of your choice — for example `C:\EmoTracker` or
`C:\Games\EmoTracker`. EmoTracker runs directly from this folder; there is no traditional installer.

> **Warning: Avoid Windows-protected folders (Controlled Folder Access)**
>
> If Windows Defender's **Controlled Folder Access (CFA)** feature is enabled on your system,
> placing EmoTracker inside any of the following default protected folders will prevent the
> built-in auto-updater from working:
>
> - Desktop
> - Documents (My Documents)
> - Pictures
> - Videos
> - Music
>
> EmoTracker will detect this situation and abort the update with a warning rather than fail
> silently. To avoid the issue entirely, install EmoTracker somewhere outside these folders,
> such as `C:\EmoTracker` or a subfolder of `C:\Games`.
>
> If you must keep EmoTracker inside a protected folder, you can whitelist `EmoTracker.exe` in
> **Windows Security → Virus & threat protection → Ransomware protection →
> Allow an app through Controlled Folder Access**.

### 3. Run

Open the extracted folder and double-click **`EmoTracker.exe`**.

---

## macOS

### 1. Download

From the Releases page, download **`EmoTracker-VERSION-osx-universal.tar.gz`**.

This is a universal binary that runs natively on both Apple Silicon (arm64) and Intel (x64) Macs.
macOS 10.15 Catalina or later is required.

### 2. Extract

Double-click the `.tar.gz` file in Finder, or run the following in Terminal:

```sh
tar xf EmoTracker-VERSION-osx-universal.tar.gz
```

This produces `EmoTracker.app`.

### 3. Move to Applications

Drag `EmoTracker.app` into your `/Applications` folder.

> **Warning: Running from outside `/Applications` will break auto-updates**
>
> macOS applies **App Translocation** to apps that are run directly from a download location
> (e.g. your Downloads folder or an unextracted archive). When translocation is active, macOS
> runs the app from a hidden read-only path, which prevents the built-in auto-updater from
> replacing the app bundle. EmoTracker will detect this and abort the update with a warning
> rather than fail silently.
>
> Moving `EmoTracker.app` to `/Applications` (or any other permanent folder outside your
> Downloads directory) clears the translocation flag and allows updates to work correctly.

### 4. First launch — Gatekeeper

Because EmoTracker is distributed outside the Mac App Store, macOS will block it on the first
launch. To open it:

1. **Right-click** (or Control-click) `EmoTracker.app` and choose **Open**.
2. Click **Open** in the dialog that appears.

You only need to do this once. Subsequent launches work normally.

Alternatively, if you already double-clicked and got a "cannot be opened" message:

1. Open **System Settings → Privacy & Security**.
2. Scroll down to the Security section and click **Open Anyway** next to the EmoTracker entry.

> **Note:** The built-in auto-updater automatically removes the quarantine attribute from
> downloaded updates, so you will not need to repeat this process after each update.

### 5. Run

Double-click `EmoTracker.app` in Finder, or open it from Launchpad.

---

## Linux

### 1. Download

From the Releases page, download **`EmoTracker-VERSION-linux-x64.zip`**.

### 2. Extract

```sh
unzip EmoTracker-VERSION-linux-x64.zip -d EmoTracker
```

This creates an `EmoTracker` directory containing the application files.

### 3. Make the binary executable

```sh
chmod +x EmoTracker/EmoTracker
```

### 4. Run

```sh
./EmoTracker/EmoTracker
```

You can also create a desktop shortcut or launcher entry pointing to the `EmoTracker` binary.

> **Note:** The release includes a self-contained .NET 8 runtime, so no separate .NET
> installation is required. Audio playback uses the system audio libraries — make sure your
> distribution has ALSA or PulseAudio/PipeWire available (this is standard on most desktop
> distributions).

---

## Updating

EmoTracker includes a built-in auto-updater. When a new release is available you will be
prompted inside the application. Accepting the update downloads, installs, and relaunches
EmoTracker automatically. No manual download is required for subsequent updates.
