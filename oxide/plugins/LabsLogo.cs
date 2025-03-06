using System.Reflection.Metadata;
using Mono.Security.X509.Extensions;
using Newtonsoft.Json;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Oxide.Core;
using UnityEngine.UIElements;

namespace Oxide.Plugins
{
    [Info("LabsLogo", "Frizen", "1.0.0")]

    public class LabsLogo : RustPlugin
    {
        #region Vars
        [PluginReference] private Plugin ImageLibrary, EventRandomizer, LabsBonus, NoEscape, FreeOnline;
        private Dictionary<string, Vector3> _oilRigPositions = new Dictionary<string, Vector3>();
        private Dictionary<BasePlayer, bool> _menuOpen = new Dictionary<BasePlayer, bool>();
        public static System.Random Random = new System.Random();
        private const string _workLayer = "Labs.Info";
        private const string _menuLayer = "Labs.Menu";
        public const string Name = "oilrig";
        private bool _isCargoShip = false;
        private bool _isBradley = false;
        private bool _isCh47 = false;
        private bool _isHeli = false;
        private bool _isBigOil = false;
        private bool _isSmallOil = false;
        private const float fadeout = 0.25f;
        private const float fadein = 1f;
        #endregion

        #region Config
        private static Configuration _config;
        public class Configuration
        {
            [JsonProperty("Цвет прогрес бара онлайна")]
            public string BarColor = "#DC6D3ED2";
            [JsonProperty("Цвет прогрес бара заходящих")]
            public string JoinBarColor = "#CD5832D2";
            [JsonProperty("Цвет активного ивента Bradley")]
            public string BradleyColor = "#FF6C0AFF";
            [JsonProperty("Цвет активного ивента Ch47")]
            public string Ch47Color = "#FF6C0AFF";
            [JsonProperty("Цвет активного ивента Heli")]
            public string HeliColor = "#FF6C0AFF";
            [JsonProperty("Время рейдблока в секундах")]
            public float RaidBlockTime = 300f;
            [JsonProperty("Время уведомления рейдблока в секундах")]
            public float NotifyTime = 5f;
            [JsonProperty("Цвет активного ивента Cargo")]
            public string CargoColor = "#FF6C0AFF";
            [JsonProperty("Цвет активного ивента OilRig")]
            public string OilRigColor = "#FF6C0AFF";
            [JsonProperty("Список команд в выпадающем меню")]
            public Dictionary<string, string> MenuCmds = new Dictionary<string, string>();
            [JsonProperty("Картинки")]
            public Dictionary<string, string> Imgs = new Dictionary<string, string>();
            public static Configuration DefaultConfig()
            {
                return new Configuration
                {
                    RaidBlockTime = 300f,
                    NotifyTime = 5f,
                    BarColor = "#BD6C36FF",
                    JoinBarColor = "#BD5636FF",
                    BradleyColor = "#FF6C0AFF",
                    Ch47Color = "#FF6C0AFF",
                    HeliColor = "#FF6C0AFF",
                    CargoColor = "#FF6C0AFF",
                    MenuCmds =
                    {
                        ["https://i.imgur.com/PaLmOrC.png"] = "/fmenu",
                        ["https://i.imgur.com/lEP2hEw.png"] = "/block",
                        ["https://i.imgur.com/RFwT9ug.png"] = "/tasks",
                    },
                    Imgs =
                    {
                      ["CH47_labs"] = "https://i.imgur.com/WqqEdFc.png",
                        ["Heli_labs"] = "https://i.imgur.com/egYt670.png",
                        ["Cargo_labs"] = "https://i.imgur.com/7crGJt1.png",
                        ["Bradley_labs"] = "https://i.imgur.com/CmEdMNw.png",
                        ["Rig_labs"] = "https://i.imgur.com/pv4bJvj.png",
                        ["Left"] = "https://i.imgur.com/AQza2EB.png",
                        ["Right"] = "https://i.imgur.com/nyYk2JK.png",
                        ["Online"] = "https://i.imgur.com/vqnohiL.png",
                        ["Notify"] = "https://i.imgur.com/2mZ8oeu.png"
                    }
                };
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) LoadDefaultConfig();
                SaveConfig();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                PrintWarning("Creating new config file.");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => _config = Configuration.DefaultConfig();
        protected override void SaveConfig() => Config.WriteObject(_config);
        #endregion

        #region Hooks
        private void OnServerInitialized()
        {
            if (ImageLibrary == null)
            {
                PrintError("ImageLibrary plugin not found! Please ensure it is installed.");
                return;
            }

            GetOnline();
            timer.Every(300f, () =>
            {
                GetOnline();
            });
            FirstCheckEvents();

            timer.Once(15f, () =>
            {
                if (EventRandomizer != null)
                {
                    Interface.Oxide.ReloadPlugin("EventRandomizer");
                }
            });

            if (ImageLibrary != null)
            {
                foreach (var img in _config.Imgs)
                {
                    ImageLibrary.Call("AddImage", img.Value, img.Key);
                }

                foreach (var img in _config.MenuCmds)
                {
                    ImageLibrary.Call("AddImage", img.Key, img.Value);
                }
            }

            foreach (var item in BasePlayer.activePlayerList)
            {
                if (item != null)
                {
                    OnPlayerConnected(item);
                }
            }
        }

