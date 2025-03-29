using HarmonyLib;

namespace MapVote.Patches
{
    [HarmonyPatch(typeof(MenuManager))]
    internal class MenuManagerPatch
    {
        [HarmonyPatch(nameof(MenuManager.PageOpen))]
        [HarmonyPostfix]
        private static void PageOpenPostfix(MenuPageIndex menuPageIndex)
        {
            if(menuPageIndex == MenuPageIndex.Lobby)
            {
                MapVote.CreateVotePopup(true);
                
                if (MapVote.IS_DEBUG && SemiFunc.IsMasterClient() && SemiFunc.RunIsLobbyMenu())
                {
                    DebugManager.InitializeDebug();
                }
            }
        }
    }
}
