using HarmonyLib;
using System.Linq;

namespace MapVote.Patches
{
    [HarmonyPatch(typeof(RunManager))]
    public class RunManagerPatch
    {
        [HarmonyPatch(nameof(RunManager.SetRunLevel))]
        [HarmonyPrefix]
        static bool PrefixSetRunLevel(RunManager __instance)
        {
            var mapList = MapVote.GetSortedVoteOptions();

            if (mapList != null && mapList.Count > 0 && mapList[0] != null)
            {
                __instance.levelCurrent = __instance.levels.Find(l => l.name == mapList[0].Level);
                MapVote.Logger.LogInfo($"Set Level to: {__instance.levelCurrent}");
            }
            else
            {
                //__instance.levelCurrent = __instance.levelLobby;

                __instance.levelCurrent = __instance.levels.First(l => l.name == "Level - Manor");

                // !! - Uncomment later

                //__instance.levelCurrent = __instance.previousRunLevel;
                //while (__instance.levelCurrent == __instance.previousRunLevel)
                //{
                //    __instance.levelCurrent = __instance.levels[Random.Range(0, __instance.levels.Count)];
                //    Logger.LogInfo($"Set Level to: {__instance.levelCurrent}");
                //}
            }

            return false;
        }
    }
}
