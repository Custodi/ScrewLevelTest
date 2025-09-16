using UnityEngine;

public class Bolt : MonoBehaviour
{
    public int colorIndex;
    public string parentMeshId;
    public string pointId;
    public bool isPlaced = false;

    private bool isCollected = false;

    // ⚡ Теперь болт не сам ловит клик, а обрабатывается через GameManager
    public void OnClicked()
    {
        if (isCollected || isPlaced) return;
        isCollected = true;

        GameManager.Instance.OnBoltDetachedFromMesh(this);
    }
}
