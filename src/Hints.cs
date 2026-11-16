using System;
using System.Collections.Generic;
using System.Text;
using Sims3.Gameplay.Core;
using Sims3.SimIFace;
using Sims3.UI;

namespace ConsoleHints
{
    public class Entry
    {
        public string Name;
        public string Lower;
        public string Description;
        public bool NeedsTestingCheats;
    }

    // Hooks into the cheat console whenever it opens. Suggestions are written into the
    // console's own help box (the one "help" prints into), which sits right under the
    // input line and is normally hidden.
    public static class Hints
    {
        // Control IDs from CommandConsoleDialog.ControlID
        private const uint kInputId = 1u;
        private const uint kHelpBoxId = 2u;

        // ModalDialog.Triggers.kNOP. The OKCancelDialog trigger map binds Tab to it.
        private const uint kTabTrigger = 69926917u;

        // Down arrow isn't bound to anything in the game's dialog trigger map, so the
        // package ships its own little map (ConsoleHints.triggers) that binds it to this.
        private const string kTriggerMap = "ConsoleHints";
        private const uint kDownTrigger = 0x7c0d0001u;
        private const uint kVisibilityChangeEvent = 33u; // WindowBase.WindowBaseEvents

        private const int kMaxShown = 16;
        private const int kMaxDescription = 110;
        private const int kMaxTokens = 3000;

        private static CommandConsoleDialog sAttached;
        private static TextEdit sInput;
        private static TextEdit sBox;
        private static bool sShowing;

        private static List<Entry> sEntries = new List<Entry>();
        private static List<Entry> sMatches = new List<Entry>();
        private static int sTotalMatches;

        private static string sTyped = "";
        private static string sLastSet;
        private static int sCycle = -1;
        private static bool sBusy;

        // lower case name -> help text, only for names the game recognised
        private static Dictionary<string, string> sHelpCache = new Dictionary<string, string>();

        // These have to live in statics. Let one get garbage collected while the UI still
        // has it registered and the game thanks you with a crash to desktop. No error, no
        // log, just your desktop wallpaper. Ask me how I know.
        private static UIEventHandler<UITextChangeEventArgs> sTextHandler = OnTextChange;
        private static UIEventHandler<UITriggerEventArgs> sTriggerHandler = OnTrigger;
        private static UIEventHandler<UIMouseEventArgs> sMouseHandler = OnBoxMouseUp;
        private static UIEventHandler<UIVisibilityChangeEventArgs> sHiddenHandler = OnDialogVisibility;

        private static uint sTriggerHook;

        private static readonly char[] kSeparators =
            { ' ', '\t', '\r', '\n', ',', ';', '|', '(', ')', '[', ']', '<', '>' };
        private static readonly char[] kTrimChars = { ':', '.', '-', '"', '\'' };

        // Script mods can't write files, so there's no log. Errors from outside the console
        // are kept here and printed in the suggestion box the next time it opens.
        private static string sPendingError;

        public static void ReportError(string doing, Exception ex)
        {
            sPendingError = "Console Hints failed while " + doing + ": " + ex;
        }

        public static void Poll()
        {
            CommandConsoleDialog dialog = CommandConsoleDialog.sDialog;
            if (dialog == null)
            {
                if (sAttached != null)
                {
                    sAttached = null;
                    sInput = null;
                    sBox = null;
                    sShowing = false;
                }
                return;
            }

            if (!object.ReferenceEquals(dialog, sAttached))
            {
                Attach(dialog);
            }
        }

        // The game builds a brand new dialog every time the console is opened, so this
        // runs once per open. Nothing needs unhooking, the windows die with the dialog.
        private static void Attach(CommandConsoleDialog dialog)
        {
            sAttached = dialog;
            sInput = null;
            sBox = null;
            sShowing = false;
            sTyped = "";
            sLastSet = null;
            sCycle = -1;
            sMatches.Clear();

            Dialog window = dialog.mModalDialogWindow;
            if (window == null)
            {
                return;
            }

            TextEdit input = window.GetChildByID(kInputId, false) as TextEdit;
            TextEdit box = window.GetChildByID(kHelpBoxId, true) as TextEdit;
            if (input == null || box == null)
            {
                return;
            }
            sInput = input;
            sBox = box;

            try
            {
                BuildEntries();
            }
            catch (Exception ex)
            {
                ShowText("Console Hints couldn't read the command list: " + ex.Message);
            }

            if (sPendingError != null)
            {
                ShowText(sPendingError);
                sPendingError = null;
            }

            // The layout flags the help box as IgnoreMouse, so clicks go straight through
            // it unless that's switched off.
            box.IgnoreMouse = false;

            input.TextChange += sTextHandler;
            window.TriggerDown += sTriggerHandler;
            box.MouseUp += sMouseHandler;

            // One above the dialog's own hook (18) so the down arrow gets here first.
            // The hook has to come off again before the window is destroyed, and the
            // dialog being hidden is the last thing that happens while it still exists.
            sTriggerHook = window.AddTriggerHook(kTriggerMap, TriggerActivationMode.kPermanent, 19);
            UIManager.RegisterEvent<UIVisibilityChangeEventArgs>(window, kVisibilityChangeEvent, sHiddenHandler);
        }

