using SteamKit2;
using System.Collections.Generic;
using SteamTrade;
using SteamTrade.TradeWebAPI;

using System.Web;
using System;

namespace SteamBot
{
    public class TradeOfferDemoHandler : UserHandler
    {
        private GenericInventory mySteamInventory = new GenericInventory();
        public TradeOfferDemoHandler (Bot bot, SteamID sid) : base(bot, sid) {}

        public override bool OnFriendAdd () 
        {
            return true;
        }

        public override void OnLoginCompleted()
        {
            List<int> contextIds = new List<int>();
            SteamID partner = new SteamID();//<---MUST'T BE EMPTY

            TradeOffer myOffer = new TradeOffer(Bot.sessionId, Bot.token);

            myOffer.Start(partner);
            System.IO.File.WriteAllText("response_start.htm", myOffer.response);

            contextIds.Add(6);
            mySteamInventory.load(753, contextIds, Bot.SteamClient.SteamID);

            foreach (var item in mySteamInventory.items)
            {
                myOffer.AddItem(new TradeUserAssets(){
                    amount=1,
                    appid = item.Value.appid,
                    contextid = item.Value.contextid,
                    assetid = item.Value.assetid,
                });

                System.IO.File.WriteAllText("ajax_"+item.Value.assetid+".txt", myOffer.response);
                Console.WriteLine("RESPONSE:"+myOffer.response);
            }

            myOffer.MakeOffer("Test");
            System.IO.File.WriteAllText("response_send.htm", myOffer.response);
            //Console.WriteLine("RESPONSE: "+myOffer.response);
        }

        public override void OnChatRoomMessage(SteamID chatID, SteamID sender, string message)
        {
            Log.Info(Bot.SteamFriends.GetFriendPersonaName(sender) + ": " + message);
            base.OnChatRoomMessage(chatID, sender, message);
        }

        public override void OnFriendRemove () {}
        
        public override void OnMessage (string message, EChatEntryType type) {}

        public override bool OnTradeRequest() 
        {
            return true;
        }
        
        public override void OnTradeError (string error) 
        {
            Bot.SteamFriends.SendChatMessage (OtherSID, 
                                              EChatEntryType.ChatMsg,
                                              "Oh, there was an error: " + error + "."
                                              );
            Bot.log.Warn (error);
        }
        
        public override void OnTradeTimeout () 
        {
            Bot.SteamFriends.SendChatMessage (OtherSID, EChatEntryType.ChatMsg,
                                              "Sorry, but you were AFK and the trade was canceled.");
            Bot.log.Info ("User was kicked because he was AFK.");
        }
        
        public override void OnTradeInit() {}
        
        public override void OnTradeAddItem (Schema.Item schemaItem, Inventory.Item inventoryItem) {}
        
        public override void OnTradeRemoveItem (Schema.Item schemaItem, Inventory.Item inventoryItem) {}
        
        public override void OnTradeMessage (string message) {}
        
        public override void OnTradeReady (bool ready) 
        {
            //Because SetReady must use its own version, it's important
            //we poll the trade to make sure everything is up-to-date.
            Trade.Poll();
            if (!ready)
            {
                Trade.SetReady (false);
            }
            else
            {
                if(IsAdmin)
                {
                    Trade.SetReady (true);
                }
            }
        }
        
        public override void OnTradeAccept() 
        {
            if (IsAdmin)
            {
                //Even if it is successful, AcceptTrade can fail on
                //trades with a lot of items so we use a try-catch
                try {
                    Trade.AcceptTrade();
                }
                catch {
                    Log.Warn ("The trade might have failed, but we can't be sure.");
                }

                Log.Success ("Trade Complete!");
            }

            OnTradeClose ();
        }
        
    }
 
}

