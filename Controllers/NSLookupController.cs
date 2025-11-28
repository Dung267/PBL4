using Microsoft.AspNetCore.Mvc;
using PBL4.Services;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PBL4.Controllers
{
    public class NSLookupController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            // Mặc định vào là GUI
            ViewBag.ActiveMode = "gui";
            ViewBag.Timeout = 5000;
            ViewBag.Retries = 1;
            return View();
        }

        // --- XỬ LÝ GIAO DIỆN (GUI) ---
        [HttpPost]
        public async Task<IActionResult> Index(string domainOrIp, string recordType, string customDns, int? timeout, int retries = 1)
        {
            // Gọi hàm xử lý logic chung
            await ExecuteLookup(domainOrIp, recordType, customDns, timeout, retries);

            // Đánh dấu để View biết cần hiển thị tab GUI
            ViewBag.ActiveMode = "gui";

            return View("Index");
        }

        // --- XỬ LÝ DÒNG LỆNH (COMMAND) ---
        [HttpPost]
        public async Task<IActionResult> Command(string commandInput)
        {
            string domain = "";
            string dns = "";
            string type = "A";
            int timeoutMs = 5000;

            if (string.IsNullOrWhiteSpace(commandInput))
            {
                ViewBag.Result = "⚠️ Vui lòng nhập câu lệnh.";
            }
            else
            {
                try
                {
                    // Parse lệnh
                    var parts = commandInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        string p = part.ToLower();
                        if (p == "nslookup") continue;

                        if (p.StartsWith("-type=") || p.StartsWith("-q=") || p.StartsWith("-querytype="))
                        {
                            var split = part.Split('=');
                            if (split.Length > 1) type = split[1].ToUpper();
                        }
                        else if (p.StartsWith("-timeout="))
                        {
                            var split = part.Split('=');
                            if (split.Length > 1 && int.TryParse(split[1], out int seconds))
                            {
                                timeoutMs = seconds * 1000;
                            }
                        }
                        else if (IsValidDomainOrIp(part))
                        {
                            if (string.IsNullOrEmpty(domain)) domain = part;
                            else dns = part;
                        }
                    }

                    // Gọi hàm xử lý logic chung
                    await ExecuteLookup(domain, type, dns, timeoutMs, 1);
                }
                catch
                {
                    ViewBag.Result = "⚠️ Lỗi cú pháp lệnh.";
                }
            }

            // Đánh dấu để View biết cần hiển thị tab COMMAND
            ViewBag.ActiveMode = "command";

            // Trả lại câu lệnh người dùng vừa nhập để không bị mất
            ViewBag.CommandInput = commandInput;

            return View("Index");
        }

        // --- HÀM LOGIC CHUNG (Dùng cho cả 2 chế độ) ---
        private async Task ExecuteLookup(string domainOrIp, string recordType, string customDns, int? timeout, int retries)
        {
            int timeoutVal = timeout ?? 5000;
            if (string.IsNullOrEmpty(recordType)) recordType = "A";

            var (localServer, localAddress) = GetLocalDnsInfo();
            string dnsServer = string.IsNullOrWhiteSpace(customDns) ? localAddress : customDns;
            string serverName = string.IsNullOrWhiteSpace(customDns) ? localServer : "Custom DNS";

            string result = $"Server: {serverName}\nAddress: {dnsServer}\n\n";

            try
            {
                if (string.IsNullOrWhiteSpace(domainOrIp))
                {
                    result += "⚠️ Vui lòng nhập tên miền hoặc địa chỉ IP.";
                }
                else if (!IsValidDomainOrIp(domainOrIp))
                {
                    result += "⚠️ Định dạng không hợp lệ.";
                }
                else if (!string.IsNullOrWhiteSpace(customDns) && !IPAddress.TryParse(customDns, out _))
                {
                    result += "⚠️ Địa chỉ DNS Server không hợp lệ.";
                }
                else
                {
                    if (IPAddress.TryParse(domainOrIp, out _) && recordType != "PTR")
                        recordType = "PTR";

                    string lookupResult = await DnsLookupService.LookupWithSocket(domainOrIp, recordType, dnsServer, timeoutVal, retries);
                    result += lookupResult;
                }
            }
            catch (Exception ex)
            {
                result += $"⚠️ Lỗi hệ thống: {ex.Message}";
            }

            // Gán dữ liệu vào ViewBag để trả về View
            ViewBag.Result = result;
            ViewBag.DomainOrIp = domainOrIp;
            ViewBag.RecordType = recordType;
            ViewBag.CustomDns = customDns;
            ViewBag.Timeout = timeoutVal;
            ViewBag.Retries = retries;
        }

        // --- CÁC HÀM HELPER ---
        private static (string server, string address) GetLocalDnsInfo()
        {
            string serverName = "UnKnown";
            string address = "8.8.8.8";
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    var ipProps = ni.GetIPProperties();
                    foreach (var dns in ipProps.DnsAddresses)
                    {
                        if (dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            address = dns.ToString();
                            return (serverName, address);
                        }
                    }
                }
            }
            catch { }
            return (serverName, address);
        }

        private bool IsValidDomainOrIp(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            if (IPAddress.TryParse(input, out _)) return true;
            string domainPattern = @"^(?!\-)(?:[a-zA-Z0-9\-]{1,63}\.)+[a-zA-Z]{2,}$";
            return Regex.IsMatch(input, domainPattern);
        }
    }
}