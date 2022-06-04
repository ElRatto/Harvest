using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Default.EXtensions.CachedObjects;
using Default.EXtensions.Positions;
using DreamPoeBot.Loki.Bot;
using DreamPoeBot.Loki.Game;
using DreamPoeBot.Loki.Game.GameData;
using DreamPoeBot.Loki.Game.Objects;
using DreamPoeBot.Common;
using System.Windows.Forms;

namespace Default.EXtensions.Global
{
    //Metadata/NPC/Epilogue/EnvoyRandom envoy
    public class CombatAreaCache
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
        private static readonly Interval ScanInterval = new Interval(200);
        private static readonly Interval ItemScanInterval = new Interval(25);


        private static readonly List<PickupEvalHolder> PickupEvaluators = new List<PickupEvalHolder>();

        private static readonly Dictionary<uint, CombatAreaCache> Caches = new Dictionary<uint, CombatAreaCache>();

        private CombatAreaCache _incursionSubCache;
        private CombatAreaCache _harvestnSubCache;
        private CombatAreaCache _zanaMapSubCache;
        private CombatAreaCache _syndicateHOSubCache;

        public static bool IsInIncursion { get; set; }
        public static bool IsInHarvest { get; set; }
        public static bool IsInZanaMap { get; set; }
        public static bool IsInBetrayal { get; set; }
        public static bool IsHeisting { get; set; }
        public static bool ZanaCompleted { get; set; }
        public static bool SyndHOCompleted { get; set; } = false;

        public static CombatAreaCache Current
        {
            get
            {
                var hash = LokiPoe.LocalData.AreaHash;
                if (Caches.TryGetValue(hash, out var cache) )
                {
                    cache._lastAccessTime.Restart();

                    if (IsInIncursion)
                        return cache._incursionSubCache ?? (cache._incursionSubCache = new CombatAreaCache(hash));
                    if (IsInHarvest)
                        return cache._harvestnSubCache ?? (cache._harvestnSubCache = new CombatAreaCache(hash));
                    if (IsInZanaMap)
                        return cache._zanaMapSubCache ?? (cache._zanaMapSubCache = new CombatAreaCache(hash));
                    if (IsInBetrayal)
                        return cache._syndicateHOSubCache ?? (cache._syndicateHOSubCache = new CombatAreaCache(hash));

                    return cache;
                }
                RemoveOldCaches();
                var newCache = new CombatAreaCache(hash);
                Caches.Add(hash, newCache);
                return newCache;
            }
        }

        public uint Hash { get; }
        public DatWorldAreaWrapper WorldArea { get; }
        public ComplexExplorer Explorer { get; }
        public int DeathCount { get; internal set; }
        public int StuckCount { get; internal set; }

        public bool GoToTown = false;

        public bool AltIsPressed = false;

        private bool _stopatenvoy = false;

        //mein ArchNemesis -> durch das von keks ersetzt
        public readonly List<CachedObject> ANMonsters = new List<CachedObject>();
        public bool AN_completed = false;
        public bool AN_ihavemods = true;
        public string AN_recipe_in_progress = "";
        public string AN_FirstMod = "";
        public string AN_SecondMod = "";
        public string AN_ThirdMod = "";
        public string AN_FourthMod = "";
        public int AN_stage = 1;
        public int AN_recipelength = 0;
        public bool ArchnemesisActive { get; set; }

        //Archnemesislogging
        public bool StartedANRecipe = false;

        //Animate Guardian
        public float AG_health_percent = 0;


        //Heist
        public readonly List<CachedObject> HeistEscapeRoutes = new List<CachedObject>();
        public readonly List<CachedObject> HeistHazards = new List<CachedObject>();
        public readonly List<CachedObject> HeistDoors = new List<CachedObject>();
        public readonly List<CachedObject> HeistChests = new List<CachedObject>();
        public readonly List<CachedObject> HeistNPCChests = new List<CachedObject>();
        public readonly List<CachedObject> HeistTargets = new List<CachedObject>();
        public readonly List<CachedObject> HeistPortals = new List<CachedObject>();
        public readonly List<CachedObject> HeistVaults = new List<CachedObject>();


        //Ritual
        public bool HasRitual = false;
        public bool LootRitual = false;
        public NetworkObject ActiveRitual = null;
        public readonly List<Monster> ActiveRitualMonsters = new List<Monster>();
        public bool RitualActive { get; set; }
        public bool EinRitualBeendet = false;
        public int VerbleibendeRituale = 0;
        public int AusgesiebteRituale = 0;
        public bool RitualRerolled = false;

        //legion
        public bool HasLegion = false;
        public NetworkObject ActiveLegion = null;
        public bool ActivatedLegion = false;
        public bool LegionActive { get; set; }
        public readonly List<CachedObject> FrozenLegionBosses = new List<CachedObject>();
        public readonly List<CachedObject> OpenedLegionBosses = new List<CachedObject>();

        //betrayal
        public bool HasBetrayal = false;

        //sentinel
        //public bool ActivatedRedSentinel = false;

        //Metamorph
        public bool HasMetamorph = false;
        //public bool MetamorphActive = false;
        //public Monster ActiveMetamorph = null;
        //public WalkablePosition LastMetamorphPos = null;


        public bool CalledMetamorph = false;
        public bool BuiltMetamorph = false;
        //public NetworkObject MetamorphMachine = null;

        //zana
        public bool ZanaResetComplete = false;
        public bool ZanaMapCompleted = false;
        public DatWorldAreaWrapper FirstMap = null;

        //alva

        //expedition
        public bool HasExpedition = false;
        public bool StartedExpedition = false;
        public readonly List<CachedObject> ExpedExploder = new List<CachedObject>();
        public readonly List<CachedObject> ExpedMarkers = new List<CachedObject>();
        public readonly List<CachedObject> ExpedRelics = new List<CachedObject>();
        public readonly List<CachedObject> ExpedChests = new List<CachedObject>();

        //blight
        public readonly List<CachedObject> BlightPump = new List<CachedObject>();
        public bool BlightActive = false;
        public bool ActivatedPump = false;
        public NetworkObject ActivePump = null;
        public BlightDefensiveTower FirstTower = null;

        public WalkablePosition RandomPos = null;        

        //items
        public readonly List<CachedWorldItem> Items = new List<CachedWorldItem>();
        //public readonly Dictionary<int, CachedWorldItem> NewItems = new Dictionary<int, CachedWorldItem>();
        //public readonly List<CachedWorldItem> LastItems = new List<CachedWorldItem>();

        //harvest
        public readonly List<CachedObject> HarvestIrrigators = new List<CachedObject>();
        public readonly List<CachedObject> HarvestExtractors = new List<CachedObject>();

        public readonly List<CachedObject> ThingsToAvoid = new List<CachedObject>();

