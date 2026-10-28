using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;

namespace ChainTeams
{
    /// <summary>
    /// 分组状态，通过 Photon 房间自定义属性同步（自动覆盖后加入的玩家，房主为唯一写入者）。
    /// 属性键刻意用 "CT#" 前缀避免和游戏自己的房间属性冲突。
    ///
    /// 数据模型（自由分组，最多 6 组，按 1..GroupCount 连续编号）：
    /// - CT#Teams  : int[] [actor, 组槽, ...]，同一组内的先后顺序即链内连接顺序
    /// - CT#Names  : string[]，长度即组数（允许空组），元素为组名（空 = 默认 "组N"）
    /// - CT#Params : float[] {链长, 拉扯强度, 最大拉力}，房主在 UI 里实时调节，全房间同步
    /// 旧版（固定 3 组、无组名参数）的数据可直接读取，槽位 ≤3 自然兼容。
    /// </summary>
    public static class TeamState
    {
        public const string PropEnabled = "CT#Enabled";
        public const string PropTeams = "CT#Teams";
        public const string PropNames = "CT#Names";
        public const string PropParams = "CT#Params";

        public const int MaxSlot = 6;

        /// <summary>锁链系统是否启用。</summary>
        public static bool Enabled;

        /// <summary>Teams[槽位] = 该组有序的玩家编号列表（索引 0 = 链头），槽位 1..GroupCount 连续有效。</summary>
        public static readonly List<int>[] Teams = new List<int>[MaxSlot + 1];

        /// <summary>组名（1 起下标）；空串 = 使用默认名 "组N"。</summary>
        public static readonly string[] Names = new string[MaxSlot + 1];

        /// <summary>当前组数（0..MaxSlot），由房主添加/删除分组时显式变更。</summary>
        public static int GroupCount { get; private set; }

        // ---- 锁链参数（房间同步；无数据时用本地配置默认值） ----
        public static float ChainLength = 5f;
        public static float PullStrength = 25f;
        public static float MaxPullForce = 350f;

        static TeamState()
        {
            for (var i = 0; i <= MaxSlot; i++) Teams[i] = new List<int>();
        }

        public static int MaxGroups => Mathf.Clamp(Plugin.CfgMaxGroups.Value, 1, MaxSlot);

        public static string GroupName(int slot)
        {
            if (slot < 1 || slot > MaxSlot) return "?";
            var n = Names[slot];
            return string.IsNullOrEmpty(n) ? $"组{slot}" : n;
        }

        public static int GetTeam(int actor)
        {
            for (var t = 1; t <= GroupCount; t++)
            {
                if (Teams[t].Contains(actor)) return t;
            }
            return 0;
        }

        public static int CountInTeam(int slot)
        {
            return slot >= 1 && slot <= MaxSlot ? Teams[slot].Count : 0;
        }

        public static List<int>[] CopyTeams()
        {
            var copy = new List<int>[MaxSlot + 1];
            for (var i = 0; i <= MaxSlot; i++) copy[i] = new List<int>(Teams[i]);
            return copy;
        }

        public static string[] CopyNames()
        {
            return (string[])Names.Clone();
        }

        /// <summary>从当前房间属性读取全量状态（进房/属性变更/迟到加入时调用）。</summary>
        public static void ApplyFromRoom()
        {
            ResetLocal();

            if (!PhotonNetwork.InRoom) return;
            // 注：模拟测试模式已在 1.3.2 移除

            var props = PhotonNetwork.CurrentRoom.CustomProperties;
            Enabled = props.TryGetValue(PropEnabled, out var e) && e is bool b && b;

            // 分组与顺序
            if (props.TryGetValue(PropTeams, out var raw) && raw is int[] pairs)
            {
                var seen = new HashSet<int>();
                for (var i = 0; i + 1 < pairs.Length; i += 2)
                {
                    var actor = pairs[i];
                    var slot = pairs[i + 1];
                    if (slot < 1 || slot > MaxSlot || !seen.Add(actor)) continue;
                    Teams[slot].Add(actor);
                }
            }

            // 组名（长度即组数，优先于按成员推导）
            if (props.TryGetValue(PropNames, out var rawNames) && rawNames is string[] nameArr)
            {
                GroupCount = Mathf.Clamp(nameArr.Length, 0, MaxGroups);
                for (var i = 0; i < nameArr.Length && i < MaxSlot; i++)
                {
                    Names[i + 1] = Sanitize(nameArr[i]);
                }
            }
            else
            {
                for (var t = MaxSlot; t >= 1; t--)
                {
                    if (Teams[t].Count > 0) { GroupCount = Mathf.Clamp(t, 0, MaxGroups); break; }
                }
            }

            // 锁链参数
            if (props.TryGetValue(PropParams, out var rawP) && rawP is float[] p && p.Length >= 3)
            {
                ChainLength = Mathf.Clamp(p[0], 0.5f, 40f);
                PullStrength = Mathf.Clamp(p[1], 1f, 500f);
                MaxPullForce = Mathf.Clamp(p[2], 10f, 4000f);
            }
        }

