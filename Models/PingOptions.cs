namespace PBL4.Models
{
    public class PingOptions
    {
        public bool Continuous { get; set; } = false; // -t
        public int Count { get; set; } = 4;           // -n
        public int BufferSize { get; set; } = 32;     // -l
        public int Timeout { get; set; } = 3000;      // -w
        public int Ttl { get; set; } = 128;           // -i
    }
}
