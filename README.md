<p align="center">
  <img src="assets/banner.png" alt="Subgen for Jellyfin" width="560">
</p>

<p align="center">
  <b>Make good subtitles for any episode or movie in Jellyfin, with one click.</b><br>
  A Jellyfin plugin that sends your media to a <a href="https://github.com/McCloudS/subgen">Subgen</a> server,
  which writes the subtitle with Whisper (fast on an NVIDIA GPU).
</p>

<p align="center">
  <img alt="Jellyfin" src="https://img.shields.io/badge/Jellyfin-12.4-00a4dc">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512bd4">
  <img alt="License" src="https://img.shields.io/badge/license-GPL--3.0-blue">
</p>

> **Unofficial.** This plugin is not made by the Jellyfin team or by the Subgen author.
> It only talks to a Subgen server that you run yourself.

---

## What it does

Downloaded subtitles are often for a different release: wrong timing, wrong episode,
or text that sits on screen for 10 seconds. Subgen listens to the real audio of *your*
file and writes a subtitle that matches it. This plugin puts that inside Jellyfin:

| Feature | Where | What happens |
|---|---|---|
| **CC button** | Item page of a **series, season, episode or movie** (web client, admins only) | Sends every video under that item to Subgen. Videos that already have an English subtitle are skipped. A small message at the bottom of the screen says how many were sent. |
| **"Generate with Subgen"** | **⋮ → Edit subtitles → Search** on an episode or movie | Jellyfin extracts the audio, Subgen writes the subtitle, Jellyfin saves it. Takes about 1 minute. |

Together with Subgen's own Jellyfin webhook (optional, see [step 6](#6-optional-automatic-subtitles-on-play-and-for-new-episodes)),
you can also get subtitles automatically when you press play or when a new episode is added.

```
 Jellyfin  ──(CC button / subtitle search)──►  Subgen server  ──►  Whisper (GPU)
    ▲                                                │
    └──────────── video.en.srt next to the video ◄───┘
```

---

## Requirements

- **Jellyfin 12.4** or newer (server).
- A **Subgen** server that Jellyfin can reach over the network.
  - **NVIDIA GPU strongly recommended.** An RTX 3080 Ti makes a 10-minute episode in about 25 seconds.
  - CPU works too, but expect 10–20 minutes per episode.
