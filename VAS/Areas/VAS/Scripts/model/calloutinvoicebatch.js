; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    function CalloutInvoiceBatch() {
        VIS.CalloutEngine.call(this, "VIS.CalloutInvoiceBatch"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutInvoiceBatch, VIS.CalloutEngine);//inherit CalloutEngine

    //private static VLogger _log = VLogger.GetVLogger(typeof(CalloutInvoiceBatch).FullName);  //Sarab
    /// <summary>
    ///	Invoice Batch Line - DateInvoiced.	- updates DateAcct
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">window no</param>
    /// <param name="mTab"> tab</param>
    /// <param name="mField">field</param>
    /// <param name="value">value</param>
    /// <returns>null or error message</returns>
    CalloutInvoiceBatch.prototype.Date = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }
        mTab.setValue("DateAcct", value);
        //
        this.SetDocumentNo(ctx, windowNo, mTab);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    //	date

    /// <summary>
    ///  Invoice Batch Line - BPartner.
    //		- C_BPartner_Location_ID
    //		- AD_User_ID
    //		- PaymentRule
    //		- C_PaymentTerm_ID
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">window no</param>
    /// <param name="mTab">tab</param>
    /// <param name="mField">field</param>
    /// <param name="value">value</param>
    /// <returns>null or error message</returns>
    CalloutInvoiceBatch.prototype.BPartner = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  

        if (value == null || value.toString() == "") {
            return "";
        }
        var C_BPartner_ID = Util.getValueOfInt(value);
        if (C_BPartner_ID == null || C_BPartner_ID == 0) {
            return "";
        }
        var IsSOTrx = ctx.isSOTrx();
        var idr = null;
        try {
            var paramString = true.toString() + "," + C_BPartner_ID.toString();
            idr = VIS.dataContext.getJSONRecord("MBPartner/GetBPartnerData", paramString);

            //	PaymentRule
            if (idr != null) {
                var s = Util.getValueOfString(idr[(IsSOTrx ? "PaymentRule" : "PaymentRulePO")]);
                if (s != null && s.length != 0) {
                    if (ctx.getContext("DocBaseType").endsWith("C"))// .endsWith("C"))	//	Credits are Payment Term
                    {
                        s = "P";
                    }
                    else if (this.isSOTrx && (s == "S") || (s == "U"))	//	No Check/Transfer for SO_Trx
                    {
                        s = "P";											//  Payment Term
                    }
                }
                //  Payment Term
                var ii = Util.getValueOfInt(idr[(IsSOTrx ? "C_PaymentTerm_ID" : "PO_PaymentTerm_ID")]);
                if (ii > 0) {
                    mTab.setValue("C_PaymentTerm_ID", ii);
                }
                //	Location
                var locID = Util.getValueOfInt(idr["C_BPartner_Location_ID"]);
                //	overwritten by InfoBP selection - works only if InfoWindow
                //	was used otherwise creates error (uses last value, may belong to differnt BP)
                if (C_BPartner_ID.toString().equals(ctx.getContext("C_BPartner_ID"))) {
                    var loc = ctx.getContext("C_BPartner_Location_ID");
                    if (loc.toString().length > 0) {
                        locID = Util.getValueOfInt(loc);
                    }
                }
                if (locID == 0) {
                    mTab.setValue("C_BPartner_Location_ID", null);
                }
                else {
                    mTab.setValue("C_BPartner_Location_ID", Util.getValueOfInt(locID));
                }

                //	Contact - overwritten by InfoBP selection
                var contID = Util.getValueOfInt(idr["AD_User_ID"]);
                if (C_BPartner_ID.toString().equals(ctx.getContext("C_BPartner_ID"))) {
                    var cont = ctx.getContext("AD_User_ID");
                    if (cont.toString().length > 0) {
                        contID = Util.getValueOfInt(cont);
                    }
                }
                if (contID == 0) {
                    mTab.setValue("AD_User_ID", null);
                }
                else {
                    mTab.setValue("AD_User_ID", Util.getValueOfInt(contID));
                }
                //	CreditAvailable
                if (IsSOTrx) {
                    var CreditLimit = Util.getValueOfDouble(idr["SO_CreditLimit"]);
                    if (CreditLimit != 0) {
                        var CreditAvailable = Util.getValueOfDouble(idr["CreditAvailable"]);
                        if (idr == null && CreditAvailable < 0) {
                            VIS.ADialog.info("CreditLimitOver");
                        }
                    }
                }
            }

        }
        catch (err) {
            this.setCalloutActive(false);
            return err.message;
        }
        //
        this.SetDocumentNo(ctx, windowNo, mTab);
        oldValue = null;
        return this.Tax(ctx, windowNo, mTab, mField, value);
    };//	bPartner

    /// <summary>
    /// Document Type.
    //- called from DocType
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">window no</param>
    /// <param name="mTab"> tab</param>
    /// <param name="mField">field</param>
    /// <param name="value">value</param>
    /// <returns>null or error message</returns>
    CalloutInvoiceBatch.prototype.DocType = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        this.SetDocumentNo(ctx, windowNo, mTab);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };	//	docType

    /// <summary>
    /// Set Document No (increase existing)
    /// </summary>
    /// <param name="ctx"> Context</param>
    /// <param name="windowNo">current Window No</param>
    /// <param name="mTab"> Model Tab</param>
    CalloutInvoiceBatch.prototype.SetDocumentNo = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //	Get last line
        //  
        var C_InvoiceBatch_ID = ctx.getContextAsInt(windowNo, "C_InvoiceBatch_ID");

        var paramString = C_InvoiceBatch_ID.toString();

        //Get product price information
        var dr = null;
        dr = VIS.dataContext.getJSONRecord("MInvoiceBatchLine/GetInvoiceBatchLineDetail", paramString);
        if (dr == null) {
            return;
        }

        //	Need to Increase when different DocType or BP
        var C_DocType_ID = ctx.getContextAsInt(windowNo, "C_DocType_ID");
        var C_BPartner_ID = ctx.getContextAsInt(windowNo, "C_BPartner_ID");

        if (C_DocType_ID == dr["C_DocType_ID"]
            && C_BPartner_ID == dr["C_BPartner_ID"]) {
            return;
        }
        //	New Number
        var oldDocNo = dr["DocumentNo"];
        if (oldDocNo == null) {
            return;
        }
        var docNo = 0;
        docNo = Util.getValueOfInt(oldDocNo);
        if (docNo == 0) {
            return;
        }
        var newDocNo = Util.getValueOfString(docNo + 1);
        mTab.setValue("DocumentNo", newDocNo);
        ctx = windowNo = mTab = mField = value = oldValue = null;
    };	//	setDocumentNo

    /// <summary>
    /// Invoice Batch Line - Charge.
    //	- updates PriceEntered from Charge
    //  Calles tax
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">window no</param>
    /// <param name="mTab">tab</param>
    /// <param name="mField">field</param>
    /// <param name="value">value</param>
    /// <returns>null or error message</returns>
    CalloutInvoiceBatch.prototype.Charge = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }
        var C_Charge_ID = Util.getValueOfInt(value);
        if (C_Charge_ID == null || Util.getValueOfInt(C_Charge_ID) == 0) {
            return "";
        }

        try {
            var chargeAmt = VIS.dataContext.getJSONRecord("MCharge/GetCharge", C_Charge_ID.toString());
            mTab.setValue("PriceEntered", Util.getValueOfDecimal(chargeAmt));
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.log(Level.severe, null, err);
            return err.message;
        }
        //
        oldValue = null;
        return this.Tax(ctx, windowNo, mTab, mField, value);
    };	//	charge
    /// <summary>
    ///   Invoice Line - Tax.
    //		- basis: Charge, BPartner Location
    //		- sets C_Tax_ID
    // Calles Amount
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">window no</param>
    /// <param name="mTab"> tab</param>
    /// <param name="mField">field</param>
    /// <param name="value">value</param>
    /// <returns>null or error message</returns>
    CalloutInvoiceBatch.prototype.Tax = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        var column = mField.getColumnName();
        if (value == null || value.toString() == "") {
            return "";
        }
        var C_Charge_ID = 0;
        if (column.equals("C_Charge_ID")) {
            C_Charge_ID = Util.getValueOfInt(value);
        }
        else {
            C_Charge_ID = ctx.getContextAsInt(windowNo, "C_Charge_ID");
        }
        this.log.fine("C_Charge_ID=" + C_Charge_ID);
        if (C_Charge_ID == 0) {
            return this.Amt(ctx, windowNo, mTab, mField, value);	//
        }
        var C_BPartner_Location_ID = ctx.getContextAsInt(windowNo, "C_BPartner_Location_ID");
        if (C_BPartner_Location_ID == 0) {
            return this.Amt(ctx, windowNo, mTab, mField, value);	//
        }
        this.log.fine("BP_Location=" + C_BPartner_Location_ID);

        //	Dates
        //var billDate = CommonFunctions.CovertMilliToDate(ctx.getContextAsTime(windowNo, "DateInvoiced"));//Sarab
        var billDate = Util.getValueOfDate(ctx.getContext("DateInvoiced"));
        this.log.fine("Bill Date=" + billDate);
        var shipDate = billDate;
        this.log.fine("Ship Date=" + shipDate);

        var AD_Org_ID = ctx.getContextAsInt(windowNo, "AD_Org_ID");
        this.log.fine("Org=" + AD_Org_ID);

        var M_Warehouse_ID = ctx.getContextAsInt("#M_Warehouse_ID");
        this.log.fine("Warehouse=" + M_Warehouse_ID);

        var paramString = C_Charge_ID.toString().concat(",", billDate.toString(),
            shipDate.toString(), ",",
            AD_Org_ID.toString(), ",",
            M_Warehouse_ID.toString(), ",",
            C_BPartner_Location_ID.toString(), ",",
            C_BPartner_Location_ID.toString(), ",",
            ctx.getWindowContext(windowNo, "IsSOTrx", true).equals("Y"));


        var C_Tax_ID = VIS.dataContext.getJSONRecord("MTax/Get", paramString);

        //var C_Tax_ID = VAdvantage.Model.Tax.Get(ctx, 0, C_Charge_ID, billDate, shipDate,
        //AD_Org_ID, M_Warehouse_ID, C_BPartner_Location_ID, C_BPartner_Location_ID,
        // ctx.getContext("IsSOTrx").equals("Y"));



        if (C_Tax_ID == 0) {

            // ShowMessage.Error(VLogger.RetrieveError().toString(), true);
        }
        else {
            mTab.setValue("C_Tax_ID", Util.getValueOfInt(C_Tax_ID));
        }
        // ctx = windowNo = mTab = mField = value = oldValue = null;
        return this.Amt(ctx, windowNo, mTab, mField, value);
    };

    CalloutInvoiceBatch.prototype.Amt = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }

        this.setCalloutActive(true);

        var StdPrecision = ctx.getStdPrecision();

        //	get values
        var QtyEntered = Util.getValueOfDecimal(mTab.getValue("QtyEntered"));
        var PriceEntered = Util.getValueOfDecimal(mTab.getValue("PriceEntered"));
        this.log.fine("QtyEntered=" + QtyEntered + ", PriceEntered=" + PriceEntered);
        if (QtyEntered == null) {
            QtyEntered = VIS.Env.ZERO;
        }
        if (PriceEntered == null) {
            PriceEntered = VIS.Env.ZERO;
        }

        //	Line Net Amt
        var LineNetAmt = QtyEntered * PriceEntered;
        if (Util.scale(LineNetAmt) > StdPrecision) {
            LineNetAmt = LineNetAmt.toFixed(StdPrecision);//, MidpointRounding.AwayFromZero);
        }

        //	Calculate Tax Amount
        var IsTaxIncluded = "Y" == ctx.getContext("IsTaxIncluded");

        var TaxAmt = null;
        if (mField.getColumnName().equals("TaxAmt")) {
            TaxAmt = mTab.getValue("TaxAmt");
        }
        else {
            var taxID = mTab.getValue("C_Tax_ID");
            if (taxID != null) {
                var C_Tax_ID = Util.getValueOfInt(taxID);

                //
                var paramString = C_Tax_ID.toString().concat(",", LineNetAmt.toString(), ",", //2
                    IsTaxIncluded.toString(), ",", //3
                    StdPrecision.toString() //4 
                ); //7          
                var dr = null;
                TaxAmt = VIS.dataContext.getJSONRecord("MTax/CalculateTax", paramString);
                mTab.setValue("TaxAmt", TaxAmt);

                // Set Surcharge Amount to zero
                if (mTab.getField("SurchargeAmt") != null) {
                    mTab.setValue("SurchargeAmt", 0);
                }
            }
        }

        //	
        if (IsTaxIncluded) {
            mTab.setValue("LineTotalAmt", LineNetAmt);
            mTab.setValue("LineNetAmt", (Util.getValueOfDecimal(LineNetAmt) * Util.getValueOfDecimal(TaxAmt)));
        }
        else {
            mTab.setValue("LineNetAmt", LineNetAmt);
            mTab.setValue("LineTotalAmt", (Util.getValueOfDecimal(LineNetAmt) + Util.getValueOfDecimal(TaxAmt)));

        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutInvoiceBatch = CalloutInvoiceBatch;

})(VIS, jQuery);