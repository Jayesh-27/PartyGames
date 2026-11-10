using UnityEngine;

/// <summary>Team colours and names shared by the lobby and every mini-game. Side 0 = Blue (host), 1 = Red.</summary>
public static class PartyTeam
{
    public static readonly Color Blue = new Color(0.22f, 0.60f, 1f);
    public static readonly Color Red = new Color(1f, 0.32f, 0.38f);
    public static readonly Color Gold = new Color(1f, 0.80f, 0.22f);
    public static readonly Color Ink = new Color(0.07f, 0.09f, 0.15f);
    public static readonly Color Card = new Color(0.09f, 0.11f, 0.18f, 0.94f);
    public static readonly Color CardLight = new Color(0.17f, 0.20f, 0.30f, 1f);
    public static readonly Color Soft = new Color(0.74f, 0.79f, 0.90f);

    public static Color Of(int side) => side == 0 ? Blue : side == 1 ? Red : Soft;
    public static string Name(int side) => side == 0 ? "BLUE" : side == 1 ? "RED" : "";
    public static string Hex(int side) => ColorUtility.ToHtmlStringRGB(Of(side));
    public static string Tag(int side, string text) => $"<color=#{Hex(side)}>{text}</color>";
}
