using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using ConVar;
using UnityEngine;
using Oxide.Core.Plugins;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Event Randomizer", "mvrb", "0.3.4")]
    [Description("Set random timers for server events")]
    class EventRandomizer : RustPlugin
    {
        private float heliInterval;
        private float chinookInterval;
        private float bradleyInterval;
        private float cargoInterval;
        private float airdropInterval;

        private int lastHeli;
        private int lastChinook;
        private int lastCargo;
        private int lastAirdrop;
        private int lastBradley;

        private string permSpawnChinook = "eventrandomizer.spawn.ch47";
        private string permSpawnHeli = "eventrandomizer.spawn.heli";
        private string permSpawnCargo = "eventrandomizer.spawn.cargo";
        private string permSpawnAirdrop = "eventrandomizer.spawn.airdrop";
        private string permSpawnBradley = "eventrandomizer.spawn.bradley";
		
        private string permCheckTimer = "eventrandomizer.check";
		
		private bool initialized = false;

        private class EventTimer
        {
            public float Min;
            public float Max;
        }

        private void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NextHeliIn"] = "Next Patrol Helicopter spawns in {0}",
                ["NextChinookIn"] = "Next Chinook Helicopter spawns in {0}",
                ["NextCargoIn"] = "Next Cargo Ship spawns in {0}",
                ["NextAirdropIn"] = "Next Airdrop spawns in {0}",
                ["NextBradleyIn"] = "Next Bradley spawns in {0}",

                ["CargoSpawned"] = "A Cargo Ship has been spawned.",
                ["PatrolHelicopterSpawned"] = "A Patrol Helicopter has been spawned.",
                ["ChinookSpawned"] = "A Chinook Helicopter has been spawned.",
                ["AirdropSpawned"] = "An Airdrop has been spawned.",
                ["BradleySpawned"] = "Bradley has been spawned.",

                ["FormatTime"] = "{0} Hours {1} Minutes",

                ["EventNotEnabled"] = "The Min and Max timer for {0} is less than or equal to 0 so this event has been disabled.",

                ["Warning: MinGreaterThanMax"] = "The minimum value ({0}) for {1} is greater than the maximum value ({2})!",

                ["Error: NoPermission"] = "You do not have permission to use this command.",
            }, this);
        }

        private void OnServerInitialized()
        {
            permission.RegisterPermission(permSpawnChinook, this);
            permission.RegisterPermission(permSpawnHeli, this);
            permission.RegisterPermission(permSpawnCargo, this);
            permission.RegisterPermission(permSpawnAirdrop, this);
            permission.RegisterPermission(permSpawnBradley, this);
            permission.RegisterPermission(permCheckTimer, this);

            LoadVariables();

            foreach (var entry in configData.EventTimers)
            {
                if (entry.Value.Min > entry.Value.Max)
                {
                    PrintWarning(Lang("Warning: MinGreaterThanMax", null, entry.Value.Min, entry.Key, entry.Value.Max));
                }
            }

            if (configData.EventTimers["Helicopter"].Min > 0 && configData.EventTimers["Helicopter"].Max > 0)
            {
                heliInterval = UnityEngine.Random.Range(configData.EventTimers["Helicopter"].Min, configData.EventTimers["Helicopter"].Max);
                timer.Once(heliInterval, SpawnHeliRandom);
                PrintWarning(Lang("NextHeliIn", null, FormatTime(heliInterval)));
            }
            else
            {
                PrintWarning(Lang("EventNotEnabled", null, "Helicopter"));
            }

            if (configData.EventTimers["Chinook"].Min > 0 && configData.EventTimers["Chinook"].Max > 0)
            {
                chinookInterval = UnityEngine.Random.Range(configData.EventTimers["Chinook"].Min, configData.EventTimers["Chinook"].Max);
                timer.Once(chinookInterval, SpawnChinookRandom);
                PrintWarning(Lang("NextChinookIn", null, FormatTime(chinookInterval)));
            }
            else
            {
                PrintWarning(Lang("EventNotEnabled", null, "Chinook"));
            }

            if (configData.EventTimers["Bradley"].Min > 0 && configData.EventTimers["Bradley"].Max > 0)
            {
                bradleyInterval = UnityEngine.Random.Range(configData.EventTimers["Bradley"].Min, configData.EventTimers["Bradley"].Max);
                timer.Once(bradleyInterval, SpawnBradleyRandom);
                PrintWarning(Lang("NextBradleyIn", null, FormatTime(bradleyInterval)));
            }
            else
            {
                PrintWarning(Lang("EventNotEnabled", null, "Bradley"));
            }

            if (configData.EventTimers["Cargo"].Min > 0 && configData.EventTimers["Cargo"].Max > 0)
            {
                cargoInterval = UnityEngine.Random.Range(configData.EventTimers["Cargo"].Min, configData.EventTimers["Cargo"].Max);
                timer.Once(cargoInterval, SpawnCargoRandom);
                PrintWarning(Lang("NextCargoIn", null, FormatTime(cargoInterval)));
            }
            else
            {
                PrintWarning(Lang("EventNotEnabled", null, "Cargo"));
            }

            if (configData.EventTimers["Airdrop"].Min > 0 && configData.EventTimers["Airdrop"].Max > 0)
            {
                airdropInterval = UnityEngine.Random.Range(configData.EventTimers["Airdrop"].Min, configData.EventTimers["Airdrop"].Max);
                timer.Once(airdropInterval, SpawnAirdropRandom);
                PrintWarning(Lang("NextAirdropIn", null, FormatTime(airdropInterval)));
            }
            else
            {
                PrintWarning(Lang("EventNotEnabled", null, "Airdrop"));
            }

            var currentTime = GetUnix();

            lastHeli = currentTime;
            lastChinook = currentTime;
            lastCargo = currentTime;
            lastAirdrop = currentTime;
            lastBradley = currentTime;
			
			initialized = true;
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
			if (!initialized) return;			
            
            /*if (configData.blockServerAirdrops && entity is CargoPlane)
            {
                entity.KillMessage();
            }
            else*/ if (configData.blockServerCargoShips && entity is CargoShip)
            {
                entity.KillMessage();
            }
            else if (configData.blockServerChinooks && (entity is CH47Helicopter || entity is CH47HelicopterAIController))
            {
                //entity.KillMessage();
            }
            else if (configData.blockServerPatrolHelicopters && entity is PatrolHelicopterAI)
            {
                entity.KillMessage();
            }
        }

        [HookMethod("HeliTime")]
        private string TimeToHeli()
        {
            if (heliInterval + lastHeli - GetUnix() > 0)
            {
                return FormatTime(heliInterval + lastHeli - GetUnix());
            }
            return FormatTime(0);
        }
        [HookMethod("Ch47Time")]
        private string TimeToCh()
        {
            if(chinookInterval + lastChinook - GetUnix() > 0)
            {
                return FormatTime(chinookInterval + lastChinook - GetUnix());
            }
            return FormatTime(0);
        }
        [HookMethod("CargoTime")]
        private string TimeToCargo()
        {
            if (cargoInterval + lastCargo - GetUnix() > 0)
            {
                return FormatTime(cargoInterval + lastCargo - GetUnix());
            }
            return FormatTime(0);
        }

        [HookMethod("BradleyTime")]
        private string TimeToBradley()
        {
            if (bradleyInterval + lastBradley - GetUnix() > 0)
            {
                return FormatTime(bradleyInterval + lastBradley - GetUnix());
            }
            return FormatTime(0);
        }


        [ChatCommand("heli")]
        private void CmdHeli(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, permCheckTimer))
            {
                player.ChatMessage(Lang("Error: NoPermission", player.UserIDString));
                return;
            }

            player.ChatMessage(Lang("NextHeliIn", player.UserIDString, FormatTime(heliInterval + lastHeli - GetUnix())));
        }

        [ChatCommand("chinook")]
        private void CmdChinook(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, permCheckTimer))
            {
                player.ChatMessage(Lang("Error: NoPermission", player.UserIDString));
                return;
            }

            player.ChatMessage(Lang("NextChinookIn", player.UserIDString, FormatTime(chinookInterval + lastChinook - GetUnix())));
        }

        [ChatCommand("cargo")]
        private void CmdCargo(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, permCheckTimer))
            {
                player.ChatMessage(Lang("Error: NoPermission", player.UserIDString));
                return;
            }

            player.ChatMessage(Lang("NextCargoIn", player.UserIDString, FormatTime(cargoInterval + lastCargo - GetUnix())));
        }

        [ConsoleCommand("ch47.spawn")]
        private void ConsoleCmdSpawnCh47(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();

            if (arg?.Connection != null && player && !permission.UserHasPermission(player.UserIDString, permSpawnChinook))
            {
                player.ChatMessage(Lang("Error: NoPermission", player.UserIDString));
                return;
            }

            SpawnChinook();
        }

        [ConsoleCommand("heli.spawn")]
        private void ConsoleCmdSpawnHeli(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();

            if (arg?.Connection != null && player && !permission.UserHasPermission(player.UserIDString, permSpawnHeli))
            {
                player.ChatMessage(Lang("Error: NoPermission", player.UserIDString));
                return;
            }

            SpawnHeli();
        }

        [ConsoleCommand("cargo.spawn")]
        private void ConsoleCmdSpawnCargo(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();

            if (arg?.Connection != null && player && !permission.UserHasPermission(player.UserIDString, permSpawnCargo))
            {
                player.ChatMessage(Lang("Error: NoPermission", player.UserIDString));
                return;
            }

            SpawnCargo();
        }

        [ConsoleCommand("airdrop.spawn")]
        private void ConsoleCmdSpawnAirdrop(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();

            if (arg?.Connection != null && player && !permission.UserHasPermission(player.UserIDString, permSpawnAirdrop))
            {
                player.ChatMessage(Lang("Error: NoPermission", player.UserIDString));
                return;
            }

            SpawnAirdrop();
        }

        private void SpawnCargoRandom()
        {
            SpawnCargo();
            lastCargo = GetUnix();

            cargoInterval = UnityEngine.Random.Range(configData.EventTimers["Cargo"].Min, configData.EventTimers["Cargo"].Max);
            timer.Once(cargoInterval, SpawnCargoRandom);
        }

        private void SpawnHeliRandom()
        {
            SpawnHeli();
            lastHeli = GetUnix();

            heliInterval = UnityEngine.Random.Range(configData.EventTimers["Helicopter"].Min, configData.EventTimers["Helicopter"].Max);
            timer.Once(heliInterval, SpawnHeliRandom);
        }

        private void SpawnChinookRandom()
        {
            SpawnChinook();
            lastChinook = GetUnix();

            chinookInterval = UnityEngine.Random.Range(configData.EventTimers["Chinook"].Min, configData.EventTimers["Chinook"].Max);
            timer.Once(chinookInterval, SpawnChinookRandom);
        } 
        private void SpawnBradleyRandom()
        {
            SpawnBradley();
            lastBradley = GetUnix();

            chinookInterval = UnityEngine.Random.Range(configData.EventTimers["Bradley"].Min, configData.EventTimers["Bradley"].Max);
            timer.Once(chinookInterval, SpawnBradleyRandom);
        }

        private void SpawnAirdropRandom()
        {
            SpawnAirdrop();
            lastAirdrop = GetUnix();

            airdropInterval = UnityEngine.Random.Range(configData.EventTimers["Airdrop"].Min, configData.EventTimers["Airdrop"].Max);
            timer.Once(airdropInterval, SpawnAirdropRandom);
        }

        private string FormatTime(float seconds)
        {
            return TimeToString(seconds);
        }

        public string TimeToString(double time)
        {
            TimeSpan elapsedTime = TimeSpan.FromSeconds(time);
            int hours = elapsedTime.Hours;
            int minutes = elapsedTime.Minutes;
            int seconds = elapsedTime.Seconds;
            int days = Mathf.FloorToInt((float)elapsedTime.TotalDays);
            string s = "";
            if (days > 0) s += $"{days} дн.";
            if (hours > 0) s += $"{hours} ч. ";
            if (minutes > 0) s += $"{minutes} мин. ";
            if (seconds > 0) s += $"{seconds} сек.";
            else s = s.TrimEnd(' ');
            return s;
        }

        private void SpawnAirdrop()
        {
            var entity = GameManager.server.CreateEntity("assets/prefabs/npc/cargo plane/cargo_plane.prefab", new Vector3());
            entity?.Spawn();

            PrintWarning(Lang("AirdropSpawned"));
        }

        private void SpawnCargo()
        {
            Unsubscribe(nameof(OnEntitySpawned));
            var cargos = GameObject.FindObjectsOfType<CargoShip>();
            foreach (var item in cargos)
            {
                item.Kill();
            }
            var cargoShip = GameManager.server.CreateEntity("assets/content/vehicles/boats/cargoship/cargoshiptest.prefab") as CargoShip;
            if (cargoShip == null) return;
            cargoShip.TriggeredEventSpawn();
            cargoShip.Spawn();
            PrintWarning(Lang("CargoSpawned"));
            Subscribe(nameof(OnEntitySpawned));
        }

        private void SpawnBradley()
        {
            Unsubscribe(nameof(OnEntitySpawned));
            var monument = UnityEngine.Object.FindObjectsOfType<MonumentInfo>().FirstOrDefault(p => p.name.Contains("launch_site_1"));
            if (monument == null)
            {
                Puts("Launch Site not found!");
                return;
            }
            var pos = monument.transform.position + monument.transform.rotation * new Vector3(30f, 4f, 30f);
            var bradleys = GameObject.FindObjectsOfType<BradleyAPC>();
            foreach (var item in bradleys)
            {
                item.Kill();
            }
            var bradley = GameManager.server.CreateEntity("assets/prefabs/npc/m2bradley/bradleyapc.prefab", pos) as BradleyAPC;
            if (bradley == null) return;
            bradley.Spawn();
            Subscribe(nameof(OnEntitySpawned));
        }

        private void SpawnHeli()
        {
            Unsubscribe(nameof(OnEntitySpawned));
            var helis = GameObject.FindObjectsOfType<BaseHelicopter>();
            foreach (var item in helis)
            {
                item.Kill();
            }
            var heli = GameManager.server.CreateEntity("assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab");
            if (heli == null) return;
            heli.Spawn();

            PrintWarning(Lang("PatrolHelicopterSpawned"));
            Subscribe(nameof(OnEntitySpawned));
        }

        private void SpawnChinook()
        {
            Unsubscribe(nameof(OnEntitySpawned));
            var ch47s = GameObject.FindObjectsOfType<CH47Helicopter>();
            foreach (var item in ch47s)
            {
                item.Kill();
            }

            var ch47 = (CH47HelicopterAIController)GameManager.server.CreateEntity("assets/prefabs/npc/ch47/ch47scientists.entity.prefab", new Vector3(0, 200, 0));
            if (ch47 == null) return;
            ch47.Spawn();

            PrintWarning(Lang("ChinookSpawned"));
            Subscribe(nameof(OnEntitySpawned));
        }

        private int GetUnix() => (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        #region Config        
        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Event Timers in seconds")]
            public Dictionary<string, EventTimer> EventTimers { get; set; }

            [JsonProperty(PropertyName = "Block Airdrops spawned by the server")]
            public bool blockServerAirdrops;

            [JsonProperty(PropertyName = "Block Cargo Ships spawned by the server")]
            public bool blockServerCargoShips;

            [JsonProperty(PropertyName = "Block Chinooks spawned by the server")]
            public bool blockServerChinooks;

            [JsonProperty(PropertyName = "Block Patrol Helicopters spawned by the server")]
            public bool blockServerPatrolHelicopters;
        }

        private void LoadVariables()
        {
            LoadConfigVariables();
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            var config = new ConfigData
            {
                EventTimers = new Dictionary<string, EventTimer>()
                {
                    { "Helicopter", new EventTimer(){ Min = 3600, Max = 7200 } },
                    { "Chinook", new EventTimer(){ Min = 7200, Max = 14400 } },
                    { "Cargo", new EventTimer(){ Min = 7200, Max = 10800 } },
                    { "Airdrop", new EventTimer(){ Min = 3600, Max = 3600 } },
                    { "Bradley", new EventTimer(){ Min = 3600, Max = 3600 } }
                },
                blockServerPatrolHelicopters = true,
                blockServerChinooks =  true,
                blockServerAirdrops = true,
                blockServerCargoShips = true
            };

            SaveConfig(config);
        }

        private void LoadConfigVariables() => configData = Config.ReadObject<ConfigData>();

        private void SaveConfig(ConfigData config) => Config.WriteObject(config, true);
        #endregion
    }
}