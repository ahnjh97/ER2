using Google.Protobuf.Protocol;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UI_SelectedCharacterImage;

public class PickScene : BaseScene
{
    UI_PickSceneUI _pickSceneUI = null;

    public int PickIdx {  get; set; }
    public int Team {  get; set; }

    CharacterType _characterType = CharacterType.CharacterNone;
    string _nickname = "";
    public string NickName 
    { 
        get { return _nickname; } 
        set { _nickname = value; ChangeNickname(_nickname, PickIdx); } 
    }
    // 다음 씬에 넘겨야 되는 정보 및 현재 가지고 있어야 될 정보
    // 이 씬에서 몇번째 인덱스인지 ( 0 ~ 7)
    // 캐릭터
    // 닉네임
    // TODO 무기 특성 팀 정보

    Dictionary<CharacterType, AudioClip> _selectedAudios = new Dictionary<CharacterType, AudioClip>(); // 픽 선택완료 했을때 사운드

    protected override async void Init()
    {
        base.Init();

        SceneType = Define.Scene.Pick;

        GameObject go = await Managers.Resource.InstantiateAsync("UI/Scene/PickSceneUI");

        _pickSceneUI = go.GetComponent<UI_PickSceneUI>();
        _pickSceneUI.OnClickedPickButton += ClickedCharPickButton;

        PickIdx = Managers.Info.PickIdx;
        Team = Managers.Info.Team;

        LoadAudioClips();

        foreach (PickScenePlayerInfo pspi in Managers.Info._pspiList)
            Spawn(pspi);

        GameObject loadingUI = GameObject.Find("LoadingUI");
        loadingUI.SetActive(false);
        //Managers.Sound.Play("sound/bgm/BGM_Lobby", Define.Sound.Bgm);
    }

    private void Start()
    {
        
    }

    void Update()
    {

    }

    public override void Clear()
    {
        //Debug.Log("PickScene Clear");
    }

    private void ClickedCharPickButton(string charName)
    {
        if (Enum.TryParse(charName, out _characterType))
        {
            C_Character charPacket = new C_Character();
            charPacket.CharType = _characterType;
            charPacket.PickIdx = PickIdx;
            Managers.Network.Send(charPacket);

            C_Weapon weaponPacket = new C_Weapon();
            weaponPacket.WeaponType = CharTypeToWeaponType(_characterType);
            weaponPacket.PickIdx = PickIdx;
            Managers.Network.Send(weaponPacket);

            ChangePickImage(_characterType, PickIdx);
            ChangeWeaponImage(CharTypeToWeaponType(_characterType), PickIdx);
        }
    }

    public void ChangePickImage(CharacterType charType, int idx)
    {
        if (_pickSceneUI == null)
            return;

        _pickSceneUI.ChangePickImage(charType, idx);

        if(PickIdx == idx)
            _pickSceneUI.ChangeFullSizeImage(charType);
    }

    private void ChangeNickname(string nickname, int idx)
    {
        if (_pickSceneUI == null)
            return;

        _pickSceneUI.ChangeNickname(nickname, idx);
    }

    public void ChangeBar(int idx, int team)
    {
        if (_pickSceneUI == null)
            return;

        BarType barType;

        if (PickIdx == idx)
            barType = BarType.My;
        else if (team != Team)
            barType = BarType.Enemy;
        else
            barType = BarType.Team;

        _pickSceneUI.ChangeBar(barType, idx);
    }

    public void ChangeTraitImage(TraitType traitType, int idx)
    {
        if (_pickSceneUI == null)
            return;

        _pickSceneUI.ChangeTraitImage(traitType, idx);
    }

    public void ChangeWeaponImage(Weapon weaponType, int idx)
    {
        if (_pickSceneUI == null)
            return;

        _pickSceneUI.ChangeWeaponImage(weaponType, idx);
    }

    //TEMP
    private Weapon CharTypeToWeaponType(CharacterType charType)
    {
        switch (charType)
        {
            case CharacterType.Rozzi:
                return Weapon.Pistol; 
            case CharacterType.Yuki:
                return Weapon.TwoHandSword;
            case CharacterType.Abigail:
                return Weapon.Axe; 
            case CharacterType.Hyunwoo:
                return Weapon.Glove; 
            case CharacterType.Theodore:
                return Weapon.SniperRifle; 
        }

        return Weapon.None;
    }

    public void Spawn(PickScenePlayerInfo info)
    {
        ChangeNickname(info.UserName, info.PickIdx);
        ChangeBar(info.PickIdx, info.Team);
    }

    public void LoadAudioClips()
    {
        AudioClip abigail = Managers.Resource.Load<AudioClip>("Abigail/voice/Abigail_selected_1_ko");
        _selectedAudios[CharacterType.Abigail] = abigail;

        AudioClip rozzi = Resources.Load<AudioClip>("Rozzi/Sound/Voice/Rozzi_selected_1_ko");
        _selectedAudios[CharacterType.Rozzi] = rozzi;

        AudioClip hyunwoo = Managers.Resource.Load<AudioClip>("Hyunwoo/Resources/sound/voice/hyunwoo/s000/ko/Hyunwoo_selected_1_ko");
        _selectedAudios[CharacterType.Hyunwoo] = hyunwoo;

        AudioClip yuki = Managers.Resource.Load<AudioClip>("Yuki/sound/yuki/s000/ko/Yuki_selected_1_ko");
        _selectedAudios[CharacterType.Yuki] = yuki;

        AudioClip theodore = Resources.Load<AudioClip>("sound/voice/theodore/s000/ko/Theodore_selected_1_ko");
        _selectedAudios[CharacterType.Theodore] = theodore;
    }

    public void PlaySelectedSound(CharacterType charType)
    {
        if (!_selectedAudios.TryGetValue(charType, out AudioClip audioClip))
            return;

        Managers.Sound.Play(audioClip, Define.Sound.Voice, 0.2f);
    }
}
