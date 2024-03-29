using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nixtus.Plugin.Payments.Nmi.Components;

namespace Nixtus.Plugin.Payments.Nmi
{
    /// <summary>
    /// NMI payment processor
    /// </summary>
    public class NmiPaymentProcessor : BasePlugin, IPaymentMethod
    {
        private const string NMI_DIRECT_POST_URL = "https://secure.nmi.com/api/transact.php";
        private HttpClient _httpClient = new HttpClient();

        #region Fields

        private readonly ISettingService _settingService;
        private readonly ICustomerService _customerService;
        private readonly IWebHelper _webHelper;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ILogger _logger;
        private readonly NmiPaymentSettings _nmiPaymentSettings;
        private readonly ILocalizationService _localizationService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IStateProvinceService _stateProvinceService;

        #endregion

        #region Ctor

        public NmiPaymentProcessor(ISettingService settingService,
            ICustomerService customerService,
            IWebHelper webHelper,
            IOrderTotalCalculationService orderTotalCalculationService,
            ILogger logger,
            NmiPaymentSettings nmiPaymentSettings,
            ILocalizationService localizationService, IGenericAttributeService genericAttributeService, IStateProvinceService stateProvinceService)
        {
            _nmiPaymentSettings = nmiPaymentSettings;
            _settingService = settingService;
            _customerService = customerService;
            _webHelper = webHelper;
            _orderTotalCalculationService = orderTotalCalculationService;
            _logger = logger;
            _localizationService = localizationService;
            _genericAttributeService = genericAttributeService;
            _stateProvinceService = stateProvinceService;
        }

        #endregion

        #region Utilities
        private NameValueCollection ExtractResponseValues(string response)
        {
            var responseValues = new NameValueCollection();
            var split = response.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var parts in split
                .Select(s => s.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length == 2))
            {
                responseValues.Add(parts[0], parts[1]);
            }

            return responseValues;
        }

        /// <summary>
        /// Adds either security key or username/password
        /// </summary>
        /// <param name="values"></param>
        private void AddSecurityValues(IDictionary<string, string> values)
        {
            if (_nmiPaymentSettings.UseUsernamePassword)
            {
                values.Add("username", _nmiPaymentSettings.Username);
                values.Add("password", _nmiPaymentSettings.Password);
            }
            else
            {
                values.Add("security_key", _nmiPaymentSettings.SecurityKey);
            }
        }

        /// <summary>
        /// Add customer vault values to payment request, if enabled
        /// </summary>
        /// <param name="processPaymentRequest"></param>
        /// <param name="customer"></param>
        /// <param name="values"></param>
        /// <returns>Task of boolean True - if saving new customer is enabled, else False</returns>
        private async Task<bool> AddCustomerVaultValues(ProcessPaymentRequest processPaymentRequest, Customer customer, IDictionary<string, string> values)
        {
            var saveCustomerKeySuccess = processPaymentRequest.CustomValues.TryGetValue(Constants.SaveCustomerKey, out object saveCustomerKey);
            var saveCustomer = Convert.ToBoolean(saveCustomerKeySuccess ? saveCustomerKey.ToString() : "false");
            if (_nmiPaymentSettings.AllowCustomerToSaveCards && saveCustomer)
            {
                var existingCustomerVaultId = await _genericAttributeService.GetAttributeAsync<string>(customer, Constants.CustomerVaultIdKey);
                if (string.IsNullOrEmpty(existingCustomerVaultId))
                {
                    values.Add("customer_vault", "add_customer");
                    values.Add("customer_vault_id", customer.CustomerGuid.ToString());
                    return true;
                }

                // existing customer
                // we already have the customer vault id saved, so we can just return false
                values.Add("customer_vault", "update_customer");
                values.Add("customer_vault_id", existingCustomerVaultId);
                return false;
            }

            return false;
        }

        /// <summary>
        /// Add stored card details if customer selected to use a stored card
        /// </summary>
        /// <param name="processPaymentRequest"></param>
        /// <param name="customer"></param>
        /// <param name="values"></param>
        private async Task AddStoredCardValues(ProcessPaymentRequest processPaymentRequest, Customer customer, IDictionary<string, string> values)
        {
            if (processPaymentRequest.CustomValues.TryGetValue(Constants.StoredCardKey, out object storedCardId) &&
                !storedCardId.ToString().Equals("0"))
            {
                var existingCustomerVaultId = await _genericAttributeService.GetAttributeAsync<string>(customer, Constants.CustomerVaultIdKey);
                if (!string.IsNullOrEmpty(existingCustomerVaultId))
                {
                    values.Add("customer_vault_id", existingCustomerVaultId);
                }
                else
                {
                    await _logger.WarningAsync("Customer tried use a stored card but did not have a customer vault ID saved");
                }

                values.Add("billing_id", storedCardId.ToString());
            }
            else
            {
                values.Add("payment_token", processPaymentRequest.CustomValues[Constants.CardToken].ToString());
            }
        }
        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the process payment result
        /// </returns>
        public async Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            var customer = await _customerService.GetCustomerByIdAsync(processPaymentRequest.CustomerId);
            var billingAddress = await _customerService.GetCustomerBillingAddressAsync(customer);

