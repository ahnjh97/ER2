using Google.Protobuf;
using Google.Protobuf.Protocol;
using Server.Data;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using static Player_StunState;
using static Server.Data.DataUtils;
using static Server.Game.GameObject;

namespace Server.Game
{
    public partial class GameRoom : Room
    {
        public GameRoom()
        {
            SpawnRegistry = new SpawnPointRegistry(spawnCooldownSec: 5.0);

            foreach (var kv in DataManager.SpawnPointDict)
            {
                var info = kv.Value;
                SpawnRegistry.Add(info);
            }

            Spawn = new SpawnSystem(SpawnRegistry);
            Teleport = new TeleportSystem(SpawnRegistry);
        }

        ConcurrentDictionary<int, Player> _players = new ConcurrentDictionary<int, Player>();
        ConcurrentDictionary<int, EnvironmentObject> _envs = new ConcurrentDictionary<int, EnvironmentObject>();
        ConcurrentDictionary<int, Monster> _monsters = new ConcurrentDictionary<int, Monster>();
        ConcurrentDictionary<int, Projectile> _projectiles = new ConcurrentDictionary<int, Projectile>();

        MonsterManager _monsterManager = new MonsterManager();
        CollisionManager _collisionManager = new CollisionManager();
        EnvironmentManager _envManager = new EnvironmentManager();
        BeaconManager _beaconManager = new BeaconManager();

        ConcurrentDictionary<int, ConcurrentDictionary<int, Player>> _teams = new ConcurrentDictionary<int, ConcurrentDictionary<int, Player>>();
        Dictionary<CharacterType, SkillHandler> _skillHandlers = new Dictionary<CharacterType, SkillHandler>();

        public EnvironmentManager GetEnvManager { get { return _envManager; } private set { _envManager = value; } }


        #region Spawn
        public SpawnPointRegistry SpawnRegistry { get; private set; }
        public SpawnSystem Spawn { get; private set; }
        public TeleportSystem Teleport { get; private set; }
        #endregion

        #region Phase, Time, Exp

        private long _startTick;         // 게임 시작 기준 Tick
        private long _phaseStartTick;    // 현재 페이즈 시작 Tick
        private long _phaseEndTick;      // 현재 페이즈 종료 Tick

        private System.Timers.Timer _syncTimer;
        private System.Timers.Timer _expTimer;

        public int CurPhase { get; private set; } = 0;

        public void StartPhase()
        {
            _startTick = TimeUtil.Instance.LastTick;
            _phaseStartTick = _startTick;
            ChangePhase(0);
            if (DataManager.PhaseDict.TryGetValue(0, out int durationSec))
                _phaseEndTick = TimeUtil.Instance.LastTick + durationSec * 1000L;
        }

        public long GameElapsedMs => TimeUtil.Instance.LastTick - _startTick;
        public long PhaseElapsedMs => TimeUtil.Instance.LastTick - _phaseStartTick;

        public void ChangePhase(int newPhase)
        {
            CurPhase = newPhase;
            _phaseStartTick = TimeUtil.Instance.LastTick;

            // 페이즈 지속 시간 가져오기
            if (DataManager.PhaseDict.TryGetValue(newPhase, out int durationSec))
            {
                _phaseEndTick = TimeUtil.Instance.LastTick + durationSec * 1000L;
            }
            else
            {
                _phaseEndTick = long.MaxValue; // 지속시간 정의 안되면 수동 종료
            }

            _monsterManager.Add(CurPhase, this);

            // 클라이언트 동기화
            SyncTimer();

            Console.WriteLine($"Phase Change : {CurPhase}");

            // 특별한 페이즈 로직
            switch (newPhase)
            {
                case 0:
                    PlayBGMByPhase(newPhase);
                    break;
                case 1:
                    {
                        foreach (var p in _players)
                        {
                            p.Value.AcquireItem(new WardInfo()/*DataManager.ItemDict[502212] as WardInfo*/);
                            Push(p.Value.EquipItemSet, p.Value.Info.Player.CharType, CurPhase - 1);
                        }

                        StartExpTimer(); // 1페이즈부터 경험치 획득
                        PlayBGMByPhase(newPhase);

                        S_MinimapIcon minimapIcon = new S_MinimapIcon();
                        minimapIcon.Type = MinimapIcon.OmegaExpected;
                        minimapIcon.IsActivate = true;
                        Push(Broadcast, minimapIcon);
                        break;
                    }
                case 2:
                case 3:
                    foreach (var p in _players)
                    {
                        p.Value.AcquireItem(new WardInfo()/*DataManager.ItemDict[502212] as WardInfo*/);
                        Push(p.Value.EquipItemSet, p.Value.Info.Player.CharType, CurPhase - 1);
                    }
                    PlayBGMByPhase(newPhase);
                    break;
            }
        }

