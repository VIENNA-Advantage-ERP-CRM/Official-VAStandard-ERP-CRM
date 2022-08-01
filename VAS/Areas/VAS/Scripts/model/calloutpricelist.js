; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    //****************CalloutCurrency Start ******
    function CalloutCurrency() {
        VIS.CalloutEngine.call(this, "VIS.CalloutCurrency"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutCurrency, VIS.CalloutEngine);//inherit CalloutEngine

    CalloutCurrency.prototype.Currency = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }
        try {
            var PriceListData = VIS.dataContext.getJSONRecord("MPriceList/GetPriceList", value.toString());
            if (PriceListData != null) {
                mTab.setValue("C_Currency_ID", PriceListData.C_Currency_ID);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";

    };
    VIS.Model.CalloutCurrency = CalloutCurrency;
    //****************CalloutCurrency End ******


    //**************CalloutPriclistUOM Start********************
    function CalloutPriclistUOM() {
        VIS.CalloutEngine.call(this, "VIS.CalloutPriclistUOM");//must call
    };
    VIS.Utility.inheritPrototype(CalloutPriclistUOM, VIS.CalloutEngine); //inherit prototype

    CalloutPriclistUOM.prototype.SetUOM = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);

        // JID_0910: On change of product on line system is not removing the ASI. if product is changed then also update the ASI field.
        if (mTab.getField("M_AttributeSetInstance_ID") != null) {
            mTab.setValue("M_AttributeSetInstance_ID", null);
        }
        try {
            var _C_UOM_ID = VIS.dataContext.getJSONRecord("MProduct/GetC_UOM_ID", value.toString());
            mTab.setValue("C_UOM_ID", _C_UOM_ID);
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString()); // SD
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    VIS.Model.CalloutPriclistUOM = CalloutPriclistUOM;
    //**************CalloutPriclistUOM End********************


})(VIS, jQuery);