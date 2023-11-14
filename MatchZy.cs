﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Core.Attributes;


namespace MatchZy
{
    [MinimumApiVersion(30)]
    public partial class MatchZy : BasePlugin
    {

        public override string ModuleName => "MatchZy";
        public override string ModuleVersion => "0.3.0-alpha";

        public override string ModuleAuthor => "WD- (https://github.com/shobhit-pathak/)";

        public override string ModuleDescription => "A plugin for running and managing CS2 practice/pugs/scrims/matches!";

        public string chatPrefix = $"[{ChatColors.Green}MatchZy{ChatColors.Default}]";

        // Match phase data
        public bool isPractice = false;
        public bool readyAvailable = true;
        public bool matchStarted = false;
        public bool isWarmup = true;
        public bool isKnifeRound = false;
        public bool isSideSelectionPhase = false;
        public bool isMatchLive = false;
        public long liveMatchId = -1;

        // Pause Data
        public bool isPaused = false;
        public Dictionary<string, object> unpauseData = new Dictionary<string, object> {
            { "ct", false },
            { "t", false },
            { "pauseTeam", "" }
        };

        // Knife Data
        public int knifeWinner = 0;
        public string knifeWinnerName = "";

        // Players Data (including admins)
        public int connectedPlayers = 0;
        private Dictionary<int, bool> playerReadyStatus = new Dictionary<int, bool>();
        private Dictionary<int, CCSPlayerController> playerData = new Dictionary<int, CCSPlayerController>();

        // Admin Data
        private Dictionary<string, string> loadedAdmins = new Dictionary<string, string>();

        // Timers
        public CounterStrikeSharp.API.Modules.Timers.Timer? unreadyPlayerMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? sideSelectionMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? pausedStateTimer = null;

        // Each message is kept in chat display for ~13 seconds, hence setting default chat timer to 12 seconds.
        // Configurable using matchzy_chat_messages_timer_delay <seconds>
        public int chatTimerDelay = 12;

        // Game Config
        public bool isKnifeRequired = true;
        public int minimumReadyRequired = 2; // Number of ready players required start the match. If set to 0, all connected players have to ready-up to start the match.

        // User command - action map
        public Dictionary<string, Action<CCSPlayerController?, CommandInfo?>>? commandActions;

        // SQLite Database 
        private Database database;
    
