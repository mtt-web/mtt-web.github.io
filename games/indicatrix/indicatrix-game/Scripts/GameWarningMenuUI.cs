using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameWarningMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// Analógia k InGameMenuUI – obsluhuje kliky tlačidiel v CONTENT sekcii
/// okna "GameWarningUIPanel - Window" (univerzálne potvrdzovacie okno
/// s otázkou a tlačidlami No / Yes).
///
/// Okno samo o sebe NEVIE, čo sa má po potvrdení stať. Volajúci (napr.
/// DepotRailConstructionMenuUI / DepotRoadConstructionMenuUI) ho otvorí cez
/// Open(...) a odovzdá akciu, ktorá sa vykoná po kliknutí na Yes:
///
///   gameWarningMenuUI.Open("Delete all routes?", () => DeleteAll(), ownerPanel);
///
/// Okno obsahuje (v "GameWarningUI - Content"):
///   - WarningText       → text otázky (TMP_Text)
///   - GWNoGameButton    → zavrie okno, nestane sa nič
///   - GWYesGameButton   → zavrie okno a vykoná odovzdanú akciu
///
/// Close (X) tlačidlo "GWCloseWindowButton" v titulku NIE JE súčasťou tohto
/// skriptu – rieši ho GameWarningUIwindow (rovnako ako pri InGameMenuUI má
/// Close na starosti InGameMenuUIwindow). X sa správa rovnako ako No –
/// odložená akcia sa pri skrytí okna zahodí (OnDisable).
///
/// OWNER (voliteľný):
///   Ak sa pri Open(...) odovzdá GameObject "owner" (napr. panel depa),
///   varovné okno sa samo zavrie (ako No), keď sa owner skryje – napr. hráč
///   zatvorí okno depa cez X alebo ESC. Potvrdenie tak nikdy "neprežije"
///   okno, z ktorého bolo vyvolané.
///
/// POZN.: Drag-and-drop pohyb okna rieši samostatný WindowDragHandle pripojený
/// na "GameWarningUI - Title" (drag-uje root panel "GameWarningUIPanel - Window").
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class GameWarningMenuUI : MonoBehaviour
{
    [Header("Root Panel")]
    [Tooltip("Root panel 'GameWarningUIPanel - Window'. Ak ostane prázdny, " +
             "použije sa tento gameObject (skript je na root paneli).")]
    [SerializeField] private GameObject gameWarningUIPanel;

    [Header("Game Warning – Content")]
    [Tooltip("WarningText – text otázky. Ak Open(...) dostane prázdnu správu, " +
             "ponechá sa text nastavený v Inspectore.")]
    [SerializeField] private TMP_Text warningText;

    [Tooltip("GWNoGameButton – zavrie okno, nestane sa nič.")]
    [SerializeField] private Button gameWarningNoButton;

    [Tooltip("GWYesGameButton – zavrie okno a vykoná potvrdzovanú akciu.")]
    [SerializeField] private Button gameWarningYesButton;

    // Akcia, ktorá sa vykoná po kliknutí na Yes.
    private Action _pendingYesAction;

    // Okno, z ktorého bolo varovanie vyvolané (voliteľné).
    private GameObject _owner;
    private bool _hasOwner;

    // True, ak už niekto zavolal Open() – Start() potom panel neskryje.
    private bool _openRequested;

    private GameObject Panel => gameWarningUIPanel != null ? gameWarningUIPanel : gameObject;

    /// <summary>True, ak je varovné okno práve zobrazené.</summary>
    public bool IsOpen => Panel.activeSelf;

    void Awake()
    {
        if (gameWarningNoButton != null)
            gameWarningNoButton.onClick.AddListener(OnNoButtonClick);

        if (gameWarningYesButton != null)
            gameWarningYesButton.onClick.AddListener(OnYesButtonClick);
    }

    void Start()
    {
        // Rovnaký vzor ako Depot*ConstructionMenuUI: panel skryjeme až v Start(),
        // keď už zbehli všetky Awake() (aj GameWarningUIwindow – Close listener).
        //
        // Poistka: ak je panel v scéne pri štarte neaktívny, Awake/Start zbehnú
        // až pri prvom Open(). Vtedy ho tu NESMIEME hneď skryť.
        if (!_openRequested)
            Panel.SetActive(false);
    }

    void Update()
    {
        if (!Panel.activeSelf) return;

        // Owner (okno depa) bol zatvorený → varovanie zrušíme ako No.
        if (_hasOwner && (_owner == null || !_owner.activeInHierarchy))
            Close();
    }

    void OnDisable()
    {
        // Okno sa skrylo akýmkoľvek spôsobom (No, X, deaktivácia rodiča ...)
        // → odloženú akciu zahodíme, aby sa nikdy nevykonala omylom.
        ClearPending();
    }

    // =====================================================================
    // PUBLIC API
    // =====================================================================

    /// <summary>
    /// Zobrazí varovné okno.
    /// </summary>
    /// <param name="message">Text otázky. Null/prázdne = ponechá text z Inspectora.</param>
    /// <param name="onYes">Akcia vykonaná po kliknutí na Yes.</param>
    /// <param name="owner">Voliteľné okno, pri ktorého zatvorení sa varovanie zruší.</param>
    public void Open(string message, Action onYes, GameObject owner = null)
    {
        _openRequested = true;

        _pendingYesAction = onYes;
        _owner = owner;
        _hasOwner = owner != null;

        if (warningText != null && !string.IsNullOrEmpty(message))
            warningText.text = message;

        GameObject panel = Panel;

        // Varovanie musí byť NAD oknom, z ktorého bolo vyvolané.
        panel.transform.SetAsLastSibling();
        panel.SetActive(true);
    }

    /// <summary>
    /// Zavrie okno bez vykonania akcie (rovnaké ako No).
    /// </summary>
    public void Close()
    {
        ClearPending();
        Panel.SetActive(false);
    }

    /// <summary>
    /// Zavrie okno (ako No) len vtedy, ak bolo vyvolané z daného ownera.
    /// Používa sa napr. keď okno depa prepne na iné depo.
    /// </summary>
    public void CloseIfOwnedBy(GameObject owner)
    {
        if (owner != null && _hasOwner && _owner == owner && Panel.activeSelf)
            Close();
    }

    // =====================================================================
    // BUTTON HANDLERS
    // =====================================================================

    private void OnNoButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();
        Close();
    }

    private void OnYesButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        // Akciu si odložíme PRED zatvorením (Close ju vymaže) a vykonáme ju
        // až po skrytí okna – ak by akcia vyhodila výnimku, okno ostane zavreté.
        Action action = _pendingYesAction;
        Close();
        action?.Invoke();
    }

    private void ClearPending()
    {
        _pendingYesAction = null;
        _owner = null;
        _hasOwner = false;
    }
}
