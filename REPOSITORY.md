# Baseline and project boundaries

Current baseline: **LumaPlayer 0.3 exp**, selected by the project owner.
The `main` tree contains only code and notices corresponding to that baseline;
past branches, builds and research results are historical, not current code.

The local 0.3 exp application is the build and runtime reference. Keep copies
of its runtime and model dependencies; source files here alone do not rebuild
those binary dependencies. The SHA-256 values in `BASELINE-HASHES.json` record
the binaries used when this snapshot was created.

The most recent 1080p experiments reported speeds below 1× on difficult scenes.
Do not release versions under 1×. GPU-direct remains experimental research and
is not integrated in the baseline. The 0.4 exp 2048 MiB test and the later
GPU-direct prototype are not the selected baseline.
