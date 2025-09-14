using UnityEngine;

public class Bolt : MonoBehaviour
{
    public int colorIndex;      // Индекс цвета
    private bool isCollected = false;

    private void OnMouseDown()
    {
        if (isCollected) return;
        isCollected = true;

        // Отправляем себя в GameManager
        GameManager.Instance.OnBoltClicked(this);
    }
}
