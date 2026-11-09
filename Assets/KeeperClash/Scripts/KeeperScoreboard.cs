using TMPro;
using UnityEngine;

/// <summary>Two-faced scoreboard over the field: score, phase/countdown, room code and ball speed.</summary>
public class KeeperScoreboard : MonoBehaviour
{
    public TMP_Text[] scoreTexts;
    public TMP_Text[] statusTexts;
    public TMP_Text[] speedTexts;

    string lastScore, lastStatus, lastSpeed;

    void Update()
    {
        KeeperMatch m = KeeperMatch.Instance;
        KeeperBall ball = KeeperBall.Instance;
        bool online = m != null && m.IsSpawned;

        string blue = ColorUtility.ToHtmlStringRGB(KeeperFx.TeamColor(0));
        string red = ColorUtility.ToHtmlStringRGB(KeeperFx.TeamColor(1));
        string score = online
            ? $"<color=#{blue}>BLUE {m.ScoreBlue}</color>  :  <color=#{red}>{m.ScoreRed} {(m.BotActive ? "BOT" : "RED")}</color>"
            : "KEEPER CLASH";

        string status = online ? Status(m) : "Host, join with a code, or Practice";
        string speed = online && ball != null && ball.IsFlying ? $"{ball.State.speed * 3.6f:0} km/h" : "";

        if (score != lastScore) { lastScore = score; Set(scoreTexts, score); }
        if (status != lastStatus) { lastStatus = status; Set(statusTexts, status); }
        if (speed != lastSpeed) { lastSpeed = speed; Set(speedTexts, speed); }
    }

    static string Status(KeeperMatch m)
    {
        string room = KeeperLobby.Instance != null ? KeeperLobby.Instance.RoomInfo : "";
        switch (m.Phase)
        {
            case KeeperPhase.Waiting: return "Waiting for opponent" + (room.Length > 0 ? "\n" + room : "");
            case KeeperPhase.Countdown: return Mathf.CeilToInt(m.TimeLeft).ToString() + (m.BotActive && room.Length > 0 ? "\n" + room : "");
            case KeeperPhase.Playing: return m.BotActive && room.Length > 0 ? "Practice  " + room : "";
            case KeeperPhase.GoalScored: return (m.LastScorer == 0 ? "BLUE" : "RED") + " SCORES!";
            case KeeperPhase.MatchOver: return (m.Winner == 0 ? "BLUE" : "RED") + " WINS!";
        }
        return "";
    }

    static void Set(TMP_Text[] texts, string s)
    {
        if (texts == null) return;
        foreach (TMP_Text t in texts)
            if (t != null) t.text = s;
    }
}
