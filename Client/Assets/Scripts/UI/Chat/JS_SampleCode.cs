using Google.Protobuf.Protocol;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 채팅 시스템 전반에서 공통으로 사용하는 메시지 데이터 구조체
/// </summary>
public struct ChatMessage
{
    public int PlayerId;
    public int TeamId;
    public string PlayerName;
    public string Message;
    public ChatType ChatType;
    public CharacterType CharacterType;
}


/// <summary>
/// 채팅 UI 입력 및 출력 담당 클래스
/// - 입력 처리 (Enter / Shift+Enter)
/// - 메시지 표시
/// - 네트워크 및 스레드 로직과 분리된 이벤트 기반 구조
/// </summary>
public class ChatHandler : MonoBehaviour
{
    public static ChatHandler Instance;

    public bool IsChatting { get; private set; } = false;
    public int MyId => Managers.Object.MyPlayer.Id;

    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private RectTransform contentRect;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private TMP_Text placeholderText;

    private CanvasGroup cg;
    private ChatType chatType;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        cg = inputField.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = inputField.gameObject.AddComponent<CanvasGroup>();

        HideInputField(); // 시작 시 숨김
    }

    // 채팅 메시지 이벤트 구독
    private void OnEnable()
    {
        ChatEvents.OnChatReceived += OnChatReceived;
    }

    private void OnDisable()
    {
        ChatEvents.OnChatReceived -= OnChatReceived;
    }

    void Update()
    {
        HandleInput();
    }

    // 채팅 입력 관리
    private void HandleInput()
    {
        // Shift + Enter : 전체 채팅
        if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            && Input.GetKey(KeyCode.LeftShift))
        {
            ShowInputField(isTeam: false);
            return;
        }

        // Enter : 팀 채팅 or 전송
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (!cg.interactable)
                ShowInputField(isTeam: true);
            else
            {
                SendChat();
                HideInputField();
            }
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
            HideInputField();
    }

    // 인풋필드 활성화
    private void ShowInputField(bool isTeam)
    {
        IsChatting = true;

        cg.alpha = 0.8f;
        cg.blocksRaycasts = true;
        cg.interactable = true;

        chatType = isTeam ? ChatType.Team : ChatType.All;
        inputField.text = isTeam ? "" : "/All ";

        inputField.ActivateInputField();
        inputField.Select();

        StartCoroutine(SetCaretToEnd());
    }

    private IEnumerator SetCaretToEnd()
    {
        yield return null;
        inputField.caretPosition = inputField.text.Length;
        inputField.stringPosition = inputField.text.Length;
    }

    private void HideInputField()
    {
        IsChatting = false;

        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
    }

    // 채팅 메시지를 패킷으로 서버 전송
    private void SendChat()
    {
        string msg = inputField.text.Trim();
        if (msg.Length <= 0)
            return;

        if (chatType == ChatType.All && msg.StartsWith("/All "))
            msg = msg.Substring(5).Trim();

        C_Chat chatPkt = new C_Chat
        {
            ChatType = chatType,
            Message = msg
        };

        Managers.Network.Send(chatPkt);
        inputField.text = "";
    }

    // 채팅 메시지 수신
    private void OnChatReceived(ChatMessage msg)
    {
        AddMessage(
            msg.PlayerId,
            msg.TeamId,
            msg.PlayerName,
            msg.Message,
            msg.ChatType,
            msg.CharacterType
        );
    }

    // 채팅 메시지 UI 출력 담당
    private void AddMessage(int playerId, int teamId, string playerName, string message, ChatType type, CharacterType charType)
    {
        GameObject prefab = Resources.Load<GameObject>("Prefabs/UI/Chat/ChatText");
        GameObject inst = Instantiate(prefab, contentRect, false);

        string prefix = type == ChatType.Team
            ? "<color=#52D1FF>[팀]</color>"
            : "<color=#FFD400>[전체]</color>";

        int myTeam = Managers.Object.MyPlayer.ObjInfo.Player.Team;
        bool isTeam = teamId == myTeam;

        string nameColor = type == ChatType.Team
            ? "#01DCE3"
            : isTeam ? "#01DCE3" : "#FF0000";

        inst.GetComponent<TMP_Text>().text =
            $"{prefix} <color={nameColor}>{playerName}({CharacterName(charType)})</color> : {message}";

        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        scrollRect.verticalNormalizedPosition = 0f;
    }

    private string CharacterName(CharacterType charType)
    {
        return charType switch
        {
            CharacterType.Abigail => "아비게일",
            CharacterType.Rozzi => "로지",
            CharacterType.Yuki => "유키",
            CharacterType.Hyunwoo => "현우",
            CharacterType.Theodore => "테오도르",
            _ => "Unknown"
        };
    }
}


