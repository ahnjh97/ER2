using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Data;
using Google.Protobuf.Protocol;
using UnityEngine;
using UnityEngine.AI;

public class PlayerController : CreatureController
{
    bool _isKeyInput = false;
    int _atkCount = 1;
    int _maxAtkCount = 2;

    // SyncPos
    float _minDist = 3f;
    float _syncSpeed = 20f;
    Vector3 _serverPos;

    [SerializeField]
    private float AGENT_SPEED_RATIO = 1.3f;

    // Fog
    private FogOfWarVision _fogOfWarVision;

    protected bool _isSkillDebug = true;
    protected bool _isRest = false;
    public bool AllowOffPathMovement { get; set; } = false;

    // NameTag
    protected UI_PlayerNameTag _nameTag;
    public UI_PlayerNameTag NameTag { get { return _nameTag; } }

    public string NickName { get; set; } = "UserName";

    // 장착 아이템
    private Dictionary<EquipItemType, EquipItemInfo> _equipItemSlot = new Dictionary<EquipItemType, EquipItemInfo>();
    public Dictionary<EquipItemType, EquipItemInfo> EquipItemSlot { get { return _equipItemSlot; } }
    public ItemStat ItemStat { get; private set; } = new ItemStat();

    // 애니메이션 관련
    protected GameObject _eqipWeapon = null;
    protected List<GameObject> _restItems = new List<GameObject>();
    protected Animator _weaponAnimator = null;

    public SoundController Sound;

    // 유키 스킬 이펙트
    public SkillEffectHandler YukiEffects { get; private set; } = new SkillEffectHandler();

    // Kill Count : 20초 안에 얼만큼의 처치했는는가?
    public float CurrentMultiKillCnt
    {
        get { return _currentMultiKillCount;  }
        set { ++_currentMultiKillCount; }
    }
    private const float _multiKillTimeLimit = 20.0f;
    private float _currentMultiKillCount = 0;
    private float _lastKillTime = 0.0f;

    private float _baseMoveSpeed; // 기본 이동속도

    // Ping
    public PingController Ping { get; private set; }

    // Emoticon
    public EmoticonController Emoticon { get; private set; }
    public UI_Emoticon EmoticonUI { get; protected set; }

    #region Property
    public override float Attack
    {
        get { return base.Attack; }
        set { base.Attack = value; }
    }

    public float AttackSpeed
    {
        get { return Stat.AttackSpeed; }
        set { Stat.AttackSpeed = value; }
    }

    public override float Defense
    {
        get { return base.Defense; }
        set { base.Defense = value; }
    }

    public float CriticalRatio { get { return Mathf.Min(ItemStat.CriticalRatio, 1f); } }

    public virtual float Healing
    {
        get { return Stat.Healing; }
        set { Stat.Healing = value; }
    }

    public override float Hp
    {
        get { return base.Hp; }
        set { Stat.Hp = Math.Clamp(value, 0, MaxHp); UpdateHp(); }
    }

    public override float MaxHp
    {
        get { return base.MaxHp + ItemStat.MaxHp + ItemStat.MaxHpPerLevel * Stat.Level; }
        set { base.MaxHp = value; }
    }

    public override float HpRegen
    {
        get { return base.HpRegen * (1 + ItemStat.HpRegen); }
        set { Stat.HpRegen = Math.Max(value, 0); }
    }

    public override float MaxStamina
    {
        get { return base.MaxStamina + ItemStat.MaxStamina; }
        set { base.MaxStamina = value; }
    }

    public override float Stamina
    {
        get { return base.Stamina; }
        set { Stat.Stamina = Math.Clamp(value, 0, MaxStamina); UpdateStamina(); }
    }

    public override float StaminaRegen
    {
        get { return base.StaminaRegen * (1 + ItemStat.StaminaRegen); }
        set { Stat.StaminaRegen = Math.Max(value, 0); }
    }

    public float SkillAmplification
    {
        get
        {
            return (ItemStat.FixedSkillAmplification + ItemStat.SkillAmplificationPerLevel * Stat.Level + AdaptiveStat)
                * (1 + ItemStat.PercentageSkillAmplification);
        }
    }

