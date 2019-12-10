using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Item Skin Randomizer", "Orange", "1.4.3")]
    [Description("Simple plugin that will select a random skin for an item when crafting")]
    public class ItemSkinRandomizer : RustPlugin
    {
        #region Vars

        private const string permUse = "itemskinrandomizer.use";
        private const string permReSkin = "itemskinrandomizer.reskin";
        private Dictionary<string, List<int>> cache = new Dictionary<string, List<int>>();

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            permission.RegisterPermission(permUse, this);
            permission.RegisterPermission(permReSkin, this);
            cmd.AddChatCommand(config.commandReskin, this, nameof(cmdRandomizeChat));
            cmd.AddConsoleCommand(config.commandReskin, this, nameof(cmdRandomizeConsole));
            timer.Every(Core.Random.Range(500, 700), () => { cache.Clear(); });
        }

        private void OnItemCraftFinished(ItemCraftTask task, Item item)
        {
            if (task.skinID != 0)
            {
                return;
            }

            if (permission.UserHasPermission(task.owner.UserIDString, permUse) == false)
            {
                return;
            }

            SetRandomSkin(null, item);
        }

        #endregion

        #region Commands

        private void cmdRandomizeConsole(ConsoleSystem.Arg arg)
        {
            cmdRandomizeChat(arg.Player());
        }

        private void cmdRandomizeChat(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, permReSkin) == false)
            {
                Message(player, "Permission");
                return;
            }

            var item = player.GetActiveItem();
            if (item != null)
            {
                SetRandomSkin(player, item);
                return;
            }

            var entity = GetLookEntity(player);
            if (entity != null)
            {
                SetRandomSkin(player, entity);
                return;
            }
            
            Message(player, "No Object");
        }

        #endregion

        #region Core

        private void SetRandomSkin(BasePlayer player, Item item)
        {
            var skin = GetRandomSkin(item.info);
            if (skin == 0)
            {
                return;
            }

            item.skin = skin;
            item.MarkDirty();

            var held = item.GetHeldEntity();
            if (held != null)
            {
                held.skinID = skin;
                held.SendNetworkUpdate();
            }
            
            Message(player, "Changed To", skin);
        }

        private void SetRandomSkin(BasePlayer player, BaseEntity entity)
        {
            var shortname = entity.ShortPrefabName;

            switch (shortname)
            {
                case "sleepingbag_leather_deployed":
                    shortname = "sleepingbag";
                    break;

                case "vendingmachine.deployed":
                    shortname = "vending.machine";
                    break;

                case "woodbox_deployed":
                    shortname = "box.wooden";
                    break;
                
                case "reactivetarget_deployed":
                    shortname = "target.reactive";
                    break;
            }

            var def = ItemManager.FindItemDefinition(shortname);
            if (def != null)
            {
                var skin = GetRandomSkin(def);
                entity.skinID = skin;
                entity.SendNetworkUpdate();
                Message(player, "Changed To", skin);
            }
        }

        private ulong GetRandomSkin(ItemDefinition def)
        {
            var skins = new List<int>();

            if (cache.TryGetValue(def.shortname, out skins) == false)
            {
                skins = new List<int>();
                if (def.skins != null) skins.AddRange(def.skins.Select(x => x.id));
                if (def.skins2 != null) skins.AddRange(def.skins2.Select(x => x.Id));
                cache.Add(def.shortname, skins);
            }

            if (skins.Count == 0)
            {
                return 0;
            }

            var ticks = 0;
            var skin = skins.GetRandom();
            var skinUL = ItemDefinition.FindSkin(def.itemid, skin);

            while (ticks < 20 && config.blocked.Contains(skinUL))
            {
                skin = skins.GetRandom();
                skinUL = ItemDefinition.FindSkin(def.itemid, skin);
                ticks++;
            }
            
            return ticks > 20 ? 0 : skinUL;
        }
        
        private BaseEntity GetLookEntity(BasePlayer player)
        {
            RaycastHit rhit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out rhit)) {return null;}
            return rhit.GetEntity();
        }

        #endregion

        #region Configuration 1.1.0

        private static ConfigData config;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Command to reskin")]
            public string commandReskin;

            [JsonProperty(PropertyName = "Blocked")]
            public List<ulong> blocked = new List<ulong>();
        }

        private ConfigData GetDefaultConfig()
        {
            return new ConfigData
            {
                commandReskin = "reskin",
                blocked = new List<ulong>
                {
                    12345,
                    67890
                }
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<ConfigData>();

                if (config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                PrintError("Configuration file is corrupt! Unloading plugin...");
                Interface.Oxide.RootPluginManager.RemovePlugin(this);
                return;
            }

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        #endregion

        #region Localization 1.1.1

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"Permission", "You don't have permission to use that!"},
                {"No Object", "You need to hold item or look on object!"},
                {"Changed To", "Skin was changed to {0}"}
            }, this);
        }

        private void Message(BasePlayer player, string messageKey, params object[] args)
        {
            if (player == null)
            {
                return;
            }

            var message = GetMessage(messageKey, player.UserIDString, args);
            player.SendConsoleCommand("chat.add", (object) 0, (object) message);
        }

        private string GetMessage(string messageKey, string playerID, params object[] args)
        {
            return string.Format(lang.GetMessage(messageKey, this, playerID), args);
        }

        #endregion
    }
}