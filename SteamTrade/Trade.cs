using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamTrade.Exceptions;
using SteamTrade.TradeWebAPI;

namespace SteamTrade
{
    public partial class Trade
    {
        #region Static Public data
        public static Schema CurrentSchema = null;
        #endregion

        private const int WEB_REQUEST_MAX_RETRIES = 3;
        private const int WEB_REQUEST_TIME_BETWEEN_RETRIES_MS = 600;

        // list to store all trade events already processed
        private readonly List<TradeEvent> eventList;

        // current bot's sid
        private readonly SteamID mySteamId;
        private readonly TradeSession session;
        private readonly Task<Inventory> myInventoryTask;
        private readonly Task<Inventory> otherInventoryTask;

        internal Trade(SteamID me, SteamID other, string sessionId, string token, Task<Inventory> myInventoryTask, Task<Inventory> otherInventoryTask)
        {
            TradeStarted = false;
            OtherIsReady = false;
            MeIsReady = false;
            mySteamId = me;
            OtherSID = other;

            session = new TradeSession(sessionId, token, other);

            this.eventList = new List<TradeEvent>();

            OtherOfferedItems = new List<UserAsset>();
            MyOfferedItems = new List<UserAsset>();
            MyOfferedItemsBySlot = new Dictionary<int, UserAsset>();

            this.otherInventoryTask = otherInventoryTask;
            this.myInventoryTask = myInventoryTask;
        }

        #region Public Properties

        /// <summary>Gets the other user's steam ID.</summary> 
        public SteamID OtherSID { get; private set; }

        /// <summary>
        /// Gets the bot's Steam ID.
        /// </summary>
        public SteamID MySteamId
        {
            get { return mySteamId; }
        }

        /// <summary> 
        /// Gets the inventory of the other user. 
        /// </summary>
        public Inventory OtherInventory
        {
            get
            {
                if (otherInventoryTask == null)
                    return null;

                otherInventoryTask.Wait();
                return otherInventoryTask.Result;
            }
        }

        /// <summary> 
        /// Gets the private inventory of the other user. 
        /// </summary>
        public ForeignInventory OtherPrivateInventory { get; private set; }

        /// <summary> 
        /// Gets the inventory of the bot.
        /// </summary>
        public Inventory MyInventory
        {
            get
            {
                if (myInventoryTask == null)
                    return null;

                myInventoryTask.Wait();
                return myInventoryTask.Result;
            }
        }

        /// <summary>
        /// Gets the items the user has offered.
        /// </summary>
        /// <value>
        /// The other offered items.
        /// </value>
        public List<UserAsset> OtherOfferedItems { get; private set; }

        /// <summary>
        /// Gets the items the bot has offered.
        /// </summary>
        /// <value>
        /// Bot offered items.
        /// </value>
        public List<UserAsset> MyOfferedItems { get; private set; }
        
        
        public Dictionary<int, UserAsset> MyOfferedItemsBySlot { get; private set; }

        /// <summary>
        /// Gets a value indicating if the other user is ready to trade.
        /// </summary>
        public bool OtherIsReady { get; private set; }

        /// <summary>
        /// Gets a value indicating if the bot is ready to trade.
        /// </summary>
        public bool MeIsReady { get; private set; }

        /// <summary>
        /// Gets a value indicating if a trade has started.
        /// </summary>
        public bool TradeStarted { get; private set; }

