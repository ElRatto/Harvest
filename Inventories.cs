using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using Default.EXtensions.Positions;
using DreamPoeBot.Loki.Bot;
using DreamPoeBot.Common;
using DreamPoeBot.Loki.Game;
using DreamPoeBot.Loki.Game.GameData;
using DreamPoeBot.Loki.Game.Objects;
using Cursor = DreamPoeBot.Loki.Game.LokiPoe.InGameState.CursorItemOverlay;
using InventoryUi = DreamPoeBot.Loki.Game.LokiPoe.InGameState.InventoryUi;
using StashUi = DreamPoeBot.Loki.Game.LokiPoe.InGameState.StashUi;


namespace Default.EXtensions
{
    public static class Inventories
    {
        public static List<Item> InventoryItems => LokiPoe.InstanceInfo.GetPlayerInventoryItemsBySlot(InventorySlot.Main);
        public static List<Item> StashTabItems => StashUi.InventoryControl?.Inventory?.Items;
        public static int AvailableInventorySquares => LokiPoe.InstanceInfo.GetPlayerInventoryBySlot(InventorySlot.Main).AvailableInventorySquares;

        public static async Task<bool> OpenStash()
        {
            if (StashUi.IsOpened)
                return true;

            WalkablePosition stashPos;
            if (World.CurrentArea.IsTown)
            {
                stashPos = StaticPositions.GetStashPosByAct();
            }
            else
            {
                var stashObj = LokiPoe.ObjectManager.Stash;
                if (stashObj == null)
                {
                    GlobalLog.Error("[OpenStash] Fail to find any Stash nearby.");
                    return false;
                }
                stashPos = stashObj.WalkablePosition();
            }

            await PlayerAction.EnableAlwaysHighlight();

            await stashPos.ComeAtOnce();

            if (!await PlayerAction.Interact(LokiPoe.ObjectManager.Stash, () => StashUi.IsOpened && StashUi.StashTabInfo != null, "stash opening"))
                return false;

            await Wait.SleepSafe(200);
            return true;
        }

        public static async Task<bool> OpenSentinelStash()
        {
            if (DreamPoeBot.Loki.Game.LokiPoe.InGameState.SentinelLockerUi.IsOpened)
                return true;

            WalkablePosition stashPos;

            var stashObj = LokiPoe.ObjectManager.Objects.Where(s => s.Name == "Sentinel Locker").FirstOrDefault();
            if (stashObj == null)
            {
                GlobalLog.Error("[OpenStash] Fail to find any Sentinel Locker nearby.");
                return false;
            }
            stashPos = stashObj.WalkablePosition();


            await PlayerAction.EnableAlwaysHighlight();

            await stashPos.ComeAtOnce();

            if (!await PlayerAction.Interact(stashObj, () => LokiPoe.InGameState.SentinelLockerUi.IsOpened, "locker opening"))
                return false;

            await Wait.SleepSafe(200);
            return true;
        }

        public static async Task<bool> OpenSentinelTab(int tabtype)
        {
            if (!await OpenSentinelStash())
                return false;

            
            return true;
        }

        public static async Task<bool> OpenStashTab(string tabName, bool usekeyboard = false)
        {
            if (!await OpenStash())
                return false;

            if (StashUi.TabControl.CurrentTabName != tabName)
            {
                GlobalLog.Debug($"[OpenStashTab] Now switching to tab \"{tabName}\".");

                var id = StashUi.StashTabInfo.InventoryId;

                SwitchToTabResult err;

                if (usekeyboard)
                {
                    err = StashUi.TabControl.SwitchToTabKeyboard(tabName);
                }
                else
                {
                    err = StashUi.TabControl.SwitchToTabMouse(tabName);
                }
                
                if (err != SwitchToTabResult.None)
                {
                    GlobalLog.Error($"[OpenStashTab] Fail to switch to tab \"{tabName}\". Error \"{err}\".");
                    return false;
                }

                if (!await Wait.For(() => StashUi.StashTabInfo != null && StashUi.StashTabInfo.InventoryId != id, "stash tab switching"))
                    return false;

                await Wait.SleepSafe(200);
            }
            return true;
        }

        public static async Task<bool> OpenInventory()
        {
            if (InventoryUi.IsOpened && !LokiPoe.InGameState.PurchaseUi.IsOpened && !LokiPoe.InGameState.SellUi.IsOpened)
                return true;

            await Coroutines.CloseBlockingWindows();

            LokiPoe.Input.SimulateKeyEvent(LokiPoe.Input.Binding.open_inventory_panel, true, false, false);

            if (!await Wait.For(() => InventoryUi.IsOpened, "inventory panel opening"))
                return false;

            if (Settings.Instance.ArtificialDelays)
                await Wait.ArtificialDelay();

            return true;
        }

        public static async Task<bool> OpenLegacyUi()
        {
            GlobalLog.Error($"[OpenLegacyUi] LEGACY TAB - was soll das sein?");
            return false;

            /*
            if (!LokiPoe.InGameState.ChallengesUi.IsOpened)
            {
                if (!await OpenChallenges())
                    return false;
            }

            if (LokiPoe.InGameState.ChallengesUi.IsLegacyTabSelected)
                return true;

            var err = LokiPoe.InGameState.ChallengesUi.SwitchToLegacyTab();
            if (err != SwitchToTabResult.None)
            {
                GlobalLog.Error($"[OpenLegacyUi] Switch to legacy tab error: \"{err}\".");
                return false;
            }

            if (!await Wait.For(() => LokiPoe.InGameState.ChallengesUi.IsLegacyTabSelected, "switching to legacy tab"))
                return false;

            if (Settings.Instance.ArtificialDelays)
                await Wait.ArtificialDelay();

            return true;
            */
        }

        public static async Task<bool> OpenProphecyUi()
        {
            GlobalLog.Error($"[OpenProphecyUi] Geht nicht, code es dch selbst");
            return false;

            /*
            if (!LokiPoe.InGameState.ChallengesUi.IsOpened)
            {
                if (!await OpenChallenges())
                    return false;
            }

            if (LokiPoe.InGameState.ChallengesUi.IsPropheciesTabSelected)
                return true;

            var err = LokiPoe.InGameState.ChallengesUi.SwitchToPropheciesTab();
            if (err != SwitchToTabResult.None)
            {
                GlobalLog.Error($"[OpenProphecyUi] Switch to prophecy tab error: \"{err}\".");
                return false;
            }

            if (!await Wait.For(() => LokiPoe.InGameState.ChallengesUi.IsPropheciesTabSelected, "switching to prophecy tab"))
                return false;

            if (Settings.Instance.ArtificialDelays)
                await Wait.ArtificialDelay();

            return true;
            */
        }

        public static IEnumerable<Item> GetExcessCurrency(string name)
        {
            var currency = InventoryItems.FindAll(c => c.Name == name);

            if (currency.Count <= 1)
                return Enumerable.Empty<Item>();

            return currency.OrderByDescending(c => c.StackCount).Skip(1).ToList();
        }

