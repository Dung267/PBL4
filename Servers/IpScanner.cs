using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

/// <summary>
/// Lớp quét IP cơ bản (ICMP + fallback TCP).
/// Không dùng thư viện ngoài.
/// </summary>
public static class IpScanner
{
    public class ScanResult
    {
        public string Ip { get; set; } = "";
        public bool Alive { get; set; }
        public int? RttMs { get; set; }
    }

    /// <summary>
    /// Quét một range IP.
    /// range có thể là single IP, start-end hoặc CIDR (đã được kiểm tra từ controller).
    /// </summary>
    
    public static async Task<ConcurrentBag<ScanResult>> ScanAsync(
        string range,
        int timeoutMs = 700,
        int maxParallel = 150,
        int[]? tcpFallbackPorts = null)
    {
        // parse range sang list IP
        var ips = ExpandRange(range);
        var bag = new ConcurrentBag<ScanResult>();

        using (var sem = new System.Threading.SemaphoreSlim(maxParallel))
        {
            var tasks = new List<Task>();
            foreach (var ip in ips)
            {
                await sem.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var alive = await PingHost(ip, timeoutMs, tcpFallbackPorts);
                        bag.Add(alive);
                    }
                    finally { sem.Release(); }
                }));
            }
            await Task.WhenAll(tasks);
        }

        return bag;
    }

    #region --- helpers ---
    private static async Task<ScanResult> PingHost(string ip, int timeoutMs, int[]? tcpFallbackPorts)
    {
        var result = new ScanResult { Ip = ip, Alive = false };

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // thử ICMP (cần quyền admin trên Windows)
            using (var ping = new System.Net.NetworkInformation.Ping())
            {
                var reply = await ping.SendPingAsync(ip, timeoutMs);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    result.Alive = true;
                    result.RttMs = (int)reply.RoundtripTime;
                    return result;
                }
            }
        }
        catch { /* ignore, fallback TCP */ }

        // fallback TCP nếu ICMP thất bại
        if (tcpFallbackPorts != null)
        {
            foreach (var port in tcpFallbackPorts)
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    using (var client = new TcpClient())
                    {
                        var connectTask = client.ConnectAsync(ip, port);
                        var finished = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                        if (finished == connectTask && client.Connected)
                        {
                            result.Alive = true;
                            result.RttMs = (int)sw.ElapsedMilliseconds;
                            return result;
                        }
                    }
                }
                catch { /* ignore */ }
            }
        }

        return result;
    }

    private static IEnumerable<string> ExpandRange(string range)
    {
        // ở controller mình đã kiểm tra rồi, nhưng có thể double-check
        if (range.Contains("-"))
        {
            var parts = range.Split('-', StringSplitOptions.RemoveEmptyEntries);
            var start = IPAddress.Parse(parts[0].Trim());
            var end = IPAddress.Parse(parts[1].Trim());

            uint s = IpToUint(start), e = IpToUint(end);
            for (uint i = s; i <= e; i++)
                yield return UintToIp(i).ToString();
        }
        else if (range.Contains("/"))
        {
            var parts = range.Split('/');
            var ip = IPAddress.Parse(parts[0]);
            int prefix = int.Parse(parts[1]);
            var (start, end) = CidrToRange(ip, prefix);

            uint s = IpToUint(start), e = IpToUint(end);
            for (uint i = s; i <= e; i++)
                yield return UintToIp(i).ToString();
        }
        else
        {
            yield return IPAddress.Parse(range).ToString();
        }
    }

    private static (IPAddress start, IPAddress end) CidrToRange(IPAddress ip, int prefix)
    {
        uint ipu = IpToUint(ip);
        uint mask = prefix == 0 ? 0u : 0xffffffffu << (32 - prefix);
        uint net = ipu & mask;
        uint startHost = net;
        uint endHost = net | ~mask;
        return (UintToIp(startHost), UintToIp(endHost));
    }

    private static uint IpToUint(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToUInt32(b, 0);
    }

    private static IPAddress UintToIp(uint v)
    {
        var b = BitConverter.GetBytes(v);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return new IPAddress(b);
    }
    #endregion
}
