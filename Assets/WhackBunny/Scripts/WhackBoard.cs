using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The board floating over the table (one face per player): round, whose turn, timer and both scores.</summary>
public class WhackBoard : MonoBehaviour
{
    public TMP_Text[] roundTexts;
    public TMP_Text[] headlineTexts;
    public TMP_Text[] timerTexts;
    public TMP_Text[] detailTexts;
    public TMP_Text[] blueTexts;
    public TMP_Text[] redTexts;
    public TMP_Text[] blueNames;
    public TMP_Text[] redNames;
    public Image[] timerBars;

    void Update()
    {
        WhackMatch m = WhackMatch.Instance;
        PartySession party = PartySession.Instance;
        bool online = m != null && m.IsSpawned && party != null;

        string round = "WHACK-A-BUNNY", head = "", timer = "", detail = "", blue = "0", red = "0";
        float bar = 0f;
        Color barColor = PartyTeam.Soft;

        if (online)
        {
            round = $"ROUND {Mathf.Max(1, party.Round)} / {party.roundsPerGame}";
            blue = m.Score(0).ToString();
            red = m.Score(1).ToString();
            Set(blueNames, party.SideName(0));
            Set(redNames, party.SideName(1));

            if (party.Phase != PartyPhase.Round)
            {
                head = party.Headline();
                detail = party.Detail();
                if (party.Phase == PartyPhase.GameResult) { blue = party.GamePoints(0).ToString(); red = party.GamePoints(1).ToString(); }
                else if (party.Phase == PartyPhase.RoundResult) { blue = party.RoundPoints(0).ToString(); red = party.RoundPoints(1).ToString(); }
            }
            else
            {
                int side = m.ActiveSide;
                string who = side >= 0 ? PartyTeam.Tag(side, party.SideName(side)) : "";
                switch (m.Phase)
                {
                    case WhackPhase.Ready:
                        head = who + " IS UP";
                        timer = Mathf.CeilToInt(m.PhaseTimeLeft).ToString();
                        detail = party.IsLocalHumanSide(side) ? "Your turn! Swing down on the bunnies" : "Watch and wait for your turn";
                        break;
                    case WhackPhase.Turn:
                        head = who + "'S TURN";
                        float left = m.TurnTimeLeft;
                        timer = left < 5f ? left.ToString("0.0") : Mathf.CeilToInt(left).ToString();
                        bar = left / m.TurnSeconds;
                        barColor = PartyTeam.Of(side);
                        break;
                    case WhackPhase.TurnOver:
                        head = "TIME!";
                        break;
                }
                if (detail.Length == 0) detail = "PARTY   " + party.TotalsLine();
            }
        }

        Set(roundTexts, round);
        Set(headlineTexts, head);
        Set(timerTexts, timer);
        Set(detailTexts, detail);
        Set(blueTexts, blue);
        Set(redTexts, red);
        if (timerBars != null)
            foreach (Image i in timerBars)
            {
                if (i == null) continue;
                i.fillAmount = bar;
                i.color = barColor;
            }
    }

    static void Set(TMP_Text[] texts, string s)
    {
        if (texts == null) return;
        foreach (TMP_Text t in texts)
            if (t != null && t.text != s) t.text = s;
    }
}
