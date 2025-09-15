using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    public Transform target;       // Точка, вокруг которой вращаемся
    public float distance = 10f;   // Расстояние до цели
    public float rotateSpeed = 50f;

    private void LateUpdate()
    {
        if (target == null) return;

        float h = 0f;
        float z = 0f;
        if (Input.GetKey(KeyCode.LeftArrow)) h = -1f;
        if (Input.GetKey(KeyCode.RightArrow)) h = 1f;
        if (Input.GetKey(KeyCode.UpArrow)) z = -1f;
        if (Input.GetKey(KeyCode.DownArrow)) z = 1f;

        if (Mathf.Abs(h) > 0.01f || Mathf.Abs(z) > 0.01f)
        {
            transform.RotateAround(target.position, Vector3.up, h * rotateSpeed * Time.deltaTime);
            transform.RotateAround(target.position, Vector3.right, z * rotateSpeed * Time.deltaTime);
        }

        transform.LookAt(target);
    }
}
