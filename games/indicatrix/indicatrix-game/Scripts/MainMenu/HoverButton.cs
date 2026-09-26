using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Swaps a button's Image sprite on pointer enter and restores it on pointer exit.
/// Attach to each button GameObject (the one carrying the Image).
///
/// IMPORTANT: Set the Button component's Transition to "None" so its own
/// color/sprite transitions do not fight with this script's sprite swap.
///
/// DISABLED STAV:
///   Ak má button aj vlastnú grafiku (tento skript), samotné "Button.interactable = false"
///   sa opticky vôbec neprejaví – hover sprite by ho aj tak prekryl. Preto tento skript
///   pozná aj tretí sprite "Disabled Sprite". Iný skript (napr. MainMenuController) ho
///   vypne/zapne cez SetInteractable(bool) – to nastaví aj Button.interactable (ak Button
///   existuje) aj vizuál, a kým je vypnutý, hover naň nereaguje.
/// </summary>
[RequireComponent(typeof(Image))]
public class HoverButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("References")]
    [Tooltip("Image whose sprite will be swapped. Auto-filled with the Image on this object if left empty.")]
    [SerializeField] private Image targetImage;
    [Tooltip("Voliteľné. Auto-filled s Button na tomto objekte, ak existuje. Používa sa na synchronizáciu Button.interactable pri SetInteractable().")]
    [SerializeField] private Button targetButton;

    [Header("Sprites")]
    [SerializeField] private Sprite defaultSprite;
    [SerializeField] private Sprite hoverSprite;
    [Tooltip("Sprite zobrazený, keď je button vypnutý (SetInteractable(false)). Ak nie je priradený, ostane defaultSprite (bez vizuálnej zmeny).")]
    [SerializeField] private Sprite disabledSprite;

    // True = normálne funguje hover swap. False = zobrazuje disabledSprite a hover ho neovplyvní.
    private bool isInteractable = true;

    private void Awake()
    {
        if (targetImage == null) targetImage = GetComponent<Image>();
        if (targetButton == null) targetButton = GetComponent<Button>();

        // Fall back to whatever sprite is currently assigned if no default was set.
        if (defaultSprite == null && targetImage != null) defaultSprite = targetImage.sprite;

        ApplyCurrentState();
    }

    private void OnEnable()
    {
        // Reset to default whenever the button is re-shown (e.g. coming back from a panel).
        ApplyCurrentState();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isInteractable) return;
        if (targetImage != null && hoverSprite != null)
            targetImage.sprite = hoverSprite;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isInteractable) return;
        ApplyDefault();
    }

    /// <summary>
    /// Zapne/vypne button: nastaví Button.interactable (ak Button existuje) a prepne
    /// vizuál na disabledSprite / defaultSprite. Kým je vypnutý, hover ho neovplyvní.
    /// </summary>
    public void SetInteractable(bool interactable)
    {
        isInteractable = interactable;

        if (targetButton != null) targetButton.interactable = interactable;

        ApplyCurrentState();
    }

    private void ApplyCurrentState()
    {
        if (!isInteractable) ApplyDisabled();
        else ApplyDefault();
    }

    private void ApplyDefault()
    {
        if (targetImage != null && defaultSprite != null)
            targetImage.sprite = defaultSprite;
    }

    private void ApplyDisabled()
    {
        if (targetImage == null) return;
        targetImage.sprite = (disabledSprite != null) ? disabledSprite : defaultSprite;
    }
}