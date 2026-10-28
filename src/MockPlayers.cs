using System.Collections.Generic;
using UnityEngine;

namespace ChainTeams
{
    /// <summary>
    /// 本地模拟组员测试模式：不进联机房也能测试分组界面、拖拽排序、智能分组等。
    /// 启用时注入一组假玩家并预置分组（红组 3 人、蓝组 2 人、其余未分组），
    /// TeamState 的所有修改只保留在本地（不写 Photon 房间属性）。
    /// 再次切换可恢复现场（清空本地状态，在房则重新读房间属性）。
    /// </summary>
    public static class MockPlayers
    {
        public struct MockEntry
        {
            public string Name;
            public int Actor;

            public MockEntry(string name, int actor)
            {
                Name = name;
                Actor = actor;
            }
        }

        public static bool Enabled { get; private set; }

        public static readonly List<MockEntry> Fixed = new List<MockEntry>
        {
            new MockEntry("测试·阿伟", 101),
            new MockEntry("测试·小明", 102),
            new MockEntry("测试·小红", 103),
            new MockEntry("测试·老王", 104),
            new MockEntry("测试·珍妮", 105),
            new MockEntry("测试·大熊", 106),
            new MockEntry("测试·汤姆", 107),
            new MockEntry("测试·妮娜", 108),
        };

        public static void Toggle()
        {
            Enabled = !Enabled;
            if (Enabled)
            {
                Seed();
                Plugin.Log.LogInfo($"[MockPlayers] 模拟测试模式已开启：{Fixed.Count} 名模拟组员，组1 3 人 / 组2 2 人 / 未分组 3 人");
            }
            else
            {
                TeamState.ClearLocal();
                if (Photon.Pun.PhotonNetwork.InRoom) TeamState.ApplyFromRoom();
                Plugin.Log.LogInfo("[MockPlayers] 模拟测试模式已关闭，恢复真实房间状态");
            }
        }

        private static void Seed()
        {
            var teams = new List<int>[TeamState.MaxSlot + 1];
            var names = new string[TeamState.MaxSlot + 1];
            for (var i = 0; i <= TeamState.MaxSlot; i++)
            {
                teams[i] = new List<int>();
                names[i] = "";
            }
            teams[1].AddRange(new[] { 101, 102, 103 }); // 组1：链头 阿伟 → 小明 → 小红 链尾
            teams[2].AddRange(new[] { 104, 105 });      // 组2：老王 → 珍妮
            names[1] = "红组";
            names[2] = "蓝组";
            TeamState.ApplyLocal(true, 2, teams, names);
        }
    }
}