        public void SyncTimer(object state = null)
        {
            S_SyncTimer syncTimerPacket = new S_SyncTimer();

            syncTimerPacket.Phase = CurPhase;
            syncTimerPacket.CurrentTick = TimeUtil.Instance.LastTick; 
            syncTimerPacket.PhaseEndTime = _phaseEndTick; 

            Push(Broadcast, syncTimerPacket);
        }

        public void GetTickExp(object state = null)
        {
            lock (this)
            {
                foreach (var p in _players.Values)
                    p.Exp += 100;
            }
        }

        private void StartSyncTimer(float tick = 5000)
        {
            _syncTimer = new System.Timers.Timer();
            _syncTimer.Interval = tick;
            _syncTimer.Elapsed += ((s, e) => { SyncTimer(); });
            _syncTimer.AutoReset = true;
            _syncTimer.Enabled = true;
        }

        private void StopSyncTimer()
        {
            _syncTimer?.Stop();
            _syncTimer?.Dispose();
            _syncTimer = null;
        }

        private void StartExpTimer(float tick = 2500)
        {
            _expTimer = new System.Timers.Timer();
            _expTimer.Interval = tick;
            _expTimer.Elapsed += ((s, e) => { GetTickExp(); });
            _expTimer.AutoReset = true;
            _expTimer.Enabled = true;
        }

        private void StopExpTimer()
        {
            _expTimer?.Stop();
            _expTimer?.Dispose();
            _expTimer = null;
        }

        public void GetTeamExp(int teamIndex, int exp)
        {
            foreach (Player p in _teams[teamIndex].Values)
            {
                p.Exp += exp;
            }
        }

        public void PlayBGMByPhase(int phase)
        {
            foreach(Player p in _players.Values)
            {
                S_Sound soundPacket = new S_Sound();
                soundPacket.ObjectId = p.Id;
                soundPacket.Name = "BGM";
                soundPacket.Type = "BGM";

                Push(p.Session.Send, soundPacket);
            }
        }
        #endregion

        public bool TryGetMonster(int objectId, out Monster monster)
        {
            return _monsters.TryGetValue(objectId, out monster);
        }
        public CollisionManager CollManager { get { return _collisionManager; } private set { } }
        public BeaconManager BeaconManager { get { return _beaconManager; } private set { } }

        public PathfindInstance PathFind { get; set; }

        #region Score
        private int[] _teamScores = new int[3] { 40, 40, 40 }; // 1번, 2번 팀 사용
        private bool _isGameOver = false;

        public int ReduceScore(int team, int amount)
        {
            int oldValue, newValue;
            do
            {
                oldValue = _teamScores[team];
                newValue = Math.Max(0, oldValue - amount);
            } while (Interlocked.CompareExchange(ref _teamScores[team], newValue, oldValue) != oldValue);

            if(newValue == 0 && _isGameOver == false)
            {
                S_GameOver packet = new S_GameOver();
                if(team == 1)
                    packet.WinTeam = 2;
                else
                    packet.WinTeam = 1;

                _isGameOver = true;
                Push(Broadcast, packet);
            }

            return newValue; // 감소 후 점수 반환
        }
        public int GetScore(int team) { return _teamScores[team]; }
        #endregion

        #region StatusEffect
        Dictionary<CharacterType, Dictionary<KeyCode, Dictionary<int, List<StatusEffect>>>> _statusEffects // Buffs & Debuffs
            = new Dictionary<CharacterType, Dictionary<KeyCode, Dictionary<int, List<StatusEffect>>>>();
        #endregion

