using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace YourProject.Controllers
{
    /// <summary>
    /// Controller cho chức năng scan IP.
    /// Yêu cầu trong project phải có IpScanner.ScanAsync (lớp static mà Ngọc đã có).
    /// </summary>
    [Route("ScanIp")]
    public class ScanIpController : Controller
    {
        // GET: /ScanIp
        [HttpGet("")]
        public IActionResult Index() => View();

        /// <summary>
        /// POST: /ScanIp/Start
        /// Body JSON: { "range":"192.168.1.1-192.168.1.20" , "timeoutMs":700, "maxParallel":150, "tcpFallbackPorts":[80,443] }
        /// Trả về JSON: { ok=true, results=[{ip, alive, rttMs}, ...] }
        /// </summary>
        [HttpPost("Start")]
        public async Task<IActionResult> Start([FromBody] ScanRequest req)
        {
            // 1) Basic check
            if (req == null || string.IsNullOrWhiteSpace(req.Range))
                return BadRequest(new { error = "Thiếu tham số Range (VD: 192.168.1.1, 192.168.1.1-192.168.1.20 hoặc 192.168.1.0/28)" });

            try
            {
                // 2) Parse range input -> (start,end,count)
                var (startIp, endIp, count) = ParseRangeAndCount(req.Range.Trim());

                // 3) Safety check
                const uint MAX_HOSTS = 1024;
                if (count == 0) return BadRequest(new { error = "Phạm vi IP rỗng" });
                if (count > MAX_HOSTS)
                    return BadRequest(new { error = $"Phạm vi quá lớn ({count} host). Tối đa cho phép: {MAX_HOSTS}" });

                // 4) Gọi scanner
                var bag = await IpScanner.ScanAsync(
                    req.Range,
                    timeoutMs: req.TimeoutMs ?? 700,
                    maxParallel: req.MaxParallel ?? 150,
                    tcpFallbackPorts: req.TcpFallbackPorts?.ToArray()
                );

                // 5) Lấy hostname (nếu alive) + sắp xếp kết quả
                var list = new List<object>();

                foreach (var x in bag.OrderBy(x => IPAddress.Parse(x.Ip).GetAddressBytes(), new IpSorter()))
                {
                    string? hostname = null;
                    if (x.Alive)
                    {
                        try
                        {
                            var entry = await Dns.GetHostEntryAsync(x.Ip);
                            hostname = entry.HostName;
                        }
                        catch
                        {
                            hostname = null; // không có tên, bỏ qua
                        }
                    }

                    list.Add(new
                    {
                        Ip = x.Ip,
                        Alive = x.Alive,
                        RttMs = x.RttMs,
                        Hostname = hostname
                    });
                }

                return Ok(new { ok = true, results = list });

            }
            catch (FormatException fex)
            {
                return BadRequest(new { error = "Range format không đúng: " + fex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ok = false, error = ex.Message });
            }
        }

        #region --- Helper: parse range/cidr and count hosts ---
        private static (IPAddress start, IPAddress end, uint count) ParseRangeAndCount(string input)
        {
            if (input.Contains("/"))
            {
                // CIDR
                var parts = input.Split('/');
                if (parts.Length != 2) throw new FormatException("CIDR phải có dạng x.x.x.x/yy");
                var ip = IPAddress.Parse(parts[0]);
                if (!int.TryParse(parts[1], out int prefix) || prefix < 0 || prefix > 32)
                    throw new FormatException("Prefix CIDR không hợp lệ (0-32)");
                var (start, end) = CidrToRange(ip, prefix);
                uint cnt = IpCount(start, end);
                return (start, end, cnt);
            }
            else if (input.Contains("-"))
            {
                // Range
                var parts = input.Split('-', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) throw new FormatException("Range phải có dạng start-end");
                var s = IPAddress.Parse(parts[0].Trim());
                var e = IPAddress.Parse(parts[1].Trim());
                if (IpToUint(e) < IpToUint(s)) throw new FormatException("End IP phải >= Start IP");
                uint cnt = IpCount(s, e);
                return (s, e, cnt);
            }
            else
            {
                // Single IP
                var ip = IPAddress.Parse(input);
                return (ip, ip, 1);
            }
        }

        private static (IPAddress start, IPAddress end) CidrToRange(IPAddress ip, int prefix)
        {
            uint ipu = IpToUint(ip);
            uint mask = prefix == 0 ? 0u : 0xffffffffu << (32 - prefix);
            uint net = ipu & mask;
            if (prefix >= 31)
            {
                uint start = net;
                uint end = net | ~mask;
                return (UintToIp(start), UintToIp(end));
            }
            uint startHost = net + 1;
            uint endHost = (net | ~mask) - 1;
            return (UintToIp(startHost), UintToIp(endHost));
        }

        private static uint IpCount(IPAddress start, IPAddress end)
        {
            return IpToUint(end) - IpToUint(start) + 1;
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

        /// <summary>
        /// So sánh mảng byte của IP để sắp xếp
        /// </summary>
        public class IpSorter : IComparer<byte[]>
        {
            public int Compare(byte[]? x, byte[]? y)
            {
                for (int i = 0; i < 4; i++)
                {
                    int c = x![i].CompareTo(y![i]);
                    if (c != 0) return c;
                }
                return 0;
            }
        }
    }

    /// <summary>
    /// Model request
    /// </summary>
    public class ScanRequest
    {
        public string Range { get; set; } = "";
        public int? TimeoutMs { get; set; }
        public int? MaxParallel { get; set; }
        public int[]? TcpFallbackPorts { get; set; }
    }
}
