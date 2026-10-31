using System.Collections;
using UnityEngine;

public class ObjectDetection : MonoBehaviour
{
    [SerializeField] private GameObject animatePosition;
    [SerializeField] private float deleteAfter = 2f;

    private bool enable = false;
    private GameObject targetObject = null;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("obj_rope"))
            return;

        Collider ropeCollider = other.GetComponent<Collider>();
        ropeCollider.enabled = false;
        // other.GetComponent<Rigidbody>().useGravity = false;
        targetObject = other.gameObject;

        Destroy(targetObject.GetComponent<Rigidbody>());
        StartCoroutine(animateTargetObject());
    }

    private void Update()
    {
        if(!enable)
            return;
        
        targetObject.transform.position = animatePosition.transform.position;
        targetObject.transform.rotation = animatePosition.transform.rotation;
    }

    private IEnumerator animateTargetObject()
    {
        enable = true;
        yield return new WaitForSeconds(deleteAfter);
        enable = false;
        Destroy(targetObject);
        Debug.Log("Destroying: " + targetObject.name);
    }
}