        public static bool StashTabCanFitItem(Vector2i itemPos)
        {
            var item = InventoryUi.InventoryControl_Main.Inventory.FindItemByPos(itemPos);
            if (item == null)
            {
                GlobalLog.Error($"[StashCanFitItem] Fail to find item at {itemPos} in player's inventory.");
                return false;
            }

            var tabType = StashUi.StashTabInfo.TabType;
            if (tabType == InventoryTabType.Currency)
            {
                var name = item.Name;
                var stackCount = item.StackCount;
                var control = GetCurrencyControl(name);

                if (control != null && control.CanFit(name, stackCount))
                    return true;

                return StashUi.CurrencyTab.NonCurrency.Any(miscControl => miscControl.CanFit(name, stackCount));
            }
            if (tabType == InventoryTabType.Essence)
            {
                var name = item.Name;
                var stackCount = item.StackCount;
                var control = StashUi.EssenceTab.GetInventoryControlForMetadata(item.Metadata);

                if (control != null && control.CanFit(name, stackCount))
                    return true;

                return StashUi.EssenceTab.NonEssences.Any(miscControl => miscControl.CanFit(name, stackCount));
            }
            if (tabType == InventoryTabType.Divination)
            {
                var control = StashUi.DivinationTab.GetInventoryControlForMetadata(item.Metadata);
                return control != null && control.CanFit(item.Name, item.StackCount);
            }
            if (tabType == InventoryTabType.Fragment)
            {
                var control = StashUi.FragmentTab.GetInventoryControlForMetadata(item.Metadata);
                return control != null && control.CanFit(item.Name, item.StackCount);
            }
            if (tabType == InventoryTabType.Map)
            {
                GlobalLog.Error("[StashTabCanFitItem] Map tab is unsupported. Returning false.");
                return false;
            }
            return StashUi.InventoryControl.Inventory.CanFitItem(item.Size);
        }

        public static int GetCurrencyAmountInTrade(string currencyName, List<Item> itemlist)
        {
            int total = 0;

            foreach (var item in itemlist)
            {
                if (item.Name == currencyName)
                    total += item.StackCount;
            }
            //GlobalLog.Debug($"[MoneyMaker] Angebot des Anderen: {total} {currencyName} ");
            return total;
            
        }


        public static int GetCurrencyAmountInStashTab(string currencyName)
        {
            int total = 0;
            var tabType = StashUi.StashTabInfo.TabType;

            if (tabType == InventoryTabType.Currency)
            {
                var control = GetCurrencyControl(currencyName);
                if (control != null)
                {
                    var item = control.CustomTabItem;
                    if (item != null)
                        total += item.StackCount;
                }
                foreach (var miscControl in StashUi.CurrencyTab.NonCurrency)
                {
                    var item = miscControl.CustomTabItem;
                    if (item != null && item.Name == currencyName)
                        total += item.StackCount;
                }
                return total;
            }
            if (tabType == InventoryTabType.Fragment)
            {
                var control = GetFragmentControl(currencyName);
                if (control != null)
                {
                    var item = control.CustomTabItem;
                    if (item != null)
                        total += item.StackCount;
                }
                return total;
            }

            if (tabType == InventoryTabType.Essence)
            {
                foreach (var miscControl in StashUi.EssenceTab.NonEssences)
                {
                    var item = miscControl.CustomTabItem;
                    if (item != null && item.Name == currencyName)
                        total += item.StackCount;
                }
                return total;
            }

            if (tabType == InventoryTabType.Divination ||
                tabType == InventoryTabType.Map)
                return 0;

            foreach (var item in StashTabItems)
            {
                if (item.Name == currencyName)
                    total += item.StackCount;
            }
            return total;
        }


        public static async Task<bool> WithdrawAmountOfFragments(string fragmentName, int amount)
        {
            //nochmal zur Sicherheit
            GlobalLog.Debug($"[Fragments] soll {amount} {fragmentName} abheben");
            var amountinstash = Inventories.GetCurrencyAmountInStashTab(fragmentName);
            GlobalLog.Debug($"[Fragments] {amountinstash} davon im stash gefunden");
            if (amountinstash < amount) return false;

            var control = Inventories.GetFragmentControl(fragmentName);
            if (control != null)
            {
                var item = control.CustomTabItem;
                var stackcount = item.MaxStackCount;
                GlobalLog.Debug($"[Fragments] stackcount {stackcount}");
                var wieoftabheben = amount / stackcount;
                GlobalLog.Debug($"[Fragments] dann muss ich {wieoftabheben} mal abheben");

                for (var i = 0; i < wieoftabheben; i++)
                {
                    if (await Inventories.WithdrawFragments(fragmentName) == WithdrawResult.Error)
                    {
                        ErrorManager.ReportError();
                    }

                }

                var rest = amount - (wieoftabheben * stackcount);
                amountinstash = Inventories.GetCurrencyAmountInStashTab(fragmentName);

                GlobalLog.Debug($"[Fragments] rest ist {rest} habe noch {amountinstash}");

                if (rest != 0)
                {
                    if (rest == amountinstash)
                    {

                        if (await Inventories.WithdrawFragments(fragmentName) == WithdrawResult.Error)
                        {
                            ErrorManager.ReportError();
                        }
                    }
                    else
                    {

                        control.SplitStack(rest, true);

                        await Wait.SleepSafe(200);

                        var cursorItem = LokiPoe.InGameState.CursorItemOverlay.Item;

                        if (!LokiPoe.InGameState.InventoryUi.InventoryControl_Main.Inventory.CanFitItem(cursorItem.Size, out int col, out int row))
                        {
                            GlobalLog.Error("[WithdrawAmountOfCurrency] There is no space in main inventory.");
                            return false;
                        }

                        if (!await LokiPoe.InGameState.InventoryUi.InventoryControl_Main.PlaceItemFromCursor(new Vector2i(col, row)))
                            ErrorManager.ReportError();

                        await Wait.SleepSafe(200);
                    }
                }

                //await Coroutines.CloseBlockingWindows();

            }

            return true;
        }

        public static async Task<bool> WithdrawAmountOfCurrency(string currencyName, int amount)
        {
            //nochmal zur Sicherheit
            //GlobalLog.Error($"[MoneyMaker] soll {amount} {currencyName} abheben");
            var amountinstash = Inventories.GetCurrencyAmountInStashTab(currencyName);
            //GlobalLog.Error($"[MoneyMaker] {amountinstash} davon im stash gefunden");
            if (amountinstash < amount) return false;

            var control = Inventories.GetCurrencyControl(currencyName);
            if (control != null)
            {
                var item = control.CustomTabItem;
                var stackcount = item.MaxStackCount;
                //GlobalLog.Error($"[MoneyMaker] stackcount {stackcount}");
                var wieoftabheben = amount / stackcount;
                //GlobalLog.Error($"[MoneyMaker] dann muss ich {wieoftabheben} mal abheben");
                                
                for (var i = 0; i < wieoftabheben; i++)
                {
                    if (await Inventories.WithdrawCurrency(currencyName) == WithdrawResult.Error)
                    {
                        ErrorManager.ReportError();
                    }

                }
                
                var rest = amount - (wieoftabheben * stackcount);

                //GlobalLog.Error($"[MoneyMaker] rest ist {rest}");

                if (rest != 0)
                {

                    control.SplitStack(rest, true);

                    await Wait.SleepSafe(200);

                    var cursorItem = LokiPoe.InGameState.CursorItemOverlay.Item;

                    if (!LokiPoe.InGameState.InventoryUi.InventoryControl_Main.Inventory.CanFitItem(cursorItem.Size, out int col, out int row))
                    {
                        GlobalLog.Error("[WithdrawAmountOfCurrency] There is no space in main inventory.");
                        return false;
                    }

                    if (!await LokiPoe.InGameState.InventoryUi.InventoryControl_Main.PlaceItemFromCursor(new Vector2i(col, row)))
                        ErrorManager.ReportError();

                    await Wait.SleepSafe(200);
                }

                //await Coroutines.CloseBlockingWindows();

            }

            return true;
        }

