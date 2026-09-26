using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// FactoryConstructionMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// Analógia k RoadConstructionMenuUI / RailConstructionMenuUI – obsluhuje
/// kliky tlačidiel pre stavbu tovární a prepína GameManager do príslušného
/// FactoryConstructionMode.
///
/// Na rozdiel od RAIL/ROAD je tu menu jednoduché – obsahuje len po jednom
/// tlačidle pre každý typ továrne (žiadne LevelUp/LevelDown, žiadne zatáčky
/// atď.). Spolu je 16 tovární rozdelených do dvoch skupín:
///
///   Factory (tileID = 4) – ťažba surovín:
///     - Coal Mine       (FCCoalMineButton)
///     - Forest          (FCForestButton)
///     - Iron Ore Mine   (FCIronOreMineButton)
///     - Gold Mine       (FCGoldMineButton)
///     - Silver Mine     (FCSilverMineButton)
///     - Farm            (FCFarmButton)
///     - Oil Wells       (FCOilWellsButton)
///
///   Processing (tileID = 5) – spracovanie surovín:
///     - Power Station       (FCPowerStationButton)
///     - Sawmill             (FCSawMillButton)
///     - Oil Refinery        (FCOilRefineryButton)
///     - Electronics Factory (FCElectronicsFactoryButton)
///     - Furniture Factory   (FCFurnitureFactoryButton)
///     - Slaughterhouse      (FCSlaughterhouseButton)
///     - Grain Factory       (FCGrainFactoryButton)
///     - Smelter             (FCSmelterButton)
///     - Glass Factory       (FCGlassFactoryButton)
///
/// Demolish nie je súčasťou tohto menu – továrne sa rušia rovnako ako
/// doteraz (napr. cez ROAD/RAIL Demolish, prípadne ho možno doplniť neskôr).
///
/// POZN.: Názvy GameObjectov v Hierarchy sú "FC..." (FCCoalMineButton,
/// FCForestButton, ...). V Inspectore stačí priradiť konkrétne Button
/// komponenty z FactoryConstructionMenuUI panelu na nižšie uvedené
/// SerializeField polia.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class FactoryConstructionMenuUI : MonoBehaviour
{
    [Header("Factory (tileID = 4) – ťažba")]
    [SerializeField] private Button factoryConstructionMenuCoalMineButton;
    [SerializeField] private Button factoryConstructionMenuForestButton;
    [SerializeField] private Button factoryConstructionMenuIronOreMineButton;
    [SerializeField] private Button factoryConstructionMenuGoldMineButton;
    [SerializeField] private Button factoryConstructionMenuSilverMineButton;
    [SerializeField] private Button factoryConstructionMenuFarmButton;
    [SerializeField] private Button factoryConstructionMenuOilWellsButton;

    [Header("Processing (tileID = 5) – spracovanie")]
    [SerializeField] private Button factoryConstructionMenuPowerStationButton;
    [SerializeField] private Button factoryConstructionMenuSawMillButton;
    [SerializeField] private Button factoryConstructionMenuOilRefineryButton;
    [SerializeField] private Button factoryConstructionMenuElectronicsFactoryButton;
    [SerializeField] private Button factoryConstructionMenuFurnitureFactoryButton;
    [SerializeField] private Button factoryConstructionMenuSlaughterhouseButton;
    [SerializeField] private Button factoryConstructionMenuGrainFactoryButton;
    [SerializeField] private Button factoryConstructionMenuSmelterButton;
    [SerializeField] private Button factoryConstructionMenuGlassFactoryButton;


    void Start()
    {
        // Factory (tileID = 4)
        factoryConstructionMenuCoalMineButton.onClick.AddListener(FactoryConstructionMenuCoalMineButtonClick);
        factoryConstructionMenuForestButton.onClick.AddListener(FactoryConstructionMenuForestButtonClick);
        factoryConstructionMenuIronOreMineButton.onClick.AddListener(FactoryConstructionMenuIronOreMineButtonClick);
        factoryConstructionMenuGoldMineButton.onClick.AddListener(FactoryConstructionMenuGoldMineButtonClick);
        factoryConstructionMenuSilverMineButton.onClick.AddListener(FactoryConstructionMenuSilverMineButtonClick);
        factoryConstructionMenuFarmButton.onClick.AddListener(FactoryConstructionMenuFarmButtonClick);
        factoryConstructionMenuOilWellsButton.onClick.AddListener(FactoryConstructionMenuOilWellsButtonClick);

        // Processing (tileID = 5)
        factoryConstructionMenuPowerStationButton.onClick.AddListener(FactoryConstructionMenuPowerStationButtonClick);
        factoryConstructionMenuSawMillButton.onClick.AddListener(FactoryConstructionMenuSawMillButtonClick);
        factoryConstructionMenuOilRefineryButton.onClick.AddListener(FactoryConstructionMenuOilRefineryButtonClick);
        factoryConstructionMenuElectronicsFactoryButton.onClick.AddListener(FactoryConstructionMenuElectronicsFactoryButtonClick);
        factoryConstructionMenuFurnitureFactoryButton.onClick.AddListener(FactoryConstructionMenuFurnitureFactoryButtonClick);
        factoryConstructionMenuSlaughterhouseButton.onClick.AddListener(FactoryConstructionMenuSlaughterhouseButtonClick);
        factoryConstructionMenuGrainFactoryButton.onClick.AddListener(FactoryConstructionMenuGrainFactoryButtonClick);
        factoryConstructionMenuSmelterButton.onClick.AddListener(FactoryConstructionMenuSmelterButtonClick);
        factoryConstructionMenuGlassFactoryButton.onClick.AddListener(FactoryConstructionMenuGlassFactoryButtonClick);
    }

    // ── Factory (tileID = 4) ──

    private void FactoryConstructionMenuCoalMineButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.CoalMine);
    }

    private void FactoryConstructionMenuForestButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.Forest);
    }

    private void FactoryConstructionMenuIronOreMineButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.IronOreMine);
    }

    private void FactoryConstructionMenuGoldMineButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.GoldMine);
    }

    private void FactoryConstructionMenuSilverMineButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.SilverMine);
    }

    private void FactoryConstructionMenuFarmButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.Farm);
    }

    private void FactoryConstructionMenuOilWellsButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.OilWells);
    }

    // ── Processing (tileID = 5) ──

    private void FactoryConstructionMenuPowerStationButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.PowerStation);
    }

    private void FactoryConstructionMenuSawMillButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.SawMill);
    }

    private void FactoryConstructionMenuOilRefineryButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.OilRefinery);
    }

    private void FactoryConstructionMenuElectronicsFactoryButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.ElectronicsFactory);
    }

    private void FactoryConstructionMenuFurnitureFactoryButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.FurnitureFactory);
    }

    private void FactoryConstructionMenuSlaughterhouseButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.Slaughterhouse);
    }

    private void FactoryConstructionMenuGrainFactoryButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.GrainFactory);
    }

    private void FactoryConstructionMenuSmelterButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.Smelter);
    }

    private void FactoryConstructionMenuGlassFactoryButtonClick()
    {
        GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.GlassFactory);
    }
}
