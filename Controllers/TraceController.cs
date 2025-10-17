using Microsoft.AspNetCore.Mvc;
using PBL4.Models;
using System.Net;
using System.Runtime.InteropServices;


namespace PBL4.Controllers
{
    public class TraceController : Controller
    {
        private const int PING_COUNT = 3;

       
        // PHẦN P/INVOKE: ÁNH XẠ CÁC HÀM CỦA WINDOWS API

        [DllImport("IPHLPAPI.DLL", SetLastError = true)]
        private static extern IntPtr IcmpCreateFile();

        [DllImport("IPHLPAPI.DLL", SetLastError = true)]
        private static extern uint IcmpSendEcho(
            IntPtr IcmpHandle,
            uint DestinationAddress,
            IntPtr RequestData,
            ushort RequestSize,
            IntPtr RequestOptions,
            IntPtr ReplyBuffer,
            uint ReplySize,
            uint Timeout
        );

        [DllImport("IPHLPAPI.DLL", SetLastError = true)]
        private static extern bool IcmpCloseHandle(IntPtr IcmpHandle);
        
        // HÀM HỖ TRỢ CHUYỂN ĐỔI IP
        private static string UintToIP(uint ip)
        {
            return new IPAddress(BitConverter.GetBytes(ip)).ToString();
        }

        // LOGIC TRACERT CHÍNH
        public ActionResult Index()
        {
            // Truyền Model trống với Options mặc định cho lần tải trang đầu tiên
            return View(new TracertResult { Options = new TracertOptions() });
        }

        [HttpPost]
        public async Task<ActionResult> Index(TracertOptions options)
        {
            // 1. KIỂM TRA VALIDATION
            if (!ModelState.IsValid)
            {
                return View(new TracertResult { Options = options, ErrorMessage = "Vui lòng kiểm tra lại các giá trị nhập liệu." });
            }

            var resultModel = new TracertResult
            {
                TargetHost = options.Host,
                MaxHops = options.MaxHops,
                Options = options
            };

            uint destinationAddressUint = 0;

            // 2. PHÂN GIẢI DNS
            try
            {
                var hostEntry = await Dns.GetHostEntryAsync(options.Host);
                var targetAddress = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                if (targetAddress == null)
                {
                    resultModel.ErrorMessage = "Không tìm thấy địa chỉ IPv4 cho Host này.";
                    return View(resultModel);
                }

                resultModel.TargetIP = targetAddress;
                destinationAddressUint = BitConverter.ToUInt32(targetAddress.GetAddressBytes(), 0);
            }
            catch (Exception)
            {
                resultModel.ErrorMessage = $"Lỗi: Không thể phân giải tên miền/IP: {options.Host}";
                return View(resultModel);
            }

            // 3. LOGIC P/INVOKE CHÍNH
            IntPtr icmpHandle = IcmpCreateFile();
            if (icmpHandle == IntPtr.Zero)
            {
                resultModel.ErrorMessage = $"Lỗi hệ thống: Không thể tạo ICMP handle (Lỗi: {Marshal.GetLastWin32Error()}).";
                return View(resultModel);
            }

            const int ReplyBufferSize = 256;
            IntPtr replyBufferPtr = Marshal.AllocHGlobal(ReplyBufferSize);
            IntPtr requestDataPtr = Marshal.AllocHGlobal(32); // 32 bytes data

            try
            {
                bool destinationReached = false;
                // Dùng options.MaxHops
                for (int ttl = 1; ttl <= options.MaxHops; ttl++)
                {
                    if (destinationReached) break;

                    var currentHop = new TracertHop { Hop = ttl };
                    IPAddress lastAddress = null;

                    var icmpOptions = new IcmpOptions { Ttl = (byte)ttl };
                    IntPtr optionsPtr = Marshal.AllocHGlobal(Marshal.SizeOf(icmpOptions));
                    Marshal.StructureToPtr(icmpOptions, optionsPtr, false);

                    for (int i = 1; i <= PING_COUNT; i++)
                    {
                        uint repliesCount = 0;

                        // SỬ DỤNG TIMEOUT TÙY CHỌN (-w)
                        repliesCount = IcmpSendEcho(
                            icmpHandle,
                            destinationAddressUint,
                            requestDataPtr,
                            32,
                            optionsPtr,
                            replyBufferPtr,
                            (uint)ReplyBufferSize,
                            (uint)options.TimeoutMs
                        );

                        string responseTime = "*";

                        if (repliesCount > 0)
                        {
                            IcmpEchoReply reply = (IcmpEchoReply)Marshal.PtrToStructure(replyBufferPtr, typeof(IcmpEchoReply));

                            uint statusFromBuffer = (uint)Marshal.ReadInt32(replyBufferPtr, 4);
                            uint rttFromBuffer = (uint)Marshal.ReadInt32(replyBufferPtr, 8);

                            if (reply.Status == 0) // Thành công (Echo Reply hoặc Time Exceeded)
                            {
                                uint replyAddressUint = reply.Address;
                                lastAddress = IPAddress.Parse(UintToIP(replyAddressUint));
                                if (rttFromBuffer == 0 && lastAddress.Equals(resultModel.TargetIP))
                                {
                                    if (reply.RoundTripTime > 0)
                                    {
                                        rttFromBuffer = reply.RoundTripTime;
                                    }
                                }

                                responseTime = $"{rttFromBuffer} ms";
                                // KIỂM TRA ĐÃ ĐẾN ĐÍCH CHƯA
                                if (lastAddress.Equals(resultModel.TargetIP))
                                {
                                    destinationReached = true;
                                }
                            }
                        }

                        if (i == 1) currentHop.Time1 = responseTime;
                        else if (i == 2) currentHop.Time2 = responseTime;
                        else if (i == 3) currentHop.Time3 = responseTime;
                    }

                    // Cập nhật HostName/IP
                    if (lastAddress != null)
                    {
                        currentHop.IPAddress = lastAddress;
                        currentHop.IsDestination = destinationReached;

                        // KIỂM TRA TÙY CHỌN KHÔNG PHÂN GIẢI (-d)
                        if (!options.NoReverseDns)
                        {
                            try
                            {
                                var hostEntry = await Dns.GetHostEntryAsync(lastAddress);
                                currentHop.HostName = hostEntry.HostName;
                            }
                            catch { currentHop.HostName = lastAddress.ToString(); }
                        }
                        else
                        {
                            currentHop.HostName = lastAddress.ToString();
                        }
                    }
                    else
                    {
                        currentHop.HostName = "*";
                    }

                    resultModel.Hops.Add(currentHop);
                    Marshal.FreeHGlobal(optionsPtr);

                    if (destinationReached) break;
                }
            }
            finally
            {
                if (icmpHandle != IntPtr.Zero) IcmpCloseHandle(icmpHandle);
                if (replyBufferPtr != IntPtr.Zero) Marshal.FreeHGlobal(replyBufferPtr);
                if (requestDataPtr != IntPtr.Zero) Marshal.FreeHGlobal(requestDataPtr);
            }

            return View(resultModel);
        }
    }
}