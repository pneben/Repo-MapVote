using ExitGames.Client.Photon;
using MenuLib;
using MenuLib.MonoBehaviors;
using REPOLib.Modules;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections;
using HarmonyLib;
using BepInEx.Logging;
using BepInEx;

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

        public const string VOTE_RANDOM_LABEL = "Random Map";
        public const string TRUCK_LEVEL_NAME = "Level - Lobby";
        public const string REQUEST_VOTE_LEVEL = "Level - Manor";

        private const bool IS_DEBUG = true;

        // Harmony
        internal Harmony? Harmony { get; set; }

        // Logger
        internal static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource(MOD_NAME);
        
        // Network Events
        public static NetworkedEvent? OnVoteEvent;

        // Vote Data
        public static readonly VotesDictionary CurrentVotes = new() { };
        public static readonly List<VoteOptionButton> VoteOptionButtons = new();
        public static string? OwnVoteLevel;
        public static string? WonMap;

        public static MapVote Instance;

        public void Awake()
        {
            Instance = this;

            // Prevent the plugin from being deleted
            this.gameObject.transform.parent = null;
            this.gameObject.hideFlags = HideFlags.HideAndDontSave;

            Patch();

            Initialize();
            Debug.Log("Loaded Mod!!!!");
        }
        internal void Patch()
        {
            try
            {
                Harmony ??= new Harmony(Info.Metadata.GUID);
                Harmony.PatchAll();
                Logger.LogDebug($"Loaded {MOD_NAME}!!");
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

            MenuAPI.AddElementToLobbyMenu(parent => MenuAPI.CreateREPOButton("Map Vote", CreateVotePopup, parent, new Vector2(40f, 10f)));

            if(IS_DEBUG)
            {
                DebugManager.InitializeDebug();
            }
        }

        private static void HandleOnVoteEvent(EventData data)
        {
            string message = (string)data.CustomData;
            CurrentVotes[data.sender] = message;
            Logger.LogDebug($"Received OnVoteEvent: {message}");
        }
        public static List<VoteOptionButton> GetSortedVoteOptions()
        {
            return (VoteOptionButtons
                    .Select(b => new { Item = b, Count = b.GetVotes(MapVote.CurrentVotes.Values) })
                    .OrderByDescending(b => b.Count)
                    .Select(b => b.Item)
                    ).ToList();
        }

        public static void CreateVotePopup()
        {
            Debug.Log("Should Open Popup now");
            MenuAPI.CloseAllPagesAddedOnTop();

            if (RunManager.instance.levelCurrent.name == TRUCK_LEVEL_NAME)
            {
                GameDirector.instance.DisableInput = true;
            }

            var votePopup = MenuAPI.CreateREPOPopupPage("Map Vote", false, false, 0f, new Vector2(20f, 0f));
            var runManger = FindObjectOfType<RunManager>();

            var levels = runManger.levels;

            // Generate Vote Options from Levels
            foreach (var (level, index) in levels.Select((level, index) => (level, index)))
            {
                votePopup.AddElementToScrollView(parent =>
                {
                    var btn = MenuAPI.CreateREPOButton(null, () => {
                        OwnVoteLevel = level.name;
                        OnVoteEvent?.RaiseEvent(level.name, REPOLib.Modules.NetworkingEvents.RaiseAll, SendOptions.SendReliable);
                    }, parent);
                    VoteOptionButtons.Add(new VoteOptionButton(level.name, 0, btn));
                    return btn.rectTransform;
                });
            }

            // Generate "I don't care" Vote Option
            votePopup.AddElementToScrollView(parent =>
            {
                var btn = MenuAPI.CreateREPOButton(null, () => {
                    OwnVoteLevel = VOTE_RANDOM_LABEL;
                    OnVoteEvent?.RaiseEvent(VOTE_RANDOM_LABEL, REPOLib.Modules.NetworkingEvents.RaiseAll, SendOptions.SendReliable);
                }, parent);
                VoteOptionButtons.Add(new VoteOptionButton(VOTE_RANDOM_LABEL, 0, btn, true));
                return btn.rectTransform;
            });

            if (IS_DEBUG)
            {
                votePopup.AddElementToScrollView(parent =>
                {
                    var btn = MenuAPI.CreateREPOButton("End Vote Now", () => {
                        Instance.StartCoroutine(OnVotingDone());
                    }, parent);
                    return btn.rectTransform;
                });
            }

            votePopup.OpenPage(true);
            UpdateButtonLabels();
            votePopup.GetComponent<MenuPage>().PageStateSet(MenuPage.PageState.Active);

        }
        public static IEnumerator DelayedOpenMenu(int seconds = 1)
        {
            yield return new WaitForSeconds(seconds);
            CreateVotePopup();
        }

        public static void UpdateButtonLabels()
        {
            VoteOptionButtons.ForEach(b =>
            {
                b.UpdateLabel(CurrentVotes.Values, OwnVoteLevel);
            });
        }

        public static IEnumerator OnVotingDone()
        {
            List<VoteOptionButton> eligibleOptions = new();

            var sortedOptions = GetSortedVoteOptions();

            var mostVoted = sortedOptions.GroupBy(x => x.GetVotes(CurrentVotes.Values)).OrderByDescending(x => x.Key).FirstOrDefault().ToList();

            if(mostVoted.Find(x => x.Level == VOTE_RANDOM_LABEL) != null)
            {
                eligibleOptions = VoteOptionButtons;
            } else
            {
                eligibleOptions = mostVoted ?? eligibleOptions;
            }

            // Remove "I Dont Care" Votes
            eligibleOptions = eligibleOptions.FindAll(x => x.IsRandomButton == false);

            if (eligibleOptions.Count > 1)
            {
                int winningIndex = Random.Range(0, eligibleOptions.Count);

                yield return Instance.StartCoroutine(SpinWheelOptions(eligibleOptions, winningIndex));
                yield return Instance.StartCoroutine(BlinkButton(eligibleOptions[winningIndex]));

                WonMap = eligibleOptions[winningIndex].Level;
            }
            else
            {
                // Straight up select first level
                var wonOption = eligibleOptions.FirstOrDefault();
                
                if(wonOption != null)
                {
                    WonMap = wonOption.Level;
                    yield return Instance.StartCoroutine(BlinkButton(wonOption));
                }
                
            }
        }

        public static IEnumerator BlinkButton(VoteOptionButton voteOption)
        {
            const float blinkFrequency = 0.5f;
            const float blinkDuration = 5f;

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

            Debug.Log($"Should arrive at {eligibleOptions[endIndex].Level}");

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
