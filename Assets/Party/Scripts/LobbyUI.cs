using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The lobby screen. Three pages: Home (Host / Join / Play vs Bot), Join (keypad), Room (players, Start, results).
/// It shows whatever PartyConnection and PartySession say, so it is always right after a scene reload.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [Header("Pages")]
    public GameObject homePage;
    public GameObject joinPage;
    public GameObject roomPage;
    public GameObject busyOverlay;
    public TMP_Text busyText;

    [Header("Home")]
    public Button hostButton;
    public Button joinButton;
    public Button botButton;
    public TMP_Text homeStatus;

    [Header("Join")]
    public TMP_Text[] codeDigits = new TMP_Text[PartyConnection.CodeLength];
    public Image[] codeBoxes = new Image[PartyConnection.CodeLength];
    public Button[] digitButtons = new Button[10];
    public Button backspaceButton;
    public Button joinConfirmButton;
    public Button joinBackButton;
    public TMP_Text joinStatus;

    [Header("Room")]
    public TMP_Text roomCodeLabel;
    public TMP_Text roomCode;
    public TMP_Text blueName, blueSub, redName, redSub;
    public Image redCard;
    public TMP_Text gamesLine;
    public Button startButton;
    public TMP_Text startLabel;
    public TMP_Text waitingText;
    public Button leaveButton;

    [Header("Results (shown in the room after a party)")]
    public GameObject resultsCard;
    public TMP_Text resultsTitle;
    public TMP_Text resultsScore;
    public TMP_Text resultsWins;

    bool wantJoin;
    bool shownFinal;
    float redCardAlpha = -1f;

    void Awake()
    {
        if (hostButton) hostButton.onClick.AddListener(() => { if (Conn) Conn.StartHost(); });
        if (botButton) botButton.onClick.AddListener(() => { if (Conn) Conn.StartVsBot(); });
        if (joinButton) joinButton.onClick.AddListener(() => { wantJoin = true; if (Conn) Conn.ClearCode(); });
        if (joinBackButton) joinBackButton.onClick.AddListener(() => wantJoin = false);
        if (joinConfirmButton) joinConfirmButton.onClick.AddListener(() => { if (Conn) Conn.StartClient(); });
        if (backspaceButton) backspaceButton.onClick.AddListener(() => { if (Conn) Conn.Backspace(); });
        for (int i = 0; i < digitButtons.Length; i++)
        {
            if (digitButtons[i] == null) continue;
            int d = i;
            digitButtons[i].onClick.AddListener(() => { if (Conn) Conn.AddDigit(d); });
        }
        if (startButton) startButton.onClick.AddListener(() => { if (Session) Session.StartParty(); });
        if (leaveButton) leaveButton.onClick.AddListener(() => { wantJoin = false; if (Conn) Conn.Leave(); });
    }

    static PartyConnection Conn => PartyConnection.Instance;
    static PartySession Session => PartySession.Instance;

    void Update()
    {
        PartyConnection c = Conn;
        PartySession s = Session;
        bool online = PartyConnection.InSession;
        if (online) wantJoin = false;

        Show(homePage, !online && !wantJoin);
        Show(joinPage, !online && wantJoin);
        Show(roomPage, online);

        bool busy = c != null && c.Busy;
        Show(busyOverlay, busy);
        string status = c != null ? c.Status : "Starting...";
        Set(busyText, status);

        if (!online)
        {
            if (wantJoin) UpdateJoin(c, status);
            else Set(homeStatus, status);
            shownFinal = false;
            return;
        }
        if (s != null) UpdateRoom(c, s);
    }

    void UpdateJoin(PartyConnection c, string status)
    {
        string code = c != null ? c.JoinCode : "";
        for (int i = 0; i < codeDigits.Length; i++)
        {
            Set(codeDigits[i], i < code.Length ? code[i].ToString() : "");
            if (i < codeBoxes.Length && codeBoxes[i] != null)
                codeBoxes[i].color = i == code.Length ? PartyTeam.Gold : i < code.Length ? Color.white : new Color(1f, 1f, 1f, 0.28f);
        }
        if (joinConfirmButton) joinConfirmButton.interactable = code.Length == PartyConnection.CodeLength && (c == null || !c.Busy);
        Set(joinStatus, status.Length > 0 ? status : "Type the 6-digit code from your friend's lobby");
    }

    void UpdateRoom(PartyConnection c, PartySession s)
    {
        bool offline = c != null && c.Offline;
        Set(roomCodeLabel, offline ? "SOLO" : "ROOM CODE");
        Set(roomCode, offline ? "VS BOT" : c != null && c.RoomCode.Length > 0 ? c.RoomCode.Insert(3, " ") : "------");

        int me = s.LocalSide;
        Set(blueName, "BLUE");
        Set(blueSub, me == 0 ? "You" : "Host");

        bool human = s.HasOpponent;
        Set(redName, human ? "RED" : "BOT");
        Set(redSub, human ? (me == 1 ? "You" : "Friend") : offline ? "Ready to play" : "Waiting for a friend...");
        float a = human || offline ? 1f : 0.45f;
        if (redCard != null && !Mathf.Approximately(a, redCardAlpha))
        {
            redCardAlpha = a;
            Color col = redCard.color;
            col.a = a;
            redCard.color = col;
        }

        string list = "";
        for (int i = 0; i < s.games.Count; i++) list += (i > 0 ? "   <color=#55607A>|</color>   " : "") + s.games[i].title;
        Set(gamesLine, $"{s.games.Count} mini-games, {s.roundsPerGame} rounds each\n<color=#FFCC38>{list}</color>");

        bool isHost = s.IsServer;
        Show(startButton != null ? startButton.gameObject : null, isHost);
        Show(waitingText != null ? waitingText.gameObject : null, !isHost);
        if (startButton) startButton.interactable = s.CanStart;
        bool final = s.Phase == PartyPhase.Final;
        Set(startLabel, final ? "PLAY AGAIN" : human || offline ? "START GAME" : "START VS BOT");
        Set(waitingText, "Waiting for the host to start...");

        Show(resultsCard, final);
        if (final)
        {
            int w = s.PartyWinner;
            Set(resultsTitle, w == 2 || w < 0 ? "IT'S A DRAW!" : $"{PartyTeam.Tag(w, s.SideName(w))} WINS THE PARTY!");
            Set(resultsScore, $"{PartyTeam.Tag(0, s.Total(0).ToString())}  <color=#8892A8>:</color>  {PartyTeam.Tag(1, s.Total(1).ToString())}");
            Set(resultsWins, $"Parties won    {PartyTeam.Tag(0, s.Wins(0).ToString())}  -  {PartyTeam.Tag(1, s.Wins(1).ToString())}");
            if (!shownFinal)
            {
                shownFinal = true;
                PartySfx.Play2D(PartySfx.Win, 0.8f);
            }
        }
        else shownFinal = false;
    }

    static void Show(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }

    static void Set(TMP_Text t, string s)
    {
        if (t != null && t.text != s) t.text = s;
    }
}
