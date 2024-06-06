; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    function CalloutCashJournal() {
        VIS.CalloutEngine.call(this, "VIS.CalloutCashJournal"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutCashJournal, VIS.CalloutEngine);//inherit CalloutEngine
    /// <summary>
    ///  Cash Journal Line Invoice. when Invoice selected - set C_Currency,
    ///  DiscountAnt, Amount, WriteOffAmt
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="windowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    CalloutCashJournal.prototype.Invoice = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive()) // assuming it is resetting value
        {
            return "";
        }
        if (value == null || value.toString() == "") {
            /*Clear references and amounts when we clear invoice*/
            if (mField.getColumnName() == "C_Invoice_ID") {
                this.setCalloutActive(true);
                mTab.setValue("C_BPartner_ID", null);
                mTab.setValue("C_BPartner_Location_ID", null);
                mTab.setValue("C_InvoicePaySchedule_ID", null);
                ctx.setContext(windowNo, "InvTotalAmt", "");

                // JID_0780: Open cash journal line and select the invoice now on clearing the invoice system was giving error message.
                ctx.setContext("InvTotalAmt", "");
                mTab.setValue("ConvertedAmt", VIS.Env.ZERO);
                mTab.setValue("Amount", VIS.Env.ZERO);
                mTab.setValue("DiscountAmt", VIS.Env.ZERO);
                mTab.setValue("WriteOffAmt", VIS.Env.ZERO);
                mTab.setValue("OverUnderAmt", VIS.Env.ZERO);
                this.setCalloutActive(false);
            }
            return "";
        }
        this.setCalloutActive(true);

        var C_Invoice_ID = Util.getValueOfInt(value);

        //------14-11-2014-------
        var dueAmount = 0;
        var C_InvoicePaySchedule_ID = 0;
        var _chk = 0;
        //var _sqlAmt = "SELECT * FROM   (SELECT ips.C_InvoicePaySchedule_ID, "
        //+ " ips.DueAmt  FROM C_Invoice i  INNER JOIN C_InvoicePaySchedule ips "
        //+ " ON (i.C_Invoice_ID        =ips.C_Invoice_ID)  WHERE i.IsPayScheduleValid='Y' "
        //+ " AND ips.IsValid           ='Y'  AND ips.isactive          ='Y' "
        //+ " AND i.C_Invoice_ID    = " + C_Invoice_ID
        //+ "  AND ips.C_InvoicePaySchedule_ID NOT IN"
        //+ "(SELECT NVL(C_InvoicePaySchedule_ID,0) FROM C_InvoicePaySchedule WHERE c_payment_id IN"
        //+ "(SELECT NVL(c_payment_id,0) FROM C_InvoicePaySchedule)  union "
        //+ " SELECT NVL(C_InvoicePaySchedule_id,0) FROM C_InvoicePaySchedule  WHERE c_cashline_id IN"
        //+ "(SELECT NVL(c_cashline_id,0) FROM C_InvoicePaySchedule )) "
        //+ " ORDER BY ips.duedate ASC  ) WHERE rownum=1";
        var drAmt = null;
        try {
            drAmt = VIS.dataContext.getJSONRecord("MInvoice/GetInvPaySchedDetail", C_Invoice_ID.toString());
            if (drAmt != null) {
                C_InvoicePaySchedule_ID = Util.getValueOfInt(drAmt["C_InvoicePaySchedule_ID"]);
                mTab.setValue("C_InvoicePaySchedule_ID", C_InvoicePaySchedule_ID);
                dueAmount = Util.getValueOfDecimal(drAmt["dueAmount"]);
                _chk = 1;
            }

        }
        catch (err) {
            if (drAmt != null) {
                drAmt = null;
            }
            this.log.log(Level.SEVERE, "CalloutCashJournal.Invoice -" + C_Invoice_ID, err.message);
            return err.toString();
        }
        //-------------
        if (C_Invoice_ID == null || C_Invoice_ID == 0) {
            mTab.setValue("C_Currency_ID", null);
            // commented by Arpit
            /*
            mTab.setValue("C_BPartner_ID", null);
            mTab.setValue("C_BPartner_Location_ID", null);
            mTab.setValue("ConvertedAmt", VIS.Env.ZERO);
            mTab.setValue("Amount", VIS.Env.ZERO);
            mTab.setValue("DiscountAmt", VIS.Env.ZERO);
            mTab.setValue("WriteOffAmt", VIS.Env.ZERO);
             mTab.setValue("OverUnderAmt", VIS.Env.ZERO);
            */
            this.setCalloutActive(false);
            return "";
        }

        // Date
        // var ts = CommonFunctions.CovertMilliToDate(ctx.getContextAsTime(windowNo, "DateAcct"));
        //DateTime billDate = CommonFunctions.CovertMilliToDate(ctx.getContextAsTime(windowNo, "DateOrdered"));
        var ts = new Date(ctx.getContext(windowNo, "DateAcct"));
        //DateTime ts = new DateTime(ctx.getContextAsTime(windowNo, "DateAcct")); // from
        // C_Cash
        var tsDate = "TO_DATE( '" + (Number(ts.getMonth()) + 1) + "-" + ts.getDate() + "-" + ts.getFullYear() + "', 'MM-DD-YYYY')";// GlobalVariable.TO_DATE(Util.GetValueOfDateTime(srchCtrls[i].Ctrl.getValue()), true);
        var paramString = C_Invoice_ID.toString() + "," + tsDate.toString() + "," + C_InvoicePaySchedule_ID.toString();
        //var sql = "SELECT C_BPartner_ID, C_Currency_ID, invoiceOpen(C_Invoice_ID, 0) as invoiceOpen, IsSOTrx, paymentTermDiscount(invoiceOpen(C_Invoice_ID, 0),C_Currency_ID,C_PaymentTerm_ID,DateInvoiced, " + tsDate
        //    + " ) as paymentTermDiscount,C_DocTypeTarget_ID FROM C_Invoice WHERE C_Invoice_ID=" + C_Invoice_ID;
        var data = null;
        try {
            data = VIS.dataContext.getJSONRecord("MInvoice/GetInvoiceDetails", paramString);

            //idr = CalloutDB.executeCalloutReader(sql, null, null);
            //pstmt.setTimestamp(1, ts);
            //pstmt.setInt(2, C_Invoice_ID.intValue());
            //ResultSet rs = pstmt.executeQuery();
            if (data != null) {

                var payAmt = 0;
                mTab.setValue("C_BPartner_ID", Util.getValueOfInt(data["C_BPartner_ID"]));
                mTab.setValue("C_Currency_ID", Util.getValueOfInt(data["C_Currency_ID"]));//.getInt(2)));

                // JID_1208: System should set Currency Type that is defined on Invoice.
                mTab.setValue("C_ConversionType_ID", Util.getValueOfInt(data["C_ConversionType_ID"]));
                mTab.setValue("C_BPartner_Location_ID", Util.getValueOfInt(data["C_BPartner_Location_ID"]));//Arpit
                if (_chk == 0) {
                    payAmt = Util.getValueOfDecimal(data["invoiceOpen"]);//.getBigDecimal(3);
                }
                else {
                    //payAmt = (dueAmount) * (-1);
                    payAmt = dueAmount;
                }
                var discountAmt = Util.getValueOfDecimal(data["paymentTermDiscount"]);//.getBigDecimal(5);
                var isSOTrx = "Y" == data["IsSOTrx"];//.getString(4));
                if (!isSOTrx) {
                    if (_chk == 0)//Pratap
                    {
                        payAmt = (payAmt) * (-1);
                    }
                    else//Pratap
                    {
                        payAmt = (dueAmount) * (-1);
                    }
                    discountAmt = (discountAmt) * (-1);//.negate();
                }
                // // Bharat
                //var doctype_ID = Util.getValueOfInt(idr.get("c_doctypetarget_id"));
                //var _qry = "SELECT DocBaseType FROM C_DocType WHERE C_DocType_ID = " + doctype_ID;
                var docbaseType = Util.getValueOfString(data["docbaseType"]);
                if ("ARC" == docbaseType || "API" == docbaseType) {
                    mTab.setValue("VSS_PAYMENTTYPE", "P");
                }
                else {
                    mTab.setValue("VSS_PAYMENTTYPE", "R");
                }
                mTab.setValue("DiscountAmt", discountAmt);
                ctx.setContext("InvTotalAmt", payAmt.toString());
                mTab.setValue("Amount", (payAmt - discountAmt));
                mTab.setValue("WriteOffAmt", VIS.Env.ZERO);


            }
        }
        catch (err) {
            if (idr != null) {
                idr.close();
                idr = null;
            }
            this.log.log(Level.SEVERE, "invoice", err);
            this.setCalloutActive(false);
            return err.toString();
            //return e.getLocalizedMessage();
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// Invoice Pay Schedule
    /// When Invoice Pay Schedule Selected
    /// The Amount Corresponding to that pay Schedule
    /// filled in Amount
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="WindowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    CalloutCashJournal.prototype.SetAmount = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive()) {
            return "";
        }
        if (value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        if (value == null || value.toString() == "") {
            if (Util.getValueOfInt(mTab.getValue("C_Invoice_ID")) > 0) {
                //var sql = "SELECT sum(ips.DueAmt)  FROM C_Invoice i INNER JOIN C_InvoicePaySchedule ips ON (i.C_Invoice_ID=ips.C_Invoice_ID) WHERE i.IsPayScheduleValid='Y' AND ips.IsValid ='Y' AND ips.isactive ='Y'" +
                //   "AND i.C_Invoice_ID = " + Util.getValueOfInt(mTab.getValue("C_Invoice_ID")) + " AND C_InvoicePaySchedule_ID NOT IN (SELECT NVL(C_InvoicePaySchedule_ID,0) FROM C_InvoicePaySchedule WHERE C_Payment_ID IN " +
                //  "(SELECT NVL(C_Payment_ID,0) FROM C_InvoicePaySchedule) UNION SELECT NVL(C_InvoicePaySchedule_ID,0) FROM C_InvoicePaySchedule WHERE C_Cashline_ID IN (SELECT NVL(C_Cashline_ID,0) FROM C_InvoicePaySchedule))";
                //var Amount = Util.getValueOfDecimal(CalloutDB.executeCalloutScalar(sql, null, null));
                var Amount = Util.getValueOfDecimal(VIS.dataContext.getJSONRecord("MCashBook/GetPaySheduleAmt", mTab.getValue("C_Invoice_ID").toString()));
                ctx.setContext(windowNo, "InvTotalAmt", Amount.toString());
                mTab.setValue("Amount", Amount);
                this.setCalloutActive(false);
                return "";
            }
            else {
                return "";
            }
        }
        mTab.setValue("DiscountAmt", VIS.Env.ZERO);
        mTab.setValue("WriteOffAmt", VIS.Env.ZERO);
        mTab.setValue("OverUnderAmt", VIS.Env.ZERO);

        var DataPrefix = VIS.dataContext.getJSONRecord("ModulePrefix/GetModulePrefix", "VA009_");
        if (DataPrefix["VA009_"]) {
            ctx.setContext("InvTotalAmt", "");
            var C_InvoicePaySchedule_ID = Util.getValueOfInt(value);
            if (C_InvoicePaySchedule_ID == null || C_InvoicePaySchedule_ID == 0) {
                ctx.setContext(windowNo, "InvTotalAmt", null);
                if (mTab.getTableName() == "C_Payment") {
                    mTab.setValue("PayAmt", null);              // For Payment window
                    mTab.setValue("PaymentAmount", null);
                }
                else {
                    mTab.setValue("Amount", null);             // For Cash Journal Line 
                }
                mTab.setValue("DiscountAmt", 0);
                mTab.setValue("WriteOffAmt", VIS.Env.ZERO);
                ctx.setContext("InvTotalAmt", 0);
                this.setCalloutActive(false);
                return "";
            }
            //var qry = "SELECT DueAmt , DiscountDate , DiscountAmt , DiscountDays2 , Discount2  FROM C_InvoicePaySchedule WHERE C_InvoicePaySchedule_ID=" + C_InvoicePaySchedule_ID;
            var paramString = C_InvoicePaySchedule_ID.toString() + "," + mTab.getValue("C_Cash_ID") + "," + mTab.getValue("C_Invoice_ID");
            var data = VIS.dataContext.getJSONRecord("MCashBook/GetPaySheduleData", paramString);
            /*VIS_427 Set Currency when user change invoicepayschedule so that can convert value of schedule
            according to currency*/
            mTab.setValue("C_Currency_ID", Util.getValueOfInt(data["C_Currency_ID"]));
           // mTab.setValue("C_ConversionType_ID", Util.getValueOfInt(data["C_ConversionType_ID"]));
            if (data != null) {
                if (mTab.getTableName() == "C_Payment") {
                    var dateTrx = mTab.getValue("DateTrx");
                    var IsReturnTrx = data["IsReturnTrx"];
                    //VIS_427 Bug id 5620 set value of payment and discount when user select value through payment window 
                    if (IsReturnTrx == "Y") {
                        if (//(Globalize.format(new Date(dateTrx), "yyyy-MM-dd") >= Globalize.format(new Date(data["DateInvoiced"]), "yyyy-MM-dd")) &&
                            (Globalize.format(new Date(data["DiscountDate"]), "yyyy-MM-dd") >= Globalize.format(new Date(dateTrx), "yyyy-MM-dd"))) {
                            mTab.setValue("DiscountAmt", -1 * Util.getValueOfDecimal(data["DiscountAmt"]));
                            mTab.setValue("PayAmt", -1 * (Util.getValueOfDecimal(data["DueAmt"]) - Util.getValueOfDecimal(data["DiscountAmt"])));
                            mTab.setValue("PaymentAmount", -1 * (Util.getValueOfDecimal(data["DueAmt"]) - Util.getValueOfDecimal(data["DiscountAmt"])));
                        }
                        else if (//(Globalize.format(new Date(dateTrx), "yyyy-MM-dd") >= Globalize.format(new Date(data["DateInvoiced"]), "yyyy-MM-dd")) &&
                            (Globalize.format(new Date(data["DiscountDays2"]), "yyyy-MM-dd") >= Globalize.format(new Date(dateTrx), "yyyy-MM-dd"))) {
                            mTab.setValue("DiscountAmt", -1 * Util.getValueOfDecimal(data["Discount2"]));
                            mTab.setValue("PayAmt", -1 * (Util.getValueOfDecimal(data["DueAmt"]) - Util.getValueOfDecimal(data["Discount2"])));
                            mTab.setValue("PaymentAmount", -1 * (Util.getValueOfDecimal(data["DueAmt"]) - Util.getValueOfDecimal(data["Discount2"])));
                        }
                        else {
                            mTab.setValue("DiscountAmt", 0);
                            mTab.setValue("PayAmt", -1 * (Util.getValueOfDecimal(data["DueAmt"])));
                            mTab.setValue("PaymentAmount", -1 * (Util.getValueOfDecimal(data["DueAmt"])));
                        }
                      
                    }
                    else {
                        if (//(Globalize.format(new Date(dateTrx), "yyyy-MM-dd") >= Globalize.format(new Date(data["DateInvoiced"]), "yyyy-MM-dd")) &&
                            (Globalize.format(new Date(data["DiscountDate"]), "yyyy-MM-dd") >= Globalize.format(new Date(dateTrx), "yyyy-MM-dd"))) {
                            mTab.setValue("DiscountAmt", Util.getValueOfDecimal(data["DiscountAmt"]));
                            mTab.setValue("PayAmt", (Util.getValueOfDecimal(data["DueAmt"]) - Util.getValueOfDecimal(data["DiscountAmt"])));
                            mTab.setValue("PaymentAmount", (Util.getValueOfDecimal(data["DueAmt"]) - Util.getValueOfDecimal(data["DiscountAmt"])));
                        }
                        else if (//(Globalize.format(new Date(dateTrx), "yyyy-MM-dd") >= Globalize.format(new Date(data["DateInvoiced"]), "yyyy-MM-dd")) &&
                            (Globalize.format(new Date(data["DiscountDays2"]), "yyyy-MM-dd") >= Globalize.format(new Date(dateTrx), "yyyy-MM-dd"))) {
                            mTab.setValue("DiscountAmt", Util.getValueOfDecimal(data["Discount2"]));
                            mTab.setValue("PayAmt", (Util.getValueOfDecimal(data["DueAmt"]) - Util.getValueOfDecimal(data["Discount2"])));
                            mTab.setValue("PaymentAmount", (Util.getValueOfDecimal(data["DueAmt"]) - Util.getValueOfDecimal(data["Discount2"])));
                        }
                        else {
                            mTab.setValue("DiscountAmt", 0);
                            mTab.setValue("PayAmt", (Util.getValueOfDecimal(data["DueAmt"])));
                            mTab.setValue("PaymentAmount", (Util.getValueOfDecimal(data["DueAmt"])));
                        }
                    }
                }
                else {
                    mTab.setValue("Amount", Util.getValueOfDecimal(data["DueAmt"]));                    // For Cash Journal Line 
                    ctx.setContext(windowNo, "InvTotalAmt", Util.getValueOfDecimal(mTab.getValue("Amount")));
                    mTab.setValue("WriteOffAmt", 0);
                    mTab.setValue("OverUnderAmt", 0);
                    //qry = "SELECT DateAcct FROM C_Cash WHERE IsActive = 'Y' AND C_Cash_ID = " + mTab.getValue("C_Cash_ID");
                    var accountDate = Util.getValueOfDate(data["accountDate"]);
                    //qry = "SELECT IsSoTrx FROM C_Invoice WHERE C_Invoice_ID = " + mTab.getValue("C_Invoice_ID");
                    var isSoTrx = Util.getValueOfString(data["isSoTrx"]);
                    //VIS_427 Bug Id 5620 changed sign of amount according to IsSotrx check
                    if (Util.getValueOfDate(data["DiscountDate"]) >= accountDate) {
                        if (isSoTrx == "N") {
                            mTab.setValue("DiscountAmt", -1 * Util.getValueOfDecimal(data["DiscountAmt"]));
                            mTab.setValue("Amount", -1 * (Util.getValueOfDecimal(data["DueAmt"]) - Util.getValueOfDecimal(data["DiscountAmt"])));
                            //VIS_427 For Payment allocate set due amount in invoice amount
                            if (mTab.getTableName() == "C_PaymentAllocate") {
                                mTab.setValue("InvoiceAmt", -1 * (Util.getValueOfDecimal(data["DueAmt"])));
                            }
                        }
                        else {
                            mTab.setValue("DiscountAmt", Util.getValueOfDecimal(data["DiscountAmt"]));
                            mTab.setValue("Amount", (Util.getValueOfDecimal(data["DueAmt"]) - Util.getValueOfDecimal(data["DiscountAmt"])));
                            if (mTab.getTableName() == "C_PaymentAllocate") {
                                mTab.setValue("InvoiceAmt", Util.getValueOfDecimal(data["DueAmt"]));
                            }
                        }
                       
                    }
                    else if (Util.getValueOfDate(data["DiscountDays2"]) >= accountDate) {
                        if (isSoTrx == "N") {
                            mTab.setValue("DiscountAmt", -1 * Util.getValueOfDecimal(data["Discount2"]));
                            mTab.setValue("Amount", -1 * (Util.getValueOfDecimal(data["DueAmt"]) - Util.getValueOfDecimal(data["DiscountAmt"])));
                            if (mTab.getTableName() == "C_PaymentAllocate") {
                                mTab.setValue("InvoiceAmt", -1 * (Util.getValueOfDecimal(data["DueAmt"])));
                            }
                        }
                        else {
                            mTab.setValue("DiscountAmt", Util.getValueOfDecimal(data["Discount2"]));
                            mTab.setValue("Amount", (Util.getValueOfDecimal(data["DueAmt"]) - Util.getValueOfDecimal(data["DiscountAmt"])));
                            if (mTab.getTableName() == "C_PaymentAllocate") {
                                mTab.setValue("InvoiceAmt", Util.getValueOfDecimal(data["DueAmt"]));
                            }
                        }
                    }
                    else {
                        if (isSoTrx == "N") {
                            mTab.setValue("Amount", -1 * Util.getValueOfDecimal(data["DueAmt"]));
                            if (mTab.getTableName() == "C_PaymentAllocate") {
                                mTab.setValue("InvoiceAmt", -1 * (Util.getValueOfDecimal(data["DueAmt"])));
                            }
                        }
                        else {
                            mTab.setValue("Amount", Util.getValueOfDecimal(data["DueAmt"]));
                            if (mTab.getTableName() == "C_PaymentAllocate") {
                                mTab.setValue("InvoiceAmt", Util.getValueOfDecimal(data["DueAmt"]));
                            }
                        }
                        mTab.setValue("DiscountAmt", 0);
                    }
                }
            }
        }
        else {
            var C_InvoicePaySchedule_ID = Util.getValueOfInt(value);
            if (C_InvoicePaySchedule_ID == null || C_InvoicePaySchedule_ID == 0) {
                ctx.setContext(windowNo, "InvTotalAmt", null);
                if (mTab.getTableName() == "C_Payment") {
                    mTab.setValue("PayAmt", null);              // For Payment window
                    mTab.setValue("PaymentAmount", null);
                }
                else {
                    mTab.setValue("Amount", null);             // For Cash Journal Line 
                }
                this.setCalloutActive(false);
                return "";
            }
            //var result = "SELECT DueAmt FROM C_InvoicePaySchedule WHERE C_InvoicePaySchedule_ID=" + C_InvoicePaySchedule_ID;
            var dr = VIS.dataContext.getJSONRecord("MCashBook/GetInvSchedDueAmt", C_InvoicePaySchedule_ID.toString());
            //var Amt = Util.getValueOfDecimal(result);
            if (dr != null) {
                var Amt = dr["DueAmt"];
                if (mTab.getTableName() == "C_Payment") {
                    if (dr["IsReturnTrx"] == "Y") {
                        mTab.setValue("PayAmt", Amt * -1);
                        mTab.setValue("PaymentAmount", Amt * -1);
                    }
                    else {
                        mTab.setValue("PayAmt", Amt);                    // For Payment window
                        mTab.setValue("PaymentAmount", Amt);
                    }
                }
                else {
                    mTab.setValue("Amount", Amt);                    // For Cash Journal Line 
                    ctx.setContext(windowNo, "InvTotalAmt", Util.getValueOfDecimal(mTab.getValue("Amount")));
                }
            }
        }
        this.setCalloutActive(false);
        return "";
    }

    /// <summary>
    /// Cash Journal Line Invoice Amounts. when DiscountAnt, Amount, WriteOffAmt
    /// change making sure that add up to InvTotalAmt (created by
    /// CashJournal_Invoice)
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="windowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    CalloutCashJournal.prototype.Amounts = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //if (value == DBNull.Value || value == null || value.toString() == "")
        //{
        //    return "";
        //}
        // Needs to be Invoice
        if (this.isCalloutActive()) {
            //if (this.isCalloutActive() || "I" != mTab.getValue("CashType")) {
            return "";
        }
        //VIS_427 Handle the issue on subtraction of write off, discount, and over under and it should update fields with accurate amount.
        var invTotalAmt = 0;
        var colName = mField.getColumnName();
        var payAmt = Util.getValueOfDecimal(mTab.getValue("Amount"));
        var stdPrecision = 2;
        this.setCalloutActive(true);
        var convertedAmt = 0;   
        if ("I" == mTab.getValue("CashType")) {            
            var discountAmt = Util.getValueOfDecimal(mTab.getValue("DiscountAmt"));
            var writeOffAmt = Util.getValueOfDecimal(mTab.getValue("WriteOffAmt"));
            var overUnderAmt = Util.getValueOfDecimal(mTab.getValue("OverUnderAmt"));
            var paramstring = Util.getValueOfInt(mTab.getValue("C_InvoicePaySchedule_ID")).toString() + "," + Util.getValueOfInt(mTab.getValue("C_Currency_ID")).toString();
            //Getting the open amount of invoice schedule
            var AmountSched = VIS.dataContext.getJSONRecord("MCashBook/GetInvSchedDueAmt", paramstring);
            if (AmountSched != null) {
                invTotalAmt = Util.getValueOfDecimal(AmountSched["DueAmt"]);
                stdPrecision = Util.getValueOfInt(AmountSched["StdPrecision"]);
            }
            var PaymentType = mTab.getValue("VSS_PaymentType");
            if (PaymentType == "P") {
                if (payAmt > 0) {
                    payAmt = payAmt * -1;
                    mTab.setValue("Amount", payAmt);
                }

                if (discountAmt > 0) {
                    discountAmt = discountAmt * -1;
                }
                if (writeOffAmt > 0) {
                    writeOffAmt = writeOffAmt * -1;
                    mTab.setValue("WriteOffAmt", writeOffAmt);
                }
                if (overUnderAmt > 0) {
                    overUnderAmt = overUnderAmt * -1;
                    mTab.setValue("OverUnderAmt", overUnderAmt);
                }
                if (invTotalAmt > 0) {
                    invTotalAmt = invTotalAmt * -1;
                }
            }
            else {
                if (payAmt < 0) {
                    payAmt = payAmt * -1;
                    mTab.setValue("Amount", payAmt);
                }

                if (discountAmt < 0) {
                    discountAmt = discountAmt * -1;
                }
                if (writeOffAmt < 0) {
                    writeOffAmt = writeOffAmt * -1;
                    mTab.setValue("WriteOffAmt", writeOffAmt);
                }
                if (overUnderAmt < 0) {
                    overUnderAmt = overUnderAmt * -1;
                    mTab.setValue("OverUnderAmt", overUnderAmt);
                }               
                if (invTotalAmt < 0) {
                    invTotalAmt = invTotalAmt * -1;
                }
            }

            this.log.fine(colName + " - Invoice=" + invTotalAmt + " - Amount=" + payAmt
                + ", Discount=" + discountAmt + ", WriteOff=" + writeOffAmt);

            // Amount - calculate write off
            if (colName == "Amount") {
                var sub = (invTotalAmt - payAmt);
                overUnderAmt = ((sub - discountAmt) - writeOffAmt).toFixed(stdPrecision);
                //  writeOffAmt = Decimal.Subtract(Decimal.Subtract(invTotalAmt, payAmt), discountAmt);
                if ((PaymentType == "R" && overUnderAmt > 0) || (PaymentType == "P" && overUnderAmt < 0)) {
                    VIS.ADialog.info("LessScheduleAmount");
                }

                if ((PaymentType == "P" && overUnderAmt > 0) || (PaymentType == "R" && overUnderAmt < 0)) {
                    VIS.ADialog.info("MoreScheduleAmount");
                    payAmt = ((invTotalAmt - discountAmt) - writeOffAmt);
                    overUnderAmt = (((invTotalAmt - payAmt) - discountAmt) - writeOffAmt);
                    mTab.setValue("Amount", payAmt.toFixed(stdPrecision));
                }
                //precised value's according to precision if value does not match
                if (discountAmt.toFixed(stdPrecision) != Util.getValueOfDecimal(mTab.getValue("DiscountAmt"))) {
                    mTab.setValue("DiscountAmt", discountAmt.toFixed(stdPrecision));
                }
                if (overUnderAmt != Util.getValueOfDecimal(mTab.getValue("OverUnderAmt"))) {
                    mTab.setValue("OverUnderAmt", Util.getValueOfDecimal(overUnderAmt).toFixed(stdPrecision));
                }
                if (writeOffAmt.toFixed(stdPrecision) != Util.getValueOfDecimal(mTab.getValue("WriteOffAmt"))) {
                    mTab.setValue("WriteOffAmt", writeOffAmt.toFixed(stdPrecision));
                }
                if (payAmt.toFixed(stdPrecision) != Util.getValueOfDecimal(mTab.getValue("Amount"))) {
                    mTab.setValue("Amount", payAmt.toFixed(stdPrecision));
                }
            }
            else // calculate PayAmt
            {               
                var sub = (invTotalAmt - discountAmt);
                payAmt = ((sub - writeOffAmt) - overUnderAmt).toFixed(stdPrecision);
                //payAmt = Decimal.Subtract(Decimal.Subtract(invTotalAmt, discountAmt), writeOffAmt);

                if ((PaymentType == "P" && payAmt > 0) || (PaymentType == "R" && payAmt < 0)) {
                    VIS.ADialog.info("MoreScheduleAmount");
                    if (colName == "OverUnderAmt") {
                        payAmt = ((invTotalAmt - discountAmt) - writeOffAmt);
                        overUnderAmt = (((invTotalAmt - payAmt) - discountAmt) - writeOffAmt);
                        mTab.setValue("OverUnderAmt", overUnderAmt);
                    }
                    if (colName == "DiscountAmt") {
                        payAmt = ((invTotalAmt - overUnderAmt) - writeOffAmt);
                        discountAmt = (((invTotalAmt - payAmt) - overUnderAmt) - writeOffAmt);
                        mTab.setValue("DiscountAmt", discountAmt.toFixed(stdPrecision));
                    }
                    if (colName == "WriteOffAmt") {
                        payAmt = ((invTotalAmt - discountAmt) - overUnderAmt);
                        writeOffAmt = (((invTotalAmt - payAmt) - discountAmt) - overUnderAmt);
                        mTab.setValue("WriteOffAmt", writeOffAmt.toFixed(stdPrecision));
                    }
                }
                //precised value's according to precision if value does not match
                if (discountAmt.toFixed(stdPrecision) != Util.getValueOfDecimal(mTab.getValue("DiscountAmt"))) {
                    mTab.setValue("DiscountAmt", discountAmt.toFixed(stdPrecision));
                }
                if (writeOffAmt.toFixed(stdPrecision) != Util.getValueOfDecimal(mTab.getValue("WriteOffAmt"))) {
                    mTab.setValue("WriteOffAmt", writeOffAmt.toFixed(stdPrecision));
                }
                if (overUnderAmt != Util.getValueOfDecimal(mTab.getValue("OverUnderAmt"))) {
                    mTab.setValue("OverUnderAmt", Util.getValueOfDecimal(overUnderAmt).toFixed(stdPrecision));
                }
                if (payAmt != Util.getValueOfDecimal(mTab.getValue("Amount"))) {
                    mTab.setValue("Amount", Util.getValueOfDecimal(payAmt).toFixed(stdPrecision));
                }
                if ((PaymentType == "R" && overUnderAmt > 0) || (PaymentType == "P" && overUnderAmt < 0)) {
                    VIS.ADialog.info("LessScheduleAmount");
                }
            }
        }
        var paramString = mTab.getValue("C_Cash_ID").toString();
        var cash = VIS.dataContext.getJSONRecord("MCashBook/GetCashJournal", paramString);
        var convDate = cash["DateAcct"];
        var CurrFrom = cash["C_Currency_ID"];
        var CurrTo = cash["C_Currency_ID"];
        if (mTab.getValue("C_Currency_ID") != null) {
            CurrFrom = mTab.getValue("C_Currency_ID").toString();
        }
        var conversionType_ID = 0;
        if (mTab.getField("C_ConversionType_ID") != null) {
            conversionType_ID = Util.getValueOfInt(mTab.getValue("C_ConversionType_ID"));
        }
        if (CurrFrom != CurrTo) {
            paramString = payAmt.toString() + "," + CurrFrom + "," + CurrTo + "," + convDate + "," + conversionType_ID.toString() + "," +
                ctx.getAD_Client_ID().toString() + "," + ctx.getAD_Org_ID().toString();
            convertedAmt = VIS.dataContext.getJSONRecord("MConversionRate/CurrencyConvert", paramString);
            if (Util.getValueOfDecimal(mTab.getValue("Amount")) != 0 && convertedAmt == 0) {
                VIS.ADialog.info("NoCurrencyConversion");
            }
        }
        else {
            convertedAmt = payAmt;
        }
        mTab.setValue("convertedAmt", Util.getValueOfDecimal(convertedAmt).toFixed(stdPrecision));
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// Biginning balace calculation
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="windowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    CalloutCashJournal.prototype.BeginningBalCalc = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        if (this.isCalloutActive())		// assuming it is resetting value
        {
            return "";
        }
        this.setCalloutActive(true);

        var C_Cash_ID = Util.getValueOfInt(value);
        if (C_Cash_ID == null || C_Cash_ID == 0) {
            mTab.setValue("BeginningBalance", 0);
            this.setCalloutActive(false);
            return "";
        }


        var C_CashBook_ID = Util.getValueOfInt(mTab.getValue("C_CashBook_ID"));
        var AD_Client_ID = Util.getValueOfInt(mTab.getValue("AD_Client_ID"));
        var AD_Org_ID = Util.getValueOfInt(mTab.getValue("AD_Org_ID"));

        // Date
        //var ts = CommonFunctions.CovertMilliToDate(ctx.getContextAsTime(windowNo, "DateAcct"));
        var ts = ctx.getContext("DateAcct");
        //DateTime ts = new DateTime(ctx.getContextAsTime(windowNo, "DateAcct"));     // from
        // C_Cash

        var param = C_CashBook_ID.toString() + "," + AD_Client_ID.toString() + "," + AD_Org_ID.toString();
        var beginningBalance = VIS.dataContext.getJSONRecord("MCashJournal/GetBeginningBalCalc", param);


        //var sql = "SELECT EndingBalance FROM C_Cash WHERE C_CashBook_ID=" + C_CashBook_ID + " AND" +
        //    " AD_Client_ID=" + AD_Client_ID + " AND AD_Org_ID=" + AD_Org_ID + " AND " +
        //    "c_cash_id IN (SELECT Max(c_cash_id) FROM C_Cash WHERE C_CashBook_ID=" + C_CashBook_ID
        //    + "AND AD_Client_ID=" + AD_Client_ID + " AND AD_Org_ID=" + AD_Org_ID + ") AND Processed='Y'";
        //var idr = null;
        try {
            //idr = VIS.DB.executeReader(sql, null, null);
            //pstmt.setInt(1, C_CashBook_ID.intValue());
            //pstmt.setInt(2, AD_Client_ID.intValue());
            //pstmt.setInt(3, AD_Org_ID.intValue());
            //pstmt.setInt(4, C_CashBook_ID.intValue());
            //pstmt.setInt(5, AD_Client_ID.intValue());
            //pstmt.setInt(6, AD_Org_ID.intValue());
            //ResultSet rs = pstmt.executeQuery();
            //if (idr.read()) {
            //var beginningBalance = Util.getValueOfDecimal(idr.get(0));//.getBigDecimal(1);

            if (beginningBalance > 0) {
                mTab.setValue("BeginningBalance", beginningBalance);
            }
            else {
                //var zero = 0;
                mTab.setValue("BeginningBalance", 0);
            }

            idr.close();
        }
        catch (err) {
            if (idr != null) {
                idr.close();
            }
            this.log.log(Level.SEVERE, "Beginning balance", err);
            this.setCalloutActive(false);
            //return e.getLocalizedMessage();
            return err.toString();
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    CalloutCashJournal.prototype.SetCurrency = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if ((value == null || value.toString() == "") && mField.getColumnName() != "C_ConversionType_ID") {
            //if (value == null || value.toString() == "") {
            //mTab.setValue("C_Currency_ID", 0);
            return "";
        }
        if (this.isCalloutActive())		// assuming it is resetting value
        {
            return "";
        }
        this.setCalloutActive(true);
        var paramString = "";
        var payAmt = Util.getValueOfDecimal(mTab.getValue("Amount"));
        if (mField.getColumnName() == "C_CashBook_ID") {
            var C_CashBook_ID = Util.getValueOfInt(value);
            if (C_CashBook_ID == null || C_CashBook_ID == 0) {
                mTab.setValue("C_Currency_ID", 0);
                this.setCalloutActive(false);
                return "";
            }
            paramString = C_CashBook_ID.toString();
            //var paramString = C_CashBook_ID.toString();
            var cBook = VIS.dataContext.getJSONRecord("MCashBook/GetCashBook", paramString);
            // MCashBook cBook = new MCashBook(ctx, C_CashBook_ID, null);
            mTab.setValue("C_Currency_ID", cBook["C_Currency_ID"]);
        }
        else if (mField.getColumnName() == "C_BankAccount_ID") {
            var C_BankAccount_ID = Util.getValueOfInt(value);
            if (C_BankAccount_ID == null || C_BankAccount_ID == 0) {
                mTab.setValue("C_Currency_ID", 0);
                this.setCalloutActive(false);
                return "";
            }
            paramString = C_BankAccount_ID.toString();
            //var qry = "SELECT C_Currency_ID FROM C_BankAccount WHERE C_BankAccount_ID = " + C_BankAccount_ID;
            //var currency = Util.getValueOfInt(VIS.DB.executeScalar(qry, null, null));

            var currency = Util.getValueOfInt(VIS.dataContext.getJSONRecord("MPayment/GetBankAcctCurrency", paramString));
            mTab.setValue("C_Currency_ID", currency);
        }
        if (payAmt != 0) {
            paramString = mTab.getValue("C_Cash_ID").toString();
            var convertedAmt = 0;
            var cash = VIS.dataContext.getJSONRecord("MCashBook/GetCashJournal", paramString);
            var convDate = cash["DateAcct"];
            var CurrFrom = cash["C_Currency_ID"];
            var CurrTo = cash["C_Currency_ID"];
            if (mTab.getValue("C_Currency_ID") != null) {
                CurrFrom = mTab.getValue("C_Currency_ID").toString();
            }
            var conversionType_ID = 0;
            if (mTab.getField("C_ConversionType_ID") != null) {
                conversionType_ID = Util.getValueOfInt(mTab.getValue("C_ConversionType_ID"));
            }
            if (CurrFrom != CurrTo) {
                paramString = payAmt.toString() + "," + CurrFrom + "," + CurrTo + "," + convDate + "," + conversionType_ID.toString() + "," +
                    ctx.getAD_Client_ID().toString() + "," + ctx.getAD_Org_ID().toString();
                convertedAmt = VIS.dataContext.getJSONRecord("MConversionRate/CurrencyConvert", paramString);
                if (convertedAmt == 0) {
                    VIS.ADialog.info("NoCurrencyConversion");
                }
            }
            else {
                convertedAmt = payAmt;
            }
            mTab.setValue("convertedAmt", convertedAmt);
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// ConvertedAmt calculation
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="WindowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    CalloutCashJournal.prototype.ConvertedAmt = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }
        if (this.isCalloutActive())		// assuming it is resetting value
        {
            return "";
        }
        this.setCalloutActive(true);

        try {
            if (value > 0) {

                // when conversion type not selected then given message and clear value
                if (Util.getValueOfInt(mTab.getValue("C_ConversionType_ID")) <= 0) {
                    mTab.setValue("C_CashLine_ID_1", "");
                    VIS.ADialog.info("VIS_SelectConversion");
                    this.setCalloutActive(false);
                    return "";
                }

                var paramString = value.toString() + "," + mTab.getValue("C_Cash_ID").toString() +
                    "," + mTab.getValue("AD_Org_ID").toString() + "," + mTab.getValue("C_ConversionType_ID").toString();
                var result = VIS.dataContext.getJSONRecord("MCashJournal/ConvertedAmt", paramString);

                if (Util.getValueOfInt(result["currTo"]) == 0) {
                    this.setCalloutActive(false);
                    return "";
                }

                if (Util.getValueOfDecimal(result["amt"]) != 0 && Util.getValueOfDecimal(result["transferdAmt"]) == 0) {
                    VIS.ADialog.info("NoCurrencyConversion");
                }

                mTab.setValue("ConvertedAmt", Util.getValueOfDecimal(result["transferdAmt"]));
                mTab.setValue("Amount", Util.getValueOfDecimal(result["amt"]));
            }
            else {
                mTab.setValue("ConvertedAmt", 0);
                mTab.setValue("Amount", 0);
            }

        }
        catch (err) {
            this.setCalloutActive(false);
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    CalloutCashJournal.prototype.GetBeginBal = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value == "") {
            // JID_1321: "On change of cash journal org system through error message ""[TypeError: Cannot read property 'setReadOnly' of null]"          
            //mTab.getField("C_Invoice_ID").setReadOnly(false);
            return "";
        }
        if (this.isCalloutActive()) {
            return "";
        }
        this.setCalloutActive(true);
        //decimal BeginAmt = Util.GetValueOfDecimal(DB.ExecuteScalar(" select CompletedBalance from c_cashbook where c_cashbook_id=" + value));
        //tab.SetValue("BeginningBalance", BeginAmt);
        //Added By Manjot Changes Done to  Set Beginning Balance
        //var TotalAmt = Util.getValueOfDecimal(VIS.DB.executeScalar(" select sum(nvl(CompletedBalance,0)) + sum(nvl(runningbalance,0)) as TotalBal from c_cashbook where c_cashbook_id=" + value));

        var TotalAmt = VIS.dataContext.getJSONRecord("MCashBook/GetBegiBalance", value.toString());
        mTab.setValue("BeginningBalance", TotalAmt);
        this.setCalloutActive(false);
        return "";
    }

    VIS.Model.CalloutCashJournal = CalloutCashJournal;

    //*******CalloutSetReadOnly Starts**************
    function CalloutSetReadOnly() {
        VIS.CalloutEngine.call(this, "VIS.CalloutSetReadOnly");//must call
    };
    VIS.Utility.inheritPrototype(CalloutSetReadOnly, VIS.CalloutEngine); //inherit prototype


    CalloutSetReadOnly.prototype.SetReadnly = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "" || value.toString() == "E") {
            this.setCalloutActive(false);
            if (value != null) {
                if (value.toString() == "E") {
                    mTab.getField("VSS_PAYMENTTYPE").setReadOnly(true);
                    mTab.setValue("VSS_PAYMENTTYPE", "P");
                }
            }
            //
            return "";
        }
        this.setCalloutActive(true);
        if (Util.getValueOfString(mTab.getValue("CashType")) == "A" || Util.getValueOfString(mTab.getValue("CashType")) == "E") {
            mTab.setValue("VSS_PAYMENTTYPE", "P");
            mTab.getField("VSS_PAYMENTTYPE").setReadOnly(true);
        }
        else if (Util.getValueOfString(mTab.getValue("CashType")) == "F" || Util.getValueOfString(mTab.getValue("CashType")) == "R") {
            mTab.setValue("VSS_PAYMENTTYPE", "R");
            mTab.getField("VSS_PAYMENTTYPE").setReadOnly(true);
        }
        else if (Util.getValueOfString(mTab.getValue("CashType")) == "I" || Util.getValueOfString(mTab.getValue("CashType")) == "O") { //VA230:Invoice and Order
            mTab.getField("VSS_PAYMENTTYPE").setReadOnly(true);            
        }
        else {
            mTab.getField("VSS_PAYMENTTYPE").setReadOnly(false);
        }
        //VIS_427 Set Tax to null if case type is not charge
        if (Util.getValueOfString(mTab.getValue("CashType")) != "C") {
            mTab.setValue("C_Tax_ID", null);
        }

        if (Util.getValueOfString(mTab.getValue("VSS_PAYMENTTYPE")) == "P" ||
            Util.getValueOfString(mTab.getValue("VSS_PAYMENTTYPE")) == "E") {/*Receipt Return and Payment*/
            if (Util.getValueOfDecimal(mTab.getValue("amount")) > 0) {
                mTab.setValue("Amount", (0 - Util.getValueOfDecimal(mTab.getValue("amount"))));
            }
        }
        else if (Util.getValueOfString(mTab.getValue("VSS_PAYMENTTYPE")) == "R" ||
            Util.getValueOfString(mTab.getValue("VSS_PAYMENTTYPE")) == "A") { /*Payment Return and Receipt*/
            if (Util.getValueOfDecimal(mTab.getValue("amount")) < 0) {
                mTab.setValue("Amount", (0 - Util.getValueOfDecimal(mTab.getValue("amount"))));
            }
        }
        this.setCalloutActive(false);
        return "";
    }


    CalloutSetReadOnly.prototype.SetAmountValue = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value.toString() == "") {
            mTab.getField("VSS_PAYMENTTYPE").setReadOnly(false);
            this.setCalloutActive(false);
            return "";
        }
        this.setCalloutActive(true);
        // JID_1326_1: "1. On Cash journal line, in case of cash type BP Amount should allow to save in -ve and +ve.
        if (Util.getValueOfString(mTab.getValue("CashType")) != "B") {

            if (Util.getValueOfString(mTab.getValue("VSS_PAYMENTTYPE")) == "P") {
                if (Util.getValueOfDecimal(mTab.getValue("amount")) > 0) {
                    mTab.setValue("Amount", (0 - Util.getValueOfDecimal(mTab.getValue("amount"))));
                }
            }
            else if (Util.getValueOfString(mTab.getValue("VSS_PAYMENTTYPE")) == "R") {
                if (Util.getValueOfDecimal(mTab.getValue("amount")) < 0) {
                    mTab.setValue("Amount", (0 - Util.getValueOfDecimal(mTab.getValue("amount"))));
                }
            }
        }
        else if (Util.getValueOfString(mTab.getValue("CashType")) == "B") {// Cash Type = Business Partner
            // Get Payment Type
            var paymenttype = Util.getValueOfString(mTab.getValue("VSS_PAYMENTTYPE"));
            // Payment or Receipt Return
            if ((paymenttype == "P" || paymenttype == "E") && Util.getValueOfDecimal(mTab.getValue("amount")) > 0) {
                mTab.setValue("Amount", (0 - Util.getValueOfDecimal(mTab.getValue("amount"))));
                mTab.setValue("ConvertedAmount", (0 - Util.getValueOfDecimal(mTab.getValue("ConvertedAmount"))));
            }
            // Receipt or Payment Return
            else if ((paymenttype == "R" || paymenttype == "A") && Util.getValueOfDecimal(mTab.getValue("amount")) < 0) {
                mTab.setValue("Amount", (0 - Util.getValueOfDecimal(mTab.getValue("amount"))));
                mTab.setValue("ConvertedAmount", (0 - Util.getValueOfDecimal(mTab.getValue("ConvertedAmount"))));
            }
        }
        this.setCalloutActive(false);
        return "";
    }

    VIS.Model.CalloutSetReadOnly = CalloutSetReadOnly;
    //*******CalloutSetReadOnly Ends**************


    function CalloutCashJournalLine() {
        VIS.CalloutEngine.call(this, "VIS.CalloutCashJournalLine");//must call
    };
    VIS.Utility.inheritPrototype(CalloutCashJournalLine, VIS.CalloutEngine); //inherit prototype
    CalloutCashJournalLine.prototype.SetBPLocation = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            mTab.setValue("C_BPartner_Location_ID", null);
            return "";
        }
        this.setCalloutActive(true);
        try {
            if (mTab.getValue("CashType") == "B" || mTab.getValue("CashType") == "C") {
                var BusinessPartner_ID = mTab.getValue("C_BPartner_ID");
                if (BusinessPartner_ID == null || BusinessPartner_ID == "") {
                    mTab.setValue("C_BPartner_Location_ID", null);
                    this.setCalloutActive(false);
                    return "";
                }
                var paramString = BusinessPartner_ID.toString() + "," + ctx.getAD_Client_ID().toString();
                var data = VIS.dataContext.getJSONRecord("MCashJournal/GetBPLocation", paramString);
                if (data != null) {
                    var CreditStatus = Util.getValueOfInt(data["CreditStatusSettingOn"]);
                    var Location_ID = Util.getValueOfInt(data["C_BPartner_Location_ID"]);
                    if (CreditStatus == "CL" && Location_ID == 0) {
                        this.setCalloutActive(false);
                        return VIS.Msg.getMsg("LocationNew");
                    }
                    mTab.setValue("C_BPartner_Location_ID", Location_ID);
                }
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        this.setCalloutActive(false);
        return "";
    };

    /**
    *To set Account Number and Routing no against bank account selection on journal line
    *@alias    setAccountNo
    *@memberof CalloutCashJournalLine
    *@param {object}   ctx              Context.
    *@param {number}   windowNo         Current Window No.
    *@param {object}   mTab             a GridTab object.
    *@param {object}   mField           Model Field.
    *@param {string}   value            The new value.
    *@param {string}   oldValue         The new value.
    *@return {string} message           Error message or "".
    */
    CalloutCashJournalLine.prototype.setAccountNo = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            //mTab.setValue("C_BankAccount_ID", null);
            return "";
        }
        this.setCalloutActive(true);
        try {
            //if (mTab.getValue("TransferType") == "CK") {
            // JID_1244: On Cash Journal line when we change trasfer type cash to check. Callout should fire and set routing number and account number
            var data = VIS.dataContext.getJSONRecord("MCashJournal/GetBankAccountData", value);
            if (data != null) {
                mTab.setValue("RoutingNo", Util.getValueOfString(data["RoutingNo"]));
                mTab.setValue("AccountNo", Util.getValueOfString(data["AccountNo"]));
            }
            //}
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        this.setCalloutActive(false);
        return "";
    };

    VIS.Model.CalloutCashJournalLine = CalloutCashJournalLine;

})(VIS, jQuery);