using Google.Protobuf.Protocol;
using Google.Protobuf.WellKnownTypes;
using Server.Game;
using System;
using System.Numerics;
using static Server.Data.DataUtils;
using static Server.Game.GameObject;

public sealed class Yuki_D : SkillHandlerBase
{
    private Vector3 _startPos, _endPos, nextPos, _dir;
    private float _dashElapsed;
    private float _stanceElapsed;
    private float _dashDuration;
    private float _waitDuration;
    private float _dashRange;       // 대쉬 이동거리
    private float _speed;
    private float _stopElasped;
    private float _stopSkillTime;

    public Yuki_D()
    {
        _characterType = CharacterType.Yuki;
        _animName = "SKILL_D";
        _keyCode = KeyCode.D;
    }

    public override void OnEnter(Player p, SkillContext ctx)
    {
        base.OnEnter(p, ctx);

        _startPos = p.Position;

        // 초기화
        _dashRange = 3.5f;
        _dashElapsed = 0f;
        _stanceElapsed = 0f;
        _speed = 25f;
        _waitDuration = 0.75f;
        _stopElasped = 0f;
        _stopSkillTime = 1f; // 3프레임 * 30FPS

        Vector3 mouseWorldPos = new Vector3(ctx.MousePos.X, p.Position.Y, ctx.MousePos.Y);

        _dir = mouseWorldPos - p.Position;
        _dir.Y = 0;
        _dir = Vector3.Normalize(_dir);

        _endPos = _startPos + _dir * _dashRange;

        _dashDuration = _dashRange / _speed;

        Vector3 targetPos = _endPos;
        SendSkillCollisionRequestPacket(p, CollisionType.Block, _startPos, targetPos);

        Skill skill = p.GetSkill(KeyCode.D);

        StatusEffect statusEffectUnstoppable = new StatusEffect();
        statusEffectUnstoppable.type = skill.SkillData.levels[skill.CurLevel].effects[0].type;
        statusEffectUnstoppable.duration = skill.SkillData.levels[skill.CurLevel].effects[0].duration;
        if (!System.Enum.TryParse<Subject>(skill.SkillData.levels[skill.CurLevel].effects[0].subject, out statusEffectUnstoppable.subject))
            return;
        p.AddStatusEffect(statusEffectUnstoppable);

        p.LookAtMouse(ctx.MousePos);

        p.SendYukiSkillEffect(SkillEffectType.WpSkill);
        SendSkillConfirmPacket(p);

        p.Room.Push(p.Room.BroadcastAbigailSound, p, AbigailSound.YukiWeaponSkill1, 1f);
    }

    public override void OnHit(Player p, SkillContext ctx)
    {
        return;
    }

    public override void OnTick(Player p, SkillContext ctx)
    {
        if (CanStopSkill)
            return;

        _stopElasped += TimeUtil.Instance.DeltaTime;

        if (_stopElasped >= _stopSkillTime)
        {
            CanStopSkill = true;
            p.SendCanStopSkillPacket(CanStopSkill);
        }
        else
        {
            _stanceElapsed += TimeUtil.Instance.DeltaTime;

            if (_stanceElapsed > _waitDuration)
            {
                if (_requestId != _commitId)
                {
                    if (TryConsumeLatest(ref _commitId, out SkillCollisionProposal prop))
                    {
                        _startPos = p.Position;
                        _endPos = prop.collisionPos;
                    }
                }

                if (_requestId == _commitId)
                {
                    if (_dashElapsed == 0f)
                    {
                        p.Room.Push(p.Room.BroadcastAbigailSound, p, AbigailSound.YukiWeaponSkill2, 1f);
                    }

                    _dashElapsed += TimeUtil.Instance.DeltaTime;

                    if (_dashElapsed < _dashDuration)
                    {
                        float t = Math.Clamp(_dashElapsed / _dashDuration, 0f, 1f);
                        nextPos = Vector3.Lerp(_startPos, _endPos, t);
                    }

                    p.SendSkillMotion(
                        type: SkillMotionType.Transform,
                        start: p.Position,
                        end: nextPos
                    );
                }
            }
        }
            
        return;
    }

    public override void OnExit(Player p, SkillContext ctx)
    {
        base.OnExit(p, ctx);
    }
}

