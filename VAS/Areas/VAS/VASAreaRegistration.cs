using System.Web.Mvc;
using System.Web.Optimization;

namespace VAS
{
    public class VASAreaRegistration : AreaRegistration
    {
        public override string AreaName
        {
            get
            {
                return "VAS";
            }
        }

        public override void RegisterArea(AreaRegistrationContext context)
        {
            context.MapRoute(
                "VAS_default",
                "VAS/{controller}/{action}/{id}",
                new { action = "Index", id = UrlParameter.Optional }
            );

            StyleBundle style = new StyleBundle("~/Areas/VAS/Content/VASstyle");

            ScriptBundle modScript = new ScriptBundle("~/Areas/VAS/Scripts/VASjs");

            modScript.Include(
                  "~/Areas/VAS/Scripts/app/forms/createforecast.js",
                "~/Areas/VAS/Scripts/app/forms/glDimensionValue.js",
                  "~/Areas/VAS/Scripts/app/forms/vallocation.js",
                "~/Areas/VAS/Scripts/app/forms/vcreatefrom.js",
                "~/Areas/VAS/Scripts/app/forms/vcreatefrominvoice.js",
                "~/Areas/VAS/Scripts/app/forms/vcreatefromrequisition.js",
                "~/Areas/VAS/Scripts/app/forms/vcreatefromshipment.js",
                "~/Areas/VAS/Scripts/app/forms/vcreatefromstatement.js",
                "~/Areas/VAS/Scripts/app/forms/vcreateformprovisionalinvoice.js",
                "~/Areas/VAS/Scripts/app/forms/vcreaterelatedlines.js",
                "~/Areas/VAS/Scripts/app/forms/acctviewer.js",
                "~/Areas/VAS/Scripts/app/forms/acctviewermenu.js",
                "~/Areas/VAS/Scripts/app/forms/vinoutgen.js",
                "~/Areas/VAS/Scripts/app/forms/vinvoicegen.js",
                "~/Areas/VAS/Scripts/app/forms/vmatch.js",
                "~/Areas/VAS/Scripts/app/forms/vcharge.js",
                 "~/Areas/VAS/Scripts/app/forms/vattributegrid.js",
                 "~/Areas/VAS/Scripts/app/forms/vpayselect.js",
                  "~/Areas/VAS/Scripts/app/forms/vpayprint.js",
                  "~/Areas/VAS/Scripts/app/forms/vBOMdrop.js",
                  "~/Areas/VAS/Scripts/app/forms/vtrxmaterial.js",
                  "~/Areas/VAS/Scripts/app/forms/TabAlertRuleSql.js",
                  "~/Areas/VAS/Scripts/app/forms/vasattachuser.js",
                  "~/Areas/VAS/Scripts/app/forms/vasattachusererror.js",
                   "~/Areas/VAS/Scripts/model/Callouts.js",
                   "~/Areas/VAS/Scripts/model/CalloutAssignment.js",
                   "~/Areas/VAS/Scripts/model/calloutbankstatement.js",
                   "~/Areas/VAS/Scripts/model/calloutbpartner.js",
                   "~/Areas/VAS/Scripts/model/CalloutCashJournal.js",
                   "~/Areas/VAS/Scripts/model/calloutcheckinout.js",
                   "~/Areas/VAS/Scripts/model/calloutcontract.js",
                   "~/Areas/VAS/Scripts/model/calloutcrm.js",
                   "~/Areas/VAS/Scripts/model/calloutframework.js",
                   "~/Areas/VAS/Scripts/model/calloutgljournal.js",
                   "~/Areas/VAS/Scripts/model/calloutinout.js",
                   "~/Areas/VAS/Scripts/model/calloutinventory.js",
                   "~/Areas/VAS/Scripts/model/calloutinvoice.js",
                   "~/Areas/VAS/Scripts/model/calloutinvoicebatch.js",
                   "~/Areas/VAS/Scripts/model/calloutmasterforecast.js",
                   "~/Areas/VAS/Scripts/model/calloutmovement.js",
                   "~/Areas/VAS/Scripts/model/calloutoffer.js",
                   "~/Areas/VAS/Scripts/model/calloutofferserincluded.js",
                   "~/Areas/VAS/Scripts/model/calloutofferservices.js",
                   "~/Areas/VAS/Scripts/model/CalloutOrder.js",
                   "~/Areas/VAS/Scripts/model/calloutorderline.js",
                   "~/Areas/VAS/Scripts/model/calloutpayment.js",
                   "~/Areas/VAS/Scripts/model/calloutpayselection.js",
                   "~/Areas/VAS/Scripts/model/calloutpricelist.js",
                   "~/Areas/VAS/Scripts/model/CalloutProduct.js",
                   "~/Areas/VAS/Scripts/model/calloutproduction.js",
                   "~/Areas/VAS/Scripts/model/calloutproject.js",
                   "~/Areas/VAS/Scripts/model/calloutrequest.js",
                   "~/Areas/VAS/Scripts/model/calloutrequisition.js",
                   "~/Areas/VAS/Scripts/model/CalloutSetAttributeCode.js",
                   "~/Areas/VAS/Scripts/model/callouttax.js",
                   "~/Areas/VAS/Scripts/model/calloutteamforecast.js",
                   "~/Areas/VAS/Scripts/model/callouttimeexpense.js",
                   "~/Areas/VAS/Scripts/model/calloutInvRevaluation.js",
                   "~/Areas/VAS/Scripts/model/VAS_CalloutContract.js",
                   "~/Areas/VAS/Scripts/model/calloutTerm.js",
                   "~/Areas/VAS/Scripts/app/forms/PoReceiptTabPanel.js",
                   "~/Areas/VAS/Scripts/app/forms/InvoiceTaxTabPanel.js",
                   "~/Areas/VAS/Scripts/app/forms/PurchaseOrderTabPanel.js",
                   "~/Areas/VAS/Scripts/app/forms/LineHistoryTabPanel.js",
                   "~/Areas/VAS/Scripts/app/forms/RequisitionLinesTabPanel.js",
                   "~/Areas/VAS/Scripts/app/forms/MatchPOTabPanel.js",
                   "~/Areas/VAS/Scripts/app/forms/VAS_TimeSheetInvoice.js",
                   "~/Areas/VAS/Scripts/app/forms/VAS_ProductUomSetup.js",
                   "~/Areas/VAS/Scripts/app/forms/VAS_PurchaseOrderTabPanel.js",
                   "~/Areas/VAS/Scripts/app/forms/VAS_OrderSummary.js",
                   "~/Areas/VAS/Scripts/app/forms/UnAllocatedPaymentTabPanel.js",
                   "~/Areas/VAS/Scripts/app/forms/InvoiceLineTabPanel.js",
                   "~/Areas/VAS/Scripts/app/forms/VAS_ARInvoiceWidget.js",
                   "~/Areas/VAS/Scripts/app/forms/VAS_InvGrandTotalWidget.js"
                   );


            style.Include("~/Areas/VAS/Content/PaymentRule.css",
                "~/Areas/VAS/Content/style.css",
                "~/Areas/VAS/Content/PoReceiptTabPanel.css",
                "~/Areas/VAS/Content/VPaySelect.css",
                "~/Areas/VAS/Content/vasattachuser.css",
                "~/Areas/VAS/Content/VAS_PurchaseOrderTabPanel.css",
                "~/Areas/VAS/Content/VAS_ProductUomSetup.css");

            style.Include("~/Areas/VAS/Content/VIS.rtl.css");

            //style.Include("~/Areas/VAS/Content/VAS.all.min.css");
            //modScript.Include("~/Areas/VAS/Scripts/VAS.all.min.js");



            VAdvantage.ModuleBundles.RegisterScriptBundle(modScript, "VAS", -9);
            VAdvantage.ModuleBundles.RegisterStyleBundle(style, "VAS", -9);

        }
    }
}