using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Default.EXtensions.CachedObjects;
using Default.EXtensions.Global;
using Default.EXtensions.Positions;
using Default.EXtensions;
using DreamPoeBot.Loki.Bot;
using DreamPoeBot.Common;
using DreamPoeBot.Loki.Game;
using DreamPoeBot.Loki.Game.Objects;
using DreamPoeBot.Loki.Game.GameData;
using System.Windows.Forms;
using DreamPoeBot.BotFramework;
using Message = DreamPoeBot.Loki.Bot.Message;
using ExSettings = Default.EXtensions.Settings;

namespace Default.EXtensions.CommonTasks
{
    public class HarvestTask : ITask
    {
        //wenn tot da drin
        //implicit

        public static int _runtergescrollt = 0;
        private static bool _openedportal = false;
        private static bool _stoppedforboss = false;

        public static DreamPoeBot.Loki.Element UiRoot => LokiPoe.GetGuiElements().FirstOrDefault(x => x.IdLabel == "root");
        public static DreamPoeBot.Loki.Element HarvestCraftWindow => UiRoot.Children[1].Children[95]; //.IsVisible //verify 93 after each patch
        public static DreamPoeBot.Loki.Element ToggleStashButton => HarvestCraftWindow.Children[9];
        public static DreamPoeBot.Loki.Element CraftButton => HarvestCraftWindow.Children[11].Children[1];
        public static DreamPoeBot.Loki.Element ItemFieldTwo => HarvestCraftWindow.Children[11].Children[0].Children[1]; //
        public static DreamPoeBot.Loki.Element ItemFieldOne => HarvestCraftWindow.Children[11].Children[0].Children[0].Children[0]; //
        public static List<DreamPoeBot.Loki.Element> HarvestCrafts => HarvestCraftWindow.Children[8].Children[0].Children[1].Children; //children = harvestcrafts


        private static readonly Settings Settings = Settings.Instance;

        private static readonly Dictionary<int, Stopwatch> TemporaryIgnoredObjects = new Dictionary<int, Stopwatch>();
        private static readonly TimeSpan IgnoringTime = TimeSpan.FromSeconds(5);


        
        private static CachedObject _harvest;
        public static CachedObject _irrigator;
        public static CachedObject _extractor;

        private static List<CachedObject> _activatedextractors = new List<CachedObject>();

        private static NetworkObject OshabiBoss => LokiPoe.ObjectManager.Objects
            .Where(o => o.Name == "Heart of the Grove").FirstOrDefault();


        public async Task<bool> Run()
        {
            if (!World.CurrentArea.IsCombatArea)
                return false;

            if (!Settings.Instance.UseHarvest)
                return false;

            if (CombatAreaCache.IsInIncursion || CombatAreaCache.Current.RitualActive)
                return false;

            var cache = CombatAreaCache.Current;

            if (CombatAreaCache.IsInHarvest)
            {
                if (!_openedportal)
                {
                    // Have to do this because portal spawned right near Time Portal has a high chance to overlap labels
                    var distantPos = WorldPosition.FindPathablePositionAtDistance(30, 35, 5);
                    if (distantPos != null)
                    {
                        await Move.AtOnce(distantPos, "away from harvest entrance", 10);
                        await PlayerAction.CreateTownPortal();
                    }
                    else
                    {
                        await PlayerAction.CreateTownPortal();
                    }
                    _openedportal = true;
                }

                if (OshabiBoss != null && !_stoppedforboss)
                {
                    GlobalLog.Warn("HARVEST BOSS FOUND");
                    _stoppedforboss = true;
                    await Wait.SleepSafe(2000);
                    BotManager.Stop();
                }

                Settings.Instance.StuckDetectionEnabled = false;
                if (await TrackMobLogic.Execute(200)) //tötet alle monster
                    return true;

                if (await TryToCraft()) //versucht zu craften wenn möglich
                    return true;

                if (await ActivateExtractor()) // ist false wenn keine extractors da
                    return true;

                if (await FindHarvestToBegin()) // ist false wenn keine irrigators da
                    return true;
                
                if (await cache.Explorer.Execute()) //erkundet harvest wenn nichts zu tun
                    return true;


                //harvest beenden
                if (_harvest != null)
                {
                    await ProcessHarvest(false);
                    Settings.Instance.StuckDetectionEnabled = true;
                    return true;
                }
                var harvi = cache.AreaTransitions.Where(a => a.Type == TransitionType.Harvest).ClosestValid();
                if (harvi != null && !IsTemporaryIgnored(harvi.Id))
                {
                    _harvest = harvi;
                    return true;
                }


                return true; //muss nicht weiter nach unten in harvest
            }


            if (_harvest != null)
            {
                await ProcessHarvest(true);
                return true;
            }
            var harv = cache.AreaTransitions.Where(a => a.Type == TransitionType.Harvest).ClosestValid();
            if (harv != null && !IsTemporaryIgnored(harv.Id))
            {
                _harvest = harv;
                return true;
            }

            return false;
        }


        private static async Task<bool> FindHarvestToBegin()
        {
            //GlobalLog.Debug("bearbeite irrigator");

            if (_irrigator != null)
            {
                //hinlaufen
                if (Blacklist.Contains(_irrigator.Id))
                {
                    GlobalLog.Error("[HArvest] Current irrigator was blacklisted from outside.");
                    _irrigator.Ignored = true;
                    _irrigator = null;
                    return true;
                }


                var pos = _irrigator.Position;


                if (pos.IsFar)
                {
                    //GlobalLog.Error("[OpenChestTask] breach far??");
                    if (!pos.TryCome())
                    {
                        GlobalLog.Error($"[HArvest] Fail to move to {pos}. Marking this Irrigator as unwalkable.");
                        _irrigator.Unwalkable = true;
                        _irrigator = null;
                    }
                    return true;
                }

                Move.Towards(pos, "walk zu irrigator");

                var irrObj = _irrigator.Object;
                if (irrObj == null || !irrObj.IsTargetable)
                {
                    CombatAreaCache.Current.HarvestIrrigators.Remove(_irrigator);
                    _irrigator = null;
                    return true;
                }

                var irri = irrObj as HarvestIrrigator;
                var craftcount = irri.CraftingOutCome.Count;

                if (!irri.IsUiVisible)
                {
                    GlobalLog.Debug("irrigator ui nicht sichtbar");
                    CombatAreaCache.Current.HarvestIrrigators.Remove(_irrigator);
                    _irrigator = null;
                    return true;
                }


                if (pos.Distance < 20)
                {
                    GlobalLog.Debug($"[HArvest] Irrigator erreicht. dis {pos.Distance}");
                    await Coroutines.FinishCurrentAction();
                    if (pos.Distance > 20)
                    {
                        pos.TryCome();
                        return true;
                    }

                    await Wait.SleepSafe(500);

                    //schauen ob andere craftingstange besser
                    if (CombatAreaCache.Current.HarvestIrrigators.Count > 1)
                    {
                        if (CombatAreaCache.Current.HarvestIrrigators.Where(i => (i.Position.Distance < 80 && i.Id != _irrigator.Id)).Any())
                        {
                            GlobalLog.Debug("gibt noch andren irrgiator");
                            var otherirri = CombatAreaCache.Current.HarvestIrrigators.Where(i => (i.Position.Distance < 80 && i.Id != _irrigator.Id && (i.Object as HarvestIrrigator).IsUiVisible)).FirstOrDefault();
                            var otherirriobj = otherirri.Object as HarvestIrrigator;
                            var othercraftcount = otherirriobj.CraftingOutCome.Count;

                            if (othercraftcount > craftcount)
                            {

                                GlobalLog.Error($"[HArvest] anderer irrigator hat mehr crafts, delete diesen hier");
                                CombatAreaCache.Current.HarvestIrrigators.Remove(_irrigator);
                                _irrigator = null;
                                return true;
                            }
                            else //anderen removen
                            {
                                GlobalLog.Debug("remove die andere option");
                                CombatAreaCache.Current.HarvestIrrigators.Remove(otherirri);
                            }
                        }
                    }
                    GlobalLog.Error("aktiviere irrigator");
                    irri.Activate();

                    

                    await Wait.SleepSafe(2000);

                    CombatAreaCache.Current.HarvestIrrigators.Remove(_irrigator);
                    _irrigator = null;
                    return true;

                }

                return true;
            }

            var irr = CombatAreaCache.Current.HarvestIrrigators.ClosestValid();
            if (irr != null && !IsTemporaryIgnored(irr.Id))
            {
                _irrigator = irr;
                return true;
            }

            return false;
        }

        private static async Task<bool> ActivateExtractor()
        {
            //GlobalLog.Debug("bearbeite extractor");

            var extractors = CombatAreaCache.Current.HarvestExtractors;

            foreach (var extractor in extractors)
            {
                if (_activatedextractors.Contains(extractor))
                    continue;

                if (extractor.Position.Distance > 50)
                    continue;

                if (extractor.Object == null)
                    continue;

                var extr = extractor.Object as HarvestExtraxtor;

                if (!extr.IsUiVisible)
                {
                    continue;
                }

                if (extr.IsUiVisible)
                {
                    GlobalLog.Error("aktiviere den extractor");
                    extr.Activate();
                    var distantPos = WorldPosition.FindPathablePositionAtDistance(50, 55, 5);
                    if (distantPos != null)
                    {
                        await Move.AtOnce(distantPos, "away from harvest extractor", 10);

                    }

                    await Wait.SleepSafe(4000);
                    _activatedextractors.Add(extractor);

                    return true;

                }

            }

            return false;
        }

        private static async Task<bool> TryToCraft()
        {
            //GlobalLog.Debug("versuche zu craften");

            if (_extractor != null)
            {
                //hinlaufen
                if (Blacklist.Contains(_extractor.Id))
                {
                    GlobalLog.Error("[HArvest] Current extractor was blacklisted from outside.");
                    _extractor.Ignored = true;
                    _extractor = null;
                    return true;
                }


                var pos = _extractor.Position;


                if (pos.IsFar)
                {
                    //GlobalLog.Error("[OpenChestTask] breach far??");
                    if (!pos.TryCome())
                    {
                        GlobalLog.Error($"[HArvest] Fail to move to {pos}. Marking this Extractor as unwalkable.");
                        _extractor.Unwalkable = true;
                        _extractor = null;
                    }
                    return true;
                }

                Move.Towards(pos, "walk zu extractor");

                var extrObj = _extractor.Object;
                if (extrObj == null || !extrObj.IsTargetable)
                {
                    CombatAreaCache.Current.HarvestExtractors.Remove(_extractor);
                    _extractor = null;
                    return true;
                }

                var extr = extrObj as HarvestExtraxtor;
                var craftcount = extr.CraftingOutCome.Count;

                if (!extr.IsUiVisible)
                {
                    GlobalLog.Debug("extractor ui nicht sichtbar");
                    CombatAreaCache.Current.HarvestExtractors.Remove(_extractor);
                    _extractor = null;
                    return true;
                }


                if (pos.Distance < 20)
                {
                    GlobalLog.Debug($"[HArvest] Extractor erreicht. dis {pos.Distance}");
                    await Coroutines.FinishCurrentAction();
                    if (pos.Distance > 20)
                    {
                        pos.TryCome();
                        return true;
                    }

                    await Wait.SleepSafe(500);

                    //schauen ob andere craftingstange besser

                    extr.Activate();
                    await Wait.SleepSafe(2000);

                    CombatAreaCache.Current.HarvestExtractors.Remove(_extractor);
                    _extractor = null;
                    MapBot.Statistics.Instance.harvests++;
                    await HarvestCraft.CraftItems();

                    await Coroutines.CloseBlockingWindows();
                    return true;

                }

                return true;
            }

            var extractor = CombatAreaCache.Current.HarvestExtractors.Where(e => _activatedextractors.Contains(e)).ClosestValid();
            if (extractor != null && !IsTemporaryIgnored(extractor.Id))
            {
                _extractor = extractor;
                return true;
            }


            return false;
        }

        private static async Task ProcessHarvest(bool switchisinharvest)
        {
            //check if current shrine was blacklisted by Combat Routine
            //GlobalLog.Error("[OpenChestTask] trying to open breach");

            if (Blacklist.Contains(_harvest.Id))
            {
                GlobalLog.Error("[HArvest] Current harvest was blacklisted from outside.");
                _harvest.Ignored = true;
                _harvest = null;
                return;
            }


            var pos = _harvest.Position;


            if (pos.IsFar)
            {
                //GlobalLog.Error("[OpenChestTask] breach far??");
                if (!pos.TryCome())
                {
                    GlobalLog.Error($"[HArvest] Fail to move to {pos}. Marking this harvest as unwalkable.");
                    _harvest.Unwalkable = true;
                    _harvest = null;
                }
                return;
            }

            Move.Towards(pos, "walk zu harvest");

            var harvObj = _harvest.Object;
            if (harvObj == null || !harvObj.IsTargetable)
            {
                CombatAreaCache.Current.Harvest.Remove(_harvest);
                _harvest = null;
                return;
            }

            if (pos.Distance < 30)
            {
                GlobalLog.Debug($"[HArvest] Harvest erreicht. dis {pos.Distance}");

                var harvestportal = harvObj as AreaTransition;

                if (await PlayerAction.TakeTransition(harvestportal))
                {
                    GlobalLog.Warn("[HArvest] IsInHarvest: toggle");
                    

                    CombatAreaCache.Current.AreaTransitions.Remove(_harvest as CachedTransition);
                    _harvest = null;
                    if (switchisinharvest)
                    {
                        CombatAreaCache.IsInHarvest = true;
                        _openedportal = false;
                        _stoppedforboss = false;
                    }
                    else CombatAreaCache.IsInHarvest = false;
                    await Wait.SleepSafe(500, 1500);
                    
                }
                //
                return;
            }

            await Wait.SleepSafe(400);
        }

