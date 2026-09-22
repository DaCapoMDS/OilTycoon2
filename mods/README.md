# Mods

Every change to the game lives here. `game/` stays byte-identical to the
disc until a mod is applied, and reverting restores it exactly.

## Why a tool is needed

The engine has **no mod system** — no override folder, no load order, no
patch layer. Changing anything means overwriting files inside the install.

`Ot2Mod` makes that reversible: before touching a file it copies the original
into `mods/.backups/<mod>/`, and `revert` puts it back. Restoring from the
backup rather than from the disc matters, because `Extract.exe --repair` can
only restore the 681 files the archive stores uncompressed.

## Usage

```powershell
tools\bin\Ot2Mod.exe list
tools\bin\Ot2Mod.exe apply  display-1440x1080
tools\bin\Ot2Mod.exe status
tools\bin\Ot2Mod.exe revert display-1440x1080     # or: revert all
```

Confirm the install is untouched at any time:

```powershell
tools\bin\Extract.exe <disc>\irsetup.dat "<disc>\Big Oil Setup Release.exe" "" --verify game
```

## Writing a mod

A mod is a folder whose layout mirrors the install:

```
mods/
  my-mod/
    mod.txt                 first non-comment line is the description
    DATA/config.xml         written to game/DATA/config.xml
    DATA/Scenario/...
```

**Author in plain text.** Most of the game's data files are encrypted; if the
file being replaced carries the `encr` header, `Ot2Mod` encrypts your version
on apply. Use `decrypted/` as the reference copy to edit from.

Files that do not exist in the install are recorded as absent, so `revert`
deletes them instead of restoring.

## Included

| Mod | What it does |
|---|---|
| `display-1440x1080` | 4:3 at full screen height — corrects the horizontal stretch caused by the engine having no aspect-ratio handling |

## Two things to know

**`DATA/ot2.cfg` is rewritten by the game on exit.** A mod touching it seeds
the values; the game then owns the file. So its contents will drift from what
the mod folder says, and that is expected. Close the game before applying.

**Saves live inside the install**, at `game/DATA/SAVE/`. They are not part of
the archive, so verification ignores them and `revert` never touches them —
but a reinstall would remove them. Copy that folder elsewhere before
reinstalling.
