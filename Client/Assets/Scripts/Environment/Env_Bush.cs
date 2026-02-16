using Google.Protobuf.Protocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class Env_Bush : EnvController
{
    public enum BushState
    {
        Visible,
        Hidden,
        Translucent
    }

    [SerializeField] public BoxCollider _bushCollider;

    List<int> _insidePlayersId = new List<int>();
    Dictionary<int, Coroutine> _delayedVisibleCoroutines = new Dictionary<int, Coroutine>();

    protected override void Init() => base.Init();
    void FixedUpdate()
    {
        CheckBushStatus();
    }
    private void CheckBushStatus()
    {
        Vector3 center = transform.TransformPoint(_bushCollider.center);
        Vector3 halfExtents = Vector3.Scale(_bushCollider.size / 2f, transform.lossyScale);
        Quaternion rotation = transform.rotation;

        Collider[] hitColliders = Physics.OverlapBox(center, halfExtents, rotation, ~0);
        List<int> currentInsidePlayersId = new List<int>();
        foreach (Collider hitCollider in hitColliders)
        {
            if (hitCollider.TryGetComponent<PlayerController>(out PlayerController pc))
            {
                currentInsidePlayersId.Add(pc.Id);
            }
        }

        CheckWardsInBush(hitColliders);

        // 나간 플레이어 처리
        for (int i = _insidePlayersId.Count - 1; i >= 0; i--)
        {
            int oldId = _insidePlayersId[i];
            GameObject exGo = Managers.Object.FindById(oldId);
            if (exGo == null) 
                continue;
            PlayerController pc = exGo.GetComponentInChildren<PlayerController>();

            if (pc != null && pc.State == CreatureState.Dead)
            {
                _insidePlayersId.RemoveAt(i);

                if (_delayedVisibleCoroutines.ContainsKey(oldId))
                {
                    StopCoroutine(_delayedVisibleCoroutines[oldId]);
                    _delayedVisibleCoroutines.Remove(oldId);
                }

                GameObject effect = Managers.FX.Effect.FindCurrentPlayEffect(oldId, "FX_PassiveShideld");
                if (effect != null)
                    Managers.FX.Effect.RemoveEffect(oldId, effect);

                pc.BushRenderType((int)BushState.Visible);
                continue;
            }

            if (!currentInsidePlayersId.Contains(oldId))
            {
                _insidePlayersId.RemoveAt(i);

                if (exGo != null && exGo.TryGetComponent<PlayerController>(out PlayerController exPc))
                {
                    BushExitRender(exPc);
                }
            }
        }

        // 새로 들어온 플레이어 처리
        foreach (int newId in currentInsidePlayersId)
        {
            if (!_insidePlayersId.Contains(newId))
            {
                GameObject newGo = Managers.Object.FindById(newId);
                PlayerController pc = newGo.GetComponentInChildren<PlayerController>();

                if (pc != null && (pc.State == CreatureState.Dead || pc.IsRespawned))
                    continue;

                if (newGo != null && newGo.TryGetComponent<PlayerController>(out PlayerController newPc))
                {
                    _insidePlayersId.Add(newId);
                    BushEnterRender(newPc);
                }
            }
        }

        UpdateInsidePlayersRender(); 
    }

    private void BushExitRender(PlayerController pc)
    {
        if (pc.ObjInfo.Player.CharType == CharacterType.Theodore)
        {
            bool isEnemyTeam = pc.ObjInfo.Player.Team != Managers.Object.MyPlayer.ObjInfo.Player.Team;
            if (isEnemyTeam)
            {
                pc.BushRenderType((int)BushState.Hidden);
                SetVisibility(pc.Id, false);

                if (_delayedVisibleCoroutines.ContainsKey(pc.Id))
                {
                    StopCoroutine(_delayedVisibleCoroutines[pc.Id]);
                }
                _delayedVisibleCoroutines[pc.Id] = StartCoroutine(DelayedVisible(pc, 2.5f));
            }
            else
            {
                GameObject effect = Managers.FX.Effect.FindCurrentPlayEffect(pc.Id, "FX_PassiveShideld");
                if (effect != null)
                    Managers.FX.Effect.RemoveEffect(pc.Id, effect);

                pc.PlaySkillEffect(KeyCode.F1, default(Vector3), default(Vector3));
                pc.BushRenderType((int)BushState.Translucent);
                SetVisibility(pc.Id, true);

                if (_delayedVisibleCoroutines.ContainsKey(pc.Id))
                {
                    StopCoroutine(_delayedVisibleCoroutines[pc.Id]);
                }
                _delayedVisibleCoroutines[pc.Id] = StartCoroutine(DelayedVisible(pc, 2.5f));
            }
        }
        else
        {
            pc.BushRenderType((int)BushState.Visible);
            SetVisibility(pc.Id, true);
        }


        Managers.Object.MyPlayer.UI.PlayerHUD.SetMinimapCharImgEnable(pc.Id, false);
        foreach (int id in _insidePlayersId)
        {
            GameObject inGo = Managers.Object.FindById(id);
            if (inGo == null) continue;

            PlayerController inPc = inGo.GetComponent<PlayerController>();
            if (inPc == null) continue;

            // 안에 있는 적팀 아이콘 끄기
            if (Managers.Object.MyPlayer.ObjInfo.Player.Team != inPc.ObjInfo.Player.Team)
                Managers.Object.MyPlayer.UI.PlayerHUD.SetMinimapCharImgEnable(id, false);
        }

        //UpdateRemainingPlayersRender();
    }
    private void UpdateInsidePlayersRender()
    {
        bool hasMyTeammate = false;

        foreach (int id in _insidePlayersId)
        {
            GameObject go = Managers.Object.FindById(id);
            if (go == null) continue;

            PlayerController pc = go.GetComponent<PlayerController>();
            if (pc == null) continue;

            if (pc.ObjInfo.Player.Team == Managers.Object.MyPlayer.ObjInfo.Player.Team)
            {
                hasMyTeammate = true;
                break;
            }
        }

        foreach (int id in _insidePlayersId)
        {
            GameObject go = Managers.Object.FindById(id);
            if (go == null) continue;

            PlayerController pc = go.GetComponent<PlayerController>();
            if (pc == null) continue;

            bool isSameTeam = pc.ObjInfo.Player.Team == Managers.Object.MyPlayer.ObjInfo.Player.Team;
            bool isTheodore = pc.ObjInfo.Player.CharType == CharacterType.Theodore;
            bool hasTheodorePassive = false;

            if (isTheodore && !isSameTeam)
            {
                GameObject effect = Managers.FX.Effect.FindCurrentPlayEffect(pc.Id, "FX_PassiveShideld");
                hasTheodorePassive = (effect != null);
            }

            BushState targetState;
            if (hasTheodorePassive)
            {
                targetState = BushState.Hidden;
            }
            else if (isSameTeam || hasMyTeammate || IsOurTeamWardInBush)
            {
                targetState = BushState.Translucent;
            }
            else
            {
                targetState = BushState.Hidden;
            }

            if (Managers.Object.MyPlayer.View.VisibleObjectIds.Contains(id)) // VisionShare 상태이면
            {
                targetState = BushState.Translucent;
            }

            pc.BushRenderType((int)targetState);
            SetVisibility(pc.Id, targetState != BushState.Hidden);
        }

        UpdateInsideEnemyWardsRender(hasMyTeammate || IsOurTeamWardInBush);
    }

    private IEnumerator DelayedVisible(PlayerController pc, float delay)
    {
        float elapsed = 0f;

        while (elapsed < delay)
        {
            if (pc == null || pc.State == CreatureState.Dead)
            {
                CleanupDelayedVisible(pc);
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

         CleanupDelayedVisible(pc);
    }

    private void CleanupDelayedVisible(PlayerController pc)
    {
        GameObject effect = Managers.FX.Effect.FindCurrentPlayEffect(pc.Id, "FX_PassiveShideld");
        if (effect != null)
            Managers.FX.Effect.RemoveEffect(pc.Id, effect);

        if (_delayedVisibleCoroutines.ContainsKey(pc.Id))
            _delayedVisibleCoroutines.Remove(pc.Id);

        pc.BushRenderType((int)BushState.Visible);
        SetVisibility(pc.Id, true);
    }

    #region Interaction
    private void BushEnterRender(PlayerController target)
    {
        if (_delayedVisibleCoroutines.ContainsKey(target.Id))
        {
            StopCoroutine(_delayedVisibleCoroutines[target.Id]);
            _delayedVisibleCoroutines.Remove(target.Id);

            GameObject effect = Managers.FX.Effect.FindCurrentPlayEffect(target.Id, "FX_PassiveShideld");
            if (effect != null)
                Managers.FX.Effect.RemoveEffect(target.Id, effect);
        }

        bool isSameTeam = Managers.Object.MyPlayer.ObjInfo.Player.Team == target.ObjInfo.Player.Team;
        bool amIInsideBush = _insidePlayersId.Contains(Managers.Object.MyPlayer.Id);
        bool isTheodore = target.ObjInfo.Player.CharType == CharacterType.Theodore;
        bool hasTheodorePassive = false;

        if (isTheodore && !isSameTeam)
        {
            GameObject effect = Managers.FX.Effect.FindCurrentPlayEffect(target.Id, "FX_PassiveShideld");
            hasTheodorePassive = (effect != null);
        }

        BushState targetState;

        if (hasTheodorePassive)
        {
            targetState = BushState.Hidden;
        }
        else if (isSameTeam)
        {
            targetState = BushState.Translucent;
        }
        else if (amIInsideBush)
        {
            targetState = BushState.Translucent;
            Managers.Object.MyPlayer.UI.PlayerHUD.SetMinimapCharImgEnable(id: target.Id, true);
        }
        else
        {
            targetState = BushState.Hidden;
            Managers.Object.MyPlayer.UI.PlayerHUD.SetMinimapCharImgEnable(id: target.Id, false);
        }

        target.BushRenderType((int)targetState);
        SetVisibility(target.Id, targetState != BushState.Hidden);

        foreach (int id in _insidePlayersId)
        {
            if (id == target.Id) continue;

            GameObject inGo = Managers.Object.FindById(id);
            if (inGo == null) continue;

            PlayerController inPc = inGo.GetComponent<PlayerController>();
            if (inPc == null) continue;

            if (inPc.ObjInfo.Player.Team != target.ObjInfo.Player.Team)
                Managers.Object.MyPlayer.UI.PlayerHUD.SetMinimapCharImgEnable(id: inPc.Id, true);

            if (Managers.Object.MyPlayer.Id == target.Id)
            {
                inPc.BushRenderType((int)BushState.Translucent);
                SetVisibility(inPc.Id, true); 
            }
        }
    }
    #endregion

    #region Ward
    bool IsOurTeamWardInBush = false;
    HashSet<int> enemyWardIds = new HashSet<int>();

    void CheckWardsInBush(Collider[] hitColliders) // 우리팀이 설치한 와드가 이 부쉬 안에 있는지
    {
        bool ourTeamWardInBush = false;
        enemyWardIds.Clear();

        foreach (Collider hitCollider in hitColliders)
        {
            if (!hitCollider.TryGetComponent<WardController>(out WardController wc))
                continue;
            if (wc.TeamIndex == Managers.Object.MyPlayer.ObjInfo.Player.Team) // 아군 와드
            {
                ourTeamWardInBush = true;
            }
            else // 적군 와드
            {
                enemyWardIds.Add(wc.Id);
            }
        }

        IsOurTeamWardInBush = ourTeamWardInBush;
    }

    void UpdateInsideEnemyWardsRender(bool hasVision)
    {
        foreach (int id in enemyWardIds)
        {
            GameObject go = Managers.Object.FindById(id);
            if (go == null) continue;
            WardController wc = go.GetComponentInChildren<WardController>();
            if (wc == null) continue;
            wc.IsVisible = hasVision;
            wc.IsInBush = true;
        }
    }

    public bool IsInBush(Vector3 pos)
    {
        Vector3 center = transform.TransformPoint(_bushCollider.center);
        Vector3 halfExtents = Vector3.Scale(_bushCollider.size / 2f, transform.lossyScale);
        Quaternion rotation = transform.rotation;
        var bounds = new Bounds(center, halfExtents * 2f);
        if (bounds.Contains(pos))
            return true;
        return false;
    }


    #endregion

    #region FX/UI
    private void SetVisibility(int targetId, bool visible)
    {
        Managers.FX.Effect.SetOwnerVisible(targetId, visible);
        Managers.WorldUI.SetEmoticonVisibility(targetId, visible);
    }
    #endregion
}
