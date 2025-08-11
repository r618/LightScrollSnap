using UnityEngine.UIElements;

namespace LightScrollSnap
{
    public static class InputHelper
    {
        public static bool MouseButtonPressed(MouseButton button)
        {
#if ENABLE_INPUT_SYSTEM
            // New Input System
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null) {
                switch (button) {
                    case MouseButton.LeftMouse:
                        return mouse.leftButton.isPressed;
                    case MouseButton.RightMouse:
                        return mouse.rightButton.isPressed;
                    case MouseButton.MiddleMouse:
                        return mouse.middleButton.isPressed;
                }

                return false;
            }

            // No mouse device (e.g., phone/tablet). Treat any touch as left mouse.
            var touchscreen = UnityEngine.InputSystem.Touchscreen.current;
            if (touchscreen != null) {
                if (button == MouseButton.LeftMouse) {
                    // Any finger currently pressed?
                    for (var i = 0; i < touchscreen.touches.Count; i++) {
                        if (touchscreen.touches[i].press.isPressed) {
                            return true;
                        }
                    }
                }

                return false; // right/ middle don't exist on touch.
            }

            // Generic pointer fallback (e.g., pen)
            var pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer != null && button == MouseButton.LeftMouse) {
                return pointer.press.isPressed;
            }

            return false;

#elif ENABLE_LEGACY_INPUT_MANAGER
            // Old (legacy) Input Manager
            switch (button) {
                case MouseButton.LeftMouse:
                    // On mobile, GetMouseButton(0) is false; map touch to left click.
                    return UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.touchCount > 0;
                case MouseButton.RightMouse:
                    return UnityEngine.Input.GetMouseButton(1);
                case MouseButton.MiddleMouse:
                    return UnityEngine.Input.GetMouseButton(2);
                default:
                    return false;
            }
#else
            // Neither system compiled in
            return false;
#endif
        }
    }
}