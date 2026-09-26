using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TileModelLibrary
/// ─────────────────────────────────────────────────────────────────────────
/// KNIŽNICA 3D MODELOV / SPRITOV pre konštrukčné dlaždice (tiles) – voliteľná.
/// Analógia k TrainModelLibrary / VehicleModelLibrary, ale pre tile systém
/// (IndicatrixAPI.cs).
///
/// Tento komponent drží VOLITEĽNÉ prefab-y skutočných 3D modelov (alebo
/// spritov zabalených v prefabe) pre KAŽDÝ konštrukčný typ dlaždice, ktorý
/// kladie tile – zvlášť pre RAIL a zvlášť pre ROAD systém. Prefab-y sa do
/// jednotlivých slotov priradia v Unity Inspectore pretiahnutím myšou
/// (drag-and-drop).
///
/// ─────────────────────────────────────────────────────────────────────────
/// LOGIKA NAHRADENIA (presne podľa zadania):
///
///   • Ak je pre daný konštrukčný typ priradený prefab (slot != None/null):
///         → IndicatrixAPI.UpdateTileMap vytvorí inštanciu tohto modelu/spritu
///           NAMIESTO pôvodnej textúrovanej dlaždice (quadu).
///
///   • Ak prefab priradený NIE JE (slot ostane prázdny = null):
///         → vykreslí sa pôvodná textúra dlaždice, presne ako doteraz.
///           Nič sa nemení.
///
/// Voľba je per-typ a úplne nezávislá: môžeš mať model len pre niektoré
/// dlaždice, zvyšok ostane na pôvodných textúrach.
///
/// ─────────────────────────────────────────────────────────────────────────
/// ORIENTÁCIA = SAMOSTATNÝ PREFAB (dôležité!):
///   Otáčanie dlaždíc v hre NIE JE povolené. Preto KAŽDÝ stranový variant má
///   VLASTNÝ slot/prefab. Napr. "StationVertical" a "StationHorizontal" sú
///   dva rôzne prefaby; rovnako 4 zatáčky, 4 depá a 4 výhybky. Orientácia je
///   zapečená priamo v prefabe – IndicatrixAPI rotáciu nikdy nemení.
///
/// MIERKA = 1 DLAŽDICA:
///   IndicatrixAPI model proporčne prispôsobí tak, aby jeho pôdorys (X×Z)
///   zaplnil práve 1 dlaždicu (1×1 svetová jednotka). Stačí teda dodať prefab
///   v ľubovoľnej rozumnej mierke; ak ho chceš mať presný, priprav ho 1×1.
///   (Auto-fit sa dá vypnúť na komponente IndicatrixAPI.)
///
/// SPRITY (2D textúry):
///   Sprite sa dodáva tiež ako GameObject prefab – stačí prefab s plochým
///   quadom alebo SpriteRenderer-om uložený v rovine XZ (lícom nahor, +Y).
///   Z pohľadu tejto knižnice je to bežný GameObject prefab.
///
/// LevelUp / LevelDown / Demolish NEMAJÚ slot:
///   LevelUp/LevelDown neukladajú dlaždicu (upravujú terén). Demolish dlaždicu
///   maže – odstránenie modelu rieši IndicatrixAPI (UpdateTileMap pri tileID 0
///   zničí čokoľvek, čo na dlaždici je, vrátane modelu).
///
/// TOVÁRNE (multi-tile footprint) – UŽ NIE 3D MODEL, ALE 2D SPRITE:
///   Továrne (Factory tileID 4 / Processing tileID 5) sú VIAC-TILE objekty
///   (footprint 2×2, 2×3, 3×3 …) a od tejto zmeny sa NEVYKRESĽUJÚ 3D modelom,
///   ale JEDNÝM 2D SPRITOM na CELÝ footprint (izometricky nakreslený obrázok
///   továrne, napr. "CoalMineUI.png").
///
///   Preto sú sloty tovární typu <see cref="Sprite"/> (nie GameObject prefab) –
///   do Inspectora sa ťahá priamo importovaný obrázok (Texture Type = Sprite).
///   Vykreslenie, mierku podľa footprintu, billboard (natočenie na kameru) a
///   Z-poradie rieši IndicatrixAPI + komponent FactorySpriteBillboard.
///
///   Prázdny slot = pôvodné textúry footprintu (Factory_Tex / Processing_Tex),
///   presne ako doteraz (spätná kompatibilita – nič sa nerozbije).
///
///   KAŽDÁ z 16 tovární má NAVYŠE vlastný blok nastavení spritu
///   (FactorySpriteSettings): mierka, zvislý posun, hĺbka. Keď niektorá továreň
///   sedí na teréne inak než ostatné, ladí sa VÝHRADNE v jej vlastnom bloku –
///   ostatných 15 to nijako neovplyvní.
///
///   RAIL a ROAD sloty ostávajú NEZMENENÉ (naďalej 3D prefaby).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class TileModelLibrary : MonoBehaviour
{
    /// <summary>
    /// Fixný 90° krok pootočenia modelu dlaždice. Používa sa pre VŠETKY TRI osi:
    ///
    ///   • Yaw   – okolo osi Y = otáčanie v rovine X-Z (pôdorys)
    ///   • Pitch – okolo osi X = otáčanie v rovine Z-Y (preklopenie dopredu/dozadu)
    ///   • Roll  – okolo osi Z = otáčanie v rovine X-Y (preklopenie doľava/doprava)
    ///
    /// Nastavuje sa RAZ pre každý slot (v Inspectore vedľa prefabu, alebo tu
    /// v kóde zmenou default hodnoty) a počas hry sa už nikdy nemení – slúži
    /// len na zrovnanie modelu, ktorý bol vyexportovaný natočený inak, než
    /// dlaždica očakáva. Herná logika ani IndicatrixAPI s ňou ďalej nepracujú.
    ///
    /// Pitch/Roll majú len dlaždice ciest a koľají. STANICE a DEPÁ (RAIL aj ROAD)
    /// a TOVÁRNE ich zámerne nemajú – tie stoja na zemi a preklopenie by im
    /// rozhodilo pôdorys aj napojenie; ostávajú len pri Yaw (rovina X-Z).
    ///
    /// 360° sa zámerne neuvádza – je totožné s 0° a v Inspectore by len mýlilo.
    /// Hodnoty enumu sú priamo stupne, takže sa dajú použiť aj ako číslo.
    /// </summary>
    public enum TileYaw
    {
        Deg0 = 0,
        Deg90 = 90,
        Deg180 = 180,
        Deg270 = 270
    }

    /// <summary>
    /// Statická inštancia pre pohodlné dohľadanie z IndicatrixAPI bez nutnosti
    /// priraďovať referenciu ručne. Prvá vytvorená inštancia "vyhrá".
    /// </summary>
    public static TileModelLibrary instance;

    // =========================================================================
    // RAIL – priame koľaje + križovatka
    // =========================================================================
    [Header("RAIL – koľaje (voliteľné, prázdne = textúra)")]
    [Tooltip("Model/sprite pre RailHorizontal. Prázdne = pôvodná textúra.")]
    [SerializeField] private GameObject railHorizontalPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railHorizontalYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railHorizontalPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railHorizontalRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RailVertical. Prázdne = pôvodná textúra.")]
    [SerializeField] private GameObject railVerticalPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railVerticalYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railVerticalPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railVerticalRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RailCrossroad. Prázdne = pôvodná textúra.")]
    [SerializeField] private GameObject railCrossroadPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railCrossroadYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railCrossroadPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railCrossroadRoll = TileYaw.Deg0;

    // =========================================================================
    // RAIL – zatáčky (4 stranové varianty)
    // =========================================================================
    [Header("RAIL – zatáčky")]
    [Tooltip("Model/sprite pre RailCurveRightBottom.")]
    [SerializeField] private GameObject railCurveRightBottomPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railCurveRightBottomYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railCurveRightBottomPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railCurveRightBottomRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RailCurveLeftBottom.")]
    [SerializeField] private GameObject railCurveLeftBottomPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railCurveLeftBottomYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railCurveLeftBottomPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railCurveLeftBottomRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RailCurveRightTop.")]
    [SerializeField] private GameObject railCurveRightTopPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railCurveRightTopYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railCurveRightTopPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railCurveRightTopRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RailCurveLeftTop.")]
    [SerializeField] private GameObject railCurveLeftTopPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railCurveLeftTopYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railCurveLeftTopPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railCurveLeftTopRoll = TileYaw.Deg0;

    // =========================================================================
    // RAIL – stanice (2 stranové varianty)
    // =========================================================================
    [Header("RAIL – stanice")]
    [Tooltip("Model/sprite pre StationHorizontal (RAIL).")]
    [SerializeField] private GameObject railStationHorizontalPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railStationHorizontalYaw = TileYaw.Deg0;
    [Tooltip("Model/sprite pre StationVertical (RAIL).")]
    [SerializeField] private GameObject railStationVerticalPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railStationVerticalYaw = TileYaw.Deg0;

    // =========================================================================
    // RAIL – depá (4 stranové varianty)
    // =========================================================================
    [Header("RAIL – depá")]
    [Tooltip("Model/sprite pre DepotHorizontalBottom (RAIL).")]
    [SerializeField] private GameObject railDepotHorizontalBottomPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railDepotHorizontalBottomYaw = TileYaw.Deg0;
    [Tooltip("Model/sprite pre DepotVerticalBottom (RAIL).")]
    [SerializeField] private GameObject railDepotVerticalBottomPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railDepotVerticalBottomYaw = TileYaw.Deg0;
    [Tooltip("Model/sprite pre DepotHorizontalTop (RAIL).")]
    [SerializeField] private GameObject railDepotHorizontalTopPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railDepotHorizontalTopYaw = TileYaw.Deg0;
    [Tooltip("Model/sprite pre DepotVerticalTop (RAIL).")]
    [SerializeField] private GameObject railDepotVerticalTopPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railDepotVerticalTopYaw = TileYaw.Deg0;

    // =========================================================================
    // RAIL – výhybky (4 stranové varianty)
    // =========================================================================
    [Header("RAIL – výhybky")]
    [Tooltip("Model/sprite pre RailSwitchHorizontalBottom.")]
    [SerializeField] private GameObject railSwitchHorizontalBottomPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railSwitchHorizontalBottomYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railSwitchHorizontalBottomPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railSwitchHorizontalBottomRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RailSwitchHorizontalTop.")]
    [SerializeField] private GameObject railSwitchHorizontalTopPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railSwitchHorizontalTopYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railSwitchHorizontalTopPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railSwitchHorizontalTopRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RailSwitchVerticalBottom.")]
    [SerializeField] private GameObject railSwitchVerticalBottomPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railSwitchVerticalBottomYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railSwitchVerticalBottomPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railSwitchVerticalBottomRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RailSwitchVerticalTop.")]
    [SerializeField] private GameObject railSwitchVerticalTopPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw railSwitchVerticalTopYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railSwitchVerticalTopPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw railSwitchVerticalTopRoll = TileYaw.Deg0;

    // =========================================================================
    // ROAD – priame cesty + križovatka
    // =========================================================================
    [Header("ROAD – cesty (voliteľné, prázdne = textúra)")]
    [Tooltip("Model/sprite pre RoadHorizontal. Prázdne = pôvodná textúra.")]
    [SerializeField] private GameObject roadHorizontalPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadHorizontalYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadHorizontalPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadHorizontalRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RoadVertical. Prázdne = pôvodná textúra.")]
    [SerializeField] private GameObject roadVerticalPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadVerticalYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadVerticalPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadVerticalRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RoadCrossroad. Prázdne = pôvodná textúra.")]
    [SerializeField] private GameObject roadCrossroadPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadCrossroadYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadCrossroadPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadCrossroadRoll = TileYaw.Deg0;

    // =========================================================================
    // ROAD – zatáčky (4 stranové varianty)
    // =========================================================================
    [Header("ROAD – zatáčky")]
    [Tooltip("Model/sprite pre RoadCurveRightBottom.")]
    [SerializeField] private GameObject roadCurveRightBottomPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadCurveRightBottomYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadCurveRightBottomPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadCurveRightBottomRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RoadCurveLeftBottom.")]
    [SerializeField] private GameObject roadCurveLeftBottomPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadCurveLeftBottomYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadCurveLeftBottomPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadCurveLeftBottomRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RoadCurveRightTop.")]
    [SerializeField] private GameObject roadCurveRightTopPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadCurveRightTopYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadCurveRightTopPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadCurveRightTopRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RoadCurveLeftTop.")]
    [SerializeField] private GameObject roadCurveLeftTopPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadCurveLeftTopYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadCurveLeftTopPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadCurveLeftTopRoll = TileYaw.Deg0;

    // =========================================================================
    // ROAD – stanice (2 stranové varianty)
    // =========================================================================
    [Header("ROAD – stanice")]
    [Tooltip("Model/sprite pre StationHorizontal (ROAD).")]
    [SerializeField] private GameObject roadStationHorizontalPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadStationHorizontalYaw = TileYaw.Deg0;
    [Tooltip("Model/sprite pre StationVertical (ROAD).")]
    [SerializeField] private GameObject roadStationVerticalPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadStationVerticalYaw = TileYaw.Deg0;

    // =========================================================================
    // ROAD – depá (4 stranové varianty)
    // =========================================================================
    [Header("ROAD – depá")]
    [Tooltip("Model/sprite pre DepotHorizontalBottom (ROAD).")]
    [SerializeField] private GameObject roadDepotHorizontalBottomPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadDepotHorizontalBottomYaw = TileYaw.Deg0;
    [Tooltip("Model/sprite pre DepotVerticalBottom (ROAD).")]
    [SerializeField] private GameObject roadDepotVerticalBottomPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadDepotVerticalBottomYaw = TileYaw.Deg0;
    [Tooltip("Model/sprite pre DepotHorizontalTop (ROAD).")]
    [SerializeField] private GameObject roadDepotHorizontalTopPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadDepotHorizontalTopYaw = TileYaw.Deg0;
    [Tooltip("Model/sprite pre DepotVerticalTop (ROAD).")]
    [SerializeField] private GameObject roadDepotVerticalTopPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadDepotVerticalTopYaw = TileYaw.Deg0;

    // =========================================================================
    // ROAD – výhybky / T-križovatky (4 stranové varianty)
    // =========================================================================
    [Header("ROAD – výhybky")]
    [Tooltip("Model/sprite pre RoadSwitchHorizontalBottom.")]
    [SerializeField] private GameObject roadSwitchHorizontalBottomPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadSwitchHorizontalBottomYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadSwitchHorizontalBottomPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadSwitchHorizontalBottomRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RoadSwitchHorizontalTop.")]
    [SerializeField] private GameObject roadSwitchHorizontalTopPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadSwitchHorizontalTopYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadSwitchHorizontalTopPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadSwitchHorizontalTopRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RoadSwitchVerticalBottom.")]
    [SerializeField] private GameObject roadSwitchVerticalBottomPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadSwitchVerticalBottomYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadSwitchVerticalBottomPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadSwitchVerticalBottomRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre RoadSwitchVerticalTop.")]
    [SerializeField] private GameObject roadSwitchVerticalTopPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw roadSwitchVerticalTopYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadSwitchVerticalTopPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw roadSwitchVerticalTopRoll = TileYaw.Deg0;

    // =========================================================================
    // ZMIEŠANÉ KRIŽOVATKY RAIL + ROAD (úrovňové prejazdy)
    // -------------------------------------------------------------------------
    // Hráč ich NEstavia cez menu – vzniknú AUTOMATICKY, keď položí priamu
    // cestu kolmo na priamu koľaj (alebo naopak):
    //   • RoadVertical na RailHorizontal / RailHorizontal na RoadVertical
    //       → RailHorizontalRoadVertical (koľaj pozdĺž X, cesta pozdĺž Z)
    //   • RoadHorizontal na RailVertical / RailVertical na RoadHorizontal
    //       → RailVerticalRoadHorizontal (koľaj pozdĺž Z, cesta pozdĺž X)
    // Slot aj pootočenie fungujú presne ako pri RailHorizontal / RailVertical.
    // Prázdny slot = textúra Resources/Textures/Rail/RailRoadCrossing_Tex
    // (ak neexistuje, Rail_Tex).
    // =========================================================================
    [Header("RAIL + ROAD – zmiešané križovatky (voliteľné, prázdne = textúra)")]
    [Tooltip("Model/sprite pre zmiešanú križovatku: koľaj HORIZONTÁLNE (os X), " +
             "cesta VERTIKÁLNE (os Z). Prázdne = pôvodná textúra.")]
    [SerializeField] private GameObject levelCrossingRailHorizontalRoadVerticalPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw levelCrossingRailHorizontalRoadVerticalYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw levelCrossingRailHorizontalRoadVerticalPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw levelCrossingRailHorizontalRoadVerticalRoll = TileYaw.Deg0;
    [Tooltip("Model/sprite pre zmiešanú križovatku: koľaj VERTIKÁLNE (os Z), " +
             "cesta HORIZONTÁLNE (os X). Prázdne = pôvodná textúra.")]
    [SerializeField] private GameObject levelCrossingRailVerticalRoadHorizontalPrefab;
    [Tooltip("Fixné pootočenie modelu okolo osi Y. Nastav raz podľa toho, "
             + "ako je model natočený v prefabe.")]
    [SerializeField] private TileYaw levelCrossingRailVerticalRoadHorizontalYaw = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw levelCrossingRailVerticalRoadHorizontalPitch = TileYaw.Deg0;
    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
    [SerializeField] private TileYaw levelCrossingRailVerticalRoadHorizontalRoll = TileYaw.Deg0;

    // =========================================================================
    // FACTORY – ťažba surovín (tileID = 4), 7 typov
    // -------------------------------------------------------------------------
    // Na rozdiel od RAIL/ROAD je továreň VIAC-TILE objekt (footprint, napr.
    // 2×2, 2×3, 3×3) a NEVYKRESĽUJE sa 3D modelom, ale JEDNÝM 2D SPRITOM na
    // celý footprint. Sprite je izometricky nakreslený obrázok továrne
    // (napr. "CoalMineUI.png").
    //
    // AKO PRIPRAVIŤ OBRÁZOK V UNITY (raz pre všetkých 16):
    //   1. Obrázok vlož do Assets (napr. Assets/Sprites/Factories/).
    //   2. V Inspectore obrázka nastav:
    //        Texture Type   = Sprite (2D and UI)
    //        Sprite Mode    = Single
    //        Pivot          = Center       (posun sa dopočíta, ale Center je najčistejší)
    //        Mesh Type      = Full Rect    (dôležité – Tight by orezal quad)
    //        Alpha Is Transparency = ✓
    //        Compression    = podľa chuti, Max Size aspoň 512
    //   3. Výsledný Sprite pretiahni myšou do príslušného slotu nižšie.
    //
    // KOMPOZÍCIA OBRÁZKA (aby sedel na footprint bez ručného doťahovania):
    //   Šírka obrázka = šírka izometrického kosoštvorca footprintu (spodná
    //   "podstava" továrne sa dotýka ľavého aj pravého okraja obrázka) a
    //   spodný vrchol kosoštvorca leží na spodnej hrane obrázka. Presne tak
    //   je urobený vzor CoalMineUI.png. Ak niektorý obrázok má okolo seba
    //   priehľadný okraj, doladí sa cez tabuľku factorySpriteTuning nižšie
    //   (scaleMultiplier / verticalOffset) – bez zásahu do kódu.
    //
    // Prázdny slot = ponechajú sa pôvodné textúry footprintu (Factory_Tex /
    // Processing_Tex), presne ako doteraz.
    // =========================================================================
    [Header("FACTORY – ťažba, 2D sprite (voliteľné, prázdne = textúra)")]
    [Tooltip("2D sprite pre Coal Mine (footprint 2×3). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite coalMineSprite;
    [Tooltip("2D sprite pre Forest (3×3). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite forestSprite;
    [Tooltip("2D sprite pre Iron Ore Mine (3×3). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite ironOreMineSprite;
    [Tooltip("2D sprite pre Gold Mine (3×3). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite goldMineSprite;
    [Tooltip("2D sprite pre Silver Mine (3×3). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite silverMineSprite;
    [Tooltip("2D sprite pre Farm (3×3). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite farmSprite;
    [Tooltip("2D sprite pre Oil Wells (3×3). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite oilWellsSprite;

    // =========================================================================
    // PROCESSING – spracovanie surovín (tileID = 5), 9 typov
    // =========================================================================
    [Header("PROCESSING – spracovanie, 2D sprite (voliteľné, prázdne = textúra)")]
    [Tooltip("2D sprite pre Power Station (2×2). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite powerStationSprite;
    [Tooltip("2D sprite pre Sawmill (2×2). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite sawMillSprite;
    [Tooltip("2D sprite pre Oil Refinery (2×2). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite oilRefinerySprite;
    [Tooltip("2D sprite pre Electronics Factory (3×3). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite electronicsFactorySprite;
    [Tooltip("2D sprite pre Furniture Factory (3×3). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite furnitureFactorySprite;
    [Tooltip("2D sprite pre Slaughterhouse (2×2). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite slaughterhouseSprite;
    [Tooltip("2D sprite pre Grain Factory (2×3). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite grainFactorySprite;
    [Tooltip("2D sprite pre Smelter (2×3). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite smelterSprite;
    [Tooltip("2D sprite pre Glass Factory (2×2). Prázdne = pôvodná textúra.")]
    [SerializeField] private Sprite glassFactorySprite;

    // =========================================================================
    // NASTAVENIA SPRITU – SAMOSTATNE PRE KAŽDÚ Z 16 TOVÁRNÍ
    // -------------------------------------------------------------------------
    // 16 obrázkov od rôznych autorov nemá rovnaké okraje ani rovnakú kompozíciu,
    // takže KAŽDÁ továreň má vlastný blok nastavení (mierka, zvislý posun,
    // hĺbka). V Inspectore je to skladací blok pomenovaný podľa továrne.
    //
    //   widthMultiplier    – 1 = sprite presne na šírku kosoštvorca footprintu
    //   verticalOffset     – posun hore (+) / dole (−) v TILE jednotkách
    //                        ← toto použi, keď továreň "lieta" nad terénom
    //   depthAnchor        – kam sa v hĺbke položí rovina spritu (Near/Center/Far)
    //   depthBias          – jemné doladenie hĺbky
    //   sortingOrderOffset – doladenie poradia oproti ostatným továrňam
    //
    // Nastavenia spoločné pre všetky továrne (sorting layer, render queue,
    // diagnostika) sú na komponente IndicatrixAPI v bloku
    // "Továrne – spoločné nastavenia 2D spritov".
    // =========================================================================
    [Header("FACTORY – nastavenia spritu (per továreň)")]
    [SerializeField] private FactorySpriteSettings coalMineSpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings forestSpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings ironOreMineSpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings goldMineSpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings silverMineSpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings farmSpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings oilWellsSpriteSettings = new FactorySpriteSettings();

    [Header("PROCESSING – nastavenia spritu (per továreň)")]
    [SerializeField] private FactorySpriteSettings powerStationSpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings sawMillSpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings oilRefinerySpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings electronicsFactorySpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings furnitureFactorySpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings slaughterhouseSpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings grainFactorySpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings smelterSpriteSettings = new FactorySpriteSettings();
    [SerializeField] private FactorySpriteSettings glassFactorySpriteSettings = new FactorySpriteSettings();

    // =========================================================================
    // LOOKUP MAPY – kľúč = konštrukčný mód, hodnota = prefab.
    // Zostavené z pomenovaných polí vyššie. Módy bez slotu (None, LevelUp,
    // LevelDown, Demolish) v mape jednoducho nie sú → GetXxx vráti null.
    // =========================================================================

    private Dictionary<GameManager.RailConstructionMode, GameObject> _railMap;
    private Dictionary<GameManager.RoadConstructionMode, GameObject> _roadMap;
    private Dictionary<GameManager.FactoryConstructionMode, Sprite> _factorySpriteMap;
    private Dictionary<GameManager.FactoryConstructionMode, FactorySpriteSettings> _factorySpriteSettingsMap;

    // Paralelné mapy fixného pootočenia (Euler uhly v stupňoch). Zámerne
    // SAMOSTATNE od _railMap/_roadMap, aby sa nemenil typ existujúcich prefab
    // slotov – Unity by pri zmene typu poľa zahodila už priradené prefaby v scéne.
    // FACTORY tu nemá obdobu: rotáciu tovární rieši hráč (R / koliesko myši)
    // a IndicatrixAPI ju zachováva.
    private Dictionary<GameManager.RailConstructionMode, Vector3> _railEulerMap;
    private Dictionary<GameManager.RoadConstructionMode, Vector3> _roadEulerMap;

    void Awake()
    {
        if (instance == null)
            instance = this;
        else if (instance != this)
            Debug.LogWarning("[TileModelLibrary] V scéne je viac inštancií – " +
                             "ponechávam prvú. Tento komponent sa ignoruje pri auto-dohľadaní.");

        BuildMaps();
    }

    /// <summary>
    /// Naplní lookup mapy z pomenovaných serializovaných polí.
    /// Volá sa v Awake; je idempotentné a bezpečné volať aj opakovane.
    /// </summary>
    private void BuildMaps()
    {
        _railMap = new Dictionary<GameManager.RailConstructionMode, GameObject>
        {
            { GameManager.RailConstructionMode.RailHorizontal,            railHorizontalPrefab },
            { GameManager.RailConstructionMode.RailVertical,              railVerticalPrefab },
            { GameManager.RailConstructionMode.RailCrossroad,             railCrossroadPrefab },
            { GameManager.RailConstructionMode.RailCurveRightBottom,      railCurveRightBottomPrefab },
            { GameManager.RailConstructionMode.RailCurveLeftBottom,       railCurveLeftBottomPrefab },
            { GameManager.RailConstructionMode.RailCurveRightTop,         railCurveRightTopPrefab },
            { GameManager.RailConstructionMode.RailCurveLeftTop,          railCurveLeftTopPrefab },
            { GameManager.RailConstructionMode.StationHorizontal,         railStationHorizontalPrefab },
            { GameManager.RailConstructionMode.StationVertical,           railStationVerticalPrefab },
            { GameManager.RailConstructionMode.DepotHorizontalBottom,     railDepotHorizontalBottomPrefab },
            { GameManager.RailConstructionMode.DepotVerticalBottom,       railDepotVerticalBottomPrefab },
            { GameManager.RailConstructionMode.DepotHorizontalTop,        railDepotHorizontalTopPrefab },
            { GameManager.RailConstructionMode.DepotVerticalTop,          railDepotVerticalTopPrefab },
            { GameManager.RailConstructionMode.RailSwitchHorizontalBottom,railSwitchHorizontalBottomPrefab },
            { GameManager.RailConstructionMode.RailSwitchHorizontalTop,   railSwitchHorizontalTopPrefab },
            { GameManager.RailConstructionMode.RailSwitchVerticalBottom,  railSwitchVerticalBottomPrefab },
            { GameManager.RailConstructionMode.RailSwitchVerticalTop,     railSwitchVerticalTopPrefab },
        };

        _roadMap = new Dictionary<GameManager.RoadConstructionMode, GameObject>
        {
            { GameManager.RoadConstructionMode.RoadHorizontal,            roadHorizontalPrefab },
            { GameManager.RoadConstructionMode.RoadVertical,              roadVerticalPrefab },
            { GameManager.RoadConstructionMode.RoadCrossroad,             roadCrossroadPrefab },
            { GameManager.RoadConstructionMode.RoadCurveRightBottom,      roadCurveRightBottomPrefab },
            { GameManager.RoadConstructionMode.RoadCurveLeftBottom,       roadCurveLeftBottomPrefab },
            { GameManager.RoadConstructionMode.RoadCurveRightTop,         roadCurveRightTopPrefab },
            { GameManager.RoadConstructionMode.RoadCurveLeftTop,          roadCurveLeftTopPrefab },
            { GameManager.RoadConstructionMode.StationHorizontal,         roadStationHorizontalPrefab },
            { GameManager.RoadConstructionMode.StationVertical,           roadStationVerticalPrefab },
            { GameManager.RoadConstructionMode.DepotHorizontalBottom,     roadDepotHorizontalBottomPrefab },
            { GameManager.RoadConstructionMode.DepotVerticalBottom,       roadDepotVerticalBottomPrefab },
            { GameManager.RoadConstructionMode.DepotHorizontalTop,        roadDepotHorizontalTopPrefab },
            { GameManager.RoadConstructionMode.DepotVerticalTop,          roadDepotVerticalTopPrefab },
            { GameManager.RoadConstructionMode.RoadSwitchHorizontalBottom,roadSwitchHorizontalBottomPrefab },
            { GameManager.RoadConstructionMode.RoadSwitchHorizontalTop,   roadSwitchHorizontalTopPrefab },
            { GameManager.RoadConstructionMode.RoadSwitchVerticalBottom,  roadSwitchVerticalBottomPrefab },
            { GameManager.RoadConstructionMode.RoadSwitchVerticalTop,     roadSwitchVerticalTopPrefab },
        };

        _railEulerMap = new Dictionary<GameManager.RailConstructionMode, Vector3>
        {
            { GameManager.RailConstructionMode.RailHorizontal,             Euler(railHorizontalPitch, railHorizontalYaw, railHorizontalRoll) },
            { GameManager.RailConstructionMode.RailVertical,               Euler(railVerticalPitch, railVerticalYaw, railVerticalRoll) },
            { GameManager.RailConstructionMode.RailCrossroad,              Euler(railCrossroadPitch, railCrossroadYaw, railCrossroadRoll) },
            { GameManager.RailConstructionMode.RailCurveRightBottom,       Euler(railCurveRightBottomPitch, railCurveRightBottomYaw, railCurveRightBottomRoll) },
            { GameManager.RailConstructionMode.RailCurveLeftBottom,        Euler(railCurveLeftBottomPitch, railCurveLeftBottomYaw, railCurveLeftBottomRoll) },
            { GameManager.RailConstructionMode.RailCurveRightTop,          Euler(railCurveRightTopPitch, railCurveRightTopYaw, railCurveRightTopRoll) },
            { GameManager.RailConstructionMode.RailCurveLeftTop,           Euler(railCurveLeftTopPitch, railCurveLeftTopYaw, railCurveLeftTopRoll) },
            { GameManager.RailConstructionMode.StationHorizontal,          Euler(TileYaw.Deg0, railStationHorizontalYaw, TileYaw.Deg0) },
            { GameManager.RailConstructionMode.StationVertical,            Euler(TileYaw.Deg0, railStationVerticalYaw, TileYaw.Deg0) },
            { GameManager.RailConstructionMode.DepotHorizontalBottom,      Euler(TileYaw.Deg0, railDepotHorizontalBottomYaw, TileYaw.Deg0) },
            { GameManager.RailConstructionMode.DepotVerticalBottom,        Euler(TileYaw.Deg0, railDepotVerticalBottomYaw, TileYaw.Deg0) },
            { GameManager.RailConstructionMode.DepotHorizontalTop,         Euler(TileYaw.Deg0, railDepotHorizontalTopYaw, TileYaw.Deg0) },
            { GameManager.RailConstructionMode.DepotVerticalTop,           Euler(TileYaw.Deg0, railDepotVerticalTopYaw, TileYaw.Deg0) },
            { GameManager.RailConstructionMode.RailSwitchHorizontalBottom, Euler(railSwitchHorizontalBottomPitch, railSwitchHorizontalBottomYaw, railSwitchHorizontalBottomRoll) },
            { GameManager.RailConstructionMode.RailSwitchHorizontalTop,    Euler(railSwitchHorizontalTopPitch, railSwitchHorizontalTopYaw, railSwitchHorizontalTopRoll) },
            { GameManager.RailConstructionMode.RailSwitchVerticalBottom,   Euler(railSwitchVerticalBottomPitch, railSwitchVerticalBottomYaw, railSwitchVerticalBottomRoll) },
            { GameManager.RailConstructionMode.RailSwitchVerticalTop,      Euler(railSwitchVerticalTopPitch, railSwitchVerticalTopYaw, railSwitchVerticalTopRoll) },
        };

        _roadEulerMap = new Dictionary<GameManager.RoadConstructionMode, Vector3>
        {
            { GameManager.RoadConstructionMode.RoadHorizontal,             Euler(roadHorizontalPitch, roadHorizontalYaw, roadHorizontalRoll) },
            { GameManager.RoadConstructionMode.RoadVertical,               Euler(roadVerticalPitch, roadVerticalYaw, roadVerticalRoll) },
            { GameManager.RoadConstructionMode.RoadCrossroad,              Euler(roadCrossroadPitch, roadCrossroadYaw, roadCrossroadRoll) },
            { GameManager.RoadConstructionMode.RoadCurveRightBottom,       Euler(roadCurveRightBottomPitch, roadCurveRightBottomYaw, roadCurveRightBottomRoll) },
            { GameManager.RoadConstructionMode.RoadCurveLeftBottom,        Euler(roadCurveLeftBottomPitch, roadCurveLeftBottomYaw, roadCurveLeftBottomRoll) },
            { GameManager.RoadConstructionMode.RoadCurveRightTop,          Euler(roadCurveRightTopPitch, roadCurveRightTopYaw, roadCurveRightTopRoll) },
            { GameManager.RoadConstructionMode.RoadCurveLeftTop,           Euler(roadCurveLeftTopPitch, roadCurveLeftTopYaw, roadCurveLeftTopRoll) },
            { GameManager.RoadConstructionMode.StationHorizontal,          Euler(TileYaw.Deg0, roadStationHorizontalYaw, TileYaw.Deg0) },
            { GameManager.RoadConstructionMode.StationVertical,            Euler(TileYaw.Deg0, roadStationVerticalYaw, TileYaw.Deg0) },
            { GameManager.RoadConstructionMode.DepotHorizontalBottom,      Euler(TileYaw.Deg0, roadDepotHorizontalBottomYaw, TileYaw.Deg0) },
            { GameManager.RoadConstructionMode.DepotVerticalBottom,        Euler(TileYaw.Deg0, roadDepotVerticalBottomYaw, TileYaw.Deg0) },
            { GameManager.RoadConstructionMode.DepotHorizontalTop,         Euler(TileYaw.Deg0, roadDepotHorizontalTopYaw, TileYaw.Deg0) },
            { GameManager.RoadConstructionMode.DepotVerticalTop,           Euler(TileYaw.Deg0, roadDepotVerticalTopYaw, TileYaw.Deg0) },
            { GameManager.RoadConstructionMode.RoadSwitchHorizontalBottom, Euler(roadSwitchHorizontalBottomPitch, roadSwitchHorizontalBottomYaw, roadSwitchHorizontalBottomRoll) },
            { GameManager.RoadConstructionMode.RoadSwitchHorizontalTop,    Euler(roadSwitchHorizontalTopPitch, roadSwitchHorizontalTopYaw, roadSwitchHorizontalTopRoll) },
            { GameManager.RoadConstructionMode.RoadSwitchVerticalBottom,   Euler(roadSwitchVerticalBottomPitch, roadSwitchVerticalBottomYaw, roadSwitchVerticalBottomRoll) },
            { GameManager.RoadConstructionMode.RoadSwitchVerticalTop,      Euler(roadSwitchVerticalTopPitch, roadSwitchVerticalTopYaw, roadSwitchVerticalTopRoll) },
        };

        _factorySpriteMap = new Dictionary<GameManager.FactoryConstructionMode, Sprite>
        {
            // Factory (tileID 4)
            { GameManager.FactoryConstructionMode.CoalMine,           coalMineSprite },
            { GameManager.FactoryConstructionMode.Forest,             forestSprite },
            { GameManager.FactoryConstructionMode.IronOreMine,        ironOreMineSprite },
            { GameManager.FactoryConstructionMode.GoldMine,           goldMineSprite },
            { GameManager.FactoryConstructionMode.SilverMine,         silverMineSprite },
            { GameManager.FactoryConstructionMode.Farm,               farmSprite },
            { GameManager.FactoryConstructionMode.OilWells,           oilWellsSprite },
            // Processing (tileID 5)
            { GameManager.FactoryConstructionMode.PowerStation,       powerStationSprite },
            { GameManager.FactoryConstructionMode.SawMill,            sawMillSprite },
            { GameManager.FactoryConstructionMode.OilRefinery,        oilRefinerySprite },
            { GameManager.FactoryConstructionMode.ElectronicsFactory, electronicsFactorySprite },
            { GameManager.FactoryConstructionMode.FurnitureFactory,   furnitureFactorySprite },
            { GameManager.FactoryConstructionMode.Slaughterhouse,     slaughterhouseSprite },
            { GameManager.FactoryConstructionMode.GrainFactory,       grainFactorySprite },
            { GameManager.FactoryConstructionMode.Smelter,            smelterSprite },
            { GameManager.FactoryConstructionMode.GlassFactory,       glassFactorySprite },
        };

        _factorySpriteSettingsMap = new Dictionary<GameManager.FactoryConstructionMode, FactorySpriteSettings>
        {
            // Factory (tileID 4)
            { GameManager.FactoryConstructionMode.CoalMine,           coalMineSpriteSettings },
            { GameManager.FactoryConstructionMode.Forest,             forestSpriteSettings },
            { GameManager.FactoryConstructionMode.IronOreMine,        ironOreMineSpriteSettings },
            { GameManager.FactoryConstructionMode.GoldMine,           goldMineSpriteSettings },
            { GameManager.FactoryConstructionMode.SilverMine,         silverMineSpriteSettings },
            { GameManager.FactoryConstructionMode.Farm,               farmSpriteSettings },
            { GameManager.FactoryConstructionMode.OilWells,           oilWellsSpriteSettings },
            // Processing (tileID 5)
            { GameManager.FactoryConstructionMode.PowerStation,       powerStationSpriteSettings },
            { GameManager.FactoryConstructionMode.SawMill,            sawMillSpriteSettings },
            { GameManager.FactoryConstructionMode.OilRefinery,        oilRefinerySpriteSettings },
            { GameManager.FactoryConstructionMode.ElectronicsFactory, electronicsFactorySpriteSettings },
            { GameManager.FactoryConstructionMode.FurnitureFactory,   furnitureFactorySpriteSettings },
            { GameManager.FactoryConstructionMode.Slaughterhouse,     slaughterhouseSpriteSettings },
            { GameManager.FactoryConstructionMode.GrainFactory,       grainFactorySpriteSettings },
            { GameManager.FactoryConstructionMode.Smelter,            smelterSpriteSettings },
            { GameManager.FactoryConstructionMode.GlassFactory,       glassFactorySpriteSettings },
        };
    }

    /// <summary>
    /// Vráti prefab modelu/spritu pre daný RAIL konštrukčný mód, alebo null
    /// ak preň nie je priradený žiadny prefab (→ ponechá pôvodnú textúru).
    /// </summary>
    public GameObject GetRailTilePrefab(GameManager.RailConstructionMode mode)
    {
        if (_railMap == null) BuildMaps();
        return (_railMap.TryGetValue(mode, out var go)) ? go : null;
    }

    /// <summary>
    /// Vráti prefab modelu/spritu pre daný ROAD konštrukčný mód, alebo null
    /// ak preň nie je priradený žiadny prefab (→ ponechá pôvodnú textúru).
    /// </summary>
    public GameObject GetRoadTilePrefab(GameManager.RoadConstructionMode mode)
    {
        if (_roadMap == null) BuildMaps();
        return (_roadMap.TryGetValue(mode, out var go)) ? go : null;
    }

    /// <summary>
    /// Prefab zmiešanej križovatky RAIL + ROAD pre danú orientáciu, alebo null
    /// (→ textúra RailRoadCrossing_Tex / Rail_Tex).
    /// </summary>
    public GameObject GetLevelCrossingPrefab(IndicatrixAPI.LevelCrossingMode mode)
    {
        switch (mode)
        {
            case IndicatrixAPI.LevelCrossingMode.RailHorizontalRoadVertical: return levelCrossingRailHorizontalRoadVerticalPrefab;
            case IndicatrixAPI.LevelCrossingMode.RailVerticalRoadHorizontal: return levelCrossingRailVerticalRoadHorizontalPrefab;
            default: return null;
        }
    }

    /// <summary>
    /// Fixné pootočenie zmiešanej križovatky ako Euler uhly v stupňoch
    /// (x = rovina Z-Y, y = rovina X-Z, z = rovina X-Y). None → Vector3.zero.
    /// </summary>
    public Vector3 GetLevelCrossingEulerAngles(IndicatrixAPI.LevelCrossingMode mode)
    {
        switch (mode)
        {
            case IndicatrixAPI.LevelCrossingMode.RailHorizontalRoadVertical:
                return Euler(levelCrossingRailHorizontalRoadVerticalPitch,
                             levelCrossingRailHorizontalRoadVerticalYaw,
                             levelCrossingRailHorizontalRoadVerticalRoll);
            case IndicatrixAPI.LevelCrossingMode.RailVerticalRoadHorizontal:
                return Euler(levelCrossingRailVerticalRoadHorizontalPitch,
                             levelCrossingRailVerticalRoadHorizontalYaw,
                             levelCrossingRailVerticalRoadHorizontalRoll);
            default:
                return Vector3.zero;
        }
    }

    /// <summary>Zloží tri 90° kroky do vektora Euler uhlov (v stupňoch).</summary>
    private static Vector3 Euler(TileYaw pitchX, TileYaw yawY, TileYaw rollZ)
        => new Vector3((int)pitchX, (int)yawY, (int)rollZ);

    /// <summary>
    /// Fixné pootočenie pre daný RAIL mód ako Euler uhly v stupňoch
    /// (x = rovina Z-Y, y = rovina X-Z, z = rovina X-Y).
    /// Neznámy mód alebo nenastavený slot → Vector3.zero.
    /// </summary>
    public Vector3 GetRailTileEulerAngles(GameManager.RailConstructionMode mode)
    {
        if (_railEulerMap == null) BuildMaps();
        return (_railEulerMap.TryGetValue(mode, out var e)) ? e : Vector3.zero;
    }

    /// <summary>
    /// Fixné pootočenie pre daný ROAD mód ako Euler uhly v stupňoch
    /// (x = rovina Z-Y, y = rovina X-Z, z = rovina X-Y).
    /// Neznámy mód alebo nenastavený slot → Vector3.zero.
    /// </summary>
    public Vector3 GetRoadTileEulerAngles(GameManager.RoadConstructionMode mode)
    {
        if (_roadEulerMap == null) BuildMaps();
        return (_roadEulerMap.TryGetValue(mode, out var e)) ? e : Vector3.zero;
    }

    /// <summary>
    /// Fixné pootočenie (v stupňoch okolo osi Y) pre daný RAIL mód.
    /// Neznámy mód alebo nenastavený slot → 0.
    /// </summary>
    public float GetRailTileYawDegrees(GameManager.RailConstructionMode mode)
        => GetRailTileEulerAngles(mode).y;

    /// <summary>
    /// Fixné pootočenie (v stupňoch okolo osi Y) pre daný ROAD mód.
    /// Neznámy mód alebo nenastavený slot → 0.
    /// </summary>
    public float GetRoadTileYawDegrees(GameManager.RoadConstructionMode mode)
        => GetRoadTileEulerAngles(mode).y;

    /// <summary>Pohodlný obal – rovno ako Quaternion pripravený na použitie.</summary>
    public Quaternion GetRailTileRotation(GameManager.RailConstructionMode mode)
        => Quaternion.Euler(GetRailTileEulerAngles(mode));

    /// <summary>Pohodlný obal – rovno ako Quaternion pripravený na použitie.</summary>
    public Quaternion GetRoadTileRotation(GameManager.RoadConstructionMode mode)
        => Quaternion.Euler(GetRoadTileEulerAngles(mode));

    /// <summary>
    /// Vráti 2D SPRITE pre daný FACTORY/PROCESSING typ továrne, alebo null ak
    /// preň nie je priradený žiadny sprite (→ ponechá pôvodné textúry
    /// footprintu). Jeden sprite = celá továreň (celý footprint); mierku podľa
    /// footprintu, billboard a Z-poradie rieši IndicatrixAPI cez komponent
    /// FactorySpriteBillboard.
    /// </summary>
    public Sprite GetFactorySprite(GameManager.FactoryConstructionMode mode)
    {
        if (_factorySpriteMap == null) BuildMaps();
        return (_factorySpriteMap.TryGetValue(mode, out var sp)) ? sp : null;
    }

    /// <summary>
    /// Vráti nastavenia spritu pre KONKRÉTNY typ továrne (mierka, zvislý posun,
    /// hĺbka). Nikdy nevráti null – ak by sa typ v mape nenašiel, vráti
    /// neutrálne hodnoty, aby volajúci nemusel riešiť null.
    /// </summary>
    public FactorySpriteSettings GetFactorySpriteSettings(GameManager.FactoryConstructionMode mode)
    {
        if (_factorySpriteSettingsMap == null) BuildMaps();

        if (_factorySpriteSettingsMap.TryGetValue(mode, out var st) && st != null)
            return st;

        return FactorySpriteSettings.Default;
    }

    /// <summary>
    /// Opraví nezmyselné hodnoty vo VŠETKÝCH 16 blokoch nastavení.
    ///
    /// PREČO: keď Unity pridá do už existujúceho komponentu v scéne NOVÉ
    /// serializované pole, naplní ho nulami. widthMultiplier = 0 by znamenal
    /// sprite so šírkou 0, teda NEVIDITEĽNÚ továreň. OnValidate to opraví hneď,
    /// ako sa komponent zobrazí v Inspectore, takže sa to nedá prehliadnuť.
    /// </summary>
    void OnValidate()
    {
        NormalizeSettings(coalMineSpriteSettings);
        NormalizeSettings(forestSpriteSettings);
        NormalizeSettings(ironOreMineSpriteSettings);
        NormalizeSettings(goldMineSpriteSettings);
        NormalizeSettings(silverMineSpriteSettings);
        NormalizeSettings(farmSpriteSettings);
        NormalizeSettings(oilWellsSpriteSettings);

        NormalizeSettings(powerStationSpriteSettings);
        NormalizeSettings(sawMillSpriteSettings);
        NormalizeSettings(oilRefinerySpriteSettings);
        NormalizeSettings(electronicsFactorySpriteSettings);
        NormalizeSettings(furnitureFactorySpriteSettings);
        NormalizeSettings(slaughterhouseSpriteSettings);
        NormalizeSettings(grainFactorySpriteSettings);
        NormalizeSettings(smelterSpriteSettings);
        NormalizeSettings(glassFactorySpriteSettings);
    }

    private static void NormalizeSettings(FactorySpriteSettings st)
    {
        if (st != null) st.Normalize();
    }
}
