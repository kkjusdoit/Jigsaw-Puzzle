using JigsawPuzzle.Puzzle.Core;
using UnityEngine;

namespace JigsawPuzzle.Puzzle.Runtime
{
    public sealed class PuzzleMergeResolver
    {
        public void ApplyAutoMerges(PuzzleState state, PuzzleBoardController board)
        {
            bool merged;
            do
            {
                merged = false;
                foreach (PuzzlePieceState piece in state.Pieces.Values)
                {
                    int currentCell = piece.CurrentCellIndex;
                    if (TryMergeNeighbor(state, board, piece, currentCell, 0, 1))
                    {
                        merged = true;
                        break;
                    }

                    if (TryMergeNeighbor(state, board, piece, currentCell, 1, 0))
                    {
                        merged = true;
                        break;
                    }
                }
            }
            while (merged);
        }

        private static bool TryMergeNeighbor(
            PuzzleState state,
            PuzzleBoardController board,
            PuzzlePieceState piece,
            int currentCell,
            int rowOffset,
            int columnOffset)
        {
            var currentCoords = board.GetCellCoords(currentCell);
            int neighborRow = currentCoords.x + rowOffset;
            int neighborColumn = currentCoords.y + columnOffset;
            if (neighborRow >= board.Rows || neighborColumn >= board.Columns)
            {
                return false;
            }

            int neighborCell = board.GetCellIndex(neighborRow, neighborColumn);
            int neighborPieceId = state.CellToPiece[neighborCell];
            PuzzlePieceState neighbor = state.GetPiece(neighborPieceId);
            if (neighbor.GroupId == piece.GroupId)
            {
                return false;
            }

            if (!ArePiecesCorrectNeighbors(piece, neighbor, board))
            {
                return false;
            }

            int targetGroupId = piece.GroupId < neighbor.GroupId ? piece.GroupId : neighbor.GroupId;
            int absorbedGroupId = targetGroupId == piece.GroupId ? neighbor.GroupId : piece.GroupId;

            foreach (int memberId in state.GetGroup(absorbedGroupId).PieceIds)
            {
                state.GetPiece(memberId).GroupId = targetGroupId;
            }

            state.RebuildGroups();
            return true;
        }

        private static bool ArePiecesCorrectNeighbors(PuzzlePieceState a, PuzzlePieceState b, PuzzleBoardController board)
        {
            Vector2Int currentA = board.GetCellCoords(a.CurrentCellIndex);
            Vector2Int currentB = board.GetCellCoords(b.CurrentCellIndex);
            Vector2Int currentDelta = currentA - currentB;
            Vector2Int correctDelta = new Vector2Int(a.CorrectRow - b.CorrectRow, a.CorrectColumn - b.CorrectColumn);

            if (Mathf.Abs(currentDelta.x) + Mathf.Abs(currentDelta.y) != 1)
            {
                return false;
            }

            if (Mathf.Abs(correctDelta.x) + Mathf.Abs(correctDelta.y) != 1)
            {
                return false;
            }

            return currentDelta == correctDelta;
        }
    }
}
