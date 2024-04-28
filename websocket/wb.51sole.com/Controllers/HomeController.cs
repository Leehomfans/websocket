using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using _51Sole.DJG.Common;
using Newtonsoft.Json;

namespace wb._51sole.com.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            ViewBag.Title = "Home Page";

            return View();
        }
        [HttpPost]
        [Route("api/v1/ax/finish")]
        public JsonResult finish()
        {
            string jsondata = HttpHelper.GetPostJSON();
            reqModel model=JsonConvert.DeserializeObject<reqModel>(jsondata);
            return Json(model, JsonRequestBehavior.AllowGet);
        }
        public class reqModel
        {
            public string request_id { get; set; }
            public string tel_b { get; set; }
        }
    }
}
