using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// A drawer that slides out of its cabinet. Meta's OneGrabTranslateTransformer keeps it on its rail while a hand
/// holds it; this script sends how far it is open to the other headset (through HideSync) and follows the
/// value that comes back when somebody else moves it.
/// </summary>
public class SlideDrawer : MonoBehaviour
{
    [Tooltip("Set by HideSync: this drawer's number")]
    public int id;
    public Grabbable grabbable;
    [Tooltip("Local position (in the parent) when fully closed")]
    public Vector3 closedLocalPosition;
    [Tooltip("Slide direction in the parent's space")]
    public Vector3 slideAxis = Vector3.forward;
    public float maxSlide = 0.3f;
    public float sendRate = 15f;

    float remote = -1f;
    float lastSent = -1f;
    float nextSend;

    /// <summary>How far the drawer is open, in metres.</summary>
    public float Open => Mathf.Clamp(Vector3.Dot(transform.localPosition - closedLocalPosition, slideAxis.normalized), 0f, maxSlide);

    public bool HeldHere => grabbable != null && grabbable.SelectingPointsCount > 0;

    void Reset() => grabbable = GetComponent<Grabbable>();

    /// <summary>Another headset moved this drawer.</summary>
    public void SetRemote(float open) => remote = Mathf.Clamp(open, 0f, maxSlide);

    void SetOpen(float open) => transform.localPosition = closedLocalPosition + slideAxis.normalized * Mathf.Clamp(open, 0f, maxSlide);

    void LateUpdate()
    {
        if (HeldHere)
        {
            // keep it exactly on the rail, then tell the others
            float open = Open;
            SetOpen(open);
            remote = -1f;
            if (Time.unscaledTime >= nextSend && Mathf.Abs(open - lastSent) > 0.002f)
            {
                nextSend = Time.unscaledTime + 1f / Mathf.Max(1f, sendRate);
                lastSent = open;
                if (HideSync.Instance != null) HideSync.Instance.SendDrawer(id, open);
            }
        }
        else
        {
            if (lastSent >= 0f && Mathf.Abs(Open - lastSent) > 0.002f && HideSync.Instance != null)
            {
                // the final position after letting go
                lastSent = Open;
                HideSync.Instance.SendDrawer(id, lastSent);
            }
            if (remote >= 0f)
            {
                float open = Mathf.Lerp(Open, remote, 1f - Mathf.Exp(-14f * Time.deltaTime));
                if (Mathf.Abs(open - remote) < 0.001f) { open = remote; remote = -1f; }
                SetOpen(open);
            }
        }
    }
}
