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

            return View("~/Plugins/Payments.Nmi/Views/PaymentInfo.cshtml", model);
        }
    }
}
