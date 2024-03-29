﻿using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nixtus.Plugin.Payments.Nmi.Models
{
    public class PaymentInfoModel
    {
        public PaymentInfoModel()
        {
            StoredCards = new List<SelectListItem>();
        }

        // These properties are only used to display label on the payment info screen
        [NopResourceDisplayName("Payment.CardNumber")]
        public string CardNumber { get; set; }

        [NopResourceDisplayName("Payment.ExpirationDate")]
        public string ExpireMonth { get; set; }

        [NopResourceDisplayName("Payment.CardCode")]
        public string CardCode { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Nmi.SaveCustomer")]
        public string SaveCustomer { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Nmi.Fields.StoredCard")]
        public string StoredCardId { get; set; }
        public IList<SelectListItem> StoredCards { get; set; }


        public string Token { get; set; }

        public bool IsGuest { get; set; }

        public bool AllowCustomerToSaveCards { get; set; }
    }
}