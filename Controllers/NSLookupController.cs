using Microsoft.AspNetCore.Mvc;
using System;
using System.Net;
using System.Net.Sockets;

{
    public class NSLookupController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        {

            {
                else
            }
            {
            }
            else
            {
            }

            ViewBag.Result = result;
            ViewBag.RecordType = recordType;

            return View("Index");
        }

        public IActionResult Index()
        {
            return View();
        }
    }
}
