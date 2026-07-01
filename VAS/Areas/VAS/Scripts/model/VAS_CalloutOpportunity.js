; VAS = window.VAS || {};
; (function (VAS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;
    //*********** CalloutVASOpportunity Start ****
    function CalloutVASOpportunity() {
        VIS.CalloutEngine.call(this, "VAS.CalloutVASOpportunity");//must call
    };
    VIS.Utility.inheritPrototype(CalloutVASOpportunity, VIS.CalloutEngine); //inherit prototype
    /**
     * To set Product info without C_ProjectTask_ID, C_ProjectPhase_ID for VAS_opportunity window.
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     * @returns
     */
    CalloutVASOpportunity.prototype.ProductInfo = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (this.isCalloutActive()) {
            return "";
        }
        this.setCalloutActive(true);
        if (value == null || value.toString() == "") {
            if (Util.getValueOfInt(mTab.getValue("M_Product_ID")) == 0) {
                //set prices and quantities 0 if product is deselected
                mTab.setValue("PriceList", 0);
                mTab.setValue("PlannedPrice", 0);
                mTab.setValue("Discount", 0)
                mTab.setValue("PlannedMarginAmt", 0);
                mTab.setValue("PlannedAmt", 0);
                mTab.setValue("ProductType", "");
            }
            this.setCalloutActive(false);
            return "";
        }

        // because, this method is used to get product info based on Product ID
        if (Util.getValueOfInt(mTab.getValue("M_Product_ID")) == 0) {
            mTab.setValue("PriceList", 0);
            mTab.setValue("PlannedPrice", 0);
            mTab.setValue("Discount", 0)
            mTab.setValue("PlannedMarginAmt", 0);
            mTab.setValue("PlannedAmt", 0);
            mTab.setValue("ProductType", "");
            this.setCalloutActive(false);
            return "";
        }

        var OpportunityID = Util.getValueOfInt(mTab.getValue("VAS_Opportunity_ID"));
        var productID = Util.getValueOfInt(mTab.getValue("M_Product_ID"));

        var attributeID = 0;
        var uomID = 0;
        //get price on the basis of Attribute and UOM if selected
        if (mTab.findColumn("M_AttributeSetInstance_ID") > 0) {
            attributeID = Util.getValueOfInt(mTab.getValue("M_AttributeSetInstance_ID"));
        }
        if (mTab.findColumn("C_UOM_ID") > 0) {
            uomID = Util.getValueOfInt(mTab.getValue("C_UOM_ID"));
        }

        //(VAI094): Fill Product Type on selection of Product
        var type = VIS.dataContext.getJSONRecord("MProject/GetProductType", productID.toString());
        mTab.setValue("ProductType", type);

        var paramString = OpportunityID.toString() + "," + productID.toString() + "," + attributeID.toString() + "," + uomID.toString();
        var dr = VIS.dataContext.getJSONRecord("MProject/GetVASProjectDetail", paramString);
        if (dr != null) {
            var PriceList = Util.getValueOfDecimal(dr["PriceList"]);
            mTab.setValue("PriceList", PriceList);

            var PriceStd = Util.getValueOfDecimal(dr["PriceStd"]);
            mTab.setValue("PlannedPrice", PriceStd);
            mTab.setValue("PlannedQty", 1);
            var PriceLimit = Util.getValueOfDecimal(dr["PriceLimit"]);
            mTab.setValue("PriceLimit", PriceLimit)
            var discount;
            try {

                discount = ((PriceList - PriceStd) * 100) / PriceList;
                if (isNaN(discount)) {
                    this.setCalloutActive(false);
                    return VIS.Msg.getMsg("PriceNotDefined");
                }
            }
            catch (err) {
                this.setCalloutActive(false);
                return "PriceListNotSelected";
            }

            mTab.setValue("Discount", discount.toFixed(2));
            // oppLine.SetDiscount(Decimal.Subtract(PriceList ,PriceStd));

            mTab.setValue("PlannedMarginAmt", (PriceStd - PriceLimit));
            // oppLine.SetPlannedMarginAmt( Decimal.Subtract(PriceStd, PriceLimit));

            //set base UOM if not selected
            if (Util.getValueOfInt(mTab.getValue("C_UOM_ID")) == 0) {
                mTab.setValue("C_UOM_ID", dr["C_UOM_ID"]);
            }
        }
        else {
            //if no data found then set prices as 0
            mTab.setValue("PriceList", 0);
            mTab.setValue("PlannedPrice", 0);
            mTab.setValue("Discount", 0)
            mTab.setValue("PlannedMarginAmt", 0);
            mTab.setValue("PlannedAmt", 0);
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    /**
     * 
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     * @returns
     */
    CalloutVASOpportunity.prototype.Planned = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  

        if (value == null || value.toString() == "") {
            return "";
        }
        if (this.isCalloutActive() || value == null) {
            return "";
        }
        this.setCalloutActive(true);

        var PlannedQty, PlannedPrice, PriceList, Discount;
        var RemainingMargin = 0;
        var StdPrecision = ctx.getStdPrecision();
        var PriceListPrecision = StdPrecision;
        var paramString = ctx.getContextAsInt(windowNo, "M_PriceList_Version_ID");
        if (paramString != 0) {
            var dr = VIS.dataContext.getJSONRecord("MProject/GetPriceListPrecision", paramString.toString());
            if (dr != null) {
                StdPrecision = Util.getValueOfInt(dr["StdPrecision"]);
                PriceListPrecision = Util.getValueOfInt(dr["PriceListPrecision"]);
            }
        }
        var OpportunityID = Util.getValueOfInt(mTab.getValue("VAS_Opportunity_ID"));
        var productID = Util.getValueOfInt(mTab.getValue("M_Product_ID"));

        var paramString = OpportunityID.toString() + "," + productID.toString();

        var PriceLimit = VIS.dataContext.getJSONRecord("MProject/GetVASProjectPriceLimit", paramString);

        PlannedQty = Util.getValueOfDecimal(mTab.getValue("PlannedQty"));
        if (PlannedQty == null) {
            PlannedQty = Envs.ONE;
        }

        PlannedPrice = Util.getValueOfDecimal(mTab.getValue("PlannedPrice"));
        if (PlannedPrice == null) {
            PlannedPrice = VIS.Env.ZERO;
        }
        else {
            PlannedPrice = PlannedPrice.toFixed(PriceListPrecision);
        }

        PriceList = Util.getValueOfDecimal(mTab.getValue("PriceList"));
        if (PriceList == null) {
            PriceList = PlannedPrice;
        }
        else {
            PriceList = PriceList.toFixed(PriceListPrecision);
        }

        Discount = Util.getValueOfDecimal(mTab.getValue("Discount"));
        if (Discount == null) {
            Discount = VIS.Env.ZERO;
        }
        else {
            Discount = Discount.toFixed(PriceListPrecision);
        }

        var columnName = mField.getColumnName();
        if (columnName == "PlannedPrice") {
            if (PriceList == 0) {
                Discount = VIS.Env.ZERO;
            }
            else {
                var multiplier = ((PlannedPrice * VIS.Env.ONEHUNDRED) /
                    PriceList).toFixed(10);//, MidpointRounding.AwayFromZero);
                Discount = (VIS.Env.ONEHUNDRED - multiplier);
            }
            mTab.setValue("Discount", Discount.toFixed(PriceListPrecision));
            mTab.setValue("PlannedPrice", PlannedPrice);
            this.log.fine("PriceList=" + PriceList + " - Discount=" + Discount
                + " -> [PlannedPrice=" + PlannedPrice + "] (Precision=" + PriceListPrecision + ")");
        }
        else if (columnName == "PriceList") {
            if (VIS.Env.signum(PriceList) == 0) {
                Discount = VIS.Env.ZERO;
            }
            else {
                var multiplier = ((PlannedPrice * VIS.Env.ONEHUNDRED) /
                    PriceList);//, MidpointRounding.AwayFromZero);
                Discount = VIS.Env.ONEHUNDRED - multiplier;
            }
            mTab.setValue("Discount", Discount.toFixed(PriceListPrecision));
            mTab.setValue("PriceList", PriceList);
            this.log.fine("[PriceList=" + PriceList + "] - Discount=" + Discount
                + " -> PlannedPrice=" + PlannedPrice + " (Precision=" + PriceListPrecision + ")");
        }
        else if (columnName == "Discount") {
            var multiplier = (Discount / VIS.Env.ONEHUNDRED).toFixed(10);//, MidpointRounding.AwayFromZero);

            multiplier = VIS.Env.ONE - multiplier;
            //
            PlannedPrice = PriceList * multiplier;
            if (Util.scale(PlannedPrice) > PriceListPrecision) {
                PlannedPrice = PlannedPrice.toFixed(PriceListPrecision);//, MidpointRounding.AwayFromZero);
            }
            mTab.setValue("PlannedPrice", PlannedPrice);
            mTab.setValue("Discount", Discount);
            this.log.fine("PriceList=" + PriceList + " - [Discount=" + Discount
                + "] -> PlannedPrice=" + PlannedPrice + " (Precision=" + PriceListPrecision + ")");
        }

        //	Calculate Amount
        var PlannedAmt = PlannedQty * PlannedPrice;
        if (Util.scale(PlannedAmt) > StdPrecision) {
            PlannedAmt = PlannedAmt.toFixed(StdPrecision);//, MidpointRounding.AwayFromZero);

        }

        // Calculate Planned Margin Amount after calculation of Palnned Amount, so that its to be accurate
        if (PlannedQty != null) {

            RemainingMargin = (Util.getValueOfDecimal(mTab.getValue("PlannedPrice")) - PriceLimit) * Util.getValueOfDecimal(mTab.getValue("PlannedQty"));
            RemainingMargin = RemainingMargin.toFixed(StdPrecision);
        }

        this.log.fine("PlannedQty=" + PlannedQty + " * PlannedPrice=" + PlannedPrice + " -> PlannedAmt=" + PlannedAmt + " (Precision=" + StdPrecision + ")");
        mTab.setValue("PlannedAmt", PlannedAmt);
        mTab.setValue("PlannedMarginAmt", (RemainingMargin));
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VAS.Model = VAS.Model || {};
    VAS.Model.CalloutVASOpportunity = CalloutVASOpportunity;
    //*********** CalloutVASOpportunity End*************

})(VAS, jQuery);