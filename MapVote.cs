using ExitGames.Client.Photon;
using MenuLib;
using REPOLib.Modules;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections;
using HarmonyLib;
using BepInEx.Logging;
using BepInEx;
using MonoMod.RuntimeDetour;
using System;
using Sirenix.Serialization.Utilities;
using MenuLib.MonoBehaviors;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using System.Globalization;
using Unity.VisualScripting;
using TMPro;
using UnityEngine.UI;

namespace MapVote {
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("nickklmao.menulib", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("ViViKo.StartInShop", BepInDependency.DependencyFlags.SoftDependency)]
    internal sealed class MapVote : BaseUnityPlugin
    {
        // Constants
        public const string VOTE_RANDOM_LABEL = "Random";
        public const string TRUCK_LEVEL_NAME = "Level - Lobby";
        public const string SHOP_LEVEL_NAME = "Level - Shop";
        public const string REQUEST_VOTE_LEVEL = TRUCK_LEVEL_NAME;

        public const bool IS_DEBUG = false;

        // Harmony
        internal Harmony? Harmony { get; set; }

        // Logger
        internal static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource(MyPluginInfo.PLUGIN_NAME);
        
        // Network Events
        public static NetworkedEvent? OnVoteEvent;
        public static NetworkedEvent? OnVoteEndedEvent;
        public static NetworkedEvent? OnSyncVotes;
        public static NetworkedEvent? OnSyncLastMapPlayed;
        public static NetworkedEvent? OnStartCountdown;

        // Configs
        public static ConfigEntry<int> VotingTime;
        public static ConfigEntry<bool> HideInMenu;
        public static ConfigEntry<bool> NoRepeatedMaps;

        // Vote Data
        public static VotesDictionary CurrentVotes = new() { };
        public static readonly List<VoteOptionButton> VoteOptionButtons = new();
        public static string? OwnVoteLevel;
        public static string? WonMap;

        public static REPOPopupPage? VotePopup;

        public static float VotingTimeLeft = 0f;
        public static REPOLabel? VotingTimeLabel;

        public static bool DisableInput = false;
        public static bool ShouldHookRunMangerSetRunLevel = false;

        public static string? LastMapPlayed;

        public static MapVote Instance;

        private static Hook RunManagerSetRunLevelHook = new(
            AccessTools.DeclaredMethod(typeof(RunManager), nameof(RunManager.SetRunLevel)), HookRunManagerSetRunLevel);
        private static void HookRunManagerSetRunLevel(Action<RunManager> orig, RunManager self)
        {
            if (SemiFunc.IsMasterClientOrSingleplayer() && ShouldHookRunMangerSetRunLevel)
            {
                self.levelCurrent = self.levels.Find(x => x.name == WonMap);
                ShouldHookRunMangerSetRunLevel = false;

                LastMapPlayed = WonMap;
                OnSyncLastMapPlayed?.RaiseEvent(LastMapPlayed, NetworkingEvents.RaiseOthers, SendOptions.SendReliable);

                Reset();
                WonMap = null;
                return;
            }
            orig(self);
        }

        private static int ButtonStartHookRunAmount = 0;
        private static Hook ButtonStartHook = new(
            AccessTools.DeclaredMethod(typeof(MenuPageLobby), nameof(MenuPageLobby.ButtonStart)), HookButtonStart);
        private static void HookButtonStart(Action<MenuPageLobby> orig, MenuPageLobby self)
        {
            if(DisableInput)
            {
                return;
            }

            if (ButtonStartHookRunAmount > 0 || HideInMenu.Value)
            {
                ButtonStartHookRunAmount = 0;
                orig(self);
            } else
            {
                ButtonStartHookRunAmount++;
                var map = GetWinningMap();

                OnVoteEndedEvent?.RaiseEvent(map, NetworkingEvents.RaiseOthers, SendOptions.SendReliable);
                Instance.StartCoroutine(OnVotingDone(map));
            }
        }

