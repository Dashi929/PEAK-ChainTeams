using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace ChainTeams
{
    /// <summary>
    /// 常驻管理器：本地角色的锁链物理 + Photon 房间属性监听 + UI 宿主。
    /// 物理模型与经过验证的单链 mod 相同：每个玩家只对自己刚体施力，
    /// 因此网络权威天然正确（每个角色的刚体由其所有者客户端模拟）。
    /// </summary>
    public class ChainManager : MonoBehaviour, IInRoomCallbacks, IMatchmakingCallbacks
    {
        public static ChainManager Instance { get; private set; }

        private bool _onGuiLogged;
        private float _lastKeyAction;
        internal bool SawMouseUpThisFrame;

        internal TeamUI UI { get; private set; }

        private void Awake()
        {
            Plugin.Log.LogInfo("ChainManager.Awake lifecycle OK");
            Instance = this;
            UI = new TeamUI(this);
            gameObject.AddComponent<ChainVisual>();
        }

        private void OnEnable()
        {
            Plugin.Log.LogInfo("ChainManager.OnEnable lifecycle OK");
            try
            {
                PhotonNetwork.AddCallbackTarget(this);
                TeamState.ApplyFromRoom();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"ChainManager.OnEnable exception: {e}");
            }
        }

        private void OnDisable()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
            TeamState.ClearLocal();
        }

        private bool _heartbeatLogged;

        private void Update()
        {
            try
            {
                if (!_heartbeatLogged)
                {
                    _heartbeatLogged = true;
                    Plugin.Log.LogInfo("ChainManager.Update 心跳正常（输入读取模式见后续日志）");
                }
                var now = Time.realtimeSinceStartup;
                if (now - _lastKeyAction > 0.4f)
                {
                    if (InputHelper.KeyDown(Plugin.CfgUIHotkey.Value)) { _lastKeyAction = now; UI.Toggle(); }
                    else if (InputHelper.KeyDown(Plugin.CfgMockHotkey.Value)) { _lastKeyAction = now; MockPlayers.Toggle(); }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"ChainManager.Update 异常: {e}");
            }
        }

        private void FixedUpdate()
        {
            if (!TeamState.Enabled || !PhotonNetwork.InRoom) return;

            var me = Character.localCharacter;
            if (!ChainUtil.Chainable(me)) return;

            var myTeam = TeamState.GetTeam(ChainUtil.ActorOf(me));
            if (myTeam <= 0) return;

            var members = ChainUtil.TeamMembers(myTeam);
            var idx = members.IndexOf(me);
            if (idx < 0) return;

            if (idx > 0) Pull(me, members[idx - 1]);
            if (idx < members.Count - 1) Pull(me, members[idx + 1]);
        }

        private static void Pull(Character me, Character other)
        {
            var myHip = ChainUtil.GetHip(me);
            var otherHip = ChainUtil.GetHip(other);
            var myRig = ChainUtil.GetHipRig(me);
            if (myHip == null || otherHip == null || myRig == null) return;

            var chainLength = TeamState.ChainLength;
            var distance = Vector3.Distance(myHip.position, otherHip.position);
            if (distance <= chainLength) return;

            var dir = (otherHip.position - myHip.position).normalized;
            var force = Mathf.Min((distance - chainLength) * TeamState.PullStrength, TeamState.MaxPullForce);
            myRig.AddForce(dir * force, ForceMode.Acceleration);
        }

        private void OnGUI()
        {
            if (!_onGuiLogged)
            {
                _onGuiLogged = true;
                Plugin.Log.LogInfo("ChainManager.OnGUI heartbeat OK (IMGUI loop is running)");
            }

            // 第三层按键兜底：直接用 IMGUI 键盘事件，完全不依赖 Update 与输入系统设置
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown && Time.realtimeSinceStartup - _lastKeyAction > 0.4f)
            {
                if (e.keyCode == Plugin.CfgUIHotkey.Value)
                {
                    _lastKeyAction = Time.realtimeSinceStartup;
                    UI.Toggle();
                    e.Use();
                }
                else if (e.keyCode == Plugin.CfgMockHotkey.Value)
                {
                    _lastKeyAction = Time.realtimeSinceStartup;
                    MockPlayers.Toggle();
                    e.Use();
                }
            }

            UI?.Draw();
        }

        // ---- IInRoomCallbacks ----

        public void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
        {
            // 新玩家进房时房间属性自动带全量分组状态，无需额外同步
        }

        public void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
        {
            TeamState.HostRemoveActor(otherPlayer.ActorNumber);
        }

        public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            TeamState.OnRoomPropertiesChanged(propertiesThatChanged);
        }

        public void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, Hashtable changedProps)
        {
        }

        public void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
        {
            // 房主掉线后新房主自动接管分组权限，UI 会自然切换为房主模式
        }

        // ---- IMatchmakingCallbacks ----

        public void OnJoinedRoom()
        {
            // 自己进房：房间属性里带全量分组状态，直接读取
            TeamState.ApplyFromRoom();
        }

        public void OnLeftRoom()
        {
            TeamState.ClearLocal();
        }

        public void OnCreatedRoom()
        {
        }

        public void OnCreateRoomFailed(short returnCode, string message)
        {
        }

        public void OnJoinRoomFailed(short returnCode, string message)
        {
        }

        public void OnJoinRandomFailed(short returnCode, string message)
        {
        }

        public void OnFriendListUpdate(List<FriendInfo> friendList)
        {
        }
    }
}