        public static bool IsTemporaryIgnored(int id)
        {
            if (TemporaryIgnoredObjects.TryGetValue(id, out var sw))
            {
                if (sw.Elapsed >= IgnoringTime)
                {
                    TemporaryIgnoredObjects.Remove(id);
                    return false;
                }
                return true;
            }
            return false;
        }

        public MessageResult Message(Message message)
        {
            if (message.Id == Events.Messages.AreaChanged)
            {
                _harvest = null;
                _runtergescrollt = 0;
                TemporaryIgnoredObjects.Clear();
                return MessageResult.Processed;
            }
            return MessageResult.Unprocessed;
        }

        #region Unused interface methods

        public void Tick()
        {

        }


        public async Task<LogicResult> Logic(Logic logic)
        {
            return LogicResult.Unprovided;
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public string Name => "HarvestTask";
        public string Description => "Task for handling harvests";
        public string Author => "ElRatto";
        public string Version => "0.6";

        #endregion
    }

    public static class HarvestCraft
    {
        //HARVEST CRAFT CONFIG
        //tabs
        private static string CraftTab = "1";
        private static List<string> TabsToLookForFossils = new List<string>() { "7" };
        private static List<string> TabsToLookForOils = new List<string>() { "7" };
        private static List<string> TabsToLookForCatalysts = new List<string>() { "8" };
        private static List<string> TabsToLookForDelOrbs = new List<string>() { "8" };

        //jewel implicits
        private static Vector2i JewelOverridePos = new Vector2i() { X = 0, Y = 0 };
        private static StatTypeGGG JewelDesiredImplicit = StatTypeGGG.BaseAvoidStunPct;
        //reduced ignite duration: StatTypeGGG.BaseSelfIgniteDurationNegPct
        //reduced chill duration: StatTypeGGG.ChillEffectivenessOnSelfPosPct
        //chance to avoid being stunned: StatTypeGGG.BaseAvoidStunPct

        //colouring
        private static Vector2i ColourungItemPos = new Vector2i() { X = 10, Y = 9 };
        private static int DesiredRedSocks = 0;
        private static int DesiredBlueSocks = 0;
        private static int DesiredGreenSocks = 0;



        public static HarvestCraft.PossibleCraft CachedCraft;
        private static bool _heveredclusters = false;

        private static List<string> _cheapFossils = new List<string>() { "Aberrant Fossil" , "Metallic Fossil", "Scorched Fossil", "Frigid Fossil", "Pristine Fossil", "Lucent Fossil", "Jagged Fossil" };
        private static List<string> _cheapOils = new List<string>() { "Verdant Oil", "Teal Oil", "Sepia Oil", "Clear Oil", "Azure Oil", "Amber Oil" };
        private static List<string> _cheapCatalysts = new List<string>() { "Imbued Catalyst", "Abrasive Catalyst", "Turbulent Catalyst", "Noxious Catalyst", "Intrinsic Catalyst" };
        private static List<string> _cheapDelirOrbs = new List<string>() { "Blacksmith's Delirium Orb", "Imperial Delirium Orb", "Jeweller's Delirium Orb", "Whispering Delirium Orb", "Fragmented Delirium Orb", "Armoursmith's Delirium Orb", "Abyssal Delirium Orb", "Timeless Delirium Orb", "Thaumaturge's Delirium Orb" };
        public static async Task<bool> CraftItems()
        {
            var stop = false;
            CachedCraft = null;
            _heveredclusters = false;
            while (!stop)
            {
                var craftresult = await TryHarvestCraft();

                if (craftresult == 1)
                    stop = true;
                else if (craftresult == -1)
                {
                    BotManager.Stop();
                    stop = true;
                }
            }

            return true;
            
        }

        public static async Task<int> TryHarvestCraft(bool overridecached = false)
        {
            // -1 error
            // 1 done
            // 2 keep craft success
            // 3 item craft success
            // 4 curr exchange success
            // 5 other change success



            if (!HarvestTask.HarvestCraftWindow.IsVisible)
            {
                GlobalLog.Error("HarvestTask - HarvestCraftWindow not open");
                return -1;
            }

            //erst stash aufmachen, sonst falsche x- werte
            if (!LokiPoe.InGameState.StashUi.IsOpened)
            {
                var clickone = HarvestTask.ToggleStashButton.CenterClickLocation();
                MouseManager.SetMousePosition(clickone.X, clickone.Y);
                await Wait.SleepSafe(150, 200);
                //GlobalLog.Warn($"mouse moved to pos {clickone}");
                MouseManager.ClickLMB();
                await Wait.SleepSafe(150, 200);
            }

            var craftlist = HarvestCraft.GetHarvestItemCrafts(); //noch nicht fertig!!!


            if (!craftlist.Any())
            {
                GlobalLog.Error("HarvestTask - keine Crafts (mehr)!!?");
                return 1;
            }

            if (craftlist.Where(c => c.Type == HarvestCraft.HarvestCraftType.Keep).Any())
            {
                foreach (var craft in craftlist)
                {
                    if (craft.Type != HarvestCraftType.Keep)
                        continue;

                    if (IsCachedCraft(craft) && !overridecached)
                        continue;

                    ScrollToCraft(craft);
                    await Wait.SleepSafe(150, 200);
                    MouseManager.SetMousePosition(craft.ClickPos.X, craft.ClickPos.Y - (48 * HarvestTask._runtergescrollt)); //craft auswählen
                    await Wait.SleepSafe(150, 200);
                    MouseManager.ClickLMB();
                    await Wait.SleepSafe(150, 200);
                    MouseManager.SetMousePosition(craft.ClickToSavePos.X, craft.ClickToSavePos.Y - (48 * HarvestTask._runtergescrollt)); 
                    await Wait.SleepSafe(150, 200);
                    MouseManager.ClickLMB();
                    await Wait.SleepSafe(150, 200);

                    if (craft.Count > 1)
                    {
                        var clickone = new Vector2i(craft.ClickPos.X, craft.ClickPos.Y - (48 * HarvestTask._runtergescrollt));
                        MouseManager.SetMousePosition(clickone); //craft auswählen
                        GlobalLog.Warn($"mouse moved to pos {clickone}");
                        await Wait.SleepSafe(150, 200);
                        MouseManager.ClickLMB();
                        await Wait.SleepSafe(150, 200);
                    }

                    GlobalLog.Error($"craft gespeichert");
                    CachedCraft = craft; // cachen um fehler aufzuzeigen
                    return 2;


                }

                GlobalLog.Debug($"kein passendes craft to keep gefunden");

            }

            if (craftlist.Where(c => c.Type == HarvestCraft.HarvestCraftType.Reforge).Any())
            {

                if (!await Inventories.OpenStashTab(CraftTab, true))
                {
                    GlobalLog.Error($"HarvestTask - kann nicht zu tab {CraftTab} wechseln");
                    return -1;
                }

                if (!_heveredclusters)
                {
                    foreach (Item item in Inventories.StashTabItems)
                    {
                        if (item.Metadata.Contains("JewelPassiveTreeExpansion"))
                        {
                            //über cluster hovern
                            LokiPoe.InGameState.StashUi.InventoryControl.ViewItemsInInventory((inv, it) => it.LocalId == item.LocalId, () => LokiPoe.InGameState.StashUi.IsOpened);
                        }
                    }
                    _heveredclusters = true;
                }

                foreach (var craft in craftlist)
                {
                    if (craft.Type != HarvestCraftType.Reforge)
                        continue;

                    if (IsCachedCraft(craft) && !overridecached)
                        continue;

                    var craftitem = HarvestCraft.FindItemToCraft(craft.ItemSubType, Inventories.StashTabItems); //noch nicht fertig!!!!


                    if (craftitem != null)
                    {
                        GlobalLog.Error($"item der wahl {craftitem.FullName} für type: {craft.ItemSubType}");

                        LokiPoe.InGameState.StashUi.InventoryControl.FastMove(craftitem.LocalId); //item auswählen
                        await Wait.SleepSafe(150, 200);

                        await CraftClicking(craft);


                        GlobalLog.Error($"item {craftitem.FullName} einmal reforged");
                        CachedCraft = craft; // cachen um fehler aufzuzeigen
                        return 3;

                    }
                }

                GlobalLog.Debug($"kein passendes itemcraft gefunden");
            }

            if (craftlist.Where(c => c.Type == HarvestCraft.HarvestCraftType.Curr).Any())
            {
                foreach (var craft in craftlist)
                {
                    if (craft.Type != HarvestCraftType.Curr)
                        continue;

                    if (IsCachedCraft(craft))
                        continue;

                    GlobalLog.Error($"trying to change {craft.ItemOrCurr}");
                    


                    await Inventories.PutCurrencyInHarvest(craft.ItemOrCurr, craft.Needed);


                    await Wait.SleepSafe(150, 200);


                    await CraftClicking(craft);


                    GlobalLog.Error($"item {craft.ItemOrCurr} ausgetauscht");
                    CachedCraft = craft; // cachen um fehler aufzuzeigen
                    return 4;

                }
                GlobalLog.Debug($"kein passendes currency craft gefunden");
            }

            if (craftlist.Where(c => c.Type == HarvestCraft.HarvestCraftType.ChangeOther).Any())
            {
                var craftsupported = false;
                PossibleCraft selectedcraft = new PossibleCraft();

                foreach (var craft in craftlist)
                {
                    if (craft.Type != HarvestCraftType.ChangeOther)
                        continue;

                    if (IsCachedCraft(craft) && !overridecached)
                        continue;

                    GlobalLog.Error($"trying to exchange {craft.ItemOrCurr}");
                    selectedcraft = craft;
                    
                    //Je nach craft verschiedene items testen
                    if (craft.ItemSubType == HarvestItemCraftSubType.Offering)
                    {
                        var tabs = new List<string>(ExSettings.Instance.GetTabsForCategory(ExSettings.StashingCategory.Fragment));
                        if (await Inventories.ClickStuffIntoHarvest("Offering to the Goddess", tabs) == true)
                            craftsupported = true;
                    }
                    if (craft.ItemSubType == HarvestItemCraftSubType.Fossils)
                    {

                        foreach (var cheapfoss in _cheapFossils)
                        {
                            if (await Inventories.ClickStuffIntoHarvest(cheapfoss, TabsToLookForFossils) == true)
                            {
                                craftsupported = true;
                                break;
                            }
                        }
                    }
                    if (craft.ItemSubType == HarvestItemCraftSubType.Oils)
                    {
                        foreach (var cheapoil in _cheapOils)
                        {
                            if (await Inventories.ClickStuffIntoHarvest(cheapoil, TabsToLookForOils) == true)
                            {
                                craftsupported = true;
                                break;
                            }
                        }
                    }
                    if (craft.ItemSubType == HarvestItemCraftSubType.DivCard)
                    {
                        var tabs = new List<string>(ExSettings.Instance.GetTabsForCategory(ExSettings.StashingCategory.Card));

                        if (await Inventories.ClickStuffIntoHarvest("cheapestcard", tabs) == true)
                        {
                            //GlobalLog.Debug("sollte auf true schalten");
                            craftsupported = true;
                            break;
                        }

                    }
                    if (craft.ItemSubType == HarvestItemCraftSubType.Catalysts)
                    {
                        foreach (var cheapcata in _cheapCatalysts)
                        {
                            if (await Inventories.ClickStuffIntoHarvest(cheapcata, TabsToLookForCatalysts) == true)
                            {
                                craftsupported = true;
                                break;
                            }
                        }
                    }
                    if (craft.ItemSubType == HarvestItemCraftSubType.DelOrbs)
                    {

                        foreach (var cheapdelo in _cheapDelirOrbs)
                        {
                            if (await Inventories.ClickStuffIntoHarvest(cheapdelo, TabsToLookForDelOrbs) == true)
                            {
                                craftsupported = true;
                                break;
                            }
                        }
                    }

                    //SCARABS ADDEN

                    
                    

                }

                if (craftsupported)
                {
                    //GlobalLog.Debug("auf true geschalten");
                    await Wait.SleepSafe(150, 200);


                    await CraftClicking(selectedcraft, true);

                    GlobalLog.Error($"item {selectedcraft.ItemOrCurr} ausgetauscht");
                    CachedCraft = selectedcraft; // cachen um fehler aufzuzeigen
                    return 5;
                }
                GlobalLog.Debug($"kein passendes otherstuff craft gefunden");
            }

            if (craftlist.Where(c => c.Type == HarvestCraft.HarvestCraftType.Sockets).Any())
            {

                if (!await Inventories.OpenStashTab(CraftTab, true))
                {
                    GlobalLog.Error($"HarvestTask - kann nicht zu tab {CraftTab} wechseln");
                    return -1;
                }

                foreach (var craft in craftlist)
                {
                    if (craft.Type != HarvestCraftType.Sockets)
                        continue;

                    if (IsCachedCraft(craft))
                        continue;

                    var craftitem = HarvestCraft.FindSocketCraft(craft.ItemSubType, Inventories.StashTabItems);


                    if (craftitem != null)
                    {
                        GlobalLog.Error($"item der wahl {craftitem.FullName}");

                        
                        //GlobalLog.Error($"XXX{HarvestTask.CachedCraft.Type}");

                        LokiPoe.InGameState.StashUi.InventoryControl.FastMove(craftitem.LocalId); //item auswählen
                        await Wait.SleepSafe(150, 200);

                        await CraftClicking(craft);

                        GlobalLog.Error($"item {craftitem.FullName} einmal gesockt");
                        CachedCraft = craft; // cachen um fehler aufzuzeigen
                        return 3;

                    }
                }

                GlobalLog.Debug($"kein passendes socketcraft gefunden");
            }

            if (craftlist.Where(c => c.Type == HarvestCraft.HarvestCraftType.Links).Any())
            {
                if (!await Inventories.OpenStashTab(CraftTab, true))
                {
                    GlobalLog.Error($"HarvestTask - kann nicht zu tab {CraftTab} wechseln");
                    return -1;
                }

                foreach (var craft in craftlist)
                {
                    if (craft.Type != HarvestCraftType.Links)
                        continue;

                    if (IsCachedCraft(craft))
                        continue;

                    var craftitem = HarvestCraft.FindLinkCraft(craft.ItemSubType, Inventories.StashTabItems);


                    if (craftitem != null)
                    {
                        GlobalLog.Error($"item der wahl {craftitem.FullName}");

                        
                        //GlobalLog.Error($"XXX{HarvestTask.CachedCraft.Type}");

                        LokiPoe.InGameState.StashUi.InventoryControl.FastMove(craftitem.LocalId); //item auswählen
                        await Wait.SleepSafe(150, 200);

                        await CraftClicking(craft);

                        GlobalLog.Error($"item {craftitem.FullName} einmal gelinkt");
                        CachedCraft = craft; // cachen um fehler aufzuzeigen
                        return 3;

                    }
                }

                GlobalLog.Debug($"kein passendes Linkcraft gefunden");
            }

            if (craftlist.Where(c => c.Type == HarvestCraft.HarvestCraftType.Implicit).Any())
            {

                if (!await Inventories.OpenStashTab(CraftTab, true))
                {
                    GlobalLog.Error($"HarvestTask - kann nicht zu tab {CraftTab} wechseln");
                    return -1;
                }

                if (!_heveredclusters)
                {
                    foreach (Item item in Inventories.StashTabItems)
                    {
                        if (item.Metadata.Contains("JewelPassiveTreeExpansion"))
                        {
                            //über cluster hovern
                            LokiPoe.InGameState.StashUi.InventoryControl.ViewItemsInInventory((inv, it) => it.LocalId == item.LocalId, () => LokiPoe.InGameState.StashUi.IsOpened);
                        }
                    }
                    _heveredclusters = true;
                }

                foreach (var craft in craftlist)
                {
                    if (craft.Type != HarvestCraftType.Implicit)
                        continue;

                    if (IsCachedCraft(craft) && !overridecached)
                        continue;

                    var craftitem = FindJewelToImplicitRoll(craft.ItemSubType, Inventories.StashTabItems, JewelOverridePos, JewelDesiredImplicit); 


                    if (craftitem != null)
                    {
                        GlobalLog.Error($"item der wahl {craftitem.FullName} für type: {craft.ItemSubType}");

                        LokiPoe.InGameState.StashUi.InventoryControl.FastMove(craftitem.LocalId); //item auswählen
                        await Wait.SleepSafe(150, 200);

                        await CraftClicking(craft);


                        GlobalLog.Error($"item {craftitem.FullName} einmal implicit geändert");
                        CachedCraft = craft; // cachen um fehler aufzuzeigen
                        return 3;

                    }
                }


                GlobalLog.Debug($"kein passendes Implicit craft gefunden");
            }

            if (craftlist.Where(c => c.Type == HarvestCraft.HarvestCraftType.Color).Any())
            {

                if (!await Inventories.OpenStashTab(CraftTab, true))
                {
                    GlobalLog.Error($"HarvestTask - kann nicht zu tab {CraftTab} wechseln");
                    return -1;
                }

                foreach (var craft in craftlist)
                {
                    if (craft.Type != HarvestCraftType.Color)
                        continue;

                    if (IsCachedCraft(craft) && !overridecached)
                        continue;

                    var craftitem = FindItemToColour(craft.ItemSubType, Inventories.StashTabItems, ColourungItemPos);


                    if (craftitem != null)
                    {
                        GlobalLog.Error($"item der wahl {craftitem.FullName} für type: {craft.ItemSubType}");

                        LokiPoe.InGameState.StashUi.InventoryControl.FastMove(craftitem.LocalId); //item auswählen
                        await Wait.SleepSafe(150, 200);

                        await CraftClicking(craft);


                        GlobalLog.Error($"item {craftitem.FullName} einmal farben geändert");
                        CachedCraft = craft; // cachen um fehler aufzuzeigen
                        return 3;

                    }
                }


                GlobalLog.Debug($"kein passendes colour craft gefunden");
            }

            GlobalLog.Error("HarvestTask - alles durch");
            return 1;

        }

