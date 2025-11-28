//using System;
//using System.Net;
//using System.Collections.Generic;
//using System.Runtime.InteropServices;
//using System.ComponentModel.DataAnnotations;

//// 1. MODEL KẾT QUẢ VÀ TÙY CHỌN TRACERT

//public class TracertResult
//{
//    public string TargetHost { get; set; }
//    public IPAddress TargetIP { get; set; }
//    public int MaxHops { get; set; }
//    public List<TracertHop> Hops { get; set; } = new List<TracertHop>();
//    public TracertOptions Options { get; set; } = new TracertOptions();
//    public string ErrorMessage { get; set; } // Dùng cho thông báo lỗi
//}

//public class TracertHop
//{
//    public int Hop { get; set; }
//    public string Time1 { get; set; } = "*";
//    public string Time2 { get; set; } = "*";
//    public string Time3 { get; set; } = "*";
//    public IPAddress IPAddress { get; set; }
//    public string HostName { get; set; }
//    public bool IsDestination { get; set; }
//}

//public class TracertOptions
//{
//    [Required(ErrorMessage = "Vui lòng nhập Hostname hoặc IP.")]
//    public string Host { get; set; }

//    [Display(Name = "Max Hops (-h)")]
//    [Range(1, 128, ErrorMessage = "Giá trị phải từ 1 đến 128.")]
//    public int MaxHops { get; set; } = 30;

//    [Display(Name = "Timeout (ms) (-w)")]
//    [Range(100, 60000, ErrorMessage = "Giá trị phải từ 100 đến 60000 ms.")]
//    public int TimeoutMs { get; set; } = 4000;

//    [Display(Name = "Không phân giải Host (-d)")]
//    public bool NoReverseDns { get; set; } = false;
//}



//// 2. CẤU TRÚC DỮ LIỆU CHO P/INVOKE (Ánh xạ API Windows)


//[StructLayout(LayoutKind.Sequential)]

//public struct IcmpEchoReply

//{

//    public uint Address;

//    public uint RoundTripTime;

//    public ushort DataSize;

//    public ushort Status; // 0 = Success (Echo Reply hoặc Time Exceeded)

//    public IntPtr DataPtr;

//    public ushort OptionsSize;

//    public ushort Ttl;

//}

//[StructLayout(LayoutKind.Sequential, Pack = 1)]
//public struct IcmpOptions
//{
//    public byte Ttl;
//    public byte Tos;
//    public byte Flags;
//    public byte OptionsSize;
//    public IntPtr OptionsData;
//}
using System.Collections.Generic;
using System.Linq;

namespace PBL4.Models
{
    public class HopResult
    {
        public int HopNumber { get; set; }
        public string Address { get; set; }
        public string HostName { get; set; } = "";
        public List<long> Times { get; set; } = new List<long>();
        public bool ReachedDestination { get; set; } = false;

        public string GetTimeDisplay(int index)
        {
            if (Times.Count <= index || Times[index] == -1)
            {
                return "*";
            }
            return $"{Times[index]} ms";
        }
    }

    public class TracertResult
    {
        public string Target { get; set; } = "";
        public string TargetIP { get; set; } = "";
        public List<HopResult> Hops { get; set; } = new List<HopResult>();
        public List<string> Messages { get; set; } = new List<string>();
        public bool Success { get; set; } = false;
        public int MaxHops { get; set; }
    }
}