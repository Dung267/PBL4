using Microsoft.AspNetCore.Mvc;
using System;
using System.Net;
using System.Net.Sockets;

namespace PBL4.Controllers
{
    public class NSLookupController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Index(string domainOrIp, string recordType)
        {
            string result = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(domainOrIp))
                {
                    result = "⚠️ Vui lòng nhập tên miền hoặc địa chỉ IP.";
                }
                else
                {
                    // Kiểm tra xem là IP hay tên miền
                    if (IPAddress.TryParse(domainOrIp, out IPAddress ip))
                    {
                        // Reverse lookup
                        try
                        {
                            IPHostEntry hostEntry = Dns.GetHostEntry(ip);
                            result += $"Name: {hostEntry.HostName}\n";
                            foreach (var address in hostEntry.AddressList)
                                result += $"Address: {address}\n";
                        }
                        catch
                        {
                            result = "❌ Không thể tra ngược địa chỉ IP.";
                        }
                    }
                    else
                    {
                        // Lookup theo loại bản ghi
                        IPAddress[] addresses = Dns.GetHostAddresses(domainOrIp);

                        if (addresses.Length == 0)
                        {
                            result = "❌ Không tìm thấy kết quả.";
                        }
                        else
                        {
                            result = $"Name: {domainOrIp}\n";
                            foreach (var address in addresses)
                            {
                                if (recordType == "A" && address.AddressFamily == AddressFamily.InterNetwork)
                                    result += $"IPv4: {address}\n";
                                else if (recordType == "AAAA" && address.AddressFamily == AddressFamily.InterNetworkV6)
                                    result += $"IPv6: {address}\n";
                                else if (string.IsNullOrEmpty(recordType)) // mặc định hiển thị cả 2
                                    result += $"{(address.AddressFamily == AddressFamily.InterNetwork ? "IPv4" : "IPv6")}: {address}\n";
                            }
                        }

                        // Nếu chọn CNAME
                        if (recordType == "CNAME")
                        {
                            try
                            {
                                IPHostEntry hostEntry = Dns.GetHostEntry(domainOrIp);
                                result += $"\nCanonical Name: {hostEntry.HostName}";
                            }
                            catch
                            {
                                result += "\n❌ Không tìm thấy CNAME.";
                            }
                        }

                        // Nếu chọn PTR
                        if (recordType == "PTR")
                        {
                            try
                            {
                                var entry = Dns.GetHostEntry(domainOrIp);
                                result = $"Reverse (PTR): {entry.HostName}";
                            }
                            catch
                            {
                                result = "❌ Không thể tra PTR.";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result = $"⚠️ Lỗi: {ex.Message}";
            }

            ViewBag.Result = result;
            ViewBag.DomainOrIp = "";
            ViewBag.RecordType = recordType;

            return View();
        }
    }
}
