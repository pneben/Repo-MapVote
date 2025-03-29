using ExitGames.Client.Photon;
using HarmonyLib;
using REPOLib.Modules;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

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
            }
        }
    }
}
