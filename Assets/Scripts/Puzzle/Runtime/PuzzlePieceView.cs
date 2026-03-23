using UnityEngine;

namespace JigsawPuzzle.Puzzle.Runtime
{
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class PuzzlePieceView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private BoxCollider2D boxCollider;

        private TextMesh indexText;
        private Vector3 defaultScale;

        public int PieceId { get; private set; }

        public static PuzzlePieceView Create(Transform parent, string name)
        {
            GameObject pieceObject = new GameObject(name);
            pieceObject.transform.SetParent(parent, false);
            PuzzlePieceView view = pieceObject.AddComponent<PuzzlePieceView>();
            view.spriteRenderer = pieceObject.GetComponent<SpriteRenderer>();
            view.boxCollider = pieceObject.GetComponent<BoxCollider2D>();
            return view;
        }

        public void Initialize(int pieceId, Sprite sprite, Vector2 targetWorldSize, bool showIndex)
        {
            PieceId = pieceId;
            spriteRenderer.sprite = sprite;
            spriteRenderer.color = Color.white;
            spriteRenderer.sortingOrder = 10;

            Vector2 spriteSize = sprite.bounds.size;
            transform.localScale = new Vector3(
                targetWorldSize.x / spriteSize.x,
                targetWorldSize.y / spriteSize.y,
                1f);
            defaultScale = transform.localScale;

            boxCollider.size = sprite.bounds.size;
            boxCollider.offset = sprite.bounds.center;

            if (showIndex)
            {
                indexText = CreateIndexText();
                indexText.text = (pieceId + 1).ToString();
            }
        }

        public void SetWorldPosition(Vector3 worldPosition)
        {
            transform.position = worldPosition;
        }

        public void SetSelected(bool isSelected)
        {
            spriteRenderer.sortingOrder = isSelected ? 100 : 10;
            transform.localScale = isSelected ? defaultScale * 1.03f : defaultScale;
            spriteRenderer.color = isSelected ? new Color(1f, 0.97f, 0.85f, 1f) : Color.white;

            if (indexText != null)
            {
                MeshRenderer textRenderer = indexText.GetComponent<MeshRenderer>();
                textRenderer.sortingOrder = isSelected ? 101 : 11;
            }
        }

        private TextMesh CreateIndexText()
        {
            GameObject textObject = new GameObject("Index");
            textObject.transform.SetParent(transform, false);
            textObject.transform.localScale = Vector3.one * 0.12f;

            TextMesh textMesh = textObject.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.LowerRight;
            textMesh.alignment = TextAlignment.Right;
            textMesh.characterSize = 0.12f;
            textMesh.fontSize = 48;
            textMesh.color = new Color(0.08f, 0.12f, 0.16f, 0.82f);
            textMesh.text = string.Empty;

            Bounds bounds = boxCollider.bounds;
            Vector3 worldBottomRight = new Vector3(
                bounds.max.x - 0.08f,
                bounds.min.y + 0.06f,
                transform.position.z - 0.1f);
            textObject.transform.position = worldBottomRight;

            MeshRenderer textRenderer = textObject.GetComponent<MeshRenderer>();
            textRenderer.sortingOrder = 11;
            return textMesh;
        }

        private void Reset()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            boxCollider = GetComponent<BoxCollider2D>();
        }
    }
}
