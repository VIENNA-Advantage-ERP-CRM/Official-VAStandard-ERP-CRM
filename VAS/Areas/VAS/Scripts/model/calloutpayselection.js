; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    function CalloutPaySelection() {
        VIS.CalloutEngine.call(this, "VIS.CalloutPaySelection"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutPaySelection, VIS.CalloutEngine);//inherit CalloutEngine
    /// <summary>
    /// Payment Selection Line - Payment Amount.
    /// - called from C_PaySelectionLine.PayAmt
    /// - update DifferenceAmt
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="WindowNo">current Window No</param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value">New Value</param>
    /// <returns> null or error message</returns>
    CalloutPaySelection.prototype.PayAmt = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  

        if (this.isCalloutActive() || value == null || value == "") {
            return "";
        }
        //	get invoice info
        var ii = Util.getValueOfInt(mTab.getValue("C_Invoice_ID"));
        if (ii == null) {
            return "";
        }
        var C_Invoice_ID = ii;// ii.intValue();
        if (C_Invoice_ID == 0) {
            return "";
        }
        //
        var OpenAmt = Util.getValueOfDecimal(mTab.getValue("OpenAmt"));
        var PayAmt = Util.getValueOfDecimal(mTab.getValue("PayAmt"));
        var DiscountAmt = Util.getValueOfDecimal(mTab.getValue("DiscountAmt"));
        this.setCalloutActive(true);
        // var DifferenceAmt = Decimal.Subtract(Decimal.Subtract(OpenAmt, PayAmt), DiscountAmt);

        var DifferenceAmt = ((OpenAmt - PayAmt) - DiscountAmt);


        this.log.fine(" - OpenAmt=" + OpenAmt + " - PayAmt=" + PayAmt
            + ", Discount=" + DiscountAmt + ", Difference=" + DifferenceAmt);

        mTab.setValue("DifferenceAmt", DifferenceAmt);

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// Payment Selection Line - Invoice.
    /// - called from C_PaySelectionLine.C_Invoice_ID
    /// - update PayAmt & DifferenceAmt
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="WindowNo">current Window No</param>
    /// <param name="mTab">Grid Tab</param>
    /// <param name="mField">Grid Field</param>
    /// <param name="value">New Value</param>
    /// <returns>null or error message</returns>
    CalloutPaySelection.prototype.Invoice = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive() || value == null || value == "") {
            return "";
        }
        //	get value
        var C_Invoice_ID = Util.getValueOfInt(value);
        if (C_Invoice_ID == 0) {
            return "";
        }

        this.setCalloutActive(true);

        var C_BankAccount_ID = ctx.getContextAsInt(windowNo, "C_BankAccount_ID");
        var PayDate = ctx.getContext("PayDate");
        var OpenAmt = VIS.Env.ZERO;
        var DiscountAmt = VIS.Env.ZERO;
        var isSOTrx = false;


        var idr = null;
        try {
            var fields = C_Invoice_ID.toString() + ", " + C_BankAccount_ID.toString() + "," + PayDate.toString();
            idr = VIS.dataContext.getJSONRecord("MInvoice/GetInvoiceOpenDetail", fields);
            if (idr != null && Object.keys(idr).length > 0) {
                OpenAmt = Util.getValueOfDecimal(idr["OpenAmt"]);
                DiscountAmt = Util.getValueOfDecimal(idr["DiscountAmt"]);
                IsSOTrx = "Y" == Util.getValueOfString(idr["IsSOTrx"]);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
        }

        this.log.fine(" - OpenAmt=" + OpenAmt + " (Invoice=" + C_Invoice_ID + ",BankAcct=" + C_BankAccount_ID + ")");
        mTab.setValue("OpenAmt", OpenAmt);
        mTab.setValue("PayAmt", (OpenAmt - DiscountAmt));
        mTab.setValue("DiscountAmt", DiscountAmt);
        mTab.setValue("DifferenceAmt", VIS.Env.ZERO);
        mTab.setValue("IsSOTrx", isSOTrx);

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutPaySelection = CalloutPaySelection;

})(VIS, jQuery);