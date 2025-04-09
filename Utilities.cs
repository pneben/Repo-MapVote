using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.VisualScripting;
using UnityEngine;

namespace MapVote
{
    internal sealed class Utilities
    {
        public static string ColorString(string text, string colorHex)
        {
            return $"<color=#{colorHex}>{text}</color>";
        }
        public static string ColorString(string text, Color color)
        {
            return $"<color=#{color.ToHexString()}>{text}</color>";
        }

        public static string RemoveLevelPrefix(string text)
        {
            return text.Replace("Level - ", "");
        }

    }
}
