# Windows developer integration boundaries

The Windows developer stack depends on the reviewed peer candidate in PR #19.
The peer messaging, ControlPort and Run Only workflows retain their existing
entry points. Developer actions are separate and explicitly initiated.

The scanner consumes W2 host and tool probes, W1 DependencyCheck validation and
the actual UCRT64 recipe. The GUI reads its structured output. Neither
discovery nor preflight starts COMPILE.cmd, installs packages or fetches
archives. The SDK and Git have separate roles from tools invoked by the recipe.

Installation plans describe a closed set of reviewed package files. The GUI
confirms a specific plan before an installer can run. The executor accepts
typed inputs and forces local signature verification. It serializes mutations
and asks a fresh scan to establish the result. A signed package from a local
cache is the supported automatic method. Wider MSYS2 or .NET setup has an
official manual path.

Native builds call the existing windows-native pipeline from an isolated
snapshot. The Windows source manifest, patches, compiler options and package
recipe retain their existing ownership. Missing source archives block the
managed workflow. Build measurement, qualification and activation remain
separate decisions. Developer operations use owned Windows jobs.

Artifacts have immutable versioned paths and a locally checked inventory.
Activation requires its own validation and approval. A selection change must
preserve the former verified runtime and leave live sessions untouched.
