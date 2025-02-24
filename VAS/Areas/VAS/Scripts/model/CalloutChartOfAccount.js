/********************************************************************************
 * Module Name    : VAS
 * Purpose        : This Callout is created to clear the bank and foreign currency
                    details
 * chronological  : Development
 * Created Date   : 22 Nov 2024
 * Created by     : VIS_427
 ********************************************************************************/

; (function (VIS, $) {

    function CalloutChartOfAccount() {
        VIS.CalloutEngine.call(this, "VIS.CalloutChartOfAccount"); //must call
    }
    VIS.Utility.inheritPrototype(CalloutChartOfAccount, VIS.CalloutEngine);//inherit CalloutEngine
    /**
     * this callout use to clear the bank account,currency and mark foreign currency checkbox as false
     * on click of bank account checkbox
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutChartOfAccount.prototype.ClearBankAccountDetails = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive()) {
            return "";
        }
        if (value == null || value.toString() == "") {
            return "";
        }
        try {
            this.setCalloutActive(true);
            if (mTab.getValue("C_BankAccount_ID") != null) {
                mTab.setValue("C_BankAccount_ID", null);
            }
            if (mTab.getValue("C_Currency_ID") != null) {
                mTab.setValue("C_Currency_ID", null);
            } 
            mTab.setValue("IsForeignCurrency", false)
        }
        catch (err) {
            this.setCalloutActive(false);
            return err;
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    /**
    * this callout use to clear currency on click of foreign currency checkbox
    * @param {any} ctx
    * @param {any} windowNo
    * @param {any} mTab
    * @param {any} mField
    * @param {any} value
    * @param {any} oldValue
    */
    CalloutChartOfAccount.prototype.ClearForeignCurrencyDetails = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive()) {
            return "";
        }
        if (value == null || value.toString() == "") {
            return "";
        }
        try {
            this.setCalloutActive(true);
            if (mTab.getValue("C_Currency_ID") != null) {
                mTab.setValue("C_Currency_ID", null);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            return err;
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutChartOfAccount = CalloutChartOfAccount;

})(VIS, jQuery);