        public override void Init()
        {
            PathFind = new PathfindInstance(0);

            // Spawn Env
            _envManager.Init(this);

            _skillHandlers[CharacterType.Theodore] = new TheodoreSkillHandler();

            _collisionManager.Init();

            // Skill Register
            SkillRegistry.InitRegister();
            SetUpStatusEffectDict(); // StatusEffectDict 초기화 

            StartPhase();
            StartSyncTimer();
        }

        public override void Update()
        {
            if (_phaseEndTick > 0 && TimeUtil.Instance.LastTick >= _phaseEndTick)
                ChangePhase(CurPhase + 1);

            Flush();

            foreach (Projectile projectile in _projectiles.Values)
            {
                projectile.Update();
            }

            foreach (Player player in _players.Values)
            {
                int levelUpCnt = player.CheckLevelUp();
                if (levelUpCnt > 0)
                    Push(BroadcastLevelUp, player, levelUpCnt, player.Info.Player.CharType);

                player.Update();
            }
            foreach (Monster monster in _monsters.Values)
            {   
                monster.Update();
            }
            foreach (EnvironmentObject env in _envs.Values)
            {
                env.Update();
            }

            foreach (Player player in _players.Values)
                player.RemoveExpiredStatusEffects();

            foreach (var monster in _monsters.Values)
                monster.RemoveExpiredStatusEffects();

            _collisionManager.CurTick = TimeUtil.Instance.LastTick;
            _collisionManager.Flush();
            _collisionManager.CheckAllCollisions(_teams, _monsters, _projectiles);
            _collisionManager.Update();

            _beaconManager.Update(this);

            SendVisibleObjsPkts();
            CheckLastPing();
        }
       
        public void EnterGame(GameObject gameObject, int team)
        {
            if (gameObject == null)
                return;

            GameObjectType type = ObjectManager.GetObjectTypeById(gameObject.Id);

            if (type == GameObjectType.Player)
            {
                if (gameObject is not Player player)
                    return;

                // 이미 입장한 플레이어는 중복 초기화하지 않음
                if (!_players.TryAdd(player.Id, player))
                    return;
                player.Info.Player.Team = team;
                player.Info.Player.Weapon = FindWeapon(player.Info.Player.CharType);
                player.WeaponAttackRange = DataManager.WeaponDict[player.Info.Player.Weapon].Range;

                var teamPlayers = _teams.GetOrAdd(player.Info.Player.Team,
                    static _ => new ConcurrentDictionary<int, Player>());
                teamPlayers.TryAdd(player.Id, player);

                ObjectManager.Instance.RegisterTeam(gameObject.Id, player.Info.Player.Team);

                player.Room = this;
                player.Init();

                // 본인한테 정보 전송
                {
                    if (Spawn == null)
                        Console.WriteLine($"Spawn Error! : Spawn is null");
                    
                    player.Info.PosInfo = Spawn.GetSpawnPoint(player.Team).ToPositionInfo();

                    S_EnterGame enterPacket = new S_EnterGame();
                    enterPacket.ObjInfo = new ObjectInfo();
                    enterPacket.ObjInfo.MergeFrom(player.Info.ToByteArray());
                    player.Session.Send(enterPacket);

                    S_Spawn spawnPacket = new S_Spawn();
                    foreach (Player p in _players.Values.ToArray())
                    {
                        if (p != null && player.Id != p.Id)
                            AddObjectToPacket(spawnPacket, p, player.Id);                       
                    }

                    foreach (Monster m in _monsters.Values.ToArray())
                    {
                        if (m != null)
                            AddObjectToPacket(spawnPacket, m);
                    }

                    foreach (Projectile proj in _projectiles.Values.ToArray())
                    {
                        if (proj != null)
                            AddObjectToPacket(spawnPacket, proj);
                    }

                    foreach (EnvironmentObject env in _envs.Values.ToArray())
                    {
                        if (env != null)
                            AddObjectToPacket(spawnPacket, env);
                    }

                    player.Session.Send(spawnPacket);

                    // Temp Cobalt Exp
                    player.Info.StatInfo.Exp = 15800;
                    int levelUpCnt = player.CheckLevelUp();
                    if (levelUpCnt > 0)
                        Push(BroadcastLevelUp, player, levelUpCnt, player.Info.Player.CharType);

                    // 페이즈에 해당하는 아이템 장착
                    if (CurPhase > 0)
                        player.EquipItemSet(player.Info.Player.CharType, CurPhase - 1);

                    // 시간 동기화
                    SyncTimer();
                    player.SendChangeAttackRangePacket();
                }
            }
            else if (type == GameObjectType.Monster)
            {
                Monster monster = gameObject as Monster;

                monster.Room = this;
                _monsters.TryAdd(gameObject.Id, monster);
            }
            else if (type == GameObjectType.Projectile)
            {
                Projectile projectile = gameObject as Projectile;
                projectile.Room = this;

                if(projectile.Info.Projectile == null)
                    projectile.Info.Projectile = new ProjectileInfo();

                projectile.Info.Projectile.ProjectileType = projectile.ProjectileType;
                projectile.Info.Projectile.OwnerId = projectile.Owner?.Id ?? -1;
                _projectiles.TryAdd(gameObject.Id, projectile);
            }
            else if (type == GameObjectType.Environment)
            {
                EnvironmentObject env = gameObject as EnvironmentObject;

                env.Room = this;
                _envs.TryAdd(gameObject.Id, env);
            }

            // 타인한테 정보 전송
            {
                foreach (Player p in _players.Values.ToArray())
                {
                    if (p != null && p.Id != gameObject.Id && p.Session != null)
                    {
                        S_Spawn spawnPacket = new S_Spawn();
                        var playerInfoCopy = new ObjectInfo();
                        playerInfoCopy.MergeFrom(gameObject.Info.ToByteArray());
                        spawnPacket.Objects.Add(playerInfoCopy);
                        p.Session.Send(spawnPacket);
                    }

                    //p.SendItemStat();
                }
            }
        }

