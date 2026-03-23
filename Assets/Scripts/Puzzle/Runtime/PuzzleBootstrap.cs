using JigsawPuzzle.Puzzle.Core;
using UnityEngine;

namespace JigsawPuzzle.Puzzle.Runtime
{
    public static class PuzzleBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Object.FindFirstObjectByType<PuzzleGameController>() != null)
            {
                return;
            }

            GameObject root = new GameObject("PuzzleRuntime");
            root.AddComponent<PuzzleGameController>();
            root.AddComponent<PuzzleInputController>();
        }
    }
}