        private static void BuildEntries()
        {
            Dictionary<string, Entry> byName = new Dictionary<string, Entry>();

            AddRegistry(byName, Commands.sGameCommands);
            AddRegistry(byName, Commands.sWorldCommands);

            // GetCommandList is what "help" prints. It covers native commands and anything
            // other mods registered. It comes back as one giant string, because of course
            // it does, and I'm not betting on how a 2009 engine decided to format it. So:
            // chop it into words and keep the ones GetCommandHelp admits to knowing.
            string list = CommandSystem.GetCommandList();
            if (!string.IsNullOrEmpty(list))
            {
                string[] tokens = list.Split(kSeparators, StringSplitOptions.RemoveEmptyEntries);
                int count = Math.Min(tokens.Length, kMaxTokens);
                for (int i = 0; i < count; i++)
                {
                    AddIfCommand(byName, tokens[i].Trim(kTrimChars));
                }
            }

            // Hidden commands never show up in the list above
            AddIfCommand(byName, "help");
            AddIfCommand(byName, "freeRealEstate");
            AddIfCommand(byName, "unlockOutfits");

            sEntries = new List<Entry>(byName.Values);
            sEntries.Sort(CompareByName);
        }

        private static void AddRegistry(Dictionary<string, Entry> byName, Commands.CommandRegistry registry)
        {
            // sWorldCommands is null at the main menu
            if (registry == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Commands.CommandInfo> pair in registry.mCommands)
            {
                string name = pair.Key;
                string lower = name.ToLower();
                if (byName.ContainsKey(lower))
                {
                    continue;
                }

                Entry entry = new Entry();
                entry.Name = name;
                entry.Lower = lower;
                entry.Description = Clean(pair.Value.mDescription);
                // Cheat type commands stay in the registry but only work once
                // testingcheatsenabled is on
                entry.NeedsTestingCheats = pair.Value.mType == Commands.CommandType.Cheat
                    && !Cheats.sTestingCheatsEnabled;
                byName.Add(lower, entry);
            }
        }

        private static void AddIfCommand(Dictionary<string, Entry> byName, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            string lower = name.ToLower();
            if (byName.ContainsKey(lower))
            {
                return;
            }

            string help = GetHelp(lower);
            if (help.Length == 0)
            {
                return;
            }

            Entry entry = new Entry();
            entry.Name = name;
            entry.Lower = lower;
            entry.Description = Clean(help);
            byName.Add(lower, entry);
        }

        private static string GetHelp(string lower)
        {
            string help;
            if (sHelpCache.TryGetValue(lower, out help))
            {
                return help;
            }

            help = CommandSystem.GetCommandHelp(lower);
            if (string.IsNullOrEmpty(help))
            {
                // Not cached: the name could be registered later (e.g. after turning
                // testing cheats on).
                return "";
            }

            sHelpCache.Add(lower, help);
            return help;
        }

        private static string Clean(string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                return "";
            }