        public override void Load(bool hotReload) {
            
            LoadAdmins();

            database = new Database();
            database.InitializeDatabase(ModuleDirectory);

            // This sets default config ConVars
            Server.ExecuteCommand("execifexists MatchZy/config.cfg");

            if (!hotReload) {
                StartWarmup();
            } else {
                // Pluign should not be reloaded while a match is live (this would messup with the match flags which were set)
                // Only hot-reload the plugin if you are testing something and don't want to restart the server time and again.
                UpdatePlayersMap();
            }

            commandActions = new Dictionary<string, Action<CCSPlayerController?, CommandInfo?>> {
                { ".ready", (player, commandInfo) => OnPlayerReady(player, commandInfo) },
                { ".r", (player, commandInfo) => OnPlayerReady(player, commandInfo) },
                { ".unready", (player, commandInfo) => OnPlayerUnReady(player, commandInfo) },
                { ".ur", (player, commandInfo) => OnPlayerUnReady(player, commandInfo) },
                { ".stay", (player, commandInfo) => OnTeamStay(player, commandInfo) },
                { ".switch", (player, commandInfo) => OnTeamSwitch(player, commandInfo) },
                { ".tech", (player, commandInfo) => OnPauseCommand(player, commandInfo) },
                { ".pause", (player, commandInfo) => OnPauseCommand(player, commandInfo) },
                { ".unpause", (player, commandInfo) => OnUnpauseCommand(player, commandInfo) },
                { ".tac", (player, commandInfo) => OnTacCommand(player, commandInfo) },
                { ".knife", (player, commandInfo) => OnKifeCommand(player, commandInfo) },
                { ".start", (player, commandInfo) => OnStartCommand(player, commandInfo) },
                { ".restart", (player, commandInfo) => OnRestartMatchCommand(player, commandInfo) },
                { ".settings", (player, commandInfo) => OnMatchSettingsCommand(player, commandInfo) },
                { ".reload_admins", (player, commandInfo) => OnReloadAdmins(player, commandInfo) },
                { ".prac", (player, commandInfo) => OnPracCommand(player, commandInfo) },
                { ".bot", (player, commandInfo) => OnBotCommand(player, commandInfo) },
                { ".nobots", (player, commandInfo) => OnNoBotsCommand(player, commandInfo) },
                { ".match", (player, commandInfo) => OnMatchCommand(player, commandInfo) },
                { ".exitprac", (player, commandInfo) => OnMatchCommand(player, commandInfo) },
                { ".stop", (player, commandInfo) => OnStopCommand(player, commandInfo) }
            };

            RegisterEventHandler<EventPlayerConnectFull>((@event, info) => {
                Log($"[FULL CONNECT] Player ID: {@event.Userid.UserId}, Name: {@event.Userid.PlayerName} has connected!");
                var player = @event.Userid;
                player.PrintToChat($"{chatPrefix} Welcome to the server!");
                if (@event.Userid.UserId.HasValue) {
                    
                    playerData[@event.Userid.UserId.Value] = @event.Userid;
                    connectedPlayers++;
                    if (readyAvailable && !matchStarted) {
                        playerReadyStatus[@event.Userid.UserId.Value] = false;
                    } else {
                        playerReadyStatus[@event.Userid.UserId.Value] = true;
                    }
                }
                // May not be required, but just to be on safe side so that player data is properly updated in dictionaries
                UpdatePlayersMap();

                if (readyAvailable && !matchStarted) {
                    // Start Warmup when first player connect and match is not started.
                    if (GetRealPlayersCount() == 1) {
                        Log($"[FULL CONNECT] First player has connected, starting warmup!");
                        StartWarmup();
                    }
                }
                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerDisconnect>((@event, info) => {
                Log($"[EventPlayerDisconnect] Player ID: {@event.Userid.UserId}, Name: {@event.Userid.PlayerName} has disconnected!");
                if (@event.Userid.UserId.HasValue) {
                    if (playerReadyStatus.ContainsKey(@event.Userid.UserId.Value)) {
                        playerReadyStatus.Remove(@event.Userid.UserId.Value);
                        connectedPlayers--;
                    }
                    if (playerData.ContainsKey(@event.Userid.UserId.Value)) {
                        playerData.Remove(@event.Userid.UserId.Value);
                    }
                }

                return HookResult.Continue;
            });

            RegisterListener<Listeners.OnClientDisconnectPost>(playerSlot => { 
               // May not be required, but just to be on safe side so that player data is properly updated in dictionaries
                UpdatePlayersMap();
            });

            RegisterEventHandler<EventCsWinPanelRound>((@event, info) => {
                Log($"[EventCsWinPanelRound PRE] finalEvent: {@event.FinalEvent}");
                if (isKnifeRound && matchStarted) {
                    HandleKnifeWinner(@event);
                }
                return HookResult.Continue;
            }, HookMode.Pre);

            RegisterEventHandler<EventCsWinPanelMatch>((@event, info) => {
                Log($"[EventCsWinPanelMatch]");
                HandleMatchEnd();
                // ResetMatch();
                return HookResult.Continue;
            });

           RegisterEventHandler<EventRoundStart>((@event, info) => {
                HandlePostRoundStartEvent(@event);
                return HookResult.Continue;
            });

            RegisterEventHandler<EventRoundEnd>((@event, info) => {
                Log($"[EventRoundEnd PRE] Winner: {@event.Winner}, Reason: {@event.Reason}");
                if (isKnifeRound) {
                    @event.Winner = knifeWinner;
                    int finalEvent = 10;
                    if (knifeWinner == 3) {
                        finalEvent = 8;
                    } else if (knifeWinner == 2) {
                        finalEvent = 9;
                    }
                    @event.Reason = finalEvent;
                    Log($"[EventRoundEnd Updated] Winner: {@event.Winner}, Reason: {@event.Reason}");
                    isSideSelectionPhase = true;
                    isKnifeRound = false;
                    StartAfterKnifeWarmup();
                }
                return HookResult.Continue;
            }, HookMode.Pre);

           RegisterEventHandler<EventRoundEnd>((@event, info) => {
                Log($"[EventRoundEnd POST] Winner: {@event.Winner}, Reason: {@event.Reason}");
                HandlePostRoundEndEvent(@event);
                return HookResult.Continue;
            }, HookMode.Post);

            RegisterEventHandler<EventMapShutdown>((@event, info) => {
                Log($"[EventMapShutdown] Resetting match!");
                ResetMatch();
                return HookResult.Continue;
            });

            RegisterListener<Listeners.OnMapStart>(mapName => { 
                Log($"[Listeners.OnMapStart] Resetting match!");
                ResetMatch();
            });

            RegisterListener<Listeners.OnMapEnd>(() => {
                Log($"[Listeners.OnMapEnd] Resetting match!");
                ResetMatch();
            });

            RegisterEventHandler<EventPlayerDeath>((@event, info) => {
                // Setting money back to 16000 when a player dies in warmup
                var player = @event.Userid;
                if (isWarmup) {
                    if (player.InGameMoneyServices != null) player.InGameMoneyServices.Account = 16000;
                }
                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerHurt>((@event, info) =>
			{
				CCSPlayerController attacker = @event.Attacker;

				if (!attacker.IsValid || attacker.IsBot && !(@event.DmgHealth > 0 || @event.DmgArmor > 0))
					return HookResult.Continue;
                if (matchStarted) {
                    if (@event.Userid.TeamNum != attacker.TeamNum)
                    {
                        int targetId = (int)@event.Userid.UserId!;

                        UpdatePlayerDamageInfo(@event, targetId);
                    }
                }

				return HookResult.Continue;
			});

            RegisterEventHandler<EventPlayerChat>((@event, info) => {

                int currentVersion = Api.GetVersion();
                int index = @event.Userid;
                // From APIVersion 50 and above, EventPlayerChat userid property will be a "slot", rather than an entity index 
                // Player index is slot + 1
                if (currentVersion >= 50)
                {
                    index += 1;
                }
                var playerUserId = NativeAPI.GetUseridFromIndex(index);
                Log($"[EventPlayerChat] UserId(Index): {index} playerUserId: {playerUserId} Message: {@event.Text}");

                var originalMessage = @event.Text.Trim();
                var message = @event.Text.Trim().ToLower();

                CCSPlayerController? player = null;
                if (playerData.ContainsKey(playerUserId)) {
                    player = playerData[playerUserId];
                }

                if (player == null) {
                    // Somehow we did not had the player in playerData, hence updating the maps again before getting the player
                    UpdatePlayersMap();
                    player = playerData[playerUserId];
                }

                // Handling player commands
                if (commandActions.ContainsKey(message)) {
                    commandActions[message](player, null);
                }

                if (message.StartsWith(".map")) {
                    string command = ".map";
                    string mapName = message.Substring(command.Length).Trim();
                    HandleMapChangeCommand(player, mapName);
                }
                if (message.StartsWith(".readyrequired")) {
                    string command = ".readyrequired";
                    string commandArg = message.Substring(command.Length).Trim();

                    HandleReadyRequiredCommand(player, commandArg);
                }

                if (message.StartsWith(".restore")) {
                    string command = ".restore";
                    string commandArg = message.Substring(command.Length).Trim();

                    HandleRestoreCommand(player, commandArg);
                }
                if (originalMessage.StartsWith(".asay")) {
                    string command = ".asay";
                    string commandArg = originalMessage.Substring(command.Length).Trim();

                    if (IsPlayerAdmin(player)) {
                        if (commandArg != "") {
                            Server.PrintToChatAll($"[{ChatColors.Red}ADMIN{ChatColors.Default}] {commandArg}");
                        } else {
                            ReplyToUserCommand(player, "Usage: .asay <message>");
                        }
                    } else {
                        SendPlayerNotAdminMessage(player);
                    }
                }
                if (message.StartsWith(".spawn")) {
                    string command = ".spawn";
                    string commandArg = message.Substring(command.Length).Trim();

                    HandleSpawnCommand(player, commandArg, player.TeamNum, "spawn");
                }
                if (message.StartsWith(".ctspawn")) {
                    string command = ".ctspawn";
                    string commandArg = message.Substring(command.Length).Trim();

                    HandleSpawnCommand(player, commandArg, (byte)CsTeam.CounterTerrorist, "ctspawn");
                }
                if (message.StartsWith(".tspawn")) {
                    string command = ".tspawn";
                    string commandArg = message.Substring(command.Length).Trim();

                    HandleSpawnCommand(player, commandArg, (byte)CsTeam.Terrorist, "tspawn");
                }
                if (originalMessage.StartsWith(".team1")) {
                    string command = ".team1";
                    string commandArg = originalMessage.Substring(command.Length).Trim();

                    HandleTeamNameChangeCommand(player, commandArg, 1);
                }
                if (originalMessage.StartsWith(".team2")) {
                    string command = ".team2";
                    string commandArg = originalMessage.Substring(command.Length).Trim();

                    HandleTeamNameChangeCommand(player, commandArg, 2);
                }
                if (originalMessage.StartsWith(".rcon")) {
                    string command = ".rcon";
                    string commandArg = originalMessage.Substring(command.Length).Trim();
                    if (IsPlayerAdmin(player)) {
                        Server.ExecuteCommand(commandArg);
                        ReplyToUserCommand(player, "Command sent successfully!");
                    } else {
                        SendPlayerNotAdminMessage(player);
                    }
                }

                return HookResult.Continue;
            });

            Console.WriteLine("[MatchZy LOADED] MatchZy by WD- (https://github.com/shobhit-pathak/)");
        }
    }
}