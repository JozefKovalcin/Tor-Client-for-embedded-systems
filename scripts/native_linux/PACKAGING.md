# Native Linux runtime archive

Without `--out`, the command produces a read-only inventory for Ubuntu 24.04
x86_64 and glibc 2.39. Add `--out /home/operator/exports/tor-linux-x64.tar.gz`
to create and verify a new archive. The destination must be absent.

```sh
python3 -B -m scripts.native_linux.packaging \
  --build-root /home/operator/native-builds/completed-run \
  --expected-build-manifest-sha256 TRUSTED_PRODUCER_DIGEST
```

Supply the manifest digest from the verified build producer. The packager
reads the pinned executable and licenses, then checks the finished archive.
It invokes no executable from the build.

## Implemented inventory checks

- Bounded regular-file snapshots; no symlinks or redirected path components.
- Successful finished source manifest, unchanged deterministic input digest,
  unchanged W1 build-mode request, selected target/source pins/epoch, complete
  fifteen-command outcomes and exact original log hashes. Empty verified logs
  are valid evidence of silent commands; bools are not process exit integers.
- Exact expected `prefix/bin/tor` size/hash, then bounded ELF64 little-endian
  x86_64 metadata parsing. Validate program/dynamic/string bounds and duplicate
  critical properties. No malformed dependency path or arbitrary loader.
- PIE/interpreter, non-executable stack, structurally mapped RELRO, immediate
  binding, no text relocations (both DT_TEXTREL and DT_FLAGS/DF_TEXTREL),
  no embedded search paths and a conservative declared system-SONAME inventory.
  RELRO must map to a single aligned, nonoverlapping load segment with
  consistent memory bounds and file offsets. The policy rejects DT_AUDIT,
  DT_DEPAUDIT, DT_AUXILIARY and DT_FILTER.
  Other crypto/event/compression runtime dependencies are not silently promoted
  to OS dependencies: the executor plans static private dependency libraries.
- Original pinned source licenses checked again and referenced individually.

The allowlist is `bin/tor`, five license files and `runtime-inventory.json`.
The inventory lists the loader, system dependencies, source input digest and
payload hashes. The archive uses the source epoch and normalized file modes.
The source/cache/run tree is never recursively packaged. Private state,
identities, hidden-service directories, cookies, credentials, host config,
home/tmp and logs are excluded. GeoIP/data files require a later explicit
reviewed hashed allowlist; no wildcard copying is planned.

## Manager

On Ubuntu 24.04 x86_64, build the shared Avalonia project from the locked
dependencies. Open **Developer**, enter the clean checkout and local archive
cache, and select an existing private build workspace on a native path
without spaces. Create a fresh workspace with `install -d -m 700`.
Run preflight, approve the displayed input digest, then compile. **Builds**
shows results. **Run offline smoke** verifies and starts the selected client
with networking disabled, then terminates it. **Export verified runtime
archive** writes the archive into the workspace and reports its SHA-256.

A successful archive check leaves `readyForDistribution` false. Clean-host
runtime closure, public Tor and physical interoperability have separate
acceptance gates. Extract the checked archive as a regular user and run
`./bin/tor --version` on a compatible system. Keep Tor state and identities
in the user's private directory.

Format references: [ELF ABI program headers](https://gabi.xinuos.com/elf/07-pheader.html)
and [ELF dynamic linking](https://gabi.xinuos.com/elf/08-dynamic.html).
