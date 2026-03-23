using UnityEngine;

namespace JigsawPuzzle.Puzzle.Core
{
    public sealed class PuzzleSpriteSlicer
    {
        public Sprite CreateSlice(Sprite source, int rows, int columns, int row, int column)
        {
            Rect sourceRect = source.rect;
            float sliceWidth = sourceRect.width / columns;
            float sliceHeight = sourceRect.height / rows;

            Rect sliceRect = new Rect(
                sourceRect.x + column * sliceWidth,
                sourceRect.y + (rows - row - 1) * sliceHeight,
                sliceWidth,
                sliceHeight);

            Vector2 pivot = new Vector2(0.5f, 0.5f);
            return Sprite.Create(source.texture, sliceRect, pivot, source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }
}
