using Data;
using Google.Protobuf.Protocol;
using NUnit.Framework.Constraints;
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.AI;
using static EmoticonController;

public class PlayerInputController : MonoBehaviour
{
    protected MyPlayerController _player;    
    protected PlayerSkillController _skill;
    private NavMeshAgent _agent;

    [SerializeField] float _stopBuffer = 0.1f;

    private GameObject _target;

    [SerializeField] private LayerMask _groundMask;
    [SerializeField] private LayerMask _monsterMask;
    [SerializeField] private LayerMask _playerMask;
    [SerializeField] private LayerMask _beaconMask;
    [SerializeField] private LayerMask _deployingLoopMask;

    // 커서가 올려져 있는 현재 타겟
    private GameObject _hoverTarget;

    // 사거리 밖에서 타겟팅된 DeployingLoop
    private IO_DeployingLoop _pendingDeployingLoop;
    private IO_DeployingLoop _activeDeployingLoop; // 실제 상호작용 중인 대상

    // 공격 입력을 얼마나 자주 서버로 보낼지 제한 (스팸 방지)
    private float _nextAutoAttackSendTime;
    [SerializeField] private float _attackInputInterval = 0.08f; // 0.08초마다 1번(초당 약 12번)

    // 최소 이동 가능 거리
    private float _minClickMoveDistance = 0.3f;

    readonly private string Rest_Desc_NotReady = "전투 중에는 휴식을 할 수 없습니다.";
    readonly private string DeployingLoop_Desc_NotReady = "지금은 준비되지 않았습니다.";
    readonly private string Ping_Desc_NotReady = "추가 신호를 보내려면 기다려야 합니다.";
    readonly private string Emoticon_Desc_NotReady = "추가 감정표현을 하려면 기다려야 합니다.";

    private void Awake()
    {
        _player = GetComponentInChildren<MyPlayerController>();
        _agent = GetComponentInChildren<NavMeshAgent>();
        _skill = GetComponentInChildren<PlayerSkillController>();

        _groundMask = 1 << LayerMask.NameToLayer("Map");
        _monsterMask = 1 << LayerMask.NameToLayer("Monster");
        _playerMask = 1 << LayerMask.NameToLayer("Player");
        _beaconMask = 1 << LayerMask.NameToLayer("Beacon");
        _deployingLoopMask = 1 << LayerMask.NameToLayer("DeployingLoop");
    }

    // 커서 아래에 대상 없음 → 지형 이동 패킷
    // 커서 아래에 대상은 있지만 사거리 밖 -> 타겟 추적 이동
    public virtual C_SetMoveTarget GetSetMoveTarget()
    {
        if (_player.State == CreatureState.Idle 
            || _player.State == CreatureState.Moving 
            || _player.State == CreatureState.Attack 
            || _player.State == CreatureState.Skill 
            || _player.State == CreatureState.Operate
            || _player.State == CreatureState.Teleport)
        {
            if (!Input.GetMouseButton(1))
                return null;

            GameObject target = GetAttackableUnderCursor();
            if (target == null)
            {
                // 땅 이동
                if (!TryGetGroundDestination(out Vector3 final))
                    return null;

                // 너무 가까운 위치면 이동 패킷 보내지 않기
                Vector3 diff = final - _player.transform.position;
                if (diff.sqrMagnitude < _minClickMoveDistance * _minClickMoveDistance)
                    return null;

                if (Input.GetMouseButtonDown(1))
                {
                    Vector3 mousePos = GetMouseWorldPosition();
                    mousePos.y = _player.transform.position.y;
                    _player.PlayCommonCasterEffect(commonName: "Move", mousePos: mousePos, default, default);
                }
                    
                return new C_SetMoveTarget
                {
                    IsGround = true,
                    TargetPos = new PositionInfo { PosX = final.x, PosY = final.y, PosZ = final.z }
                };
            }
            else
            {
                // 사거리 안이면 이동 패킷 보내지 않기
                var cc = target.GetComponentInChildren<CreatureController>();
                if (cc != null && IsInAttackRange(_player.transform.position, cc.transform.position))
                    return null;

                if (!TryGetTargetDestination(target, out Vector3 final, out int id))
                    return null;

                return new C_SetMoveTarget
                {
                    IsGround = false,
                    TargetId = id,
                    TargetPos = final,
                };
            }
        }
        else if (_player.State == CreatureState.Rest)
        {
            if(!_agent.enabled)
                _agent.enabled = true;
            _agent.isStopped = true;
            return null;
        }
        else
        {
            return null;
        }
    }

