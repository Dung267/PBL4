using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PBL_4.Models
{
    public class NSLookupModel
    {
        // 🔹 Hàm lấy thông tin DNS server hiện tại
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

        // 🔹 1. Tra cứu tên miền -> IPv4 & IPv6
        public static string GetIPFromDomain(string domain)
        {
            var (server, address) = GetLocalDnsInfo();
            string result = $"Server:\t {server}\nAddress:\t {address}\n\n";

            try
            {
                IPHostEntry host = Dns.GetHostEntry(domain);
                List<string> ipv4 = new List<string>();
                List<string> ipv6 = new List<string>();

                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                        ipv4.Add(ip.ToString());
                    else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                        ipv6.Add(ip.ToString());
                }

                result += "Non-authoritative answer:\n";
                result += $"Name:\t {domain}\nAddresses:";

                foreach (var ip in ipv6)
                    result += $"\n\t {ip}";
                foreach (var ip in ipv4)
                    result += $"\n\t {ip}";

                if (ipv4.Count == 0 && ipv6.Count == 0)
                    result += "\n\t Không tìm thấy địa chỉ IP.";

                return result;
            }
            catch (Exception ex)
            {
                return result + "\nLỗi: " + ex.Message;
            }
        }

        // 🔹 2. Tra ngược IP -> Domain
        public static string GetDomainFromIP(string ip)
        {
            var (server, address) = GetLocalDnsInfo();
            string result = $"Server:\t {server}\nAddress:\t {address}\n\n";

            try
            {
                if (IPAddress.TryParse(ip, out IPAddress addressObj))
                {
                    IPHostEntry host = Dns.GetHostEntry(addressObj);
                    result += "Non-authoritative answer:\n";
                    result += $"Name:\t {host.HostName}\nAddress:\t {ip}";
                    return result;
                }
                return result + "Địa chỉ IP không hợp lệ.";
            }
            catch (SocketException)
            {
                return result + "Không tìm thấy PTR record cho IP này.";
            }
            catch (Exception ex)
            {
                return result + "Lỗi: " + ex.Message;
            }
        }

        // 🔹 3. Tra cứu bản ghi DNS (A, AAAA, MX, SOA)
        public static string GetDnsRecords(string domain, string recordType)
        {
            var (server, address) = GetLocalDnsInfo();
            string result = $"Server:\t {server}\nAddress:\t {address}\n\n";

            recordType = recordType.ToUpper();

            try
            {
                // A và AAAA dùng Dns.GetHostEntry (nhanh)
                if (recordType == "A" || recordType == "AAAA")
                {
                    IPHostEntry host = Dns.GetHostEntry(domain);
                    List<string> records = new List<string>();

                    if (recordType == "A")
                    {
                        foreach (var ip in host.AddressList)
                            if (ip.AddressFamily == AddressFamily.InterNetwork)
                                records.Add(ip.ToString());
                    }
                    else
                    {
                        foreach (var ip in host.AddressList)
                            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                                records.Add(ip.ToString());
                    }

                    result += "Non-authoritative answer:\n";
                    result += $"Name:\t {domain}\n{recordType} Records:";

                    if (records.Count == 0)
                        result += "\n\t Không tìm thấy bản ghi phù hợp.";
                    else
                        foreach (var r in records)
                            result += $"\n\t {r}";

                    return result;
                }

                // MX và SOA dùng lệnh nslookup thật
                if (recordType == "MX" || recordType == "SOA")
                {
                    string output = RunNslookupCommand(domain, recordType);
                    return result + output;
                }



                return result + "Loại bản ghi này chưa hỗ trợ trong phiên bản hiện tại.";
            }
            catch (Exception ex)
            {
                return result + "Lỗi: " + ex.Message;
            }
        }

        // 🔹 Chạy lệnh nslookup thực trong nền
        private static string RunNslookupCommand(string domain, string type)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "nslookup",
                    Arguments = $"-type={type} {domain}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    return output;
                }
            }
            catch (Exception ex)
            {
                return "\nKhông thể thực thi lệnh nslookup: " + ex.Message;
            }
        }
    }
}
