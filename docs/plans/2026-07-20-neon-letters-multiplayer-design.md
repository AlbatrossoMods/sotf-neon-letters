# Neon Letters Multiplayer Design

## Status

Approved by the user on 2026-07-20. The selected approach is host-authoritative
multiplayer implemented through the installed SonsSdk 0.8.6 networking API. Every
participant, including the host, must run the same mod version and asset bundle.

This iteration starts from checkpoint commit `b990172`, where A-Z construction,
wall placement, post-build color editing, and the enlarged color picker are working
in Single Player.

## Goal

Make the existing neon-letter construction and color-editing flow work in a
multiplayer game without replacing SonsSdk construction or introducing a custom
transport. A letter built or recolored by one participant must have the same
orientation, position, and color for the host, current clients, and clients that
join later.

## Selected Architecture

SonsSdk already prepares every registered custom blueprint as a Bolt prefab. It
adds the built `BoltEntity`, derives deterministic prefab IDs from the recipe ID,
registers the crafting and built prefabs, and attaches `ScrewStructure`. The mod
will continue to register A-Z through `CustomBlueprintManager.TryRegister`; it will
not send its own construction or spawn packets during normal play.

Custom color state will use `SonsSdk.Networking.Packets.NetEvent` and the existing
Bolt entity identity:

- a client sends a color-change request to `GlobalTargets.OnlyServer`;
- the request contains the target `BoltEntity` and an RGBA32 color;
- the host verifies that the entity is live and its recipe belongs to the A-Z
  catalog;
- only the host commits authoritative color state;
- the host applies the accepted color locally and sends a color-state event to
  all clients;
- clients apply received state through the existing `MaterialPropertyBlock`
  emission path without emitting another event.

Using `OnlyServer` for requests is mandatory. A relayed client request would allow
other clients to apply unvalidated state before the host accepts it.

## Color Editing Flow

Single Player behavior remains unchanged.

In multiplayer, aiming at a completed neon letter and pressing Use opens the same
color picker. Preview changes remain local to the editor user. The buttons behave
as follows:

- **Apply:** the host validates and commits directly; a client sends a request and
  waits for the authoritative state event;
- **Cancel:** restores the last authoritative color locally and sends nothing;
- **Reset:** previews the original color and follows the normal Apply path if the
  user commits it.

When a request is rejected, the requester restores the last authoritative color.
Malformed colors, missing entities, destroyed entities, and non-neon recipe IDs
are rejected without changing any peer.

Any participant may recolor a neon letter that they can target through the current
three-metre interaction raycast. Per-player ownership and anti-cheat enforcement
are outside this iteration.

## Session Identity and State

`BoltEntity` is the authoritative identity inside a running multiplayer session.
Unity `GetInstanceID`, hierarchy paths, transforms, and raw pointers are never
transmitted or used to match peers.

The host keeps a map from live Bolt entity identity to accepted RGBA32 color.
Default-colored letters do not need an entry. Before snapshots and saves, stale or
destroyed entities are removed. All network state and pending client state are
cleared on world exit.

## Late Join

After the local player enters the world and network entities are available, a
client sends a snapshot request to the host. The host responds only to that
connection with the current color state of every live customized letter.

If a state packet arrives before its Bolt entity is present locally, the client
keeps the state in a bounded pending queue and applies it when the entity appears.
Pending entries expire instead of polling forever. Requesting and applying a
snapshot never causes a second network broadcast.

## Saving and Loading

The host is the only multiplayer save authority. Multiplayer clients neither write
nor independently load neon-letter world state.

The host save envelope is versioned and records enough information to restore an
untracked custom letter safely:

- recipe ID;
- native `ScrewStructure` save ID when available;
- position and rotation;
- accepted RGBA32 color.

On load, native `ScrewStructureManager` identity is preferred. If the base game has
already restored the matching neon structure, the mod only reapplies its color. If
the custom structure is not natively tracked or restored, the host creates it with
`BoltNetwork.Instantiate` after the multiplayer world is ready. This fallback is
never run on clients and must check for a native restored instance first so the mod
cannot create duplicates.

Loading may occur before host/client role and Bolt startup are ready, so save data
is queued until game start. The existing Single Player save path remains separate
and is not rewritten as part of multiplayer transport work.

## Protocol Compatibility

All event IDs are stable strings and all payloads begin with a protocol version.
The host rejects mismatched protocol versions before reading or applying state.
The supported deployment is modded-only: the same released DLL and asset bundle
must be installed for the host and every client. A dedicated server must also have
the mod installed.

The primary acceptance target is a normal host-created multiplayer game with one
or more clients. Dedicated-server startup will avoid client UI initialization and
share the same authoritative event and save code, but it will not be called tested
until a dedicated-server environment is exercised separately.

## Failure Handling

- Network handlers catch and log packet-level failures without throwing into the
  game update loop.
- Invalid requests never mutate host state or get broadcast.
- A missing client-side entity queues only the decoded state for bounded retry.
- A rejected Apply restores the requester's last authoritative color.
- World exit closes the editor and clears session, snapshot, and pending state.
- Existing blueprint registration, placement values, colliders, shaders, and book
  pages are not changed by this iteration.

## Test Strategy

Implementation follows red-green-refactor cycles. Permanent automated tests cover:

- deterministic RGBA32 packing and decoding;
- protocol-version validation;
- requests route only to the server;
- only live A-Z recipe entities can be accepted;
- clients cannot commit authoritative state directly;
- accepted state is applied once locally and broadcast once;
- received state applies without creating a network loop;
- snapshot responses contain all live customized letters and no stale entities;
- pending late-join state is bounded, expires, and applies when identity resolves;
- multiplayer clients never save or load world state;
- host save envelopes round-trip recipe, identity, transform, and color;
- native restore is preferred and fallback spawning cannot duplicate a letter;
- Single Player color-editing and the full existing build/package gate remain
  green.

Runtime acceptance requires two modded peers and screenshot evidence for:

1. both peers seeing the same newly built letter on a wall;
2. a client recoloring it and both peers seeing the accepted color;
3. the host recoloring it and both peers seeing the accepted color;
4. a later-joining client receiving the existing color;
5. host save/reload restoring one copy of the letter with its color;
6. no regression in Single Player construction and color editing.