    // 타겟 + 사거리 안
    public C_Attack GetAttackCommand()
    {
        // 공격 가능한 상태만 처리
        if (!(_player.State == CreatureState.Idle
            || _player.State == CreatureState.Moving
            || _player.State == CreatureState.Attack
            || _player.State == CreatureState.Skill))
            return null;

        // 우클릭이 아예 안 눌려 있으면 상태 리셋
        if (!Input.GetMouseButton(1))
        {
            _hoverTarget = null;
            _nextAutoAttackSendTime = 0f;
            return null;
        }

        // 커서 아래 공격 가능한 대상 찾기
        GameObject target = GetAttackableUnderCursor();
        _hoverTarget = target;

        if (_hoverTarget == null)
            return null;

        var cc = _hoverTarget.GetComponent<CreatureController>();
        if (cc == null)
            return null;

        // ===== 거리(사거리) 체크 =====
        if (!IsInAttackRange(_player.transform.position, cc.transform.position))
            return null;

        // ===== 실제로 공격 패킷을 보낼지 결정 =====
        bool explicitClick = Input.GetMouseButtonDown(1); // 딱 누른 순간
        bool autoRepeat = Input.GetMouseButton(1) && Time.time >= _nextAutoAttackSendTime; // 홀드 중 자동 반복

        if (!explicitClick && !autoRepeat)
            return null;

        // 다음 자동 공격 입력 시간 갱신 (스팸 방지용)
        _nextAutoAttackSendTime = Time.time + _attackInputInterval;

        // 공격 패킷 생성
        return new C_Attack { TargetId = cc.Id };
    }

    public C_Operate GetOperateCommand()
    {
        if(_player.State == CreatureState.Idle || _player.State == CreatureState.Moving || _player.State == CreatureState.Attack)
        {
            if (!Input.GetMouseButtonDown(1))
                return null;

            GameObject beacon = GetBeaconUnderCursor();
            if (null == beacon)
                return null;

            C_Operate operatePkt = new C_Operate();
            operatePkt.BeaconName = beacon.name;

            Vector3 playerPos = _player.transform.position;
            Vector3 beaconPos = beacon.transform.position;

            Vector3 dir = (beaconPos - playerPos).normalized;
            float distance = Vector3.Distance(playerPos, beaconPos);

            Vector3 bestPos = playerPos;
            bool found = false;

            // 일정 간격으로 앞으로 이동하면서 네비메쉬 위 지점 탐색
            for (float d = 0.5f; d <= distance; d += 0.5f)
            {
                Vector3 checkPos = playerPos + dir * d;
                if (NavMesh.SamplePosition(checkPos, out NavMeshHit hit, 0.4f, NavMesh.AllAreas))
                {
                    bestPos = hit.position;
                    found = true;
                }
            }

            // 혹시 플레이어-비콘 사이에 네비 지점이 없으면 비콘 근처라도 시도
            if (!found && NavMesh.SamplePosition(beaconPos, out NavMeshHit fallback, 3.0f, NavMesh.AllAreas))
            {
                bestPos = fallback.position;
            }

            operatePkt.PosX = bestPos.x;
            operatePkt.PosZ = bestPos.z;

            return operatePkt;
        }

        return null;
    }

