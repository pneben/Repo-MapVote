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
using Photon.Realtime;
using Sirenix.Serialization.Utilities;
using MenuLib.MonoBehaviors;
using BepInEx.Configuration;

namespace MapVote {

    [BepInPlugin(MOD_GUID, MOD_NAME, MOD_VERSION)]
    [BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("nickklmao.menulib", BepInDependency.DependencyFlags.HardDependency)]
    internal sealed class MapVote : BaseUnityPlugin
    {
        // Constants
        private const string MOD_GUID = "Patrick.MapVote";
        private const string MOD_NAME = "MapVote";
        private const string MOD_VERSION = "1.0.0";

        public const string VOTE_RANDOM_LABEL = "Random";
        public const string TRUCK_LEVEL_NAME = "Level - Lobby";
        public const string REQUEST_VOTE_LEVEL = TRUCK_LEVEL_NAME;

        public const bool IS_DEBUG = false;

        // Harmony
        internal Harmony? Harmony { get; set; }

        // Logger
        internal static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource(MOD_NAME);
        
        // Network Events
        public static NetworkedEvent? OnVoteEvent;
        public static NetworkedEvent? OnVoteEndedEvent;
        public static NetworkedEvent? OnSyncVotes;
        public static NetworkedEvent? OnStartCountdown;

        // Configs
        public static ConfigEntry<int> VotingTime;

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
        
        public static MapVote Instance;

        private static Hook RunManagerSetRunLevelHook = new(
            AccessTools.DeclaredMethod(typeof(RunManager), nameof(RunManager.SetRunLevel)), HookRunManagerSetRunLevel);
        private static void HookRunManagerSetRunLevel(Action<RunManager> orig, RunManager self)
        {
            if (SemiFunc.IsMasterClient() && ShouldHookRunMangerSetRunLevel)
            {
                self.levelCurrent = self.levels.Find(x => x.name == WonMap);
                ShouldHookRunMangerSetRunLevel = false;
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

            if (ButtonStartHookRunAmount > 0)
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
            this.gameObject.transform.parent = null;
            this.gameObject.hideFlags = HideFlags.HideAndDontSave;

            Patch();

            VotingTime = Config.Bind<int>("General", "Voting Time", 10, new ConfigDescription("The amount of seconds until the voting ends, after the first player voted.", new AcceptableValueRange<int>(3, 30)));

            Initialize();
        }
        internal void Patch()
        {
            try
            {
                Harmony ??= new Harmony(Info.Metadata.GUID);
                Harmony.PatchAll();
                Logger.LogDebug($"Loaded {MOD_NAME}!");
            }
            catch (System.Exception e)
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
            OnStartCountdown = new NetworkedEvent("OnStartCountdown", HandleOnStartCountdown);

            MenuAPI.AddElementToLobbyMenu(parent => MenuAPI.CreateREPOButton("Map Vote", () => CreateVotePopup(true), parent, new Vector2(175.2f, 62.8f)));
        }

        public static void Reset()
        {
            CurrentVotes.Values.Clear();
            VoteOptionButtons.Clear();
            OwnVoteLevel = null;
            UpdateButtonLabels();
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
                return;
            }

            Dictionary<int, string> votes = (Dictionary<int, string>)data.CustomData;
            if (votes != null)
            {
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
            return (VoteOptionButtons
                    .Select(b => new { Item = b, Count = b.GetVotes(CurrentVotes.Values) })
                    .OrderByDescending(b => b.Count)
                    .Select(b => b.Item)
                    ).ToList();
        }

        public static void CreateNextMapLabel(string mapName)
        {
            var label = MenuAPI.CreateREPOLabel(null, GameObject.Find("Game Hud").transform, new Vector2(-100f, 110f));
            label.labelTMP.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Center;
            label.labelTMP.text = $"Next Map: <color={LevelColorDictionary.GetColor(mapName)}><size=32>{Utilities.RemoveLevelPrefix(mapName)}</size></color>";
        }

        public static IEnumerator WaitForVote()
        {
            if(!SemiFunc.IsMasterClient()) {
                yield break;
            }

            while (CurrentVotes.Values.Count <= 0)
            {
                yield return new WaitForSeconds(0.5f);
            }

            Instance.StartCoroutine(StartCountdown());

            yield break;
        }

        public static IEnumerator StartCountdown()
        {
            if(SemiFunc.IsMasterClient())
            {
                VotingTimeLeft = (float)VotingTime.Value;
                OnStartCountdown?.RaiseEvent(VotingTimeLeft, NetworkingEvents.RaiseOthers, SendOptions.SendReliable);
            }

            while (VotingTimeLeft > 0)
            {
                VotingTimeLeft -= Time.deltaTime;
                if(VotingTimeLabel != null)
                {
                    VotingTimeLabel.labelTMP.text = $"{VotingTimeLeft:0.00}";
                }
                yield return null;
            }

            if(VotingTimeLabel != null && VotingTimeLabel.gameObject != null)
            {
                Destroy(VotingTimeLabel.gameObject);
            }

            if(SemiFunc.IsMasterClient())
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

            VotePopup = MenuAPI.CreateREPOPopupPage("Vote for the next map", true, false, 0f, isInMenu ? new Vector2(40f, 0f) : new Vector2(-100f,0f));
            var runManger = FindObjectOfType<RunManager>();

            var levels = runManger.levels;

            // Generate Vote Options from Levels
            foreach (var (level, index) in levels.Select((level, index) => (level, index)))
            {
                VotePopup.AddElementToScrollView(parent =>
                {
                    var btn = MenuAPI.CreateREPOButton(null, () => {
                        if(DisableInput)
                        {
                            return;
                        }
                        OwnVoteLevel = level.name;
                        OnVoteEvent?.RaiseEvent(level.name, REPOLib.Modules.NetworkingEvents.RaiseAll, SendOptions.SendReliable);
                    }, parent);
                    VoteOptionButtons.Add(new VoteOptionButton(level.name, 0, btn));
                    return btn.rectTransform;
                });
            }

            // Generate "I don't care" Vote Option
            VotePopup.AddElementToScrollView(parent =>
            {
                var btn = MenuAPI.CreateREPOButton(null, () => {
                    if (DisableInput)
                    {
                        return;
                    }
                    OwnVoteLevel = VOTE_RANDOM_LABEL;
                    OnVoteEvent?.RaiseEvent(VOTE_RANDOM_LABEL, REPOLib.Modules.NetworkingEvents.RaiseAll, SendOptions.SendReliable);
                }, parent);
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
                b.UpdateLabel(CurrentVotes.Values, OwnVoteLevel);
            });
        }

        public static List<VoteOptionButton> GetEligibleOptions()
        {
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

            eligibleOptions = eligibleOptions.FindAll(x => x.IsRandomButton == false);

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
                voteOption.UpdateLabel(CurrentVotes.Values, OwnVoteLevel, true);
                yield return new WaitForSeconds(blinkFrequency / 2);

                voteOption.UpdateLabel(CurrentVotes.Values, OwnVoteLevel, false);
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
                eligibleOptions[index].UpdateLabel(CurrentVotes.Values, OwnVoteLevel, true);

                if (recentIndex >= 0)
                {
                    eligibleOptions[recentIndex].UpdateLabel(CurrentVotes.Values, OwnVoteLevel, false);
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

            eligibleOptions[recentIndex].UpdateLabel(CurrentVotes.Values, OwnVoteLevel, false);
        }
    }
}