        public void LeaveGame(int objectId)
        {
            GameObjectType type = ObjectManager.GetObjectTypeById(objectId);

            if (type == GameObjectType.Player)
            {
                Player player = null;
                if (!_players.TryGetValue(objectId, out player) || player == null)
                    return;
                int teamId = player.Info.Player.Team;
                _players.TryRemove(objectId, out _);

                if (_teams.TryGetValue(teamId, out var myTeam))
                    myTeam.TryRemove(objectId, out _);

                ObjectManager.Instance.Remove(player.Id);

                player.Room = null;
                player.OnDestroy();

                // 본인한테 정보 전송
                {
                    S_LeaveGame leavePacket = new S_LeaveGame();
                    player.Session.Send(leavePacket);
                }

                if (_players.IsEmpty)
                    RoomManager.Instance.Remove(RoomId);
            }
            else if (type == GameObjectType.Monster)
            {
                if (_monsters.TryRemove(objectId, out Monster monster) && monster != null)
                {
                    ObjectManager.Instance.Remove(objectId);
                    monster.Room = null;
                    _monsterManager.Add(-1, this);
                }
            }
            else if (type == GameObjectType.Projectile)
            {
                if (_projectiles.TryRemove(objectId, out Projectile projectile) && projectile != null)
                {
                    // 본인한테 정보 전송
                    {
                        S_Despawn despawnPacket = new S_Despawn();
                        despawnPacket.ObjectIds.Add(objectId);
                        Player player = projectile.Owner as Player;
                        if(player != null)
                        {
                            Push(player.Session.Send, despawnPacket);
                        }                      
                    }

                    projectile.Room = null;
                    projectile.Owner = null;
                }
            }

            // 타인한테 정보 전송
            {

                foreach (Player p in _players.Values.ToList())
                {
                    if (p.Id != objectId && p.Session != null)
                    {
                        S_Despawn despawnPacket = new S_Despawn();
                        despawnPacket.ObjectIds.Add(objectId);
                        p.Session.Send(despawnPacket);
                    }                        
                }
            }
        }

        #region Handler
        public void HandleAmplificationSkill(Player player, S_Interact skillPacket)
        {
            if (_skillHandlers.TryGetValue(player.Info.Player.CharType, out var handler))
                handler.CanUse(player, skillPacket);
        }

