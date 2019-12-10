using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Godmode", "Wulf/lukespragg/Arainrr", "4.2.3", ResourceId = 673)]
    [Description("Allows players with permission to be invulerable and god-like")]
    internal class Godmode : RustPlugin
    {
        #region Initialization

        private readonly Dictionary<string, DateTime> informHistory = new Dictionary<string, DateTime>();

        private const string permAdmin = "godmode.admin";
        private const string permInvulerable = "godmode.invulnerable";
        private const string permLootPlayers = "godmode.lootplayers";
        private const string permLootProtection = "godmode.lootprotection";
        private const string permNoAttacking = "godmode.noattacking";
        private const string permToggle = "godmode.toggle";
        private const string permUntiring = "godmode.untiring";

        private void Init()
        {
            LoadData();
            LoadConfig();
            AddCovalenceCommand(configData.godCommand, nameof(GodCommand));
            AddCovalenceCommand(configData.godsCommand, nameof(GodsCommand));

            permission.RegisterPermission(permAdmin, this);
            permission.RegisterPermission(permInvulerable, this);
            permission.RegisterPermission(permLootPlayers, this);
            permission.RegisterPermission(permLootProtection, this);
            permission.RegisterPermission(permNoAttacking, this);
            permission.RegisterPermission(permToggle, this);
            permission.RegisterPermission(permUntiring, this);
        }

        private void OnServerInitialized()
        {
            foreach (var god in storedData.godPlayers)
            {
                var player = RelationshipManager.FindByID(ulong.Parse(god));
                if (player == null) continue;
                ModifyMetabolism(player, true);

                if (configData.showNamePrefix)
                    Rename(player, true);
            }
            CheckOnlineGods();
        }

        private void Unload()
        {
            foreach (var god in storedData.godPlayers)
            {
                var player = RelationshipManager.FindByID(ulong.Parse(god));
                if (player == null) continue;
                ModifyMetabolism(player, false);
                if (configData.showNamePrefix)
                    Rename(player, false);
            }
            SaveData();
        }

        private void CheckOnlineGods()
        {
            if (storedData.godPlayers.Count > 0)
            {
                Subscribe(nameof(CanBeWounded));
                Subscribe(nameof(CanLootPlayer));
                Subscribe(nameof(OnEntityTakeDamage));
                Subscribe(nameof(OnRunPlayerMetabolism));
            }
            else
            {
                Unsubscribe(nameof(CanBeWounded));
                Unsubscribe(nameof(CanLootPlayer));
                Unsubscribe(nameof(OnEntityTakeDamage));
                Unsubscribe(nameof(OnRunPlayerMetabolism));
            }
        }

        #endregion Initialization

        #region Commands

        private void GodCommand(IPlayer iPlayer, string command, string[] args)
        {
            if ((args.Length > 0 && !iPlayer.HasPermission(permAdmin)) || !iPlayer.HasPermission(permToggle))
            {
                Print(iPlayer, Lang("NotAllowed", iPlayer.Id, command));
                return;
            }
            if (args.Length == 0 && iPlayer.Id == "server_console")
            {
                Print(iPlayer, $"The server console cannot use {command}");
                return;
            }
            var target = args.Length > 0 ? RustCore.FindPlayer(args[0]) : iPlayer.Object as BasePlayer;
            if (args.Length > 0 && target == null)
            {
                Print(iPlayer, Lang("PlayerNotFound", iPlayer.Id, args[0]));
                return;
            }
            object obj = ToggleGodmode(target, iPlayer.Object as BasePlayer);
            if (iPlayer.Id == "server_console" && args.Length > 0 && obj is bool)
            {
                if ((bool)obj) Print(iPlayer, $"'{target.displayName}' have enabled godmode");
                else Print(iPlayer, $"'{target.displayName}' have disabled godmode");
            }
        }

        private void GodsCommand(IPlayer iPlayer, string command, string[] args)
        {
            if (!iPlayer.HasPermission(permAdmin))
            {
                Print(iPlayer, Lang("NotAllowed", iPlayer.Id, command));
                return;
            }
            if (storedData.godPlayers.Count == 0)
            {
                Print(iPlayer, Lang("NoGods", iPlayer.Id));
                return;
            }
            string result = string.Empty;
            foreach (var god in storedData.godPlayers)
            {
                BasePlayer player = RustCore.FindPlayerByIdString(god);
                if (player == null) continue;
                result += $"\n[{god}] {player.displayName}";
            }
            Print(iPlayer, result);
        }

        #endregion Commands

        #region Godmode Toggle

        private object ToggleGodmode(BasePlayer target, BasePlayer player)
        {
            object obj = Interface.CallHook("OnGodmodeToggle", target.UserIDString, !IsGod(target.UserIDString));
            if (obj != null) return null;

            if (IsGod(target.UserIDString))
            {
                DisableGodmode(target.UserIDString);
                if (player != null)
                {
                    if (target == player) Print(player, Lang("GodmodeDisabled", player.UserIDString));
                    else
                    {
                        Print(player, Lang("GodmodeDisabledFor", player.UserIDString, target.displayName));
                        Print(target, Lang("GodmodeDisabledBy", target.UserIDString, player.displayName));
                    }
                }
                else Print(target, Lang("GodmodeDisabledBy", target.UserIDString, "server console"));
                return false;
            }
            else
            {
                EnableGodmode(target.UserIDString);
                if (player != null)
                {
                    if (target == player) Print(player, Lang("GodmodeEnabled", player.UserIDString));
                    else
                    {
                        Print(player, Lang("GodmodeEnabledFor", player.UserIDString, target.displayName));
                        Print(target, Lang("GodmodeEnabledBy", target.UserIDString, player.displayName));
                    }
                }
                else Print(target, Lang("GodmodeEnabledBy", target.UserIDString, "server console"));
                string targetID = target.UserIDString;
                if (configData.timeLimit > 0) timer.Once(configData.timeLimit, () => DisableGodmode(targetID));
                return true;
            }
        }

        private bool EnableGodmode(IPlayer iPlayer) => EnableGodmode(iPlayer.Id);

        private bool EnableGodmode(string playerID)
        {
            if (string.IsNullOrEmpty(playerID)) return false;
            var player = RustCore.FindPlayerById(ulong.Parse(playerID));
            if (player == null) return false;
            storedData.godPlayers.Add(player.UserIDString);
            if (configData.showNamePrefix) Rename(player, true);

            ModifyMetabolism(player, true);
            CheckOnlineGods();
            return true;
        }

        private bool DisableGodmode(IPlayer iPlayer) => DisableGodmode(iPlayer.Id);

        private bool DisableGodmode(string playerID)
        {
            if (string.IsNullOrEmpty(playerID)) return false;
            var player = RustCore.FindPlayerById(ulong.Parse(playerID));
            if (player == null) return false;
            if (IsGod(player.UserIDString)) storedData.godPlayers.Remove(player.UserIDString);
            if (configData.showNamePrefix) Rename(player, false);

            ModifyMetabolism(player, false);
            CheckOnlineGods();
            return true;
        }

        private void Rename(BasePlayer player, bool isGod)
        {
            if (player == null) return;
            if (isGod && !player.displayName.Contains(configData.namePrefix)) RenameFunction(player, $"{configData.namePrefix} {player.displayName}");
            else RenameFunction(player, player.displayName.Replace(configData.namePrefix, "").Trim());
        }

        private void RenameFunction(BasePlayer player, string name)
        {
            if (player == null) return;
            name = string.IsNullOrEmpty(name.Trim()) ? player.displayName : name;
            if (player.net?.connection != null) player.net.connection.username = name;
            player.displayName = name;
            player._name = name;
            if (player.IPlayer != null) player.IPlayer.Name = name;
            permission.UpdateNickname(player.UserIDString, name);
            player.SendNetworkUpdateImmediate();
        }

        #endregion Godmode Toggle

        #region Oxide Hook

        private object CanBeWounded(BasePlayer player) => IsGod(player.UserIDString) ? (object)false : null;

        private object CanLootPlayer(BasePlayer target, BasePlayer looter)
        {
            if (target == null || looter == null) return null;
            if (permission.UserHasPermission(target.UserIDString, permLootProtection) && !permission.UserHasPermission(looter.UserIDString, permLootPlayers))
            {
                NextTick(() =>
                {
                    looter.EndLooting();
                    Print(looter, Lang("NoLooting", looter.UserIDString));
                });
                return false;
            }
            return null;
        }

        private object OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            if (player == null || (player?.userID.IsSteamId() == false)) return null;
            var attacker = info?.Initiator as BasePlayer;
            if (IsGod(player.UserIDString) && permission.UserHasPermission(player.UserIDString, permInvulerable))
            {
                if (configData.informOnAttack && attacker != null)
                    InformPlayers(player, attacker);
                NullifyDamage(ref info);
                return true;
            }
            if (attacker != null && IsGod(attacker.UserIDString) && permission.UserHasPermission(attacker.UserIDString, permNoAttacking))
            {
                if (configData.informOnAttack)
                    InformPlayers(player, attacker);
                NullifyDamage(ref info);
                return true;
            }
            return null;
        }

        private object OnRunPlayerMetabolism(PlayerMetabolism metabolism, BasePlayer player, float delta)
        {
            if (player == null) return null;
            if (!IsGod(player.UserIDString)) return null;
            metabolism.hydration.value = 250;
            if (!permission.UserHasPermission(player.UserIDString, permUntiring)) return null;
            var craftLevel = player.currentCraftLevel;
            player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench1, craftLevel == 1f);
            player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench2, craftLevel == 2f);
            player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench3, craftLevel == 3f);
            player.SetPlayerFlag(BasePlayer.PlayerFlags.NoSprint, false);
            return true;
        }

        private void NullifyDamage(ref HitInfo info)
        {
            info.damageTypes = new DamageTypeList();
            info.HitMaterial = 0;
            info.PointStart = Vector3.zero;
        }

        private static void ModifyMetabolism(BasePlayer player, bool isGod)
        {
            if (player == null) return;
            if (isGod)
            {
                player.health = player._maxHealth;
                player.metabolism.bleeding.max = 0;
                player.metabolism.bleeding.value = 0;
                player.metabolism.calories.min = 500;
                player.metabolism.calories.value = 500;
                player.metabolism.dirtyness.max = 0;
                player.metabolism.dirtyness.value = 0;
                player.metabolism.heartrate.min = 0.5f;
                player.metabolism.heartrate.max = 0.5f;
                player.metabolism.heartrate.value = 0.5f;
                //player.metabolism.hydration.min = 250;
                player.metabolism.hydration.value = 250;
                player.metabolism.oxygen.min = 1;
                player.metabolism.oxygen.value = 1;
                player.metabolism.poison.max = 0;
                player.metabolism.poison.value = 0;
                player.metabolism.radiation_level.max = 0;
                player.metabolism.radiation_level.value = 0;
                player.metabolism.radiation_poison.max = 0;
                player.metabolism.radiation_poison.value = 0;
                player.metabolism.temperature.min = 32;
                player.metabolism.temperature.max = 32;
                player.metabolism.temperature.value = 32;
                player.metabolism.wetness.max = 0;
                player.metabolism.wetness.value = 0;
            }
            else
            {
                player.metabolism.bleeding.min = 0;
                player.metabolism.bleeding.max = 1;
                player.metabolism.calories.min = 0;
                player.metabolism.calories.max = 500;
                player.metabolism.dirtyness.min = 0;
                player.metabolism.dirtyness.max = 100;
                player.metabolism.heartrate.min = 0;
                player.metabolism.heartrate.max = 1;
                //player.metabolism.hydration.min = 0;
                player.metabolism.hydration.max = 250;
                player.metabolism.oxygen.min = 0;
                player.metabolism.oxygen.max = 1;
                player.metabolism.poison.min = 0;
                player.metabolism.poison.max = 100;
                player.metabolism.radiation_level.min = 0;
                player.metabolism.radiation_level.max = 100;
                player.metabolism.radiation_poison.min = 0;
                player.metabolism.radiation_poison.max = 500;
                player.metabolism.temperature.min = -100;
                player.metabolism.temperature.max = 100;
                player.metabolism.wetness.min = 0;
                player.metabolism.wetness.max = 1;
            }
            player.metabolism.SendChangesToClient();
        }

        private void InformPlayers(BasePlayer victim, BasePlayer attacker)
        {
            if (victim == null || attacker == null || victim == attacker) return;
            if (!informHistory.ContainsKey(victim.UserIDString)) informHistory.Add(victim.UserIDString, DateTime.MinValue);
            if (!informHistory.ContainsKey(attacker.UserIDString)) informHistory.Add(attacker.UserIDString, DateTime.MinValue);
            if (IsGod(victim.UserIDString))
            {
                if (DateTime.Now.Subtract(informHistory[victim.UserIDString]).TotalSeconds > 15)
                {
                    Print(attacker, Lang("InformAttacker", attacker.UserIDString, victim.displayName));
                    informHistory[victim.UserIDString] = DateTime.Now;
                }
                if (DateTime.Now.Subtract(informHistory[attacker.UserIDString]).TotalSeconds > 15)
                {
                    Print(victim, Lang("InformVictim", victim.UserIDString, attacker.displayName));
                    informHistory[attacker.UserIDString] = DateTime.Now;
                }
            }
            else if (IsGod(attacker.UserIDString))
            {
                if (DateTime.Now.Subtract(informHistory[victim.UserIDString]).TotalSeconds > 15)
                {
                    Print(attacker, Lang("CantAttack", attacker.UserIDString, victim.displayName));
                    informHistory[victim.UserIDString] = DateTime.Now;
                }
                if (DateTime.Now.Subtract(informHistory[attacker.UserIDString]).TotalSeconds > 15)
                {
                    Print(victim, Lang("InformVictim", victim.UserIDString, attacker.displayName));
                    informHistory[attacker.UserIDString] = DateTime.Now;
                }
            }
        }

        #endregion Oxide Hook

        #region ConfigurationFile

        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Inform On Attack (true/false)")]
            public bool informOnAttack = true;

            [JsonProperty(PropertyName = "Show Name Prefix (true/false)")]
            public bool showNamePrefix = true;

            [JsonProperty(PropertyName = "Name Prefix (Default [God])")]
            public string namePrefix = "[God]";

            [JsonProperty(PropertyName = "Time Limit (Seconds, 0 to Disable)")]
            public float timeLimit = 0f;

            [JsonProperty(PropertyName = "Chat Prefix")]
            public string prefix = "[Godmode]:";

            [JsonProperty(PropertyName = "Chat Prefix color")]
            public string prefixColor = "#00FFFF";

            [JsonProperty(PropertyName = "Chat steamID icon")]
            public ulong steamIDIcon = 0;

            [JsonProperty(PropertyName = "God commands")]
            public string[] godCommand = new string[] { "god", "godmode" };

            [JsonProperty(PropertyName = "Gods commands")]
            public string[] godsCommand = new string[] { "gods", "godlist" };

            public static ConfigData DefaultConfig()
            {
                return new ConfigData()
                {
                    informOnAttack = true,
                    showNamePrefix = true,
                    timeLimit = 0f,
                    namePrefix = "[God]",
                    prefix = "[Godmode]:",
                    prefixColor = "#00FFFF",
                    steamIDIcon = 0,
                    godCommand = new string[] { "god", "godmode" },
                    godsCommand = new string[] { "gods", "godlist" },
                };
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                configData = Config.ReadObject<ConfigData>();
                if (configData == null)
                    LoadDefaultConfig();
            }
            catch
            {
                PrintError("The configuration file is corrupted");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            configData = ConfigData.DefaultConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(configData);

        #endregion ConfigurationFile

        #region DataFile

        private StoredData storedData;

        private class StoredData
        {
            public HashSet<string> godPlayers = new HashSet<string>();
        }

        private void LoadData()
        {
            try
            {
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch
            {
                ClearData();
            }
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);

        private void ClearData()
        {
            storedData = new StoredData();
            SaveData();
        }

        #endregion DataFile

        #region LanguageFile

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["GodmodeDisabled"] = "You have <color=#FF4500>Disabled</color> godmode",
                ["GodmodeDisabledBy"] = "Your godmode has been <color=#FF4500>Disabled</color> by {0}",
                ["GodmodeDisabledFor"] = "You have <color=#FF4500>Disabled</color> godmode for {0}",
                ["GodmodeEnabled"] = "You have <color=#00FF00>Enabled</color> godmode",
                ["GodmodeEnabledBy"] = "Your godmode has been <color=#00FF00>Enabled</color> by {0}",
                ["GodmodeEnabledFor"] = "You have <color=#00FF00>Enabled</color> godmode for {0}",
                ["InformAttacker"] = "{0} is in godmode and can't take any damage",
                ["InformVictim"] = "{0} just tried to deal damage to you",
                ["CantAttack"] = "you is in godmode and can't attack {0}",
                ["NoGods"] = "No players currently have godmode enabled",
                ["NoLooting"] = "You are not allowed to loot a player with godmode",
                ["NotAllowed"] = "You are not allowed to use the '{0}' command",
                ["PlayerNotFound"] = "Player '{0}' was not found",
            }, this);
        }

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        #endregion LanguageFile

        #region Helpers

        private void Print(IPlayer iPlayer, string message)
        {
            if (iPlayer == null) return;
            if (iPlayer.Id == "server_console") iPlayer.Reply(message, $"{configData.prefix}");
            else iPlayer.Reply(message, $"<color={configData.prefixColor}>{configData.prefix}</color>");
        }

        private void Print(BasePlayer player, string message) => Player.Message(player, message, $"<color={configData.prefixColor}>{configData.prefix}</color>", configData.steamIDIcon);

        private bool IsGod(string playerID) => storedData.godPlayers.Contains(playerID);

        #endregion Helpers
    }
}