            string text = description.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length > kMaxDescription)
            {
                text = text.Substring(0, kMaxDescription - 3) + "...";
            }
            return text;
        }

        private static int CompareByName(Entry a, Entry b)
        {
            return string.CompareOrdinal(a.Lower, b.Lower);
        }

        private static void OnTextChange(WindowBase sender, UITextChangeEventArgs args)
        {
            if (sBusy || sInput == null)
            {
                return;
            }

            try
            {
                string caption = sInput.Caption ?? "";

                // This is our own Tab completion coming back round. Ignore it, otherwise
                // the list would narrow to the completed name and Tab couldn't cycle.
                if (sLastSet != null && caption == sLastSet)
                {
                    return;
                }

                sLastSet = null;
                sTyped = caption;
                sCycle = -1;
                Refresh();
            }
            catch (Exception ex)
            {
                ShowText("Console Hints error: " + ex.Message);
            }
        }

        private static void OnTrigger(WindowBase sender, UITriggerEventArgs args)
        {
            bool next = args.TriggerCode == kTabTrigger || args.TriggerCode == kDownTrigger;
            if (!next || sInput == null || sMatches.Count == 0)
            {
                return;
            }

            try
            {
                // Past the command name and into its arguments, nothing to complete
                if (sTyped.TrimStart().IndexOf(' ') >= 0)
                {
                    return;
                }

                Select((sCycle + 1) % sMatches.Count);
                args.Handled = true;
            }
            catch (Exception ex)
            {
                ShowText("Console Hints error: " + ex.Message);
            }
        }

        private static void OnDialogVisibility(WindowBase sender, UIVisibilityChangeEventArgs args)
        {
            if (!args.Visible && sTriggerHook != 0)
            {
                UIManager.RemoveTriggerHook(sTriggerHook);
                sTriggerHook = 0;
            }
        }

        // Clicking a suggestion. The list is one big TextEdit rather than a row per
        // command, but a click puts the box's caret where the mouse was, so counting the
        // line breaks before the caret says which line got clicked.
        private static void OnBoxMouseUp(WindowBase sender, UIMouseEventArgs args)
        {
            if (args.MouseKey != MouseKeys.kMouseLeft || !sShowing || sMatches.Count == 0)
            {
                return;
            }

            try
            {
                string shown = sBox.Caption ?? "";
                int caret = Math.Min((int)sBox.CursorIndex, shown.Length);
                int line = 0;
                for (int i = 0; i < caret; i++)
                {
                    if (shown[i] == '\n')
                    {
                        line++;
                    }
                }

                // Don't wipe out arguments that are already typed, and ignore the
                // "... N more" line at the bottom
                bool typingArgs = sTyped.TrimStart().IndexOf(' ') >= 0;
                if (!typingArgs && line < sMatches.Count)
                {
                    Select(line);
                }

                // The click moved keyboard focus to the list, give it back
                UIManager.SetFocus(InputContext.kICKeyboard, sInput);
                args.Handled = true;
            }
            catch (Exception ex)
            {
                ShowText("Console Hints error: " + ex.Message);
            }
        }

        private static void Select(int index)
        {
            sCycle = index;
            string name = sMatches[index].Name;
            sLastSet = name;

            sBusy = true;
            try
            {
                sInput.Caption = name;
                sInput.AnchorIndex = (uint)name.Length;
                sInput.CursorIndex = (uint)name.Length;
            }
            finally
            {
                sBusy = false;
            }

            Render();
        }

        private static void Refresh()
        {
            sMatches.Clear();
            sTotalMatches = 0;

            string text = sTyped.TrimStart();
            int space = text.IndexOf(' ');
            string word = (space < 0 ? text : text.Substring(0, space)).ToLower();
            if (word.Length == 0)
            {
                Hide();
                return;
            }

            if (space >= 0)
            {
                // Typing arguments now, so only show the usage line for this command
                foreach (Entry entry in sEntries)
                {
                    if (entry.Lower == word)
                    {
                        sMatches.Add(entry);
                        sTotalMatches = 1;
                        break;
                    }
                }
            }
            else
            {
                // Two passes so names starting with the text come before ones that
                // just contain it
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (Entry entry in sEntries)
                    {
                        int at = entry.Lower.IndexOf(word);
                        bool hit = pass == 0 ? at == 0 : at > 0;
                        if (!hit)
                        {
                            continue;
                        }

                        sTotalMatches++;
                        if (sMatches.Count < kMaxShown)
                        {
                            sMatches.Add(entry);
                        }
                    }
                }
            }

            if (sMatches.Count == 0)
            {
                Hide();
            }
            else
            {
                Render();
            }
        }

        private static void Render()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < sMatches.Count; i++)
            {
                Entry entry = sMatches[i];
                if (i > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(i == sCycle ? "> " : "   ");
                sb.Append(entry.Name);
                if (entry.NeedsTestingCheats)
                {
                    sb.Append("  (needs testingcheatsenabled true)");
                }
                if (entry.Description.Length > 0)
                {
                    sb.Append("   -   ").Append(entry.Description);
                }
            }

            int hidden = sTotalMatches - sMatches.Count;
            if (hidden > 0)
            {
                sb.Append("\n   ... ").Append(hidden).Append(" more, keep typing");
            }

            ShowText(sb.ToString());
        }

        private static void ShowText(string text)
        {
            if (sBox == null)
            {
                return;
            }

            sBox.Visible = true;
            sBox.Caption = text;
            sBox.CursorIndex = 0u;
            sBox.AnchorIndex = 0u;
            sShowing = true;
        }

        private static void Hide()
        {
            // Only hide what we put there. The game uses the same box for its own
            // "unknown command" message.
            if (sBox != null && sShowing)
            {
                sBox.Caption = "";
                sBox.Visible = false;
            }
            sShowing = false;
        }
    }
}
