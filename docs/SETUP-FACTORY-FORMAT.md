# Big Oil / Oil Tycoon 2 — disc and installer format notes

Reverse-engineered from `Big-Oil_Win_EN_Disc-Image` so the game can be unpacked
without executing the 2006 installer.

## The disc

`Big Oil.bin` / `.cue` — single track, `MODE1/2352`, 135,951 sectors.
Convert to ISO by taking 2048 bytes at offset 16 of every 2352-byte sector
(`tools/bin2iso.ps1`). Result: 278,427,648 bytes, volume label `Big Oil`.

Disc contents:

| File | Size | Notes |
|---|---|---|
| `Big Oil Setup Release.exe` | 274,040,303 | Setup Factory 6.0 installer + archive |
| `irsetup.exe` | 1,224,704 | SUF60 runtime engine (not packed) |
| `irsetup.dat` | 2,901,916 | Installer script + file manifest |
| `IRIMG1/2/3.BMP` | — | Installer artwork |

## Installer container

`Big Oil Setup Release.exe` is a 4-section PE whose sections end at `0x11000`;
everything after that is overlay.

```
0x00000000  PE stub (69,632 bytes)
0x00011000  magic E0 E1 E2 E3 E4 E5 E6 E7
0x0001100D  irsetup.exe, XOR-0x07 obfuscated
0x0008431D  irsetup.dat, compressed (same codec as payload files)
0x001C7703  file payload: 3,337 blocks, back to back, no per-file headers
0x105585EF  EOF
```

The XOR is confirmed: `0x1100D` decoded with key `0x07` yields a valid
`MZ`/PE image matching the disc's `irsetup.exe`.

## Manifest records (`irsetup.dat`)

MFC-serialised `CSetupFileData` objects. Every record is length-prefixed
strings followed by fixed fields:

```
[u8 len] source path      "E:\MasterInstall\Big Oil\Master Full\<rel>"
[u8 len] file name        (must equal basename of source path)
[u8 len] source folder    (must equal dirname of source path)
[u8 len] extension
[u8 len] group            always "Archive"
00 01 00
[u32] uncompressed size
[u8]  file attributes     (0x20 = ARCHIVE)
[u32] ctime, [u32] mtime, [u32] atime    (time_t)
... variable fields, then destination folder e.g. "%AppFolder%\Manuals"
03 "All" 04 "None" 00 00 00
[u32] compressed size
[u32] CRC32 of the uncompressed file
00 04 80 01 00 00 00 00   <- 8-byte separator, next record follows
```

Because the separator is fixed, a record's compressed size and CRC sit at
`nextRecordLenBytePos - 16` and `-12`.

Parsed result: **3,337 files**, 399,389,223 bytes uncompressed,
272,174,828 bytes compressed. Payload therefore starts at
`274,040,303 - 272,174,828 = 0x1C7703`. This is independently confirmed: the
running offset of the last record lands exactly on `0x10421671`, where the
manual PDF's `%PDF-` signature sits.

Full inventory with sizes, offsets and CRC32s: `docs/archive-manifest.tsv`.

## Payload blocks

Blocks are concatenated in manifest order with no headers or padding.
681 files are **stored** (compressed size == uncompressed size); 680 of those
verify against their manifest CRC32 byte for byte. The one exception,
`DATA\Scenario\lennin\variable\vssver.scc`, is 64 bytes in and out but is
actually compressed — so "sizes equal" is a heuristic, not a stored flag.

The remaining 2,656 files are compressed. **Every one** begins with the
constant 2-byte header `00 06`; the bitstream follows.

## Codec (partially solved)

The bitstream is **LSB-first bit-packed LZSS**:

- flag bit `0` → literal: the next 8 bits, LSB-first, are the byte.
- flag bit `1` → match (encoding not yet recovered).

The literal path is confirmed against three independent files:

| File | First decoded bytes | Meaning |
|---|---|---|
| `game.exe` | `4D 5A 90 00 03 00 00 00 04` | PE/DOS header |
| `BO-icon.ico` | `00 00 01 00 03 00 30 30` | ICO, 3 images, 48×48 |
| `arrow blue.cur` | `00 00 02 00 01 00 20 20` | CUR, 1 image, 32×32 |

It is **not** zlib/deflate, bzip2, LZMA or PPMd: none of those decode the
stream, and `irsetup.exe` contains none of their tables, error strings or
signatures (it is statically linked and unpacked, so they would be visible).

### Known match samples

Bits listed are those following the match flag.

| Source | dist | len | bits after flag |
|---|---|---|---|
| `game.exe` @out 9 | 4 | 3 | `111111000001111111101111111111011111...` |
| `irsetup.dat` @out 29 | 25 | 2 | `101100000000000000010111110000000010...` |

`game.exe`'s values are certain (the MSVC DOS header is fixed). The
`irsetup.dat` sample is less certain — the embedded copy may differ slightly
from the disc copy, exactly as the embedded `irsetup.exe` diverges from the
disc copy after byte 278.

A search allowing any bit-width per match found a consistent decode of
`irsetup.dat` over 30 events, but the implied 3 bits total for a
(dist=25, len=2) match is information-theoretically impossible, which suggests
the grammar has state not yet modelled (a recent-distance cache, or tags wider
than one bit).

## Verification oracle

The manifest carries a CRC32 for all 3,337 files. Any extraction — by this
tooling, by the original installer, or by a third-party unpacker — can be
checked byte-for-byte against the original archive.
