using System.Reflection;
using HarmonyLib;

namespace ChainTeams
{
    /// <summary>
    /// 分组面板打开期间，把游戏自己的 GUI 系统切到"窗口模式"：
    /// windowShowingCursor 让 CursorHandler 解锁并显示鼠标；
    /// windowBlockingInput 让 Character.CanDoInput() 返回 false，
    /// 输入采样会被清零，因此走路/视角都不会响应。面板关闭后标志自动复原。
    /// </summary>
    [HarmonyPatch(typeof(GUIManager), "UpdateWindowStatus")]
    public static class GuiWindowStatusPatch
    {
        private static MethodInfo _showSetter;
        private static MethodInfo _blockSetter;

        private static void Postfix(GUIManager __instance)
        {
            var ui = ChainManager.Instance?.UI;
            if (ui == null || !ui.IsOpen) return;

            if (_showSetter == null)
            {
                _showSetter = AccessTools.PropertySetter(typeof(GUIManager), "windowShowingCursor");
                _blockSetter = AccessTools.PropertySetter(typeof(GUIManager), "windowBlockingInput");
            }
            _showSetter?.Invoke(__instance, new object[] { true });
            _blockSetter?.Invoke(__instance, new object[] { true });
        }
    }
}
