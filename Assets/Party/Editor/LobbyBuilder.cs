using System;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static PartyBuild;

/// <summary>
/// Builds the Lobby scene (Tools > Party > Build Lobby Scene): the startup scene where players host, join with a
/// room code or play the bot, and where the host starts the party. Re-runnable.
/// </summary>
public static class LobbyBuilder
{
    public const string ScenePath = "Assets/Scenes/Lobby.unity";
    const float W = 1200f, H = 760f, M = 56f;

    static readonly Color Teal = new Color(0.22f, 0.84f, 0.74f);
    static readonly Color Violet = new Color(0.68f, 0.55f, 1f);
    static readonly Color Deep = new Color(0.045f, 0.055f, 0.10f, 1f);

    [MenuItem("Tools/Party/Build Lobby Scene")]
    public static void BuildMenu()
    {
        BuildScene();
        CaptureScreenshots();
    }

    public static void BuildScene()
    {
        LoadUi();
        var scene = OpenOrCopy(ScenePath);
        Strip(scene, true, true, "Lobby_");

        // ---- network object: keeps the connection and the party state alive across scenes
        NetworkManager nm = Object.FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
        if (nm == null) throw new Exception("No NetworkManager in " + SourceScene);
        GameObject core = nm.gameObject;
        core.transform.SetParent(null);
        if (core.GetComponent<PartyCore>() == null) core.AddComponent<PartyCore>();
        if (core.GetComponent<PartyConnection>() == null) core.AddComponent<PartyConnection>();
        if (core.GetComponent<PartySession>() == null) core.AddComponent<PartySession>();

        Clouds("Lobby_Clouds", 2.4f);

        GameObject player = new GameObject("Lobby_Player");
        PartyRig rig = player.AddComponent<PartyRig>();
        rig.rig = Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
        rig.homeSpot = Vector3.zero;
        rig.homeForward = Vector3.forward;
        rig.holdMenuToLeave = 0f;

        BuildCanvas();

        SaveScene(scene);
        ScenesFirst(ScenePath, KeeperClashBuilder.ScenePath, WhackBunnyBuilder.ScenePath);
        Debug.Log("[Party] Lobby scene built: " + ScenePath);
    }

    static void BuildCanvas()
    {
        // reuse the canvas from SampleScene: it carries the Meta ray-pointer wiring
        Canvas canvas = null;
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.transform.parent == null && c.transform.Find("ISDK_RayCanvasInteraction") != null) canvas = c;
        if (canvas == null) throw new Exception("No canvas with ISDK_RayCanvasInteraction in " + SourceScene);

        RelayNetworkButtons old = canvas.GetComponent<RelayNetworkButtons>();
        if (old != null) Object.DestroyImmediate(old);
        LobbyUI oldUi = canvas.GetComponent<LobbyUI>();
        if (oldUi != null) Object.DestroyImmediate(oldUi);
        for (int i = canvas.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = canvas.transform.GetChild(i);
            if (child.name != "ISDK_RayCanvasInteraction") Object.DestroyImmediate(child.gameObject);
        }

