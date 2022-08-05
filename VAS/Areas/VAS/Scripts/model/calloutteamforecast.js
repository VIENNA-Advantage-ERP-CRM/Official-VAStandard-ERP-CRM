; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    function CalloutTeamForcast() {
        VIS.CalloutEngine.call(this, "VIS.CalloutTeamForcast");//must call
    };
    VIS.Utility.inheritPrototype(CalloutTeamForcast, VIS.CalloutEngine); //inherit prototype
    CalloutTeamForcast.prototype.ProductInfo = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            return "";
        }

        var M_Product_ID = value;
        if (M_Product_ID == null || M_Product_ID == 0)
            return "";

        var M_Product_ID = ctx.getContextAsInt(windowNo, "M_Product_ID");
        if (M_Product_ID == null || M_Product_ID == 0)
            return "";

        var M_PriceList_ID = ctx.getContextAsInt(windowNo, "M_PriceList_ID");
        var M_Attribute_ID = ctx.getContextAsInt(windowNo, "M_AttributeSetInstance_ID");
        if (M_PriceList_ID != 0) {

            var fields = M_Product_ID.toString() + ", " + M_PriceList_ID.toString() + "," + M_Attribute_ID.toString();
            var PriceStd = VIS.dataContext.getJSONRecord("MTeamForcast/GetProductPrice", fields);

            if (PriceStd != 0) {
                mTab.setValue("PriceStd", PriceStd);
                mTab.setValue("UnitPrice", PriceStd);
                mTab.setValue("PriceStd", (PriceStd * Util.getValueOfDecimal(mTab.getValue("QtyEntered"))));
                mTab.setValue("TotalPrice", (PriceStd * Util.getValueOfDecimal(mTab.getValue("QtyEntered"))));
            }
        }
        else {
            VIS.ADialog.info("PriceLisetNotFound");
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    CalloutTeamForcast.prototype.Charge = function (ctx, windowNo, mTab, mField, value, oldValue) {
        try {
            if (this.isCalloutActive() || value == null || value.toString() == "") {
                //mTab.getField("C_UOM_ID").setReadOnly(false);
                return "";
            }

            this.setCalloutActive(true);

            //	No Product defined
            if (mTab.getValue("M_Product_ID") != null) {
                mTab.setValue("M_Product_ID", null);
                mTab.setValue("M_AttributeSetInstance_ID", null);
            }

            //	Default charge from context
            var c_uom_id = ctx.getContextAsInt("#C_UOM_ID");
            if (c_uom_id > 0) {
                mTab.setValue("C_UOM_ID", c_uom_id);
            }
            else {
                mTab.setValue("C_UOM_ID", 100);	//	EA
            }
            //mTab.getField("C_UOM_ID").setReadOnly(true);
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.log(Level.SEVERE, sql, err);
            return err
        }
        this.setCalloutActive(false);
        oldValue = null;
    };

    CalloutTeamForcast.prototype.CalculatePrice = function (ctx, windowNo, mTab, mField, value, oldValue) {
        // 
        if (this.isCalloutActive() || value == null || value.toString() == "" || Util.getValueOfInt(value) <= 0) {
            return "";
        }
        this.setCalloutActive(true);
        var stdPrecision = VIS.dataContext.getJSONRecord("MCurrency/GetCurrency", Util.getValueOfString(ctx.getContextAsInt(windowNo, "C_Currency_ID")));
        var price = Util.getValueOfDecimal(mTab.getValue("UnitPrice")) * Util.getValueOfDecimal(mTab.getValue("BaseQty"));
        // ForcastLine.SetQtyEntered(price);
        mTab.setValue("PriceStd", price.toFixed(Util.getValueOfInt(stdPrecision.StdPrecision)));
        mTab.setValue("TotalPrice", price.toFixed(Util.getValueOfInt(stdPrecision.StdPrecision)));

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /**
     * UOM Conversion
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutTeamForcast.prototype.Qty = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            return "";
        }
        this.setCalloutActive(true);
        var C_UOM_ID = mTab.getValue("C_UOM_ID");
        if (C_UOM_ID == null) {
            C_UOM_ID = ctx.getContextAsInt(windowNo, "C_UOM_ID");
        }
        var Qty = mTab.getValue("BaseQty");
        var stdPrecision = VIS.dataContext.getJSONRecord("MCurrency/GetCurrency", Util.getValueOfString(ctx.getContextAsInt(windowNo, "C_Currency_ID")));
        if (mTab.getValue("M_Product_ID") != null) {
            var M_Product_ID = mTab.getValue("M_Product_ID");
            var paramStr = M_Product_ID.toString().concat(",", C_UOM_ID.toString(), ",", Qty.toString());
            var pc = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramStr);
            if (pc != null) {
                mTab.setValue("QtyEntered", pc);
            }
            else {
                mTab.setValue("QtyEntered", Qty);
            }
        }
        else {
            mTab.setValue("QtyEntered", Qty);
        }
        if (Util.getValueOfDecimal(mTab.getValue("UnitPrice")) != 0 && Qty != 0) {
            mTab.setValue("PriceStd", (Qty * mTab.getValue("UnitPrice")).toFixed(Util.getValueOfInt(stdPrecision.StdPrecision)));
            mTab.setValue("TotalPrice", (Qty * mTab.getValue("UnitPrice")).toFixed(Util.getValueOfInt(stdPrecision.StdPrecision)));
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /**
     * set currency on the basis of Pricelist
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutTeamForcast.prototype.Currency = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            return "";
        }
        this.setCalloutActive(true);
        //get currency from pricelist
        var pricelist = VIS.dataContext.getJSONRecord("MPriceList/GetPriceListData", Util.getValueOfString(mTab.getValue("M_PriceList_ID")));
        if (pricelist["C_Currency_ID"] != null) {
            mTab.setValue("C_Currency_ID", pricelist["C_Currency_ID"]);
        }

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /**
     *Set Std price 
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutTeamForcast.prototype.ProductPrice = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            if (mTab.getValue("M_Product_ID") == null) {
                //set values to 0 if no product is selected
                mTab.setValue("PriceStd", 0);
                mTab.setValue("TotalPrice", 0);
                mTab.setValue("UnitPrice", 0);
                mTab.setValue("QtyEntered", 1);
                mTab.setValue("BaseQty", 1);
            }
            return "";
        }
        this.setCalloutActive(true);
        if (ctx.getContextAsInt(windowNo, "M_PriceList_ID") > 0) {
            var stdPrecision = VIS.dataContext.getJSONRecord("MCurrency/GetCurrency", Util.getValueOfString(ctx.getContextAsInt(windowNo, "C_Currency_ID")));
            var paramString = Util.getValueOfString(mTab.getValue("M_Product_ID")).concat(",", Util.getValueOfString(mTab.getValue("M_AttributeSetInstance_ID")), ",",
                Util.getValueOfString(ctx.getContextAsInt(windowNo, "M_PriceList_ID")), ",",
                Util.getValueOfString(mTab.getValue("C_UOM_ID")))

            //get the price from product price inly if pricelist is selected
            var ProductData = VIS.dataContext.getJSONRecord("MProductPricing/GetProductdata", paramString);
            if (ProductData != null) {
                mTab.setValue("PriceStd", ProductData["PriceStd"]);
                mTab.setValue("UnitPrice", ProductData["PriceStd"]);
                mTab.setValue("PriceStd", (ProductData["PriceStd"] * Util.getValueOfDecimal(mTab.getValue("BaseQty"))).toFixed(Util.getValueOfInt(stdPrecision.StdPrecision)));
                mTab.setValue("TotalPrice", (ProductData["PriceStd"] * Util.getValueOfDecimal(mTab.getValue("BaseQty"))).toFixed(Util.getValueOfInt(stdPrecision.StdPrecision)));

                if (Util.getValueOfInt(mTab.getValue("C_UOM_ID")) == 0) {
                    mTab.setValue("C_UOM_ID", ProductData["C_UOM_ID"]);
                }

                if (mTab.findColumn("IsBOM") > 0) {
                    mTab.setValue("IsBOM", ProductData["IsBOM"]);
                }
            }
        }
        //set BOM,BOMuse and Routing
        var BOMData = VIS.dataContext.getJSONRecord("MTeamForcast/GetBOMdetails", Util.getValueOfString(mTab.getValue("M_Product_ID")));
        if (BOMData != null) {
            mTab.setValue("BOMUse", BOMData["BOMUse"]);
            mTab.setValue("VAMFG_M_Routing_ID", BOMData["VAMFG_M_Routing_ID"]);
            mTab.setValue("M_BOM_ID", BOMData["M_BOM_ID"]);
        }

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    /**
     * Set Period as per account date
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutTeamForcast.prototype.AccountDate = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        try {
            this.setCalloutActive(true);

            var paramString = Util.getValueOfString(mTab.getValue("AD_Client_ID")).concat(",", Util.getValueOfString(mTab.getValue("DateAcct")), ",",
                Util.getValueOfString(mTab.getValue("AD_Org_ID")))

            //get Period
            var Period = VIS.dataContext.getJSONRecord("MPeriod/GetPeriod", paramString);
            if (Period != null) {
                mTab.setValue("C_Period_ID", Period);
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
     * Set Supervisor
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutTeamForcast.prototype.SuperVisor = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            return "";
        }
        try {
            this.setCalloutActive(true)
            //get supervisor
            var SuperVisor = VIS.dataContext.getJSONRecord("MTeamForcast/GetSuperVisor", Util.getValueOfString(value));
            if (SuperVisor != null) {
                mTab.setValue("Supervisor_ID", SuperVisor);
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

    VIS.Model.CalloutTeamForcast = CalloutTeamForcast;

})(VIS, jQuery);