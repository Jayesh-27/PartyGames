using UnityEngine;

public class Grinder : MonoBehaviour
{
    [SerializeField] private GameObject Grinder1;
    [SerializeField] private GameObject Grinder2;
    [SerializeField] private float rotationSpeed = 1f;

    private void Update()
    {
        Grinder1.transform.Rotate(-Vector3.up * rotationSpeed * Time.deltaTime);
        Grinder2.transform.Rotate(-Vector3.up * rotationSpeed * Time.deltaTime);
    }
}
