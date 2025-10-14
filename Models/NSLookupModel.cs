using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace PBL_4.Models
{
    public class NSLookupModel
    {
        private static (string server, string address) GetLocalDnsInfo()
        {
            string serverName = Dns.GetHostName();
            string address = "Không xác định";

            try
            {
                var addrs = Dns.GetHostAddresses(serverName);
                if (addrs.Length > 0)
                    address = addrs[0].ToString();
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

                    result += "Non-authoritative answer:\n";
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
    }
}