        public void AttackSkillTarget(Player player, GameObject target, KeyCode keyCode) // 타게팅 스킬. 대상 1명.
        {
            if (player == null)
                return;

            float damage = 0f;

            if (target.ObjectType == GameObjectType.Player)
                damage = _collisionManager.CalcDamage(player, target as Player, keyCode);
            else
                damage = _collisionManager.CalcDamage(player, target.Stat, keyCode);

            if (player.Info.Player.CharType == CharacterType.Abigail && keyCode == KeyCode.E)
            {
                S_RemoveAbigailCoord removeAbigailCoordPkt = new S_RemoveAbigailCoord();
                removeAbigailCoordPkt.ObjectId = target.Id;
                player.Room.Broadcast(removeAbigailCoordPkt);

                int removeCnt = target.RemoveStatusEffects("Coord");
                if (removeCnt > 0) // 표식 있는 적에게 E 사용시 E 쿨타임 초기화
                    player.Skill.SetCooldown(keyCode, 0);
            }

            target.OnDamaged(player, damage);
        }

        #region StatusEffect
        public void AddStatusEffect(Player player, GameObject target, KeyCode keyCode, string allowedCondition) // 타게팅 스킬 StatusEffect 적용
        {
            List<StatusEffect> statusEffectList = GetStatusEffectList(player.Info.Player.CharType, keyCode, player.GetSkillLevel(keyCode));

            foreach (var effect in statusEffectList)
            {
                if (effect.condition != allowedCondition)
                    continue;

                effect.attacker = player;

                switch (effect.subject)
                {
                    case Subject.Self:
                        Push(player.AddStatusEffect, effect);
                        break;
                    case Subject.Ally:
                        break;
                    case Subject.Enemy:
                        Push(target.AddStatusEffect, effect);
                        break;
                    case Subject.W:
                        if (effect.type == "CDR")
                            player.Skill.Reduce(KeyCode.W, effect.value, effect.valueType == ValueType.Ratio);
                        break;
                    case Subject.E:
                        if (effect.type == "CDR")
                            player.Skill.Reduce(KeyCode.E, effect.value, effect.valueType == ValueType.Ratio);
                        break;
                }
            }
        }

        void SetUpStatusEffectDict()
        {
            foreach (var nestedKvp in DataManager.SkillDict)
            {
                foreach (var kvp in nestedKvp.Value)
                {
                    foreach (var levelKvp in kvp.Value.levels)
                    {
                        if (levelKvp.Value.effects == null)
                            continue;

                        foreach (EffectData effectData in levelKvp.Value.effects)
                        {
                            CharacterType charType = nestedKvp.Key;
                            KeyCode keyCode = kvp.Key;
                            int level = levelKvp.Key;

                            if (!_statusEffects.TryGetValue(charType, out var skillDict))
                                _statusEffects[charType] = skillDict = new Dictionary<KeyCode, Dictionary<int, List<StatusEffect>>>();

                            if (!skillDict.TryGetValue(keyCode, out var levelDict))
                                skillDict[keyCode] = levelDict = new Dictionary<int, List<StatusEffect>>();

                            if (!levelDict.TryGetValue(level, out var effects))
                                levelDict[level] = effects = new List<StatusEffect>();

                            StatusEffect newEffect = new StatusEffect
                            {
                                type = effectData.type,
                                stat = effectData.stat,
                                duration = effectData.duration,
                                value = effectData.value,
                                subject = Enum.TryParse(effectData.subject, true, out Subject temp) ? temp : Subject.Subject_None,
                                valueType = Enum.TryParse(effectData.valueType, true, out ValueType type) ? type : ValueType.ValueType_None,
                                coeff = effectData.coeff,
                                ratioPerTarget = effectData.ratioPerTarget,
                                maxRatio = effectData.maxRatio,
                                condition = effectData.condition
                            };

                            effects.Add(newEffect);
                        }
                    }
                }
            }
        }

        List<StatusEffect> GetStatusEffectList(CharacterType charType, KeyCode keyCode, int skillLevel)
        {
            if (!_statusEffects.TryGetValue(charType, out var keyDict))
                return null;

            if (!keyDict.TryGetValue(keyCode, out var skillData))
                return null;

            if (!skillData.TryGetValue(skillLevel, out var statusEffectsList))
                return null;

            return statusEffectsList;
        }
        #endregion

        public void HandleMoveSync(Player player, C_MoveSync movePacket)
        {
            if (player == null)
                return;

            player.PosInfo.SetPosInfoFromVector3(movePacket.PosInfo.ToVector());
            player.RotInfo.MergeFrom(movePacket.RotInfo);

            player.SendMovePacket(new PositionInfo(player.PosInfo), new RotationInfo(player.RotInfo));
        }

