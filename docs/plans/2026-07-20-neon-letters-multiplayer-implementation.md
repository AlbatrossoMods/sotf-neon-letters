# Neon Letters Multiplayer Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make A-Z neon-letter construction and post-build color editing synchronize through SonsSdk for a modded host, current clients, and late-joining clients, while preserving Single Player behavior.

**Architecture:** Keep `CustomBlueprintManager` as the only construction path because SonsSdk already registers the crafting and built Bolt prefabs. Add small host-authoritative `Packets.NetEvent` adapters around pure protocol/state policies: clients request color changes, the host validates and broadcasts accepted state, and late joiners request a targeted snapshot. The host alone persists multiplayer world state and uses native `ScrewStructure` restoration before any Bolt fallback spawn.

**Tech Stack:** C# 10 / .NET 6, RedLoader 0.8.6, SonsSdk 0.8.6, Bolt, UdpKit, existing console contract tests, Unity 2022.2.16f1 asset gate.

---

## Goal

Support normal host-created multiplayer sessions where every participant has the
same mod version. Building and recoloring one letter must produce the same result
for the host, existing clients, and late joiners without regressing Single Player.

## Existing flow / patterns to preserve

- `NeonLetterSmallBlueprint.Register` remains the sole A-Z blueprint registration
  path; its recipe IDs, wall placement, colliders, book pages, and assets do not
  change.
- `NeonLetterColorRuntime` keeps the current three-metre `E`-key raycast and
  `MaterialPropertyBlock` emission path.
- `SOTFNeonLettersUi` keeps live local preview plus Apply, Cancel, and Reset.
- Pure policies remain separate from Unity/Bolt adapters and are linked into
  `tests/SOTFNeonLetters.ContractTests`.
- Network event registration follows the locally supplied `Signs` pattern, but no
  Signs code or assets are copied.

## What needs to happen in code

1. Encode a finite `NeonRgba` as deterministic RGBA32 and reject unsupported
   protocol versions or unknown recipe IDs.
2. Register SonsSdk packet handlers before a multiplayer connection starts.
3. Resolve the selected built letter to its Bolt `NetworkId` while keeping the
   existing Single Player target identity.
4. Route a client Apply only to the host; let the host validate, apply, remember,
   and broadcast accepted state.
5. Apply host state on clients without sending another packet and queue bounded
   state when the corresponding Bolt entity has not spawned yet.
6. Let a late joiner request a targeted snapshot after entering the world.
7. Let only the host save/load multiplayer letter state, prefer native restoration,
   and use `BoltNetwork.Instantiate` only for a missing untracked letter.
8. Run the full gate, deploy one clean commit, then verify Single Player and a
   two-peer multiplayer session with screenshots.

## Required changes

- Add pure protocol, host-state, pending-state, and host-save policies with
  behavior-focused contract tests.
- Add `Packets.NetEvent` request/state/snapshot adapters using `OnlyServer`,
  `AllClients`, and targeted `BoltConnection` responses.
- Make completed A-Z letters editable in multiplayer and route Apply according to
  the local Bolt role.
- Clear all multiplayer state on world exit and request late-join state once the
  client is in world.
- Add host-only persistence and deferred multiplayer restoration.
- Add the direct `udpkit` assembly reference needed by packet handlers.
- Bump the mod version and document the same-version requirement.

## Optional improvements

- None. Keep this iteration limited to required multiplayer behavior.

## Out of scope

- Compatibility with players who do not have the mod.
- A custom Bolt serializer or replacement construction transport.
- Ownership permissions, moderation, or anti-cheat controls.
- Medium and large letter variants, power-grid behavior, and animated effects.
- Claiming dedicated-server support as tested without a dedicated-server run.
- Refactoring existing color, placement, asset, or book code unrelated to the
  multiplayer path.

## Risks / unknowns

- Runtime acceptance needs a second modded peer; a single local game cannot prove
  host-to-client replication.