        public readonly List<CachedObject> Chests = new List<CachedObject>();
        public readonly List<CachedObject> Levers = new List<CachedObject>();
        public readonly List<CachedObject> VeinsnStuff = new List<CachedObject>();
        public readonly List<CachedObject> Essences = new List<CachedObject>();
        public readonly List<CachedObject> CRecipe = new List<CachedObject>();
        public readonly List<CachedObject> RitualShrines = new List<CachedObject>();
        public readonly List<CachedObject> Breaches = new List<CachedObject>();
        public readonly List<CachedObject> Legions = new List<CachedObject>();
        public readonly List<CachedObject> LegionChests = new List<CachedObject>();
        public readonly List<CachedObject> SmugglerChests = new List<CachedObject>();
        public readonly List<CachedObject> BetrayalDudes = new List<CachedObject>();
        public readonly List<CachedObject> SyndHO = new List<CachedObject>();
        public readonly List<CachedObject> SyndHOExits = new List<CachedObject>();

        public readonly List<CachedObject> Harvest = new List<CachedObject>();
        public readonly List<CachedObject> Delirium = new List<CachedObject>();
        public readonly List<CachedObject> AbyssStart = new List<CachedObject>();
        public readonly List<CachedObject> Zana = new List<CachedObject>();
        public readonly List<CachedObject> Alvas = new List<CachedObject>();
        public readonly List<CachedObject> IncursionExits = new List<CachedObject>();
        public readonly List<CachedObject> SpecialChests = new List<CachedObject>();
        public readonly List<CachedStrongbox> Strongboxes = new List<CachedStrongbox>();
        public readonly List<CachedObject> Shrines = new List<CachedObject>();


        public readonly List<CachedObject> Monsters = new List<CachedObject>();
        
        public readonly Dictionary<int, CachedObject> NewMonsters = new Dictionary<int, CachedObject>();

        public readonly List<CachedTransition> AreaTransitions = new List<CachedTransition>();
        public readonly ObjectDictionary Storage = new ObjectDictionary();

        public readonly HashSet<int> _processedObjects = new HashSet<int>();

        //keep this in a separate collection to reset on ItemEvaluatorRefresh
        public readonly HashSet<int> _processedItems = new HashSet<int>();

        private readonly Stopwatch _lastAccessTime;
        private bool hasswitched = false;

        //dangerdodger-stuff
        //public readonly Dictionary<CachedObject, DateTime> TemporaryDangers = new Dictionary<CachedObject, DateTime>();
        public readonly List<TempDanger> NewTempDangers = new List<TempDanger>();
        public readonly Dictionary<int, CachedObject> PermanentDangers = new Dictionary<int, CachedObject>();


        static CombatAreaCache()
        {
            ItemEvaluator.OnRefreshed += OnItemEvaluatorRefresh;
            ComplexExplorer.LocalTransitionEntered += OnLocalTransitionEntered;
            BotManager.OnBotChanged += (sender, args) => Caches.Clear();
        }

        private CombatAreaCache(uint hash)
        {
            GlobalLog.Info($"[CombatAreaCache] Creating cache for \"{World.CurrentArea.Name}\" (hash: {hash})");
            Hash = hash;
            WorldArea = World.CurrentArea;
            Explorer = new ComplexExplorer();
            _lastAccessTime = Stopwatch.StartNew();
            MapBot.FinalThingsInMapTask._hasstoppedonce = false;
        }

        private static void RemoveOldCaches()
        {
            var toRemove = Caches.Where(c => c.Value._lastAccessTime.Elapsed > Lifetime).Select(c => c.Value).ToList();
            foreach (var cache in toRemove)
            {
                GlobalLog.Info($"[CombatAreaCache] Removing cache for \"{cache.WorldArea.Name}\" (hash: {cache.Hash}). Last accessed {(int) cache._lastAccessTime.Elapsed.TotalMinutes} minutes ago.");
                Caches.Remove(cache.Hash);
            }
        }

        private static void RemoveAllCaches()
        {
            var toRemove = Caches.Values.ToList();
            foreach (var cache in toRemove)
            {
                GlobalLog.Info($"[CombatAreaCache] Removing cache for \"{cache.WorldArea.Name}\" (hash: {cache.Hash}). Last accessed {(int)cache._lastAccessTime.Elapsed.TotalMinutes} minutes ago.");
                Caches.Remove(cache.Hash);
            }
        }

