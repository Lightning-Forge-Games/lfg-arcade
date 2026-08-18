using UnityEngine;
using UnityEngine.InputSystem;

namespace LightningForge.Chess.Game
{
    /// <summary>
    /// Feeds pointer presses to the controller. Kept separate so the game logic itself
    /// stays free of any input dependency and remains testable.
    /// </summary>
    [RequireComponent(typeof(ChessGameController))]
    public class ChessPointerInput : MonoBehaviour
    {
        [SerializeField] ChessGameController controller;
        [SerializeField] Camera targetCamera;

        void Reset()
        {
            controller = GetComponent<ChessGameController>();
            targetCamera = Camera.main;
        }

        void Awake()
        {
            if (controller == null) controller = GetComponent<ChessGameController>();
            if (targetCamera == null) targetCamera = Camera.main;
        }

        void Update()
        {
            if (controller == null) return;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                controller.HandlePointer(mouse.position.ReadValue(), targetCamera);
                return;
            }

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                controller.HandlePointer(touchscreen.primaryTouch.position.ReadValue(), targetCamera);
            }
        }
    }
}
