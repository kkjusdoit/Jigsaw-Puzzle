using System.Collections.Generic;
using System.Linq;
using JigsawPuzzle.Puzzle.Core;
using UnityEngine;

namespace JigsawPuzzle.Puzzle.Runtime
{
    public sealed class PuzzleSwapResolver
    {
        public bool TryCreateMovePlan(
            PuzzleState state,
            PuzzleBoardController board,
            int movingGroupId,
            int anchorPieceId,
            int targetAnchorCellIndex,
            out PuzzleMovePlan movePlan)
        {
            PuzzlePieceState anchorPiece = state.GetPiece(anchorPieceId);
            Vector2Int anchorCoords = board.GetCellCoords(anchorPiece.CurrentCellIndex);
            Vector2Int targetCoords = board.GetCellCoords(targetAnchorCellIndex);
            Vector2Int delta = targetCoords - anchorCoords;

            if (delta == Vector2Int.zero)
            {
                movePlan = default;
                return false;
            }

            List<int> movingPieceIds = state.GetGroup(movingGroupId).PieceIds;
            Dictionary<int, int> assignments = new Dictionary<int, int>();
            HashSet<int> movingPieceSet = new HashSet<int>(movingPieceIds);
            HashSet<int> targetCells = new HashSet<int>();
            HashSet<int> oldCells = new HashSet<int>();

            foreach (int pieceId in movingPieceIds)
            {
                PuzzlePieceState piece = state.GetPiece(pieceId);
                oldCells.Add(piece.CurrentCellIndex);

                if (!board.TryGetTranslatedCell(piece.CurrentCellIndex, delta, out int translatedCell))
                {
                    movePlan = default;
                    return false;
                }

                assignments[pieceId] = translatedCell;
                targetCells.Add(translatedCell);
            }

            List<int> displacedPieceIds = new List<int>();
            foreach (int targetCell in targetCells)
            {
                int occupantPieceId = state.CellToPiece[targetCell];
                if (!movingPieceSet.Contains(occupantPieceId))
                {
                    displacedPieceIds.Add(occupantPieceId);
                }
            }

            List<int> vacatedCells = oldCells.Where(cell => !targetCells.Contains(cell)).OrderBy(cell => cell).ToList();
            displacedPieceIds = displacedPieceIds.Distinct().OrderBy(pieceId => state.GetPiece(pieceId).CurrentCellIndex).ToList();

            if (displacedPieceIds.Count != vacatedCells.Count)
            {
                movePlan = default;
                return false;
            }

            for (int index = 0; index < displacedPieceIds.Count; index++)
            {
                assignments[displacedPieceIds[index]] = vacatedCells[index];
            }

            movePlan = new PuzzleMovePlan(
                movingGroupId,
                anchorPieceId,
                assignments,
                new HashSet<int>(displacedPieceIds));
            return true;
        }
    }
}
