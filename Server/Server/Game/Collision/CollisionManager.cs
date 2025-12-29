using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using Google.Protobuf.Collections;
using Google.Protobuf.Protocol;
using Lucene.Net.Index;
using Microsoft.VisualBasic;
using Server.Data;
using static System.Net.Mime.MediaTypeNames;
using static NUnit.Framework.Constraints.Tolerance;
using static Server.Data.DataUtils;
using static Server.Game.GameObject;

namespace Server.Game
{
    public class Hitbox
    {
        public Creature Creature { get; set; }
        public float PosX { get; set; } = 0;
        public float PosZ { get; set; } = 0;

        public Vector2 MousePos { get; set; } = new Vector2();
        public float ChargeRatio { get; set; } = 1;
        public CharacterType CharType { get; set; }
        public KeyCode KeyCode { get; set; }
        public int Team { get; set; }

        public SkillHitbox Data { get; set; }
        public long StartTick { get; set; } // Skill Start Time
        public long EndTick { get; set; } // Skill End Time

        public bool IsUsed { get; set; } = false;

        // Key: ObjectId, Value: Nothing
        public ConcurrentDictionary<int, byte> HitObjs = new ConcurrentDictionary<int, byte>();

        // Key: StatusEffect, Value: Count
        public ConcurrentDictionary<StatusEffect, int> effectCnt = new ConcurrentDictionary<StatusEffect, int>();

        int _addedToHitSoundList = 0;

        public bool TryAddToSoundList()
        {
            return Interlocked.CompareExchange(ref _addedToHitSoundList, 1, 0) == 0;
        }

        #region 흡혈
        public bool Omnivamp { get; set; } = false;

        private float _totalDamage;
        public float TotalDamage
        {
            get => _totalDamage;
            set => _totalDamage = value;
        }

        public void AddDamage(float amount)
        {
            float initialValue, newValue;
            int initialBits, newBits;

            do
            {
                initialValue = _totalDamage;
                newValue = initialValue + amount;
                initialBits = BitConverter.SingleToInt32Bits(initialValue);
                newBits = BitConverter.SingleToInt32Bits(newValue);
            }
            while (Interlocked.CompareExchange(ref Unsafe.As<float, int>(ref _totalDamage), newBits, initialBits) != initialBits);
        }
        #endregion 흡혈

        #region 추가 데이터
        public MonsterSkill MonsterSkillType;
        public Dictionary<KeyCode, List<string>> Interactions { get; set; } = new Dictionary<KeyCode, List<string>>();
        public HashSet<Hitbox> InteractedHitboxes { get; } = new HashSet<Hitbox>();
        public Hitbox trackingHitbox { get; set; } = null;
        public MonsterType MonstType { get; set; }

        public bool IsInteracted = true;
        public float OffsetRadius = 0;
        public Vector3 FixedPosition = new Vector3();

        public float HitInterval { get; set; } = 500.0f;
        public ConcurrentDictionary<int, long> LastHitTicks = new ConcurrentDictionary<int, long>();
        #endregion
    }

    public enum Subject { Subject_None, Self, Ally, Enemy, Q, W, E, R, T }
    public enum ValueType { Ratio, Flat, ValueType_None }

    public class CollisionManager
    {
        object _lock = new object();

        // Key: ObjectId
        private Dictionary<int, HashSet<Hitbox>> _hitboxDict = new Dictionary<int, HashSet<Hitbox>>();

        // 2타 
        Dictionary<CharacterType, Dictionary<KeyCode, KeyCode>> _hitboxChainDict = new Dictionary<CharacterType, Dictionary<KeyCode, KeyCode>>();

        // 아군 대상 스킬
        Dictionary<CharacterType, HashSet<KeyCode>> _allyHitSkillDict = new Dictionary<CharacterType, HashSet<KeyCode>>();

        List<Hitbox> _pendingHitboxes = new List<Hitbox>();

        private InteractionManager _interactionManager = new InteractionManager();

        Dictionary<CharacterType, Dictionary<KeyCode, Dictionary<int, List<StatusEffect>>>> _statusEffects // Buffs & Debuffs
            = new Dictionary<CharacterType, Dictionary<KeyCode, Dictionary<int, List<StatusEffect>>>>();

        Dictionary<KeyCode, AbigailSound> _abigailSoundDict = new Dictionary<KeyCode, AbigailSound>();

        private long _curTick;
        public long CurTick
        {
            get => Interlocked.Read(ref _curTick);
            set => Interlocked.Exchange(ref _curTick, value);
        }

        public void Init()
        {
            // 2타 hitbox 세팅
            Dictionary<KeyCode, KeyCode> abigailChainDict = new Dictionary<KeyCode, KeyCode> { { KeyCode.Q, KeyCode.F1 } };
            _hitboxChainDict.Add(CharacterType.Abigail, abigailChainDict);

            _abigailSoundDict[KeyCode.Q] = AbigailSound.QfirstHit;
            _abigailSoundDict[KeyCode.F1] = AbigailSound.QsecondHit; 
            _abigailSoundDict[KeyCode.W] = AbigailSound.Whit;
            _abigailSoundDict[KeyCode.R] = AbigailSound.Rhit; 

            SetUpAllyHitSkills();
            SetUpStatusEffects();
        }

        public Hitbox AddHitbox(Creature player, CharacterType charType, KeyCode keyCode, Vector2 mousePos = new Vector2(), float chargeRatio = 0)
        {
            Hitbox hitbox = null;
            lock (_lock)
            {
                SkillHitbox skillHitbox = DataManager.SkillHitboxDict[charType][keyCode];
                if (skillHitbox.EndFrame <= 0)
                    return null;

                hitbox = new Hitbox
                {
                    Creature = player,
                    PosX = player.PosInfo.PosX,
                    PosZ = player.PosInfo.PosZ,
                    ChargeRatio = chargeRatio,
                    CharType = charType,
                    KeyCode = keyCode,
                    Team = player.Info.Player.Team,
                    Data = skillHitbox,

                    MousePos = mousePos,
                    Interactions = ConvertProtoInteractionsToKeyCodeDictionary(skillHitbox.Interactions)
                };

                if (charType == CharacterType.Rozzi && (keyCode == KeyCode.E || keyCode == KeyCode.F2))
                {
                    hitbox.PosX = mousePos.X;
                    hitbox.PosZ = mousePos.Y;
                }
                else if (charType == CharacterType.Abigail && keyCode == KeyCode.D)
                    hitbox.Omnivamp = true;

                SettingType(hitbox);

                _pendingHitboxes.Add(hitbox);
            }            

            // 2타 hitbox 추가
            if(_hitboxChainDict.TryGetValue(charType, out Dictionary<KeyCode, KeyCode> chainDict))
            {
                if (chainDict.TryGetValue(keyCode, out KeyCode value))
                    AddHitbox(player, charType, value, mousePos, chargeRatio);
            }
            return hitbox;
        }
       
