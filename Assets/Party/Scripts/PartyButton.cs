using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Button feel: grows a little when pointed at, shrinks when pressed, plays a click, greys out when disabled.</summary>
[RequireComponent(typeof(Button))]
public class PartyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public float hoverScale = 1.06f;
    public float pressScale = 0.94f;
    public CanvasGroup group;

    Button button;
    bool over, down;
    float scale = 1f;

    void Awake()
    {
        button = GetComponent<Button>();
        if (group == null) group = GetComponent<CanvasGroup>();
        button.onClick.AddListener(() => PartySfx.Play2D(PartySfx.Click, 0.7f));
    }

    void OnDisable()
    {
        over = down = false;
        scale = 1f;
        transform.localScale = Vector3.one;
    }

    void Update()
    {
        bool usable = button.interactable;
        float target = !usable ? 1f : down ? pressScale : over ? hoverScale : 1f;
        scale = Mathf.Lerp(scale, target, 1f - Mathf.Exp(-22f * Time.unscaledDeltaTime));
        transform.localScale = new Vector3(scale, scale, 1f);
        if (group != null) group.alpha = usable ? 1f : 0.35f;
    }

    public void OnPointerEnter(PointerEventData e)
    {
        over = true;
        if (button.interactable) PartySfx.Play2D(PartySfx.Hover, 0.35f);
    }

    public void OnPointerExit(PointerEventData e) { over = false; down = false; }
    public void OnPointerDown(PointerEventData e) => down = true;
    public void OnPointerUp(PointerEventData e) => down = false;
}
