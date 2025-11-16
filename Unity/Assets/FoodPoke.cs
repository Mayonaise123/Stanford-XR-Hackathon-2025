using UnityEngine;

public class FoodPoke : MonoBehaviour
{
    public ChecklistImageManager checklist;
    public GameObject nextFood;

    public void OnFoodSelected()
    {
        checklist.AdvanceChecklist();

        if (nextFood != null)
            nextFood.SetActive(true);

        gameObject.SetActive(false);
    }
}