        public void Update()
        {
            RemoveExpired();
            UpdatePos();
        }

        // 충돌체 찾기
        public Hitbox FindCollision(int id, KeyCode key)
        {
            foreach (var nestedKvp in _hitboxDict[id])
            {
                if (nestedKvp.KeyCode == key)
                    return nestedKvp;
            }
            return null;
        }

        public void CheckAllCollisions(
            ConcurrentDictionary<int, ConcurrentDictionary<int, Player>> teams,
            ConcurrentDictionary<int, Monster> monsters,
            ConcurrentDictionary<int, Projectile> projectiles)
        {
            Dictionary<int, Dictionary<int, float>> damageDict = new Dictionary<int, Dictionary<int, float>>();
            List<Hitbox> hitSoundList = new List<Hitbox>();
             
            CheckCollisionHit();
            CheckPlayerHit(teams, damageDict, hitSoundList);
            CheckHit(monsters, damageDict, hitSoundList);
            
            SendChangeHpPkts(teams, damageDict);
            BroadcastHitSoundPkts(hitSoundList);
        }

        public void RemoveExpired()
        {
            List<Hitbox> removeQueue = new List<Hitbox>();

            foreach (HashSet<Hitbox> hitboxSet in _hitboxDict.Values)
            {
                foreach (Hitbox hitbox in hitboxSet)
                {
                    if (CurTick >= hitbox.EndTick || hitbox.IsUsed || hitbox.Creature.Hp <= 0)
                    {
                        removeQueue.Add(hitbox);

                        if(hitbox.CharType == CharacterType.Abigail && hitbox.KeyCode == KeyCode.D)
                        {
                            float healAmount = hitbox.TotalDamage * 0.8f;
                            hitbox.Creature.Room.Push(hitbox.Creature.OnHeal, hitbox.Creature, healAmount);
                        }
                    }                        
                }
            }

            lock (_lock)
            {
                foreach (Hitbox hitbox in removeQueue)
                {
                    if (_hitboxDict.TryGetValue(hitbox.Creature.Id, out var set))
                        set.Remove(hitbox);
                }
            }
        }
        public void UpdatePos()
        {
            foreach (var set in _hitboxDict.Values)
            {
                foreach (Hitbox hitbox in set)
                {
                    if (hitbox.Creature == null || hitbox.Data == null)
                        continue;
                    if (false == System.Enum.TryParse<SkillType>(hitbox.Data.Type, out SkillType type))
                        continue;
                    if (type == SkillType.SkillPoint || type == SkillType.SkillTargeting)
                        continue;

                    if (type == SkillType.SkillProjectile)
                    {
                        UpdatePosProjectile(hitbox);
                        continue;
                    }

                    if (hitbox.Creature is Monster)
                        UpdateTransformRay(hitbox);
                    else
                    {
                        Quaternion rot = hitbox.Creature.RotInfo.GetQuatFromRotInfo();
                        Vector3 offset = new Vector3(hitbox.Data.RightOffset, 0, hitbox.Data.LookOffset);
                        Vector3 rotatedOffset = Vector3.Transform(offset, rot);

                        hitbox.PosX = hitbox.Creature.PosInfo.PosX + rotatedOffset.X;
                        hitbox.PosZ = hitbox.Creature.PosInfo.PosZ + rotatedOffset.Z;
                    }
                }
            }
        }

        void CheckPlayerHit(ConcurrentDictionary<int, ConcurrentDictionary<int, Player>> teams, Dictionary<int, Dictionary<int, float>> damageDict,
            List<Hitbox> hitSoundList)
        {
            foreach (var nestedKvp in _hitboxDict)
            {
                int ownerId = nestedKvp.Key;
                HashSet<Hitbox> hitboxes = nestedKvp.Value;
                if (hitboxes.Count == 0)
                    continue;

                int myTeam = ObjectManager.Instance.GetTeam(ownerId);

                foreach (var hitbox in hitboxes)
                {
                    if (CurTick < hitbox.StartTick || CurTick > hitbox.EndTick)
                        continue;

                    List<Player> hitPlayers = new List<Player>();

                    foreach (var teamKvp in teams)
                    {
                        int teamId = teamKvp.Key;
                        if (teamId == myTeam)
                        {
                            bool isBusy = (hitbox.Creature.Info.Player.CharType == CharacterType.Theodore && hitbox.KeyCode == KeyCode.R);

                            HandleAllyHit(hitbox, teamKvp.Value, isBusy);
                            continue;
                        }                            
                        HandleCollision<Player>(hitbox, teamKvp.Value, hitPlayers, damageDict);
                    }

                    if(hitPlayers.Count > 0)
                    {
                        if(hitbox.CharType == CharacterType.Rozzi)
                        {
                            if (HandleRozziRHitbox(hitbox, hitPlayers, ownerId, isPlayerTarget: true))
                                continue;
                        }

                        HandleDamage<Player>(hitbox, hitPlayers, damageDict);
                        HandleStatusEffects<Player>(hitbox, hitPlayers);
                        if (hitbox.TryAddToSoundList())
                            hitSoundList.Add(hitbox);
                    }
                }
            }
        }

        void CheckHit<T>(IDictionary<int, T> targets, Dictionary<int, Dictionary<int, float>> damageDict,
            List<Hitbox> hitSoundList) where T : GameObject, new()
        {
            foreach (var nestedKvp in _hitboxDict)
            {
                int ownerId = nestedKvp.Key;
                HashSet<Hitbox> hitboxes = nestedKvp.Value;
                if (hitboxes.Count == 0)
                    continue;
              
                foreach (var hitbox in hitboxes)
                {
                    if (CurTick < hitbox.StartTick || CurTick > hitbox.EndTick)
                        continue;

                    List<T> hitTargets = new List<T>();

                    HandleCollision<T>(hitbox, targets, hitTargets, damageDict);

                    if (hitTargets.Count > 0)
                    {
                        if (hitbox.CharType == CharacterType.Rozzi)
                        {
                            if (HandleRozziRHitbox<T>(hitbox, hitTargets, ownerId, isPlayerTarget: false))
                                continue;
                        }

                        HandleDamage<T>(hitbox, hitTargets, damageDict);
                        HandleStatusEffects<T>(hitbox, hitTargets);
                        if (hitbox.TryAddToSoundList())
                            hitSoundList.Add(hitbox);
                    }
                }
            }
        }


