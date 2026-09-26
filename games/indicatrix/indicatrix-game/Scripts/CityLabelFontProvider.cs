using UnityEngine;
using TMPro;

/// <summary>
/// CityLabelFontProvider
/// ─────────────────────────────────────────────────────────────────────────
/// Umožňuje nastaviť font pre VŠETKY CityLabel-y (mestá, stanice, rozostavané
/// továrne...) cez Inspector, bez zásahu do kódu.
///
/// Použitie:
///   1. Pridaj tento komponent na ľubovoľný GameObject v scéne
///      (napr. na ten istý objekt ako CityManager).
///   2. Do poľa „Font“ pretiahni TMP Font Asset (SDF).
///      (Z .ttf/.otf ho vytvoríš: Window → TextMeshPro → Font Asset Creator,
///       alebo pravý klik na font → Create → TextMeshPro → Font Asset.)
///
/// Beží skoro (DefaultExecutionOrder -1000), aby bol font nastavený skôr, než
/// managery začnú vytvárať labely. CityLabel si provider aj tak dohľadá sám,
/// ak by sa poradie nepodarilo.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public class CityLabelFontProvider : MonoBehaviour
{
    [Tooltip("TMP Font Asset použitý pre všetky CityLabel-y v scéne.")]
    [SerializeField] private TMP_FontAsset font;

    public TMP_FontAsset Font => font;

    void Awake()
    {
        Register();
    }

    void OnEnable()
    {
        Register();
    }

    void OnDestroy()
    {
        // Pri zmene scény nenechávame font „visieť“ – nová scéna môže mať iný.
        if (CityLabel.GlobalFont == font) CityLabel.GlobalFont = null;
    }

    /// <summary>Zapíše font z Inspectora ako globálny font pre CityLabel.</summary>
    public void Register()
    {
        if (font != null) CityLabel.GlobalFont = font;
    }
}