    public C_DeployingLoop GetDeployingLoopCommand()
    {
        // 스킬/휴식/스턴 등 다른 상태면 상호작용 대기 취소
        if ((_player.State != CreatureState.Idle && _player.State != CreatureState.Moving) || _player.State == CreatureState.Dead)
        {
            _pendingDeployingLoop = null;
            return null;
        }

        // 1) 새 우클릭이 들어온 경우 → 기존 pending 취소/갱신
        if (Input.GetMouseButtonDown(1))
        {
            GameObject hit = GetMouseTargetInLayer(_deployingLoopMask);
            IO_DeployingLoop clickedIo = hit ? hit.GetComponentInChildren<IO_DeployingLoop>() : null;

            // 이전에 다른 DeployingLoop를 향해 가고 있었는데,
            // 이번에 클릭한 게 그게 아니면 → 대기 상호작용 취소
            if (_pendingDeployingLoop != null && clickedIo != _pendingDeployingLoop)
                _pendingDeployingLoop = null;

            // 이번 우클릭이 DeployingLoop가 아니면 return
            if (clickedIo == null)
                return null;

            // 사거리 안이면 즉시 상호작용 패킷 전송
            if (clickedIo.IsPlayerInside)
            {
                if(clickedIo.IsUsable)
                {
                    _activeDeployingLoop = clickedIo;
                    _activeDeployingLoop.Begin();
                    return new C_DeployingLoop
                    {
                        ObjectId = _player.Id,
                        IoPos = clickedIo.GetLookTargetPosition(),
                    };
                }
                else
                    _player.UI.ActionNotReady.Show(DeployingLoop_Desc_NotReady);
            }
                         
            // 사거리 밖이면 : 도착 후 자동 상호작용을 위해 pending 으로 기록만 해 둠
            _pendingDeployingLoop = clickedIo;
            return null;
        }

        // 2) 새 클릭은 없고, 예전에 클릭해 둔 DeployingLoop가 있는 경우
        if (_pendingDeployingLoop != null)
        {
            // 트리거 안에 들어온 순간 → 한 번만 패킷 전송
            if (_pendingDeployingLoop.IsPlayerInside && _pendingDeployingLoop.IsUsable)
            {
                _activeDeployingLoop = _pendingDeployingLoop;
                _activeDeployingLoop.Begin();
                var pkt = new C_DeployingLoop
                {
                    ObjectId = _player.Id,
                    IoPos = _pendingDeployingLoop.GetLookTargetPosition(),
                };

                _pendingDeployingLoop = null;
                return pkt;
            }
        }

        return null;
    }

    // S키 : 공격, 이동 중지 -> Idle 상태 벗어나면 다시 자동 공격
    // H키 : 이동 중지
    public C_Stop GetStopCommand()
    {
        if (ChatHandler.Instance.IsChatting)
            return null;

        if (_player.State == CreatureState.Dead)
            return null;

        if (Input.GetKeyDown(KeyCode.S))
        {
            CancelDeployingLoopInteraction();
            return new C_Stop { Reason = StopReason.StopAll };
        }
        if (Input.GetKeyDown(KeyCode.H))
        {
            CancelDeployingLoopInteraction();
            return new C_Stop { Reason = StopReason.StopMoveOnly };
        }
        return null;
    }

    protected static readonly KeyCode[] _skillKeys =
    {
        KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.D, KeyCode.F
    };