        public static async Task<bool> PutCurrencyInHarvest(string name, int howmanyneeded)
        {
            foreach (var tab in Settings.Instance.GetTabsForCurrency(name))
            {
                GlobalLog.Debug($"[WithdrawCurrency] Looking for \"{name}\" in \"{tab}\" tab.");

                if (!await OpenStashTab(tab))
                    return false;

                var tabType = StashUi.StashTabInfo.TabType;

                if (tabType == InventoryTabType.Currency)
                {
                    var control = GetControlWithCurrency(name);
                    if (control == null)
                    {
                        GlobalLog.Debug($"[WithdrawCurrency] There are no \"{name}\" in \"{tab}\" tab.");
                        continue;
                    }

                    if (!await HarvestFastMoveFromPremiumStashTab(control, howmanyneeded))
                        return false;

                    GlobalLog.Debug($"[WithdrawCurrency] \"{name}\" have been successfully taken from \"{tab}\" tab.");
                    return true;
                }
                if (tabType == InventoryTabType.Divination ||
                    tabType == InventoryTabType.Map ||
                    tabType == InventoryTabType.Fragment)
                {
                    GlobalLog.Error($"[WithdrawCurrency] Unsupported behavior. Current stash tab is {tabType}.");
                    continue;
                }
                var item = StashTabItems.Where(i => i.Name == name).OrderByDescending(i => i.StackCount).FirstOrDefault();
                if (item == null)
                {
                    GlobalLog.Debug($"[WithdrawCurrency] There are no \"{name}\" in \"{tab}\" tab.");
                    continue;
                }

                if (!await HarvestFastMoveFromStashTab(item.LocationTopLeft, howmanyneeded))
                    return false;

                GlobalLog.Debug($"[WithdrawCurrency] \"{name}\" have been successfully taken from \"{tab}\" tab.");
                return true;
            }
            return false;
        }

        public static async Task<bool> DropSentinel(int type)
        {
            if (!await Inventories.OpenInventory())
            {
                ErrorManager.ReportError();
                return false;
            }

            InventoryControlWrapper Sent = new InventoryControlWrapper();

            if (type == 1)
            {
                Sent = LokiPoe.InGameState.InventoryUi.Sentinels.InventoryControl_StalkerSentinel;
            }
            else if (type == 2)
            {
                Sent = LokiPoe.InGameState.InventoryUi.Sentinels.InventoryControl_PandemoniumSentinel;
            }
            else if (type == 3)
            {
                Sent = LokiPoe.InGameState.InventoryUi.Sentinels.InventoryControl_ApexSentinel;
            }

            if (await Sent.PickItemToCursor())
            {
                GlobalLog.Error($"[Sentinel] Swapping Sentinels");
                var cursorItem = LokiPoe.InGameState.CursorItemOverlay.Item;
                if (cursorItem != null)
                {
                    int col, row;
                    if (!LokiPoe.InGameState.InventoryUi.InventoryControl_Main.Inventory.CanFitItem(cursorItem.Size, out col, out row))
                    {
                        return false;
                    }
                    var cursor = LokiPoe.InGameState.InventoryUi.InventoryControl_Main.PlaceCursorInto(col, row);
                    if (cursor == PlaceCursorIntoResult.None)
                    {
                        if (!await WaitForCursorToBeEmpty())
                        {
                            GlobalLog.Debug("[Sentinel] Unable to place item in inventory, now stopping the bot because it cannot continue.");
                            return false;
                        }
                    }
                    return true;
                }
                else
                {
                    GlobalLog.Debug("[Sentinel] No item on cursor, return");
                    return false;
                }
            }
            return false;
        }

        private static async Task<bool> WaitForCursorToBeEmpty(int timeout = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (LokiPoe.InstanceInfo.GetPlayerInventoryItemsBySlot(InventorySlot.Cursor).Any())
            {
                GlobalLog.Info("[TakeMapTask] Waiting for the cursor to be empty.");
                await Coroutines.LatencyWait();
                if (sw.ElapsedMilliseconds > timeout)
                {
                    GlobalLog.Info("[TakeMapTask] Timeout while waiting for the cursor to become empty.");
                    return false;
                }
            }
            return true;
        }

        public static async Task<WithdrawResult> SwapOutSentinels(int dronetype)
        {

            GlobalLog.Debug($"[SwapOutSentinels] Trying to replace used Sentinel Drone.");


            //stalker sentinel
            if (!await OpenSentinelTab(dronetype))
                return WithdrawResult.Error;

            if (!LokiPoe.InGameState.SentinelLockerUi.IsOpened)
                return WithdrawResult.Error;

            InventoryControlWrapper control = new InventoryControlWrapper();

            if (dronetype == 1)
            {
                control = LokiPoe.InGameState.SentinelLockerUi.StalkerSentinels;
            }
            else if (dronetype == 2)
            {
                control = LokiPoe.InGameState.SentinelLockerUi.PandemoniumSentinels;
            }
            else if (dronetype == 3)
            {
                control = LokiPoe.InGameState.SentinelLockerUi.ApexSentinels;
            }


            if (control == null)
            {
                GlobalLog.Debug($"[WithdrawCurrency] There are no sentinels");
                return WithdrawResult.Error;
            }


            if (!await FastMoveFromDroneTab(control))
                return WithdrawResult.Error;


            GlobalLog.Debug($"[WithdrawDrone] drone moved tab.");
            return WithdrawResult.Success;

        }

        public static async Task<WithdrawResult> WithdrawCurrency(string name)
        {
            foreach (var tab in Settings.Instance.GetTabsForCurrency(name))
            {
                GlobalLog.Debug($"[WithdrawCurrency] Looking for \"{name}\" in \"{tab}\" tab.");

                if (!await OpenStashTab(tab))
                    return WithdrawResult.Error;

                var tabType = StashUi.StashTabInfo.TabType;

                if (tabType == InventoryTabType.Currency)
                {
                    var control = GetControlWithCurrency(name);
                    if (control == null)
                    {
                        GlobalLog.Debug($"[WithdrawCurrency] There are no \"{name}\" in \"{tab}\" tab.");
                        continue;
                    }

                    if (!await FastMoveFromPremiumStashTab(control))
                        return WithdrawResult.Error;

                    GlobalLog.Debug($"[WithdrawCurrency] \"{name}\" have been successfully taken from \"{tab}\" tab.");
                    return WithdrawResult.Success;
                }
                if (tabType == InventoryTabType.Essence)
                {
                    var control = StashUi.EssenceTab.NonEssences.FirstOrDefault(c => c.CustomTabItem?.Name == name);
                    if (control == null)
                    {
                        GlobalLog.Debug($"[WithdrawCurrency] There are no \"{name}\" in \"{tab}\" tab.");
                        continue;
                    }

                    if (!await FastMoveFromPremiumStashTab(control))
                        return WithdrawResult.Error;

                    GlobalLog.Debug($"[WithdrawCurrency] \"{name}\" have been successfully taken from \"{tab}\" tab.");
                    return WithdrawResult.Success;
                }
                if (tabType == InventoryTabType.Divination ||
                    tabType == InventoryTabType.Map ||
                    tabType == InventoryTabType.Fragment)
                {
                    GlobalLog.Error($"[WithdrawCurrency] Unsupported behavior. Current stash tab is {tabType}.");
                    continue;
                }
                var item = StashTabItems.Where(i => i.Name == name).OrderByDescending(i => i.StackCount).FirstOrDefault();
                if (item == null)
                {
                    GlobalLog.Debug($"[WithdrawCurrency] There are no \"{name}\" in \"{tab}\" tab.");
                    continue;
                }

                if (!await FastMoveFromStashTab(item.LocationTopLeft))
                    return WithdrawResult.Error;

                GlobalLog.Debug($"[WithdrawCurrency] \"{name}\" have been successfully taken from \"{tab}\" tab.");
                return WithdrawResult.Success;
            }
            return WithdrawResult.Unavailable;
        }

