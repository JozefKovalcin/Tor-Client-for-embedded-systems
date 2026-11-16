# Native Windows Tor client

For the two-PC test, run `TEST.cmd` on each PC and choose A or B. It starts Tor
and the local test server on 8081, uses SOCKS 9150, and stops its own processes
when you choose Q. Follow the short README shipped with the test bundle.
The manual commands below are for running Tor independently of that test.

Keep this entire folder together. Run it from a writable, private per-user
directory on Windows x64. No MSYS2, Python, WSL, QEMU or Tor Browser is needed
on the destination PC.

Double-click `start-tor.cmd`, or run it from Command Prompt. Wait for
`Bootstrapped 100%`. Configure applications for SOCKS5 at `127.0.0.1:9050`
with remote DNS. For example:

```bat
curl.exe --socks5-hostname 127.0.0.1:9050 https://check.torproject.org/api/ip
```

Stop with **Ctrl+C** and let Tor exit. Keep `data` for later starts.
The ControlPort is disabled by default.

For an onion endpoint, start an application on `127.0.0.1:8081`, then run
`start-tor.cmd onion`. Share only the public address in
`onion-service\hostname`. This profile uses separate `data-onion` state and
SOCKS `127.0.0.1:9150`. The peer connects to that `.onion:80` address through
its own local SOCKS proxy with remote DNS. Keep the service directory and keys
private and on this endpoint.

Useful commands from this folder:

```bat
tor.exe --version
tor.exe --list-modules
tor.exe --defaults-torrc config\defaults-torrc -f config\torrc --verify-config
```

This is the client-only Tor 0.4.9.14 source build: relay, bridge, directory
authority and directory-cache modules are disabled. V3 onion client and
service functionality is retained. A reviewed source patch queues CONNECTED
before processing buffered server data. See `PORTING.md`,
`source-manifest.json`, `package-report.json` and `licenses` for provenance.

The separate source project contains the build scripts, reviewed patch,
Windows test tools and executed validation evidence. Never redistribute live
`data`, `data-onion`, `onion-service`, cookies or private keys.
