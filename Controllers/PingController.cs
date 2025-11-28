using Microsoft.AspNetCore.Mvc;
using PBL4.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
namespace PBL4.Controllers
{
    public class PingController : Controller
    {
        private static CancellationTokenSource? _cts;
        private static PingResult? _currentResult;

        // GET: Hiển thị giao diện và kết quả cuối cùng
        public IActionResult Index()
        {
            ViewBag.ActiveMode = "gui";
            if (_currentResult != null)
                return View(_currentResult);
            return View(new PingResult());
        }

        // POST 1: Chế độ Giao diện (GUI Mode) - Bắt đầu Ping
        [HttpPost]
        public IActionResult Index(string host, PingOptions options)
        {
            ViewBag.ActiveMode = "gui";
            if (options.Count == 0 && !options.Continuous) options.Count = 4;
            if (options.Timeout == 0) options.Timeout = 5000;

            if (options.ForceIpv4 && options.ForceIpv6)
            {
                _currentResult = new PingResult
                {
                    Target = "Conflict Error",
                };
                _currentResult.Replies.Add("Lỗi: Không thể dùng -4 và -6 cùng một lúc.");
                return View("Index", _currentResult);
            }
            return StartPing(host, options);
        }

        // POST 2: Chế độ Lệnh (Command Mode) - Bắt đầu Ping
        [HttpPost]
        public IActionResult Command(string command)
        {
            ViewBag.ActiveMode = "command";
            ViewBag.CommandInput = command;
            if (string.IsNullOrWhiteSpace(command))
            {
                _currentResult = new PingResult { Target = "No command entered." };
                _currentResult.Replies.Add("Vui lòng nhập lệnh ping.");
                return View("Index", _currentResult);
            }

            try
            {
                var (host, options) = ParsePingCommand(command);

                if (options.Count == 0 && !options.Continuous) options.Count = 4;
                if (options.Timeout == 0) options.Timeout = 5000;

                if (options.ForceIpv4 && options.ForceIpv6)
                {
                    _currentResult = new PingResult { Target = "Conflict Error" };
                    _currentResult.Replies.Add("Lỗi: Không thể dùng -4 và -6 cùng một lúc.");
                    return View("Index", _currentResult);
                }

                return StartPing(host, options);
            }
            catch (ArgumentException ex)
            {
                _currentResult = new PingResult { Target = "Command Error" };
                _currentResult.Replies.Add($"Lỗi cú pháp lệnh: {ex.Message}");
                return View("Index", _currentResult);
            }
        }

       

        [HttpPost]
        public IActionResult GenerateCommand(string host, PingOptions options)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return Json(new { success = false, command = "Vui lòng nhập Host/IP." });
            }

            if (options.ForceIpv4 && options.ForceIpv6)
            {
                return Json(new { success = false, command = "Lỗi: Không thể dùng -4 và -6 cùng lúc." });
            }

            StringBuilder commandBuilder = new StringBuilder();
            commandBuilder.Append("ping");

            // Chỉ thêm những tùy chọn khác mặc định
            if (options.Continuous) commandBuilder.Append(" -t");
            else if (options.Count > 0 && options.Count != 4) commandBuilder.Append($" -n {options.Count}");

            if (options.BufferSize > 0 && options.BufferSize != 32) commandBuilder.Append($" -l {options.BufferSize}");
            if (options.Timeout > 0 && options.Timeout != 5000) commandBuilder.Append($" -w {options.Timeout}");
            if (options.Ttl > 0 && options.Ttl != 128) commandBuilder.Append($" -i {options.Ttl}");

            if (options.ResolveHostname) commandBuilder.Append(" -a");
            if (options.ForceIpv4) commandBuilder.Append(" -4");
            if (options.ForceIpv6) commandBuilder.Append(" -6");
            if (options.DontFragment) commandBuilder.Append(" -f");
            if (!string.IsNullOrWhiteSpace(options.SourceAddress)) commandBuilder.Append($" -S {options.SourceAddress}");

            // Host luôn ở cuối
            commandBuilder.Append($" {host}");