        public static async Task<bool> ClickStuffIntoHarvest(string name, List<string> tabs)
        {
            
            foreach (var tab in tabs)
            {
                GlobalLog.Debug($"[ClickStuffIntoHarvest] Looking for \"{name}\" in \"{tab}\" tab.");

                if (!await OpenStashTab(tab, true))
                    return false;

                var tabType = StashUi.StashTabInfo.TabType;

                if (tabType == InventoryTabType.Fragment)
                {
                    var control = GetControlWithFragment(name);
                    if (control == null)
                    {
                        GlobalLog.Debug($"[ClickStuffIntoHarvest] There are no \"{name}\" in \"{tab}\" tab.");
                        continue;
                    }

                    if (!await HarvestFastMoveFromPremiumStashTab(control, 1)) //er clickt egal wie viele rein
                        return false;

                    GlobalLog.Debug($"[ClickStuffIntoHarvest] \"{name}\" have been successfully taken from \"{tab}\" tab.");
                    return true;
                }

                if (name == "cheapestcard")
                {
 
                    Item cheapestcard = null;
                    double cheapestprice = 999;
                    foreach(var it in StashTabItems)
                    {
                        if (it.Class != ItemClasses.DivinationCard)
                            continue;
                        var val = MoneyMaker.Helpers.Helper.GetChaosValue(it);
                        GlobalLog.Debug($"[ClickStuffIntoHarvest] \"card {it.FullName}\" value \"{val}\" tab.");

                        if (val < cheapestprice)
                        {
                            cheapestprice = val;
                            cheapestcard = it;
                        }
                    }

                    if (cheapestcard != null)
                    {
                        if (!await HarvestFastMoveFromStashTab(cheapestcard.LocationTopLeft, 1))
                            return false;

                        GlobalLog.Debug($"[ClickStuffIntoHarvest] \"{name}\" have been successfully taken from \"{tab}\" tab.");
                        return true;
                    }

                }
                else
                {
                    //normaler tab
                    var item = StashTabItems.Where(i => i.Name == name).OrderByDescending(i => i.StackCount).FirstOrDefault();
                    if (item == null)
                    {
                        GlobalLog.Debug($"[ClickStuffIntoHarvest] There are no \"{name}\" in \"{tab}\" tab.");
                        continue;
                    }

                    if (!await HarvestFastMoveFromStashTab(item.LocationTopLeft, 1))
                        return false;

                    GlobalLog.Debug($"[ClickStuffIntoHarvest] \"{name}\" have been successfully taken from \"{tab}\" tab.");
                    return true;
                }
            }
            return false;
        }

        public static async Task<WithdrawResult> WithdrawFragments(string name)
        {
            foreach (var tab in Settings.Instance.GetTabsForCategory("Fragments"))
            {
                GlobalLog.Debug($"[WithdrawFragments] Looking for \"{name}\" in \"{tab}\" tab.");

                if (!await OpenStashTab(tab))
                    return WithdrawResult.Error;

                var tabType = StashUi.StashTabInfo.TabType;

                if (tabType == InventoryTabType.Fragment)
                {
                    var control = GetControlWithFragment(name);
                    if (control == null)
                    {
                        GlobalLog.Debug($"[WithdrawFragments] There are no \"{name}\" in \"{tab}\" tab.");
                        continue;
                    }

                    if (!await FastMoveFromPremiumStashTab(control))
                        return WithdrawResult.Error;

                    GlobalLog.Debug($"[WithdrawCurrency] \"{name}\" have been successfully taken from \"{tab}\" tab.");
                    return WithdrawResult.Success;
                }
                

                //normaler tab
                var item = StashTabItems.Where(i => i.Name == name).OrderByDescending(i => i.StackCount).FirstOrDefault();
                if (item == null)
                {
                    GlobalLog.Debug($"[WithdrawFragments] There are no \"{name}\" in \"{tab}\" tab.");
                    continue;
                }

                if (!await FastMoveFromStashTab(item.LocationTopLeft))
                    return WithdrawResult.Error;

                GlobalLog.Debug($"[WithdrawCurrency] \"{name}\" have been successfully taken from \"{tab}\" tab.");
                return WithdrawResult.Success;
            }
            return WithdrawResult.Unavailable;
        }

        public static async Task<bool> FindTabWithCurrency(string name)
        {
            var tabs = new List<string>(Settings.Instance.GetTabsForCurrency(name)); //OoB -> curr

            // Moving currently opened stash tab to the front of the list, so bot does search in it first
            if (tabs.Count > 1 && StashUi.IsOpened)
            {
                var currentTab = StashUi.StashTabInfo.DisplayName;
                var index = tabs.IndexOf(currentTab);
                if (index > 0)
                {
                    var tab = tabs[index];
                    tabs.RemoveAt(index);
                    tabs.Insert(0, tab);
                }
            }

            foreach (var tab in tabs)
            {
                GlobalLog.Debug($"[FindTabWithCurrency] Looking for \"{name}\" in \"{tab}\" tab.");

                if (!await OpenStashTab(tab))
                {
                    ErrorManager.ReportError();
                    continue;
                }
                
                if (StashUi.StashTabInfo.IsPublicFlagged)
                {
                    GlobalLog.Error($"[FindTabWithCurrency] Stash tab \"{tab}\" is public. Cannot use currency from it.");
                    continue;
                }
                var amount = GetCurrencyAmountInStashTab(name);
                if (amount > 0)
                {
                    GlobalLog.Debug($"[FindTabWithCurrency] Found {amount} \"{name}\" in \"{tab}\" tab.");
                    return true;
                }
                GlobalLog.Debug($"[FindTabWithCurrency] There are no \"{name}\" in \"{tab}\" tab.");
            }
            return false;
        }

        public static InventoryControlWrapper GetControlWithCurrency(string currencyName)
        {
            var control = GetCurrencyControl(currencyName);

            if (control?.CustomTabItem != null)
                return control;

            return StashUi.CurrencyTab.NonCurrency.FirstOrDefault(c => c.CustomTabItem?.Name == currencyName);
        }

        public static InventoryControlWrapper GetControlWithFragment(string fragmentName)
        {
            var control = GetFragmentControl(fragmentName);

            if (control?.CustomTabItem != null)
                return control;

            return null;
            //return StashUi.CurrencyTab.NonCurrency.FirstOrDefault(c => c.CustomTabItem?.Name == fragmentName);
        }

        public static List<InventoryControlWrapper> GetControlsWithCurrency(string currencyName)
        {
            var controls = new List<InventoryControlWrapper>();

            var control = GetCurrencyControl(currencyName);

            if (control?.CustomTabItem != null)
                controls.Add(control);

            foreach (var miscControl in StashUi.CurrencyTab.NonCurrency)
            {
                if (miscControl.CustomTabItem?.Name == currencyName)
                    controls.Add(miscControl);
            }
            return controls;
        }



