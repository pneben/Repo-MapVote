using MenuLib.MonoBehaviors;
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
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
                if (entry.Value == Level)
                {
                    votesNum++;
                }
            }

            return votesNum;
        }

        public void UpdateLabel(bool _highlight = false, bool _disabled = false)
        {
            var votes = MapVote.CurrentVotes.Values;
            var ownVote = MapVote.OwnVoteLevel == Level;

            var playerCount = Math.Max(Math.Min(GameDirector.instance.PlayerList.Count, 12), 4);
            var votesCount = GetVotes(votes);
            Color mainColor = _disabled ? Color.gray : (_highlight == true ? Color.green : ownVote ? Color.yellow : Color.white);

            StringBuilder sb = new();

            if (_disabled) sb.Append("<s>");

            sb.Append($"<mspace=0.25em>[{Utilities.ColorString((ownVote || _highlight ? "X" : " "), mainColor)}]</mspace>  ");
            if(!_disabled) sb.Append($"<color={LevelColorDictionary.GetColor(Level)}>");
            sb.Append($"{(IsRandomButton ? MapVote.VOTE_RANDOM_LABEL : Utilities.RemoveLevelPrefix(Level))}");
            if (!_disabled)
            {
                sb.Append("</color>");
            }

            if (_disabled) sb.Append("</s>");

            var votesLabel = Button.transform.GetChild(1);
            votesLabel.GetComponent<TextMeshProUGUI>().text = $"{Utilities.ColorString(new string('I', votesCount), Color.green)}{Utilities.ColorString(new string('I', playerCount - votesCount), Color.white)}";

            Button.labelTMP.text =
                $"{sb.ToString()}";
        }
    }
}
