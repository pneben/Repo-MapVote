using Sirenix.Serialization.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MapVote
{
    internal class CompatibilityPatches
    {
        private static Dictionary<string, Action> Patches = new Dictionary<string, Action>();

        public static void PopulatePatches()
        {
            Patches.Add("ViViKo.StartInShop", () =>
            {
                MapVote.HideInMenu = true;
            });
        }

        public static void RunPatches(List<string> pluginGUIDs)
        {
            PopulatePatches();

            Patches.Where(x => pluginGUIDs.Contains(x.Key)).ForEach(plugin =>
            {
                plugin.Value();
                MapVote.Logger.LogInfo($"Ran Compatibility Patch for {plugin.Key}");
            });
        }
    }
}