        public void HandleAnim(Player player, C_Anim animPacket)
        {
            if (player == null)
                return;

            S_Anim anim = new S_Anim() { AnimInfo = new AnimInfo() };
            anim.ObjectId = player.Id;
            anim.AnimInfo = animPacket.AnimInfo;
            Broadcast(anim);
        }

        public void HandleRest(Player player, C_Rest pkt)
        {
            if (player == null)
                return;
            if (player.IsDead)
                return;

            player.ChangeState(new Player_RestState(pkt.IsRest));
        }

        public void HandleDeath(Player player, C_Death pkt)
        {
            if (player == null)
                return;

            player.Hp = 0;
            player.IsDeath = true;
        }

        public void HandleKeyInputForTest(Player player, C_KeyInputForTest pkt)
        {
            if (player == null)
                return;

            //player.Skill.SetCooldown(KeyCode.R, 0f);
            //StunStateDesc desc = new StunStateDesc();
            //desc.Duration = 5;
            ////desc.Speed = 17;
            //desc.EndPos = Vector3.Zero;
            //player.ChangeState(new Player_StunState(desc));
            player.Exp += 5000;
        }

        #endregion
        public void Broadcast(IMessage packet)
        {
            foreach (Player p in _players.Values.ToArray())
            {
                if (p == null || p.Session == null)
                    continue;

                p.Session.Send(packet);
            }
        }

        public override void CheckLastPing()
        {
            foreach(Player p in _players.Values.ToArray())
            {
                if (p == null || p.Session == null)
                    continue;

                if (p.Session.CheckTimeout())
                    p.Session.Disconnect();
            }
        }

        private void AddVisibleObjects<T>(List<int> visibleObjs, ConcurrentDictionary<int, T> dict, Player player) where T : GameObject
        {
            foreach (var pair in dict)
            {
                GameObject go = pair.Value;
                if (go.Id == player.Id)
                    continue;

                if (go.IsVisionShare() || (go is Player p && p.Info.Player.Team == player.Info.Player.Team))
                {
                    visibleObjs.Add(go.Id);
                }
            }
        }

        List<int> GetObjectsInRange<T>(Dictionary<int, T> dict, Player player, int range = 8) where T : GameObject
        {
            List<int> result = new List<int>();

            foreach (GameObject go in dict.Values)
            {
                if (go.PosInfo.Distance(player.PosInfo) < range)
                {
                    if (go.Id == player.Id)
                        continue;
                    result.Add(go.Id);
                }

            }
            return result;
        }

        void SendVisibleObjsPkts()
        {
            foreach (Player player in _players.Values)
            {
                List<int> visibleObjs = new List<int>();
                AddVisibleObjects(visibleObjs, _players, player);
                AddVisibleObjects(visibleObjs, _monsters, player);
                AddVisibleObjects(visibleObjs, _projectiles, player);
                AddVisibleObjects(visibleObjs, _envs, player);
                player.SendVisibleObjsPkt(visibleObjs);
            }
        }

        void BroadcastLevelUp(Player player, int levelUpCnt, CharacterType charType)
        {
            S_LevelUp levelUpPkt = new S_LevelUp();
            levelUpPkt.ObjectId = player.Id;
            levelUpPkt.Level = player.Stat.Level;
            levelUpPkt.LevelUpCnt = levelUpCnt;

            StatInfo grouwthStatInfo = new StatInfo(DataManager.StatGrowthDict[charType]);
            grouwthStatInfo.MultiplyForGrowth(levelUpCnt);

            StatInfo statInfo = new StatInfo(player.Stat);
            statInfo.Attack = player.Attack;
            statInfo.Defense = player.Defense;
            statInfo.MaxHp = player.Stat.MaxHp;
            statInfo.Hp = grouwthStatInfo.MaxHp;
            statInfo.HpRegen = player.Stat.HpRegen;
            statInfo.MaxStamina = player.Stat.MaxStamina;
            statInfo.Stamina = grouwthStatInfo.MaxStamina;
            statInfo.StaminaRegen = player.Stat.StaminaRegen;

            levelUpPkt.StatGrowth = statInfo;
            //StatInfo statInfo = new StatInfo(DataManager.StatGrowthDict[charType]);
            //statInfo.MultiplyForGrowth(levelUpCnt);
            //levelUpPkt.StatGrowth = statInfo;

            levelUpPkt.NextMaxExp = DataManager.ExpDict[player.Stat.Level];
            levelUpPkt.CurExp = player.Exp;

            Broadcast(levelUpPkt);
        }

        

