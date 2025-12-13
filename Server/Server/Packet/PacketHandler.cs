using Google.Protobuf;
using Google.Protobuf.Protocol;
using Server;
using Server.Data;
using Server.Game;
using ServerCore;
using System;
using static Server.Data.DataUtils;

class PacketHandler
{
    public static void C_ReadyBtnHandler(PacketSession session, IMessage packet)
    {
        ClientSession clientSession = session as ClientSession;
        PickRoom room = RoomManager.Instance.Find(clientSession.CurRoom) as PickRoom;
        if (room == null)
            return;

        room.OnReadyBtnClick(clientSession);
    }

    public static void C_MoveHandler(PacketSession session, IMessage packet)
    {
        // TEMP
    }

    public static void C_MoveSyncHandler(PacketSession session, IMessage packet)
    {
        C_MoveSync movePacket = packet as C_MoveSync;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandleMoveSync, player, movePacket);
    }

    public static void C_SkillHandler(PacketSession session, IMessage packet)
    {
        //      C_Skill skillPacket = packet as C_Skill;
        //      ClientSession clientSession = session as ClientSession;

        //      Player player = clientSession.MyPlayer;
        //      if (player == null)
        //          return;

        //      GameRoom room = player.Room;
        //      if (room == null)
        //          return;

        //room.Push(room.HandleSkill, player, skillPacket);
    }

    public static void C_AnimHandler(PacketSession session, IMessage packet)
    {
        C_Anim animPacket = packet as C_Anim;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandleAnim, player, animPacket);
    }
    public static void C_FxHandler(PacketSession session, IMessage effectPacket)
    {
        C_Fx skillPacket = effectPacket as C_Fx;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        //room.Push(room.HandleVF, player, skillPacket);
    }

    public static void C_CharacterHandler(PacketSession session, IMessage packet)
    {
        ClientSession clientSession = session as ClientSession;
        C_Character c_charPacket = packet as C_Character;
        clientSession.MyCharacter = c_charPacket.CharType;

        PickRoom room = RoomManager.Instance.Find(clientSession.CurRoom) as PickRoom;
        if (room == null || room.IsReady(c_charPacket.PickIdx))
            return;

        S_Character s_charPacket = new S_Character();
        s_charPacket.CharType = c_charPacket.CharType;
        s_charPacket.PickIdx = c_charPacket.PickIdx;
        room.Push(room.BroadcastToTeam, s_charPacket, c_charPacket.PickIdx);
    }

    public static void C_TraitHandler(PacketSession session, IMessage packet)
    {
        ClientSession clientSession = session as ClientSession;
        C_Trait c_traitPacket = packet as C_Trait;
        clientSession.TraitType = c_traitPacket.TraitType;

        PickRoom room = RoomManager.Instance.Find(clientSession.CurRoom) as PickRoom;
        if (room == null || room.IsReady(c_traitPacket.PickIdx))
            return;

        room.Push(room.SetTrait, c_traitPacket.TraitType, c_traitPacket.PickIdx);

        S_Trait s_traitPacket = new S_Trait();
        s_traitPacket.TraitType = c_traitPacket.TraitType;
        s_traitPacket.PickIdx = c_traitPacket.PickIdx;
        room.Push(room.BroadcastToTeam, s_traitPacket, c_traitPacket.PickIdx);
    }

    public static void C_InteractHandler(PacketSession session, IMessage packet)
    {
        //C_Interact interactPacket = packet as C_Interact;
        //ClientSession clientSession = session as ClientSession;

        //Player player = clientSession.MyPlayer;
        //if (player == null)
        //    return;

        //PickRoom room = RoomManager.Instance.Find(1) as PickRoom;
        //if (room == null)
        //    return;

    }

    public static void C_WeaponHandler(PacketSession session, IMessage packet)
    {
        ClientSession clientSession = session as ClientSession;
        C_Weapon c_weaponPacket = packet as C_Weapon;
        clientSession.WeaponType = c_weaponPacket.WeaponType;

        PickRoom room = RoomManager.Instance.Find(clientSession.CurRoom) as PickRoom;
        if (room == null || room.IsReady(c_weaponPacket.PickIdx))
            return;

        room.Push(room.SetWeapon, c_weaponPacket.WeaponType, c_weaponPacket.PickIdx);

        S_Weapon s_weaponPacket = new S_Weapon();
        s_weaponPacket.WeaponType = c_weaponPacket.WeaponType;
        s_weaponPacket.PickIdx = c_weaponPacket.PickIdx;

        room.Push(room.BroadcastToTeam, s_weaponPacket, c_weaponPacket.PickIdx);
    }

    public static void C_PingHandler(PacketSession session, IMessage packet)
    {
        ClientSession clientSession = session as ClientSession;

        clientSession.LastPing = DateTime.Now;
    }

    public static void C_EnterLobbyHandler(PacketSession session, IMessage packet)
    {
        C_EnterLobby enterLobbyPkt = packet as C_EnterLobby;
        ClientSession clientSession = session as ClientSession;

        LobbyRoom room = RoomManager.Instance.Find(1) as LobbyRoom;
        if (room == null)
            return;

        int slotIdx = room.GetEmptySlotIdx();
        if (slotIdx == -1)
            return;

        LobbyPlayer lp = new LobbyPlayer();
        lp.Session = clientSession;
        lp.UserName = enterLobbyPkt.Nickname;
        room.Push(room.EnterLobby, lp, slotIdx);
    }

    public static void C_SkillLevelUpHandler(PacketSession session, IMessage packet)
    {
        C_SkillLevelUp skillInfoChangePacket = packet as C_SkillLevelUp;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        //스킬 레벨업이 성공하면 
        if (player.SkillLevelUp((KeyCode)skillInfoChangePacket.KeyCode))
        {
            room.Push(room.SkillLevelUp, player.Id, skillInfoChangePacket.KeyCode);
        }
    }

    public static void C_AttackHandler(PacketSession session, IMessage packet)
    {
        var client = (ClientSession)session;
        var player = client?.MyPlayer;
        if (player?.Room == null)
            return;
        var req = (C_Attack)packet;

        player.Room.Push(player.Room.HandleAttack, player, req);
    }

    public static void C_SetMoveTargetHandler(PacketSession session, IMessage packet)
    {
        var client = (ClientSession)session;
        var player = client?.MyPlayer;
        if (player?.Room == null)
            return;
        var req = (C_SetMoveTarget)packet;

        player.Room.Push(player.Room.HandleSetMoveTarget, player, req);
    }

    public static void C_StopHandler(PacketSession session, IMessage packet)
    {
        var client = (ClientSession)session;
        var player = client?.MyPlayer;
        if (player?.Room == null)
            return;
        var req = (C_Stop)packet;

        player.Room.Push(player.Room.HandleStop, player, req);
    }

    public static void C_SkillInputHandler(PacketSession session, IMessage packet)
    {
        C_SkillInput skillPacket = packet as C_SkillInput;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandleSkill, player, skillPacket);
    }

    public static void C_SkillPrepareHandler(PacketSession session, IMessage packet)
    {
        C_SkillPrepare skillPacket = packet as C_SkillPrepare;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandlerPrepareSkill, player, skillPacket);
    }
    public static void C_SkillExecuteHandler(PacketSession session, IMessage packet)
    {
        C_SkillExecute skillPacket = packet as C_SkillExecute;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandleExecuteSkill, player, skillPacket);
    }
    public static void C_SkillCancelHandler(PacketSession session, IMessage packet)
    {
        C_SkillCancel skillPacket = packet as C_SkillCancel;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandlerChargeCancelSkill, player, skillPacket);
    }


    public static void C_EnvRequestHandler(PacketSession session, IMessage packet)
    {
        ClientSession clientSession = session as ClientSession;
        C_EnvRequest envPacket = packet as C_EnvRequest;

        Player player = clientSession.MyPlayer;
        if (player == null) return;

        GameRoom room = player.Room;
        if (room == null) return;

        if (!DataManager.EnvDict.TryGetValue(envPacket.EnvType, out EnvInfo envData))
            return;

        if (envPacket.TargetId == player.Id)
            room.GetEnvManager?.GiveRewardToPlayer(player, envPacket.EnvType);

        S_EnvRequest sendPacket = new S_EnvRequest()
        {
            ObjectId = envPacket.ObjectId,
            EnvType = envPacket.EnvType,
            TargetId = envPacket.TargetId
        };
        room.Push(room.Broadcast, sendPacket);
    }

    public static void C_TestDamageHandler(PacketSession session, IMessage packet)
    {
        ClientSession clientSession = session as ClientSession;
        C_TestDamage damagePacket = packet as C_TestDamage;

        // 검증 필요하면 추가하기..
        Player player = clientSession.MyPlayer;
        player.OnDamaged(player, 500);
    }

    public static void C_SkillCollisionProposeHandler(PacketSession session, IMessage packet)
    {
        C_SkillCollisionPropose skillPacket = packet as C_SkillCollisionPropose;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandleSkillCollision, player, skillPacket);
    }

    public static void C_RestHandler(PacketSession session, IMessage packet)
    {
        var client = (ClientSession)session;
        var player = client?.MyPlayer;
        if (player?.Room == null)
            return;
        var req = (C_Rest)packet;

        player.Room.Push(player.Room.HandleRest, player, req);
    }

    // temp 임시 코드 나중에 수정
    public static void C_DeathHandler(PacketSession session, IMessage packet)
    {
        var client = (ClientSession)session;
        var player = client?.MyPlayer;
        if (player?.Room == null)
            return;
        var req = (C_Death)packet;

        player.Room.Push(player.Room.HandleDeath, player, req);
    }

    public static void C_KeyInputForTestHandler(PacketSession session, IMessage packet)
    {
        var client = (ClientSession)session;
        var player = client?.MyPlayer;
        if (player?.Room == null)
            return;
        var req = (C_KeyInputForTest)packet;

        player.Room.Push(player.Room.HandleKeyInputForTest, player, req);
    }

    // Receive and save the charging ratio from the client.
    public static void C_ChargingSkillHandler(PacketSession session, IMessage packet)
    {
        C_ChargingSkill chargePacket = packet as C_ChargingSkill;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandleChargingSkill, player, chargePacket);
    }

    public static void C_OperateHandler(PacketSession session, IMessage packet)
    {
        C_Operate operatePkt = packet as C_Operate;
        ClientSession clientSession = session as ClientSession;
        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        if (!Enum.TryParse<Server.Game.Beacon>(operatePkt.BeaconName, true, out Server.Game.Beacon beacon))
            return;

        room.Push(player.Room.HandleOperate, player, beacon, operatePkt.PosX, operatePkt.PosZ);
    }

    public static void C_ChatHandler(PacketSession session, IMessage packet)
    {
        C_Chat chatPkt = packet as C_Chat;
        ClientSession clientSession = session as ClientSession;
        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandlerChat, player, chatPkt);
    }

    public static void C_SlotClickHandler(PacketSession session, IMessage packet)
    {
        ClientSession clientSession = session as ClientSession;
        C_SlotClick slotClickPkt = packet as C_SlotClick;

        Room room = RoomManager.Instance.Find(1);

        LobbyRoom lr = room as LobbyRoom;
        if (lr == null)
            return;

        lr.Push(lr.OnSlotClick, clientSession.SessionId, slotClickPkt.SlotIdx);
    }

    public static void C_StartBtnHandler(PacketSession session, IMessage packet)
    {
        ClientSession clientSession = session as ClientSession;
        Room room = RoomManager.Instance.Find(1);
        LobbyRoom lr = room as LobbyRoom;
        if (lr == null)
            return;

        lr.Push(lr.AddPickRoom, clientSession.SessionId);
    }

    public static void C_UseItemHandler(PacketSession session, IMessage packet)
    {
        C_UseItem useItemPkt = packet as C_UseItem;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandleUseItem, player, useItemPkt);
    }

    public static void C_DeployingLoopHandler(PacketSession session, IMessage packet)
    {
        C_DeployingLoop deployingPacket = packet as C_DeployingLoop;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandleDeployingLoop, player, deployingPacket);
    }

    public static void C_RozziNormalAttackHandler(PacketSession session, IMessage packet)
    {
        C_RozziNormalAttack attackPacket = packet as C_RozziNormalAttack;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandleRozziNormalAttack, player, attackPacket);
    }

    public static void C_AttackTargetInvalidHandler(PacketSession session, IMessage packet)
    {
        C_AttackTargetInvalid attackPacket = packet as C_AttackTargetInvalid;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandleAttackTargetInvalid, player, attackPacket);
    }

    public static void C_BaseTriggerHandler(PacketSession session, IMessage packet)
    {
        C_BaseTrigger basePacket = packet as C_BaseTrigger;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandleBaseTrigger, player, basePacket);
    }

    public static void C_PingMarkerHandler(PacketSession session, IMessage packet)
    {
        C_PingMarker pingPacket = packet as C_PingMarker;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandlePingMarker, player, pingPacket);
    }

    public static void C_EmoticonHandler(PacketSession session, IMessage packet)
    {
        C_Emoticon emoticonPacket = packet as C_Emoticon;
        ClientSession clientSession = session as ClientSession;

        Player player = clientSession.MyPlayer;
        if (player == null)
            return;

        GameRoom room = player.Room;
        if (room == null)
            return;

        room.Push(room.HandleEmoticon, player, emoticonPacket);
    }
}
