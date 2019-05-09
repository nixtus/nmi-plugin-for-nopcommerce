using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Mvc.Models;

namespace Nixtus.Plugin.Payments.Nmi.Models
{
    public class PaymentInfoModel : BaseNopModel
    {
        [NopResourceDisplayName("Payment.CardNumber")]
        public string CardNumber { get; set; }

        [NopResourceDisplayName("Payment.ExpirationDate")]
        public string ExpireMonth { get; set; }

        [NopResourceDisplayName("Payment.ExpirationDate")]
        public string ExpireYear { get; set; }

        [NopResourceDisplayName("Payment.CardCode")]
        public string CardCode { get; set; }

        public string Token { get; set; }
    }
}