        canvas.gameObject.name = "Canvas";
        canvas.gameObject.SetActive(true);
        canvas.enabled = true;
        RectTransform crt = (RectTransform)canvas.transform;
        crt.sizeDelta = new Vector2(W, H);
        crt.localScale = Vector3.one * (2.0f / W);
        crt.position = new Vector3(0f, 1.42f, 2.35f);
        crt.rotation = Quaternion.identity;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null) scaler.dynamicPixelsPerUnit = 2f;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;

        // the ray pointer only works inside this box
        Transform isdk = canvas.transform.Find("ISDK_RayCanvasInteraction");
        isdk.gameObject.SetActive(true);
        foreach (MonoBehaviour mb in isdk.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            SerializedObject so = new SerializedObject(mb);
            SerializedProperty size = so.FindProperty("_size");
            if (size != null && size.propertyType == SerializedPropertyType.Vector3)
            {
                size.vector3Value = new Vector3(W, H, 0.01f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        LobbyUI ui = canvas.gameObject.AddComponent<LobbyUI>();
        Transform card = Card(canvas.transform, "Card", 0f, 0f, W, H, PartyTeam.Card, 48f, true);
        isdk.SetAsLastSibling();

        // ---- header
        Label(card, "Title", M, 30f, 640f, 86f, "PARTY GAMES", 76f, Color.white, TitleFont, TextAlignmentOptions.Left);
        Label(card, "Tagline", M + 2f, 112f, 640f, 34f, "Mini-game party for two  <color=#FFCC38>|</color>  VR", 24f, PartyTeam.Soft, BodyFont, TextAlignmentOptions.Left);
        Panel(card, "AccentBlue", W - M - 172f, 66f, 80f, 16f, PartyTeam.Blue, 8f);
        Panel(card, "AccentRed", W - M - 80f, 66f, 80f, 16f, PartyTeam.Red, 8f);
        Panel(card, "Divider", M, 160f, W - M * 2f, 3f, new Color(1f, 1f, 1f, 0.09f), 2f);

        // ---- home
        Transform home = Fill(card, "Home");
        ui.homePage = home.gameObject;
        ui.hostButton = Tile(home, "Host", M, Teal, "+", "HOST GAME", "Open a room and get a code\nfor your friend");
        ui.joinButton = Tile(home, "Join", M + 374f, PartyTeam.Gold, "#", "JOIN GAME", "Type the 6-digit code\nfrom your friend's room");
        ui.botButton = Tile(home, "Bot", M + 748f, Violet, "AI", "PLAY VS BOT", "No internet needed.\nPractice against the bot");
        ui.homeStatus = Label(home, "Status", M, 640f, W - M * 2f, 70f, "", 28f, PartyTeam.Gold);

        // ---- join
        Transform join = Fill(card, "Join");
        ui.joinPage = join.gameObject;
        Label(join, "Heading", M, 184f, 564f, 60f, "ENTER ROOM CODE", 46f, Color.white, TitleFont, TextAlignmentOptions.Left);
        for (int i = 0; i < PartyConnection.CodeLength; i++)
        {
            Image box = Panel(join, "Digit" + i, M + i * 96f, 262f, 84f, 108f, new Color(1f, 1f, 1f, 0.28f), 18f);
            ui.codeBoxes[i] = box;
            ui.codeDigits[i] = Label(box.transform, "Text", 0f, 0f, 84f, 108f, "", 70f, PartyTeam.Ink, TitleFont);
        }
        ui.joinConfirmButton = Btn(join, "JoinRoom", M, 400f, 564f, 96f, "JOIN ROOM", PartyTeam.Gold, PartyTeam.Ink, 46f);
        ui.joinBackButton = Btn(join, "Back", M, 520f, 220f, 76f, "BACK", PartyTeam.CardLight, Color.white, 34f);
        ui.joinStatus = Label(join, "Status", M, 620f, 564f, 90f, "", 24f, PartyTeam.Soft, BodyFont, TextAlignmentOptions.TopLeft);

        const float kx = 700f, ky = 184f, kw = 136f, kh = 104f, kg = 14f;
        for (int d = 1; d <= 9; d++)
        {
            int col = (d - 1) % 3, row = (d - 1) / 3;
            ui.digitButtons[d] = Btn(join, "Key" + d, kx + col * (kw + kg), ky + row * (kh + kg), kw, kh, d.ToString(), PartyTeam.CardLight, Color.white, 54f);
        }
        ui.digitButtons[0] = Btn(join, "Key0", kx, ky + 3f * (kh + kg), kw * 2f + kg, kh, "0", PartyTeam.CardLight, Color.white, 54f);
        ui.backspaceButton = Btn(join, "KeyDelete", kx + 2f * (kw + kg), ky + 3f * (kh + kg), kw, kh, "DEL", new Color(0.36f, 0.20f, 0.26f), Color.white, 38f);

        // ---- room
        Transform room = Fill(card, "Room");
        ui.roomPage = room.gameObject;
        ui.roomCodeLabel = Label(room, "CodeLabel", M + 2f, 182f, 520f, 34f, "ROOM CODE", 26f, PartyTeam.Soft, BodyFont, TextAlignmentOptions.Left);
        ui.roomCode = Label(room, "Code", M, 208f, 540f, 140f, "482 913", 124f, PartyTeam.Gold, TitleFont, TextAlignmentOptions.Left);
        ui.gamesLine = Label(room, "Games", M + 2f, 362f, 540f, 90f, "2 mini-games, 2 rounds each\n<color=#FFCC38>KEEPER CLASH   |   WHACK-A-BUNNY</color>", 24f, PartyTeam.Soft, BodyFont, TextAlignmentOptions.TopLeft);

        PlayerCard(room, "BlueCard", 186f, PartyTeam.Blue, "P1", out ui.blueName, out ui.blueSub);
        ui.redCard = PlayerCard(room, "RedCard", 322f, PartyTeam.Red, "P2", out ui.redName, out ui.redSub);

        Label(room, "Tip", M, 474f, W - M * 2f, 110f,
            "Every mini-game is 2 rounds. Goals and bunnies give points, winning a round gives a bonus.\nMost points after all mini-games wins the party.",
            24f, PartyTeam.Soft);

        RectTransform results = Card(room, "Results", M, 462f, W - M * 2f, 140f, Deep, 30f);
        ui.resultsCard = results.gameObject;
        ui.resultsTitle = Label(results, "Title", 32f, 14f, 620f, 64f, "BLUE WINS THE PARTY!", 48f, Color.white, TitleFont, TextAlignmentOptions.Left, true);
        ui.resultsWins = Label(results, "Wins", 34f, 82f, 620f, 40f, "Parties won    1  -  0", 26f, PartyTeam.Soft, BodyFont, TextAlignmentOptions.Left, true);
        // one line that shrinks to fit, so big totals never spill out of the card
        ui.resultsScore = Label(results, "Score", 660f, 14f, 396f, 112f, "1320  :  1250", 92f, Color.white, TitleFont, TextAlignmentOptions.Right, true);
        foreach (TMP_Text t in new[] { ui.resultsTitle, ui.resultsWins, ui.resultsScore }) t.textWrappingMode = TextWrappingModes.NoWrap;
        ui.resultsScore.overflowMode = TextOverflowModes.Truncate;

        ui.leaveButton = Btn(room, "Leave", M, 640f, 220f, 80f, "LEAVE", PartyTeam.CardLight, Color.white, 34f);
        ui.startButton = Btn(room, "Start", W - M - 470f, 626f, 470f, 100f, "START GAME", PartyTeam.Gold, PartyTeam.Ink, 50f, out TextMeshProUGUI startLabel);
        ui.startLabel = startLabel;
        ui.waitingText = Label(room, "Waiting", W - M - 560f, 626f, 560f, 100f, "Waiting for the host to start...", 30f, PartyTeam.Soft, BodyFont, TextAlignmentOptions.Right);

        // ---- busy
        Image busy = Panel(card, "Busy", 0f, 0f, W, H, new Color(0.04f, 0.05f, 0.09f, 0.86f), 48f, true);
        ui.busyOverlay = busy.gameObject;
        ui.busyText = Label(busy.transform, "Text", 0f, 0f, W, H, "Connecting...", 44f, Color.white, TitleFont);

        join.gameObject.SetActive(false);
        room.gameObject.SetActive(false);
        results.gameObject.SetActive(false);
        ui.waitingText.gameObject.SetActive(false);
        busy.gameObject.SetActive(false);
        EditorUtility.SetDirty(ui);
    }

    static Button Tile(Transform parent, string name, float x, Color accent, string glyph, string title, string text)
    {
        const float w = 340f, h = 410f, y = 196f;
        Button b = Btn(parent, name, x, y, w, h, "", PartyTeam.CardLight, Color.white, 10f, out TextMeshProUGUI unused, 34f);
        Object.DestroyImmediate(unused.gameObject);
        Transform t = b.transform;
        Panel(t, "Bar", 0f, 0f, w, 12f, accent, 6f);
        Circle(t, "Badge", (w - 132f) * 0.5f, 44f, 132f, accent);
        Label(t, "Glyph", (w - 132f) * 0.5f, 44f, 132f, 132f, glyph, glyph.Length > 1 ? 58f : 84f, PartyTeam.Ink, TitleFont);
        Label(t, "Title", 0f, 204f, w, 64f, title, 46f, Color.white, TitleFont);
        Label(t, "Text", 20f, 276f, w - 40f, 100f, text, 23f, PartyTeam.Soft);
        return b;
    }

    static Image PlayerCard(Transform parent, string name, float y, Color color, string tag, out TMP_Text playerName, out TMP_Text sub)
    {
        const float x = 620f, w = W - M - 620f, h = 120f;
        Image card = Panel(parent, name, x, y, w, h, color, 30f);
        Panel(card.transform, "Sheen", 0f, 0f, w, h * 0.5f, new Color(1f, 1f, 1f, 0.10f), 30f);
        playerName = Label(card.transform, "Name", 32f, 10f, 320f, 62f, name.StartsWith("Blue") ? "BLUE" : "RED", 52f, Color.white, TitleFont, TextAlignmentOptions.Left);
        sub = Label(card.transform, "Sub", 34f, 72f, 380f, 36f, "You", 26f, new Color(1f, 1f, 1f, 0.9f), BodyFont, TextAlignmentOptions.Left);
        Label(card.transform, "Tag", w - 190f, 14f, 160f, 92f, tag, 78f, new Color(1f, 1f, 1f, 0.28f), TitleFont, TextAlignmentOptions.Right);
        return card;
    }

    [MenuItem("Tools/Party/Capture Lobby Screenshots")]
    public static void CaptureScreenshots()
    {
        LobbyUI ui = Object.FindFirstObjectByType<LobbyUI>(FindObjectsInactive.Include);
        if (ui == null) return;
        Vector3 eye = new Vector3(0f, 1.6f, 0f), look = new Vector3(0f, 1.42f, 2.35f);

        Shot("lobby_home.png", eye, look, 62f);

        ui.homePage.SetActive(false);
        ui.joinPage.SetActive(true);
        for (int i = 0; i < 3; i++) { ui.codeDigits[i].text = (4 + i * 2).ToString(); ui.codeBoxes[i].color = Color.white; }
        ui.codeBoxes[3].color = PartyTeam.Gold;
        Shot("lobby_join.png", eye, look, 62f);
        for (int i = 0; i < ui.codeDigits.Length; i++) { ui.codeDigits[i].text = ""; ui.codeBoxes[i].color = new Color(1f, 1f, 1f, 0.28f); }

        ui.joinPage.SetActive(false);
        ui.roomPage.SetActive(true);
        Shot("lobby_room.png", eye, look, 62f);
        ui.resultsCard.SetActive(true);
        Shot("lobby_results.png", eye, look, 62f);
        ui.resultsCard.SetActive(false);

        ui.roomPage.SetActive(false);
        ui.homePage.SetActive(true);
        Shot("lobby_wide.png", new Vector3(2.6f, 2.3f, -3.4f), new Vector3(0f, 1.1f, 1.2f), 60f);
        Debug.Log("[Party] Lobby screenshots saved to " + ShotDir());
    }
}

/// <summary>
/// The mini-game scenes have no NetworkManager of their own, so pressing Play in one of them starts in the Lobby.
/// Other scenes (the grinder scene) are left alone.
/// </summary>
[InitializeOnLoad]
public static class PartyPlayFromLobby
{
    static PartyPlayFromLobby()
    {
        EditorApplication.playModeStateChanged += change =>
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;
            string active = EditorSceneManager.GetActiveScene().path;
            bool partyScene = active == KeeperClashBuilder.ScenePath || active == WhackBunnyBuilder.ScenePath;
            EditorSceneManager.playModeStartScene = partyScene ? AssetDatabase.LoadAssetAtPath<SceneAsset>(LobbyBuilder.ScenePath) : null;
        };
    }
}
