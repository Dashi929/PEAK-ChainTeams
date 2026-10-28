using System;

namespace ChainTeams
{
    /// <summary>
    /// 双模式按键读取。
    /// 游戏更新后 Active Input Handling 可能切到"仅新 Input System"，
    /// 此时旧版 UnityEngine.Input 的所有读取会直接抛异常
    /// （表现为所有仍用旧输入的 mod 热键集体失效）。
    /// 这里优先尝试旧输入，一旦抛异常就锁定切换到新 Input System，并记录一条日志。
    /// </summary>
    public static class InputHelper
    {
        private static int _mode; // 0=未探测 1=旧输入可用 2=已切换到新 Input System
        private static bool _logged;

        public static bool KeyDown(UnityEngine.KeyCode code)
        {
            if (_mode != 2)
            {
                bool result;
                try
                {
                    result = UnityEngine.Input.GetKeyDown(code);
                    _mode = 1;
                    return result;
                }
                catch (Exception)
                {
                    SwitchedToNewInput();
                }
            }
            return NewKeyDown(code);
        }

        public static bool MouseButtonUp(int button)
        {
            if (_mode != 2)
            {
                try
                {
                    var result = UnityEngine.Input.GetMouseButtonUp(button);
                    _mode = 1;
                    return result;
                }
                catch (Exception)
                {
                    SwitchedToNewInput();
                }
            }
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return false;
            return button == 0 ? mouse.leftButton.wasReleasedThisFrame
                : button == 1 ? mouse.rightButton.wasReleasedThisFrame
                : button == 2 && mouse.middleButton.wasReleasedThisFrame;
        }

        private static void SwitchedToNewInput()
        {
            _mode = 2;
            if (_logged) return;
            _logged = true;
            Plugin.Log.LogWarning(
                "检测到旧版 Input API 不可用（游戏当前只启用了新 Input System，游戏更新所致）。" +
                "本 mod 已自动切换到 InputSystem 读键，功能不受影响；" +
                "其它仍用旧输入的热键类 mod 也会因此失效，需要各自的作者更新。");
        }

        private static bool NewKeyDown(UnityEngine.KeyCode code)
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return false;
            var key = MapKey(code);
            return key.HasValue && keyboard[key.Value].wasPressedThisFrame;
        }

        /// <summary>KeyCode 名字大多与 InputSystem.Key 一致（F9→F9、Escape→Escape），数字键做一次映射。</summary>
        private static UnityEngine.InputSystem.Key? MapKey(UnityEngine.KeyCode code)
        {
            var name = code.ToString();
            if (name.StartsWith("Alpha", StringComparison.Ordinal)) name = "Digit" + name.Substring(5);
            if (name.StartsWith("Keypad", StringComparison.Ordinal)) name = "Numpad" + name.Substring(6);
            return System.Enum.TryParse(name, out UnityEngine.InputSystem.Key key) && key != UnityEngine.InputSystem.Key.None
                ? key
                : (UnityEngine.InputSystem.Key?)null;
        }
    }
}