        void HandleCollision<T>(Hitbox hitbox, IDictionary<int, T> targets, List<T> hitTargets, Dictionary<int, Dictionary<int, float>> damageDict) where T : GameObject, new()
        {
            foreach (var targetKvp in targets)
            {
                T target = targetKvp.Value;

                if (hitbox.IsUsed)
                    continue;

                if (!CheckCollision(hitbox, target))
                    continue;

                if (hitbox.Data.RepeatingDamage)
                {
                    if (hitbox.LastHitTicks.TryGetValue(targetKvp.Key, out long lastHitTick))
                    {
                        if (CurTick - lastHitTick < hitbox.HitInterval)
                            continue;

                        hitbox.LastHitTicks[targetKvp.Key] = CurTick;
                    }
                    else
                    {
                        hitbox.LastHitTicks[targetKvp.Key] = CurTick;
                    }
                    hitTargets.Add(target);
                }
                else
                {
                    if (!hitbox.HitObjs.TryAdd(targetKvp.Key, 1))
                        continue;

                    hitTargets.Add(target);
                    HandlerInteraction(hitbox, target);
                }
            }
        }

        void HandleDamage<T>(Hitbox hitbox, List<T> hitTargets, Dictionary<int, Dictionary<int, float>> damageDict) where T : GameObject, new()
        {
            float totalDmg = 0;
            if (false == hitbox.Data.IsOneTimeUse) // 단일대상 히트박스가 아닌 경우
            {
                foreach (T target in hitTargets)
                {
                    float dmg = ApplyDamage(hitbox, target, damageDict);
                    if (hitbox.Omnivamp)
                        totalDmg += target.CalcFinalDamage(hitbox.Creature, dmg);
                }
            }
            else
            {
                T target = FindNearestTarget(hitbox, hitTargets);
                if (target == null) return;

                float dmg = ApplyDamage(hitbox, target, damageDict);
                if (hitbox.Omnivamp)
                    totalDmg += target.CalcFinalDamage(hitbox.Creature, dmg);

                if (hitbox.Creature is Monster)
                {
                    if (target is Player && target.Team != hitbox.Creature.MonsterTeam)
                        hitbox.IsUsed = true;
                }
                else
                     hitbox.IsUsed = true;
            }

            CheckAndApplyMonsterHit(hitbox, hitTargets);

            if (hitbox.Omnivamp)
                hitbox.AddDamage(totalDmg);
        }

       
        T FindNearestTarget<T>(Hitbox hitbox, List<T> targets) where T : GameObject, new()
        {
            T nearestTarget = null;
            float nearestDistSq = float.MaxValue;
            foreach (var target in targets)
            {
                float dx = target.PosInfo.PosX - hitbox.PosX;
                float dz = target.PosInfo.PosZ - hitbox.PosZ;
                float distSq = dx * dx + dz * dz;

                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearestTarget = target;
                }
            }
            return nearestTarget;
        }
       
