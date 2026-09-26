using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// WindowDragHandle
/// ─────────────────────────────────────────────────────────────────────────
/// Generický komponent pre ťahanie (drag-and-drop) UI okna za jeho titulok.
///
/// POUŽITIE:
///   Pripoj tento skript na GameObject titulku okna. Aktuálne podporované
///   okná v hre:
///     - "RailConstructionMenuUI - Title"    → drag panela RailConstructionMenuUIPanel - Window
///     - "RoadConstructionMenuUI - Title"    → drag panela RoadConstructionMenuUIPanel - Window
///     - "DepotConstructionMenuUI - Title"   → drag panela DepotRailConstructionMenuUIPanel - Window
///     - "FactoryConstructionMenuUI - Title" → drag panela FactoryConstructionMenuUIPanel - Window
///   (každé nové okno môže tento skript ďalej znovupoužiť bez úprav)
///
///   Do poľa "Target Rect Transform" priraď RectTransform koreňového panela
///   okna, ktorý sa má posúvať. Ak pole ostane prázdne, skript skúsi
///   automaticky nájsť RectTransform v rodičovi (transform.parent).
///
/// SPRÁVANIE:
///   - Drag funguje LEN keď hráč zatlačí myš v ploche titulku (a jeho
///     vizuálnych children, ako Image titulku alebo Text titulku).
///   - Drag NEFUNGUJE keď hráč zatlačí myš v ploche Content sekcie alebo
///     iných častí okna – tie nie sú children titulku, takže pointer event
///     k tomuto skriptu nedôjde.
///   - Close button (X) ako child titulku NEPREVZNIKÁ drag, lebo Button
///     konzumuje pointer event pre svoju klik akciu. Dragging nad jeho
///     plochou jednoducho nezačne; mimo neho – v rámci titulku – áno.
///
/// POZNÁMKA K RAYCAST-u:
///   Aby tento skript zachytával pointer events, musí mať titulok aspoň
///   jeden Graphic komponent (Image alebo TextMeshProUGUI) so zapnutým
///   Raycast Target = true v jeho podstrome alebo priamo na ňom. V tomto
///   projekte to spĺňajú:
///     - "RailConstructionUIImage - Title"
///     - "RoadConstructionUIImage - Title"
///     - "DepotConstructionUIImage - Title"
///     - "FactoryConstructionUIImage - Title"
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class WindowDragHandle : MonoBehaviour, IDragHandler
{
    [Tooltip("RectTransform okna, ktoré sa má posúvať pri ťahaní za titulok. " +
             "Ak nie je priradené, použije sa RectTransform rodiča.")]
    [SerializeField] private RectTransform targetRectTransform;

    void Awake()
    {
        // Ak používateľ v Inspectore nepriradil target, skús automaticky
        // detekovať: drag-uj rodiča titulku (typicky root panel okna).
        if (targetRectTransform == null && transform.parent != null)
        {
            targetRectTransform = transform.parent as RectTransform;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (targetRectTransform != null)
            targetRectTransform.anchoredPosition += eventData.delta;
    }
}
