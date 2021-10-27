using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nixtus.Plugin.Payments.Nmi.Dtos;
using Nixtus.Plugin.Payments.Nmi.Models;
using Nop.Core;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Web.Framework.Components;

namespace Nixtus.Plugin.Payments.Nmi.Components
{
    [ViewComponent(Name = "Nmi")]
    public class NmiViewComponent : NopViewComponent
    {
        private readonly IWorkContext _workContext;
        private readonly ILogger _logger;
        private readonly NmiPaymentSettings _nmiPaymentSettings;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly HttpClient _httpClient = new HttpClient();
        private const string NMI_QUERY_URL = "https://msgpay.transactiongateway.com/api/query.php";

        public NmiViewComponent(IWorkContext workContext, ILogger logger, NmiPaymentSettings nmiPaymentSettings,
            ICustomerService customerService, IGenericAttributeService genericAttributeService)
        {
            _workContext = workContext;
            _logger = logger;
            _nmiPaymentSettings = nmiPaymentSettings;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
        }

        /// <summary>
        /// Invoke view component
        /// </summary>
        /// <param name="widgetZone">Widget zone name</param>
        /// <param name="additionalData">Additional data</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the view component result
        /// </returns>
        public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
        {
            var model = new PaymentInfoModel
            {
                IsGuest = await _customerService.IsGuestAsync(await _workContext.GetCurrentCustomerAsync()),
                AllowCustomerToSaveCards = _nmiPaymentSettings.AllowCustomerToSaveCards
            };

            if (_nmiPaymentSettings.AllowCustomerToSaveCards)
            {
                if (!string.IsNullOrEmpty(await _genericAttributeService.GetAttributeAsync<string>(await _workContext.GetCurrentCustomerAsync(), Constants.CustomerVaultIdKey)))
                {
                    await PopulateStoredCards(model);
                }

                model.StoredCards.Insert(0, new SelectListItem { Text = "Select a card...", Value = "0" });
            }

            return View("~/Plugins/Payments.Nmi/Views/PaymentInfo.cshtml", model);
        }

        private async Task PopulateStoredCards(PaymentInfoModel model)
        {
            try
            {
                var values = new Dictionary<string, string>
                {
                    { "username", _nmiPaymentSettings.Username },
                    { "password", _nmiPaymentSettings.Password },
                    { "report_type", "customer_vault" },
                    { "customer_vault_id", await _genericAttributeService.GetAttributeAsync<string>(await _workContext.GetCurrentCustomerAsync(), Constants.CustomerVaultIdKey) },
                    // this is an undocumented variable which will make the API return multiple billings (aka credit cards)
                    { "ver", "2" }
                };

                var response = await _httpClient.PostAsync(NMI_QUERY_URL, new FormUrlEncodedContent(values));
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var nmiQueryResponse = await DeserializeXml(content);

                    if (nmiQueryResponse?.CustomerVault != null)
                    {
                        foreach (var billing in nmiQueryResponse.CustomerVault.Customer.Billing ?? new List<Billing>())
                        {
                            model.StoredCards.Add(new SelectListItem
                            {
                                Value = billing.Id,
                                Text = $"{billing.CcNumber} " +
                                       $"(Exp. {billing.CcExp.Substring(0, 2)}/" +
                                       $"{billing.CcExp.Substring(2, 2)})"
                            });
                        }
                    }
                    else
                    {
                        await _logger.WarningAsync($"No saved cards where found in the response from NMI, Response: {content}");
                    }
                }

            }
            catch (Exception exception)
            {
                await _logger.ErrorAsync("NMI Error querying customer vault records", exception);
            }
        }

        private async Task<NmiQueryResponse> DeserializeXml(string xml)
        {
            try
            {
                var ser = new XmlSerializer(typeof(NmiQueryResponse));

                using (StringReader sr = new StringReader(xml))
                {
                    return (NmiQueryResponse)ser.Deserialize(sr);
                }
            }
            catch (Exception e)
            {
                await _logger.ErrorAsync("Error parsing xml", e);
                return null;
            }
        }
    }
}
