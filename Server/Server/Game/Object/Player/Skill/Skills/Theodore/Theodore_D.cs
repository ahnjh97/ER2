using Google.Protobuf.Protocol;
using Server.Game;
using System.Numerics;
using static Server.Data.DataUtils;

public sealed class Theodore_D : SkillHandlerBase
{
    public override bool CanMoveDuringCast => false;
    public override float MoveSpeedMultiplier => 1.2f;

    private const string ANIM_START = "SKILL_D_START";
    private const string ANIM_SKILL = "SKILL_D";
    private const string ANIM_END = "SKILL_D_END";

    private float _tAnimEnd = 0.0f;
    private float _timeElapsed = 0.0f;
    private bool _isEnding = false;

    public Theodore_D()
    {
        _characterType = CharacterType.Theodore;
        _animName = ANIM_START;
        _keyCode = KeyCode.D;
        _tAnimEnd = GetDuration();
    }

    public override void OnEnter(Player p, SkillContext ctx)
    {
        p.SendSkillCostPacket(_keyCode);

        HitboxRequired = false;
        base.OnEnter(p, ctx);
        p.LookAtMouse(ctx.MousePos);
    }

    public override void OnHit(Player p, SkillContext ctx)
    {
        return;
    }
 
    public override void OnTick(Player p, SkillContext ctx)
    {
        if (_isEnding)
        {
             CanStopSkill = true;
            _timeElapsed += TimeUtil.Instance.DeltaTime;
            if (_timeElapsed >= _tAnimEnd)
            {
                p.ChangeState(new Player_IdleState());
            }
        }
    }
    public override void OnAttack(Player p)
    {
        Player_SkillState skillstate = p.CurrentState as Player_SkillState;
        p.LookAtMouse(skillstate.Ctx.MousePos);
        
        // > Animation
        _animName = ANIM_SKILL;
        p.SendAnimPacket(_animName, 0.05f);

        // > Skill
        CreateHitbox(p, skillstate.Ctx);
        p.SendSkillConfirmPacket(
            canUse : true,
            keyCode : _keyCode, sendCostPacket : false);

        // > Effect
        p.SendSkillEffect(
            mousePos : skillstate.Ctx.MousePos, 
            keyCode: _keyCode, 
            sendLookatMousePacket: true);
    }
    public override void OnStop(Player p)
    {
        _animName = ANIM_END;
        p.SendAnimPacket(_animName, 0.1f);

        _isEnding = true;
        _timeElapsed = 0.0f; 
        _tAnimEnd = GetDuration();
    } 
    public override void OnExit(Player p, SkillContext ctx)
    {
        base.OnExit(p, ctx);
    }
}

