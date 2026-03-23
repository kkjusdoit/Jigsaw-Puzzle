using System.Collections.Generic;
using UnityEngine;

namespace JigsawPuzzle.Puzzle.Runtime
{
    public sealed class PuzzlePieceState
    {
        public int PieceId;
        public int CorrectCellIndex;
        public int CurrentCellIndex;
        public int GroupId;
        public int CorrectRow;
        public int CorrectColumn;
    }

    public sealed class PuzzleGroupState
    {
        public int GroupId;
        public readonly List<int> PieceIds = new List<int>();
    }

    public sealed class PuzzleState
    {
        public readonly Dictionary<int, PuzzlePieceState> Pieces = new Dictionary<int, PuzzlePieceState>();
        public readonly Dictionary<int, PuzzleGroupState> Groups = new Dictionary<int, PuzzleGroupState>();
        public readonly Dictionary<int, int> CellToPiece = new Dictionary<int, int>();

        public PuzzlePieceState GetPiece(int pieceId) => Pieces[pieceId];

        public PuzzleGroupState GetGroup(int groupId) => Groups[groupId];

        public IEnumerable<int> GetGroupPieceIds(int groupId) => Groups[groupId].PieceIds;

        public bool IsSolved()
        {
            foreach (PuzzlePieceState piece in Pieces.Values)
            {
                if (piece.CurrentCellIndex != piece.CorrectCellIndex)
                {
                    return false;
                }
            }

            return true;
        }

        public void RebuildGroups()
        {
            Groups.Clear();
            foreach (PuzzlePieceState piece in Pieces.Values)
            {
                if (!Groups.TryGetValue(piece.GroupId, out PuzzleGroupState group))
                {
                    group = new PuzzleGroupState { GroupId = piece.GroupId };
                    Groups.Add(group.GroupId, group);
                }

                group.PieceIds.Add(piece.PieceId);
            }
        }
    }

    public readonly struct PuzzleMovePlan
    {
        public PuzzleMovePlan(
            int movingGroupId,
            int anchorPieceId,
            Dictionary<int, int> pieceToCellAssignments,
            HashSet<int> displacedPieceIds)
        {
            MovingGroupId = movingGroupId;
            AnchorPieceId = anchorPieceId;
            PieceToCellAssignments = pieceToCellAssignments;
            DisplacedPieceIds = displacedPieceIds;
        }

        public int MovingGroupId { get; }
        public int AnchorPieceId { get; }
        public Dictionary<int, int> PieceToCellAssignments { get; }
        public HashSet<int> DisplacedPieceIds { get; }
    }

    public readonly struct PuzzleDragContext
    {
        public PuzzleDragContext(
            int groupId,
            int anchorPieceId,
            Vector3 pointerWorldStart,
            Vector3 anchorWorldStart,
            Dictionary<int, Vector3> pieceWorldStarts)
        {
            GroupId = groupId;
            AnchorPieceId = anchorPieceId;
            PointerWorldStart = pointerWorldStart;
            AnchorWorldStart = anchorWorldStart;
            PieceWorldStarts = pieceWorldStarts;
        }

        public int GroupId { get; }
        public int AnchorPieceId { get; }
        public Vector3 PointerWorldStart { get; }
        public Vector3 AnchorWorldStart { get; }
        public Dictionary<int, Vector3> PieceWorldStarts { get; }
    }
}