- SonsSdk 0.8.6 `EntityManager.OnUpdateLookup` contains an early return; actual
  host/client construction must verify that all A-Z prefab IDs are present.
- SonsSaveTools may load before Bolt role and entity startup are ready, so restore
  must remain deferred and idempotent.
- The existing test-save instruction forbids saving the game. A real host
  save/reload smoke test requires fresh explicit permission; automated persistence
  tests do not override that instruction.
- CrossOver/Wine failures must be diagnosed from RedLoader and Wine logs and must
  not be hidden by retry loops or fallback input hacks.

## Implementation tasks

### Task 1: Add the network protocol contract

**Files:**

- Create: `NeonLetterMultiplayerPolicy.cs`
- Modify: `tests/SOTFNeonLetters.ContractTests/Program.cs`
- Modify: `tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj`

**Step 1: Write the failing protocol tests**

Add `CheckMultiplayerProtocolContract();` beside the other color contracts and
cover observable encoding and validation:

```csharp
void CheckMultiplayerProtocolContract()
{
    var color = new NeonRgba(0.10f, 0.50f, 1f, 1f);
    uint packed = NeonLetterNetworkProtocol.Pack(color);
    CheckEqual(0xFFFF801Au, packed, "multiplayer color uses stable RGBA32 bytes");
    CheckEqual(
        new NeonRgba(26f / 255f, 128f / 255f, 1f, 1f),
        NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            packed),
        "all peers decode the same RGBA32 color");
    CheckThrows<InvalidOperationException>(
        () => NeonLetterNetworkProtocol.Pack(
            new NeonRgba(float.NaN, 0f, 0f, 1f)),
        "finite",
        "non-finite colors never enter a network packet");
    CheckThrows<InvalidOperationException>(
        () => NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion + 1,
            packed),
        "protocol",
        "unsupported multiplayer payloads are rejected");
}
```

Link `../../NeonLetterMultiplayerPolicy.cs` in the contract-test project.

**Step 2: Run the contract test and verify RED**

Run:

```bash
DOTNET_ROOT="$PWD/.tools/dotnet-6" \
DOTNET_CLI_HOME="$PWD/.tools/dotnet-cli" \
./.tools/dotnet-6/dotnet run \
  --project tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj
```

Expected: compilation fails because `NeonLetterNetworkProtocol` does not exist.

**Step 3: Implement the minimal pure protocol**

Create the new file with a version byte, finite-value validation, component
clamping, `MidpointRounding.AwayFromZero`, and this byte layout:

```csharp
public static class NeonLetterNetworkProtocol
{
    public const byte CurrentVersion = 1;

    public static uint Pack(NeonRgba color)
    {
        ValidateFinite(color);
        uint red = ToByte(color.Red);
        uint green = ToByte(color.Green);
        uint blue = ToByte(color.Blue);
        uint alpha = ToByte(color.Alpha);
        return red | green << 8 | blue << 16 | alpha << 24;
    }

    public static NeonRgba Unpack(byte version, uint packed)
    {
        if (version != CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported neon multiplayer protocol version {version}.");
        }

        return new NeonRgba(
            (packed & 0xFF) / 255f,
            ((packed >> 8) & 0xFF) / 255f,
            ((packed >> 16) & 0xFF) / 255f,
            ((packed >> 24) & 0xFF) / 255f);
    }
}
```

**Step 4: Run the contract test and verify GREEN**

Run the command from Step 2.

Expected: `All SOTFNeonLetters behavior contract tests passed.`

**Step 5: Commit**

```bash
git add NeonLetterMultiplayerPolicy.cs \
  tests/SOTFNeonLetters.ContractTests/Program.cs \
  tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj
git commit -m "test: define neon multiplayer protocol"
```

### Task 2: Add host-authoritative and pending-state policies

**Files:**

