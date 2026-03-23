using System;
using System.Collections.Generic;
using System.Linq;
using JigsawPuzzle.Puzzle.Data;
using JigsawPuzzle.Puzzle.Runtime;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace JigsawPuzzle.Puzzle.Core
{
    public sealed class PuzzleGameController : MonoBehaviour
    {
        private const string DefaultLevelResourcePath = "DefaultPuzzleLevel";
        private const string DefaultSpriteResourcePath = "Puzzle/demo_puzzle_source";
        private const string DefaultSpriteAssetPath = "Assets/Puzzle/demo_puzzle_source.png";

        [SerializeField] private PuzzleLevelConfig levelConfig;

        private readonly PuzzleSpriteSlicer slicer = new PuzzleSpriteSlicer();
        private readonly PuzzleSwapResolver swapResolver = new PuzzleSwapResolver();
        private readonly PuzzleMergeResolver mergeResolver = new PuzzleMergeResolver();
        private readonly Dictionary<int, PuzzlePieceView> pieceViews = new Dictionary<int, PuzzlePieceView>();

        private PuzzleBoardController board;
        private PuzzleState state;
        private PuzzleDragContext? activeDrag;
        private System.Random random;
        private Sprite sourceSprite;
        private bool isSolved;
        private string statusText;

        public Camera WorldCamera { get; private set; }

        private void Awake()
        {
            random = new System.Random();
            WorldCamera = EnsureCamera();
            EnsureBackgroundColor();
        }

        private void Start()
        {
            BuildGame();
        }

        public bool CanInteract => !isSolved && state != null;

        public bool TryBeginDrag(int pieceId, Vector3 pointerWorldPosition)
        {
            if (!CanInteract || !pieceViews.TryGetValue(pieceId, out PuzzlePieceView view))
            {
                return false;
            }

            PuzzlePieceState piece = state.GetPiece(pieceId);
            List<int> groupPieceIds = state.GetGroup(piece.GroupId).PieceIds;
            Dictionary<int, Vector3> pieceWorldStarts = new Dictionary<int, Vector3>();
            foreach (int groupPieceId in groupPieceIds)
            {
                PuzzlePieceView pieceView = pieceViews[groupPieceId];
                pieceWorldStarts[groupPieceId] = pieceView.transform.position;
                pieceView.SetSelected(true);
            }

            activeDrag = new PuzzleDragContext(
                piece.GroupId,
                pieceId,
                pointerWorldPosition,
                pieceViews[pieceId].transform.position,
                pieceWorldStarts);

            statusText = $"Dragging group #{piece.GroupId + 1}";
            return true;
        }

        public void UpdateDrag(Vector3 pointerWorldPosition)
        {
            if (!activeDrag.HasValue)
            {
                return;
            }

            PuzzleDragContext drag = activeDrag.Value;
            Vector3 delta = pointerWorldPosition - drag.PointerWorldStart;

            foreach ((int pieceId, Vector3 pieceStart) in drag.PieceWorldStarts)
            {
                pieceViews[pieceId].SetWorldPosition(pieceStart + delta);
            }
        }

        public void EndDrag(Vector3 pointerWorldPosition)
        {
            if (!activeDrag.HasValue)
            {
                return;
            }

            PuzzleDragContext drag = activeDrag.Value;
            Vector3 delta = pointerWorldPosition - drag.PointerWorldStart;
            Vector3 anchorTargetWorld = drag.AnchorWorldStart + delta;

            if (!board.TryGetCellIndex(anchorTargetWorld, out int targetAnchorCell) ||
                !swapResolver.TryCreateMovePlan(state, board, drag.GroupId, drag.AnchorPieceId, targetAnchorCell, out PuzzleMovePlan movePlan))
            {
                CancelActiveDrag();
                statusText = "Invalid move";
                return;
            }

            ApplyMovePlan(movePlan);
            activeDrag = null;
            statusText = "Move resolved";
        }

        private void BuildGame()
        {
            if (levelConfig == null)
            {
                levelConfig = Resources.Load<PuzzleLevelConfig>(DefaultLevelResourcePath);
            }

            sourceSprite = ResolveSourceSprite();
            PuzzleLevelConfig config = levelConfig != null ? levelConfig : PuzzleLevelConfig.CreateFallback(sourceSprite);

            BuildBoard(config, sourceSprite);
            BuildPieces(config, sourceSprite);
            RefreshAllPieceWorldPositions();
            statusText = "Swap groups to solve the board";
            isSolved = state.IsSolved();
        }

        private void BuildBoard(PuzzleLevelConfig config, Sprite sprite)
        {
            float aspect = sprite.bounds.size.x / sprite.bounds.size.y;
            float maxBoardHeight = 7.0f;
            float maxBoardWidth = 4.6f;
            float boardHeight = maxBoardHeight;
            float boardWidth = boardHeight * aspect;
            if (boardWidth > maxBoardWidth)
            {
                boardWidth = maxBoardWidth;
                boardHeight = boardWidth / aspect;
            }

            Vector3 boardCenter = new Vector3(0f, -0.45f, 0f);
            board = new PuzzleBoardController(config.Rows, config.Columns, boardWidth, boardHeight, boardCenter);
            FitCameraToBoard();
            CreateBoardVisual();
        }

        private void BuildPieces(PuzzleLevelConfig config, Sprite sprite)
        {
            state = new PuzzleState();
            pieceViews.Clear();

            int[] arrangement = config.CreateInitialArrangement(random);
            Transform root = new GameObject("PuzzlePieces").transform;
            root.SetParent(transform, false);

            Vector2 cellSize = new Vector2(board.CellWidth, board.CellHeight);
            for (int pieceId = 0; pieceId < config.PieceCount; pieceId++)
            {
                int correctRow = pieceId / config.Columns;
                int correctColumn = pieceId % config.Columns;
                PuzzlePieceState pieceState = new PuzzlePieceState
                {
                    PieceId = pieceId,
                    CorrectCellIndex = pieceId,
                    GroupId = pieceId,
                    CorrectRow = correctRow,
                    CorrectColumn = correctColumn
                };
                state.Pieces.Add(pieceId, pieceState);
            }

            for (int cellIndex = 0; cellIndex < arrangement.Length; cellIndex++)
            {
                int pieceId = arrangement[cellIndex];
                PuzzlePieceState piece = state.GetPiece(pieceId);
                piece.CurrentCellIndex = cellIndex;
                state.CellToPiece[cellIndex] = pieceId;
            }

            state.RebuildGroups();
            mergeResolver.ApplyAutoMerges(state, board);

            for (int pieceId = 0; pieceId < config.PieceCount; pieceId++)
            {
                PuzzlePieceState piece = state.GetPiece(pieceId);
                Sprite slice = slicer.CreateSlice(sprite, config.Rows, config.Columns, piece.CorrectRow, piece.CorrectColumn);
                PuzzlePieceView view = PuzzlePieceView.Create(root, $"Piece_{pieceId:00}");
                view.Initialize(pieceId, slice, cellSize, config.ShowPieceIndex);
                pieceViews.Add(pieceId, view);
            }
        }

        private void ApplyMovePlan(PuzzleMovePlan movePlan)
        {
            foreach ((int pieceId, int targetCellIndex) in movePlan.PieceToCellAssignments)
            {
                state.GetPiece(pieceId).CurrentCellIndex = targetCellIndex;
            }

            state.CellToPiece.Clear();
            foreach (PuzzlePieceState piece in state.Pieces.Values)
            {
                state.CellToPiece[piece.CurrentCellIndex] = piece.PieceId;
            }

            mergeResolver.ApplyAutoMerges(state, board);
            RefreshAllPieceWorldPositions();

            isSolved = state.IsSolved();
            if (isSolved)
            {
                statusText = "Puzzle complete!";
            }
        }

        private void RefreshAllPieceWorldPositions()
        {
            foreach (PuzzlePieceState piece in state.Pieces.Values)
            {
                PuzzlePieceView view = pieceViews[piece.PieceId];
                view.SetSelected(false);
                view.SetWorldPosition(board.GetCellWorldPosition(piece.CurrentCellIndex));
            }
        }

        private void CancelActiveDrag()
        {
            if (!activeDrag.HasValue)
            {
                return;
            }

            foreach ((int pieceId, Vector3 startPosition) in activeDrag.Value.PieceWorldStarts)
            {
                PuzzlePieceView view = pieceViews[pieceId];
                view.SetSelected(false);
                view.SetWorldPosition(startPosition);
            }

            activeDrag = null;
        }

        private Sprite ResolveSourceSprite()
        {
            if (levelConfig != null && levelConfig.SourceSprite != null)
            {
                return levelConfig.SourceSprite;
            }

            Sprite resourceSprite = Resources.Load<Sprite>(DefaultSpriteResourcePath);
            if (resourceSprite != null)
            {
                return resourceSprite;
            }

#if UNITY_EDITOR
            Sprite editorSprite = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultSpriteAssetPath);
            if (editorSprite != null)
            {
                return editorSprite;
            }
#endif

            return CreateFallbackSprite();
        }

        private Camera EnsureCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                camera = cameraObject.AddComponent<Camera>();
                camera.tag = "MainCamera";
            }

            camera.orthographic = true;
            camera.orthographicSize = 4.7f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            return camera;
        }

        private void EnsureBackgroundColor()
        {
            if (WorldCamera != null)
            {
                WorldCamera.backgroundColor = new Color(0.10f, 0.12f, 0.17f, 1f);
            }
        }

        private void FitCameraToBoard()
        {
            if (WorldCamera == null || board == null)
            {
                return;
            }

            float aspect = Mathf.Max(0.01f, WorldCamera.aspect);
            float verticalPadding = 0.45f;
            float horizontalPadding = 0.30f;
            float topUiPadding = 0.95f;

            float requiredByHeight = board.BoardHeight * 0.5f + verticalPadding + topUiPadding;
            float requiredByWidth = (board.BoardWidth * 0.5f + horizontalPadding) / aspect;
            WorldCamera.orthographicSize = Mathf.Max(requiredByHeight, requiredByWidth);
            WorldCamera.transform.position = new Vector3(board.Center.x, board.Center.y, -10f);
        }

        private void CreateBoardVisual()
        {
            Transform boardRoot = new GameObject("PuzzleBoard").transform;
            boardRoot.SetParent(transform, false);

            Sprite whiteSprite = CreateSolidSprite();
            CreateQuad(boardRoot, "BoardBackground", board.Center, new Vector2(board.BoardWidth + 0.12f, board.BoardHeight + 0.12f), new Color(0.15f, 0.18f, 0.24f, 1f), 0, whiteSprite);

            for (int row = 1; row < board.Rows; row++)
            {
                float y = board.BottomLeft.y + board.BoardHeight - row * board.CellHeight;
                CreateQuad(boardRoot, $"Row_{row}", new Vector3(board.Center.x, y, 0f), new Vector2(board.BoardWidth + 0.02f, 0.03f), new Color(0.28f, 0.33f, 0.40f, 1f), 1, whiteSprite);
            }

            for (int column = 1; column < board.Columns; column++)
            {
                float x = board.BottomLeft.x + column * board.CellWidth;
                CreateQuad(boardRoot, $"Column_{column}", new Vector3(x, board.Center.y, 0f), new Vector2(0.03f, board.BoardHeight + 0.02f), new Color(0.28f, 0.33f, 0.40f, 1f), 1, whiteSprite);
            }
        }

        private static void CreateQuad(Transform parent, string name, Vector3 position, Vector2 size, Color color, int sortingOrder, Sprite sprite)
        {
            GameObject quad = new GameObject(name);
            quad.transform.SetParent(parent, false);
            quad.transform.position = position;

            SpriteRenderer renderer = quad.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        }

        private static Sprite CreateSolidSprite()
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        private static Sprite CreateFallbackSprite()
        {
            const int width = 512;
            const int height = 512;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = x / (float)(width - 1);
                    float v = y / (float)(height - 1);
                    Color gradient = Color.Lerp(new Color(0.92f, 0.48f, 0.27f), new Color(0.16f, 0.65f, 0.89f), u);
                    gradient = Color.Lerp(gradient, new Color(0.96f, 0.87f, 0.35f), v * 0.35f);

                    float stripe = Mathf.Sin((u + v) * 18f) * 0.08f;
                    gradient *= 1f + stripe;

                    if ((x / 64 + y / 64) % 2 == 0)
                    {
                        gradient *= 0.92f;
                    }

                    texture.SetPixel(x, y, gradient);
                }
            }

            for (int index = 0; index < width; index++)
            {
                texture.SetPixel(index, index, Color.white);
                texture.SetPixel(width - 1 - index, index, Color.black);
            }

            texture.Apply();
            texture.name = "PuzzleFallbackTexture";
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        private void OnGUI()
        {
            GUIStyle boxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleLeft
            };

            GUI.Box(new Rect(16f, 16f, 340f, 84f), $"Auto-Merge Puzzle\n{statusText}", boxStyle);
            if (state != null)
            {
                int solvedCount = state.Pieces.Values.Count(piece => piece.CurrentCellIndex == piece.CorrectCellIndex);
                GUI.Box(new Rect(16f, 108f, 340f, 42f), $"Solved pieces: {solvedCount}/{state.Pieces.Count}", boxStyle);
            }

            if (isSolved)
            {
                GUIStyle solvedStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 24,
                    alignment = TextAnchor.MiddleCenter
                };
                GUI.Box(new Rect(Screen.width * 0.5f - 180f, 20f, 360f, 56f), "Puzzle Complete", solvedStyle);
            }
        }
    }
}
