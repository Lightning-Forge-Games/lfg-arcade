# Chess

A 3D chess game built in Unity 6000.3.7f1 with HDRP.

Open `Assets/Scenes/Chess.unity` and press play. Click a piece to select it, click a
highlighted square to move. Two players share the mouse.

## Layout

| Path | What it is |
| --- | --- |
| `Assets/Scripts/Chess/Core` | Rules engine. Pure C#, no UnityEngine dependency. |
| `Assets/Scripts/Chess/Game` | Board and piece visuals, input, HUD. |
| `Assets/Scripts/Chess/Editor` | Procedural piece model generator. |
| `Assets/Tests/EditMode` | Rules tests, including perft. |
| `Assets/Tests/PlayMode` | Piece view and animation tests. |
| `Assets/Models/Pieces`, `Assets/Prefabs/Pieces` | Generated meshes and prefabs. |

## The rules engine

`LightningForge.Chess.Core` is an assembly with `noEngineReferences: true`. Keeping it
free of UnityEngine means the rules can be compiled and run outside the editor, which is
how they were first verified.

It is checked against **perft**, the standard move generation benchmark: the six
reference positions are walked to a fixed depth and the leaf node counts compared against
published values. Exact matches across 16.5 million nodes are strong evidence that
castling rights, en passant, promotion, pins and check evasion are all correct. A subtle
bug in any of those shifts the counts.

Run the tests from **Window → General → Test Runner**.

## Piece models

The models are generated, not authored. `Tools → Chess → Generate Piece Models` rebuilds
every mesh and prefab from code in `ChessPieceMeshGenerator`.

Each piece except the knight is a **surface of revolution**, which is how a real chess
piece is turned on a lathe: a 2D profile is revolved around the vertical axis. The rook's
crenellations, the queen's crown points and the king's cross are separate parts merged in.

The knight cannot be lathed. It extrudes a side-on horse silhouette, with ear clipping to
triangulate the concave notch behind the ear. Extrusion caps and side walls get their own
vertices so the creases stay hard, otherwise smoothed normals turn the piece into a shell.

## Look

`Assets/Settings/ChessLookProfile.asset` drives the render: dark gradient sky, fixed
exposure, ACES tonemapping, bloom, SSAO, vignette and grading. Lighting is a warm key, a
cool fill and a rim for edge separation.

If you edit that profile from code, note that `VolumeProfile.Add<T>()` only creates the
component in memory. It must be persisted with `AssetDatabase.AddObjectToAsset`, or the
profile silently reloads empty.

## Architecture notes

`ChessGameController` takes a screen position rather than reading input itself, so it has
no dependency on the Input System package and stays testable. `ChessPointerInput` is the
thin layer that feeds it.

The HUD uses UI Toolkit rather than TextMeshPro because it ships in the engine and needs
no imported font essentials, so the project builds from a clean clone.

`Board` is a plain C# object, so a domain reload during play mode wipes it without `Awake`
running again. Controller entry points call `EnsureInitialised` to recover.

## Not in this version

- No AI opponent. Hot seat only.
- No move list, clock, or draw by threefold repetition (fifty move and insufficient
  material are detected).
- No sound.
- No board coordinate labels.
- Pieces are stand-in geometry, good enough to read clearly but not final art.
