using Microsoft.AspNetCore.Mvc;
using PBL_4.Models;
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PBL4.Controllers
{
    public class NSLookupController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        // Lấy thông tin DNS server cục bộ
        private static (string server, string address) GetLocalDnsInfo()
        {
            string serverName = "Local DNS Resolver";
            string address = "Không xác định";

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var ipProps = ni.GetIPProperties();
                    foreach (var dns in ipProps.DnsAddresses)
                    {
                        address = dns.ToString();
                        return (serverName, address);
                    }
                }
            }
            catch { }

            return (serverName, address);
        }

        [HttpPost]
        public async Task<IActionResult> Index(string domainOrIp, string recordType, string customDns, int timeout = 2000, int retries = 1)
        {
            var (localServer, localAddress) = GetLocalDnsInfo();
            string dnsServer = string.IsNullOrWhiteSpace(customDns) ? localAddress : customDns;
            string serverName = string.IsNullOrWhiteSpace(customDns) ? localServer : "Custom DNS";
            string result = $"Server: {(string.IsNullOrWhiteSpace(customDns) ? localServer : "Custom DNS")}\nAddress: {dnsServer}\n\n";


            try
            {
                if (string.IsNullOrWhiteSpace(domainOrIp))
                {
                    result += "⚠️ Vui lòng nhập tên miền hoặc địa chỉ IP.";
                }
                else if (!IsValidDomainOrIp(domainOrIp))
                {
                    result += "⚠️ Định dạng không hợp lệ. Vui lòng nhập địa chỉ IP hợp lệ (vd: 8.8.8.8) hoặc tên miền (vd: vnexpress.com).\n";
                }
                else
                {
                    // Kiểm tra là IP hay tên miền
                    if (IPAddress.TryParse(domainOrIp, out IPAddress ip))
                    {
                        // Reverse lookup (PTR)
                        try
                        {
                            var entry = await ResolveDnsAsync(() => Dns.GetHostEntry(ip), timeout, retries);
                            result += "Non-authoritative answer:\n";
                            result += $"Name: {entry.HostName}\n";
                            result += $"Address: {ip}\n";
                        }
                        catch
                        {
                            result += "❌ Không thể tra ngược địa chỉ IP.\n";
                        }
                    }
                    else
                    {
                        // Forward lookup
                        result += "Non-authoritative answer:\n";
                        result += $"Name: {domainOrIp}\n";

                        try
                        {
                            var addresses = await ResolveDnsAsync(() => Dns.GetHostAddresses(domainOrIp), timeout, retries);

                            if (addresses.Length == 0)
                            {
                                result += "❌ Không tìm thấy kết quả.\n";
                            }
                            else
                            {
                                foreach (var addr in addresses)
                                {
                                    if (recordType == "A" && addr.AddressFamily == AddressFamily.InterNetwork)
                                        result += $"Address: {addr}\n";
                                    else if (recordType == "AAAA" && addr.AddressFamily == AddressFamily.InterNetworkV6)
                                        result += $"IPv6 Address: {addr}\n";
                                    else if (string.IsNullOrEmpty(recordType))
                                        result += $"{(addr.AddressFamily == AddressFamily.InterNetwork ? "Address" : "IPv6 Address")}: {addr}\n";
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            result += $"❌ Lỗi khi truy vấn DNS: {ex.Message}\n";
                        }

                        // CNAME
                        if (recordType == "CNAME")
                        {
                            try
                            {
                                var entry = await ResolveDnsAsync(() => Dns.GetHostEntry(domainOrIp), timeout, retries);
                                result += $"\nCanonical Name: {entry.HostName}\n";
                            }
                            catch
                            {
                                result += "\n❌ Không tìm thấy CNAME.\n";
                            }
                        }

                        // PTR
                        if (recordType == "PTR")
                        {
                            try
                            {
                                var entry = await ResolveDnsAsync(() => Dns.GetHostEntry(domainOrIp), timeout, retries);
                                result += $"\nReverse (PTR): {entry.HostName}\n";
                            }
                            catch
                            {
                                result += "\n❌ Không thể tra PTR.\n";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result += $"⚠️ Lỗi: {ex.Message}";
            }

            ViewBag.Result = result;
            ViewBag.DomainOrIp = "";
            ViewBag.RecordType = recordType;
            ViewBag.CustomDns = customDns;
            ViewBag.Timeout = timeout;
            ViewBag.Retries = retries;

            return View();
        }

        // Kiểm tra hợp lệ domain hoặc IP
        private bool IsValidDomainOrIp(string input)
        {
            // Là IP thì hợp lệ
            if (IPAddress.TryParse(input, out _))
                return true;

            // Regex kiểm tra domain (vd: example.com)
            string domainPattern = @"^(?!\-)(?:[a-zA-Z0-9\-]{1,63}\.)+[a-zA-Z]{2,}$";
            return System.Text.RegularExpressions.Regex.IsMatch(input, domainPattern);
        }


        // Hàm POST cũ, giữ lại để tương thích

        [HttpPost]
        public IActionResult Lookup(string domain, string recordType, string customDns, int timeout, int retry)
        {
            string result;

            if (string.IsNullOrWhiteSpace(customDns))
                result = NSLookupModel.Lookup(domain, recordType); // Gọi hàm cũ
            else
                result = NSLookupModel.LookupWithCustomDns(domain, recordType, customDns, timeout, retry);

            ViewBag.Result = result;
            ViewBag.Domain = "";
            return View();
        }


        // Hàm chạy DNS có Timeout & Retry
        private async Task<T> ResolveDnsAsync<T>(Func<T> action, int timeoutMs, int retries)
        {
            for (int attempt = 0; attempt < retries; attempt++)
            {
                var task = Task.Run(action);
                if (await Task.WhenAny(task, Task.Delay(timeoutMs)) == task)
                    return task.Result; // Thành công
            }
            throw new TimeoutException("DNS query timed out.");
        }
    }
}
