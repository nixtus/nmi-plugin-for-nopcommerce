using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Mvc.Models;

namespace Nixtus.Plugin.Payments.Nmi.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Nmi.Fields.Username")]
        public string Username { get; set; }
        public bool Username_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Nmi.Fields.Password")]
        public string Password { get; set; }
        public bool Password_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Nmi.Fields.SecurityKey")]
        public string SecurityKey { get; set; }
        public bool SecurityKey_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Nmi.Fields.CollectJsTokenizationKey")]
        public string CollectJsTokenizationKey { get; set; }
        public bool CollectJsTokenizationKey_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Nmi.Fields.TransactModeValues")]
        public int TransactModeId { get; set; }
        public bool TransactModeId_OverrideForStore { get; set; }
        public SelectList TransactModeValues { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Nmi.Fields.AdditionalFee")]
        public decimal AdditionalFee { get; set; }
        public bool AdditionalFee_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Nmi.Fields.AdditionalFeePercentage")]
        public bool AdditionalFeePercentage { get; set; }
        public bool AdditionalFeePercentage_OverrideForStore { get; set; }
    }
}