        #region Fast moving

        public static async Task<bool> FastMoveFromInventory(Vector2i itemPos)
        {
            var item = InventoryUi.InventoryControl_Main.Inventory.FindItemByPos(itemPos);
            if (item == null)
            {
                GlobalLog.Error($"[FastMoveFromInventory] Fail to find item at {itemPos} in player's inventory.");
                return false;
            }

            var itemName = item.FullName;
            GlobalLog.Debug($"[FastMoveFromInventory] Fast moving \"{itemName}\" at {itemPos} from player's inventory.");

            var err = InventoryUi.InventoryControl_Main.FastMove(item.LocalId);
            if (err != FastMoveResult.None)
            {
                GlobalLog.Error($"[FastMoveFromInventory] Fast move error: \"{err}\".");
                return false;
            }

            if (await Wait.For(() => InventoryUi.InventoryControl_Main.Inventory.FindItemByPos(itemPos) == null, "fast move", 30))  //step um 1/3 verkleinert
            {
                GlobalLog.Debug($"[FastMoveFromInventory] \"{itemName}\" at {itemPos} has been successfully fast moved from player's inventory.");

                if (Settings.Instance.ArtificialDelays)
                    await Wait.ArtificialDelay();

                return true;
            }
            GlobalLog.Error($"[FastMoveFromInventory] Fast move timeout for \"{itemName}\" at {itemPos} in player's inventory.");
            return false;
        }

        public static async Task<bool> FastMoveToVendor(Vector2i itemPos)
        {
            var item = InventoryUi.InventoryControl_Main.Inventory.FindItemByPos(itemPos);
            if (item == null)
            {
                GlobalLog.Error($"[FastMoveToVendor] Fail to find item at {itemPos} in player's inventory.");
                return false;
            }

            var itemName = item.FullName;
            GlobalLog.Debug($"[FastMoveToVendor] Fast moving \"{itemName}\" at {itemPos} from player's inventory.");

            var err = InventoryUi.InventoryControl_Main.FastMove(item.LocalId);
            if (err != FastMoveResult.None && err != FastMoveResult.ItemTransparent)
            {
                GlobalLog.Error($"[FastMoveToVendor] Fast move error: \"{err}\".");
                return false;
            }

            if (await Wait.For(() =>
            {
                var movedItem = InventoryUi.InventoryControl_Main.Inventory.FindItemByPos(itemPos);
                if (movedItem == null)
                {
                    GlobalLog.Error("[FastMoveToVendor] Unexpected error. Item became null instead of transparent.");
                    return false;
                }
                return InventoryUi.InventoryControl_Main.IsItemTransparent(movedItem.LocalId);
            }, "fast move", 30)) //step um 1/3 verkleinert
            {
                GlobalLog.Debug($"[FastMoveToVendor] \"{itemName}\" at {itemPos} has been successfully fast moved from player's inventory.");

                if (Settings.Instance.ArtificialDelays)
                    await Wait.ArtificialDelay();

                return true;
            }
            GlobalLog.Error($"[FastMoveToVendor] Fast move timeout for \"{itemName}\" at {itemPos} in player's inventory.");
            return false;
        }

        public static async Task<bool> HarvestFastMoveFromStashTab(Vector2i itemPos, int needed)
        {
            var tabName = StashUi.TabControl.CurrentTabName;
            var item = StashUi.InventoryControl.Inventory.FindItemByPos(itemPos);
            if (item == null)
            {
                GlobalLog.Error($"[FastMoveFromStashTab] Fail to find item at {itemPos} in \"{tabName}\" tab.");
                return false;
            }

            var itemName = item.FullName;
            var stackCount = item.StackCount;

            if (stackCount < needed)
            {
                GlobalLog.Error("[FastMoveFromPremiumStashTab] zu wenige auf lager");
                return false;
            }

            GlobalLog.Debug($"[FastMoveFromStashTab] Fast moving \"{itemName}\" at {itemPos} from \"{tabName}\" tab.");

            var err = StashUi.InventoryControl.FastMove(item.LocalId);

            if (await Wait.For(() =>
            {
                var i = CommonTasks.HarvestCraft.ItemInHarvestSlot();
                return i;
            }, "fast move to harvest"))
            {
                GlobalLog.Debug($"[FastMoveFromStashTab] \"{itemName}\" at {itemPos} has been successfully fast moved from \"{tabName}\" tab.");

                if (Settings.Instance.ArtificialDelays)
                    await Wait.ArtificialDelay();

                return true;
            }
            GlobalLog.Error($"[FastMoveFromStashTab] Fast move timeout for \"{itemName}\" at {itemPos} in \"{tabName}\" tab.");
            return false;
        }

        public static async Task<bool> FastMoveFromStashTab(Vector2i itemPos)
        {
            var tabName = StashUi.TabControl.CurrentTabName;
            var item = StashUi.InventoryControl.Inventory.FindItemByPos(itemPos);
            if (item == null)
            {
                GlobalLog.Error($"[FastMoveFromStashTab] Fail to find item at {itemPos} in \"{tabName}\" tab.");
                return false;
            }

            var itemName = item.FullName;
            var stackCount = item.StackCount;
            GlobalLog.Debug($"[FastMoveFromStashTab] Fast moving \"{itemName}\" at {itemPos} from \"{tabName}\" tab.");

            var err = StashUi.InventoryControl.FastMove(item.LocalId);
            if (err != FastMoveResult.None)
            {
                GlobalLog.Error($"[FastMoveFromStashTab] Fast move error: \"{err}\".");
                return false;
            }

            if (await Wait.For(() =>
            {
                var i = StashUi.InventoryControl.Inventory.FindItemByPos(itemPos);
                return i == null || i.StackCount < stackCount;
            }, "fast move"))
            {
                GlobalLog.Debug($"[FastMoveFromStashTab] \"{itemName}\" at {itemPos} has been successfully fast moved from \"{tabName}\" tab.");

                if (Settings.Instance.ArtificialDelays)
                    await Wait.ArtificialDelay();

                return true;
            }
            GlobalLog.Error($"[FastMoveFromStashTab] Fast move timeout for \"{itemName}\" at {itemPos} in \"{tabName}\" tab.");
            return false;
        }

        public static async Task<bool> FastMoveFromDroneTab(InventoryControlWrapper inv)
        {
            var drone = inv.Inventory.Items.Where(i => i.DroneCurrentCharges != 0).FirstOrDefault();
            if (drone == null)
            {
                GlobalLog.Error($"[FastMoveFromStashTab] Fail to find drone in tab");
                return false;
            }

            var itemPos = drone.LocationTopLeft;

            /*
            LokiPoe.InGameState.SentinelLockerUi.StalkerSentinels.Pickup(drone.LocalId);
            await Coroutines.FinishCurrentAction();
            await Coroutines.CloseBlockingWindows();
            if (!await Inventories.OpenInventory())
            {
                ErrorManager.ReportError();
                 return false;
            }
            LokiPoe.InGameState.InventoryUi.Sentinels.InventoryControl_StalkerSentinel.PlaceCursorInto(true, true);

            return true;
            */
            
            var err = inv.FastMove(drone.LocalId);
            if (err != FastMoveResult.None)
            {
                GlobalLog.Error($"[FastMoveFromStashTab] Fast move error: \"{err}\".");
                return false;
            }

            if (await Wait.For(() =>
            {
                var i = inv.Inventory.FindItemByPos(itemPos);
                return i == null;
            }, "fast move"))
            {
                GlobalLog.Debug($"[FastMoveFromDroneTab] drone has been successfully fast moved from tab.");

                if (Settings.Instance.ArtificialDelays)
                    await Wait.ArtificialDelay();

                //alte drohneverstauen
                var altedrohne = InventoryItems.Where(i => i.Metadata.Contains("Sentinel") && i.DroneCurrentCharges == 0).FirstOrDefault();

                await FastMoveFromInventory(altedrohne.LocationTopLeft);

                if (Settings.Instance.ArtificialDelays)
                    await Wait.ArtificialDelay();

                await Coroutines.CloseBlockingWindows();
                if (!await Inventories.OpenInventory())
                {
                    ErrorManager.ReportError();
                    return false;
                }

                var droneininv = InventoryItems.Where(i => i.Metadata.Contains("Sentinel") && i.DroneCurrentCharges != 0).FirstOrDefault();

                await FastMoveFromInventory(droneininv.LocationTopLeft);

                return true;
            }

            GlobalLog.Error($"[FastMoveFromStashTab] Fast move timeout for drone tab.");
            return false;
            
        }


