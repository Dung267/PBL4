using System.Runtime.InteropServices;

namespace PBL4.Models
{
    // Cấu trúc DNS Header (12 bytes)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DnsHeader
    {
        public ushort ID;          // Transaction ID
        public ushort Flags;       // Cờ (QR, Opcode, AA, TC, RD, RA, Z, RCODE)
        public ushort QdCount;     // Số lượng câu hỏi (Question)
        public ushort AnCount;     // Số lượng câu trả lời (Answer)
        public ushort NsCount;     // Số lượng Name Server
        public ushort ArCount;     // Số lượng Additional Record
    }

    // Cấu trúc DNS Question
    // QName sẽ được mã hóa thủ công
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DnsQuestion
    {
        public ushort QType; // Loại Record (A=1, AAAA=28, PTR=12, ...)
        public ushort QClass; // Lớp (IN=1)
    }

    // Cấu trúc Answer Resource Record (không cần struct vì phức tạp bởi Pointer Compression)
    // Sẽ phân tích thủ công: Name (Pointer), Type, Class, TTL, Data Length, Data
}