    public override float Speed
    {
        get { return Stat.MoveSpeed; }
        set { Stat.MoveSpeed = value; _agent.speed = value * AGENT_SPEED_RATIO; }
    }

    public override float FixedDefensePenetration { get { return ItemStat.FixedDefensePenetration; } }
    public override float PercentageDefensePenetration { get { return ItemStat.PercentageDefensePenetration; } }

    public float AdaptiveStat
    {
        get
        {
            if (ItemStat.AdaptiveStat == 0)
                return 0;

            float att, skillamp;
            att = ItemStat.AttackDamage + ItemStat.AttackDamagePerLevel * Stat.Level;
            skillamp = (ItemStat.FixedSkillAmplification + ItemStat.SkillAmplificationPerLevel * Stat.Level)
                * (1 + ItemStat.PercentageSkillAmplification);

            if (att * 2 > skillamp)
                return ItemStat.AdaptiveStat;
            else
                return ItemStat.AdaptiveStat * 2;
        }
    }

    private bool _untargetable;
    public override bool Untargetable 
    { 
        get => _untargetable; 
        set 
        {
            if (_untargetable == value)
                return;

            _untargetable = value;

            if (_untargetable)
                _nameTag.SetUntargetable();
            else
                _nameTag.SetNameText(ObjInfo.Player.Nickname, 16);

            _nameTag.SetHPColor(_untargetable);
        } 
    }
    private bool _unstoppable;
    public override bool Unstoppable 
    {
        get { return _unstoppable; }
        set 
        {
            if (_unstoppable == value)
                return;

            _unstoppable = value;

            if (_unstoppable)
                _nameTag.SetUnstoppable();
            else
                _nameTag.SetNameText(ObjInfo.Player.Nickname, 16);
        } 
    }
    #endregion

    // 레이어
    protected string layerName;
    
    // 화살
    protected Transform _equipTransform = null;

    #region KDA

    public int KillAmount { get; private set; } = 0; 
    public int DeathAmount { get; private set; } = 0; 
    public int AsistAmount { get; private set; } = 0; 

    public virtual void SetKDA(int Kiil,int Death,int Asist)
    {
        KillAmount = Kiil;
        DeathAmount = Death;
        AsistAmount = Asist;

        // UI에 알리는 코드 필요할 듯.
    }

    #endregion

    CombatState _combatMode;
    public virtual CombatState CombatStat
    {
        get { return _combatMode; }
        set { _combatMode = value; }
    }


    public bool IsKeyInput
    {
        get { return _isKeyInput; }
        set
        {
            _isKeyInput = value;
        }
    }

    public int AttackCount
    {
        get { return _atkCount; }
        set { _atkCount = value; }
    }

    public int MaxAttackCount
    {
        get { return _maxAtkCount; }
        set { _maxAtkCount = value; }
    }

    public bool IsRest
    {
        get { return _isRest; }
        set { _isRest = value; }
    }

    protected override void Init()
    {
        base.Init();

        this.gameObject.layer = LayerMask.NameToLayer("Player");

        // Fog
        GameObject go = new GameObject();
        go.name = "FogOfWarVision";
        go.transform.parent = transform;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.AddComponent<FogOfWarVision>();
        string layerName = $"FogTeam{ObjInfo.Player.Team}";
        go.layer = LayerMask.NameToLayer(layerName);

        // 체력바
        InitNameTag();

        // 유키용
        YukiEffects.InitEffects(this);

        // Chat
        //GameObject goChat = Managers.Resource.Instantiate("UI/Chat/ChatBackground");
        //goChat.transform.SetParent(gameObject.transform);

        // 장비 슬롯
        InitEquipItem();
        InitializeXRay();

        VisualEffectController he = gameObject.GetOrAddComponent<VisualEffectController>();
        he.Owner = this;

        // NavMesh Agent
        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = Speed * AGENT_SPEED_RATIO;
        _baseMoveSpeed = Speed;

        float animRate = Speed / _baseMoveSpeed;

        ChangeSpeed("MoveSpeed", animRate);

        _agent.acceleration = 999;
        _agent.angularSpeed = 720;
        _agent.stoppingDistance = 0.1f;

        // Sound
        Sound = gameObject.GetOrAddComponent<SoundController>();
        if (Sound != null)
            Sound.PreloadCharAllSounds(ObjInfo.Player.CharType);

        // Rest Item
        RegisterRestItem();

        // Weapon Anim
        RegisterWeaponAnimator();

        // Ping
        Ping = new PingController(this);

        // Emoticon
        InstantiateUI();
    }