- Create: `NeonLetterMultiplayerState.cs`
- Modify: `tests/SOTFNeonLetters.ContractTests/Program.cs`
- Modify: `tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj`

**Step 1: Write failing state-flow tests**

Add tests using string identities and fake apply/broadcast delegates. They must
prove:

```csharp
var state = new NeonLetterAuthoritativeColors<string>();
var accepted = state.TryAccept(
    isHost: true,
    identity: "entity-a",
    isLive: true,
    recipeId: NeonLetterSmallCatalog.Get('A').RecipeId,
    color: new NeonRgba(1f, 0f, 0f, 1f));
CheckEqual(true, accepted.Accepted, "the host accepts a live A-Z color request");
CheckEqual(false, state.TryAccept(false, "entity-a", true,
    NeonLetterSmallCatalog.Get('A').RecipeId,
    NeonRgba.ProjectCyan).Accepted,
    "a client cannot commit authoritative color state");
CheckEqual(false, state.TryAccept(true, "entity-a", true,
    int.MinValue,
    NeonRgba.ProjectCyan).Accepted,
    "the host rejects non-neon entities");
```

Also test that snapshots prune dead identities, a pending queue applies when an
identity resolves, the queue has a fixed maximum, expired entries disappear, and
`Clear()` removes all world state.

**Step 2: Run the contract test and verify RED**

Run the Task 1 contract-test command.

Expected: compilation fails because the authoritative state and pending queue do
not exist.

**Step 3: Implement minimal generic policies**

Add:

```csharp
public readonly record struct NeonLetterColorAcceptance(
    bool Accepted,
    NeonRgba AuthoritativeColor);

public sealed class NeonLetterAuthoritativeColors<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, NeonRgba> _colors = new();

    public NeonLetterColorAcceptance TryAccept(
        bool isHost,
        TKey identity,
        bool isLive,
        int recipeId,
        NeonRgba color);

    public IReadOnlyList<KeyValuePair<TKey, NeonRgba>> Snapshot(
        Func<TKey, bool> isLive);

    public NeonRgba Resolve(TKey identity);
    public void Clear();
}
```

Add `NeonLetterPendingColors<TKey>` with constructor-supplied `capacity` and
`lifetimeSeconds`, `Enqueue`, `ApplyReady`, `Prune`, and `Clear`. Keep it pure: time
and identity resolution are supplied by callers.

**Step 4: Run the contract test and verify GREEN**

Run the Task 1 contract-test command.

Expected: all behavior contracts pass.

**Step 5: Commit**

```bash
git add NeonLetterMultiplayerState.cs \
  tests/SOTFNeonLetters.ContractTests/Program.cs \
  tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj
git commit -m "feat: add authoritative neon color state"
```

### Task 3: Add the SonsSdk packet transport

**Files:**

- Create: `NeonLetterMultiplayerRuntime.cs`
- Modify: `SOTFNeonLetters.cs`
- Modify: `SOTFNeonLetters.csproj`

**Step 1: Add the direct packet dependency and event skeletons**

Add the `udpkit` reference:

```xml
<Reference Include="udpkit">
  <HintPath>$(GameDir)\_RedLoader\Game\udpkit.dll</HintPath>
</Reference>
```

Create three internal `Packets.NetEvent` adapters with stable IDs:

```csharp
private const string ChangeRequestEventId =
    "SOTFNeonLetters.ColorChangeRequest.v1";
private const string ColorStateEventId =
    "SOTFNeonLetters.ColorState.v1";
private const string SnapshotRequestEventId =
    "SOTFNeonLetters.ColorSnapshotRequest.v1";
```

Register exactly one instance of each through `Packets.Register` from a new
`NeonLetterMultiplayerRuntime.Initialize()` call in `OnInitializeMod`, following
the local Signs pattern.

**Step 2: Build and verify the incomplete adapter fails visibly**

Run:

