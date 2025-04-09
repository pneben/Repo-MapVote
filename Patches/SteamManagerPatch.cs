using ExitGames.Client.Photon;
using HarmonyLib;
using REPOLib.Modules;

namespace MapVote.Patches
{
    [HarmonyPatch(typeof(MenuPageLobby))]
    internal class SteamManagerPatch
    {
        [HarmonyPatch(nameof(MenuPageLobby.PlayerAdd))]
        [HarmonyPostfix]
        public static void PostfixJoiningPlayer()
        {
            if (SemiFunc.IsMasterClient())
            {
                MapVote.OnSyncVotes?.RaiseEvent(MapVote.CurrentVotes.Values, NetworkingEvents.RaiseAll, SendOptions.SendReliable);
                MapVote.OnSyncLastMapPlayed?.RaiseEvent(MapVote.LastMapPlayed, NetworkingEvents.RaiseOthers, SendOptions.SendReliable);
            }
        }
    }
}