        float GetOnline()
        {
			if (FreeOnline)
				return FreeOnline.Call<float>("GetOnline");
			
            return 0;
        }

        void OnPlayerConnected(BasePlayer player)
        {
            if (player.IsReceivingSnapshot)
            {
                NextTick(() => OnPlayerConnected(player));
                return;
            }

            if (!_menuOpen.ContainsKey(player)) 
            {
                _menuOpen.Add(player, true);
            }

            DrawMenu(player);

            timer.Once(1f, () =>
            {
                foreach (var players in BasePlayer.activePlayerList)
                {
                    if (players.userID == player.userID) continue;
                    bool IsOpen = false;
                    if (_menuOpen.TryGetValue(players, out IsOpen) && IsOpen)
                    {
                        OnlineUi(players);
                    }
                }
            });
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            timer.Once(1f, () =>
            {
                foreach (var players in BasePlayer.activePlayerList)
                {
                    bool IsOpen = false;
                    if (_menuOpen.TryGetValue(players, out IsOpen) && IsOpen)
                    {
                        OnlineUi(players);
                    }
                }
            });
        }


        void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity == null) return;
            if (entity is CH47Helicopter)
            {
                _isCh47 = true;
                foreach (var player in BasePlayer.activePlayerList) 
                {
                    RefreshEvents(player, "CH47");
                }
                return;
            }
            if (entity is BaseHelicopter)
            {
                _isHeli = true;
                foreach (var player in BasePlayer.activePlayerList) 
                {
                    RefreshEvents(player, "Heli");
                }
                return;
            }
            if (entity is CargoShip)
            {
                _isCargoShip = true;
                foreach (var player in BasePlayer.activePlayerList) 
                {
                    RefreshEvents(player, "Cargo");
                }
                return;
            }
            if (entity is BradleyAPC)
            {
                _isBradley = true;
                foreach (var player in BasePlayer.activePlayerList) 
                {
                    RefreshEvents(player, "Bradley");
                }
                return;
            }
            if (entity is HackableLockedCrate)
            {
                if (!_oilRigPositions.IsEmpty())
                {
                    if (Vector3.Distance(entity.transform.position, _oilRigPositions["OilrigAI"]) < 50)
                    {
                        _isSmallOil = true;
                        foreach (var player in BasePlayer.activePlayerList)
                        {
                            RefreshEvents(player, "Rig");
                        }
                        return;
                    }
                    if (Vector3.Distance(entity.transform.position, _oilRigPositions["OilrigAI2"]) < 50)
                    {
                        _isBigOil = true;
                        foreach (var player in BasePlayer.activePlayerList)
                        {
                            RefreshEvents(player, "Rig");
                        }
                        return;
                    }

                }
            }
        }

        void OnEntityKill(BaseEntity entity)
        {
            if (entity == null) return;
            if (entity is CH47Helicopter)
            {
                _isCh47 = false;
                foreach (var player in BasePlayer.activePlayerList)
                {
                    RefreshEvents(player, "CH47");
                }
                return;
            }
            if (entity is BaseHelicopter)
            {
                _isHeli = false;
                foreach (var player in BasePlayer.activePlayerList)
                {
                    RefreshEvents(player, "Heli");
                }
                return;
            }
            if (entity is CargoShip)
            {
                _isCargoShip = false;
                foreach (var player in BasePlayer.activePlayerList)
                {
                    RefreshEvents(player, "Cargo");
                }
                return;
            }
            if (entity is BradleyAPC)
            {
                _isBradley = false;
                foreach (var player in BasePlayer.activePlayerList)
                {
                    RefreshEvents(player, "Bradley");
                }
                return;
            }
            if (entity is HackableLockedCrate)
            {
                if (!_oilRigPositions.IsEmpty())
                {
                    if (Vector3.Distance(entity.transform.position, _oilRigPositions["OilrigAI"]) < 50)
                    {
                        _isSmallOil = false;
                        foreach (var player in BasePlayer.activePlayerList)
                        {
                            RefreshEvents(player, "Rig");
                        }
                        return;
                    }
                    if (Vector3.Distance(entity.transform.position, _oilRigPositions["OilrigAI2"]) < 50)
                    {
                        _isBigOil = false;
                        foreach (var player in BasePlayer.activePlayerList)
                        {
                            RefreshEvents(player, "Rig");
                        }
                        return;
                    }
                }
            }
        }

