using Google.Protobuf.Protocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using static UnityEngine.GraphicsBuffer;

public class YukiController : MyPlayerController
{
    float _dashDistance = 5f;
    float _dashDuration = 0.2f;

    private bool isDashing = false;

    protected override void Init()
    {
        base.Init();
        _attackRange = 1.5f;
    }

    protected override void UpdateSkillKeyInput()
    {
        if (IsKeyInput == false && Input.GetKeyDown(KeyCode.Q))
        {
            _isUseSkill = true;
            _keyCode = KeyCode.Q;
        }
        else if (IsKeyInput == false && Input.GetKeyDown(KeyCode.W))
        {
            _isUseSkill = true;
            _keyCode = KeyCode.W;
        }
        else if (IsKeyInput == false && Input.GetKeyDown(KeyCode.E))
        {
            _isUseSkill = true;
            _keyCode = KeyCode.E;
        }
        else if (IsKeyInput == false && Input.GetKeyDown(KeyCode.R))
        {
            _isUseSkill = true;
            _keyCode = KeyCode.R;
        }
        else if (Input.GetKeyDown(KeyCode.D))
        {

        }
    }

    protected override void Skill_Q()
    {
        PlayAnimation("SKILL_Q", 0.1f);
    }

    protected override void Skill_W()
    {
        PlayAnimation("SKILL_W", 0.1f);
    }

    protected override void Skill_E()
    {
        PlayAnimation("SKILL_E", 0.1f);

        Dash();
    }

    protected override void Skill_R()
    {
        PlayAnimation("SKILL_R", 0.1f);
    }

    #region Skill : E
    void Dash()
    {
        Vector3 _targetPos = GetTargetPos(_dashDistance);

        StartCoroutine(CoMoveToTarget(_targetPos));
    }

    IEnumerator CoMoveToTarget(Vector3 targetPos)
    {
        while (Vector3.Distance(transform.position, targetPos) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPos, 15f * Time.deltaTime);
            yield return null;
        }

        transform.position = targetPos;
    }

    //void Dash()
    //{
    //    Vector3 mousePos = Input.mousePosition;
    //    mousePos.z = Camera.main.transform.position.y;
    //    Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(mousePos);

    //    Vector3 direction = (mouseWorld - transform.position);
    //    direction.y = 0f;
    //    direction.Normalize();

    //    if (direction.sqrMagnitude > 0.001f)
    //    {
    //        transform.rotation = Quaternion.LookRotation(direction);
    //    }

    //    StartCoroutine(DashCoroutine(direction));
    //}

    //private IEnumerator DashCoroutine(Vector3 direction)
    //{
    //    isDashing = true;
    //    _navMeshAgent.enabled = false;

    //    Vector3 startPos = transform.position;
    //    Vector3 rawEndPos = startPos + direction * dashDistance;
    //    Vector3 endPos = rawEndPos;

    //    // Collider 있는 벽 체크
    //    if (CheckWall(startPos, rawEndPos, out Vector3 stopPos))
    //    {
    //        endPos = stopPos;
    //    }
    //    else
    //    {
    //        if (NavMesh.SamplePosition(rawEndPos, out NavMeshHit hit, 0.5f, NavMesh.AllAreas))
    //            endPos = hit.position;
    //        else
    //            endPos = startPos;
    //    }

    //    // 실제 대시 이동
    //    float elapsed = 0f;
    //    while (elapsed < dashDuration)
    //    {
    //        transform.position = Vector3.Lerp(startPos, endPos, elapsed / dashDuration);
    //        elapsed += Time.deltaTime;
    //        yield return null;
    //    }

    //    transform.position = endPos;
    //    _navMeshAgent.enabled = true;
    //    isDashing = false;
    //}

    //// Collider 있는 벽 체크
    //private bool CheckWall(Vector3 start, Vector3 end, out Vector3 stopPos)
    //{
    //    Vector3 dir = (end - start).normalized;
    //    float dist = Vector3.Distance(start, end);

    //    int dashWallMask = LayerMask.GetMask("Wall");

    //    if (Physics.Raycast(start, dir, out RaycastHit hit, dist, dashWallMask))
    //    {
    //        stopPos = hit.point;
    //        return true;
    //    }

    //    stopPos = end;
    //    return false;
    //}
    #endregion
}