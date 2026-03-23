using UnityEngine;

namespace JigsawPuzzle.Puzzle.Core
{
    public sealed class PuzzleBoardController
    {
        public PuzzleBoardController(int rows, int columns, float boardWidth, float boardHeight, Vector3 center)
        {
            Rows = rows;
            Columns = columns;
            CellWidth = boardWidth / columns;
            CellHeight = boardHeight / rows;
            BoardWidth = boardWidth;
            BoardHeight = boardHeight;
            Center = center;
            BottomLeft = center - new Vector3(boardWidth * 0.5f, boardHeight * 0.5f, 0f);
        }

        public int Rows { get; }
        public int Columns { get; }
        public float CellWidth { get; }
        public float CellHeight { get; }
        public float BoardWidth { get; }
        public float BoardHeight { get; }
        public Vector3 Center { get; }
        public Vector3 BottomLeft { get; }

        public int GetCellIndex(int row, int column) => row * Columns + column;

        public Vector2Int GetCellCoords(int cellIndex)
        {
            return new Vector2Int(cellIndex / Columns, cellIndex % Columns);
        }

        public Vector3 GetCellWorldPosition(int cellIndex)
        {
            Vector2Int coords = GetCellCoords(cellIndex);
            float x = BottomLeft.x + (coords.y + 0.5f) * CellWidth;
            float y = BottomLeft.y + BoardHeight - (coords.x + 0.5f) * CellHeight;
            return new Vector3(x, y, 0f);
        }

        public bool TryGetCellIndex(Vector3 worldPosition, out int cellIndex)
        {
            float localX = worldPosition.x - BottomLeft.x;
            float localY = (BottomLeft.y + BoardHeight) - worldPosition.y;

            int column = Mathf.FloorToInt(localX / CellWidth);
            int row = Mathf.FloorToInt(localY / CellHeight);

            if (row < 0 || row >= Rows || column < 0 || column >= Columns)
            {
                cellIndex = -1;
                return false;
            }

            cellIndex = GetCellIndex(row, column);
            return true;
        }

        public bool TryGetTranslatedCell(int sourceCellIndex, Vector2Int delta, out int targetCellIndex)
        {
            Vector2Int coords = GetCellCoords(sourceCellIndex);
            int targetRow = coords.x + delta.x;
            int targetColumn = coords.y + delta.y;

            if (targetRow < 0 || targetRow >= Rows || targetColumn < 0 || targetColumn >= Columns)
            {
                targetCellIndex = -1;
                return false;
            }

            targetCellIndex = GetCellIndex(targetRow, targetColumn);
            return true;
        }
    }
}
