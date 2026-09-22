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

### Windowed takes any resolution; fullscreen does not

| Setting | Result |
|---|---|
| `vid_fullscreen 1` + 1920×1080 | **rejected** — silently rewritten to 1280×1024 |
| `vid_fullscreen 0` + 1920×1080 | accepted and kept |
| `vid_fullscreen 0` + 1440×1080 | accepted and kept |

Fullscreen validates the request against an enumerated display-mode list and
falls back when it does not find a match. Windowed mode does no such check.
So any non-default resolution needs `vid_fullscreen 0`.

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

## Example

Correct proportions at high resolution:

```
vid_fullscreen 0
vid_width 1440
vid_height 1080
vid_colorbits 32
drawfps 1
```