- For the **CC button**: Subgen must see your media at **the same paths** as Jellyfin
  (see [Paths](#paths-important)).
- The **CC button** shows in the Jellyfin **web client** (and apps that load the web client,
  such as Jellyfin Media Player). Native apps (Android TV, Swiftfin, Findroid …) do not show it.
  "Edit subtitles → Search" works anywhere the app offers subtitle search.

---

## Setup

### 1. Run Subgen

Subgen runs as a Docker container. For a GPU you need the NVIDIA driver and the
[NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html)
on the Docker host. Test it first:

```bash
docker run --rm --gpus all ubuntu nvidia-smi
```

**docker run (NVIDIA GPU):**

```bash
docker run -d --name subgen --restart unless-stopped --gpus all -p 9000:9000 \
  -v /media/tv:/media/tv \
  -v /media/movies:/media/movies \
  -v subgen_models:/subgen/models \
  -e TRANSCRIBE_DEVICE=cuda \
  -e WHISPER_MODEL=large-v3-turbo \
  -e CONCURRENT_TRANSCRIPTIONS=1 \
  -e 'SUBGEN_KWARGS={"vad": True}' \
  -e SUBTITLE_LANGUAGE_NAME=en \
  -e SHOW_IN_SUBNAME_SUBGEN=False \
  -e SHOW_IN_SUBNAME_MODEL=False \
  -e SKIP_ONLY_SUBGEN_SUBTITLES=True \
  -e JELLYFIN_SERVER=http://your-jellyfin:8096 \
  -e JELLYFIN_TOKEN=your_jellyfin_api_key \
  mccloud/subgen:latest
```

**docker-compose.yml (same settings):**

```yaml
services:
  subgen:
    image: mccloud/subgen:latest
    container_name: subgen
    restart: unless-stopped
    ports:
      - "9000:9000"
    volumes:
      - /media/tv:/media/tv            # same path as in Jellyfin
      - /media/movies:/media/movies    # same path as in Jellyfin
      - subgen_models:/subgen/models   # keeps the downloaded Whisper model
    environment:
      - TRANSCRIBE_DEVICE=cuda
      - WHISPER_MODEL=large-v3-turbo
      - CONCURRENT_TRANSCRIPTIONS=1
      - 'SUBGEN_KWARGS={"vad": True}'
      - SUBTITLE_LANGUAGE_NAME=en
      - SHOW_IN_SUBNAME_SUBGEN=False
      - SHOW_IN_SUBNAME_MODEL=False
      - SKIP_ONLY_SUBGEN_SUBTITLES=True
      - JELLYFIN_SERVER=http://your-jellyfin:8096
      - JELLYFIN_TOKEN=your_jellyfin_api_key
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: all
              capabilities: [gpu]
volumes:
  subgen_models:
```

**What each setting does**

| Setting | Value | Why |
|---|---|---|
| `TRANSCRIBE_DEVICE` | `cuda` | Use the NVIDIA GPU. Use `cpu` if you have none. |
| `WHISPER_MODEL` | `large-v3-turbo` | Fast and accurate. `medium` or `small` are lighter for CPU. |
| `CONCURRENT_TRANSCRIPTIONS` | `1` | **Important.** With 2 or more jobs at once *and* VAD on, Subgen can crash (`SIGSEGV`) and lose its queue. |
| `SUBGEN_KWARGS` | `{"vad": True}` | Voice activity detection: skips music and silence, so Whisper does not invent text in the intro. Keep the single quotes around it. |
| `SUBTITLE_LANGUAGE_NAME` | `en` | Language part of the file name: `Episode.en.srt`. |
| `SHOW_IN_SUBNAME_SUBGEN` / `SHOW_IN_SUBNAME_MODEL` | `False` | Clean file names (`Episode.en.srt` instead of `Episode.subgen.large-v3-turbo.en.srt`), so Jellyfin shows just "English". |
| `SKIP_ONLY_SUBGEN_SUBTITLES` | `True` | Only skip a file when Subgen's own subtitle exists, not when some other old subtitle exists. |
| `JELLYFIN_SERVER` / `JELLYFIN_TOKEN` | your server / API key | Only needed for the automatic webhook in step 6. Make a key in **Dashboard → API Keys**. |
| `-v subgen_models:/subgen/models` | | Keeps the model (about 1.5 GB) between restarts. |

**Other ways to run Subgen**

- **CPU only:** use the image `mccloud/subgen:cpu`, set `TRANSCRIBE_DEVICE=cpu`, and drop `--gpus all` / the `deploy:` block.
- **Proxmox:** run Docker in a VM or LXC that has the GPU passed through. Mount the media there at the same paths as in Jellyfin (NFS or SMB is fine).
- **Windows:** Docker Desktop with the WSL 2 backend supports NVIDIA GPUs. Not tested with this plugin.
- More options and all settings: [Subgen's README](https://github.com/McCloudS/subgen).

Check that Subgen runs: open `http://your-server:9000/docs` in a browser.

### Paths (important)

| Feature | Needs the same paths? |
|---|---|
| **CC button** | **Yes.** The plugin sends the file path to Subgen, and Subgen opens that file itself. |
| **Edit subtitles → Search** | No. Jellyfin sends the audio to Subgen. |
| **Webhook (step 6)** | Yes, or set Subgen's `USE_PATH_MAPPING=True`, `PATH_MAPPING_FROM` and `PATH_MAPPING_TO`. |

Example: if Jellyfin sees a show at `/media/tv/Show/Season 1/Episode.mkv`, Subgen must see
the same file at `/media/tv/Show/Season 1/Episode.mkv`. Use the same volume paths on both
containers. Subgen also needs **write** access there, because it saves the `.srt` next to the video.

Subgen runs as user `99`, group `100` by default. On an SMB/CIFS mount, mount the share with
`uid=99,gid=100,file_mode=0664,dir_mode=0775`, or the subtitle cannot be saved.

### 2. Install the plugin

1. In Jellyfin, open **Dashboard → Plugins → Repositories** (or **Catalog → ⚙**).
2. Click **+** and add:
   - **Name:** `Subgen`
   - **URL:** `https://raw.githubusercontent.com/JRDevo/jellyfin-plugin-subgen/main/manifest.json`
3. Open **Catalog**, find **Subgen** under **Subtitles**, click **Install**.
4. **Restart Jellyfin.**

### 3. Point the plugin at Subgen

**Dashboard → Plugins → Subgen**

| Setting | Example | Notes |
|---|---|---|
| **Subgen URL** | `http://192.168.1.20:9000` | Address of your Subgen server, as Jellyfin can reach it. |
| **Timeout (minutes)** | `20` | Longest wait for one subtitle from the subtitle search. |

### 4. Turn it on for your libraries

**Dashboard → Libraries → (your TV or Movies library) → Manage library**

- **Subtitle downloaders:** tick **Subgen**.
- **Download languages:** leave **empty**, unless you really want Jellyfin's nightly
  "Download missing subtitles" task to send your **whole library** to Subgen.
- **Save subtitles into media folders:** tick it.
- **Only download subtitles that are a perfect match:** can stay on. Subgen results always count as a perfect match.

### 5. User rights

- The **CC button** shows only for **administrators**.
- **Edit subtitles** needs **Dashboard → Users → (user) → Allow subtitle management** (or administrator).

After installing or updating, press **Ctrl + F5** in the browser once so the web page reloads.

### 6. Optional: automatic subtitles on play and for new episodes

This part is Subgen's own feature, not this plugin's. It uses the official Jellyfin
**Webhook** plugin:

1. Install **Webhook** from the Jellyfin catalog and restart.
2. **Dashboard → Plugins → Webhook → Add Generic Destination**
   - **Webhook URL:** `http://your-server:9000/jellyfin`
   - Tick **Item Added** and **Playback Start**.
   - Tick **Send All Properties (ignores template)**.
   - **Add Request Header:** `Content-Type` = `application/json`.
3. Save. Subgen also needs `JELLYFIN_SERVER` and `JELLYFIN_TOKEN` (step 1).

Now pressing play on a video without a subtitle makes one in the background (ready after
about 1 minute; stop and start again to pick it), and new episodes get one when they are added.

---

## Using it

- **One episode, a season or a whole series:** open its page and click the **CC** button
  (after the ♥). A message says how many files were sent. About 30–60 seconds each on a GPU.
- **Replace a bad subtitle:** **⋮ → Edit subtitles**, delete the old one, then either click
  **CC** or use **Search → Generate with Subgen**.
- Subtitles are saved as `Video name.en.srt` next to the video.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| No CC button | Press **Ctrl + F5**. Check you are an administrator. Native apps do not show it. |
| "Subgen: request failed" | Check the **Subgen URL** in the plugin settings, and that Jellyfin can reach port 9000. |
| Button says "sent", but nothing appears | Check Subgen's log (`docker logs subgen`). Usually the paths differ between Jellyfin and Subgen, or Subgen cannot write to the folder. |
| Subgen log shows `SIGSEGV` and stops | Set `CONCURRENT_TRANSCRIPTIONS=1`. A crash empties Subgen's queue, so click CC again afterwards. |
| "Skipping …: Subtitles already exist" | An English `.srt` is already there. Delete it, then try again. |
| `HF_TOKEN` warning in Subgen's log | Harmless. It is only about the one-time model download. |
| Subgen not listed under Subtitle downloaders | Restart Jellyfin after installing. Check **Dashboard → Plugins** shows Subgen as Active. |
| Plugin shows "Not supported" after a Jellyfin update | A new plugin build for that Jellyfin version is needed (see below). |

---

## Building from source

Needs the .NET 10 SDK (or Docker).

```bash
dotnet publish Jellyfin.Plugin.Subgen/Jellyfin.Plugin.Subgen.csproj -c Release -o out
```

Or with Docker only:

```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet publish Jellyfin.Plugin.Subgen/Jellyfin.Plugin.Subgen.csproj -c Release -o out
```

Copy `out/Jellyfin.Plugin.Subgen.dll` into a folder `Subgen` inside Jellyfin's `plugins`
folder and restart Jellyfin.

**New Jellyfin version:** change the `Jellyfin.Controller` and `Jellyfin.Model` versions in
`Jellyfin.Plugin.Subgen.csproj`, commit, and push a new tag (for example `v1.0.1`). The
**Release** workflow builds the zip, makes a GitHub release and adds the version to
`manifest.json`, so every Jellyfin that uses the catalog URL is offered the update.

---

## How it works (short)

- **Subtitle search:** the plugin is a normal Jellyfin `ISubtitleProvider`. Picking its
  result runs Jellyfin's FFmpeg to extract 16 kHz mono audio and posts it to Subgen's
  `/asr` endpoint, which returns an SRT.
- **CC button:** the plugin adds a small script to the web page as Jellyfin serves it
  (nothing on disk is changed). The button calls the plugin's API
  (`POST /Subgen/Generate/{itemId}`, administrators only), which sends each video path
  to Subgen's `/batch` endpoint.

---

## Credits and licence

- **[Subgen](https://github.com/McCloudS/subgen)** by McCloudS (MIT) does the real work.
  The Subgen icon in `assets/` comes from that project.
- **[Jellyfin](https://jellyfin.org)** (GPL-2.0-or-later).
- This plugin: **GPL-3.0**, see [LICENSE](LICENSE).
