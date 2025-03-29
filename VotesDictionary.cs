using System.Collections.Generic;

namespace MapVote
{
    public class VotesDictionary
    {
        public readonly Dictionary<int, string> Values = new();

        public string this[int key]
        {
            get { return Values[key]; }
            set
            {
                Values[key] = value;
                MapVote.UpdateButtonLabels();
            }
        }
    }
}