        public void Awake()
        {
            Instance = this;

            // Prevent the plugin from being deleted
            gameObject.transform.parent = null;
            gameObject.hideFlags = HideFlags.HideAndDontSave;

            Patch();

            VotingTime = Config.Bind("General", "Voting Time", 10, new ConfigDescription("The amount of seconds until the voting ends, after the first player voted.", new AcceptableValueRange<int>(3, 30)));
            HideInMenu = Config.Bind("General", "Hide in Menu", false, new ConfigDescription("When true - hides the Menu in the lobby menu and randomly selects a random map - Voting is still enabled in the truck"));
            NoRepeatedMaps = Config.Bind("General", "No Repeated Maps", false, new ConfigDescription("When true - disallows votes for the most recently played map - You won't play the same map twice in a row"));

            CompatibilityPatches.RunPatches(Chainloader.PluginInfos.Select(x => (x.Key)).ToList());

            Initialize();
            Logger.LogDebug($"Loaded {MyPluginInfo.PLUGIN_NAME} V{MyPluginInfo.PLUGIN_VERSION}!");
        }
        internal void Patch()
        {
            try
            {
                Harmony ??= new Harmony(Info.Metadata.GUID);
                Harmony.PatchAll();
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }

        internal void Unpatch()
        {
            Harmony?.UnpatchSelf();
        }

        internal static void Initialize()
        {
            OnVoteEvent = new NetworkedEvent("OnVoteEvent", HandleOnVoteEvent);
            OnVoteEndedEvent = new NetworkedEvent("OnVoteEndedEvent", HandleOnVoteEndEvent);
            OnSyncVotes = new NetworkedEvent("OnSyncVotes", HandleOnSyncVotes);
            OnSyncLastMapPlayed = new NetworkedEvent("OnSyncLastMapPlayed", HandleOnSyncLastMapPlayed);
            OnStartCountdown = new NetworkedEvent("OnStartCountdown", HandleOnStartCountdown);

            if(!HideInMenu.Value)
            {
                MenuAPI.AddElementToLobbyMenu(parent => MenuAPI.CreateREPOButton("Map Vote", () => CreateVotePopup(true), parent, new Vector2(175.2f, 62.8f)));
            }
        }

        public static bool HasBeenLastPlayed(string? level)
        {
            if (level == null || NoRepeatedMaps.Value == false) return false;

            return (LastMapPlayed == level && LastMapPlayed != WonMap);
        }

        public static void Reset()
        {
            CurrentVotes.Values.Clear();
            VoteOptionButtons.Clear();
            OwnVoteLevel = null;
            UpdateButtonLabels();
        }
        private static void HandleOnSyncLastMapPlayed(EventData data)
        {
            string lastMap = (string)data.CustomData;

            LastMapPlayed = lastMap;
            WonMap = null;
        }

        private static void HandleOnStartCountdown(EventData data)
        {
            if (SemiFunc.IsMasterClient())
            {
                return;
            }

            float countdown = (float)data.CustomData;

            VotingTimeLeft = countdown;

            Instance.StartCoroutine(StartCountdown());
        }
        private static void HandleOnSyncVotes(EventData data)
        {
            if (SemiFunc.IsMasterClient())
            {
                UpdateButtonLabels();
                return;
            }

            Dictionary<int, string> votes = (Dictionary<int, string>)data.CustomData;
            if (votes != null)
            {
                Reset();
                WonMap = null;
                votes.ForEach(x =>
                {
                    CurrentVotes[x.Key] = x.Value;
                });
                
                UpdateButtonLabels();
            }
        }

        private static void HandleOnVoteEvent(EventData data)
        {
            string message = (string)data.CustomData;
            CurrentVotes[data.sender] = message;
        }

        private static void HandleOnVoteEndEvent(EventData data)
        {
            string winningLevel = (string)data.CustomData;
            Instance.StartCoroutine(OnVotingDone(winningLevel));
        }

        public static List<VoteOptionButton> GetSortedVoteOptions()
        {
            return VoteOptionButtons
                    .Select(b => new { Item = b, Count = b.GetVotes(CurrentVotes.Values) })
                    .OrderByDescending(b => b.Count)
                    .Select(b => b.Item)
                    .ToList();
        }

        public static void CreateNextMapLabel(string mapName)
        {
            var label = MenuAPI.CreateREPOLabel(null, GameObject.Find("Game Hud").transform, new Vector2(-100f, 110f));
            label.labelTMP.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Center;
            label.labelTMP.text = $"Next Map: <color={LevelColorDictionary.GetColor(mapName)}><size=32>{Utilities.RemoveLevelPrefix(mapName)}</size></color>";
        }

        public static IEnumerator WaitForVote()
        {
            while (CurrentVotes.Values.Count <= 0)
            {
                UpdateButtonLabels();
                yield return new WaitForSeconds(0.5f);
            }

            if (!SemiFunc.IsMasterClientOrSingleplayer())
            {
                yield break;
            }

            Instance.StartCoroutine(StartCountdown());

            yield break;
        }

        public static IEnumerator StartCountdown()
        {
            if(SemiFunc.IsMasterClientOrSingleplayer())
            {
                VotingTimeLeft = VotingTime.Value;
                OnStartCountdown?.RaiseEvent(VotingTimeLeft, NetworkingEvents.RaiseOthers, SendOptions.SendReliable);
            }

            string format = "00.00";
            var nfi = new NumberFormatInfo
            {
                NumberDecimalSeparator = ":"
            };

            while (VotingTimeLeft > 0)
            {
                VotingTimeLeft -= Time.deltaTime;
                if(VotingTimeLabel != null)
                {
                    VotingTimeLabel.labelTMP.text = $"<mspace=0.5em>{VotingTimeLeft.ToString(format, nfi)}</mspace> Seconds Left";
                }
                yield return null;
            }

            if(VotingTimeLabel != null && VotingTimeLabel.gameObject != null)
            {
                Destroy(VotingTimeLabel.gameObject);
            }

            if(SemiFunc.IsMasterClientOrSingleplayer())
            {
                var map = GetWinningMap();
                OnVoteEndedEvent?.RaiseEvent(map, NetworkingEvents.RaiseOthers, SendOptions.SendReliable);
                Instance.StartCoroutine(OnVotingDone(map));
            }
        }

        public static void CreateVotePopup(bool isInMenu = false)
        {
            MenuAPI.CloseAllPagesAddedOnTop();
            VoteOptionButtons.Clear();

            if(VotePopup != null)
            {
                VotePopup.ClosePage(true);
                VotePopup = null;
            }

            if (RunManager.instance.levelCurrent.name == TRUCK_LEVEL_NAME)
            {
                GameDirector.instance.DisableInput = true;
            }

            VotePopup = MenuAPI.CreateREPOPopupPage("Next map", true, !isInMenu, 0f, isInMenu ? new Vector2(40f, 0f) : new Vector2(-100f,0f));
            var runManger = FindObjectOfType<RunManager>();

            var levels = runManger.levels;

            // Generate Vote Options from Levels
            foreach (var (level, index) in levels.Select((level, index) => (level, index)))
            {
                var name = level.name;
                VotePopup.AddElementToScrollView(parent =>
                {
                    var btn = MenuAPI.CreateREPOButton(null, () => {
                        if(DisableInput)
                        {
                            return;
                        }
                        OwnVoteLevel = name;
                        OnVoteEvent?.RaiseEvent(name, NetworkingEvents.RaiseAll, SendOptions.SendReliable);
                    }, parent);

                    if(HasBeenLastPlayed(name))
                    {
                        btn.gameObject.GetComponent<MenuButton>().disabled = true;
                    }

                    var layoutGroup = btn.AddComponent<HorizontalLayoutGroup>();
                    layoutGroup.spacing = 235f;

                    var votesLabel = GameObject.Instantiate(btn.labelTMP.gameObject, btn.transform);
                    var lbl = votesLabel.GetComponent<TextMeshProUGUI>();
                    lbl.horizontalAlignment = HorizontalAlignmentOptions.Right;

                    VoteOptionButtons.Add(new VoteOptionButton(name, 0, btn));
                    return btn.rectTransform;
                });
            }

            // Generate "Random" Vote Option
            VotePopup.AddElementToScrollView(parent =>
            {
                var btn = MenuAPI.CreateREPOButton(null, () => {
                    if (DisableInput)
                    {
                        return;
                    }
                    OwnVoteLevel = VOTE_RANDOM_LABEL;
                    OnVoteEvent?.RaiseEvent(VOTE_RANDOM_LABEL, NetworkingEvents.RaiseAll, SendOptions.SendReliable);
                }, parent);

                var layoutGroup = btn.AddComponent<HorizontalLayoutGroup>();
                layoutGroup.spacing = 235f;

                var votesLabel = GameObject.Instantiate(btn.labelTMP.gameObject, btn.transform);
                var lbl = votesLabel.GetComponent<TextMeshProUGUI>();
                lbl.horizontalAlignment = HorizontalAlignmentOptions.Right;

                VoteOptionButtons.Add(new VoteOptionButton(VOTE_RANDOM_LABEL, 0, btn, true));
                return btn.rectTransform;
            });

            VotePopup.AddElement(parent =>
            {
                VotingTimeLabel = MenuAPI.CreateREPOLabel(null, parent, new Vector2(isInMenu ? 394f : 254f, 30f));
            });

            VotePopup.OpenPage(true);
            UpdateButtonLabels();
            VotePopup.GetComponent<MenuPage>().PageStateSet(MenuPage.PageState.Active);
        }
        public static void UpdateButtonLabels()
        {
            VoteOptionButtons.ForEach(b =>
            {
                b.UpdateLabel(false, HasBeenLastPlayed(b.Level));
            });
        }

        public static List<VoteOptionButton> GetEligibleOptions()
        {
            if(CurrentVotes.Values.Count == 0)
            {
                return VoteOptionButtons.FindAll(x => x.IsRandomButton == false);
            }

            List<VoteOptionButton> eligibleOptions = new();

            var sortedOptions = GetSortedVoteOptions();

            var mostVoted = sortedOptions.GroupBy(x => x.GetVotes(CurrentVotes.Values)).OrderByDescending(x => x.Key).FirstOrDefault().ToList();

            if (mostVoted.Find(x => x.Level == VOTE_RANDOM_LABEL) != null)
            {
                eligibleOptions = VoteOptionButtons;
            }
            else
            {
                eligibleOptions = mostVoted ?? eligibleOptions;
            }

            if (NoRepeatedMaps.Value)
            {
                eligibleOptions = eligibleOptions.FindAll(x => !HasBeenLastPlayed(x.Level));
            }

            // Fallback
            if (eligibleOptions.FindAll(x => !x.IsRandomButton).Count <= 0)
            {
                eligibleOptions = VoteOptionButtons;
            }

            eligibleOptions.RemoveAll(x => x.IsRandomButton);

            return eligibleOptions;
        }

        public static string GetWinningMap()
        {
            var eligibleOptions = GetEligibleOptions();

            int index = UnityEngine.Random.RandomRangeInt(0, eligibleOptions.Count);

            return eligibleOptions[index].Level;
        }

        public static IEnumerator OnVotingDone(string winningMap)
        {
            DisableInput = true;
            ShouldHookRunMangerSetRunLevel = true;

            WonMap = winningMap;

            var eligibleOptions = GetEligibleOptions();

            if (eligibleOptions.Count > 1)
            {
                int winningIndex = eligibleOptions.FindIndex(x => x.Level == winningMap);

                yield return Instance.StartCoroutine(SpinWheelOptions(eligibleOptions, winningIndex));
                yield return Instance.StartCoroutine(BlinkButton(eligibleOptions[winningIndex]));

            }
            else
            {
                var wonOption = eligibleOptions.FirstOrDefault();
                
                if(wonOption != null)
                {
                    yield return Instance.StartCoroutine(BlinkButton(wonOption));
                }
            }

            DisableInput = false;

            if(SemiFunc.RunIsLobby())
            {
                CreateNextMapLabel(WonMap);

                if(VotePopup != null)
                {
                    VotePopup.ClosePage(true);
                }

                MenuAPI.CloseAllPagesAddedOnTop();
            }

            if(SemiFunc.IsMasterClient())
            {
                if (SemiFunc.RunIsLobbyMenu())
                {
                    MenuPageLobby.instance.ButtonStart();
                }
            }
            Reset();
        }

        public static IEnumerator BlinkButton(VoteOptionButton voteOption)
        {
            const float blinkFrequency = 0.5f;
            const float blinkDuration = 3f;

            int maxBlinks = (int)Mathf.Ceil(blinkDuration / blinkFrequency);
            int currentBlink = 0;

            while (currentBlink < maxBlinks) 
            {
                voteOption.UpdateLabel(true);
                yield return new WaitForSeconds(blinkFrequency / 2);

                voteOption.UpdateLabel(false);
                yield return new WaitForSeconds(blinkFrequency / 2);
                currentBlink++;
            }
        }

        public static IEnumerator SpinWheelOptions(List<VoteOptionButton> eligibleOptions, int winningIndex)
        {
            const float slowDownFactor = 1.15f;
            const float maxDelay = 0.5f;
            const float initialSpeed = 0.05f;
            const float allowSelectionDelayFactor = 0.8f;
            
            float delay = initialSpeed;
            int index = 0;
            int endIndex = winningIndex;
            int recentIndex = -1;

            while (index != endIndex || delay < maxDelay)
            {
                eligibleOptions[index].UpdateLabel(true);

                if (recentIndex >= 0)
                {
                    eligibleOptions[recentIndex].UpdateLabel(false);
                }

                yield return new WaitForSeconds(delay);

                if(delay > maxDelay * allowSelectionDelayFactor && index == endIndex)
                {
                    break;
                }

                delay *= slowDownFactor;
                recentIndex = index;
                index = (index + 1) % eligibleOptions.Count;
            }

            eligibleOptions[recentIndex].UpdateLabel(false);
        }
    }
}
