# YARG Demucs

Generates stems (vocals, bass, drums, and the rest) for [YARG](https://github.com/YARC-Official/YARG)
songs that only ship a full mix, using [Demucs](https://github.com/adefossez/demucs).
It runs on its own, so YARG doesn't have to be open and doesn't share the PC while it works.

YARG only reads the result: songs show **(no stems)** until they're separated and
**(custom stems)** after. Any tool that writes the same layout (below) works the same way.

## Requirements

- Windows, [.NET 8 runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- `ffmpeg` and `demucs` on PATH (`pip install demucs`; for a GPU, a CUDA build of PyTorch)
- For CON/LIVE files: [Nautilus](https://github.com/AARP41298/Nautilus) built from the
  `master-backstage` branch, which has the headless `-backstagehero` CLI (`dta`, `moggs`, `decrypt`)

## Build

```
dotnet build -c Release
```

The exe is `src/YargDemucs/bin/Release/net8.0/yarg-demucs.exe`.

## Use

```
yarg-demucs --nautilus "C:\path\to\Nautilus.exe" --list   # first run: remember Nautilus, list songs
yarg-demucs                                               # separate every song missing stems
yarg-demucs --limit 5 --priority idle                     # a few, only using spare CPU
yarg-demucs --match gorillaz                              # songs whose artist, title, or path match
yarg-demucs --check-only                                  # only refresh the "no stems" tags
```

It reads the song folders from YARG's `settings.json` (the profile used last in
`%USERPROFILE%\AppData\LocalLow\YARC\YARG`). `--folder`, `--profile`, and `--data` choose others.
Run `yarg-demucs --help` for every option.

Stems are written as Opus at 160 kbps by default, about a tenth of the size of WAV with no
audible difference. `--format wav|ogg|mp3` and `--bitrate` change that.

Ctrl+C stops after killing the running ffmpeg or Demucs. Finished songs are kept.

### What counts as missing stems

- Song folders and `.sng` files with a `song` audio file but no vocals, bass, or drums.
- Rock Band songs (CON or extracted) whose `songs.dta` lists no drums, bass, or vocals,
  or lists them on channels that are silent. The tool listens to one second at a few points
  to tell, once per song; results are cached in `%LOCALAPPDATA%\YARG Demucs\cache.json`.

## Stem layout (read by YARG)

Under each song folder added to YARG (the library root):

| Path | What |
|---|---|
| `demucs/.yarg-stems` | Marks the folder so YARG's song scan skips it |
| `demucs/<id>/song.*`, `vocals.*`, `bass.*`, `drums.*` | One song's stems, any format YARG plays |
| `demucs/<id>/.yarg-stems` | Says the stems are complete; YARG only uses folders that have it |
| `demucs/missing-stems.txt` | Rock Band short names whose stem channels are silent, one per line |

`<id>` is the Rock Band short name (the folder in `songs/<id>/` inside the CON) for Rock Band
songs, so every copy of a song shares one folder. For `.sng` files it is `sng_` plus the file's
path inside the root, with folder separators turned into `__`
(`Pack\Song.sng` → `sng_Pack__Song.sng`).

Unpacked song folders get their stems in place: the original `song.*` and stem files are renamed
to `*.pre-demucs.*` and `.yarg-stems` is written in the song folder.

`song` is Demucs's "other" stem: the backing that's left after vocals, bass, and drums.
