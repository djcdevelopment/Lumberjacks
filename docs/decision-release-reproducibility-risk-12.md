# Risk 12 — release reproducibility: what is true now, and the smallest decision left

2026-07-19. Written immediately after the release-gate artifact-boundary defect was
fixed and its regression suite went green. **This document decides nothing and changes
no code.** It records current behavior and proposes the smallest explicit decision, so
that the decision can be made deliberately rather than inherited.

## 1. What risk 12 currently says

From the trailing note in `C:\work\comfy\infra\gcp\p7\scripts\New-ReleaseCut.ps1`
(root cause found 2026-07-18, named in commit `909315b`):

- The .NET SDK's implicit source-control tasks embed the git HEAD sha as
  `SourceRevisionId`, which reaches the portable PDB; the PDB checksum rides in the
  DLL's debug directory.
- So a DLL's identity bytes change on **every commit**, even with unchanged source.
- Cuts are ordered build-then-commit, so the shipped DLL embeds the sha of the release
  commit's **parent**. No checkout of the release commit can ever rebuild its own
  artifact.
- `-p:EnableSourceControlManagerQueries=false` was proven to make a clone and this tree
  build byte-identical DLLs.
- Two candidate fixes were named, and neither chosen: pin the queries off (hash depends
  on source alone, loses embedded provenance), or reorder to commit-first-build-second
  (keeps provenance, but a rebuild needs the same origin URL).

## 2. What changed underneath it

The release-gate fix moved the authoritative artifact. Verification now reads
`Game.Gateway.dll` **out of the built Docker image**
(`Test-GatewayImageRelease.ps1`); the `bin/Release` read that risk 12 describes is
explicitly demoted to advisory.

That matters because the two builds do not see the same inputs.

## 3. The finding: the image build cannot see git at all

Provable from the files, not inferred from behavior:

- `C:\work\Lumberjacks\.dockerignore:13` excludes `.git/` from the build context.
- `C:\work\Lumberjacks\Dockerfile` copies only `Game.sln`, `Directory.Build.props`,
  `Directory.Packages.props`, the eight `.csproj` files, and `src/`. There is no
  `COPY . .`.

So inside the `sdk:9.0` build stage there is no git repository. `Microsoft.Build.Tasks.Git`
has nothing to query, `SourceRevisionId` stays empty, and no HEAD sha reaches the PDB or
the informational version.

**Consequence: the shipped Gateway DLL is already a function of source alone.** Risk 12,
as written, describes the *local* `dotnet build` path — which is exactly the path that
just stopped being authoritative.

> Deduced from the build inputs, not yet demonstrated. See the acceptance test in §4;
> it has not been run. Do not treat this as established until it has.

## 4. The smallest explicit decision

**Proposal: declare the Docker image the unit of reproducibility, and say so out loud.**

That is the whole decision. It needs no code change, because the image path already
behaves this way. What it needs is to stop being accidental:

1. State in the release documentation that a release is reproducible **as an image
   built from a given source tree**, not as a DLL rebuilt from a git checkout.
2. Keep build-then-commit ordering. Its defect — the parent-sha embedding — applies
   only to the advisory `bin/Release` artifact, which no longer gates anything.
3. Record image digests in the manifest as the reproducibility claim, and stop implying
   that `bin/Release` hashes attest to what shipped.

**Acceptance test, which must pass before this is adopted:** build the Gateway image at
two different HEADs with identical `src/` content and assert the extracted
`Game.Gateway.dll` is byte-identical.

```powershell
# at HEAD, then again after any commit that does not touch src/
docker build --target gateway -t lj-repro:a --build-arg LUMBERJACKS_EXPECTED_MOD_RELEASE=m9-repro-20260719-r1 --build-arg LUMBERJACKS_REQUIRE_RELEASE=1 .
# ... commit something outside src/ ...
docker build --target gateway -t lj-repro:b --build-arg LUMBERJACKS_EXPECTED_MOD_RELEASE=m9-repro-20260719-r1 --build-arg LUMBERJACKS_REQUIRE_RELEASE=1 .
# extract both /app/Game.Gateway.dll and compare sha256
```

If the hashes differ, this proposal is wrong and the two original options return.

**Optional, and deliberately separated:** pinning
`EnableSourceControlManagerQueries=false` in `Game.Gateway.csproj` would make local
`bin/Release` builds match container builds byte-for-byte, restoring meaning to the
advisory check. It is *not* required for the shipped artifact and should be decided on
its own merits, not folded into this.

## 5. Explicitly not decided here

- Whether to pin `EnableSourceControlManagerQueries` off (§4, optional).
- Whether to reorder cuts to commit-first-build-second.
- Anything about the mod's reproducibility. `ComfyNetworkSense` is built by
  `dotnet build` on the host with full git visibility, so risk 12 applies to it
  unchanged. This document covers the Gateway only.
- The existing recorded hashes. Per the script's note, they "attest what this machine
  built at this exact HEAD" and are not re-litigated here.

## Related

- Release-gate defect and its fix: `C:\work\comfy\fieldlab\evidence\p7-session-20260719-release-gate-defect\deployment-identifiers.md`
- Authoritative gate: `C:\work\comfy\infra\gcp\p7\scripts\Test-GatewayImageRelease.ps1`
- Gateway-only cut: `C:\work\comfy\infra\gcp\p7\scripts\New-GatewayReleaseCut.ps1`
