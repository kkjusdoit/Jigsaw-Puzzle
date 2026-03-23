using JigsawPuzzle.Puzzle.Core;
using System.Collections.Generic;
using UnityEngine;

namespace JigsawPuzzle.Puzzle.Runtime
{
    public sealed class PuzzleMergeResolver
    {
        public void ApplyAutoMerges(PuzzleState state, PuzzleBoardController board)
        {
            HashSet<int> visitedPieceIds = new HashSet<int>();
            int fallbackGroupId = 0;

            foreach (PuzzlePieceState piece in state.Pieces.Values)
            {
                if (visitedPieceIds.Contains(piece.PieceId))
                {
                    continue;
                }

                List<int> component = CollectConnectedComponent(state, board, piece.PieceId, visitedPieceIds);
                int componentGroupId = component.Count > 0 ? GetStableGroupId(component) : fallbackGroupId++;
                foreach (int pieceId in component)
                {
                    state.GetPiece(pieceId).GroupId = componentGroupId;
                }
            }

            state.RebuildGroups();
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

        private static List<int> CollectConnectedComponent(
            PuzzleState state,
            PuzzleBoardController board,
            int rootPieceId,
            HashSet<int> visitedPieceIds)
        {
            List<int> component = new List<int>();
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(rootPieceId);
            visitedPieceIds.Add(rootPieceId);

            while (queue.Count > 0)
            {
                int pieceId = queue.Dequeue();
                component.Add(pieceId);

                PuzzlePieceState piece = state.GetPiece(pieceId);
                Vector2Int coords = board.GetCellCoords(piece.CurrentCellIndex);
                EnqueueNeighbor(state, board, piece, coords.x - 1, coords.y, visitedPieceIds, queue);
                EnqueueNeighbor(state, board, piece, coords.x + 1, coords.y, visitedPieceIds, queue);
                EnqueueNeighbor(state, board, piece, coords.x, coords.y - 1, visitedPieceIds, queue);
                EnqueueNeighbor(state, board, piece, coords.x, coords.y + 1, visitedPieceIds, queue);
            }

            return component;
        }

        private static void EnqueueNeighbor(
            PuzzleState state,
            PuzzleBoardController board,
            PuzzlePieceState sourcePiece,
            int neighborRow,
            int neighborColumn,
            HashSet<int> visitedPieceIds,
            Queue<int> queue)
        {
            if (neighborRow < 0 || neighborRow >= board.Rows || neighborColumn < 0 || neighborColumn >= board.Columns)
            {
                return;
            }

            int neighborCell = board.GetCellIndex(neighborRow, neighborColumn);
            int neighborPieceId = state.CellToPiece[neighborCell];
            if (visitedPieceIds.Contains(neighborPieceId))
            {
                return;
            }

            PuzzlePieceState neighborPiece = state.GetPiece(neighborPieceId);
            if (!ArePiecesCorrectNeighbors(sourcePiece, neighborPiece, board))
            {
                return;
            }

            visitedPieceIds.Add(neighborPieceId);
            queue.Enqueue(neighborPieceId);
        }

        private static int GetStableGroupId(List<int> component)
        {
            int groupId = int.MaxValue;
            foreach (int pieceId in component)
            {
                if (pieceId < groupId)
                {
                    groupId = pieceId;
                }
            }

            return groupId;
        }
    }
}
