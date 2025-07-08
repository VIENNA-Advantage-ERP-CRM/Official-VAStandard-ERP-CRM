; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    //**************CalloutTax Start**********
    function CalloutTax() {
        VIS.CalloutEngine.call(this, "VIS.CalloutTax");//must call
    };
    VIS.Utility.inheritPrototype(CalloutTax, VIS.CalloutEngine); //inherit prototype
    CalloutTax.prototype.Tax = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value == 0 || value.toString() == "") {
            return "";
        }
        var C_Tax_ID = 0;
        var Rate = VIS.Env.ZERO;
        var LineAmount = "";
        var TotalRate = VIS.Env.ZERO;
        C_Tax_ID = Util.getValueOfInt(mTab.getValue("C_Tax_ID"));
        //var sqltax = "select rate from c_tax WHERE c_tax_id=" + C_Tax_ID + "";
        //Rate = Util.getValueOfDecimal(VIS.DB.executeScalar(sqltax, null, null));        

        // if Surcharge Tax is selected on Tax Rate, calculate surcharge tax amount accordingly
        if (mTab.getField("SurchargeAmt") != null) {
            var StdPrecision = 0;
            var IsTaxIncluded = false;

            var currency = VIS.dataContext.getJSONRecord("MPriceList/GetPriceListData", Util.getValueOfInt(mTab.getValue("M_PriceList_ID")).toString());
            if (currency != null) {
                StdPrecision = currency["StdPrecision"];
                IsTaxIncluded = "Y" == currency["IsTaxIncluded"];
            }

            var LineNetAmt = Util.getValueOfDecimal(mTab.getValue("LineNetAmt"));

            var dr = VIS.dataContext.getJSONRecord("MTax/CalculateSurcharge", C_Tax_ID.toString() + "," + LineNetAmt.toString() + "," + StdPrecision.toString()
                + "," + IsTaxIncluded.toString());

            TotalRate = dr["TaxAmt"];
            mTab.setValue("TaxAmt", TotalRate);
            mTab.setValue("SurchargeAmt", dr["SurchargeAmt"]);

            if (!IsTaxIncluded) {
                mTab.setValue("GrandTotal", (LineNetAmt + TotalRate + dr["SurchargeAmt"]));
            }
            else {
                mTab.setValue("GrandTotal", LineNetAmt);
            }
        }
        else {
            // JID_0872: Grand Total is not calculating right
            Rate = VIS.dataContext.getJSONRecord("MTax/GetTaxRate", C_Tax_ID.toString());
            var LineNetAmt = Util.getValueOfDecimal(mTab.getValue("LineNetAmt"));
            TotalRate = Util.getValueOfDecimal((LineNetAmt * Rate) / 100);

            TotalRate = Util.getValueOfDecimal(TotalRate.toFixed(2));

            mTab.setValue("GrandTotal", (TotalRate + LineNetAmt));
            mTab.setValue("taxamt", TotalRate);
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    /**
     *  VIS319  Set TaxExempt and TaxExemptReason AT Invoice Line and Ordert Line
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutTax.prototype.SetTaxExemptReason = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value == 0 || value.toString() == "" || this.isCalloutActive()) {
            return "";
        }
        try {
            this.setCalloutActive(true);
            //check whether window is of sales type
            var isSOTrx = ctx.getWindowContext(windowNo, "IsSOTrx", true) == "Y";
            var paramString = Util.getValueOfString(mTab.getValue("C_Tax_ID"));
            //Added parameter in order to handle the connditions
            if (mTab.getKeyColumnName() == "C_InvoiceLine_ID") {
                paramString = Util.getValueOfString(mTab.getValue("C_Tax_ID")) + "," + Util.getValueOfString(mTab.getValue("AD_Org_ID")) + ","
                    + Util.getValueOfString(mTab.getValue("C_Invoice_ID")) + "," + Util.getValueOfString(ctx.getWindowContext(windowNo, "C_BPartner_Location_ID"))
                    + "," + Util.getValueOfString(isSOTrx) + ", " + mTab.getKeyColumnName().toString();
            }
            else if (mTab.getKeyColumnName() == "C_OrderLine_ID") {
                paramString = Util.getValueOfString(mTab.getValue("C_Tax_ID")) + "," + Util.getValueOfString(mTab.getValue("AD_Org_ID")) + ","
                    + Util.getValueOfString(mTab.getValue("C_Order_ID")) + "," + Util.getValueOfString(ctx.getWindowContext(windowNo, "C_BPartner_Location_ID"))
                    + "," + Util.getValueOfString(isSOTrx) + ", " + mTab.getKeyColumnName().toString();
            }
            var data = VIS.dataContext.getJSONRecord("MTax/GetTaxExempt", paramString);

            if (data != null) {
                mTab.setValue("IsTaxExempt", Util.getValueOfString(data["IsTaxExempt"]).equals("Y") ? true : false);
                mTab.setValue("C_TaxExemptReason_ID", Util.getValueOfInt(data["C_TaxExemptReason_ID"]));
            }
            //if their is any message on change of tax then displayed in ui for sale type windows
            if (data["errorMsg"] != "" && isSOTrx) {
                VIS.ADialog.info("", "", data["errorMsg"]);
            }
        }
        catch (err) {
            this.log.log(Level.SEVERE, sql, err);
            this.setCalloutActive(false);
            return err.message;
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /**
     * VIS319_: When TaxExempt is true then Rate should be zero
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */

    CalloutTax.prototype.SetTaxRate = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value == 0 || value.toString() == "" || this.isCalloutActive()) {
            return "";
        }

        this.setCalloutActive(true);
        var taxexempt = mTab.getValue("IsTaxExempt");
        if (taxexempt) {
            mTab.setValue("Rate", 0);
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    }
    VIS.Model.CalloutTax = CalloutTax;
    //***********CalloutTax End *************

    //************CalloutTaxAmt Start***************
    function CalloutTaxAmt() {
        VIS.CalloutEngine.call(this, "VIS.CalloutTaxAmt");//must call
    };
    VIS.Utility.inheritPrototype(CalloutTaxAmt, VIS.CalloutEngine); //inherit prototype
    CalloutTaxAmt.prototype.TaxID = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            return "";
        }
        if (this.isCalloutActive()) {
            return "";
        }
        try {
            this.setCalloutActive(true);
            var rate = VIS.dataContext.getJSONRecord("MTax/GetTaxRate", Util.getValueOfInt(value));
            if (rate != 0) {
                var taxAmt = (Util.getValueOfDecimal(mTab.getValue("ApprovedExpenseAmt")) * rate) / 100;
                taxAmt = taxAmt.toFixed(2);
                mTab.setValue("TaxAmt", taxAmt);
            }
            else {
                mTab.setValue("TaxAmt", VIS.Env.ZERO);
            }
            this.setCalloutActive(false);
        }
        catch (err) {
            this.setCalloutActive(false);
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    CalloutTaxAmt.prototype.ExpenseAmt = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            return "";
        }
        if (this.isCalloutActive()) {
            return "";
        }
        try {
            this.setCalloutActive(true);
            var rate = VIS.dataContext.getJSONRecord("MTax/GetTaxRate", Util.getValueOfInt(mTab.getValue("C_Tax_ID")));
            if (rate != 0) {
                var taxAmt = (Util.getValueOfDecimal(value) * rate) / 100;
                taxAmt = taxAmt.toFixed(2);
                mTab.setValue("TaxAmt", taxAmt);
            }
            else {
                mTab.setValue("TaxAmt", VIS.Env.ZERO);
            }
            this.setCalloutActive(false);
        }
        catch (err) {
            this.setCalloutActive(false);
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutTaxAmt = CalloutTaxAmt;
    //*************CalloutTaxAmt End**************


    //**************CalloutTaxAmount Start*********************

    function CalloutTaxAmount() {
        VIS.CalloutEngine.call(this, "VIS.CalloutTaxAmount");//must call
    };
    VIS.Utility.inheritPrototype(CalloutTaxAmount, VIS.CalloutEngine);//inherit prototype
    // Method on payment window
    CalloutTaxAmount.prototype.PaymentTaxAmount = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        try {
            // get precision from currency
            var currency = VIS.dataContext.getJSONRecord("MCurrency/GetCurrency", mTab.getValue("C_Currency_ID").toString());
            var StdPrecision = currency["StdPrecision"];

            if (mField.getColumnName() == "C_Tax_ID") {
                //if (Util.getValueOfDecimal(mTab.getValue("PayAmt")) > 0) {
                var dr = null;
                // if Surcharge Tax is selected on Tax Rate, calculate surcharge tax amount accordingly
                if (mTab.getField("SurchargeAmt") != null) {
                    dr = VIS.dataContext.getJSONRecord("MTax/CalculateSurcharge", value.toString() + "," + mTab.getValue("PayAmt").toString() + "," + StdPrecision.toString());
                    mTab.setValue("TaxAmount", dr["TaxAmt"]);
                    mTab.setValue("SurchargeAmt", dr["SurchargeAmt"]);
                    this.setCalloutActive(false);
                    return "";
                }
                else {
                    dr = VIS.dataContext.getJSONRecord("MTax/GetTaxRate", value.toString());
                    var Rate = Util.getValueOfDecimal(dr);
                    if (Rate > 0) {
                        //Formula for caluculating Tax amount ==>  Amount - Amount / ((Rate / 100) + 1)
                        var TaxAmt = Util.getValueOfDecimal(Util.getValueOfDecimal(mTab.getValue("PayAmt")) - (Util.getValueOfDecimal(mTab.getValue("PayAmt")) / ((Rate / 100) + 1)));

                        // Round the amount according to currency precision
                        TaxAmt = Util.getValueOfDecimal(TaxAmt.toFixed(StdPrecision));
                        mTab.setValue("TaxAmount", TaxAmt);
                    }
                    else {
                        mTab.setValue("TaxAmount", 0);
                        this.setCalloutActive(false);
                        return "";
                    }
                }
                //}
                //else {
                //    this.setCalloutActive(false);
                //    return "";
                //}
            }
            else {
                if (Util.getValueOfInt(mTab.getValue("C_Tax_ID")) > 0) {
                    var dr = null;
                    // if Surcharge Tax is selected on Tax Rate, calculate surcharge tax amount accordingly
                    if (mTab.getField("SurchargeAmt") != null) {
                        dr = VIS.dataContext.getJSONRecord("MTax/CalculateSurcharge", mTab.getValue("C_Tax_ID").toString() + "," + mTab.getValue("PayAmt").toString() + "," + StdPrecision.toString());
                        mTab.setValue("TaxAmount", dr["TaxAmt"]);
                        mTab.setValue("SurchargeAmt", dr["SurchargeAmt"]);
                        this.setCalloutActive(false);
                        return "";
                    }
                    else {
                        dr = VIS.dataContext.getJSONRecord("MTax/GetTaxRate", mTab.getValue("C_Tax_ID").toString());
                        var Rate = Util.getValueOfDecimal(dr);
                        if (Rate > 0) {
                            //Formula for caluculating Tax amount ==>  Amount - Amount / ((Rate / 100) + 1)
                            var TaxAmt = Util.getValueOfDecimal(Util.getValueOfDecimal(mTab.getValue("PayAmt")) - (Util.getValueOfDecimal(mTab.getValue("PayAmt")) / ((Rate / 100) + 1)));

                            // Round the amount according to currency precision                        
                            TaxAmt = Util.getValueOfDecimal(TaxAmt.toFixed(StdPrecision));
                            mTab.setValue("TaxAmount", TaxAmt);
                        }
                        else {
                            mTab.setValue("TaxAmount", 0);
                            this.setCalloutActive(false);
                            return "";
                        }
                    }
                }
                else {
                    this.setCalloutActive(false);
                    return "";
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
    };

    // Method on Cash Journal
    CalloutTaxAmount.prototype.CashLineTaxAmount = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        try {
            var StdPrecision = 2;
            // get precision from currency if tax id is greater than 0
            if (mField.getColumnName() == "C_Tax_ID" || Util.getValueOfInt(mTab.getValue("C_Tax_ID")) > 0) {
                var currency = VIS.dataContext.getJSONRecord("MCurrency/GetCurrency", ctx.getContext(windowNo, "C_Currency_ID").toString());
                StdPrecision = currency["StdPrecision"];
            }

            if (mField.getColumnName() == "C_Tax_ID") {
                if (Util.getValueOfDecimal(mTab.getValue("Amount")) != 0) {
                    var dr = null;
                    // if Surcharge Tax is selected on Tax Rate, calculate surcharge tax amount accordingly
                    if (mTab.getField("SurchargeAmt") != null) {
                        dr = VIS.dataContext.getJSONRecord("MTax/CalculateSurcharge", value.toString() + "," + mTab.getValue("Amount").toString() + "," + StdPrecision.toString());
                        mTab.setValue("TaxAmt", dr["TaxAmt"]);
                        mTab.setValue("SurchargeAmt", dr["SurchargeAmt"]);
                        this.setCalloutActive(false);
                        return "";
                    }
                    else {
                        dr = VIS.dataContext.getJSONRecord("MTax/GetTaxRate", value.toString());
                        var Rate = Util.getValueOfDecimal(dr);
                        if (Rate > 0) {
                            //Formula for caluculating Tax amount ==>  Amount - Amount / ((Rate / 100) + 1)
                            var TaxAmt = Util.getValueOfDecimal(Util.getValueOfDecimal(mTab.getValue("Amount")) - (Util.getValueOfDecimal(mTab.getValue("Amount")) / ((Rate / 100) + 1)));

                            // JID_1037: On cash line tax amount field was not working accordingling to currency precision                            
                            TaxAmt = Util.getValueOfDecimal(TaxAmt.toFixed(StdPrecision));
                            mTab.setValue("TaxAmt", TaxAmt);
                        }
                        else {
                            mTab.setValue("TaxAmt", 0);
                            this.setCalloutActive(false);
                            return "";
                        }
                    }
                }
                else {
                    this.setCalloutActive(false);
                    return "";
                }
            }
            else {
                if (Util.getValueOfInt(mTab.getValue("C_Tax_ID")) > 0) {
                    var dr = null;
                    // if Surcharge Tax is selected on Tax Rate, calculate surcharge tax amount accordingly
                    if (mTab.getField("SurchargeAmt") != null) {
                        dr = VIS.dataContext.getJSONRecord("MTax/CalculateSurcharge", mTab.getValue("C_Tax_ID").toString() + "," + mTab.getValue("Amount").toString() + "," + StdPrecision.toString());
                        mTab.setValue("TaxAmt", dr["TaxAmt"]);
                        mTab.setValue("SurchargeAmt", dr["SurchargeAmt"]);
                        this.setCalloutActive(false);
                        return "";
                    }
                    else {
                        dr = VIS.dataContext.getJSONRecord("MTax/GetTaxRate", mTab.getValue("C_Tax_ID").toString());
                        var Rate = Util.getValueOfDecimal(dr);

                        if (Rate > 0) {
                            //Formula for caluculating Tax amount ==>  Amount - Amount / ((Rate / 100) + 1)
                            var TaxAmt = Util.getValueOfDecimal(Util.getValueOfDecimal(mTab.getValue("Amount")) - (Util.getValueOfDecimal(mTab.getValue("Amount")) / ((Rate / 100) + 1)));

                            // JID_1037: On cash line tax amount field was not working accordingling to currency precision
                            TaxAmt = Util.getValueOfDecimal(TaxAmt.toFixed(StdPrecision));
                            mTab.setValue("TaxAmt", TaxAmt);
                        }
                        else {
                            mTab.setValue("TaxAmt", 0);
                            this.setCalloutActive(false);
                            return "";
                        }
                    }
                }
                else {
                    this.setCalloutActive(false);
                    return "";
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
    };

    // Method on Bank Statement Line
    CalloutTaxAmount.prototype.BankStatementLineTaxAmount = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var paramString = "";
        try {
            // Need to round Tax Amount according to Currency Precision  
            paramString = mTab.getValue("C_Currency_ID").toString();
            var currency = VIS.dataContext.getJSONRecord("MCurrency/GetCurrency", paramString);
            var StdPrecision = currency["StdPrecision"];

            if (mField.getColumnName() == "C_Tax_ID") {
                if (Util.getValueOfDecimal(mTab.getValue("ChargeAmt")) != 0) {
                    var dr = null;
                    // if Surcharge Tax is selected on Tax Rate, calculate surcharge tax amount accordingly
                    if (mTab.getField("SurchargeAmt") != null) {
                        dr = VIS.dataContext.getJSONRecord("MTax/CalculateSurcharge", value.toString() + "," + mTab.getValue("ChargeAmt").toString() + "," + StdPrecision.toString());
                        mTab.setValue("TaxAmt", dr["TaxAmt"]);
                        mTab.setValue("SurchargeAmt", dr["SurchargeAmt"]);
                        this.setCalloutActive(false);
                        return "";
                    }
                    else {
                        dr = VIS.dataContext.getJSONRecord("MTax/GetTaxRate", value.toString());
                        var Rate = Util.getValueOfDecimal(dr);
                        if (Rate > 0) {
                            //Formula for caluculating Tax amount ==>  Amount - Amount / ((Rate / 100) + 1)
                            var TaxAmt = Util.getValueOfDecimal(Util.getValueOfDecimal(mTab.getValue("ChargeAmt")) - (Util.getValueOfDecimal(mTab.getValue("ChargeAmt")) / ((Rate / 100) + 1)));

                            // JID_0333: Need to round Tax Amount according to Currency Precision                            
                            TaxAmt = Util.getValueOfDecimal(TaxAmt.toFixed(StdPrecision));
                            mTab.setValue("TaxAmt", TaxAmt);
                        }
                        else {
                            mTab.setValue("TaxAmt", 0);
                            this.setCalloutActive(false);
                            ctx = windowNo = mTab = mField = value = oldValue = null;
                            return "";
                        }
                    }
                }
                else {
                    this.setCalloutActive(false);
                    ctx = windowNo = mTab = mField = value = oldValue = null;
                    return "";
                }
            }
            else {
                if (Util.getValueOfInt(mTab.getValue("C_Tax_ID")) > 0) {
                    var dr = null;
                    if (mTab.getField("SurchargeAmt") != null) {
                        // if Surcharge Tax is selected on Tax Rate, calculate surcharge tax amount accordingly
                        dr = VIS.dataContext.getJSONRecord("MTax/CalculateSurcharge", mTab.getValue("C_Tax_ID").toString() + "," + mTab.getValue("ChargeAmt").toString() + "," + StdPrecision.toString());
                        mTab.setValue("TaxAmt", dr["TaxAmt"]);
                        mTab.setValue("SurchargeAmt", dr["SurchargeAmt"]);
                        this.setCalloutActive(false);
                        return "";
                    }
                    else {
                        dr = VIS.dataContext.getJSONRecord("MTax/GetTaxRate", mTab.getValue("C_Tax_ID").toString());
                        var Rate = Util.getValueOfDecimal(dr);
                        if (Rate > 0) {
                            //Formula for caluculating Tax amount ==>  Amount - Amount / ((Rate / 100) + 1)
                            var TaxAmt = Util.getValueOfDecimal(Util.getValueOfDecimal(mTab.getValue("ChargeAmt")) - (Util.getValueOfDecimal(mTab.getValue("ChargeAmt")) / ((Rate / 100) + 1)));

                            // JID_0333: Need to round Tax Amount according to Currency Precision                           
                            TaxAmt = Util.getValueOfDecimal(TaxAmt.toFixed(StdPrecision));
                            mTab.setValue("TaxAmt", TaxAmt);
                        }
                        else {
                            mTab.setValue("TaxAmt", 0);
                            this.setCalloutActive(false);
                            ctx = windowNo = mTab = mField = value = oldValue = null;
                            return "";
                        }
                    }
                }
                else {
                    this.setCalloutActive(false);
                    ctx = windowNo = mTab = mField = value = oldValue = null;
                    return "";
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
    };

    VIS.Model.CalloutTaxAmount = CalloutTaxAmount;
    //**************CalloutTaxAmount End*********************

})(VIS, jQuery);