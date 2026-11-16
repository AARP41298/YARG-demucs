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

Double-click `build.bat` (or run it from a terminal; `build.bat debug` for a Debug build).
It needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), not Visual Studio.

The exe is `src/YargDemucs/bin/Release/net8.0/yarg-demucs.exe`.

## Use

```
yarg-demucs --nautilus "C:\path\to\Nautilus.exe" --list   # first run: remember Nautilus, list songs
yarg-demucs                                               # separate every song missing stems
yarg-demucs --limit 5 --priority idle                     # a few, only using spare CPU
yarg-demucs --match gorillaz                              # songs whose artist, title, or path match
yarg-demucs --check-only                                  # only refresh the "no stems" tags
```

It reads the song folders from YARG's `settings.json`. YARG keeps one profile per build
(`release`, `nightly`, `dev`) in `%USERPROFILE%\AppData\LocalLow\YARC\YARG`; when there are
several, the tool asks which one to use, and Enter keeps the last choice. `--profile dev` skips
the question, and `--folder` or `--data` use other folders. Run `yarg-demucs --help` for every option.

`--shifts n` makes Demucs separate each song n times, each shifted by a random fraction of a
second, and average the results. Stems come out slightly cleaner, and it takes n times as long.
The default is 1.

Stems are written as Opus at 160 kbps by default, about a tenth of the size of WAV with no
audible difference. `--format wav|ogg|mp3` and `--bitrate` change that.


### What counts as missing stems

- Song folders and `.sng` files with a `song` audio file but no vocals, bass, or drums.
- Rock Band songs (CON or extracted) whose `songs.dta` lists no drums, bass, or vocals,
  or lists them on channels that are silent. The tool listens to one second at four points
  to tell, once per song; results are cached in `%LOCALAPPDATA%\YARG Demucs\cache.json`.
- Songs from official Rock Band and Guitar Hero games and DLC (`game_origin` such as `rb1`,
  `rb3_dlc`, `beatles`, `lego`, `gh5`) ship with stems, so they aren't listened to unless you
  pass `--check-official`. Customs (`ugc_plus`) are always checked.

The first run reads every CON and listens to every custom Rock Band song, which can take a
while on a big library; a progress line shows how far along it is. Later runs only read new
or changed files. Press Ctrl+C once to stop after the current step, twice to quit at once.

The check reads each song's whole mogg (about 25 MB), so on a hard drive it is limited by how
fast the drive reads. Several small CON files are checked at once (`--jobs`, default 4), since
those spend most of their time starting Nautilus and ffmpeg. Big packs are read one at a time,
straight through, because several readers on one multi-GB file make a hard drive seek instead
of read.

## Shared database (`stemsdb/`)

Listening to every custom Rock Band song takes a while, and everyone has many of the same songs.
`stemsdb/` holds the songs already checked, one JSON file per `game_origin`, and the tool reads
it before listening: only songs nobody has checked yet are analyzed. It is copied next to the
exe when you build.

```json
{
  "source": "ugc_plus",
  "songs": [
    {
      "con_id": "highwaytohell",
      "artist": "AC/DC",
      "title": "Highway to Hell",
      "layout": "d0,1 b2,3 v6,7 s10,11",
      "stems": "silent",
      "checked_by": "AARP41298"
    }
  ]
}
```

| field | meaning |
|---|---|
| `con_id` | the song's short name in the CON (`songs/<con_id>/...`) |
| `layout` | mogg channels of drums (`d`), bass (`b`), vocals (`v`), and the song stem (`s`) |
| `stems` | `silent`: drums, bass, and vocals are silent, so the song needs stems; `present`: it has them |
| `checked_by` | who checked it |

A song matches when its source, `con_id`, and `layout` are all the same, so a custom and an
official copy of a song (or a re-release with different audio) don't share a result. Your own
results win over the database.

### Contributing

1. Run the tool as usual (`--contributor <name>` once, to be credited).
2. At the end it says how many songs it checked that the database doesn't have, and writes
   them to `%LOCALAPPDATA%\YARG Demucs\contributions.json`.
3. Attach that file to an issue or pull request.

To merge submissions (from inside the repository):

```
yarg-demucs --merge contributions.json [more.json ...]           # dry run: report only
yarg-demucs --merge contributions.json [more.json ...] --write   # save into stemsdb/
```

It prints `+` for new songs and `!` for conflicts (the database disagrees; kept as is, fix by
hand if the submission is right). Review `git diff stemsdb/` before committing.

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