        /// <summary>
        /// Gets a value indicating if the remote trading partner cancelled the trade.
        /// </summary>
        public bool OtherUserCancelled { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the trade completed normally. This
        /// is independent of other flags.
        /// </summary>
        public bool HasTradeCompletedOk { get; private set; }

        /// <summary>
        /// Gets a value indicating if the remote trading partner accepted the trade.
        /// </summary>
        public bool OtherUserAccepted { get; private set; }

        #endregion

        #region Public Events

        public delegate void CloseHandler();

        public delegate void CompleteHandler();

        public delegate void ErrorHandler(string error);

        public delegate void TimeoutHandler();

        public delegate void SuccessfulInit();

        public delegate void UserAddItemHandler(Schema.Item schemaItem, Inventory.Item inventoryItem);

        public delegate void UserRemoveItemHandler(Schema.Item schemaItem, Inventory.Item inventoryItem);

        public delegate void MessageHandler(string msg);

        public delegate void UserSetReadyStateHandler(bool ready);

        public delegate void UserAcceptHandler();

        /// <summary>
        /// When the trade closes, this is called.  It doesn't matter
        /// whether or not it was a timeout or an error, this is called
        /// to close the trade.
        /// </summary>
        public event CloseHandler OnClose;

        /// <summary>
        /// Called when the trade completes successfully.
        /// </summary>
        public event CompleteHandler OnSuccess;

        /// <summary>
        /// This is for handling errors that may occur, like inventories
        /// not loading.
        /// </summary>
        public event ErrorHandler OnError;

        /// <summary>
        /// This occurs after Inventories have been loaded.
        /// </summary>
        public event SuccessfulInit OnAfterInit;

        /// <summary>
        /// This occurs when the other user adds an item to the trade.
        /// </summary>
        public event UserAddItemHandler OnUserAddItem;

        /// <summary>
        /// This occurs when the other user removes an item from the 
        /// trade.
        /// </summary>
        public event UserAddItemHandler OnUserRemoveItem;

        /// <summary>
        /// This occurs when the user sends a message to the bot over
        /// trade.
        /// </summary>
        public event MessageHandler OnMessage;

        /// <summary>
        /// This occurs when the user sets their ready state to either
        /// true or false.
        /// </summary>
        public event UserSetReadyStateHandler OnUserSetReady;

        /// <summary>
        /// This occurs when the user accepts the trade.
        /// </summary>
        public event UserAcceptHandler OnUserAccept;

        #endregion

        /// <summary>
        /// Cancel the trade.  This calls the OnClose handler, as well.
        /// </summary>
        public bool CancelTrade()
        {
            return RetryWebRequest(session.CancelTradeWebCmd);
        }

        /// <summary>
        /// Adds a specified TF2 item by its itemid.
        /// If the item is not a TF2 item, use the AddItem(ulong itemid, int appid, long contextid) overload
        /// </summary>
        /// <returns><c>false</c> if the tf2 item was not found in the inventory.</returns>
        public bool AddItem(ulong itemid)
        {
            Inventory.Item item = MyInventory.GetItem(itemid);

            if (item == null || item.IsNotTradeable)
            {
                return false;
            }
            else
            {
                return AddItem(item);
            }
        }
        public bool AddItem(ulong itemid, int appid, long contextid)
        {
            return AddItem(new UserAsset() { Id = itemid, AppId = appid, ContextId = contextid });
        }
        public bool AddItem(UserAsset item)
        {
            var slot = NextTradeSlot();
            bool success = RetryWebRequest(() => session.AddItemWebCmd(item, slot));

            if (success)
                MyOfferedItemsBySlot.Add(slot, item);

            return success;
        }

        /// <summary>
        /// Adds a single item by its Defindex.
        /// </summary>
        /// <returns>
        /// <c>true</c> if an item was found with the corresponding
        /// defindex, <c>false</c> otherwise.
        /// </returns>
        public bool AddItemByDefindex(int defindex)
        {
            List<Inventory.Item> items = MyInventory.GetItemsByDefindex(defindex);
            foreach (Inventory.Item item in items)
            {
                if (item != null && !MyOfferedItems.Contains(item) && !item.IsNotTradeable)
                {
                    return AddItem(item);
                }
            }
            return false;
        }

        /// <summary>
        /// Adds an entire set of items by Defindex to each successive
        /// slot in the trade.
        /// </summary>
        /// <param name="defindex">The defindex. (ex. 5022 = crates)</param>
        /// <param name="numToAdd">The upper limit on amount of items to add. <c>0</c> to add all items.</param>
        /// <returns>Number of items added.</returns>
        public uint AddAllItemsByDefindex(int defindex, uint numToAdd = 0)
        {
            List<Inventory.Item> items = MyInventory.GetItemsByDefindex(defindex);

            uint added = 0;

            foreach (Inventory.Item item in items)
            {
                if (!MyOfferedItems.Contains(item) && !item.IsNotTradeable)
                {
                    if (AddItem(item))
                        added++;

                    if (numToAdd > 0 && added >= numToAdd)
                        return added;
                }
            }

            return added;
        }


        /// <summary>
        /// Removes an item by its itemid.
        /// </summary>
        /// <returns><c>false</c> the item was not found in the trade.</returns>
        public bool RemoveItem(ulong itemid)//Default item
        {
            Inventory.Item item = MyInventory.GetItem(itemid);

            if (item == null || item.IsNotTradeable)
                return false;
            else
                return RemoveItem(item);
        }
        public bool RemoveItem(ulong itemid, int appid, long contextid)
        {
            return RemoveItem(new UserAsset() { Id = itemid, AppId = appid, ContextId = contextid });
        }
        public bool RemoveItem(UserAsset item)
        {
            int? slot = GetItemSlot(item);
            if (!slot.HasValue)
                return false;

            bool success = RetryWebRequest(() => session.RemoveItemWebCmd(item, slot.Value));

            if (success)
                MyOfferedItemsBySlot.Remove(slot.Value);

            return success;
        }

        /// <summary>
        /// Removes an item with the given Defindex from the trade.
        /// </summary>
        /// <returns>
        /// Returns <c>true</c> if it found a corresponding item; <c>false</c> otherwise.
        /// </returns>
        public bool RemoveItemByDefindex(int defindex)
        {
            foreach (UserAsset offeredItem in MyOfferedItems)
            {
                Inventory.Item item = MyInventory.GetItem(offeredItem.Id);
                if (item != null && item.Defindex == defindex)
                {
                    return RemoveItem(item);
                }
            }
            return false;
        }

        /// <summary>
        /// Removes an entire set of items by Defindex.
        /// </summary>
        /// <param name="defindex">The defindex. (ex. 5022 = crates)</param>
        /// <param name="numToRemove">The upper limit on amount of items to remove. <c>0</c> to remove all items.</param>
        /// <returns>Number of items removed.</returns>
        public uint RemoveAllItemsByDefindex(int defindex, uint numToRemove = 0)
        {
            List<Inventory.Item> items = MyInventory.GetItemsByDefindex(defindex);

            uint removed = 0;

            foreach (Inventory.Item item in items)
            {
                if (MyOfferedItems.Contains(item))
                {
                    bool success = RemoveItem(item);

                    if (success)
                        removed++;

                    if (numToRemove > 0 && removed >= numToRemove)
                        return removed;
                }
            }

            return removed;
        }

        /// <summary>
        /// Removes all offered items from the trade.
        /// </summary>
        /// <returns>Number of items removed.</returns>
        public uint RemoveAllItems()
        {
            uint numRemoved = 0;
            List<UserAsset> offeredItems = new List<UserAsset>(MyOfferedItems);

            foreach (UserAsset item in offeredItems)
            {
                if (RemoveItem(item))
                {
                    Console.WriteLine("Item removed");
                    numRemoved++;
                }
                else
                {
                    Console.WriteLine("Couldn't remove item " + item.ToString());
                }
            }

            return numRemoved;
        }

        /// <summary>
        /// Sends a message to the user over the trade chat.
        /// </summary>
        public bool SendMessage(string msg)
        {
            return RetryWebRequest(() => session.SendMessageWebCmd(msg));
        }

        /// <summary>
        /// Sets the bot to a ready status.
        /// </summary>
        public bool SetReady(bool ready)
        {
            //If the bot calls SetReady(false) and the call fails, we still want meIsReady to be
            //set to false.  Otherwise, if the call to SetReady() was a result of a callback
            //from Trade.Poll() inside of the OnTradeAccept() handler, the OnTradeAccept()
            //handler might think the bot is ready, when really it's not!
            if (!ready)
                MeIsReady = false;

            // testing
            ValidateLocalTradeItems();

            return RetryWebRequest(() => session.SetReadyWebCmd(ready));
        }

        /// <summary>
        /// Accepts the trade from the user.  Returns a deserialized
        /// JSON object.
        /// </summary>
        public bool AcceptTrade()
        {
            ValidateLocalTradeItems();

            return RetryWebRequest(session.AcceptTradeWebCmd);
        }

        /// <summary>
        /// Calls the given function multiple times, until we get a non-null/non-false/non-zero result, or we've made at least
        /// WEB_REQUEST_MAX_RETRIES attempts (with WEB_REQUEST_TIME_BETWEEN_RETRIES_MS between attempts)
        /// </summary>
        /// <returns>The result of the function if it succeeded, or default(T) (null/false/0) otherwise</returns>
        private T RetryWebRequest<T>(Func<T> webEvent)
        {
            for (int i = 0; i < WEB_REQUEST_MAX_RETRIES; i++)
            {
                //Don't make any more requests if the trade has ended!
                if (HasTradeCompletedOk || OtherUserCancelled)
                    return default(T);

                try
                {
                    T result = webEvent();

                    // if the web request returned some error.
                    if (!EqualityComparer<T>.Default.Equals(result, default(T)))
                        return result;
                }
                catch (Exception ex)
                {
                    // TODO: log to SteamBot.Log but... see issue #394
                    // realistically we should not throw anymore
                    Console.WriteLine(ex);
                }

                if (i != WEB_REQUEST_MAX_RETRIES)
                {
                    //This will cause the bot to stop responding while we wait between web requests.  ...Is this really what we want?
                    Thread.Sleep(WEB_REQUEST_TIME_BETWEEN_RETRIES_MS);
                }
            }

            return default(T);
        }

        /// <summary>
        /// This updates the trade.  This is called at an interval of a
        /// default of 800ms, not including the execution time of the
        /// method itself.
        /// </summary>
        /// <returns><c>true</c> if the other trade partner performed an action; otherwise <c>false</c>.</returns>
        public bool Poll()
        {
            bool otherDidSomething = false;

            if (!TradeStarted)
            {
                TradeStarted = true;

                // since there is no feedback to let us know that the trade
                // is fully initialized we assume that it is when we start polling.
                if (OnAfterInit != null)
                    OnAfterInit();
            }

            TradeStatus status = RetryWebRequest(session.GetStatus);

            if (status == null)
                return false;

            switch (status.trade_status)
            {
                // Nothing happened. i.e. trade hasn't closed yet.
                case 0:
                    break;

                // Successful trade
                case 1:
                    HasTradeCompletedOk = true;
                    return false;

                //Trade Closed
                case 3:
                case 4:
                    OtherUserCancelled = true;
                    return false;

                // All other known values.
                default:
                    FireOnErrorEvent("Trade was closed by other user. Trade status: " + status.trade_status);
                    OtherUserCancelled = true;
                    return false;
            }

            if (status.newversion)
            {
                // handle item adding and removing
                session.Version = status.version;

                HandleTradeVersionChange(status);
                return true;
            }
            else if (status.version > session.Version)
            {
                // oh crap! we missed a version update abort so we don't get 
                // scammed. if we could get what steam thinks what's in the 
                // trade then this wouldn't be an issue. but we can only get 
                // that when we see newversion == true
                throw new TradeException("The trade version does not match. Aborting.");
            }

            // Update Local Variables
            if (status.them != null)
            {
                OtherIsReady = status.them.ready == 1;
                MeIsReady = status.me.ready == 1;
                OtherUserAccepted = status.them.confirmed == 1;
            }

            var events = status.GetAllEvents();
            foreach (var tradeEvent in events)
            {
                if (eventList.Contains(tradeEvent))
                    continue;

                //add event to processed list, as we are taking care of this event now
                eventList.Add(tradeEvent);

                bool isBot = tradeEvent.steamid == MySteamId.ConvertToUInt64().ToString();

                // dont process if this is something the bot did
                if (isBot)
                    continue;

                otherDidSomething = true;

                switch ((TradeEventType)tradeEvent.action)
                {
                    case TradeEventType.ItemAdded:
                        FireOnUserAddItem(tradeEvent);
                        break;
                    case TradeEventType.ItemRemoved:
                        FireOnUserRemoveItem(tradeEvent);
                        break;
                    case TradeEventType.UserSetReady:
                        OnUserSetReady(true);
                        break;
                    case TradeEventType.UserSetUnReady:
                        OnUserSetReady(false);
                        break;
                    case TradeEventType.UserAccept:
                        OnUserAccept();
                        break;
                    case TradeEventType.UserChat:
                        OnMessage(tradeEvent.text);
                        break;
                    default:
                        // Todo: add an OnWarning or similar event
                        FireOnErrorEvent("Unknown Event ID: " + tradeEvent.action);
                        break;
                }
            }

            if (status.logpos != 0)
            {
                session.LogPos = status.logpos;
            }

            return otherDidSomething;
        }

        private void HandleTradeVersionChange(TradeStatus status)
        {
            OtherOfferedItems = new List<UserAsset>(status.them.GetAssets());
            MyOfferedItems = new List<UserAsset>(status.me.GetAssets());
        }

        /// <summary>
        /// Gets an item from a TradeEvent, and passes it into the UserHandler's implemented OnUserAddItem([...]) routine.
        /// Passes in null items if something went wrong.
        /// </summary>
        /// <param name="tradeEvent">TradeEvent to get item from</param>
        /// <returns></returns>
        private void FireOnUserAddItem(TradeEvent tradeEvent)
        {
            if (OtherInventory != null)
            {
                Inventory.Item item = OtherInventory.GetItem(tradeEvent.Id);
                Schema.Item schemaItem;

                if (item != null)
                {
                    schemaItem = CurrentSchema.GetItem(item.Defindex);
                    if (schemaItem == null)
                    {
                        Console.WriteLine("User added an unknown item to the trade.");
                    }

                    OnUserAddItem(schemaItem, item);
                }
                else
                {
                    item = new Inventory.Item
                    {
                        Id = tradeEvent.Id,
                        AppId = tradeEvent.AppId,
                        ContextId = tradeEvent.ContextId
                    };
                    //Console.WriteLine("User added a non TF2 item to the trade.");
                    OnUserAddItem(null, item);
                }
            }
            else
            {
                var schemaItem = GetItemFromPrivateBp(tradeEvent);
                if (schemaItem == null)
                {
                    Console.WriteLine("User added an unknown item to the trade.");
                }

                OnUserAddItem(schemaItem, null);
                // todo: figure out what to send in with Inventory item.....
            }
        }

        private Schema.Item GetItemFromPrivateBp(TradeEvent tradeEvent)
        {
            if (OtherPrivateInventory == null)
            {
                // get the foreign inventory
                var f = session.GetForiegnInventory(OtherSID, tradeEvent.ContextId, tradeEvent.AppId);
                OtherPrivateInventory = new ForeignInventory(f);
            }

            ushort defindex = OtherPrivateInventory.GetDefIndex(tradeEvent.Id);

            Schema.Item schemaItem = CurrentSchema.GetItem(defindex);
            return schemaItem;
        }

        /// <summary>
        /// Gets an item from a TradeEvent, and passes it into the UserHandler's implemented OnUserRemoveItem([...]) routine.
        /// Passes in null items if something went wrong.
        /// </summary>
        /// <param name="tradeEvent">TradeEvent to get item from</param>
        /// <returns></returns>
        private void FireOnUserRemoveItem(TradeEvent tradeEvent)
        {
            if (OtherInventory != null)
            {
                Inventory.Item item = OtherInventory.GetItem(tradeEvent.Id);
                if (item != null)
                {
                    Schema.Item schemaItem = CurrentSchema.GetItem(item.Defindex);
                    if (schemaItem == null)
                    {
                        // TODO: Add log (counldn't find item in CurrentSchema)
                    }

                    OnUserRemoveItem(schemaItem, item);
                }
                else
                {
                    // TODO: Log this (Couldn't find item in user's inventory can't find item in CurrentSchema
                    item = new Inventory.Item()
                    {
                        Id = tradeEvent.Id,
                        AppId = tradeEvent.AppId,
                        ContextId = tradeEvent.ContextId
                    };
                    OnUserRemoveItem(null, item);
                }
            }
            else
            {
                var schemaItem = GetItemFromPrivateBp(tradeEvent);
                if (schemaItem == null)
                {
                    // TODO: Add log (counldn't find item in CurrentSchema)
                }

                OnUserRemoveItem(schemaItem, null);
            }
        }

        internal void FireOnSuccessEvent()
        {
            var onSuccessEvent = OnSuccess;

            if (onSuccessEvent != null)
                onSuccessEvent();
        }

        internal void FireOnCloseEvent()
        {
            var onCloseEvent = OnClose;

            if (onCloseEvent != null)
                onCloseEvent();
        }

        internal void FireOnErrorEvent(string errorMessage)
        {
            var onErrorEvent = OnError;

            if (onErrorEvent != null)
                onErrorEvent(errorMessage);
        }

        private int NextTradeSlot()
        {
            int slot = 0;
            while (MyOfferedItemsBySlot.ContainsKey(slot))
            {
                slot++;
            }
            return slot;
        }

        private int? GetItemSlot(UserAsset item)
        {
            foreach (int slot in MyOfferedItemsBySlot.Keys)
            {
                if (MyOfferedItemsBySlot[slot].Equals(item))
                    return slot;
            }
            return null;
        }

        private void ValidateLocalTradeItems()
        {
            if (MyOfferedItemsBySlot.Values.Count != MyOfferedItems.Count)
            {
                throw new TradeException("Error validating local copy of items in the trade: Count mismatch");
            }

            if (MyOfferedItemsBySlot.Values.Any(asset => !MyOfferedItems.Contains(asset)))
            {
                throw new TradeException("Error validating local copy of items in the trade: Item was not in the Steam Copy.");
            }

        }

        public Inventory.Item GetFirstItem(int defindex)
        {
            List<Inventory.Item> items = MyInventory.GetItemsByDefindex(defindex);
            foreach (Inventory.Item item in items)
            {
                if (!(MyOfferedItems.Contains(item) || item.IsNotTradeable))
                {
                    return item;
                }
            }
            return null;
        }

        public Inventory.Item GetFirstOfferedItem(int defindex)
        {
            List<Inventory.Item> items = MyInventory.GetItemsByDefindex(defindex);
            foreach (Inventory.Item item in items)
            {
                if (MyOfferedItems.Contains(item))
                {
                    return item;
                }
            }
            return null;
        }

    }
}
