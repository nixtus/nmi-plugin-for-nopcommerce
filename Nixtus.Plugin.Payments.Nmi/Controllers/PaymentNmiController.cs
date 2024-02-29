using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nixtus.Plugin.Payments.Nmi.Models;
using Nop.Core;
using Nop.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nixtus.Plugin.Payments.Nmi.Controllers
{
    [AuthorizeAdmin]
    [Area(AreaNames.Admin)]
    [AutoValidateAntiforgeryToken]
    public class PaymentNmiController : BasePaymentController
    {
        private readonly ILocalizationService _localizationService;
        private readonly ISettingService _settingService;
        private readonly IPermissionService _permissionService;
        private readonly IStoreContext _storeContext;
        private readonly INotificationService _notificationService;

        public PaymentNmiController(ILocalizationService localizationService,
            ISettingService settingService,
            IPermissionService permissionService, IStoreContext storeContext, INotificationService notificationService)
        {
            _localizationService = localizationService;
            _settingService = settingService;
            _permissionService = permissionService;
            _storeContext = storeContext;
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var nmiPaymentSettings = await _settingService.LoadSettingAsync<NmiPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                Username = nmiPaymentSettings.Username,
                Password = nmiPaymentSettings.Password,
                UseUsernamePassword = nmiPaymentSettings.UseUsernamePassword,
                AllowCustomerToSaveCards = nmiPaymentSettings.AllowCustomerToSaveCards,
                SecurityKey = nmiPaymentSettings.SecurityKey,
                CollectJsTokenizationKey = nmiPaymentSettings.CollectJsTokenizationKey,
                TransactModeId = Convert.ToInt32(nmiPaymentSettings.TransactMode),
                TransactModeValues = await nmiPaymentSettings.TransactMode.ToSelectListAsync(),
                AdditionalFee = nmiPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = nmiPaymentSettings.AdditionalFeePercentage,
                ActiveStoreScopeConfiguration = storeScope
            };

            if (storeScope > 0)
            {
                model.Username_OverrideForStore = await _settingService.SettingExistsAsync(nmiPaymentSettings, x => x.Username, storeScope);
                model.Password_OverrideForStore = await _settingService.SettingExistsAsync(nmiPaymentSettings, x => x.Password, storeScope);
                model.UseUsernamePassword_OverrideForStore = await _settingService.SettingExistsAsync(nmiPaymentSettings, x => x.UseUsernamePassword, storeScope);
                model.AllowCustomerToSaveCards_OverrideForStore = await _settingService.SettingExistsAsync(nmiPaymentSettings, x => x.AllowCustomerToSaveCards, storeScope);
                model.SecurityKey_OverrideForStore = await _settingService.SettingExistsAsync(nmiPaymentSettings, x => x.SecurityKey, storeScope);
                model.CollectJsTokenizationKey_OverrideForStore = await _settingService.SettingExistsAsync(nmiPaymentSettings, x => x.CollectJsTokenizationKey, storeScope);
                model.TransactModeId_OverrideForStore = await _settingService.SettingExistsAsync(nmiPaymentSettings, x => x.TransactMode, storeScope);
                model.AdditionalFee_OverrideForStore = await _settingService.SettingExistsAsync(nmiPaymentSettings, x => x.AdditionalFee, storeScope);
                model.AdditionalFeePercentage_OverrideForStore = await _settingService.SettingExistsAsync(nmiPaymentSettings, x => x.AdditionalFeePercentage, storeScope);
            }

            return View("~/Plugins/Payments.Nmi/Views/Configure.cshtml", model);
        }

        [HttpPost]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var nmiPaymentSettings = await _settingService.LoadSettingAsync<NmiPaymentSettings>(storeScope);

            //save settings
            nmiPaymentSettings.Username = model.Username;
            nmiPaymentSettings.Password = model.Password;
            nmiPaymentSettings.UseUsernamePassword = model.UseUsernamePassword;
            nmiPaymentSettings.AllowCustomerToSaveCards = model.AllowCustomerToSaveCards;
            nmiPaymentSettings.AdditionalFee = model.AdditionalFee;
            nmiPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;
            nmiPaymentSettings.SecurityKey = model.SecurityKey;
            nmiPaymentSettings.CollectJsTokenizationKey = model.CollectJsTokenizationKey;
            nmiPaymentSettings.TransactMode = (TransactMode)model.TransactModeId;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            await _settingService.SaveSettingOverridablePerStoreAsync(nmiPaymentSettings, x => x.Password, model.Password_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(nmiPaymentSettings, x => x.Username, model.Username_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(nmiPaymentSettings, x => x.SecurityKey, model.SecurityKey_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(nmiPaymentSettings, x => x.CollectJsTokenizationKey, model.CollectJsTokenizationKey_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(nmiPaymentSettings, x => x.AdditionalFee, model.AdditionalFee_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(nmiPaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentage_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(nmiPaymentSettings, x => x.TransactMode, model.TransactModeId_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(nmiPaymentSettings, x => x.UseUsernamePassword, model.UseUsernamePassword_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(nmiPaymentSettings, x => x.AllowCustomerToSaveCards, model.AllowCustomerToSaveCards_OverrideForStore, storeScope, false);

            //now clear settings cache
            await _settingService.ClearCacheAsync();

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return await Configure();
        }
    }
}