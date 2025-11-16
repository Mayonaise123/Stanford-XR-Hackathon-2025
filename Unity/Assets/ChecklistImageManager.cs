using UnityEngine;
using UnityEngine.UI;

public class ChecklistImageManager : MonoBehaviour
{
    public Image checklistImage;      // assign the ChecklistImage UI
    public Sprite[] checklistStates;  // fill in inspector with sprites in order
    private int index = 0;

    void Start()
    {
        if (checklistImage != null && checklistStates != null && checklistStates.Length > 0)
            checklistImage.sprite = checklistStates[Mathf.Clamp(index, 0, checklistStates.Length - 1)];
    }

    public void AdvanceChecklist()
    {
        index = Mathf.Min(index + 1, checklistStates.Length - 1);
        UpdateImage();
    }

    public void SetChecklist(int stateIndex)
    {
        index = Mathf.Clamp(stateIndex, 0, checklistStates.Length - 1);
        UpdateImage();
    }

    public void ResetChecklist()
    {
        index = 0;
        UpdateImage();
    }

    private void UpdateImage()
    {
        if (checklistImage != null && checklistStates != null && checklistStates.Length > 0)
            checklistImage.sprite = checklistStates[index];
    }

    public int GetIndex() => index;
}