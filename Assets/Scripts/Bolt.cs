using UnityEngine;

public class Bolt : MonoBehaviour
{
    public int colorIndex;      // ������ �����
    private bool isCollected = false;

    private void OnMouseDown()
    {
        if (isCollected) return;
        isCollected = true;

        // ���������� ���� � GameManager
        GameManager.Instance.OnBoltClicked(this);
    }
}
