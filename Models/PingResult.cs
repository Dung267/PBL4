namespace PBL4.Models
{
    public class PingResult
    {
        public string Target { get; set; } = "";
        public List<string> Replies { get; set; } = new();
        public int Sent { get; set; }
        public int Received { get; set; }
        public int Lost => Sent - Received;
        public long MinTime { get; set; }
        public long MaxTime { get; set; }
        public long AvgTime { get; set; }
    }
}
