# Display, speed and configuration

Findings from driving the engine directly. The config file is
`DATA/ot2.cfg`, read and rewritten by the game **relative to the working
directory** — so launch with the working directory set to the install folder.

As shipped, `DATA/ot2.cfg` is encrypted (see `ENCRYPTION.md`), but the game
writes it back in **plain text** after the first run and reads plain text
happily. So: run once, then edit.

> The game rewrites this file on exit. Close the game before editing, or your
> changes are overwritten.

## Resolution

### Both windowed and fullscreen accept non-default resolutions

| Setting | Result |
|---|---|
| `vid_fullscreen 0` + 1920×1080 | accepted and kept |
| `vid_fullscreen 0` + 1440×1080 | accepted and kept |
| `vid_fullscreen 1` + 1920×1080 | accepted and kept — ran a scenario cleanly |
| `vid_fullscreen 1` + 1920×1080 (first attempt) | fell back to 1280×1024 |

That last row was measured before the others and led to an earlier claim
here that fullscreen validates against an enumerated mode list. **That was
wrong.** The same setting worked later, so the fallback was a symptom of a
startup failure in that particular run — which also logged a `WndProc`
exception and never reached a scenario — rather than the resolution being
refused.

If you see the resolution silently revert to 1280×1024, read it as "startup
failed", check `log.txt`, and try windowed. Windowed is the default here only
because it has never failed, not because fullscreen is unreliable.

### The engine has no aspect-ratio correction

There is no `aspect` string anywhere in `core.dll` or `renderer.dll`, and the
only FOV references are the camera state definitions in `camera.cfg`. The
projection is built from a fixed field of view on the assumption of a 4:3
backbuffer.

Give it a 16:9 backbuffer and the image is **stretched horizontally by
1.333×** — not letterboxed, not scaled. There is no setting that changes
this; the code never considered it.

Note that the 1280×1024 the game picks for itself is **5:4**, so it has
always been stretching slightly (1.067×). There is no "original" widescreen
look to return to.

**Therefore: pick a 4:3 resolution.** On a 1920×1080 panel:

| Resolution | Notes |
|---|---|
| **1440×1080** | 4:3, full screen height, ~2× the pixels of the engine default |
| 1400×1050 | 4:3, slightly smaller |
| 1280×960 | 4:3, safe fallback |
| 1024×768 | 4:3, the engine's own default |

True widescreen would mean patching the projection matrix in `renderer.dll`
so the aspect argument is taken from the actual backbuffer — widening the
view rather than stretching it. That is a binary patch, and it would stop
`game/` verifying clean against the disc.

### `vid_modes` crashes

```
(0) Exception: .\CmdVarFunctions.cpp(107) - CCmdVarFunc_Vid_Modes::Process
```

A leftover developer command that prints to a console which does not exist.
The exception is caught and the game continues, but it will not report the
mode list. Do not put it in a config file.

## Frame rate and game speed are independent

Raising or smoothing the frame rate **cannot** change how fast the game runs.
The simulation advances on a logical clock (`m_pLogicalClock->AddDailyTask`),
not per rendered frame.

| Simulation | Rendering |
|---|---|
| `time_speed`, `time_pause` | `drawfps` — on-screen counter |
| `timemultiplier` — 1.0 / 3.0 / 8.0 | internal `FPS = %4d` |
| `dayspeed` (default 0.02) | internal `Present = %4d milisec` |

The three speed buttons in the UI (`main_interface_time_1/2/3`) simply set
`timemultiplier` to 1.0, 3.0 and 8.0.