        void OnEntityDeath(BaseEntity entity, HitInfo info)
        {
            if (entity == null) return;
            if (entity is CH47Helicopter)
            {
                _isCh47 = false;
                foreach (var player in BasePlayer.activePlayerList) 
                {
                    RefreshEvents(player, "CH47");
                }
                return;
            }
            if (entity is BaseHelicopter)
            {
                _isHeli = false;
                foreach (var player in BasePlayer.activePlayerList)
                {
                    RefreshEvents(player, "Heli");
                }
                return;
            }
            if (entity is CargoShip)
            {
                _isCargoShip = false;
                foreach (var player in BasePlayer.activePlayerList)
                {
                    RefreshEvents(player, "Cargo");
                }
                return;
            }
            if (entity is BradleyAPC)
            {
                _isBradley = false;
                foreach (var player in BasePlayer.activePlayerList) 
                {
                    RefreshEvents(player, "Bradley");
                }
                return;
            }
            if (entity is HackableLockedCrate)
            {
                if (!_oilRigPositions.IsEmpty())
                {
                    if (Vector3.Distance(entity.transform.position, _oilRigPositions["OilrigAI"]) < 50)
                    {
                        _isSmallOil = false;
                        foreach (var player in BasePlayer.activePlayerList)
                        {
                            RefreshEvents(player, "Rig");
                        }
                        return;
                    }
                    if (Vector3.Distance(entity.transform.position, _oilRigPositions["OilrigAI2"]) < 50)
                    {
                        _isBigOil = false;
                        foreach (var player in BasePlayer.activePlayerList)
                        {
                            RefreshEvents(player, "Rig");
                        }
                        return;
                    }
                }
            }
        }

