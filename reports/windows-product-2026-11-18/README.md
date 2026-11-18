# Windows Tor Manager candidate · 2026-11-18

## Redesigned preview

`TorManager-win-x64-0.1.0-dev.20261118.5.zip` is in the local
`build/windows-manager-redesign/` directory. It was published from
`a74df30e603a837f305145cdd2b5307a9e1f16e8` and reused the verified
Tor 0.4.9.14 bundle listed below.

| Check | Result |
| --- | --- |
| Archive bytes | 55,417,628 |
| Archive SHA-256 | `4a8a5b126dd2c7446d0e21c030078966c37dfc05b9d561cd8c1e8c798afd8481` |
| Locked restore and self-contained win-x64 publish | PASS |
| Extracted file hashes and native imports | PASS, 319 payload files and 20 native import inventories |
| Launch from extracted archive | PASS, development host with a fresh isolated offline profile |
| Visual checks | Redesigned Dashboard viewed. Final visual walkthrough and DPI matrix NOT RUN. |
| Release qualification | Clean-host and public tests of this archive NOT RUN. The [separate Developer runtime public test](../windows-persistent-host-2026-11-18/README.md) remains FAIL. |

## Previous candidate

The local self-contained Windows x64 candidate is
`TorManager-win-x64-0.1.0-dev.20261118.4.zip` in the ignored
`build/windows-product-release/` directory of the Windows product worktree.
The archive was assembled from source commit
`9b35250a6de0dd3bffcd7834ab20bb5bd4891f52`.

| Item | Result |
| --- | --- |
| ZIP size | 55,412,356 bytes |
| ZIP SHA-256 | `169bd39d6aa028988d6b9f3d4936e470df647ce3f6517fe4e683ff6a8994b844` |
| Bundled Tor | 0.4.9.14, 8,642,974 bytes, SHA-256 `3a65024937320fb53a76615273d9cdf7718d9cf90855081dab4b73ae52d2c7f9` |
| Bundled EXE and DLL combined | 8,716,282 bytes |
| Bundled DLL | `libwinpthread-1.dll`, 73,308 bytes, SHA-256 `94334eab25e1a4043a3efdcf88636e722f387fe62fcc57c2533876a019baa04c` |
| Locked win-x64 restore and publish | PASS from the existing hash-verified 28-archive feed with SDK 11.0.100 |
| Extraction | PASS. All 319 payload sizes and SHA-256 values matched the manifest. |
| Native PE imports | PASS for 20 executables and DLLs. Dependencies resolved inside the corresponding package directory, as Windows API sets, or in local System32. |
| Manager launch | PASS on the development host from a directory with spaces and a System32-only PATH. The process loaded the packaged hostfxr, hostpolicy and coreclr modules. |
| Bundled Tor offline lifecycle | PASS. Configuration and version checked, authenticated stop, unexpected exit, cleanup after owner death and fixture runtime rollback passed. |
| GUI | PASS for navigation to Peers, Developer and Build Manager, keyboard focus and a real read-only dependency scan. Closing with offline Tor active reaped both processes. |
| Repository regression | 178 PASS, 10 platform SKIPPED. Native pipeline 6 PASS. |
| Public Onion Service lifecycle, clean-host, physical two-PC, DPI matrix | NOT RUN |

The archive includes the current Peers and Developer features, the runtime
locks, reviewed native Tor configuration and licenses, and an exact file
inventory. Local test state was created outside the installation directory.
The versioned package and checksum are local artifacts.

## Native Developer build investigation

| GUI run | Observation |
| --- | --- |
| `run-j018ltyt` | FAIL, exit 2. Native GCC tried to create a temporary file in the Windows directory before compiling its first zlib object. |
| `run-nt2xx8nv` | FAIL, exit 2. The isolated temporary paths enabled GCC and the static zlib archive. The next tool, windres, could not start its preprocessor with the reduced environment. |
| `run-mm7s33j8` | PASS from the GUI with compiler exit 0. Pinned source and patch checks, optimized static compilation and PE package validation passed. |

The adapter gives each run a private scratch directory using MSYS and native
path forms. It sets `COMSPEC` to the system command processor for the native
resource compiler. Real GCC and windres fixtures passed with the corrected
environment. Original failed logs and partial outputs remain under their
individual run directories in `%LOCALAPPDATA%/TorManagerDeveloper/builds/`.

The new package is at
`%LOCALAPPDATA%/TorManagerDeveloper/builds/run-mm7s33j8/pipeline/dist/`.
The GUI smoke used a verified copy of this package and returned eight
offline PASS results, including version, configuration, authenticated stop
and restart. Public bootstrap and SOCKS remote DNS were SKIPPED.

| New native output | Bytes | SHA-256 |
| --- | ---: | --- |
| `tor.exe` | 8,643,965 | `bff29b2d69f7aee10aea577736c04b37661ca7965f052238dfa78214368cc3f3` |
| `libwinpthread-1.dll` | 64,574 | `a552d178319ab78b09c19f14fbc2125d7104a15ce54f8a9d41d06326d181b307` |
| EXE and DLL combined | 8,708,539 | — |

The GUI export `tor-0.4.9.14-optimized-static-run-mm7s33j8.zip` is under
`%LOCALAPPDATA%/TorManagerDeveloper/exports/`. All 21 files passed archive
inventory, CRC and payload verification. The ZIP is 3,836,459 bytes with
SHA-256 `b8936f7647db6aabad682404e847bcb1559dd3a87133b626de11beaf039265aa`.
The Manager distribution uses the previously reviewed bundled runtime.
The new custom runtime is available as the separate verified export.
The final Manager selected the latest successful build first when it
refreshed the three real build records.

See [the local install and package guide](../../desktop/packaging/WINDOWS-RELEASE.md),
[draft #31 for persistent Onion hosting](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/pull/31)
and [draft #32 for modes, packaging and Developer fixes](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/pull/32).
