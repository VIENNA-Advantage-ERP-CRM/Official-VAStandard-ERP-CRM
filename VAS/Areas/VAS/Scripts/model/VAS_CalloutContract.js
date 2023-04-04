; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;
    var steps = false;

    //*************** VAS_CalloutContract ************  
    function VAS_CalloutContract() {
        VIS.CalloutEngine.call(this, "VIS.VAS_CalloutContract");//must call
    };
    VIS.Utility.inheritPrototype(VAS_CalloutContract, VIS.CalloutEngine); //inherit prototype
    VAS_CalloutContract.prototype.CalculateContDuration = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var SDate = Util.getValueOfDate(mTab.getValue("StartDate"));
        var Edate = Util.getValueOfDate(mTab.getValue("EndDate"));
        //var totaldays = dateDiffInYears(SDate, Edate);
        var totalMonths = (Edate.getDate() - SDate.getDate()) / 30 +
            Edate.getMonth() - SDate.getMonth();
        var totalYears = (Edate.getFullYear() - SDate.getFullYear());
        //var difference = (Edate.getDate() - SDate.getDate()) / 30 +
        //Edate.getMonth() - SDate.getMonth() +
        //(12 * (Edate.getFullYear() - SDate.getFullYear()));
        //var count = difference.toFixed(2);
        mTab.setValue("VAS_ContractMonths", totalMonths.toFixed(2));
        mTab.setValue("VAS_ContractDuration", totalYears.toFixed(2));
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";

    };
    /**
      * *************************Callout check End date must be greater than Start date****************************
      * @param {any} ctx
      * @param {any} windowNo
      * @param {any} mTab
      * @param {any} mField
      * @param {any} value
      * @param {any} oldValue
      */
    VAS_CalloutContract.prototype.EndDate = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var startDate = new Date(mTab.getValue("StartDate"));
        var endDate = new Date(value);
        endDate = endDate.toISOString();
        startDate = startDate.toISOString();
            if (mTab.getValue("StartDate") != null) {
                if (endDate < startDate) {
                    mTab.setValue("EndDate", null);
                    this.setCalloutActive(false);
                    return "VAS_EndDateMustGreater";
                }
            }        
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    /**
     * *****************Callout check Start Date must be less than End Date********************
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    VAS_CalloutContract.prototype.StartDate = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var startDate = new Date(value);
        var endDate = new Date(mTab.getValue("EndDate"));
        endDate = endDate.toISOString();
        startDate = startDate.toISOString();
            if (mTab.getValue("EndDate") != null) {
                if (endDate < startDate) {
                    mTab.setValue("StartDate", null);
                    this.setCalloutActive(false);
                    return "VAS_EndDateMustGreater";
                }
            }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    VAS_CalloutContract.prototype.ContractRef = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var result = VIS.dataContext.getJSONRecord("MVASContract/GetContractDetails", Util.getValueOfInt(value));
        if (result) {
            mTab.setValue("Bill_Location_ID", result["Bill_Location_ID"]);
            mTab.setValue("Bill_User_ID", result["Bill_User_ID"]);
            mTab.setValue("C_BPartner_ID", result["C_BPartner_ID"]);
            mTab.setValue("C_Currency_ID", result["C_Currency_ID"]);
            mTab.setValue("C_IncoTerm_ID", result["C_IncoTerm_ID"]);
            mTab.setValue("C_PaymentTerm_ID", result["C_PaymentTerm_ID"]);
            mTab.setValue("C_Project_ID", result["C_Project_ID"]);
            mTab.setValue("M_PriceList_ID", result["M_PriceList_ID"]);
            mTab.setValue("VA009_PaymentMethod_ID", result["VA009_PaymentMethod_ID"]);
            mTab.setValue("C_BPartner_Location_ID", result["Bill_Location_ID"]);
            mTab.setValue("VAS_ContractCategory_ID", result["VAS_ContractCategory_ID"]);
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VAS_CalloutContract.prototype.PriceList = function (ctx, windowNo, mTab, mField, value, oldValue) {

        var dr = null;
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        try {

            var M_PriceList_ID = Util.getValueOfInt(value.toString());
            if (M_PriceList_ID == null || M_PriceList_ID == 0)
                return "";
            this.setCalloutActive(true);
            if (steps) {
                this.log.warning("init");
            }
            dr = VIS.dataContext.getJSONRecord("MPriceList/GetPriceListData", value.toString());
            if (dr != null) {
                //	Currency
                mTab.setValue("C_Currency_ID", dr["C_Currency_ID"]);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            if (dr != null) {
                dr.close();
            }
            this.log(Level.SEVERE, "", err);
            return err;
        }
        if (steps) {
            this.log.warning("finish");
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    function dateDiffInYears(dateold, datenew) {
        var ynew = datenew.getFullYear();
        var mnew = datenew.getMonth();
        var dnew = datenew.getDate();
        var yold = dateold.getFullYear();
        var mold = dateold.getMonth();
        var dold = dateold.getDate();
        var diff = ynew - yold;
        if (mold > mnew) diff--;
        else {
            if (mold == mnew) {
                if (dold > dnew) diff--;
            }
        }
        return diff;
    };
    VIS.Model.VAS_CalloutContract = VAS_CalloutContract;
    //***************VAS_CalloutContract End ************

})(VIS, jQuery);