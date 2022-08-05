; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    function CalloutBankStatement() {
        VIS.CalloutEngine.call(this, "VIS.CalloutBankStatement"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutBankStatement, VIS.CalloutEngine);//inherit CalloutEngine
    /// <summary>
    /// Bank Account Changed.
    /// Update Beginning Balance
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">window no</param>
    /// <param name="mTab">tab</param>
    /// <param name="mField">field</param>
    /// <param name="value">value</param>
    /// <returns>null or error message</returns>
    CalloutBankStatement.prototype.BankAccount = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }

        try {
            var C_BankAccount_ID = value;//).intValue();

            var paramString = value.toString();


            //Get BankAccount information
            var balance;
            balance = VIS.dataContext.getJSONRecord("MBankStatement/GetCurrentBalance", paramString);


            //dr = jQuery.parseJSON(VIS.dataContext.getJSONRecord("MBankStatement/GetBankAccount", paramString));
            //MBankAccount ba = MBankAccount.Get(ctx, C_BankAccount_ID);
            mTab.setValue("BeginningBalance", balance);
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.log(Level.SEVERE, "", err);

        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// BankStmt - Amount.
    /// Calculate ChargeAmt = StmtAmt - TrxAmt - InterestAmt
    /// or id Charge is entered - InterestAmt = StmtAmt - TrxAmt - ChargeAmt
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">window no</param>
    /// <param name="mTab">tab</param>
    /// <param name="mField">field</param>
    /// <param name="value">value</param>
    /// <returns>null or error message</returns>
    CalloutBankStatement.prototype.Amount = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        if (this.isCalloutActive()) {
            return "";
        }

        this.setCalloutActive(true);

        var stmt = mTab.getValue("StmtAmt");
        if (stmt == null) {
            stmt = 0;
        }
        var trx = mTab.getValue("TrxAmt");
        if (trx == null) {
            trx = 0;
        }
        //Update by Pratap: 18/11/15 - To set Charge and Statement Amount 
        var charge = mTab.getValue("ChargeAmt");
        if (charge == null) {
            charge = 0;
        }

        //var _chargeAmt = stmt - trx;
        //var _statementAmt = stmt - (_chargeAmt - charge);
        //if (mField.getColumnName() == "StmtAmt") {
        //    mTab.setValue("ChargeAmt", _chargeAmt);
        //}
        //else if (mField.getColumnName() == "ChargeAmt") {
        //    mTab.setValue("StmtAmt", _statementAmt);
        //}

        // JID_0333: Currently charge is only calculte only on change Statement Amount it should also be calcualte on change Interest Amount
        var bd = stmt - trx;
        //  Charge - calculate Interest
        if (mField.getColumnName() == "ChargeAmt") {
            var charge = value;
            if (charge == null) {
                charge = 0;
            }

            bd = bd - charge;
            mTab.setValue("InterestAmt", bd);
        }
        //      Calculate Charge
        else {
            var interest = mTab.getValue("InterestAmt");
            if (interest == null) {
                interest = 0;
            }

            bd = bd - interest;
            mTab.setValue("ChargeAmt", bd);
        }

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    //CalloutBankStatement.prototype.Amount = function (ctx, windowNo, mTab, mField, value, oldValue) {
    //    if (value == null || value.toString() == "") {
    //        return "";
    //    }
    //    if (this.isCalloutActive()) {
    //        return "";
    //    }
    //    this.setCalloutActive(true);

    //      Get Stmt & Trx
    //    var stmt = mTab.getValue("StmtAmt");
    //    if (stmt == null) {
    //        stmt = 0;
    //    }
    //    var trx = mTab.getValue("TrxAmt");
    //    if (trx == null) {
    //        trx = 0;
    //    }
    //    var bd = stmt - trx;
    //      Charge - calculate Interest
    //    if (mField.getColumnName() == "ChargeAmt") {
    //        var charge = value;
    //        if (charge == null) {
    //            charge = 0;
    //        }
    //        bd = Decimal.Subtract(bd, charge);
    //        bd = bd - charge;
    //        log.trace(log.l5_DData, "Interest (" + bd + ") = Stmt(" + stmt + ") - Trx(" + trx + ") - Charge(" + charge + ")");
    //        mTab.setValue("InterestAmt", bd);
    //    }
    //          Calculate Charge
    //    else {
    //        var interest = mTab.getValue("InterestAmt");
    //        if (interest == null) {
    //            interest = 0;
    //        }
    //          bd = Decimal.Subtract(bd, interest);
    //        bd = bd - interest;
    //        log.trace(log.l5_DData, "Charge (" + bd + ") = Stmt(" + stmt + ") - Trx(" + trx + ") - Interest(" + interest + ")");
    //        mTab.setValue("ChargeAmt", bd);
    //    }
    //    this.setCalloutActive(false);
    //    ctx = windowNo = mTab = mField = value = oldValue = null;
    //    return "";
    //};
    /// <summary>
    /// BankStmt - Payment.
    /// Update Transaction Amount when payment is selected
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">window no</param>
    /// <param name="mTab">tab</param>
    /// <param name="mField">field</param>
    /// <param name="value">value</param>
    /// <returns>null or error message</returns>
    CalloutBankStatement.prototype.Payment = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            // JID_0333: Once user remove the payment reference Reset column Statement Amount, Transaction Amount, Charge Amount, Interest Amount, business partner and Invoice reference
            mTab.setValue("StmtAmt", 0);
            mTab.setValue("TrxAmt", 0);
            mTab.setValue("ChargeAmt", 0);
            mTab.setValue("C_BPartner_ID", 0);
            mTab.setValue("C_Invoice_ID", 0);
            //clear the C_ConversionType_ID
            if (mTab.findColumn("C_ConversionType_ID") >= 0) {
                mTab.setValue("C_ConversionType_ID", 0);
            }
            //setCalloutActive as false
            this.setCalloutActive(false);
            return "";
        }

        this.setCalloutActive(true);

        var C_Payment_ID = mTab.getValue("C_Payment_ID");
        if (C_Payment_ID == null || C_Payment_ID == 0) {
            mTab.setValue("StmtAmt", 0);
            mTab.setValue("TrxAmt", 0);
            mTab.setValue("ChargeAmt", 0);
            mTab.setValue("C_BPartner_ID", 0);
            mTab.setValue("C_Invoice_ID", 0);
            //clear the C_ConversionType_ID if C_Payment_ID is zero or null
            if (mTab.findColumn("C_ConversionType_ID") >= 0) {
                mTab.setValue("C_ConversionType_ID", 0);
            }
            //setCalloutActive as false
            this.setCalloutActive(false);
            return "";
        }
        //
        var stmt = mTab.getValue("StmtAmt");
        if (stmt == null) {
            stmt = 0;
        }

        //JID_0084: if the Payment currency is different from the bank statement currency it will add the converted amount based in the currency conversion available for selected date.
        var C_Currency_ID = mTab.getValue("C_Currency_ID");
        var statementDate = mTab.getValue("DateAcct"); /*ValutaDate*/
        //Get the Org_ID from the StatementLine - Tab
        var org_Id = mTab.getValue("AD_Org_ID");

        // JID_1418: When select payment on Bank statement line, system gives an error meassage
        //When select payment on Bank statement line with out select the statmenet Date, return error meassage
        if (statementDate == null) {
            //statementDate = new Date();
            mTab.setValue("C_Payment_ID", 0);
            this.setCalloutActive(false);
            return "PlzSelectStmtDate";
        }

        //var sql = "SELECT PayAmt FROM C_Payment_v WHERE C_Payment_ID=@C_Payment_ID";		//	1
        //var dr = null;
        //var param = [];
        try {
            //passed statement Date, Org_ID as in JSON format
            var paramStr = C_Payment_ID.toString() + "," + C_Currency_ID.toString() + "," + statementDate + "," + org_Id.toString();//JSON.stringify(statementDate)
            var payAmt = VIS.dataContext.getJSONRecord("MBankStatement/GetPayment", paramStr);
            //Update the BankStatementLine fields
            if (payAmt != null && payAmt.length > 0) {
                if (payAmt[0]["ConvertedAmt"] != "") {
                    mTab.setValue("C_Payment_ID", 0);
                    this.setCalloutActive(false);
                    return payAmt[0]["ConvertedAmt"];
                }
                mTab.setValue("TrxAmt", payAmt[0]["payAmt"]);
                mTab.setValue("StmtAmt", payAmt[0]["payAmt"]);

                //to Avoid Exception if Column not exists Used findColumn()
                if (mTab.findColumn("C_ConversionType_ID") >= 0) {
                    mTab.setValue("C_ConversionType_ID", payAmt[0]["C_ConversionType_ID"]);
                }
                //Update PaymentMethod, CheckNo and CheckDate When select the Payment from Bank Statement Line
                if (mTab.findColumn("VA009_PaymentMethod_ID") >= 0 && mTab.getValue("VA009_PaymentMethod_ID") > 0) {
                    mTab.setValue("VA009_PaymentMethod_ID", payAmt[0]["VA009_PaymentMethod_ID"]);
                }
                else if (mTab.findColumn("VA009_PaymentMethod_ID") >= 0) {
                    mTab.setValue("VA009_PaymentMethod_ID", null);
                }
                if (mTab.getValue("EFTCheckNo") == null && payAmt[0]["EFTCheckNo"] != null) {
                    mTab.setValue("EFTCheckNo", payAmt[0]["EFTCheckNo"]);
                }
                if (payAmt[0]["EFTValutaDate"] != null) {
                    mTab.setValue("EFTValutaDate", payAmt[0]["EFTValutaDate"]);
                }
                if (mTab.findColumn("TenderType") >= 0 && payAmt[0]["TenderType"] != null) {
                    mTab.setValue("TenderType", payAmt[0]["TenderType"]);
                }
                //Requirement change no need to update DateAcct by the Payment reference
                //mTab.setValue("DateAcct", Globalize.format(new Date(payAmt[0]["DateAcct"]), "yyyy-MM-dd"));
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.log(Level.SEVERE, "BankStmt_Payment", err);
            return err.toString();
        }
        this.setCalloutActive(false);
        //  Recalculate Amounts
        this.Amount(ctx, windowNo, mTab, mField, value);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    // Callout on change of Account Date on Bank Statement Line.
    CalloutBankStatement.prototype.SetConvertedAmt = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }

        var C_Payment_ID = Util.getValueOfInt(mTab.getValue("C_Payment_ID"));

        if (C_Payment_ID == 0) {
            return "";
        }

        //var stmt = mTab.getValue("StmtAmt");
        //if (stmt == null) {
        //    stmt = 0;
        //}

        var C_Currency_ID = mTab.getValue("C_Currency_ID");
        var acctDate = mTab.getValue("DateAcct"); /*ValutaDate*/
        //When select payment on Bank statement line, system gives an error meassage
        if (acctDate == null) {
            return "PlzSelectStmtDate";
        }

        try {
            var paramStr = C_Payment_ID.toString() + "," + C_Currency_ID.toString() + "," + acctDate.toString();
            var payAmt = VIS.dataContext.getJSONRecord("MBankStatement/GetConvertedAmt", paramStr);
            mTab.setValue("StmtAmt", payAmt);
            //set transcation Amount also if change the date
            mTab.setValue("TrxAmt", payAmt);
            //if (stmt == 0) {
            //    mTab.setValue("StmtAmt", payAmt);
            //}
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.log(Level.SEVERE, "BankStmt_DateAcct", err);
            return err.toString();
        }

        //  Recalculate Amounts
        this.Amount(ctx, windowNo, mTab, mField, value);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    VIS.Model.CalloutBankStatement = CalloutBankStatement;

})(VIS, jQuery);