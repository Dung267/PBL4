
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using PBL4.Models; 

namespace PBL4.Services
{
    public static class DnsLookupService
    {
        // --- HẰNG SỐ DNS ---
        private const ushort QTypeA = 1;
        private const ushort QTypePTR = 12;
        private const ushort QClassIN = 1;
        private const ushort QTypeAAAA = 28; // IPv6 Address
        private const ushort QTypeCNAME = 5; // Canonical Name
        private const ushort QTypeMX = 15;   // Mail Exchange
        private const ushort QTypeTXT = 16;  // Text Record

        // --- HÀM HỖ TRỢ CHUNG ---
        private static ushort GetQueryType(string recordType)
        {
            return recordType?.ToUpper() switch
            {
                "A" => QTypeA,
                "AAAA" => QTypeAAAA,
                "CNAME" => QTypeCNAME,
                "MX" => QTypeMX,
                "TXT" => QTypeTXT,
                "PTR" => QTypePTR,
                _ => QTypeA,
            };
        }

        private static byte[] EncodeDomainName(string domain)
        {
            var parts = domain.Split('.');
            var encoded = new List<byte>();

            foreach (var part in parts)
            {
                if (part.Length > 63) throw new ArgumentException("Domain label quá dài.");
                encoded.Add((byte)part.Length);
                encoded.AddRange(Encoding.ASCII.GetBytes(part));
            }
            encoded.Add(0);
            return encoded.ToArray();
        }

        private static byte[] BuildDnsQuery(string domain, ushort qType)
        {
            ushort transactionId = (ushort)new Random().Next(0, 65535);

            var header = new DnsHeader
            {
                // Ép kiểu an toàn từ ushort sang short và ngược lại
                ID = (ushort)IPAddress.HostToNetworkOrder((short)transactionId),
                Flags = (ushort)IPAddress.HostToNetworkOrder((short)0x0100),
                QdCount = (ushort)IPAddress.HostToNetworkOrder((short)1),
                AnCount = (ushort)IPAddress.HostToNetworkOrder((short)0),
                NsCount = (ushort)IPAddress.HostToNetworkOrder((short)0),
                ArCount = (ushort)IPAddress.HostToNetworkOrder((short)0)
            };

            byte[] headerBytes = GetBytes(header);

            byte[] qName = EncodeDomainName(domain);
            var question = new DnsQuestion
            {
                QType = (ushort)IPAddress.HostToNetworkOrder((short)qType),
                QClass = (ushort)IPAddress.HostToNetworkOrder((short)QClassIN)
            };
            byte[] questionBytes = GetBytes(question);

            return headerBytes.Concat(qName).Concat(questionBytes).ToArray();
        }

        private static async Task<byte[]> SendAndReceive(byte[] queryData, string dnsServer, int timeoutMs, int retries)
        {
            using (var udpClient = new UdpClient())
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse(dnsServer), 53);

