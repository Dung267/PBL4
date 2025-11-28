namespace PBL4.Models
{
    public class PingOptions
    {
        public bool Continuous { get; set; } = false; // -t
        public int Count { get; set; } = 4;           // -n
        public int BufferSize { get; set; } = 32;     // -l
        public int Timeout { get; set; } = 3000;      // -w
        public int Ttl { get; set; } = 128;           // -i

        // các tùy chọn mới bổ sung
        public bool ResolveHostname { get; set; } = false; // -a
        public bool ForceIpv4 { get; set; } = false;     // -4
        public bool ForceIpv6 { get; set; } = false;     // -6

        // 2 tùy chọn triển khai chưa hoàn chỉnh
        public bool DontFragment { get; set; } = false;  // -f
        public string SourceAddress { get; set; } = null;// -S

    }
}
