/** 
  *    Sample Class for Callout
       -  must call base class (CalloutEngine)
       -- must inheirt Base class
  */


; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;
    var steps = false;
    var countEd011 = 0;


    //*************CalloutDocumentType Starts***********
    function CalloutDocumentType() {
        VIS.CalloutEngine.call(this, "VIS.CalloutDocumentType");//must call
    };
    VIS.Utility.inheritPrototype(CalloutDocumentType, VIS.CalloutEngine); //inherit prototype


    CalloutDocumentType.prototype.SetSalesQuotation = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if ((this.isCalloutActive()) || value == null || value.toString() == "") {
            return "";
        }
        try {
            this.setCalloutActive(true);
            if (Util.getValueOfString(mTab.getValue("DocSubTypeSO")) == 'OB' || Util.getValueOfString(mTab.getValue("DocSubTypeSO")) == 'ON') {
                mTab.setValue("IsSalesQuotation", true);
            }
            else {
                mTab.setValue("IsSalesQuotation", false);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            return err;
        }
        this.setCalloutActive(false);
        return "";
    }

    // JID_0811 
    // Added by Bharat on 24 August 2018 on change of Organization set Warehouse from Organization Info
    CalloutDocumentType.prototype.Organization = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if ((this.isCalloutActive()) || value == null || value.toString() == "") {
            return "";
        }
        try {
            this.setCalloutActive(true);
            var colName = "";
            if (mTab.getField("M_Warehouse_ID") != null) {
                var warehouse_ID = VIS.dataContext.getJSONRecord("MDocType/GetWarehouse", value.toString());
                if (warehouse_ID > 0) {
                    if (mTab.getTableName() == "M_Movement" && mTab.getField("DTD001_MWarehouseSource_ID") != null) {
                        mTab.setValue("DTD001_MWarehouseSource_ID", warehouse_ID);
                    }
                    else {
                        mTab.setValue("M_Warehouse_ID", warehouse_ID);
                    }
                }
                else {
                    //if get value < 0 then set as Zero.
                    if (mTab.getTableName() == "M_Movement" && mTab.getField("DTD001_MWarehouseSource_ID") != null) {
                        mTab.setValue("DTD001_MWarehouseSource_ID", 0);
                    }
                    else {
                        mTab.setValue("M_Warehouse_ID", 0);
                    }
                }
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            return err;
        }
        this.setCalloutActive(false);
        return "";
    }
    VIS.Model.CalloutDocumentType = CalloutDocumentType;
    //*************CalloutDocumentType Ends*************
   

    /*  Callout Recurring*******************Added by Arpit Rai on 2nd Jan,2016  */
    function CalloutRecurring() {
        VIS.CalloutEngine.call(this, "VIS.CalloutRecurring"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutRecurring, VIS.CalloutEngine);//inherit CalloutEngine

    //Callout To Set Remaning Runs Zero while editing the Maximum Runs
    CalloutRecurring.prototype.SetMaxRunZero = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var maxRun = mTab.getValue("RunsMax");
        if (maxRun != "" && maxRun > 0) {
            mTab.setValue("RunsRemaining", 0);
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    }
    VIS.Model.CalloutRecurring = CalloutRecurring;    


    /*  Callout CalloutProfitLoss**********Added on 05 December, 2017 By SUkhwinder */
    function CalloutProfitLoss() {
        VIS.CalloutEngine.call(this, "VIS.CalloutProfitLoss"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutProfitLoss, VIS.CalloutEngine);//inherit CalloutEngine

    //Callout To Set Datefrom and DateTo from selected year.(Sukhwinder on 7th Dec)
    CalloutProfitLoss.prototype.SetDateFromAndDateTo = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        try {
            var paramString = value.toString() + "," + ctx.getAD_Client_ID().toString();
            //var adClientID = ctx.getAD_Client_ID();
            //var yearID = mTab.getValue("C_Year_ID");

            var dr = VIS.dataContext.getJSONRecord("Common/GetPeriodFromYear", paramString);
            if (dr != null) {
                var fromDate = Util.getValueOfDate(dr["STARTDATE"]);
                var toDate = Util.getValueOfDate(dr["ENDDATE"]);

                mTab.setValue("DateFrom", fromDate);
                mTab.setValue("DateTo", toDate);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    }


    //Callout To Set Currencyfrom selected acconting schema.(Sukhwinder on 12th Dec)
    CalloutProfitLoss.prototype.SetCurrency = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        try {
            var paramString = value.toString() + "," + ctx.getAD_Client_ID().toString();
            //var adClientID = ctx.getAD_Client_ID();
            //var yearID = mTab.getValue("C_Year_ID");

            var dr = VIS.dataContext.getJSONRecord("Common/GetCurrencyFromAccountingSchema", paramString);
            if (dr != null) {
                var C_Currency_ID = Util.getValueOfInt(dr["CurrencyID"]);

                mTab.setValue("C_Currency_ID", C_Currency_ID);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    }

    VIS.Model.CalloutProfitLoss = CalloutProfitLoss;


    //  ****Callout Profit Tax Start*******************Added by Vikas  29-dec*********************************88
    function CalloutProfitTax() {
        VIS.CalloutEngine.call(this, "VIS.CalloutProfitTax"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutProfitTax, VIS.CalloutEngine);//inherit CalloutEngine

    /// <param name="ctx">context</param>
    /// <param name="windowNo">current Window No</param>
    /// <param name="mTab">Grid Tab</param>
    /// <param name="mField">Gridfield</param>
    /// <param name="value">New Value</param>
    /// <returns>null or error message</returns>
    CalloutProfitTax.prototype.SetProfitBeforeTax = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value.toString() == "") {
            return "";
        }
        if (this.isCalloutActive() || value == null) {
            return "";
        }

        this.setCalloutActive(true);
        try {
            var idr = VIS.dataContext.getJSONRecord("MProfitTax/GetProfitLossDetails", value.toString());
            if (Object.keys(idr).length > 0) {
                mTab.setValue("ProfitBeforeTax", Util.getValueOfDecimal(idr["ProfitBeforeTax"]));
                mTab.setValue("C_Year_ID", Util.getValueOfInt(idr["C_Year_ID"]));
                mTab.setValue("C_ProfitAndLoss_ID", Util.getValueOfInt(idr["C_ProfitAndLoss_ID"]));
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    }
    VIS.Model.CalloutProfitTax = CalloutProfitTax;
    //********************* Callout Profit Tax Start End ***************************************************************


    //et multiply rate 1 in case of same UOM in both the columns.
    function CalloutUOMConversion() {
        VIS.CalloutEngine.call(this, "VIS.CalloutUOMConversion");//must call
    };
    VIS.Utility.inheritPrototype(CalloutUOMConversion, VIS.CalloutEngine); //inherit prototype
    CalloutUOMConversion.prototype.SetMultiplyRate = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            mTab.setValue("MultiplyRate", 0);
            return "";
        }
        this.setCalloutActive(true);
        var fromUOM = mTab.getValue("C_UOM_ID");
        var toUOM = mTab.getValue("C_UOM_To_ID");
        if (fromUOM == toUOM) {
            mTab.setValue("DivideRate", 1);
            mTab.setValue("MultiplyRate", 1);
        }
        this.setCalloutActive(false);
        return "";
    }
    VIS.Model.CalloutUOMConversion = CalloutUOMConversion;


    //to check inco term reference changed

    function CalloutIncoTerm() {
        VIS.CalloutEngine.call(this, "VIS.CalloutIncoTerm");//must call
    };

    VIS.Utility.inheritPrototype(CalloutIncoTerm, VIS.CalloutEngine); //inherit prototype

    CalloutIncoTerm.prototype.CheckIncoTerm = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var isSOTrx = ctx.isSOTrx(windowNo);
        var recordID = 0;
        var incoTerm = 0;
        var paramString = "";
        try {
            if (mTab.getTableName().startsWith("C_Order")) {
                if (isSOTrx && Util.getValueOfInt(mTab.getValue("C_Order_Quotation")) > 0) {
                    paramString = isSOTrx + "," + mTab.getTableName() + ",C_Order_Quotation," + Util.getValueOfInt(mTab.getValue("C_Order_Quotation")).toString();
                    incoTerm = VIS.dataContext.getJSONRecord("MOrder/GetIncoTerm", paramString);
                }
                else if (!isSOTrx && Util.getValueOfInt(mTab.getValue("Ref_Order_ID")) > 0) {
                    paramString = isSOTrx + "," + mTab.getTableName() + ",Ref_Order_ID," + Util.getValueOfInt(mTab.getValue("Ref_Order_ID")).toString();
                    incoTerm = VIS.dataContext.getJSONRecord("MOrder/GetIncoTerm", paramString);
                }
                else if (Util.getValueOfInt(mTab.getValue("C_Order_Blanket")) > 0) {
                    paramString = isSOTrx + "," + mTab.getTableName() + ",C_Order_Blanket," + Util.getValueOfInt(mTab.getValue("C_Order_Blanket")).toString();
                    incoTerm = VIS.dataContext.getJSONRecord("MOrder/GetIncoTerm", paramString);
                }
            }
            else if (mTab.getTableName().startsWith("C_Invoice")) {
                if (isSOTrx && Util.getValueOfInt(mTab.getValue("C_Order_ID")) > 0) {
                    paramString = isSOTrx + "," + mTab.getTableName() + ",C_Order_ID," + Util.getValueOfInt(mTab.getValue("C_Order_ID")).toString();
                    incoTerm = VIS.dataContext.getJSONRecord("MOrder/GetIncoTerm", paramString);
                }
                else if (!isSOTrx && Util.getValueOfInt(mTab.getValue("C_Order_ID")) > 0) {
                    paramString = isSOTrx + "," + mTab.getTableName() + ",C_Order_ID," + Util.getValueOfInt(mTab.getValue("C_Order_ID")).toString();
                    incoTerm = VIS.dataContext.getJSONRecord("MOrder/GetIncoTerm", paramString);
                }
                else if (!isSOTrx && Util.getValueOfInt(mTab.getValue("M_InOut_ID")) > 0) {
                    paramString = isSOTrx + "," + mTab.getTableName() + ",M_InOut_ID," + Util.getValueOfInt(mTab.getValue("M_InOut_ID")).toString();
                    incoTerm = VIS.dataContext.getJSONRecord("MOrder/GetIncoTerm", paramString);
                }
            }
            else if (mTab.getTableName().startsWith("M_InOut")) {
                if (isSOTrx && Util.getValueOfInt(mTab.getValue("C_Order_ID")) > 0) {
                    paramString = isSOTrx + "," + mTab.getTableName() + ",C_Order_ID," + Util.getValueOfInt(mTab.getValue("C_Order_ID")).toString();
                    incoTerm = VIS.dataContext.getJSONRecord("MOrder/GetIncoTerm", paramString);
                }
                else if (!isSOTrx && Util.getValueOfInt(mTab.getValue("C_Order_ID")) > 0) {
                    paramString = isSOTrx + "," + mTab.getTableName() + ",C_Order_ID," + Util.getValueOfInt(mTab.getValue("C_Order_ID")).toString();
                    incoTerm = VIS.dataContext.getJSONRecord("MOrder/GetIncoTerm", paramString);
                }
                else if (!isSOTrx && Util.getValueOfInt(mTab.getValue("C_Invoice_ID")) > 0) {
                    paramString = isSOTrx + "," + mTab.getTableName() + ",C_Invoice_ID," + Util.getValueOfInt(mTab.getValue("C_Invoice_ID")).toString();
                    incoTerm = VIS.dataContext.getJSONRecord("MOrder/GetIncoTerm", paramString);
                }
            }
            if (incoTerm > 0 & incoTerm != value) {
                this.setCalloutActive(false);
                VIS.ADialog.warn("IncoTermChanged");
                return "";
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        this.setCalloutActive(false);
        return "";
    }

    VIS.Model.CalloutIncoTerm = CalloutIncoTerm;


    //Neha - On change of Warehouse or Locator, make parent container and Locator as null.
    function CalloutLocatorPC() {
        VIS.CalloutEngine.call(this, "VIS.CalloutLocatorPC");//must call
    };
    VIS.Utility.inheritPrototype(CalloutLocatorPC, VIS.CalloutEngine); //inherit prototype
    CalloutLocatorPC.prototype.SetLocatorPC = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        if (mField.getColumnName() == "M_Warehouse_ID") {
            if (value != oldValue) {
                mTab.setValue("M_Locator_ID", "");
                mTab.setValue("Ref_M_Container_ID", "");
            }
        }
        else
            if (mField.getColumnName() == "M_Locator_ID") {
                if (value != oldValue) {
                    mTab.setValue("Ref_M_Container_ID", "");
                }
            }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    }

    VIS.Model.CalloutLocatorPC = CalloutLocatorPC;


    // Callout on selection of Product to Remove Attribute Set Instance if selected.
    function CalloutLandedCost() {
        VIS.CalloutEngine.call(this, "VIS.CalloutLandedCost");//must call
    };
    VIS.Utility.inheritPrototype(CalloutLandedCost, VIS.CalloutEngine); //inherit prototype
    CalloutLandedCost.prototype.Product = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }

        this.setCalloutActive(true);
        if (mTab.getField("M_AttributeSetInstance_ID") != null) {
            mTab.setValue("M_AttributeSetInstance_ID", null);
        }
        this.setCalloutActive(false);
        return "";
    }
    VIS.Model.CalloutLandedCost = CalloutLandedCost;

    // In RFQ window, workcompletedate Should be greater than workstartdate
    function CalloutRFQ() {
        VIS.CalloutEngine.call(this, "VIS.CalloutRFQ");//must call
    };
    VIS.Utility.inheritPrototype(CalloutRFQ, VIS.CalloutEngine); //inherit prototype
    CalloutRFQ.prototype.Comparedates = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var _startDate = new Date(mTab.getValue("DateWorkStart"));
        var _endDate = new Date(mTab.getValue("DateWorkComplete"));
        if (mField.getColumnName() == "DateWorkStart") {
            if (_startDate >= _endDate && mTab.getValue("DateWorkComplete") != null) {
                mTab.setValue("DateWorkStart", "");
                this.setCalloutActive(false);
                return VIS.ADialog.info("DateWorkGreater");
            }
        }
        else {
            if (_startDate >= _endDate && mTab.getValue("DateWorkStart") != null) {
                mTab.setValue("DateWorkComplete", "");
                this.setCalloutActive(false);
                return VIS.ADialog.info("DateWorkGreater");
            }
        }
        this.setCalloutActive(false);
        return "";
    }
    VIS.Model.CalloutRFQ = CalloutRFQ;

    //** clearing WeekDay value when FixDueDate checkbox is true ** Dt: 02/04/2021 ** Modified By: Kumar ** //
    //*************CalloutPaymentTerm Start**************
    function CalloutPaymentTerm() {
        VIS.CalloutEngine.call(this, "VIS.CalloutPaymentTerm");//must call
    };
    VIS.Utility.inheritPrototype(CalloutPaymentTerm, VIS.CalloutEngine); //inherit prototype
    CalloutPaymentTerm.prototype.ClearWeekDay = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        if (value == true) {
            mTab.setValue("NetDay", "");
            mTab.setValue("WeekOffset", VIS.Env.ZERO);
        }

        this.setCalloutActive(false);
        return "";
    };
    
    VIS.Model.CalloutPaymentTerm = CalloutPaymentTerm;
    //**************CalloutPaymentTerm End*************


})(VIS, jQuery);
