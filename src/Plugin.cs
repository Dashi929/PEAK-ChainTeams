using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ChainTeams
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.peakmod.chainteams";
        public const string PluginName = "ChainTeams 组队锁链";
        public const string PluginVersion = "1.3.3";

        // GUID of the old single-chain mod; both mods pulling players at once feels broken.
        public const string LegacyChainedGuid = "com.github.Quaree.Chained";

        internal static ManualLogSource Log;

        internal static ConfigEntry<KeyCode> CfgUIHotkey;
        internal static ConfigEntry<int> CfgUISize;
        internal static ConfigEntry<int> CfgMaxPerTeam;
        internal static ConfigEntry<int> CfgMaxGroups;
        internal static ConfigEntry<float> CfgChainLength;
        internal static ConfigEntry<float> CfgPullStrength;
        internal static ConfigEntry<float> CfgMaxPullForce;
        internal static ConfigEntry<float> CfgChainWidth;
        internal static ConfigEntry<bool> CfgShowChains;

        private bool _pluginUpdateLogged;

        private void Update()
        {
            if (_pluginUpdateLogged) return;
            _pluginUpdateLogged = true;
            Plugin.Log.LogInfo("Plugin.Update heartbeat OK (plugin MonoBehaviour is running)");
        }

        private void Awake()
        {
            Log = Logger;

            CfgUIHotkey = Config.Bind("General", "UIHotkey", KeyCode.F9, "打开/关闭分组界面的按键");
            CfgUISize = Config.Bind("General", "PanelSize", 66,
                new ConfigDescription("面板占屏幕的百分比（宽和高同比例）", new AcceptableValueRange<int>(30, 95)));
            CfgMaxPerTeam = Config.Bind("General", "MaxPerTeam", 6,
                new ConfigDescription("每组最多人数（房主端生效，上限 6）", new AcceptableValueRange<int>(1, 6)));
            CfgMaxGroups = Config.Bind("General", "MaxGroups", 6,
                new ConfigDescription("最多分组数（房主端生效，上限 6）", new AcceptableValueRange<int>(1, 6)));
            CfgChainLength = Config.Bind("Physics", "ChainLength", 5f,
                new ConfigDescription("锁链自然长度（米），超过后开始互相拉扯", new AcceptableValueRange<float>(1f, 20f)));
            CfgPullStrength = Config.Bind("Physics", "PullStrength", 25f,
                new ConfigDescription("拉扯强度", new AcceptableValueRange<float>(1f, 200f)));
            CfgMaxPullForce = Config.Bind("Physics", "MaxPullForce", 350f,
                new ConfigDescription("单次拉扯的最大力度", new AcceptableValueRange<float>(10f, 2000f)));
            CfgChainWidth = Config.Bind("Visual", "ChainWidth", 0.09f,
                new ConfigDescription("锁链视觉粗细", new AcceptableValueRange<float>(0.01f, 0.5f)));
            CfgShowChains = Config.Bind("Visual", "ShowChains", true, "是否绘制锁链（纯本地视觉）");

            var go = new GameObject("ChainTeams.Manager");
            go.hideFlags = HideFlags.DontSave;
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ChainManager>();

            new Harmony(PluginGuid).PatchAll(typeof(GuiWindowStatusPatch));

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded");
        }

        internal static int MaxPerTeam => CfgMaxPerTeam.Value;
    }
}
