# LFG Arcade

Six classic games in one Unity project: chess, draughts, Connect 4, backgammon, Yahtzee and
snooker. Boots to a grid of tiles, you pick one, you play. Unity 6000.3.7f1 with URP,
builds to web.

**Play it: https://lightning-forge-games.github.io/lfg-arcade/**

Open `Assets/Scenes/Arcade.unity` and press play.

## The games

| Game | Vs computer | Hot seat | Online |
| --- | :---: | :---: | :---: |
| Chess | yes | yes | yes |
| Draughts | yes | yes | yes |
| Connect 4 | yes | yes | yes |
| Backgammon | yes | yes | yes |
| Yahtzee | yes | yes | yes |
| Snooker | no | yes | no |

Snooker has no computer opponent, so the setup screen offers a solo table instead of a
match and drops the difficulty and seat pickers. Everything else plays at three
difficulties.

Online, one player hosts and gets a four character match code plus an invite URL. Opening
that URL joins the match directly and skips the menu.

## Setup

**The Photon Fusion SDK is not in this repo.** It is a commercial SDK this project is not
licensed to redistribute, and its settings asset carries an App ID that bills to an
account. To build anything with online play:

1. Install **Photon Fusion 2** into `Assets/Photon` from the Photon dashboard.
2. Put your own Fusion App ID in `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset`.
3. Pin `FixedRegion` to one region. Left unset, clients pick their own nearest region, and
   two players who chose differently create two separate rooms with the same code and sit
   there waiting for each other.

Everything except the online mode compiles and runs without it.

## Layout

| Path | What it is |
| --- | --- |
| `Assets/Scripts/Arcade/Core/<Game>` | Rules and AI. Pure C#, no UnityEngine dependency. |
| `Assets/Scripts/Arcade/Game/Shared` | Shell, menu, theme, camera rig, procedural meshes, audio. |
| `Assets/Scripts/Arcade/Game/<Game>` | Boards, pieces, input, per-game presentation. |
| `Assets/Scripts/Arcade/Net` | Fusion session and move relay. No asmdef, on purpose. |
| `Assets/Tests/EditMode` | Rules, notation, search and wire format tests. |
| `Assets/Tests/PlayMode` | Animation, online session and networked-turn tests. |

Run them from **Window -> General -> Test Runner**. EditMode 172, PlayMode 23.

## How it fits together

`ArcadeGame` is the contract. The shell owns the menu, the camera and the surrounding
chrome; a game owns its board, its rules and its opponent. That seam is what lets one shell
start, stop and describe six unrelated games, and it is what the online layer relays
across: a game turns a move into an opaque string and back, and never touches Photon
itself.

Adding a game means rules and tests in `Core`, an `ArcadeGame` in `Game`, one entry in
`ArcadeCatalog` with `Playable = true`, and one inactive root object in the scene. No asset
wiring.

`LightningForge.Arcade.Core` has `noEngineReferences: true`. Keeping it free of UnityEngine
means the rules compile and run outside the editor, which is how most of them were first
verified. Chess is checked against **perft**: six reference positions walked to a fixed
depth with leaf node counts compared against published values, 16.5 million nodes matching
exactly.

The UI is built entirely in C# with UI Toolkit. There are no `.uxml` or `.uss` files.

## Online play

Fusion **Shared Mode**: no dedicated host and no server code. Both clients run the same
rules and validate locally, so the network only relays what was played. Chess sends UCI,
Connect 4 sends a column, Yahtzee sends four messages a turn.

Whoever spawns the link object takes the first seat. `ControlMode` stops you moving your
opponent's pieces. A rejected remote move means the two sides have diverged, so it is
logged as an error rather than dropped.

Yahtzee is the awkward one. Dice are genuinely thrown by the physics engine, and PhysX is
not deterministic across platforms, so replaying a throw on the other machine would scatter
differently and land on different numbers. The thrower's tray is authoritative: the throw
carries where each die landed and what it shows, and the other end throws its own dice for
the look of it, then eases them onto the real outcome while they are still slowing.

Three things about Fusion are easy to miss. `NetworkProjectConfig.fusion` must exist, the
prefab table must be rebuilt after creating a NetworkObject prefab from code, and
NetworkBehaviours must live in Assembly-CSharp or their RPCs throw `FieldAccessException`
at the first call. That last one is why `Assets/Scripts/Arcade/Net` deliberately has no
asmdef.

## Models

Generated, not authored. Chess pieces come from **Tools -> Chess -> Generate Piece Models**;
everything else is built at runtime by `ArcadeMeshes`.

Each chess piece except the knight is a surface of revolution, as a real piece is turned on
a lathe. The knight extrudes a side-on silhouette, with ear clipping for the concave notch
behind the ear. Extrusion caps and walls get their own vertices so the creases stay hard.

## Building for web

Compression must stay **Disabled** and the template is `PROJECT:Fullscreen`. Gzip produces
valid files but the loader still demands a `Content-Encoding` header, and the built-in
decompression fallback does not cover it. `runInBackground` must stay enabled or Fusion
disconnects when the tab loses focus.

Output is around 66 MB. The wasm trips GitHub's 50 MB advisory warning, which is harmless;
the hard limit is 100 MB.

## Not in this version

- Snooker has no computer opponent and no online mode. Shooter-authoritative relay would
  work, since the physics only has to be right on the machine taking the shot.
- Backgammon relays a whole turn in one message, so the opponent sees the result rather
  than the play. Yahtzee used to do the same and no longer does.
- Sound effects are synthesised at runtime rather than authored.
- No reconnect: refreshing loses an online game.

## Names

Yahtzee and Connect 4 are trademarks of Hasbro. This is an unaffiliated hobby project and
the names are used only to say what the games are.
