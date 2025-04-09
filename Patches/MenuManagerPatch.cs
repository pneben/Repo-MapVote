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
                if(SemiFunc.IsMasterClientOrSingleplayer())
                {
                    MapVote.Reset();
                    MapVote.WonMap = null;
                }

                if(!MapVote.HideInMenu.Value)
                {
                    MapVote.CreateVotePopup(true);
                }
                
                if (MapVote.IS_DEBUG && SemiFunc.IsMasterClientOrSingleplayer() && SemiFunc.RunIsLobbyMenu())
                {
                    DebugManager.InitializeDebug();
                }
            }
        }
    }
}