        public static async Task<bool> FastMoveFromPremiumStashTab(InventoryControlWrapper control)
        {
            if (control == null)
            {
                GlobalLog.Error("[FastMoveFromPremiumStashTab] Inventory control is null.");
                return false;
            }
            var item = control.CustomTabItem;
            if (item == null)
            {
                GlobalLog.Error("[FastMoveFromPremiumStashTab] Inventory control has no item.");
                return false;
            }

            var itemName = item.Name;
            var stackCount = item.StackCount;
            var tabName = StashUi.TabControl.CurrentTabName;

            GlobalLog.Debug($"[FastMoveFromPremiumStashTab] Fast moving \"{itemName}\" from \"{tabName}\" tab.");

            var moved = control.FastMove();
            if (moved != FastMoveResult.None)
            {
                GlobalLog.Error($"[FastMoveFromPremiumStashTab] Fast move error: \"{moved}\".");
                return false;
            }
            if (await Wait.For(() =>
            {
                var i = control.CustomTabItem;
                return i == null || i.StackCount < stackCount;
            }, "fast move", 30)) //delay um 1/3 verkleinert
            {
                GlobalLog.Debug($"[FastMoveFromPremiumStashTab] \"{itemName}\" has been successfully fast moved from \"{tabName}\" tab.");

                if (Settings.Instance.ArtificialDelays)
                    await Wait.ArtificialDelay();

                return true;
            }
            GlobalLog.Error($"[FastMoveFromPremiumStashTab] Fast move timeout for \"{itemName}\" in \"{tabName}\" tab.");
            return false;
        }

        public static async Task<bool> HarvestFastMoveFromPremiumStashTab(InventoryControlWrapper control, int needed)
        {
            if (control == null)
            {
                GlobalLog.Error("[FastMoveFromPremiumStashTab] Inventory control is null.");
                return false;
            }
            var item = control.CustomTabItem;
            if (item == null)
            {
                GlobalLog.Error("[FastMoveFromPremiumStashTab] Inventory control has no item.");
                return false;
            }

            var itemName = item.Name;
            var stackCount = item.StackCount;
            var tabName = StashUi.TabControl.CurrentTabName;

            if (stackCount < needed)
            {
                GlobalLog.Error("[FastMoveFromPremiumStashTab] Zu wenige auf lager");
                return false;
            }

            GlobalLog.Debug($"[FastMoveFromPremiumStashTab] Fast moving \"{itemName}\" from \"{tabName}\" tab.");

            var moved = control.FastMove();

            if (await Wait.For(() =>
            {
                var i = CommonTasks.HarvestCraft.ItemInHarvestSlot();
                return i;
            }, "fast move to harvest", 30)) //delay um 1/3 verkleinert
            {
                GlobalLog.Debug($"[FastMoveFromPremiumStashTab] \"{itemName}\" has been successfully fast moved from \"{tabName}\" tab.");

                if (Settings.Instance.ArtificialDelays)
                    await Wait.ArtificialDelay();

                return true;
            }
            GlobalLog.Error($"[FastMoveFromPremiumStashTab] Fast move timeout for \"{itemName}\" in \"{tabName}\" tab.");
            return false;
        }

        #endregion

        #region Extension methods

        public static int ItemAmount(this Inventory inventory, string itemName)
        {
            int amount = 0;
            foreach (var item in inventory.Items)
            {
                if (item.Name == itemName)
                    amount += item.StackCount;
            }
            return amount;
        }

        public static async Task<bool> PickItemToCursor(this InventoryControlWrapper inventory, Vector2i itemPos, bool rightClick = false)
        {
            var item = inventory.Inventory.FindItemByPos(itemPos);
            if (item == null)
            {
                GlobalLog.Error($"[PickItemToCursor] Cannot find item at {itemPos}");
                return false;
            }

            GlobalLog.Debug($"[PickItemToCursor] Now going to pick \"{item.Name}\" at {itemPos} to cursor.");
            int id = item.LocalId;

            if (rightClick)
            {
                var err = inventory.UseItem(id);
                if (err != UseItemResult.None)
                {
                    GlobalLog.Error($"[PickItemToCursor] Fail to pick item to cursor. Error: \"{err}\".");
                    return false;
                }
            }
            else
            {
                var err = inventory.Pickup(id);
                if (err != PickupResult.None)
                {
                    GlobalLog.Error($"[PickItemToCursor] Fail to pick item to cursor. Error: \"{err}\".");
                    return false;
                }
            }
            return await Wait.For(() => Cursor.Item != null, "item appear under cursor");
        }

        public static async Task<bool> PickItemToCursor(this InventoryControlWrapper inventory, bool rightClick = false)
        {
            var item = inventory.CustomTabItem;
            if (item == null)
            {
                GlobalLog.Error("[PickItemToCursor] Custom inventory control is empty.");
                return false;
            }

            GlobalLog.Debug($"[PickItemToCursor] Now going to pick \"{item.Name}\" to cursor.");
            if (rightClick)
            {
                var err = inventory.UseItem();
                if (err != UseItemResult.None)
                {
                    GlobalLog.Error($"[PickItemToCursor] Fail to pick item to cursor. Error: \"{err}\".");
                    return false;
                }
            }
            else
            {
                var err = inventory.Pickup();
                if (err != PickupResult.None)
                {
                    GlobalLog.Error($"[PickItemToCursor] Fail to pick item to cursor. Error: \"{err}\".");
                    return false;
                }
            }
            return await Wait.For(() => Cursor.Item != null, "item appear under cursor");
        }

