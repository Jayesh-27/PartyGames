using System.Collections;
using Oculus.Interaction;
using UnityEngine;

[DefaultExecutionOrder(200)]
public class Slingshot : MonoBehaviour
{
    [SerializeField] private Transform pull;
    [SerializeField] private Transform pouch;
    [SerializeField] private Transform aimingPoint; // "aiming point"
    [SerializeField] private LayerMask stoneLayers;
    [SerializeField] private float loadRadius = 0.1f;
    [SerializeField] private float maxStretch = 3f;
    [SerializeField] private float power = 25f;
    [SerializeField] private float maxSpeed = 15f;

    private OVRCameraRig rig;
    private Transform pullHand;
    private Transform holdHand;

    private Grabbable body;
    private Vector3 holdLocal;
    private bool wasHeld;

    private Grabbable stone;
    private Collider stoneCollider;
    private MeshRenderer stoneRenderer;
    private Transform visual;
    private Vector3 centerOffset;

    private Transform pivot;
    private float restDistance;
    private float pullAmount;
    private Collider[] slingColliders;
    private readonly Collider[] hits = new Collider[8];

    private void Start()
    {
        rig = FindFirstObjectByType<OVRCameraRig>();
        body = GetComponent<Grabbable>();

        if (aimingPoint == null || pouch == null)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (aimingPoint == null && t.name == "aiming point") aimingPoint = t;
                else if (pouch == null && t.name == "pouchrests") pouch = t;
            }
        }

        if (aimingPoint != null)
            aimingPoint.SetParent(transform, true);

        pivot = new GameObject("PullPivot").transform;
        pivot.SetParent(transform, false);
        Vector3 pouchLocal = transform.InverseTransformPoint(pouch.position);
        Vector3 pullLocal = transform.InverseTransformPoint(pull.position);
        pivot.localPosition = new Vector3(pouchLocal.x, pouchLocal.y, pullLocal.z);
        pull.SetParent(pivot, true);

        restDistance = Vector3.Distance(pivot.position, pouch.position);
        slingColliders = GetComponentsInChildren<Collider>();
    }

    private Transform GetHand(bool right)
    {
        AvatarHands avatar = AvatarHands.Local;
        Transform t = avatar != null ? avatar.Get(right) : null;
        if (t != null) return t;
        return right ? rig.rightControllerAnchor : rig.leftControllerAnchor;
    }

    private void LateUpdate()
    {
        if (stone == null)
        {
            TryLoad();
            return;
        }

        Vector3 toHand = pullHand.position - pivot.position;

        bool held = body != null && body.SelectingPointsCount > 0;
        if (held && !wasHeld)
            holdLocal = transform.InverseTransformPoint(holdHand.position);
        wasHeld = held;

        Vector3 center = held ? transform.TransformPoint(holdLocal) : pivot.position;
        Vector3 back = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        Vector3 want = Vector3.ProjectOnPlane(toHand, Vector3.up);
        transform.RotateAround(center, Vector3.up, Vector3.SignedAngle(back, want, Vector3.up));

        Vector3 local = transform.InverseTransformDirection(toHand);
        pivot.localRotation = Quaternion.Euler(-Mathf.Atan2(local.y, local.z) * Mathf.Rad2Deg, 0f, 0f);

        pullAmount = toHand.magnitude;
        pivot.localScale = new Vector3(1f, 1f, Mathf.Min(pullAmount / restDistance, maxStretch));

        visual.SetPositionAndRotation(pouch.position, pivot.rotation);
        visual.position -= visual.TransformVector(centerOffset);

        // Fire only after aiming this frame: the grab resets the slingshot's pose every frame,
        // so firing before the re-aim would shoot along the raw hand-grab orientation.
        if (stone.SelectingPointsCount == 0)
            StartCoroutine(Fire());
    }

    private void TryLoad()
    {
        if (rig == null)
        {
            rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null) return;
        }

        int n = Physics.OverlapSphereNonAlloc(pouch.position, loadRadius, hits, stoneLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Grabbable g = hits[i].GetComponentInParent<Grabbable>();
            if (g == null || g == body) continue;
            if (g.SelectingPointsCount == 0) continue;

            MeshFilter mf = g.GetComponentInChildren<MeshFilter>();
            MeshRenderer mr = g.GetComponentInChildren<MeshRenderer>();
            Collider sc = g.GetComponentInChildren<Collider>();
            
            if (mf == null || mr == null || sc == null) continue;

            stone = g;
            stoneCollider = sc;
            stoneRenderer = mr;
            centerOffset = mf.transform.InverseTransformPoint(stoneCollider.bounds.center);

            // Identify the hand holding the SLINGSHOT from its own grab point.
            // Do not infer this from the stone: at load time both hands can be near the pouch.
            Transform rightT = GetHand(true);
            Transform leftT = GetHand(false);

            Vector3 slingGrabPosition =
                body != null && body.GrabPoints.Count > 0
                    ? body.GrabPoints[0].position
                    : transform.position;

            bool slingHeldByRight =
                (slingGrabPosition - rig.rightControllerAnchor.position).sqrMagnitude <
                (slingGrabPosition - rig.leftControllerAnchor.position).sqrMagnitude;

            holdHand = slingHeldByRight ? rightT : leftT;
            pullHand = slingHeldByRight ? leftT : rightT;

            Debug.Log(
                $"[Slingshot] Slingshot hand: {(slingHeldByRight ? "RIGHT" : "LEFT")}; " +
                $"Pull hand: {(slingHeldByRight ? "LEFT" : "RIGHT")}; " +
                $"Grab point available: {(body != null && body.GrabPoints.Count > 0)}",
                this
            );

            wasHeld = false;

            GameObject v = new GameObject("LoadedRock");
            v.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            v.AddComponent<MeshRenderer>().sharedMaterials = stoneRenderer.sharedMaterials;
            v.transform.localScale = mf.transform.lossyScale;
            visual = v.transform;
            stoneRenderer.enabled = false;

            foreach (Collider col in slingColliders)
                if (col != null) Physics.IgnoreCollision(stoneCollider, col, true);

            IgnoreLocalPlayer(stoneCollider);
            return;
        }
    }

    private void IgnoreLocalPlayer(Collider stoneCol)
    {
        if (AvatarHands.Local != null)
        {
            foreach (Collider col in AvatarHands.Local.GetComponentsInChildren<Collider>(true))
                if (col != null) Physics.IgnoreCollision(stoneCol, col, true);
        }

        if (rig != null)
        {
            foreach (Collider col in rig.GetComponentsInChildren<Collider>(true))
                if (col != null) Physics.IgnoreCollision(stoneCol, col, true);
        }
    }

    private IEnumerator Fire()
    {
        Rigidbody rb = stone.GetComponent<Rigidbody>();
        MeshRenderer rend = stoneRenderer;
        Collider stoneCol = stoneCollider;
        Vector3 direction = (aimingPoint.position - pouch.position).normalized;
        float speed = Mathf.Min(Mathf.Max(0f, pullAmount - restDistance) * power, maxSpeed);

        stone.transform.SetPositionAndRotation(visual.position, visual.rotation);
        rend.enabled = true;
        Destroy(visual.gameObject);
        stone = null;

        pivot.localRotation = Quaternion.identity;
        pivot.localScale = Vector3.one;

        yield return new WaitForFixedUpdate();
        if (rb != null)
        {
            rb.linearVelocity = direction * speed;
            rb.angularVelocity = Vector3.zero;
        }

        yield return new WaitForSeconds(0.2f);
        
        float timeout = 3f;
        while (stoneCol != null && timeout > 0f && TouchingSling(stoneCol))
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (stoneCol != null)
            foreach (Collider col in slingColliders)
                if (col != null) Physics.IgnoreCollision(stoneCol, col, false);
    }

    private bool TouchingSling(Collider c)
    {
        Bounds b = c.bounds;
        b.Expand(0.05f);
        foreach (Collider col in slingColliders)
            if (col != null && col.bounds.Intersects(b))
                return true;
        return false;
    }
}