                for (int attempt = 0; attempt < retries; attempt++)
                {
                    try
                    {
                        await udpClient.SendAsync(queryData, queryData.Length, remoteEP);

                        var receiveTask = udpClient.ReceiveAsync();
                        if (await Task.WhenAny(receiveTask, Task.Delay(timeoutMs)) == receiveTask)
                        {
                            return receiveTask.Result.Buffer;
                        }
                    }
                    catch (SocketException) { }
                    catch (TimeoutException) { }
                }
                return null;
            }
        }

        private static byte[] GetBytes<T>(T structure) where T : struct
        {
            int size = Marshal.SizeOf(structure);
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(structure, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            return arr;
        }

        private static T FromBytes<T>(byte[] arr) where T : struct
        {
            T structure = new T();
            int size = Marshal.SizeOf(structure);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.Copy(arr, 0, ptr, size);
            structure = (T)Marshal.PtrToStructure(ptr, structure.GetType());
            Marshal.FreeHGlobal(ptr);
            return structure;
        }

        // --- HÀM GIẢI MÃ TÊN MIỀN VÀ TÍNH TOÁN OFFSET ---
        private static string DecodeDomainName(byte[] response, int offset)
        {
            var sb = new StringBuilder();
            int currentOffset = offset;
            // Dùng HashSet để tránh vòng lặp vô hạn (rất tốt)
            var pointerOffsets = new HashSet<int>();

            while (true)
            {
                if (currentOffset >= response.Length) break;

                // Byte độ dài/Trường hợp Pointer
                byte length = response[currentOffset];

                if ((length & 0xC0) == 0xC0) // Pointer Compression
                {
                    // Đọc 2 bytes để xác định offset (pointer = 0x3FFF)
                    ushort pointerOffset = (ushort)(((length & 0x3F) << 8) | response[currentOffset + 1]);

                    // Phải dừng việc tiêu thụ byte trong Vòng lặp hiện tại (không tăng currentOffset)
                    // vì logic GetNameLength đã lo phần đó.

                    // Kiểm tra vòng lặp vô hạn (rất quan trọng)
                    if (!pointerOffsets.Add(pointerOffset))
                    {
                        // Nếu pointer bị lặp, break để tránh Stack Overflow
                        break;
                    }

                    // GỌI ĐỆ QUY để giải mã phần còn lại của tên miền từ vị trí con trỏ trỏ tới
                    string compressedPart = DecodeDomainName(response, pointerOffset);

                    // Nối phần tên miền đã được giải mã
                    sb.Append(compressedPart).Append('.');

                    // Dừng vòng lặp hiện tại, vì phần tên miền đã kết thúc bằng con trỏ
                    break;
                }
                else if (length == 0) // End of Name (kết thúc tên miền)
                {
                    break;
                }
                else // Label Encoding (đọc Label)
                {
                    currentOffset++; // Tăng offset qua byte độ dài
                    string label = Encoding.ASCII.GetString(response, currentOffset, length);
                    sb.Append(label).Append('.');
                    currentOffset += length; // Tăng offset qua dữ liệu Label
                }
            }

            return sb.ToString().Trim('.');
        }
        // Tính độ dài tên miền đã đọc trong gói tin (bao gồm Pointers và Labels)
        private static int GetNameLength(byte[] response, ref int offset)
        {
            int bytesConsumed = 0; // Số byte đã tiêu thụ (trường Tên)

            while (offset < response.Length)
            {
                byte currentByte = response[offset];

                if ((currentByte & 0xC0) == 0xC0) // Pointer Compression (2 bytes)
                {
                    offset += 2;
                    bytesConsumed += 2;
                    break; // Kết thúc trường Tên (đã nhảy qua 2 bytes)
                }
                else if (currentByte == 0) // End of Name (1 byte)
                {
                    offset += 1;
                    bytesConsumed += 1;
                    break; // Kết thúc trường Tên
                }
                else // Label Encoding
                {
                    int labelLength = currentByte;
                    // Di chuyển offset qua byte độ dài (1 byte) và dữ liệu label (labelLength bytes)
                    offset += labelLength + 1;
                    bytesConsumed += labelLength + 1;
                }
            }
            return bytesConsumed;
        }

        // KHÔNG CẦN thay đổi logic của ParseDnsResponse, vì nó đã sử dụng GetNameLength(response, ref offset)
        // và dùng giá trị trả về để tính toán, nhưng GetNameLength cần phải cập nhật offset bằng ref!

        // --- LOGIC TRA CỨU CHÍNH (FORWARD & REVERSE) ---

        public static async Task<string> LookupWithSocket(string domainOrIp, string recordType, string dnsServer, int timeout, int retries)
        {
            if (IPAddress.TryParse(domainOrIp, out IPAddress ipAddress))
            {
                return await ExecuteReverseLookup(ipAddress, dnsServer, timeout, retries);
            }
            return await ExecuteForwardLookup(domainOrIp, recordType, dnsServer, timeout, retries);
        }

        public static async Task<string> ExecuteForwardLookup(string domain, string recordType, string dnsServer, int timeout, int retries)
        {
            ushort qType = GetQueryType(recordType);
            byte[] query = BuildDnsQuery(domain, qType);
            byte[] response = await SendAndReceive(query, dnsServer, timeout, retries);

            if (response == null) return "❌ Lỗi: Không nhận được phản hồi từ DNS Server.";

            return ParseDnsResponse(response, domain, isPtr: false, expectedType: qType);
        }

        private static async Task<string> ExecuteReverseLookup(IPAddress ipAddress, string dnsServer, int timeout, int retries)
        {
            string ptrDomain = string.Join(".", ipAddress.GetAddressBytes().Reverse().Select(b => b.ToString())) + ".in-addr.arpa";
            byte[] query = BuildDnsQuery(ptrDomain, QTypePTR);
            byte[] response = await SendAndReceive(query, dnsServer, timeout, retries);

            if (response == null) return "❌ Lỗi: Không nhận được phản hồi từ DNS Server.";

            return ParseDnsResponse(response, ipAddress.ToString(), isPtr: true, expectedType: QTypePTR);
        }

        // --- HÀM PHÂN TÍCH RESPONSE ĐÃ TỐI ƯU ---

        private static string ParseDnsResponse(byte[] response, string queryName, bool isPtr = false, ushort expectedType = QTypeA)
        {
            var header = FromBytes<DnsHeader>(response.Take(12).ToArray());
            int answerCount = IPAddress.NetworkToHostOrder((short)header.AnCount);

            ushort flags = (ushort)IPAddress.NetworkToHostOrder((short)header.Flags);
            int rcode = flags & 0x000F;

            // Xử lý Lỗi Header (RCODE)
            if (rcode != 0)
            {
                string error = rcode switch
                {
                    1 => "Format Error (FORMERR)",
                    3 => "Non-Existent Domain (NXDOMAIN)",
                    2 => "Server Failure (SERVFAIL)",
                    _ => $"RCODE {rcode}"
                };
                return $"❌ Lỗi DNS Server: {error}";
            }

            // Nếu không có lỗi RCODE nhưng không có Answer Record
            if (answerCount == 0) return "❌ Không tìm thấy kết quả hợp lệ trong phần Answer.";

            int offset = 12;
            var result = new StringBuilder();

            // Bỏ qua phần Question
            GetNameLength(response, ref offset);
            offset += 4; // QType (2 bytes) + QClass (2 bytes)

            // Thêm thông báo Non-authoritative answer CHỈ MỘT LẦN
            result.AppendLine("Non-authoritative answer:");

            // Phân tích Answer Records
            for (int i = 0; i < answerCount; i++)
            {
                // Nhảy qua trường Tên trong Answer và cập nhật offset
                GetNameLength(response, ref offset);

                if (offset + 10 > response.Length)
                {
                    // Nếu không đủ chỗ để đọc Type, Class, TTL và Data Length
                    result.AppendLine("⚠️ Lỗi phân tích: Gói tin bị cắt cụt hoặc offset không hợp lệ.");
                    break;
                }

                // Đọc các trường cố định
                ushort answerType = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(response, offset)); offset += 2;
                offset += 2; // Class
                offset += 4; // TTL
                ushort dataLength = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(response, offset)); offset += 2;

                if (offset + dataLength > response.Length)
                {
                    // Nếu Data Length chỉ ra rằng dữ liệu nằm ngoài giới hạn gói tin
                    result.AppendLine($"⚠️ Lỗi phân tích: Resource Data Length ({dataLength}) vượt quá giới hạn gói tin.");
                    break;
                }

                // Lọc Record
                bool shouldProcess = isPtr && answerType == QTypePTR ||
                                     answerType == expectedType ||
                                     (expectedType == QTypeA && (answerType == QTypeA || answerType == QTypeAAAA));

                if (!shouldProcess)
                {
                    offset += dataLength;
                    continue;
                }

                // Phân tích Resource Data dựa trên Type
                switch (answerType)
                {
                    case QTypeA:
                        if (dataLength == 4)
                        {
                            IPAddress ipv4 = new IPAddress(response.Skip(offset).Take(4).ToArray());
                            result.AppendLine($"Address: {ipv4}");
                        }
                        break;

                    case QTypeAAAA:
                        if (dataLength == 16)
                        {
                            IPAddress ipv6 = new IPAddress(response.Skip(offset).Take(16).ToArray());
                            result.AppendLine($"IPv6 Address: {ipv6}");
                        }
                        break;

                    case QTypePTR:
                        string ptrName = DecodeDomainName(response, offset);
                        result.AppendLine($"Name: {ptrName}");
                        break;

                    case QTypeCNAME:
                        string cname = DecodeDomainName(response, offset);
                        result.AppendLine($"Canonical Name: {cname}");
                        break;

                    case QTypeMX:
                        ushort preference = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(response, offset));
                        string exchangeName = DecodeDomainName(response, offset + 2);
                        result.AppendLine($"Mail Exchanger: {exchangeName} (Preference: {preference})");
                        break;

                    case QTypeTXT:
                        int txtOffset = offset;
                        while (txtOffset < offset + dataLength)
                        {
                            int len = response[txtOffset++];
                            string text = Encoding.ASCII.GetString(response, txtOffset, len);
                            result.AppendLine($"TXT Data: \"{text}\"");
                            txtOffset += len;
                        }
                        break;
                }

                // Nhảy qua Resource Data để đến Record tiếp theo
                offset += dataLength;
            }

            if (result.Length <= "Non-authoritative answer:\r\n".Length)
            {
                return $"❌ Không tìm thấy Record loại {expectedType} hợp lệ.";
            }

            return result.ToString();
        }
    }
}




