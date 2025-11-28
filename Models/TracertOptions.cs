namespace PBL4.Models
{
    public class TracertOptions
    {
        public bool NoResolve { get; set; } = false;          // -d (Không phân giải tên)
        public int MaxHops { get; set; } = 30;               // -h (Hop tối đa)
        public int Timeout { get; set; } = 4000;             // -w (Timeout, ms)
        public bool ForceIpv4 { get; set; } = false;          // -4
        public bool ForceIpv6 { get; set; } = false;          // -6
    }
}