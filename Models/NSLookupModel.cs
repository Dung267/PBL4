using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PBL_4.Models
{
    public class NSLookupModel
    {
        private static (string server, string address) GetLocalDnsInfo()
        {
            string serverName = "Không xác định";
            string address = "Không xác định";

            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var adapter in adapters)
                {
                    var props = adapter.GetIPProperties();
                    foreach (var dns in props.DnsAddresses)
                    {
                        address = dns.ToString();
                        serverName = "Local DNS Resolver";
                        return (serverName, address);
                    }
                }
            }
            catch { }

            return (serverName, address);
        }

        public static string Lookup(string input, string recordType)
        {
            var (server, address) = GetLocalDnsInfo();
            string result = $"Server:\t {server}\nAddress:\t {address}\n\n";
            Stopwatch sw = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(input))
                return result + "Vui lòng nhập tên miền hoặc địa chỉ IP.";

            recordType = recordType?.Trim().ToUpper();

            try
            {
                bool isAuthoritative = false;
                // Nếu nhập là IP → tra ngược (PTR)
                if (IPAddress.TryParse(input, out IPAddress ip))
                {
                    if (string.IsNullOrEmpty(recordType) || recordType == "PTR")
                    {
                        try
                        {
                            IPHostEntry host = Dns.GetHostEntry(ip);
                            result += "Non-authoritative answer:\n";
                            result += $"Name:\t {host.HostName}\nAddress:\t {ip}";
                        }
                        catch (SocketException)
                        {
                            result += $"Không tìm thấy PTR record cho {ip}.";
                        }
                    }
                    else
                    {
                        result += $"Loại bản ghi {recordType} không áp dụng cho IP.";
                    }
                }
                else
                {
                    // Nếu nhập là tên miền
                    IPHostEntry host = Dns.GetHostEntry(input);

                    List<string> ipv4 = new List<string>();
                    List<string> ipv6 = new List<string>();
                    List<string> cname = new List<string>();

                    foreach (var alias in host.Aliases)
                        cname.Add(alias);

                    foreach (var ipAddr in host.AddressList)
                    {
                        if (ipAddr.AddressFamily == AddressFamily.InterNetwork)
                            ipv4.Add(ipAddr.ToString());
                        else if (ipAddr.AddressFamily == AddressFamily.InterNetworkV6)
                            ipv6.Add(ipAddr.ToString());
                    }

                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "nslookup",
                            Arguments = input,
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        var p = Process.Start(psi);
                        string nsOutput = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();

                        if (nsOutput.Contains("Non-authoritative"))
                            isAuthoritative = false;
                        else if (nsOutput.Contains("Authoritative"))
                            isAuthoritative = true;
                    }
                    catch
                    {
                        // Nếu không chạy được nslookup → coi như non-authoritative
                        isAuthoritative = false;
                    }

                    result += (isAuthoritative ? "Authoritative answer:\n" : "Non-authoritative answer:\n");
                    result += $"Name:\t {input}\n";

                    // A record
                    if (string.IsNullOrEmpty(recordType) || recordType == "A")
                    {
                        if (ipv4.Count > 0)
                        {
                            result += "A Records:";
                            foreach (var ip4 in ipv4)
                                result += $"\n\t {ip4}";
                            result += "\n";
                        }
                    }

                    // AAAA record
                    if (string.IsNullOrEmpty(recordType) || recordType == "AAAA")
                    {
                        if (ipv6.Count > 0)
                        {
                            result += "AAAA Records:";
                            foreach (var ip6 in ipv6)
                                result += $"\n\t {ip6}";
                            result += "\n";
                        }
                    }

                    // CNAME record
                    if (string.IsNullOrEmpty(recordType) || recordType == "CNAME")
                    {
                        if (cname.Count > 0)
                        {
                            result += "CNAME Records:";
                            foreach (var a in cname)
                                result += $"\n\t {a}";
                        }
                    }

                    if (ipv4.Count == 0 && ipv6.Count == 0 && cname.Count == 0)
                        result += "Không tìm thấy bản ghi phù hợp.";
                }
            }
            catch (Exception ex)
            {
                result += "Lỗi: " + ex.Message;
            }

            sw.Stop();
            result += $"\n\nThời gian truy vấn: {sw.ElapsedMilliseconds} ms";
            return result;
        }

        public static string LookupWithCustomDns(string input, string recordType, string customDns, int timeout = 3000, int retry = 1)
        {
            string server = string.IsNullOrWhiteSpace(customDns) ? "Mặc định (Hệ thống)" : customDns;
            string result = $"Server:\t {server}\nAddress:\t {customDns}\n\n";
            Stopwatch sw = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(input))
                return result + "Vui lòng nhập tên miền hoặc địa chỉ IP.";

            try
            {
                // Sử dụng nslookup với tham số người dùng chọn
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "nslookup",
                    Arguments = $"-timeout={(timeout / 1000)} -retry={retry} {input} {customDns}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var p = Process.Start(psi);
                string nsOutput = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                result += nsOutput;
            }
            catch (Exception ex)
            {
                result += "Lỗi: " + ex.Message;
            }

            sw.Stop();
            result += $"\n\nThời gian truy vấn: {sw.ElapsedMilliseconds} ms";
            return result;
        }

    }
}
