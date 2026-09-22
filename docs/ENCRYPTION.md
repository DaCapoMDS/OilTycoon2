# The `encr` container — solved

1,377 of the game's data files ship encrypted. The scheme is fully broken and
the transform is symmetric, so the same routine decrypts and re-encrypts.

## Format

```
offset 0   'e' 'n' 'c' 'r'          4-byte magic
offset 4   payload                  plain[i] = payload[i] ^ KEY[i % 64]
```

No length field, no padding, no per-file salt: encrypted size is exactly
plaintext size + 4. `DATA/Scenario/.../4_s09_endgame.lua` is 4 bytes — an
empty file, header only — which is what first revealed there is no trailer.

## Key

A single **64-byte key, repeated**, stored verbatim in `core.dll` at file
offset `0x270A98`:

```
34 F4 21 AC D5 BF 99 A3 DE 32 21 AC 91 EE 89 AB
84 28 C1 11 D5 BF 55 AE 3B 58 CF AC 78 69 DD 23
C8 F4 21 55 D5 2F CC E7 9B 5B CA AC 23 74 BB F2
2F 3D ED 77 D5 BF AA F1 11 39 5D 33 65 B6 CD EF
```

The key is global — the same for every file and every file type.

## How it was recovered

1. `ot2.cfg` and `camera.cfg` share their first three cipher bytes; both
   plaintexts begin `// `. So the keystream depends on **position only**, not
   on file content or name — i.e. a plain XOR stream.
2. Deriving `k = 34 F4 21 AC` from `ot2.cfg`'s known `// C` prefix correctly
   predicted `MainMenu.xml` decrypting to `<?xm`, confirming the stream is
   shared across files.
3. With ~600 encrypted text files sharing one keystream, the remaining bytes
   fell out statistically: at each position, the correct key byte is the one
   that makes the most files decrypt to plausible text.
4. Searching `core.dll` for the recovered prefix found the table at
   `0x270A98`; testing repeat lengths showed 64 decrypts a 348,900-byte XML
   end to end.

## Verification

Independent of the method: **330 of 331 decrypted XML files parse as
well-formed XML**.

The single failure, `DATA/Scenario/default_variable/Economy.xml`, is a
**genuine authoring bug in the shipped game data**, not a decryption error —
line 7 reads `<Corporation ID="2"Type="1" ...` with no space between the
attributes, while lines 5 and 9 are correctly spaced.

## What is and isn't encrypted

| Encrypted (1,377) | Plain (1,954) |
|---|---|
| `.lua` 207 — game and AI logic | `.dds` 1,644 — textures |
| `.xml` 331 — UI, scenarios, economy | `.tga` 186 — textures |
| `.o2m` 641 — models | `.ogg` 17 — music |
| `.wav` 125 — audio | `.bmp` 19 |
| `.fx` 34 — HLSL shaders | `.db` 16 |
| `.particle` 34, `.cfg` 3, `.lng` 1 | `.scc`, `.ifl`, `.dat`, `.map` |

## Tooling

`tools/Ot2Crypt.cs` (compile with the .NET Framework `csc`):

```powershell
Ot2Crypt.exe key     game\core.dll
Ot2Crypt.exe decrypt game\core.dll game\DATA  decrypted\DATA
Ot2Crypt.exe encrypt game\core.dll edited\DATA game\DATA
```

`decrypt` skips files without the magic; `encrypt` skips files that already
have it, so both are safe to re-run over a tree.

A full decrypted copy lives in `decrypted/` — 1,377 files, 146 MB. `game/`
is left untouched so it still verifies byte-for-byte against the disc.
