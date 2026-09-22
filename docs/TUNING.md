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

## Performance: one CPU loop in Renderer.dll

The game runs at roughly **10 fps** in a developed round, on any hardware,
with both CPU and GPU apparently idle. A sampling profile settles why.

### What the profiler found

`tools/Sampler.exe` stops the busiest thread ~1,000 times and attributes each
instruction pointer to a module. Two runs, 958 and 959 samples, no failures:

| Module | Share |
|---|---|
| **`Renderer.dll`** | **83–85%** |
| `d3d9.dll` | 6–8% |
| `AMDXN32.DLL` (driver) | 4–5% |
| `ntdll` / `win32u` | 3% |
| `Core.DLL` | **0.7–1.7%** |

Within `Renderer.dll` the time is extraordinarily concentrated — 97% of it
falls in two regions:

| RVA | Share | What the code is |
|---|---|---|
| `0x0BBAC0` | 46.6% | SSE 4×4 matrix × vector transform — `movaps` matrix rows, `shufps` broadcast, `mulps`/`addps` accumulate |
| `0x030B80`–`0x030CC0` | 50.5% | **x87** scalar vector add — `fld`/`fadd`/`fstp` per component |

An earlier version of this document called the x87 half the problem, on the
grounds that modern CPUs run it poorly. **That was wrong.** `fld`/`fadd`/
`fstp` are perhaps 1.5–2× slower than scalar SSE from stack management, not
the order of magnitude implied. The instruction set is not the story.

### What the loop is actually doing

The effect files answer it. `building.fx` and its siblings set
**fixed-function** state:

```hlsl
float4x4 g_matWorld : WORLD;
technique lod_0 { pass p0 { WorldTransform[0] = <g_matWorld>; ... } }
```

`WorldTransform[0]` hands the matrix to the GPU, so **the GPU already does
the vertex transform** — and only 3 of 34 effects declare a vertex shader.
The CPU loop is therefore not transforming vertices. It is doing *per-object*
work: culling and matrix setup.

And the arithmetic fits:

```
14,077 objects × 53 render passes ≈ 746,000 operations per frame
```

At roughly 75 ns apiece that is ~56 ms — the measured frame time.

**The engine re-culls and re-transforms every object for every render pass**,
rather than culling once per frame and reusing the result. The cost is a
product, not a sum:

| Settings | objects × passes | Result |
|---|---|---|
| everything on | 14,077 × 53 ≈ 746k | ~10 fps |
| `mods/minimum` | ~8k × 4 ≈ 32k | ~50 fps |

That is the whole explanation. It is also why no single setting ever helped
much: halving the passes halves the product, halving the objects halves the
product, and only doing both is dramatic. `citydistance` mattered most
because it is the one setting that reduces the object count directly.

The real repair would be to cull once per frame and share the result across
passes — an algorithmic fix inside `Renderer.dll`, not an instruction-level
one. No translation layer helps here: the problem is O(objects × passes)
where it should be O(objects + passes).

### What this rules out

Each of these was suspected during testing and each is disproved by the
numbers above:

- **The GPU.** 4–10% load. It is starved, not saturated.
- **The driver and API.** D3D9 plus the AMD driver total ~11%.
- **Game logic.** `Core.DLL` is under 2%, and `Logic Time` reads 0 ms.
- **Software vertex processing.** That runs inside the D3D9 runtime, which
  would put the time in `d3d9.dll`. It is not there, and `Renderer.dll`
  contains shader machinery rather than a software fallback.
- **DXVK.** Measured directly: it loads correctly and changes nothing. With
  only ~11% of time in D3D9, there was nothing for a translation layer to
  win. `d3d9.cachedDynamicBuffers` and `apitraceMode` made no difference
  either.

### Why no single setting fixes it

Every graphics setting feeds the same loop, so each removes only a slice:

| Change | Gain |
|---|---|
| `water 0` | ~5 fps |
| `reflections 0` + `shadows 0` | ~5 fps |
| `drawtrees 0` | negligible |
| `citydistance` 135 → 60 | ~10 fps |
| **all of the above together** | **10 → 50 fps** |

`Renderer.dll` exposes `CWaterImp::BeginRenderReflection` *and*
`BeginRenderRefraction`, plus `CO2_TerrainMgr::RenderReflection` and
`RenderRefraction`. Water and reflections drive **separate full scene
passes**, and each pass re-runs that transform loop — the DXVK HUD counts 53
render passes at full settings against 4 with everything off. The cost is
distributed across passes and objects, not concentrated in one feature.

So the practical lever is total work. `mods/balanced` spends the budget
deliberately: keep trees, cars and particles, drop the extra passes, pull
`citydistance` in.

### CPU affinity

Processor affinity applies to a live process, so this is the one comparison
here that could be made **without relaunching** — same session, same camera,
same scene. `tools/ccd-test.ps1` cycles the settings and stacks the readings
into one image.

On a Ryzen 9 9950X3D (dual CCD, V-Cache on one die only), identical scene of
14,077 objects:

| Affinity | FPS | Frame Time |
|---|---|---|
| all cores, as Windows scheduled | 17 | 59 ms |
| **CCD0 — the V-Cache die** | **18** | **56 ms** |
| CCD1 — the high-clock die | 17 | 61 ms |

CCD0 is best: about 9% ahead of CCD1 and 5% ahead of unpinned. Free, and it
applies to a running game:

```powershell
tools\boost.ps1 -Ccd 0 -Priority High
```

Worth knowing what this is *not*: a 5% gain against a frame that is four to
five times too long. The x87 loop is still the wall. It is also mildly
surprising — the hot code is only ~400 bytes, so a cache-resident loop was
expected to favour the higher-clocking die. That it does not suggests the
loop streams through enough vertex data for cache to matter.

### Older, wrong conclusions

Earlier versions of this document blamed reflections, then shadows, then
trees, and recommended DXVK. All were wrong. They came from comparing runs
where the camera had moved between launches, and from reading queue-sync
counts that the profiler overlay was itself producing. The sampling profile
above replaced four hours of that guesswork in fifteen seconds.

## Notes on measuring this game

The game runs in the teens in a round — around **11 fps** stock — with both
CPU and GPU idle. Idle hardware and a 90 ms frame means it is blocked, not
busy.

**Read the rest of this section with care.** The measurements below are
indicative, not rigorous, and two confounds turned up late:

1. **The camera sits wherever the last session left it**, so no two launches
   render the same scene. The same settings produced 33 ms in one run and
   66 ms in another. Differences smaller than about 2× are not trustworthy.
2. **The profiler overlay is itself active in every measurement**, because
   `drawfps 1` enables it, and it contributes roughly 31 of the queue syncs
   on its own.

What survives both caveats: turning scene features off produced 20 ms against
90 ms in the same session, which is far outside the noise. `reflections` and
`shadows` were the two that mattered. The precise split between them is not
established.

To judge it for yourself, park the camera somewhere recognisable and swap
between `display-1440x1080` and `display-1440x1080-fast` without moving it.
That is the controlled comparison none of the runs below actually were.

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

**No F-key is bound.** An earlier version of this document claimed `F1`
toggles the developer overlay. It does not — nothing happens. The overlay is
enabled by the `drawfps` command variable, not by a key.

`tools/Keybinds.exe` remaps keys from outside the game, defaulting to WASD
over the arrows. `Esc` needs no remapping.

## The developer overlay

The engine ships with its own profiler, left in the retail build. It is
enabled by `drawfps 1` in the config — there is no key for it.

**It is not free.** With it on, the frame carries roughly 31 CPU/GPU
synchronisation points that disappear when it is off, measured with the same
settings in the same session. It barely moves the frame rate (15.25 vs 15.2
fps), but it means every measurement below was taken with the overlay
running, so treat the sync counts as including its own contribution.

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
