using System.Net;
using Microsoft.AspNetCore.Mvc;
using Nixtus.Plugin.Payments.Nmi.Models;
using Nop.Web.Framework.Components;

namespace Nixtus.Plugin.Payments.Nmi.Components
{
    [ViewComponent(Name = "Nmi")]
    public class NmiViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            var model = new PaymentInfoModel();

            //set postback values (we cannot access "Form" with "GET" requests)
            if (Request.Method == WebRequestMethods.Http.Get)
                return View("~/Plugins/Payments.Nmi/Views/PaymentInfo.cshtml", model);

            var form = Request.Form;
            model.CardholderName = form["CardholderName"];
            model.CardNumber = form["CardNumber"];
            model.CardCode = form["CardCode"];

            return View("~/Plugins/Payments.Nmi/Views/PaymentInfo.cshtml", model);
        }
    }
}
