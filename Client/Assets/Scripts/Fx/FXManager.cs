using UnityEngine;
using Data;
using Google.Protobuf.Protocol;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.VFX;


public class FXManager : MonoBehaviour
{
    public EffectFXManager Effect { get; private set; }
    public UIFXManager UI { get; private set; }

    private bool _isShuttingDown = false;

    public void Init()
    {
        GameObject rootObj = new GameObject("@UIFXPool_Root");
        rootObj.transform.SetParent(transform);
        _poolRoot = rootObj.transform;

        GameObject effectGO = new GameObject("EffectFXManager");
        effectGO.transform.SetParent(this.transform);
        Effect = effectGO.AddComponent<EffectFXManager>();
        Effect.Init();

        // UIFXManager 생성 및 자식으로 설정
        GameObject uiGO = new GameObject("UIFXManager");
        uiGO.transform.SetParent(this.transform); 
        UI = uiGO.AddComponent<UIFXManager>();
        UI.Init();
    }
    private Quaternion GetPlayerRotation(Transform casterTransform, string name = "")
    {
        if (name == "FX_Shield")
        {
            float playerYaw = casterTransform.rotation.eulerAngles.y;
            Quaternion yawRotationOnly = Quaternion.Euler(0, playerYaw, 0);
            Quaternion desiredXRotation = Quaternion.Euler(-90f, 180f, 0);
            return yawRotationOnly * desiredXRotation;
        }
  
        return casterTransform.rotation;
    }

    public List<GameObject> PlayEffect
        (int ownerId,                           // owner ID
        List<EffectData> effectData,            // Effect List
        Transform casterTransform,              // 대상 transform
        Vector3 mousePos,                       // 마우스 위치
        bool isCommon = false                    // 공통 이펙트
        )
    {
        bool hasShield = effectData.Exists(data => data.prefabName.Contains("FX_Shield"));
        Quaternion rotation = GetPlayerRotation(casterTransform, hasShield ? "FX_Shield" : "");

        return Effect.PlayEffect(ownerId, effectData, casterTransform, mousePos, new Vector3(), rotation, isCommon);
    }

    public List<GameObject> PlayEffect
       (int ownerId,                            // owner ID
       List<EffectData> effectData,             // Effect List
       Transform casterTransform,               // 대상 transform
       Vector3 mousePos,                        // 마우스 위치
       Vector3 targetPos,                       // 이펙트 목표 위치
       Quaternion rot = default(Quaternion),    // 이펙트 회전 값
       bool isCommon = false                    // 공통 이펙트
       )
    {
        return Effect.PlayEffect(ownerId, effectData, casterTransform, mousePos, targetPos, rot, isCommon);
    }


    public void PlayStatusEffect(GameObject target, CharacterType charType, float duration)
    {
        UI.PlayStatusEffect(target, charType, duration);
    }

    // 기타 유틸리티 
    public void RemoveAllEffect(int ownerId)
    {
        Effect.RemoveAllEffect(ownerId);
        UI.RemoveAllMarks(ownerId);
    }

    public void Clear()
    {
        Effect.Clear();
        UI.Clear();
    }

 
    #region FX 전용 Pool 
    // Pool
    private class Pool
    {
        public GameObject Prefab;
        public Transform Root;
        public Stack<GameObject> Available = new Stack<GameObject>();
        public HashSet<GameObject> InUse = new HashSet<GameObject>();
    }

    private Dictionary<int, Pool> _pools = new Dictionary<int, Pool>();
    private Transform _poolRoot;
    public void CreatePool(GameObject prefab, int initialSize)
    {
        if (prefab == null) 
            return;

        int prefabId = prefab.GetInstanceID();
        if (_pools.ContainsKey(prefabId)) 
            return;

        GameObject poolRoot = new GameObject($"Pool_{prefab.name}");
        if (poolRoot == null)
            return;

        poolRoot.transform.SetParent(_poolRoot);

        Pool pool = new Pool
        {
            Prefab = prefab,
            Root = poolRoot.transform
        };

        // 초기 오브젝트 생성
        for (int i = 0; i < initialSize; i++)
        {
            GameObject obj = Instantiate(prefab, pool.Root);
            obj.name = prefab.name;
            obj.SetActive(false);
            pool.Available.Push(obj);
        }

        _pools.Add(prefabId, pool);
    }
    public GameObject Pop(GameObject prefab, Transform parent)
    {
        if (prefab == null) return null;

        int prefabId = prefab.GetInstanceID();
        if (!_pools.TryGetValue(prefabId, out Pool pool))
        {
            CreatePool(prefab, 10);
            pool = _pools[prefabId];
        }

        GameObject obj;

        if (pool.Available.Count > 0)
        {
            obj = pool.Available.Pop();
        }
        else
        {
            obj = Instantiate(pool.Prefab, pool.Root);
            obj.name = pool.Prefab.name;
        }

        obj.transform.SetParent(parent);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;
        obj.SetActive(true);

        CreateEffect(obj);

        pool.InUse.Add(obj);
        return obj;
    }

    public void Push(GameObject obj)
    {
        if (obj == null) return;

        // 어느 풀에 속하는지 찾기
        foreach (var pool in _pools.Values)
        {
            if (!pool.InUse.Contains(obj))
                continue;

            pool.InUse.Remove(obj);

            // 매니저/풀 파괴 중이면 그냥 버림
            if (_isShuttingDown || pool.Root == null ||
                !pool.Root.gameObject.scene.IsValid())
            {
                Destroy(obj);
                return;
            }

            obj.transform.SetParent(pool.Root, false);
            pool.Available.Push(obj);

            CreateEffect(obj);

            obj.SetActive(false);

            return;
        }
    }

    void CreateEffect(GameObject obj)
    {
        ParticleSystem ps = obj.GetComponentInChildren<ParticleSystem>();
        if (ps != null)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(true);
        }

        VisualEffect vfx = obj.GetComponentInChildren<VisualEffect>();
        if (vfx != null)
        {
            vfx.Stop();
            vfx.Reinit();
        }
    }
    private void OnDestroy()
    {
        _isShuttingDown = true;
    }

    private void OnApplicationQuit()
    {
        _isShuttingDown = true;
    }
    #endregion
}