```bash
DOTNET_ROOT="$PWD/.tools/dotnet-6" \
DOTNET_CLI_HOME="$PWD/.tools/dotnet-cli" \
./.tools/dotnet-6/dotnet build SOTFNeonLetters.csproj \
  --configuration Release -p:DisableCopyToGame=True
```

Expected before handlers are complete: compilation errors for missing packet
read/write methods or runtime callbacks, not a silent no-op event.

**Step 3: Implement request and state payloads through Bolt/UdpKit**

Use exact installed APIs:

```csharp
EventPacket packet = NewPacket(64, GlobalTargets.OnlyServer);
packet.Packet.WriteByte(NeonLetterNetworkProtocol.CurrentVersion);
packet.Packet.WriteNetworkId(entity.networkId);
packet.Packet.WriteUInt(NeonLetterNetworkProtocol.Pack(color));
Send(packet);
```

On read, use `ReadByte`, `ReadNetworkId`, `ReadUInt`, and
`BoltNetwork.FindEntity(networkId)`. Host broadcasts use
`GlobalTargets.AllClients`; targeted snapshot replies use
`NewPacket(size, BoltConnection)`. Wrap each read boundary in `try/catch`, log the
event ID and error, and never throw back into Bolt.

**Step 4: Build and run all contract tests**

Run the Task 1 contract command and the Release build command from Step 2.

Expected: both pass; no game files are copied.

**Step 5: Commit**

```bash
git add NeonLetterMultiplayerRuntime.cs SOTFNeonLetters.cs SOTFNeonLetters.csproj
git commit -m "feat: add SonsSdk neon color transport"
```

### Task 4: Route the existing color editor by network role

**Files:**

- Modify: `NeonLetterColorInteractionPolicy.cs`
- Modify: `NeonLetterColorRuntime.cs`
- Modify: `tests/SOTFNeonLetters.ContractTests/Program.cs`

**Step 1: Change the interaction expectation to multiplayer-editable**

Replace the obsolete permanent assertion that multiplayer is rejected with:

```csharp
CheckEqual(
    true,
    NeonLetterColorInteractionPolicy.IsEditable(
        hasCompletedStructure: true,
        recipeId: knownRecipeId),
    "a completed A-Z structure is editable in Single Player or multiplayer");
```

Add a pure role decision test proving Single Player commits locally, a host commits
and broadcasts, and a client sends a request without committing host state.

**Step 2: Run the contract test and verify RED**

Run the Task 1 contract-test command.

Expected: the old `isSinglePlayer` policy rejects the new multiplayer behavior.

**Step 3: Implement the smallest role-aware target path**

- Remove the early `NetUtils.IsMultiplayer` return from
  `TryResolveTargetFromView`.
- Keep the completed `ScrewStructure` and A-Z recipe validation.
- In multiplayer require a live, attached root `BoltEntity` with a non-zero
  `networkId`.
- Store that entity in `NeonLetterColorTarget` only as a session network target.
- Keep `PreviewColor` local.
- Route `CommitColor` to the existing Single Player persistence path when Bolt is
  not running, and to `NeonLetterMultiplayerRuntime.RequestColor` when it is.
- Resolve `CurrentColor` from host/client authoritative session state in
  multiplayer and from the existing SaveId/session state in Single Player.

Do not change the ray distance, key registration, SUI layout, emission policy, or
shared-material behavior.

**Step 4: Run contract tests and Release build**

Expected: both pass and every previous Single Player contract remains green.

**Step 5: Commit**

```bash
git add NeonLetterColorInteractionPolicy.cs NeonLetterColorRuntime.cs \
  tests/SOTFNeonLetters.ContractTests/Program.cs
git commit -m "feat: enable multiplayer neon color editing"
```

### Task 5: Add late-join snapshot synchronization

**Files:**

- Modify: `NeonLetterMultiplayerRuntime.cs`
- Modify: `SOTFNeonLetters.cs`
- Modify: `tests/SOTFNeonLetters.ContractTests/Program.cs`

