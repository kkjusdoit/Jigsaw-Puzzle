using JigsawPuzzle.Puzzle.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace JigsawPuzzle.Puzzle.Runtime
{
    public sealed class PuzzleInputController : MonoBehaviour
    {
        [SerializeField] private PuzzleGameController gameController;

        private int activePointerId = int.MinValue;
        private bool isDragging;

        private void Awake()
        {
            if (gameController == null)
            {
                gameController = GetComponent<PuzzleGameController>();
            }
        }

        private void Update()
        {
            if (gameController == null || !gameController.CanInteract)
            {
                return;
            }

            if (TryGetTouchPointer(out int touchId, out Vector2 touchPosition, out bool touchDown, out bool touchHeld, out bool touchUp))
            {
                HandlePointer(touchId, touchPosition, touchDown, touchHeld, touchUp);
                return;
            }

            if (Mouse.current == null)
            {
                return;
            }

            HandlePointer(
                -1,
                Mouse.current.position.ReadValue(),
                Mouse.current.leftButton.wasPressedThisFrame,
                Mouse.current.leftButton.isPressed,
                Mouse.current.leftButton.wasReleasedThisFrame);
        }

        private void HandlePointer(int pointerId, Vector2 screenPosition, bool pointerDown, bool pointerHeld, bool pointerUp)
        {
            Vector3 worldPosition = gameController.WorldCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -gameController.WorldCamera.transform.position.z));

            if (pointerDown)
            {
                Collider2D hit = Physics2D.OverlapPoint(worldPosition);
                if (hit != null && hit.TryGetComponent(out PuzzlePieceView pieceView))
                {
                    isDragging = gameController.TryBeginDrag(pieceView.PieceId, worldPosition);
                    activePointerId = isDragging ? pointerId : int.MinValue;
                }
            }

            if (pointerHeld && isDragging && activePointerId == pointerId)
            {
                gameController.UpdateDrag(worldPosition);
            }

            if (pointerUp && isDragging && activePointerId == pointerId)
            {
                gameController.EndDrag(worldPosition);
                isDragging = false;
                activePointerId = int.MinValue;
            }
        }

        private static bool TryGetTouchPointer(out int pointerId, out Vector2 screenPosition, out bool pointerDown, out bool pointerHeld, out bool pointerUp)
        {
            pointerId = int.MinValue;
            screenPosition = default;
            pointerDown = false;
            pointerHeld = false;
            pointerUp = false;

            if (Touchscreen.current == null || Touchscreen.current.touches.Count == 0)
            {
                return false;
            }

            foreach (TouchControl touch in Touchscreen.current.touches)
            {
                if (!touch.press.isPressed &&
                    !touch.press.wasPressedThisFrame &&
                    !touch.press.wasReleasedThisFrame)
                {
                    continue;
                }

                pointerId = touch.touchId.ReadValue();
                screenPosition = touch.position.ReadValue();
                pointerDown = touch.press.wasPressedThisFrame;
                pointerHeld = touch.press.isPressed;
                pointerUp = touch.press.wasReleasedThisFrame;
                return true;
            }

            return false;
        }
    }
}
