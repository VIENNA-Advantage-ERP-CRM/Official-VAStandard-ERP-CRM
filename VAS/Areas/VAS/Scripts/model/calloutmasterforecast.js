; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    function CalloutMasterForecast() {
        VIS.CalloutEngine.call(this, "VIS.CalloutMasterForecast");//must call
    };
    VIS.Utility.inheritPrototype(CalloutMasterForecast, VIS.CalloutEngine); //inherit prototype

    /**
     * set currency 
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutMasterForecast.prototype.Currency = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            if (mTab.getValue("M_PriceList_ID") == null) {
                mTab.setValue("C_Currency_ID", Util.getValueOfInt(ctx.getContext("$C_Currency_ID")));
            }
            return "";
        }
        try {
            this.setCalloutActive(true);
            //get currency from pricelist
            var pricelist = VIS.dataContext.getJSONRecord("MPriceList/GetPriceListData", Util.getValueOfString(mTab.getValue("M_PriceList_ID")));
            if (pricelist["C_Currency_ID"] != null) {
                mTab.setValue("C_Currency_ID", pricelist["C_Currency_ID"]);
            }
        }
        catch (err) {
            this.log.log(Level.SEVERE, sql, err);
            return err.message;
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";;
    };

    /**
     * Set Price as per price list
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutMasterForecast.prototype.Product = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            if (mTab.getValue("M_Product_ID") == null) {
                //set values to 0 if no product is selected
                mTab.setValue("Price", 0);
                mTab.setValue("PlannedRevenue", 0);
                mTab.setValue("ForcastQty", 0);
                mTab.setValue("TotalQty", 0);
            }
            return "";
        }
        try {
            this.setCalloutActive(true);
            if (ctx.getContextAsInt(windowNo, "M_PriceList_ID") > 0) {
                var stdPrecision = VIS.dataContext.getJSONRecord("MCurrency/GetCurrency", Util.getValueOfString(ctx.getContextAsInt(windowNo, "C_Currency_ID")));
                var paramString = Util.getValueOfString(mTab.getValue("M_Product_ID")).concat(",", Util.getValueOfString(mTab.getValue("M_AttributeSetInstance_ID")), ",",
                    Util.getValueOfString(ctx.getContextAsInt(windowNo, "M_PriceList_ID")), ",",
                    Util.getValueOfString(mTab.getValue("C_UOM_ID")))

                //get the price from product price only if pricelist is selected
                var ProductData = VIS.dataContext.getJSONRecord("MProductPricing/GetProductdata", paramString);
                if (ProductData != null) {
                    mTab.setValue("Price", ProductData["PriceStd"]);
                    mTab.setValue("PlannedRevenue", (ProductData["PriceStd"] * Util.getValueOfDecimal(mTab.getValue("TotalQty"))).toFixed(Util.getValueOfInt(stdPrecision.StdPrecision)));

                    //set BaseUOM
                    if (Util.getValueOfInt(mTab.getValue("C_UOM_ID")) == 0) {
                        mTab.setValue("C_UOM_ID", ProductData["C_UOM_ID"]);
                    }

                    if (mTab.findColumn("IsBOM") > 0) {
                        mTab.setValue("IsBOM", ProductData["IsBOM"]);
                    }

                }
                //set BOM,BOMuse and Routing
                var BOMData = VIS.dataContext.getJSONRecord("MTeamForcast/GetBOMdetails", Util.getValueOfString(mTab.getValue("M_Product_ID")));
                if (BOMData != null) {
                    mTab.setValue("BOMUse", BOMData["BOMUse"]);
                    mTab.setValue("VAMFG_M_Routing_ID", BOMData["VAMFG_M_Routing_ID"]);
                    mTab.setValue("M_BOM_ID", BOMData["M_BOM_ID"]);
                }
            }
        }
        catch (err) {
            this.log.log(Level.SEVERE, sql, err);
            return err.message;
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    /**
     * Set price and Qty
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutMasterForecast.prototype.CalculatePrice = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "" || Util.getValueOfInt(value) <= 0) {
            return "";
        }
        try {
            this.setCalloutActive(true);
            var stdPrecision = VIS.dataContext.getJSONRecord("MCurrency/GetCurrency", Util.getValueOfString(ctx.getContextAsInt(windowNo, "C_Currency_ID")));
            //set Total Qty
            var totalqty = Util.getValueOfDecimal(mTab.getValue("ForcastQty")) + Util.getValueOfDecimal(mTab.getValue("SalesOrderQty"))
                + Util.getValueOfDecimal(mTab.getValue("OppQty"));
            mTab.setValue("TotalQty", totalqty);

            //set Planned Revenue
            var price = Util.getValueOfDecimal(mTab.getValue("Price")) * Util.getValueOfDecimal(mTab.getValue("TotalQty"));
            mTab.setValue("PlannedRevenue", price.toFixed(Util.getValueOfInt(stdPrecision.StdPrecision)));

        }
        catch (err) {
            this.log.log(Level.SEVERE, sql, err);
            return err.message;
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    VIS.Model.CalloutMasterForecast = CalloutMasterForecast;

})(VIS, jQuery);