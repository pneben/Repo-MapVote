using MenuLib.MonoBehaviors;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MapVote
{
    internal sealed class VoteOptionButton
    {
        public string Level { get; set; }
        public REPOButton Button { get; set; }
        public bool IsRandomButton { get; set; }
        public VoteOptionButton(string _level, int _votes, REPOButton _button, bool _isRandomButton = false)
        {
            Level = _level;
            Button = _button;
            IsRandomButton = _isRandomButton;
        }

        public int GetVotes(Dictionary<int, string> votes)
        {
            var votesNum = 0;

            foreach (var entry in votes)
            {
                if (entry.Value == this.Level)
                {
                    votesNum++;
                }
            }

            return votesNum;
        }

        public void UpdateLabel(Dictionary<int, string> votes, string? _ownVote, bool _highlight = false)
        {
            Color mainColor = _highlight == true ? Color.green : _ownVote == this.Level ? Color.yellow : Color.white;

            this.Button.labelTMP.text =
                $"{Utilities.ColorString(($"Vote for <color={LevelColorDictionary.GetColor(this.Level)}>{(this.IsRandomButton ? MapVote.VOTE_RANDOM_LABEL : Utilities.RemoveLevelPrefix(this.Level))}</color>\t   {Utilities.ColorString(new string('I', this.GetVotes(votes)), Color.green)}"), mainColor)}";
        }
    }
}
