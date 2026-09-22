# Oil Tycoon 2 / Big Oil — revival toolkit

Tools and documentation for getting **Big Oil: Build an Oil Empire** (Tri
Synergy, 2006) — the English release of *Öl-Imperium 2 / Oil Tycoon 2* —
running again on Windows 10/11 from a retail CD, and for opening up its data
files.

**The game runs.** Confirmed on Windows 11 24H2: a couple of hours of
uninterrupted play, no compatibility shims, no patches to the executable.

## This repository contains no game content

Big Oil is commercial software and still under copyright. "Abandonware" is not
a legal status — the rights did not lapse because the game left shelves.

Shipped here: the toolchain, the file-format documentation, and a manifest of
filenames, sizes and CRC32 checksums. **You supply your own disc.** Nothing
here will produce a playable game without it.

No download links are provided, and none will be added.

## What you need

A retail *Big Oil* CD, or an image you made from one. Dump it with any disc
imaging tool to `.bin`/`.cue` (raw `MODE1/2352`) — `.iso` works too.

The game was never released digitally and its publisher, Tri Synergy, is
gone, so second-hand physical copies are realistically the only source. If
a storefront ever does list it, buy it there.

### Is my image a good dump?

The English release this project was built and tested against:

| | |
|---|---|
| File | `Big Oil.bin` + `Big Oil.cue` |
| Size | 319,756,752 bytes |
| Layout | single track, `MODE1/2352`, 135,951 sectors |
| SHA-256 | `BDC3A2FA4CBEC95289A6525D3A84BFAEDDAD0A3E17082D5D542662D651637310` |
| MD5 | `85822D4D147321234AB74F427F984F61` |

```
FILE "Big Oil.bin" BINARY
  TRACK 01 MODE1/2352
    INDEX 01 00:00:00
```

A different hash does not mean your disc is bad — regional releases and
re-presses differ. What actually matters is the per-file check: after
installing, the toolchain verifies all 3,337 files against CRC32 values held
in the installer's own manifest. **That is the real test of a good dump**, and
it works regardless of which pressing you have.

## Why you need this at all

The disc's installer, `Big Oil Setup Release.exe`, **crashes on modern
Windows** — an access violation (`0xC0000005`) in its bootstrap stub, before
any UI appears. Compatibility shims (WINXPSP3, WIN98, WIN95) do not help, and
it fails the same way from a local copy, so it is not a read-only-media issue.

The installer *engine* is fine. The stub's only job is to unpack that engine
and hand it the archive offset, so you can skip the stub and invoke the engine
yourself. That is what `setup.ps1` does.

## Usage

### The launcher

If you'd rather not touch a terminal:

```powershell
.\tools\build.ps1                          # one time
.\tools\bin\OilTycoon2Launcher.exe
```

Pick your disc image, choose where to install, press **Install and verify**.
The other buttons re-verify an existing install, decrypt the data files for
modding, and launch the game — the last one sets the working directory
correctly, which the game needs and which is easy to get wrong.

The launcher picks up the game's own icon from *your* installed copy at
runtime; no game artwork is bundled here.

### Modern keys (WASD)

The game's input is DirectInput with the keys compiled in — there is no
binding table in the data files and no `bind` command, so the keys cannot be
changed from config. `tools/Keybinds.exe` remaps them from outside instead:

```powershell
.\tools\bin\Keybinds.exe
```

It edits `tools/bin/keybinds.ini`, created on first run, which defaults to
WASD over the arrow keys. It is active **only while the game window has
focus**, every other application passes through untouched, and it translates
keys without recording anything. Close the console window to stop it.

Patching `core.dll` would be the alternative, but that stops `game/`
verifying clean against the disc.

### Or from the command line

```powershell
.\setup.ps1 -Bin "D:\images\Big Oil.bin" -Decrypt
```

That will:

1. convert the `MODE1/2352` disc image to ISO and mount it,
2. drive the installer engine directly — approve the UAC prompt, then install
   **wherever you like**. The wizard defaults to `C:\Tri Synergy\Big Oil`;
   the script reads the installer's own log afterwards to find where it
   actually went, so `-Target` is only a suggestion,
3. **verify all 3,337 files** against CRC32s read from the installer's own
   manifest,
4. repair any file that fails, where the archive stores it uncompressed,
5. optionally decrypt the 1,377 encrypted data files.

Requires Windows and .NET Framework 4 (`csc.exe`, present on every Windows
install). Nothing to download.

### Running the game

Launch it with the working directory set to the install folder, or it scatters
config and logs wherever it was started from:

```powershell
Start-Process -FilePath "game\game.exe" -WorkingDirectory "game"
```

## Verification