    private void InitEquipItem()
    {
        for (int i = 0; i < (int)EquipItemType.End; ++i)
        {
            _equipItemSlot.Add((EquipItemType)i, new EquipItemInfo());
        }

        EquipWeapon();
    }

    private void EquipWeapon()
    {
        if (ObjInfo.Player.CharType == CharacterType.Theodore)
        {
            Transform RTransform = Util.FindChildByName(transform, "Equip_R").transform;

            // 스나이퍼
            _eqipWeapon = Managers.Resource.Instantiate($"Creature/Weapon/WP_Theodore_SP01_Sniperrifle_LOD");
            if (_eqipWeapon != null)
            {
                if (RTransform != null)
                {
                    _eqipWeapon.gameObject.AddComponent<WeaponController>();

                    _eqipWeapon.transform.SetParent(RTransform);
                    _eqipWeapon.transform.localPosition = Vector3.zero;
                    _eqipWeapon.transform.localRotation = Quaternion.identity;
                    _eqipWeapon.transform.localScale = Vector3.one;
                }
            }
        }
    }

    public void ManualInit()
    {
        Init();
    }

    void Start()
    {

    }

    protected override void UpdateController()
    {
        base.UpdateController();
        MultiKillTimer();

        if (Id != Managers.Object.MyPlayer.Id)
        {
            float dist = Vector3.Distance(transform.position, _serverPos);
            if (dist > _minDist)
            {
                if (_agent == null || !_agent.isOnNavMesh)
                    return;
                _agent.Warp(_serverPos);
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, _serverPos, Time.deltaTime * _syncSpeed);
            }
        }
    }
 
    protected virtual void CheckUpdatedFlag() { }

    public override void OnDamaged()
    {
    }

    public void OnHit(S_AttackInfo atkInfoPacket)
    {
        BaseController tbc = Managers.Object.FindById(atkInfoPacket.ObjectId)?.GetComponentInChildren<BaseController>();
        if (tbc == null)
            return;
        Vector3 targetPosition = tbc.transform.position;

        // 사용 중인 키(Player)/몬스터 스킬(Monster) 이름 + hit
        // ex. Q_Hit, W_Hit, 
        if (Sound != null)
            Sound.GetRandom3DEffect($"{atkInfoPacket.AttackType}_Hit", targetPosition);

        if (Enum.TryParse<KeyCode>(atkInfoPacket.AttackType, out KeyCode key))
            PlaySelectEffect(key, default(Vector3), default(Vector3), default(Quaternion), $"FX_{atkInfoPacket.AttackType}_Hit", tbc.transform);
        else
        {
            if (ObjInfo.Player.CharType == CharacterType.Theodore) // Normal Attack
                PlaySelectEffect(KeyCode.F2, default(Vector3), default(Vector3), default(Quaternion), $"FX_{atkInfoPacket.AttackType}_Hit", tbc.transform);
        }
    }

    public void OnStop(S_Stop packet)
    {
        if (_agent == null || !_agent.isOnNavMesh)
            return;

        _agent.isStopped = true;
        _agent.ResetPath();
    }

    public void OnRespawn(S_Respawn packet)
    {
        _serverPos = transform.position = packet.PosInfo.ToVector();
        _agent.Warp(new Vector3(packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ));
        Hp = packet.Hp;
    }

    public void ChangeState(S_PlayerState packet)
    {
        State = packet.State;
    }

    public void ChangeStatus(S_ChangeStatus packet)
    {
        Speed = packet.MoveSpeed;
        float animRate = Speed / _baseMoveSpeed;
        ChangeSpeed("MoveSpeed", animRate);

        Attack = packet.Attack;
        AttackSpeed = packet.AttackSpeed;
        Defense = packet.Defense;
        Healing = packet.Healing;
    }

    public void ChangeAttackRange(S_ChangeAttackRange packet)
    {
        AttackRange = packet.AttackRange;
    }

    #region Util
    public Vector3 GetMouseWorldPosition()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) return Vector3.zero;

        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, LayerMask.GetMask("Map")))
            return hit.point;

        return Vector3.zero;
    }
    protected string GetCharacterName()
    {
        return System.Enum.GetName(typeof(CharacterType), ObjInfo.Player.CharType);
    }
    #endregion

    #region Animation
    protected virtual void PlayAnimation(string animName, float ratio)
    {
        int layerIndex = _animator.GetLayerIndex(layerName);
        if (layerIndex == -1)
            return;

        _animator.CrossFadeInFixedTime(animName, ratio);
    }

    
    public void PlayAnimFromServer(AnimInfo animInfo)
    {
        PlayAnimationSound(animInfo.Name);

        bool isUpperBodySkill = animInfo.Name == "ROZZI_D" || animInfo.Name == "YUKI_W";
        if (isUpperBodySkill)
        {
            int upperLayer = _animator.GetLayerIndex("UpperBody");
            _animator.CrossFadeInFixedTime(animInfo.Name, animInfo.Ratio, upperLayer);
            return;
        }

        AnimCondition(animInfo.Name);

        _animator.CrossFadeInFixedTime(animInfo.Name, animInfo.Ratio);

        if (animInfo.IsChangeSpeed == true)
            _animator.SetFloat("AttackSpeed", animInfo.Speed);

        WeaponAnim(animInfo.Name, animInfo.Ratio, animInfo.IsChangeSpeed, animInfo.Speed);
    }

    private void AnimCondition(string name)
    {
        if (ObjInfo.Player.CharType == CharacterType.Theodore)
        {
            // *todo. operate 조건이 자꾸 true로 만들어서 애니메이션으로 조정
            if (name == "OPERATE" && _eqipWeapon.gameObject.activeInHierarchy == true)
            {
                _eqipWeapon.gameObject.SetActive(false);
            }
            else if (_eqipWeapon.gameObject.activeInHierarchy == false)
            {
                _eqipWeapon.gameObject.SetActive(true);
                BushRenderType(0);
            }

            if (name == "REST_START" || name == "REST_LOOP")
                RenderRestItem(true);
            else
                RenderRestItem(false);
        }
        else if(ObjInfo.Player.CharType == CharacterType.Abigail)
        {
            if (name == "REST_START" || name == "REST_LOOP")
                RenderRestItem(true);
            else
                RenderRestItem(false);
        }
    }
    private int GetAnimationLayer(string animName)
    {
        int layerCount = _animator.layerCount;
        for (int i = 0; i < layerCount; i++)
        {
            AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(i);

            if (stateInfo.IsName(animName))
                return i;
        }

        return -1;
    }
    public void ChangeSpeed(string paramName, float speed)
    {
        _animator.SetFloat(paramName, speed);
    }

    public void PlayEffectFromServer(S_Fx packet, Vector3 mousePos, Vector3 targetPos = new Vector3(), Quaternion targetRot = default(Quaternion))
    {
        Transform targetTransform = null;
        if(packet.UseTargetTransform)
        {
            GameObject go = Managers.Object.FindById(packet.TargetId);
            if (go == null)
                return;
            targetTransform = go.transform;
        }

        if(!packet.IsCommon)
        {
            if (packet.Type == "Caster")
                PlaySkillEffect((KeyCode)packet.SkillKey, mousePos, targetPos, targetRot, targetTransform: targetTransform);
            else if (packet.Type == "Select")
                PlaySelectEffect((KeyCode)packet.SkillKey, mousePos, targetPos, targetRot, packet.FxName, targetTransform: targetTransform);
        }
        else
        {
            if (packet.Type == "Caster")
                PlayCommonCasterEffect(packet.CommonName, mousePos, targetPos, targetRot);
            else if(packet.Type == "Select")
                PlayCommonSelectEffect(packet.CommonName, packet.FxName, mousePos, targetPos, targetRot);
        }
    }
    #endregion

    #region Sound
    private void PlayAnimationSound(string name)
    {
        // Animation에 맞는 Sound
        if (Sound == null)
            return;

        if (name == "RUN")
        {
            if (_runSoundCoroutine == null)
                _runSoundCoroutine = StartCoroutine(FootStepSound());
        }
        else
        {
            if (_runSoundCoroutine != null)
            {
                StopCoroutine(_runSoundCoroutine);
                _runSoundCoroutine = null;
            }

            Sound.GetEffect3D(name, transform.position); 
            Sound.GetRandom3DVoice(name, transform.position);
        }
    }

    Coroutine _runSoundCoroutine = null;
    private float _footstepTimer = 0.4f;
    private float _footstepInterval = 0.4f;
    protected IEnumerator FootStepSound()
    {
        _footstepTimer = 0.4f;
        while (true)
        {
            _footstepTimer -= Time.deltaTime;
            if (_footstepTimer <= 0)
            {
                Sound.GetRandom3DEffect("FootStep", transform.position);
                _footstepTimer = _footstepInterval;
            }
            yield return null;
        }
    }

    #endregion

    public void LookAtMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 100f))
        {
            Vector3 targetPoint = hit.point;
            targetPoint.y = transform.position.y;
            Vector3 direction = targetPoint - transform.position;

            if (direction != Vector3.zero)
            {
                Quaternion newRotation = Quaternion.LookRotation(direction);
                RotInfo = newRotation;
                SyncPos(true);
            }
        }
    }

    public Vector2 GetMousePos()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 100f))
        {
            Vector3 targetPoint = hit.point;
            return new Vector2(targetPoint.x, targetPoint.z);
        }
        return Vector2.zero;
    }

    public void LookAtMouse(Vector2 mousePos)
    {
        Vector3 casterPosition = transform.position;

        Vector3 targetPoint = new Vector3(mousePos.x, casterPosition.y, mousePos.y);

        Vector3 direction = targetPoint - casterPosition;

        if (direction != Vector3.zero)
        {
            Quaternion newRotation = Quaternion.LookRotation(direction);

            RotInfo = newRotation;
            SyncPos(true);
        }
    }

    #region NameTagAndHp
    protected void InitNameTag()
    {
        GameObject go = null;

        if(ObjInfo.Player.CharType == CharacterType.Yuki)
        {
            go = Managers.Resource.Instantiate("UI/SubItem/YukiNameTagCanvas", gameObject.transform);
        }
        else
        {
            go = Managers.Resource.Instantiate("UI/SubItem/PlayerNameTagCanvas", gameObject.transform);
        }

        if (null == go)
        {
            //Debug.Log("go is null : InitNameTag()");
            return;
        }

        _nameTag = go.GetComponentInChildren<UI_PlayerNameTag>();
        if (null == _nameTag)
        {
            //Debug.Log("_nameTag is null : InitNameTag()");
            return;
        }

        go.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

        _nameTag.SetTarget(gameObject);
        _nameTag.SetHPColor();
        _nameTag.SetNameText(ObjInfo.Player.Nickname, 16);

        //이거 왜 터지지?
        _nameTag.SetLevelText(Stat.Level);
        UpdateHp();
        UpdateMaxHp();
        UpdateStamina();
        UpdateMaxStamina();
    }

    protected override void UpdateHp()
    {
        if (_nameTag == null)
            return;
        _nameTag.SetHp(Hp);
    }
    protected override void UpdateMaxHp()
    {
        if (_nameTag == null)
            return;
        _nameTag.SetMaxHp(MaxHp);
    }

    protected override void UpdateBarrier()
    {
        if (_nameTag == null)
            return;
        _nameTag.SetBarrier(Barrier);
    }
    protected override void UpdateStamina()
    {
        if (_nameTag == null)
            return;
        _nameTag.SetStamina(Stamina);
    }
    protected override void UpdateMaxStamina()
    {
        if (_nameTag == null)
            return;
        _nameTag.SetMaxStamina(MaxStamina);
    }

    public void SetNameTagLevel()
    {
        if (_nameTag == null)
            return;
        _nameTag.SetLevelText(Stat.Level);
    }

    #endregion

    #region Item
    public virtual void UpdateItemStat(ItemStat stat)
    {
        ItemStat = stat;
        UpdateHp();
        UpdateMaxHp();
        UpdateStamina();
        UpdateMaxStamina();
    }

    public virtual void EquipItem(int itemId)
    {
        //TODO 아이템 도감에서 아이템을 가져와서 처리(+UI도)
        EquipItemInfo item = DataManager.ItemDict[itemId] as EquipItemInfo;
        _equipItemSlot[item.Type] = item;
    }

    #endregion

    #region Effect
    // 기본 스킬 이펙트 호출 : Caster Type - 무조건 플레이어 따라
    public void PlaySkillEffect(KeyCode skillKey, Vector3 mousePos, Vector3 targetPos, Quaternion targetRot = default(Quaternion), Transform targetTransform = null)
    {
        CharacterType type = ObjInfo.Player.CharType;
        CreatureState state = CreatureState.Skill;

        if (!DataManager.PlayerFxDict.ContainsKey(type))
            return;
        if (!DataManager.PlayerFxDict[type].ContainsKey(state))
            return;
        if (!DataManager.PlayerFxDict[type][state].ContainsKey(skillKey))
            return;

        SkillEffectList myEffectList = DataManager.PlayerFxDict[type][state][skillKey];
        List<EffectData> dataList = new List<EffectData>();
        foreach (EffectData effect in myEffectList.Caster)
        {
            dataList.Add(effect);
        }

        Managers.FX.PlayEffect(ObjInfo.ObjectId, dataList, targetTransform ? targetTransform : transform, mousePos);
    }

    // 직접 선택해서 호출하는 이펙트 : Type Select
    public void PlaySelectEffect(KeyCode skillKey, Vector3 mousePos, Vector3 targetPos, Quaternion targetRot, string fxName, Transform targetTransform = null)
    {
        CharacterType type = ObjInfo.Player.CharType;
        CreatureState state = CreatureState.Skill;

        if (!DataManager.PlayerFxDict.ContainsKey(type))
            return;
        if (!DataManager.PlayerFxDict[type].ContainsKey(state))
            return;
        if (!DataManager.PlayerFxDict[type][state].ContainsKey(skillKey))
            return;

        SkillEffectList myEffectList = DataManager.PlayerFxDict[type][state][skillKey];
        if (myEffectList?.Select == null)
            return;

        List<EffectData> dataList = myEffectList.Select
       .Where(effect => effect != null && effect.prefabName == fxName)
       .ToList();

        if (dataList.Count == 0)
            return;

        Managers.FX.PlayEffect(ObjInfo.ObjectId, dataList, targetTransform ? targetTransform : transform, mousePos, targetPos, targetRot);
    }

    // 공통 이펙트 : Type Common - Caster
    public void PlayCommonCasterEffect(string commonName, Vector3 mousePos, Vector3 targetPos, Quaternion targetRot, Transform targetTransform = null)
    {
        if (DataManager.CommonFxDict == null)
            return;

        if (!DataManager.CommonFxDict.TryGetValue(commonName, out SkillEffectList effectList))
            return;

        var dataList = new List<EffectData>();
        if (effectList.Caster != null)
            dataList.AddRange(effectList.Caster);

        Managers.FX.PlayEffect(ObjInfo.ObjectId, dataList, targetTransform ? targetTransform : transform, mousePos, targetPos, targetRot, isCommon: true);
    }

    // 공통 이펙트 : Type Common - Select
    public void PlayCommonSelectEffect(string commonName, string fxName, Vector3 mousePos, Vector3 targetPos, Quaternion targetRot, Transform targetTransform = null)
    {
        if (DataManager.CommonFxDict == null)
            return;

        if (!DataManager.CommonFxDict.TryGetValue(commonName, out SkillEffectList effectList))
            return;

        var dataList = new List<EffectData>();

        if (effectList.Select != null)
        {
            foreach (EffectData effect in effectList.Select)
            {
                if (effect.prefabName == fxName)
                    dataList.Add(effect);
            }
        }

        if (dataList.Count == 0)
            return;

        Managers.FX.PlayEffect(ObjInfo.ObjectId, dataList, targetTransform ? targetTransform : transform, mousePos, targetPos, targetRot, isCommon: true);
    }
    #endregion

    #region State:Operate
    public IEnumerator CoRotateToPosition(Vector3 targetPos)
    {
        float rotateSpeed = 15f;
       
        while (true)
        {
            if (State == CreatureState.Moving)
                break;

            Vector3 dir = targetPos - transform.position;
            dir.y = 0;

            if (dir.magnitude < 0.1f)
                break;

            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotateSpeed);

            if (Quaternion.Angle(transform.rotation, targetRot) < 1f)
                break;

            yield return null;
        }
    }
    #endregion

    [Header("X-Ray Settings")]
    [SerializeField] private int xRayGroupStencilID = 100;
    [SerializeField] private Color allyXRayColor = new Color(0.3f, 0.6f, 1f, 1f);
    [SerializeField] private Color enemyXRayColor = new Color(1f, 0.2f, 0.2f, 1f);

    void InitializeXRay()
    {
        SetupPlayerWeaponXRay();
    }

    void SetupPlayerWeaponXRay()
    {
        bool isEnemy = IsEnemyTeam();
        Color xrayColor = isEnemy ? enemyXRayColor : allyXRayColor;
        SetXRayGroup(gameObject, xRayGroupStencilID, xrayColor);

        if (_eqipWeapon != null)
            SetXRayGroup(_eqipWeapon, xRayGroupStencilID, xrayColor);
    }

    public void SetxRayFromPlayer(GameObject player)
    {
        // 중요: player 객체의 팀을 체크해야 함!
        PlayerController playerScript = player.GetComponent<PlayerController>();
        if (playerScript == null)
            return;

        bool isEnemy = IsEnemyTeam(playerScript);
        Color xrayColor = isEnemy ? enemyXRayColor : allyXRayColor;

        SetXRayGroup(player, xRayGroupStencilID, xrayColor);
    }

    void SetXRayGroup(GameObject root, int stencilID, Color xrayColor)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;

            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];

                if (mat.shader.name.Contains("Toon_CharacterNy"))
                {
                    if (mat.HasProperty("_StencilRef"))
                    {
                        mat.SetInt("_StencilRef", stencilID);
                        mat.SetInt("_StencilComp", (int)UnityEngine.Rendering.CompareFunction.Always);
                        mat.SetInt("_StencilOp", (int)UnityEngine.Rendering.StencilOp.Replace);
                        mat.SetInt("_ZTestMode", (int)UnityEngine.Rendering.CompareFunction.LessEqual);

                        if (mat.HasProperty("_OccludedColor"))
                        {
                            mat.SetColor("_OccludedColor", xrayColor);
                            Debug.Log($"Set X-Ray: {renderer.gameObject.name} = {xrayColor}");
                        }
                    }
                }
            }

            renderer.materials = materials;
        }
    }

    bool IsEnemyTeam()
    {
        if (Managers.Object.MyPlayer == null || ObjInfo?.Player == null)
            return false;

        return Managers.Object.MyPlayer.ObjInfo.Player.Team != ObjInfo.Player.Team;
    }

    bool IsEnemyTeam(PlayerController targetPlayer)
    {
        if (Managers.Object.MyPlayer == null ||
            targetPlayer == null ||
            targetPlayer.ObjInfo?.Player == null)
            return false;

        return Managers.Object.MyPlayer.ObjInfo.Player.Team != targetPlayer.ObjInfo.Player.Team;
    }

    void OnWeaponEquipped(GameObject newWeapon)
    {
        if (newWeapon != null)
        {
            bool isEnemy = IsEnemyTeam();
            Color xrayColor = isEnemy ? enemyXRayColor : allyXRayColor;
            SetXRayGroup(newWeapon, xRayGroupStencilID, xrayColor);
        }
    }
    public void SyncPosFromServer(S_Move movePacket)
    {
        if (_agent == null || !_agent.isOnNavMesh)
            return;

        _agent.isStopped = false;

        _serverPos = new Vector3
        {
            x = movePacket.PosInfo.PosX,
            y = movePacket.PosInfo.PosY,
            z = movePacket.PosInfo.PosZ
        };

        transform.rotation = movePacket.RotInfo;
    }

    public void SyncPosFromServer(PositionInfo positionInfo, RotationInfo rotationInfo)
    {
        if (_agent == null || !_agent.isOnNavMesh)
            return;

        _agent.isStopped = false;

        _serverPos = new Vector3
        {
            x = positionInfo.PosX,
            y = positionInfo.PosY,
            z = positionInfo.PosZ
        };

        transform.rotation = rotationInfo;
    }
    private void MultiKillTimer()
    {
        if (CurrentMultiKillCnt <= 0)
            return;

        _lastKillTime += Time.deltaTime;
        if (_multiKillTimeLimit <= _lastKillTime)
        {
            CurrentMultiKillCnt = 0;
            return;
        }
        return;
    }

    #region Bush Renderer

    public void BushRenderType(int state)
    {
        VisualEffectController he = GetComponentInChildren<VisualEffectController>();
        if (he == null)  return;

        switch (state) 
        {
         case 0:    // visible
             {
                 Hiding(false);
                 he.MakeVisible();
             }
             break;
         case 1:    // invisible
             {
                 Hiding(true);
                    he.MakeInvisible();
             }
             break;
         case 2:    // change
             {
                 Hiding(false);
                    he.ChangeBushRenderer();
             }
             break;
        }
    }
    #endregion

    public void Hiding(bool hide)
    {
        if (hide)
        {
            IsHide = true;
            _nameTag.SetVisible(false);
            //_nameTag.gameObject.SetActive(false);
        }
        else
        {
            IsHide = false;
            _nameTag.SetVisible(true);
            //_nameTag.gameObject.SetActive(true);
        }
    }

    #region State: Rest
    void RegisterRestItem()
    {
        if(ObjInfo.Player.CharType == CharacterType.Abigail)
        {
            foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "RestTable" || child.name == "AbigailCard")
                {
                    _restItems.Add(child.gameObject);
                }
            }
            RenderRestItem(false);
        }
        else if (ObjInfo.Player.CharType == CharacterType.Theodore)
        {
            foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "RestBox")
                {
                    _restItems.Add(child.gameObject);
                    RenderRestItem(false);
                    return;
                }
            }
        }
    }

    public void RenderRestItem(bool render)
    {
        if (_restItems == null || _restItems.Count() == 0)
            return;
        foreach (GameObject restItem in _restItems)
        {
            if (restItem == null) continue;
            restItem.SetActive(render);
        }        
    }
    #endregion

    #region WeaponAnim
    void RegisterWeaponAnimator()
    {
        if (ObjInfo.Player.CharType == CharacterType.Abigail)
        {
            Transform weaponTransform = GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == "AbigailWeapon");
            if (weaponTransform != null)
            {
                // AbigailWeapon의 자식에서 Animator 찾기
                _weaponAnimator = weaponTransform.GetComponentInChildren<Animator>();
            }
        }
    }

    void WeaponAnim(string animName, float transDuration, bool speedChanged, float speed)
    {
        if (_weaponAnimator == null)
            return;

        if(speedChanged)
            _weaponAnimator.SetFloat("AttackSpeed", speed);

        if(animName == "SKILL_T" || animName == "SKILL_Q" || animName == "SKILL_W")
            _weaponAnimator.CrossFadeInFixedTime(animName, transDuration);
        else
            _weaponAnimator.CrossFadeInFixedTime("WAIT", transDuration);
    }
    #endregion

    #region Emoticon
    private void InstantiateUI()
    {
        Managers.WorldUI.RegisterEmoticonUI(Id, transform);

        Emoticon = new EmoticonController(this);
        // Emoticon
        //GameObject em = Managers.Resource.Instantiate("UI/Common/EmoticonUI");
        //em?.transform.SetParent(gameObject.transform);
        //EmoticonUI = em.GetComponentInChildren<UI_Emoticon>(true);
        //EmoticonUI?.SetTarget(gameObject);
    }
    #endregion
}
