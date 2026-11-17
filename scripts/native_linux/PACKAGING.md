# W6 native Linux packaging inventory and design

This dependent PR does **not** create or distribute a package. It supplies a
read-only inventory for the selected Ubuntu 24.04 x86_64 / glibc 2.39 ABI and
an explicit plan for the later authorized packaging gate. No ELF binary was
compiled or executed for its tests; fixtures are structural byte arrays.

```sh
python3 -B -m scripts.native_linux.packaging \
  --build-root /home/operator/native-builds/completed-run \
  --expected-build-manifest-sha256 TRUSTED_PRODUCER_DIGEST
```

The caller must supply the manifest digest from a verified producer/ref. A
hash supplied alongside arbitrary untrusted data is not producer authentication.
There is no `--execute`, downloader, shell, ldd or binary invocation.

## Implemented inventory checks

- Bounded regular-file snapshots; no symlinks or redirected path components.
- Successful finished source manifest, unchanged deterministic input digest,
  unchanged W1 build-mode request, selected target/source pins/epoch, complete
  fifteen-command outcomes and exact original log hashes. Empty verified logs
  are valid evidence of silent commands; bools are not process exit integers.
- Exact expected `prefix/bin/tor` size/hash, then bounded ELF64 little-endian
  x86_64 metadata parsing. Validate program/dynamic/string bounds and duplicate
  critical properties. No malformed dependency path or arbitrary loader.
- PIE/interpreter, non-executable stack, RELRO, immediate binding, no text
  relocations (both DT_TEXTREL and DT_FLAGS/DF_TEXTREL), no embedded search
  paths and a conservative declared system-SONAME inventory.
  Other crypto/event/compression runtime dependencies are not silently promoted
  to OS dependencies: the executor plans static private dependency libraries.
- Original pinned source licenses checked again and referenced individually.

The proposed file allowlist is exactly `bin/tor` plus the five license files.
The source/cache/run tree is never recursively packaged. Private state,
identities, hidden-service directories, cookies, credentials, host config,
home/tmp and logs are excluded. GeoIP/data files require a later explicit
reviewed hashed allowlist; no wildcard copying is planned.

## Unexecuted distribution gate

Even when these metadata checks PASS, the result is:

```text
packagingExecutionStatus = NOT RUN
packageArtifact = null
readyForDistribution = false
```

ELF headers cannot prove stack protector/fortify coverage, successful loading,
GLIBC symbol compatibility or complete runtime closure. The next separately
authorized packaging implementation must:

1. Verify the selected 24.04/2.39 ABI and inspect GLIBC symbol requirements,
   final link map, fortify and stack-check symbols; reject unsupported closure.
2. Audit transitive/bundled notices, then verify all runtime dependencies on a supported clean host without executing
   untrusted `ldd` targets. Record loader/system package versions; self-contained
   application logic does not remove the OS libc/kernel dependency.
3. Snapshot/reverify exactly consumed files into a fresh private package tree,
   with explicit normalized modes/ownership and SOURCE_DATE_EPOCH. Preserve
   failures; never package a changing build prefix or previous runtime state.
4. Create and reinspect the final deterministic archive allowlist/digests,
   then emit a real W1 BuildArtifact with existing signature status accurately
   `not-verified` unless an authorized trust system actually verified it.
5. Execute the authorized offline smoke on the declared native host. Frozen
   public Tor assertions require a distinct later approval; neither metadata
   inspection nor WSL evidence substitutes for native runtime qualification.

Format references: [ELF ABI program headers](https://gabi.xinuos.com/elf/07-pheader.html)
and [ELF dynamic linking](https://gabi.xinuos.com/elf/08-dynamic.html).