        private static async Task<bool> CraftClicking(PossibleCraft craft, bool pressctrl = false)
        {
            Vector2i clickone = new Vector2i(0, 0);
            ScrollToCraft(craft);
            await Wait.SleepSafe(150, 200);

            clickone = new Vector2i(craft.ClickPos.X, craft.ClickPos.Y - (48 * HarvestTask._runtergescrollt));
            MouseManager.SetMousePosition(clickone); //craft auswählen
            GlobalLog.Warn($"mouse moved to pos {clickone}");
            await Wait.SleepSafe(150, 200);
            MouseManager.ClickLMB();
            await Wait.SleepSafe(150, 200);
            clickone = HarvestTask.CraftButton.CenterClickLocation();
            MouseManager.SetMousePosition(clickone); //craft drücken
            GlobalLog.Warn($"mouse moved to pos {clickone}");
            await Wait.SleepSafe(150, 200);
            MouseManager.ClickLMB();
            await Wait.SleepSafe(150, 200);

            if (ItemInHarvestSlot())
            {
                clickone = HarvestTask.ItemFieldTwo.CenterClickLocation();
                MouseManager.SetMousePosition(clickone); //item wieder raus
                GlobalLog.Warn($"mouse moved to pos {clickone}");
                await Wait.SleepSafe(150, 200);
                if (pressctrl)
                {
                    LokiPoe.ProcessHookManager.SetKeyState(Keys.ControlKey, -32768);
                    await Wait.SleepSafe(15, 20);
                }
                MouseManager.ClickLMB();
                await Wait.SleepSafe(150, 200);
                if (pressctrl)
                {
                    LokiPoe.ProcessHookManager.SetKeyState(Keys.ControlKey, 0);
                    await Wait.SleepSafe(150, 255);
                }
                await Wait.SleepSafe(150, 255);
                if (ItemInHarvestSlot())
                {
                    GlobalLog.Error($"Konnte Item nicht aus Slot entfernen");
                    await Wait.SleepSafe(150, 200);
                    //BotManager.Stop();

                }
            }

            //craft abwählen wenn multicraft
            if (craft.Count > 1)
            {
                clickone = new Vector2i(craft.ClickPos.X, craft.ClickPos.Y - (48 * HarvestTask._runtergescrollt));
                MouseManager.SetMousePosition(clickone); //craft auswählen
                GlobalLog.Warn($"mouse moved to pos {clickone}");
                await Wait.SleepSafe(150, 200);
                MouseManager.ClickLMB();
                await Wait.SleepSafe(150, 200);
            }

            return true;
        }


        public static int UnfinishedCraftingItemsCount(List<Item> items)
        {
            var count = 0;

            foreach (Item item in items)
            {
                //clusters
                if (item.Metadata.Contains("JewelPassiveTreeExpansion"))
                {
                    if (!IsFinishedClusterJewel(item))
                    {
                        count++;
                        continue;
                    }
                }
                if (item.Class == ItemClasses.Amulet || item.Class == ItemClasses.Wand || item.Class == "Convoking Wand")
                {
                    if (!IsFinishedRare(item))
                    {
                        count++;
                        continue;
                    }
                }
            }

            return count;
        }

        public static bool ItemInHarvestSlot()
        {
            if (HarvestTask.ItemFieldOne.Children.Count > 1 || HarvestTask.ItemFieldTwo.Children.Any())
            {
                GlobalLog.Debug("Item drin");
                return true;

            }
            else
            {
                GlobalLog.Debug("kein Item drin");
                return false;
            }
                

        }