            if (customer == null || billingAddress == null)
            {
                throw new NopException("Could not retrieve customer or billing address");
            }

            var state = await _stateProvinceService.GetStateProvinceByIdAsync(billingAddress.StateProvinceId ?? 0);

            var values = new Dictionary<string, string>
            {
                { "payment", "creditcard" },
                { "type", _nmiPaymentSettings.TransactMode == TransactMode.AuthorizeAndCapture ? "sale" : "auth" },
                { "firstname", billingAddress.FirstName },
                { "lastname", billingAddress.LastName },
                { "address1", billingAddress.Address1 },
                { "city", billingAddress.City },
                { "state", state?.Abbreviation },
                { "zip", billingAddress.ZipPostalCode.Substring(0, 5) },
                { "amount", processPaymentRequest.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture) },
                { "orderid", processPaymentRequest.OrderGuid.ToString() }
            };

            // save customer card if needed
            var saveCustomer = await AddCustomerVaultValues(processPaymentRequest, customer, values);

            // determine if we need to used the stored card or the token generated from the new card
            await AddStoredCardValues(processPaymentRequest, customer, values);

            // add security key or username/password
            AddSecurityValues(values);

            try
            {
                var response = _httpClient.PostAsync(NMI_DIRECT_POST_URL, new FormUrlEncodedContent(values)).Result;

                var responseValues = ExtractResponseValues(response.Content.ReadAsStringAsync().Result);

                var responseValue = responseValues["response"];

                // transaction approved
                if (responseValue == "1")
                {
                    result.AuthorizationTransactionCode = $"{responseValues["transactionid"]},{responseValues["authcode"]}";

                    result.AuthorizationTransactionResult = $"Approved ({responseValues["responsetext"]})";
                    result.AvsResult = responseValues["avsresponse"];
                    result.Cvv2Result = responseValues["cvvresponse"];

                    result.NewPaymentStatus = _nmiPaymentSettings.TransactMode == TransactMode.AuthorizeAndCapture
                        ? PaymentStatus.Paid
                        : PaymentStatus.Authorized;

                    // save customer vault id, if needed
                    if (saveCustomer)
                    {
                        await _genericAttributeService.SaveAttributeAsync(customer, Constants.CustomerVaultIdKey, customer.CustomerGuid.ToString());
                    }

                    // remove custom values so that they don't get saved on the order and also won't display
                    // on the generated PDF
                    processPaymentRequest.CustomValues = new Dictionary<string, object>();
                }
                // transaction declined or error - responseValue = 2 or 3
                else
                {
                    result.AddError(responseValues["responsetext"]);
                }
            }
            catch (Exception exception)
            {
                await _logger.ErrorAsync("NMI Direct Post Error", exception, customer);
                result.AddError("Exception Occurred: " + exception.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //nothing
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the additional handling fee
        /// </returns>
        public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return await _orderTotalCalculationService.CalculatePaymentAdditionalFeeAsync(cart,
                _nmiPaymentSettings.AdditionalFee, _nmiPaymentSettings.AdditionalFeePercentage);
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the rue - hide; false - display.
        /// </returns>
        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return Task.FromResult(false);
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the capture payment result
        /// </returns>
        public async Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();

            var values = new Dictionary<string, string>
            {
                { "type", "capture" },
                { "amount", capturePaymentRequest.Order.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture) }
            };

            // add security key or username/password
            AddSecurityValues(values);

            var codes = capturePaymentRequest.Order.AuthorizationTransactionCode.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            values.Add("transactionid", codes[0]);

            try
            {
                var response = await _httpClient.PostAsync(NMI_DIRECT_POST_URL, new FormUrlEncodedContent(values));

                var content = await response.Content.ReadAsStringAsync();
                var responseValues = ExtractResponseValues(content);

                var responseValue = responseValues["response"];

                // transaction approved
                if (responseValue == "1")
                {
                    result.CaptureTransactionId = $"{responseValues["transactionid"]},{responseValues["authcode"]}";

                    result.NewPaymentStatus = PaymentStatus.Paid;
                }
                // transaction declined or error - responseValue = 2 or 3
                else
                {
                    result.AddError(responseValues["responsetext"]);
                }
            }
            catch (Exception exception)
            {
                await _logger.ErrorAsync("NMI Direct Post Error", exception);
                result.AddError("Exception Occurred: " + exception.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public async Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();

            var values = new Dictionary<string, string>
            {
                { "type", "capture" },
                { "amount", refundPaymentRequest.AmountToRefund.ToString("0.00", CultureInfo.InvariantCulture) }
            };

            // add security key or username/password
            AddSecurityValues(values);

            var codes = refundPaymentRequest.Order.CaptureTransactionId == null
                ? refundPaymentRequest.Order.AuthorizationTransactionCode.Split(',')
                : refundPaymentRequest.Order.CaptureTransactionId.Split(',');

            values.Add("transactionid", codes[0]);

            try
            {
                var response = await _httpClient.PostAsync(NMI_DIRECT_POST_URL, new FormUrlEncodedContent(values));

                var content = await response.Content.ReadAsStringAsync();
                var responseValues = ExtractResponseValues(content);

                var responseValue = responseValues["response"];

                // transaction approved
                if (responseValue == "1")
                {
                    var refundedTotalAmount = refundPaymentRequest.AmountToRefund + refundPaymentRequest.Order.RefundedAmount;

                    var isOrderFullyRefunded = refundedTotalAmount == refundPaymentRequest.Order.OrderTotal;

                    result.NewPaymentStatus = isOrderFullyRefunded ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
                }
                // transaction declined or error - responseValue = 2 or 3
                else
                {
                    result.AddError(responseValues["responsetext"]);
                }
            }
            catch (Exception exception)
            {
                await _logger.ErrorAsync("NMI Direct Post Error", exception);
                result.AddError("Exception Occurred: " + exception.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public async Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();

            var values = new Dictionary<string, string>
            {
                { "type", "void" }
            };

            // add security key or username/password
            AddSecurityValues(values);

            var codes = voidPaymentRequest.Order.CaptureTransactionId == null
                ? voidPaymentRequest.Order.AuthorizationTransactionCode.Split(',')
                : voidPaymentRequest.Order.CaptureTransactionId.Split(',');

            values.Add("transactionid", codes[0]);

            try
            {
                var response = await _httpClient.PostAsync(NMI_DIRECT_POST_URL, new FormUrlEncodedContent(values));

                var content = await response.Content.ReadAsStringAsync();
                var responseValues = ExtractResponseValues(content);

                var responseValue = responseValues["response"];

                // transaction approved
                if (responseValue == "1")
                {
                    result.NewPaymentStatus = PaymentStatus.Voided;
                }
                // transaction declined or error - responseValue = 2 or 3
                else
                {
                    result.AddError(responseValues["responsetext"]);
                }
            }
            catch (Exception exception)
            {
                await _logger.ErrorAsync("NMI Direct Post Error", exception);
                result.AddError("Exception Occurred: " + exception.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the process payment result
        /// </returns>
        public async Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();

            var customer = await _customerService.GetCustomerByIdAsync(processPaymentRequest.CustomerId);
            var billingAddress = await _customerService.GetCustomerBillingAddressAsync(customer);

            if (customer == null || billingAddress == null)
            {
                throw new NopException("Could not retrieve customer or billing address");
            }

            var state = await _stateProvinceService.GetStateProvinceByIdAsync(billingAddress.StateProvinceId ?? 0);

            var orderTotal = processPaymentRequest.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture);

            var values = new Dictionary<string, string>
            {
                { "payment", "creditcard" },
                { "type", _nmiPaymentSettings.TransactMode == TransactMode.AuthorizeAndCapture ? "sale" : "auth" },
                { "firstname", billingAddress.FirstName },
                { "lastname", billingAddress.LastName },
                { "address1", billingAddress.Address1 },
                { "city", billingAddress.City },
                { "state", state?.Abbreviation },
                { "zip", billingAddress.ZipPostalCode.Substring(0, 5) },
                { "amount",  orderTotal },
                { "orderid", processPaymentRequest.OrderGuid.ToString() }
            };

            // save customer card if needed
            var saveCustomer = await AddCustomerVaultValues(processPaymentRequest, customer, values);

            // determine if we need to used the stored card or the token generated from the new card
            await AddStoredCardValues(processPaymentRequest, customer, values);

            // add security key or username/password
            AddSecurityValues(values);

            // recurring payment information
            values.Add("recurring", "add_subscription");
            values.Add("plan_amount", orderTotal);

            // continue until canceled
            values.Add("plan_payments", "0");

            switch (processPaymentRequest.RecurringCyclePeriod)
            {
                case RecurringProductCyclePeriod.Days:
                    values.Add("day_frequency", processPaymentRequest.RecurringCycleLength.ToString());
                    break;
                case RecurringProductCyclePeriod.Weeks:
                    var days = processPaymentRequest.RecurringCycleLength * 7;

                    values.Add("day_frequency", days.ToString());
                    break;
                case RecurringProductCyclePeriod.Months:
                    values.Add("month_frequency", processPaymentRequest.RecurringCycleLength.ToString());
                    values.Add("day_of_month", DateTime.UtcNow.Day.ToString());
                    break;
                case RecurringProductCyclePeriod.Years:
                    // NMI gateway doesn't support recurring greater than 24 months
                    // but just let the gateway return the error and we will handle like all 
                    // other errors
                    var months = processPaymentRequest.RecurringCycleLength * 12;
                    values.Add("month_frequency", months.ToString());
                    values.Add("day_of_month", DateTime.UtcNow.Day.ToString());
                    break;
                default:
                    throw new NopException("NMI: Reoccurring Product Cycle not supported");
            }

            try
            {
                var response = await _httpClient.PostAsync(NMI_DIRECT_POST_URL, new FormUrlEncodedContent(values));

                var content = await response.Content.ReadAsStringAsync();
                var responseValues = ExtractResponseValues(content);

                var responseValue = responseValues["response"];

                // transaction approved
                if (responseValue == "1")
                {
                    result.SubscriptionTransactionId = $"{responseValues["transactionid"]},{responseValues["authcode"]}";

                    result.AvsResult = responseValues["avsresponse"];
                    result.Cvv2Result = responseValues["cvvresponse"];

                    result.NewPaymentStatus = _nmiPaymentSettings.TransactMode == TransactMode.AuthorizeAndCapture
                        ? PaymentStatus.Paid
                        : PaymentStatus.Authorized;

                    // save customer vault id, if needed
                    if (saveCustomer)
                    {
                        await _genericAttributeService.SaveAttributeAsync(customer, Constants.CustomerVaultIdKey, customer.CustomerGuid.ToString());
                    }

                    // remove custom values so that they don't get saved on the order and also won't display
                    // on the generated PDF
                    processPaymentRequest.CustomValues = new Dictionary<string, object>();
                }
                // transaction declined or error - responseValue = 2 or 3
                else
                {
                    result.AddError(responseValues["responsetext"]);
                }
            }
            catch (Exception exception)
            {
                await _logger.ErrorAsync("NMI Direct Post Error", exception, customer);
                result.AddError("Exception Occurred: " + exception.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public async Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();
            var values = new Dictionary<string, string>
            {
                { "recurring", "delete_subscription" }
            };

            // add security key or username/password
            AddSecurityValues(values);

            values.Add("subscription_id", cancelPaymentRequest.Order.SubscriptionTransactionId);

            try
            {
                var response = await _httpClient.PostAsync(NMI_DIRECT_POST_URL, new FormUrlEncodedContent(values));

                var content = await response.Content.ReadAsStringAsync();
                var responseValues = ExtractResponseValues(content);

                var responseValue = responseValues["response"];

                // transaction approved
                if (responseValue == "1")
                {
                    // nothing to do
                }
                // transaction declined or error - responseValue = 2 or 3
                else
                {
                    result.AddError(responseValues["responsetext"]);
                }
            }
            catch (Exception exception)
            {
                await _logger.ErrorAsync("NMI Direct Post Error", exception);
                result.AddError("Exception Occurred: " + exception.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //it's not a redirection payment method. So we always return false
            return Task.FromResult(false);
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the list of validating errors
        /// </returns>
        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            if (form == null)
                throw new ArgumentException(nameof(form));


            var warnings = new List<string>();

            //try to get errors
            if (form.TryGetValue("Errors", out StringValues errorsString) && !StringValues.IsNullOrEmpty(errorsString))
            {
                warnings.Add(errorsString.ToString());
            }

            return Task.FromResult<IList<string>>(warnings);
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the payment info holder
        /// </returns>
        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            var paymentRequest = new ProcessPaymentRequest();

            //pass custom values to payment method
            if (form.TryGetValue("Token", out StringValues token) && !StringValues.IsNullOrEmpty(token))
                paymentRequest.CustomValues.Add(Constants.CardToken, token.ToString());

            if (form.TryGetValue("StoredCardId", out StringValues storedCardId) && !StringValues.IsNullOrEmpty(storedCardId) && !storedCardId.Equals("0"))
                paymentRequest.CustomValues.Add(Constants.StoredCardKey, storedCardId.ToString());

            if (form.TryGetValue("SaveCustomer", out StringValues saveCustomerValue) && !StringValues.IsNullOrEmpty(saveCustomerValue) && bool.TryParse(saveCustomerValue[0], out bool saveCustomer) && saveCustomer)
                paymentRequest.CustomValues.Add(Constants.SaveCustomerKey, saveCustomer);

            return Task.FromResult(paymentRequest);
        }

        /// <summary>
        /// Gets a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <param name="viewComponentName">View component name</param>
        public void GetPublicViewComponent(out string viewComponentName)
        {
            viewComponentName = "Nmi";
        }

        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentNmi/Configure";
        }

        public string GetPublicViewComponentName()
        {
            return "Nmi";
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task InstallAsync()
        {
            //settings
            var settings = new NmiPaymentSettings
            {
                Password = "123",
                Username = "456",
                UseUsernamePassword = false,
                TransactMode = TransactMode.AuthorizeAndCapture
            };
            await _settingService.SaveSettingAsync(settings);

            //locales
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Payments.Nmi.Fields.Username"] = "Username",
                ["Plugins.Payments.Nmi.Fields.Username.Hint"] = "Username assigned to the merchant account",
                ["Plugins.Payments.Nmi.Fields.Password"] = "Password",
                ["Plugins.Payments.Nmi.Fields.Password.Hint"] = "Password assigned to the merchant account",
                ["Plugins.Payments.Nmi.Fields.SecurityKey"] = "Security Key",
                ["Plugins.Payments.Nmi.Fields.SecurityKey.Hint"] = "API security key assigned to the merchant account, using this combined with username/password will result in an error.  If you want to save cards, then select that checkbox and enter username/password",
                ["Plugins.Payments.Nmi.Fields.CollectJsTokenizationKey"] = "Collect JS Tokenization Key",
                ["Plugins.Payments.Nmi.Fields.CollectJsTokenizationKey.Hint"] = "Tokenization key used for Collect.js library",
                ["Plugins.Payments.Nmi.Fields.TransactModeValues"] = "Transaction mode",
                ["Plugins.Payments.Nmi.Fields.TransactModeValues.Hint"] = "Choose transaction mode.",
                ["Plugins.Payments.Nmi.Fields.AdditionalFee"] = "Additional fee",
                ["Plugins.Payments.Nmi.Fields.AdditionalFee.Hint"] = "Enter additional fee to charge your customers.",
                ["Plugins.Payments.Nmi.Fields.AdditionalFeePercentage"] = "Additional fee. Use percentage",
                ["Plugins.Payments.Nmi.Fields.AdditionalFeePercentage.Hint"] = "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.",
                ["Plugins.Payments.Nmi.Fields.UseUsernamePassword"] = "Use username/password",
                ["Plugins.Payments.Nmi.Fields.UseUsernamePassword.Hint"] = "If enabled username/password will be used for authentication to the payment API instead of the security key",
                ["Plugins.Payments.Nmi.Fields.AllowCustomerToSaveCards"] = "Allow Customers To Store Cards",
                ["Plugins.Payments.Nmi.Fields.AllowCustomerToSaveCards.Hint"] = "If enabled registered customers will be able to save cards for future use.  Also, you must enter the username/password for this functionality to be available.",
                ["Plugins.Payments.Nmi.PaymentMethodDescription"] = "Pay by credit / debit card",
                ["Plugins.Payments.Nmi.SaveCustomer"] = "Save card information",
                ["Plugins.Payments.Nmi.Fields.StoredCard"] = "Use a previously saved card"
            });

            await base.InstallAsync();
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task UninstallAsync()
        {
            //settings
            await _settingService.DeleteSettingAsync<NmiPaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Nmi");

            await base.UninstallAsync();
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        /// <remarks>
        /// return description of this payment method to be display on "payment method" checkout step. good practice is to make it localizable
        /// for example, for a redirection payment method, description may be like this: "You will be redirected to PayPal site to complete the payment"
        /// </remarks>
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("Plugins.Payments.Nmi.PaymentMethodDescription");
        }

        public Type GetPublicViewComponent()
        {
            return typeof(NmiViewComponent);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture => true;

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund => true;

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund => true;

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid => true;

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.Automatic;

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType => PaymentMethodType.Standard;

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo => false;

        #endregion
    }
}
