using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.RegularExpressions;


namespace MatchZy
{
    public partial class MatchZy
    {

        [ConsoleCommand("matchzy_knife_enabled_default", "Whether knife round is enabled by default or not. Default value: true")]
        public void MatchZyKnifeConvar(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string args = command.ArgString;

            isKnifeRequired = bool.TryParse(args, out bool isKnifeRequiredValue) ? isKnifeRequiredValue : args != "0" && isKnifeRequired;
        }

        [ConsoleCommand("matchzy_minimum_ready_required", "Minimum ready players required to start the match. Default: 1")]
        public void MatchZyMinimumReadyRequired(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            // Since there is already a console command for this purpose, we will use the same.   
            OnReadyRequiredCommand(player, command);
        }

        [ConsoleCommand("matchzy_demo_path", "Path of folder in which demos will be saved. If defined, it must not start with a slash and must end with a slash. Set to empty string to use the csgo root.")]
        public void MatchZyDemoPath(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            if (command.ArgCount == 2)
            {
                string path = command.ArgByIndex(1);
                if (path[0] == '/' || path[0] == '.' || path[^1] != '/' || path.Contains("//"))
                {
                    Log($"matchzy_demo_path must end with a slash and must not start with a slash or dot. It will be reset to an empty string! Current value: {demoPath}");
                }
                else
                {
                    demoPath = path;
                }
            }
        }

        [ConsoleCommand("matchzy_stop_command_available", "Whether .stop command is enabled or not (to restore the current round). Default value: false")]
        public void MatchZyStopCommandEnabled(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string args = command.ArgString;

            isStopCommandAvailable = bool.TryParse(args, out bool isStopCommandAvailableValue) ? isStopCommandAvailableValue : args != "0" && isStopCommandAvailable;
        }

        [ConsoleCommand("matchzy_pause_after_restore", "Whether to pause the match after a round is restored using matchzy. Default value: true")]
        public void MatchZyPauseAfterStopEnabled(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string args = command.ArgString;

            pauseAfterRoundRestore = bool.TryParse(args, out bool pauseAfterRoundRestoreValue) ? pauseAfterRoundRestoreValue : args != "0" && pauseAfterRoundRestore;
        }

        [ConsoleCommand("matchzy_chat_prefix", "Default value of chat prefix for MatchZy messages. Default value: [{Green}MatchZy{Default}]")]
        public void MatchZyChatPrefix(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;

            string args = command.ArgString.Trim();

            if (string.IsNullOrEmpty(args))
            {
                chatPrefix = $"[{ChatColors.Green}MatchZy{ChatColors.Default}]";
                return;
            }

            // Adding extra space before args if message starts with a color name
            // This is because colors cannot be applied from 1st character, hence we make first character as an empty space
            if (args.StartsWith('{')) args = " " + args;

            foreach (var field in typeof(ChatColors).GetFields())
            {
                string pattern = $"{{{field.Name}}}";
                string replacement = field.GetValue(null).ToString();

                // Create a case-insensitive regular expression pattern for the color name
                string patternIgnoreCase = Regex.Escape(pattern);
                args = Regex.Replace(args, patternIgnoreCase, replacement, RegexOptions.IgnoreCase);
            }
            chatPrefix = args;

            Log($"[PREFIX CHANGE] chatPrefix: {chatPrefix}");
        }

        [ConsoleCommand("matchzy_chat_messages_timer_delay", "Number of seconds of delay before sending reminder messages from MatchZy (like unready message, paused message, etc). Default: 12")]
        public void MatchZyChatMessagesTimerDelay(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                if (!string.IsNullOrWhiteSpace(commandArg))
                {
                    if (int.TryParse(commandArg, out int chatTimerDelayValue) && chatTimerDelayValue >= 0)
                    {
                        chatTimerDelay = chatTimerDelayValue;
                    }
                    else
                    {
                        ReplyToUserCommand(player, $"Invalid value for matchzy_chat_messages_timer_delay. Please specify a valid non-negative number.");
                    }
                }
            } else if (command.ArgCount == 1) {
                ReplyToUserCommand(player, $"matchzy_chat_messages_timer_delay = {chatTimerDelay}");
            }
        }

    }
}