            return Json(new { success = true, command = commandBuilder.ToString() });
        }



        [HttpPost]
        public IActionResult Stop()
        {
            _cts?.Cancel();
            if (_currentResult == null) _currentResult = new PingResult();
            _currentResult.Replies.Add("Ping stopped by user.");
            return View("Index", _currentResult);
        }

        // Action dùng chung để bắt đầu Ping
        private IActionResult StartPing(string host, PingOptions options)
        {
            _cts = new CancellationTokenSource();
            _currentResult = DoIcmpPing(host, options, _cts.Token);
            return View("Index", _currentResult);
        }


        private (string host, PingOptions options) ParsePingCommand(string command)
        {
            var options = new PingOptions();
            string host = null;
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2 || !parts[0].Equals("ping", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Lệnh không hợp lệ. Phải bắt đầu bằng 'ping'.");
            }

            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i];

                if (part.StartsWith("-"))
                {
                    string option = part;

                    // Tùy chọn không có giá trị
                    if (option.Equals("-t", StringComparison.OrdinalIgnoreCase)) { options.Continuous = true; continue; }
                    if (option.Equals("-a", StringComparison.OrdinalIgnoreCase)) { options.ResolveHostname = true; continue; }
                    if (option.Equals("-4", StringComparison.OrdinalIgnoreCase)) { options.ForceIpv4 = true; continue; }
                    if (option.Equals("-6", StringComparison.OrdinalIgnoreCase)) { options.ForceIpv6 = true; continue; }
                    if (option.Equals("-f", StringComparison.OrdinalIgnoreCase)) { options.DontFragment = true; continue; }

                    // Tùy chọn có giá trị
                    if (i + 1 >= parts.Length) throw new ArgumentException($"Thiếu giá trị cho tùy chọn '{option}'.");
                    string valueStr = parts[++i];

                    if (option.Equals("-n", StringComparison.OrdinalIgnoreCase) && int.TryParse(valueStr, out int count)) options.Count = count;
                    else if (option.Equals("-l", StringComparison.OrdinalIgnoreCase) && int.TryParse(valueStr, out int size)) options.BufferSize = size;
                    else if (option.Equals("-w", StringComparison.OrdinalIgnoreCase) && int.TryParse(valueStr, out int timeout)) options.Timeout = timeout;
                    else if (option.Equals("-i", StringComparison.OrdinalIgnoreCase) && int.TryParse(valueStr, out int ttl)) options.Ttl = ttl;
                    else if (option.Equals("-S", StringComparison.OrdinalIgnoreCase)) options.SourceAddress = valueStr;
                    else throw new ArgumentException($"Tùy chọn '{option}' không hợp lệ hoặc giá trị '{valueStr}' không hợp lệ.");
                }
                else
                {
                    if (host == null) host = part; // Host là phần tử không phải option đầu tiên
                    else
                        throw new ArgumentException($"Không thể có nhiều hơn 1 host. Phần tử '{part}' không hợp lệ.");
                }
            }

            if (host == null) throw new ArgumentException("Không tìm thấy host trong lệnh.");

            if (options.Continuous) options.Count = int.MaxValue;

            return (host, options);
        }

        private PingResult DoIcmpPing(string host, PingOptions options, CancellationToken token)
        {
            PingResult result = new PingResult { Target = host };
            IPAddress ip = null;
            IPAddress[] ipAddresses;

            try
            {
                ipAddresses = Dns.GetHostAddresses(host);

                // --- LOGIC LỌC IP (-4 và -6) ---
                if (options.ForceIpv4)
                {
                    ip = ipAddresses.FirstOrDefault(addr => addr.AddressFamily == AddressFamily.InterNetwork)
                        ?? throw new Exception("Host has no IPv4 address.");
                }
                else if (options.ForceIpv6)
                {
                    ip = ipAddresses.FirstOrDefault(addr => addr.AddressFamily == AddressFamily.InterNetworkV6)
                        ?? throw new Exception("Host has no IPv6 address.");
                }
                else
                {
                    ip = ipAddresses.FirstOrDefault()
                        ?? throw new Exception("Cannot resolve host or no supported address found.");
                }
            }
            catch (Exception ex)
            {
                result.Replies.Add($"DNS resolution failed: {ex.Message}");
                return result;
            }

            // --- LOGIC PHÂN GIẢI TÊN (-a) ---
            string targetName = host;
            if (options.ResolveHostname && ip != null)
            {
                try
                {
                    targetName = Dns.GetHostEntry(ip).HostName;
                }
                catch
                {
                    targetName = ip.ToString();
                    result.Replies.Add($"Warning: Reverse DNS failed for {ip}.");
                }
            }

            result.Target = $"{targetName} [{ip}]";

            List<long> times = new();
            int sent = 0;
            int received = 0;

            int loopCount = options.Continuous ? int.MaxValue : options.Count;

            for (int i = 0; i < loopCount; i++)
            {
                if (token.IsCancellationRequested)
                {
                    result.Replies.Add("Ping stopped by user.");
                    break;
                }

                sent++;
                try
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp))
                        {
                            socket.ReceiveTimeout = options.Timeout;
                            socket.Ttl = (short)options.Ttl;

                            // --- THỰC THI -S (Source Address) ---
                            if (!string.IsNullOrWhiteSpace(options.SourceAddress))
                            {
                                if (IPAddress.TryParse(options.SourceAddress, out IPAddress sourceIp))
                                {
                                    socket.Bind(new IPEndPoint(sourceIp, 0));
                                }
                                else
                                {
                                    throw new Exception($"Invalid source address: {options.SourceAddress}");
                                }
                            }

                            // --- THỰC THI -f (Don't Fragment) ---
                            if (options.DontFragment)
                            {
                                // Cần quyền Admin cao cấp
                                const int IP_DONTFRAGMENT = 14;
                                socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)IP_DONTFRAGMENT, true);
                            }

                            byte[] packet = BuildIcmpv4Packet((ushort)i, options.BufferSize);
                            EndPoint endPoint = new IPEndPoint(ip, 0);
                            byte[] buffer = new byte[1024];

                            Stopwatch sw = Stopwatch.StartNew();
                            socket.SendTo(packet, endPoint);
                            int recv = socket.Receive(buffer);
                            sw.Stop();

                            if (recv > 20 && buffer[20] == 0)
                            {
                                received++;
                                long time = sw.ElapsedMilliseconds;
                                times.Add(time);
                                int ttl = buffer[8];
                                int bytes = packet.Length;
                                result.Replies.Add($"Reply from {ip}: bytes={bytes} time={time}ms TTL={ttl}");
                            }
                            else
                            {
                                result.Replies.Add("Received non-echo reply or error (IPv4)");
                            }
                        }
                    }
                    else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        using (Socket socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Raw, (ProtocolType)58))
                        {
                            socket.ReceiveTimeout = options.Timeout;

                            // --- THỰC THI -S (Source Address) cho IPv6 ---
                            if (!string.IsNullOrWhiteSpace(options.SourceAddress))
                            {
                                if (IPAddress.TryParse(options.SourceAddress, out IPAddress sourceIp))
                                {
                                    socket.Bind(new IPEndPoint(sourceIp, 0));
                                }
                                else
                                {
                                    throw new Exception($"Invalid source address: {options.SourceAddress}");
                                }
                            }

                            byte[] packet = BuildIcmpv6Packet((ushort)i, options.BufferSize);
                            EndPoint endPoint = new IPEndPoint(ip, 0);
                            byte[] buffer = new byte[1024];

                            Stopwatch sw = Stopwatch.StartNew();
                            socket.SendTo(packet, endPoint);
                            int recv = socket.Receive(buffer);
                            sw.Stop();

                            int offset = recv > 40 ? 40 : 0;
                            if (recv > offset && buffer[offset] == 129)
                            {
                                received++;
                                long time = sw.ElapsedMilliseconds;
                                times.Add(time);
                                int bytes = packet.Length;
                                result.Replies.Add($"Reply from {ip}: bytes={bytes} time={time}ms (IPv6)");
                            }
                            else
                            {
                                result.Replies.Add("Received non-echo reply or error (IPv6)");
                            }
                        }
                    }
                }
                // Bắt lỗi Socket, ví dụ: Lỗi quyền hạn khi set -f (thường là lỗi 10022 - Invalid Argument)
                catch (SocketException sex) when (options.DontFragment && sex.ErrorCode == 10022)
                {
                    result.Replies.Add($"Error: Could not set Don't Fragment flag (-f). Check Admin privileges.");
                }
                catch (Exception ex)
                {
                    result.Replies.Add($"Request timed out or network error: {ex.Message}");
                }

                if (!options.Continuous)
                    Thread.Sleep(1000);
            }

            result.Sent = sent;
            result.Received = received;

            if (times.Count > 0)
            {
                result.MinTime = times.Min();
                result.MaxTime = times.Max();
                result.AvgTime = (long)times.Average();
            }

            _currentResult = result;
            return result;
        }

        private byte[] BuildIcmpv4Packet(ushort seq, int size)
        {
            byte type = 8;
            byte code = 0;
            ushort id = 1;
            byte[] data = Encoding.ASCII.GetBytes(new string('A', size));

            byte[] packet = new byte[8 + data.Length];
            packet[0] = type;
            packet[1] = code;
            packet[4] = (byte)(id >> 8);
            packet[5] = (byte)(id & 0xFF);
            packet[6] = (byte)(seq >> 8);
            packet[7] = (byte)(seq & 0xFF);

            Array.Copy(data, 0, packet, 8, data.Length);

            ushort cs = CalculateChecksum(packet);
            packet[2] = (byte)(cs >> 8);
            packet[3] = (byte)(cs & 0xFF);

            return packet;
        }

        private byte[] BuildIcmpv6Packet(ushort seq, int size)
        {
            byte type = 128;
            byte code = 0;
            ushort id = 1;
            byte[] data = Encoding.ASCII.GetBytes(new string('A', size));

            byte[] packet = new byte[8 + data.Length];
            packet[0] = type;
            packet[1] = code;
            packet[4] = (byte)(id >> 8);
            packet[5] = (byte)(id & 0xFF);
            packet[6] = (byte)(seq >> 8);
            packet[7] = (byte)(seq & 0xFF);

            Array.Copy(data, 0, packet, 8, data.Length);
            return packet;
        }

        private ushort CalculateChecksum(byte[] buffer)
        {
            int sum = 0;
            for (int i = 0; i < buffer.Length; i += 2)
            {
                ushort word = (ushort)((buffer[i] << 8) + (i + 1 < buffer.Length ? buffer[i + 1] : 0));
                sum += word;
                while ((sum >> 16) != 0)
                    sum = (sum & 0xFFFF) + (sum >> 16);
            }
            return (ushort)~sum;
        }
    }
}