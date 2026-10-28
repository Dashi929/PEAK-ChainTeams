using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChainTeams
{
    /// <summary>
    /// IMGUI 分组界面（仅联机进房后或模拟测试模式下可打开，主菜单按热键无反应）。
    /// 房主：自由添加/删除/重命名分组（最多 6 组）、把未分组玩家加入某组、
    /// 组员上移/下移调整链内顺序、移除组员、实时调节锁链参数（同步全房间）。
    /// 成员：只读查看。左上角另有小型状态 HUD。
    /// 面板默认占屏幕约 2/3（General.UISize 可调），内部所有尺寸按 600px 基准设计宽等比缩放。
    /// </summary>
    public class TeamUI
    {
        private const int WindowId = 73017;
        private const float DesignWidth = 600f;

        private readonly ChainManager _owner;
        private bool _open;
        private bool _positioned;
        private Rect _windowRect = new Rect(60, 60, 600, 300);
        private Vector2 _scroll;

        /// <summary>UI 缩放系数 = 面板宽 / 600 设计宽。</summary>
        private static float _scale = 1f;

        private static float W(float v) => v * _scale;


        // ---- 组名编辑草稿（槽位 -> 输入中内容；null = 未在编辑） ----
        private static readonly string[] NameDraft = new string[TeamState.MaxSlot + 1];

        // ---- 参数滑条节流发布 ----
        private static bool _paramsDirty;
        private static float _lastParamPublish;

        // ---- 玩家数据抽象：真实房间玩家 或 模拟测试玩家 ----

        public struct PlayerInfo
        {
            public string Name;
            public int Actor;
            public bool IsLocal;
            public bool IsHost;
        }

        /// <summary>当前可用于分组的玩家列表（模拟模式下返回假组员）。</summary>
        private static List<PlayerInfo> GetRoomPlayers()
        {
            return PhotonNetwork.PlayerList
                .Where(p => !p.IsInactive)
                .Select(p => new PlayerInfo
                {
                    Name = p.NickName,
                    Actor = p.ActorNumber,
                    IsLocal = p.IsLocal,
                    IsHost = p.IsMasterClient,
                })
                .ToList();
        }

        private static PlayerInfo? FindPlayer(int actor)
        {
            foreach (var p in GetRoomPlayers())
            {
                if (p.Actor == actor) return p;
            }
            return null;
        }

        // IMGUI 每帧会跑多个事件，样式必须缓存复用，避免每帧分配；比例变化时重建
        private static GUIStyle _hudStyle;
        private static GUIStyle _nameStyle;
        private static GUIStyle _idStyle;
        private static GUIStyle _statusStyle;
        private static GUIStyle _hintStyle;
        private static GUIStyle _warnStyle;
        private static GUIStyle _centerStyle;
        private static GUIStyle _headerStyle;
        private static float _stylesForScale = -1f;

        // 汇总条样式（组号颜色文字 + PEAK 奶油底 Box），随样式重建
        private static readonly GUIStyle[] _summaryStyles = new GUIStyle[TeamState.MaxSlot + 1];

        private static void EnsureStyles()
        {
            if (_hudStyle != null && Mathf.Abs(_stylesForScale - _scale) < 0.01f) return;
            var s = _scale;
            var body = Mathf.RoundToInt(13 * s);
            _hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _nameStyle = new GUIStyle(GUI.skin.label) { fontSize = body, alignment = TextAnchor.MiddleLeft, normal = { textColor = PeakStyle.Text } };
            _idStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(11 * s), alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.45f, 0.40f, 0.33f) } };
            _statusStyle = new GUIStyle(GUI.skin.label) { fontSize = body };
            _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(11 * s), normal = { textColor = new Color(0.50f, 0.44f, 0.36f) } };
            _warnStyle = new GUIStyle(GUI.skin.label) { fontSize = body, normal = { textColor = new Color(0.68f, 0.20f, 0.16f) } };
            _centerStyle = new GUIStyle(GUI.skin.label) { fontSize = body, alignment = TextAnchor.MiddleCenter };
            _headerStyle = new GUIStyle(GUI.skin.label) { fontSize = body, fontStyle = FontStyle.Bold, normal = { textColor = PeakStyle.Text } };

            for (var t = 1; t <= TeamState.MaxSlot; t++)
            {
                _summaryStyles[t] = new GUIStyle(GUI.skin.box)
                {
                    fontSize = body,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = GroupTextColor(t) },
                };
            }
            _stylesForScale = _scale;
        }

        private static void EnsureScaledSkin()
        {
            PeakStyle.Build(_scale);
        }

        /// <summary>组员文字用组色的加深版，保证奶油底上可读。</summary>
        private static Color GroupTextColor(int slot)
        {
            var c = ChainUtil.GroupColor(slot);
            return new Color(c.r * 0.72f + 0.06f, c.g * 0.72f + 0.06f, c.b * 0.72f + 0.06f, 1f);
        }

        public TeamUI(ChainManager owner)
        {
            _owner = owner;
        }

        public bool IsOpen => _open;

        /// <summary>面板只允许房主（或模拟测试模式）在联机房间内打开；主菜单、成员按热键均无反应。</summary>
        private static bool CanOpen => PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;

        // 0 = 分组管理页，1 = 设置页
        private int _page;

        public void Toggle()
        {
            if (!CanOpen)
            {
                _open = false;
                return;
            }
            _open = !_open;
            if (_open)
            {
                _bufInit = false; // 滑条缓冲按当前房间参数重新初始化
            }
            else
            {
                CommitNameDrafts();
                _positioned = false; // 下次打开按当前屏幕/配置重新计算尺寸
            }
        }

        public void Draw()
        {
            if (!_open)
            {
                DrawHud();
                return;
            }

            // 离开房间后面板自动收起（模拟模式除外）
            if (!CanOpen)
            {
                _open = false;
                return;
            }

            if (!_positioned)
            {
                var ratio = Mathf.Clamp(Plugin.CfgUISize.Value, 40, 95) / 100f;
                _windowRect = new Rect(
                    Screen.width * (1f - ratio) * 0.5f,
                    Screen.height * 0.10f,
                    Screen.width * ratio,
                    Screen.height * ratio);
                _scale = _windowRect.width / DesignWidth;
                // 硬上限：无论内容多高，窗口底部都不越过屏幕的 92%，彻底杜绝显示不全
                _windowRect.height = Mathf.Min(_windowRect.height, Screen.height * 0.92f - _windowRect.y);
                _positioned = true;
            }

            EnsureStyles();
            EnsureScaledSkin();
            var prevSkin = GUI.skin;
            if (PeakStyle.Skin != null) GUI.skin = PeakStyle.Skin;

            var hotkeyName = Plugin.CfgUIHotkey.Value.ToString();
            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow,
                $"组队锁链 v{Plugin.PluginVersion}   [{hotkeyName} 关闭]");

            GUI.skin = prevSkin;

            // 参数滑条节流发布（拖动中每 0.5 秒同步一次，避免刷屏房间属性）
            if (_paramsDirty && Time.realtimeSinceStartup - _lastParamPublish > 0.5f)
            {
                _paramsDirty = false;
                _lastParamPublish = Time.realtimeSinceStartup;
                TeamState.PublishParams();
            }
        }

        private static void DrawHud()
        {
            if (!TeamState.Enabled) return;

            var me = Character.localCharacter;
            if (me == null || me.IsGhost) return;

            var myTeam = TeamState.GetTeam(ChainUtil.ActorOf(me));
            string text;
            Color color;
            if (myTeam > 0)
            {
                text = $"锁链已启用 · {TeamState.GroupName(myTeam)} ({TeamState.CountInTeam(myTeam)}人)";
                color = ChainUtil.GroupColor(myTeam);
            }
            else
            {
                text = "锁链已启用 · 你未分组";
                color = ChainUtil.TeamColors[0];
            }

            EnsureStyles();
            _hudStyle.normal.textColor = color;
            GUI.Label(new Rect(14, 10, 420, 24), text, _hudStyle);
        }

        private void DrawWindow(int id)
        {
            EnsureStyles();
            GUILayout.BeginVertical();

            // 标签页：分组管理 / 设置
            GUILayout.BeginHorizontal();
            DrawTabButton("分组管理", 0);
            DrawTabButton("设置", 1);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(W(4));

            if (_page == 0) DrawGroupsPage();
            else DrawSettingsPage();

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, W(26)));

            // 回车提交组名编辑
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Return)
            {
                CommitNameDrafts();
                e.Use();
            }
        }

        private void DrawTabButton(string label, int page)
        {
            var active = _page == page;
            var style = PeakStyle.Button(active ? PeakStyle.Accent : PeakStyle.CreamDark);
            if (GUILayout.Button(label, style, GUILayout.Width(W(110)), GUILayout.Height(W(34))))
            {
                _page = page;
            }
        }

        // ---------------- 分组管理页 ----------------

        private void DrawGroupsPage()
        {
            DrawStatusRow();
            GUILayout.Space(W(4));

            DrawTeamSummary();
            GUILayout.Space(W(6));

            // 滚动区撑满面板剩余高度，让面板稳定占满设定的屏幕比例
            _scroll = GUILayout.BeginScrollView(_scroll, false, true,
                GUILayout.Height(Mathf.Max(W(180), _windowRect.height - W(285))));

            for (var slot = 1; slot <= TeamState.GroupCount; slot++) DrawGroupSection(slot);
            DrawAddGroupRow();
            GUILayout.Space(W(6));
            DrawUnassignedSection();

            GUILayout.EndScrollView();

            DrawHostActions();

            if (LegacyModDetected())
            {
                GUILayout.Space(W(4));
                GUILayout.Label("警告：检测到旧版 Chained mod（com.github.Quaree.Chained），两套锁链会同时拉扯，请删除它！", _warnStyle);
            }
        }

        // ---------------- 设置页 ----------------

        private void DrawSettingsPage()
        {
            _scroll = GUILayout.BeginScrollView(_scroll, false, true,
                GUILayout.Height(Mathf.Max(W(220), _windowRect.height - W(120))));

            GUILayout.BeginVertical(GUI.skin.box);
            _headerStyle.normal.textColor = PeakStyle.Text;
            GUILayout.Label("锁链参数", _headerStyle);
            GUILayout.Label("拖动即时生效，全房间同步（拖动中每 0.5 秒同步一次）。", _hintStyle);
            GUILayout.Space(W(4));
            ParamSlider("链长(米)", ref _lenBuf, 1f, 20f, v => TeamState.ChainLength = v, "{0:0.0}");
            ParamSlider("拉扯强度", ref _strBuf, 1f, 200f, v => TeamState.PullStrength = Mathf.Round(v), "{0:0}");
            ParamSlider("最大拉力", ref _maxBuf, 10f, 2000f, v => TeamState.MaxPullForce = Mathf.Round(v), "{0:0}");
            if (GUILayout.Button("恢复默认参数", PeakStyle.Button(PeakStyle.Accent), GUILayout.Width(W(170))))
            {
                _lenBuf = Plugin.CfgChainLength.Value;
                _strBuf = Plugin.CfgPullStrength.Value;
                _maxBuf = Plugin.CfgMaxPullForce.Value;
                TeamState.ChainLength = _lenBuf;
                TeamState.PullStrength = _strBuf;
                TeamState.MaxPullForce = _maxBuf;
                _paramsDirty = true;
            }
            GUILayout.EndVertical();

            GUILayout.Space(W(6));
            GUILayout.BeginVertical(GUI.skin.box);
            _headerStyle.normal.textColor = PeakStyle.Text;
            GUILayout.Label("当前规则", _headerStyle);
            GUILayout.Label($"最多 {TeamState.MaxGroups} 组、每组 {Plugin.MaxPerTeam} 人（可在 BepInEx 配置里调小）。");
            GUILayout.Label($"成员看到的参数与此一致：链长 {TeamState.ChainLength:0.0} 米 / 强度 {TeamState.PullStrength:0} / 最大拉力 {TeamState.MaxPullForce:0}。");
            GUILayout.EndVertical();

            GUILayout.EndScrollView();
        }

        private static void DrawStatusRow()
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label("锁链系统：", GUILayout.Width(W(64)));
            if (GUILayout.Button(TeamState.Enabled ? "● 已启用（点击停用）" : "○ 已停用（点击启用）",
                PeakStyle.Button(TeamState.Enabled ? PeakStyle.Good : PeakStyle.Bad), GUILayout.Width(W(190))))
            {
                TeamState.Publish(!TeamState.Enabled, TeamState.GroupCount, TeamState.CopyTeams(), TeamState.CopyNames());
            }

            GUILayout.FlexibleSpace();
            GUILayout.Space(W(8));
            GUILayout.Label($"成员 {GetRoomPlayers().Count} 人", GUILayout.Width(W(90)));
            GUILayout.EndHorizontal();
        }

        private static void DrawTeamSummary()
        {
            GUILayout.BeginHorizontal();
            if (TeamState.GroupCount == 0)
            {
                GUILayout.Label("还没有分组：点下方「＋添加分组」创建，再从「未分组」把玩家加进去。", _hintStyle);
            }
            for (var t = 1; t <= TeamState.GroupCount; t++)
            {
                GUILayout.Box($"  {t}·{TeamState.GroupName(t)}  {TeamState.CountInTeam(t)}/{Plugin.MaxPerTeam}  ", _summaryStyles[t], GUILayout.Height(W(30)));
                GUILayout.Space(W(6));
            }
            GUILayout.EndHorizontal();
        }

        // ---------------- 分组区块 ----------------

        private static void DrawGroupSection(int slot)
        {
            var color = ChainUtil.GroupColor(slot);
            var members = TeamState.Teams[slot]
                .Where(a => FindPlayer(a) != null)
                .ToList();

            GUILayout.BeginVertical(GUI.skin.box);

            // 组头：色点 + 组名（房主可编辑）+ 人数 + 删除组
            GUILayout.BeginHorizontal();
            _headerStyle.normal.textColor = GroupTextColor(slot);
            GUILayout.Label(slot.ToString(), _headerStyle, GUILayout.Width(W(30)));

            NameDraft[slot] = GUILayout.TextField(NameDraft[slot] ?? TeamState.Names[slot], GUILayout.Width(W(180)));
            if (GUILayout.Button("改名", PeakStyle.Button(PeakStyle.CreamDark), GUILayout.Width(W(56)))) CommitNameDrafts();

            GUILayout.FlexibleSpace();
            GUILayout.Label($"{members.Count}/{Plugin.MaxPerTeam}", _idStyle, GUILayout.Width(W(44)));
            if (GUILayout.Button("删除组", PeakStyle.Button(PeakStyle.Bad), GUILayout.Width(W(72)))) DeleteGroup(slot);
            GUILayout.EndHorizontal();

            foreach (var actor in members)
            {
                DrawMemberRow(actor, slot, members.Count);
            }

            if (members.Count == 0) GUILayout.Space(W(2));
            GUILayout.EndVertical();
        }

        /// <summary>组内一行：名字 + 编号 + 上移/下移/移除（链头在上）。</summary>
        private static void DrawMemberRow(int actor, int slot, int memberCount)
        {
            var player = FindPlayer(actor);
            if (player == null) return;

            var color = ChainUtil.GroupColor(slot);
            var order = TeamState.Teams[slot].IndexOf(actor);

            GUILayout.BeginHorizontal();
            GUILayout.Space(W(30));

            var suffix = player.Value.IsLocal ? "（你）" : "";
            if (memberCount > 1)
            {
                if (order == 0) suffix += "·链头";
                else if (order == memberCount - 1) suffix += "·链尾";
            }
            _nameStyle.normal.textColor = color;
            GUILayout.Label($"{player.Value.Name}{suffix}", _nameStyle, GUILayout.Width(W(230)));
            GUILayout.Label($"#{actor}", _idStyle, GUILayout.Width(W(46)));

            GUILayout.FlexibleSpace();

            var prevEnabled = GUI.enabled;
            var tanButton = PeakStyle.Button(PeakStyle.CreamDark);

            GUI.enabled = order > 0;
            if (GUILayout.Button("上移", tanButton, GUILayout.Width(W(60)))) MoveMember(slot, actor, -1);

            GUI.enabled = order >= 0 && order < memberCount - 1;
            if (GUILayout.Button("下移", tanButton, GUILayout.Width(W(60)))) MoveMember(slot, actor, +1);

            GUI.enabled = true;
            if (GUILayout.Button("移除", PeakStyle.Button(PeakStyle.Bad), GUILayout.Width(W(60)))) RemoveMember(slot, actor);

            GUI.enabled = prevEnabled;

            GUILayout.EndHorizontal();
        }

        private static void DrawAddGroupRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var canAdd = TeamState.GroupCount < TeamState.MaxGroups;
            var prevEnabled = GUI.enabled;
            GUI.enabled = canAdd;
            if (GUILayout.Button($"＋添加分组（{TeamState.GroupCount}/{TeamState.MaxGroups}）",
                PeakStyle.Button(PeakStyle.Good), GUILayout.Width(W(260)), GUILayout.Height(W(34))))
            {
                var teams = TeamState.CopyTeams();
                var names = TeamState.CopyNames();
                var count = TeamState.GroupCount + 1;
                names[count] = "";
                TeamState.Publish(TeamState.Enabled, count, teams, names);
            }
            GUI.enabled = prevEnabled;
            GUILayout.EndHorizontal();
        }

        private static void DrawUnassignedSection()
        {
            var players = GetRoomPlayers()
                .Where(p => TeamState.GetTeam(p.Actor) == 0)
                .OrderBy(p => p.Actor)
                .ToList();

            GUILayout.BeginVertical(GUI.skin.box);

            _headerStyle.normal.textColor = PeakStyle.Text;
            GUILayout.BeginHorizontal();
            GUILayout.Label("未分组（点数字按钮加入对应组）", _headerStyle, GUILayout.Width(W(320)));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{players.Count}人", _idStyle, GUILayout.Width(W(44)));
            GUILayout.EndHorizontal();

            foreach (var player in players)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(W(30));
                _nameStyle.normal.textColor = PeakStyle.Text;
                var suffix = player.IsLocal ? "（你）" : (player.IsHost ? "（房主）" : "");
                GUILayout.Label($"{player.Name}{suffix}", _nameStyle, GUILayout.Width(W(230)));
                GUILayout.Label($"#{player.Actor}", _idStyle, GUILayout.Width(W(46)));
                GUILayout.FlexibleSpace();

                if (TeamState.GroupCount == 0)
                {
                    GUILayout.Label("先添加分组", _hintStyle, GUILayout.Width(W(90)));
                }
                for (var t = 1; t <= TeamState.GroupCount; t++)
                {
                    var full = TeamState.CountInTeam(t) >= Plugin.MaxPerTeam;
                    var prevEnabled = GUI.enabled;
                    GUI.enabled = !full;
                    if (GUILayout.Button(t.ToString(), PeakStyle.Button(ChainUtil.GroupColor(t)), GUILayout.Width(W(42)), GUILayout.Height(W(32)))) AddMember(t, player.Actor);
                    GUI.enabled = prevEnabled;
                }

                GUILayout.EndHorizontal();
            }

            if (players.Count == 0) GUILayout.Space(W(2));
            GUILayout.EndVertical();
        }

        // ---- 参数滑条 ----

        private static float _lenBuf;
        private static float _strBuf;
        private static float _maxBuf;
        private static bool _bufInit;

        private static void ParamSlider(string label, ref float buf, float min, float max,
            System.Action<float> apply, string format)
        {
            if (!_bufInit)
            {
                _lenBuf = TeamState.ChainLength;
                _strBuf = TeamState.PullStrength;
                _maxBuf = TeamState.MaxPullForce;
                _bufInit = true;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(W(110)));
            var changed = GUILayout.HorizontalSlider(buf, min, max, PeakStyle.SliderTrack, PeakStyle.SliderThumb, GUILayout.Width(W(360)));
            if (Mathf.Abs(changed - buf) > 0.001f)
            {
                buf = changed;
                apply(buf);
                _paramsDirty = true;
            }
            GUILayout.Label(string.Format(format, buf), _idStyle, GUILayout.Width(W(80)));
            GUILayout.EndHorizontal();
        }

        private static void DrawHostActions()
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("智能分组（按进场顺序均分）", PeakStyle.Button(PeakStyle.Accent), GUILayout.Height(W(36))))
            {
                SmartGroup();
            }
            if (GUILayout.Button("清空分组", PeakStyle.Button(PeakStyle.CreamDark), GUILayout.Height(W(36))))
            {
                TeamState.Publish(TeamState.Enabled, 0, TeamState.CopyTeams(), TeamState.CopyNames());
            }

            GUILayout.EndHorizontal();
            GUILayout.Label(
                $"规则：最多 {TeamState.MaxGroups} 组、每组 {Plugin.MaxPerTeam} 人；组内从上到下即链的连接顺序，用上移/下移调整。",
                _hintStyle);
        }

        // ---------------- 状态修改（房主） ----------------

        private static void PublishNow(List<int>[] teams, string[] names, int? count = null)
        {
            TeamState.Publish(TeamState.Enabled, count ?? TeamState.GroupCount, teams, names);
        }

        /// <summary>加入分组：接在链尾。</summary>
        private static void AddMember(int slot, int actor)
        {
            var teams = TeamState.CopyTeams();
            var names = TeamState.CopyNames();
            for (var t = 1; t <= TeamState.MaxSlot; t++) teams[t].Remove(actor);
            if (slot >= 1 && slot <= TeamState.GroupCount && teams[slot].Count < Plugin.MaxPerTeam) teams[slot].Add(actor);
            PublishNow(teams, names);
        }

        /// <summary>移除组员（回到未分组）。</summary>
        private static void RemoveMember(int slot, int actor)
        {
            var teams = TeamState.CopyTeams();
            var names = TeamState.CopyNames();
            teams[slot].Remove(actor);
            PublishNow(teams, names);
        }

        /// <summary>上移/下移：与相邻位置交换。</summary>
        private static void MoveMember(int slot, int actor, int delta)
        {
            var teams = TeamState.CopyTeams();
            var names = TeamState.CopyNames();
            var list = teams[slot];
            var idx = list.IndexOf(actor);
            var target = idx + delta;
            if (idx < 0 || target < 0 || target >= list.Count) return;
            list[idx] = list[target];
            list[target] = actor;
            PublishNow(teams, names);
        }

        /// <summary>删除分组：成员回到未分组，后面的组顺位补上。</summary>
        private static void DeleteGroup(int slot)
        {
            var teams = TeamState.CopyTeams();
            var names = TeamState.CopyNames();
            for (var t = slot; t < TeamState.MaxSlot; t++)
            {
                teams[t] = teams[t + 1];
                names[t] = names[t + 1];
            }
            teams[TeamState.MaxSlot] = new List<int>();
            names[TeamState.MaxSlot] = "";
            PublishNow(teams, names, TeamState.GroupCount - 1);
        }

        /// <summary>提交组名编辑草稿。</summary>
        private static void CommitNameDrafts()
        {
            var dirty = false;
            var names = TeamState.CopyNames();
            for (var t = 1; t <= TeamState.GroupCount; t++)
            {
                if (NameDraft[t] != null && NameDraft[t] != names[t])
                {
                    names[t] = NameDraft[t];
                    dirty = true;
                }
                NameDraft[t] = null;
            }
            if (dirty && PhotonNetwork.IsMasterClient)
            {
                PublishNow(TeamState.CopyTeams(), names);
            }
        }

        private static void SmartGroup()
        {
            var players = GetRoomPlayers().OrderBy(p => p.Actor).ToList();
            if (players.Count == 0) return;

            var maxPer = Plugin.MaxPerTeam;
            var groupCount = Mathf.Clamp(Mathf.CeilToInt(players.Count / (float)maxPer), 1, TeamState.MaxGroups);

            var baseSize = players.Count / groupCount;
            var remainder = players.Count % groupCount;

            var teams = TeamState.CopyTeams();
            for (var t = 1; t <= TeamState.MaxSlot; t++) teams[t].Clear();
            var names = TeamState.CopyNames();

            var index = 0;
            for (var t = 1; t <= groupCount; t++)
            {
                var take = baseSize + (t <= remainder ? 1 : 0);
                for (var k = 0; k < take && index < players.Count; k++)
                {
                    teams[t].Add(players[index++].Actor);
                }
            }

            TeamState.Publish(TeamState.Enabled, groupCount, teams, names);
        }

        private static bool LegacyModDetected()
        {
            return BepInEx.Bootstrap.Chainloader.PluginInfos != null
                && BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(Plugin.LegacyChainedGuid);
        }
    }
}
