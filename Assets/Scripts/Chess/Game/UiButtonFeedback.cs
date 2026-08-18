using UnityEngine;
using UnityEngine.UIElements;

namespace LightningForge.Chess.Game
{
    /// <summary>
    /// Gives a button hover and press states.
    ///
    /// The UI is built in code rather than from USS, so there are no :hover or :active
    /// pseudo classes to lean on and the states have to be wired by hand. Without them a
    /// button gives no sign it is interactive, and on a slow connection a player cannot
    /// tell whether their click registered.
    ///
    /// Pointer events rather than mouse events, so a touch press reacts the same way.
    /// </summary>
    public static class UiButtonFeedback
    {
        static readonly Color Normal = new Color(0.12f, 0.11f, 0.11f, 0.90f);
        static readonly Color Hover = new Color(0.20f, 0.18f, 0.15f, 0.96f);
        static readonly Color Pressed = new Color(0.30f, 0.22f, 0.12f, 1f);

        static readonly Color BorderNormal = new Color(0.42f, 0.33f, 0.22f, 1f);
        static readonly Color BorderHover = new Color(0.74f, 0.58f, 0.33f, 1f);

        public static void Apply(Button button)
        {
            if (button == null) return;

            button.style.backgroundColor = Normal;
            SetBorder(button, BorderNormal);

            bool held = false;

            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                button.style.backgroundColor = held ? Pressed : Hover;
                SetBorder(button, BorderHover);
            });

            button.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                button.style.backgroundColor = Normal;
                SetBorder(button, BorderNormal);
            });

            button.RegisterCallback<PointerDownEvent>(_ =>
            {
                held = true;
                button.style.backgroundColor = Pressed;
                SetBorder(button, BorderHover);
            });

            // Released anywhere: a press that drags off the button must not stay lit.
            button.RegisterCallback<PointerUpEvent>(evt =>
            {
                held = false;
                bool inside = button.worldBound.Contains(evt.position);
                button.style.backgroundColor = inside ? Hover : Normal;
                SetBorder(button, inside ? BorderHover : BorderNormal);
            });
        }

        static void SetBorder(VisualElement element, Color color)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }
    }
}
