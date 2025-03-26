using System;
using System.Collections.Generic;
using System.Text;

namespace MapVote
{
    internal class DebugManager
    {
        public static void InitializeDebug()
        {
            PopulateMockData();
        }

        private static void PopulateMockData()
        {
            MapVote.CurrentVotes[0] = "Level - Arctic";
            MapVote.CurrentVotes[1] = "Level - Arctic";
            MapVote.CurrentVotes[2] = "Level - Manor";
            MapVote.CurrentVotes[3] = "Level - Manor";
            MapVote.CurrentVotes[4] = "Level - Wizard";
            MapVote.CurrentVotes[5] = MapVote.VOTE_RANDOM_LABEL;
            MapVote.CurrentVotes[7] = MapVote.VOTE_RANDOM_LABEL;
            MapVote.CurrentVotes[8] = MapVote.VOTE_RANDOM_LABEL;
        }
    }
}