        public void SkillLevelUp(int id, int key)
        {
            S_SkillLevelUp skillLevelUpPacket = new S_SkillLevelUp();
            skillLevelUpPacket.KeyCode = key;

            Player player = FindPlayer(p =>
            {
                if (p.Id == id) return true;
                return false;
            });

            player.Session.Send(skillLevelUpPacket);
        }

        #region Search
        private Weapon FindWeapon(CharacterType type)
        {
            switch (type)
            {
                case CharacterType.Rozzi:
                    return Weapon.Pistol;
                case CharacterType.Yuki:
                    return Weapon.TwoHandSword;
                case CharacterType.Abigail:
                    return Weapon.Axe;
                case CharacterType.Theodore:
                    return Weapon.SniperRifle;
                case CharacterType.Hyunwoo:
                    return Weapon.Glove;
            }

            return Weapon.Pistol;
        }
        public Projectile FindProjectile(Creature owner, ProjectileType type = ProjectileType.ProjectileNone)
        {
            foreach (Projectile projectile in _projectiles.Values)
            {
                if (projectile.Owner == owner)
                {
                    if(type != ProjectileType.ProjectileNone && type != projectile.ProjectileType)
                        continue;

                    return projectile;
                }
            }
            return null;
        }
        public Player FindPlayer(Func<GameObject, bool> condition)
        {
            foreach (Player player in _players.Values)
            {
                if (condition.Invoke(player))
                    return player;
            }
            return null;
        }

        public GameObject FindNearestEnemy(int team, int id, Vector2 pos, float radius)
        {
            GameObject nearest = null;
            float nearestDistSq = radius * radius;

            int enemyTeam = (team == 1) ? 2 : 1;

            if (_teams.TryGetValue(enemyTeam, out var enemyDict) && enemyDict != null)
            {
                foreach (var kvp in enemyDict)
                {
                    if (kvp.Key == id || kvp.Value.IsUntargetable() || kvp.Value.IsDead)
                        continue;
                    var player = kvp.Value;
                    Vector2 playerPos = new Vector2(player.PosInfo.PosX, player.PosInfo.PosZ);
                    float distSq = Vector2.DistanceSquared(pos, playerPos);
                    if (distSq < nearestDistSq)
                    {
                        nearestDistSq = distSq;
                        nearest = player;
                    }
                }
            }            

            foreach (var kvp in _monsters)
            {
                if (kvp.Key == id)
                    continue;
                var monster = kvp.Value;
                if (monster.IsUntargetable() || monster.Info.Monster.MonsterType == MonsterType.Turret)
                    continue;
                Vector2 monsterPos = new Vector2(monster.PosInfo.PosX, monster.PosInfo.PosZ);
                float distSq = Vector2.DistanceSquared(pos, monsterPos);
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = monster;
                }
            }

            return nearest;
        }

        #endregion

        public void CallOnCollision<T>(Player player, List<T> hitTargets, StatusEffect effect) where T : GameObject, new()
        {
            if (player.CurrentState is Player_SkillState skillState)
                skillState.Handler.OnCollision(player, hitTargets, effect);
        }
        public void CallOnCollision<T>(Player player, T nearTarget, StatusEffect effect) where T : GameObject, new()
        {
            if (player.CurrentState is Player_SkillState skillState)
                skillState.Handler.OnCollision(player, nearTarget, effect);
        }

        public void HandleOperate(Player player, Beacon beacon, float posX, float posZ)
        {
            player.Beacon = beacon;

            bool canOperate = BeaconManager.IsOperatable(player.Info.Player.Team, beacon);
            bool canOccupy = BeaconManager.IsOccupiable(player.Info.Player.Team, beacon);
            bool inRange = BeaconManager.IsInRange(player.Position, beacon);

            if (canOperate && canOccupy && inRange) // 점령 가능 && 사거리 내 -> 점령
            {
                player.ChangeState(new Player_OperateState());
                return;
            }

            var move = new C_Move
            {
                IsTargetOn = false,
                TargetPosition = new PositionInfo
                {
                    PosX = posX,
                    PosY = 0,
                    PosZ = posZ
                }
            };

            player.ChangeState(new Player_MovingState(move));
            player.SendMoveSyncPacket(move.TargetPosition);

            if (canOccupy) // 점령 가능하지만 사거리 밖이면 이동종료됐을 때의 상태 예약
                player.ReservedState = new Player_OperateState();
        }

