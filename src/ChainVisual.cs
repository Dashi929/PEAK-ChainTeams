using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace ChainTeams
{
    /// <summary>
    /// 锁链渲染。纯本地视觉：每个客户端把所有组的相邻角色两两连线。
    /// 每段线用两端角色的皮肤色做渐变（A 的肤色 → B 的肤色），
    /// 角色位置本身就是同步的，所以所有人看到的线一致，无需额外网络同步。
    /// </summary>
    public class ChainVisual : MonoBehaviour
    {
        private Material _lineMaterial;
        private readonly List<Segment> _pool = new List<Segment>();

        private class Segment
        {
            public LineRenderer Line;
            public Gradient Gradient = new Gradient();
            public GradientColorKey[] ColorKeys =
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            };
            public GradientAlphaKey[] AlphaKeys =
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f),
            };
        }

        private void LateUpdate()
        {
            if (!Plugin.CfgShowChains.Value || !TeamState.Enabled || !PhotonNetwork.InRoom)
            {
                SetActiveCount(0);
                return;
            }

            var used = 0;
            for (var team = 1; team <= TeamState.GroupCount; team++)
            {
                var members = ChainUtil.TeamMembers(team);
                if (members.Count < 2) continue;

                for (var i = 0; i + 1 < members.Count; i++)
                {
                    var hipA = ChainUtil.GetHip(members[i]);
                    var hipB = ChainUtil.GetHip(members[i + 1]);
                    if (hipA == null || hipB == null) continue;

                    var seg = GetSegment(used++);
                    seg.ColorKeys[0].color = ChainUtil.GetSkinColor(members[i]);
                    seg.ColorKeys[1].color = ChainUtil.GetSkinColor(members[i + 1]);
                    // Unity 6 的 SetKeys 只剩 Span 重载，net472 下编译不过，用属性赋值等效替代
                    seg.Gradient.colorKeys = seg.ColorKeys;
                    seg.Gradient.alphaKeys = seg.AlphaKeys;
                    seg.Line.colorGradient = seg.Gradient;

                    seg.Line.SetPosition(0, hipA.position);
                    seg.Line.SetPosition(1, hipB.position);
                }
            }

            SetActiveCount(used);
        }

        private Segment GetSegment(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject("ChainTeams.Line");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                if (_lineMaterial == null)
                {
                    _lineMaterial = new Material(Shader.Find("Sprites/Default"));
                }
                line.material = _lineMaterial;
                line.positionCount = 2;
                line.useWorldSpace = true;
                var width = Plugin.CfgChainWidth.Value;
                line.startWidth = width;
                line.endWidth = width;
                line.enabled = false;
                _pool.Add(new Segment { Line = line });
            }
            return _pool[index];
        }

        private void SetActiveCount(int count)
        {
            for (var i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null && _pool[i].Line != null) _pool[i].Line.enabled = i < count;
            }
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null) Destroy(_lineMaterial);
        }
    }
}