        bool CheckCollision(Hitbox hitbox, GameObject go)
        {
            if (go.IsDead)
                return false;

            if (go.IsUntargetable())
                return false;

            if (go is Monster monster && monster.Info.Monster.MonsterType == MonsterType.Turret)
                return false; // 터렛은 피격 당하지 않음

            if (!System.Enum.TryParse<SkillShape>(hitbox.Data.Shape, out var shape))
                return false;

            if (hitbox.trackingHitbox != null)
            {
                if(CheckTrackingCollision(hitbox, go))
                    return true;
                return false;
            }

            switch (shape)
            {
                case SkillShape.Circle:
                    {
                        float dx = go.PosInfo.PosX - hitbox.PosX;
                        float dz = go.PosInfo.PosZ - hitbox.PosZ;
                        float distanceSq = dx * dx + dz * dz;

                        float hitboxRadius = hitbox.Data.Radius + hitbox.OffsetRadius;
                        float totalRadius = hitboxRadius + go.Radius;

                        return distanceSq <= totalRadius * totalRadius;
                    }
                case SkillShape.Rectangle:
                    {
                        Vector2 center = hitbox.MousePos;

                        Vector2 forward = Vector2.Normalize(new Vector2(
                            center.X - hitbox.Creature.PosInfo.PosX,
                            center.Y - hitbox.Creature.PosInfo.PosZ));

                        Vector2 right = new Vector2(-forward.Y, forward.X);

                        Vector2 toTarget = new Vector2(
                            go.PosInfo.PosX - center.X,
                            go.PosInfo.PosZ - center.Y);

                        float projForward = Vector2.Dot(toTarget, forward);
                        float projRight = Vector2.Dot(toTarget, right);

                        float halfHeight = hitbox.Data.Height * 0.5f;
                        float halfWidth = hitbox.Data.Width * 0.5f;

                        // 사각형 내부의 최근접점 찾기
                        float clampedForward = MathF.Max(-halfHeight, MathF.Min(projForward, halfHeight));
                        float clampedRight = MathF.Max(-halfWidth, MathF.Min(projRight, halfWidth));

                        // 최근접점에서 원 중심까지 거리 제곱 계산
                        float deltaForward = projForward - clampedForward;
                        float deltaRight = projRight - clampedRight;

                        float distSq = deltaForward * deltaForward + deltaRight * deltaRight;

                        // 거리 <= 반지름이면 충돌
                        return distSq <= go.Radius * go.Radius;
                    }
                case SkillShape.Point:
                    {
                        Vector2 center = new Vector2(hitbox.PosX, hitbox.PosZ);
                        Vector2 playerPos = new Vector2(hitbox.FixedPosition.X, hitbox.FixedPosition.Z);
                        Vector2 forward = Vector2.Normalize(center - playerPos);
                        Vector2 right = new Vector2(-forward.Y, forward.X);

                        Vector2 toTarget = new Vector2(go.PosInfo.PosX - center.X, go.PosInfo.PosZ - center.Y);

                        float projForward = Vector2.Dot(toTarget, forward);
                        float projRight = Vector2.Dot(toTarget, right);

                        float halfHeight = hitbox.Data.Height * 0.5f;
                        float halfWidth = hitbox.Data.Width * 0.5f;

                        float clampedForward = MathF.Max(-halfHeight, MathF.Min(projForward, halfHeight));
                        float clampedRight = MathF.Max(-halfWidth, MathF.Min(projRight, halfWidth));

                        float deltaForward = projForward - clampedForward;
                        float deltaRight = projRight - clampedRight;

                        float distSq = deltaForward * deltaForward + deltaRight * deltaRight;

                        return distSq <= go.Radius * go.Radius;
                    }

                case SkillShape.Ray:
                    {
                        Vector2 origin = new Vector2(hitbox.PosX, hitbox.PosZ);
                        Vector2 forward = Vector2.Normalize(hitbox.MousePos - origin);
                        Vector2 right = new Vector2(-forward.Y, forward.X);
                        Vector2 toTarget = new Vector2(go.PosInfo.PosX - origin.X, go.PosInfo.PosZ - origin.Y);

                        float projForward = Vector2.Dot(toTarget, forward);
                        float projRight = Vector2.Dot(toTarget, right);

                        if (!Enum.TryParse<SkillType>(hitbox.Data.Type, out SkillType type))
                            return false;

                        float range = hitbox.Data.MaxRange;
                        if (type == SkillType.SkillTrack)
                            range = hitbox.Data.MinRange + (hitbox.Data.MaxRange - hitbox.Data.MinRange) * hitbox.ChargeRatio;

                        float halfWidth = hitbox.Data.Width * 0.5f;

                        float clampedForward = MathF.Max(0f, MathF.Min(projForward, range));
                        float clampedRight = MathF.Max(-halfWidth, MathF.Min(projRight, halfWidth));

                        float deltaForward = projForward - clampedForward;
                        float deltaRight = projRight - clampedRight;
                        float distSq = deltaForward * deltaForward + deltaRight * deltaRight;

                        return distSq <= go.Radius * go.Radius;
                    }
                case SkillShape.Sector:
                    {
                        Vector2 center = new Vector2(hitbox.PosX, hitbox.PosZ);
                        Vector2 toTarget = new Vector2(go.PosInfo.PosX - center.X, go.PosInfo.PosZ - center.Y);

                        Vector2 mouseDir = Vector2.Normalize(new Vector2(hitbox.MousePos.X - center.X, hitbox.MousePos.Y - center.Y));
                        Vector2 mouseRightVec = new Vector2(mouseDir.Y, -mouseDir.X);


                        if (hitbox.Data.LookOffset != 0f || hitbox.Data.RightOffset != 0f)
                        {
                            center += mouseDir * hitbox.Data.LookOffset;
                            center += mouseRightVec * hitbox.Data.RightOffset;
                        }

                        toTarget = new Vector2(go.PosInfo.PosX - center.X, go.PosInfo.PosZ - center.Y);
                        float dist = toTarget.Length();
                        if (dist > hitbox.Data.Radius + go.Radius)
                            return false;

                        if (dist <= go.Radius)
                            return true;

                        
                        Vector2 targetDir = toTarget / dist;

                        float dot = Math.Clamp(Vector2.Dot(mouseDir, targetDir), -1f, 1f);
                        float angleRad = MathF.Acos(dot);

                        float halfAngleRad = (hitbox.Data.Angle * 0.5f) * (MathF.PI / 180f);
                        if (angleRad <= halfAngleRad)
                            return true;

                        float sin = MathF.Sin(halfAngleRad);
                        float cos = MathF.Cos(halfAngleRad);

                        Vector2 leftDir = new Vector2(mouseDir.X * cos - mouseDir.Y * sin, mouseDir.X * sin + mouseDir.Y * cos);
                        Vector2 rightDir = new Vector2(mouseDir.X * cos + mouseDir.Y * sin, -mouseDir.X * sin + mouseDir.Y * cos);

                        float leftDist = MathF.Abs(toTarget.X * leftDir.Y - toTarget.Y * leftDir.X);
                        float rightDist = MathF.Abs(toTarget.X * rightDir.Y - toTarget.Y * rightDir.X);

                        return (leftDist <= go.Radius || rightDist <= go.Radius);
                    }
            }
            return false;
        }

        float ApplyDamage(Hitbox hitbox, GameObject target, Dictionary<int, Dictionary<int, float>> damageDict)
        {
            float dmg = 0f;
            if (hitbox.Creature is Player)
            {
                dmg = CalcDamage(hitbox.Creature, target.Stat, hitbox.KeyCode);
            }
            else if (hitbox.Creature is Monster mc)
            {
                dmg = mc.CalcDamage(hitbox.Creature, target as Creature);
            }


            if (target is Player)
            {
                Console.WriteLine($"Attacker:{hitbox.CharType}_{hitbox.Creature.Id}, Target:{target.Info.Player.CharType}_{target.Id}, Damage:{dmg}"); 
            }
            else if (target is Monster)
            {
                Monster monster = target as Monster;
                if (monster != null)
                    monster.OnHit(hitbox.Creature);
                Console.WriteLine($"Attacker:{hitbox.CharType}_{hitbox.Creature.Id}, Target:{target.Info.Monster.MonsterType}_{target.Id}, Damage:{dmg}");
            }
            else
                Console.WriteLine($"Attacker:{hitbox.CharType}_{hitbox.Creature.Id}, Target:Env_{target.Id}, Damage:{dmg}");

            if (damageDict.ContainsKey(target.Id))
            {
                if (damageDict[target.Id].ContainsKey(hitbox.Creature.Id))
                {
                    damageDict[target.Id][hitbox.Creature.Id] += dmg;
                }
                else
                    damageDict[target.Id][hitbox.Creature.Id] = dmg;
            }
            else
            {
                damageDict[target.Id] = new Dictionary<int, float>();
                damageDict[target.Id][hitbox.Creature.Id] = dmg;
            }
            hitbox.HitObjs.TryAdd(target.Id, 0);

            // 공격자 및 피격자 정보 필요
            

            return dmg;
        }

        public float CalcDamage(Creature attacker, Player target, KeyCode keyCode)
        {
            // 플레이어가 플레이어 때릴 때
            StatInfo info = target.Stat.Clone();
            info.Defense = target.Defense;
            info.MaxHp = target.MaxHp;
            return CalcDamage(attacker, info, keyCode);
        }