This is the part worth having. The installer's manifest carries a CRC32 for
every archived file, so an install can be checked byte-for-byte against the
original disc:

```
VERIFY ...\game
  match=3337  missing=0  wrong-size=0  wrong-crc=0  (of 3337)
```

This is not theoretical. On the first run here it caught two files the
installer had silently left corrupt — a half-written 536 KB sound file and a
truncated config — because an interrupted earlier attempt had left partial
files behind and the installer *skipped* them rather than overwriting. Without
the manifest, a truncated asset would have sat there unnoticed.

## Documentation

| Document | Contents |
|---|---|
| [`docs/SETUP-FACTORY-FORMAT.md`](docs/SETUP-FACTORY-FORMAT.md) | The Setup Factory container: overlay layout, XOR-0x07 engine obfuscation, MFC-serialised manifest, payload blocks |
| [`docs/ENCRYPTION.md`](docs/ENCRYPTION.md) | The `encr` data-file cipher, fully solved |
| [`docs/TUNING.md`](docs/TUNING.md) | Resolution and aspect ratio, game speed vs frame rate, and the full command-variable reference |
| [`docs/archive-manifest.tsv`](docs/archive-manifest.tsv) | All 3,337 files — relative path, size, compressed size, offset, CRC32 |

## The engine

`game.exe` plus `core.dll` / `renderer.dll` / `Feniks.Client.Video.Engine.dll`
/ `Oxygen.Network.dll`. MSVC 7.1, Direct3D 9, build stamp `Jan 9 2006`. No CD
check. Data is entirely loose on disk — no packed archives.

| Type | Count | Encrypted |
|---|---|---|
| `.dds` / `.tga` textures | 1,830 | no |
| `.o2m` models | 641 | yes |
| `.xml` data | 331 | yes |
| `.lua` scripts | 207 | yes |
| `.wav` / `.ogg` audio | 142 | `.wav` only |
| `.fx` / `.fxh` shaders | 34 | yes |
| `.particle` | 34 | yes |

### Data encryption — solved

1,377 files carry a 4-byte `encr` header followed by
`plain[i] = cipher[i] ^ KEY[i % 64]`, using a single repeating 64-byte key
stored in `core.dll`. The transform is symmetric, so the same tool decrypts
and re-encrypts:

```powershell
tools\bin\Ot2Crypt.exe decrypt game\core.dll game\DATA  decrypted\DATA
tools\bin\Ot2Crypt.exe encrypt game\core.dll edited\DATA game\DATA
```

Verification: **330 of 331** decrypted XML files parse as well-formed XML. The
one failure is a genuine 2006 authoring bug in the shipped data —
`Scenario/default_variable/Economy.xml` line 7 reads `ID="2"Type="1"` with no
space between attributes, while the surrounding entries are correct. That
stock-market corporation has presumably never loaded properly.

### Console variables

`core.dll` registers 110 command variables, settable from the config file,
including `GodMode`, `InfiniteMoney`, `MegaCheat`, `DumpLogic`, `ShowNetStats`
and an `Exec` that runs script files. The multiplayer stack
(`EstablishServer` / `ConnectToServer`) is still present.

## Known issues

- **No true widescreen.** The engine has no aspect-ratio correction at all, so
  a 16:9 backbuffer stretches the image by 1.333× rather than widening the
  view. Run a 4:3 resolution instead — `1440×1080` windowed gives correct
  proportions at roughly twice the pixels of the engine default. Fullscreen
  additionally refuses any resolution outside its enumerated mode list.
  See [`docs/TUNING.md`](docs/TUNING.md).
- `vid_modes` throws an exception (caught, non-fatal) — a developer command
  with no console to print to. Leave it out of config files.
- `log.txt` records `Exception: .\core.cpp(465) - CCoreImp::WndProc` at
  startup. Handled, not fatal; the game runs past it.
- The game reads and writes config and logs relative to the **working
  directory**, not the executable.

## Licence

[CC0 1.0](LICENSE) — public domain dedication. The tools and documentation
here are given away: use them for anything, no attribution required, no
permission to ask for.

Two things the dedication deliberately does **not** cover:

- **The game.** No game code, assets or data are included here. Big Oil /
  Oil Tycoon 2 remains the property of its rights holders.
- **The facts about its file formats** — field offsets, the obfuscation key,
  and the filenames, sizes and CRC32 values in `docs/archive-manifest.tsv`.
  Those are measurements of an existing work rather than original authorship,
  and no ownership of them is claimed.

This repository is not affiliated with, authorised by, or endorsed by Tri
Synergy, JoWooD, Greenwood Entertainment, or any other rights holder. All
trademarks belong to their respective owners. Everything here is for
interoperability, preservation, and personal backup of software you own.