        /// <summary>本地重置（不清房间属性）。参数回落到本地配置默认值。</summary>
        public static void ResetLocal()
        {
            Enabled = false;
            GroupCount = 0;
            for (var i = 0; i <= MaxSlot; i++)
            {
                Teams[i].Clear();
                Names[i] = "";
            }
            if (Plugin.CfgChainLength != null)
            {
                ChainLength = Plugin.CfgChainLength.Value;
                PullStrength = Plugin.CfgPullStrength.Value;
                MaxPullForce = Plugin.CfgMaxPullForce.Value;
            }
        }

        /// <summary>离开房间时本地清空（房间属性保留给还在房里的人）。</summary>
        public static void ClearLocal()
        {
            ResetLocal();
        }

        public static void OnRoomPropertiesChanged(Hashtable changed)
        {
            if (changed == null) return;
            if (changed.ContainsKey(PropEnabled) || changed.ContainsKey(PropTeams) ||
                changed.ContainsKey(PropNames) || changed.ContainsKey(PropParams))
            {
                ApplyFromRoom();
            }
        }

        /// <summary>
        /// 房主专用：发布新的分组状态（组数/组名/成员与顺序）。剔除已离房玩家。
        /// 乐观地同时更新本地状态，让 UI 立即反映。模拟模式下只写本地。
        /// </summary>
        public static void Publish(bool enabled, int groupCount, List<int>[] teams, string[] names)
        {
            var count = Mathf.Clamp(groupCount, 0, MaxGroups);

            var cleaned = new List<int>[MaxSlot + 1];
            for (var i = 0; i <= MaxSlot; i++) cleaned[i] = new List<int>();
            for (var t = 1; t <= count; t++) cleaned[t].AddRange(teams[t]);

            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;

            var present = new HashSet<int>(
                PhotonNetwork.PlayerList.Where(p => !p.IsInactive).Select(p => p.ActorNumber));
            for (var t = 1; t <= count; t++)
            {
                cleaned[t].RemoveAll(a => !present.Contains(a));
            }

            var ht = new Hashtable
            {
                { PropEnabled, enabled },
                { PropTeams, Encode(cleaned, count) },
                { PropNames, EncodeNames(names, count) },
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(ht);
            ApplyLocal(enabled, count, cleaned, names);
        }

        /// <summary>房主专用：只发布锁链参数（滑条调节时高频调用，走小包）。</summary>
        public static void PublishParams()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
            var ht = new Hashtable
            {
                { PropParams, new[] { ChainLength, PullStrength, MaxPullForce } },
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(ht);
        }

        /// <summary>把状态写入本地缓存（Publish 的本地落地，也供模拟模式直接使用）。</summary>
        public static void ApplyLocal(bool enabled, int groupCount, List<int>[] teams, string[] names)
        {
            Enabled = enabled;
            GroupCount = Mathf.Clamp(groupCount, 0, MaxGroups);
            for (var t = 1; t <= MaxSlot; t++)
            {
                Teams[t].Clear();
                if (t <= GroupCount) Teams[t].AddRange(teams[t]);
                Names[t] = Sanitize(names != null && t < names.Length ? names[t] : "");
            }
        }

        /// <summary>房主侧：玩家离房时清掉他的分组条目并同步。</summary>
        public static void HostRemoveActor(int actor)
        {
            if (!PhotonNetwork.IsMasterClient || GetTeam(actor) == 0) return;
            var copy = CopyTeams();
            for (var t = 1; t <= MaxSlot; t++) copy[t].Remove(actor);
            Publish(Enabled, GroupCount, copy, CopyNames());
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
        }

        private static int[] Encode(List<int>[] teams, int count)
        {
            var total = 0;
            for (var t = 1; t <= count; t++) total += teams[t].Count;
            var arr = new int[total * 2];
            var i = 0;
            for (var t = 1; t <= count; t++)
            {
                foreach (var actor in teams[t])
                {
                    arr[i++] = actor;
                    arr[i++] = t;
                }
            }
            return arr;
        }

        private static string[] EncodeNames(string[] names, int count)
        {
            var arr = new string[Mathf.Max(count, 0)];
            for (var t = 1; t <= count; t++)
            {
                arr[t - 1] = Sanitize(names != null && t < names.Length ? names[t] : "");
            }
            return arr;
        }
    }
}
