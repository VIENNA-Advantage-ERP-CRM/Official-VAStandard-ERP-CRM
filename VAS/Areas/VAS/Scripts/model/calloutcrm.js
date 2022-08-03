; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    //*************CalloutProductToOpportunity Start***********
    function CalloutProductToOpportunity() {
        VIS.CalloutEngine.call(this, "VIS.CalloutProductToOpportunity");//must call
    };
    VIS.Utility.inheritPrototype(CalloutProductToOpportunity, VIS.CalloutEngine); //inherit prototype
    CalloutProductToOpportunity.prototype.ProductInfo = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value.toString() == "") {
            if (Util.getValueOfInt(mTab.getValue("M_Product_ID")) == 0) {
                //set prices and quantities 0 if product is deselected
                mTab.setValue("PriceList", 0);
                mTab.setValue("PlannedPrice", 0);
                mTab.setValue("Discount", 0)
                mTab.setValue("PlannedMarginAmt", 0);
                mTab.setValue("PlannedAmt", 0);
            }
            return "";
        }

        // because, this method is used to get product info based on Product ID
        if (Util.getValueOfInt(mTab.getValue("M_Product_ID")) == 0) {
            mTab.setValue("PriceList", 0);
            mTab.setValue("PlannedPrice", 0);
            mTab.setValue("Discount", 0)
            mTab.setValue("PlannedMarginAmt", 0);
            mTab.setValue("PlannedAmt", 0);
            return "";
        }

        // Added by Bharat on 23 May 2017 to remove client side queries
        var taskID = Util.getValueOfInt(mTab.getValue("C_ProjectTask_ID"));
        var phaseID = Util.getValueOfInt(mTab.getValue("C_ProjectPhase_ID"));
        var projID = Util.getValueOfInt(mTab.getValue("C_Project_ID"));
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
        //
        var paramString = taskID.toString() + "," + phaseID.toString() + "," + projID.toString() + "," + productID.toString() + "," + attributeID.toString() + "," + uomID.toString();

        var dr = VIS.dataContext.getJSONRecord("MProject/GetProjectDetail", paramString);
        if (dr != null) {
            var PriceList = Util.getValueOfDecimal(dr["PriceList"]);
            mTab.setValue("PriceList", PriceList);

            var PriceStd = Util.getValueOfDecimal(dr["PriceStd"]);
            mTab.setValue("PlannedPrice", PriceStd);
            mTab.setValue("PlannedQty", 1);
            var PriceLimit = Util.getValueOfDecimal(dr["PriceLimit"]);

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
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /**
     * Set BaseQuantity as per UOM
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutProductToOpportunity.prototype.SetQty = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            return "";
        }
        this.setCalloutActive(true);
        var C_UOM_ID = mTab.getValue("C_UOM_ID");
        var Qty = mTab.getValue("PlannedQty");
        if (mTab.getValue("M_Product_ID") != null) {
            var M_Product_ID = mTab.getValue("M_Product_ID");
            var paramStr = M_Product_ID.toString().concat(",", C_UOM_ID.toString(), ",", Qty.toString());
            var pc = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramStr);
            if (pc != null) {
                mTab.setValue("BaseQty", pc);
            }
            else {
                mTab.setValue("BaseQty", Qty);
            }
        }
        else {
            mTab.setValue("BaseQty", Qty);
        }

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    }

    VIS.Model.CalloutProductToOpportunity = CalloutProductToOpportunity;
    //**********CalloutProductToOpportunity End**************


    //*************CalloutPriceListOpp Start**************
    function CalloutPriceListOpp() {
        VIS.CalloutEngine.call(this, "VIS.CalloutPriceListOpp");//must call
    };
    VIS.Utility.inheritPrototype(CalloutPriceListOpp, VIS.CalloutEngine); //inherit prototype
    CalloutPriceListOpp.prototype.PriceList = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }
        var sql = "";
        try {
            //sql ="select M_PriceList_ID from M_PriceList_Version where M_PriceList_Version_ID = " + Util.getValueOfInt(value);
            //var M_PriceList_ID = Util.getValueOfInt(VIS.DB.executeScalar(sql, null, null));
            var ds = VIS.dataContext.getJSONRecord("MPriceList/GetM_PriceList", Util.getValueOfInt(value));
            if (ds != null && Object.keys(ds).length > 0) {
                if (ds.M_PriceList_ID != 0) {
                    mTab.setValue("M_PriceList_ID", ds.M_PriceList_ID);

                    //sql = "select C_Currency_id from m_pricelist where m_pricelist_ID = " + M_PriceList_ID;
                    //var C_Currency_ID = Util.getValueOfInt(VIS.DB.executeScalar(sql, null, null));
                    //var C_Currency_ID = VIS.dataContext.getJSONRecord("MPriceList/GetC_Currency", Util.getValueOfInt(M_PriceList_ID));
                    if (ds.C_Currency_id != 0) {
                        mTab.setValue("C_Currency_ID", ds.C_Currency_id);
                    }
                    else {
                        //  ShowMessage.Info("CurrencyNotDefinedForThePriceList", true, null, null);
                        VIS.ADialog.info("CurrencyNotDefinedForThePriceList");
                    }
                }
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.saveError(sql, sql);
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    VIS.Model.CalloutPriceListOpp = CalloutPriceListOpp;
    //**************CalloutPriceListOpp End*************


    //*************CalloutMCampaign Starts***********
    function CalloutMCampaign() {
        VIS.CalloutEngine.call(this, "VIS.CalloutMCampaign");//must call
    };
    VIS.Utility.inheritPrototype(CalloutMCampaign, VIS.CalloutEngine); //inherit prototype


    CalloutMCampaign.prototype.DateRequired = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }

        this.setCalloutActive(true);
        try {
            var DateDoc, DateReq;
            DateDoc = new Date(mTab.getValue("DateContract"));
            DateReq = new Date(mTab.getValue("DateFinish"));
            if (DateReq.toISOString() < DateDoc.toISOString()) {
                mTab.setValue("DateFinish", "");
                this.setCalloutActive(false);
                VIS.ADialog.info("DateInvalid", null, "", "");
            }
            this.log.fine("DateFinish=" + DateReq);
        }
        catch (err) {
            VIS.ADialog.info("DateError" + err, null, "", "");
            this.setCalloutActive(false);
            return err;
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutMCampaign = CalloutMCampaign;
    //*************CalloutMCampaign Ends*************


    //*********** CalloutLead Start ****
    function CalloutLead() {
        VIS.CalloutEngine.call(this, "VIS.CalloutLead"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutLead, VIS.CalloutEngine);//inherit CalloutEngine

    /**
     * Set full name on Lead window
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutLead.prototype.SetFullName = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if ((this.isCalloutActive() || value == null || value.toString() == "") && (mField.getColumnName() != "LastName")) {
            return "";
        }
        this.setCalloutActive(true);
        var fName = "";
        if (mTab.getValue("Firstname") != null) {
            fName = mTab.getValue("Firstname");
        }
        if (mTab.getValue("LastName") != null && mTab.getValue("LastName") != "") {
            if (fName == "")
                fName = mTab.getValue("LastName");
            else
                fName = fName + " " + mTab.getValue("LastName");
        }
        if (fName != "") {
            mTab.setValue("ContactName", fName);
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    VIS.Model.CalloutLead = CalloutLead;
    //**************CalloutLead End*************


})(VIS, jQuery);