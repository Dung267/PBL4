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
        private static CancellationTokenSource _cts;
        private static PingResult _currentResult;

        public IActionResult Index()
        {
            if (_currentResult != null)
                return View(_currentResult);

            return View(new PingResult());
        }

        [HttpPost]
        public IActionResult Index(string host, PingOptions options)
        {
            _cts = new CancellationTokenSource();
            _currentResult = DoIcmpPing(host, options, _cts.Token);
            return View(_currentResult);
        }

        [HttpPost]
        public IActionResult Stop()
        {
            _cts?.Cancel();

            if (_currentResult == null)
                _currentResult = new PingResult();

            _currentResult.Replies.Add("Ping stopped by user.");
            return View("Index", _currentResult);
        }

        private PingResult DoIcmpPing(string host, PingOptions options, CancellationToken token)
        {
            PingResult result = new PingResult { Target = host };
            IPAddress ip;

            try
            {
                ip = Dns.GetHostAddresses(host).FirstOrDefault()
                     ?? throw new Exception("Cannot resolve host");
            }
            catch
            {
                result.Replies.Add("DNS resolution failed");
                return result;
            }

            result.Target = $"{host} [{ip}]";

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
                                result.Replies.Add("Received non-echo reply (IPv4)");
                            }
                        }
                    }
                    else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        using (Socket socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Raw, (ProtocolType)58))
                        {
                            socket.ReceiveTimeout = options.Timeout;

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
                                result.Replies.Add("Received non-echo reply (IPv6)");
                            }
                        }
                    }
                }
                catch
                {
                    result.Replies.Add("Request timed out.");
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
