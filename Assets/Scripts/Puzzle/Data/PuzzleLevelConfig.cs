using System;
using System.Collections.Generic;
using UnityEngine;

namespace JigsawPuzzle.Puzzle.Data
{
    public enum PuzzleInitialLayoutMode
    {
        Random,
        Preset
    }

    [CreateAssetMenu(fileName = "PuzzleLevelConfig", menuName = "Jigsaw Puzzle/Level Config")]
    public sealed class PuzzleLevelConfig : ScriptableObject
    {
        [SerializeField] private Sprite sourceSprite;
        [SerializeField] private int rows = 3;
        [SerializeField] private int columns = 3;
        [SerializeField] private PuzzleInitialLayoutMode initialLayoutMode = PuzzleInitialLayoutMode.Random;
        [SerializeField] private int minMisplacedCount = 6;
        [SerializeField] private bool showPieceIndex = true;
        [SerializeField] private int[] presetArrangement = Array.Empty<int>();

        public Sprite SourceSprite => sourceSprite;
        public int Rows => Mathf.Max(2, rows);
        public int Columns => Mathf.Max(2, columns);
        public PuzzleInitialLayoutMode InitialLayoutMode => initialLayoutMode;
        public int MinMisplacedCount => Mathf.Max(1, minMisplacedCount);
        public bool ShowPieceIndex => showPieceIndex;
        public int PieceCount => Rows * Columns;

        public int[] CreateInitialArrangement(System.Random random)
        {
            int pieceCount = PieceCount;

            if (initialLayoutMode == PuzzleInitialLayoutMode.Preset &&
                presetArrangement != null &&
                presetArrangement.Length == pieceCount &&
                IsValidPermutation(presetArrangement, pieceCount))
            {
                int[] arrangement = new int[pieceCount];
                Array.Copy(presetArrangement, arrangement, pieceCount);
                return arrangement;
            }

            int[] shuffled = CreateIdentity(pieceCount);
            int targetMisplaced = Mathf.Min(MinMisplacedCount, pieceCount);

            for (int attempt = 0; attempt < 64; attempt++)
            {
                Shuffle(shuffled, random);
                if (CountMisplaced(shuffled) >= targetMisplaced)
                {
                    return shuffled;
                }
            }

            Array.Reverse(shuffled);
            return shuffled;
        }

        public static PuzzleLevelConfig CreateFallback(Sprite source)
        {
            PuzzleLevelConfig config = CreateInstance<PuzzleLevelConfig>();
            config.rows = 4;
            config.columns = 4;
            config.minMisplacedCount = 10;
            config.showPieceIndex = true;
            config.initialLayoutMode = PuzzleInitialLayoutMode.Random;
            config.sourceSprite = source;
            config.presetArrangement = Array.Empty<int>();
            return config;
        }

        private void OnValidate()
        {
            rows = Mathf.Max(2, rows);
            columns = Mathf.Max(2, columns);
            minMisplacedCount = Mathf.Max(1, minMisplacedCount);
        }

        private static bool IsValidPermutation(IReadOnlyList<int> values, int expectedCount)
        {
            if (values.Count != expectedCount)
            {
                return false;
            }

            HashSet<int> seen = new HashSet<int>();
            for (int index = 0; index < expectedCount; index++)
            {
                int value = values[index];
                if (value < 0 || value >= expectedCount || !seen.Add(value))
                {
                    return false;
                }
            }

            return true;
        }

        private static int[] CreateIdentity(int count)
        {
            int[] values = new int[count];
            for (int index = 0; index < count; index++)
            {
                values[index] = index;
            }

            return values;
        }

        private static void Shuffle(int[] values, System.Random random)
        {
            for (int index = values.Length - 1; index > 0; index--)
            {
                int swapIndex = random.Next(index + 1);
                (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
            }
        }

        private static int CountMisplaced(IReadOnlyList<int> arrangement)
        {
            int misplaced = 0;
            for (int index = 0; index < arrangement.Count; index++)
            {
                if (arrangement[index] != index)
                {
                    misplaced++;
                }
            }

            return misplaced;
        }
    }
}
