using Microsoft.AspNetCore.Mvc;
using PBL4.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Linq;

namespace PBL4.Controllers
{
    public class TracertController : Controller
    {
        // GET: Hiển thị giao diện hoặc kết quả cuối cùng
        public IActionResult Index()
        {
            return View(new TracertResult());
        }

        // POST: Bắt đầu Tracert
        [HttpPost]
        public IActionResult Index(string host, TracertOptions options)
        {
            if (options.ForceIpv4 && options.ForceIpv6)
            {
                var conflictResult = new TracertResult { Target = host };
                conflictResult.Messages.Add("Lỗi: Không thể dùng -4 và -6 cùng một lúc.");
                return View(conflictResult);
            }

            if (options.MaxHops == 0) options.MaxHops = 30;
            if (options.Timeout == 0) options.Timeout = 4000;

            var result = DoTracert(host, options);
            return View(result);
        }

        private TracertResult DoTracert(string host, TracertOptions options)
        {
            var result = new TracertResult { Target = host };
            IPAddress targetIp = null;

            try
            {
                // 1. LỌC IP (-4 và -6)
                var ipAddresses = Dns.GetHostAddresses(host);

                if (options.ForceIpv4)
                    targetIp = ipAddresses.FirstOrDefault(addr => addr.AddressFamily == AddressFamily.InterNetwork)
                        ?? throw new Exception("Host has no IPv4 address.");
                else if (options.ForceIpv6)
                    targetIp = ipAddresses.FirstOrDefault(addr => addr.AddressFamily == AddressFamily.InterNetworkV6)
                        ?? throw new Exception("Host has no IPv6 address.");
                else
                    targetIp = ipAddresses.FirstOrDefault()
                        ?? throw new Exception("Cannot resolve host.");

                result.TargetIP = targetIp.ToString();
                result.MaxHops = options.MaxHops; // Lưu MaxHops vào Result
            }
            catch (Exception ex)
            {
                result.Messages.Add($"Lỗi DNS: {ex.Message}");
                return result;
            }

            // 2. CHUẨN BỊ RAW SOCKET
            var protocolFamily = targetIp.AddressFamily;
            // Tracert dựa trên ICMP/ICMPv6
            var protocolType = protocolFamily == AddressFamily.InterNetwork ? ProtocolType.Icmp : ProtocolType.IcmpV6;

            // Khởi tạo Raw Socket (CẦN ADMIN!)
            using (var socket = new Socket(protocolFamily, SocketType.Raw, protocolType))
            {
                try
                {
                    // Listener/Sender dùng chung một Socket khi là Raw Socket
                    socket.Bind(new IPEndPoint(protocolFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0));
                    socket.ReceiveTimeout = options.Timeout;
                }
                catch (SocketException ex) when (ex.ErrorCode == 10013)
                {
                    result.Messages.Add("LỖI: Tracert bằng Raw Socket cần quyền Quản trị viên (Admin).");
                    result.Messages.Add($"Chi tiết: {ex.Message}");
                    return result;
                }
                catch (Exception ex)
                {
                    result.Messages.Add($"Lỗi cấu hình Socket: {ex.Message}");
                    return result;
                }

                // 3. VÒNG LẶP CHÍNH CỦA TRACERT
                for (int ttl = 1; ttl <= options.MaxHops; ttl++)
                {
                    var hop = new HopResult { HopNumber = ttl };
                    bool destinationReached = false;

                    // Gửi 3 gói tin cho mỗi hop
                    for (int seq = 0; seq < 3; seq++)
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        try
                        {
                            // --- THAO TÁC TTL VÀ GỬI GÓI TIN ICMP ---
                            socket.Ttl = (short)ttl;

                            // Chỉ hỗ trợ ICMPv4 trong hàm BuildIcmpv4Packet, giả định Tracert chỉ chạy trên IPv4.
                            if (protocolFamily != AddressFamily.InterNetwork)
                            {
                                // Bỏ qua nếu là IPv6 vì chưa có hàm BuildIcmpv6Packet
                                result.Messages.Add("Cảnh báo: Hàm BuildIcmpv4Packet không hỗ trợ IPv6.");
                                result.Success = false;
                                return result;
                            }

                            // Xây dựng gói ICMP Echo Request
                            byte[] packet = BuildIcmpv4Packet((ushort)seq, 32);

                            EndPoint endPoint = new IPEndPoint(targetIp, 0);
                            socket.SendTo(packet, endPoint);

                            // --- LẮNG NGHE PHẢN HỒI ---
                            byte[] buffer = new byte[1024];
                            EndPoint senderEndPoint = new IPEndPoint(IPAddress.Any, 0);

                            int bytesRead = socket.ReceiveFrom(buffer, ref senderEndPoint);
                            sw.Stop();

                            var replyIp = ((IPEndPoint)senderEndPoint).Address;

                            if (bytesRead > 0)
                            {
                                // Xử lý IPv4 ICMP: ICMP Header bắt đầu từ byte 20 (sau 20 byte IP Header)
                                int icmpTypeOffset = 20;
                                byte icmpType = buffer[icmpTypeOffset];

                                // Cập nhật thông tin Hop
                                hop.Address = replyIp.ToString();
                                hop.Times.Add(sw.ElapsedMilliseconds);

                                // Kiểm tra: ICMP Type 0 là Echo Reply (Đích đến). 
                                // ICMP Type 11 là Time Exceeded (Hop trung gian).
                                if (icmpType == 0)
                                {
                                    destinationReached = true;
                                    hop.ReachedDestination = true;
                                    break; // Dừng 3 lần gửi nếu đã đến đích
                                }
                            }
                        }
                        catch (SocketException ex) when (ex.ErrorCode == 10060) // Lỗi Timeout
                        {
                            hop.Times.Add(-1);
                            sw.Stop();
                        }
                        catch (Exception)
                        {
                            hop.Times.Add(-1);
                            sw.Stop();
                        }
                    } // Kết thúc 3 gói gửi

                    // 4. XỬ LÝ VÀ HIỂN THỊ KẾT QUẢ HOP
                    if (hop.Address == null)
                    {
                        hop.Address = "*";
                        while (hop.Times.Count < 3) hop.Times.Add(-1);
                    }

                    // THỰC THI -d (Không phân giải tên)
                    if (!options.NoResolve && hop.Address != "*")
                    {
                        try
                        {
                            hop.HostName = Dns.GetHostEntry(hop.Address).HostName;
                        }
                        catch
                        {
                            hop.HostName = hop.Address;
                        }
                    }
                    else
                    {
                        hop.HostName = hop.Address;
                    }

                    result.Hops.Add(hop);

                    // Dừng nếu đã đến đích
                    if (destinationReached)
                    {
                        result.Success = true;
                        break;
                    }
                } // Kết thúc vòng lặp TTL
            }

