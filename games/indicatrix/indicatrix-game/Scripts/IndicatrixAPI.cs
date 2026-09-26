using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem.HID;
using UnityEngine.Splines;


public class IndicatrixAPI : MonoBehaviour
{
    public static IndicatrixAPI instance;

    // =========================
    // DIRECTION MASK
    // =========================

    /// <summary>
    /// Smery, ktorými môže koľaj/dlaždica prepojiť susedov.
    /// Konvencia (zhodná s A* DIRECTIONS v TrainSystem):
    ///   Right  = +X (sused na východ)
    ///   Left   = -X (sused na západ)
    ///   Top    = +Z (sused na sever)
    ///   Bottom = -Z (sused na juh)
    /// </summary>
    [Flags]
    public enum DirectionMask
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8
    }

    /// <summary>
    /// Vráti opačný smer ku zadanému jednoduchému smeru.
    /// Pre kombinovaný mask vracia DirectionMask.None.
    /// </summary>
    public static DirectionMask Opposite(DirectionMask d)
    {
        switch (d)
        {
            case DirectionMask.Left: return DirectionMask.Right;
            case DirectionMask.Right: return DirectionMask.Left;
            case DirectionMask.Top: return DirectionMask.Bottom;
            case DirectionMask.Bottom: return DirectionMask.Top;
            default: return DirectionMask.None;
        }
    }

    // =========================
    // TILE CATEGORY (RAIL vs ROAD)
    // =========================

    /// <summary>
    /// Kategória dlaždice – odlišuje železničné a cestné prvky pri rovnakom
    /// tileID. Železnice a cesty zdieľajú jeden tile grid, ale TrainSystem
    /// si vyberá iba kategóriu Rail a (budúci) RoadSystem iba kategóriu Road.
    /// </summary>
    public enum TileCategory
    {
        None = 0,
        Rail = 1,
        Road = 2,
        Factory = 3,  // Spoločná kategória pre Factory (tileID 4) aj Processing (tileID 5)

        /// <summary>
        /// ZMIEŠANÁ KRIŽOVATKA železnice a cesty (úrovňový prejazd). tileID = 1,
        /// stateID = (int)LevelCrossingMode. Vzniká AUTOMATICKY, keď hráč položí
        /// RoadVertical na RailHorizontal (alebo naopak / RailVertical × RoadHorizontal).
        /// TrainSystem cez GetTileByIndex vidí LEN priamu koľaj, VehicleSystem cez
        /// GetRoadTileByIndex LEN priamu cestu – každý systém prejde krížom po svojej osi.
        /// </summary>
        RailRoadCrossing = 4
    }

    /// <summary>
    /// Orientácia zmiešanej križovatky (stateID pri TileCategory.RailRoadCrossing).
    /// </summary>
    public enum LevelCrossingMode
    {
        None = 0,
        /// <summary>Koľaj pozdĺž X (Left↔Right), cesta pozdĺž Z (Bottom↔Top).</summary>
        RailHorizontalRoadVertical = 1,
        /// <summary>Koľaj pozdĺž Z (Bottom↔Top), cesta pozdĺž X (Left↔Right).</summary>
        RailVerticalRoadHorizontal = 2
    }

    /// <summary>
    /// Mapuje RailConstructionMode (uložený ako stateID) na DirectionMask.
    /// Pre režimy, ktoré nie sú koľajové (None, LevelUp, ...) vracia None.
    /// </summary>
    public static DirectionMask GetConnectionsForMode(GameManager.RailConstructionMode mode)
    {
        switch (mode)
        {
            // Priame koľaje
            case GameManager.RailConstructionMode.RailHorizontal:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RailConstructionMode.RailVertical:
                return DirectionMask.Top | DirectionMask.Bottom;

            // Križovatka
            case GameManager.RailConstructionMode.RailCrossroad:
                return DirectionMask.Left | DirectionMask.Right | DirectionMask.Top | DirectionMask.Bottom;

            // Krivky (uhlové, ostré – bez vyhladenia)
            case GameManager.RailConstructionMode.RailCurveRightBottom:
                return DirectionMask.Right | DirectionMask.Bottom;
            case GameManager.RailConstructionMode.RailCurveLeftBottom:
                return DirectionMask.Left | DirectionMask.Bottom;
            case GameManager.RailConstructionMode.RailCurveRightTop:
                return DirectionMask.Right | DirectionMask.Top;
            case GameManager.RailConstructionMode.RailCurveLeftTop:
                return DirectionMask.Left | DirectionMask.Top;

            // Stanice
            case GameManager.RailConstructionMode.StationHorizontal:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RailConstructionMode.StationVertical:
                return DirectionMask.Top | DirectionMask.Bottom;

            // Depá
            case GameManager.RailConstructionMode.DepotHorizontalBottom:
            case GameManager.RailConstructionMode.DepotHorizontalTop:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RailConstructionMode.DepotVerticalBottom:
            case GameManager.RailConstructionMode.DepotVerticalTop:
                return DirectionMask.Top | DirectionMask.Bottom;

            // Výhybky (3-cestné, plne obojsmerné).
            // Každá výhybka má 1 priamy smer (Left↔Right alebo Top↔Bottom)
            // a 1 odbočný smer (kolmý). Všetky 3 spojenia môžu prechádzať
            // medzi sebou navzájom (žiadne asymetrické turnout).
            case GameManager.RailConstructionMode.RailSwitchHorizontalBottom:
                return DirectionMask.Left | DirectionMask.Right | DirectionMask.Bottom;
            case GameManager.RailConstructionMode.RailSwitchHorizontalTop:
                return DirectionMask.Left | DirectionMask.Right | DirectionMask.Top;
            case GameManager.RailConstructionMode.RailSwitchVerticalBottom:
                return DirectionMask.Top | DirectionMask.Bottom | DirectionMask.Right;
            case GameManager.RailConstructionMode.RailSwitchVerticalTop:
                return DirectionMask.Top | DirectionMask.Bottom | DirectionMask.Left;

            // Hlavy MOSTOV a TUNELOV (RailCrossingSystem). Navonok sa správajú
            // ako rovná koľaj – na vonkajšiu stranu sa napojí akákoľvek koľaj,
            // zákruta, výhybka, stanica či depo. Stranu SMEROM DO prechodu
            // TrainSystem nahradí skokom na partnerskú hlavu (A*).
            case GameManager.RailConstructionMode.TunnelHorizontal:
            case GameManager.RailConstructionMode.BridgeHorizontal:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RailConstructionMode.TunnelVertical:
            case GameManager.RailConstructionMode.BridgeVertical:
                return DirectionMask.Top | DirectionMask.Bottom;

            default:
                return DirectionMask.None;
        }
    }

    /// <summary>
    /// Mapuje RoadConstructionMode (uložený ako stateID) na DirectionMask.
    /// Úplne analogické k RAIL verzii vyššie.
    /// Pre režimy, ktoré nie sú cestné (None, LevelUp, ...) vracia None.
    /// </summary>
    public static DirectionMask GetConnectionsForMode(GameManager.RoadConstructionMode mode)
    {
        switch (mode)
        {
            // Priame cesty
            case GameManager.RoadConstructionMode.RoadHorizontal:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RoadConstructionMode.RoadVertical:
                return DirectionMask.Top | DirectionMask.Bottom;

            // Križovatka
            case GameManager.RoadConstructionMode.RoadCrossroad:
                return DirectionMask.Left | DirectionMask.Right | DirectionMask.Top | DirectionMask.Bottom;

            // Krivky
            case GameManager.RoadConstructionMode.RoadCurveRightBottom:
                return DirectionMask.Right | DirectionMask.Bottom;
            case GameManager.RoadConstructionMode.RoadCurveLeftBottom:
                return DirectionMask.Left | DirectionMask.Bottom;
            case GameManager.RoadConstructionMode.RoadCurveRightTop:
                return DirectionMask.Right | DirectionMask.Top;
            case GameManager.RoadConstructionMode.RoadCurveLeftTop:
                return DirectionMask.Left | DirectionMask.Top;

            // Stanice (autobusové / nákladné zastávky)
            case GameManager.RoadConstructionMode.StationHorizontal:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RoadConstructionMode.StationVertical:
                return DirectionMask.Top | DirectionMask.Bottom;

            // Depá (garáže)
            case GameManager.RoadConstructionMode.DepotHorizontalBottom:
            case GameManager.RoadConstructionMode.DepotHorizontalTop:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RoadConstructionMode.DepotVerticalBottom:
            case GameManager.RoadConstructionMode.DepotVerticalTop:
                return DirectionMask.Top | DirectionMask.Bottom;

            // T-križovatky / Y-rozdvojenia (3-cestné, plne obojsmerné)
            case GameManager.RoadConstructionMode.RoadSwitchHorizontalBottom:
                return DirectionMask.Left | DirectionMask.Right | DirectionMask.Bottom;
            case GameManager.RoadConstructionMode.RoadSwitchHorizontalTop:
                return DirectionMask.Left | DirectionMask.Right | DirectionMask.Top;
            case GameManager.RoadConstructionMode.RoadSwitchVerticalBottom:
                return DirectionMask.Top | DirectionMask.Bottom | DirectionMask.Right;
            case GameManager.RoadConstructionMode.RoadSwitchVerticalTop:
                return DirectionMask.Top | DirectionMask.Bottom | DirectionMask.Left;

            // Hlavy CESTNÝCH MOSTOV a TUNELOV (RoadCrossingSystem) – analógia
            // k RAIL. Navonok rovná cesta; stranu smerom DO prechodu VehicleSystem
            // nahradí skokom na partnerskú hlavu (A*).
            case GameManager.RoadConstructionMode.TunnelHorizontal:
            case GameManager.RoadConstructionMode.BridgeHorizontal:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RoadConstructionMode.TunnelVertical:
            case GameManager.RoadConstructionMode.BridgeVertical:
                return DirectionMask.Top | DirectionMask.Bottom;

            default:
                return DirectionMask.None;
        }
    }

    /// <summary>
    /// Mapuje FactoryConstructionMode na DirectionMask.
    ///
    /// POZN.: Továrne (Factory / Processing) NIE SÚ dopravný graf – nemajú
    /// koľajové/cestné prepojenia so susedmi. Sú to staticky umiestnené
    /// viac-tile objekty (footprint). Preto vždy vraciame DirectionMask.None.
    /// Toto preťaženie existuje hlavne kvôli konzistentnosti API (Load,
    /// SetTile, TileData konštruktor) – rovnako ako pri RAIL/ROAD.
    /// </summary>
    public static DirectionMask GetConnectionsForMode(GameManager.FactoryConstructionMode mode)
    {
        // Továrne nemajú smerové spojenia.
        return DirectionMask.None;
    }

    // =========================
    // ZMIEŠANÁ KRIŽOVATKA RAIL + ROAD
    // =========================

    /// <summary>
    /// Surové spojenia zmiešanej križovatky = zjednotenie oboch osí (4 smery).
    /// Dopravné systémy ich NEČÍTAJÚ priamo – dostanú len svoju os cez
    /// RailViewOf / RoadViewOf.
    /// </summary>
    public static DirectionMask GetConnectionsForMode(LevelCrossingMode mode)
    {
        return mode == LevelCrossingMode.None
            ? DirectionMask.None
            : DirectionMask.Left | DirectionMask.Right | DirectionMask.Top | DirectionMask.Bottom;
    }

    /// <summary>
    /// ŽELEZNIČNÝ pohľad na tile: zmiešaná križovatka sa javí ako priama koľaj
    /// (RailHorizontal / RailVertical). Ostatné tily bez zmeny.
    /// </summary>
    public static TileData RailViewOf(TileData td)
    {
        if (td.category != TileCategory.RailRoadCrossing) return td;

        var rail = (LevelCrossingMode)td.stateID == LevelCrossingMode.RailVerticalRoadHorizontal
            ? GameManager.RailConstructionMode.RailVertical
            : GameManager.RailConstructionMode.RailHorizontal;
        return new TileData(1, (int)rail, GetConnectionsForMode(rail), TileCategory.Rail);
    }

    /// <summary>
    /// CESTNÝ pohľad na tile: zmiešaná križovatka sa javí ako priama cesta
    /// (RoadVertical / RoadHorizontal). Ostatné tily bez zmeny.
    /// </summary>
    public static TileData RoadViewOf(TileData td)
    {
        if (td.category != TileCategory.RailRoadCrossing) return td;

        var road = (LevelCrossingMode)td.stateID == LevelCrossingMode.RailVerticalRoadHorizontal
            ? GameManager.RoadConstructionMode.RoadHorizontal
            : GameManager.RoadConstructionMode.RoadVertical;
        return new TileData(1, (int)road, GetConnectionsForMode(road), TileCategory.Road);
    }

    /// <summary>
    /// Vznikne zmiešaná križovatka, ak sa RAIL diel <paramref name="railMode"/>
    /// položí na existujúci tile <paramref name="existing"/>? Len priama koľaj
    /// KOLMO na priamu cestu. Inak None.
    /// </summary>
    public static LevelCrossingMode LevelCrossingForRailOnRoad(GameManager.RailConstructionMode railMode, TileData existing)
    {
        if (existing.category != TileCategory.Road || existing.tileID != 1) return LevelCrossingMode.None;
        var road = (GameManager.RoadConstructionMode)existing.stateID;

        if (railMode == GameManager.RailConstructionMode.RailHorizontal && road == GameManager.RoadConstructionMode.RoadVertical)
            return LevelCrossingMode.RailHorizontalRoadVertical;
        if (railMode == GameManager.RailConstructionMode.RailVertical && road == GameManager.RoadConstructionMode.RoadHorizontal)
            return LevelCrossingMode.RailVerticalRoadHorizontal;
        return LevelCrossingMode.None;
    }

    /// <summary>ROAD analógia k LevelCrossingForRailOnRoad.</summary>
    public static LevelCrossingMode LevelCrossingForRoadOnRail(GameManager.RoadConstructionMode roadMode, TileData existing)
    {
        if (existing.category != TileCategory.Rail || existing.tileID != 1) return LevelCrossingMode.None;
        var rail = (GameManager.RailConstructionMode)existing.stateID;

        if (roadMode == GameManager.RoadConstructionMode.RoadVertical && rail == GameManager.RailConstructionMode.RailHorizontal)
            return LevelCrossingMode.RailHorizontalRoadVertical;
        if (roadMode == GameManager.RoadConstructionMode.RoadHorizontal && rail == GameManager.RailConstructionMode.RailVertical)
            return LevelCrossingMode.RailVerticalRoadHorizontal;
        return LevelCrossingMode.None;
    }

    /// <summary>True, ak ide o priamy RAIL alebo ROAD diel rovnobežný s <paramref name="horizontal"/>.</summary>
    public static bool IsStraightTile(TileData td, out bool horizontal)
    {
        horizontal = false;
        if (td.tileID != 1) return false;
        if (td.category == TileCategory.Rail)
        {
            var m = (GameManager.RailConstructionMode)td.stateID;
            if (m == GameManager.RailConstructionMode.RailHorizontal) { horizontal = true; return true; }
            return m == GameManager.RailConstructionMode.RailVertical;
        }
        if (td.category == TileCategory.Road)
        {
            var m = (GameManager.RoadConstructionMode)td.stateID;
            if (m == GameManager.RoadConstructionMode.RoadHorizontal) { horizontal = true; return true; }
            return m == GameManager.RoadConstructionMode.RoadVertical;
        }
        return false;
    }

    // =========================
    // TILE ENGINE
    // =========================

    [System.Serializable]
    public struct TileData
    {
        public int tileID;
        public int stateID;          // = (int)RailConstructionMode ALEBO (int)RoadConstructionMode
        public DirectionMask connections;
        public TileCategory category; // RAIL / ROAD / None

        public TileData(int t, int s, DirectionMask c, TileCategory cat)
        {
            tileID = t;
            stateID = s;
            connections = c;
            category = cat;
        }

        // Pôvodný konštruktor (3 parametre) – pre spätnú kompatibilitu predpokladá
        // RAIL kategóriu (pretože pred zavedením ROAD bolo všetko rail).
        public TileData(int t, int s, DirectionMask c)
        {
            tileID = t;
            stateID = s;
            connections = c;
            category = (t == 0) ? TileCategory.None : TileCategory.Rail;
        }

        // Spätne kompatibilný konštruktor – connections sa odvodí zo stateID.
        // Predpokladá RAIL kategóriu (zachovanie pôvodného správania).
        public TileData(int t, int s)
        {
            tileID = t;
            stateID = s;
            connections = GetConnectionsForMode((GameManager.RailConstructionMode)s);
            category = (t == 0) ? TileCategory.None : TileCategory.Rail;
        }
    }

    // =========================
    // FACTORY FOOTPRINT
    // =========================

    /// <summary>
    /// FactoryFootprint
    /// ─────────────────────────────────────────────────────────────────────
    /// Popisuje veľkosť (footprint) jednej továrne v tiloch a pozíciu
    /// "kurzorového" (anchor) tilu v rámci tohto obdĺžnika.
    ///
    /// width  = počet tilov v smere X
    /// depth  = počet tilov v smere Z
    /// anchorX, anchorZ = lokálny index tilu (0..width-1, 0..depth-1), nad
    ///                    ktorým je snap kurzor myši. Zvyšok footprintu sa
    ///                    odvodzuje relatívne od tohto tilu.
    ///
    /// PREČO ANCHOR:
    ///   Pre nepárny rozmer (napr. 3×3) existuje presný stredový tile.
    ///   Pre párny rozmer (napr. 2×3 alebo 2×2) geometrický stred padne na
    ///   hranu medzi tilmi – stredový tile neexistuje. Anchor preto definuje
    ///   fixný, predvídateľný tile, ktorý hráč "drží" pod myšou, takže
    ///   umiestnenie je konzistentné pri každej veľkosti.
    ///
    /// VÝPOČET ROHOV footprintu z anchor tilu (ax, az):
    ///   minX = ax - anchorX
    ///   minZ = az - anchorZ
    ///   maxX = minX + width  - 1
    ///   maxZ = minZ + depth  - 1
    /// </summary>
    /// <summary>
    /// FactoryRotation
    /// ─────────────────────────────────────────────────────────────────────
    /// Otočenie továrne na tile mape v krokoch po 90°.
    ///
    /// PREČO ENUM A NIE bool:
    ///   Bool (true/false) by stačil len na prepnutie 2×3 ↔ 3×2. Ale pri
    ///   nesymetrickom footprinte (2×3) chýba aj otočenie o 180° a 270°,
    ///   ktoré menia, KTORÝM smerom je továreň "otočená" (kde má vstup,
    ///   ktorý roh je anchor). Enum so 4 hodnotami pokrýva všetky prípady
    ///   a je ľahko rozšíriteľný.
    ///
    /// VPLYV NA FOOTPRINT:
    ///   - Deg0   / Deg180 → rozmery zostávajú width×depth.
    ///   - Deg90  / Deg270 → rozmery sa PREHODIA na depth×width
    ///     (to je presne tvoja predstava "rotation XZ / ZX").
    ///   Anchor (kurzorový tile) sa transformuje spolu s obdĺžnikom, takže
    ///   kurzor vždy "drží" ten istý logický tile továrne.
    /// </summary>
    public enum FactoryRotation
    {
        Deg0 = 0,   // bez rotácie (natívna orientácia z GetFactoryFootprint)
        Deg90 = 1,   // 90°  proti smeru hod. ručičiek – prehodí width↔depth
        Deg180 = 2,   // 180°
        Deg270 = 3    // 270° proti smeru hod. ručičiek – prehodí width↔depth
    }

    [System.Serializable]
    public struct FactoryFootprint
    {
        public int width;
        public int depth;
        public int anchorX;
        public int anchorZ;

        public FactoryFootprint(int width, int depth, int anchorX, int anchorZ)
        {
            this.width = width;
            this.depth = depth;
            this.anchorX = anchorX;
            this.anchorZ = anchorZ;
        }

        public int TileCount => width * depth;

        /// <summary>
        /// Vráti NOVÝ footprint otočený o zadaný uhol.
        ///
        /// Transformácia lokálnej súradnice (lx, lz) v rámci obdĺžnika
        /// width×depth do otočeného obdĺžnika:
        ///   Deg0:   (lx, lz)                  rozmer width×depth
        ///   Deg90:  (lz, width-1-lx)          rozmer depth×width
        ///   Deg180: (width-1-lx, depth-1-lz)  rozmer width×depth
        ///   Deg270: (depth-1-lz, lx)          rozmer depth×width
        /// Tá istá transformácia sa aplikuje na anchor, aby kurzor zostal
        /// nad rovnakým logickým tilom továrne.
        /// </summary>
        public FactoryFootprint Rotated(FactoryRotation rot)
        {
            switch (rot)
            {
                case FactoryRotation.Deg90:
                    // rozmer depth×width, anchor (anchorZ, width-1-anchorX)
                    return new FactoryFootprint(depth, width,
                                                anchorZ, width - 1 - anchorX);

                case FactoryRotation.Deg180:
                    // rozmer width×depth, anchor zrkadlený v oboch osiach
                    return new FactoryFootprint(width, depth,
                                                width - 1 - anchorX, depth - 1 - anchorZ);

                case FactoryRotation.Deg270:
                    // rozmer depth×width, anchor (depth-1-anchorZ, anchorX)
                    return new FactoryFootprint(depth, width,
                                                depth - 1 - anchorZ, anchorX);

                case FactoryRotation.Deg0:
                default:
                    return this;
            }
        }
    }

    /// <summary>
    /// Vráti footprint (veľkosť + anchor) pre zadaný typ továrne.
    ///
    /// VEĽKOSTI (podľa zadania):
    ///   Factory (tileID 4):
    ///     - Coal Mine       2×3
    ///     - Forest          3×3
    ///     - Iron Ore Mine   3×3
    ///     - Gold Mine       3×3
    ///     - Silver Mine     3×3
    ///     - Farm            3×3
    ///     - Oil Wells       3×3
    ///   Processing (tileID 5):
    ///     - Power Station       2×2
    ///     - Sawmill             2×2
    ///     - Oil Refinery        2×2
    ///     - Electronics Factory 3×3
    ///     - Furniture Factory   3×3
    ///     - Slaughterhouse      2×2
    ///     - Grain Factory       2×3
    ///     - Smelter             2×3
    ///     - Glass Factory       2×2
    ///
    /// ANCHOR: zvolený tak, aby kurzor bol čo najbližšie stredu obdĺžnika.
    ///   - Pre nepárny rozmer → presný stred (3 → index 1).
    ///   - Pre párny rozmer → "ľavý/dolný zo stredovej dvojice"
    ///     (2 → index 0), čím je footprint deterministický.
    ///
    /// Tieto hodnoty sú jediné miesto, kde sa veľkosti definujú – ide o
    /// "nastaviteľný parameter". Pre zmenu veľkosti továrne stačí upraviť
    /// príslušný riadok tu.
    /// </summary>
    public static FactoryFootprint GetFactoryFootprint(GameManager.FactoryConstructionMode mode)
    {
        switch (mode)
        {
            // ── Factory (tileID 4) ──

            // Coal Mine 2×3  → anchor (0,1)
            case GameManager.FactoryConstructionMode.CoalMine:
                return new FactoryFootprint(3, 3, 0, 1);

            // Forest 3×3 → anchor (1,1) = presný stred
            case GameManager.FactoryConstructionMode.Forest:
                return new FactoryFootprint(3, 3, 1, 1);

            // Iron Ore Mine 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.IronOreMine:
                return new FactoryFootprint(3, 3, 1, 1);

            // Gold Mine 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.GoldMine:
                return new FactoryFootprint(3, 3, 1, 1);

            // Silver Mine 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.SilverMine:
                return new FactoryFootprint(3, 3, 1, 1);

            // Farm 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.Farm:
                return new FactoryFootprint(3, 3, 1, 1);

            // Oil Wells 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.OilWells:
                return new FactoryFootprint(3, 3, 1, 1);

            // ── Processing (tileID 5) ──

            // Power Station 2×2 → anchor (0,0)
            case GameManager.FactoryConstructionMode.PowerStation:
                return new FactoryFootprint(2, 2, 0, 0);

            // Sawmill 2×2 → anchor (0,0)
            case GameManager.FactoryConstructionMode.SawMill:
                return new FactoryFootprint(2, 2, 0, 0);

            // Oil Refinery 2×2 → anchor (0,0)
            case GameManager.FactoryConstructionMode.OilRefinery:
                return new FactoryFootprint(3, 3, 0, 0);

            // Electronics Factory 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.ElectronicsFactory:
                return new FactoryFootprint(3, 3, 1, 1);

            // Furniture Factory 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.FurnitureFactory:
                return new FactoryFootprint(3, 3, 1, 1);

            // Slaughterhouse 2×2 → anchor (0,0)
            case GameManager.FactoryConstructionMode.Slaughterhouse:
                return new FactoryFootprint(2, 2, 0, 0);

            // Grain Factory 2×3 → anchor (0,1)
            case GameManager.FactoryConstructionMode.GrainFactory:
                return new FactoryFootprint(3, 3, 0, 1);

            // Smelter 2×3 → anchor (0,1)
            case GameManager.FactoryConstructionMode.Smelter:
                return new FactoryFootprint(2, 2, 0, 1);

            // Glass Factory 2×2 → anchor (0,0)
            case GameManager.FactoryConstructionMode.GlassFactory:
                return new FactoryFootprint(2, 2, 0, 0);

            // None / neznáme → 1×1 (degeneruje na klasický single-tile)
            default:
                return new FactoryFootprint(1, 1, 0, 0);
        }
    }

    /// <summary>
    /// Vráti footprint pre daný typ továrne UŽ OTOČENÝ o zadaný uhol.
    /// Pohodlné preťaženie – spojí GetFactoryFootprint(mode) a .Rotated(rot).
    /// </summary>
    public static FactoryFootprint GetFactoryFootprint(
        GameManager.FactoryConstructionMode mode, FactoryRotation rotation)
    {
        return GetFactoryFootprint(mode).Rotated(rotation);
    }

    const int GRID_SIZE = 256;
    TileData[,] tileGrid;

    // =========================
    // SNAP SYSTEM
    // =========================

    GameObject snapSphere;
    GameObject lineFace01, lineFace02, lineFace03, lineFace04;

    LineRenderer lineRenderer01, lineRenderer02, lineRenderer03, lineRenderer04;

    Material lineFaceMat, snapFaceMatTex;

    //Textúry pre RAIL
    Texture2D snapFaceTex_Rail;     //ID = 1
    Texture2D snapFaceTex_RailStation;  //ID = 2
    Texture2D snapFaceTex_RailDepot;    //ID = 3

    //Textúry pre ROAD
    Texture2D snapFaceTex_Road;     //ID = 1
    Texture2D snapFaceTex_RoadStation;  //ID = 2
    Texture2D snapFaceTex_RoadDepot;    //ID = 3

    //Textúry pre FACTORY
    Texture2D snapFaceTex_Factory;     //ID = 4
    Texture2D snapFaceTex_Processing;  //ID = 5

    // ZMIEŠANÁ KRIŽOVATKA RAIL + ROAD – voliteľná textúra
    // (Resources/Textures/Rail/RailRoadCrossing_Tex). Chýba = Rail_Tex.
    Texture2D snapFaceTex_RailRoadCrossing;

    Mesh meshFace;

    GameObject[,] tileMap;

    // =========================================================================
    // VOLITEĽNÉ 3D MODELY / SPRITY DLAŽDÍC (TileModelLibrary)
    // -------------------------------------------------------------------------
    // Ak je pre konštrukčný typ dlaždice (podľa kategórie RAIL/ROAD a stateID =
    // konštrukčný mód) priradený prefab, UpdateTileMap vytvorí inštanciu tohto
    // modelu/spritu NAMIESTO pôvodného textúrovaného quadu. Ak nie je, vykreslí
    // sa pôvodná textúra – presne ako doteraz.
    //
    // Referencia je voliteľná: ak nie je priradená v Inspectore, dohľadá sa
    // cez TileModelLibrary.instance, prípadne FindObjectOfType. Ak knižnica
    // v scéne nie je, všetko ostane na pôvodných textúrach.
    // =========================================================================
    [Header("Tile 3D modely / sprity (voliteľné)")]
    [Tooltip("Knižnica prefab-ov modelov/spritov dlaždíc. Prázdne sloty = " +
             "pôvodná textúra. Ak tu nepriradíš nič, knižnica sa dohľadá v scéne.")]
    [SerializeField] private TileModelLibrary tileModelLibrary;

    [Tooltip("Ak je zapnuté, model/sprite sa proporčne (so zachovaním pomeru " +
             "strán) zmenší/zväčší tak, aby jeho pôdorys X×Z zaplnil práve 1 " +
             "dlaždicu a vycentruje sa na ňu. Vypni, ak máš prefab už pripravený " +
             "v presnej mierke 1×1.")]
    [SerializeField] private bool fitTileModelsToTile = true;

    // -------------------------------------------------------------------------
    // NATOČENIE DLAŽDÍC NA ŠIKMEJ PLOŠE (rampy)
    // -------------------------------------------------------------------------
    // Fixné pootočenie z TileModelLibrary (Yaw/Pitch/Roll po 90°) je STATICKÉ –
    // je to jednorazová korekcia toho, ako je model v prefabe vyexportovaný.
    // Na vodorovnej dlaždici to stačí, na šikmej NIE: rampa má sklon, ktorý
    // vznikne až za behu podľa výšok rohov terénu, takže sa v Inspectore
    // dopredu nastaviť nedá.
    //
    // Týka sa to iba dlaždíc, ktoré sa dajú postaviť na svah:
    //     RailHorizontal, RailVertical, RoadHorizontal, RoadVertical
    // Zvyšné typy (križovatky, oblúky, výhybky, stanice, depá) ostávajú bez
    // zmeny – správajú sa presne ako doteraz.
    //
    // Sklon sa počíta zo 4 rohových výšok dlaždice, takže funguje pre ľubovoľný
    // uhol aj pre rohové (diagonálne) svahy, nielen pre 4 základné smery.
    // -------------------------------------------------------------------------

    [Header("Dlaždice na šikmej ploche (rampy)")]

    [Tooltip("Zapnuté = dlaždica RailHorizontal/RailVertical/RoadHorizontal/" +
             "RoadVertical sa na svahu automaticky preklopí do roviny terénu " +
             "(podľa normály spočítanej zo 4 rohových výšok). Vypni, ak chceš " +
             "sklon riešiť výhradne ručne cez tabuľku nižšie.")]
    [SerializeField] private bool alignSlopeTilesToTerrain = true;

    [Tooltip("Od akého prevýšenia (v jednotkách, cez celú dlaždicu) sa dlaždica " +
             "považuje za šikmú. Menšie rozdiely sa berú ako rovina.")]
    [SerializeField] private float slopeDetectThreshold = 0.01f;

    [Tooltip("RUČNÉ DOLADENIE pre svah stúpajúci na VÝCHOD (+X, Right). " +
             "Euler v stupňoch, aplikuje sa NAVYŠE k automatickému preklopeniu. " +
             "Pozn.: stúpanie na +X je tá istá dlaždica ako klesanie na -X, " +
             "preto 4 smery pokrývajú všetkých 8 prípadov.")]
    [SerializeField] private Vector3 slopeExtraEulerRight = Vector3.zero;

    [Tooltip("RUČNÉ DOLADENIE pre svah stúpajúci na ZÁPAD (-X, Left). " +
             "Euler v stupňoch, aplikuje sa NAVYŠE k automatickému preklopeniu.")]
    [SerializeField] private Vector3 slopeExtraEulerLeft = Vector3.zero;

    [Tooltip("RUČNÉ DOLADENIE pre svah stúpajúci na SEVER (+Z, Top). " +
             "Euler v stupňoch, aplikuje sa NAVYŠE k automatickému preklopeniu.")]
    [SerializeField] private Vector3 slopeExtraEulerTop = Vector3.zero;

    [Tooltip("RUČNÉ DOLADENIE pre svah stúpajúci na JUH (-Z, Bottom). " +
             "Euler v stupňoch, aplikuje sa NAVYŠE k automatickému preklopeniu.")]
    [SerializeField] private Vector3 slopeExtraEulerBottom = Vector3.zero;

    [Tooltip("Zapnuté = model rampy sa uniformne zväčší o 1/cos(sklon), aby " +
             "pokryl aj predĺženú (šikmú) dĺžku dlaždice a nevznikla škára pri " +
             "napojení na susedov. Pri strmých svahoch mierne presahuje.")]
    [SerializeField] private bool stretchSlopeTilesToRamp = true;

    [Tooltip("Diagnostika: pre každú šikmú dlaždicu vypíše do konzoly zistený " +
             "smer stúpania, uhol sklonu a použité doladenie.")]
    [SerializeField] private bool logSlopeAlignment = false;

    // -------------------------------------------------------------------------
    // TOVÁRNE = 2D SPRITE (multi-tile footprint)
    // -------------------------------------------------------------------------
    // Na rozdiel od RAIL/ROAD (1 model = 1 dlaždica) je továreň JEDEN objekt na
    // CELÝ footprint (2×2, 2×3, 3×3 …). Od prechodu na 2D grafiku sa NEVYTVÁRA
    // 3D model z prefabu, ale JEDEN 2D SPRITE (izometrický obrázok továrne,
    // napr. "CoalMineUI.png") – viď TileModelLibrary.GetFactorySprite a
    // komponent FactorySpriteBillboard, ktorý rieši:
    //     • billboard (sprite je vždy natočený na kameru),
    //     • mierku podľa footprintu (šírka spritu = šírka kosoštvorca footprintu),
    //     • umiestnenie (spodná hrana obrázka sadne na predný roh footprintu),
    //     • Z-poradie (ZWrite off + nízky sortingOrder → všetko ostatné sa
    //       kreslí PRED sprite továrne).
    //
    // Sprity sú vedené mimo tileMap[,] – dátové štruktúry ostávajú rovnaké ako
    // pri modeloch, takže sa nič iné v engine nemenilo:
    //   • factoryModels      – kľúč = origin (ľavý-dolný roh footprintu) → sprite GO
    //   • factoryCoveredTiles – množina tilov, ktoré sprite prekrýva;
    //                           UpdateTileMap pre ne NEVytvára textúrový quad.
    //                           (Dá sa vypnúť cez factorySpriteGlobal.
    //                            keepFootprintTextures.)
    // Životný cyklus je naviazaný na FactoryRegistry (Register/Unregister/Clear),
    // takže pokrýva ručné položenie, Load aj demoláciu jednou cestou.
    // -------------------------------------------------------------------------
    private readonly Dictionary<Vector2Int, GameObject> factoryModels
        = new Dictionary<Vector2Int, GameObject>();
    private readonly HashSet<Vector2Int> factoryCoveredTiles
        = new HashSet<Vector2Int>();

    // -------------------------------------------------------------------------
    // MOSTY / TUNELY – hlavy prekryté 2D spritom (RailCrossingSystem aj RoadCrossingSystem)
    // -------------------------------------------------------------------------
    // Analógia k factoryCoveredTiles: pre tieto tily UpdateTileMap NEvytvára
    // textúrový quad koľaje, lebo ho prekrýva sprite nástupu mosta / portálu.
    // Dátovo ostáva tile normálnou RAIL dlaždicou (tileID 1).
    private readonly HashSet<Vector2Int> crossingCoveredTiles
        = new HashSet<Vector2Int>();

    /// <summary>
    /// Označí / odznačí tile hlavy mosta alebo tunela ako prekrytý spritom
    /// a prekreslí ho. Volá výhradne CrossingSystemBase (RAIL aj ROAD).
    /// </summary>
    public void SetCrossingTileCovered(int x, int z, bool covered)
    {
        var key = new Vector2Int(x, z);
        bool changed = covered ? crossingCoveredTiles.Add(key) : crossingCoveredTiles.Remove(key);
        if (changed && tileGrid != null && IsValidIndex(key))
            UpdateTileMap(x, z);
    }

    [Header("Továrne – spoločné nastavenia 2D spritov")]
    [Tooltip("Nastavenia SPOLOČNÉ pre všetky továrne: sorting layer/order, " +
             "render queue, zarovnanie podľa nakresleného obsahu a diagnostika.\n\n" +
             "MIERKA A ZVISLÝ POSUN KONKRÉTNEJ TOVÁRNE sa nastavujú inde – " +
             "v TileModelLibrary, kde má každá zo 16 tovární vlastný blok " +
             "(\"FACTORY / PROCESSING – nastavenia spritu\").")]
    [SerializeField]
    private FactorySpriteGlobalSettings factorySpriteGlobal
        = new FactorySpriteGlobalSettings();

    // =========================
    // INITIALIZATION
    // =========================

    private void Awake()
    {
        instance = this;
    }

    // -------------------------------------------------------------------------
    // MEDZISCÉNOVÉ NAČÍTANIE HRY (MainMenu → IndicatrixScene)
    // ─────────────────────────────────────────────────────────────────────────
    // LoadGame() vie bežať len v hernej scéne (IndicatrixScene), kde existuje
    // IndicatrixAPI aj všetky herné systémy. Z MainMenu sa preto pri kliknutí na
    // "Load Game" len nastaví tento STATICKÝ príznak a prepne sa scéna. Statická
    // premenná prežíva prechod scén v rámci behu aplikácie, takže IndicatrixAPI
    // v novej scéne ju prečíta a po pripravení systémov spustí LoadGame().
    //
    // Príznak je JEDNORAZOVÝ – po spotrebovaní sa zhodí, takže ďalšie spustenia
    // (napr. "New Game") sa nenačítajú omylom zo save.
    // -------------------------------------------------------------------------
    public static bool LoadGameOnSceneStart = false;

    private void Start()
    {
        InitializeTileEngine();
        InitializeSnapSystem();

        // Ak sme do hernej scény prišli cez "Load Game" v MainMenu, načítaj
        // uloženú hru – ale až keď budú systémy (najmä terén) pripravené.
        if (LoadGameOnSceneStart)
        {
            LoadGameOnSceneStart = false;   // jednorazové – hneď spotrebuj
            StartCoroutine(LoadGameWhenReady());
        }
    }

    /// <summary>
    /// Počká, kým je terén vygenerovaný (TerrainManager.coordsF) a dobehnú
    /// Start()/Awake() ostatných systémov, a až potom zavolá <see cref="LoadGame"/>.
    /// Bez čakania by ReadTerrain nemal kam zapísať výšky (terén ešte nie je
    /// hotový) a CityManager/registre by neboli pripravené.
    /// </summary>
    private System.Collections.IEnumerator LoadGameWhenReady()
    {
        // 1) Počkaj na hotový terén (coordsF naplnené v TerrainManager.Start).
        while (TerrainManager.instance == null
               || TerrainManager.instance.coordsF == null
               || TerrainManager.instance.coordsF.Length == 0)
        {
            yield return null;
        }

        // 2) Ešte 1 frame, aby dobehli zvyšné Start() v scéne (GameClock,
        //    GameEconomy, BudgetSystem, TrainSystem, VehicleSystem, registre…).
        yield return null;

        LoadGame();
    }

    void InitializeTileEngine()
    {
        tileGrid = new TileData[GRID_SIZE, GRID_SIZE];
        tileMap = new GameObject[GRID_SIZE, GRID_SIZE];

        for (int x = 0; x < GRID_SIZE; x++)
        {
            for (int z = 0; z < GRID_SIZE; z++)
            {
                tileGrid[x, z] = new TileData(0, 0, DirectionMask.None, TileCategory.None);
                tileMap[x, z] = null;
            }
        }
    }

    void InitializeSnapSystem()
    {
        snapSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(snapSphere.GetComponent<SphereCollider>());
        snapSphere.transform.localScale = Vector3.one * 0.15f;
        snapSphere.GetComponent<Renderer>().material.color = new Color(1, 1, 1, 0);

        lineFaceMat = Resources.Load<Material>("Materials/LineMat");
        snapFaceMatTex = Resources.Load<Material>("Materials/FaceMatTex");

        //Načítanie textúr pre RAIL
        snapFaceTex_Rail = Resources.Load<Texture2D>("Textures/Rail/Rail_Tex");
        snapFaceTex_RailStation = Resources.Load<Texture2D>("Textures/Rail/RailStation_Tex");
        snapFaceTex_RailDepot = Resources.Load<Texture2D>("Textures/Rail/RailDepot_Tex");

        //Načítanie textúr pre ROAD
        snapFaceTex_Road = Resources.Load<Texture2D>("Textures/Road/Road_Tex");
        snapFaceTex_RoadStation = Resources.Load<Texture2D>("Textures/Road/RoadStation_Tex");
        snapFaceTex_RoadDepot = Resources.Load<Texture2D>("Textures/Road/RoadDepot_Tex");

        //Načítanie textúr pre FACTORY
        snapFaceTex_Factory = Resources.Load<Texture2D>("Textures/Factory/Factory_Tex");
        snapFaceTex_Processing = Resources.Load<Texture2D>("Textures/Factory/Processing_Tex");

        snapFaceTex_RailRoadCrossing = Resources.Load<Texture2D>("Textures/Rail/RailRoadCrossing_Tex");

        meshFace = new Mesh();

        CreateLineRenderer(ref lineFace01, ref lineRenderer01, "LineFace01");
        CreateLineRenderer(ref lineFace02, ref lineRenderer02, "LineFace02");
        CreateLineRenderer(ref lineFace03, ref lineRenderer03, "LineFace03");
        CreateLineRenderer(ref lineFace04, ref lineRenderer04, "LineFace04");

        HideSnapSphere();
        HideSnapLines();
    }

    void HideSnapSphere()
    {
        if (snapSphere != null)
            snapSphere.SetActive(false);
    }

    void ShowSnapSphere()
    {
        if (snapSphere != null)
            snapSphere.SetActive(true);
    }

    void HideSnapLines()
    {
        if (lineFace01 != null) lineFace01.SetActive(false);
        if (lineFace02 != null) lineFace02.SetActive(false);
        if (lineFace03 != null) lineFace03.SetActive(false);
        if (lineFace04 != null) lineFace04.SetActive(false);
    }

    void ShowSnapLines()
    {
        if (lineFace01 != null) lineFace01.SetActive(true);
        if (lineFace02 != null) lineFace02.SetActive(true);
        if (lineFace03 != null) lineFace03.SetActive(true);
        if (lineFace04 != null) lineFace04.SetActive(true);
    }

    /// <summary>
    /// Skryje všetky snap vizuály (snap sphere aj línie). Volá sa pri prepnutí do vlakových režimov.
    /// </summary>
    public void HideAllSnapVisuals()
    {
        HideSnapSphere();
        HideSnapLines();
    }

    void CreateLineRenderer(ref GameObject obj, ref LineRenderer lr, string name)
    {
        obj = new GameObject(name);
        lr = obj.AddComponent<LineRenderer>();
        lr.material = lineFaceMat;
        lr.startWidth = 0.1f;
        lr.endWidth = 0.1f;
        lr.positionCount = 2;
    }

    // =========================
    // TILE ENGINE CORE
    // =========================

    public Vector2Int SnapTileIndex(Vector3 hitPoint)
    {
        int x = Mathf.FloorToInt(hitPoint.x);
        int z = Mathf.FloorToInt(hitPoint.z);

        return new Vector2Int(x, z);
    }

    /// <summary>
    /// Pôvodná signatúra – stateID 0 znamená "žiadny špecifický režim" (Demolish / clear).
    /// Connections sa nastaví na None. Predpokladá RAIL kategóriu (spätná kompatibilita).
    /// </summary>
    public void SetTile(Vector3 hitPoint, int tileID, int stateID)
    {
        SetTile(hitPoint, tileID, (GameManager.RailConstructionMode)stateID);
    }

    /// <summary>
    /// RAIL verzia – ukladá tileID + RailConstructionMode (ako stateID),
    /// connections sa odvodí cez GetConnectionsForMode, category = Rail
    /// (alebo None ak tileID == 0, t.j. Demolish/clear).
    /// </summary>
    public void SetTile(Vector3 hitPoint, int tileID, GameManager.RailConstructionMode mode)
    {
        Vector2Int index = SnapTileIndex(hitPoint);

        if (!IsValidIndex(index)) return;

        DirectionMask conns = (tileID == 0) ? DirectionMask.None : GetConnectionsForMode(mode);
        TileCategory cat = (tileID == 0) ? TileCategory.None : TileCategory.Rail;

        TileData before = tileGrid[index.x, index.y];
        tileGrid[index.x, index.y] = new TileData(tileID, (int)mode, conns, cat);
        UpdateTileMap(index.x, index.y);
        EmitTileChangeEffect(index.x, index.y, before, tileGrid[index.x, index.y]);
    }

    /// <summary>
    /// ROAD verzia – ukladá tileID + RoadConstructionMode (ako stateID),
    /// connections sa odvodí cez GetConnectionsForMode preťaženie pre Road,
    /// category = Road (alebo None ak tileID == 0).
    ///
    /// POZN.: stateID pre RAIL a ROAD pochádza z odlišných enum-ov, preto
    /// raw int by mohol kolidovať. Kategória je preto určujúca pri spätnej
    /// rekonštrukcii (pri Load alebo iných operáciách, ktoré poznajú len
    /// raw stateID).
    /// </summary>
    public void SetTile(Vector3 hitPoint, int tileID, GameManager.RoadConstructionMode mode)
    {
        Vector2Int index = SnapTileIndex(hitPoint);

        if (!IsValidIndex(index)) return;

        DirectionMask conns = (tileID == 0) ? DirectionMask.None : GetConnectionsForMode(mode);
        TileCategory cat = (tileID == 0) ? TileCategory.None : TileCategory.Road;

        TileData before = tileGrid[index.x, index.y];
        tileGrid[index.x, index.y] = new TileData(tileID, (int)mode, conns, cat);
        UpdateTileMap(index.x, index.y);
        EmitTileChangeEffect(index.x, index.y, before, tileGrid[index.x, index.y]);
    }

    /// <summary>
    /// FACTORY verzia – multi-tile umiestnenie továrne.
    ///
    /// Na rozdiel od RAIL/ROAD (vždy 1×1) zaberá továreň obdĺžnik N×M tilov
    /// podľa <see cref="GetFactoryFootprint"/>. Hit point určuje "anchor"
    /// tile (kurzor myši); zvyšok footprintu sa dopočíta okolo neho.
    ///
    /// Do KAŽDÉHO tilu footprintu sa zapíše tá istá TileData:
    ///   tileID   = 4 (Factory) alebo 5 (Processing)
    ///   stateID  = (int)FactoryConstructionMode (rozlíši CoalMine/Forest/...)
    ///   category = TileCategory.Factory
    ///   connections = None (továrne nie sú dopravný graf)
    ///
    /// Vďaka tomu sa textúra (Factory_Tex / Processing_Tex) vykreslí na
    /// všetkých tiloch footprintu (napr. 2×3 = 6 tilov).
    ///
    /// VALIDÁCIA: celý footprint musí byť (a) vnútri gridu a (b) prázdny
    /// (samé tileID == 0). Ak čokoľvek prekáža, NIČ sa nepoloží a metóda
    /// vráti false – továreň sa nepoloží "čiastočne".
    ///
    /// stateID == 0 (FactoryConstructionMode.None) sa berie ako "clear" –
    /// vymaže 1×1 tile pod kurzorom (rovnaká sémantika ako Demolish).
    ///
    /// PARAMETER rotation:
    ///   Otočenie továrne o 0/90/180/270°. Pri 90°/270° sa footprint
    ///   prehodí (width↔depth). Anchor sa transformuje spolu s obdĺžnikom,
    ///   takže kurzor drží stále ten istý logický tile. Default = Deg0
    ///   (bez rotácie) – staré volania bez tohto parametra fungujú ďalej.
    /// </summary>
    public bool SetTile(Vector3 hitPoint, int tileID, GameManager.FactoryConstructionMode mode,
                        FactoryRotation rotation = FactoryRotation.Deg0)
    {
        // Deleguje na preťaženie s out parametrami; výsledný footprint sa
        // tu zahodí (volajúci ho nepotrebuje).
        return SetTile(hitPoint, tileID, mode, out _, out _, out _, out _, rotation);
    }

    /// <summary>
    /// FACTORY verzia s VÝSTUPOM footprintu – funkčne identická s preťažením
    /// vyššie, navyše ale cez out parametre oznámi, KAM presne sa továreň
    /// položila (ľavý-dolný roh + rozmery už po rotácii).
    ///
    /// Vďaka tomu vie volajúci (GameManager) zaevidovať FactoryInstance bez
    /// toho, aby duplikoval výpočet footprintu – jediný zdroj pravdy pre
    /// pozíciu zostáva tu.
    ///
    /// Pri neúspechu (mimo gridu / kolízia) sú out parametre nastavené na 0
    /// a metóda vráti false. Pri None/clear vetve out parametre popisujú
    /// vymazaný 1×1 tile (origin = anchor, width = depth = 1).
    /// </summary>
    public bool SetTile(Vector3 hitPoint, int tileID, GameManager.FactoryConstructionMode mode,
                        out int originX, out int originZ, out int width, out int depth,
                        FactoryRotation rotation = FactoryRotation.Deg0)
    {
        originX = 0; originZ = 0; width = 0; depth = 0;

        Vector2Int anchor = SnapTileIndex(hitPoint);
        if (!IsValidIndex(anchor)) return false;

        // None / clear → vymaž 1 tile pod kurzorom.
        if (tileID == 0 || mode == GameManager.FactoryConstructionMode.None)
        {
            TileData before = tileGrid[anchor.x, anchor.y];
            tileGrid[anchor.x, anchor.y] = new TileData(0, 0, DirectionMask.None, TileCategory.None);
            UpdateTileMap(anchor.x, anchor.y);
            EmitTileChangeEffect(anchor.x, anchor.y, before, tileGrid[anchor.x, anchor.y]);
            originX = anchor.x; originZ = anchor.y; width = 1; depth = 1;
            return true;
        }

        // Footprint UŽ otočený podľa rotation.
        FactoryFootprint fp = GetFactoryFootprint(mode, rotation);

        // Ľavý-dolný roh footprintu odvodený z anchor tilu.
        int minX = anchor.x - fp.anchorX;
        int minZ = anchor.y - fp.anchorZ;
        int maxX = minX + fp.width - 1;
        int maxZ = minZ + fp.depth - 1;

        // (a) celý footprint musí byť vnútri gridu
        if (minX < 0 || minZ < 0 || maxX >= GRID_SIZE || maxZ >= GRID_SIZE)
        {
            Debug.LogWarning($"[IndicatrixAPI] Factory '{mode}' ({fp.width}x{fp.depth}) " +
                             $"presahuje okraj mapy – neumiestnené.");
            return false;
        }

        // (b) celý footprint musí byť prázdny
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                if (tileGrid[x, z].tileID != 0)
                {
                    Debug.LogWarning($"[IndicatrixAPI] Factory '{mode}' – tile [{x},{z}] " +
                                     $"je obsadený. Footprint musí byť celý prázdny – neumiestnené.");
                    return false;
                }
            }
        }

        // Zápis tej istej TileData do všetkých tilov footprintu.
        DirectionMask conns = GetConnectionsForMode(mode); // None
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                TileData before = tileGrid[x, z];
                tileGrid[x, z] = new TileData(tileID, (int)mode, conns, TileCategory.Factory);
                UpdateTileMap(x, z);
                EmitTileChangeEffect(x, z, before, tileGrid[x, z]);
            }
        }

        // Výstup položeného footprintu (po rotácii).
        originX = minX; originZ = minZ; width = fp.width; depth = fp.depth;

        Debug.Log($"[IndicatrixAPI] Factory '{mode}' (rot {rotation}) umiestnená na footprint " +
                  $"[{minX},{minZ}]–[{maxX},{maxZ}] ({fp.TileCount} tilov).");
        return true;
    }

    /// <summary>
    /// ZMIEŠANÁ KRIŽOVATKA – zapíše na tile [x,z] úrovňový prejazd železnice a
    /// cesty (tileID 1, category RailRoadCrossing, stateID = mode) a prekreslí ho.
    /// Volá GameManager, keď hráč položí priamu koľaj kolmo na priamu cestu
    /// (alebo naopak). Validáciu robí volajúci.
    /// </summary>
    public void SetLevelCrossing(int x, int z, LevelCrossingMode mode)
    {
        if (!IsValidIndex(new Vector2Int(x, z)) || mode == LevelCrossingMode.None) return;

        TileData before = tileGrid[x, z];
        tileGrid[x, z] = new TileData(1, (int)mode, GetConnectionsForMode(mode), TileCategory.RailRoadCrossing);
        UpdateTileMap(x, z);
        EmitTileChangeEffect(x, z, before, tileGrid[x, z]);
    }

    /// <summary>
    /// Vymaže JEDEN tile na danom indexe [x,z] (nastaví prázdnu TileData,
    /// tileID 0, category None) a prekreslí jeho mesh cez UpdateTileMap.
    ///
    /// Na rozdiel od SetTile(Vector3 ...) pracuje priamo s tile indexom, takže
    /// volajúci (napr. GameManager.DemolishFactory) môže v cykle vyčistiť celý
    /// footprint viac-tile objektu (továrne) bez prepočtu svetových súradníc.
    /// Index mimo mriežky sa bezpečne ignoruje.
    /// </summary>
    public void ClearTileByIndex(int x, int z)
    {
        if (!IsValidIndex(new Vector2Int(x, z))) return;

        TileData before = tileGrid[x, z];
        tileGrid[x, z] = new TileData(0, 0, DirectionMask.None, TileCategory.None);
        UpdateTileMap(x, z);
        EmitTileChangeEffect(x, z, before, tileGrid[x, z]);
    }

    // -------------------------------------------------------------------------
    // VIZUÁLNY EFEKT VÝSTAVBY / DEMOLÁCIE NA TERÉNE
    // -------------------------------------------------------------------------

    /// <summary>
    /// True počas LoadGame – obnova mapy zo save nesmie spúšťať efekty stavby
    /// ani búrania (všetky tily by naraz zasvietili).
    /// </summary>
    private bool suppressTileEffects;

    /// <summary>
    /// Verejný prepínač pre iné systémy: ak nejaký skript hromadne zapisuje
    /// tily bez účasti hráča (napr. generovanie mapy pri štarte), môže efekty
    /// dočasne vypnúť: api.SuppressTileEffects = true; … = false;
    /// </summary>
    public bool SuppressTileEffects
    {
        get => suppressTileEffects;
        set => suppressTileEffects = value;
    }

    /// <summary>
    /// Po každej zmene tile rozhodne, či ide o VÝSTAVBU alebo DEMOLÁCIU, a
    /// spustí príslušný efekt v shaderi terénu (TerrainEditHighlight).
    ///
    ///   • tile sa vyprázdnil (tileID != 0 → 0)               → Demolish
    ///   • zo zmiešanej križovatky ostala len koľaj / cesta   → Demolish
    ///   • na tile vzniklo čokoľvek (tileID != 0)             → Build
    /// </summary>
    private void EmitTileChangeEffect(int x, int z, TileData before, TileData after)
    {
        if (suppressTileEffects || !Application.isPlaying) return;

        TerrainEditHighlight.FlashType type;
        if (after.tileID == 0)
        {
            if (before.tileID == 0) return;             // prázdne → prázdne, nič sa nestalo
            type = TerrainEditHighlight.FlashType.Demolish;
        }
        else if (before.category == TileCategory.RailRoadCrossing
                 && after.category != TileCategory.RailRoadCrossing)
        {
            type = TerrainEditHighlight.FlashType.Demolish;
        }
        else
        {
            type = TerrainEditHighlight.FlashType.Build;
        }

        TerrainEditHighlight.GetOrCreate().FlashTile(x, z, type);
    }

    /// <summary>
    /// Vypočíta (BEZ akéhokoľvek zápisu do mapy) obdĺžnik footprintu, ktorý by
    /// zabrala továreň typu <paramref name="mode"/> položená pod kurzorom
    /// <paramref name="hitPoint"/> s rotáciou <paramref name="rotation"/>.
    ///
    /// Slúži na PRED-validáciu v GameManager-i (kontrola obsadenosti footprintu
    /// a ochrannej zóny okolo iných tovární) – footprintová matematika tak
    /// zostáva na jedinom mieste (zhodná s tým, čo neskôr spraví SetTile).
    ///
    /// Vráti true, ak je celý footprint vnútri gridu; out parametre potom držia
    /// ľavý-dolný roh a rozmery (už po rotácii). Pri footprinte mimo mapy vráti
    /// false a out parametre sú 0.
    /// </summary>
    public bool TryGetFactoryFootprintBounds(
        Vector3 hitPoint, GameManager.FactoryConstructionMode mode, FactoryRotation rotation,
        out int originX, out int originZ, out int width, out int depth)
    {
        originX = 0; originZ = 0; width = 0; depth = 0;

        Vector2Int anchor = SnapTileIndex(hitPoint);
        if (!IsValidIndex(anchor)) return false;
        if (mode == GameManager.FactoryConstructionMode.None) return false;

        FactoryFootprint fp = GetFactoryFootprint(mode, rotation);

        int minX = anchor.x - fp.anchorX;
        int minZ = anchor.y - fp.anchorZ;
        int maxX = minX + fp.width - 1;
        int maxZ = minZ + fp.depth - 1;

        if (minX < 0 || minZ < 0 || maxX >= GRID_SIZE || maxZ >= GRID_SIZE)
            return false;

        originX = minX; originZ = minZ; width = fp.width; depth = fp.depth;
        return true;
    }

    /// <summary>
    /// Vráti tile dáta pre daný hit point.
    ///
    /// KRITICKÉ: Táto metóda RETURNS ONLY RAIL tiles. Ak je na pozícii ROAD
    /// tile, vráti prázdnu TileData (tileID=0). Týmto sa zabezpečuje, že
    /// existujúci kód (predovšetkým TrainSystem.cs a jeho A* algoritmus)
    /// vidí len RAIL graf a nemusí byť modifikovaný kvôli ROAD systému.
    ///
    /// Pre prístup ku všetkým tile dátam (bez ohľadu na kategóriu) použite
    /// <see cref="GetTileAny"/>.
    /// </summary>
    public TileData GetTile(Vector3 hitPoint)
    {
        Vector2Int index = SnapTileIndex(hitPoint);

        if (!IsValidIndex(index))
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        var td = tileGrid[index.x, index.y];
        if (td.category == TileCategory.Road)
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        // Zmiešaná križovatka → pre RAIL priama koľaj.
        return RailViewOf(td);
    }

    /// <summary>
    /// Vráti tile dáta pre daný hit point BEZ filtra kategórie. Použije sa
    /// keď volajúci potrebuje vidieť všetky tile dáta (napr. GameManager
    /// pri Demolish overuje kategóriu, alebo budúci RoadSystem).
    /// </summary>
    public TileData GetTileAny(Vector3 hitPoint)
    {
        Vector2Int index = SnapTileIndex(hitPoint);

        if (!IsValidIndex(index))
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        return tileGrid[index.x, index.y];
    }

    /// <summary>
    /// Priamy prístup k tileGrid cez tile indexy (pre TrainSystem a A*).
    ///
    /// KRITICKÉ: Vracia len RAIL tiles (rovnaká logika ako GetTile vyššie).
    /// ROAD tiles sa javia ako prázdne (tileID=0). Týmto je TrainSystem.cs
    /// úplne izolovaný od ROAD systému.
    /// </summary>
    public TileData GetTileByIndex(int x, int z)
    {
        if (x < 0 || x >= GRID_SIZE || z < 0 || z >= GRID_SIZE)
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        var td = tileGrid[x, z];
        if (td.category == TileCategory.Road)
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        // Zmiešaná križovatka → pre RAIL (TrainSystem) priama koľaj po jej osi.
        return RailViewOf(td);
    }

    /// <summary>
    /// CESTNÝ pohľad cez tile indexy (pre VehicleSystem a jeho A*): vracia LEN
    /// ROAD tiles, zmiešaná križovatka sa javí ako priama cesta po svojej osi.
    /// Všetko ostatné (RAIL, továrne, mimo gridu) = prázdna TileData.
    /// Cestná analógia k GetTileByIndex.
    /// </summary>
    public TileData GetRoadTileByIndex(int x, int z)
    {
        if (x < 0 || x >= GRID_SIZE || z < 0 || z >= GRID_SIZE)
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        var td = tileGrid[x, z];
        if (td.category == TileCategory.Road) return td;
        if (td.category == TileCategory.RailRoadCrossing) return RoadViewOf(td);
        return new TileData(0, 0, DirectionMask.None, TileCategory.None);
    }

    /// <summary>
    /// Priamy prístup k tileGrid cez tile indexy BEZ filtra kategórie.
    /// Použije sa keď volajúci potrebuje vidieť všetky tile dáta vrátane ROAD
    /// (napr. GameManager kontroluje kategóriu pred Demolish, alebo budúci
    /// RoadSystem.cs si bude takto čítať road graf).
    /// </summary>
    public TileData GetTileByIndexAny(int x, int z)
    {
        if (x < 0 || x >= GRID_SIZE || z < 0 || z >= GRID_SIZE)
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        return tileGrid[x, z];
    }

    // =========================
    // TERÉNNE DOTAZY (rovinatosť face, vrchol vs. obsadené tiles)
    //
    // Pomocné read-only metódy pre GameManager error-guardy:
    //   • IsFaceFlat            – je tile vodorovný (4 vertexy rovnaké Y)?
    //   • IsVertexOnOccupiedTile – patrí vrchol niektorému obsadenému tile?
    // =========================

    /// <summary>
    /// Vráti true, ak je face tile [tileX, tileZ] VODOROVNÝ – t.j. všetky
    /// 4 rohové vertexy majú rovnaké Y. Používa FunctionalValueY (číta výšky
    /// vertexov z TerrainManager.coordsF), rovnako ako SnapMeshFace.
    ///
    /// Rohy tile [x,z]: (x,z), (x,z+1), (x+1,z+1), (x+1,z).
    ///
    /// Slúži ako guard pre stavbu staníc/dep – tie sa smú stavať len na rovine.
    /// Ak TerrainManager ešte nie je inicializovaný, vracia true (fail-open –
    /// guard vtedy nebráni stavbe; reálne sa volá až počas hry, keď terén beží).
    /// </summary>
    public bool IsFaceFlat(int tileX, int tileZ)
    {
        if (TerrainManager.instance == null) return true;

        float y0 = FunctionalValueY(tileX, tileZ);
        float y1 = FunctionalValueY(tileX, tileZ + 1);
        float y2 = FunctionalValueY(tileX + 1, tileZ + 1);
        float y3 = FunctionalValueY(tileX + 1, tileZ);

        const float EPS = 0.0001f; // tolerancia na float nepresnosti
        return Mathf.Abs(y0 - y1) < EPS
            && Mathf.Abs(y0 - y2) < EPS
            && Mathf.Abs(y0 - y3) < EPS;
    }

    /// <summary>
    /// Vráti true, ak je face tile [tileX, tileZ] VODA – t.j. aspoň jeden zo
    /// 4 rohových vertexov leží NA alebo POD hladinou vody. Vodná hladina je
    /// vizuálne vždy na Y = TerrainManager.MinTerrainHeight (2.75f, zodpovedá
    /// úrovni E = -1) – pozri TerrainElement.BuildMesh, kde sa child[0]
    /// (vodná rovina) umiestňuje presne na túto výšku bez ohľadu na terén.
    ///
    /// Rohy tile [x,z]: (x,z), (x,z+1), (x+1,z+1), (x+1,z) – rovnaké poradie
    /// ako IsFaceFlat/IsFaceRamp.
    ///
    /// Slúži ako guard proti stavbe čohokoľvek (koľaje, cesty, stanice, depá,
    /// výhybky, továrne) na vodnej hladine – tile treba najprv vyplniť
    /// terénom (LevelUp) nad úroveň vody, až potom je stavba možná.
    /// Ak TerrainManager ešte nie je inicializovaný, vracia false (fail-open –
    /// guard vtedy nebráni stavbe).
    /// </summary>
    public bool IsFaceWater(int tileX, int tileZ)
    {
        if (TerrainManager.instance == null) return false;

        float y0 = FunctionalValueY(tileX, tileZ);
        float y1 = FunctionalValueY(tileX, tileZ + 1);
        float y2 = FunctionalValueY(tileX + 1, tileZ + 1);
        float y3 = FunctionalValueY(tileX + 1, tileZ);

        const float EPS = 0.0001f; // tolerancia na float nepresnosti
        float waterLevel = TerrainManager.MinTerrainHeight;

        return y0 <= waterLevel + EPS
            || y1 <= waterLevel + EPS
            || y2 <= waterLevel + EPS
            || y3 <= waterLevel + EPS;
    }

    /// <summary>
    /// Vráti true, ak je face tile [tileX, tileZ] ŠIKMÝ – čistá RAMPA: dve
    /// susedné vertexy (jedna hrana) sú na jednej Y a protiľahlá hrana (druhé
    /// dva susedné vertexy) je na inej Y. Sklon teda vedie buď pozdĺž osi X,
    /// alebo pozdĺž osi Z.
    ///
    /// Rohy tile [x,z] (poradie ako GetFaceVertices):
    ///   y0=(x,z), y1=(x,z+1), y2=(x+1,z+1), y3=(x+1,z).
    ///
    /// NIE JE rampa: rovina (vylúčená podmienkou „hrany v rôznej výške“) ani
    /// skrútený tile (napr. jeden roh hore) – tam neplatí ani jeden zo vzorov.
    ///
    /// Slúži ako guard pre rovné koľaje/cesty, ktoré sa smú stavať aj na rampe.
    /// </summary>
    public bool IsFaceRamp(int tileX, int tileZ)
    {
        if (TerrainManager.instance == null) return false;

        float y0 = FunctionalValueY(tileX, tileZ);     // (x,   z)
        float y1 = FunctionalValueY(tileX, tileZ + 1); // (x,   z+1)
        float y2 = FunctionalValueY(tileX + 1, tileZ + 1); // (x+1, z+1)
        float y3 = FunctionalValueY(tileX + 1, tileZ);     // (x+1, z)

        const float EPS = 0.0001f;

        // Sklon pozdĺž Z: hrana pri z je rovná (y0==y3), hrana pri z+1 je rovná
        // (y1==y2), a navzájom sú v rôznej výške.
        bool rampAlongZ = Mathf.Abs(y0 - y3) < EPS
                       && Mathf.Abs(y1 - y2) < EPS
                       && Mathf.Abs(y0 - y1) >= EPS;

        // Sklon pozdĺž X: hrana pri x je rovná (y0==y1), hrana pri x+1 je rovná
        // (y3==y2), a navzájom sú v rôznej výške.
        bool rampAlongX = Mathf.Abs(y0 - y1) < EPS
                       && Mathf.Abs(y3 - y2) < EPS
                       && Mathf.Abs(y0 - y3) >= EPS;

        return rampAlongZ || rampAlongX;
    }

    /// <summary>
    /// Vráti true, ak vrchol na grid pozícii [vertX, vertZ] je rohom aspoň
    /// jedného OBSADENÉHO tile (tileID != 0).
    ///
    /// Jeden vrchol je zdieľaný až 4 susednými tilmi:
    ///   (vertX-1, vertZ-1), (vertX, vertZ-1), (vertX-1, vertZ), (vertX, vertZ).
    /// GetTileByIndexAny ošetrí indexy mimo mriežky (vráti tileID 0).
    ///
    /// Slúži ako guard pre úpravu terénu (LevelUp/LevelDown): ak by sa
    /// upravovaný vrchol dotýkal obsadeného tile, operácia sa zablokuje –
    /// a to aj vtedy, keď hráč klikol „vedľa“ obsadeného tile, lebo vrchol
    /// patrí aj jeho face.
    /// </summary>
    public bool IsVertexOnOccupiedTile(int vertX, int vertZ)
    {
        for (int tx = vertX - 1; tx <= vertX; tx++)
            for (int tz = vertZ - 1; tz <= vertZ; tz++)
                if (GetTileByIndexAny(tx, tz).tileID != 0)
                    return true;

        return false;
    }

    /// <summary>
    /// Vráti súradnice [x, z] VŠETKÝCH staníc danej kategórie na mape.
    ///
    /// Stanica je dlaždica s tileID == 2. Železnice a cesty zdieľajú jeden
    /// tile grid, líšia sa len kategóriou (TileCategory) – preto sa pri
    /// vyhľadávaní filtruje aj podľa category:
    ///   • TileCategory.Rail → vlakové (Train) stanice,
    ///   • TileCategory.Road → vozidlové (Vehicle) stanice.
    ///
    /// Metóda jednorázovo prejde celý tileGrid (GRID_SIZE × GRID_SIZE) a
    /// vyzbiera zhodné dlaždice. tileGrid ostáva privátny – navonok dávame
    /// len read-only zoznam súradníc, takže volajúci nemôže poškodiť mapu.
    ///
    /// Slúži pre informačné UI (StatusStationsMenuUI).
    /// </summary>
    /// <param name="category">Rail = Train stanice, Road = Vehicle stanice.</param>
    public List<Vector2Int> GetAllStationCoords(TileCategory category)
    {
        var result = new List<Vector2Int>();

        // tileGrid ešte nemusí byť inicializovaný (InitializeTileEngine sa
        // volá v Start()) – vtedy vrátime prázdny zoznam.
        if (tileGrid == null)
            return result;

        for (int x = 0; x < GRID_SIZE; x++)
        {
            for (int z = 0; z < GRID_SIZE; z++)
            {
                TileData td = tileGrid[x, z];
                if (td.tileID == 2 && td.category == category)
                    result.Add(new Vector2Int(x, z));
            }
        }

        return result;
    }

    /// <summary>
    /// Celkový počet staníc danej kategórie na mape. Pohodlný getter –
    /// vnútorne využíva GetAllStationCoords (počet = Count zoznamu).
    /// </summary>
    /// <param name="category">Rail = Train stanice, Road = Vehicle stanice.</param>
    public int GetStationCount(TileCategory category)
    {
        return GetAllStationCoords(category).Count;
    }

    bool IsValidIndex(Vector2Int index)
    {
        return index.x >= 0 && index.x < GRID_SIZE &&
               index.y >= 0 && index.y < GRID_SIZE;
    }

    void UpdateTileMap(int x, int z)
    {
        TileData data = tileGrid[x, z];

        if (tileMap[x, z] != null)
        {
            Destroy(tileMap[x, z]);
            tileMap[x, z] = null;
        }

        if (data.tileID == 0)
            return;

        // ── TOVÁREŇ S MODELOM: tile je prekrytý jedným modelom celej továrne ──
        // Pre tieto tily NEVytvárame textúrový quad – pokrýva ich model továrne
        // vedený mimo tileMap (factoryModels). Platí aj pri prekreslení terénu.
        if (data.category == TileCategory.Factory &&
            factoryCoveredTiles.Contains(new Vector2Int(x, z)))
        {
            return;
        }

        // ── HLAVA MOSTA / PORTÁL TUNELA prekrytý spritom (RAIL aj ROAD) ──
        if ((data.category == TileCategory.Rail || data.category == TileCategory.Road) &&
            crossingCoveredTiles.Contains(new Vector2Int(x, z)))
        {
            return;
        }

        // ── VOLITEĽNÝ 3D MODEL / SPRITE namiesto textúrovaného quadu ──
        // Ak je pre tento konštrukčný typ (kategória + stateID) priradený
        // prefab, vytvoríme jeho inštanciu a uložíme ju do tileMap[x,z]
        // (rovnaký slot ako quad) – tým ho neskorší UpdateTileMap / Demolish
        // (tileID 0) korektne zničí. Pôvodný textúrovaný quad sa NEVYTVÁRA.
        if (TryGetTilePrefab(data, out GameObject tilePrefab, out Vector3 tileEuler,
                             out bool slopeCapable))
        {
            tileMap[x, z] = InstantiateTileModel(tilePrefab, x, z, tileEuler, slopeCapable);
            return;
        }

        GameObject go = new GameObject($"Tile_{x}_{z}");
        tileMap[x, z] = go;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();

        Mesh mesh = CreateTileMesh(x, z);
        mf.mesh = mesh;

        Material mat = new Material(snapFaceMatTex);

        // Výber textúry podľa kategórie (RAIL vs ROAD vs FACTORY) a tileID
        // (1 = trať/cesta, 2 = stanica, 3 = depo, 4 = továreň, 5 = spracovateľská)
        if (data.category == TileCategory.Road)
        {
            if (data.tileID == 1)
                mat.mainTexture = snapFaceTex_Road;
            else if (data.tileID == 2)
                mat.mainTexture = snapFaceTex_RoadStation;
            else if (data.tileID == 3)
                mat.mainTexture = snapFaceTex_RoadDepot;
        }
        else if (data.category == TileCategory.Factory)
        {
            if (data.tileID == 4)
                mat.mainTexture = snapFaceTex_Factory;
            else if (data.tileID == 5)
                mat.mainTexture = snapFaceTex_Processing;
        }
        else if (data.category == TileCategory.RailRoadCrossing)
        {
            // Zmiešaná križovatka – vlastná textúra, ak existuje, inak Rail_Tex.
            mat.mainTexture = snapFaceTex_RailRoadCrossing != null
                ? snapFaceTex_RailRoadCrossing
                : snapFaceTex_Rail;
        }
        else // Rail (alebo nezadefinované – default RAIL pre spätnú kompatibilitu)
        {
            if (data.tileID == 1)
                mat.mainTexture = snapFaceTex_Rail;
            else if (data.tileID == 2)
                mat.mainTexture = snapFaceTex_RailStation;
            else if (data.tileID == 3)
                mat.mainTexture = snapFaceTex_RailDepot;
        }

        mr.material = mat;
    }

    // =========================================================================
    // VOLITEĽNÉ 3D MODELY / SPRITY DLAŽDÍC – pomocné metódy
    // =========================================================================

    /// <summary>
    /// Dohľadá komponent <see cref="TileModelLibrary"/> s prefab-mi dlaždíc.
    /// Priorita: Inspector referencia → statická inštancia → scéna.
    /// Môže vrátiť null – vtedy sa použijú výhradne pôvodné textúry.
    /// </summary>
    TileModelLibrary ResolveTileModelLibrary()
    {
        if (tileModelLibrary != null) return tileModelLibrary;
        if (TileModelLibrary.instance != null)
        {
            tileModelLibrary = TileModelLibrary.instance;
            return tileModelLibrary;
        }
        //tileModelLibrary = FindObjectOfType<TileModelLibrary>();
        tileModelLibrary = FindAnyObjectByType<TileModelLibrary>();
        return tileModelLibrary;
    }

    /// <summary>
    /// Pre danú dlaždicu zistí, či má priradený prefab modelu/spritu.
    /// Mapuje (kategória, stateID = konštrukčný mód) na prefab cez knižnicu.
    /// FACTORY je zámerne nepodporované (mimo rozsahu) – ostáva textúra.
    /// Vráti true a naplní <paramref name="prefab"/>, ak prefab existuje.
    /// Zároveň vráti fixné pootočenie <paramref name="eulerDegrees"/> nastavené
    /// pre daný slot v TileModelLibrary – Euler uhly po 90° krokoch pre všetky
    /// tri osi (x = rovina Z-Y, y = rovina X-Z, z = rovina X-Y).
    /// </summary>
    bool TryGetTilePrefab(TileData data, out GameObject prefab, out Vector3 eulerDegrees)
    {
        return TryGetTilePrefab(data, out prefab, out eulerDegrees, out _);
    }

    /// <summary>
    /// Rozšírená verzia – navyše vráti príznak <paramref name="slopeCapable"/>,
    /// ktorý je true len pre tie 4 konštrukčné módy, ktoré sa dajú postaviť na
    /// šikmú plochu a teda sa majú preklopiť do roviny terénu:
    ///     RailHorizontal, RailVertical, RoadHorizontal, RoadVertical
    /// Pre všetky ostatné typy (križovatky, oblúky, výhybky, stanice, depá)
    /// ostáva správanie nezmenené – použije sa výhradne fixné pootočenie.
    /// </summary>
    bool TryGetTilePrefab(TileData data, out GameObject prefab, out Vector3 eulerDegrees,
                          out bool slopeCapable)
    {
        prefab = null;
        eulerDegrees = Vector3.zero;
        slopeCapable = false;

        TileModelLibrary lib = ResolveTileModelLibrary();
        if (lib == null) return false;

        if (data.category == TileCategory.Rail)
        {
            var mode = (GameManager.RailConstructionMode)data.stateID;
            prefab = lib.GetRailTilePrefab(mode);
            eulerDegrees = lib.GetRailTileEulerAngles(mode);
            slopeCapable = (mode == GameManager.RailConstructionMode.RailHorizontal
                         || mode == GameManager.RailConstructionMode.RailVertical);
        }
        else if (data.category == TileCategory.Road)
        {
            var mode = (GameManager.RoadConstructionMode)data.stateID;
            prefab = lib.GetRoadTilePrefab(mode);
            eulerDegrees = lib.GetRoadTileEulerAngles(mode);
            slopeCapable = (mode == GameManager.RoadConstructionMode.RoadHorizontal
                         || mode == GameManager.RoadConstructionMode.RoadVertical);
        }
        else if (data.category == TileCategory.RailRoadCrossing)
        {
            // Zmiešaná križovatka – vlastný slot v TileModelLibrary (len rovina).
            var mode = (LevelCrossingMode)data.stateID;
            prefab = lib.GetLevelCrossingPrefab(mode);
            eulerDegrees = lib.GetLevelCrossingEulerAngles(mode);
            slopeCapable = false;
        }

        return prefab != null;
    }

    // =========================================================================
    // SKLON DLAŽDICE – detekcia zo 4 rohových výšok terénu
    // =========================================================================

    /// <summary>Smer, ktorým dlaždica STÚPA. Klesanie na +X = stúpanie na -X.</summary>
    public enum SlopeDirection
    {
        /// <summary>Vodorovná dlaždica – žiadne preklopenie.</summary>
        Flat,
        /// <summary>Stúpa na východ (+X).</summary>
        Right,
        /// <summary>Stúpa na západ (-X).</summary>
        Left,
        /// <summary>Stúpa na sever (+Z).</summary>
        Top,
        /// <summary>Stúpa na juh (-Z).</summary>
        Bottom
    }

    /// <summary>
    /// Zistí sklon dlaždice [x,z] zo 4 rohových výšok terénu.
    ///
    ///   normal    – normála plochy dlaždice (jednotková). Pre rovinu = up.
    ///   direction – prevládajúci smer STÚPANIA (na doladenie cez tabuľku).
    ///   cosTilt   – kosínus uhla medzi normálou a zvislicou. Vodorovná = 1.
    ///               Používa sa na predĺženie modelu po šikmej dĺžke (1/cosTilt).
    ///
    /// Vráti false, ak je dlaždica v rámci tolerancie vodorovná.
    ///
    /// Gradient sa počíta ako priemerný spád protiľahlých dvojíc rohov, takže
    /// výsledok je korektný aj pre rohové (diagonálne) svahy, nielen pre 4
    /// základné smery.
    /// </summary>
    bool TryGetTileSlope(int x, int z, out Vector3 normal,
                         out SlopeDirection direction, out float cosTilt)
    {
        normal = Vector3.up;
        direction = SlopeDirection.Flat;
        cosTilt = 1f;

        float h00 = FunctionalValueY(x, z);      // ľavý-dolný roh
        float h10 = FunctionalValueY(x + 1, z);      // pravý-dolný roh
        float h01 = FunctionalValueY(x, z + 1);  // ľavý-horný roh
        float h11 = FunctionalValueY(x + 1, z + 1);  // pravý-horný roh

        // Priemerný spád pozdĺž osí (výška na 1 dlaždicu).
        float dhdx = ((h10 + h11) - (h00 + h01)) * 0.5f;
        float dhdz = ((h01 + h11) - (h00 + h10)) * 0.5f;

        if (Mathf.Abs(dhdx) < slopeDetectThreshold &&
            Mathf.Abs(dhdz) < slopeDetectThreshold)
            return false; // v tolerancii vodorovná

        // Normála plochy y = h(x,z): n = normalize(-dh/dx, 1, -dh/dz)
        normal = new Vector3(-dhdx, 1f, -dhdz).normalized;
        cosTilt = Mathf.Clamp(Vector3.Dot(normal, Vector3.up), 0.05f, 1f);

        // Prevládajúci smer stúpania – podľa väčšej zložky gradientu.
        if (Mathf.Abs(dhdx) >= Mathf.Abs(dhdz))
            direction = (dhdx > 0f) ? SlopeDirection.Right : SlopeDirection.Left;
        else
            direction = (dhdz > 0f) ? SlopeDirection.Top : SlopeDirection.Bottom;

        return true;
    }

    /// <summary>
    /// Ručné doladenie (Euler v stupňoch) pre zadaný smer stúpania.
    /// Aplikuje sa NAVYŠE k automatickému preklopeniu do roviny terénu.
    /// </summary>
    Vector3 GetSlopeExtraEuler(SlopeDirection direction)
    {
        switch (direction)
        {
            case SlopeDirection.Right: return slopeExtraEulerRight;
            case SlopeDirection.Left: return slopeExtraEulerLeft;
            case SlopeDirection.Top: return slopeExtraEulerTop;
            case SlopeDirection.Bottom: return slopeExtraEulerBottom;
            default: return Vector3.zero;
        }
    }

    /// <summary>
    /// Vytvorí inštanciu prefabu dlaždice na dlaždici [x,z].
    ///   • Pozícia = stred dlaždice, Y = priemer 4 rohových výšok terénu
    ///     (rovnako ako mesh dlaždice) + malý offset proti z-fightingu.
    ///   • Rotácia = fixné pootočenie z TileModelLibrary (0/90/180/270 okolo
    ///     každej z troch osí).
    ///     Herná orientácia variantu je naďalej daná samotným prefabom
    ///     (Horizontal/Vertical/Top/Bottom...); toto je len jednorazová
    ///     korekcia toho, ako je model v prefabe vyexportovaný.
    ///   • Mierka = (ak fitTileModelsToTile) proporčné prispôsobenie tak, aby
    ///     pôdorys X×Z zaplnil 1 dlaždicu, + vycentrovanie na stred dlaždice.
    ///   • Kolíznych komponentov sa zbavíme (rovnako ako textúrovaný quad ich
    ///     nemá), aby model neblokoval raycast terénu pri stavbe/demolácii.
    /// </summary>
    GameObject InstantiateTileModel(GameObject prefab, int x, int z, Vector3 eulerDegrees)
    {
        return InstantiateTileModel(prefab, x, z, eulerDegrees, false);
    }

    /// <summary>
    /// Rozšírená verzia – <paramref name="slopeCapable"/> zapína preklopenie
    /// modelu do roviny terénu na šikmej dlaždici (rampe).
    ///
    /// PORADIE ROTÁCIÍ (zľava sa aplikuje ako posledné):
    ///     rotation = slope * extra * Euler(fixné) * rotáciaPrefabu
    ///
    ///   1. rotáciaPrefabu – ako je model uložený v prefabe
    ///   2. Euler(fixné)   – statická korekcia z TileModelLibrary (Yaw/Pitch/Roll)
    ///                       → po nej model LEŽÍ vodorovne, správne otočený
    ///   3. extra          – ručné doladenie pre daný smer svahu (Inspector)
    ///                       → aplikuje sa v SVETOVÝCH osiach na ležiaci model,
    ///                         takže Yaw 180 ho otočí naopak, Pitch ho preklopí
    ///   4. slope          – preklopenie roviny XZ do roviny terénu
    ///
    /// Poradie je podstatné: doladenie sa robí na modeli, ktorý ešte leží
    /// vodorovne, takže sa v ňom dá uvažovať intuitívne. Až potom sa celok
    /// nakloní podľa svahu.
    /// </summary>
    GameObject InstantiateTileModel(GameObject prefab, int x, int z, Vector3 eulerDegrees,
                                    bool slopeCapable)
    {
        GameObject go = Instantiate(prefab);
        go.name = $"TileModel_{x}_{z}";

        // Rotácia MUSÍ byť aplikovaná PRED FitTileModel – pootočenie mení svetový
        // AABB modelu a podľa neho sa ďalej škáluje aj centruje pôdorys. Pri
        // preklopení cez Pitch/Roll to platí dvojnásobne: výška modelu sa vtedy
        // stane jeho pôdorysom, takže poradie nie je kozmetika.
        if (eulerDegrees.sqrMagnitude > 0.001f)
            go.transform.rotation = Quaternion.Euler(eulerDegrees) * go.transform.rotation;

        // ── ŠIKMÁ PLOCHA (rampa) ─────────────────────────────────────────
        // Len pre RailHorizontal/RailVertical/RoadHorizontal/RoadVertical.
        float rampStretch = 1f;
        if (slopeCapable &&
            TryGetTileSlope(x, z, out Vector3 normal, out SlopeDirection dir, out float cosTilt))
        {
            Vector3 extraEuler = GetSlopeExtraEuler(dir);
            if (extraEuler.sqrMagnitude > 0.001f)
                go.transform.rotation = Quaternion.Euler(extraEuler) * go.transform.rotation;

            if (alignSlopeTilesToTerrain)
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal)
                                      * go.transform.rotation;

            // Šikmá dlaždica je po svahu dlhšia než jej pôdorys (1 / cos).
            if (stretchSlopeTilesToRamp)
                rampStretch = 1f / cosTilt;

            if (logSlopeAlignment)
            {
                Debug.Log($"[IndicatrixAPI] RAMPA [{x},{z}]: stúpa {dir}, " +
                          $"sklon {Mathf.Acos(cosTilt) * Mathf.Rad2Deg:F1}°, " +
                          $"normála {normal}, doladenie {extraEuler}, " +
                          $"predĺženie ×{rampStretch:F3}");
            }
        }

        float dy = 0.01f;
        float yCenter = (FunctionalValueY(x, z)
                       + FunctionalValueY(x, z + 1)
                       + FunctionalValueY(x + 1, z + 1)
                       + FunctionalValueY(x + 1, z)) * 0.25f;

        float cx = x + 0.5f;
        float cz = z + 0.5f;
        go.transform.position = new Vector3(cx, yCenter + dy, cz);

        // Odstránime prípadné kolízne komponenty (model nemá blokovať raycast
        // terénu – pôvodný quad tiež žiadny collider nemá).
        var colliders = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) Destroy(colliders[i]);

        if (fitTileModelsToTile)
            FitTileModel(go, cx, cz, rampStretch);

        return go;
    }

    /// <summary>
    /// Proporčne (uniformne) preškáluje model tak, aby väčší z rozmerov jeho
    /// pôdorysu (X alebo Z) zodpovedal 1 dlaždici (1 svetová jednotka), a
    /// vycentruje ho v X/Z na stred dlaždice. Y (sadnutie na povrch) sa
    /// zachová – pivot prefabu by mal byť pri spodnej časti modelu.
    /// </summary>
    void FitTileModel(GameObject go, float tileCenterX, float tileCenterZ)
        => FitTileModel(go, tileCenterX, tileCenterZ, 1f);

    /// <summary>
    /// Ako vyššie, ale s dodatočným násobiteľom <paramref name="rampStretch"/>.
    ///
    /// Na šikmej dlaždici je pôdorys stále 1×1, ale samotná plocha je po svahu
    /// dlhšia – v pomere 1/cos(sklon). Keby sme model zmenšili len na pôdorys,
    /// pri napojení na susednú dlaždicu by vznikla škára. Násobiteľ ho preto
    /// uniformne zväčší na šikmú dĺžku. Pre vodorovnú dlaždicu je rovný 1 a
    /// správanie je identické s pôvodným kódom.
    ///
    /// Zväčšenie je uniformné (nie len pozdĺž spádnice), takže pri strmých
    /// svahoch model mierne presahuje aj do strán. Pri sprite-och to škáru
    /// spoľahlivo prekryje; ak by presah prekážal, prepínač
    /// stretchSlopeTilesToRamp sa dá vypnúť.
    /// </summary>
    void FitTileModel(GameObject go, float tileCenterX, float tileCenterZ, float rampStretch)
    {
        if (!TryGetCombinedBounds(go, out Bounds b)) return;

        float footprint = Mathf.Max(b.size.x, b.size.z);
        if (footprint > 1e-5f)
            go.transform.localScale *= (rampStretch / footprint);

        // Po preškálovaní re-centrovať pôdorys na stred dlaždice (pivot prefabu
        // nemusí byť v strede). Y ponecháme.
        if (TryGetCombinedBounds(go, out Bounds b2))
        {
            Vector3 p = go.transform.position;
            p.x += tileCenterX - b2.center.x;
            p.z += tileCenterZ - b2.center.z;
            go.transform.position = p;
        }
    }

    /// <summary>
    /// Spočíta spoločný (world-space) Bounds všetkých Rendererov v hierarchii.
    /// Vráti false, ak objekt nemá žiadny Renderer.
    /// </summary>
    static bool TryGetCombinedBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return false;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    // =========================================================================
    // MODELY TOVÁRNÍ (multi-tile footprint) – životný cyklus + builder
    // -------------------------------------------------------------------------
    // Volá sa z FactoryRegistry (Register / Unregister / Clear), takže pokrýva
    // ručné položenie, Load aj demoláciu jednou cestou. Ak pre typ továrne nie
    // je priradený prefab, nič sa nedeje a footprint si ponechá pôvodné textúry.
    // =========================================================================

    /// <summary>
    /// Zavolá sa po zaregistrovaní továrne (položenie / Load). Ak má daný typ
    /// továrne priradený 2D SPRITE (TileModelLibrary), vytvorí 1 sprite objekt
    /// na celý footprint (šírka podľa footprintu, billboard na kameru), prekryje
    /// footprint (skryje jeho textúry) a sprite si zaeviduje. Ak sprite
    /// priradený nie je, ponechá pôvodné textúry footprintu – presne ako pred
    /// prechodom na 2D grafiku.
    /// </summary>
    public void OnFactoryRegistered(FactoryInstance inst)
    {
        if (inst == null || inst.Definition == null) return;

        GameManager.FactoryConstructionMode mode = inst.Definition.Mode;

        TileModelLibrary lib = ResolveTileModelLibrary();
        if (lib == null)
        {
            Debug.LogWarning($"[IndicatrixAPI] Továreň '{mode}': v scéne sa nenašiel " +
                             "komponent TileModelLibrary – sprite sa nevytvorí " +
                             "(ostávajú pôvodné textúry footprintu).");
            return;
        }

        Sprite sprite = lib.GetFactorySprite(mode);
        if (sprite == null)
        {
            Debug.LogWarning($"[IndicatrixAPI] Továreň '{mode}': v TileModelLibrary NIE JE " +
                             "priradený 2D sprite pre tento typ – ostávajú pôvodné textúry " +
                             "footprintu. Skontroluj slot v Inspectore.");
            return;
        }

        var origin = new Vector2Int(inst.OriginX, inst.OriginZ);

        // Ak by na tomto origine už nejaký sprite bol (napr. re-register), zruš ho.
        RemoveFactoryModel(origin);

        // Označ tily footprintu ako "prekryté" a zruš ich prípadné textúrové
        // quady. Ak si chceš pod spritom ponechať pôvodnú textúru footprintu
        // (Factory_Tex / Processing_Tex), zapni factorySpriteGlobal.
        // keepFootprintTextures v Inspectore.
        if (factorySpriteGlobal == null || !factorySpriteGlobal.keepFootprintTextures)
        {
            for (int x = inst.OriginX; x < inst.OriginX + inst.Width; x++)
            {
                for (int z = inst.OriginZ; z < inst.OriginZ + inst.Depth; z++)
                {
                    factoryCoveredTiles.Add(new Vector2Int(x, z));
                    if (IsValidIndex(new Vector2Int(x, z)) && tileMap[x, z] != null)
                    {
                        Destroy(tileMap[x, z]);
                        tileMap[x, z] = null;
                    }
                }
            }
        }

        GameObject spriteGO = InstantiateFactorySprite(sprite, inst, mode, lib);
        factoryModels[origin] = spriteGO;
    }

    /// <summary>
    /// Zavolá sa pri odregistrovaní továrne (demolácia / Clear pred Load).
    /// Zničí sprite továrne a uvoľní prekrytie tilov footprintu. Samotné
    /// vyčistenie/obnovu dlaždíc (textúr) rieši volajúci cez ClearTileByIndex /
    /// UpdateTileMap (pri demolácii sú tily aj tak nastavené na tileID 0).
    /// </summary>
    public void OnFactoryUnregistered(FactoryInstance inst)
    {
        if (inst == null) return;

        for (int x = inst.OriginX; x < inst.OriginX + inst.Width; x++)
            for (int z = inst.OriginZ; z < inst.OriginZ + inst.Depth; z++)
                factoryCoveredTiles.Remove(new Vector2Int(x, z));

        RemoveFactoryModel(new Vector2Int(inst.OriginX, inst.OriginZ));
    }

    /// <summary>
    /// Zničí VŠETKY sprity tovární a vyčistí prekrytia (napr. pred načítaním
    /// novej mapy – FactoryRegistry.Clear). Tile textúry zostávajú v réžii
    /// UpdateTileMap (na Load sa aj tak prekresľujú).
    /// </summary>
    public void OnAllFactoriesCleared()
    {
        foreach (var kv in factoryModels)
            if (kv.Value != null) Destroy(kv.Value);

        factoryModels.Clear();
        factoryCoveredTiles.Clear();
    }

    /// <summary>Zničí a odeviduje sprite továrne na danom origine (ak existuje).</summary>
    void RemoveFactoryModel(Vector2Int origin)
    {
        if (factoryModels.TryGetValue(origin, out GameObject go))
        {
            if (go != null) Destroy(go);
            factoryModels.Remove(origin);
        }
    }

    /// <summary>
    /// Vytvorí JEDEN 2D SPRITE na celý footprint továrne.
    ///
    ///   • Sprite je izometrický obrázok továrne z TileModelLibrary (16 kusov,
    ///     jeden na typ továrne). Namiesto 3D modelu vzniká GameObject so
    ///     SpriteRendererom a komponentom FactorySpriteBillboard.
    ///   • Mierku (šírka spritu = šírka izometrického kosoštvorca footprintu),
    ///     billboard (natočenie na kameru), zvislé usadenie na predný roh
    ///     footprintu aj Z-poradie rieši FactorySpriteBillboard.Setup(...).
    ///   • Sprite NEMÁ collider – klik myšou naň neprekáža, raycast prejde na
    ///     terén a továreň sa nájde cez FactoryRegistry.GetFactoryAt (klik na
    ///     továreň → StatusFactoryMenuUI funguje presne ako doteraz).
    ///
    /// POZN. k rotácii: width/depth z FactoryInstance sú UŽ otočené rozmery
    /// footprintu (rotácia R / koliesko myši). Keďže sa sprite škáluje na
    /// AKTUÁLNY footprint, otočený obdĺžnik (D×W) sa premietne správne bez
    /// ďalšieho zásahu. Samotný obrázok sa nezrkadlí – ak by si chcel 4 pohľady
    /// na továreň, stačí do TileModelLibrary pridať 4 sloty a vyberať podľa
    /// inst.Rotation.
    /// </summary>
    GameObject InstantiateFactorySprite(Sprite sprite, FactoryInstance inst,
                                        GameManager.FactoryConstructionMode mode,
                                        TileModelLibrary lib)
    {
        var go = new GameObject($"FactorySprite_{inst.OriginX}_{inst.OriginZ}");

        // 4 rohy footprintu vo svete (vrátane výšky terénu v danom vertexe).
        int x0 = inst.OriginX;
        int z0 = inst.OriginZ;
        int x1 = inst.OriginX + inst.Width;
        int z1 = inst.OriginZ + inst.Depth;

        // POZN.: zámerne cez SafeTerrainY (nie FunctionalValueY) – footprint pri
        // okraji mapy by inak mohol vyhodiť IndexOutOfRange a sprite by vôbec
        // nevznikol (bez viditeľnej stopy okrem chyby v Console).
        float y00 = SafeTerrainY(x0, z0);
        float y10 = SafeTerrainY(x1, z0);
        float y11 = SafeTerrainY(x1, z1);
        float y01 = SafeTerrainY(x0, z1);

        Vector3[] corners =
        {
            new Vector3(x0, y00, z0),
            new Vector3(x1, y10, z0),
            new Vector3(x1, y11, z1),
            new Vector3(x0, y01, z1),
        };

        // Stred footprintu (X/Z stred, Y = priemer terénu cez rohy).
        Vector3 center = new Vector3(
            inst.OriginX + inst.Width * 0.5f,
            (y00 + y10 + y11 + y01) * 0.25f,
            inst.OriginZ + inst.Depth * 0.5f);

        // Nastavenia TEJTO továrne (mierka, zvislý posun, hĺbka) – každá zo 16
        // tovární má v TileModelLibrary vlastný blok.
        FactorySpriteSettings perFactory = (lib != null)
            ? lib.GetFactorySpriteSettings(mode)
            : FactorySpriteSettings.Default;

        var billboard = go.AddComponent<FactorySpriteBillboard>();
        billboard.Setup(sprite, corners, center, factorySpriteGlobal, perFactory);

        if (factorySpriteGlobal != null && factorySpriteGlobal.verboseLog)
            Debug.Log($"[IndicatrixAPI] Továreň '{mode}': vytvorený 2D sprite " +
                      $"'{sprite.name}' na footprint [{inst.OriginX},{inst.OriginZ}] " +
                      $"{inst.Width}×{inst.Depth}, rohy y=({y00:F2},{y10:F2},{y11:F2},{y01:F2}).");

        return go;
    }

    /// <summary>
    /// Bezpečné načítanie výšky terénu vo vertexe [x,z] – s orezaním na rozsah
    /// mapy a bez výnimky, ak TerrainManager ešte nie je pripravený. Používa sa
    /// pri stavbe spritu továrne, ktorý sa môže dotýkať okraja mapy.
    /// </summary>
    float SafeTerrainY(int x, int z)
    {
        TerrainManager tm = TerrainManager.instance;
        if (tm == null || tm.coordsF == null || tm.terrainWidth <= 0) return 0f;

        int vWidth = tm.terrainWidth + 1;
        int cx = Mathf.Clamp(x, 0, tm.terrainWidth);
        int cz = Mathf.Clamp(z, 0, tm.terrainWidth);
        int idx = cz * vWidth + cx;

        if (idx < 0 || idx >= tm.coordsF.Length) return 0f;
        return tm.coordsF[idx].y;
    }

    /// <summary>Priemerná výška terénu cez rohy celého footprintu továrne.</summary>
    float FactoryFootprintAverageY(FactoryInstance inst)
    {
        float y0 = FunctionalValueY(inst.OriginX, inst.OriginZ);
        float y1 = FunctionalValueY(inst.OriginX, inst.OriginZ + inst.Depth);
        float y2 = FunctionalValueY(inst.OriginX + inst.Width, inst.OriginZ + inst.Depth);
        float y3 = FunctionalValueY(inst.OriginX + inst.Width, inst.OriginZ);
        return (y0 + y1 + y2 + y3) * 0.25f;
    }



    Mesh CreateTileMesh(int x, int z)
    {
        Vector3[] verts = new Vector3[4];

        float dy = 0.01f;

        verts[0] = new Vector3(x, FunctionalValueY(x, z) + dy, z);
        verts[1] = new Vector3(x, FunctionalValueY(x, z + 1) + dy, z + 1);
        verts[2] = new Vector3(x + 1, FunctionalValueY(x + 1, z + 1) + dy, z + 1);
        verts[3] = new Vector3(x + 1, FunctionalValueY(x + 1, z) + dy, z);

        Mesh mesh = new Mesh();
        mesh.vertices = verts;
        mesh.triangles = new int[] { 0, 1, 2, 2, 3, 0 };
        mesh.uv = new Vector2[]
        {
        new Vector2(0,0),
        new Vector2(1,0),
        new Vector2(1,1),
        new Vector2(0,1)
        };

        mesh.RecalculateNormals();
        return mesh;
    }




    // =========================
    // LOAD / SAVE
    // =========================
    //
    // KOMPLETNÁ uložená hra (binárne, VERZIOVANÉ). Jeden súbor SaveData.dat
    // drží všetko, čo sa NEDÁ deterministicky znovu vyrobiť. Veci, ktoré sa
    // dajú dopočítať z uloženého stavu, sa zámerne NEukladajú a po načítaní sa
    // vygenerujú:
    //
    //   ── UKLADÁ SA (zdroj pravdy, hráč to ovplyvnil) ─────────────────────
    //     • terén  – reálne Y výšky všetkých vrcholov (coordsF), lebo hráč ich
    //                upravuje (TerrainVertexLevel). Náhodný seed by neobnovil
    //                ručné úpravy, preto ukladáme priamo výšky.
    //     • tile grid – tileID + stateID + category každého tilu (256×256).
    //     • konto (GameEconomy.Balance, vrátane mínusu),
    //     • herný čas (GameClock: Year/Month/Day/TotalDaysElapsed),
    //     • cenový násobiteľ (ResourcePricing.Multiplier),
    //     • ročná účtovná kniha (BudgetSystem – akumulátor prebiehajúceho roka),
    //     • TOVÁRNE (FactoryRegistry) – typ, footprint, rotácia + PREMENLIVÝ
    //       stav: množstvá/kapacity slotov, mzda, mzdový level, occupancy flag,
    //       progres výstavby,
    //     • VLAKY (TrainSystem) a VOZIDLÁ (VehicleSystem) viazané na depá –
    //       typ súpravy/vozidla, vek, náklad, priradené stanice, beh/stop.
    //     • MESTÁ (CityManager) – názov, typ, región a každá budova ako
    //       (tier, variant, tile) + priradenia názvov staníc (mesto + prípona).
    //       Modely budov sa neukladajú ako asset – ukladá sa typ a prefab sa
    //       pri Load dohľadá z CityBuildingLibrary (inak sa postaví primitíva).
    //
    //   ── NEUKLADÁ SA (dopočíta sa pri LoadGame) ──────────────────────────
    //     • mesh terénu (prekreslí sa z výšok),
    //     • textúry/GameObjekty tilov (UpdateTileMap z tile gridu),
    //     • priradenie tovární staniciam – 9×9 catchment (RescanAll),
    //     • konkrétne trasy/sub-tile pozícia vlakov a vozidiel (A* sa prepočíta;
    //       po načítaní stoja v depe a ak bežali, znovu sa rozbehnú).
    //
    // Poradie blokov je ZHODNÉ pri zápise aj čítaní. Tile engine je orchestrátor:
    // terén/tiles/továrne/skalárne stavy rieši sám, serializáciu vlakov,
    // vozidiel a knihy delegujе na príslušné systémy (každý si vlastný stav
    // serializuje sám – pozri WriteSave/ReadSave v TrainSystem/VehicleSystem/
    // BudgetSystem v companion patchi).

    private const int SAVE_MAGIC = 0x494E4458;   // "INDX"
    private const int SAVE_VERSION = 6;   // v5: RAIL mosty a tunely, v6: ROAD mosty a tunely

    // -------------------------------------------------------------------------
    // CESTA K ULOŽENEJ HRE
    // ─────────────────────────────────────────────────────────────────────────
    // Vždy JEDEN súbor: "<projekt>/Assets/SaveGame/SaveGame.dat". Save aj Load
    // pracujú výhradne s týmto súborom – Save ho VŽDY prepíše (zmaže a vytvorí
    // odznova, bez pýtania), Load číta práve jeho.
    //
    // Application.dataPath ukazuje na priečinok "Assets" projektu (v Editore),
    // takže výsledná relatívna cesta je presne "Assets/SaveGame/savegame.dat".
    // POZN.: V hotovom buildi priečinok "Assets" neexistuje – tam by bolo treba
    // použiť Application.persistentDataPath. Podľa zadania však zostávame na
    // "Assets/SaveGame/" (vývoj v Editore).
    // -------------------------------------------------------------------------
    private const string SaveDirName = "SaveGame";
    private const string SaveFileName = "SaveGame.dat";

    /// <summary>Absolútna cesta k priečinku "Assets/SaveGame".</summary>
    private static string SaveDirPath => Path.Combine(Application.dataPath, SaveDirName);

    /// <summary>Absolútna cesta k súboru "Assets/SaveGame/savegame.dat".</summary>
    public static string SaveFilePath => Path.Combine(SaveDirPath, SaveFileName);

    /// <summary>Zaistí existenciu priečinka "Assets/SaveGame".</summary>
    private static void EnsureSaveDir()
    {
        if (!Directory.Exists(SaveDirPath))
            Directory.CreateDirectory(SaveDirPath);
    }

    public void SaveGame()
    {
        EnsureSaveDir();
        string path = SaveFilePath;

        // OVERWRITE: existujúci súbor vždy zmaž a vytvor odznova (rewrite, bez
        // pýtania). FileMode.Create síce sám truncatuje, ale explicitné zmazanie
        // presne plní zadanie "zmaže a vytvorí odznova".
        if (File.Exists(path))
            File.Delete(path);

        using (BinaryWriter bw = new BinaryWriter(File.Open(path, FileMode.Create)))
        {
            // ── Hlavička ──
            bw.Write(SAVE_MAGIC);
            bw.Write(SAVE_VERSION);

            // ── 1) TERÉN (reálne Y výšky vrcholov) ──
            WriteTerrain(bw);

            // ── 2) TILE GRID ──
            for (int x = 0; x < GRID_SIZE; x++)
            {
                for (int z = 0; z < GRID_SIZE; z++)
                {
                    bw.Write(tileGrid[x, z].tileID);
                    bw.Write(tileGrid[x, z].stateID);
                    bw.Write((int)tileGrid[x, z].category);
                    // connections sa neukladá – odvodí sa pri Load zo stateID a category.
                }
            }

            // ── 3) KONTO / ČAS / CENOVÝ NÁSOBITEĽ ──
            bw.Write(GameEconomy.instance != null ? GameEconomy.instance.Balance : 0L);

            var clock = GameClock.instance;
            bw.Write(clock != null ? clock.Year : 1950);
            bw.Write(clock != null ? clock.Month : 1);
            bw.Write(clock != null ? clock.Day : 1);
            bw.Write(clock != null ? clock.TotalDaysElapsed : 0L);

            bw.Write(ResourcePricing.Multiplier);

            // ── 4) ROČNÁ ÚČTOVNÁ KNIHA ──
            if (BudgetSystem.instance != null) BudgetSystem.instance.WriteSave(bw);
            else WriteEmptyBudget(bw);

            // ── 5) TOVÁRNE ──
            WriteFactories(bw);

            // ── 6) VLAKY a VOZIDLÁ ──
            if (TrainSystem.instance != null) TrainSystem.instance.WriteSave(bw);
            else bw.Write(0);
            if (VehicleSystem.instance != null) VehicleSystem.instance.WriteSave(bw);
            else bw.Write(0);

            // ── 7) MESTÁ + NÁZVY STANÍC (pridané vo verzii 3) ──
            //   Na konci streamu, aby staršie save (v2) ostali čitateľné: Load
            //   tento blok číta len ak v súbore reálne je (EOF kontrola).
            if (CityManager.instance != null) CityManager.instance.WriteSave(bw);
            else { bw.Write(0); bw.Write(0); }   // 0 miest, 0 priradení staníc

            // ── 8) ENVIRONMENT – stromy / kamene / landing locations (verzia 4) ──
            //   Na úplnom konci streamu, aby staršie save (v2/v3) ostali čitateľné.
            if (EnvironmentManager.instance != null) EnvironmentManager.instance.WriteSave(bw);
            else bw.Write(0);   // 0 environment objektov

            // ── 9) MOSTY A TUNELY (verzia 5) ──
            //   Na úplnom konci streamu – staršie save (v2–v4) ostávajú čitateľné.
            //   Hlavy sú už v tile gride (blok 2); tu sa ukladá geometria
            //   prechodu (dĺžka, výška mostovky/tunela) a zaplatená cena.
            if (RailCrossingSystem.instance != null) RailCrossingSystem.instance.WriteSave(bw);
            else CrossingSystemBase.WriteEmptySave(bw);

            // ── 10) CESTNÉ MOSTY A TUNELY (verzia 6) ──
            //   Rovnaký formát ako blok 9, len pre RoadCrossingSystem.
            if (RoadCrossingSystem.instance != null) RoadCrossingSystem.instance.WriteSave(bw);
            else CrossingSystemBase.WriteEmptySave(bw);
        }

        Debug.Log("[IndicatrixAPI] Hra uložená do " + path);
    }

    public void LoadGame()
    {
        // Počas obnovy mapy zo save žiadne efekty výstavby / demolácie.
        suppressTileEffects = true;
        try
        {
            LoadGameCore();
        }
        finally
        {
            suppressTileEffects = false;
        }

        // Prípadné efekty spred načítania (napr. rozbehnutá demolácia) zrušiť.
        if (TerrainEditHighlight.instance != null)
            TerrainEditHighlight.instance.ClearAll();
    }

    private void LoadGameCore()
    {
        string path = SaveFilePath;
        if (!File.Exists(path))
        {
            Debug.LogWarning("[IndicatrixAPI] Save súbor neexistuje: " + path);
            return;
        }

        using (BinaryReader br = new BinaryReader(File.Open(path, FileMode.Open)))
        {
            // ── Hlavička ──
            int magic = br.ReadInt32();
            int version = br.ReadInt32();
            if (magic != SAVE_MAGIC)
            {
                Debug.LogError("[IndicatrixAPI] SaveData.dat má neznámy formát (magic) – " +
                               "načítanie zrušené.");
                return;
            }
            if (version != SAVE_VERSION)
            {
                Debug.LogWarning($"[IndicatrixAPI] SaveData.dat má verziu {version} (očakávaná " +
                                 $"{SAVE_VERSION}). Pokúsim sa načítať, no formát sa mohol zmeniť.");
            }

            // Mosty a tunely z aktuálnej hry zrušíme ešte PRED načítaním tile
            // gridu (sprity, prekrytia). Obnovia sa z bloku 9 nižšie.
            RailCrossingSystem.instance?.ClearAll();
            RoadCrossingSystem.instance?.ClearAll();

            // ── 1) TERÉN ──
            ReadTerrain(br);

            // ── 2) TILE GRID (+ prekreslenie meshu každého tilu) ──
            for (int x = 0; x < GRID_SIZE; x++)
            {
                for (int z = 0; z < GRID_SIZE; z++)
                {
                    int tileID = br.ReadInt32();
                    int stateID = br.ReadInt32();
                    TileCategory category = (TileCategory)br.ReadInt32();

                    DirectionMask conns = RebuildConnections(tileID, stateID, ref category);
                    tileGrid[x, z] = new TileData(tileID, stateID, conns, category);
                    UpdateTileMap(x, z);
                }
            }

            // ── 3) KONTO / ČAS / NÁSOBITEĽ ──
            long balance = br.ReadInt64();
            int year = br.ReadInt32();
            int month = br.ReadInt32();
            int day = br.ReadInt32();
            long totalDays = br.ReadInt64();
            int multiplier = br.ReadInt32();

            if (GameEconomy.instance != null) GameEconomy.instance.SetBalanceSigned(balance);
            if (GameClock.instance != null) GameClock.instance.LoadDate(year, month, day, totalDays);
            ResourcePricing.Multiplier = multiplier < 1 ? 1 : multiplier;

            // EconomySystem si pri štarte zapamätal lastMonth/lastYear z PÔVODNÉHO
            // (defaultného) dátumu. Po prepísaní hodín ich treba zladiť, inak by
            // najbližší denný tik mohol omylom zúčtovať "nový mesiac/rok" navyše.
            if (EconomySystem.instance != null) EconomySystem.instance.ResyncToClock();

            // ── 4) ROČNÁ KNIHA ──
            if (BudgetSystem.instance != null) BudgetSystem.instance.ReadSave(br);
            else SkipBudget(br);

            // ── 5) TOVÁRNE (vyprázdni register, potom obnov) ──
            ReadFactories(br);

            // ── 6) PRIRADENIE TOVÁRNÍ STANICIAM – dopočítané, NEukladá sa ──
            //   Tile grid + továrne sú už načítané, takže scan má z čoho zbierať.
            RailStationRegistry.RescanAll();
            RoadStationRegistry.RescanAll();

            // ── 7) VLAKY a VOZIDLÁ ──
            if (TrainSystem.instance != null) TrainSystem.instance.ReadSave(br);
            else SkipCollection(br);
            if (VehicleSystem.instance != null) VehicleSystem.instance.ReadSave(br);
            else SkipCollection(br);

            // ── 8) MESTÁ + NÁZVY STANÍC (verzia 3) ──
            //   Staršie save (v2) tento blok nemajú – číta sa len ak v súbore
            //   ešte niečo zostalo (EOF kontrola), inak sa preskočí a mestá
            //   ostanú tak, ako sa vygenerovali pri štarte.
            if (br.BaseStream.Position < br.BaseStream.Length)
            {
                if (CityManager.instance != null) CityManager.instance.LoadSave(br);
                else SkipCities(br);
            }
            else if (version >= 3)
            {
                Debug.LogWarning("[IndicatrixAPI] Save verzie 3 nemá očakávaný blok miest " +
                                 "(neúplný súbor?). Mestá ostávajú vygenerované.");
            }

            // ── 8b) ENVIRONMENT – stromy / kamene / landing locations (verzia 4) ──
            //   Staršie save (v2/v3) tento blok nemajú – číta sa len ak v súbore
            //   ešte niečo zostalo (EOF kontrola). Ak chýba, príroda ostane tak,
            //   ako sa vygenerovala pri štarte.
            if (br.BaseStream.Position < br.BaseStream.Length)
            {
                if (EnvironmentManager.instance != null) EnvironmentManager.instance.LoadSave(br);
                else SkipEnvironment(br);
            }
            else if (version >= 4)
            {
                Debug.LogWarning("[IndicatrixAPI] Save verzie 4 nemá očakávaný blok " +
                                 "environmentu (neúplný súbor?). Príroda ostáva vygenerovaná.");
            }

            // ── 8c) MOSTY A TUNELY (verzia 5) ──
            //   Staršie save (v2–v4) tento blok nemajú (EOF kontrola).
            if (br.BaseStream.Position < br.BaseStream.Length)
            {
                if (RailCrossingSystem.instance != null || version >= 5)
                    RailCrossingSystem.GetOrCreate().LoadSave(br);
                else
                    CrossingSystemBase.SkipSave(br);
            }

            // ── 8d) CESTNÉ MOSTY A TUNELY (verzia 6) ──
            //   Save v2–v5 tento blok nemajú (verzia + EOF kontrola).
            if (version >= 6 && br.BaseStream.Position < br.BaseStream.Length)
                RoadCrossingSystem.GetOrCreate().LoadSave(br);

            // ── 9) LABELY STANÍC – rekonštrukcia po Load ──
            //   Load nikdy nevolá GameManager.SetTile (ten normálne CreateLabel
            //   vyvolá pri stavbe), takže po obnovení tile gridu + miest treba
            //   labely staníc postaviť ručne. MUSÍ bežať AŽ TU, po CityManager.
            //   LoadSave(), inak GetStationDisplayName vráti len fallback názov.
            if (StationLabelManager.Instance != null)
            {
                StationLabelManager.Instance.ClearAll(); // zmaž prípadné staré/zavesené labely
                for (int x = 0; x < GRID_SIZE; x++)
                    for (int z = 0; z < GRID_SIZE; z++)
                        if (tileGrid[x, z].tileID == 2) // stanica (RAIL alebo ROAD)
                            StationLabelManager.Instance.CreateLabel(x, z);
            }

            // ── 10) SPUSTENIE SÚPRAV – AŽ TERAZ ──
            //   TrainSystem/VehicleSystem.ReadSave (blok 7) bežiace súpravy
            //   len zapamätali. Spúšťajú sa až tu, keď sú načítané mosty a
            //   tunely (bloky 8c/8d) – inak by prvý A* snapshot nemal skokové
            //   hrany a trasa cez most/tunel by sa nenašla.
            if (TrainSystem.instance != null) TrainSystem.instance.StartTrainsAfterLoad();
            if (VehicleSystem.instance != null) VehicleSystem.instance.StartVehiclesAfterLoad();
        }

        Debug.Log("[IndicatrixAPI] Hra načítaná z " + path);
    }

    // ------------------------------------------------------------------
    // POMOCNÉ – PRESKOČENIE BLOKU MIEST (keď CityManager v scéne chýba)
    // ------------------------------------------------------------------
    private void SkipCities(BinaryReader br)
    {
        // Mestá
        int cityCount = br.ReadInt32();
        for (int c = 0; c < cityCount; c++)
        {
            br.ReadString();                 // name
            br.ReadInt32();                  // type
            br.ReadInt32(); br.ReadInt32();  // region x,z
            br.ReadInt32(); br.ReadInt32();  // region w,h

            int buildings = br.ReadInt32();
            for (int b = 0; b < buildings; b++)
            {
                br.ReadInt32(); br.ReadInt32();  // tier, variant
                br.ReadInt32(); br.ReadInt32();  // x, z
            }
        }
        // Priradenia názvov staníc
        int assigns = br.ReadInt32();
        for (int i = 0; i < assigns; i++)
        {
            br.ReadInt32(); br.ReadInt32();  // x, z
            br.ReadInt32(); br.ReadInt32();  // cityIndex, suffixIndex
        }
    }

    // ------------------------------------------------------------------
    // POMOCNÉ – PRESKOČENIE BLOKU ENVIRONMENTU (keď EnvironmentManager chýba)
    // ------------------------------------------------------------------
    private void SkipEnvironment(BinaryReader br)
    {
        int count = br.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            br.ReadInt32();   // kind
            br.ReadInt32();   // variant
            br.ReadInt32();   // originX
            br.ReadInt32();   // originZ
            br.ReadInt32();   // width
            br.ReadInt32();   // depth
            br.ReadSingle();  // yawJitter
        }
    }

    // ------------------------------------------------------------------
    // POMOCNÉ – TERÉN
    // ------------------------------------------------------------------
    //
    // Ukladajú sa reálne Y výšky vrcholov (coordsF). x,z sú deterministicky
    // dané indexom (CreateMap), preto ich netreba ukladať. Po načítaní výšok
    // sa dopočíta coordsI (ConvertCoordsFI(true)) a prekreslia sa meshe
    // elementov terénu.

    private void WriteTerrain(BinaryWriter bw)
    {
        var tm = TerrainManager.instance;
        if (tm == null || tm.coordsF == null)
        {
            bw.Write(0);
            return;
        }

        bw.Write(tm.coordsF.Length);
        for (int i = 0; i < tm.coordsF.Length; i++)
            bw.Write(tm.coordsF[i].y);
    }

    private void ReadTerrain(BinaryReader br)
    {
        int count = br.ReadInt32();

        var tm = TerrainManager.instance;
        if (tm == null || tm.coordsF == null)
        {
            // Preskoč dáta (4 bajty na float), nech ostane stream zarovnaný.
            for (int i = 0; i < count; i++) br.ReadSingle();
            Debug.LogWarning("[IndicatrixAPI] TerrainManager nie je pripravený – výšky preskočené.");
            return;
        }

        if (count != tm.coordsF.Length)
        {
            Debug.LogWarning($"[IndicatrixAPI] Veľkosť terénu v save ({count}) sa nezhoduje s " +
                             $"aktuálnou ({tm.coordsF.Length}). Načítam min. spoločnú časť.");
        }

        int n = Mathf.Min(count, tm.coordsF.Length);
        for (int i = 0; i < n; i++)
        {
            float y = br.ReadSingle();
            tm.coordsF[i].y = y;
        }
        // Zvyšok (ak bol save väčší) dočítaj, nech je stream zarovnaný.
        for (int i = n; i < count; i++) br.ReadSingle();

        // Dopočítaj celočíselné úrovne z výšok a prekresli meshe.
        tm.ConvertCoordsFI(true);
        if (tm.terrainElements != null)
        {
            foreach (var e in tm.terrainElements)
                if (e != null) e.Rebuild();
        }
    }

    // ------------------------------------------------------------------
    // POMOCNÉ – TILE CONNECTIONS (odvodenie pri Load, ako v pôvodnej verzii)
    // ------------------------------------------------------------------
    private DirectionMask RebuildConnections(int tileID, int stateID, ref TileCategory category)
    {
        if (tileID == 0)
            return DirectionMask.None;

        if (category == TileCategory.Road)
            return GetConnectionsForMode((GameManager.RoadConstructionMode)stateID);

        if (category == TileCategory.Factory)
            return GetConnectionsForMode((GameManager.FactoryConstructionMode)stateID); // None

        if (category == TileCategory.RailRoadCrossing)
            return GetConnectionsForMode((LevelCrossingMode)stateID);

        // Rail (alebo None pri starších záznamoch – dorovnáme na Rail).
        if (category == TileCategory.None) category = TileCategory.Rail;
        return GetConnectionsForMode((GameManager.RailConstructionMode)stateID);
    }

    // ------------------------------------------------------------------
    // POMOCNÉ – TOVÁRNE
    // ------------------------------------------------------------------
    //
    // Ukladá sa typ (FactoryConstructionMode), footprint (origin + rozmery),
    // rotácia a PREMENLIVÝ stav. Pri Load sa register vyprázdni a každá továreň
    // sa znovu zaregistruje cez FactoryRegistry.Register (rovnaká cesta ako pri
    // ručnom položení), následne sa obnovia premenlivé hodnoty.

    private void WriteFactories(BinaryWriter bw)
    {
        var all = FactoryRegistry.All;
        bw.Write(all.Count);

        foreach (var f in all)
        {
            bw.Write((int)f.Definition.Mode);
            bw.Write(f.OriginX);
            bw.Write(f.OriginZ);
            bw.Write(f.Width);
            bw.Write(f.Depth);
            bw.Write((int)f.Rotation);

            // Premenlivý stav.
            bw.Write(f.EmployeeSalary);
            bw.Write(f.LevelSalary);
            bw.Write(f.OccupancyFlag);
            bw.Write(f.BuildElapsed);     // progres výstavby (sekundy)

            // Sloty (amount + capacity – kapacitu hráč mohol editovať).
            WriteSlots(bw, f.Load);
            WriteSlots(bw, f.UnLoad);
        }
    }

    private void ReadFactories(BinaryReader br)
    {
        FactoryRegistry.Clear();

        int count = br.ReadInt32();
        for (int k = 0; k < count; k++)
        {
            var mode = (GameManager.FactoryConstructionMode)br.ReadInt32();
            int originX = br.ReadInt32();
            int originZ = br.ReadInt32();
            int width = br.ReadInt32();
            int depth = br.ReadInt32();
            var rotation = (FactoryRotation)br.ReadInt32();

            int employeeSalary = br.ReadInt32();
            short levelSalary = br.ReadInt16();
            bool occupancy = br.ReadBoolean();
            float buildElapsed = br.ReadSingle();

            // Sloty zo save (do dočasných zoznamov, aplikujeme po Register).
            var savedLoad = ReadSlots(br);
            var savedUnLoad = ReadSlots(br);

            FactoryDefinition def = FactoryDatabase.GetDefinition(mode);
            if (def == null)
            {
                Debug.LogWarning($"[IndicatrixAPI] Neznáma továreň '{mode}' v save – preskočená.");
                continue;
            }

            var inst = FactoryRegistry.Register(def, originX, originZ, width, depth, rotation);
            if (inst == null) continue;

            inst.EmployeeSalary = employeeSalary;
            inst.LevelSalary = levelSalary;
            inst.OccupancyFlag = occupancy;

            // Obnov množstvá/kapacity slotov (poradie je deterministické z definície).
            ApplySavedSlots(inst.Load, savedLoad);
            ApplySavedSlots(inst.UnLoad, savedUnLoad);

            // Obnov progres výstavby. Register cez konštruktor začína na 0 a
            // (ak má def BuildingTime>0) v stave "vo výstavbe". AdvanceConstruction
            // dotiahne elapsed na uloženú hodnotu; pri dokončení sa flag zhodí sám.
            inst.AdvanceConstruction(buildElapsed);
        }
    }

    private static void WriteSlots(BinaryWriter bw, System.Collections.Generic.List<ResourceSlot> slots)
    {
        bw.Write(slots.Count);
        foreach (var s in slots)
        {
            bw.Write((int)s.type);
            bw.Write(s.amount);
            bw.Write(s.capacity);
        }
    }

    private struct SavedSlot { public int type; public int amount; public int capacity; }

    private static System.Collections.Generic.List<SavedSlot> ReadSlots(BinaryReader br)
    {
        int n = br.ReadInt32();
        var list = new System.Collections.Generic.List<SavedSlot>(n);
        for (int i = 0; i < n; i++)
            list.Add(new SavedSlot { type = br.ReadInt32(), amount = br.ReadInt32(), capacity = br.ReadInt32() });
        return list;
    }

    private static void ApplySavedSlots(System.Collections.Generic.List<ResourceSlot> target,
                                        System.Collections.Generic.List<SavedSlot> saved)
    {
        // Spárуj podľa typu (robustnejšie než podľa indexu, keby sa definícia
        // časom rozšírila). amount aj capacity prepíšeme zo save.
        foreach (var sv in saved)
        {
            var rt = (ResourceType)sv.type;
            foreach (var slot in target)
            {
                if (slot.type == rt)
                {
                    slot.capacity = Mathf.Max(0, sv.capacity);
                    slot.amount = Mathf.Clamp(sv.amount, 0, slot.capacity);
                    break;
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // POMOCNÉ – PRÁZDNE / PRESKOČENIE BLOKOV (keď systém v scéne chýba)
    // ------------------------------------------------------------------

    private void WriteEmptyBudget(BinaryWriter bw)
    {
        // 10 položiek knihy (pozri BudgetSystem.WriteSave) – samé nuly.
        bw.Write(0L);  // Revenue
        bw.Write(0); bw.Write(0L);  // TrainsBought, TrainsCost
        bw.Write(0); bw.Write(0L);  // VehiclesBought, VehiclesCost
        bw.Write(0); bw.Write(0L);  // FactoriesBought, FactoriesCost
        bw.Write(0L);  // TrainOperating
        bw.Write(0L);  // VehicleOperating
        bw.Write(0L);  // EmployeeSalaries
    }

    private void SkipBudget(BinaryReader br)
    {
        br.ReadInt64();
        br.ReadInt32(); br.ReadInt64();
        br.ReadInt32(); br.ReadInt64();
        br.ReadInt32(); br.ReadInt64();
        br.ReadInt64();
        br.ReadInt64();
        br.ReadInt64();
    }

    // Vlaky/vozidlá serializujú prvý int = count, potom count záznamov. Ak
    // systém v scéne nie je, nevieme bezpečne preskočiť premenlivú dĺžku –
    // ošetríme len count==0 (čo zapisuje SaveGame, keď systém chýbal).
    private void SkipCollection(BinaryReader br)
    {
        int count = br.ReadInt32();
        if (count != 0)
            Debug.LogWarning("[IndicatrixAPI] Save obsahuje vlaky/vozidlá, ale systém v scéne " +
                             "chýba – ich blok sa nedá preskočiť. Stream je odteraz nezarovnaný.");
    }

    // =========================
    // ORIGINAL SNAP FUNCTIONS
    // =========================

    public Vector3 SnapVertex(Vector3 hitPoint)
    {
        HideSnapLines();
        ShowSnapSphere();

        int snapX = 0, snapZ = 0;
        float snapY = 0;

        int xx = (int)hitPoint.x; float x = hitPoint.x - xx;
        int zz = (int)hitPoint.z; float z = hitPoint.z - zz;

        if (x >= 0.0f && x <= 0.5f) { snapX = xx; }
        else snapX = xx + 1;

        if (z >= 0.0f && z <= 0.5f) { snapZ = zz; }
        else snapZ = zz + 1;

        snapY = FunctionalValueY(snapX, snapZ);

        snapSphere.transform.position = new Vector3(snapX, snapY, snapZ);

        return new Vector3(snapX, snapY, snapZ);
    }

    public Vector3 SnapLineFace(Vector3 hitPoint)
    {
        HideSnapSphere();
        ShowSnapLines();

        Vector3[] verts = GetFaceVertices(hitPoint);

        lineRenderer01.SetPosition(0, verts[0]);
        lineRenderer01.SetPosition(1, verts[1]);

        lineRenderer02.SetPosition(0, verts[1]);
        lineRenderer02.SetPosition(1, verts[2]);

        lineRenderer03.SetPosition(0, verts[2]);
        lineRenderer03.SetPosition(1, verts[3]);

        lineRenderer04.SetPosition(0, verts[3]);
        lineRenderer04.SetPosition(1, verts[0]);

        return GetCenterFace(hitPoint);
    }

    public Vector3 SnapMeshFace(Vector3 hitPoint)
    {
        return GetCenterFace(hitPoint);
    }

    /// <summary>
    /// SnapAreaFace – snap vizuál pre VIAC-TILE footprint (továrne).
    ///
    /// Analógia k SnapLineFace, ale namiesto jednej 1×1 plochy zvýrazní
    /// obvod celého obdĺžnika N×M tilov, do ktorého sa továreň umiestni.
    /// Hit point určuje anchor tile; obdĺžnik sa dopočíta cez FactoryFootprint
    /// rovnako ako v SetTile (FACTORY verzia), takže náhľad presne zodpovedá
    /// tomu, čo sa po kliknutí položí.
    ///
    /// Používa tie isté 4 line renderery ako SnapLineFace – vykreslí nimi
    /// 4 strany veľkého obdĺžnika namiesto malého tilu.
    ///
    /// PARAMETER rotation: footprint sa zvýrazní UŽ otočený, takže náhľad
    /// presne zodpovedá tomu, čo SetTile s rovnakou rotáciou položí.
    /// Default = Deg0.
    /// </summary>
    public Vector3 SnapAreaFace(Vector3 hitPoint, GameManager.FactoryConstructionMode mode,
                                FactoryRotation rotation = FactoryRotation.Deg0)
    {
        HideSnapSphere();
        ShowSnapLines();

        Vector2Int anchor = SnapTileIndex(hitPoint);
        FactoryFootprint fp = GetFactoryFootprint(mode, rotation);

        // Ľavý-dolný roh footprintu (rovnaký výpočet ako v SetTile).
        int minX = anchor.x - fp.anchorX;
        int minZ = anchor.y - fp.anchorZ;
        int maxX = minX + fp.width;   // +width  → pravá hrana posledného tilu
        int maxZ = minZ + fp.depth;   // +depth  → horná hrana posledného tilu

        float dy = 0.02f;

        // 4 rohy veľkého obdĺžnika (proti smeru hod. ručičiek).
        Vector3 c0 = new Vector3(minX, FunctionalValueY(minX, minZ) + dy, minZ);
        Vector3 c1 = new Vector3(minX, FunctionalValueY(minX, maxZ) + dy, maxZ);
        Vector3 c2 = new Vector3(maxX, FunctionalValueY(maxX, maxZ) + dy, maxZ);
        Vector3 c3 = new Vector3(maxX, FunctionalValueY(maxX, minZ) + dy, minZ);

        lineRenderer01.SetPosition(0, c0);
        lineRenderer01.SetPosition(1, c1);

        lineRenderer02.SetPosition(0, c1);
        lineRenderer02.SetPosition(1, c2);

        lineRenderer03.SetPosition(0, c2);
        lineRenderer03.SetPosition(1, c3);

        lineRenderer04.SetPosition(0, c3);
        lineRenderer04.SetPosition(1, c0);

        // Geometrický stred footprintu (užitočné pre prípadné umiestnenie
        // 3D modelu továrne neskôr).
        float cx = (minX + maxX) * 0.5f;
        float cz = (minZ + maxZ) * 0.5f;
        return new Vector3(cx, FunctionalValueY(anchor.x, anchor.y), cz);
    }

    Vector3[] GetFaceVertices(Vector3 hitPoint)
    {
        int xx = Mathf.FloorToInt(hitPoint.x);
        int zz = Mathf.FloorToInt(hitPoint.z);

        float dy = 0.01f;

        Vector3[] verts = new Vector3[4];

        verts[0] = new Vector3(xx, FunctionalValueY(xx, zz) + dy, zz);
        verts[1] = new Vector3(xx, FunctionalValueY(xx, zz + 1) + dy, zz + 1);
        verts[2] = new Vector3(xx + 1, FunctionalValueY(xx + 1, zz + 1) + dy, zz + 1);
        verts[3] = new Vector3(xx + 1, FunctionalValueY(xx + 1, zz) + dy, zz);

        return verts;
    }

    public Vector3 GetCenterFace(Vector3 hitPoint)
    {
        int xx = Mathf.FloorToInt(hitPoint.x);
        int zz = Mathf.FloorToInt(hitPoint.z);

        float dx = xx + 0.5f;
        float dz = zz + 0.5f;
        float dy = FunctionalValueY(xx, zz);

        return new Vector3(dx, dy, dz);
    }

    float FunctionalValueY(int snapX, int snapZ)
    {
        int width = TerrainManager.instance.terrainWidth + 1;

        int index = snapZ * width + snapX;

        return TerrainManager.instance.coordsF[index].y;
    }


}