        public float CalcDamage(Creature attacker, StatInfo target, KeyCode keyCode)
        {
            // TODO 버프 디버프 정보도 가지고 와야함. 예를 들면 방깍 디버프 같은거 
            Player playerAttacker = attacker as Player;
            if (playerAttacker == null) return 0f;

            Skill skill = playerAttacker.GetSkill(keyCode);

            float damage = skill.GetSkillDamage()
                + skill.SkillData.scaling.adRatio * playerAttacker.Attack * 0.01f
                + skill.SkillData.scaling.apRatio * playerAttacker.SkillAmplification * 0.01f
                + skill.SkillData.scaling.dstCurHpRatio * target.Hp * 0.01f
                + skill.SkillData.scaling.dstMaxHpRatio * target.MaxHp * 0.01f
                + skill.SkillData.scaling.srcCurHpRatio * playerAttacker.Hp * 0.01f
                + skill.SkillData.scaling.srcMaxHpRatio * playerAttacker.MaxHp * 0.01f;

            // charging skill
            if(playerAttacker.Info.Player.CharType == CharacterType.Hyunwoo && keyCode == KeyCode.R)
            {
                // lerp with Charging Ratio and Charging Coeff
                damage = damage * (1 + playerAttacker.ChargingRatio * skill.SkillData.mechanics.chargeCoefficient);
            }

            // 그냥 예시 : 추가 데미지를 입힐 시에
            if (playerAttacker.IsSkillAmplification)
                damage += skill.GetSkillBonusDamage();

            float result = damage;

            return result;
        }

        void SendChangeHpPkts(ConcurrentDictionary<int, ConcurrentDictionary<int, Player>> teams, Dictionary<int, Dictionary<int, float>> damageDict)
        {
            foreach (var kvp in damageDict)
            {
                GameObject hitTarget = ObjectManager.Instance.Find(kvp.Key);
                if (hitTarget == null)
                    continue;

                foreach (var attakerKvp in kvp.Value)
                {
                    GameObject attacker = ObjectManager.Instance.Find(attakerKvp.Key);
                    if (attacker == null)
                        continue;

                    float damage = attakerKvp.Value;
                    hitTarget.Room.Push(hitTarget.OnDamaged, attacker, damage, false, false);
                }
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                foreach (Hitbox pendingHitbox in _pendingHitboxes)
                {
                    if (!_hitboxDict.TryGetValue(pendingHitbox.Creature.Id, out var set))
                    {
                        set = new HashSet<Hitbox>();
                        _hitboxDict[pendingHitbox.Creature.Id] = set;
                    }

                    pendingHitbox.StartTick = CurTick + (int)((pendingHitbox.Data.StartFrame / (float)pendingHitbox.Data.Fps) * 1000);
                    pendingHitbox.EndTick = CurTick + (int)((pendingHitbox.Data.EndFrame / (float)pendingHitbox.Data.Fps) * 1000);


                    set.Add(pendingHitbox);
                }
                _pendingHitboxes.Clear();
            }
        }

        void SetUpAllyHitSkills() // 아군 대상 스킬 
        {
            foreach(var nestedKvp in DataManager.SkillDict)
            {
                foreach(var kvp in nestedKvp.Value)
                {
                    if(kvp.Value.levels.TryGetValue(1, out SkillLevel skillLevel))
                    {
                        if (skillLevel.effects == null)
                            continue;

                        foreach (EffectData effect in skillLevel.effects)
                        {
                            if (effect.condition == "AllyHit") // 아군 적중
                            {
                                if (!_allyHitSkillDict.ContainsKey(nestedKvp.Key))
                                    _allyHitSkillDict[nestedKvp.Key] = new HashSet<KeyCode>();

                                _allyHitSkillDict[nestedKvp.Key].Add(kvp.Key); // Key: CharactorType, Value: Keycode
                            }
                        }
                    }
                }
            }
        }

        void HandleAllyHit(Hitbox hitbox, ConcurrentDictionary<int, Player> targets, bool isBusy = false)
        {
            if (!_allyHitSkillDict.TryGetValue(hitbox.CharType, out HashSet<KeyCode> keySet))
                return;
            if (!keySet.Contains(hitbox.KeyCode))
                return;

            foreach (var targetKvp in targets)
            {
                Player target = targetKvp.Value;
                 if (!isBusy && (hitbox.HitObjs.ContainsKey(targetKvp.Key) || true == hitbox.IsUsed))
                    continue;

                if (CheckCollision(hitbox, target))
                {
                    Player skillOwner = hitbox.Creature as Player;
                    Skill skill = skillOwner.GetSkill(hitbox.KeyCode);
                    SkillLevel skillLevel = skill.SkillData.levels[skill.CurLevel];
                    if (skillLevel.effects.Count == 0)
                        continue;

                    foreach (EffectData effect in skillLevel.effects)
                    {
                        if (isBusy && target.FindStatStatusEffect(effect.stat))
                            continue;

                        StatusEffect newEffect = new StatusEffect
                        {
                            type = effect.type,
                            stat = effect.stat,
                            duration = effect.duration,
                            value = effect.value,
                            subject = Enum.TryParse(effect.subject, true, out Subject temp) ? temp : Subject.Subject_None,
                            valueType = Enum.TryParse(effect.valueType, true, out ValueType type) ? type : ValueType.ValueType_None,
                            coeff = effect.coeff,
                            ratioPerTarget = effect.ratioPerTarget,
                            maxRatio = effect.maxRatio
                        };

                        if (effect.type == "Heal")
                        {
                            target.Room.Push(target.OnHeal, target, effect.value);
                            target.SendSoundPacket("SKILL_HEAL");
                        }
                        else if (effect.type == "Buff")
                        {
                            target.Room.Push(target.AddStatusEffect, newEffect);
                            
                            if(hitbox.CharType == CharacterType.Theodore)
                                target.SendSkillEffect(default(Vector2), KeyCode.R, type : "Select", name : "FX_Skill04_Buff");
                        }
                    }

                    hitbox.HitObjs.TryAdd(target.Id, 0);
                }
            }
        }