        void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, "JoinBar");
                CuiHelper.DestroyUi(player, "Joins");
                CuiHelper.DestroyUi(player, "OnlineBar");
                CuiHelper.DestroyUi(player, "OnlineColored");
                CuiHelper.DestroyUi(player, "Online");
                CuiHelper.DestroyUi(player, "Line");
                CuiHelper.DestroyUi(player, "InfoR");
                CuiHelper.DestroyUi(player, "RBPanel");
                CuiHelper.DestroyUi(player, "OnlineIMG");
                CuiHelper.DestroyUi(player, "Info");
                CuiHelper.DestroyUi(player, _workLayer + "Info");
                CuiHelper.DestroyUi(player, _workLayer + "OnlinePanel");
                CuiHelper.DestroyUi(player, "ArrowBTN");
                CuiHelper.DestroyUi(player, "ArrowIMG");
                CuiHelper.DestroyUi(player, "Arrow");
                CuiHelper.DestroyUi(player, _workLayer);
                CuiHelper.DestroyUi(player, "NotifyIMG");
                CuiHelper.DestroyUi(player, "Info");
                CuiHelper.DestroyUi(player, "Text");
                CuiHelper.DestroyUi(player, "NotifyPanel");

                DestroyButtons(player);
            }
        }

        #endregion

        #region Methods
        private void FirstCheckEvents()
        {
            var rigs = UnityEngine.Object.FindObjectsOfType<MonumentInfo>().Where(p => p.name.ToLower().Contains(Name.ToLower()));
            
            foreach (var rig in rigs)
            {
                if (rig != null && !string.IsNullOrEmpty(rig.name))
                {
                    _oilRigPositions[rig.name] = rig.transform.position;
                }
            }

            foreach (var entity in BaseEntity.serverEntities)
            {
                if (entity == null) continue;
                
                if (entity is CargoShip)
                {
                    _isCargoShip = true;
                }
                if (entity is BradleyAPC)
                {
                    _isBradley = true;
                }
                if (entity is CH47Helicopter)
                {
                    _isCh47 = true;
                }
                if (entity is BaseHelicopter)
                {
                    _isHeli = true;
                }
            }

            if (!_oilRigPositions.IsEmpty())
            {
                foreach (var crate in UnityEngine.Object.FindObjectsOfType<HackableLockedCrate>())
                {
                    if (crate == null) continue;

                    Vector3 smallRigPos;
                    Vector3 bigRigPos;

                    if (_oilRigPositions.TryGetValue("OilrigAI", out smallRigPos))
                    {
                        if (Vector3.Distance(crate.transform.position, smallRigPos) < 50)
                        {
                            _isSmallOil = true;
                        }
                    }

                    if (_oilRigPositions.TryGetValue("OilrigAI2", out bigRigPos))
                    {
                        if (Vector3.Distance(crate.transform.position, bigRigPos) < 50)
                        {
                            _isBigOil = true;
                        }
                    }
                }
            }
        }

        private void DrawMenu(BasePlayer player)
        {
            if (player == null) return;
            
            CuiElementContainer container = new CuiElementContainer();
            CuiHelper.DestroyUi(player, "JoinBar");
            CuiHelper.DestroyUi(player, "Joins");
            CuiHelper.DestroyUi(player, "OnlineBar");
            CuiHelper.DestroyUi(player, "OnlineColored");
            CuiHelper.DestroyUi(player, "Online");
            CuiHelper.DestroyUi(player, "Line");
            CuiHelper.DestroyUi(player, "InfoR");
            CuiHelper.DestroyUi(player, "RBPanel");
            CuiHelper.DestroyUi(player, "OnlineIMG");
            CuiHelper.DestroyUi(player, _workLayer + "OnlinePanel");
            CuiHelper.DestroyUi(player, "ArrowBTN");
            CuiHelper.DestroyUi(player, "ArrowIMG");
            CuiHelper.DestroyUi(player, "Arrow");
            CuiHelper.DestroyUi(player, _workLayer);
            CuiHelper.DestroyUi(player, "NotifyIMG");
            CuiHelper.DestroyUi(player, "Info");
            CuiHelper.DestroyUi(player, "Text");
            CuiHelper.DestroyUi(player, "NotifyPanel");
            DestroyButtons(player);

            bool IsOpen = false;
            string arrow = "";
            
            if (ImageLibrary != null)
            {
                _menuOpen.TryGetValue(player, out IsOpen);
                arrow = IsOpen ? 
                    ImageLibrary.Call<string>("GetImage", "Left") ?? "" : 
                    ImageLibrary.Call<string>("GetImage", "Right") ?? "";
            }

            UI.AddImage(ref container, "Overlay", _workLayer, "0 0 0 0", "", "", "0 1", "0 1", "7 -60", "173 -7", fadein, fadeout);
            UI.AddImage(ref container, _workLayer, "Arrow", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "140 -26", "166 0", fadein, fadeout);
            
            if (!string.IsNullOrEmpty(arrow))
            {
                UI.AddRawImage(ref container, "Arrow", "ArrowIMG", arrow, "1 1 1 1", "", "", "0 0", "1 1", "7 7", "-7 -7", fadein, fadeout);
            }
            
            UI.AddButton(ref container, "Arrow", "ArrowBTN", "labs.menu", "", "0 0 0 0", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);
           
            CuiHelper.AddUi(player, container);

            RefreshEvents(player, "All");
            if (IsOpen)
            {
                OnlineUi(player);
                ButtonUi(player);
            }

            if (LabsBonus != null && player != null && LabsBonus.Call<bool>("IsPromo", player))
            {
                LabsBonus.CallHook("DrawUI", player);
            }

            if (NoEscape != null && player != null && NoEscape.Call<bool>("IsBlocked", player))
            {
                var time = NoEscape.Call<double>("NoEscape_Time", player);
                RaidBlockUi(player, time);
            }
        }

        private void DestroyButtons(BasePlayer player)
        {
            foreach (var x in _config.MenuCmds)
            {
                CuiHelper.DestroyUi(player, _menuLayer + x.Value + 1);
                CuiHelper.DestroyUi(player, _menuLayer + x.Value);
            }
            CuiHelper.DestroyUi(player, _menuLayer);
        }

        private void ButtonUi(BasePlayer player)
        {
            CuiElementContainer container = new CuiElementContainer();

            DestroyButtons(player);

            UI.AddImage(ref container, "Overlay", _menuLayer, "0 0 0 0", "", "", "1 1", "1 1", "-300 -33", "-7 -7", fadein, fadeout);

            
            int minx = -25;
            int maxx = 0;

            foreach (var x in _config.MenuCmds)
            {
                UI.AddImage(ref container, _menuLayer, $"{_menuLayer + x.Value}", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "1 1", "1 1", $"{minx} -25", $"{maxx} 0", fadein, fadeout);
                UI.AddRawImage(ref container, $"{_menuLayer + x.Value}", $"{_menuLayer + x.Value + 1}", ImageLibrary?.Call<string>("GetImage", x.Value), $"1 1 1 1", "", "", "0 0", "1 1", $"3 3", $"-3 -3", fadein, fadeout);
                UI.AddButton(ref container, $"{_menuLayer + x.Value}", $"{_menuLayer + x.Value + 2}", $"chat.say {x.Value}", "", "0 0 0 0", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);

                minx -= 29;
                maxx -= 29;
            }

            CuiHelper.AddUi(player, container);
        }

        private void OnlineUi(BasePlayer player)
        {
            CuiElementContainer container = new CuiElementContainer();
            UI.AddImage(ref container, _workLayer, _workLayer + "OnlinePanel", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "0 -54", "166 -28", fadein, fadeout);
            UI.AddImage(ref container, _workLayer + "OnlinePanel", "OnlineIMGParent", $"0 0 0 0", "", "", "0 0", "0 0", "6 6", "22 22", fadein, fadeout);
            UI.AddRawImage(ref container, "OnlineIMGParent", "OnlineIMG", ImageLibrary.Call<string>("GetImage", "Online"), "1 1 1 1", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);
            UI.AddImage(ref container, _workLayer + "OnlinePanel", "OnlineColored", $"0 0 0 0", "", "", "0 0", "0 0", "28 4", "138 23", fadein, fadeout);
            float onlinebar;
            if (GetOnline() == 0)
            {
                onlinebar = Convert.ToSingle(BasePlayer.activePlayerList.Count) / Convert.ToSingle(ConVar.Server.maxplayers);
            }
            else
            {
                onlinebar = Convert.ToSingle(GetOnline() / Convert.ToSingle(ConVar.Server.maxplayers));
            }
            UI.AddImage(ref container, "OnlineColored", "OnlineBar", $"{HexToRustFormat(_config.BarColor)}", "", "", "0 0", $"{onlinebar} 1", "0 0", "0 0", fadein, fadeout);

            string online;

            if (GetOnline() == 0)
            {
                online = $"{BasePlayer.activePlayerList.Count}/{ConVar.Server.maxplayers}";
            }
            else
            {
                online = $"{GetOnline()}/{ConVar.Server.maxplayers}";
            }

            UI.AddText(ref container, "OnlineColored", "Online", "1 1 1 0.6", $"{online}", TextAnchor.MiddleLeft, 12, "0 0", "1 1", "5 3", "-1 -1", fadein: fadein, fadeout: fadeout);
            UI.AddImage(ref container, _workLayer + "OnlinePanel", "JoinBar", $"{HexToRustFormat(_config.JoinBarColor)}", "", "", "0 0", "0 0", "141 4", "163 23", fadein, fadeout);
            UI.AddText(ref container, "JoinBar", "Joins", "1 1 1 0.6", $"+{1 + ServerMgr.Instance.connectionQueue.Joining}", TextAnchor.MiddleCenter, 12, "0 0", "1 1", "0 1", "0 0", fadein:fadein, fadeout:fadeout);

            CuiHelper.DestroyUi(player, "JoinBar");
            CuiHelper.DestroyUi(player, "Joins");
            CuiHelper.DestroyUi(player, "OnlineBar");
            CuiHelper.DestroyUi(player, "Online");
            CuiHelper.DestroyUi(player, "OnlineColored");
            CuiHelper.DestroyUi(player, "OnlineIMG");
            CuiHelper.DestroyUi(player, _workLayer + "OnlinePanel");
            CuiHelper.AddUi(player, container);
        }


        private void RefreshEvents(BasePlayer player, string events)
        {
            CuiElementContainer container = new CuiElementContainer();

            switch (events)
            {
                case "All":
                    CuiHelper.DestroyUi(player, "CH47IMG");
                    CuiHelper.DestroyUi(player, _workLayer + "CH47");
                    CuiHelper.DestroyUi(player, "HeliIMG");
                    CuiHelper.DestroyUi(player, _workLayer + "Heli");
                    CuiHelper.DestroyUi(player, "CargoIMG");
                    CuiHelper.DestroyUi(player, _workLayer + "Cargo");
                    CuiHelper.DestroyUi(player, "BradleyIMG");
                    CuiHelper.DestroyUi(player, _workLayer + "Bradley");
                    CuiHelper.DestroyUi(player, "RigIMG");
                    CuiHelper.DestroyUi(player, "RigInfo");
                    CuiHelper.DestroyUi(player, _workLayer + "Rig");

                    UI.AddImage(ref container, _workLayer, _workLayer + "CH47", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "0 -26", "26 0", fadein, fadeout);
                    UI.AddRawImage(ref container, _workLayer + "CH47", "CH47IMG", ImageLibrary.Call<string>("GetImage", "CH47_labs"), _isCh47 ? $"{HexToRustFormat(_config.Ch47Color)}": "1 1 1 1", "", "", "0 0", "1 1", "5 5", "-5 -5", fadein, fadeout);
                    UI.AddImage(ref container, _workLayer, _workLayer + "Heli", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "28 -26", "54 0", fadein, fadeout);
                    UI.AddRawImage(ref container, _workLayer + "Heli", "HeliIMG", ImageLibrary.Call<string>("GetImage", "Heli_labs"), _isHeli ? $"{HexToRustFormat(_config.HeliColor)}" : "1 1 1 1", "", "", "0 0", "1 1", "5 5", "-5 -5", fadein, fadeout);
                    UI.AddImage(ref container, _workLayer, _workLayer + "Cargo", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "56 -26", "82 0", fadein, fadeout);
                    UI.AddRawImage(ref container, _workLayer + "Cargo", "CargoIMG", ImageLibrary.Call<string>("GetImage", "Cargo_labs"), _isCargoShip ? $"{HexToRustFormat(_config.CargoColor)}" : "1 1 1 1", "", "", "0 0", "1 1", "5 5", "-5 -5", fadein, fadeout);
                    UI.AddImage(ref container, _workLayer, _workLayer + "Bradley", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "84 -26", "110 0", fadein, fadeout);
                    UI.AddRawImage(ref container, _workLayer + "Bradley", "BradleyIMG", ImageLibrary.Call<string>("GetImage", "Bradley_labs"), _isBradley ? $"{HexToRustFormat(_config.BradleyColor)}" : "1 1 1 1", "", "", "0 0", "1 1", "5 5", "-5 -5", fadein, fadeout);
                    UI.AddImage(ref container, _workLayer, _workLayer + "Rig", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "112 -26", "138 0", fadein, fadeout);
                    UI.AddRawImage(ref container, _workLayer + "Rig", "RigIMG", ImageLibrary.Call<string>("GetImage", "Rig_labs"), _isBigOil || _isSmallOil ? $"{HexToRustFormat(_config.OilRigColor)}" : "1 1 1 1", "", "", "0 0", "1 1", "5 5", "-5 -5", fadein, fadeout);
                    UI.AddButton(ref container, _workLayer + "CH47", "CH47Info", "labs.info ch47", "", "0 0 0 0", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);
                    UI.AddButton(ref container, _workLayer + "Heli", "HeliInfo", "labs.info heli", "", "0 0 0 0", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);
                    UI.AddButton(ref container, _workLayer + "Cargo", "CargoInfo", "labs.info cargo", "", "0 0 0 0", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);
                    UI.AddButton(ref container, _workLayer + "Bradley", "BradleyInfo", "labs.info bradley", "", "0 0 0 0", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);
                    UI.AddButton(ref container, _workLayer + "Rig", "RigInfo", "labs.info oilrig", "", "0 0 0 0", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);
                    break;
                case "CH47":
                    CuiHelper.DestroyUi(player, "CH47IMG");
                    CuiHelper.DestroyUi(player, _workLayer + "CH47");

                    UI.AddImage(ref container, _workLayer, _workLayer + "CH47", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "0 -26", "26 0", fadein, fadeout);
                    UI.AddRawImage(ref container, _workLayer + "CH47", "CH47IMG", ImageLibrary.Call<string>("GetImage", "CH47_labs"), _isCh47 ? $"{HexToRustFormat(_config.Ch47Color)}" : "1 1 1 1", "", "", "0 0", "1 1", "5 5", "-5 -5", fadein, fadeout);
                    UI.AddButton(ref container, _workLayer + "CH47", "CH47Info", "labs.info ch47", "", "0 0 0 0", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);
                    break;
                case "Heli":
                    CuiHelper.DestroyUi(player, "HeliIMG");
                    CuiHelper.DestroyUi(player, _workLayer + "Heli");

                    UI.AddImage(ref container, _workLayer, _workLayer + "Heli", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "28 -26", "54 0", fadein, fadeout);
                    UI.AddRawImage(ref container, _workLayer + "Heli", "HeliIMG", ImageLibrary.Call<string>("GetImage", "Heli_labs"), _isHeli ? $"{HexToRustFormat(_config.HeliColor)}" : "1 1 1 1", "", "", "0 0", "1 1", "5 5", "-5 -5", fadein, fadeout);
                    UI.AddButton(ref container, _workLayer + "Heli", "HeliInfo", "labs.info heli", "", "0 0 0 0", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);
                    break;
                case "Cargo":
                    CuiHelper.DestroyUi(player, "CargoIMG");
                    CuiHelper.DestroyUi(player, _workLayer + "Cargo");

                    UI.AddImage(ref container, _workLayer, _workLayer + "Cargo", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "56 -26", "82 0", fadein, fadeout);
                    UI.AddRawImage(ref container, _workLayer + "Cargo", "CargoIMG", ImageLibrary.Call<string>("GetImage", "Cargo_labs"), _isCargoShip ? $"{HexToRustFormat(_config.CargoColor)}" : "1 1 1 1", "", "", "0 0", "1 1", "5 5", "-5 -5", fadein, fadeout);
                    UI.AddButton(ref container, _workLayer + "Cargo", "CargoInfo", "labs.info cargo", "", "0 0 0 0", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);
                    break;
                case "Bradley":
                    CuiHelper.DestroyUi(player, "BradleyIMG");
                    CuiHelper.DestroyUi(player, _workLayer + "Bradley");

                    UI.AddImage(ref container, _workLayer, _workLayer + "Bradley", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "84 -26", "110 0", fadein, fadeout);
                    UI.AddRawImage(ref container, _workLayer + "Bradley", "BradleyIMG", ImageLibrary.Call<string>("GetImage", "Bradley_labs"), _isBradley ? $"{HexToRustFormat(_config.BradleyColor)}" : "1 1 1 1", "", "", "0 0", "1 1", "5 5", "-5 -5", fadein, fadeout);
                    UI.AddButton(ref container, _workLayer + "Bradley", "BradleyInfo", "labs.info bradley", "", "0 0 0 0", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);
                    break;
                case "Rig":
                    CuiHelper.DestroyUi(player, "RigIMG");
                    CuiHelper.DestroyUi(player, "RigInfo");
                    CuiHelper.DestroyUi(player, _workLayer + "Rig");

                    UI.AddImage(ref container, _workLayer, _workLayer + "Rig", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "112 -26", "138 0", fadein, fadeout);
                    UI.AddRawImage(ref container, _workLayer + "Rig", "RigIMG", ImageLibrary.Call<string>("GetImage", "Rig_labs"), _isBigOil || _isSmallOil ? $"{HexToRustFormat(_config.OilRigColor)}" : "1 1 1 1", "", "", "0 0", "1 1", "5 5", "-5 -5", fadein, fadeout);
                    UI.AddButton(ref container, _workLayer + "Rig", "RigInfo", "labs.info oilrig", "", "0 0 0 0", "", "", "0 0", "1 1", "0 0", "0 0", fadein, fadeout);
                    break;
            }

            CuiHelper.AddUi(player, container);
        }

        private void InfoUi(BasePlayer player,string text)
        {
           
            CuiElementContainer container = new CuiElementContainer();
            CuiHelper.DestroyUi(player, "Info");
            CuiHelper.DestroyUi(player, _workLayer + "Info");

            UI.AddImage(ref container, _workLayer, _workLayer + "Info", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 1", "0 1", "168 -26", "324 0", fadein, fadeout);
            UI.AddText(ref container, _workLayer + "Info", "Info", "1 1 1 0.8", text, TextAnchor.MiddleCenter, 10, "0 0", "1 1", "0 0", "0 0", fadein:fadein, fadeout:fadeout);

            CuiHelper.AddUi(player, container);

            timer.Once(5f, () =>
            {
                CuiHelper.DestroyUi(player, "Info");
                CuiHelper.DestroyUi(player, _workLayer + "Info");
            });
        }

        [HookMethod("RaidBlockLabs")]
        public void RaidBlockUi(BasePlayer player, double time)
        {
            bool IsOpen = false;
            CuiElementContainer container = new CuiElementContainer();
            CuiHelper.DestroyUi(player, "Line");
            CuiHelper.DestroyUi(player, "InfoR");
            CuiHelper.DestroyUi(player, "RBPanel");
            if(_menuOpen.TryGetValue(player, out IsOpen) && IsOpen)
            {
                UI.AddImage(ref container, _workLayer, "RBPanel", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 0", "0 0", "0 -28", "166 -3", 0, 0);
            }
            else
            {
                UI.AddImage(ref container, _workLayer, "RBPanel", $"{HexToRustFormat("#C3C3C309")}", "", "assets/icons/greyout.mat", "0 0", "0 0", "0 0", "166 25", 0, 0);
            }
            UI.AddText(ref container, "RBPanel", "InfoR", "1 1 1 0.5", $"Вы в зоне рейдблока {GetFormatTime(TimeSpan.FromSeconds(time))}", TextAnchor.MiddleCenter, 12, "0 0", "1 1", "0 0", "0 3");
            UI.AddImage(ref container, "RBPanel", "Line", $"{HexToRustFormat("#C34D4D")}", "", "", "0 0", $"{time/_config.RaidBlockTime} 1", "0 0", "0 -21", 0, 0);

            CuiHelper.AddUi(player, container);
           
        }



        [HookMethod("IsOpen")]
        public bool IsOpened(BasePlayer player)
        {
            bool isOpen = false;
            _menuOpen.TryGetValue(player, out isOpen);
            return isOpen;
        }

        #endregion

        #region UI class
        public static class UI
        {
            public static void AddImage(ref CuiElementContainer container, string parrent, string name, string color, string sprite, string mat, string aMin, string aMax, string oMin, string oMax, float fadein, float fadeout, string outline = "", string dist = "")
            {
                if (string.IsNullOrEmpty(sprite) && !string.IsNullOrEmpty(mat))
                    container.Add(new CuiElement()
                    {
                        Parent = parrent,
                        Name = name,
                        FadeOut = fadeout,
                        Components =
                        {
                            new CuiImageComponent{Color = color, Material = "assets/icons/greyout.mat", FadeIn = fadein},
                            new CuiRectTransformComponent{AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax}
                        }
                    });

                if (string.IsNullOrEmpty(sprite) && string.IsNullOrEmpty(mat))
                    container.Add(new CuiElement()
                    {
                        Parent = parrent,
                        Name = name,
                        FadeOut = fadeout,
                        Components =
                    {
                        new CuiImageComponent{Color = color, FadeIn = fadein},
                        new CuiRectTransformComponent{AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax}
                    }
                    });
            }

            public static void AddRawImage(ref CuiElementContainer container, string parrent, string name, string png, string color, string sprite, string mat, string aMin, string aMax, string oMin, string oMax, float fadein, float fadeout)
            {
                if (string.IsNullOrEmpty(sprite) && string.IsNullOrEmpty(mat))
                    container.Add(new CuiElement()
                    {
                        Parent = parrent,
                        Name = name,
                        FadeOut = fadeout,
                        Components =
                    {
                        new CuiRawImageComponent{Color = color, Png = png, FadeIn = fadein},
                        new CuiOutlineComponent { Color = "0 0 0 0", Distance = "0 0"},
                        new CuiRectTransformComponent{AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax}
                    }
                    });
            }

            public static void AddText(ref CuiElementContainer container, string parrent, string name, string color, string text, TextAnchor align, int size, string aMin, string aMax, string oMin, string oMax, string outColor = "0 0 0 0", string font = "robotocondensed-bold.ttf", string dist = "0.5 0.5", float fadein = 0f, float fadeout = 0f)
            {
                container.Add(new CuiElement()
                {
                    Parent = parrent,
                    Name = name,
                    FadeOut = fadeout,
                    Components =
                    {
                        new CuiTextComponent{Color = color,Text = text, Align = align, FontSize = size, Font = font, FadeIn = fadein},
                        new CuiOutlineComponent{Color = outColor, Distance = dist},
                        new CuiRectTransformComponent{AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax}
                    }
                });

            }

            public static void AddButton(ref CuiElementContainer container, string parrent, string name, string cmd, string close, string color, string sprite, string mat, string aMin, string aMax, string oMin, string oMax, float fadein, float fadeout, string outline = "", string dist = "")
            {
                if (!string.IsNullOrEmpty(sprite) && !string.IsNullOrEmpty(mat))
                    container.Add(new CuiElement()
                    {
                        Parent = parrent,
                        Name = name,
                        FadeOut = fadeout,
                        Components =
                        {
                            new CuiButtonComponent{Command = cmd, Color = color, Close = close, Sprite = sprite, Material = "assets/icons/greyout.mat", FadeIn = fadein },
                            new CuiRectTransformComponent{AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax}
                        }
                    });

                if (!string.IsNullOrEmpty(sprite) && !string.IsNullOrEmpty(mat) && !string.IsNullOrEmpty(outline))
                    container.Add(new CuiElement()
                    {
                        Parent = parrent,
                        Name = name,
                        FadeOut = fadeout,
                        Components =
                        {
                            new CuiButtonComponent{Command = cmd, Color = color, Close = close, Sprite = sprite, Material = "assets/icons/greyout.mat", FadeIn = fadein },
                            new CuiOutlineComponent{Color = outline, Distance = dist},
                            new CuiRectTransformComponent{ AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax }
                        }
                    });

                if (string.IsNullOrEmpty(sprite) && !string.IsNullOrEmpty(mat))
                    container.Add(new CuiElement()
                    {
                        Parent = parrent,
                        Name = name,
                        FadeOut = fadeout,
                        Components =
                    {
                        new CuiButtonComponent{Command = cmd, Color = color, Close = close, Material = "assets/icons/greyout.mat", FadeIn = fadein },
                        new CuiRectTransformComponent{AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax}
                    }
                    });

                if (!string.IsNullOrEmpty(sprite) && string.IsNullOrEmpty(mat))
                    container.Add(new CuiElement()
                    {
                        Parent = parrent,
                        Name = name,
                        FadeOut = fadeout,
                        Components =
                    {
                        new CuiButtonComponent{Command = cmd, Color = color, Close = close, Sprite = sprite, FadeIn = fadein},
                        new CuiRectTransformComponent{AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax}
                    }
                    });

                if (string.IsNullOrEmpty(sprite) && string.IsNullOrEmpty(mat))
                    container.Add(new CuiElement()
                    {
                        Parent = parrent,
                        Name = name,
                        FadeOut = fadeout,
                        Components =
                    {
                        new CuiButtonComponent{Command = cmd, Color = color, Close = close, FadeIn = fadein },
                        new CuiRectTransformComponent{AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax}
                    }
                    });
            }
        }
        #endregion

        #region Helpers
        private static string HexToRustFormat(string hex)
        {
            if (string.IsNullOrEmpty(hex)) hex = "#FFFFFFFF";
            var str = hex.Trim('#');
            if (str.Length == 6) str += "FF";
            if (str.Length != 8)
            {
                throw new Exception(hex);
                throw new InvalidOperationException("Cannot convert a wrong format.");
            }
            var r = byte.Parse(str.Substring(0, 2), NumberStyles.HexNumber);
            var g = byte.Parse(str.Substring(2, 2), NumberStyles.HexNumber);
            var b = byte.Parse(str.Substring(4, 2), NumberStyles.HexNumber);
            var a = byte.Parse(str.Substring(6, 2), NumberStyles.HexNumber);
            Color color = new Color32(r, g, b, a);
            return string.Format("{0:F2} {1:F2} {2:F2} {3:F2}", color.r, color.g, color.b, color.a);
        }

        [ConsoleCommand("labs.menu")]
        private void MenuHandler(ConsoleSystem.Arg args)
        {
            BasePlayer player = args.Player();
            if (player == null) return;
            if (!_menuOpen.ContainsKey(player)) _menuOpen.Add(player, false);

            if (!_menuOpen[player])
            {
                _menuOpen[player] = true;
                DrawMenu(player);
                return;
            }
            if (_menuOpen[player])
            {
                _menuOpen[player] = false;
                DrawMenu(player);
                return;
            }
        }

        [ConsoleCommand("labs.info")]
        private void InfoHandler(ConsoleSystem.Arg args)
        {
            BasePlayer player = args.Player();
            if (player == null) return;

            switch (args.Args[0])
            {
                case "oilrig":
                    var text = string.Empty;
                    if (_isBigOil && _isSmallOil)
                    {
                        text = "2 из 2 нефтевышек доступно";
                    }
                    if (_isSmallOil && !_isBigOil)
                    {
                        text = "Маленькая нефтевышка доступна";
                    }
                    if (_isBigOil && !_isSmallOil)
                    {
                        text = "Большая нефтевышка доступна";
                    }
                    if (!_isBigOil && !_isSmallOil)
                    {
                        text = "0 из 2 нефтевышек доступно";
                    }
                    InfoUi(player, text);
                    break;
                case "ch47":
                    text = $"Следующий чинук через:\n{EventRandomizer?.Call<string>("Ch47Time")}";
                    if (_isCh47)
                        text = "Ивент активен";
                    InfoUi(player, text);
                    break;
                case "heli":
                    text = $"Следующий верт через:\n{EventRandomizer?.Call<string>("HeliTime")}";
                    if (_isHeli)
                        text = "Ивент активен";
                    InfoUi(player, text);
                    break;
                case "bradley":
                    text = $"Следующий танк через:\n{EventRandomizer?.Call<string>("BradleyTime")}";
                    if (_isBradley)
                        text = "Ивент активен";
                    InfoUi(player, text);
                    break;
                case "cargo":
                    text = $"Следующий карго через:\n{EventRandomizer?.Call<string>("CargoTime")}";
                    if (_isBradley)
                        text = "Ивент активен";
                    InfoUi(player, text);
                    break;
            }
           
        }

        private string GetFormatTime(TimeSpan timespan)
        {
            return string.Format(timespan.TotalHours >= 1 ? "{2:00}:{0:00}:{1:00}" : "{0:00}:{1:00}", timespan.Minutes, timespan.Seconds, System.Math.Floor(timespan.TotalHours));
        }

        #endregion
    }

}
