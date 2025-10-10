using Microsoft.AspNetCore.Mvc;
using PBL_4.Models;

namespace PBL_4.Controllers
{
    public class NSLookupController : Controller
    {
        [HttpPost]
        public IActionResult Lookup(string domain, string ip, string recordType)
        {
            string result = "";

            if (!string.IsNullOrEmpty(domain) && string.IsNullOrEmpty(ip))
            {
                // Nếu người dùng nhập domain
                if (!string.IsNullOrEmpty(recordType))
                    result = NSLookupModel.GetDnsRecords(domain, recordType);
                else
                    result = NSLookupModel.GetIPFromDomain(domain);
            }
            else if (!string.IsNullOrEmpty(ip))
            {
                // Nếu người dùng nhập IP
                result = NSLookupModel.GetDomainFromIP(ip);
            }
            else
            {
                result = "Vui lòng nhập tên miền hoặc địa chỉ IP để tra cứu.";
            }

            ViewBag.Result = result;
            ViewBag.Domain = domain;
            ViewBag.IP = ip;
            ViewBag.RecordType = recordType;

            return View("Index");
        }

        public IActionResult Index()
        {
            return View();
        }
    }
}
