using System;
using System.Collections.Generic;

namespace GolfResume
{
    public class PlayerRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int?[] Scores { get; set; }
    }

    public class HoleEvent
    {
        public DateTime Time { get; set; }
        public string Player { get; set; }
        public int Hole { get; set; }
        public int Score { get; set; }
    }

    public class SessionRecord
    {
        public string Course { get; set; }
        public DateTime Started { get; set; }
        public DateTime Updated { get; set; }
        public List<PlayerRecord> Players { get; set; } = new();
        public List<HoleEvent> History { get; set; } = new();
    }
}
