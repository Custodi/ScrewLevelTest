using UnityEngine;

public class Bolt : MonoBehaviour
{
    [Header("Bolt Data")]
    public string pointId;          // ID точки, на которой стоит болт (из JSON)
    public string parentMeshId;     // меш, к которому прикручен болт
    public string blockingMeshId;   // меш, блокирующий выкручивание (если есть)
    public int colorIndex;          // цвет болта (для корзины)

    [Header("State")]
    public bool isPlaced = false;   // установлен ли болт в корзину/буфер
    public bool isUnscrewed = false; // выкручен ли болт (false = ещё прикручен к мешу)

    /// <summary>
    /// Можно ли выкрутить болт прямо сейчас?
    /// </summary>
    public bool CanBeUnscrewed
    {
        get
        {
            if (isUnscrewed)
                return false; // уже выкручен, повторно нельзя

            // если ничего не блокирует
            if (string.IsNullOrEmpty(blockingMeshId))
                return true;

            if (GameManager.Instance == null)
                return true;

            // если меш-блокер всё ещё существует — выкрутить нельзя
            return !GameManager.Instance.IsMeshStillExists(blockingMeshId);
        }
    }

    /// <summary>
    /// Вызывается при клике игрока
    /// </summary>
    public void OnClicked()
    {
        if (!CanBeUnscrewed)
        {
            Debug.Log($"Bolt {name}: меш {blockingMeshId} ещё существует, выкрутить нельзя!");
            return;
        }

        if (isUnscrewed)
        {
            Debug.Log($"Bolt {name}: уже выкручен.");
            return;
        }

        // Помечаем как выкрученный
        isUnscrewed = true;

        // Передаём управление GameManager
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnBoltClicked(this);
            GameManager.Instance.OnBoltDetachedFromMesh(this);
        }
    }
}
