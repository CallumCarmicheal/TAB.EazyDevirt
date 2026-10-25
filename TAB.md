# TAB EazyDevirt integration profile

**Optional**. The current TAB research has not established that gameplay methods
are protected with the Eazfuscator virtual machine. This fork stays available
for future virtualized builds but is **not run on every game update**.

The `TAB.core.slnf` solution filter builds only the EazyDevirt engine and its
Echo dependency graph, leaving the demo `EazSample` out of the default path.
The upstream full solution and entire research implementation are preserved.

```sh
git submodule update --init --recursive
dotnet build TAB.core.slnf -c Release
# Analyze the given assembly without emitting a second rewritten EXE:
dotnet run --project src/EazyDevirt/EazyDevirt.csproj -- \
  /path/to/game.exe --tab-analyze-only --preserve-all
```

`--tab-analyze-only` exercises the current devirtualization pipeline in memory,
but **does not write** a PE. When discovery fails it exits nonzero (including
when `--save-anyway` is also supplied). The CLI no longer waits for Enter on
completion and now uses `Path.Combine` to construct cross-platform output paths.

Devirtualization is not a substitute for TAB's single final PE writer, nor
an unconditional cleanup pass. Keep method discovery, opcode mapping, resource
parsing, `Echo` dataflow, and exception-handler reconstruction because they may
matter if virtualized methods appear in future versions.

GPLv3: see LICENSE. Preserve Echo's own license and submodule history.
