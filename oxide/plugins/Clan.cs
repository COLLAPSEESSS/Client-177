using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using ProtoBuf;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Clan", "Frizen", "1.0.0")]
    public class Clan : RustPlugin
    {
        #region Variables


        [PluginReference] private Plugin ImageLibrary = null;
        
        private string[] _gatherHooks = {
            "OnDispenserGather",
            "OnDispenserBonus",
        };
        
        private static Clan Instance;

        private static FieldInfo hookSubscriptions = 
            typeof(PluginManager).GetField("hookSubscriptions", (BindingFlags.Instance | BindingFlags.NonPublic));

        private List<ulong> _lootEntity = new List<ulong>();
        private Dictionary<string, int> ItemID = new Dictionary<string, int>();

        private const string Layer = "UI_LayerClan";

        #endregion
        
        #region Data

        public List<ClanData> _clanList = new List<ClanData>();

        public class ClanData
        {
            #region Member

            public class MemberData
            {
                public int MemberScores = 0;

                public int MemberKill = 0;
                
                public int MemberDeath = 0;

                public bool MemberFriendlyFire = false;

                public float AliveTime;

                public string LastTime;


                public Dictionary<string, int> GatherMember = new Dictionary<string, int>();
            }
            
            
            #endregion
            
            #region Gather

            public class GatherData
            {
                public int TotalFarm;

                public int Need;
            }

            #endregion
            
            #region Variables

            public string ClanTag;

            public string ImageAvatar;

            public ulong  LeaderUlongID;

            public string Task;

            public int    TotalScores;

            public ulong  TeamIDCreate;

            // 1.1.1
            

            public int    Turret;

            public int    SamSite;

            public int    Cupboard;

            public int    Raid;
            
            //

            public Dictionary<ulong, MemberData> Members 
                = new Dictionary<ulong, MemberData>();
            
            public Dictionary<string, ulong> ItemList
                = new Dictionary<string, ulong>();

            public Dictionary<string, GatherData> GatherList
                = new Dictionary<string, GatherData>();

            public List<ulong> Moderators = new List<ulong>();

            public List<ulong> PendingInvites
                = new List<ulong>();


            [JsonIgnore]
            private RelationshipManager.PlayerTeam Team =>
                RelationshipManager.Instance.FindTeam(TeamIDCreate) ?? FindOrCreateTeam();

            #endregion

            #region ClanTeam

            
            public RelationshipManager.PlayerTeam FindOrCreateTeam()
            {
                var leaderTeam = RelationshipManager.Instance.FindPlayersTeam(LeaderUlongID);
                if (leaderTeam != null)
                {
                    if (leaderTeam.teamLeader == LeaderUlongID)
                    {
                        TeamIDCreate = leaderTeam.teamID;
                        return leaderTeam;
                    }

                    leaderTeam.RemovePlayer(LeaderUlongID);
                }

                return CreateTeam();
            }

            private RelationshipManager.PlayerTeam CreateTeam()
            {
                var team = RelationshipManager.Instance.CreateTeam();
                team.teamLeader = LeaderUlongID;
                AddPlayer(LeaderUlongID, team);

                TeamIDCreate = team.teamID;

                return team;
            }
            
            public void AddPlayer(ulong member, RelationshipManager.PlayerTeam team = null)
            {
                if (team == null)
                    team = Team;

                if (!team.members.Contains(member))
                    team.members.Add(member);

                if (member == LeaderUlongID)
                    team.teamLeader = LeaderUlongID;

                RelationshipManager.Instance.playerToTeam[member] = team;

                var player = RelationshipManager.FindByID(member);
                if (player != null)
                {
                    if (player.Team != null && player.Team.teamID != team.teamID)
                    {
                        player.Team.RemovePlayer(player.userID);
                        player.ClearTeam();
                    }

                    player.currentTeam = team.teamID;

                    team.MarkDirty();
                    player.SendNetworkUpdate();
                }
            }



            #endregion
            
            #region AddPlayer


            public void InvitePlayer(BasePlayer player)
            {
                Members.Add(player.userID, new MemberData
                {
                    GatherMember = config.Main.GatherDictionary.ToDictionary(x => x, p => 0),
                });

                
                if (config.Main.EnableTeam)
                {
                    if (player.Team != null)
                        player.Team.RemovePlayer(player.userID);
                    
                    if (Team != null)
                        Team.AddPlayer(player);
                }
                
                if (config.Main.TagInPlayer)
                    player.displayName = $"[{ClanTag}] {player.displayName}";

                
                if (PendingInvites.Contains(player.userID))
                    PendingInvites.Remove(player.userID);
                

                Interface.CallHook("OnClanUpdate", ClanTag, Members.Keys.ToList());
            }

            #endregion

            #region RemovePlayer


            public void RemovePlayerInClan(ulong player)
            {
                Members.Remove(player);
                
                if (Moderators.Contains(player))
                    Moderators.Remove(player);

                if (config.Main.TagInPlayer)
                {
                    var target = BasePlayer.Find(player.ToString());
                    if (target != null)
                        target.displayName = target.IPlayer.Name;
                }

                if (config.Main.EnableTeam)
                {
                    if (Team != null)
                        Team.RemovePlayer(player);
                }
                
                Interface.CallHook("OnClanUpdate", ClanTag, Members.Keys.ToList());
                
            }
            

            #endregion

            #region Disband

            public void Disband()
            {
                Interface.CallHook("OnClanDestroy", ClanTag);

                var listMember = Members.Keys.ToList();
                
                listMember.ForEach(p => RemovePlayerInClan(p));

                
                

                Instance._clanList.Remove(this);
            }

            #endregion

            #region SetOwner


            public void SetOwner(ulong playerID)
            {
                Interface.CallHook("OnClanOwnerChanged", ClanTag, LeaderUlongID, playerID);
                
                LeaderUlongID = playerID;

                ImageAvatar = $"avatar_{playerID}";
                
                if (config.Main.EnableTeam && Team != null)
                    Team.SetTeamLeader(playerID);
                

            }

            #endregion
            
            #region Functional Info

            public bool IsOwner(ulong playerID) => LeaderUlongID == playerID;

            public bool IsModerator(ulong playerID) => Moderators.Contains(playerID) || IsOwner(playerID);

            public bool IsMember(ulong playerID) => Members.ContainsKey(playerID);

            #endregion

            #region Gather
            
            public void ProcessingGather(BasePlayer player, Item item, bool bonus = false)
            {
                if (!GatherList.ContainsKey(item.info.shortname)) return;

                var getGather = GatherList[item.info.shortname];

                if (getGather.TotalFarm < getGather.Need)
                {
                    getGather.TotalFarm += item.amount;
                    
                    if (getGather.TotalFarm > getGather.Need)
                        getGather.TotalFarm = getGather.Need;
                }

                if (!Members[player.userID].GatherMember.ContainsKey(item.info.shortname))
                    Members[player.userID].GatherMember.Add(item.info.shortname, item.amount);
                else Members[player.userID].GatherMember[item.info.shortname] += item.amount;

                if (bonus)
                {
                    int ScoresGive;
                    
                    if (config.Point._gatherPoint.TryGetValue(item.info.shortname, out ScoresGive))
                    {
                        TotalScores += ScoresGive;
                        Members[player.userID].MemberScores += ScoresGive;
                    }
                }
            }

            #endregion

            #region GiveScores


            public void GiveScores(BasePlayer player, int scores)
            {
                TotalScores += scores;
                Members[player.userID].MemberScores += scores;
            }


            #endregion

            #region FF

            public void ChangeFriendlyFire(BasePlayer player)
            {
                var memberData = Members[player.userID];

                if (memberData.MemberFriendlyFire == true)
                    memberData.MemberFriendlyFire = false;
                else memberData.MemberFriendlyFire = true;
            }


            public bool GetValueFriendlyFire(BasePlayer player) => Members[player.userID].MemberFriendlyFire;
            
            
            #endregion

            #region Information


            [JsonIgnore]
            public Dictionary<string, string> GetInformation
            {
                get { return new Dictionary<string, string> { ["НАЗВАНИЕ КЛАНА:"] = ClanTag, ["ГЛАВА КЛАНА:"] = Instance.covalence.Players.FindPlayer(LeaderUlongID.ToString()).Name.ToUpper(), ["УЧАСТНИКОВ В ИГРЕ:"] = $"{Members.Keys.Select(p => BasePlayer.Find(p.ToString()) != null).Count()} ИЗ {Members.Count}", ["ОБЩЕЕ КОЛИЧЕСТВО ОЧКОВ:"] = $"{TotalScores}", }; }
            }

            [JsonIgnore]
            public Dictionary<string, string> GetInformationInfo
            {
                get { int totalPercent = GatherList.Sum(p => p.Value.TotalFarm) * 100 / GatherList.Sum(p => p.Value.Need > 0 ? p.Value.Need : 1); if (totalPercent > 100) totalPercent = 100; return new Dictionary<string, string> { ["ГЛАВА КЛАНА:"] = Instance.covalence.Players.FindPlayer(LeaderUlongID.ToString()).Name, ["ИГРОКОВ В КЛАНЕ:"] = $"{Members.Count}", ["НАБРАНО ОЧКОВ:"] = $"{TotalScores}", ["ОБЩАЯ АКТИВНОСТЬ:"] = $"{totalPercent}%", ["ВСЕГО УБИЙСТВ:"] = $"{Members.Sum(p => p.Value.MemberKill)}", ["ВСЕГО СМЕРТЕЙ:"] = $"{Members.Sum(p => p.Value.MemberDeath)}", }; }
            }

            public int TotalAmountFarm(ulong playerID) => Members[playerID].GatherMember.Sum(p => p.Value) * 100 / GatherList.Sum(p => p.Value.Need > 0 ? p.Value.Need : 1);
            
            
            #endregion

            #region JObject

            internal JObject ToJObject()
            {
                var obj = new JObject();
                obj["tag"] = ClanTag;
                obj["owner"] = LeaderUlongID;
                var jmoderators = new JArray();
                foreach (var moderator in Moderators) jmoderators.Add(moderator);
                obj["moderators"] = jmoderators;
                var jmembers = new JArray();
                foreach (var member in Members) jmembers.Add(member.Key);
                obj["members"] = jmembers;
                return obj;
            }

            #endregion
            
        }

        #endregion

        #region Hooks
        
        #region Loaded
        
        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin.Title == "Better Chat" || plugin.Title == "ChatPlus")
            {
                Interface.CallHook("API_RegisterThirdPartyTitle", this, new Func<IPlayer, string>(getFormattedClanTag));  
            }
            
            NextTick(() =>
            {
                
                foreach (string hook in _gatherHooks)
                {
                    Unsubscribe(hook);
                    Subscribe(hook);
                }
            });
        }
        
        #endregion
        
#region Initialized

void OnServerInitialized()
{
    try
    {
        Instance = this;

        // Load data and messages
        LoadData();
        LoadMessages();

        // Subscribe to internal hooks
        SubscribeInternalHookSafe("IOnBasePlayerHurt");

        // Add covalence command
        AddCovalenceCommand(config.Main.CommandsMain.ToArray(), nameof(ClanCmd));

        // Conditional unsubscriptions based on config
        if (!config.Stats.Gather)
        {
            SafeUnsubscribe(nameof(OnDispenserGather));
            SafeUnsubscribe(nameof(OnDispenserBonus));
        }

        if (!config.Stats.Entities && !config.Limit.EnableLimit)
        {
            SafeUnsubscribe(nameof(OnEntityDeath));
        }

        if (!config.Stats.Loot)
        {
            SafeUnsubscribe(nameof(OnLootEntity));
        }

        if (!config.Main.AutomaticCupboard && !config.Limit.EnableLimit)
        {
            SafeUnsubscribe(nameof(OnEntityBuilt));
        }

        if (!config.Main.AutomaticLock)
        {
            SafeUnsubscribe(nameof(CanUseLockedEntity));
        }

        if (!config.Main.AutomaticTurret)
        {
            SafeUnsubscribe(nameof(OnTurretTarget));
        }

        // Load images with ImageLibrary plugin
        SafeCallImageLibrary("https://i.imgur.com/J5l4FWq.png", "lootbox");
        SafeCallImageLibrary("https://i.imgur.com/HVkdiY0.png", "GradientGreen");
        SafeCallImageLibrary("https://i.imgur.com/DO5rXKC.png", "ClanInvite");

        // Populate ItemID dictionary
        foreach (var key in ItemManager.GetItemDefinitions())
        {
            if (!ItemID.ContainsKey(key.shortname))
            {
                ItemID.Add(key.shortname, key.itemid);
            }
        }

        // Trigger OnPlayerConnected for active players
        foreach (var player in BasePlayer.activePlayerList)
        {
            OnPlayerConnected(player);
        }
    }
    catch (Exception ex)
    {
       // Puts($"[Error] Failed to initialize server: {ex.Message}");
        Interface.Oxide.LogException("OnServerInitialized error", ex);
    }
}

void SubscribeInternalHookSafe(string hookName)
{
    try
    {
        SubscribeInternalHook(hookName);
    }
    catch (Exception ex)
    {
      //  Puts($"[Warning] Failed to subscribe to internal hook '{hookName}': {ex.Message}");
    }
}

void SafeUnsubscribe(string hookName)
{
    try
    {
        Unsubscribe(hookName);
    }
    catch (Exception ex)
    {
      //  Puts($"[Warning] Failed to unsubscribe from hook '{hookName}': {ex.Message}");
    }
}

