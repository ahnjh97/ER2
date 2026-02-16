using Data;
using Google.Protobuf;
using Google.Protobuf.Protocol;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Data.EffectData;

class PacketHandler
{
    public static void S_LoadGameSceneHandler(PacketSession session, IMessage packet)
    {
        LoadingManager.Instance.LoadScene(Define.Scene.Game);
    }

	public static void S_EnterGameHandler(PacketSession session, IMessage packet)
	{
        if (!IsSceneReady("Game", () => S_EnterGameHandler(session, packet))) return;
        S_EnterGame enterGamePacket = packet as S_EnterGame;
        Managers.Object.Add(enterGamePacket.ObjInfo, myPlayer: true);
    }

    public static void S_LeaveGameHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_LeaveGameHandler(session, packet))) return;
        S_LeaveGame leaveGamePacket = packet as S_LeaveGame;
        Managers.Object.Clear();
    }

    public static void S_SpawnHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SpawnHandler(session, packet))) return;
        S_Spawn spawnPacket = packet as S_Spawn;
        foreach (ObjectInfo obj in spawnPacket.Objects)
        {
            Managers.Object.Add(obj, myPlayer: false);
        }
    }

    public static void S_DespawnHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_DespawnHandler(session, packet))) return;
        S_Despawn despawnPacket = packet as S_Despawn;
        foreach (int id in despawnPacket.ObjectIds)
        {
            Managers.Object.Remove(id);
        }
    }

    public static void S_MoveHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_MoveHandler(session, packet))) return;
        S_Move mPacket = packet as S_Move;
        ServerSession serverSession = session as ServerSession;

        GameObject go = Managers.Object.FindById(mPacket.ObjectId);
        if (go == null)
            return;

        if (Managers.Object.MyPlayer.Id != mPacket.ObjectId)
        {
            BaseController bc = go.GetComponentInChildren<BaseController>();
            if (bc == null)
                return;
            GameObjectType objectType = ObjectManager.GetObjectTypeById(bc.Id);
            if (objectType == GameObjectType.Player)
            {
                PlayerController pc = go.GetComponentInChildren<PlayerController>();
                if (pc == null)
                    return;

                pc.SyncPosFromServer(mPacket);
            }
            else
            {
                bc.transform.position = mPacket.PosInfo.ToVector();
                bc.transform.rotation = mPacket.RotInfo;
            }
                
            bc.PosInfo = mPacket.PosInfo;
            bc.RotInfo = mPacket.RotInfo;
        }     
    }

    public static void S_TargetChangeHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_TargetChangeHandler(session, packet))) return;
        S_TargetChange targetChangePacket = packet as S_TargetChange;
        ServerSession serverSession = session as ServerSession;

        Managers.Object.MyPlayer.View.RotateAttack(targetChangePacket.TargetId);
    }

    public static void S_SetMoveTargetHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SetMoveTargetHandler(session, packet))) return;
        S_SetMoveTarget targetPacket = packet as S_SetMoveTarget;
        ServerSession serverSession = session as ServerSession;

        if (Managers.Object.MyPlayer.Id == targetPacket.Id)
        {
            Managers.Object.MyPlayer.OnServerUpdate(targetPacket);
        }
    }

    public static void S_StateHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_StateHandler(session, packet))) return;
        S_State skillPacket = packet as S_State;
        if (skillPacket == null)
            return;

        GameObject go = Managers.Object.FindById(skillPacket.ObjectId);
        if (go == null)
        {
            return;
        }

        MonsterController mc = go.GetComponentInChildren<MonsterController>();
        if (mc != null)
            mc.OnRecvStatePacket(skillPacket);  
    }

    public static void S_SkillHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SkillHandler(session, packet))) return;
        S_Skill skillPacket = packet as S_Skill;

        GameObject go = Managers.Object.FindById(skillPacket.ObjectId);
        if (go == null)
            return;

        CreatureController cc = go.GetComponentInChildren<CreatureController>();
        if (cc != null)
        {
            GameObjectType objectType = ObjectManager.GetObjectTypeById(cc.Id);
            if (objectType == GameObjectType.Player)
            {
                cc.UseSkill(skillPacket);
            }
        }
    }

    public static void S_AnimHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_AnimHandler(session, packet))) return;
        S_Anim animPacket = packet as S_Anim;

        GameObject go = Managers.Object.FindById(animPacket.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponent<PlayerController>();
        if (pc != null)
        {
            pc.PlayAnimFromServer(animPacket.AnimInfo);
        }
    }
    
    public static void S_ChangeHpHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChangeHpHandler(session, packet))) return;
        S_ChangeHp changePacket = packet as S_ChangeHp;

        GameObject go = Managers.Object.FindById(changePacket.ObjectId);
        if (go == null)
            return;

        CreatureController cc = go.GetComponentInChildren<CreatureController>();
        if (cc != null)
        {
            cc.Hp = changePacket.Hp;
            cc.Barrier = changePacket.Barrier;

            //foreach(var v in changePacket.Damages)
            //    Managers.CombatText.SetCombatText(CombatTextManager.TextType.AdDamage, v.Damage, cc.transform.position);
        }
    }

    public static void S_DieHandler(PacketSession session, IMessage packet)
    {
        S_Die diePacket = packet as S_Die;

        GameObject go = Managers.Object.FindById(diePacket.ObjectId);
        if (go == null)
            return;

        CreatureController cc = go.GetComponent<CreatureController>();
        if (cc != null)
        {
            cc.Hp = 0;
            cc.OnDead();
        }
        
        if (Managers.Object.MyPlayer.Sound)
            Managers.Object.MyPlayer.Sound.GetRandomVoice("Player_Kill");

        if (Managers.Object.MyPlayer != null)
        {
            if (Managers.Object.MyPlayer.Id == diePacket.ObjectId)
            {
                go.GetComponentInChildren<MyPlayerController>().UI.PlayerInterface.OnDead(diePacket.RespawnTime);
            }

            // 죽은 플레이어
            PlayerController pc = cc as PlayerController;
            if (pc == null)
                return;

            // 공격 플레이어
            GameObject attackerGo = Managers.Object.FindById(diePacket.AttackerId);
            if (attackerGo == null)
                return;

            PlayerController attPc = attackerGo.GetComponentInChildren<PlayerController>();
            if (attPc == null)
                return;

            if (attPc.Sound != null)
            {
                attPc.CurrentMultiKillCnt++;
                attPc.Sound.GetRandom3DVoice($"Kill{attPc.CurrentMultiKillCnt}", attPc.transform.position);
            }
            Managers.Object.MyPlayer.UI.NotifyKill(attPc, pc); 
        }
    }

    public static void S_CharacterHandler(PacketSession session, IMessage packet)
    {
        S_Character charPacket = packet as S_Character;

        GameObject go = GameObject.Find("PickScene");
        if (go == null) return;

        PickScene pickScene = go.GetComponent<PickScene>();
        if (pickScene == null) return;

        pickScene.ChangePickImage(charPacket.CharType, charPacket.PickIdx);
    }
    public static void S_TraitHandler(PacketSession session, IMessage packet)
    {
        S_Trait traitPacket = packet as S_Trait;

        GameObject go = GameObject.Find("PickScene");
        if (go == null) return;

        PickScene pickScene = go.GetComponent<PickScene>();
        if (pickScene == null) return;

        pickScene.ChangeTraitImage(traitPacket.TraitType, traitPacket.PickIdx);
    }

    public static void S_InteractHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_InteractHandler(session, packet))) return;
        S_Interact interactPacket = packet as S_Interact;

        GameObject go = Managers.Object.FindById(interactPacket.ObjectId);
        if (go == null) return;

        CreatureController creature = go.GetComponentInChildren<CreatureController>();
        if (creature == null) return;

        GameObjectType objectType = ObjectManager.GetObjectTypeById(creature.Id);

        // Hitbox 충돌
       if (objectType == GameObjectType.Player)
       {
           KeyCode mkey = (KeyCode)interactPacket.KeyCode;
           KeyCode tKey = (KeyCode)interactPacket.TargetKeyCode;
           creature.OnHitboxCollision(mkey, tKey);

            PlayerController pc = creature.GetComponentInChildren<PlayerController>();
            if (pc == null)
                return;

            if (pc.Sound != null) // 테오도르 WQ skill 사운드
            {
                if(tKey == KeyCode.Q)
                { 
                    pc.Sound.GetEffect3D("SKILL_WQ", pc.transform.position); 
                }
                else if(tKey == KeyCode.E)
                {
                    GameObject effect = Managers.FX.Effect.FindCurrentPlayEffect(pc.Id, "FX_Skill03_Shield");
                    if(effect)
                        effect.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                    pc.Sound.GetEffect3D("SKILL_WE", pc.transform.position); 
                }
            }
        }
    }

    public static void S_WeaponHandler(PacketSession session, IMessage packet)
    {
        S_Weapon weaponPacket = packet as S_Weapon;

        GameObject go = GameObject.Find("PickScene");
        if (go == null) return;

        PickScene pickScene = go.GetComponent<PickScene>();
        if (pickScene == null) return;

        pickScene.ChangeWeaponImage(weaponPacket.WeaponType, weaponPacket.PickIdx);
    }

    public static void S_EnterPickHandler(PacketSession session, IMessage packet)
    {
        LoadingManager.Instance.LoadScene(Define.Scene.Pick);

        S_EnterPick enterPickPacket = packet as S_EnterPick;
        Managers.Info.PickIdx = enterPickPacket.PickIdx;
        Managers.Info.Team = enterPickPacket.Team;
    }

    public static void S_SpawnPickHandler(PacketSession session, IMessage packet)
    {
        S_SpawnPick spawnPickPacket = packet as S_SpawnPick;
        Managers.Info._pspiList = spawnPickPacket.Players.ToList();
    }

    public static void S_VisibleObjectsHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_VisibleObjectsHandler(session, packet))) return;
        S_VisibleObjects visibleObjectsPkt = packet as S_VisibleObjects;

        GameObject go = Managers.Object.FindById(visibleObjectsPkt.ObjectId);
        if (go == null)
            return;

        MyPlayerController mpc = go.GetComponent<MyPlayerController>();
        if(mpc == null) 
            return;

        mpc.View.VisibleObjectIds.Clear(); // 나중에 렌더링 하고나서 바로 Clear하는게 나을듯?
        mpc.View.VisibleObjectIds = visibleObjectsPkt.VisibleObjectIds.ToHashSet();
       // Managers.Object.SetObjectVisible();

       //// TEMP
       // PlayerViewController pvc = go.GetComponent<PlayerViewController>();
       // if (pvc == null) return;
       // pvc.VisibleObjectIds.Clear();
       // pvc.VisibleObjectIds = visibleObjectsPkt.VisibleObjectIds.ToHashSet();
    }

    public static void S_LevelUpHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_LevelUpHandler(session, packet))) return;
        S_LevelUp levelUpPkt = packet as S_LevelUp;

        GameObject go = Managers.Object.FindById(levelUpPkt.ObjectId);
        if (go == null)
            return;

        CreatureController cc = go.GetComponent<CreatureController>();
        if (cc == null)
            return;

        cc.Stat.Level = levelUpPkt.Level;
        cc.ChangeStat(levelUpPkt.StatGrowth);



        //Debug.Log($" Id {cc.Id} ");
        //Debug.Log($" LevelUpCnt : {levelUpPkt.LevelUpCnt}, After Level : {cc.Stat.Level} ");
        //Debug.Log($" MaxHp : {levelUpPkt.StatGrowth.MaxHp}, MaxStamina : {levelUpPkt.StatGrowth.MaxStamina} ");

        //아래는 레벨이 제대로 표시되게 하는 코드
        //마이 플레이어면 업데이트 하고 리턴
        const int BaseLevel = 9;
        
        if (Managers.Object.MyPlayer != null && Managers.Object.MyPlayer.Id == levelUpPkt.ObjectId)
        {
            Managers.Object.MyPlayer.UI.PlayerInterface.OnLevelUp(levelUpPkt.LevelUpCnt);
            Managers.Object.MyPlayer.UpdateLevel();
            Managers.Object.MyPlayer.UI.PlayerInterface.UpdateStat();
            Managers.Object.MyPlayer.Exp = levelUpPkt.CurExp;
            Managers.Object.MyPlayer.MaxExp = levelUpPkt.NextMaxExp;
            Managers.Object.MyPlayer.UI.PlayerHUD.UpdateBattleBoard(Managers.Object.MyPlayer.Id);
            Managers.Sound.Play("sound/ui/effect_levelup");
            if(levelUpPkt.Level != BaseLevel)
            {
                Managers.Object.MyPlayer.PlayCommonCasterEffect(commonName: "LevelUp", mousePos: default, targetPos: default, targetRot: default, targetTransform: Managers.Object.MyPlayer.transform);
                Managers.Object.MyPlayer.Sound.GetEffect3D("LevelUp", Managers.Object.MyPlayer.transform.position);
            }
            return;
        }

        //다른 플레이어면 위에서 안걸리고 내려와서 여기 걸림. 몬스터도 레벨업 하나?
        PlayerController pc = go.GetComponent<PlayerController>();
        if(null !=  pc)
        {
            pc.SetNameTagLevel();
            Managers.Object.MyPlayer.UI.PlayerHUD.UpdateBattleBoard(pc.Id);
            Managers.Sound.Play3D("sound/ui/effect_levelup", pc.transform.position);
            if (levelUpPkt.Level != BaseLevel)
            {
                Managers.Object.MyPlayer.PlayCommonCasterEffect(commonName: "LevelUp", mousePos: default, targetPos: default, targetRot: default, targetTransform: pc.transform);
                Managers.Object.MyPlayer.Sound.GetEffect3D("LevelUp", pc.transform.position);
            }
        }
    }

    public static void S_FxHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_FxHandler(session, packet))) return;
        S_Fx fxPacket = packet as S_Fx;
        GameObject go = Managers.Object.FindById(fxPacket.ObjectId);
        if (go == null)     
            return;

        PlayerController pc = go.GetComponent<PlayerController>();
        if (pc == null)      
            return;

        Vector3 mousePos = new Vector3(fxPacket.MousePosX, 0, fxPacket.MousePosZ);
        Vector3 targetPos = fxPacket.TargetPosition.ToVector();
        Quaternion targetRot = fxPacket.TargetRotation;

        if (fxPacket.CanLookatMouse == true)
            pc.LookAtMouse(new Vector2(mousePos.x, mousePos.z));

        pc.PlayEffectFromServer(fxPacket, mousePos, targetPos, targetRot);
    }

    public static void S_RespawnHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_RespawnHandler(session, packet))) return;
        S_Respawn respawnPacket = packet as S_Respawn;

        GameObject go = Managers.Object.FindById(respawnPacket.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (Managers.Object.MyPlayer.Id == respawnPacket.ObjectId)
        {
            Managers.Object.MyPlayer.OnServerUpdate(respawnPacket);
            Managers.Sound.Play("sound/ui/TeamRevival");
        }
        else
        {
            if (pc != null)
            {
                pc.OnRespawn(respawnPacket);
                if(pc.ObjInfo.Player.Team == Managers.Info.Team)
                    Managers.Sound.Play("sound/ui/TeamRevival");

                Managers.Object.MyPlayer.View.TargetId = 0;
            }
        }

        // Todo* 급조
        if (pc != null)
            pc.RespawnStart();
    }

    public static void S_SkillLevelUpHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SkillLevelUpHandler(session, packet))) return;
        S_SkillLevelUp skillLevelUpPacket = packet as S_SkillLevelUp;

        KeyCode key = (KeyCode)skillLevelUpPacket.KeyCode;

        Managers.Object.MyPlayer.UI.PlayerInterface.SpecificSkillLevelUp(key);
        Managers.Object.MyPlayer.UI.UpdateSkillMaxCool();
        Managers.Sound.Play("sound/ui/SkillUp", Define.Sound.Effect, 0.3f);
    }

    public static void S_ChangeStatHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChangeStatHandler(session, packet))) return;
        S_ChangeStat statPacket = packet as S_ChangeStat;

        GameObject go = Managers.Object.FindById(statPacket.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponent<PlayerController>();
        if (pc == null)
            return;

        pc.Hp = statPacket.Hp;
        pc.Barrier = statPacket.Barrier;
        pc.Stamina = statPacket.Stamina;
    }

    public static void S_PlayerStateHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_PlayerStateHandler(session, packet))) return;
        S_PlayerState statePacket = packet as S_PlayerState;

        GameObject go = Managers.Object.FindById(statePacket.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponent<PlayerController>();
        if (pc == null)
            return;

        pc.ChangeState(statePacket);
    }

    public static void S_StopHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_StopHandler(session, packet))) return;
        S_Stop stopPacket = packet as S_Stop;

        GameObject go = Managers.Object.FindById(stopPacket.Id);
        if (go == null)
            return;

        if (Managers.Object.MyPlayer.Id == stopPacket.Id)
        {
            Managers.Object.MyPlayer.OnServerUpdate(stopPacket);
        }
        else
        {
            PlayerController pc = go.GetComponentInChildren<PlayerController>();
            if (pc == null)
                return;

            pc.OnStop(stopPacket);
        }
    }

    public static void S_SkillConfirmHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SkillConfirmHandler(session, packet))) return;
        S_SkillConfirm confirmPacket = packet as S_SkillConfirm;

        GameObject go = Managers.Object.FindById(confirmPacket.ObjectId);
        if (go == null)
            return;
        PlayerController pc = go.GetComponent<PlayerController>();
        if (pc == null)
            return;

        if (Managers.Object.MyPlayer.Id == confirmPacket.ObjectId)
        {
            if (true == confirmPacket.CanUse)
                Managers.Object.MyPlayer.OnServerUpdate(confirmPacket);
        }
    }

    public static void S_SkillCollisionRequestHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SkillCollisionRequestHandler(session, packet))) return;
        S_SkillCollisionRequest requestPacket = packet as S_SkillCollisionRequest;

        Managers.Object.MyPlayer.OnServerUpdate(requestPacket);
    }

    public static void S_SkillCostHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SkillCostHandler(session, packet))) return;
        S_SkillCost costPacket = packet as S_SkillCost;

        GameObject go = Managers.Object.FindById(costPacket.ObjectId);
        if (go == null)
            return;

        Managers.Object.MyPlayer.OnServerUpdate(costPacket);
    }

    public static void S_SkillMotionHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SkillMotionHandler(session, packet))) return;
        S_SkillMotion motionPacket = packet as S_SkillMotion;

        GameObject go = Managers.Object.FindById(motionPacket.ObjectId);
        if (go == null)
            return;

        if (Managers.Object.MyPlayer.Id == motionPacket.ObjectId)
        {
            Managers.Object.MyPlayer.OnServerUpdate(motionPacket);
        }
    }

    public static void S_MoveSyncHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_MoveSyncHandler(session, packet))) return;
        S_MoveSync syncPacket = packet as S_MoveSync;

        GameObject go = Managers.Object.FindById(syncPacket.ObjectId);
        if (go == null)
            return;

        if (Managers.Object.MyPlayer.Id == syncPacket.ObjectId)
        {
            Managers.Object.MyPlayer.OnServerUpdate(syncPacket);
        }
    }

    public static void S_ChangeItemStatHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChangeItemStatHandler(session, packet))) return;
        S_ChangeItemStat changeItemStatPacket = packet as S_ChangeItemStat;

        GameObject go = Managers.Object.FindById(changeItemStatPacket.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponent<PlayerController>();
        if (pc == null)
            return;

        pc.UpdateItemStat(changeItemStatPacket.ItemStat);

        if(pc is MyPlayerController mpc)
        {
            mpc.UI.PlayerInterface.UpdateStat();
            mpc.UI.PlayerInterface.UpdateSkillAccForPopup((int)changeItemStatPacket.ItemStat.SkillAcceleration);
            mpc.UI.UpdateSkillMaxCool();
        }
    }

    public static void S_ChangeEquipItemHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChangeEquipItemHandler(session, packet))) return;
        S_ChangeEquipItem changeEquipPacket = packet as S_ChangeEquipItem;

        GameObject go = Managers.Object.FindById(changeEquipPacket.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponent<PlayerController>();
        if (pc == null)
            return;

        pc.EquipItem(changeEquipPacket.ItemId);

        Managers.Object.MyPlayer.UI.PlayerHUD.UpdateBattleBoard(pc.Id);
    }
    
    public static void S_EnvRequestHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_EnvRequestHandler(session, packet))) return;
        S_EnvRequest revPacket = packet as S_EnvRequest;

        GameObject go = Managers.Object.FindById(revPacket.ObjectId);
        if (go == null)
            return;
        EnvController ec = go.GetComponent<EnvController>();

        GameObject tc = Managers.Object.FindById(revPacket.TargetId);
        if (tc == null)
            return;

        PlayerController pc = tc.GetComponent<PlayerController>();
        if (pc == null)
            return;

        ec.OnInteractionAuthorized(pc);
    }
    
    public static void S_ChangeInventoryHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChangeInventoryHandler(session, packet))) return;
        S_ChangeInventory changeInventoryPacket = packet as S_ChangeInventory;

        MyPlayerController mpc = Managers.Object.MyPlayer;
        if (mpc == null) 
            return;

        mpc.ChangeInventory(changeInventoryPacket);
    }

    public static void S_AttackInfoHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_AttackInfoHandler(session, packet))) return;

        S_AttackInfo atkInfoPacket = packet as S_AttackInfo;
        BaseController bc = Managers.Object.FindById(atkInfoPacket.AttackerId)?.GetComponentInChildren<BaseController>();
        if (bc == null)
            return;
        BaseController tbc = Managers.Object.FindById(atkInfoPacket.ObjectId)?.GetComponentInChildren<BaseController>();
        if (tbc == null)
            return;

        GameObjectType attackerObjType = ObjectManager.GetObjectTypeById(bc.Id);
        if (attackerObjType == GameObjectType.Player)
        {
            PlayerController atkPlayer = (PlayerController)bc;
            if (atkPlayer == null) 
                    return;

            // * Damage Screen
            if (atkInfoPacket.ObjectId == Managers.Object.MyPlayer.Id)
                Managers.Object.MyPlayer.UI.SetDamageOverlay();

            atkPlayer.OnHit(atkInfoPacket);
        }
        // *Monster 
        else if (attackerObjType == GameObjectType.Monster)
        {
            MonsterController atkMonster = (MonsterController)bc;
            if (atkMonster == null) 
                return;

            atkMonster.OnHit(atkInfoPacket);
        }
    }

    public static void S_CombatTextHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_CombatTextHandler(session, packet))) return;
        S_CombatText textPacket = packet as S_CombatText;

        GameObject go = Managers.Object.FindById(textPacket.ObjectId);
        if (go == null)
            return;
        CreatureController cc = go.GetComponentInChildren<CreatureController>();
        if (cc)
        {
            GameObjectType objectType = ObjectManager.GetObjectTypeById(cc.Id);
            if (objectType == GameObjectType.Monster)
            {
                MonsterController mc = go.GetComponentInChildren<MonsterController>();
                Managers.CombatText.SetCombatText(textPacket.Type, textPacket.Value, mc.transform.position);
                return;
            }
        }

        Managers.CombatText.SetCombatText(textPacket.Type, textPacket.Value, go.transform.position);
    }

    public static void S_ChangeKDAHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChangeKDAHandler(session, packet))) return;
        S_ChangeKDA KDAPacket = packet as S_ChangeKDA;

        foreach(KDAInfo info in KDAPacket.KDAs)
        {
            GameObject go = Managers.Object.FindById(info.ObjectId);
            if (go != null)
            {
                PlayerController pc = go.GetComponentInChildren<PlayerController>();
                if (pc != null)
                {
                    pc.SetKDA(info.Kill, info.Death, info.Asist);
                    Managers.Object.MyPlayer.UI.PlayerHUD.UpdateBattleBoard(pc.Id);
                }
            }               
        }
    }

    public static void S_SnareHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SnareHandler(session, packet))) return;
        S_Snare stunPacket = packet as S_Snare;

        GameObject go = Managers.Object.FindById(stunPacket.ObjectId);
        if (go == null)     return;
        GameObject goAtk = Managers.Object.FindById(stunPacket.AttackerId);
        if (goAtk == null)     return;
        CreatureController cc = go.GetComponentInChildren<CreatureController>();
        if (cc == null)     return;
        CreatureController atkc = goAtk.GetComponentInChildren<CreatureController>();
        if (atkc == null && !(atkc is PlayerController))    return;

        cc.Snare(stunPacket, atkc.ObjInfo.Player.CharType);
    }

    public static void S_SyncTimerHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SyncTimerHandler(session, packet))) return;
        S_SyncTimer syncTimerPacket = packet as S_SyncTimer;

        float clientPacketReceiveTime = Time.realtimeSinceStartup; // 패킷을 받은 로컬 시간 (Unity)

        float oneWayLatencySeconds = GetCurrentEstimatedOneWayLatency(); 

        long compensatedServerCurrentTimeMs = syncTimerPacket.CurrentTick + (long)(oneWayLatencySeconds * 1000);
        long compensatedPhaseServerEndTimeMs = syncTimerPacket.PhaseEndTime + (long)(oneWayLatencySeconds * 1000);

        // 서버가 생각하는 남은 시간 (밀리초)
        long estimatedServerRemainingDurationMs = compensatedPhaseServerEndTimeMs - compensatedServerCurrentTimeMs;

        // 클라이언트의 Time.realtimeSinceStartup을 기준으로 타이머가 끝날 최종 목표 시간
        float clientLocalTargetRealtimeSinceStartupEnd = clientPacketReceiveTime + (estimatedServerRemainingDurationMs / 1000f);

        if(Managers.Object.MyPlayer != null)
        {
            if (Managers.Object.MyPlayer.CurPhase != syncTimerPacket.Phase)
            {
                // CurPhase : 현재 페이즈를 어디서 가져올 지 몰라서 일단 MyPlayer에 넣어둠...
                Managers.Object.MyPlayer.CurPhase = syncTimerPacket.Phase;
                Managers.Object.MyPlayer.UI.ActiveAppearMonsterBar(syncTimerPacket.Phase);
            }
            Managers.Object.MyPlayer.UI.SetTimer(syncTimerPacket.Phase, clientLocalTargetRealtimeSinceStartupEnd);
        }
    }

    public static void S_AddAbigailCoordHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_AddAbigailCoordHandler(session, packet))) return;
        S_AddAbigailCoord addAbigailCoordPkt = packet as S_AddAbigailCoord;

        GameObject go = Managers.Object.FindById(addAbigailCoordPkt.ObjectId);
        if (go == null)
            return;

        AbigailCoord abigailCoord = go.GetComponentInChildren<AbigailCoord>();
        if (abigailCoord == null)
            return;

        abigailCoord.ActivateAbigailCoord(addAbigailCoordPkt.Duration, addAbigailCoordPkt.AttackerTeam);
    }

    public static void S_RemoveAbigailCoordHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_RemoveAbigailCoordHandler(session, packet))) return;
        S_RemoveAbigailCoord addAbigailCoordPkt = packet as S_RemoveAbigailCoord;

        GameObject go = Managers.Object.FindById(addAbigailCoordPkt.ObjectId);
        if (go == null)
            return;

        AbigailCoord abigailCoord = go.GetComponentInChildren<AbigailCoord>();
        if (abigailCoord == null)
            return;

        abigailCoord.DeactivateAbigailCoord();
    }

    public static void S_AddYukiPyosikHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_AddYukiPyosikHandler(session, packet))) return;
        S_AddYukiPyosik addYukiPyosikPkt = packet as S_AddYukiPyosik;

        GameObject go = Managers.Object.FindById(addYukiPyosikPkt.ObjectId);
        if (go == null)
            return;

        GameObject attackerGo = Managers.Object.FindById(addYukiPyosikPkt.AttackerId);
        if (attackerGo == null)
            return;

        YukiPyosik yukiPyosik = go.GetComponentInChildren<YukiPyosik>();
        if (yukiPyosik == null)
            return;

        yukiPyosik.ActivateYukiPyosik(go);
    }
    
    public static void S_SoundHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SoundHandler(session, packet))) return;
        S_Sound soundPkt = packet as S_Sound;
        GameObject go = Managers.Object.FindById(soundPkt.ObjectId);
        if (go == null) return;

        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (pc == null) return;

        if(soundPkt.Name.Contains("BGM"))
        {
            Managers.Sound.Play($"sound/bgm/P{Managers.Object.MyPlayer.CurPhase}", Define.Sound.Bgm, 0.1f);
            return;
        }

        if (pc.Sound != null)
        {
            string name = soundPkt.Name;
            if (soundPkt.Type == "Voice")
                pc.Sound.GetRandomVoice(name);
            else
                pc.Sound.GetRandom3DEffect(name, pc.transform.position);
        }

        if (soundPkt.Name == "Blink")
            Managers.Sound.Blink(soundPkt.ObjectId, pc.CellPos);
    }

    public static void S_SkillEffectHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SkillEffectHandler(session, packet))) return;
        S_SkillEffect YukiSkillEffectPkt = packet as S_SkillEffect;

        GameObject go = Managers.Object.FindById(YukiSkillEffectPkt.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (pc == null) return;

        if (YukiSkillEffectPkt.IsPlay)
            pc.YukiEffects.PlayEffect((SkillEffectType)YukiSkillEffectPkt.EffectType);
        else
            pc.YukiEffects.StopEffect((SkillEffectType)YukiSkillEffectPkt.EffectType);
    }

    public static void S_OccupyBeaconHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_OccupyBeaconHandler(session, packet))) return;
        S_OccupyBeacon occupyBeaconPkt = packet as S_OccupyBeacon;
        if(Enum.TryParse<Beacon>(occupyBeaconPkt.BeaconName, out Beacon result))
            Managers.Object.MyPlayer.UI.PlayerHUD.CaptureTurbine(result, occupyBeaconPkt.Team);

        GameObject beacon = GameObject.Find("Beacon_" + occupyBeaconPkt.BeaconName);
        if (beacon == null) return;
        BeaconController bc = beacon.GetComponent<BeaconController>();
        if (bc == null) return;

        bc.CompleteCapture(occupyBeaconPkt.Team);
    }

    public static void S_ChangeBeaconTimeHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChangeBeaconTimeHandler(session, packet))) return;
        S_ChangeBeaconTime changeBeaconTimePkt = packet as S_ChangeBeaconTime;

        Managers.Object.MyPlayer.UI.PlayerHUD.SetBeaconTimer((Beacon)changeBeaconTimePkt.Beacon, changeBeaconTimePkt.Time);
    }

    public static void S_ChangeScoreHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChangeScoreHandler(session, packet))) return;
        S_ChangeScore changeScorePkt = packet as S_ChangeScore;

        Managers.Object.MyPlayer.UI.PlayerHUD.SetScore(changeScorePkt.Team, changeScorePkt.Score);
    }

    public static void S_GameOverHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_GameOverHandler(session, packet))) return;
        S_GameOver gameOverPkt = packet as S_GameOver;

        bool isWin = false;

        if (Managers.Info.Team == gameOverPkt.WinTeam)
            isWin = true;
        else
            isWin = false;
        // 여기여기다
        //LoadingManager.Instance.LoadScene(Define.Scene.GameResult);
        Managers.Object.MyPlayer.UI.PlayerHUD.SetGameResult(isWin);
    }

    public static void S_ChangeTransformHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChangeTransformHandler(session, packet))) return;
        S_ChangeTransform changeTransformPkt = packet as S_ChangeTransform;

        GameObject go = Managers.Object.FindById(changeTransformPkt.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (pc == null)
            return;

        pc.CellPos = changeTransformPkt.PosInfo.ToVector();
        pc.RotInfo = changeTransformPkt.RotInfo;
        pc.SyncPos(changeTransformPkt.IsWarp);
    }

    public static void S_CanStopSkillHandler(PacketSession session, IMessage packet) 
    {
        if (!IsSceneReady("Game", () => S_CanStopSkillHandler(session, packet))) return;
        S_CanStopSkill canStopSkillPkt = packet as S_CanStopSkill;

        GameObject go = Managers.Object.FindById(canStopSkillPkt.ObjectId);
        if (go == null)
            return;

        MyPlayerController mpc = go.GetComponentInChildren<MyPlayerController>();
        if (mpc == null)
            return;

        mpc.CanStopSkill = canStopSkillPkt.CanStopSkill;
    }

    public static void S_RotateToPosHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_RotateToPosHandler(session, packet))) return;
        S_RotateToPos rotateToPosPkt = packet as S_RotateToPos;
        GameObject go = Managers.Object.FindById(rotateToPosPkt.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponentInChildren<MyPlayerController>();
        if (pc == null)
            return;

        pc.StartCoroutine(pc.CoRotateToPosition(new Vector3(rotateToPosPkt.PosX, 0, rotateToPosPkt.PosZ)));
    }

    public static void S_ChangeStatusHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChangeStatusHandler(session, packet))) return;
        S_ChangeStatus statusPacket = packet as S_ChangeStatus;

        GameObject go = Managers.Object.FindById(statusPacket.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (pc == null)
            return;

        pc.ChangeStatus(statusPacket);

        if (pc is MyPlayerController mpc)
        {
            mpc.UI.PlayerInterface.UpdateStat();
        }
    }

    public static void S_ChangeAttackRangeHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChangeAttackRangeHandler(session, packet))) return;
        S_ChangeAttackRange changeAtkRangePkt = packet as S_ChangeAttackRange;

        GameObject go = Managers.Object.FindById(changeAtkRangePkt.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (pc == null)
            return;

        pc.AttackRange = changeAtkRangePkt.AttackRange;

        if (pc is MyPlayerController mpc)
            mpc.UI.PlayerInterface.UpdateStat();
    }

    public static void S_UntargetableHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_UntargetableHandler(session, packet))) return;
        S_Untargetable untargetablePkt = packet as S_Untargetable;

        GameObject go = Managers.Object.FindById(untargetablePkt.ObjectId);
        if (go == null)
            return;

        CreatureController cc = go.GetComponentInChildren<CreatureController>();
        if (cc == null)
            return;

        cc.Untargetable = untargetablePkt.Untargetable;  
    }

    public static void S_UnstoppableHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_UnstoppableHandler(session, packet))) return;
        S_Unstoppable unstoppablePkt = packet as S_Unstoppable;

        GameObject go = Managers.Object.FindById(unstoppablePkt.ObjectId);
        if (go == null)
            return;

        CreatureController cc = go.GetComponentInChildren<CreatureController>();
        if (cc == null)
            return;

        cc.Unstoppable = unstoppablePkt.Unstoppable;  
    }

    public static void S_CombatModeHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_CombatModeHandler(session, packet))) return;
        S_CombatMode combatModePkt = packet as S_CombatMode;

        if(Managers.Object.MyPlayer != null)
        {
            switch (Managers.Object.MyPlayer.CombatStat = combatModePkt.CombatMode)
            {
                case CombatState.Combat:
                    Managers.Object.MyPlayer.UI.PlayerInterface.ActivateCombatImg(true);
                    break;
                case CombatState.NonCombat:
                    Managers.Object.MyPlayer.UI.PlayerInterface.ActivateCombatImg(false);
                    break;
            }
        }
    }

    public static void S_ChatHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChatHandler(session, packet))) return;
        S_Chat chatPkt = packet as S_Chat;

        //GameObject go = Managers.Object.FindById(chatPkt.ObjectId);
        //if (go == null)
        //    return;

        ChatHandler.Instance.EnqueueMessage(chatPkt.ObjectId, chatPkt.TeamId, chatPkt.PlayerName, chatPkt.Message, chatPkt.ChatType, chatPkt.CharType);
    }

    public static void S_AnimSpeedHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_AnimSpeedHandler(session, packet))) return;
        S_AnimSpeed speedPkt = packet as S_AnimSpeed;

        GameObject go = Managers.Object.FindById(speedPkt.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (pc == null)
            return;

        pc.ChangeSpeed(speedPkt.Name, speedPkt.Speed);
    }

    public static void S_RestHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_RestHandler(session, packet))) return;
        S_Rest restPkt = packet as S_Rest;

        GameObject go = Managers.Object.FindById(restPkt.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (pc == null)
            return;

        pc.IsRest = restPkt.IsRest;

        if (pc is MyPlayerController mpc)
            mpc.OnServerUpdate(restPkt);
    }

    public static void S_ProjectileRozziHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ProjectileRozziHandler(session, packet))) return;
        S_ProjectileRozzi projectilePacket = packet as S_ProjectileRozzi;

        GameObject go = Managers.Object.FindById(projectilePacket.ObjectId);
        if (go == null)
            return;

        Projectile_Rozzi pr = go.GetComponentInChildren<Projectile_Rozzi>();
        if (pr == null)
            return;

        pr.ChangeState(projectilePacket);
    }

    public static void S_YukiStudHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_YukiStudHandler(session, packet))) return;
        S_YukiStud yukiStudPacket = packet as S_YukiStud;

        GameObject go = Managers.Object.FindById(yukiStudPacket.ObjectId);
        if (go == null)
            return;

        UI_YukiNameTag yukiNameTag = go.GetComponentInChildren<UI_YukiNameTag>();
        if (yukiNameTag == null)
        {
            return;
        }

        yukiNameTag.SetStud(yukiStudPacket.StudCnt);
    }

    public static void S_EnterSlotHandler(PacketSession session, IMessage packet)
    {
        S_EnterSlot enterSlotPkt = packet as S_EnterSlot;
        GameObject go = GameObject.Find("LobbySceneUI");
        if (go == null) return;
        
        UI_LobbyScene lobbySceneUI = go.GetComponent<UI_LobbyScene>();
        if (lobbySceneUI == null) return;

        lobbySceneUI.SetNickname(enterSlotPkt.SlotIdx, enterSlotPkt.Nickname);
        lobbySceneUI.SetSlotImage(enterSlotPkt.SlotIdx, enterSlotPkt.SlotType);
    }

    public static void S_SpawnSlotHandler(PacketSession session, IMessage packet)
    {
        S_SpawnSlot spawnSlotPkt = packet as S_SpawnSlot;
        GameObject go = GameObject.Find("LobbySceneUI");
        if (go == null) return;

        UI_LobbyScene lobbySceneUI = go.GetComponent<UI_LobbyScene>();
        if (lobbySceneUI == null) return;

        int cnt = spawnSlotPkt.SlotIdxs.Count;
        for (int i = 0; i < cnt; ++i)
        {
            lobbySceneUI.SetNickname(spawnSlotPkt.SlotIdxs[i], spawnSlotPkt.Nicknames[i]);
            lobbySceneUI.SetSlotImage(spawnSlotPkt.SlotIdxs[i], Slot.Other);
        }
    }

    public static void S_LeaveLobbyHandler(PacketSession session, IMessage packet)
    {
        S_LeaveLobby leaveLobbyPkt = packet as S_LeaveLobby;
        GameObject go = GameObject.Find("LobbySceneUI");
        if (go == null) return;

        UI_LobbyScene lobbySceneUI = go.GetComponent<UI_LobbyScene>();
        if (lobbySceneUI == null) return;

        lobbySceneUI.SetNickname(leaveLobbyPkt.SlotIdx);
        lobbySceneUI.SetSlotImage(leaveLobbyPkt.SlotIdx, Slot.Empty);
    }

    public static void S_LobbyCntHandler(PacketSession session, IMessage packet)
    {
        S_LobbyCnt lobbyCntPkt = packet as S_LobbyCnt;
        GameObject go = GameObject.Find("LobbySceneUI");
        if (go == null) return;

        UI_LobbyScene lobbySceneUI = go.GetComponent<UI_LobbyScene>();
        if (lobbySceneUI == null) return;

        lobbySceneUI.SetCount(lobbyCntPkt.PlayerCnt, lobbyCntPkt.ObserverCnt);
    }

    public static void S_NicknameHandler(PacketSession session, IMessage packet)
    {
        S_Nickname nicknamePkt = packet as S_Nickname;
        Managers.Info.UserName = nicknamePkt.Nickname;
    }

    public static void S_CountdownHandler(PacketSession session, IMessage packet)
    {
        S_Countdown countdownPkt = packet as S_Countdown;
        GameObject go = GameObject.Find("PickSceneUI");
        if (go == null) return;

        UI_PickSceneUI pickSceneUI = go.GetComponent<UI_PickSceneUI>();
        if (pickSceneUI == null) return;

        pickSceneUI.ChangeCountdown(countdownPkt.Count);
    }

    public static void S_PickAllReadyHandler(PacketSession session, IMessage packet)
    {
        S_PickAllReady pickAllReady = packet as S_PickAllReady;
        GameObject go = GameObject.Find("PickSceneUI");
        if (go == null) return;

        UI_PickSceneUI pickSceneUI = go.GetComponent<UI_PickSceneUI>();
        if (pickSceneUI == null) return;

        pickSceneUI.OnAllReady(pickAllReady.StartIdx, pickAllReady.CharList.ToList<CharacterType>(), 
            pickAllReady.WeaponList.ToList<Weapon>(), pickAllReady.TraitList.ToList<TraitType>());
    }

    public static void S_RandomPickHandler(PacketSession session, IMessage packet)
    {
        S_RandomPick randomPickPkt = packet as S_RandomPick;
        GameObject go = GameObject.Find("PickSceneUI");
        if (go == null) return;

        UI_PickSceneUI pickSceneUI = go.GetComponent<UI_PickSceneUI>();
        if (pickSceneUI == null) return;

        pickSceneUI.OnClickedPickButton.Invoke(randomPickPkt.CharType.ToString());

        S_ReadyBtn readyBtnPkt = new S_ReadyBtn();
        S_ReadyBtnHandler(session, readyBtnPkt);
    }

    public static void S_ReadyBtnHandler(PacketSession session, IMessage packet)
    {
        S_ReadyBtn readyBtn = packet as S_ReadyBtn;
        GameObject go = GameObject.Find("ReadyButton");
        if (go == null) return;

        UI_ReadyButton readyButton = go.GetComponent<UI_ReadyButton>();
        if (readyButton == null) return;

        Managers.Info.IsReady = true;
        readyButton.OnReady();
    }

    public static void S_SpawnWardHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_SpawnWardHandler(session, packet))) return;

        S_SpawnWard wardPacket = packet as S_SpawnWard;

        Managers.Object.AddWard(wardPacket.ObjInfo, wardPacket.TeamIndex);
    }

    public static void S_RemoveEffectHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_RemoveEffectHandler(session, packet))) return;

        S_RemoveEffect removeEffectPacket = packet as S_RemoveEffect;
       
        if(!removeEffectPacket.IsCommon)
            Managers.FX.Effect.RemoveEffect(removeEffectPacket);
        else
            Managers.FX.Effect.RemoveCommonEffect(removeEffectPacket);
    }

    public static void S_StartOperateHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_StartOperateHandler(session, packet))) return;

        S_StartOperate startOperatePkt = packet as S_StartOperate;

        GameObject beacon = GameObject.Find(startOperatePkt.BeaconName);
        
        if (beacon == null) return;
        BeaconController bc = beacon.GetComponent<BeaconController>();
        if (bc == null) return;

        bc.StartCapture(startOperatePkt.Team);

        if (startOperatePkt.ObjectId == Managers.Object.MyPlayer.Id)
            bc.Begin();
    }

    public static void S_StopOperateHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_StopOperateHandler(session, packet))) return;

        S_StopOperate stopOperatePkt = packet as S_StopOperate;
        GameObject beacon = GameObject.Find(stopOperatePkt.BeaconName);
        
        if (beacon == null) return;
        BeaconController bc = beacon.GetComponent<BeaconController>();
        if (bc == null) return;

        bc.FailCapture();

        if (stopOperatePkt.ObjectId == Managers.Object.MyPlayer.Id)
            bc.Cancel();
    }

    public static void S_AbigailSoundHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_AbigailSoundHandler(session, packet))) return;

        S_AbigailSound abigailSoundPkt = packet as S_AbigailSound;
        GameObject go = Managers.Object.FindById(abigailSoundPkt.ObjectId);
        if (go == null) return;
        AbigailAudioManager aam = go.GetComponentInChildren<AbigailAudioManager>();
        if(aam == null) return;

        aam.Play(abigailSoundPkt.ObjectId, abigailSoundPkt.Sound, abigailSoundPkt.Pos.ToVector(), abigailSoundPkt.Idx);
    }

    public static void S_PickSoundHandler(PacketSession session, IMessage packet)
    {
        S_PickSound pickSoundPkt = packet as S_PickSound;

        GameObject go = GameObject.Find("PickScene");
        if (go == null) return;

        PickScene pickScene = go.GetComponent<PickScene>();
        if (pickScene == null) return;

        pickScene.PlaySelectedSound(pickSoundPkt.CharType);
    }

    public static void S_RozziNormalAttackHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_RozziNormalAttackHandler(session, packet)))
            return;

        S_RozziNormalAttack attackPacket = packet as S_RozziNormalAttack;

        var projectile = Managers.Object.FindById(attackPacket.ObjectId);
        if (projectile == null)
            return;

        Projectile_Rozzi_NormalAttack pr = projectile.GetComponentInChildren<Projectile_Rozzi_NormalAttack>();
        if (pr == null)
            return;

        pr.Init(attackPacket);
    }
    public static void S_TheodoreAttackHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_TheodoreAttackHandler(session, packet)))
            return;

        S_TheodoreAttack attackPacket = packet as S_TheodoreAttack;

        var projectile = Managers.Object.FindById(attackPacket.ObjectId);
        if (projectile == null)
            return;

        Projectile_Theodore_Attack pr = projectile.GetComponentInChildren<Projectile_Theodore_Attack>();
        if (pr == null)
            return;

        pr.Init(attackPacket);
    }
    

    public static void S_ChangeExpHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_ChangeExpHandler(session, packet))) return;

        S_ChangeExp changeExpPacket = packet as S_ChangeExp;
        Managers.Object.MyPlayer.Exp = changeExpPacket.Exp;
    }

    public static void S_AbigailFxHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_AbigailFxHandler(session, packet))) return;
        S_AbigailFx abigailFx = packet as S_AbigailFx;

        GameObject go = Managers.Object.FindById(abigailFx.ObjectId);
        if (go == null) return;
        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (pc == null) return;
        pc.YukiEffects.PlayEffect(abigailFx.Fx, abigailFx.Duration);
    }

    public static void S_StopAbglFxHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_StopAbglFxHandler(session, packet))) return;
        S_StopAbglFx stopAbglFx = packet as S_StopAbglFx;
        GameObject go = Managers.Object.FindById(stopAbglFx.ObjectId);
        if (go == null) return;
        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (pc == null) return;
        pc.YukiEffects.StopEffect(stopAbglFx.Fx);
    }

    public static void S_DeployingLoopHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_DeployingLoopHandler(session, packet)))
            return;

        S_DeployingLoop deploying = packet as S_DeployingLoop;
        GameObject go = Managers.Object.FindById(deploying.ObjectId);
        if (go == null)
            return;

        MyPlayerController mpc = go.GetComponentInChildren<MyPlayerController>();
        if (mpc == null)
            return;
        mpc.OnServerUpdate(deploying);
    }

    public static void S_AbigailPortalHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_AbigailPortalHandler(session, packet))) return;

        S_AbigailPortal abglPortal = packet as S_AbigailPortal;
        GameObject go = Managers.Object.FindById(abglPortal.ObjectId);
        if (go == null) return;
        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (pc == null) return;

        Vector3 startPos = new Vector3(abglPortal.StartX, pc.ObjInfo.PosInfo.PosY, abglPortal.StartZ);
        Vector3 endPos = new Vector3(abglPortal.EndX, pc.ObjInfo.PosInfo.PosY, abglPortal.EndZ);
        Vector3 dirAB = (endPos - startPos).normalized;
        Quaternion rotA = Quaternion.LookRotation(dirAB, Vector3.up);
        Quaternion rotB = Quaternion.LookRotation(-dirAB, Vector3.up);

        pc.YukiEffects.PlayEffect(AbigailFx.EPortal1, startPos, rotA);
        pc.YukiEffects.PlayEffect(AbigailFx.EPortal2, endPos, rotB);
    }

    public static void S_PingMarkerHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_PingMarkerHandler(session, packet)))
            return;

        S_PingMarker pingPacket = packet as S_PingMarker;
        if (pingPacket.ObjectId == Managers.Object.MyPlayer.Id)
            return;

        GameObject go = Managers.Object.FindById(pingPacket.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (pc == null)
            return;

        pc.Ping.PlayPing(pingPacket.TargetPos.ToVector());
    }

    public static void S_MinimapIconHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_MinimapIconHandler(session, packet)))
            return;

        S_MinimapIcon minimapIconPacket = packet as S_MinimapIcon;

        switch (minimapIconPacket.Type)
        {
            case MinimapIcon.OmegaExpected:
                Managers.Object.MyPlayer.UI.PlayerHUD.SetMinimapOmegaExpected(minimapIconPacket.IsActivate);
                break;
            case MinimapIcon.OmegaGo:
                Managers.Object.MyPlayer.UI.PlayerHUD.SetMinimapOmegaGo(minimapIconPacket.IsActivate);
                break;
            case MinimapIcon.GammaGo:
                Managers.Object.MyPlayer.UI.PlayerHUD.SetMinimapGammaGo(minimapIconPacket.IsActivate);
                break;
        }
    }

    public static void S_EmoticonHandler(PacketSession session, IMessage packet)
    {
        if (!IsSceneReady("Game", () => S_EmoticonHandler(session, packet)))
            return;

        S_Emoticon emoticonPacket = packet as S_Emoticon;
        if (emoticonPacket.ObjectId == Managers.Object.MyPlayer.Id)
            return;

        GameObject go = Managers.Object.FindById(emoticonPacket.ObjectId);
        if (go == null)
            return;

        PlayerController pc = go.GetComponentInChildren<PlayerController>();
        if (pc == null)
            return;

        pc.Emoticon.PlayEmoticonFromServer(emoticonPacket.EmoticonId);
    }

    static float GetCurrentEstimatedOneWayLatency()
    {
        return 0.05f;
    }

    public static bool IsSceneReady(string sceneName, Action callback)
    {
        if (!LoadingManager.Instance.IsGameSceneReady())
        {
            LoadingManager.Instance.EnqueuePostLoadAction(callback);
            return false;
        }
        return true;
    } // 특정 Scene이 아직 존재하지 않으면 해당 Scene이 될 때까지 기다렸다가 실행
}
