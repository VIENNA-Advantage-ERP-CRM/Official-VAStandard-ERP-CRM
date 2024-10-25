/********************************************************************************
 * Module Name    : VAS
 * Purpose        : This Callout is created to set the value of year and period 
                    null when Budget Control Basis Field is changed
 * chronological  : Development
 * Created Date   : 25 Oct 2024
 * Created by     : VIS_427
 ********************************************************************************/

; (function (VIS, $) {

    function CalloutBudget() {
        VIS.CalloutEngine.call(this, "VIS.CalloutBudget"); //must call
    }
    VIS.Utility.inheritPrototype(CalloutBudget, VIS.CalloutEngine);//inherit CalloutEngine
    /**
     * Setting the value of year and period null when Budget Control Basis Field is changed
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutBudget.prototype.SetYearAndPeriodNull = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive()) {
            return "";
        }
        if (value == null || value.toString() == "") {
            return "";
        }
        try {
            this.setCalloutActive(true);
            //If BudgetControlBasis is Annual then set C_Period_ID null
            if (mTab.getValue("BudgetControlBasis") == "A") {
                mTab.setValue("C_Period_ID", null)
            }
            //If BudgetControlBasis is Period then set C_Year_ID null
            else if (mTab.getValue("BudgetControlBasis") == "P") {
                mTab.setValue("C_Year_ID", null)
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
    VIS.Model.CalloutBudget = CalloutBudget;

})(VIS, jQuery);