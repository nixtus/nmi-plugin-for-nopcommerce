using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
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

namespace Nixtus.Plugin.Payments.Nmi
{
    /// <summary>
    /// NMI payment processor
    /// </summary>
    public class NmiPaymentProcessor : BasePlugin, IPaymentMethod
    {
        private const string NMI_DIRECT_POST_URL = "https://msgpay.transactiongateway.com/api/transact.php";
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
        private readonly IPaymentService _paymentService;

        #endregion

        #region Ctor

        public NmiPaymentProcessor(ISettingService settingService,
            ICustomerService customerService,
            IWebHelper webHelper,
            IOrderTotalCalculationService orderTotalCalculationService,
            ILogger logger,
            NmiPaymentSettings nmiPaymentSettings,
            ILocalizationService localizationService, IGenericAttributeService genericAttributeService, IStateProvinceService stateProvinceService, IPaymentService paymentService)
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
            _paymentService = paymentService;
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
        /// <returns>True - if saving new customer is enabled, else False</returns>
        private bool AddCustomerVaultValues(ProcessPaymentRequest processPaymentRequest, Customer customer, IDictionary<string, string> values)
        {
            var saveCustomerKeySuccess = processPaymentRequest.CustomValues.TryGetValue(Constants.SaveCustomerKey, out object saveCustomerKey);
            var saveCustomer = Convert.ToBoolean(saveCustomerKeySuccess ? saveCustomerKey.ToString() : "false");
            if (_nmiPaymentSettings.AllowCustomerToSaveCards && saveCustomer)
            {
                var existingCustomerVaultId = _genericAttributeService.GetAttribute<string>(customer, Constants.CustomerVaultIdKey);
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
        private void AddStoredCardValues(ProcessPaymentRequest processPaymentRequest, Customer customer, IDictionary<string, string> values)
        {
            if (processPaymentRequest.CustomValues.TryGetValue(Constants.StoredCardKey, out object storedCardId) &&
                !storedCardId.ToString().Equals("0"))
            {
                var existingCustomerVaultId = _genericAttributeService.GetAttribute<string>(customer, Constants.CustomerVaultIdKey);
                if (!string.IsNullOrEmpty(existingCustomerVaultId))
                {
                    values.Add("customer_vault_id", existingCustomerVaultId);
                }
                else
                {
                    _logger.Warning("Customer tried use a stored card but did not have a customer vault ID saved");
                }

                values.Add("billing_id", storedCardId.ToString());
            }
            else
            {
                values.Add("payment_token", processPaymentRequest.CustomValues[Constants.CardToken].ToString());
            }
        }

        /// <summary>
        /// Add name values based on if the "Name on card" fields are enabled, otherwise use the name from the billing address
        /// </summary>
        /// <param name="processPaymentRequest"></param>
        /// <param name="billingAddress"></param>
        /// <param name="values"></param>
        private void AddNameValues(ProcessPaymentRequest processPaymentRequest, Address billingAddress, IDictionary<string, string> values)
        {
            // if use name on card is enabled and has values then use that name for the transaction
            // otherwise use the billing address name
            if (_nmiPaymentSettings.UseNameOnCardField)
            {
                processPaymentRequest.CustomValues.TryGetValue(Constants.FirstNameOnCardKey, out object firstNameValue);
                processPaymentRequest.CustomValues.TryGetValue(Constants.LastNameOnCardKey, out object lastNameValue);

                values.Add("firstname", firstNameValue.ToString());
                values.Add("lastname", lastNameValue.ToString());
            }
            else
            {
                values.Add("firstname", billingAddress.FirstName);
                values.Add("lastname", billingAddress.LastName);
            }
        }
        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            var customer = _customerService.GetCustomerById(processPaymentRequest.CustomerId);
            var billingAddress = _customerService.GetCustomerBillingAddress(customer);

            if (customer == null || billingAddress == null)
            {
                throw new NopException("Could not retrieve customer or billing address");
            }

            var state = _stateProvinceService.GetStateProvinceById(billingAddress.StateProvinceId ?? 0);

            var values = new Dictionary<string, string>
            {
                { "payment", "creditcard" },
                { "type", _nmiPaymentSettings.TransactMode == TransactMode.AuthorizeAndCapture ? "sale" : "auth" },
                { "address1", billingAddress.Address1 },
                { "city", billingAddress.City },
                { "state", state?.Abbreviation },
                { "zip", billingAddress.ZipPostalCode.Substring(0, 5) },
                { "amount", processPaymentRequest.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture) },
                { "orderid", processPaymentRequest.OrderGuid.ToString() }
            };

            // add name values
            AddNameValues(processPaymentRequest, billingAddress, values);

            // save customer card if needed
            var saveCustomer = AddCustomerVaultValues(processPaymentRequest, customer, values);

            // determine if we need to used the stored card or the token generated from the new card
            AddStoredCardValues(processPaymentRequest, customer, values);

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
                        _genericAttributeService.SaveAttribute(customer, Constants.CustomerVaultIdKey, customer.CustomerGuid.ToString());
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
                _logger.Error("NMI Direct Post Error", exception, customer);
                result.AddError("Exception Occurred: " + exception.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //nothing
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>Additional handling fee</returns>
        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            return _paymentService.CalculateAdditionalFee(cart,
                _nmiPaymentSettings.AdditionalFee, _nmiPaymentSettings.AdditionalFeePercentage);
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return false;
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
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
                var response = _httpClient.PostAsync(NMI_DIRECT_POST_URL, new FormUrlEncodedContent(values)).Result;

                var responseValues = ExtractResponseValues(response.Content.ReadAsStringAsync().Result);

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
                _logger.Error("NMI Direct Post Error", exception);
                result.AddError("Exception Occurred: " + exception.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
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
                var response = _httpClient.PostAsync(NMI_DIRECT_POST_URL, new FormUrlEncodedContent(values)).Result;

                var responseValues = ExtractResponseValues(response.Content.ReadAsStringAsync().Result);

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
                _logger.Error("NMI Direct Post Error", exception);
                result.AddError("Exception Occurred: " + exception.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
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
                var response = _httpClient.PostAsync(NMI_DIRECT_POST_URL, new FormUrlEncodedContent(values)).Result;

                var responseValues = ExtractResponseValues(response.Content.ReadAsStringAsync().Result);

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
                _logger.Error("NMI Direct Post Error", exception);
                result.AddError("Exception Occurred: " + exception.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();

            var customer = _customerService.GetCustomerById(processPaymentRequest.CustomerId);
            var billingAddress = _customerService.GetCustomerBillingAddress(customer);

            if (customer == null || billingAddress == null)
            {
                throw new NopException("Could not retrieve customer or billing address");
            }

            var state = _stateProvinceService.GetStateProvinceById(billingAddress.StateProvinceId ?? 0);

            var orderTotal = processPaymentRequest.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture);

            var values = new Dictionary<string, string>
            {
                { "payment", "creditcard" },
                { "type", _nmiPaymentSettings.TransactMode == TransactMode.AuthorizeAndCapture ? "sale" : "auth" },
                { "address1", billingAddress.Address1 },
                { "city", billingAddress.City },
                { "state", state?.Abbreviation },
                { "zip", billingAddress.ZipPostalCode.Substring(0, 5) },
                { "amount",  orderTotal },
                { "orderid", processPaymentRequest.OrderGuid.ToString() }
            };

            // add name values
            AddNameValues(processPaymentRequest, billingAddress, values);

            // save customer card if needed
            var saveCustomer = AddCustomerVaultValues(processPaymentRequest, customer, values);

            // determine if we need to used the stored card or the token generated from the new card
            AddStoredCardValues(processPaymentRequest, customer, values);

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
                var response = _httpClient.PostAsync(NMI_DIRECT_POST_URL, new FormUrlEncodedContent(values)).Result;

                var responseValues = ExtractResponseValues(response.Content.ReadAsStringAsync().Result);

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
                        _genericAttributeService.SaveAttribute(customer, Constants.CustomerVaultIdKey, customer.CustomerGuid.ToString());
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
                _logger.Error("NMI Direct Post Error", exception, customer);
                result.AddError("Exception Occurred: " + exception.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
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
                var response = _httpClient.PostAsync(NMI_DIRECT_POST_URL, new FormUrlEncodedContent(values)).Result;

                var responseValues = ExtractResponseValues(response.Content.ReadAsStringAsync().Result);

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
                _logger.Error("NMI Direct Post Error", exception);
                result.AddError("Exception Occurred: " + exception.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public bool CanRePostProcessPayment(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //it's not a redirection payment method. So we always return false
            return false;
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>List of validating errors</returns>
        public IList<string> ValidatePaymentForm(IFormCollection form)
        {
            if (form == null)
                throw new ArgumentException(nameof(form));

            var warnings = new List<string>();

            if (_nmiPaymentSettings.UseNameOnCardField)
            {
                var firstName = form["FirstNameOnCard"].ToString();
                var lastName = form["LastNameOnCard"].ToString();

                if (string.IsNullOrEmpty(firstName))
                {
                    warnings.Add("First name cannot be empty");
                }

                if (string.IsNullOrEmpty(lastName))
                {
                    warnings.Add("Last name cannot be empty");
                }
            }

            //try to get errors
            if (form.TryGetValue("Errors", out StringValues errorsString) && !StringValues.IsNullOrEmpty(errorsString))
                warnings.Add(errorsString.ToString());

            return warnings;
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>Payment info holder</returns>
        public ProcessPaymentRequest GetPaymentInfo(IFormCollection form)
        {
            var paymentRequest = new ProcessPaymentRequest();

            //pass custom values to payment method
            if (form.TryGetValue("Token", out StringValues token) && !StringValues.IsNullOrEmpty(token))
                paymentRequest.CustomValues.Add(Constants.CardToken, token.ToString());

            if (form.TryGetValue("StoredCardId", out StringValues storedCardId) && !StringValues.IsNullOrEmpty(storedCardId) && !storedCardId.Equals("0"))
                paymentRequest.CustomValues.Add(Constants.StoredCardKey, storedCardId.ToString());

            if (form.TryGetValue("SaveCustomer", out StringValues saveCustomerValue) && !StringValues.IsNullOrEmpty(saveCustomerValue) && bool.TryParse(saveCustomerValue[0], out bool saveCustomer) && saveCustomer)
                paymentRequest.CustomValues.Add(Constants.SaveCustomerKey, saveCustomer);

            if (form.TryGetValue("FirstNameOnCard", out StringValues firstNameOnCardValue) && !StringValues.IsNullOrEmpty(firstNameOnCardValue))
                paymentRequest.CustomValues.Add(Constants.FirstNameOnCardKey, firstNameOnCardValue.ToString());

            if (form.TryGetValue("LastNameOnCard", out StringValues lastNameOnCardValue) && !StringValues.IsNullOrEmpty(lastNameOnCardValue))
                paymentRequest.CustomValues.Add(Constants.LastNameOnCardKey, lastNameOnCardValue.ToString());

            return paymentRequest;
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
        /// Install plugin
        /// </summary>
        public override void Install()
        {
            //settings
            var settings = new NmiPaymentSettings
            {
                Password = "123",
                Username = "456",
                UseUsernamePassword = false,
                TransactMode = TransactMode.AuthorizeAndCapture
            };
            _settingService.SaveSetting(settings);

            //locales
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.Username", "Username");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.Username.Hint", "Username assigned to the merchant account");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.Password", "Password");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.Password.Hint", "Password assigned to the merchant account");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.SecurityKey", "Security Key");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.SecurityKey.Hint", "API security key assigned to the merchant account, using this combined with username/password will result in an error.  If you want to save cards, then select that checkbox and enter username/password");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.CollectJsTokenizationKey", "Collect JS Tokenization Key");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.CollectJsTokenizationKey.Hint", "Tokenization key used for Collect.js library");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.TransactModeValues", "Transaction mode");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.TransactModeValues.Hint", "Choose transaction mode.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.AdditionalFee", "Additional fee");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.AdditionalFeePercentage", "Additional fee. Use percentage");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.AdditionalFeePercentage.Hint", "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.UseUsernamePassword", "Use username/password");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.UseUsernamePassword.Hint", "If enabled username/password will be used for authentication to the payment API instead of the security key");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.AllowCustomerToSaveCards", "Allow Customers To Store Cards");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.AllowCustomerToSaveCards.Hint", "If enabled registered customers will be able to save cards for future use.  Also, you must enter the username/password for this functionality to be available.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.PaymentMethodDescription", "Pay by credit / debit card");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.SaveCustomer", "Save card information");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.StoredCard", "Use a previously saved card");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.FirstNameOnCard", "First name on card");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.LastNameOnCard", "Last name on card");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.UseNameOnCardField", "Use 'Name on card' field");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Nmi.Fields.UseNameOnCardField.Hint", "Enables the 'Name on card' field during the payment checkout, instead of using the customers billing address name");

            base.Install();
        }

        /// <summary>
        /// Uninstall plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<NmiPaymentSettings>();

            //locales
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.Username");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.Username.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.Password");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.Password.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.SecurityKey");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.SecurityKey.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.CollectJsTokenizationKey");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.CollectJsTokenizationKey.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.TransactModeValues");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.TransactModeValues.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.AdditionalFee");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.AdditionalFee.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.AdditionalFeePercentage");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.AdditionalFeePercentage.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.UseUsernamePassword");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.UseUsernamePassword.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.PaymentMethodDescription");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.SaveCustomer");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.AllowCustomerToSaveCards");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.AllowCustomerToSaveCards.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.StoredCard");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.FirstNameOnCard");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.LastNameOnCard");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.UseNameOnCardField");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Nmi.Fields.UseNameOnCardField.Hint");

            base.Uninstall();
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

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public string PaymentMethodDescription => _localizationService.GetResource("Plugins.Payments.Nmi.PaymentMethodDescription");

        #endregion
    }
}
