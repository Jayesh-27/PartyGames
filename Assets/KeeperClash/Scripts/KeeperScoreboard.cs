using TMPro;
using UnityEngine;

/// <summary>The boards above the two goals: round, score, what is happening, ball speed and the party totals.</summary>
public class KeeperScoreboard : MonoBehaviour
{
    public TMP_Text[] roundTexts;
    public TMP_Text[] scoreTexts;
    public TMP_Text[] statusTexts;
    public TMP_Text[] speedTexts;   // small line at the bottom

    string lastRound, lastScore, lastStatus, lastSpeed;

    void Update()
    {
        KeeperMatch m = KeeperMatch.Instance;
        KeeperBall ball = KeeperBall.Instance;
        PartySession party = PartySession.Instance;
        bool online = m != null && m.IsSpawned && party != null;

        string round = "KEEPER CLASH", score = "", status = "", small = "";
        if (online)
        {
            round = $"ROUND {Mathf.Max(1, party.Round)} / {party.roundsPerGame}   <color=#8892A8>|</color>   FIRST TO {m.GoalsToWin}";
            if (party.Phase == PartyPhase.Round)
            {
                score = $"{PartyTeam.Tag(0, m.ScoreBlue.ToString())} <color=#8892A8>:</color> {PartyTeam.Tag(1, m.ScoreRed.ToString())}";
                status = Status(m, party);
            }
            else
            {
                // intro, round result, game result: the party's headline takes the big line
                score = party.Headline();
                status = party.Detail();
            }
            small = ball != null && ball.IsFlying && m.Phase == KeeperPhase.Playing
                ? $"{ball.State.speed * 3.6f:0} km/h"
                : "PARTY   " + party.TotalsLine();
        }

        if (round != lastRound) { lastRound = round; Set(roundTexts, round); }
        if (score != lastScore) { lastScore = score; Set(scoreTexts, score); }
        if (status != lastStatus) { lastStatus = status; Set(statusTexts, status); }
        if (small != lastSpeed) { lastSpeed = small; Set(speedTexts, small); }
    }

    static string Status(KeeperMatch m, PartySession party)
    {
        switch (m.Phase)
        {
            case KeeperPhase.Countdown: return Mathf.CeilToInt(m.TimeLeft).ToString();
            case KeeperPhase.GoalScored: return PartyTeam.Tag(m.LastScorer, party.SideName(m.LastScorer)) + " SCORES!";
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