/// <summary>
/// 메인 스레드 디스패처
/// - 네트워크 수신 등 다른 스레드에서 발생한 작업을
///   Unity 메인 스레드(Update)에서 안전하게 실행하기 위한 브릿지
/// </summary>
public class MainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> actions = new();
    private const int maxPerFrame = 20;

    public static void Enqueue(Action action)
    {
        if (action == null)
            return;

        lock (actions)
            actions.Enqueue(action);
    }

    void Update()
    {
        // 프레임 당 처리량 제한
        for (int i = 0; i < maxPerFrame; i++)
        {
            Action action;

            lock (actions)
            {
                if (actions.Count == 0) return;
                action = actions.Dequeue();
            }

            try { action(); }
            catch (Exception e) { Debug.LogError(e); }
        }
    }
}


/// <summary>
/// 채팅 메시지 이벤트 브릿지
/// - Dispatcher를 통해 메인 스레드에서 호출됨
/// - UI는 이벤트 구독을 통해 메시지 수신
/// </summary>
public static class ChatEvents
{
    public static event Action<ChatMessage> OnChatReceived;

    public static void Raise(ChatMessage msg)
    {
        OnChatReceived?.Invoke(msg);
    }
}


// ---------------------------- 서버 -------------------------------
/// <summary>
/// 클라이언트로부터 채팅 패킷을 수신했을 때 호출되는 핸들러
/// 다시 클라이언트로 재전송
/// - 네트워크 스레드에서 호출됨
/// - 여기서는 최소한의 검증과 패킷 구성만 수행
/// </summary>
public void HandlerChat(Player player, C_Chat chatPkt)
{
    // 빈 문자열 방지
    if (string.IsNullOrWhiteSpace(chatPkt.Message))
        return;

    S_Chat sendPkt = new S_Chat()
    {
        ObjectId = player.Id,
        TeamId = player.Team,
        PlayerName = player.Info.Player.Nickname,
        Message = chatPkt.Message,
        ChatType = chatPkt.ChatType,
        CharType = player.CharType
    };

    // 각 클라이언트로 채팅 메시지 패킷 전송
    Push(ProcessChat, player, sendPkt);
}


/// <summary>
/// - 채팅 타입에 따라 전송 범위를 결정
/// </summary>
private void ProcessChat(Player sender, S_Chat chat)
{
    // 팀 채팅 : 같은 팀에게만 전송
    if (chat.ChatType == ChatType.Team)
        BroadcastTeam(sender.Team, chat);
    // 전체 채팅 : 모든 플레이어에게 전송
    else
        BroadcastAll(chat);
}


/// <summary>
/// 특정 팀에게만 채팅을 전송
/// - 서버에서 팀 정보를 기준으로 필터링
/// </summary>
private void BroadcastTeam(int teamId, S_Chat chat)
{
    foreach (var p in _players.Values)
    {
        if (p.Team == teamId)
            p.Session.Send(chat);
    }
}


/// <summary>
/// 모든 플레이어에게 채팅을 전송
/// </summary>
private void BroadcastAll(S_Chat chat)
{
    foreach (var p in _players.Values)
        p.Session.Send(chat);
}