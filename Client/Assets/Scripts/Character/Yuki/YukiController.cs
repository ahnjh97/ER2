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

    Vector3 _targetPos;

    protected override void Init()
    {
        base.Init();
        _attackRange = 1.5f;
    }

    //protected override void UpdateController()
    //{
    //    base.UpdateController();
    //}

    protected override void UpdateSkillKeyInput()
    {
        if (Input.GetKeyDown(KeyCode.Q))
        {
            _isSkillAtk = true;
            Debug.Log($"평타 강화 : {_isSkillAtk}");
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
        //PlayAnimation("SKILL_Q", 0.1f);
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
        LookAtMouse();

        Vector3 targetPos = GetTargetPos(_dashDistance);
        NavMeshHit hit;

        if (NavMesh.SamplePosition(GetTargetPos(_dashDistance), out hit, 0.5f, NavMesh.AllAreas))
        {
            _targetPos = hit.position;
        }
        else
        {
            if (GetReachablePosition(transform.position, targetPos, out hit) != Vector3.zero)
            {
                _targetPos = hit.position;
            }
        }

        StartCoroutine(CoMoveToTarget(_targetPos));
    }

    IEnumerator CoMoveToTarget(Vector3 targetPos)
    {
        _agent.enabled = false;

        while (Vector3.Distance(transform.position, targetPos) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPos, 15f * Time.deltaTime);
            UpdateTransform();
            yield return null;
        }

        State = CreatureState.Idle;
        _agent.enabled = true;
    }
    #endregion
}