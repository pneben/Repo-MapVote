using ExitGames.Client.Photon;
using HarmonyLib;
using MenuLib;
using REPOLib.Modules;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

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
                if(SemiFunc.IsMasterClient())
                {
                    MapVote.Instance.StartCoroutine(MapVote.WaitForVote());
                }
                MapVote.CreateVotePopup(false);
            }
        }
    }
}