            return result;
        }

        // --- HÀM XÂY DỰNG GÓI ICMP (Checksum) ---
        private static ushort Checksum(byte[] packet)
        {
            long sum = 0;
            int packetLength = packet.Length;
            int i = 0;

            // Cộng dồn 16-bit (2 bytes)
            while (packetLength > 1)
            {
                sum += (long)((packet[i] << 8) | packet[i + 1]);
                i += 2;
                packetLength -= 2;
            }

            // Nếu còn 1 byte lẻ
            if (packetLength > 0)
            {
                sum += packet[i];
            }

            // Cộng dồn 16 bit từ các overflow (carry over)
            sum = (sum >> 16) + (sum & 0xFFFF);
            sum += (sum >> 16);

            // Hoàn trả bù 1 (One's complement)
            return (ushort)~sum;
        }

        // --- HÀM XÂY DỰNG GÓI ICMPV4 ECHO REQUEST ---
        private byte[] BuildIcmpv4Packet(ushort sequenceNumber, int dataSize)
        {
            // Cấu trúc ICMP: 8 bytes Header + Data
            // Byte 0: Type (8 = Echo Request)
            // Byte 1: Code (0)
            // Byte 2-3: Checksum
            // Byte 4-5: Identifier (Process ID)
            // Byte 6-7: Sequence Number

            int totalLength = 8 + dataSize;
            byte[] packet = new byte[totalLength];

            // 1. Type và Code
            packet[0] = 8; // ICMP Type 8: Echo Request
            packet[1] = 0; // Code 0

            // 2. Checksum (Tạm thời là 0)
            packet[2] = 0;
            packet[3] = 0;

            // 3. Identifier (Dùng Process ID để nhận dạng gói tin của mình)
            ushort processId = (ushort)Process.GetCurrentProcess().Id;
            packet[4] = (byte)(processId >> 8);
            packet[5] = (byte)processId;

            // 4. Sequence Number
            packet[6] = (byte)(sequenceNumber >> 8);
            packet[7] = (byte)sequenceNumber;

            // 5. Data (Tùy chọn)
            for (int i = 8; i < totalLength; i++)
            {
                packet[i] = (byte)'a'; // Điền dữ liệu giả
            }

            // 6. Tính Checksum cuối cùng
            ushort checkSum = Checksum(packet);
            packet[2] = (byte)(checkSum >> 8);
            packet[3] = (byte)checkSum;

            return packet;
        }
    }
}