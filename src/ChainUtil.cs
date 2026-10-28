using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace ChainTeams
{
    public static class ChainUtil
    {
        public static readonly Color[] TeamColors =
        {
            new Color(0.62f, 0.62f, 0.62f), // 0: 未分组（灰）
            new Color(1.00f, 0.30f, 0.30f), // 1: 红
            new Color(0.30f, 0.55f, 1.00f), // 2: 蓝
            new Color(0.30f, 0.90f, 0.45f), // 3: 绿
            new Color(1.00f, 0.75f, 0.20f), // 4: 黄
            new Color(0.80f, 0.40f, 1.00f), // 5: 紫
            new Color(0.25f, 0.85f, 0.90f), // 6: 青
        };

        public static Color GroupColor(int slot)
        {
            return TeamColors[Mathf.Clamp(slot, 0, TeamColors.Length - 1)];
        }

        /// <summary>角色的 Photon 玩家编号；取不到返回 -1。用 photonView（Character.view 是私有的）。</summary>
        public static int ActorOf(Character c)
        {
            var view = c == null ? null : c.photonView;
            var owner = view == null ? null : view.Owner;
            return owner == null ? -1 : owner.ActorNumber;
        }

        /// <summary>
        /// 某组当前全部"可锁链"角色，按房主在 UI 里排定的链内顺序返回。
        /// </summary>
        public static List<Character> TeamMembers(int team)
        {
            var list = new List<Character>();
            if (!PhotonNetwork.InRoom) return list;
            if (team < 1 || team > TeamState.MaxSlot) return list;

            // actor -> 在场角色 映射
            var byActor = new Dictionary<int, Character>();
            foreach (var c in PlayerHandler.GetAllPlayerCharacters())
            {
                if (!Chainable(c)) continue;
                var actor = ActorOf(c);
                if (actor >= 0 && !byActor.ContainsKey(actor)) byActor[actor] = c;
            }

            foreach (var actor in TeamState.Teams[team])
            {
                if (byActor.TryGetValue(actor, out var c)) list.Add(c);
            }
            return list;
        }

        public static bool Chainable(Character c)
        {
            return c != null
                && !c.isBot
                && c.isActiveAndEnabled
                && !c.IsGhost
                && c.photonView != null
                && c.photonView.Owner != null;
        }

        // ---- 皮肤色（锁链渐变用） ----

        private static readonly Dictionary<Character, CharacterCustomization> CustomizationCache =
            new Dictionary<Character, CharacterCustomization>();

        /// <summary>角色的皮肤颜色（外观调色板色，本地/远端通用）。取不到时返回灰色。</summary>
        public static Color GetSkinColor(Character c)
        {
            if (c == null) return new Color(0.6f, 0.6f, 0.6f);
            try
            {
                if (!CustomizationCache.TryGetValue(c, out var cc) || cc == null)
                {
                    cc = c.refs != null ? c.refs.customization : null;
                    if (cc == null) cc = c.GetComponent<CharacterCustomization>();
                    if (cc == null) return new Color(0.6f, 0.6f, 0.6f);
                    if (CustomizationCache.Count > 64) SweepCustomizationCache();
                    CustomizationCache[c] = cc;
                }
                return cc.PlayerColor;
            }
            catch
            {
                return new Color(0.6f, 0.6f, 0.6f);
            }
        }

        private static void SweepCustomizationCache()
        {
            var dead = new List<Character>();
            foreach (var kv in CustomizationCache)
            {
                if (kv.Key == null || kv.Value == null) dead.Add(kv.Key);
            }
            foreach (var d in dead) CustomizationCache.Remove(d);
        }

        // ---- 胯部（Hip）查找与缓存，物理与视觉共用 ----

        private class HipInfo
        {
            public Transform Transform;
            public Rigidbody Rig;
        }

        private static readonly Dictionary<Character, HipInfo> HipCache = new Dictionary<Character, HipInfo>();

        public static Transform GetHip(Character c) => GetHipInfo(c)?.Transform;

        public static Rigidbody GetHipRig(Character c) => GetHipInfo(c)?.Rig;

        private static HipInfo GetHipInfo(Character c)
        {
            if (c == null) return null;
            if (HipCache.TryGetValue(c, out var info) && info != null && info.Transform != null) return info;

            var hip = FindHipRecursive(c.transform);
            if (hip == null)
            {
                HipCache.Remove(c);
                return null;
            }
            info = new HipInfo { Transform = hip, Rig = hip.GetComponent<Rigidbody>() };
            if (HipCache.Count > 64) SweepDeadEntries();
            HipCache[c] = info;
            return info;
        }

        private static Transform FindHipRecursive(Transform root)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == "Hip") return child;
                var found = FindHipRecursive(child);
                if (found != null) return found;
            }
            return null;
        }

        private static void SweepDeadEntries()
        {
            var dead = new List<Character>();
            foreach (var kv in HipCache)
            {
                if (kv.Key == null || kv.Value == null || kv.Value.Transform == null) dead.Add(kv.Key);
            }
            foreach (var d in dead) HipCache.Remove(d);
        }
    }
}
