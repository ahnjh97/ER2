using Google.Protobuf.Protocol;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using static CameraController;
using static Define;

public class TheodoreInputController : PlayerInputController
{
    #region const /enum 변수
    private const float EFFECT_DURATION = 4f;
    private const float CANCEL_DURATION = 0.3f;
    private const float SNIPER_AIM_DURATION = 10f;
    private const float AIM_WAIT_TIME = 0.8f;

    const bool SKIP_STATE_CHECK = false;
    #endregion

    private KeyCode? _currentSkillKey = null;
    private Coroutine _skillCoroutine = null;

    // Skill D - Sniper shot
    private const int SNIPER_SHOT_COUNT = 3;
    private int _SniperShotIdx = 0;

    // Skill Q - Speed
    float _originSpeed = 0f;
    const float SKILL_CHARGE_SPEED = 2.5f;

    private float _elapsedTime = 0f;

    private void Start()
    {
        _player.AttackRange = 6.0f;
        _originSpeed = _player.Speed;
    }
    public override C_SkillInput GetSkillCommand()
    {
        if (_skillCoroutine != null)
            return null;

        for (int i = 0; i < _skillKeys.Length; i++)
        {
            var key = _skillKeys[i];
            if (!Input.GetKeyDown(key))
                continue;

            if (IsCharge(key))
            {
                ChargeSkill(key);
                return null;
            }

            return _skill.TryCast((int)key, GetAttackableUnderCursorID(), GetMouseWorldPosition());
        }
        return null;
    }

    protected override Vector3 GetAttackStopPosition(Vector3 from, Vector3 target)
    {
        Vector3 dir = target - from;
        dir.y = 0f;
        float dist = dir.magnitude;
        if (dist <= Mathf.Epsilon)
            return target;
        dir /= dist;

        float stop = Mathf.Max(0.05f, _player.AttackRange); 

        return target - dir * stop;
    }

    public void OnDead()
    {
        if (_skillCoroutine != null)
        {
            StopCoroutine(_skillCoroutine);
            _skillCoroutine = null;
        }

        _currentSkillKey = null;
        _SniperShotIdx = 0;
        _elapsedTime = 0f;

        CameraController cc = Camera.main.gameObject.GetComponent<CameraController>();
        if (cc != null)
        {
            cc.EndAimMode();
        }

        _player.Indicator.DisableIndicator(_player.ObjInfo.Player.CharType, KeyCode.F1);
        if (_currentSkillKey.HasValue)
        {
            _player.Indicator.DisableIndicator(_player.ObjInfo.Player.CharType, _currentSkillKey.Value);
        }
        _player.UI.PlayerInterface.StopChargingBar();
    }

    protected override void ChargeSkill(KeyCode key)
    {
        if (!UseSkill(key))
            return;

        if (_skillCoroutine != null)
            return;

        _player.Indicator.EnableIndicator(_player.ObjInfo.Player.CharType, key);
        switch (key)
        {
        case KeyCode.Q:
                _skillCoroutine = StartCoroutine(ChargingSkill(key, onCancel: () => CancelSkill(key)));
                break;

            case KeyCode.D:
                _skillCoroutine = StartCoroutine(InputSkill(key,
                    onConfirm: () => ExecuteSniperSkill(key),
                    onCancel: () => CancelSkill(key)));
                break;

            case KeyCode.R:
            case KeyCode.W:
            case KeyCode.E:
                _skillCoroutine = StartCoroutine(InputSkill(key,
                    onConfirm: () => ExecuteSkill(key),
                    onCancel: () => CancelSkill(key)));
                break;
        }
    }
    #region 스킬 실행
    private void ExecuteSkill(KeyCode key)
    {
        _player.Indicator.DisableIndicator(_player.ObjInfo.Player.CharType, key);
        _player.UI.PlayerInterface.StopChargingBar();

        SendSkillInputPacket(key);
        _currentSkillKey = null;
        _skillCoroutine = null;
    }

    private void ExecuteSniperSkill(KeyCode key)
    {
        _player.Indicator.DisableIndicator(_player.ObjInfo.Player.CharType, key);
        StartCoroutine(SniperSkill(key));
    }

    private IEnumerator SniperSkill(KeyCode key)
    {
        _player.LookAtMouse();

        // 인디케이터
        SendSkillInputPacket(key);
        _player.Indicator.EnableIndicator(_player.ObjInfo.Player.CharType, KeyCode.F1);

        // 카메라
        CameraController cc = Camera.main.gameObject.GetComponent<CameraController>();
        if (cc == null)
            yield break;
        cc.StartAimMode(_player.transform, GetOppositeScreenEdgeFromPlayer());

        float elapsed = 0;
        float waitElapsed = 2;
        while (elapsed < SNIPER_AIM_DURATION)
        {
            elapsed += Time.deltaTime;

            // 1. 스킬 취소
            if (Input.GetMouseButtonDown(1))
            {
                _player.Indicator.DisableIndicator(_player.ObjInfo.Player.CharType, KeyCode.F1);
                _skillCoroutine = null;
                _SniperShotIdx = 0;
                cc.EndAimMode();
                SendSkillCancelPacket(key);
                yield break;
            }

            // 2. 스킬 공격
            waitElapsed += Time.deltaTime;
            if (Input.GetMouseButtonDown(0) && waitElapsed >= AIM_WAIT_TIME)
            {
                waitElapsed = 0;
                SendSkillExecutePacket(key);
                StartCoroutine(SniperShooting(key)); 
            }
            yield return null;
        }

         _SniperShotIdx = 0;
        _skillCoroutine = null;
        cc.EndAimMode();
        _player.Indicator.DisableIndicator(_player.ObjInfo.Player.CharType, KeyCode.F1);
    }
    private IEnumerator SniperShooting(KeyCode key)
    {
        _player.Indicator.ActiveIndicator(_player.ObjInfo.Player.CharType, KeyCode.F1, false);
        yield return new WaitForSeconds(0.5f); 
        ++_SniperShotIdx;

        if (_SniperShotIdx >= SNIPER_SHOT_COUNT) 
        {
            _SniperShotIdx = 0;

            CameraController cc = Camera.main.gameObject.GetComponent<CameraController>();
            if (cc == null)
                yield break;

            cc.EndAimMode();
            SendSkillCancelPacket(key);
            yield break;
        }

        _player.Indicator.ActiveIndicator(_player.ObjInfo.Player.CharType, KeyCode.F1, true);
    }
    #endregion