void SafeCallImageLibrary(string url, string alias)
{
    try
    {
        ImageLibrary?.Call("AddImage", url, alias);
    }
    catch (Exception ex)
    {
      //  Puts($"[Warning] Failed to add image '{alias}' from URL '{url}': {ex.Message}");
    }
}





        #endregion
        
        #region Unload

        void Unload()
        {

            foreach (var tPlayer in BasePlayer.activePlayerList)
            {
                OnPlayerDisconnected(tPlayer);
            }

            Interface.GetMod().DataFileSystem.WriteObject(Name, _clanList);

            Instance = null;
            config = null;
        }

        #endregion
        
        #region Wipe

        void OnNewSave()
        {
            LoadData();
            
            // Даем время на инициализацию даты-файла

            timer.Once(3, () =>
            {
                
                if (config.Prize.EnablePrize)
                {
                    if (config.Prize.GSSettings.SecretKey == "UNDEFINED" || config.Prize.GSSettings.ShopID == "UNDEFINED") return;

                    int index = 1;

                    foreach (var clan in _clanList.OrderByDescending(p => p.TotalScores))
                    {
                        uint amount = 0;
                        if (config.Prize.RewardDictionary.TryGetValue(index, out amount))
                        {
                            foreach (var clanMember in clan.Members)
                            {
                                Request($"&action=moneys&type=plus&steam_id={clanMember.Key}&amount={amount}", null);
                            }
                        }
                        index++;
                    }
                }
                
                if (!config.Main.ClearWipe) return;

                _clanList.Clear();
            
                Interface.GetMod().DataFileSystem.WriteObject(Name, _clanList);
                
            });

            
        }

        #endregion

        #region ServerSave

        void OnServerSave() => timer.Once(5, () => Interface.GetMod().DataFileSystem.WriteObject(Name, _clanList));

        #endregion

        #region PlayerConnected

        void OnPlayerConnected(BasePlayer player)
        {
            var clan = FindClanByUser(player.userID);

            if (clan != null)
            {
                if (config.Main.TagInPlayer)
                    player.displayName = $"[{clan.ClanTag}] {GetNamePlayer(player)}";
                
                if (config.Main.EnableTeam)
                    clan.AddPlayer(player.userID);

                clan.Members[player.userID].LastTime = $"{DateTime.Now.ToString("t")} {DateTime.Now.Day}/{DateTime.Now.Month}/{DateTime.Now.Year}";
                
                player.SendNetworkUpdateImmediate();
            }
            
            GetAvatar(player.userID,
                avatar => ImageLibrary?.Call("AddImage", avatar, $"avatar_{player.UserIDString}"));
            
        }

        #endregion

        #region PlayerDisconnected

        void OnPlayerDisconnected(BasePlayer player)
        {
           
            var clan = FindClanByUser(player.userID);
            
            if (clan != null)
            {

                if (config.Main.TagInPlayer)
                    player.displayName = player.IPlayer.Name;
            }
            
        }

        #endregion

        #region Gather


        void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            BasePlayer player = entity.ToPlayer();
            if (player == null) return;

            FindClanByUser(player.userID)?.ProcessingGather(player, item);
        }

        void OnDispenserBonus(ResourceDispenser disp, BasePlayer player, Item item)
        {
            if (player == null) return;

            FindClanByUser(player.userID)?.ProcessingGather(player, item, true);
            
        }



        #endregion

        #region Limit

        
        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null) return;
            if (info == null) return;
            
            if (entity.OwnerID == 0) return;
            
            if (!(entity as AutoTurret) || !(entity as BuildingPrivlidge) || !(entity as SamSite)) return;


            var findEntityClan = FindClanByUser(entity.OwnerID);
            var findAttackerClan = FindClanByUser(info.InitiatorPlayer.userID);
            
            if (findEntityClan == null) return;
            if (findAttackerClan == null) return;

            if (entity as AutoTurret)
            {
                findEntityClan.Turret--;
            }
            else if (entity as BuildingPrivlidge)
            {
                if(findAttackerClan != findEntityClan)
                {
                    findAttackerClan.Raid++;
                }
                findEntityClan.Cupboard--;
            }
            else if (entity as SamSite)
            {
                findEntityClan.SamSite--;
            }

        }
        

        #endregion

        #region BradleyAPC
        
        
        

        void OnEntityDeath(BradleyAPC entity, HitInfo info)
        {
            if (entity == null) return;
            if (info == null) return;
            
            if (info.InitiatorPlayer == null) return;

            var player = info.InitiatorPlayer;

            var clan = FindClanByUser(player.userID);
            if (clan == null) return;
            
            clan.GiveScores(player, config.Point.BradleyAPC);
        }

        #endregion

        #region Helicopter
        
        private readonly Dictionary<ulong, BasePlayer> _lastHeli = new Dictionary<ulong, BasePlayer>();

        private void OnEntityTakeDamage(BaseHelicopter entity, HitInfo info)
        {
            if (entity == null) return;
            if (info == null) return;

            if (info.InitiatorPlayer == null) return;
            
            _lastHeli[entity.net.ID] = info.InitiatorPlayer;

            
        }

        void OnEntityDeath(BaseHelicopter entity, HitInfo info)
        {
            if (entity == null) return;
            if (info == null) return;

            if (_lastHeli.ContainsKey(entity.net.ID))
            {
                var basePlayer = _lastHeli[entity.net.ID];
                if (basePlayer != null)
                {
                    var clan = FindClanByUser(basePlayer.userID);
                    if (clan == null) return;
                    
                    clan.GiveScores(basePlayer, config.Point.Helicopter);
                    
                }
            }
        }
        

        #endregion
        
        #region Barrel

        private void OnEntityDeath(LootContainer entity, HitInfo info)
        {
            if (entity == null) return;
            if (info == null) return;
            
            if (!entity.ShortPrefabName.Contains("barrel")) return;
            
            if (!config.Point._gatherPoint.ContainsKey("lootbox")) return;

            if (info.InitiatorPlayer == null) return;
            var player = info.InitiatorPlayer;

            var clan = FindClanByUser(player.userID);
            if (clan == null) return;
            

            clan.GiveScores(player, config.Point._gatherPoint["lootbox"]);

            clan.GatherList["lootbox"].TotalFarm++;
            clan.Members[player.userID].GatherMember["lootbox"]++;

        }

        #endregion

        #region Loot

        private void OnLootEntity(BasePlayer player, LootContainer entity)
        {
            if (player == null || entity == null) return;
            if (_lootEntity.Contains(entity.net.ID)) return;
            
            if (!config.Point._gatherPoint.ContainsKey("lootbox")) return;

            var clan = FindClanByUser(player.userID);
            if (clan == null) return;
            
            clan.GiveScores(player, config.Point._gatherPoint["lootbox"]);

            clan.GatherList["lootbox"].TotalFarm++;
            clan.Members[player.userID].GatherMember["lootbox"]++;
            
            _lootEntity.Add(entity.net.ID);
        }

        #endregion

        #region Kill && Death && Suicide


        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null || info == null || !player.userID.IsSteamId()) return;
            
            if (info.damageTypes.Has(DamageType.Suicide))
            {
                var clan = FindClanByUser(player.userID);
                if (clan == null) return;
                
                clan.TotalScores -= config.Point.Suicide;
                var member = clan.Members[player.userID];

                member.MemberDeath++;
                member.MemberScores -= config.Point.Suicide;
                
                return;
            }
            
            var attacker = info.InitiatorPlayer;

            if (attacker == null || !attacker.userID.IsSteamId() || FindClanByUser(player.userID)?.IsMember(attacker.userID) == true) return;

            if (player.userID.IsSteamId())
            {
                var clan = FindClanByUser(player.userID);
                if (clan != null)
                {
                    clan.TotalScores -= config.Point.Death;
                    var member = clan.Members[player.userID];

                    member.MemberDeath++;
                    member.MemberScores -= config.Point.Death;
                }

                var clanAttacker = FindClanByUser(attacker.userID);

                if (clanAttacker != null)
                {
                    clanAttacker.TotalScores += config.Point.Kill;
                    var member = clanAttacker.Members[attacker.userID];

                    member.MemberKill++;
                    member.MemberScores += config.Point.Kill;
                }
            }

        }

        #endregion

        #region Damage


        private object IOnBasePlayerHurt(BasePlayer player, HitInfo info)
        {
            if (player == null) return null;
            if (info == null) return null;

            if (info.InitiatorPlayer == null) return null;

            var initiator = info.InitiatorPlayer;
            if (player == initiator) return null;

            var clan = FindClanByUser(player.userID);
            if (clan == null) return null;

            if (clan.IsMember(initiator.userID) && !clan.GetValueFriendlyFire(initiator))
            {
                if (initiator.SecondsSinceAttacked > 5)
                {
                    initiator.ChatMessage(GetLang("ClanFFActive", initiator.UserIDString));

                    initiator.lastAttackedTime = UnityEngine.Time.time;
                }
                
                DisableDamage(info);

                return false;
            }

            return null;
        }


        #endregion

        #region Auth

        void OnEntityBuilt(Planner plan, GameObject go)
        {
            if (plan == null || go == null) return;
            var player = plan.GetOwnerPlayer();
            if (player == null) return;

            BaseEntity entity = go.ToBaseEntity();
            if (entity == null) return;


            var clan = FindClanByUser(player.userID);
            if (clan == null) return;
            

            if (entity.GetComponent<BuildingPrivlidge>() != null)
            {
                
            
                var cup = entity.GetComponent<BuildingPrivlidge>();

                if (config.Limit.EnableLimit)
                {
                    if (clan.Cupboard >= config.Limit.LCupboard)
                    {
                        player.GetActiveItem().amount++;
                        NextTick(() => entity.Kill());
                    }
                    else 
                        clan.Cupboard++;
                    
                    
                    player.ChatMessage(GetLang("Cupboard", player.UserIDString, config.Limit.LCupboard - clan.Cupboard, config.Limit.LCupboard));
                }

                if (config.Main.AutomaticCupboard && entity != null)
                {
                    foreach (var member in clan.Members)
                    {
                        cup.authorizedPlayers.Add(new PlayerNameID
                        {
                            userid = member.Key,
                            username = ""
                        });
                    }
                }

                return;
            }

            if (entity.GetComponent<AutoTurret>() != null)
            {
                if (config.Limit.EnableLimit)
                {
                    if (clan.Turret >= config.Limit.LTurret)
                    {
                        player.GetActiveItem().amount++;
                        NextTick(() => entity.Kill());
                    }
                    else 
                        clan.Turret++;
                    
                    
                    player.ChatMessage(GetLang("Turret", player.UserIDString, config.Limit.LTurret - clan.Turret, config.Limit.LTurret));
                }
                
                return;
            }
            
            if (entity.GetComponent<SamSite>() != null)
            {
                if (config.Limit.EnableLimit)
                {
                    if (clan.SamSite >= config.Limit.LSamSite)
                    {
                        player.GetActiveItem().amount++;
                        NextTick(() => entity.Kill());
                    }
                    else 
                        clan.SamSite++;
                    
                    
                    player.ChatMessage(GetLang("SamSite", player.UserIDString, config.Limit.LSamSite - clan.SamSite, config.Limit.LSamSite));
                }
                
                return;
            }
        }
        
        object CanUseLockedEntity(BasePlayer player, CodeLock baseLock)
        {
            if (player == null || baseLock == null) return null;
            if (baseLock.OwnerID == 0 || baseLock.OwnerID == player.userID) return null;
            if (baseLock.whitelistPlayers == null) return null;
            if (baseLock.whitelistPlayers.Contains(player.userID)) return null;

            string ownerID = baseLock.OwnerID.ToString();
            string playerID = player.userID.ToString();
            
            if (string.IsNullOrEmpty(ownerID) || string.IsNullOrEmpty(playerID)) return null;

            var result = IsClanMember(ownerID, playerID);
            return result == null ? null : (bool)result;
        }
        
        object OnTurretTarget(AutoTurret turret, BasePlayer player)
        {
            if (turret == null) return null;
            if (player == null) return null;

            if (turret.OwnerID == 0) return null;

            //if (turret.IsAuthed(player)) return null;

            if ((bool)IsClanMember(turret.OwnerID.ToString(), player.userID.ToString()))
            {
                return false;
            }

            return null;
        }

        object OnTeamAcceptInvite(RelationshipManager.PlayerTeam team, BasePlayer player)
        {
            var findClan = FindClanByUser(player.userID);
            if (findClan != null) return false;
            
            var teamLeader = team.GetLeader();

            if (teamLeader == null) return null;
            var findClanLeader = FindClanByUser(teamLeader.userID);

            if (findClanLeader == null) return null;
            
            if (!findClanLeader.IsOwner(teamLeader.userID)) return false;
            
            findClanLeader.InvitePlayer(player);
            
            
            return null;
        }

        object OnTeamInvite(BasePlayer inviter, BasePlayer target)
        {
            var findClan = FindClanByUser(inviter.userID);
            if (findClan == null) return null;

            if (!findClan.IsOwner(inviter.userID)) return false;
            
            var findClanTarget = FindClanByUser(target.userID);
            if (findClanTarget != null) return false;

            return null;
        }

        object OnTeamKick(RelationshipManager.PlayerTeam team, BasePlayer kicker, ulong targetID)
        {
            var findClan = FindClanByUser(kicker.userID);
            
            if (findClan == null) return null;
            
            if (findClan.IsOwner(kicker.userID))
            {
                findClan.RemovePlayerInClan(targetID);
            }

            return null;
            
        }


        void OnTeamLeave(RelationshipManager.PlayerTeam team, BasePlayer player)
        {
            var findClan = FindClanByUser(player.userID);
            if (findClan == null) return;
            
            if (findClan.TeamIDCreate != team.teamID) return;

            if (findClan.IsOwner(player.userID))
            {
                findClan.Disband();
                
                team.Disband();
                
            }
            else
            {
                findClan.RemovePlayerInClan(player.userID);
            }
        }

        #endregion

        #endregion

        #region UI

        private void MainUI(BasePlayer player, string active = "MyClan")
        {
            var container = new CuiElementContainer();
            CuiHelper.DestroyUi(player, "MainLayer4");

            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                Image = { Color = "0 0 0 0.5", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" }
            }, "Overlay", "MainLayer4");

            container.Add(new CuiElement
            {
                Parent = "MainLayer4",
                Name = "BG4",
                Components =
                {
                    new CuiRawImageComponent{Color = "1 1 1 1", Png = ImageLibrary.Call<string>("GetImage", "Viniette")},
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });


            container.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax= "1 1"
                },
                Text =
                {
                    Text = "",
                },
                Button =
                {
                    Color = "0 0 0 0",
                    Close = "MainLayer4",
                }
            }, "BG4");


            container.Add(new CuiPanel
            {
                RectTransform =
                {
                    AnchorMin = active != "Invite" ? "0.3708333 0.3351852" : "0.3703125 0.337963",
                    AnchorMax = active != "Invite" ? "0.6291667 0.6657407" : "0.6286459 0.6685184"
                },
                Image =
                {
                    Color = "0 0 0 0"
                }
            }, "BG4", Layer);

            switch (active)
            {
                case "MyClan":
                    container.Add(new CuiButton
                    {
                        RectTransform =
                        {
                            AnchorMin = "1.192093E-07 0.859944",
                            AnchorMax= "0.4919355 0.9971989"
                        },
                        Button =
                        {
                            Color = active == "ClanTop" ? $"{HexToRustFormat("#B6B6B680")}" : $"{HexToRustFormat("#B6B6B640")}",
                            Command = $"UI_ClanHandler clantop",
                        },
                        Text =
                        {
                            Text = $"Топ кланов",
                            Color = active == "ClanTop" ? $"{HexToRustFormat("#464646CE")}" : $"1 1 1 0.8",
                            Align = TextAnchor.MiddleCenter,
                            FontSize = 12,
                            Font = "robotocondensed-bold.ttf"
                        }
                    }, Layer, "ClanTop");

                    container.Add(new CuiButton
                    {
                        RectTransform =
                        {
                            AnchorMin = "0.5077281 0.859944",
                            AnchorMax= "1 0.9971989"
                        },
                        Button =
                        {
                            Color = active == "MyClan" ? $"{HexToRustFormat("#B6B6B680")}" : $"{HexToRustFormat("#B6B6B640")}",
                            Command = $"UI_ClanHandler myclan",
                        },
                        Text =
                        {
                            Text = "Мой клан",
                            Color = active == "MyClan" ? $"{HexToRustFormat("#464646CE")}" : $"1 1 1 0.8",
                            Align = TextAnchor.MiddleCenter,
                            FontSize = 12,
                            Font = "robotocondensed-bold.ttf"
                        }
                    }, Layer, "MyClan");

                    var clan = _clanList.FirstOrDefault(x => x.Members.ContainsKey(player.userID));

                    if (clan != null)
                    {
                        container.Add(new CuiPanel
                        {
                            RectTransform =
                                    {
                                        AnchorMin = $"2.57045E-07 0.7619048",
                                        AnchorMax = $"1 0.837535"
                                    },
                            Image =
                                    {
                                        Color = $"{HexToRustFormat("#B6B6B62E")}",
                                    }
                        }, Layer, $"Panel");

                        container.Add(new CuiElement
                        {
                            Parent = "Panel",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = "#", Align = TextAnchor.MiddleCenter, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.01982528 0.0925926", AnchorMax = "0.07190865 0.8888896"}
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = "Panel",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = "Игрок", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.1286962 0.1296297", AnchorMax = "0.2278225 0.9259268"}
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = "Panel",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = "K/D", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.7113569 0.1296297", AnchorMax = "0.8104832 0.9259268"}
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = "Panel",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = "Очки", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.902889 0.1296297", AnchorMax = "1.002015 0.9259268"}
                            }
                        });


                        double totalfarm = 0;

                        foreach (var item in clan.GatherList)
                        {
                            totalfarm += item.Value.TotalFarm;
                        }

                        container.Add(new CuiPanel
                        {
                            RectTransform =
                                    {
                                        AnchorMin = $"1.15484E-07 -0.005602248",
                                        AnchorMax = $"0.1915323 0.1316526"
                                    },
                            Image =
                                    {
                                        Color = $"{HexToRustFormat("#B6B6B62E")}",
                                    }
                        }, Layer, $"InfoPanel1");

                        container.Add(new CuiElement
                        {
                            Parent = "InfoPanel1",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9A2")}", Text = "Добыто", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.1316288 0.480348", AnchorMax = "1.03409 1"}
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = "InfoPanel1",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = $"{totalfarm}", Align = TextAnchor.MiddleLeft, FontSize = 12, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.1316288 0.05177624", AnchorMax = "1.03409 0.5714285"}
                            }
                        });

                        container.Add(new CuiPanel
                        {
                            RectTransform =
                                    {
                                        AnchorMin = $"0.2076612 -0.005602248",
                                        AnchorMax = $"0.3850806 0.1316526"
                                    },
                            Image =
                                    {
                                        Color = $"{HexToRustFormat("#B6B6B62E")}",
                                    }
                        }, Layer, $"InfoPanel2");

                        container.Add(new CuiElement
                        {
                            Parent = "InfoPanel2",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9A2")}", Text = "Взорвано", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.1316288 0.480348", AnchorMax = "1.03409 1"}
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = "InfoPanel2",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = $"{clan.Raid}", Align = TextAnchor.MiddleLeft, FontSize = 12, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.1316288 0.05177624", AnchorMax = "1.03409 0.5714285"}
                            }
                        });

                        container.Add(new CuiPanel
                        {
                            RectTransform =
                                    {
                                        AnchorMin = $"0.3991933 -0.005602248",
                                        AnchorMax = $"0.5826611 0.1316526"
                                    },
                            Image =
                                    {
                                        Color = $"{HexToRustFormat("#B6B6B62E")}",
                                    }
                        }, Layer, $"InfoPanel3");

                        container.Add(new CuiElement
                        {
                            Parent = "InfoPanel3",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9A2")}", Text = "Убито", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.1316288 0.480348", AnchorMax = "1.03409 1"}
                            }
                        });

                        double kills = 0, deaths = 0;

                        foreach (var item in clan.Members)
                        {
                            kills += item.Value.MemberKill;
                            deaths += item.Value.MemberDeath;
                        }

                        container.Add(new CuiElement
                        {
                            Parent = "InfoPanel3",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = $"{kills}", Align = TextAnchor.MiddleLeft, FontSize = 12, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.1316288 0.05177624", AnchorMax = "1.03409 0.5714285"}
                            }
                        });

                        container.Add(new CuiPanel
                        {
                            RectTransform =
                                    {
                                        AnchorMin = $"0.5987899 -0.005602248",
                                        AnchorMax = $"0.7540322 0.1316526"
                                    },
                            Image =
                                    {
                                        Color = $"{HexToRustFormat("#B6B6B62E")}",
                                    }
                        }, Layer, $"InfoPanel4");

                        container.Add(new CuiElement
                        {
                            Parent = "InfoPanel4",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9A2")}", Text = "К/Д", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.1316288 0.480348", AnchorMax = "1.03409 1"}
                            }
                        });

                        double kd = kills / deaths;

                        string kdtext = kd.ToString().Length > 3 ? kd.ToString().Substring(0, 3) : kd.ToString();

                        container.Add(new CuiElement
                        {
                            Parent = "InfoPanel4",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = $"{kdtext}", Align = TextAnchor.MiddleLeft, FontSize = 12, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.1316288 0.05177624", AnchorMax = "1.03409 0.5714285"}
                            }
                        });

                        container.Add(new CuiPanel
                        {
                            RectTransform =
                                    {
                                        AnchorMin = $"0.7681445 -0.005602248",
                                        AnchorMax = $"0.9999998 0.1316526"
                                    },
                            Image =
                                    {
                                        Color = $"{HexToRustFormat("#B6B6B62E")}",
                                    }
                        }, Layer, $"InfoPanel5");

                        container.Add(new CuiElement
                        {
                            Parent = "InfoPanel5",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9A2")}", Text = "Всего баллов", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.1316288 0.480348", AnchorMax = "1.03409 1"}
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = "InfoPanel5",
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = $"{clan.TotalScores}", Align = TextAnchor.MiddleLeft, FontSize = 12, Font = "robotocondensed-bold.ttf"},
                                new CuiRectTransformComponent{AnchorMin = "0.1316288 0.05177624", AnchorMax = "1.03409 0.5714285"}
                            }
                        });


                        double anchormin3 = 0.6106442, anchormax3 = 0.7478992;
                        foreach (var z in Enumerable.Range(0, 4))
                        {
                            if (clan.Members.Count() > z)
                            {
                                var item = clan.Members.ElementAtOrDefault(z);

                                string command = null;

                                if (item.Key == player.userID && clan.LeaderUlongID != player.userID)
                                {
                                    command = $"UI_ClanHandler leave {item.Key}";
                                }
                                else if (item.Key == player.userID && clan.LeaderUlongID == player.userID)
                                {
                                    command = $"UI_ClanHandler disband {item.Key}";
                                }
                                else if (item.Key != player.userID && clan.LeaderUlongID == player.userID)
                                {
                                    command = $"UI_ClanHandler kick {item.Key}";
                                }

                                container.Add(new CuiButton
                                {
                                    RectTransform =
                                    {
                                        AnchorMin = $"0.1169356 {anchormin3}",
                                        AnchorMax = $"1 {anchormax3}"
                                    },
                                    Button =
                                    {
                                        Color = z % 2 != 0 ? $"{HexToRustFormat("#B6B6B62E")}" : $"{HexToRustFormat("#B6B6B640")}",
                                        Command = command,
                                    },
                                    Text =
                                    {
                                        Text = ""
                                    }
                                }, Layer, $"Panel{z}");


                                if(item.Key == player.userID)
                                {
                                    container.Add(new CuiPanel
                                    {
                                        RectTransform =
                                        {
                                           AnchorMin = "0 0",
                                           AnchorMax = "1 1",
                                           OffsetMin = "0 0",
                                           OffsetMax = "0 -0.2"
                                        },
                                        Image =
                                        {
                                           Color =  $"{HexToRustFormat("#767E45A3")}",
                                           Sprite = "assets/content/ui/ui.background.transparent.linearltr.tga",
                                           FadeIn = 1f
                                        }
                                    }, $"Panel{z}");
                                }

                                container.Add(new CuiPanel
                                {
                                    RectTransform =
                                    {
                                        AnchorMin = $"2.30968E-07 {anchormin3}",
                                        AnchorMax = $"0.1008066 {anchormax3}"
                                    },
                                    Image =
                                    {
                                        Color = z % 2 != 0 ? $"{HexToRustFormat("#B6B6B62E")}" : $"{HexToRustFormat("#B6B6B640")}",
                                    }
                                }, Layer, $"PanelM{z}");

                                container.Add(new CuiElement
                                {
                                    Parent = $"PanelM{z}",
                                    Components =
                                    {
                                        new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = $"{(z + 1)}", Align = TextAnchor.MiddleCenter, FontSize = 12, Font = "robotocondensed-bold.ttf"},
                                        new CuiRectTransformComponent{AnchorMin = "0 0", AnchorMax = "1 1"}
                                    }
                                });

                                container.Add(new CuiElement
                                {
                                    Parent = $"Panel{z}",
                                    Name = $"Avatar{item.Key}",
                                    Components =
                                    {
                                        new CuiRawImageComponent{Png = (string) ImageLibrary.Call("GetImage", $"avatar_{item.Key.ToString()}")},
                                        new CuiRectTransformComponent{AnchorMin = "0.01138944 0.09259259", AnchorMax = "0.1018888 0.8979601"}
                                    }
                                });

                                container.Add(new CuiElement
                                {
                                    Parent = $"Panel{z}",
                                    Components =
                                    {
                                        new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = $"{covalence.Players.FindPlayerById(item.Key.ToString()).Name}", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                        new CuiRectTransformComponent{AnchorMin = "0.1181426 0.4191232", AnchorMax = "0.4624147 0.938776"}
                                    }
                                });


                                container.Add(new CuiElement
                                {
                                    Parent = $"Panel{z}",
                                    Components =
                                    {
                                        new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D994")}", Text = $"{item.Key}", Align = TextAnchor.MiddleLeft, FontSize = 10, Font = "robotocondensed-bold.ttf"},
                                        new CuiRectTransformComponent{AnchorMin = "0.1181426 0.05177643", AnchorMax = "0.4624147 0.5714293"}
                                    }
                                });

                                double kd2 = item.Value.MemberDeath > 0 ? Convert.ToDouble(item.Value.MemberKill) / Convert.ToDouble(item.Value.MemberDeath) : item.Value.MemberKill;

                                string kd2text = kd2.ToString().Length > 3 ? kd2.ToString().Substring(0, 3) : kd2.ToString();
                                container.Add(new CuiElement
                                {
                                    Parent = $"Panel{z}",
                                    Components =
                                    {
                                        new CuiTextComponent{Color = $"1 1 1 0.7", Text = $"{kd2text}", Align = TextAnchor.MiddleCenter, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                        new CuiRectTransformComponent{AnchorMin = "0.6387395 0.2762656", AnchorMax = "0.7506654 0.7959185"}
                                    }
                                });

                                container.Add(new CuiElement
                                {
                                    Parent = $"Panel{z}",
                                    Components =
                                    {
                                        new CuiTextComponent{Color = $"1 1 1 0.7", Text = $"{item.Value.MemberScores}", Align = TextAnchor.MiddleCenter, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                        new CuiRectTransformComponent{AnchorMin = "0.8675737 0.2762656", AnchorMax = "0.9794995 0.7959185"}
                                    }
                                });
                            }
                            else
                            {
                                container.Add(new CuiButton
                                {
                                    RectTransform =
                                    {
                                        AnchorMin = $"0.1169356 {anchormin3}",
                                        AnchorMax = $"1 {anchormax3}"
                                    },
                                    Button =
                                    {
                                        Color = z % 2 != 0 ? $"{HexToRustFormat("#B6B6B62E")}" : $"{HexToRustFormat("#B6B6B640")}",
                                        Command = $"UI_ClanHandler invite",
                                    },
                                    Text =
                                    {
                                        Text = ""
                                    }
                                }, Layer, $"Panel{z}");


                                container.Add(new CuiPanel
                                {
                                    RectTransform =
                                    {
                                        AnchorMin = $"2.30968E-07 {anchormin3}",
                                        AnchorMax = $"0.1008066 {anchormax3}"
                                    },
                                    Image =
                                    {
                                        Color = z % 2 != 0 ? $"{HexToRustFormat("#B6B6B62E")}" : $"{HexToRustFormat("#B6B6B640")}",
                                    }
                                }, Layer, $"PanelM{z}");

                                container.Add(new CuiElement
                                {
                                    Parent = $"Panel{z}",
                                    Name = "Add",
                                    Components =
                                    {
                                        new CuiRawImageComponent{Color = "1 1 1 1", Png = ImageLibrary.Call<string>("GetImage", "GradientGreen"), FadeIn = 1f},
                                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "-0.2 -0.2" }
                                    }
                                });

                                container.Add(new CuiElement
                                {
                                    Parent = $"Panel{z}",
                                    Name = "Add",
                                    Components =
                                    {
                                        new CuiRawImageComponent{Color = "1 1 1 1", Png = ImageLibrary.Call<string>("GetImage", "ClanInvite")},
                                        new CuiRectTransformComponent { AnchorMin = "0.9359102 0.317081", AnchorMax = "0.9814681 0.6640196" }
                                    }
                                });


                            }

                            anchormin3 -= 0.1512605;
                            anchormax3 -= 0.1512605;
                        }

                    }
                    else
                    {
                        container.Add(new CuiPanel
                        {
                            RectTransform =
                                    {
                                        AnchorMin = $"1.15484E-07 -0.005602248",
                                        AnchorMax = $"0.9999998 0.8403361"
                                    },
                            Image =
                                    {
                                        Color = $"{HexToRustFormat("#B6B6B62E")}",
                                    }
                        }, Layer, $"PanelBig");

                        container.Add(new CuiElement
                        {
                            Parent = Layer,
                            Name = "Input",
                            Components =
                            {
                                new CuiImageComponent { Color = $"{HexToRustFormat("#B6B6B63E")}" },
                                new CuiRectTransformComponent { AnchorMin = "0.3020833 0.3446934", AnchorMax = "0.7016129 0.4847496" }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = "Input",
                            Components =
                            {
                                new CuiInputFieldComponent{Color = $"{HexToRustFormat("#FFFFFF59")}", Text = "Создание клана", Align = TextAnchor.MiddleLeft, FontSize = 10, Font = "robotocondensed-regular.ttf", Command = "clan create"},
                                new CuiRectTransformComponent{AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "5 0", OffsetMax = "5 0"}
                            }
                        });

                        container.Add(new CuiButton
                        {
                            RectTransform =
                            {
                                AnchorMin = "0.1911962 0.3446934", AnchorMax = "0.2920026 0.4847496"
                            },
                            Button =
                            {
                                Color = $"{HexToRustFormat("#ECCC7A3E")}",
                                Command = $"",
                            },
                            Text =
                            {
                                Text = "",
                            }
                        }, Layer, "LeftIcon");

                        container.Add(new CuiButton
                        {
                            RectTransform =
                            {
                                AnchorMin = "0.7093409 0.3446934", AnchorMax = "0.8101472 0.4847496"
                            },
                            Button =
                            {
                                Color = $"{HexToRustFormat("#C0CA7C66")}",
                                Command = $"",
                            },
                            Text =
                            {
                                Text = "",
                            }
                        }, Layer, "RightIcon");

                        container.Add(new CuiPanel
                        {
                            Image = { Color = $"{HexToRustFormat("#ECCC7AC0")}", Sprite = "assets/icons/info.png" },
                            RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "5 5", OffsetMax = "-5 -5" }
                        }, "LeftIcon");

                        container.Add(new CuiPanel
                        {
                            Image = { Color = $"{HexToRustFormat("#CCD68AFF")}", Sprite = "assets/icons/check.png" },
                            RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "5 5", OffsetMax = "-5 -5" }
                        }, "RightIcon");

                    }

                    break;
                case "ClanTop":
                    container.Add(new CuiButton
                    {
                        RectTransform =
                        {
                            AnchorMin = "1.192093E-07 0.859944",
                            AnchorMax= "0.4919355 0.9971989"
                        },
                        Button =
                        {
                            Color = active == "ClanTop" ? $"{HexToRustFormat("#B6B6B680")}" : $"{HexToRustFormat("#B6B6B640")}",
                            Command = $"UI_ClanHandler clantop",
                        },
                        Text =
                        {
                            Text = $"Топ кланов",
                            Color = active == "ClanTop" ? $"{HexToRustFormat("#464646CE")}" : $"1 1 1 0.8",
                            Align = TextAnchor.MiddleCenter,
                            FontSize = 12,
                            Font = "robotocondensed-bold.ttf"
                        }
                    }, Layer, "ClanTop");

                    container.Add(new CuiButton
                    {
                        RectTransform =
                        {
                            AnchorMin = "0.5077281 0.859944",
                            AnchorMax= "1 0.9971989"
                        },
                        Button =
                        {
                            Color = active == "MyClan" ? $"{HexToRustFormat("#B6B6B680")}" : $"{HexToRustFormat("#B6B6B640")}",
                            Command = $"UI_ClanHandler myclan",
                        },
                        Text =
                        {
                            Text = "Мой клан",
                            Color = active == "MyClan" ? $"{HexToRustFormat("#464646CE")}" : $"1 1 1 0.8",
                            Align = TextAnchor.MiddleCenter,
                            FontSize = 12,
                            Font = "robotocondensed-bold.ttf"
                        }
                    }, Layer, "MyClan");

                    container.Add(new CuiPanel
                    {
                        RectTransform =
                                {
                                    AnchorMin = $"2.57045E-07 0.7619048",
                                    AnchorMax = $"1 0.837535"
                                },
                        Image =
                                {
                                    Color = $"{HexToRustFormat("#B6B6B62E")}",
                                }
                    }, Layer, $"Panel");

                    container.Add(new CuiElement
                    {
                        Parent = "Panel",
                        Components =
                        {
                            new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = "#", Align = TextAnchor.MiddleCenter, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                            new CuiRectTransformComponent{AnchorMin = "0.01982528 0.0925926", AnchorMax = "0.07190865 0.8888896"}
                        }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = "Panel",
                        Components =
                        {
                            new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = "Клан", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                            new CuiRectTransformComponent{AnchorMin = "0.1286962 0.1296297", AnchorMax = "0.2278225 0.9259268"}
                        }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = "Panel",
                        Components =
                        {
                            new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = "Приз", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                            new CuiRectTransformComponent{AnchorMin = "0.7113569 0.1296297", AnchorMax = "0.8104832 0.9259268"}
                        }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = "Panel",
                        Components =
                        {
                            new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = "Очки", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                            new CuiRectTransformComponent{AnchorMin = "0.902889 0.1296297", AnchorMax = "1.002015 0.9259268"}
                        }
                    });

                    var clanlist = from item in _clanList orderby item.TotalScores descending select item;

                    double anchormin2 = 0.6106442, anchormax2 = 0.7478992;
                    foreach (var i in Enumerable.Range(0, 5))
                    {
                        var item = clanlist.ElementAtOrDefault(i);

                        if (item != null)
                        {
                            container.Add(new CuiButton
                            {
                                RectTransform =
                                {
                                    AnchorMin = $"0.1169356 {anchormin2}",
                                    AnchorMax = $"1 {anchormax2}"
                                },
                                Button =
                                {
                                    Color = i % 2 != 0 ? $"{HexToRustFormat("#B6B6B62E")}" : $"{HexToRustFormat("#B6B6B640")}",
                                    Command = $"",
                                },
                                Text =
                                {
                                    Text = ""
                                }
                            }, Layer, $"Panel{i}");

                            if (item.Members.ContainsKey(player.userID))
                            {
                                container.Add(new CuiPanel
                                {
                                    RectTransform =
                                    {
                                        AnchorMin = "0 0",
                                        AnchorMax = "1 1",
                                        OffsetMin = "0 0",
                                        OffsetMax = "0 -0.2"
                                    },
                                    Image =
                                    {
                                        Color = $"{HexToRustFormat("#767E45A3")}",
                                        Sprite = "assets/content/ui/ui.background.transparent.linearltr.tga",
                                        FadeIn = 1f
                                    }
                                }, $"Panel{i}");
                            }

                            container.Add(new CuiPanel
                            {
                                RectTransform =
                                {
                                    AnchorMin = $"2.30968E-07 {anchormin2}",
                                    AnchorMax = $"0.1008066 {anchormax2}"
                                },
                                Image =
                                {
                                    Color = i % 2 != 0 ? $"{HexToRustFormat("#B6B6B62E")}" : $"{HexToRustFormat("#B6B6B640")}",
                                }
                            }, Layer, $"PanelM{i}");

                            container.Add(new CuiElement
                            {
                                Parent = $"PanelM{i}",
                                Components =
                                {
                                    new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = $"{(i + 1)}", Align = TextAnchor.MiddleCenter, FontSize = 12, Font = "robotocondensed-bold.ttf"},
                                    new CuiRectTransformComponent{AnchorMin = "0 0", AnchorMax = "1 1"}
                                }
                            });

                            container.Add(new CuiElement
                            {
                                Parent = $"Panel{i}",
                                Components =
                                {
                                    new CuiRawImageComponent{Color = "1 1 1 1", Png = ImageLibrary.Call<string>("GetImage", item.ImageAvatar)},
                                    new CuiRectTransformComponent{AnchorMin = "0.01138944 0.09259259", AnchorMax = "0.1018888 0.8979601"}
                                }
                            });

                            container.Add(new CuiElement
                            {
                                Parent = $"Panel{i}",
                                Components =
                                {
                                    new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = $"{item.ClanTag}", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                    new CuiRectTransformComponent{AnchorMin = "0.1181426 0.4191232", AnchorMax = "0.4624147 0.938776"}
                                }
                            });

                            int online = 0;

                            foreach (var pl in BasePlayer.activePlayerList)
                            {
                                if (item.Members.ContainsKey(pl.userID))
                                    online++;
                            }

                            container.Add(new CuiElement
                            {
                                Parent = $"Panel{i}",
                                Components =
                                {
                                    new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = $"Онлайн: {online}/{item.Members.Count()}", Align = TextAnchor.MiddleLeft, FontSize = 10, Font = "robotocondensed-bold.ttf"},
                                    new CuiRectTransformComponent{AnchorMin = "0.1181426 0.05177643", AnchorMax = "0.4624147 0.5714293"}
                                }
                            });

                            container.Add(new CuiElement
                            {
                                Parent = $"Panel{i}",
                                Components =
                                {
                                    new CuiTextComponent{Color = $"1 1 1 0.7", Text = $"{config.Prize.RewardDictionary.ElementAt(i).Value}р", Align = TextAnchor.MiddleCenter, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                    new CuiRectTransformComponent{AnchorMin = "0.6593346 0.2762656", AnchorMax = "0.7712604 0.7959185"}
                                }
                            });

                            container.Add(new CuiElement
                            {
                                Parent = $"Panel{i}",
                                Components =
                                {
                                    new CuiTextComponent{Color = $"1 1 1 0.7", Text = $"{item.TotalScores}", Align = TextAnchor.MiddleCenter, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                    new CuiRectTransformComponent{AnchorMin = "0.8675737 0.2762656", AnchorMax = "0.9794995 0.7959185"}
                                }
                            });
                        }
                        else
                        {
                            container.Add(new CuiButton
                            {
                                RectTransform =
                                {
                                    AnchorMin = $"0.1169356 {anchormin2}",
                                    AnchorMax = $"1 {anchormax2}"
                                },
                                Button =
                                {
                                    Color = i % 2 != 0 ? $"{HexToRustFormat("#B6B6B62E")}" : $"{HexToRustFormat("#B6B6B640")}",
                                    Command = $"",
                                },
                                Text =
                                {
                                    Text = ""
                                }
                            }, Layer, $"Panel{i}");

                            container.Add(new CuiPanel
                            {
                                RectTransform =
                                {
                                    AnchorMin = $"2.30968E-07 {anchormin2}",
                                    AnchorMax = $"0.1008066 {anchormax2}"
                                },
                                Image =
                                {
                                    Color = i % 2 != 0 ? $"{HexToRustFormat("#B6B6B62E")}" : $"{HexToRustFormat("#B6B6B640")}",
                                }
                            }, Layer, $"PanelM{i}");
                        }

                        anchormin2 -= 0.1512605;
                        anchormax2 -= 0.1512605;
                    }

                    break;
                case "Invite":

                    container.Add(new CuiButton
                    {
                        RectTransform =
                        {
                            AnchorMin = "1.192093E-07 0.859944",
                            AnchorMax= "0.4919355 0.9971989"
                        },
                        Button =
                        {
                            Color = active == "ClanTop" ? $"{HexToRustFormat("#B6B6B680")}" : $"{HexToRustFormat("#B6B6B640")}",
                            Command = $"UI_ClanHandler clantop",
                        },
                        Text =
                        {
                            Text = $"Топ кланов",
                            Color = active == "ClanTop" ? $"{HexToRustFormat("#464646CE")}" : $"1 1 1 0.8",
                            Align = TextAnchor.MiddleCenter,
                            FontSize = 12,
                            Font = "robotocondensed-bold.ttf"
                        }
                    }, Layer, "ClanTop");

                    container.Add(new CuiButton
                    {
                        RectTransform =
                        {
                            AnchorMin = "0.5077281 0.859944",
                            AnchorMax= "1 0.9971989"
                        },
                        Button =
                        {
                            Color = active == "Invite" ? $"{HexToRustFormat("#B6B6B680")}" : $"{HexToRustFormat("#B6B6B640")}",
                            Command = $"UI_ClanHandler myclan",
                        },
                        Text =
                        {
                            Text = "Назад",
                            Color = active == "Invite" ? $"{HexToRustFormat("#464646CE")}" : $"1 1 1 0.8",
                            Align = TextAnchor.MiddleCenter,
                            FontSize = 12,
                            Font = "robotocondensed-bold.ttf"
                        }
                    }, Layer, "MyClan");

                    container.Add(new CuiPanel
                    {
                        Image = { Color = $"{HexToRustFormat("#B6B6B63E")}" },
                        RectTransform = { AnchorMin = "-0.002015986 0.739496", AnchorMax = "0.06854836 0.8375353" }
                    }, Layer, "LeftIcon");

                    container.Add(new CuiPanel
                    {
                        Image = { Color = $"{HexToRustFormat("#ECCC7A3E")}" },
                        RectTransform = { AnchorMin = "0.9274186 0.739496", AnchorMax = "0.9979831 0.8375353" }
                    }, Layer, "RightIcon");

                    container.Add(new CuiPanel
                    {
                        Image = { Color = $"{HexToRustFormat("#ECCC7AC0")}", Sprite = "assets/icons/info.png" },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "5 5", OffsetMax = "-5 -5" }
                    }, "RightIcon");

                    container.Add(new CuiPanel
                    {
                        Image = { Color = $"1 1 1 0.5", Sprite = "assets/icons/web.png" },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "5 5", OffsetMax = "-5 -5" }
                    }, "LeftIcon");

                    container.Add(new CuiElement
                    {
                        Parent = Layer,
                        Name = "Input",
                        Components =
                        {
                            new CuiImageComponent { Color = $"{HexToRustFormat("#B6B6B63E")}" },
                            new CuiRectTransformComponent { AnchorMin = "0.08467746 0.739496", AnchorMax = "0.9112901 0.8375353" }
                        }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = "Input",
                        Components =
                        {
                            new CuiInputFieldComponent{Color = $"{HexToRustFormat("#FFFFFF59")}", Text = "Поиск по нику/steamid64", Align = TextAnchor.MiddleLeft, FontSize = 10, Font = "robotocondensed-regular.ttf", Command = "UI_ClanHandler search"},
                            new CuiRectTransformComponent{AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "5 0", OffsetMax = "5 0"}
                        }
                    });

                    break;
            }
            CuiHelper.AddUi(player, container);
            if(active == "Invite")
            {
                LoadedPlayers(player);
            }
        }

        private void LoadedPlayers(BasePlayer player, string TargetName = "")
        {
            var clan = FindClanByUser(player.userID);
            if (clan == null) return;

            var container = new CuiElementContainer();
            for (int j = 0; j < 6; j++)
            {
                CuiHelper.DestroyUi(player, $"PanelPl{j}");
            }

            double anchormin4 = 0.5742299, anchormax4 = 0.7254905;

            var dict = BasePlayer.activePlayerList
                .Where(z => z.userID != player.userID && (z.displayName.ToLower().Contains(TargetName.ToLower()) || z.userID.ToString().Contains(TargetName)));

            foreach (var i in Enumerable.Range(0, 5))
            {
                var item = dict.ElementAtOrDefault(i);
                if (item != null)
                {
                    container.Add(new CuiButton
                    {
                        RectTransform =
                                {
                                    AnchorMin = $"-0.004032254 {anchormin4}",
                                    AnchorMax = $"1 {anchormax4}"
                                },
                        Button =
                                {
                                    Color = i % 2 != 0 ? $"{HexToRustFormat("#B6B6B62E")}" : $"{HexToRustFormat("#B6B6B640")}",
                                    Command = $"clan invite {item.userID}",
                                },
                        Text =
                                {
                                    Text = ""
                                }
                    }, Layer, $"PanelPl{i}");

                    container.Add(new CuiElement
                    {
                        Parent = $"PanelPl{i}",
                        Components =
                                {
                                    new CuiRawImageComponent{Color = "1 1 1 1", Png = ImageLibrary.Call<string>("GetImage", $"avatar_{item.UserIDString}")},
                                    new CuiRectTransformComponent{AnchorMin = "0.01138944 0.09259259", AnchorMax = "0.1018888 0.8979601"}
                                }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = $"PanelPl{i}",
                        Components =
                                {
                                    new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D9E5")}", Text = $"{item.displayName}", Align = TextAnchor.MiddleLeft, FontSize = 11, Font = "robotocondensed-bold.ttf"},
                                    new CuiRectTransformComponent{AnchorMin = "0.1181426 0.4191232", AnchorMax = "0.4624147 0.938776"}
                                }
                    });


                    container.Add(new CuiElement
                    {
                        Parent = $"PanelPl{i}",
                        Components =
                                {
                                    new CuiTextComponent{Color = $"{HexToRustFormat("#E8E2D986")}", Text = $"{item.userID}", Align = TextAnchor.MiddleLeft, FontSize = 10, Font = "robotocondensed-bold.ttf"},
                                    new CuiRectTransformComponent{AnchorMin = "0.1181426 0.05177643", AnchorMax = "0.4624147 0.5714293"}
                                }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = $"PanelPl{i}",
                        Name = "Gradient",
                        Components =
                                    {
                                        new CuiRawImageComponent{Color = "1 1 1 1", Png = ImageLibrary.Call<string>("GetImage", "GradientGreen"), FadeIn = 1f},
                                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "-0.2 -0.2" }
                                    }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = $"PanelPl{i}",
                        Name = "Add",
                        Components =
                                    {
                                        new CuiRawImageComponent{Color = "1 1 1 1", Png = ImageLibrary.Call<string>("GetImage", "ClanInvite")},
                                        new CuiRectTransformComponent { AnchorMin = "0.9359102 0.317081", AnchorMax = "0.9814681 0.6640196" }
                                    }
                    });

                }
                else
                {
                    container.Add(new CuiButton
                    {
                        RectTransform =
                                {
                                    AnchorMin = $"-0.004032254 {anchormin4}",
                                    AnchorMax = $"1 {anchormax4}"
                                },
                        Button =
                                {
                                    Color = i % 2 != 0 ? $"{HexToRustFormat("#B6B6B62E")}" : $"{HexToRustFormat("#B6B6B640")}",
                                    Command = $"",
                                },
                        Text =
                                {
                                    Text = ""
                                }
                    }, Layer, $"PanelPl{i}");

                }
                anchormin4 -= 0.1652661;
                anchormax4 -= 0.1652661;
            }
            CuiHelper.AddUi(player, container);
        }

        #endregion

        #region Functional

        #region Request

        private static void Request(string ask, Action<int, string> callback)
        {
            Dictionary<string, string> reqHeaders = new Dictionary<string, string>{{ "User-Agent", "Clan Plugin" }};
            Instance.webrequest.Enqueue($"https://gamestores.app/api/?shop_id={config.Prize.GSSettings.ShopID}&secret={config.Prize.GSSettings.SecretKey}" + ask, "", (code, response) =>
            {

                switch (code)
                {
                    case 200:
                    {
                        break;
                    }

                    case 404:
                    {
                        Instance.PrintWarning($"Please check your configuration! [404] #2");
                        break;
                    }
                }

                if (response.Contains("The authentication or decryption has failed."))
                {
                    Instance.PrintWarning("HTTPS request is broken (broken CA certificate?). Changed to non secure connection!");

                    Interface.Oxide.UnloadPlugin(Instance.Name);
                    return;
                }

                callback?.Invoke(code, response);
            }, Instance, RequestMethod.GET, reqHeaders);
                      
        }

        #endregion
        
        
        #region Formated
        
        string getFormattedClanTag(IPlayer player)
        {
            var clan = FindClanByUser(ulong.Parse(player.Id));
            if (clan != null && !string.IsNullOrEmpty(clan.ClanTag)) return $"[{clan.ClanTag}]";
            return string.Empty;
        }
        
        #endregion


        #region LoadData

        void LoadData()
        {
            if (Interface.GetMod().DataFileSystem.ExistsDatafile(Name))
            {
                _clanList = Interface.GetMod().DataFileSystem.ReadObject<List<ClanData>>(Name);
            }
            else _clanList = new List<ClanData>();
        }

        #endregion

        #region GetAvatar

        private readonly Regex Regex = new Regex(@"<avatarFull><!\[CDATA\[(.*)\]\]></avatarFull>");
        
        private void GetAvatar(ulong userId, Action<string> callback)
        {
            if (callback == null) return;

            webrequest.Enqueue($"http://steamcommunity.com/profiles/{userId}?xml=1", null, (code, response) =>
            {
                if (code != 200 || response == null)
                    return;

                var avatar = Regex.Match(response).Groups[1].ToString();
                if (string.IsNullOrEmpty(avatar))
                    return;

                Puts($"{avatar}");
                
                callback.Invoke(avatar);
            }, this);
        }

        #endregion

        #region HexColor

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

        #endregion

        #region GetNamePlayer

        private string GetNamePlayer(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId())
                return string.Empty;

            var covPlayer = player.IPlayer;

            if (player.net?.connection == null)
            {
                if (covPlayer != null)
                    return covPlayer.Name;

                return player.UserIDString;
            }

            var value = player.net.connection.username;
            var str = value.ToPrintable(32).EscapeRichText().Trim();
            if (string.IsNullOrWhiteSpace(str))
            {
                str = covPlayer.Name;
                if (string.IsNullOrWhiteSpace(str))
                    str = player.UserIDString;
            }

            return str;
        }

        #endregion

        #region Disable Damage

        private void DisableDamage(HitInfo info)
        {
            info.damageTypes = new DamageTypeList();
            info.DidHit = false;
            info.HitEntity = null;
            info.Initiator = null;
            info.DoHitEffects = false;
            info.HitMaterial = 0;
        }
        
        #endregion

        #region InternalHook
        
        private void SubscribeInternalHook(string hook)
        {
            var hookSubscriptions_ = hookSubscriptions.GetValue(Interface.Oxide.RootPluginManager) as IDictionary<string, IList<Plugin>>;
			
            IList<Plugin> plugins;
            if (!hookSubscriptions_.TryGetValue(hook, out plugins))
            {
                plugins = new List<Plugin>();
                hookSubscriptions_.Add(hook, plugins);
            }
			
            if (!plugins.Contains(this))            
                plugins.Add(this);            
        }

        #endregion
        
        #region AddedClan


        public void CreateInClan(BasePlayer player, string clanTag)
        {
            var clanGather = new Dictionary<string, ClanData.GatherData>();
            
            foreach (var key in config.Main.GatherDictionary)
            {
                clanGather.Add(key, new ClanData.GatherData
                {
                    Need = 1000,
                    TotalFarm = 0,
                });
            }

            var newclan = new ClanData()
            {
                ClanTag = clanTag,

                ImageAvatar = $"avatar_{player.UserIDString}",

                GatherList = clanGather,

                LeaderUlongID = player.userID,

                Task = "",

                TotalScores = 0,

                Members
                    = new Dictionary<ulong, ClanData.MemberData>(),

                Moderators
                    = new List<ulong>(),

                PendingInvites
                    = new List<ulong>()
            };
            

            newclan.Members.Add(player.userID, new ClanData.MemberData
            {
                GatherMember = config.Main.GatherDictionary.ToDictionary(x => x, p => 0),
            });

            //_ins.PrintWarning($"[{DateTime.Now.ToString("g")}] был создан клан {clanTag} игроком {player.displayName}");

            if (config.Main.TagInPlayer)
                player.displayName = $"[{clanTag}] {GetNamePlayer(player)}";

            if (config.Main.EnableTeam)
                newclan.FindOrCreateTeam();

            player.SendNetworkUpdateImmediate();

            _clanList.Add(newclan);

            Interface.CallHook("OnClanCreate", clanTag, player);


        }

        #endregion
        
        #region FormatTime

        private string GetFormatTime(TimeSpan timespan)
        {
            return string.Format("{0:00}м. {1:00}с.", timespan.Minutes, timespan.Seconds);
        }

        #endregion
        
        #region Lang

        public static StringBuilder sb = new StringBuilder();

        public string GetLang(string LangKey, string userID = null, params object[] args)
        {
            sb.Clear();
            if (args != null)
            {
                sb.AppendFormat(lang.GetMessage(LangKey, this, userID), args);
                return sb.ToString();
            }

            return lang.GetMessage(LangKey, this, userID);
        }

        private void LoadMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
                {
                    ["ClanNotFound"] = "Что бы создать клан введите: /clan create \"название клана\"",
                    ["ClanInviteNotFoundPlayer"] = "/clan invite \"ник или steamid игрока\" - отправить предложение вступить в клан",
                    ["ClanKickNotFoundPlayer"] = "/clan kick \"ник или steamid игрока\" - исключить игрока из клана",
                    ["ClanOwnerNotFoundPlayer"] = "/clan owner \"ник или steamid игрока\" - назначить игрока главой клана",
                    ["ClanTaskNotLength"] = "/clan task \"задача\" - установить задачу",
                    ["ClanTask"] = "Вы успешно установили задачу!",
                    ["TargetNotClan"] = "Игрок {0} не был найден в клане",
                    ["TargetModeratorAndOwner"] = "Игрок является модератором или главой в клане!",
                    ["PlayerNotFound"] = "Игрок {0} не был найден",
                    ["PlayerNotClan"] = "Вы не состоите в клане",
                    ["PlayerOwner"] = "Вы являетесь главой клана",
                    ["PlayerLeave"] = "Вы успешно покинули клан",
                    ["PlayerKickSelf"] = "Вы не можете кикнуть самого себя!",
                    ["PlayerKick"] = "Вы были кикнуты из клана!",
                    ["PlayerModeratorKick"] = "Вы успешно кикнули игрока {0} из клана!",
                    ["NameClanLength"] = "Минимальная длина тега: {0}, максимальная длина тега: {1}",
                    ["ContainsTAG"] = "Данный клан тег уже занят другим кланом",
                    ["ClanTagBlocked"] = "Данный клан тег запрещен",
                    ["PlayerInClan"] = "Вы уже состоите в клане",
                    ["TargetInClan"] = "Данный игрок уже находится в клане",
                    ["PlayerNotOwnerAndModerator"] = "Вы не являетесь модератором или главой клана",
                    ["PlayerNotOwner"] = "Вы не являетесь главой клана",
                    ["PSetLeader"] = "Вы успешно стали главой клана",
                    ["PGiveLeader"] = "Вы успешно передали главу клана",
                    ["ClanDisband"] = "Вы успешно расформировали клан",
                    ["PlayerNotInvite"] = "Вы не имеете активных предложений",
                    ["PlayerStartClan"] = "Вы успешно создали клан: {0}!",
                    ["ClanLimitPlayer"] = "Превышено количество игроков в клане!",
                    ["ClanLimitModerator"] = "Превышено количество модераторов в клане!",
                    ["AcceptInvite"] = "Вы успешно присоединились к клану {0}",
                    ["DenyInvite"] = "Вы успешно отклонили предложение для вступления в клан",
                    ["PlayerStartInvite"] = "Вы получили приглашение в клан: {0}\n/clan accept- принять приглашение\n/clan deny - отклонить предложение",
                    ["InitiatorStartInvite"] = "Вы успешно отправили приглашение игроку: {0}",
                    ["ClanFFActive"] = "Вы не можете нанести урон своему напарнику. Что бы включить FF, пропишите команду /clan ff",
                    ["ClanFFActivate"] = "Вы успешно включили FF",
                    ["ClanFFDeactivation"] = "Вы успешно выключили FF",
                    ["HelpNoClan"] = "Помощь:\n/clan create \"название клана\" - создать клан\n/clan help - список доступных комманд\n/clan accept - принять предложение вступить в клан\n/clan deny - отклонить предложение вступить в клан",
                    ["HelpClanPlayer"] = "Помощь:\n/clan help - список доступных комманд\n/clan leave - покинуть клан\n/c \"сообщение\" - отправить сообщение всему клану",
                    ["HelpClanModerator"] = "Помощь:\n/clan invite \"ник или steamid игрока\" - отправить предложение вступить в клан\n/clan kick \"ник или steamid игрока\" - исключить игрока из клана\n/clan leave - покинуть клан\n",
                    ["HelpClanOwner"] = "Помощь:\n/clan invite \"ник или steamid игрока\" - отправить предложение вступить в клан\n/clan kick \"ник или steamid игрока\" - исключить игрока из клана\n/clan disband - расфомировать клан\n/clan task \"задача\" - установить задачу\n/clan owner \"ник или steamid игрока\" - назначить игрока главой клана",
                    ["Cupboard"] = "Осталось доступных шкафов: {0} из {1}",
                    ["Turret"] = "Осталось доступных турелей: {0} из {1}",
                    ["SamSite"] = "Осталось доступных ПВО: {0} из {1}",
                    ["UI_TaskContent"] = "ТЕКУЩАЯ ЗАДАЧА",
                    ["UI_ClothContent"] = "НАБОР КЛАНОВОЙ ОДЕЖДЫ",
                    ["UI_GatherContent"] = "ДОБЫЧА РЕСУРСОВ",
                    ["UI_NameContent"] = "НИК ИГРОКА",
                    ["UI_ActivityContent"] = "АКТИВНОСТЬ",
                    ["UI_StandartContent"] = "НОРМА",
                    ["UI_ScoresContent"] = "ОЧКИ ТОПА",
                    ["UI_TaskMessageContent"] = "Глава клана еще не указал текущую задачу",
                    ["UI_GatherStart"] = "Установка нормы",
                    ["UI_GatherComment"] = "Укажите количество которое должен добыть участник группы",
                    ["UI_SkinContent"] = "ВЫБЕРИТЕ СКИН",
                    ["UI_TopName"] = "НАЗВАНИЕ КЛАНА",
                    ["UI_TopTournament"] = "ШКАФ",
                    ["UI_TopReward"] = "НАГРАДА",
                    ["UI_TopScores"] = "ОЧКИ",
                    ["UI_TopPlayer"] = "ИГРОКОВ",
                    ["UI_TopInformation"] = "Очки даются:\nУбийство +20, добыча руда +1-2,разрушение бочки +1, сбитие вертолета +1000, уничтожение бредли +650\nОчки отнимаются:\nСмерть -10, самоубийство -30\nНаграда выдается после вайпа на сервере!",
                    ["UI_InfoClanName"] = "ИМЯ ИГРОКА",
                    ["UI_InfoClanScores"] = "ОЧКОВ",
                    ["UI_InfoClanFarm"] = "НОРМА",
                    ["UI_InfoClanKill"] = "УБИЙСТВ",
                    ["UI_InfoClanDeath"] = "СМЕРТЕЙ"

                }
                , this);
        }

        #endregion
        
        #region StartCommand

        
        [ConsoleCommand("Clan_Command")]
        void RunCommand(ConsoleSystem.Arg args)
        {
            var player = args.Player();
            if (player == null) return;
            if (args.FullString.Contains("/"))
                player.Command("chat.say", args.FullString);
            else
                player.Command(args.FullString);
        }

        #endregion
        
        #endregion
        
        #region Command
        
        #region ChatCommand

       
        private void ClanCmd(IPlayer cov, string command, string[] args)
        {
            var player = cov?.Object as BasePlayer;
            if (player == null) return;

            if (args.Length == 0)
            {
                var clan = FindClanByUser(player.userID);
                if (clan == null)
                {
                    player.ChatMessage(GetLang("ClanNotFound", player.UserIDString));
                }

              

                MainUI(player);
                return;
            }

            switch (args[0])
            {
                #region Create
                
                case "create":
                {
                    if (args.Length < 2)
                    {
                        player.ChatMessage(GetLang("ClanNotFound", player.UserIDString));
                        return;
                    }

                    var clan = FindClanByUser(player.userID);
                    if (clan != null)
                    {
                        player.ChatMessage(GetLang("PlayerInClan", player.UserIDString));
                        return;
                    }
                    
                    var clanTag = string.Join(" ", args.Skip(1));

                    if (string.IsNullOrEmpty(clanTag))
                    {
                        player.ChatMessage(GetLang("ClanNotFound", player.UserIDString));
                        return;
                    }

                    clanTag = clanTag.Replace(" ", "");
                    
                    
                    if (clanTag.Length < config.Main.MinNameLength || clanTag.Length > config.Main.MaxNameLength)
                    {
                        player.ChatMessage(GetLang("NameClanLength", player.UserIDString, config.Main.MinNameLength, config.Main.MaxNameLength));
                        return;
                    }
                    
                    
                    if (config.Main.ForbiddenTag.Exists(word => $"[{clanTag}]".Contains(word, CompareOptions.OrdinalIgnoreCase))) // Mevent <3
                    {
                        player.ChatMessage(GetLang("ClanTagBlocked", player.UserIDString));
                        return;
                    }

                    var alreadyClan = FindClanByTag(clanTag);
                    if (alreadyClan != null)
                    {
                        player.ChatMessage(GetLang("ContainsTAG", player.UserIDString));
                        return;
                    }


                    CreateInClan(player, clanTag);
                    
                    
                    player.ChatMessage(GetLang("PlayerStartClan", player.UserIDString, clanTag));

                        MainUI(player);
                    
                    break;
                }
                
                #endregion

                #region Accept

                case "accept":
                {
                    var clan = FindClanByUser(player.userID);
                    if (clan != null)
                    {
                        player.ChatMessage(GetLang("PlayerInClan", player.UserIDString));
                        return;
                    }

                    var clanInvite = FindClanByInvite(player.userID);
                    if (clanInvite == null)
                    {
                        player.ChatMessage(GetLang("PlayerNotInvite", player.UserIDString));
                        return;
                    }

                    if (clanInvite.Members.Count >= config.Main.MaxCountClan)
                    {
                        player.ChatMessage(GetLang("ClanLimitPlayer", player.UserIDString));
                        clanInvite.PendingInvites.Remove(player.userID);
                        return;
                    }
                    
                    clanInvite.InvitePlayer(player);
                     
                    player.ChatMessage(GetLang("AcceptInvite", player.UserIDString, clanInvite.ClanTag));
                    
                    break;
                }

                #endregion

                #region Deny

                case "deny":
                {
                    var clan = FindClanByUser(player.userID);
                    if (clan != null)
                    {
                        player.ChatMessage(GetLang("PlayerInClan", player.UserIDString));
                        return;
                    }

                    var clanInvite = FindClanByInvite(player.userID);
                    if (clanInvite == null)
                    {
                        player.ChatMessage(GetLang("PlayerNotInvite", player.UserIDString));
                        return;
                    }

                    clanInvite.PendingInvites.Remove(player.userID);
                    
                    player.ChatMessage(GetLang("DenyInvite", player.UserIDString));
                    
                    break;
                }

                #endregion

                #region Invite

                case "invite":
                {
                    if (args.Length < 2)
                    {
                        player.ChatMessage(GetLang("ClanInviteNotFoundPlayer", player.UserIDString));
                        return;
                    }

                    var clan = FindClanByUser(player.userID);
                    
                    if (clan == null)
                    {
                        player.ChatMessage(GetLang("PlayerNotClan", player.UserIDString));
                        return;
                    }

                    if (!clan.IsModerator(player.userID))
                    {
                        player.ChatMessage(GetLang("PlayerNotOwnerAndModerator", player.UserIDString));
                        return;
                    }

                    if (clan.Members.Count >= config.Main.MaxCountClan)
                    {
                        player.ChatMessage(GetLang("ClanLimitPlayer", player.UserIDString));
                        return;
                    }


                    string name = string.Join(" ", args.Skip(1));

                    var targetPlayer = BasePlayer.Find(name);
                    if (targetPlayer == null)
                    {
                        player.ChatMessage(GetLang("PlayerNotFound", player.UserIDString, name));
                        return;
                    }
                    
                    if (player == targetPlayer) return;

                    var clanTarget = FindClanByUser(targetPlayer.userID);
                    if (clanTarget != null)
                    {
                        player.ChatMessage(GetLang("TargetInClan", player.UserIDString));
                        return;
                    }
                    
                    clan.PendingInvites.Add(targetPlayer.userID);
                    
                    targetPlayer.ChatMessage(GetLang("PlayerStartInvite", targetPlayer.UserIDString, clan.ClanTag));
                    player.ChatMessage(GetLang("InitiatorStartInvite", player.UserIDString, targetPlayer.displayName));

                        var container = new CuiElementContainer();
                        CuiHelper.DestroyUi(player, "Text");
                        CuiHelper.DestroyUi(player, "Message");
                        container.Add(new CuiElement
                        {
                            Parent = Layer,
                            Name = "Message",
                            FadeOut = 0.2f,
                            Components =
                            {
                                new CuiImageComponent{Color = HexToRustFormat("#C8D38259"), FadeIn = 0.5f},
                                new CuiRectTransformComponent { AnchorMin = "-2.235174E-08 1.019803", AnchorMax = "1 1.111387" }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = "Message",
                            Name = "Text",
                            FadeOut = 0.2f,
                            Components =
                            {
                                new CuiTextComponent{Color = $"{HexToRustFormat("#CCD68AFF")}", Text = $"Вы успешно отправили заявку на вступление в клан, игроку {targetPlayer.displayName}", Align = TextAnchor.MiddleCenter, FontSize = 10, Font = "robotocondensed-bold.ttf", FadeIn = 0.5f},
                                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                            }
                        });

                        CuiHelper.AddUi(player, container);
                        timer.Once(5f, () =>
                        {
                            CuiHelper.DestroyUi(player, "Text");
                            CuiHelper.DestroyUi(player, "Message");
                        });
                        LoadedPlayers(player);
                        break;
                }

                #endregion

                #region Help

                case "help":
                {

                    var clan = FindClanByUser(player.userID);
                    if (clan == null)
                    {
                        player.ChatMessage(GetLang("HelpNoClan", player.UserIDString));
                        return;
                    }

                    if (!clan.IsModerator(player.userID))
                    {
                        player.ChatMessage(GetLang("HelpClanPlayer", player.UserIDString));
                        
                        return;
                    }

                    if (clan.LeaderUlongID != player.userID && clan.Moderators.Contains(player.userID))
                    {
                        player.ChatMessage(GetLang("HelpClanModerator", player.UserIDString));
                        
                        return;
                    }

                    if (clan.LeaderUlongID == player.userID)
                    {
                        player.ChatMessage(GetLang("HelpClanOwner", player.UserIDString));
                    }
                    
                    break;
                }

                #endregion

                #region Leave

                case "leave":
                {

                    var clan = FindClanByUser(player.userID);
                    if (clan == null)
                    {
                        player.ChatMessage(GetLang("PlayerNotClan", player.UserIDString));
                        return;
                    }

                    if (clan.IsOwner(player.userID))
                    {
                        player.ChatMessage(GetLang("PlayerOwner", player.UserIDString));
                        return;
                    }
                    
                    clan.RemovePlayerInClan(player.userID);
                    
                    player.ChatMessage(GetLang("PlayerLeave", player.UserIDString));
                    
                    break;
                }

                #endregion

                #region Kick
                
                case "kick":
                {
                    if (args.Length < 2)
                    {
                        player.ChatMessage(GetLang("ClanKickNotFoundPlayer", player.UserIDString));
                        return;
                    }
                    
                    
                    var clan = FindClanByUser(player.userID);

                    if (clan == null)
                    {
                        player.ChatMessage(GetLang("PlayerNotClan", player.UserIDString));
                        return;
                    }
                    
                    if (!clan.IsModerator(player.userID))
                    {
                        player.ChatMessage(GetLang("PlayerNotOwnerAndModerator", player.UserIDString));
                        return;
                    }
                    
                    string name = string.Join(" ", args.Skip(1));

                    var targetPlayer = covalence.Players.FindPlayer(name);
                    if (targetPlayer == null)
                    {
                        player.ChatMessage(GetLang("PlayerNotFound", player.UserIDString, name));
                        return;
                    }

                    if (player.IPlayer == targetPlayer)
                    {
                        player.ChatMessage(GetLang("PlayerKickSelf", player.UserIDString));
                        return;
                    }

                    if (!clan.IsMember(ulong.Parse(targetPlayer.Id)))
                    {
                        player.ChatMessage(GetLang("TargetNotClan", player.UserIDString, targetPlayer.Name));
                        return;
                    }

                    if (clan.IsModerator(ulong.Parse(targetPlayer.Id)))
                    {
                        player.ChatMessage(GetLang("TargetModeratorAndOwner", player.UserIDString));
                        return;
                    }
                    
                    clan.RemovePlayerInClan(ulong.Parse(targetPlayer.Id));
                    
                    player.ChatMessage(GetLang("PlayerModeratorKick", player.UserIDString, targetPlayer.Name));
                    
                    if (targetPlayer.IsConnected)
                        BasePlayer.Find(targetPlayer.Id).ChatMessage(GetLang("PlayerKick", targetPlayer.Id));
                        MainUI(player);
                    
                    break;
                }

                #endregion

                #region Disband

                case "disband":
                {
                    var clan = FindClanByUser(player.userID);
                    if (clan == null)
                    {
                        player.ChatMessage(GetLang("PlayerNotClan", player.UserIDString));
                        return;
                    }
                    
                    if (!clan.IsOwner(player.userID))
                    {
                        player.ChatMessage(GetLang("PlayerNotOwner", player.UserIDString));
                        return;
                    }
                    
                    clan.Disband();
                    
                    player.ChatMessage(GetLang("ClanDisband", player.UserIDString));
                    
                    break;
                }

                #endregion

                #region Owner

                case "owner":
                {
                    if (args.Length < 2)
                    {
                        player.ChatMessage(GetLang("ClanOwnerNotFoundPlayer", player.UserIDString));
                        return;
                    }
                    
                    
                    var clan = FindClanByUser(player.userID);

                    if (clan == null)
                    {
                        player.ChatMessage(GetLang("PlayerNotClan", player.UserIDString));
                        return;
                    }
                    
                    if (!clan.IsOwner(player.userID))
                    {
                        player.ChatMessage(GetLang("PlayerNotOwner", player.UserIDString));
                        return;
                    }
                    
                    string name = string.Join(" ", args.Skip(1));

                    var targetPlayer = BasePlayer.Find(name);
                    if (targetPlayer == null)
                    {
                        player.ChatMessage(GetLang("PlayerNotFound", player.UserIDString, name));
                        return;
                    }

                    if (player == targetPlayer) return;

                    if (!clan.IsMember(targetPlayer.userID))
                    {
                        player.ChatMessage(GetLang("TargetNotClan", player.UserIDString, targetPlayer.displayName));
                        return;
                    }
                    
                    clan.SetOwner(targetPlayer.userID);

                    targetPlayer.ChatMessage(GetLang("PSetLeader", targetPlayer.UserIDString));
                    
                    player.ChatMessage(GetLang("PGiveLeader", player.UserIDString));
                    
                    break;
                }

                #endregion

                #region Task
                
                case "task":
                {
                    if (args.Length < 1)
                    {
                        player.ChatMessage(GetLang("ClanTaskNotLength", player.UserIDString));
                        return;
                    }
                    var clan = FindClanByUser(player.userID);

                    if (clan == null)
                    {
                        player.ChatMessage(GetLang("PlayerNotClan", player.UserIDString));
                        return;
                    }
                    
                    if (!clan.IsOwner(player.userID))
                    {
                        player.ChatMessage(GetLang("PlayerNotOwner", player.UserIDString));
                        return;
                    }
                    
                    string task = string.Join(" ", args.Skip(1));
                    if (string.IsNullOrEmpty(task))
                        task = string.Empty;

                    clan.Task = task;
                    
                    player.ChatMessage(GetLang("ClanTask", player.UserIDString));
                    
                    break;
                }

                #endregion

                #region FF

                case "ff":
                {
                    var clan = FindClanByUser(player.userID);
                    if (clan == null)
                    {
                        player.ChatMessage(GetLang("PlayerNotClan", player.UserIDString));
                        return;
                    }
                    clan.ChangeFriendlyFire(player);

                    bool valueBool = clan.GetValueFriendlyFire(player);

                    string langMessage = valueBool ? "ClanFFActivate" : "ClanFFDeactivation";
                    
                    player.ChatMessage(GetLang(langMessage, player.UserIDString));
                    
                    break;
                }

                #endregion
                
            }
        }

        #endregion

        [ConsoleCommand("clan.changeowner")]
        void ChangeOwner(ConsoleSystem.Arg args)
        {
            if (!args.IsAdmin) return;
            if (args.Args.Length < 2)
            {
                SendReply(args, "clan.changeowner <tag> <nickname/steamid>");
                return;
            }
            var findClan = FindClanByTag(args.Args[0]);
            if (findClan == null) return;

            var playerCovalence = covalence.Players.FindPlayer(args.Args[1]);
            if (playerCovalence == null)
            {
                SendReply(args, $"Игрок {args.Args[1]} не найден!");
                return;
            }

            if (!findClan.IsMember(ulong.Parse(playerCovalence.Id)))
            {
                SendReply(args, $"Игрок {playerCovalence.Name} не состоит в клане!");
                return;
            }
            findClan.SetOwner(ulong.Parse(playerCovalence.Id));
            
            SendReply(args, $"Игрок {playerCovalence.Name} был установлен новым главой клана {findClan.ClanTag}!");
        }

        #region ConsoleComand

        [ConsoleCommand("UI_ClanHandler")]
        void ClanUIHandler(ConsoleSystem.Arg args)
        {
            BasePlayer player = args.Player();

            switch (args.Args[0])
            {
                case "myclan":
                    MainUI(player, "MyClan");
                    break;
                case "clantop":
                    MainUI(player, "ClanTop");
                    break;
                case "invite":
                    MainUI(player, "Invite");
                    break;
                case "disband":
                    var target2 = args.Args[1];

                    var container2 = new CuiElementContainer();

                    CuiHelper.DestroyUi(player, $"PanelParent{target2}");
                    container2.Add(new CuiPanel
                    {
                        Image = { Color = $"0 0 0 0" },
                        RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1" }
                    }, $"Avatar{target2}", $"PanelParent{target2}");

                    container2.Add(new CuiButton
                    {
                        RectTransform =
                                    {
                                        AnchorMin = $"0 0",
                                        AnchorMax = $"1 0",
                                        OffsetMin = "0 15",
                                        OffsetMax = "0 26"
                                    },
                        Button =
                                    {
                                        Color = $"{HexToRustFormat("#1F1F1FFF")}",
                                        Command = $"clan disband",
                                    },
                        Text =
                        {
                            Text = "Выйти?",
                            Align = TextAnchor.MiddleCenter,
                            Color = "1 1 1 0.7",
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 7
                        }
                    }, $"PanelParent{target2}", $"KickPanel");

                    container2.Add(new CuiButton
                    {
                        RectTransform =
                                    {
                                        AnchorMin = $"0 0",
                                        AnchorMax = $"1 0",
                                        OffsetMin = "0 0",
                                        OffsetMax = "0 11"
                                    },
                        Button =
                                    {
                                        Color = $"{HexToRustFormat("#1F1F1FFF")}",
                                        Close = $"PanelParent{target2}",
                                    },
                        Text =
                        {
                            Text = "Отмена",
                            Align = TextAnchor.MiddleCenter,
                            Color = "1 1 1 0.7",
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 7
                        }
                    }, $"PanelParent{target2}", $"CancelPanel");

                    CuiHelper.AddUi(player, container2);
                    break;
                case "leave":

                    var target1 = args.Args[1];

                    var container1 = new CuiElementContainer();

                    CuiHelper.DestroyUi(player, $"PanelParent{target1}");
                    container1.Add(new CuiPanel
                    {
                        Image = { Color = $"0 0 0 0" },
                        RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1" }
                    }, $"Avatar{target1}", $"PanelParent{target1}");

                    container1.Add(new CuiButton
                    {
                        RectTransform =
                                    {
                                        AnchorMin = $"0 0",
                                        AnchorMax = $"1 0",
                                        OffsetMin = "0 15",
                                        OffsetMax = "0 26"
                                    },
                        Button =
                                    {
                                        Color = $"{HexToRustFormat("#1F1F1FFF")}",
                                        Command = $"clan leave",
                                    },
                        Text =
                        {
                            Text = "Выйти?",
                            Align = TextAnchor.MiddleCenter,
                            Color = "1 1 1 0.7",
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 7
                        }
                    }, $"PanelParent{target1}", $"KickPanel");

                    container1.Add(new CuiButton
                    {
                        RectTransform =
                                    {
                                        AnchorMin = $"0 0",
                                        AnchorMax = $"1 0",
                                        OffsetMin = "0 0",
                                        OffsetMax = "0 11"
                                    },
                        Button =
                                    {
                                        Color = $"{HexToRustFormat("#1F1F1FFF")}",
                                        Close = $"PanelParent{target1}",
                                    },
                        Text =
                        {
                            Text = "Отмена",
                            Align = TextAnchor.MiddleCenter,
                            Color = "1 1 1 0.7",
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 7
                        }
                    }, $"PanelParent{target1}", $"CancelPanel");

                    CuiHelper.AddUi(player, container1);
                    break;
                case "kick":

                    var target = args.Args[1];

                    var container = new CuiElementContainer();

                    CuiHelper.DestroyUi(player, $"PanelParent{target}");
                    container.Add(new CuiPanel
                    {
                        Image = { Color = $"0 0 0 0" },
                        RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1" }
                    }, $"Avatar{target}", $"PanelParent{target}");

                    container.Add(new CuiButton
                    {
                        RectTransform =
                                    {
                                        AnchorMin = $"0 0",
                                        AnchorMax = $"1 0",
                                        OffsetMin = "0 15",
                                        OffsetMax = "0 26"
                                    },
                        Button =
                                    {
                                        Color = $"{HexToRustFormat("#1F1F1FFF")}",
                                        Command = $"clan kick {target}",
                                    },
                        Text =
                        {
                            Text = "Выгнать?",
                            Align = TextAnchor.MiddleCenter,
                            Color = "1 1 1 0.7",
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 7
                        }
                    }, $"PanelParent{target}", $"KickPanel");

                    container.Add(new CuiButton
                    {
                        RectTransform =
                                    {
                                        AnchorMin = $"0 0",
                                        AnchorMax = $"1 0",
                                        OffsetMin = "0 0",
                                        OffsetMax = "0 11"
                                    },
                        Button =
                                    {
                                        Color = $"{HexToRustFormat("#1F1F1FFF")}",
                                        Close = $"PanelParent{target}",
                                    },
                        Text =
                        {
                            Text = "Отмена",
                            Align = TextAnchor.MiddleCenter,
                            Color = "1 1 1 0.7",
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 7
                        }
                    }, $"PanelParent{target}", $"CancelPanel");

                    CuiHelper.AddUi(player, container);
                    break;
                case "search":
                    LoadedPlayers(player, args.Args[1]);
                    break;
                default:
                    break;
            }
        }

        #endregion
        
        #endregion
        
        #region Configuration
        
        
        private static Configuration config;

        protected override void LoadDefaultConfig()
        {
            config = Configuration.DefaultConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<Configuration>();

            Config.WriteObject(config, true);
        }
        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }
        
        private class Configuration
        {
            #region Point

            public class PointSettings
            {
                [JsonProperty("Список добываемых предметов и выдаваемое количество очков")]
                public Dictionary<string, int> _gatherPoint;
            
                [JsonProperty("Количество очков за сбитие вертолета")]
                public int Helicopter = 1000;

                [JsonProperty("Количество очков за взрыв танка")]
                public int BradleyAPC = 650;

                [JsonProperty("Добавляемое количество очков при убийстве")]
                public int Kill = 20;

                [JsonProperty("Отбираемое количество очков при смерти")]
                public int Death = 10;

                [JsonProperty("Отбираемое количество очков при суициде")]
                public int Suicide = 30;
            
            }

            #endregion

            #region Main

            public class ClanSettings
            {
                [JsonProperty("Команды")] 
                public List<string> CommandsMain;

                [JsonProperty("Команды для открытия топа")]
                public List<string> CommandsTOP;


                [JsonProperty("Минимальная длина названия клана")]
                public int MinNameLength = 2;
                
                [JsonProperty("Максимальная длина название клана")]
                public int MaxNameLength = 15;
                
                [JsonProperty("Максимальное количество участников в клане")]
                public int MaxCountClan = 7;
                
                [JsonProperty("Максимальное количество модераторов в клане")]
                public int MaxCountModeratorClan = 2;
                
                [JsonProperty("Включить клан теги у игроков?")]
                public bool TagInPlayer = true;
                
                
                [JsonProperty("Автоматическое создание игровой тимы")]
                public bool EnableTeam = true;

                [JsonProperty("Очищать дату при вайпе сервера?")]
                public bool ClearWipe = true;

                [JsonProperty("Включить автоматическую авторизацию в дверях ( соло замки )")]
                public bool AutomaticLock = true;
                
                [JsonProperty("Включить автоматическую авторизацию в шкафах?")]
                public bool AutomaticCupboard = true;

                [JsonProperty("Включить автоматическую авторизацию в турелях?")]
                public bool AutomaticTurret = true;
                
                [JsonProperty("Запретные клан теги")] 
                public List<string> ForbiddenTag;

                [JsonProperty("Начальные добываемые предметы")]
                public List<string> GatherDictionary;
                
                [JsonProperty("Начальныая одежда")]
                public Dictionary<string, ulong> WearDictionary;
                

            }

            #endregion
            
            #region Prize

            public class GameStores
            {
                public class APISettings
                {
                    [JsonProperty("ИД магазина в сервисе")]
                    public string ShopID = "UNDEFINED";
                    [JsonProperty("Секретный ключ (не распространяйте его)")]
                    public string SecretKey = "UNDEFINED";
                }
                
                [JsonProperty("Включить авто выдачу призов при вайпе сервера?")]
                public bool EnablePrize = true;
                
                [JsonProperty("Настройка подключение к GS")]
                public APISettings GSSettings = new APISettings();
                
                [JsonProperty("Место в топе клана и выдаваемый баланс каждому игроку из клана")]
                public Dictionary<int, uint> RewardDictionary;
            }

            #endregion
            
            #region Skin

            public class SkinSettings
            {
                [JsonProperty("Включить ли подгрузку скинов в конфиг при загрузке плагина?")]
                public bool LoadSkinList = true;
                
                [JsonProperty("Скины предметов")] 
                public Dictionary<string, List<ulong>> ItemsSkins;
            }

            #endregion
            
            #region Stats

            public class CollectionStats
            {
                [JsonProperty("Добыча")] 
                public bool Gather = true;

                [JsonProperty("Убийства")] 
                public bool Kill = true;

                [JsonProperty("Лутание")] 
                public bool Loot = true;

                [JsonProperty("Уничтожение объектов ( бочки, вертолет, танк )")]
                public bool Entities = false;
            }

            #endregion

            #region Limit

            public class LimitSettings
            {
                [JsonProperty("Включить лимит кланов?")]
                public bool EnableLimit = true;

                [JsonProperty("Лимит на установку турелей")]
                public int  LTurret = 100;

                [JsonProperty("Лимит на установку ПВО")]
                public int  LSamSite = 10;

                [JsonProperty("Лимит на установку шкафов")]
                public int  LCupboard = 150;
            }

            #endregion
            
            #region Variables

            [JsonProperty("Основная настройка плагина")]
            public ClanSettings Main = new ClanSettings();

            [JsonProperty("Настройка системы очков")]
            public PointSettings Point = new PointSettings();

            [JsonProperty("Настройка системы лимитов")]
            public LimitSettings Limit = new LimitSettings();

            [JsonProperty("Настройка сбора статистики и очков")]
            public CollectionStats Stats = new CollectionStats();

            [JsonProperty("Настройка призов")] 
            public GameStores Prize = new GameStores();
            
            [JsonProperty("Настройка скинов")] 
            public SkinSettings ItemSkin = new SkinSettings();

            #endregion

            #region Loaded

            public static Configuration DefaultConfig()
            {
                return new Configuration()
                {
                    Prize = new GameStores()
                    {
                        RewardDictionary = new Dictionary<int, uint>()
                        {
                            [1] = 100,
                            [2] = 80,
                            [3] = 60,
                            [4] = 40,
                            [5] = 20
                        }
                    },
                    Stats = new CollectionStats(),
                    Main = new ClanSettings()
                    {
                        CommandsMain = new List<string>()
                        {
                            "clan",
                            "clans",
                        },
                        CommandsTOP = new List<string>()
                        {
                            "ctop",
                            "top"
                        },
                        ForbiddenTag = new List<string>()
                        {
                            "[admin]",
                            "[moderator]",
                            "[god]",
                            "[adminteam]"
                        },
                        GatherDictionary = new List<string>()
                        {
                            "wood",
                            "metal.ore",
                            "stones",
                            "sulfur.ore",
                            "hq.metal.ore",
                            "fat.animal",
                            "cloth",
                            "leather",
                            "lootbox",
                         },
                        WearDictionary = new Dictionary<string, ulong>()
                        {
                            ["metal.facemask"] = 0,
                            ["metal.plate.torso"] = 0,
                            ["roadsign.kilt"] = 0,
                            ["hoodie"] = 0,
                            ["pants"] = 0,
                            ["shoes.boots"] = 0,
                            ["rifle.ak"] = 0,
                         }
                    },
                    Point = new PointSettings()
                    {
                        _gatherPoint = new Dictionary<string, int>()
                        {
                            ["wood"] = 2,
                            ["stones"] = 2,
                            ["sulfur.ore"] = 2,
                            ["metal.ore"] = 2,
                            ["hq.metal.ore"] = 1,
                            ["lootbox"] = 1,
                        }
                    },
                    ItemSkin = new SkinSettings()
                    {
                        ItemsSkins = new Dictionary<string, List<ulong>>()
                    }
                };
            }

            #endregion
        }

        #endregion

        #region API

        private ClanData FindClanByUser(ulong playerID)
        {
            return _clanList.FirstOrDefault(clan => clan.IsMember(playerID));
        }
        
        private ClanData FindClanByTag(string tag)
        {
            return _clanList.FirstOrDefault(clan => clan.ClanTag.ToLower() == tag.ToLower());
        }

        private ClanData FindClanByInvite(ulong playerID)
        {
            return _clanList.FirstOrDefault(clan => clan.PendingInvites.Contains(playerID));
        }

        private List<ulong> ApiGetClanMembers(ulong playerId)
        {
            return (List<ulong>)(FindClanByUser(playerId)?.Members.Keys.ToList() ?? new List<ulong>());
        }
        
        private List<ulong> ApiGetClanMembers(string tag)
        {
            return (List<ulong>)(FindClanByTag(tag)?.Members.Keys.ToList() ?? new List<ulong>());
        }
        
        private bool ApiIsClanMember(ulong playerID, ulong friendID)
        {
            var clan = FindClanByUser(playerID);
            if (clan == null) return false;
            if (clan.IsMember(friendID))
                return true;
            return false;
        }
        
        private object IsClanMember(string userID, string targetID)
        {
            var clan = FindClanByUser(ulong.Parse(userID));
            if (clan == null) return null;
            if (clan.IsMember(ulong.Parse(targetID)))
                return true;
            return null;
        }
        
        private bool ApiIsClanOwner(ulong playerID)
        {
            var clan = FindClanByUser(playerID);
            if (clan == null) return false;
            if (clan.IsOwner(playerID))
                return true;
            return false;
        }
        
        private bool ApiIsClanModeratorAndOwner(ulong playerID)
        {
            var clan = FindClanByUser(playerID);
            if (clan == null) return false;
            if (clan.IsModerator(playerID))
                return true;
            return false;
        }
        
        private int? ApiGetMember(ulong playerID)
        {
            var clan = FindClanByUser(playerID);
            if (clan == null) return null;
            return clan.Members.Count;
        }
        
        private string ApiGetClanTag(ulong playerID)
        {
            var clan = FindClanByUser(playerID);
            if (clan == null) return null;
            return clan.ClanTag;
        }
        
        private int? ApiGetClanScores(ulong playerID)
        {
            var clan = FindClanByUser(playerID);
            if (clan == null) return null;
            return clan.TotalScores;
        }

        private void ApiScoresAddClan(ulong playerID, int scores)
        {
            var clan = FindClanByUser(playerID);
            if (clan == null) return;
            clan.TotalScores += scores;
            clan.Members[playerID].MemberScores += scores;
        }

        private void ApiScoresAddClan(string tag, int scores)
        {
            var clan = FindClanByTag(tag);
            if (clan == null) return;
            clan.TotalScores += scores;
        }

        private void ApiScoresRemove(ulong playerID, int scores)
        {
            var clan = FindClanByUser(playerID);
            if (clan == null) return;
            clan.TotalScores -= scores;
            clan.Members[playerID].MemberScores -= scores;
        }
        
        private void ApiScoresRemoveClan(string tag, int scores)
        {
            var clan = FindClanByTag(tag);
            if (clan == null) return;
            clan.TotalScores -= scores;
        }
        
        #region API TournamentClan
        
        // Edited: 1.1.1 version

        private void ApiScoresRemoveTournament(string destroyClan, ulong initiatorPlayer, int percent)
        {
            var clanDestroy = FindClanByTag(destroyClan);
            if (clanDestroy == null) return;


            int SumPercent = (clanDestroy.TotalScores / 100) * percent;

            clanDestroy.TotalScores -= SumPercent;
            
            var clanInitiator = FindClanByUser(initiatorPlayer);
            if (clanInitiator == null) return;

            clanInitiator.TotalScores += SumPercent;
            
        }

        #endregion
        
        private List<string> ApiGetClansTags(bool key = true)
        {
            if (_clanList.Count == 0) return null;

            List<string> clantagAll = new List<string>();

            foreach (var clan in _clanList)
            {
                string tagClan = key == true ? clan.ClanTag.ToUpper() : clan.ClanTag;
            
                if (!clantagAll.Contains(tagClan))
                    clantagAll.Add(tagClan);
            }

            return clantagAll;
        }

        private List<ulong> ApiGetActiveClanMembers(BasePlayer player)
        {
            var clan = FindClanByUser(player.userID);
            if (clan == null) return null;
            List<ulong> list = clan.Members.Keys.Where(p => BasePlayer.Find(p.ToString()) != null && p != player.userID).ToList();
            if (list.Count <= 0) return null;
            return list;
        }

        private List<string> GetClanMembers(ulong playerID) =>
            (List<string>)ApiGetActiveClanMembersUserId(playerID).Select(p => p.ToString()) ?? new List<string>();

        private List<ulong> ApiGetActiveClanMembersUserId(ulong playerID)
        {
            var clan = FindClanByUser(playerID);
            if (clan == null) return null;
            List<ulong> list = clan.Members.Keys.Where(p => BasePlayer.Find(p.ToString()) != null & p != playerID).ToList();
            if (list.Count <= 0) return null;
            return list;
        }


        private Dictionary<string, int> GetTops()
        {

            Dictionary<string, int> dictionaryTops = new Dictionary<string, int>();

            int index = 1;

            foreach (var check in _clanList.OrderByDescending(p => p.TotalScores).Take(3))
            {
                if (!dictionaryTops.ContainsKey(check.ClanTag))
                    dictionaryTops.Add(check.ClanTag, check.TotalScores);
                
            }

            return dictionaryTops;
        }

        private List<ulong> ApiGetActiveClanMembersTag(string tag)
        {
            var clan = FindClanByTag(tag);
            if (clan == null) return null;
            List<ulong> list = clan.Members.Keys.Where(p => BasePlayer.Find(p.ToString()) != null).ToList();
            if (list.Count <= 0) return null;
            return list;
        }
        
        private JObject GetClan(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var clan = FindClanByTag(tag);
            
            if (clan == null) return null;
            
            return clan.ToJObject();
        }

        #endregion
    }
}