; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    function CalloutBPartner() {
        VIS.CalloutEngine.call(this, "VIS.CalloutBPartner");
    };
    VIS.Utility.inheritPrototype(CalloutBPartner, VIS.CalloutEngine);//inherit prototype
    CalloutBPartner.prototype.BPGroup = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        try {
            // var _sql = "select M_ReturnPolicy_ID, M_DiscountSchema_ID, M_PRICELIST_ID, PO_PriceList_ID,PO_DiscountSchema_ID,PO_ReturnPolicy_ID from C_BP_Group where C_BP_Group_ID=" + Util.getValueOfInt(value);
            //var ds = VIS.DB.executeDataSet(_sql);
            var idr = VIS.dataContext.getJSONRecord("MBPartner/GetBPGroup", value.toString());

            var IsCustomer = mTab.getValue("IsCustomer");
            var IsVendor = mTab.getValue("IsVendor");
            if (IsCustomer) {
                if (idr != null && Object.keys(idr).length > 0) {
                    //var _returnPolicy = Util.getValueOfInt(idr.M_ReturnPolicy_ID);
                    //var _discountSchema = Util.getValueOfInt(idr.M_DiscountSchema_ID);
                    //var _pricelist = Util.getValueOfInt(idr.M_PriceList_ID);
                    mTab.setValue("M_ReturnPolicy_ID", Util.getValueOfInt(idr.M_ReturnPolicy_ID));
                    mTab.setValue("M_DiscountSchema_ID", Util.getValueOfInt(idr.M_DiscountSchema_ID));
                    mTab.setValue("M_PRICELIST_ID", Util.getValueOfInt(idr.M_PriceList_ID));
                }
            }
            if (IsVendor) {
                if (idr != null && Object.keys(idr).length > 0) {
                    //_returnPolicy = Util.getValueOfInt(idr.PO_ReturnPolicy_ID);
                    //_discountSchema = Util.getValueOfInt(idr.PO_DiscountSchema_ID);
                    //_pricelist = Util.getValueOfInt(idr.PO_PriceList_ID);
                    mTab.setValue("PO_ReturnPolicy_ID", Util.getValueOfInt(idr.PO_ReturnPolicy_ID));
                    mTab.setValue("PO_DiscountSchema_ID", Util.getValueOfInt(idr.PO_DiscountSchema_ID));
                    mTab.setValue("PO_PriceList_ID", Util.getValueOfInt(idr.PO_PriceList_ID));
                }
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString()); // SD
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    }
    VIS.Model.CalloutBPartner = CalloutBPartner;


    //**************CalloutSetBP Start******************
    function CalloutSetBP() {
        VIS.CalloutEngine.call(this, "VIS.CalloutSetBP");//must call
    };
    VIS.Utility.inheritPrototype(CalloutSetBP, VIS.CalloutEngine); //inherit prototype
    CalloutSetBP.prototype.SetBP = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            return "";
        }
        if (this.isCalloutActive()) {
            return "";
        }
        try {
            this.setCalloutActive(true);
            mTab.setValue("C_BPartner_ID", Util.getValueOfInt(value));
            this.setCalloutActive(false);
        }
        catch (err) {
            this.setCalloutActive(false);
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutSetBP = CalloutSetBP;
    //**************CalloutSetBP End******************

})(VIS, jQuery);