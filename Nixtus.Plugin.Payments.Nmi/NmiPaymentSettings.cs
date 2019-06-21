using Nop.Core.Configuration;

namespace Nixtus.Plugin.Payments.Nmi
{
    public class NmiPaymentSettings : ISettings
    {
        /// <summary>
        /// Username assigned to the merchant account
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Password assigned to the merchant account
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// If true use username/password otherwise security key
        /// </summary>
        public bool UseUsernamePassword { get; set; }

        /// <summary>
        /// If true then customer will be allowed to save their cards for future use
        /// if they are registered 
        /// </summary>
        public bool AllowCustomerToSaveCards { get; set; }

        /// <summary>
        /// API Security key assigned to the merchant account
        /// </summary>
        public string SecurityKey { get; set; }

        /// <summary>
        /// Tokenization key used for Collect JS
        /// </summary>
        public string CollectJsTokenizationKey { get; set; }

        /// <summary>
        /// Transact Mode
        /// </summary>
        public TransactMode TransactMode { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to "additional fee" is specified as percentage. true - percentage, false - fixed value.
        /// </summary>
        public bool AdditionalFeePercentage { get; set; }

        /// <summary>
        /// Additional fee
        /// </summary>
        public decimal AdditionalFee { get; set; }
    }
}