        public static List<PossibleCraft> GetHarvestItemCrafts()
        {

            List<PossibleCraft> result = new List<PossibleCraft>();

            foreach (var crafts in HarvestTask.HarvestCrafts)
            {
                PossibleCraft parsedcraft = new PossibleCraft();
                var crafttext = crafts.Children[3].Text;
                int craftcount = Int32.Parse(crafts.Children[1].Children[0].Text);

                GlobalLog.Debug($"GETHARVESTCRAFTS - text: {crafttext}");

                if (craftcount != 0)
                {
                    if (crafttext.ToLower().Contains("reforge") && crafttext.ToLower().Contains("rare"))
                    {

                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;

                        //var vis = IsHarvestCraftVisible(crafts);
                        //var posy = crafts.CenterClickLocation().Y;


                        if (crafttext.ToLower().Contains("caster"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Caster;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("chaos"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Chaos;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("cold"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Cold;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("critical"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Critical;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("defence"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Defence;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("fire"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Fire;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("life"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Life;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("lightning"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Lightning;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("physical"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Physical;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("speed"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Speed;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("attack"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Attack;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("influence"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Influence;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("more likely"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.MoreLikely;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("less likely"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.LessLikely;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("10 times"))
                        {
                            parsedcraft.Type = HarvestCraftType.Reforge;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.TenTimes;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("suffixes"))
                        {
                            parsedcraft.Type = HarvestCraftType.Keep;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Suffixes;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("prefixes"))
                        {
                            parsedcraft.Type = HarvestCraftType.Keep;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Prefixes;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }

                        //GlobalLog.Warn($"type{parsedcraft.Type} subtype{parsedcraft.ItemSubType} isvisible{vis} posy: {posy} buttony: {HarvestTask.ToggleStashButton.CenterClickLocation().Y}");

                    }

                    if (crafttext.ToLower().Contains("reforge") && crafttext.ToLower().Contains("colour"))
                    {

                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;
                        if (crafttext.ToLower().Contains("non-blue"))
                        {
                            parsedcraft.Type = HarvestCraftType.Color;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Blue;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("non-green"))
                        {
                            parsedcraft.Type = HarvestCraftType.Color;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Green;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("non-red"))
                        {
                            parsedcraft.Type = HarvestCraftType.Color;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Red;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("random"))
                        {
                            if (crafttext.ToLower().Contains("{white}"))
                            {
                                parsedcraft.Type = HarvestCraftType.Keep;
                                parsedcraft.ItemSubType = HarvestItemCraftSubType.White;
                                result.Add(parsedcraft);
                                GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                            }
                            if (crafttext.ToLower().Contains("red") && crafttext.ToLower().Contains("blue") && crafttext.ToLower().Contains("green"))
                            {
                                parsedcraft.Type = HarvestCraftType.Color;
                                parsedcraft.ItemSubType = HarvestItemCraftSubType.RedBlueGreen;
                                result.Add(parsedcraft);
                                GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                            }
                            if (crafttext.ToLower().Contains("blue") && crafttext.ToLower().Contains("green"))
                            {
                                parsedcraft.Type = HarvestCraftType.Color;
                                parsedcraft.ItemSubType = HarvestItemCraftSubType.BlueGreen;
                                result.Add(parsedcraft);
                                GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                            }
                            if (crafttext.ToLower().Contains("red") && crafttext.ToLower().Contains("blue"))
                            {
                                parsedcraft.Type = HarvestCraftType.Color;
                                parsedcraft.ItemSubType = HarvestItemCraftSubType.RedBlue;
                                result.Add(parsedcraft);
                                GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                            }
                            if (crafttext.ToLower().Contains("red") && crafttext.ToLower().Contains("green"))
                            {
                                parsedcraft.Type = HarvestCraftType.Color;
                                parsedcraft.ItemSubType = HarvestItemCraftSubType.RedGreen;
                                result.Add(parsedcraft);
                                GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                            }
                            if (crafttext.ToLower().Contains("10 times"))
                            {
                                parsedcraft.Type = HarvestCraftType.Color;
                                parsedcraft.ItemSubType = HarvestItemCraftSubType.TenTimes;
                                result.Add(parsedcraft);
                                GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                            }

                        }
                    }

                    if (crafttext.ToLower().Contains("set") && crafttext.ToLower().Contains("sockets"))
                    {

                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;
                        if (crafttext.ToLower().Contains("three"))
                        {
                            parsedcraft.Type = HarvestCraftType.Sockets;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Three;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("four"))
                        {
                            parsedcraft.Type = HarvestCraftType.Sockets;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Four;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("five"))
                        {
                            parsedcraft.Type = HarvestCraftType.Sockets;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Five;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("six"))
                        {
                            parsedcraft.Type = HarvestCraftType.Sockets;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Six;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                    }

                    if (crafttext.ToLower().Contains("reforge") && crafttext.ToLower().Contains("number of sockets"))
                    {
                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;
                        parsedcraft.Type = HarvestCraftType.Sockets;
                        parsedcraft.ItemSubType = HarvestItemCraftSubType.TenTimes;
                        result.Add(parsedcraft);
                        GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                    }

                    if (crafttext.ToLower().Contains("reforge") && crafttext.ToLower().Contains("links"))
                    {

                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;
                        if (crafttext.ToLower().Contains("three"))
                        {
                            parsedcraft.Type = HarvestCraftType.Links;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Three;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("four"))
                        {
                            parsedcraft.Type = HarvestCraftType.Links;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Four;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("five"))
                        {
                            parsedcraft.Type = HarvestCraftType.Links;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Five;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("six"))
                        {
                            parsedcraft.Type = HarvestCraftType.Keep;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Six;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("10 times"))
                        {
                            parsedcraft.Type = HarvestCraftType.Links;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.TenTimes;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"item craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                    }
                    if (crafttext.ToLower().Contains("augment") && crafttext.ToLower().Contains("rare"))
                    {

                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;

                        parsedcraft.Type = HarvestCraftType.Keep;
                        //parsedcraft.ItemSubType = HarvestItemCraftSubType.Three;
                        result.Add(parsedcraft);
                        GlobalLog.Warn($"keep craft found");
                    }
                    if (crafttext.ToLower().Contains("fracture") && crafttext.ToLower().Contains("random"))
                    {

                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;

                        parsedcraft.Type = HarvestCraftType.Keep;
                        //parsedcraft.ItemSubType = HarvestItemCraftSubType.Three;
                        result.Add(parsedcraft);
                        GlobalLog.Warn($"keep craft found");
                    }
                    if (crafttext.ToLower().Contains("remove") && crafttext.ToLower().Contains("random"))
                    {

                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;

                        parsedcraft.Type = HarvestCraftType.Keep;
                        //parsedcraft.ItemSubType = HarvestItemCraftSubType.Three;
                        result.Add(parsedcraft);
                        GlobalLog.Warn($"keep craft found");
                    }
                    if (crafttext.ToLower().Contains("synthesise") && crafttext.ToLower().Contains("item"))
                    {

                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;

                        parsedcraft.Type = HarvestCraftType.Keep;
                        //parsedcraft.ItemSubType = HarvestItemCraftSubType.Three;
                        result.Add(parsedcraft);
                        GlobalLog.Warn($"keep craft found");
                    }
                    if (crafttext.ToLower().Contains("set") && crafttext.ToLower().Contains("implicit"))
                    {
                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;
                        if (crafttext.ToLower().Contains("cobalt"))
                        {
                            parsedcraft.Type = HarvestCraftType.Implicit;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Normal;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"jewel implicit craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("abyss"))
                        {
                            parsedcraft.Type = HarvestCraftType.Implicit;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Abyss;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"jewel implicit craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("cluster"))
                        {
                            parsedcraft.Type = HarvestCraftType.Implicit;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Cluster;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"jewel implicit craft found, subtype: {parsedcraft.ItemSubType}");
                        }
                    }
                    if (crafttext.ToLower().Contains("exchange") && crafttext.ToLower().Contains("orb"))
                    {
                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;
                        if (crafttext.ToLower().Contains("blessed") && crafttext.ToLower().Contains("altera"))
                        {
                            parsedcraft.Type = HarvestCraftType.Curr;
                            parsedcraft.ItemOrCurr = CurrencyNames.Blessed;
                            if (crafttext.ToLower().Contains("10"))
                            {
                                parsedcraft.Needed = 10;
                            }
                            else
                            {
                                parsedcraft.Needed = 3;
                            }
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, curr: {parsedcraft.ItemOrCurr}");
                        }
                        if (crafttext.ToLower().Contains("cartograph") && crafttext.ToLower().Contains("vaal"))
                        {
                            parsedcraft.Type = HarvestCraftType.Curr;
                            parsedcraft.ItemOrCurr = CurrencyNames.Chisel;
                            if (crafttext.ToLower().Contains("10"))
                            {
                                parsedcraft.Needed = 10;
                            }
                            else
                            {
                                parsedcraft.Needed = 3;
                            }
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, curr: {parsedcraft.ItemOrCurr}");
                        }
                        if (crafttext.ToLower().Contains("chaos") && crafttext.ToLower().Contains("exalted"))
                        {
                            parsedcraft.Type = HarvestCraftType.Curr;
                            parsedcraft.ItemOrCurr = CurrencyNames.Chaos;
                            if (crafttext.ToLower().Contains("10"))
                            {
                                parsedcraft.Needed = 10;
                            }
                            else
                            {
                                parsedcraft.Needed = 3;
                            }
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, curr: {parsedcraft.ItemOrCurr}");
                        }
                        if (crafttext.ToLower().Contains("chromatic") && crafttext.ToLower().Contains("gemcutter"))
                        {
                            parsedcraft.Type = HarvestCraftType.Curr;
                            parsedcraft.ItemOrCurr = CurrencyNames.Chromatic;
                            if (crafttext.ToLower().Contains("10"))
                            {
                                parsedcraft.Needed = 10;
                            }
                            else
                            {
                                parsedcraft.Needed = 3;
                            }
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, curr: {parsedcraft.ItemOrCurr}");
                        }
                        if (crafttext.ToLower().Contains("jeweller") && crafttext.ToLower().Contains("fusing"))
                        {
                            parsedcraft.Type = HarvestCraftType.Curr;
                            parsedcraft.ItemOrCurr = CurrencyNames.Jeweller;
                            if (crafttext.ToLower().Contains("10"))
                            {
                                parsedcraft.Needed = 10;
                            }
                            else
                            {
                                parsedcraft.Needed = 3;
                            }
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, curr: {parsedcraft.ItemOrCurr}");
                        }
                        if (crafttext.ToLower().Contains("alchemy") && crafttext.ToLower().Contains("chisel"))
                        {
                            parsedcraft.Type = HarvestCraftType.Curr;
                            parsedcraft.ItemOrCurr = CurrencyNames.Alchemy;
                            if (crafttext.ToLower().Contains("10"))
                            {
                                parsedcraft.Needed = 10;
                            }
                            else
                            {
                                parsedcraft.Needed = 3;
                            }
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, curr: {parsedcraft.ItemOrCurr}");
                        }
                        if (crafttext.ToLower().Contains("alteration") && crafttext.ToLower().Contains("chaos"))
                        {
                            parsedcraft.Type = HarvestCraftType.Curr;
                            parsedcraft.ItemOrCurr = CurrencyNames.Alteration;
                            if (crafttext.ToLower().Contains("10"))
                            {
                                parsedcraft.Needed = 10;
                            }
                            else
                            {
                                parsedcraft.Needed = 3;
                            }
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, curr: {parsedcraft.ItemOrCurr}");
                        }
                        if (crafttext.ToLower().Contains("augmentation") && crafttext.ToLower().Contains("regal"))
                        {
                            parsedcraft.Type = HarvestCraftType.Curr;
                            parsedcraft.ItemOrCurr = CurrencyNames.Augmentation;
                            if (crafttext.ToLower().Contains("10"))
                            {
                                parsedcraft.Needed = 10;
                            }
                            else
                            {
                                parsedcraft.Needed = 3;
                            }
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, curr: {parsedcraft.ItemOrCurr}");
                        }
                        if (crafttext.ToLower().Contains("scouring") && crafttext.ToLower().Contains("annul"))
                        {
                            parsedcraft.Type = HarvestCraftType.Curr;
                            parsedcraft.ItemOrCurr = CurrencyNames.Scouring;
                            if (crafttext.ToLower().Contains("10"))
                            {
                                parsedcraft.Needed = 10;
                            }
                            else
                            {
                                parsedcraft.Needed = 3;
                            }
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, curr: {parsedcraft.ItemOrCurr}");
                        }
                        if (crafttext.ToLower().Contains("transmutation") && crafttext.ToLower().Contains("alchemy"))
                        {
                            parsedcraft.Type = HarvestCraftType.Curr;
                            parsedcraft.ItemOrCurr = CurrencyNames.Transmutation;
                            if (crafttext.ToLower().Contains("10"))
                            {
                                parsedcraft.Needed = 10;
                            }
                            else
                            {
                                parsedcraft.Needed = 3;
                            }
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, curr: {parsedcraft.ItemOrCurr}");
                        }
                        if (crafttext.ToLower().Contains("wisdom") && crafttext.ToLower().Contains("chance"))
                        {
                            parsedcraft.Type = HarvestCraftType.Curr;
                            parsedcraft.ItemOrCurr = CurrencyNames.Wisdom;
                            if (crafttext.ToLower().Contains("10"))
                            {
                                parsedcraft.Needed = 10;
                            }
                            else
                            {
                                parsedcraft.Needed = 3;
                            }
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, curr: {parsedcraft.ItemOrCurr}");
                        }
                        if (crafttext.ToLower().Contains("vaal") && crafttext.ToLower().Contains("regret"))
                        {
                            parsedcraft.Type = HarvestCraftType.Curr;
                            parsedcraft.ItemOrCurr = CurrencyNames.Vaal;
                            if (crafttext.ToLower().Contains("10"))
                            {
                                parsedcraft.Needed = 10;
                            }
                            else
                            {
                                parsedcraft.Needed = 3;
                            }
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, curr: {parsedcraft.ItemOrCurr}");
                        }

                    }
                    if (crafttext.ToLower().Contains("change"))
                    {
                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;
                        if (crafttext.ToLower().Contains("breach splinters") && crafttext.ToLower().Contains("another"))
                        {
                            parsedcraft.Type = HarvestCraftType.ChangeOther;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.BreachSplinters;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("catalyst") && crafttext.ToLower().Contains("different"))
                        {
                            parsedcraft.Type = HarvestCraftType.ChangeOther;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Catalysts;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("delirium orbs") && crafttext.ToLower().Contains("different"))
                        {
                            parsedcraft.Type = HarvestCraftType.ChangeOther;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.DelOrbs;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("essences") && crafttext.ToLower().Contains("same tier"))
                        {
                            parsedcraft.Type = HarvestCraftType.ChangeOther;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Essences;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("fossils") && crafttext.ToLower().Contains("smaller"))
                        {
                            parsedcraft.Type = HarvestCraftType.ChangeOther;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Fossils;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("oils") && crafttext.ToLower().Contains("colour"))
                        {
                            parsedcraft.Type = HarvestCraftType.ChangeOther;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Oils;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("timeless") && crafttext.ToLower().Contains("emblem"))
                        {
                            parsedcraft.Type = HarvestCraftType.ChangeOther;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.TimelessSplinters;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("divination card") && crafttext.ToLower().Contains("random"))
                        {
                            parsedcraft.Type = HarvestCraftType.ChangeOther;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.DivCard;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("gem") && crafttext.ToLower().Contains("experience and quality"))
                        {
                            parsedcraft.Type = HarvestCraftType.ChangeOther;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Gem;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"curr exchange found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("offering") && crafttext.ToLower().Contains("goddess"))
                        {
                            parsedcraft.Type = HarvestCraftType.ChangeOther;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.Offering;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"exchange found, subtype: {parsedcraft.ItemSubType}");
                        }
                    }
                    if (crafttext.ToLower().Contains("sacrifice"))
                    {
                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;
                        if (crafttext.ToLower().Contains("divination") && crafttext.ToLower().Contains("different"))
                        {
                            parsedcraft.Type = HarvestCraftType.ChangeOther;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.DivCard;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"sacrifice found, subtype: {parsedcraft.ItemSubType}");
                        }
                        if (crafttext.ToLower().Contains("divination") && crafttext.ToLower().Contains("twice"))
                        {
                            parsedcraft.Type = HarvestCraftType.Keep;
                            parsedcraft.ItemSubType = HarvestItemCraftSubType.DivCard;
                            result.Add(parsedcraft);
                            GlobalLog.Warn($"sacrifice found, subtype: {parsedcraft.ItemSubType}");
                        }
                    }
                    if (crafttext.ToLower().Contains("awaken") && crafttext.ToLower().Contains("support"))
                    {
                        parsedcraft.ClickPos = crafts.CenterClickLocation();
                        parsedcraft.ClickToSavePos = crafts.Children[5].CenterClickLocation();
                        parsedcraft.Count = craftcount;
                        parsedcraft.Type = HarvestCraftType.Keep;
                        parsedcraft.ItemSubType = HarvestItemCraftSubType.Gem;
                        result.Add(parsedcraft);
                        GlobalLog.Warn($"keep awaken gem, subtype: {parsedcraft.ItemSubType}");

                    }
                }
            }

            return result;
        }

        public static Item FindSocketCraft(HarvestItemCraftSubType rerolltype, List<Item> items)
        {
            int socketroll = 0;
            if (rerolltype == HarvestItemCraftSubType.Three)
                socketroll = 3;
            if (rerolltype == HarvestItemCraftSubType.Four)
                socketroll = 4;
            if (rerolltype == HarvestItemCraftSubType.Five)
                socketroll = 5;
            if (rerolltype == HarvestItemCraftSubType.Six)
                socketroll = 6;
            if (rerolltype == HarvestItemCraftSubType.TenTimes)
                socketroll = 10;

            Item craftitem = null;

            foreach (Item item in items)
            {
                var intsize = item.Size.X * item.Size.Y;

                if (intsize < 3)
                    continue;

                if (socketroll == 10)
                {
                    var socks = item.SocketCount;
                    if (socks < intsize)
                    {
                        GlobalLog.Warn($"HarvestCraft - socketcraft gefunden");
                        return item;

                    }

                }

                if (intsize >= socketroll)
                {
                    var socks = item.SocketCount;
                    if(socks < socketroll)
                    {
                        craftitem = item;
                        GlobalLog.Warn($"HarvestCraft - socketcraft gefunden");
                        break;
                    }
                }
            }
            return craftitem;
        }

        public static Item FindLinkCraft(HarvestItemCraftSubType rerolltype, List<Item> items)
        {
            int linkroll = 0;
            if (rerolltype == HarvestItemCraftSubType.Three)
                linkroll = 3;
            if (rerolltype == HarvestItemCraftSubType.Four)
                linkroll = 4;
            if (rerolltype == HarvestItemCraftSubType.Five)
                linkroll = 5;
            if (rerolltype == HarvestItemCraftSubType.Six) //bisher unter keep, aber falls ich das mal ändere
                linkroll = 6;
            if (rerolltype == HarvestItemCraftSubType.TenTimes) //bisher unter keep, aber falls ich das mal ändere
                linkroll = 10;


            Item craftitem = null;

            foreach (Item item in items)
            {
                var intsize = item.Size.X * item.Size.Y;

                if (intsize < 3)
                    continue;

                if (linkroll == 10)
                {
                    var links = item.MaxLinkCount;
                    var socks = item.SocketCount;

                    if (links < socks)
                    {
                        GlobalLog.Warn($"HarvestCraft - linkcraft gefunden");
                        return item;
                    }
                }


                if (intsize >= linkroll && item.SocketCount >= linkroll)
                {
                    var links = item.MaxLinkCount;
                    if (links < linkroll)
                    {
                        craftitem = item;
                        GlobalLog.Warn($"HarvestCraft - linkcraft gefunden");
                        break;
                    }
                }
            }
            return craftitem;
        }

        public static Item FindJewelToImplicitRoll(HarvestItemCraftSubType rerolltype, List<Item> items, Vector2i overrideposition, StatTypeGGG desiredstat)
        {

            Item craftitem = null;

            foreach (Item item in items)
            {
                if (item.Class != ItemClasses.Jewel && item.Class != ItemClasses.AbyssJewel && !item.Metadata.Contains("JewelPassiveTreeExpansion"))
                    continue;

                if (rerolltype == HarvestItemCraftSubType.Normal && (item.Class != ItemClasses.Jewel || item.Metadata.Contains("JewelPassiveTreeExpansion")) )
                    continue;

                if (rerolltype == HarvestItemCraftSubType.Abyss && item.Class != ItemClasses.AbyssJewel)
                    continue;

                if (rerolltype == HarvestItemCraftSubType.Cluster && !item.Metadata.Contains("JewelPassiveTreeExpansion"))
                    continue;

                if (item.LocationTopLeft == overrideposition)
                {
                    var impl = item.ImplicitStats;
                    if (impl == null)
                    {
                        GlobalLog.Warn($"found jewel to put implicit on");
                        return item;
                    }
                    if (impl.Count == 0)
                    {
                        GlobalLog.Warn($"found jewel to put implicit on");
                        return item;
                    }
                    else
                    {
                        bool reroll = true;
                        foreach (KeyValuePair<StatTypeGGG, int> impli in impl)
                        { 
                            if (impli.Key == desiredstat)
                            {
                                GlobalLog.Warn($"habe implicit getroffen");
                                reroll = false;
                            }
                        }

                        if (reroll)
                        {
                            GlobalLog.Warn($"IMPLICIT ALWAYS ROLL IN SLOT 1");
                            return item;
                        }
                    }
                    
                }

                var implst = item.ImplicitStats;
                if (implst == null)
                {
                    GlobalLog.Warn($"found jewel to put implicit on");
                    return item;
                }
                if (implst.Count == 0)
                {
                    GlobalLog.Warn($"found jewel to put implicit on");
                    return item;
                }

            }
            GlobalLog.Warn($"HarvestCraft - keinen passendes jewel ohne implicit gefunden ");

            return craftitem;
        }

        public static Item FindItemToColour(HarvestItemCraftSubType rerolltype, List<Item> items, Vector2i overrideposition)
        {

            Item craftitem = null;
            var desiredreds = DesiredRedSocks;
            var desiredblues = DesiredBlueSocks;
            var desiredgreens = DesiredGreenSocks;

            var totalsocksneeded = desiredreds + desiredblues + desiredgreens;

            foreach (Item item in items)
            {

                if (item.LocationTopLeft != overrideposition)
                {
                    continue;
                }

                var socks = item.SocketCount;

                if (socks < totalsocksneeded)
                {
                    GlobalLog.Warn($"socketing: erstmal genügend sockets bekommen");
                    continue;
                }

                var colors = item.SocketColors;
                if (colors == null)
                    continue;

                var reds = 0;
                var blues = 0;
                var greens = 0;

                foreach (var color in colors)
                {
                    if (color == SocketColor.Red)
                    {
                        reds++;
                    }
                    if (color == SocketColor.Blue)
                    {
                        blues++;
                    }
                    if (color == SocketColor.Green)
                    {
                        greens++;
                    }

                }
                GlobalLog.Warn($"item colors Red{reds} Blue{blues} green{greens}");

                var redneeded = desiredreds - reds;
                var blueneeded = desiredblues - blues;
                var greenneeded = desiredgreens - greens;
                var allneeded = redneeded + blueneeded + greenneeded;

                if (rerolltype == HarvestItemCraftSubType.TenTimes && allneeded >= 4)
                {
                    GlobalLog.Warn($"muss eh noch {allneeded} socks ändern.. tentimes ok");
                    return item;
                }

                if (rerolltype == HarvestItemCraftSubType.Red && redneeded >= 1)
                {
                    GlobalLog.Warn($"brauche rot {redneeded} red ok");
                    return item;
                }

                if (rerolltype == HarvestItemCraftSubType.Blue && blueneeded >= 1)
                {
                    GlobalLog.Warn($"brauche rot {redneeded} blue ok");
                    return item;
                }

                if (rerolltype == HarvestItemCraftSubType.Green && greenneeded >= 1)
                {
                    GlobalLog.Warn($"brauche rot {redneeded} green ok");
                    return item;
                }

                if (rerolltype == HarvestItemCraftSubType.RedBlue && redneeded >= 1 && blueneeded >= 1)
                {
                    GlobalLog.Warn($"brauche rot {redneeded} blau {blueneeded} redblue ok");
                    return item;
                }

                if (rerolltype == HarvestItemCraftSubType.RedGreen && redneeded >= 1 && greenneeded >= 1)
                {
                    GlobalLog.Warn($"brauche rot {redneeded} gruen {greenneeded} redgreen ok");
                    return item;
                }

                if (rerolltype == HarvestItemCraftSubType.BlueGreen && blueneeded >= 1 && greenneeded >= 1)
                {
                    GlobalLog.Warn($"brauche blau {blueneeded} gruen {greenneeded} bluegreen ok");
                    return item;
                }

                if (rerolltype == HarvestItemCraftSubType.RedBlueGreen && blueneeded >= 1 && greenneeded >= 1 && redneeded >= 1)
                {
                    GlobalLog.Warn($"brauche rot {redneeded} blau {blueneeded} gruen {greenneeded} redbluegreen ok");
                    return item;
                }

            }
            GlobalLog.Warn($"HarvestCraft - kein socket colour craft gefunden");

            return craftitem;
        }

        public static Item FindItemToCraft(HarvestItemCraftSubType rerolltype, List<Item> items)
        {

            if (UnfinishedCraftingItemsCount(items) == 0)
            {

                GlobalLog.Error($"HarvestCraft - keine items zum craften da");
                return null;
            }
            Item craftitem = null;

            //erst clusters
            foreach (Item item in items)
            {
                if (item.Metadata.Contains("JewelPassiveTreeExpansion"))
                {
                    if (IsGoodCraftingBaseClusterJewel(item, rerolltype))
                    {
                        GlobalLog.Warn($"HarvestCraft - cluster zum rollen gefunden");
                        //craftitem = item;
                        return item;
                    }
                }
            }

            GlobalLog.Warn($"HarvestCraft - keinen guten cluster gefunden -> reroll any on cluster");
            //dieses mal alle, weil man hat kein gutes gefunden
            foreach (Item item in items)
            {
                if (item.Metadata.Contains("JewelPassiveTreeExpansion"))
                {

                    if (IsGoodCraftingBaseClusterJewel(item, rerolltype, true))
                    {
                        GlobalLog.Warn($"HarvestCraft - cluster zum rollen gefunden");
                        craftitem = item;
                        return item;
                    }
                }
            }

            //dann wands
            foreach (Item item in items)
            {
                if (item.Class == ItemClasses.Wand || item.Class == "Convoking Wand" || item.Class == ItemClasses.Amulet)
                {
                    if (IsGoodRareBase(item, rerolltype))
                    {
                        GlobalLog.Warn($"HarvestCraft - wand/amu zum rollen gefunden");
                        //craftitem = item;
                        return item;
                    }
                }

            }
            


            return craftitem;
        }

        private static void HarvestDebug()
        {
            var obj = LokiPoe.ObjectManager.Objects;
            foreach (var item in obj)
            {
                var irrigator = item as HarvestIrrigator;
                if (irrigator != null)
                {
                    GlobalLog.Debug($"{irrigator.Name} [Id: {irrigator.Id}]");
                    if (!irrigator.IsUiVisible)
                    {
                        GlobalLog.Debug($"\tUi not Visible:");
                    }
                    else
                    {
                        GlobalLog.Debug($"\tDescription: {irrigator.Description}");
                        GlobalLog.Debug($"\tMagicProperties:");
                        foreach (var i in irrigator.MagicProperties)
                        {
                            GlobalLog.Debug($"\t\t{i}");
                        }
                        GlobalLog.Debug($"\tCraftingOutCome:");
                        foreach (var i in irrigator.CraftingOutCome)
                        {
                            GlobalLog.Debug($"\t\t{i.Item1} : {i.Item2}");
                        }
                    }
                }
                var extractor = item as HarvestExtraxtor;
                if (extractor != null)
                {
                    GlobalLog.Debug($"{extractor.Name} [Id: {extractor.Id}]");
                    if (!extractor.IsUiVisible)
                    {
                        GlobalLog.Debug($"\tUi not Visible:");
                    }
                    else
                    {
                        GlobalLog.Debug($"\tDescription: {extractor.Description}");
                        GlobalLog.Debug($"\tMagicProperties:");
                        foreach (var i in extractor.MagicProperties)
                        {
                            GlobalLog.Debug($"\t\t{i}");
                        }
                        GlobalLog.Debug($"\tCraftingOutCome:");
                        foreach (var i in extractor.CraftingOutCome)
                        {
                            GlobalLog.Debug($"\t\t{i.Item1} : {i.Item2}");
                        }
                        if (extractor.Description == "Craft")
                        {
                            LokiPoe.ProcessHookManager.Enable();
                            extractor.Activate();
                            LokiPoe.ProcessHookManager.Disable();
                        }
                    }
                }
            }
        }

        private static void ActivateIrrigator()
        {
            var obj = LokiPoe.ObjectManager.Objects;
            foreach (var item in obj)
            {
                var irrigator = item as HarvestIrrigator;
                if (irrigator != null)
                {
                    GlobalLog.Debug($"{irrigator.Name} [Id: {irrigator.Id}]");
                    if (!irrigator.IsUiVisible)
                    {
                        GlobalLog.Debug($"\tUi not Visible:");
                    }
                    else
                    {
                        GlobalLog.Debug($"\tDescription: {irrigator.Description}");
                        GlobalLog.Debug($"\tMagicProperties:");
                        foreach (var i in irrigator.MagicProperties)
                        {
                            GlobalLog.Debug($"\t\t{i}");
                        }
                        GlobalLog.Debug($"\tCraftingOutCome:");
                        foreach (var i in irrigator.CraftingOutCome)
                        {
                            GlobalLog.Debug($"\t\t{i.Item1} : {i.Item2}");
                        }

                        if (irrigator.Distance < 50)
                        {
                            irrigator.Activate();
                        }

                    }
                }

            }
        }

        public static bool ScrollToCraft(PossibleCraft craft)
        {
            if (HarvestTask._runtergescrollt > 0)
            {
                for (var i = 0; i < HarvestTask._runtergescrollt; i++)
                {
                    GlobalLog.Debug("scrolle hoch");
                    MouseManager.SetMousePosition(Default.EXtensions.CommonTasks.HarvestTask.ToggleStashButton.CenterClickLocation().X, Default.EXtensions.CommonTasks.HarvestTask.ToggleStashButton.CenterClickLocation().Y - 70);
                    MouseManager.ScrollMouseUp();
                }
                HarvestTask._runtergescrollt = 0;
            }

            if (craft.ClickPos.Y < HarvestTask.ToggleStashButton.CenterClickLocation().Y - 40)
            {
                GlobalLog.Debug("craft auf der ersten seite");
                return true;
            }

            int scrollen = ((craft.ClickPos.Y - (HarvestTask.ToggleStashButton.CenterClickLocation().Y - 40)) / 48);

            GlobalLog.Debug($"y{craft.ClickPos.Y} refpunkt{HarvestTask.ToggleStashButton.CenterClickLocation().Y - 40}  scrolle {scrollen} mal");
            for (var i = 0; i < scrollen; i++)
            {
                GlobalLog.Debug("scrolle runter");
                MouseManager.SetMousePosition(Default.EXtensions.CommonTasks.HarvestTask.ToggleStashButton.CenterClickLocation().X, Default.EXtensions.CommonTasks.HarvestTask.ToggleStashButton.CenterClickLocation().Y - 70);
                MouseManager.ScrollMouseDown();
                HarvestTask._runtergescrollt += 1;
            }

            return true;

        }

        public static bool IsFinishedClusterJewel(Item item)
        {
            var stats = item.Stats;
            if (stats == null)
            {
                GlobalLog.Debug($"stats null");
                return false;
            }
            if (stats.Count == 0)
            {
                GlobalLog.Debug($"stats 0");
                return false;
            }

            //gute mods

            var goodstatscounter = 0;
            foreach (KeyValuePair<StatTypeGGG, int> pair in stats)
            {
                if (IsNotable(pair.Key))
                {
                    //GlobalLog.Debug($"isgoodcluster: notable - key: {pair.Key} value: {pair.Value}");
                    goodstatscounter++;
                    continue;
                }
                    
            }

            if (goodstatscounter >= 1 && item.Name.Contains("Small"))
            {
                return true;
            }

            if (goodstatscounter >= 2 && item.Name.Contains("Medium"))
            {
                return true;
            }

            if (goodstatscounter >= 3 && item.Name.Contains("Large"))
            {
                return true;
            }


            return false;
        }

        public static bool IsFinishedRare(Item item, bool logstats = false)
        {
            var stats = item.Stats;
            if (stats == null)
            {
                GlobalLog.Debug($"stats null");
                return false;
            }
            if (stats.Count == 0)
            {
                GlobalLog.Debug($"stats 0");
                return false;
            }

            //gute mods

            var goodstatscounter = 0;
            foreach (KeyValuePair<StatTypeGGG, int> pair in stats)
            {
                if (logstats)
                    GlobalLog.Warn($"stat log {pair.Key} {pair.Value}");


                if (pair.Key == StatTypeGGG.PhysicalSpellSkillGemLevelPos)
                {
                    //goodstatscounter++;
                    continue;
                }
                if (pair.Key == StatTypeGGG.SpellSkillGemLevelPos)
                {
                    goodstatscounter++;
                    continue;
                }
                if (pair.Key == StatTypeGGG.MinionSkillGemLevelPos)
                {
                    goodstatscounter++;
                    continue;
                }
                if (pair.Key == StatTypeGGG.FireSkillGemLevelPos)
                {
                    //goodstatscounter++;
                    continue;
                }
                if (pair.Key == StatTypeGGG.ColdSkillGemLevelPos)
                {
                    //goodstatscounter++;
                    continue;
                }
                if (pair.Key == StatTypeGGG.LightningSkillGemLevelPos)
                {
                    //goodstatscounter++;
                    continue;
                }
                if (pair.Key == StatTypeGGG.ChaosSkillGemLevelPos)
                {
                    //goodstatscounter++;
                    continue;
                }
                if (pair.Key == StatTypeGGG.AllSkillGemLevelPos)
                {
                    goodstatscounter++;
                    continue;
                }
                if (pair.Key == StatTypeGGG.DexteritySkillGemLevelPos)
                {
                    goodstatscounter++;
                    continue;
                }
                if (pair.Key == StatTypeGGG.StrengthSkillGemLevelPos)
                {
                    goodstatscounter++;
                    continue;
                }
                if (pair.Key == StatTypeGGG.IntelligenceSkillGemLevelPos)
                {
                    goodstatscounter++;
                    continue;
                }

            }

            if (goodstatscounter >= 1)
            {
                return true;
            }


            return false;
        }

        public static bool IsGoodRareBase(Item item, HarvestItemCraftSubType rerolltype)
        {
            if (IsFinishedRare(item, true)) //nochmal zur sicherheit
            {
                return false;
            }

            if (item.Class == ItemClasses.Wand || item.Class == ItemClasses.Amulet || item.Class == "Convoking Wand")
            {
                if (rerolltype == HarvestItemCraftSubType.Cold)
                {
                    return true;
                }
                if (rerolltype == HarvestItemCraftSubType.Physical)
                {

                    return true;

                }
                if (rerolltype == HarvestItemCraftSubType.Fire)
                {
                    return true;
                }
                if (rerolltype == HarvestItemCraftSubType.Lightning)
                {
                    return true;
                }
                if (rerolltype == HarvestItemCraftSubType.Critical)
                {

                    return true;
                }
                if (rerolltype == HarvestItemCraftSubType.Attack)
                {

                    return true;
                }
                if (rerolltype == HarvestItemCraftSubType.Caster)
                {

                    return true;

                }
                if (rerolltype == HarvestItemCraftSubType.Chaos)
                {

                    return true;

                }
                if (rerolltype == HarvestItemCraftSubType.Life) //bisher wildcard
                {

                    return true;

                }
                if (rerolltype == HarvestItemCraftSubType.Defence && item.Class != ItemClasses.Wand && item.Class != "Convoking Wand") //nicht bei wands
                {

                    return true; 

                }
                if (rerolltype == HarvestItemCraftSubType.Speed) //bisher wildcard
                {

                    return true;

                }
                if (rerolltype == HarvestItemCraftSubType.MoreLikely) //bisher wildcard
                {

                    return true;

                }
                if (rerolltype == HarvestItemCraftSubType.LessLikely) //bisher wildcard
                {

                    return true;

                }
                if (rerolltype == HarvestItemCraftSubType.TenTimes) //bisher wildcard
                {

                    return true;

                }
            }

            return false;
        }

        public static bool IsGoodCraftingBaseClusterJewel(Item item, HarvestItemCraftSubType rerolltype, bool any = false)
        {
            if (!item.Metadata.Contains("JewelPassiveTreeExpansion"))
            {
                return false;
            }

            if (IsFinishedClusterJewel(item))
            {
                return false;
            }

            var ench = item.Components.ModsComponent.EnchantmentsString;
            ench.RemoveAt(0);
            ench.RemoveAll(i => i.Contains("Passive Skills are Jewel Sockets"));
            string mod = ench.FirstOrDefault();
            mod = mod.Replace("\n", ", ");
            mod = mod.Replace("Added Small Passive Skills grant: ", "");

            //GlobalLog.Debug($"Harvestcraft cluster jewel mod: {mod}");

            if (any)
            {
                if (rerolltype == HarvestItemCraftSubType.Defence || rerolltype == HarvestItemCraftSubType.Cold || rerolltype == HarvestItemCraftSubType.Chaos /*|| rerolltype == HarvestItemCraftSubType.Physical*/ || rerolltype == HarvestItemCraftSubType.Fire || rerolltype == HarvestItemCraftSubType.Lightning /*|| rerolltype == HarvestItemCraftSubType.Critical || rerolltype == HarvestItemCraftSubType.Attack || rerolltype == HarvestItemCraftSubType.Caster*/ || rerolltype == HarvestItemCraftSubType.Life /*|| rerolltype == HarvestItemCraftSubType.Speed*/ || rerolltype == HarvestItemCraftSubType.MoreLikely || rerolltype == HarvestItemCraftSubType.LessLikely || rerolltype == HarvestItemCraftSubType.TenTimes) //bisher wildcard
                {

                    return true;

                }

            }
            else
            {
                //cold cluster

                if (rerolltype == HarvestItemCraftSubType.Cold)
                {
                    if (mod.ToLower().Contains("cold"))
                    {
                        return true;
                    }
                }
                if (rerolltype == HarvestItemCraftSubType.Physical)
                {
                    if (mod.ToLower().Contains("physical"))
                    {
                        return true;
                    }
                }
                if (rerolltype == HarvestItemCraftSubType.Fire)
                {
                    if (mod.ToLower().Contains("fire"))
                    {
                        return true;
                    }
                }
                if (rerolltype == HarvestItemCraftSubType.Lightning)
                {
                    if (mod.ToLower().Contains("lightning"))
                    {
                        return true;
                    }
                }
                if (rerolltype == HarvestItemCraftSubType.Critical)
                {
                    if (mod.ToLower().Contains("critical"))
                    {
                        return true;
                    }
                }
                if (rerolltype == HarvestItemCraftSubType.Attack)
                {
                    if (mod.ToLower().Contains("attack") || mod.ToLower().Contains("damage"))
                    {
                        return true;
                    }
                }
                if (rerolltype == HarvestItemCraftSubType.Caster)
                {
                    if (mod.ToLower().Contains("spell"))
                    {
                        return true;
                    }
                }
                if (rerolltype == HarvestItemCraftSubType.Chaos)
                {
                    if (mod.ToLower().Contains("chaos") || mod.ToLower().Contains("over time"))
                    {
                        return true;
                    }
                }
                if (rerolltype == HarvestItemCraftSubType.Life) //bisher wildcard
                {

                    return true;

                }
                if (rerolltype == HarvestItemCraftSubType.Defence) //bisher wildcard
                {

                    return true;

                }
                if (rerolltype == HarvestItemCraftSubType.Speed) //bisher wildcard
                {

                    //return true;

                }
                if (rerolltype == HarvestItemCraftSubType.MoreLikely) //bisher wildcard
                {

                    return true;

                }
                if (rerolltype == HarvestItemCraftSubType.LessLikely) //bisher wildcard
                {

                    return true;

                }
                if (rerolltype == HarvestItemCraftSubType.TenTimes) //bisher wildcard
                {

                    return true;

                }
            }


            return false;
        }

        public static bool IsCachedCraft(PossibleCraft craft)
        {
            if (CachedCraft == null)
                return false;

            


            if (craft.ClickPos == CachedCraft.ClickPos && craft.Count == CachedCraft.Count)
            {
                GlobalLog.Debug($"XXX cached {CachedCraft.ClickPos} vs {craft.ClickPos} craft {craft.Type}{craft.Count}");
                GlobalLog.Error($"HarvestTask - Irgendwas hat nicht geklappt, craft == cached");
                BotManager.Stop();
                return true;
            }

            return false;

        }

        public static bool IsNotable(StatTypeGGG stat)
        {
            if (ClusterNotables.Contains(stat))
                return true;
            else 
                return false;
        }

        private static List<List<StatTypeGGG>> GoodTripleCombosLargeCJ = new List<List<StatTypeGGG>>()
        {
            new List<StatTypeGGG> { StatTypeGGG.LocalAfflictionNotableViciousSkewering, StatTypeGGG.LocalAfflictionNotableMartialProwess , StatTypeGGG.LocalAfflictionNotableDriveTheDestruction},
            new List<StatTypeGGG> { StatTypeGGG.LocalAfflictionNotableSmiteTheWeak, StatTypeGGG.LocalAfflictionNotableFeedTheFury , StatTypeGGG.LocalAfflictionNotableDriveTheDestruction},

        };

        private static List<List<StatTypeGGG>> GoodDoubleCombosLargeCJ = new List<List<StatTypeGGG>>()
        {
            new List<StatTypeGGG> { StatTypeGGG.LocalAfflictionNotableRazeAndPillage, StatTypeGGG.LocalAfflictionNotableViciousBite },
            new List<StatTypeGGG> { StatTypeGGG.LocalAfflictionNotableCremator, StatTypeGGG.LocalAfflictionNotableSmokingRemains },
            new List<StatTypeGGG> { StatTypeGGG.LocalAfflictionNotableCallToTheSlaughter, StatTypeGGG.LocalAfflictionNotableFeastingFiends },
            new List<StatTypeGGG> { StatTypeGGG.LocalAfflictionNotableCallToTheSlaughter, StatTypeGGG.LocalAfflictionNotableRottenClaws },
            new List<StatTypeGGG> { StatTypeGGG.LocalAfflictionNotableFuelTheFight, StatTypeGGG.LocalAfflictionNotableMartialProwess },
            new List<StatTypeGGG> { StatTypeGGG.LocalAfflictionNotableFuelTheFight, StatTypeGGG.LocalAfflictionNotableCalamitous },
            new List<StatTypeGGG> { StatTypeGGG.LocalAfflictionNotableDriveTheDestruction, StatTypeGGG.LocalAfflictionNotableWeightAdvantage },





        };

        private static List<List<StatTypeGGG>> GoodDoubleCombosMediumCJ = new List<List<StatTypeGGG>>()
        {
            new List<StatTypeGGG> { StatTypeGGG.LocalAfflictionNotableCultLeader, StatTypeGGG.LocalAfflictionNotableInvigoratingPortents },


        };

        private static List<List<StatTypeGGG>> GoodSingleCombosMediumCJ = new List<List<StatTypeGGG>>()
        {
            new List<StatTypeGGG> { StatTypeGGG.LocalAfflictionNotableBlessedRebirth},
        };


        private static List<StatTypeGGG> ClusterNotables = new List<StatTypeGGG>()
        {
            StatTypeGGG.LocalAfflictionNotableProdigiousDefense,
            StatTypeGGG.LocalAfflictionNotableAdvanceGuard,
            StatTypeGGG.LocalAfflictionNotableGladiatorialCombat,
            StatTypeGGG.LocalAfflictionNotableStrikeLeader,
            StatTypeGGG.LocalAfflictionNotablePowerfulWard,
            StatTypeGGG.LocalAfflictionNotableEnduringWard,
            StatTypeGGG.LocalAfflictionNotableGladiatorsFortitude,
            StatTypeGGG.LocalAfflictionNotablePreciseRetaliation,
            StatTypeGGG.LocalAfflictionNotableVeteranDefender,
            StatTypeGGG.LocalAfflictionNotableIronBreaker,
            StatTypeGGG.LocalAfflictionNotableDeepCuts,
            StatTypeGGG.LocalAfflictionNotableMasterTheFundamentals,
            StatTypeGGG.LocalAfflictionNotableForceMultiplier,
            StatTypeGGG.LocalAfflictionNotableFuriousAssault,
            StatTypeGGG.LocalAfflictionNotableViciousSkewering,
            StatTypeGGG.LocalAfflictionNotableGrimOath,
            StatTypeGGG.LocalAfflictionNotableBattleHardened,
            StatTypeGGG.LocalAfflictionNotableReplenishingPresence,
            StatTypeGGG.LocalAfflictionNotableMasterOfCommand,
            StatTypeGGG.LocalAfflictionNotableFirstAmongEquals,
            StatTypeGGG.LocalAfflictionNotablePurposefulHarbinger,
            StatTypeGGG.LocalAfflictionNotablePreciseCommander,
            StatTypeGGG.LocalAfflictionNotablePureCommander,
            StatTypeGGG.LocalAfflictionNotableStalwartCommander,
            StatTypeGGG.LocalAfflictionNotableVengefulCommander,
            StatTypeGGG.LocalAfflictionNotableSkullbreaker,
            StatTypeGGG.LocalAfflictionNotablePressurePoints,
            StatTypeGGG.LocalAfflictionNotableOverwhelmingMalice,
            StatTypeGGG.LocalAfflictionNotableMagnifier,
            StatTypeGGG.LocalAfflictionNotableSavageResponse,
            StatTypeGGG.LocalAfflictionNotableEyeOfTheStorm,
            StatTypeGGG.LocalAfflictionNotableBasicsOfPain,
            StatTypeGGG.LocalAfflictionNotableQuickGetaway,
            StatTypeGGG.LocalAfflictionNotableAssertDominance,
            StatTypeGGG.LocalAfflictionNotableVastPower,
            StatTypeGGG.LocalAfflictionNotablePowerfulAssault,
            StatTypeGGG.LocalAfflictionNotableIntensity,
            StatTypeGGG.LocalAfflictionNotableTitanicSwings,
            StatTypeGGG.LocalAfflictionNotableToweringThreat,
            StatTypeGGG.LocalAfflictionNotableAncestralEcho,
            StatTypeGGG.LocalAfflictionNotableAncestralReach,
            StatTypeGGG.LocalAfflictionNotableAncestralMight,
            StatTypeGGG.LocalAfflictionNotableAncestralPreservation,
            StatTypeGGG.LocalAfflictionNotableSnaringSpirits,
            StatTypeGGG.LocalAfflictionNotableSleeplessSentries,
            StatTypeGGG.LocalAfflictionNotableAncestralGuidance,
            StatTypeGGG.LocalAfflictionNotableAncestralInspiration,
            StatTypeGGG.LocalAfflictionNotableVitalFocus,
            StatTypeGGG.LocalAfflictionNotableRapidInfusion,
            StatTypeGGG.LocalAfflictionNotableUnwaveringFocus,
            StatTypeGGG.LocalAfflictionNotableEnduringFocus,
            StatTypeGGG.LocalAfflictionNotablePreciseFocus,
            StatTypeGGG.LocalAfflictionNotableStoicFocus,
            StatTypeGGG.LocalAfflictionNotableHexBreaker,
            StatTypeGGG.LocalAfflictionNotableArcaneFocus,
            StatTypeGGG.LocalAfflictionNotableDistilledPerfection,
            StatTypeGGG.LocalAfflictionNotableSpikedConcoction,
            StatTypeGGG.LocalAfflictionNotableFasting,
            StatTypeGGG.LocalAfflictionNotableMendersWellspring,
            StatTypeGGG.LocalAfflictionNotableSpecialReserve,
            StatTypeGGG.LocalAfflictionNotableNumbingElixir,
            StatTypeGGG.LocalAfflictionNotableMobMentality,
            StatTypeGGG.LocalAfflictionNotableCryWolf,
            StatTypeGGG.LocalAfflictionNotableHauntingShout,
            StatTypeGGG.LocalAfflictionNotableLeadByExample,
            StatTypeGGG.LocalAfflictionNotableProvocateur,
            StatTypeGGG.LocalAfflictionNotableWarningCall,
            StatTypeGGG.LocalAfflictionNotableRattlingBellow,
            StatTypeGGG.LocalAfflictionNotableBloodscent,
            StatTypeGGG.LocalAfflictionNotableRunThrough,
            StatTypeGGG.LocalAfflictionNotableWoundAggravation,
            StatTypeGGG.LocalAfflictionNotableOverlord,
            StatTypeGGG.LocalAfflictionNotableExpansiveMight,
            StatTypeGGG.LocalAfflictionNotableWeightAdvantage,
            StatTypeGGG.LocalAfflictionNotableWindUp,
            StatTypeGGG.LocalAfflictionNotableFanOfBlades,
            StatTypeGGG.LocalAfflictionNotableDiseaseVector,
            StatTypeGGG.LocalAfflictionNotableArcingShot,
            StatTypeGGG.LocalAfflictionNotableTemperedArrowheads,
            StatTypeGGG.LocalAfflictionNotableBroadside,
            StatTypeGGG.LocalAfflictionNotableExplosiveForce,
            StatTypeGGG.LocalAfflictionNotableOpportunisticFusilade,
            StatTypeGGG.LocalAfflictionNotableStormsHand,
            StatTypeGGG.LocalAfflictionNotableBattlefieldDominator,
            StatTypeGGG.LocalAfflictionNotableMartialMastery,
            StatTypeGGG.LocalAfflictionNotableSurefootedStriker,
            StatTypeGGG.LocalAfflictionNotableGracefulExecution,
            StatTypeGGG.LocalAfflictionNotableBrutalInfamy,
            StatTypeGGG.LocalAfflictionNotableFearsomeWarrior,
            StatTypeGGG.LocalAfflictionNotableCombatRhythm,
            StatTypeGGG.LocalAfflictionNotableHitAndRun,
            StatTypeGGG.LocalAfflictionNotableInsatiableKiller,
            StatTypeGGG.LocalAfflictionNotableMageBane,
            StatTypeGGG.LocalAfflictionNotableMartialMomentum,
            StatTypeGGG.LocalAfflictionNotableDeadlyRepartee,
            StatTypeGGG.LocalAfflictionNotableQuickAndDeadly,
            StatTypeGGG.LocalAfflictionNotableSmiteTheWeak,
            StatTypeGGG.LocalAfflictionNotableHeavyHitter,
            StatTypeGGG.LocalAfflictionNotableMartialProwess,
            StatTypeGGG.LocalAfflictionNotableCalamitous,
            StatTypeGGG.LocalAfflictionNotableDevastator,
            StatTypeGGG.LocalAfflictionNotableFuelTheFight,
            StatTypeGGG.LocalAfflictionNotableDriveTheDestruction,
            StatTypeGGG.LocalAfflictionNotableFeedTheFury,
            StatTypeGGG.LocalAfflictionNotableSealMender,
            StatTypeGGG.LocalAfflictionNotableConjuredWall,
            StatTypeGGG.LocalAfflictionNotableArcaneHeroism,
            StatTypeGGG.LocalAfflictionNotablePracticedCaster,
            StatTypeGGG.LocalAfflictionNotableBurdenProjection,
            StatTypeGGG.LocalAfflictionNotableThaumophage,
            StatTypeGGG.LocalAfflictionNotableEssenceRush,
            StatTypeGGG.LocalAfflictionNotableSapPsyche,
            StatTypeGGG.LocalAfflictionNotableSadist,
            StatTypeGGG.LocalAfflictionNotableCorrosiveElements,
            StatTypeGGG.LocalAfflictionNotableDoryanisLesson,
            StatTypeGGG.LocalAfflictionNotableDisorientingDisplay,
            StatTypeGGG.LocalAfflictionNotablePrismaticHeart,
            StatTypeGGG.LocalAfflictionNotableWidespreadDestruction,
            StatTypeGGG.LocalAfflictionNotableMasterOfFire,
            StatTypeGGG.LocalAfflictionNotableSmokingRemains,
            StatTypeGGG.LocalAfflictionNotableCremator,
            StatTypeGGG.LocalAfflictionNotableSnowstorm,
            StatTypeGGG.LocalAfflictionNotableStormDrinker,
            StatTypeGGG.LocalAfflictionNotableParalysis,
            StatTypeGGG.LocalAfflictionNotableSupercharge,
            StatTypeGGG.LocalAfflictionNotableBlanketedSnow,
            StatTypeGGG.LocalAfflictionNotableColdToTheCore,
            StatTypeGGG.LocalAfflictionNotableColdBloodedKiller,
            StatTypeGGG.LocalAfflictionNotableTouchOfCruelty,
            StatTypeGGG.LocalAfflictionNotableUnwaveringlyEvil,
            StatTypeGGG.LocalAfflictionNotableUnspeakableGifts,
            StatTypeGGG.LocalAfflictionNotableDarkIdeation,
            StatTypeGGG.LocalAfflictionNotableUnholyGrace,
            StatTypeGGG.LocalAfflictionNotableWickedPall,
            StatTypeGGG.LocalAfflictionNotableRenewal,
            StatTypeGGG.LocalAfflictionNotableRazeAndPillage,
            StatTypeGGG.LocalAfflictionNotableRottenClaws,
            StatTypeGGG.LocalAfflictionNotableCallToTheSlaughter,
            StatTypeGGG.LocalAfflictionNotableHulkingCorpses,
            StatTypeGGG.LocalAfflictionNotableViciousBite,
            StatTypeGGG.LocalAfflictionNotablePrimordialBond,
            StatTypeGGG.LocalAfflictionNotableBlowback,
            StatTypeGGG.LocalAfflictionNotableFanTheFlames,
            StatTypeGGG.LocalAfflictionNotableCookedAlive,
            StatTypeGGG.LocalAfflictionNotableBurningBright,
            StatTypeGGG.LocalAfflictionNotableWrappedInFlame,
            StatTypeGGG.LocalAfflictionNotableVividHues,
            StatTypeGGG.LocalAfflictionNotableRend,
            StatTypeGGG.LocalAfflictionNotableDisorientingWounds,
            StatTypeGGG.LocalAfflictionNotableCompoundInjury,
            StatTypeGGG.LocalAfflictionNotableSepticSpells,
            StatTypeGGG.LocalAfflictionNotableLowTolerance,
            StatTypeGGG.LocalAfflictionNotableSteadyTorment,
            StatTypeGGG.LocalAfflictionNotableEternalSuffering,
            StatTypeGGG.LocalAfflictionNotableEldritchInspiration,
            StatTypeGGG.LocalAfflictionNotableWastingAffliction,
            StatTypeGGG.LocalAfflictionNotableHaemorrhage,
            StatTypeGGG.LocalAfflictionNotableFlowOfLife,
            StatTypeGGG.LocalAfflictionNotableExposureTherapy,
            StatTypeGGG.LocalAfflictionNotableBrushWithDeath,
            StatTypeGGG.LocalAfflictionNotableVileReinvigoration,
            StatTypeGGG.LocalAfflictionNotableCirclingOblivion,
            StatTypeGGG.LocalAfflictionNotableBrewedForPotency,
            StatTypeGGG.LocalAfflictionNotableAstonishingAffliction,
            StatTypeGGG.LocalAfflictionNotableColdConduction,
            StatTypeGGG.LocalAfflictionNotableInspiredOppression,
            StatTypeGGG.LocalAfflictionNotableChillingPresence,
            StatTypeGGG.LocalAfflictionNotableDeepChill,
            StatTypeGGG.LocalAfflictionNotableBlastFreeze,
            StatTypeGGG.LocalAfflictionNotableThunderstruck,
            StatTypeGGG.LocalAfflictionNotableStormrider,
            StatTypeGGG.LocalAfflictionNotableOvershock,
            StatTypeGGG.LocalAfflictionNotableEvilEye,
            StatTypeGGG.LocalAfflictionNotableWhispersOfDeath,
            StatTypeGGG.LocalAfflictionNotableWardbreaker,
            StatTypeGGG.LocalAfflictionNotableDarkDiscourse,
            StatTypeGGG.LocalAfflictionNotableVictimMaker,
            StatTypeGGG.LocalAfflictionNotableMasterOfFear,
            StatTypeGGG.LocalAfflictionNotableWishForDeath,
            StatTypeGGG.LocalAfflictionNotableHeraldry,
            StatTypeGGG.LocalAfflictionNotableEndbringer,
            StatTypeGGG.LocalAfflictionNotableCultLeader,
            StatTypeGGG.LocalAfflictionNotableEmpoweredEnvoy,
            StatTypeGGG.LocalAfflictionNotableDarkMessenger,
            StatTypeGGG.LocalAfflictionNotableAgentOfDestruction,
            StatTypeGGG.LocalAfflictionNotableLastingImpression,
            StatTypeGGG.LocalAfflictionNotableSelfFulfillingProphecy,
            StatTypeGGG.LocalAfflictionNotableInvigoratingPortents,
            StatTypeGGG.LocalAfflictionNotablePureAgony,
            StatTypeGGG.LocalAfflictionNotableDisciples,
            StatTypeGGG.LocalAfflictionNotableDreadMarch,
            StatTypeGGG.LocalAfflictionNotableBlessedRebirth,
            StatTypeGGG.LocalAfflictionNotableLifeFromDeath,
            StatTypeGGG.LocalAfflictionNotableFeastingFiends,
            StatTypeGGG.LocalAfflictionNotableBodyguards,
            StatTypeGGG.LocalAfflictionNotableFollowThrough,
            StatTypeGGG.LocalAfflictionNotableStreamlined,
            StatTypeGGG.LocalAfflictionNotableShriekingBolts,
            StatTypeGGG.LocalAfflictionNotableEyeToEye,
            StatTypeGGG.LocalAfflictionNotableRepeater,
            StatTypeGGG.LocalAfflictionNotableAerodynamics,
            StatTypeGGG.LocalAfflictionNotableChipAway,
            StatTypeGGG.LocalAfflictionNotableSeekerRunes,
            StatTypeGGG.LocalAfflictionNotableRemarkable,
            StatTypeGGG.LocalAfflictionNotableBrandLoyalty,
            StatTypeGGG.LocalAfflictionNotableHolyConquest,
            StatTypeGGG.LocalAfflictionNotableGrandDesign,
            StatTypeGGG.LocalAfflictionNotableSetAndForget,
            StatTypeGGG.LocalAfflictionNotableExpertSabotage,
            StatTypeGGG.LocalAfflictionNotableGuerillaTactics,
            StatTypeGGG.LocalAfflictionNotableExpendability,
            StatTypeGGG.LocalAfflictionNotableArcanePyrotechnics,
            StatTypeGGG.LocalAfflictionNotableSurpriseSabotage,
            StatTypeGGG.LocalAfflictionNotableCarefulHandling,
            StatTypeGGG.LocalAfflictionNotablePeakVigour,
            StatTypeGGG.LocalAfflictionNotableFettle,
            StatTypeGGG.LocalAfflictionNotableFeastOfFlesh,
            StatTypeGGG.LocalAfflictionNotableSublimeSensation,
            StatTypeGGG.LocalAfflictionNotableSurgingVitality,
            StatTypeGGG.LocalAfflictionNotablePeaceAmidstChaos,
            StatTypeGGG.LocalAfflictionNotableAdrenaline,
            StatTypeGGG.LocalAfflictionNotableWallOfMuscle,
            StatTypeGGG.LocalAfflictionNotableMindfulness,
            StatTypeGGG.LocalAfflictionNotableLiquidInspiration,
            StatTypeGGG.LocalAfflictionNotableOpenness,
            StatTypeGGG.LocalAfflictionNotableDaringIdeas,
            StatTypeGGG.LocalAfflictionNotableClarityOfPurpose,
            StatTypeGGG.LocalAfflictionNotableScintillatingIdea,
            StatTypeGGG.LocalAfflictionNotableHolisticHealth,
            StatTypeGGG.LocalAfflictionNotableGenius,
            StatTypeGGG.LocalAfflictionNotableImprovisor,
            StatTypeGGG.LocalAfflictionNotableStubbornStudent,
            StatTypeGGG.LocalAfflictionNotableSavourTheMoment,
            StatTypeGGG.LocalAfflictionNotableEnergyFromNaught,
            StatTypeGGG.LocalAfflictionNotableWillShaper,
            StatTypeGGG.LocalAfflictionNotableSpringBack,
            StatTypeGGG.LocalAfflictionNotableConservationOfEnergy,
            StatTypeGGG.LocalAfflictionNotableHeartOfIron,
            StatTypeGGG.LocalAfflictionNotablePrismaticCarapace,
            StatTypeGGG.LocalAfflictionNotableMilitarism,
            StatTypeGGG.LocalAfflictionNotableSecondSkin,
            StatTypeGGG.LocalAfflictionNotableDragonHunter,
            StatTypeGGG.LocalAfflictionNotableEnduringComposure,
            StatTypeGGG.LocalAfflictionNotablePrismaticDance,
            StatTypeGGG.LocalAfflictionNotableNaturalVigour,
            StatTypeGGG.LocalAfflictionNotableUntouchable,
            StatTypeGGG.LocalAfflictionNotableShiftingShadow,
            StatTypeGGG.LocalAfflictionNotableReadiness,
            StatTypeGGG.LocalAfflictionNotableConfidentCombatant,
            StatTypeGGG.LocalAfflictionNotableFlexibleSentry,
            StatTypeGGG.LocalAfflictionNotableViciousGuard,
            StatTypeGGG.LocalAfflictionNotableMysticalWard,
            StatTypeGGG.LocalAfflictionNotableRoteReinforcement,
            StatTypeGGG.LocalAfflictionNotableMageHunter,
            StatTypeGGG.LocalAfflictionNotableRiotQueller,
            StatTypeGGG.LocalAfflictionNotableOneWithTheShield,
            StatTypeGGG.LocalAfflictionNotableAerialist,
            StatTypeGGG.LocalAfflictionNotableElegantForm,
            StatTypeGGG.LocalAfflictionNotableDartingMovements,
            StatTypeGGG.LocalAfflictionNotableNoWitnesses,
            StatTypeGGG.LocalAfflictionNotableMoltenOnesMark,
            StatTypeGGG.LocalAfflictionNotableFireAttunement,
            StatTypeGGG.LocalAfflictionNotablePureMight,
            StatTypeGGG.LocalAfflictionNotableBlacksmith,
            StatTypeGGG.LocalAfflictionNotableNonFlammable,
            StatTypeGGG.LocalAfflictionNotableWinterProwler,
            StatTypeGGG.LocalAfflictionNotableHibernator,
            StatTypeGGG.LocalAfflictionNotablePureGuile,
            StatTypeGGG.LocalAfflictionNotableAlchemist,
            StatTypeGGG.LocalAfflictionNotableAntifreeze,
            StatTypeGGG.LocalAfflictionNotableWizardry,
            StatTypeGGG.LocalAfflictionNotableCapacitor,
            StatTypeGGG.LocalAfflictionNotablePureAptitude,
            StatTypeGGG.LocalAfflictionNotableSage,
            StatTypeGGG.LocalAfflictionNotableInsulated,
            StatTypeGGG.LocalAfflictionNotableBornOfChaos,
            StatTypeGGG.LocalAfflictionNotableAntivenom,
            StatTypeGGG.LocalAfflictionNotableRotResistant,
            StatTypeGGG.LocalAfflictionNotableBlessed,
            StatTypeGGG.LocalAfflictionNotableStudentOfDecay,
        };


        public static string[] clusterMods = new[]
                {
                    "Axe Attacks deal 12% increased Damage with Hits and Ailments\nSword Attacks deal 12% increased Damage with Hits and Ailments",
                    "Staff Attacks deal 12% increased Damage with Hits and Ailments\nMace or Sceptre Attacks deal 12% increased Damage with Hits and Ailments",
                    "Claw Attacks deal 12% increased Damage with Hits and Ailments\nDagger Attacks deal 12% increased Damage with Hits and Ailments",
                    "12% increased Damage with Bows\n12% increased Damage Over Time with Bow Skills",
                    "Wand Attacks deal 12% increased Damage with Hits and Ailments",
                    "12% increased Damage with Two Handed Weapons",
                    "12% increased Attack Damage while Dual Wielding",
                    "12% increased Attack Damage while holding a Shield",
                    "10% increased Attack Damage",
                    "10% increased Spell Damage",
                    "10% increased Elemental Damage",
                    "12% increased Physical Damage",
                    "12% increased Fire Damage",
                    "12% increased Lightning Damage",
                    "12% increased Cold Damage",
                    "12% increased Chaos Damage",
                    "Minions deal 10% increased Damage",
                    "12% increased Burning Damage",
                    "12% increased Chaos Damage over Time",
                    "12% increased Physical Damage over Time",
                    "12% increased Cold Damage over Time",
                    "10% increased Damage over Time",
                    "10% increased Effect of Non-Damaging Ailments",
                    "3% increased effect of Non-Curse Auras from your Skills (Legacy)",
                    "3% increased Effect of your Curses (Legacy)",
                    "10% increased Damage while affected by a Herald",
                    "Minions deal 10% increased Damage while you are affected by a Herald",
                    "Exerted Attacks deal 20% increased Damage",
                    "15% increased Critical Strike Chance",
                    "Minions have 12% increased maximum Life",
                    "10% increased Area Damage",
                    "10% increased Projectile Damage",
                    "12% increased Trap Damage\n12% increased Mine Damage",
                    "12% increased Totem Damage",
                    "12% increased Brand Damage",
                    "Channelling Skills deal 12% increased Damage",
                    "6% increased Flask Effect Duration",
                    "10% increased Life Recovery from Flasks\n10% increased Mana Recovery from Flasks",
                    "4% increased maximum Life",
                    "6% increased maximum Energy Shield",
                    "6% increased maximum Mana",
                    "15% increased Armour",
                    "15% increased Evasion Rating",
                    "+1% Chance to Block Attack Damage",
                    "1% Chance to Block Spell Damage",
                    "+15% to Fire Resistance",
                    "+15% to Cold Resistance",
                    "+15% to Lightning Resistance",
                    "+12% to Chaos Resistance",
                    "+2% chance to Suppress Spell Damage",
                    "+10 to Strength",
                    "+10 to Dexterity",
                    "+10 to Intelligence",
                    "6% increased Mana Reservation Efficiency of Skills",
                    "3% increased Effect of your Curses"
                };

        public class PossibleCraft
        {
            public int Count { get; set; }
            public HarvestCraftType Type { get; set; }
            public HarvestItemCraftSubType ItemSubType { get; set; }
            public Vector2i ClickPos { get; set; }
            public Vector2i ClickToSavePos { get; set; }
            public int Needed { get; set; }
            public string ItemOrCurr { get; set; }

        }

        public enum HarvestCraftType
        {
            Reforge, 
            Curr,
            Keep,
            Color,
            Links,
            Sockets,
            Implicit,
            Resistance,
            ChangeOther,

        }

        public enum HarvestItemCraftSubType
        {
            Caster,
            Chaos,
            Cold,
            Critical,
            Defence,
            Fire,
            Life,
            Lightning,
            Physical,
            Speed,
            Attack,
            Influence,
            Prefixes,
            Suffixes,
            MoreLikely,
            LessLikely,
            TenTimes,
            Blue,
            Green,
            Red,
            White,
            RedBlueGreen,
            RedBlue,
            RedGreen,
            BlueGreen,
            One,
            Two,
            Three,
            Four,
            Five,
            Six,
            Normal,
            Abyss,
            Cluster,
            BreachSplinters,
            Catalysts,
            DelOrbs,
            Essences,
            Fossils,
            Oils,
            TimelessSplinters,
            DivCard,
            Gem,
            Offering,
            ColdToFire,
            ColdToLightning,
            FireToCold,
            FireToLightning,
            LightningToCold,
            LightningToFire
            
        }

    }
}