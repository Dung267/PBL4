using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

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

        {
            var (server, address) = GetLocalDnsInfo();
            string result = $"Server:\t {server}\nAddress:\t {address}\n\n";
            Stopwatch sw = Stopwatch.StartNew();



            {
        {
            try
            {
                    result += "Non-authoritative answer:\n";
                    result += $"Name:\t {host.HostName}\nAddress:\t {ip}";
                    return result;
                }
                return result + "Địa chỉ IP không hợp lệ.";
            }
            catch (SocketException)
            {
            }
            {
            }
        }
        {



                    {
                    }

                    result += "Non-authoritative answer:\n";

                {
                }

            {
            }
        }

            {
                {

                }
            }
            catch (Exception ex)
            {
            }

            sw.Stop();
            result += $"\n\nThời gian truy vấn: {sw.ElapsedMilliseconds} ms";
            return result;
        }
    }
}
