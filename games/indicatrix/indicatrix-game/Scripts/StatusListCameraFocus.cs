using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// StatusListCameraFocus
/// ─────────────────────────────────────────────────────────────────────────
/// Univerzálny klik-handler pre zoznamové TMP texty v Status oknách
/// (StatusStationsMenuUI, StatusTrainsMenuUI, StatusVehiclesMenuUI).
///
/// SPRÁVANIE:
///   Po 1× kliknutí na riadok zistí PORADIE riadku a z neho premiestni hlavnú
///   hernú kameru (IsometricCamera) na príslušný tile tak, aby bol približne
///   v strede záberu. Okno sa NEzatvára, nič iné sa nemení.
///
/// KĽÚČOVÉ: SÚRADNICE Z KÓDU, NIE Z TEXTU
///   Súradnice sa NEČÍTAJÚ z viditeľného textu (ten sa môže zmeniť na reálny
///   názov, napr. "Station Copenhagen West", bez súradníc). Namiesto toho si
///   menu drží zoznam súradníc (List&lt;Vector2Int&gt;) získaný z dátovej
///   vrstvy a odovzdá ho cez Bind(...). Každý riadok je obalený neviditeľným
///   TMP tagom &lt;link="i"&gt; s indexom do tohto zoznamu. Po kliku sa z linku
///   prečíta index a zo zoznamu sa vezme súradnica. Viditeľný text tým pádom
///   môže byť čokoľvek – súradnica je vždy z kódu.
///
///   Tag &lt;link&gt; je navyše odolný voči zalomeniu dlhých názvov (link drží
///   pohromade aj cez viac vizuálnych riadkov), na rozdiel od mapovania podľa
///   čísla riadku.
///
/// POŽIADAVKY NA KLIK:
///   V scéne musí byť EventSystem a Canvas s GraphicRaycaster (platí, keďže
///   projekt používa uGUI tlačidlá). raycastTarget aj richText si zapína sám.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusListCameraFocus : MonoBehaviour, IPointerClickHandler
{
    private TMP_Text tmp;

    // Index = poradie riadku (zhodné s <link="i">), hodnota = tile [x,z].
    // Neplatná/neznáma pozícia = záporná súradnica (napr. (-1,-1)).
    private List<Vector2Int> entries;

    // Kamera je v scéne jedna – cache zdieľaná medzi všetkými captionmi.
    private static IsometricCamera cachedCamera;

    /// <summary>
    /// Priradí TMP text a zoznam súradníc (index = poradie riadku). Volá menu
    /// pri každom naplnení captionu. Zapne raycastTarget aj richText.
    /// </summary>
    public void Bind(TMP_Text text, List<Vector2Int> entryCoords)
    {
        tmp = text;
        entries = entryCoords;
        if (tmp != null)
        {
            tmp.raycastTarget = true;
            tmp.richText = true;
        }
    }

    void Awake()
    {
        if (tmp == null) tmp = GetComponent<TMP_Text>();
        if (tmp != null)
        {
            tmp.raycastTarget = true;
            tmp.richText = true;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (tmp == null) tmp = GetComponent<TMP_Text>();
        if (tmp == null || entries == null || entries.Count == 0) return;

        // Pre Screen Space – Overlay je pressEventCamera null (správne pre TMP
        // utility); pre Camera/World canvas vráti UI kameru.
        Camera uiCam = eventData.pressEventCamera;

        int idx = ResolveEntryIndex(eventData.position, uiCam);
        if (idx < 0 || idx >= entries.Count) return;

        Vector2Int tile = entries[idx];
        if (tile.x < 0 || tile.y < 0) return; // neznáma pozícia – nič nerobíme

        IsometricCamera cam = ResolveCamera();
        if (cam != null)
            cam.CenterOnTile(tile.x, tile.y);
    }

    /// <summary>
    /// Zistí index riadku, na ktorý hráč klikol. Primárne cez &lt;link="i"&gt;
    /// (odolné voči zalomeniu); ak link nie je, fallback na index vizuálneho
    /// riadku.
    /// </summary>
    private int ResolveEntryIndex(Vector2 screenPos, Camera uiCam)
    {
        tmp.ForceMeshUpdate();

        int linkIndex = TMP_TextUtilities.FindIntersectingLink(tmp, screenPos, uiCam);
        if (linkIndex >= 0 && linkIndex < tmp.textInfo.linkCount)
        {
            string id = tmp.textInfo.linkInfo[linkIndex].GetLinkID();
            if (int.TryParse(id, out int i)) return i;
        }

        int lineIndex = TMP_TextUtilities.FindIntersectingLine(tmp, screenPos, uiCam);
        if (lineIndex >= 0) return lineIndex;

        return -1;
    }

    private IsometricCamera ResolveCamera()
    {
        if (cachedCamera != null) return cachedCamera;
        cachedCamera = FindFirstObjectByType<IsometricCamera>();
        return cachedCamera;
    }
}