There is **no vsync or present-interval setting**, so the frame rate is not
directly configurable. If pacing is poor rather than throughput, the lever is
a D3D9 translation layer (DXVK's `d3d9.dll` dropped next to `game.exe`) or
driver-level frame limiting — not game settings.

## Performance: reflections cost you two thirds of your frame rate

Stock settings in a round give roughly **11 fps**, on any hardware. Turning
reflections off gives **~30 fps**. This is the single most important thing to
know about running this game.

### What the profiler showed

`F1` alongside the DXVK HUD, in the same scene:

| | stock | reflections + shadows off |
|---|---|---|
| Frame Time | 90–106 ms | **33 ms** |
| Logic Time | 0–1 ms | 0 ms |
| Render Time | 90–106 ms | 33 ms |
| Present | 0 ms | 0 ms |
| `Time 0` | 48–58 ms | 6 ms |
| **Queue syncs / frame** | **41** | **0** |
| Render passes | 53 | 4 |
| Draw calls | 3,579 | 3,278 |
| GPU load | 9% | 4% |

The giveaway is **41 queue syncs a frame**. A queue sync is the CPU stopping
dead until the GPU drains. The engine renders the scene again for each
reflective surface and waits on each result, so the two processors take turns
instead of working together — which is why both sit idle while 90 ms passes,
and why `Present` is 0: nothing is waiting on the display, it is waiting on
itself.

Isolated by elimination:

| Setting | Effect |
|---|---|
| `reflections 1` | **all 41 syncs**, ~50 ms. Confirmed: shadows off with reflections on still produced 41 syncs and 93 ms |
| `shadows 1` | ~26 ms, no syncs |
| trees, water, particles, cars | ordinary render cost, no stalls |

So `reflections 0` is the fix. `shadows 0` is a large bonus. Everything else
can stay on.

Use `mods/display-1440x1080-fast`.

### DXVK does not fix this

DXVK v3.1.1 loads correctly and changes nothing: still 41 syncs, still ~11
fps. Neither does `d3d9.cachedDynamicBuffers` nor `d3d9.apitraceMode`, which
target lock-induced stalls — these syncs are not from buffer locking, they
are the engine explicitly waiting on its own reflection passes. No translation
layer can remove a wait the game asks for.

DXVK is still worth keeping for its HUD, which is what made the syncs
visible. It is optional: `Ot2Mod revert dxvk`.

### Measurement caveat

The camera sits wherever the previous session left it, so each launch renders
a different view and the numbers move between runs. Differences of a few
milliseconds mean nothing here. The 90 ms → 33 ms and 41 → 0 results are far
outside that noise; finer comparisons in these tables are not.

## Controls

The keys are compiled into `core.dll` — DirectInput, no binding table in any
data file, and no `bind` among the command variables. They cannot be changed
from configuration.

| Key | Action |
|---|---|
| Arrow keys | Move the map |
| `Esc` | Open the menu |
| `F1` | Built-in developer overlay (see below) |

`tools/Keybinds.exe` remaps keys from outside the game, defaulting to WASD
over the arrows. `Esc` needs no remapping.

## The F1 developer overlay

The engine ships with its own profiler, left in the retail build. `F1`
toggles it, and it is the right instrument for diagnosing stutter — average
frame rate alone will not tell you whether pacing is the problem.

| Line | Meaning |
|---|---|
| `FPS` | Frames per second |
| `Frame Time` | Total milliseconds for the frame — the number that matters for smoothness |
| `Logic Time` | Simulation cost. Should stay low and roughly flat |
| `Render Time` | Scene rendering |
| `Render Game Time` / `Render Window System` | Split of world versus UI |
| `Present` | Time blocked in D3D `Present` — a high value here means waiting on the display, i.e. vsync, not a slow game |
| `Time 0` … `Time 6` | Internal subsystem timers |
| `Allocted Memory` | Allocation in KB (sic) |
| `Number of ID Objects` | Live object count |

Reading it:

- **`Frame Time` steady, `Present` large** — you are vsync-bound. Normal, and
  as smooth as the engine will get.
- **`Frame Time` spiky while `Logic Time` stays flat** — a rendering or
  driver pacing problem, not the simulation. This is the case where a D3D9
  translation layer such as DXVK helps.
- **`Logic Time` spiking** — the simulation itself is stalling. Reducing
  `citydistance` or object counts would help; frame settings will not.

Because the simulation runs on a logical clock, none of this changes game
speed — see above.

## Command variables

Recovered from the name table in `core.dll` (UTF-16). Defaults where the
binary carries one.

### Video
`vid_width 800` · `vid_height 600` · `vid_colorbits 32` · `vid_fullscreen 1` ·
`vid_mode` · `vid_modes` *(crashes)* · `vid_restart`

### Quality
`shadows 1` · `water 2` · `reflections 1` · `particles 1` · `texturedetail 2` ·
`terrain_lod` · `mipmaplodbias -3` · `modelslodbias 0` · `modelslodfactor 25` ·
`citydistance 350` · `cityshadowdistance 200` · `cityreflectiondistance 200`

### Trees and scenery
`drawtrees 1` · `treeloddist 50` · `treemaxdist 150` · `treepatchmaxdist 150` ·
`treeblendstart 100` · `treesoffheight 400` · `drawcars 1` · `drawwaves 1` ·
`drawhomeless`

### Interface
`drawgui 1` · `drawlogo 2` · `drawfps` · `drawvidmem 0` · `select_language` ·
`citiesfont_name [name]` and the `citiesfont_*` family

### Time
`time_speed 1` · `time_pause` · `timemultiplier` · `dayspeed 0.02` ·
`setdate [yyyy [mm [dd]]]` · `day` · `startday` · `startnight`

### Sound
`snd_mute 0` · `snd_volume [channel] [0..1]` — channels `master`, `music`,
`ambient`, `objects`, `interface` · `snd_maxdistance` · `snd_mindistance` ·
`snd_rolloff 10.0` · `snd_ambient` · `snd_interface`

### Camera
`camerastate [index] [zoom] [fov] [pitch] [width] [height] [movespeed] [rotationspeed] [flags]` ·
`camera_focus_zoom` · `in_setcamerastate`

### Lighting
`globallightdir [x] [y] [z]` · `globallightcolor [r] [g] [b] [a]` ·
`globallightambient [r] [g] [b] [a]` · `globallightattenuation [c] [x] [x^2]` ·
`globallighttype [DIRECTIONAL|POINT]`

### Debug and cheats
`godmode` · `infinitemoney` · `fastbuilding` · `megacheat` · `iamnemo` ·
`icansee` · `noai` · `noevents` · `noextender` · `activeplayer [nr]` ·
`computerplayer [nr]` · `overtake [interceptor] [looser]` ·
`setspawn` · `burn [objectID]` · `dumpmemory <filename>` · `mem_check` ·
`reloadfxs` · `echo` · `quit` · `exec <file.cfg>`

`exec` runs another `.cfg`, so tweaks can live in a separate file.

## Applying these settings

Rather than editing the install, use the mod in `mods/display-1440x1080`,
which carries exactly this configuration:

```powershell
tools\bin\Ot2Mod.exe apply display-1440x1080
```

It backs up the original first, so `revert` returns `game/` to a state that
verifies 3337/3337 against the disc. See [`../mods/README.md`](../mods/README.md).

The settings themselves:

```
vid_fullscreen 0
vid_width 1440
vid_height 1080
vid_colorbits 32
drawfps 1
```

Close the game before applying — it rewrites `DATA/ot2.cfg` on exit.