        public void HandlerChat(Player player, C_Chat chatPkt)
        {
            if (string.IsNullOrWhiteSpace(chatPkt.Message))
                return;

            S_Chat sendPkt = new S_Chat()
            {
                ObjectId = player.Id,
                TeamId = player.Team,
                PlayerName = player.Info.Player.Nickname,
                Message = chatPkt.Message,
                ChatType = chatPkt.ChatType,
                CharType = player.CharType
            };

            if (chatPkt.ChatType == ChatType.Team)
            {
                // 같은 팀에게만 채팅 전송
                foreach (var p in _players.Values)
                {
                    if (p.Team == player.Team)
                        p.Session.Send(sendPkt);
                }
            }
            else
            {
                // 전체 채팅
                Push(Broadcast, sendPkt);
            }
        }

        public void HandleUseItem(Player player, C_UseItem packet)
        {
            player.UseItem(packet.InventoryIndex, new Vector3(packet.MouseX, 0, packet.MouseZ));
        }

        #region AbigailPkts
        public void BroadcastAbigailSound(Player player, AbigailSound sound, float prob)
        {
            if (!DataManager.AbigailAudioDict.TryGetValue(sound, out List<string> paths))
                return;

            bool play = Math.Abs(prob - 1) < 0.0001f || Random.Shared.NextDouble() < prob;
            if (!play)
                return;

            S_AbigailSound abigailSound = new S_AbigailSound();
            abigailSound.ObjectId = player.Id;
            abigailSound.Sound = sound;
            abigailSound.Pos = player.PosInfo;
            abigailSound.Idx = Random.Shared.Next(0, paths.Count);
            Broadcast(abigailSound);
        }

        public void BroadcastAbigailFx(Player player, AbigailFx fx, float duration)
        {
            S_AbigailFx abigailFx = new S_AbigailFx();
            abigailFx.ObjectId = player.Id;
            abigailFx.Fx = fx;
            abigailFx.Duration = duration;
            Broadcast(abigailFx);
        }

        public void BroadcastStopAbglFx(Player player, AbigailFx fx)
        {
            S_StopAbglFx stopFx = new S_StopAbglFx();
            stopFx.ObjectId = player.Id;
            stopFx.Fx = fx;
            Broadcast(stopFx);
        }

        public void BroadcastAbglPortal(Player player, Vector2 startPos, Vector2 endPos)
        {
            S_AbigailPortal abglPortal = new S_AbigailPortal();
            abglPortal.ObjectId = player.Id;
            abglPortal.StartX = startPos.X;
            abglPortal.StartZ = startPos.Y;
            abglPortal.EndX = endPos.X;
            abglPortal.EndZ = endPos.Y;
            Broadcast(abglPortal);
        }
        #endregion

        public Player FindViableTarget(Monster monster, float range)
        {
            float rangeSq = range * range;

            foreach (var p in _players)
            {
                Player player = p.Value;
                if (player == null)
                    continue;
                if (player.Team == monster.MonsterTeam)
                    continue;

                PositionInfo playerPos = player.Info.PosInfo;

                if (monster.Info.PosInfo.GetDistanceSq(playerPos) <= rangeSq)
                {
                    if (player.CurrentState is Player_IdleState)
                        continue;

                    return player; 
                }
            }
            return null;
        }

        private void AddObjectToPacket(S_Spawn packet, GameObject gameObject, int excludeId = -1)
        {
            if (gameObject == null || gameObject.Id == excludeId)
                return;

            try
            {
                var objectInfoCopy = new ObjectInfo();
                objectInfoCopy.MergeFrom(gameObject.Info.ToByteArray());
                packet.Objects.Add(objectInfoCopy);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to copy ObjectInfo for {gameObject.Id}: {ex.Message}");
            }
        }
    }
}