    public virtual C_SkillInput GetSkillCommand()
    {
        if (ChatHandler.Instance.IsChatting)
            return null;

        if (_player.State == CreatureState.Dead)
            return null;

        // 배열 순서대로 키다운 검사 -> 처음 눌린 키에 대해 바로 생성/리턴
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

    protected bool IsCharge(KeyCode key)
    {
        SkillData skillData = DataManager.SkillDict[_player.ObjInfo.Player.CharType][key];
        if (skillData == null)
            return false;

        if (Enum.TryParse(skillData.skillType, out SkillInputType skillType))
        {
            if (skillType == SkillInputType.Charge)
                return true;
        }
        return false;
    }

    public C_Rest GetRestCommand()
    {
        if (ChatHandler.Instance.IsChatting)
            return null;

        if (_player.CombatStat == CombatState.Combat)
        {
            if (Input.GetKeyDown(KeyCode.X))
                _player.UI.ActionNotReady.Show(Rest_Desc_NotReady);
            return null;
        }

        if (_player.State == CreatureState.Dead)
            return null;

        if (_player.IsRest == false && _player.State != CreatureState.Rest)
        {
            if (Input.GetKeyDown(KeyCode.X))
            {
                //Debug.Log("휴식 진입");
                _player.IsRest = true;
                return new C_Rest() { IsRest = _player.IsRest };
            }
        }
        else if (_player.IsRest == true  && _player.State == CreatureState.Rest)
        {
            if (Input.GetKeyDown(KeyCode.X) || Input.GetMouseButtonDown(1))
            {
                //Debug.Log("휴식 해제");
                _player.IsRest = false;
                return new C_Rest() { IsRest = _player.IsRest };
            }
        }

        return null;
    }

    public C_UseItem GetUseItemCommand()
    {
        if(_player.State == CreatureState.Idle || _player.State == CreatureState.Moving
            || _player.State == CreatureState.Attack || _player.State == CreatureState.Rest)
        {
            for (int i = 0; i <= 9; i++)
            {
                KeyCode alphaKey = (KeyCode)((int)KeyCode.Alpha0 + i);

                if (Input.GetKeyDown(alphaKey))
                {
                    if(_player.CheckInventory(i))
                    {
                        int index = 0;
                        if (i == 0)
                            index = 9;
                        else
                            index = i - 1;
                        
                        Vector3 playerToMouse = GetMouseWorldPosition() - _player.transform.position;
                        playerToMouse.y = 0;
                        float dist = playerToMouse.magnitude;

                        Vector3 result = GetMouseWorldPosition();
                        if (dist > 8.5f)
                        {
                            result = _player.transform.position + playerToMouse.normalized * 8.5f;
                        }

                        C_SkillCollisionPropose propose = _skill.ComputeSkillCollision(0, 0, CollisionType.Pass, _player.transform.position.x, _player.transform.position.z, result.x, result.z);

                        result.x = propose.CollisionX;
                        result.z = propose.CollisionZ;
                        //Vector3 validPos = GetMouseWorldPosition();

                        //var path = new NavMeshPath();
                        //if (!NavMesh.CalculatePath(_player.transform.position, result, _agent.areaMask, path) || path.status != NavMeshPathStatus.PathComplete)
                        //{
                        //    // 경로 자체가 없으면 레이캐스트로 첫 히트 포인트 클램프
                        //    if (NavMesh.Raycast(_player.transform.position, result, out var hit, NavMesh.AllAreas))
                        //    {
                        //        result = hit.position;
                        //    }
                        //}
                        //return validPos;


                        //if (NavMesh.SamplePosition(result, out var navHit, 2f, NavMesh.AllAreas) && _skill.getvail)
                        //    result = navHit.position;
                        //else
                        //    return null;

                        _player.UseInventoryItem(i);

                        return new C_UseItem()
                        {
                            ObjectId = _player.Id,
                            InventoryIndex = index,
                            MouseX = result.x,
                            MouseZ = result.z
                        }; 
                    }
                }
            }
        }
        return null;
    }

    // temp 임시 커맨드 나중에 삭제
    public C_Death GetDieCommand()
    {
        if (Input.GetKeyDown(KeyCode.Z))
            return new C_Death() { IsDeath = true };

        return null;
    }

    public KeyCode GetSkillLevelUpCommand()
    {
        if (Input.GetKey(KeyCode.LeftControl))
        {
            if (Input.GetKeyDown(KeyCode.Q))        { return KeyCode.Q; }       
            else if (Input.GetKeyDown(KeyCode.W))   { return KeyCode.W; } 
            else if (Input.GetKeyDown(KeyCode.E))   { return KeyCode.E; } 
            else if (Input.GetKeyDown(KeyCode.R))   { return KeyCode.R; }
            else if (Input.GetKeyDown(KeyCode.T)) { return KeyCode.T; }
        }

        return KeyCode.None;
    }

    public void GetPingCommand()
    {
        if (Input.GetKey(KeyCode.LeftAlt) && Input.GetMouseButtonDown(0))
        {
            Vector3 mousePos = GetMouseWorldPosition();
            mousePos.y = _player.transform.position.y;
            if(_player.Ping.TryPlacePing(mousePos) == false)
                _player.UI.ActionNotReady.Show(Ping_Desc_NotReady);
        }
    }

    public void GetEmoticonCommand()
    {
        if (Input.GetKeyDown(KeyCode.T) && !Input.GetKey(KeyCode.LeftControl))
        {
            if(_player.Emoticon.TryUseEmoticon() == EmoticonUseResult.Fail_WindowLimit)
                _player.UI.ActionNotReady.Show(Emoticon_Desc_NotReady);
        }
    }

    public C_KeyInputForTest Get_KeyInputForTestCommand()
    {
        if (Input.GetKeyDown(KeyCode.L))
            return new C_KeyInputForTest() { KeyCode = (int)KeyCode.L };

        return null;
    }

    #region Charge
    protected virtual void ChargeSkill(KeyCode key)
    {
    }

    #endregion

    #region Util
    protected Vector3 GetMouseWorldPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
            return hit.point;
        return Vector3.zero;
    }