    #region 스킬 입력 처리
    private bool UseSkill(KeyCode key)
    {
        _skill.SkillDict.TryGetValue(key, out SkillBase skill);
        if (skill == null || skill.CurLevel <= 0)
            return false;
        if (_skill.CoolDownDict.TryGetValue(key, out PlayerSkillController.CoolTime coolTimeInfo))
        {
            if (coolTimeInfo.isCoolDown)
                return false;
        }
        _currentSkillKey = key;
        return true;
    }
    private IEnumerator InputSkill(KeyCode key, Action onConfirm, Action onCancel)
    {
        if(key == KeyCode.R)
        {
            _player.UI.PlayerInterface.SetMaintainChargingBar(DataManager.SkillDict[CharacterType.Theodore][key].name, 1.5f, 3); 
        }

        while (!Input.GetKeyUp(key) && !Input.GetMouseButtonDown(0))
        {
            if (Input.GetMouseButtonDown(1))
            {
                onCancel?.Invoke();
                yield break;
            }
            yield return null;
        }
        
        onConfirm?.Invoke();
    }

    private IEnumerator ChargingSkill(KeyCode key, Action onCancel)
    {
        _player.UI.PlayerInterface.SetChargingBar(DataManager.SkillDict[CharacterType.Theodore][key].name, 1.5f, EFFECT_DURATION);
        _player.Speed -= SKILL_CHARGE_SPEED;

        SendSkillPreparePacket(key);
        while (Input.GetKey(key) &&  _elapsedTime < EFFECT_DURATION)
        {
            _elapsedTime += Time.deltaTime;
            yield return null;
        }

        if (_elapsedTime < EFFECT_DURATION)
        {
            SendSkillInputPacket(key, SKIP_STATE_CHECK);
        }
        else
        {
            SendSkillCancelPacket(key);
        }

        _player.Speed = _originSpeed;
        onCancel.Invoke();
    }

    private void CancelSkill(KeyCode key)
    {
        _player.Indicator.DisableIndicator(_player.ObjInfo.Player.CharType, key);
        _player.UI.PlayerInterface.StopChargingBar();

        _elapsedTime = 0;
        _currentSkillKey = null;
        _skillCoroutine = null;
    }

#endregion


    #region Packet
    private void SendSkillInputPacket(KeyCode key, bool checkSkillState = true)
    {
        C_SkillInput skillCmd = _skill.TryCast(
            (int)key,
            GetAttackableUnderCursorID(),
            GetMouseWorldPosition(),
            checkSkillState
        );

        if (skillCmd != null)
            Managers.Network.Send(skillCmd);
    }
    private void SendSkillCancelPacket(KeyCode key)
    {
        C_SkillCancel cancelPacket = new C_SkillCancel
        {
            ObjectId = _player.ObjInfo.ObjectId,
            SkillKey = (int)key
        };
        Managers.Network.Send(cancelPacket);
    }
    private void SendSkillPreparePacket(KeyCode key)
    {
        C_SkillPrepare preparePacket = new C_SkillPrepare
        {
            ObjectId = _player.ObjInfo.ObjectId,
            SkillKey = (int)key
        };
        Managers.Network.Send(preparePacket);
    }
    private void SendSkillExecutePacket(KeyCode key)
    {
        Vector2 mousePos = _player.GetMousePos();
        C_SkillExecute executePacket = new C_SkillExecute
        {
            ObjectId = _player.ObjInfo.ObjectId,
            SkillKey = (int)key,
            MousePosX = mousePos.x,
            MousePosZ = mousePos.y
        };
        Managers.Network.Send(executePacket);
    }
    #endregion

    #region Utils
    // 마우스 방향의 반대편 스크린 좌가장자리를 가져옴
    private ScreenEdge GetOppositeScreenEdgeFromPlayer()
    {
        Vector3 playerForward = _player.transform.forward;

        if (Mathf.Abs(playerForward.x) > Mathf.Abs(playerForward.z))
        {
            return playerForward.x > 0 ? ScreenEdge.Right : ScreenEdge.Left;
        }
        else
        {
            return playerForward.z > 0 ? ScreenEdge.Top : ScreenEdge.Bottom;
        }
    }
    #endregion
}