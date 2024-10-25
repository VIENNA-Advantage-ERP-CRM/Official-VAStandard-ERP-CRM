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
    /// <summary>
    ///Setting the value of year and period null when Budget Control Basis Field is changed
    /// </summary>
    /// <param name="ctx">Context</param>
    /// <param name="WindowNo">current Window No</param>
    /// <param name="mTab">Model Tab</param>
    /// <param name="mField">Model Field</param>
    /// <param name="value">The new value</param>
    /// <returns>""</returns>
    CalloutBudget.prototype.SetYearAndPeriodNull = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive())
        {
            return "";
        }
        if (value == null || value.toString() == "") {
            return "";
        }
        if (mTab.getValue("BudgetControlBasis") == "A")
        {
            mTab.setValue("C_Period_ID", null)
        }
        else if (mTab.getValue("BudgetControlBasis") == "P")
        {
            mTab.setValue("C_Year_ID", null)
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutBudget = CalloutBudget;

})(VIS, jQuery);