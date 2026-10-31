using UnityEngine;

enum lockedAxis
{
    X,Y,Z
}
public class LookAtYOnly : MonoBehaviour
{
    private float startRotX;
    private float startRotY;
    private float startRotZ;
    [SerializeField] private lockedAxis locked;


    void Start()
    {
        startRotX = transform.eulerAngles.x;
        startRotY = transform.eulerAngles.x;
        startRotZ = transform.eulerAngles.z;
    }

    void Update()
    {
        Vector3 rot = transform.eulerAngles;

        if (locked == lockedAxis.X)
            transform.eulerAngles = new Vector3(startRotX, rot.y, rot.z);
        else if (locked == lockedAxis.Y)
            transform.eulerAngles = new Vector3(rot.x, startRotY, rot.z);
        else if (locked == lockedAxis.Z)
            transform.eulerAngles = new Vector3(rot.x, rot.y, startRotZ);
    }
}