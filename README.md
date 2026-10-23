# DeletesDupes

A small Windows Explorer utility that finds byte-identical image files in a
selected folder tree and requests that redundant copies be moved to the Recycle
Bin. Written in C# for .NET Framework 4.8, with no third-party runtime dependencies.

The repository is named **DeletesDupes**; the executable is **DeleteDuplicates.exe**.

> This tool changes files. Keep an independent backup and try it on a disposable
> copy of a folder before using it on important images. It has no preview or
> confirmation dialog. The tests use simulated recycling; they do not establish
> end-to-end recovery through the Windows Recycle Bin. See [SECURITY.md](SECURITY.md).

## How it chooses duplicates

1. Filter supported image extensions, case-insensitively: `.jpg .jpeg .jpe .jfif
   .png .apng .bmp .dib .gif .tif .tiff .webp .heic .heif .avif .ico`.
2. Group by file length and compute streaming SHA-256 with 1 MiB buffers.
3. Compare bytes before accepting a duplicate group. A shared hash is never proof.
4. Keep the **deepest** file. Among equal depths, keep the **alphabetically last**
   relative path, ordered with `OrdinalIgnoreCase`, then `Ordinal` as a tie-breaker.

For example, `Photos\A\B\image.png` survives instead of `Photos\image.png`.
Timestamps never decide which copy to retain. Recognition is by extension,
not image decoding; identical bytes can still have different filesystem metadata.

## Safeguards and boundaries

- The original selected survivor must remain valid. A changed or inaccessible
  survivor skips the entire group; another copy is never promoted as a fallback.
- A read handle denying write/delete sharing protects the survivor. If it cannot
  be acquired, the group is skipped. Hard-linked survivors are conservatively
  skipped rather than releasing this protection to remove aliases.
- File identity, length, last-write time and attributes are checked again before
  processing. Byte comparison reads the protected survivor handle and checks the
  candidate's open-handle identity. A Shell pre-delete callback revalidates again.
- Recycling uses `IFileOperation`, `FOFX_RECYCLEONDELETE` and one item per operation.
  `PreDeleteItem` cancels if the Shell does not advertise
  `TSF_DELETE_RECYCLE_IF_POSSIBLE`, or if revalidation fails. Afterwards, success
  requires a successful item result, no abort and a returned Recycle Bin item.
  A failed postcondition cannot undo an operation already performed by the Shell.
- Only fully qualified paths on fixed local drives are accepted. Volume roots,
  the profile root, Windows/program/system-data locations, `$Recycle.Bin` and
  `System Volume Information` are excluded. UNC, relative and device paths are
  rejected. Existing reparse/offline directory ancestors are rejected; this can
  exclude otherwise local cloud-synced folders too.
- Nonlocal/cloud placeholders are skipped. No content is intentionally hydrated.
- One run per Windows session, enforced with a named mutex. The application never
  requests elevation. A writable log is required before scanning.

These checks reduce risk; they are not an atomic transaction or a filesystem
sandbox. Do not run against folders being modified by other programs. Shell
operations address paths, so a concurrent rename/reparse change can still occur
between validation and the operation. Do not use this as a security boundary.

## Build

Windows, .NET Framework 4.8 and a C# compiler supporting C# 7.3 are required.
From a Visual Studio Developer PowerShell with `csc.exe` available:

```powershell
.\Build.ps1
```

Alternatively, with MSBuild and the .NET Framework 4.8 targeting pack:

```powershell
msbuild .\DeleteDuplicates.csproj /p:Configuration=Release
```

Output: `bin\Release\DeleteDuplicates.exe`. No custom icon or prebuilt executable
is distributed in this source repository.

## Verify without deleting

```powershell
.\tests\run.ps1
```

The standalone test harness compiles the production sources and tests grouping,
block-boundary comparisons, path restrictions, Shell callback cancellation,
changed/replaced/locked survivors and hardlinks. Recycling is injected and
simulated: the tests never invoke Shell deletion, install the tool or change the
registry. Synthetic fixtures and logs are retained under ignored `bin\Tests`.
Hardlink fixtures require an NTFS/ReFS volume and permission to create hardlinks.

## Install and use

Build and verify first. Installation changes Explorer's context-menu registry
entries. Prefer installation for the current user, without elevation:

```powershell
.\Install.ps1 -PerUser
```

Machine-wide installation (`.\Install.ps1`) prompts for elevation. Once installed,
right-click empty space in a folder and choose **Delete duplicates**. Windows 11
places this static verb under **Show more options**. The application itself has
no console, dialogs or elevation request. It operates on the selected folder and
its eligible descendants immediately.

For a direct invocation on a disposable folder, use:

```powershell
& .\bin\Release\DeleteDuplicates.exe 'C:\DisposablePhotos'
```

Uninstall using the same scope: `Install.ps1 -PerUser -Uninstall`, or
`Install.ps1 -Uninstall` for machine-wide installation. Only the installed
executable is removed; unrelated files in the installation directory are retained.
`Install.reg` and `Uninstall.reg` cover registry entries only for the machine-wide
layout; the PowerShell installer also copies/removes the executable.

## Logs, recovery and limitations

Each run writes a local UTF-8 log named
`DeleteDuplicates_yyyyMMdd_HHmmss_fff_PID.log` to `%TEMP%`, falling back to
`%LOCALAPPDATA%\Temp`. Logs contain full paths, filenames, hashes and errors;
review/redact them before sharing. The application does not upload them.

Inspect the log after every run. `MOVED` records report the Shell destination;
use Windows Recycle Bin to restore files when available. Maintain a separate
backup: Recycle Bin policy, capacity and providers affect recovery. Fully local
files in a synced folder may propagate removal to cloud storage. File content
checks do not preserve per-copy metadata, alternate data streams or all attributes.

Extended paths work for scanning/hashing, but the Shell may refuse recycling a
long path. Such failures are logged. A run can leave extra copies because of
locks, changes or unsupported entries. There is no fallback to `File.Delete`.
This version has controlled synthetic checks, not a real recycle/restore
compatibility certification for each Windows version, drive or provider.

Exit codes: `0` completed (inspect individual errors); `1` rejected root/arguments;
`2` no writable log; `3` another run is active; `4` unhandled error.

## License

[MIT](LICENSE), copyright 2026 toshiotomikawa.
