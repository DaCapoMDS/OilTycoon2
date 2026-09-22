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
| `animations-*` | **Generated**, not shipped — see below |

## Animation speed

The models animate slowly: a pumpjack cycle is 19 keyframes spread over about
4.7 seconds, roughly 4 keyframes a second, which reads as sluggish and steppy.

Timing lives in the plain-text header of each `.O2M`:

```
FrameAnimationKeys
{
    key 0 { StartTimeInAnimation 0   }
    key 1 { StartTimeInAnimation 259 }
    key 2 { StartTimeInAnimation 518 }
```

`FrameAnimations` only names the animation and gives `start_key`/`end_key` —
there is no duration field, so those timestamps alone set the speed.

`Ot2Anim` scales them:

```powershell
tools\bin\Ot2Anim.exe decrypted\DATA\models 0.5      # twice as fast
tools\bin\Ot2Anim.exe decrypted\DATA\models 0.25     # four times
tools\bin\Ot2Anim.exe decrypted\DATA\models 2.0      # half speed
tools\bin\Ot2Mod.exe  apply animations-2x
```

It rewrites each number **in place, space-padded to its original byte width**,
so the file length never changes and the binary mesh data that follows the
header stays exactly where it was. Verified: all 49 rewritten models match
their originals in length byte for byte.

Requires the decrypted data (launcher: *Decrypt data*).

**These mods are gitignored.** They contain rewritten copies of the game's
own models, which is game-derived content — so the tool is tracked and its
output is not. Regenerate it on any machine from your own install.

**What this does and does not fix.** It makes animations play faster. It does
not add keyframes, so if the engine does not interpolate between them, the
motion is still made of the same 19 steps — just shown twice as quickly. If
the whole game stutters rather than only the animations, that is frame
pacing; press `F1` for the profiler and read `docs/TUNING.md`.

## Two things to know

**`DATA/ot2.cfg` is rewritten by the game on exit.** A mod touching it seeds
the values; the game then owns the file. So its contents will drift from what
the mod folder says, and that is expected. Close the game before applying.

**Saves live inside the install**, at `game/DATA/SAVE/`. They are not part of
the archive, so verification ignores them and `revert` never touches them —
but a reinstall would remove them. Copy that folder elsewhere before
reinstalling.