        public static bool AddPickupItemEvaluator(string id, Func<Item, bool> evaluator)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));

            if (evaluator == null)
                throw new ArgumentNullException(nameof(evaluator));

            if (PickupEvaluators.Exists(e => e.Id == id))
                return false;

            PickupEvaluators.Add(new PickupEvalHolder(id, evaluator));
            return true;
        }

        public static bool RemovePickupItemEvaluator(string id)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));

            var index = PickupEvaluators.FindIndex(e => e.Id == id);

            if (index < 0)
                return false;

            PickupEvaluators.RemoveAt(index);
            return true;
        }

        public static void Tick()
        {
            if (!LokiPoe.IsInGame || LokiPoe.Me.IsDead || !World.CurrentArea.IsCombatArea || World.CurrentArea.Name == "The Rogue Harbour")
                return;

            Current.OnTick();
        }

        private void OnTick()
        {
            Current.Explorer.Tick();

            if (!IsHeisting && BotManager.Current.Name.Contains("HeistBot"))
            {
                GlobalLog.Warn($"[CombatAreaCache] Heist Bot am laufen...");
                IsHeisting = true;
                //IsHeisting = true;
            }

            if (ItemScanInterval.Elapsed)
            {
                if (MapBot.FinishMapTask.LastLoot && !hasswitched && !CombatAreaCache.IsInZanaMap)
                {
                    GlobalLog.Warn($"[CombatAreaCache] Switch Evaluator");
                    ItemEvaluator.Instance = LastLootItemEvaluator.Instance;
                    ItemEvaluator.Refresh();
                    hasswitched = true;

                    if (Settings.Instance.UseAltForLastLoot)
                    {
                        GlobalLog.Info("[DisableAlwaysHighlight] Now pressing alt");
                        AltIsPressed = true;
                        //LokiPoe.Input.SimulateKeyEvent(Keys.Alt, true, false, false);
                        //LokiPoe.Input.SimulateKeyEvent(LokiPoe.Input.Binding.highlight_toggle
        
                        LokiPoe.ProcessHookManager.SetKeyState(LokiPoe.Input.Binding.highlight, -32768); //alt wird gerückt
                    }
                }
 
                WorldItemScan();
            }

            if (ScanInterval.Elapsed)
            {
                UpdateMonsters();
                //UpdateDangerousStuff();


                foreach (var obj in LokiPoe.ObjectManager.Objects)
                {
                    if (!IsHeisting)
                    {
                        var mobi = obj as ArchnemesisTrappedMonster;
                        if (mobi != null)
                        {
                            ProcessANMonster(obj);
                            continue;
                        }

                        //ag health
                        if (obj.Metadata.Equals("Metadata/Monsters/AnimatedItem/AnimatedArmour") && obj.Distance < 150 && !(obj as Monster).IsAliveHostile)
                        {
                            AG_health_percent = (obj as Monster).HealthPercent;
                            continue;
                        }

                        if (obj.Name.Equals("The Envoy") && _stopatenvoy)
                        {
                            _stopatenvoy = false;
                            BotManager.Stop();
                            continue;
                        }


                        //volatile
                        if (obj.Name.Equals("Volatile Core"))
                        {
                            //GlobalLog.Error($"CAC VOLATILE ALARM {obj.Metadata}");
                            if (_processedObjects.Contains(obj.Id))
                                continue;

                            var mon = obj as Monster;
                            if (mon.CurrentAction?.Skill != null)
                            {
                                if (mon.CurrentAction.Skill.Name == "suicide_explosion")
                                {
                                    GlobalLog.Error($"CAC Versuche Suicide Explosion zu evaden");

                                    ProcessTempDangers(obj, 50, 3000);
                                }


                                //GlobalLog.Error($"volatile skillname {mon.CurrentAction.Skill.Name}");
                            }
                            continue;
                        }

                        /*
                        if (MetadataToAvoid.Contains(obj.Metadata) || obj.Metadata.Contains("ClientOnlyGroundEffect"))
                        {
                            if (obj.Metadata.Contains("ClientOnlyGroundEffect"))
                            {
                                if (obj.AnimatedPropertiesMetadata != null)
                                {
                                    foreach (var grouneffect in GroundEffectMetadata)
                                    {
                                        if (obj.AnimatedPropertiesMetadata.Contains(grouneffect))
                                        {
                                            ProcessTempDangers(obj, 50, 3000);

                                        }
                                    }
                                    foreach (var spelleffect in GroundEffectMetadata)
                                    {
                                        if (obj.AnimatedPropertiesMetadata.Contains(spelleffect))
                                        {
                                            ProcessDangerousStuff(obj);

                                        }
                                    }
                                }
                                continue;

                            }

                            ProcessDangerousStuff(obj);
                            continue;

                        }*/

                        
                        foreach (var meta in MetadataToAvoid)
                        {
                            if (obj.Metadata.Contains(meta))
                            {

                                GlobalLog.Error($"AAAAAAAAAAAAA {obj.Metadata} contains {meta}");

                                ProcessDangerousStuff(obj);
                                continue;

                            }
                        }
                        /*

                        if (obj.Metadata.Contains("ClientOnlyGroundEffect"))
                        {

                            if (obj.AnimatedPropertiesMetadata != null)
                            {
                                
                                foreach (var spelleffect in GroundEffectMetadata)
                                {
                                    if (obj.AnimatedPropertiesMetadata.Contains(spelleffect))
                                    {
                                        ProcessTempDangers(obj, 50, 3000);

                                    }
                                }
                            }
                            continue;
                        }*/

                        //alle anderen monster

                        


                        var mob = obj as Monster;
                        if (mob != null)
                        {
                            ProcessMonster(mob);
                            continue;
                        }

                        /*
                        //groundeffects
                        if (obj.Metadata.Contains("ClientOnlyGroundEffect"))
                        {

                            _processedObjects.Add(obj.Id);
                            continue;

                        }*/

                        //ritual
                        if (obj.Metadata.Equals("Metadata/Terrain/Leagues/Ritual/RitualRuneInteractable"))
                        {
                            ProcessRitualShrine(obj);
                            if (!HasRitual)
                            {
                                HasRitual = true;
                                MapBot.Statistics.Instance.rituals++;
                            }
                            continue;
                        }

                        //betrayal
                        //Metadata/Monsters/LeagueBetrayal/BetrayalCameria
                        if (obj.Metadata.Contains("/Leagues/Betrayal/Objects/BetrayalMakeChoice"))
                        {

                            ProcessBetrayalDude(obj);
                            continue;
                        }
                        if (obj.Name.Equals("Syndicate Laboratory"))
                        {
                            var ho = obj as AreaTransition;
                            ProcessSyndHO(ho);
                            continue;
                        }

                        if (IsInBetrayal)
                        {
                            if (obj.Metadata.Contains("AreaTransitionMapMarker") || obj.Metadata.Contains("PortalToggleable"))
                            {

                                var ho = obj as AreaTransition;
                                ProcessSyndHOExits(ho);
                                continue;
                            }
                        }

                        //heist
                        //Metadata/Chests/LeagueHeist/HeistSmugglersCoinCache
                        if (obj.Metadata.Contains("HeistSmugglers") || obj.Metadata.Contains("SentinelCache"))
                        {
                            var sc = obj as Chest;
                            ProcessSmugglers(sc);
                            continue;
                        }

                        //delve
                        if (obj.Metadata.EndsWith("DelveMineralVein"))
                        {
                            ProcessVein(obj);
                            continue;
                        }

                        /*
                        //archnemesis
                        if (obj.Metadata.Contains("/Leagues/Archnemesis/Objects/ArchnemesisDoodad"))
                        {
                            ProcessANMonster(obj);
                            continue;
                        }
                        */

                        //essence
                        if (obj.Metadata.Equals("Metadata/MiscellaneousObjects/Monolith"))
                        {
                            ProcessEssence(obj);
                            continue;
                        }

                        //legion
                        if (obj.Metadata.Equals("Metadata/Terrain/Leagues/Legion/Objects/LegionInitiator"))
                        {
                            if (HasLegion == false)
                            {
                                HasLegion = true;
                                MapBot.Statistics.Instance.legions++;
                                GlobalLog.Error("[CombatAreaCache] Legion detected");
                            }
                            ProcessLegions(obj);
                            continue;
                        }

                        //legionchests
                        if (obj.Metadata.Contains("LegionChest") || obj.Metadata.Contains("/Chests/Blight") || obj.Metadata.Contains("AbyssFinalChest"))  //AbyssFinalChest
                        {
                            var lchest = obj as Chest;
                            ProcessLegionChest(lchest);
                            continue;
                        }


                        //breach
                        if (obj.Metadata.Equals("Metadata/MiscellaneousObjects/Breach/BreachObject") || obj.Metadata.Equals("Metadata/Terrain/Leagues/Ritual/RitualRuneLight") || (!IsInIncursion && obj.Metadata.Contains("Objects/IncursionPortal")))
                        {
                            ProcessBreach(obj);
                            continue;
                        }

                        //delirium mirror
                        if (obj.Metadata.Equals("Metadata/MiscellaneousObjects/Affliction/AfflictionInitiator"))
                        {
                            ProcessDelirium(obj);
                            continue;
                        }

                        //abyss start
                        if (obj.IsAbyssStartNode)
                        {
                            ProcessAbyss(obj);
                            continue;
                        }

                        //blight
                        if (obj.Metadata.Equals("Metadata/Terrain/Leagues/Blight/Objects/BlightPump"))
                        {
                            ProcessBlight(obj);
                            continue;
                        }
                        //cassia: Metadata/Monsters/Masters/BlightBuilderWild
                        //start: Metadata/Terrain/Leagues/Blight/Objects/BlightPump
                        //Metadata/Monsters/LeagueBlight/BlightFoundation

                        //expedition detonator
                        if (obj.Metadata.Equals("Metadata/MiscellaneousObjects/Expedition/ExpeditionDetonator"))
                        {

                            if (_processedObjects.Contains(obj.Id))
                                continue;

                            if (HasExpedition == false)
                            {
                                HasExpedition = true;
                                GlobalLog.Error("[CombatAreaCache] Expedition detected");
                            }

                            //rog check
                            var rog = LokiPoe.ObjectManager.Objects.FirstOrDefault<Monster>(a => a.Metadata.Contains("ExpeditionRog") && a.Position.Distance(obj.Position) <= 100);

                            if (rog != null)
                            {
                                HasExpedition = false;
                                _processedObjects.Add(obj.Id);
                                GlobalLog.Error("[CombatAreaCache] Mission abbrechen - es ist nur Rog");
                                continue;
                            }

                            ProcessExpedExploder(obj);
                            continue;
                        }

                        if (obj.Metadata.Contains("/Chests/LeaguesExpedition"))
                        {
                            var ec = obj as Chest;
                            ProcessExpedChests(ec);
                            continue;
                        }

                        if (HasExpedition)
                        {

                            if (obj.Metadata.Equals("Metadata/MiscellaneousObjects/Expedition/ExpeditionRelic"))
                            {
                                ProcessRelics(obj);
                                continue;
                            }

                        }


                        //harvest
                        /*
                        if (obj.Metadata.Equals("Metadata/Terrain/Leagues/Harvest/Objects/HarvestPortalToggleableReverse"))
                        {
                            ProcessHarvest(obj);
                            continue;
                        }
                        */

                        //metamorph

                        //var hasMMorph = LokiPoe.TerrainData.TgtEntries.Any<LokiPoe.TerrainDataEntry>(t => t != null && t.TgtName.Contains("Metamorph"));

                        // vat? Metadata/MiscellaneousObjects/Metamorphosis/MetamorphosisVat
                        if (HasMetamorph == false && (obj.Metadata.Equals("Metadata/NPC/League/Metamorphosis/MetamorphosisNPCWild") || LokiPoe.InGameState.MetamorphSummonUi.IsOpened))
                        {
                            HasMetamorph = true;
                            MapBot.Statistics.Instance.metamorphs++;
                            GlobalLog.Error("[CombatAreaCache] Metamorph detected");
                            continue;
                        }

                        //zana
                        if (obj.Metadata.Equals("Metadata/NPC/Missions/Wild/StrDexInt"))
                        {
                            ProcessZana(obj);
                            continue;
                        }

                        //alva
                        if (obj.Metadata.Equals("Metadata/NPC/League/Incursion/TreasureHunterWild"))
                        {
                            ProcessAlva(obj);
                            continue;
                        }

                        //incursionexits
                        if (IsInIncursion && obj.Metadata.Contains("IncursionPortal"))
                        {
                            ProcessIncursionExit(obj);
                            continue;
                        }

                        //harvestplants
                        if (IsInHarvest && (obj.Metadata.Contains("Irrigator") || obj.Metadata.Contains("Extractor")))
                        {
                            ProcessHarvestPlants(obj);
                            continue;
                        }

                        //lab levers
                        //Metadata/Terrain/EndGame/MapLaboratory/Objects/Switch_Once_Laboratory_3
                        //wird untargetable
                        if (obj.Metadata.Contains("Switch_Once"))
                        {
                            ProcessLevers(obj);
                            continue;
                        }

                        //crafting recipe
                        if (obj.Metadata.Contains("Metadata/Terrain/Missions/CraftingUnlocks/"))
                        {
                            ProcessCraftingRecipe(obj);
                            continue;
                        }

                        //explosives
                        if (obj.Name.Contains("Metadata/Effects/Spells/monsters_effects/elemental_beacon") || obj.Metadata.Contains("architect_ground_blood"))
                        {
                            ProcessTempDangers(obj, 40, 3000);
                        }


                        var chest = obj as Chest;
                        if (chest != null)
                        {
                            if (IsSpecialChest(chest))
                            {
                                ProcessSpeacialChest(chest);
                                continue;
                            }
                            if (chest.IsStrongBox)
                            {
                                ProcessStrongbox(chest);
                                continue;
                            }
                            ProcessChest(chest);
                            continue;
                        }

                        var shrine = obj as Shrine;
                        if (shrine != null)
                        {
                            ProcessShrine(shrine);
                            continue;
                        }

                        var transition = obj as AreaTransition;
                        if (transition != null)
                        {
                            ProcessTransition(transition);
                        }
                    }                   
                    else //HEISTING
                    {
                        var mob = obj as Monster;
                        if (mob != null)
                        {
                            ProcessMonster(mob);
                            continue;
                        }

                        var transition = obj as AreaTransition;
                        if (transition != null)
                        {
                            ProcessTransition(transition);
                        }

                        //heist chests
                        //Metadata/Chests/LeagueHeist/HeistChestSecondaryCurrencyMilitary
                        //Metadata/Chests/LeagueHeist/HeistChestSecondaryDivinationCardsMilitary
                        //spezielle chests zum v-öffnen
                        //Metadata/Chests/LeagueHeist/HeistChestRewardRoomLockPickingCurrencyMilitary
                        var chest = obj as Chest;
                        if (chest != null)
                        {
                            ProcessChest(chest);
                            continue;
                        }


                        //heist doors??
                        if (obj.Metadata.Contains("Metadata/Terrain/Leagues/Heist/Objects/Level/Door_NPC"))
                        {
                            ProcessHeistDoors(obj);
                            continue;
                        }

                        //heist doors??
                        if (obj.Metadata.Contains("Metadata/Terrain/Leagues/Heist/Objects/Level/Vault"))
                        {
                            ProcessHeistVaults(obj);
                            continue;
                        }

                        //heist traps??
                        if (obj.Metadata.Contains("Metadata/Terrain/Leagues/Heist/Objects/Level/Hazards/"))
                        {
                            ProcessHeistHazards(obj);
                            continue;
                        }

                        
                        //heist targets??
                        if (obj.Metadata.Contains("Metadata/MiscellaneousObjects/Heist/CurioDisplayRoomMarker"))
                        {
                            ProcessHeistTargets(obj);
                            continue;
                        }


                        //heist npc chests

                        if (obj.Metadata.Contains("Metadata/Chests/LeagueHeist/HeistChestRewardRoom"))
                        {
                            ProcessHeistNPCChests(obj);
                            continue;
                        }


                    }


                }
                
            }
        }

        public static bool IsPoeFiltered(WorldItem worlditem) => worlditem.Components.RenderComponent.InteractCenterWorld == Vector3.Zero;


        private void WorldItemScan()
        {
            foreach (var obj in LokiPoe.ObjectManager.Objects)
            {

                var worldItem = obj as WorldItem;

                if (worldItem == null)
                    continue;

                if (Settings.Instance.OnlyLootFilterCanSee && IsPoeFiltered(worldItem))
                    continue;

				var id = worldItem.Id;

				if (_processedItems.Contains(id))
					continue;

				if (worldItem.IsAllocatedToOther && DateTime.Now < worldItem.PublicTime)
                    continue;

                var item = worldItem.Item;

                if ((Settings.Instance.LootVisibleItems && worldItem.HasVisibleHighlightLabel) ||
                    ItemEvaluator.Match(item, EvaluationType.PickUp) ||
                    PickupEvaluators.Exists(e => e.Eval(item)))
                {
                    var pos = worldItem.WalkablePosition();
                    pos.Initialized = true; //disable walkable position searching for items

                    //chance belts for hh/mb -> chanceitem flag wird gesetzt
                    if ((worldItem.Item.Class == ItemClasses.Belt && worldItem.Item.Rarity == Rarity.Normal) || (worldItem.Item.Class == ItemClasses.Shield && worldItem.Item.Rarity == Rarity.Normal))
                    {
                        Items.Add(new CachedWorldItem(id, pos, item.Size, item.Rarity, true));
                        //NewItems.Add(id, new CachedWorldItem(id, pos, item.Size, item.Rarity, true));
                    }
                    else
                    {
                        Items.Add(new CachedWorldItem(id, pos, item.Size, item.Rarity));
                        //NewItems.Add(id, new CachedWorldItem(id, pos, item.Size, item.Rarity));
                    }
                }
                _processedItems.Add(id);
            }
        }

        private void ProcessLegionChest(Chest c)
        {
            var id = c.Id;
            if (_processedObjects.Contains(id))
                return;

            if (c.IsOpened || c.IsLocked || c.OpensOnDamage || !c.IsTargetable)
                return;

            var pos = c.WalkablePosition();
            LegionChests.Add(new CachedObject(id, pos));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering {pos}");
        }

        private void ProcessSyndHO(AreaTransition c)
        {
            var id = c.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!c.IsTargetable)
                return;

            var pos = c.WalkablePosition();
            SyndHO.Add(new CachedObject(id, pos));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering {pos}");
        }

        private void ProcessSyndHOExits(AreaTransition c)
        {
            var id = c.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!c.IsTargetable)
                return;

            var pos = c.WalkablePosition();
            SyndHOExits.Add(new CachedObject(id, pos));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering {pos}");
        }

        private void ProcessSmugglers(Chest c)
        {
            var id = c.Id;
            if (_processedObjects.Contains(id))
                return;

            if (c.IsOpened || !c.IsTargetable)
                return;

            var pos = c.WalkablePosition();
            SmugglerChests.Add(new CachedObject(id, pos));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering {pos}");
        }

        private void ProcessBetrayalDude(NetworkObject c)
        {
            var id = c.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!c.IsTargetable)
                return;

            var pos = c.WalkablePosition();
            BetrayalDudes.Add(new CachedObject(id, pos));
            if (!HasBetrayal)
            {
                HasBetrayal = true;
                MapBot.Statistics.Instance.betrayals++;

            }

            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] BetrayalDudes adding {pos}");
        }

        private void ProcessExpedChests(Chest c)
        {
            var id = c.Id;
            if (_processedObjects.Contains(id))
                return;

            if (c.IsOpened || !c.IsTargetable)
                return;

            var pos = c.WalkablePosition();
            ExpedChests.Add(new CachedObject(id, pos));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering {pos}");
        }

        private void ProcessDangerousStuff(NetworkObject m)
        { 
            var id = m.Id;
            if (_processedObjects.Contains(id))
                return;


            var pos = m.WalkablePosition();
            pos.Initialized = true; //disable walkable position searching for monsters

            //NewMonsters.Add(id, new CachedObject(id, pos));
            ThingsToAvoid.Add(new CachedObject(id, pos));
            GlobalLog.Error($"AAAAAAAAAAAAA adde thing to avoid ");
            _processedObjects.Add(id);
        }

        private void ProcessMonster(Monster m)
        {
            if (RitualActive)
            {
                if (m != null)
                {
                    ProcessRitualMobs(m);
                }
            }
            else
            {
                if (ActiveRitualMonsters.Count > 0)
                {
                    ActiveRitualMonsters.Clear();
                }

            }

            if (LegionActive)
            {
                if (m != null)
                {
                    if (m.Rarity == Rarity.Rare || m.Rarity == Rarity.Unique)
                    {
                        var stats = m.Components.StatsComponent.StatsD;

                        if (stats != null && stats.Count > 0)
                        {
                            foreach (KeyValuePair<StatTypeGGG, int> pair in stats)
                            {
                                if (pair.Key == StatTypeGGG.FrozenInTime)
                                {
                                    GlobalLog.Info($"frozenintime mob gefunden");

                                    ProcessLegionMonster(m);
                                    break;
                                }
                                //GlobalLog.Info($"mod: {pair.Key} value {pair.Value}");
                            }
                        }
                    }
                }
            }

            var id = m.Id;
			if (_processedObjects.Contains(id))
				return;

			if (m.IsDead || m.CannotDie || m.Reaction != Reaction.Enemy)
                return;

            if ((!m.IsTargetable || m.GetStat(StatTypeGGG.CannotBeDamaged) == 1) && !IsEmerging(m))
                return;

            if (!IsInIncursion && m.ExplicitAffixes.Any(a => a.InternalName.StartsWith("MonsterIncursion")))
                return;

            if (HasImmunityAura(m) || SkipThisMob(m))
            {
                _processedObjects.Add(id);
                return;
            }

            var pos = m.WalkablePosition();
            pos.Initialized = true; //disable walkable position searching for monsters

            //NewMonsters.Add(id, new CachedObject(id, pos));
            Monsters.Add(new CachedObject(id, pos));
            _processedObjects.Add(id);
        }

        private void ProcessRitualMobs(Monster m)
        {
            int range = 94;

            if (m == null) return;

            var dist = m.Position.Distance(ActiveRitual.Position);

            if (ActiveRitualMonsters.Contains(m))
            {
                if (m.IsDead || m.CannotDie || m.Reaction != Reaction.Enemy)
                {
                    ActiveRitualMonsters.Remove(m);
                    return;
                }
                if (dist >= range)
                {
                    ActiveRitualMonsters.Remove(m);
                    return;
                }

                if ((!m.IsTargetable || m.GetStat(StatTypeGGG.CannotBeDamaged) == 1) && !IsEmerging(m))
                    return;

            }
            else
            {
                if (m.IsDead || m.CannotDie || m.Reaction != Reaction.Enemy || !m.IsHostile)
                    return;


                if (Blacklist.Contains(m.Id))
                    return;

                if ((!m.IsTargetable || m.GetStat(StatTypeGGG.CannotBeDamaged) == 1) && !IsEmerging(m))
                    return;

                if (dist < range)
                {
                    ActiveRitualMonsters.Add(m);
                    return;
                }
            }
            return;
        }




        private void ProcessLegionMonster(Monster m)
        {
            var id = m.Id;
            if (_processedObjects.Contains(id))
                return;
            GlobalLog.Error($"[CombatAreaCache] added Legion Monster");
            var pos = m.WalkablePosition();
            pos.Initialized = true; //disable walkable position searching for monsters
            FrozenLegionBosses.Add(new CachedObject(id, pos));
            _processedObjects.Add(id);
        }


        private void ProcessChest(Chest c)
        {
			var id = c.Id;
			if (_processedObjects.Contains(id))
				return;

			if (c.IsOpened || c.IsLocked || c.OpensOnDamage || !c.IsTargetable)
                return;
            
            if (IsHeisting)
            {
                GlobalLog.Error($"[CombatAreaCache] chest added");
                HeistChests.Add(new CachedObject(id, c.WalkablePosition(5, 20)));
            }
            else
            {
                Chests.Add(new CachedObject(id, c.WalkablePosition(5, 20)));

            }      
            _processedObjects.Add(id);
        }

        private void ProcessVein(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!n.IsTargetable)
                return;

            VeinsnStuff.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering Vein");
        }

        private void ProcessEssence(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!n.IsTargetable)
                return;

            Essences.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            MapBot.Statistics.Instance.essences++;
            GlobalLog.Warn($"[CombatAreaCache] Registering Essence");
        }

        private void ProcessCraftingRecipe(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!n.IsTargetable)
                return;

            CRecipe.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering Crafting Recipe");
        }

        private void ProcessTempDangers(NetworkObject n, int range, int msectoavoid)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            NewTempDangers.Add(new TempDanger() { CachedObj = new CachedObject(id, n.WalkablePosition(5, 20)), EvasionRange = range, MilliSecondsToAvoid = msectoavoid, Timestamp = Stopwatch.StartNew() });

            //TemporaryDangers.Add(new CachedObject(id, n.WalkablePosition(5, 20)), DateTime.Now);

            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering Temporary Danger {n.Name}");
        }

        private void ProcessHeistDoors(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!n.IsTargetable)
                return;

            HeistDoors.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Error($"[CombatAreaCache] Registering Heist Door");
        }
        private void ProcessHeistVaults(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!n.IsTargetable)
                return;

            HeistVaults.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Error($"[CombatAreaCache] Registering Heist Vault");
        }

        private void ProcessHeistHazards(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!n.IsTargetable)
                return;

            HeistHazards.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Error($"[CombatAreaCache] Registering Heist Hazard");
        }
        private void ProcessANMonster(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!n.IsTargetable)
                return;

            ANMonsters.Add(new CachedObject(id, n.WalkablePosition(5, 20)));

            Utility.BroadcastMessage(this, "an_monster_pos", n.Position);

            _processedObjects.Add(id);
            GlobalLog.Error($"[CombatAreaCache] Registering ArchNemesis Monster");
        }
        private void ProcessHeistTargets(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!n.IsTargetable)
                return;

            HeistTargets.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Error($"[CombatAreaCache] Registering Heist Target");
        }

        private void ProcessHeistNPCChests(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!n.IsTargetable)
                return;

            HeistNPCChests.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Error($"[CombatAreaCache] Registering Phat L00t Heist Chest");
        }

        private void ProcessLevers(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!n.IsTargetable)
                return;

            Levers.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering Lever");
        }


        private void ProcessRitualShrine(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!n.IsTargetable)
                return;

            if (n.Components.MinimapIconComponent.Minimapicon.Name == "RitualRuneFinished")
            {
                _processedObjects.Add(id);
                GlobalLog.Error($"[CombatAreaCache] Shrine already opened");
                return;
            }

            RitualShrines.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering Ritual Shrine");
        }

        private void ProcessLegions(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (!n.IsTargetable)
                return;

            Legions.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering Legion Starter");
        }

        private void ProcessBreach(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            Breaches.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);

            if (n.Metadata.Equals("Metadata/MiscellaneousObjects/Breach/BreachObject"))
                MapBot.Statistics.Instance.breaches++;

            GlobalLog.Warn($"[CombatAreaCache] Registering Breachstart oder Rit");
        }

        private void ProcessExpedExploder(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            ExpedExploder.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            MapBot.Statistics.Instance.expeds++;
            GlobalLog.Warn($"[CombatAreaCache] Registering Expedition Exploder");
        }

        private void ProcessRelics(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            ExpedRelics.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering Expedition Relic");
        }

        private void ProcessDelirium(NetworkObject n)
        {
            var id = n.Id;

            if (_processedObjects.Contains(id) && !n.Components.MinimapIconComponent.IsVisible)
            {
                GlobalLog.Error($"[CombatAreaCache] Delirium schon wieder geschlossen");
                Delirium.Clear(); //klappt nur, weils eh nur einen gibt
            }

            if (_processedObjects.Contains(id))
                return;

            Delirium.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering Delirium");
        }

        private void ProcessBlight(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            BlightPump.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            MapBot.Statistics.Instance.blights++;
            _processedObjects.Add(id);
            GlobalLog.Error($"[CombatAreaCache] Registering BlightPump");
            //GlobalLog.Discord("@here Blight found");
        }

        private void ProcessAbyss(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            AbyssStart.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering Abyss Start");
        }

        private void ProcessZana(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            Zana.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering Zana");
        }

        private void ProcessAlva(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            Alvas.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering Alva");
        }

        private void ProcessIncursionExit(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            IncursionExits.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
            _processedObjects.Add(id);
            GlobalLog.Error($"[CombatAreaCache] Registering IncursionExit");
        }

        private void ProcessHarvestPlants(NetworkObject n)
        {
            var id = n.Id;
            if (_processedObjects.Contains(id))
                return;

            if (n as HarvestExtraxtor != null)
            {
                HarvestExtractors.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
                _processedObjects.Add(id);
                GlobalLog.Warn($"[CombatAreaCache] Registering Harvest Extractor");
            }


            if (n as HarvestIrrigator != null)
            {
                HarvestIrrigators.Add(new CachedObject(id, n.WalkablePosition(5, 20)));
                _processedObjects.Add(id);
                GlobalLog.Warn($"[CombatAreaCache] Registering Harvest Irrigator");
            }

        }

        private void ProcessSpeacialChest(Chest c)
        {
			var id = c.Id;
			if (_processedObjects.Contains(id))
				return;

			if (c.IsOpened || !c.IsTargetable)
                return;

            // Perandus chests are always locked for some reason
            if (c.IsLocked && !c.Metadata.Contains("/PerandusChests/"))
                return;

            var pos = c.WalkablePosition();
            SpecialChests.Add(new CachedObject(id, pos));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering {pos}");
        }

        private void ProcessStrongbox(Chest box)
        {
			var id = box.Id;
			if (_processedObjects.Contains(id))
				return;

			if (box.IsOpened || box.IsLocked || !box.IsTargetable)
                return;

            var pos = box.WalkablePosition();
            Strongboxes.Add(new CachedStrongbox(id, pos, box.Rarity));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering {pos}");
        }

        private void ProcessShrine(Shrine s)
        {
			var id = s.Id;
			if (_processedObjects.Contains(id))
				return;

			if (s.IsDeactivated || !s.IsTargetable)
                return;

            var pos = s.WalkablePosition();
            Shrines.Add(new CachedObject(id, pos));
            _processedObjects.Add(id);
            GlobalLog.Warn($"[CombatAreaCache] Registering {pos}");
        }

        private void ProcessTransition(AreaTransition t)
        {
            var id = t.Id;
            if (_processedObjects.Contains(id))
                return;

            if (SkipThisTransition(t))
            {
                _processedObjects.Add(id);
                return;
            }

            TransitionType type;

            if (t.Metadata.Contains("LabyrinthTrial"))
            {
                type = TransitionType.Trial;
            }
            else if (t.Metadata.Contains("IncursionPortal"))
            {
                type = TransitionType.Incursion;
            }
            else if (t.ExplicitAffixes.Any(a => a.Category == "MapMissionMods"))
            {
                type = TransitionType.Master;
            }
            else if (t.ExplicitAffixes.Any(a => a.InternalName.Contains("CorruptedSideArea")))
            {
                type = TransitionType.Vaal;
            }
            else if (t.Metadata.Contains("HarvestPortal")) //Metadata/Terrain/Leagues/Harvest/Objects/HarvestPortalToggleableReverse
            {
                type = TransitionType.Harvest;
                GlobalLog.Warn($"[CombatAreaCache] Harvest found)");
            }
            else if (t.Name.Equals("Escape Route")) 
            {
                type = TransitionType.Heist;
                GlobalLog.Error($"[CombatAreaCache] Heist Exit found");
            }
            else if (t.TransitionType == TransitionTypes.Local)
            {
                type = TransitionType.Local;
            }
            else
            {
                type = TransitionType.Regular;
            }

            var pos = t.WalkablePosition(10, 20);
            var dest = t.Destination ?? Dat.LookupWorldArea(1);
            var cachedTrans = new CachedTransition(id, pos, type, dest);
            AreaTransitions.Add(cachedTrans);
            _processedObjects.Add(id);
            GlobalLog.Debug($"[CombatAreaCache] Registering {pos} (Type: {type})");
            TweakTransition(cachedTrans);
        }

        private void UpdateMonsters()
        {
            //GlobalLog.Debug($"update monster");


            var toRemove = new List<CachedObject>();

            
            foreach (var cachedMob in Monsters)
            {
                var m = cachedMob.Object as Monster;
                if (m != null)
                {
                    if (m.IsDead || m.CannotDie || m.Reaction != Reaction.Enemy || HasImmunityAura(m))
                    {
                        toRemove.Add(cachedMob);
                        if (cachedMob == TrackMobLogic.CurrentTarget)
                            TrackMobLogic.CurrentTarget = null;
                    }
                    else
                    {
                        var pos = m.WalkablePosition();
                        pos.Initialized = true;
                        cachedMob.Position = pos;
                    }
                }
                else
                {
                    //remove monsters that were close to us, but now are null (corpse exploded, shattered etc)
                    //optimal distance is debatable, but its not recommended to be higher than 100
                    if (cachedMob.Position.Distance <= 80)
                    {
                        toRemove.Add(cachedMob);
                        if (cachedMob == TrackMobLogic.CurrentTarget)
                            TrackMobLogic.CurrentTarget = null;
                    }
                }
            }
            foreach (var m in toRemove)
            {
                //NewMonsters.Remove(m.Id);
                //GlobalLog.Debug($"remove {toRemove.Count} monster");
                Monsters.Remove(m);
            }
        }

        private void UpdateDangerousStuff()
        {
            var toRemove = new List<CachedObject>();
            
            foreach (var cachedMob in ThingsToAvoid)
            {
                var m = cachedMob.Object;
                if (m != null)
                {

                    {
                        var pos = m.WalkablePosition();
                        pos.Initialized = true;
                        cachedMob.Position = pos;
                    }
                }
                else
                {
                    //remove monsters that were close to us, but now are null (corpse exploded, shattered etc)
                    //optimal distance is debatable, but its not recommended to be higher than 100
                    if (cachedMob.Position.Distance <= 80)
                    {
                        toRemove.Add(cachedMob);
                    }
                }
            }
            foreach (var m in toRemove)
            {
                //NewMonsters.Remove(m.Id);
                ThingsToAvoid.Remove(m);
            }
        }

        private static bool HasImmunityAura(Monster mob)
        {
            foreach (var aura in mob.Auras)
            {
                var name = aura.InternalName;
                if (name == "cannot_be_damaged" ||
                    name == "bloodlines_necrovigil" ||
                    name == "god_mode" ||
                    name == "shrine_godmode")
                    return true;
            }
            return false;
        }

        private static bool SkipThisMob(Monster mob)
        {
            var m = mob.Metadata;
            return m == "Metadata/Monsters/LeagueIncursion/VaalSaucerBoss" ||
                   m.Contains("DoedreStonePillar");
        }

        private static bool IsEmerging(Monster mob)
        {
            if (mob.GetStat(StatTypeGGG.IsHiddenMonster) != 1)
                return false;

            var m = mob.Metadata;
            return m.Contains("/SandSpitterEmerge/") ||
                   m.Contains("/WaterElemental/") ||
                   m.Contains("/RootSpiders/") ||
                   m.Contains("ZombieMiredGraspEmerge") ||
                   m.Contains("ReliquaryMonsterEmerge");
        }


        private bool SkipThisTransition(AreaTransition t)
        {
            var name = t.Name;

            if (name == "Area Transition" && t.Destination == null)
            {
                GlobalLog.Debug($"[CombatAreaCache] Skipping dummy area transition (id: {t.Id})");
                return true;
            }

            if (t.TransitionType == TransitionTypes.Local && !t.Metadata.Contains("IncursionPortal"))
            {
                /*
                if (WorldArea.Name == MapNames.Caldera && name != "Caldera of The King")
                {
                    GlobalLog.Debug($"[CombatAreaCache] Skipping \"{name}\" area transition because it leads to the same level.");
                    return true;
                }
                */
                if (WorldArea.Id == World.Act9.RottingCore.Id)
                {
                    var metadata = t.Metadata;
                    if (metadata == "Metadata/QuestObjects/Act9/HarvestFinalBossTransition")
                    {
                        GlobalLog.Debug($"[CombatAreaCache] Skipping \"{name}\" area transition because it is unlocked by a quest.");
                        return true;
                    }
                    if (metadata.Contains("BellyArenaTransition"))
                    {
                        GlobalLog.Debug($"[CombatAreaCache] Skipping \"{name}\" area transition because it is not a pathfinding obstacle.");
                        return true;
                    }
                }
            }
            return false;
        }

        private void TweakTransition(CachedTransition t)
        {
            var name = t.Position.Name;
            var areaName = WorldArea.Name;
            if (areaName == MapNames.Villa && (name == MapNames.Villa || name == "Arena"))
            {
                GlobalLog.Debug("[CombatAreaCache] Marking this area transition as unwalkable (Villa tweak)");
                t.Unwalkable = true;
                return;
            }
            if (areaName == MapNames.Summit && name == MapNames.Summit)
            {
                GlobalLog.Debug("[CombatAreaCache] Marking this area transition as back transition (Summit tweak)");
                t.LeadsBack = true;
            }
        }

        private static bool IsSpecialChest(Chest chest)
        {
            var m = chest.Metadata;

            if (SpecialChestMetadada.Contains(m))
                return true;

            if (m.Contains("/Breach/"))
                return true;

            if (m.Contains("/PerandusChests/"))
                return true;

            if (m.Contains("IncursionChest"))
                return true;

            return false;
        }

        private static readonly HashSet<string> SpecialChestMetadada = new HashSet<string>
        {
            "Metadata/Chests/BootyChest",
            "Metadata/Chests/NotSoBootyChest",
            "Metadata/Chests/VaultTreasurePile",
            "Metadata/Chests/GhostPirateBootyChest",
            "Metadata/Chests/StatueMakersTools",
            "Metadata/Chests/StrongBoxes/VaultsOfAtziriUniqueChest",
            "Metadata/Chests/CopperChestEpic3",
            "Metadata/Chests/TutorialSupportGemChest"
        };

        private static void OnLocalTransitionEntered()
        {
            GlobalLog.Info("[CombatAreaCache] Resetting unwalkable flags on all cached objects.");

            var cache = Current;

            
            foreach (var item in cache.Items)
            {
                item.Unwalkable = false;
            }
            /*
            foreach (KeyValuePair<int, CachedWorldItem> item in cache.NewItems)
            {
                item.Value.Unwalkable = false;
            }
            foreach (KeyValuePair<int, CachedObject> monster in cache.NewMonsters)
            {
                monster.Value.Unwalkable = false;
            }*/
            
            foreach (var monster in cache.Monsters)
            {
                monster.Unwalkable = false;
            }
            foreach (var dangers in cache.ThingsToAvoid)
            {
                dangers.Unwalkable = false;
            }

            foreach (var chest in cache.Chests)
            {
                chest.Unwalkable = false;
            }
            foreach (var vein in cache.VeinsnStuff)
            {
                vein.Unwalkable = false;
            }
            foreach (var ess in cache.Essences)
            {
                ess.Unwalkable = false;
            }
            foreach (var rit in cache.RitualShrines)
            {
                rit.Unwalkable = false;
            }
            foreach (var legc in cache.LegionChests)
            {
                legc.Unwalkable = false;
            }
            foreach (var breach in cache.Breaches)
            {
                breach.Unwalkable = false;
            }
            foreach (var exp in cache.ExpedExploder)
            {
                exp.Unwalkable = false;
            }
            foreach (var bli in cache.BlightPump)
            {
                bli.Unwalkable = false;
            }
            foreach (var leg in cache.Legions)
            {
                leg.Unwalkable = false;
            }
            foreach (var sc in cache.SmugglerChests)
            {
                sc.Unwalkable = false;
            }
            foreach (var ec in cache.ExpedChests)
            {
                ec.Unwalkable = false;
            }
            foreach (var lev in cache.Levers)
            {
                lev.Unwalkable = false;
            }
            foreach (var zan in cache.Zana)
            {
                zan.Unwalkable = false;
            }
            foreach (var sho in cache.SyndHO)
            {
                sho.Unwalkable = false;
            }
            foreach (var sho in cache.SyndHOExits)
            {
                sho.Unwalkable = false;
            }
            foreach (var harv in cache.Harvest)
            {
                harv.Unwalkable = false;
            }
            foreach (var del in cache.Delirium)
            {
                del.Unwalkable = false;
            }
            foreach (var dude in cache.BetrayalDudes)
            {
                dude.Unwalkable = false;
            }
            foreach (var aby in cache.AbyssStart)
            {
                aby.Unwalkable = false;
            }
            foreach (var craft in cache.CRecipe)
            {
                craft.Unwalkable = false;
            }
            foreach (var specialChest in cache.SpecialChests)
            {
                specialChest.Unwalkable = false;
            }
            foreach (var alv in cache.Alvas)
            {
                alv.Unwalkable = false;
            }
            foreach (var incex in cache.IncursionExits)
            {
                incex.Unwalkable = false;
            }
            foreach (var strongbox in cache.Strongboxes)
            {
                strongbox.Unwalkable = false;
            }
            foreach (var shrine in cache.Shrines)
            {
                shrine.Unwalkable = false;
            }
            foreach (var transition in cache.AreaTransitions)
            {
                transition.Unwalkable = false;
            }
            
            foreach (var heistchest in cache.HeistChests)
            {
                heistchest.Unwalkable = false;
            }
            foreach (var heistdoor in cache.HeistDoors)
            {
                heistdoor.Unwalkable = false;
            }
            foreach (var heisthazard in cache.HeistHazards)
            {
                heisthazard.Unwalkable = false;
            }
            foreach (var heisttarget in cache.HeistTargets)
            {
                heisttarget.Unwalkable = false;
            }
            foreach (var npcchest in cache.HeistNPCChests)
            {
                npcchest.Unwalkable = false;
            }
            foreach (var vault in cache.HeistVaults)
            {
                vault.Unwalkable = false;
            }
            foreach (var anmons in cache.ANMonsters)
            {
                anmons.Unwalkable = false;
            }
            foreach (var hextr in cache.HarvestExtractors)
            {
                hextr.Unwalkable = false;
            }
            foreach (var hirr in cache.HarvestIrrigators)
            {
                hirr.Unwalkable = false;
            }
        }

        private static void OnItemEvaluatorRefresh(object sender, ItemEvaluatorRefreshedEventArgs args)
        {
            if (Caches.TryGetValue(LokiPoe.LocalData.AreaHash, out var cache))
            {
                GlobalLog.Info("[CombatAreaCache] Clearing processed items.");
                cache._processedItems.Clear();
            }
        }


        public class ObjectDictionary
        {
            private readonly Dictionary<string, object> _dict = new Dictionary<string, object>();

            public object this[string key]
            {
                get
                {
                    _dict.TryGetValue(key, out object obj);
                    return obj;
                }
                set
                {
                    if (!_dict.ContainsKey(key))
                    {
                        GlobalLog.Debug($"[Storage] Registering [{key}] = [{value ?? "null"}]");
                        _dict.Add(key, value);
                    }
                    else
                    {
                        _dict[key] = value;
                    }
                }
            }

            public bool Contains(string key)
            {
                return _dict.ContainsKey(key);
            }
        }

        private class PickupEvalHolder
        {
            public readonly string Id;
            public readonly Func<Item, bool> Eval;

            public PickupEvalHolder(string id, Func<Item, bool> eval)
            {
                Id = id;
                Eval = eval;
            }
        }

        public class TempDanger
        {
            public CachedObject CachedObj { get; set; }
            public Stopwatch Timestamp { get; set; }
            public int EvasionRange { get; set; }
            public int MilliSecondsToAvoid { get; set; }
        }

        public static List<string> MetadataToAvoid = new List<string>()
        {
            "large_tornado",
            "living_storms/LightningTornado",
        };

        //Metadata/Effects/Spells/ground_effects_v3/burning/burning_grd.ao 

        public static List<string> GroundEffectMetadata = new List<string>()
        {
            "calamity_of_flames",
            "calamity_of_frost",
            "calamity_of_plagues",
            "calamity_of_storms",
            "volatile_explode",
            "crystal_explode",
            "ground_effects_v3/chilled/chilled_grd",
        };

        public static List<string> AnimatedMetadata = new List<string>()
        {
            "blablabla"


        };
    }
}