    private GameObject GetAttackableUnderCursor(int mask = default,  float radius = 0.1f)
    {
        return _player.GetAttackableUnderCursor(mask, radius);
    }

    protected int GetAttackableUnderCursorID(float radius = 0.1f)
    {
        GameObject target = GetAttackableUnderCursor();
        if (target == null)
            return 0;

        var cc = target.GetComponentInChildren<CreatureController>();
        if (cc == null)
            return 0;

        return cc.Id;
    }

    GameObject GetBeaconUnderCursor(float radius = 0.1f)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if(Physics.SphereCast(ray, radius, out RaycastHit hit, 1000f, _beaconMask))
        {
            return hit.collider.gameObject;
        }

        return null;
    }

    GameObject GetMouseTargetInLayer(LayerMask mask, float radius = 0.1f)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.SphereCast(ray, radius, out RaycastHit hit, 1000f, mask))
            return hit.collider.gameObject;

        return null;
    }

    // 지형 클릭 시
    private bool TryGetGroundDestination(out Vector3 final)
    {
        final = default;

        int mapMask = 1 << LayerMask.NameToLayer("Map");
        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, 1000f, mapMask))
            return false;

        if (!NavMesh.SamplePosition(hit.point, out var navHit, 2f, NavMesh.AllAreas))
            return false;

        Vector3 desired = navHit.position;
        final = CalculateFinalDestination(_player.transform.position, desired);
        return true;
    }

    // 타겟 클릭 시
    private bool TryGetTargetDestination(GameObject targetGo, out Vector3 final, out int targetId)
    {
        final = default;
        targetId = 0;

        var cc = targetGo.GetComponentInChildren<CreatureController>();
        if (cc == null)
            return false;
        targetId = cc.Id;

        Vector3 targetPos = targetGo.transform.position;
        Vector3 desiredStop = GetAttackStopPosition(_player.transform.position, targetPos);
        final = CalculateFinalDestination(_player.transform.position, desiredStop);
        return true;
    }

    // 사거리-타겟 지점 계산
    protected virtual Vector3 GetAttackStopPosition(Vector3 from, Vector3 target)
    {
        Vector3 dir = target - from;
        dir.y = 0f;
        float dist = dir.magnitude;
        if (dist <= Mathf.Epsilon)
            return target;
        dir /= dist;

        float stop = Mathf.Max(0.05f, _player.AttackRange - _stopBuffer); 
        return target - dir * stop;
    }

    // 경로가 부분 경로면 마지막 코너를 반환
    protected virtual Vector3 CalculateFinalDestination(Vector3 from, Vector3 desired)
    {
        if (!NavMesh.SamplePosition(from, out var fromHit, 2f, NavMesh.AllAreas))
            fromHit.position = from;
        if (!NavMesh.SamplePosition(desired, out var toHit, 2f, NavMesh.AllAreas))
            toHit.position = desired;

        Vector3 start = fromHit.position;
        Vector3 end = toHit.position;

        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path) || path.corners.Length == 0)
        {
            return end;
        }

        return path.corners[path.corners.Length - 1];
    }

    private bool IsInAttackRange(Vector3 myPos, Vector3 targetPos)
    {
        Vector2 myXZ = new Vector2(myPos.x, myPos.z);
        Vector2 targetXZ = new Vector2(targetPos.x, targetPos.z);
        float dist = Vector2.Distance(myXZ, targetXZ);

        float effectiveRange = Mathf.Max(0.05f, _player.AttackRange - _stopBuffer);
        return dist <= effectiveRange;
    }

    public void CancelDeployingLoopInteraction()
    {
        _pendingDeployingLoop = null;

        if (_activeDeployingLoop != null)
        {
            _activeDeployingLoop.Cancel();    // 내부에서 UI_InteractionCharge.Cancel() 호출
            _activeDeployingLoop = null;
        }
    }
    #endregion

}

