using Google.Protobuf.Protocol;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    // 메시지 큐
    private Queue<(int playerId, int teamId, string playerName, string message, ChatType chatType, CharacterType charType)> messageQueue = new Queue<(int, int, string, string, ChatType, CharacterType)>();

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

    public void EnqueueMessage(int playerId, int teamId, string playerName, string message, ChatType chatType, CharacterType charType)
    {
        messageQueue.Enqueue((playerId, teamId, playerName, message, chatType, charType));
    }

    void Update()
    {
        while (messageQueue.Count > 0)
        {
            var msg = messageQueue.Dequeue();
            AddMessage(msg.playerId, msg.teamId, msg.playerName, msg.message, msg.chatType, msg.charType);
        }

        // Shift + enter (all chat)
        if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            && Input.GetKey(KeyCode.LeftShift))
        {
            ShowInputField(isTeam: false);
            return;
        }

        // enter (team chat)
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

    private void ShowInputField(bool isTeam)
    {
        IsChatting = true;

        cg.alpha = 0.8f;
        cg.blocksRaycasts = true;
        cg.interactable = true;

        inputField.text = "";

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

    private void SendChat()
    {
        string msg = inputField.text.Trim();

        if (msg.Length <= 0)
            return;

        if (chatType == ChatType.All)
        {
            if (msg.StartsWith("/All "))
                msg = msg.Substring(5).Trim();
        }

        C_Chat chatPkt = new C_Chat();

        chatPkt.ChatType = chatType;
        chatPkt.Message = msg;

        Managers.Network.Send(chatPkt);

        inputField.text = "";
    }

    private void AddMessage(int playerId, int teamId, string playerName, string message, ChatType type, CharacterType charType)
    {
        GameObject textPrefab = Resources.Load<GameObject>("Prefabs/UI/Chat/ChatText");
        GameObject inst = Instantiate(textPrefab, contentRect, false);

        string prefix = type == ChatType.Team ? "<color=#52D1FF>[팀]</color>" : "<color=#FFD400>[전체]</color>";

        int myId = Managers.Object.MyPlayer.Id;
        int myTeam = Managers.Object.MyPlayer.ObjInfo.Player.Team;

        bool isMine = playerId == myId;
        bool isTeam = teamId == myTeam;

        string nameColor;

        if (type == ChatType.Team)
            nameColor = "#01DCE3";
        // 전체 채팅
        else
        {
            if (isTeam)
                nameColor = "#01DCE3"; // 아군 = 파랑
            else
                nameColor = "#FF0000"; // 적군 = 빨강
        }

        inst.GetComponent<TMP_Text>().text =
                $"{prefix} <color={nameColor}>{playerName}({CharacterName(charType)})</color> : {message}";
    }

    private string CharacterName(CharacterType charType)
    {
        string charName = "";

        switch (charType)
        {
            case CharacterType.Abigail:
                charName = "아비게일";
                break;
            case CharacterType.Rozzi:
                charName = "로지";
                break;
            case CharacterType.Yuki:
                charName = "유키";
                break;
            case CharacterType.Hyunwoo:
                charName = "현우";
                break;
            case CharacterType.Theodore:
                charName = "테오도르";
                break;
        }

        return charName;
    }
}
