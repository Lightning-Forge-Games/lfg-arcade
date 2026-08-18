# Chess

A 3D chess game in Unity 6000.3.7f1 with URP, an AI opponent, and online play over
Photon Fusion. Builds to web.

> Work happens on branch **`urp-web`**. `main` holds the earlier HDRP version and is kept
> only as a fallback; the two have diverged.

Open `Assets/Scenes/Chess.unity` and press play.

## Playing

The title screen offers **Single Player** (choose difficulty and colour) or **Play Online**.

Click a piece, then a highlighted square. In game you can toggle the camera between
**Angled** and **Overhead**, start a **New Game**, or **Quit to Menu**.

Online, one player hosts and gets a four character match code plus an invite URL. Opening
that URL joins the match directly and skips the menu. The host plays White.

## Layout

| Path | What it is |
| --- | --- |
| `Assets/Scripts/Chess/Core` | Rules, evaluation and search. Pure C#, no UnityEngine dependency. |
| `Assets/Scripts/Chess/Game` | Board and piece views, HUD, camera, coordinates, AI driver. |
| `Assets/Scripts/Chess/Net` | Fusion session, move relay, lobby UI. Compiles into Assembly-CSharp. |
| `Assets/Scripts/Chess/Editor` | Procedural piece model generator. |
| `Assets/Tests/EditMode` | Rules, notation and search tests, including perft. |
| `Assets/Tests/PlayMode` | Piece view, animation and input-guard tests. |

Run the tests from **Window → General → Test Runner**. EditMode 53, PlayMode 10.

## The rules engine

`LightningForge.Chess.Core` is an assembly with `noEngineReferences: true`. Keeping it free
of UnityEngine means the rules can be compiled and run outside the editor, which is how
they were first verified.

It is checked against **perft**: six reference positions walked to a fixed depth with the
leaf node counts compared against published values. Exact matches across 16.5 million nodes
is strong evidence that castling, en passant, promotion, pins and check evasion are right.

`SanWriter` produces algebraic notation. It lives beside the rules because naming a move
needs the full legal move list to decide disambiguation, and the resulting position to
append check or mate.

## The AI

Negamax with alpha-beta, material plus piece-square evaluation. Captures are ordered
most-valuable-victim first; without that ordering the same depth costs many times more nodes.

Difficulties differ in depth, node budget and deliberate sloppiness. Easy plays depth 2 and
will take a move up to 150cp worse or blunder outright one time in five. Hard is depth 4 and
always plays its best.

The search is bounded by **nodes as well as depth**, because the web build is single
threaded and an unbounded search would freeze the tab. `ChessAiPlayer` also waits for the
human's move to finish animating, so the hitch lands where it is least noticeable.

## Online play

Fusion **Shared Mode**: no dedicated host and no server code. Both clients run the same
verified rules engine and validate locally, so the network only relays the move that was
played, as a UCI string.

Whoever spawns the link object has state authority and plays White. `ControlMode` stops you
moving your opponent's pieces.

Setting it up needs a Photon **Fusion** App ID in
`Assets/Photon/Fusion/Resources/PhotonAppSettings.asset`. Three things are easy to miss:
`NetworkProjectConfig.fusion` must exist, the prefab table must be rebuilt after creating a
NetworkObject prefab from code, and NetworkBehaviours must live in Assembly-CSharp or their
RPCs throw `FieldAccessException` at the first call.

## Piece models

Generated, not authored. **Tools → Chess → Generate Piece Models** rebuilds every mesh and
prefab from `ChessPieceMeshGenerator`.

Each piece except the knight is a surface of revolution, as a real piece is turned on a
lathe. The knight extrudes a side-on silhouette, with ear clipping for the concave notch
behind the ear. Extrusion caps and walls get their own vertices so the creases stay hard.

## Building for web

Compression must stay **Disabled** and the template must be `APPLICATION:Default`. Gzip
produces valid files but the loader still demands a `Content-Encoding` header, and the
built-in decompression fallback does not cover it. `runInBackground` must stay enabled or
Fusion disconnects when the tab loses focus.

## Not in this version

- No clock, threefold repetition, or sound.
- No reconnect: refreshing loses an online game.
- Pieces are stand-in geometry. `PieceViewFactory` accepts authored prefabs per type and
  colour, so real models drop in without code changes.
- Nothing is deployed anywhere yet.