**Step 1: Add failing late-join behavior tests**

Use the pure state classes to prove that:

- a snapshot contains every live customized identity exactly once;
- default-only and destroyed identities are absent;
- a client state received before entity spawn enters the pending queue;
- resolving that identity applies once and removes the pending entry;
- applying a snapshot never emits an outgoing request.

**Step 2: Run contract tests and verify RED**

Expected: at least the snapshot/pending coordination assertion fails before the
runtime coordinator exists.

**Step 3: Implement snapshot request and bounded retry**

- On client `OnGameStart`/`OnAfterSpawn`, send one
  `ColorSnapshotRequest.v1` packet to `OnlyServer`.
- On the host, create one targeted `ColorState.v1` packet per live customized
  identity using the request's `BoltConnection`.
- On clients, apply immediately when `BoltNetwork.FindEntity` succeeds; otherwise
  enqueue by packed `NetworkId` for at most 128 entries and 15 seconds.
- Drain ready pending entries from the existing in-world update event; do not start
  an unbounded coroutine per packet.
- Clear request, pending, and authoritative state on `SdkEvents.OnWorldExited`.

**Step 4: Run contract tests and Release build**

Expected: all pass.

**Step 5: Commit**

```bash
git add NeonLetterMultiplayerRuntime.cs SOTFNeonLetters.cs \
  tests/SOTFNeonLetters.ContractTests/Program.cs
git commit -m "feat: sync neon colors for late joiners"
```

### Task 6: Add host-only multiplayer persistence

**Files:**

- Create: `NeonLetterMultiplayerPersistencePolicy.cs`
- Create: `NeonLetterMultiplayerSaveRuntime.cs`
- Modify: `SOTFNeonLetters.cs`
- Modify: `tests/SOTFNeonLetters.ContractTests/Program.cs`
- Modify: `tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj`

**Step 1: Write failing host-save tests**

Define a versioned pure envelope and test:

```csharp
var entry = new NeonLetterMultiplayerSaveEntry
{
    RecipeId = NeonLetterSmallCatalog.Get('G').RecipeId,
    NativeSaveId = 42,
    Position = new NeonVector3(1f, 2f, 3f),
    Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
    PackedColor = NeonLetterNetworkProtocol.Pack(
        new NeonRgba(0f, 1f, 0f, 1f))
};
```

Prove JSON round-trip, unknown recipe filtering, client write/load rejection, native
restore preference, and exactly one fallback-spawn decision when native identity is
missing. The decision policy must return `UseNative`, `SpawnFallback`, or `Skip`;
it must not call Unity/Bolt itself.

**Step 2: Run contract tests and verify RED**

Expected: compilation fails because multiplayer save types do not exist.

**Step 3: Implement the pure save envelope and restore policy**

Create only serializable scalar records (`NeonVector3`, `NeonQuaternion`) so tests
do not depend on Unity transforms. Validate envelope version, known A-Z recipe ID,
finite transform values, normalized non-zero quaternion, and RGBA32 payload.

**Step 4: Run contract tests and verify GREEN**

Expected: all contracts pass.

**Step 5: Implement the SonsSaveTools adapter**

- Register `NeonLetterMultiplayerSaveRuntime` once.
- Return no world payload from a multiplayer client.
- On host save, scan live `ScrewStructure` instances, filter A-Z recipes, record
  native SaveId when it is genuinely owned, transform, and authoritative color.
- On load, keep an isolated envelope and wait until host role, Bolt startup, and
  `AfterLoadSave`/game start are ready.
- First resolve native SaveId and matching recipe; apply/broadcast color there.
- Only if native restore is absent, resolve the processed recipe's built prefab and
  call `BoltNetwork.Instantiate(prefab, position, rotation)` once.
