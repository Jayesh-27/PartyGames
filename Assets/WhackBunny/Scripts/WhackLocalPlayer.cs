using UnityEngine;

/// <summary>
/// The player on this headset in Whack-a-Bunny: stands them at their side of the table (Blue at -Z, Red at +Z)
/// and lets them move the hammer to the other hand with that hand's trigger or grip.
/// </summary>
[DefaultExecutionOrder(20)]
public class WhackLocalPlayer : MonoBehaviour
{
    [Tooltip("Where someone who is not playing stands")]
    public Vector3 watchSpot = new Vector3(1.7f, 0f, 0f);

    void Update()
    {
        PartyRig rig = PartyRig.Instance;
        WhackField field = WhackField.Instance;
        PartySession party = PartySession.Instance;
        if (rig == null || field == null) return;

        int side = party != null ? party.LocalSide : -1;
        if (side >= 0) rig.SetHome(field.StandSpot(side), field.Forward(side));
        else rig.SetHome(field.transform.position + watchSpot, -watchSpot.normalized);

        WhackMatch m = WhackMatch.Instance;
        if (m == null || !m.IsSpawned || side < 0) return;
        bool left = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch) ||
                    OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
        bool right = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                     OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
        if (left && !right) m.SetLocalHand(true);
        else if (right && !left) m.SetLocalHand(false);
    }
}
