using System;
using Microsoft.AspNetCore.Mvc;
using Nixtus.Plugin.Payments.Nmi.Models;
using Nop.Core;
using Nop.Core.Domain.Payments;
using Nop.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nixtus.Plugin.Payments.Nmi.Controllers
{
    public class PaymentNmiController : BasePaymentController
    {
        private readonly ILocalizationService _localizationService;
        private readonly ISettingService _settingService;
        private readonly IStoreService _storeService;
        private readonly IWorkContext _workContext;
        private readonly IPaymentService _paymentService;
        private readonly PaymentSettings _paymentSettings;
        private readonly IPermissionService _permissionService;
        
        public PaymentNmiController(ILocalizationService localizationService,
            ISettingService settingService,
            IStoreService storeService,
            IWorkContext workContext,
            IPaymentService paymentService,
            PaymentSettings paymentSettings,
            IPermissionService permissionService)
        {
            this._localizationService = localizationService;
            this._settingService = settingService;
            this._storeService = storeService;
            this._workContext = workContext;
            this._paymentService = paymentService;
            this._paymentSettings = paymentSettings;
            this._permissionService = permissionService;
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var nmiPaymentSettings = _settingService.LoadSetting<NmiPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                Username = nmiPaymentSettings.Username,
                Password = nmiPaymentSettings.Password,
                SecurityKey = nmiPaymentSettings.SecurityKey,
                CollectJsTokenizationKey = nmiPaymentSettings.CollectJsTokenizationKey,
                TransactModeId = Convert.ToInt32(nmiPaymentSettings.TransactMode),
                TransactModeValues = nmiPaymentSettings.TransactMode.ToSelectList(),
                AdditionalFee = nmiPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = nmiPaymentSettings.AdditionalFeePercentage,
                ActiveStoreScopeConfiguration = storeScope,
            };

            if (storeScope > 0)
            {
                model.Username_OverrideForStore = _settingService.SettingExists(nmiPaymentSettings, x => x.Username, storeScope);
                model.Password_OverrideForStore = _settingService.SettingExists(nmiPaymentSettings, x => x.Password, storeScope);
                model.SecurityKey_OverrideForStore = _settingService.SettingExists(nmiPaymentSettings, x => x.SecurityKey, storeScope);
                model.CollectJsTokenizationKey_OverrideForStore = _settingService.SettingExists(nmiPaymentSettings, x => x.CollectJsTokenizationKey, storeScope);
                model.TransactModeId_OverrideForStore = _settingService.SettingExists(nmiPaymentSettings, x => x.TransactMode, storeScope);
                model.AdditionalFee_OverrideForStore = _settingService.SettingExists(nmiPaymentSettings, x => x.AdditionalFee, storeScope);
                model.AdditionalFeePercentage_OverrideForStore = _settingService.SettingExists(nmiPaymentSettings, x => x.AdditionalFeePercentage, storeScope);
            }

            return View("~/Plugins/Payments.Nmi/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            //load settings for a chosen store scope
            var storeScope = GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var nmiPaymentSettings = _settingService.LoadSetting<NmiPaymentSettings>(storeScope);

            //save settings
            nmiPaymentSettings.Username = model.Username;
            nmiPaymentSettings.Password = model.Password;
            nmiPaymentSettings.AdditionalFee = model.AdditionalFee;
            nmiPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;
            nmiPaymentSettings.SecurityKey = model.SecurityKey;
            nmiPaymentSettings.CollectJsTokenizationKey = model.CollectJsTokenizationKey;
            nmiPaymentSettings.TransactMode = (TransactMode)model.TransactModeId;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            _settingService.SaveSettingOverridablePerStore(nmiPaymentSettings, x => x.Password, model.Password_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(nmiPaymentSettings, x => x.Username, model.Username_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(nmiPaymentSettings, x => x.SecurityKey, model.SecurityKey_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(nmiPaymentSettings, x => x.CollectJsTokenizationKey, model.CollectJsTokenizationKey_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(nmiPaymentSettings, x => x.AdditionalFee, model.AdditionalFee_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(nmiPaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentage_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(nmiPaymentSettings, x => x.TransactMode, model.TransactModeId_OverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }
    }
}