        public static async Task<bool> PlaceItemFromCursor(this InventoryControlWrapper inventory, Vector2i pos)
        {
            var cursorItem = Cursor.Item;
            if (cursorItem == null)
            {
                GlobalLog.Error("[PlaceItemFromCursor] Cursor item is null.");
                return false;
            }

            GlobalLog.Debug($"[PlaceItemFromCursor] Now going to place \"{cursorItem.Name}\" from cursor to {pos}.");

            //apply item on another item, if we are in VirtualUse mode
            if (Cursor.Mode == LokiPoe.InGameState.CursorItemModes.VirtualUse)
            {
                var destItem = inventory.Inventory.FindItemByPos(pos);
                if (destItem == null)
                {
                    GlobalLog.Error("[PlaceItemFromCursor] Destination item is null.");
                    return false;
                }
                int destItemId = destItem.LocalId;
                var applied = inventory.ApplyCursorTo(destItem.LocalId);
                if (applied != ApplyCursorResult.None)
                {
                    GlobalLog.Error($"[PlaceItemFromCursor] Fail to place item from cursor. Error: \"{applied}\".");
                    return false;
                }
                //wait for destination item change, it cannot become null, ID should change
                return await Wait.For(() =>
                {
                    var item = inventory.Inventory.FindItemByPos(pos);
                    return item != null && item.LocalId != destItemId;
                }, "destination item change");
            }

            //in other cases, place item to empty inventory slot or swap it with another item
            int cursorItemId = cursorItem.LocalId;
            var placed = inventory.PlaceCursorInto(pos.X, pos.Y, true);
            if (placed != PlaceCursorIntoResult.None)
            {
                GlobalLog.Error($"[PlaceItemFromCursor] Fail to place item from cursor. Error: \"{placed}\".");
                return false;
            }

            //wait for cursor item change, if we placed - it should become null, if we swapped - ID should change
            if (!await Wait.For(() =>
            {
                var item = Cursor.Item;
                return item == null || item.LocalId != cursorItemId;
            }, "cursor item change")) return false;

            if (Settings.Instance.ArtificialDelays)
                await Wait.ArtificialDelay();

            return true;
        }

        #endregion

        #region Helpers/private

        private static bool CanFit(this InventoryControlWrapper control, string itemName, int amount)
        {
            var item = control.CustomTabItem;

            if (item == null)
                return true;

            if (itemName == CurrencyNames.Prophecy)
                return false;

            return item.Name == itemName && item.StackCount + amount <= 5000;
        }

        private static InventoryControlWrapper GetCurrencyControl(string currencyName)
        {
            return CurrencyControlDict.TryGetValue(currencyName, out var getControl) ? getControl() : null;
        }

        private static InventoryControlWrapper GetFragmentControl(string fragmentName)
        {
            return FragmentControlDict.TryGetValue(fragmentName, out var getControl) ? getControl() : null;
        }

        private static async Task<bool> OpenChallenges()
        {
            await Coroutines.CloseBlockingWindows();
            LokiPoe.Input.SimulateKeyEvent(LokiPoe.Input.Binding.open_challenges_panel, true, false, false);

            if (!await Wait.For(() => LokiPoe.InGameState.ChallengesUi.IsOpened, "challenges panel opening"))
                return false;

            if (Settings.Instance.ArtificialDelays)
                await Wait.ArtificialDelay();

            return true;
        }

        public static string CopyItemText(Item item)
        {
            StashUi.InventoryControl.ViewItemsInInventory((x, y) => { return y.LocalId == item.LocalId; }, () => { return StashUi.IsOpened; });
            string text = null;
            Exception threadEx = null;
            Thread staThread = new Thread(
                delegate ()
                {
                    try
                    {
                        var old = System.Windows.Forms.Clipboard.GetText();
                        LokiPoe.ProcessHookManager.SetKeyState(System.Windows.Forms.Keys.ControlKey, -32768);
                        Thread.Sleep(5);
                        LokiPoe.Input.SimulateKeyEvent(System.Windows.Forms.Keys.C, true, false, false);
                        Thread.Sleep(5);
                        LokiPoe.ProcessHookManager.SetKeyState(System.Windows.Forms.Keys.ControlKey, 0);
                        text = System.Windows.Forms.Clipboard.GetText();
                        System.Windows.Forms.Clipboard.SetText(old);
                    }

                    catch (Exception ex)
                    {
                        threadEx = ex;
                    }
                });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();
            return text;
        }

        private static readonly Dictionary<string, Func<InventoryControlWrapper>> CurrencyControlDict = new Dictionary<string, Func<InventoryControlWrapper>>
        {
            [CurrencyNames.ScrollFragment] = () => StashUi.CurrencyTab.ScrollFragment,
            [CurrencyNames.TransmutationShard] = () => StashUi.CurrencyTab.TransmutationShard,
            [CurrencyNames.AlterationShard] = () => StashUi.CurrencyTab.AlterationShard,
            [CurrencyNames.AlchemyShard] = () => StashUi.CurrencyTab.AlchemyShard,
            [CurrencyNames.AnnulmentShard] = () => StashUi.CurrencyTab.AnnulmentShard,
            [CurrencyNames.ChaosShard] = () => StashUi.CurrencyTab.ChaosShard,
            [CurrencyNames.RegalShard] = () => StashUi.CurrencyTab.RegalShard,
            [CurrencyNames.ExaltedShard] = () => StashUi.CurrencyTab.ExaltedShard,
            [CurrencyNames.MirrorShard] = () => StashUi.CurrencyTab.MirrorShard,
            [CurrencyNames.Wisdom] = () => StashUi.CurrencyTab.ScrollOfWisdom,
            [CurrencyNames.Portal] = () => StashUi.CurrencyTab.PortalScroll,
            [CurrencyNames.Transmutation] = () => StashUi.CurrencyTab.OrbOfTransmutation,
            [CurrencyNames.Augmentation] = () => StashUi.CurrencyTab.OrbOfAugmentation,
            [CurrencyNames.Alteration] = () => StashUi.CurrencyTab.OrbOfAlteration,
            [CurrencyNames.Scrap] = () => StashUi.CurrencyTab.ArmourersScrap,
            [CurrencyNames.Whetstone] = () => StashUi.CurrencyTab.BlacksmithsWhetstone,
            [CurrencyNames.Glassblower] = () => StashUi.CurrencyTab.GlassblowersBauble,
            [CurrencyNames.Chisel] = () => StashUi.CurrencyTab.CartographersChisel,
            [CurrencyNames.Chromatic] = () => StashUi.CurrencyTab.ChromaticOrb,
            [CurrencyNames.Chance] = () => StashUi.CurrencyTab.OrbOfChance,
            [CurrencyNames.Alchemy] = () => StashUi.CurrencyTab.OrbOfAlchemy,
            [CurrencyNames.Jeweller] = () => StashUi.CurrencyTab.JewellersOrb,
            [CurrencyNames.Fusing] = () => StashUi.CurrencyTab.OrbOfFusing,
            [CurrencyNames.Scouring] = () => StashUi.CurrencyTab.OrbOfScouring,
            [CurrencyNames.Blessed] = () => StashUi.CurrencyTab.BlessedOrb,
            [CurrencyNames.Regal] = () => StashUi.CurrencyTab.RegalOrb,
            [CurrencyNames.Chaos] = () => StashUi.CurrencyTab.ChaosOrb,
            [CurrencyNames.Vaal] = () => StashUi.CurrencyTab.VaalOrb,
            [CurrencyNames.Regret] = () => StashUi.CurrencyTab.OrbOfRegret,
            [CurrencyNames.Gemcutter] = () => StashUi.CurrencyTab.GemcuttersPrism,
            [CurrencyNames.Divine] = () => StashUi.CurrencyTab.DivineOrb,
            [CurrencyNames.Exalted] = () => StashUi.CurrencyTab.ExaltedOrb,
            [CurrencyNames.Mirror] = () => StashUi.CurrencyTab.MirrorOfKalandra,
            [CurrencyNames.SilverCoin] = () => StashUi.CurrencyTab.SilverCoin,
            [CurrencyNames.Annulment] = () => StashUi.CurrencyTab.OrbOfAnnulment,
            [CurrencyNames.Binding] = () => StashUi.CurrencyTab.OrbofBinding,
        };

