using HarmonyLib;

namespace MapVote.Patches
{
    [HarmonyPatch(typeof(HealthUI))]
    public class HealthUIPatch
    {
        [HarmonyPatch(nameof(HealthUI.Start))]
        [HarmonyPostfix]
        static void PostfixStart(HealthUI __instance)
        {
            if (RunManager.instance.levelCurrent.name == MapVote.REQUEST_VOTE_LEVEL)
            {
                MapVote.Instance.StartCoroutine(MapVote.WaitForVote());
                MapVote.CreateVotePopup(false);
            }
        }
    }
}
