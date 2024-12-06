; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    //*************CalloutUserTimeRec Start******************
    function CalloutUserTimeRec() {
        VIS.CalloutEngine.call(this, "VIS.CalloutUserTimeRec");//must call
    };
    VIS.Utility.inheritPrototype(CalloutUserTimeRec, VIS.CalloutEngine); //inherit prototype

    /// <summary>
    /// 
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="WindowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    CalloutUserTimeRec.prototype.IsInternal = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }
        if (this.isCalloutActive()) {
            return "";
        }
        try {
            this.setCalloutActive(true);
            // Util.getValueOfInt(value);
            var paramstring = Util.getValueOfInt(value).toString();
            var pType = VIS.dataContext.getJSONRecord("MExpenseReport/GetProfiletype", paramstring);
            //var sql = "select ProfileType from S_Resource where AD_User_ID = " + Util.getValueOfInt(value);
            //var pType = Util.getValueOfString(VIS.DB.executeScalar(sql, null, null));
            if (pType != "") {
                if (pType.toUpper() == "I") {
                    mTab.setValue("IsInternal", true);
                }
                else if (pType.toUpper() == "E") {
                    mTab.setValue("IsInternal", false);
                }
            }
            this.setCalloutActive(false);
        }
        catch (err) {
            this.setCalloutActive(false);
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutUserTimeRec = CalloutUserTimeRec;
    //**************** CalloutUserTimeRec End ***************

    //*************CalloutTimeExpense Start*******************
    function CalloutTimeExpense() {
        VIS.CalloutEngine.call(this, "VIS.CalloutTimeExpense");//must call
    };
    VIS.Utility.inheritPrototype(CalloutTimeExpense, VIS.CalloutEngine); //inherit prototype



    /// <summary>
    /// Expense Report Line
    //- called from M_Product_ID, S_ResourceAssignment_ID
    //set ExpenseAmt

    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current window no</param>
    /// <param name="mTab">grid tab</param>
    /// <param name="mField">grid field</param>
    /// <param name="value">new value</param>
    /// <returns>null or error message</returns>
    CalloutTimeExpense.prototype.Product = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  

        var M_Product_ID = value;
        if (M_Product_ID == null || Util.getValueOfInt(M_Product_ID) == 0) {
            return "";
        }
        this.setCalloutActive(true);
        var priceActual = null;

        //	get expense date - or default to today's date
        //var DateExpense = VIS.Env.ctx.getContext("DateExpense");
        var DateExpense = ctx.getContext(windowNo, "DateExpense");
        var sql = null;
        var idr = null;
        try {
            var noPrice = true;
            //JID_1784_1 set UOM of the selected product
            var UOM = VIS.dataContext.getJSONRecord("MProduct/GetProduct", M_Product_ID.toString());
            mTab.setValue("C_UOM_ID", Util.getValueOfInt(UOM.C_UOM_ID));
            //	Search Pricelist for current version
            uom = mTab.getValue("C_UOM_ID");
            //  var paramString = paramStr = M_Product_ID.toString().concat(',').concat(mTab.getValue("S_TimeExpense_ID").toString()).concat(',').concat(mTab.getValue("C_UOM_ID").toString().concat(',').concat(mTab.getValue("DateExpense").toString()));
            var paramString = paramStr = M_Product_ID + "," + mTab.getValue("S_TimeExpense_ID") + "," + mTab.getValue("C_UOM_ID") + "," + mTab.getValue("DateExpense");

            var price = VIS.dataContext.getJSONRecord("MExpenseReport/GetstandardPrice", paramString);
            //sql = "SELECT pp.PriceStd, "
            //    + "pp.C_UOM_ID,pv.ValidFrom,pl.C_Currency_ID "
            //    + "FROM M_Product p, M_ProductPrice pp, M_PriceList pl, M_PriceList_Version pv "
            //    + "WHERE p.M_Product_ID=pp.M_Product_ID"
            //    + " AND pp.M_PriceList_Version_ID=pv.M_PriceList_Version_ID"
            //    + " AND pv.M_PriceList_ID=pl.M_PriceList_ID"
            //    + " AND pv.IsActive='Y'"
            //    + " AND p.M_Product_ID=@param1"		//	1
            //    + " AND pl.M_PriceList_ID=@param2"	//	2
            //    + " AND pp.C_UOM_ID= " + uom
            //    + " ORDER BY pv.ValidFrom DESC";
            ////PreparedStatement pstmt = DataBase.prepareStatement(sql, null);
            //var param = [];
            ////pstmt.setInt(1, M_Product_ID.intValue());
            //param[0] = new VIS.DB.SqlParam("@param1", Util.getValueOfInt(M_Product_ID));
            ////pstmt.setInt(2, ctx.getContextAsInt(windowNo, "M_PriceList_ID"));
            //param[1] = new VIS.DB.SqlParam("@param2", ctx.getContextAsInt(windowNo, "M_PriceList_ID"));
            ////ResultSet rs = pstmt.executeQuery();
            //idr = VIS.DB.executeReader(sql, param, null);
            //while (idr.read() && noPrice) {
            //    // DateTime plDate = rs.GetDateTime("ValidFrom");
            //    var plDate = idr.get("validfrom");//.GetDateTime("ValidFrom");
            //    //	we have the price list
            //    //	if order date is after or equal PriceList validFrom
            //    // if (plDate == null || !DateExpense.before(plDate))
            //    if (plDate == null || !(DateExpense < plDate)) {
            //        noPrice = false;
            //        //	Price
            //        //priceActual =Util.getValueOfDecimal(idr["PriceStd"]);//.GetDecimal("PriceStd");
            //        priceActual = Util.getValueOfDecimal(idr.get("pricestd"));//.GetDecimal("PriceStd");

            //        if (priceActual == null) {
            //            priceActual = Util.getValueOfDecimal(idr.get("pricelist"));//.GetDecimal("PriceList");
            //        }
            //        if (priceActual == null) {
            //            priceActual = Util.getValueOfDecimal(idr.get("pricelimit"));//.GetDecimal("PriceLimit");
            //        }
            //        //	Currency
            //        var ii = Util.getValueOfInt(idr.get("c_currency_id"));
            //        if (!(idr == null)) {
            //            mTab.setValue("C_Currency_ID", ii);
            //        }
            //    }
            //}
            //idr.close();
            var priceActual = price["PriceActual"];
            var currency = price["C_Currency_ID"]
            mTab.setValue("ExpenseAmt", priceActual);
            mTab.setValue("C_Currency_ID", currency);
            mTab.setValue("ConvertedAmt", priceActual);
            //	no prices yet - look base pricelist
            //if (noPrice) {
            //    //	Find if via Base Pricelist
            //    sql = "SELECT bomPriceStd(p.M_Product_ID,pv.M_PriceList_Version_ID) AS PriceStd,"
            //        + "bomPriceList(p.M_Product_ID,pv.M_PriceList_Version_ID) AS PriceList,"
            //        + "bomPriceLimit(p.M_Product_ID,pv.M_PriceList_Version_ID) AS PriceLimit,"
            //        + "p.C_UOM_ID,pv.ValidFrom,pl.C_Currency_ID "
            //        + "FROM M_Product p, M_ProductPrice pp, M_PriceList pl, M_PriceList bpl, M_PriceList_Version pv "
            //        + "WHERE p.M_Product_ID=pp.M_Product_ID"
            //        + " AND pp.M_PriceList_Version_ID=pv.M_PriceList_Version_ID"
            //        + " AND pv.M_PriceList_ID=bpl.M_PriceList_ID"
            //        + " AND pv.IsActive='Y'"
            //        + " AND bpl.M_PriceList_ID=pl.BasePriceList_ID"	//	Base
            //        + " AND p.M_Product_ID=@param1"		//  1
            //        + " AND pl.M_PriceList_ID=@param2"	//	2
            //        + " ORDER BY pv.ValidFrom DESC";
            //    var param1 = [];
            //    //pstmt = DataBase.prepareStatement(sql, null);
            //    //pstmt.setInt(1, M_Product_ID.intValue());
            //    param1[0] = new VIS.DB.SqlParam("@param1", Util.getValueOfInt(M_Product_ID));

            //    //pstmt.setInt(2, ctx.getContextAsInt(windowNo, "M_PriceList_ID"));
            //    param1[1] = new VIS.DB.SqlParam("@param2", ctx.getContextAsInt(windowNo, "M_PriceList_ID"));
            //    //rs = pstmt.executeQuery();
            //    idr = VIS.DB.executeReader(sql, param1, null);
            //    while (idr.read() && noPrice) {
            //        var plDate = idr.get("validfrom");//.GetDateTime("ValidFrom");
            //        //	we have the price list
            //        //	if order date is after or equal PriceList validFrom
            //        if (plDate == null || !(DateExpense < plDate)) {
            //            noPrice = false;
            //            //	Price
            //            priceActual = Util.getValueOfDecimal(idr.get("pricestd"));//.GetDecimal("PriceStd");
            //            if (priceActual == null) {
            //                priceActual = Util.getValueOfDecimal(idr.get("pricelist"));//.GetDecimal("PriceList");
            //            }
            //            if (priceActual == null) {
            //                priceActual = Util.getValueOfDecimal(idr.get("pricelimit"));//.GetDecimal("PriceLimit");
            //            }
            //            //	Currency
            //            var ii = Util.getValueOfInt(idr.get("c_currency_id"));
            //            if (!(idr == null)) {
            //                mTab.setValue("C_Currency_ID", ii);
            //            }
            //        }
            //    }
            //    idr.close();
            //}

        }
        catch (err) {
            if (idr != null) {
                idr.close();
            }
            this.log.log(Level.SEVERE, sql, err);
            this.setCalloutActive(false);
            return e.message;//.getLocalizedMessage();
        }

        //	finish
        this.setCalloutActive(false);	//	calculate amount
        //if (priceActual == null)
        //    priceActual = VIS.Env.ZERO;
        //mTab.setValue("ExpenseAmt", priceActual);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };	//	Expense_Product

    /// <summary>
    ///  Expense - Amount.- called from ExpenseAmt, C_Currency_ID,- calculates ConvertedAmt
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current Window No</param>
    /// <param name="mTab">Grid Tab</param>
    /// <param name="mField">Grid Field</param>
    /// <param name="value">New Value</param>
    /// <returns> null or error message</returns>
    CalloutTimeExpense.prototype.Amount = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive()) {
            return "";
        }
        this.setCalloutActive(true);

        //	get values
        var ExpenseAmt = mTab.getValue("ExpenseAmt");
        // did changes to correct the logic for conversion and to consider expense date in conversion.-Mohit
        //var C_Currency_From_ID = mTab.getValue("C_Currency_ID");        
        //var C_Currency_To_ID = ctx.getContextAsInt(windowNo, "$C_Currency_ID");
        var C_Currency_To_ID = 0;
        var C_Currency_From_ID = mTab.getValue("C_Currency_ID");
        C_Currency_To_ID = VIS.dataContext.getJSONRecord("MExpenseReport/GetPriceListCurrency", mTab.getValue("S_TimeExpense_ID"));
        //DateTime DateExpense = new DateTime(ctx.getContextAsTime(windowNo, "DateExpense"));
        var DateExpense = ctx.getContext(windowNo, "DateExpense");
        //
        this.log.fine("Amt=" + ExpenseAmt + ", C_Currency_ID=" + C_Currency_From_ID);
        //	Converted Amount = Unit price
        var ConvertedAmt = ExpenseAmt.toString();
        //	convert if required
        if (!ConvertedAmt.equals(VIS.Env.ZERO) && C_Currency_To_ID != Util.getValueOfInt(C_Currency_From_ID)) {
            var AD_Client_ID = ctx.getContextAsInt(windowNo, "AD_Client_ID");
            var AD_Org_ID = ctx.getContextAsInt(windowNo, "AD_Org_ID");
            var paramString = ConvertedAmt.toString() + "," + C_Currency_From_ID.toString() + "," + C_Currency_To_ID.toString() + "," + DateExpense +
                "," + 0 + "," + AD_Client_ID.toString() + "," + AD_Org_ID.toString();

            //ConvertedAmt = VAdvantage.Model.MConversionRate.Convert(ctx,
            //    ConvertedAmt, Util.getValueOfInt(C_Currency_From_ID), C_Currency_To_ID,
            //    DateExpense, 0, AD_Client_ID, AD_Org_ID);
            // ConvertedAmt = VIS.dataContext.getJSONRecord("MConversionRate/Convert", paramString);

            // called currencyconvert method to calculate the conversion on basis of expense date also.
            ConvertedAmt = VIS.dataContext.getJSONRecord("MConversionRate/CurrencyConvert", paramString);
        }
        mTab.setValue("ConvertedAmt", ConvertedAmt);
        this.log.fine("= ConvertedAmt=" + ConvertedAmt);

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };	//	Expense_Amount

    //To set price in Expense amount when UOM is selected on Report line window
    CalloutTimeExpense.prototype.SetPrice = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        if (this.isCalloutActive() || value == null)
            return "";
        this.setCalloutActive(true);
        try {
            var paramStr = "";
            var M_Product_ID = ctx.getContextAsInt(windowNo, "M_Product_ID");
            //  paramStr = M_Product_ID.toString().concat(',').concat(mTab.getValue("S_TimeExpense_ID").toString()).concat(',').concat(Util.getValueOfString(mTab.getValue("C_UOM_ID")));
            paramStr = M_Product_ID + "," + mTab.getValue("S_TimeExpense_ID") + "," + mTab.getValue("C_UOM_ID");

            var prices = VIS.dataContext.getJSONRecord("MExpenseReport/GetPrices", paramStr);

            if (prices != null) {

                var PriceStd = prices["PriceStd"];
                mTab.setValue("ExpenseAmt", PriceStd);
                mTab.setValue("ConvertedAmt", PriceStd);
            }
            // To set UOM when charge is selected
            if (mTab.getValue("C_Charge_ID") > 0) {
                  var c_uom_id = ctx.getContextAsInt("#C_UOM_ID");
                if (c_uom_id > 0) {
                    mTab.setValue("C_UOM_ID", c_uom_id);	//	Default UOM from context.
                }
                else {
                    mTab.setValue("C_UOM_ID", 100);
                }
                var chargeID = VIS.Utility.Util.getValueOfInt(mTab.getValue("C_Charge_ID"));
                var paramString = chargeID.toString();
                var chargeamt = VIS.dataContext.getJSONRecord("MExpenseReport/GetChargeAmount", paramString);
                mTab.setValue("ExpenseAmt", chargeamt);
                mTab.setValue("ConvertedAmt", chargeamt);
            }
            this.setCalloutActive(false);
            return;
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        this.setCalloutActive(false);
        return "";
    };

    //autofills customer in customer field on selection of request field or project field in Report line tab in time and expense report window
    CalloutTimeExpense.prototype.SetCustomerData = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "" ) {
            return "";
        }
        this.setCalloutActive(true);
        var fieldStatus = mField.getColumnName(); var columnId = value;

        var paramString = columnId + "," + fieldStatus;
        var res = VIS.dataContext.getJSONRecord("MExpenseReport/LoadCustomerData", paramString);
        if (res != null && res.Customer>0) {
            mTab.setValue("C_BPartner_ID", res.Customer);
            mTab.setValue("C_BPartner_Location_ID", res.Location);
        }
        else {
        mTab.setValue("C_BPartner_ID",0);
        }
        this.setCalloutActive(false);
        return "";
    };


    //for fetching M_PRODUCT_ID, C_UOM_ID from database according to Id  selected in
    // projectphase or project task or product field in in Report line tab in time and expense report window
    // and set uom field to 'each' on selection of charge field
    CalloutTimeExpense.prototype.setProductData = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "" || mTab.getValue("C_Charge_ID") > 0 || mTab.getValue("IsTimeReport") == false) {
            return "";
        }  
        this.setCalloutActive(true);
        var columnName = mField.getColumnName();
        var paramString = value + "," + columnName;
        var res = VIS.dataContext.getJSONRecord("MExpenseReport/LoadProductData", paramString);

        if (res != null && res.M_Product_ID > 0 ) {
            mTab.setValue("M_Product_ID", res.M_Product_ID);
            mTab.setValue("C_UOM_ID", res.C_UOM_ID);
        }

        this.setCalloutActive(false);
        return "";
    };

    //	CalloutTimeExpense
    VIS.Model.CalloutTimeExpense = CalloutTimeExpense;
    //***************CalloutTimeExpense End*****************

})(VIS, jQuery);