        #region StatusEffects(버프, 디버프, 방어막)
        void SetUpStatusEffects() // 조건이 Hit인 효과들만 추려내기
        {
            foreach (var nestedKvp in DataManager.SkillDict)
            {
                foreach (var kvp in nestedKvp.Value)
                {
                    foreach(var levelKvp in kvp.Value.levels)
                    {
                        if (levelKvp.Value.effects == null)
                            continue;

                        foreach(EffectData effectData in levelKvp.Value.effects)
                        {
                            CharacterType charType = nestedKvp.Key;
                            KeyCode keyCode = kvp.Key;
                            int level = levelKvp.Key;

                            if (effectData.condition == "Hit")
                            {
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
                                    maxRatio = effectData.maxRatio
                                };

                                effects.Add(newEffect);
                            }
                        }
                    }
                }
            }
        }

        void HandleStatusEffects<T>(Hitbox hitbox, List<T> hitTargets) where T : GameObject, new ()
        {
            if (!(hitbox.Creature is Player))
                return;

            if (!_statusEffects.TryGetValue(hitbox.CharType, out var nestedDict))
                return;

            if (!nestedDict.TryGetValue(hitbox.KeyCode, out var dict))
                return;

            Player player = hitbox.Creature as Player;

            if (!dict.TryGetValue(player.GetSkillLevel(hitbox.KeyCode), out var statusEffectList))
                return;

            foreach (var effect in statusEffectList)
            {
                effect.targetCnt = hitTargets.Count;
                effect.attacker = hitbox.Creature;
                int cnt = hitbox.effectCnt.AddOrUpdate(effect, 1, (_, oldValue) => oldValue + 1);

                switch (effect.subject)
                {
                    case Subject.Self:
                        if (effect.type == "OnCollisionSingleTarget")
                            player.Room.Push(player.Room.CallOnCollision, player, FindNearestTarget(hitbox, hitTargets), effect);
                        else if(effect.type == "OnCollisionMultiTarget")
                            player.Room.Push(player.Room.CallOnCollision, player, hitTargets, effect);
                        else
                            player.Room.Push(player.AddStatusEffect, effect);
                        break;
                    case Subject.Ally: // 이건 아군대상 스킬에만 있을거같긴해서 생략
                        break;
                    case Subject.Enemy:
                        foreach(var enemy in hitTargets.OfType<Creature>()) 
                            enemy.Room.Push(enemy.AddStatusEffect, effect); 
                        break;
                    case Subject.T:
                        if(cnt == 1 && effect.type == "CDR") // 쿨타임 감소는 딱 한번만 발생해야 함
                            player.Skill.Reduce(KeyCode.T, effect.value, effect.valueType == ValueType.Ratio);
                        break;
                    case Subject.Q:
                        if (cnt == 1 && effect.type == "CDR")
                            player.Skill.Reduce(KeyCode.Q, effect.value, effect.valueType == ValueType.Ratio);
                        break;
                    case Subject.W:
                        if (cnt == 1 && effect.type == "CDR")
                            player.Skill.Reduce(KeyCode.W, effect.value, effect.valueType == ValueType.Ratio);
                        break;
                }
            }
        }
        #endregion

        #region 추가
        public Hitbox AddHitbox(Creature creature, MonsterSkill skilltype, Vector2 targetPos = new Vector2(), float chargeRatio = 0)
        {
            Hitbox hitbox = null;
            lock (_lock)
            {
                if (!DataManager.MonstSkillHitboxDict.ContainsKey(creature.Info.Monster.MonsterType))
                    return null;
                if (!DataManager.MonstSkillHitboxDict[creature.Info.Monster.MonsterType].ContainsKey(skilltype))
                    return null;

                SkillHitbox skillHitbox = DataManager.MonstSkillHitboxDict[creature.Info.Monster.MonsterType][skilltype];
                if (skillHitbox.EndFrame <= 0)
                    return null;

                var quat = creature.RotInfo.GetQuatFromRotInfo();
                Vector3 LocalForward = new Vector3(0, 0, 1);
                Vector3 forward3D = Vector3.Transform(LocalForward, quat);
                Vector2 forward = new Vector2(forward3D.X, forward3D.Z);

                hitbox = new Hitbox
                {
                    Creature = creature,
                    Team = creature.MonsterTeam,
                    PosX = creature.PosInfo.PosX,
                    PosZ = creature.PosInfo.PosZ,
                    ChargeRatio = chargeRatio,
                    MonstType = creature.Info.Monster.MonsterType,
                    Data = skillHitbox,
                    MousePos = forward,
                    MonsterSkillType = skilltype,
                    Interactions = ConvertProtoInteractionsToKeyCodeDictionary(skillHitbox.Interactions)
                };

                UpdateTransformRay(hitbox);
                SettingType(hitbox, targetPos);

                _pendingHitboxes.Add(hitbox);
            }
            return hitbox;
        }

        void SettingType(Hitbox hitbox, Vector2 targetPos = new Vector2())
        {
            if (System.Enum.TryParse<SkillShape>(hitbox.Data.Shape, out var shape))
            {
                if (shape == SkillShape.Point)
                {
                    hitbox.FixedPosition = hitbox.Creature.PosInfo.ToVector();
                    hitbox.PosX = hitbox.MousePos.X;
                    hitbox.PosZ = hitbox.MousePos.Y;
                }
            }
            
            if (System.Enum.TryParse<SkillType>(hitbox.Data.Type, out SkillType type))
            {
                if (type == SkillType.SkillTargeting)
                {
                    hitbox.PosX = targetPos.X;
                    hitbox.PosZ = targetPos.Y;
                }
            }
        }
        
        private void UpdatePosProjectile(Hitbox hitbox)
        {
            Quaternion rot = hitbox.Creature.RotInfo.GetQuatFromRotInfo();

            Vector3 toForward = Vector3.Transform(new Vector3(0, 0, 1), rot);
            const float TickInterval = 1.0f / 70.0f;
            float deltaMove = hitbox.Data.Speed * TickInterval;

            hitbox.PosX += toForward.X * deltaMove;
            hitbox.PosZ += toForward.Z * deltaMove;
        }
        bool CheckCollision(Hitbox myHitbox, Hitbox targetHitbox)
        {
            if (!System.Enum.TryParse<SkillShape>(myHitbox.Data.Shape, out var shape))
                return false;

            switch (shape)
            {
                case SkillShape.Circle:
                    {
                        Vector2 circleCenter = new Vector2(myHitbox.PosX, myHitbox.PosZ);
                        float circleRadius = myHitbox.Data.Radius + myHitbox.OffsetRadius;

                        Vector2 pointCenter = new Vector2(targetHitbox.PosX, targetHitbox.PosZ);
                        Vector2 pointPrevPos = new Vector2(targetHitbox.FixedPosition.X, targetHitbox.FixedPosition.Z);

                        float actualDist = Vector2.Distance(circleCenter, pointCenter);

                        Vector2 direction = pointCenter - pointPrevPos;

                        Vector2 forward = Vector2.Normalize(direction);
                        Vector2 right = new Vector2(-forward.Y, forward.X);

                        float halfHeight = targetHitbox.Data.Height * 0.5f;
                        float halfWidth = targetHitbox.Data.Width * 0.5f;

                        Vector2 toCircle = circleCenter - pointCenter;

                        float projForward = Vector2.Dot(toCircle, forward);
                        float projRight = Vector2.Dot(toCircle, right);

                        float clampedForward = MathF.Max(-halfHeight, MathF.Min(projForward, halfHeight));
                        float clampedRight = MathF.Max(-halfWidth, MathF.Min(projRight, halfWidth));

                        float deltaForward = projForward - clampedForward;
                        float deltaRight = projRight - clampedRight;
                        float distSq = deltaForward * deltaForward + deltaRight * deltaRight;

                        return distSq <= circleRadius * circleRadius;
                    }
                case SkillShape.Rectangle:
                case SkillShape.Point:
                case SkillShape.Ray:
                    return CheckPointRayCollision(targetHitbox, myHitbox);

                case SkillShape.Sector:
                    {
                        Vector2 center = new Vector2(myHitbox.PosX, myHitbox.PosZ);
                        Vector2 toTarget = new Vector2(targetHitbox.PosX - center.X, targetHitbox.PosZ - center.Y);

                        if (toTarget.LengthSquared() > myHitbox.Data.Radius * myHitbox.Data.Radius)
                            return false;

                        Vector2 mouseDir = Vector2.Normalize(new Vector2(myHitbox.MousePos.X - center.X, myHitbox.MousePos.Y - center.Y));
                        Vector2 targetDir = Vector2.Normalize(toTarget);

                        float dot = Math.Clamp(Vector2.Dot(mouseDir, targetDir), -1f, 1f);
                        float angleDeg = MathF.Acos(dot) * (180f / MathF.PI);

                        return angleDeg <= myHitbox.Data.Angle * 0.5f;
                    }
            }

            return false;
        }

        // 충돌체 끼리의 충돌
        void CheckCollisionHit()
        {
            List<Hitbox> allHitboxes = new List<Hitbox>();
            foreach (HashSet<Hitbox> hitboxSet in _hitboxDict.Values)
                allHitboxes.AddRange(hitboxSet);

            for (int i = 0; i < allHitboxes.Count; i++)
            {
                for (int j = i + 1; j < allHitboxes.Count; j++)
                {
                    Hitbox hitboxA = allHitboxes[i];
                    Hitbox hitboxB = allHitboxes[j];

                    if (hitboxA.InteractedHitboxes.Contains(hitboxB) || hitboxB.InteractedHitboxes.Contains(hitboxA))
                        continue;

                    if (!hitboxA.IsInteracted || !hitboxB.IsInteracted)
                        continue;

                    // point가 B여야 함
                    if (!System.Enum.TryParse<SkillShape>(hitboxA.Data.Shape, out var shapeA))
                        continue;
                    if (!System.Enum.TryParse<SkillShape>(hitboxB.Data.Shape, out var shapeB))
                        continue;

                    if (shapeB != SkillShape.Point && shapeA == SkillShape.Point)
                    {
                        Hitbox tmp = hitboxA;
                        hitboxA = hitboxB;
                        hitboxB = tmp;
                    }
                    else if (shapeB != SkillShape.Point && shapeA != SkillShape.Point)
                        continue;

                    if (CheckCollision(hitboxA, hitboxB))
                        HandlerInteraction(hitboxA, hitboxB);
                }
            }
        }

        bool CheckPointRayCollision(Hitbox pointHitbox, Hitbox rayHitbox)
        {
           // OBB A 설정
           Vector2 centerA = new Vector2(pointHitbox.PosX, pointHitbox.PosZ);
            Vector2 fixedPlayerPos = new Vector2(pointHitbox.FixedPosition.X, pointHitbox.FixedPosition.Z);
           Vector2 forwardA = Vector2.Normalize(centerA - fixedPlayerPos);
           Vector2 rightA = new Vector2(-forwardA.Y, forwardA.X);
           float halfHeightA = pointHitbox.Data.Height * 0.5f;
           float halfWidthA = pointHitbox.Data.Width * 0.5f;

           // OBB B 설정
           Vector2 centerB = new Vector2(rayHitbox.PosX, rayHitbox.PosZ);  
           Vector2 forwardB = Vector2.Normalize(rayHitbox.MousePos - centerB);
           Vector2 rightB = new Vector2(-forwardB.Y, forwardB.X);

           float rangeB = rayHitbox.Data.Height;
           if (System.Enum.TryParse<SkillType>(rayHitbox.Data.Type, out SkillType type))
           {
               if (type == SkillType.SkillTrack)
                   rangeB = rayHitbox.Data.MinRange + (rayHitbox.Data.MaxRange - rayHitbox.Data.MinRange) * rayHitbox.ChargeRatio;
           }
           float halfHeightB = rangeB * 0.5f;
           float halfWidthB = rayHitbox.Data.Width * 0.5f;

           // OBB 충돌 검사
           Vector2 toTarget = centerB - centerA;

           // A forward 
           float projCenterA1 = Vector2.Dot(toTarget, forwardA);
            // 내적의 경우 음수가 나올 수 있기 때문에
           float projRadiusA1 = halfHeightA + MathF.Abs(Vector2.Dot(forwardA, forwardB)) * halfHeightB + MathF.Abs(Vector2.Dot(forwardA, rightB)) * halfWidthB;
           if (MathF.Abs(projCenterA1) > projRadiusA1) return false;

           // A right 
           float projCenterA2 = Vector2.Dot(toTarget, rightA);
           float projRadiusA2 = halfWidthA + MathF.Abs(Vector2.Dot(rightA, forwardB)) * halfHeightB + MathF.Abs(Vector2.Dot(rightA, rightB)) * halfWidthB;
           if (MathF.Abs(projCenterA2) > projRadiusA2) return false;

           // B forward 
           float projCenterB1 = Vector2.Dot(toTarget, forwardB);
           float projRadiusB1 = halfHeightB + MathF.Abs(Vector2.Dot(forwardB, forwardA)) * halfHeightA + MathF.Abs(Vector2.Dot(forwardB, rightA)) * halfWidthA;
           if (MathF.Abs(projCenterB1) > projRadiusB1) return false;

           // B right 
           float projCenterB2 = Vector2.Dot(toTarget, rightB);
           float projRadiusB2 = halfWidthB + MathF.Abs(Vector2.Dot(rightB, forwardA)) * halfHeightA + MathF.Abs(Vector2.Dot(rightB, rightA)) * halfWidthA;
           if (MathF.Abs(projCenterB2) > projRadiusB2) return false;

           return true;
        }

        // 몬스터에게 맞았다면 처리
        void CheckAndApplyMonsterHit<T>(Hitbox hitbox, List<T> hitTargets)
        {
            if (hitbox.Creature is Monster)
            {
                Monster monster = hitbox.Creature as Monster;
                if (monster != null)
                {
                    foreach (T target in hitTargets)
                    {
                        Player p = target as Player;
                        if (p != null)
                            monster.OnTargetHit(p);
                    }
                }
            }
        }

        private readonly object _hitboxesLock = new object();
        public void AddInteractedHitbox(Hitbox other, Hitbox my)
        {
            lock (_hitboxesLock) 
            {
                my.InteractedHitboxes.Add(other);
            }
        }
        void HandlerInteraction(Hitbox hitboxA, Hitbox hitboxB)
        {
            lock (_hitboxesLock)
            {
                AddInteractedHitbox(hitboxB, hitboxA);
                AddInteractedHitbox(hitboxA, hitboxB);
            }

            _interactionManager.HandleInteraction(hitboxA, hitboxB);
            _interactionManager.HandleInteraction(hitboxB, hitboxA);
        }

        bool CheckTrackingCollision(Hitbox hitbox, GameObject go)
        {
            if (!System.Enum.TryParse<SkillShape>(hitbox.Data.Shape, out var shape))
                return false;

            switch (shape)
            {
                case SkillShape.Ray: // Theodore Ray - Point 개인용
                    {
                        Vector2 myPosition = hitbox.trackingHitbox.MousePos;
                        Vector2 fixedtoTarget = new Vector2(hitbox.FixedPosition.X, hitbox.FixedPosition.Z);
                        Vector2 direction = hitbox.trackingHitbox.MousePos - new Vector2(fixedtoTarget.X, fixedtoTarget.Y);

                        if (direction.LengthSquared() < 0.0001f)
                            return false;

                        Vector2 forward = Vector2.Normalize(direction);
                        Vector2 right = new Vector2(-forward.Y, forward.X);
                        Vector2 toTarget = new Vector2(go.PosInfo.PosX - myPosition.X, go.PosInfo.PosZ - myPosition.Y);
                        float projForward = Vector2.Dot(toTarget, forward);
                        float projRight = Vector2.Dot(toTarget, right);

                        if (!Enum.TryParse<SkillType>(hitbox.Data.Type, out SkillType type))
                            return false;

                        float range = hitbox.Data.MaxRange;
                        if (type == SkillType.SkillTrack)
                            range = hitbox.Data.MinRange + (hitbox.Data.MaxRange - hitbox.Data.MinRange) * hitbox.ChargeRatio;

                        float halfWidth = hitbox.Data.Width * 0.5f;
                        float clampedForward = MathF.Max(0f, MathF.Min(projForward, range));
                        float clampedRight = MathF.Max(-halfWidth, MathF.Min(projRight, halfWidth));
                        float deltaForward = projForward - clampedForward;
                        float deltaRight = projRight - clampedRight;
                        float distSq = deltaForward * deltaForward + deltaRight * deltaRight;

                        return distSq <= go.Radius * go.Radius;
                    }
            }
            return false;
        }

        void HandlerInteraction(Hitbox hitbox, GameObject target)
        {
            _interactionManager.HandleInteraction(hitbox, target);
        }

        private void UpdateTransformRay(Hitbox hitbox)
        {
            if (!System.Enum.TryParse<SkillShape>(hitbox.Data.Shape, out var shape))
                return;

            if (shape != SkillShape.Ray)
                return;

            if (CurTick < hitbox.StartTick)
                return;

            //if (hitbox.MonsterSkillType == MonsterSkill.MsGammaSkill2)
            {
                Quaternion rot = hitbox.Creature.RotInfo.GetQuatFromRotInfo();
                Vector3 localForward = new Vector3(0, 0, 1);
                Vector3 forward3D = Vector3.Transform(localForward, rot);

                Vector2 currentForward = new Vector2(forward3D.X, forward3D.Z);
                Vector2 origin = new Vector2(hitbox.Creature.PosInfo.PosX, hitbox.Creature.PosInfo.PosZ);

                hitbox.MousePos = origin + currentForward * hitbox.Data.MaxRange;
                hitbox.PosX = origin.X;
                hitbox.PosZ = origin.Y;
            }
        }
        #endregion

        #region 사운드 패킷
        void BroadcastHitSoundPkts(List<Hitbox> hitboxes)
        {
            foreach (Hitbox hitbox in hitboxes)
            {
                switch (hitbox.CharType)
                {
                    case CharacterType.Abigail:
                        if (!_abigailSoundDict.TryGetValue(hitbox.KeyCode, out AbigailSound sound))
                            break;
                        GameRoom room = hitbox.Creature.Room;
                        room.Push(room.BroadcastAbigailSound, hitbox.Creature as Player, sound, 1f);
                        break;
                }
            }
        }
        #endregion

        #region Rozzi Projectile
        bool HandleRozziRHitbox<T>(Hitbox hitbox,
                           List<T> hitTargets,
                           int ownerId,
                           bool isPlayerTarget)   // true: Player, false: Monster
        where T : GameObject
        {
            if (hitbox.CharType != CharacterType.Rozzi)
                return false;

            Projectile_Rozzi_R pj = hitbox.Creature.Room.FindProjectile(hitbox.Creature, ProjectileType.ProjectileRozziR) as Projectile_Rozzi_R;

            if (pj != null)
            {
                // 1) R 스킬: 투사체가 처음 맞은 대상만 처리, 이후 공통 데미지/상태는 스킵
                if (hitbox.KeyCode == KeyCode.R)
                {
                    foreach (var target in hitTargets)
                    {
                        if (target.Id != ownerId)
                        {
                            pj.OnProjectileHit(target);
                            break;
                        }
                    }

                    return true;
                }

                // 2) 그 외: Owner 에게 돌아오는 히트 처리
                GameObject baseTarget = pj.Target;
                if (baseTarget != null)
                {
                    // isPlayerTarget 플래그는 필요하다면 로직 분기용으로 쓸 수 있음
                    // 캐스팅은 T에 맡기기 때문에 둘 다 동일 코드로 처리 가능
                    T typedTarget = baseTarget as T;

                    if (typedTarget != null && hitTargets.Contains(typedTarget))
                    {
                        pj.RegisterOwnerHit(isSkillHit: true);
                    }
                }
            }

            // 3) F2 → R 치환
            if (hitbox.KeyCode == KeyCode.F2)
                hitbox.KeyCode = KeyCode.R;

            return false; // 이후 데미지/상태이상 계속 처리
        }
        #endregion
    }
}