        private static readonly Dictionary<string, Func<InventoryControlWrapper>> FragmentControlDict = new Dictionary<string, Func<InventoryControlWrapper>>
        {
            ["Sacrifice at Midnight"] = () => StashUi.FragmentTab.General.SacrificeatMidnight,
            ["Sacrifice at Dusk"] = () => StashUi.FragmentTab.General.SacrificeatDusk,
            ["Sacrifice at Dawn"] = () => StashUi.FragmentTab.General.SacrificeatDawn,
            ["Sacrifice at Noon"] = () => StashUi.FragmentTab.General.SacrificeatNoon,
            ["Rusted Abyss Scarab"] = () => StashUi.FragmentTab.Scarab.RustedAbyssScarab,
            ["Polished Abyss Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedAbyssScarab,
            ["Gilded Abyss Scarab"] = () => StashUi.FragmentTab.Scarab.GildedAbyssScarab,
            ["Winged Abyss Scarab"] = () => StashUi.FragmentTab.Scarab.WingedAbyssScarab,
            ["Rusted Ambush Scarab"] = () => StashUi.FragmentTab.Scarab.RustedAmbushScarab,
            ["Polished Ambush Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedAmbushScarab,
            ["Gilded Ambush Scarab"] = () => StashUi.FragmentTab.Scarab.GildedAmbushScarab,
            ["Winged Ambush Scarab"] = () => StashUi.FragmentTab.Scarab.WingedAmbushScarab,
            ["Rusted Bestiary Scarab"] = () => StashUi.FragmentTab.Scarab.RustedBestiaryScarab,
            ["Polished Bestiary Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedBestiaryScarab,
            ["Gilded Bestiary Scarab"] = () => StashUi.FragmentTab.Scarab.GildedBestiaryScarab,
            ["Winged Bestiary Scarab"] = () => StashUi.FragmentTab.Scarab.WingedBestiaryScarab,
            ["Rusted Blight Scarab"] = () => StashUi.FragmentTab.Scarab.RustedBlightScarab,
            ["Polished Blight Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedBlightScarab,
            ["Gilded Blight Scarab"] = () => StashUi.FragmentTab.Scarab.GildedBlightScarab,
            ["Winged Blight Scarab"] = () => StashUi.FragmentTab.Scarab.WingedBlightScarab,
            ["Rusted Breach Scarab"] = () => StashUi.FragmentTab.Scarab.RustedBreachScarab,
            ["Polished Breach Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedBreachScarab,
            ["Gilded Breach Scarab"] = () => StashUi.FragmentTab.Scarab.GildedBreachScarab,
            ["Winged Breach Scarab"] = () => StashUi.FragmentTab.Scarab.WingedBreachScarab,
            ["Rusted Cartography Scarab"] = () => StashUi.FragmentTab.Scarab.RustedCartographyScarab,
            ["Polished Cartography Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedCartographyScarab,
            ["Gilded Cartography Scarab"] = () => StashUi.FragmentTab.Scarab.GildedCartographyScarab,
            ["Winged Cartography Scarab"] = () => StashUi.FragmentTab.Scarab.WingedCartographyScarab,
            ["Rusted Divination Scarab"] = () => StashUi.FragmentTab.Scarab.RustedDivinationScarab,
            ["Polished Divination Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedDivinationScarab,
            ["Gilded Divination Scarab"] = () => StashUi.FragmentTab.Scarab.GildedDivinationScarab,
            ["Winged Divination Scarab"] = () => StashUi.FragmentTab.Scarab.WingedDivinationScarab,
            ["Rusted Elder Scarab"] = () => StashUi.FragmentTab.Scarab.RustedElderScarab,
            ["Polished Elder Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedElderScarab,
            ["Gilded Elder Scarab"] = () => StashUi.FragmentTab.Scarab.GildedElderScarab,
            ["Winged Elder Scarab"] = () => StashUi.FragmentTab.Scarab.WingedElderScarab,
            ["Rusted Expedition Scarab"] = () => StashUi.FragmentTab.Scarab.RustedExpeditionScarab,
            ["Polished Expedition Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedExpeditionScarab,
            ["Gilded Expedition Scarab"] = () => StashUi.FragmentTab.Scarab.GildedExpeditionScarab,
            ["Winged Expedition Scarab"] = () => StashUi.FragmentTab.Scarab.WingedExpeditionScarab,
            ["Rusted Harbinger Scarab"] = () => StashUi.FragmentTab.Scarab.RustedHarbingerScarab,
            ["Polished Harbinger Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedHarbingerScarab,
            ["Gilded Harbinger Scarab"] = () => StashUi.FragmentTab.Scarab.GildedHarbingerScarab,
            ["Winged Harbinger Scarab"] = () => StashUi.FragmentTab.Scarab.WingedHarbingerScarab,
            ["Rusted Legion Scarab"] = () => StashUi.FragmentTab.Scarab.RustedLegionScarab,
            ["Polished Legion Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedLegionScarab,
            ["Gilded Legion Scarab"] = () => StashUi.FragmentTab.Scarab.GildedLegionScarab,
            ["Winged Legion Scarab"] = () => StashUi.FragmentTab.Scarab.WingedLegionScarab,
            ["Rusted Metamorph Scarab"] = () => StashUi.FragmentTab.Scarab.RustedMetamorphScarab,
            ["Polished Metamorph Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedMetamorphScarab,
            ["Gilded Metamorph Scarab"] = () => StashUi.FragmentTab.Scarab.GildedMetamorphScarab,
            ["Winged Metamorph Scarab"] = () => StashUi.FragmentTab.Scarab.WingedMetamorphScarab,
            ["Rusted Reliquary Scarab"] = () => StashUi.FragmentTab.Scarab.RustedReliquaryScarab,
            ["Polished Reliquary Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedReliquaryScarab,
            ["Gilded Reliquary Scarab"] = () => StashUi.FragmentTab.Scarab.GildedReliquaryScarab,
            ["Winged Reliquary Scarab"] = () => StashUi.FragmentTab.Scarab.WingedReliquaryScarab,
            ["Rusted Shaper Scarab"] = () => StashUi.FragmentTab.Scarab.RustedShaperScarab,
            ["Polished Shaper Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedShaperScarab,
            ["Gilded Shaper Scarab"] = () => StashUi.FragmentTab.Scarab.GildedShaperScarab,
            ["Winged Shaper Scarab"] = () => StashUi.FragmentTab.Scarab.WingedShaperScarab,
            ["Rusted Sulphite Scarab"] = () => StashUi.FragmentTab.Scarab.RustedSulphiteScarab,
            ["Polished Sulphite Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedSulphiteScarab,
            ["Gilded Sulphite Scarab"] = () => StashUi.FragmentTab.Scarab.GildedSulphiteScarab,
            ["Winged Sulphite Scarab"] = () => StashUi.FragmentTab.Scarab.WingedSulphiteScarab,
            ["Rusted Torment Scarab"] = () => StashUi.FragmentTab.Scarab.RustedTormentScarab,
            ["Polished Torment Scarab"] = () => StashUi.FragmentTab.Scarab.PolishedTormentScarab,
            ["Gilded Torment Scarab"] = () => StashUi.FragmentTab.Scarab.GildedTormentScarab,
            ["Winged Torment Scarab"] = () => StashUi.FragmentTab.Scarab.WingedTormentScarab,
            ["Offering to the Goddess"] = () => StashUi.FragmentTab.General.OfferingtotheGoddess,

        };

        #endregion
    }
}