- Apply the restored color after the returned entity is attached, then add it to
  host authoritative state and broadcast it.
- Never run fallback spawn on a client and never restore a mismatched recipe.

**Step 6: Run contract tests and Release build**

Expected: all pass.

**Step 7: Commit**

```bash
git add NeonLetterMultiplayerPersistencePolicy.cs \
  NeonLetterMultiplayerSaveRuntime.cs SOTFNeonLetters.cs \
  tests/SOTFNeonLetters.ContractTests/Program.cs \
  tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj
git commit -m "feat: persist multiplayer neon letters on host"
```

### Task 7: Version, documentation, and complete automated gate

**Files:**

- Modify: `manifest.json`
- Modify: `README.md`
- Verify generated release: `ReleaseBuild/SOTFNeonLetters.zip`

**Step 1: Update release metadata and usage documentation**

- Bump the mod version from `0.1.0` to `0.2.0`.
- Document that host and every client need the same DLL and asset bundle.
- Document the host-authoritative Apply flow and late-join synchronization.
- Correct the stale README statement that color selection is a future iteration.
- State that dedicated-server compatibility is not yet runtime-certified.

**Step 2: Run the complete gate**

Run:

```bash
./tools/test-all.sh
```

Expected final line: `All SOTF Neon Letters test gates passed.`

**Step 3: Clean only generated test artifacts**

Inspect `git status --short`. Restore the known Unity-generated whitespace-only
`ShaderGraphSettings.asset` change and move duplicate generated folders/files to
Trash using explicit paths. Do not remove canonical source assets or user files.

**Step 4: Re-run focused checks after cleanup**

Run:

```bash
git diff --check
DOTNET_ROOT="$PWD/.tools/dotnet-6" \
DOTNET_CLI_HOME="$PWD/.tools/dotnet-cli" \
./.tools/dotnet-6/dotnet run \
  --project tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj
```

Expected: no diff errors and all contracts pass.

**Step 5: Commit the release checkpoint**

```bash
git add manifest.json README.md ReleaseBuild/SOTFNeonLetters.zip
git commit -m "chore: prepare multiplayer neon release"
```

### Task 8: Deploy and capture runtime acceptance evidence

**Files:**

- Runtime artifacts only: `/Users/nikita/Documents/Codex/2026-07-17/j/artifacts/`
- Do not modify the user's save without explicit permission.

**Step 1: Verify the game is closed before deployment**

Check the Sons of the Forest and Wine/CrossOver processes. Ask the user to close
the game only if it is actually running. Do not terminate it silently while the
user is playing.

**Step 2: Deploy exactly the committed release**

Run the Release build without `DisableCopyToGame`, then compare SHA-256 of the
project DLL, deployed DLL, project asset bundle, and deployed bundle. All matching
pairs must be identical.

**Step 3: Run the Single Player regression smoke**

Enter only Single Player, build one letter on the test cabin wall, open `E`, change
its color, and capture screenshots of placement, picker, and applied color. Exit
without saving.

Expected: placement and color editing match checkpoint `b990172`.

**Step 4: Run the two-peer multiplayer smoke**

With the same mod version installed for both peers, capture separate host/client
screenshots for:

1. one wall-mounted letter built by a participant;
2. client Apply visible on host and client;
3. host Apply visible on host and client;
4. a reconnecting client receiving the existing color.

Expected: each peer sees one correctly oriented letter with the same accepted
color, and RedLoader/Wine logs contain no unhandled exception.

**Step 5: Test save/reload only after explicit permission**

If permission is given, back up the exact multiplayer save first, save/reload once,
and capture one restored letter with its color on both peers. If permission is not
given, leave this runtime acceptance item pending and report automated persistence
coverage without claiming save/reload was manually verified.

**Step 6: Final verification**

Run:

```bash
git status --short
git log --oneline -10
```

Expected: clean working tree; each TDD phase and release checkpoint is represented
by a narrow commit.
