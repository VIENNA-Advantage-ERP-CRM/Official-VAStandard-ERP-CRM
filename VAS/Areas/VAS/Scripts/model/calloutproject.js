; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    function CalloutProject() {
        VIS.CalloutEngine.call(this, "VIS.CalloutProject"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutProject, VIS.CalloutEngine);//inherit CalloutEngine
    /// <summary>
    /// Project Line Planned - Price + Qty.
    //- called from PlannedPrice, PlannedQty, PriceList, Discount
    //- calculates PlannedAmt
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current Window No</param>
    /// <param name="mTab">Grid Tab</param>
    /// <param name="mField">Gridfield</param>
    /// <param name="value">New Value</param>
    /// <returns>null or error message</returns>
    CalloutProject.prototype.Planned = function (ctx, windowNo, mTab, mField, value, oldValue) {
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

        //	VIS0060: Handled internal server error and Call controller method to get limit price.
        var taskID = Util.getValueOfInt(mTab.getValue("C_ProjectTask_ID"));
        var projID = Util.getValueOfInt(mTab.getValue("C_Project_ID"));
        var productID = Util.getValueOfInt(mTab.getValue("M_Product_ID"));

        var paramString = taskID.toString() + "," + projID.toString() + "," + productID.toString();

        var PriceLimit = VIS.dataContext.getJSONRecord("MProject/GetProjectPriceLimit", paramString);

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
    //	planned

    /// <summary>
    /// Project Line Product
    //- called from Product
    //- calculates PlannedPrice, PriceList, Discount
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current window no</param>
    /// <param name="mTab">grid tab</param>
    /// <param name="mField">grid field</param>
    /// <param name="value">new valuw</param>
    /// <returns>null or error message</returns>
    CalloutProject.prototype.Product = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value.toString() == "") {
            return "";
        }
        var M_Product_ID = Util.getValueOfInt(value);
        var M_PriceList_Version_ID = ctx.getContextAsInt(windowNo, "M_PriceList_Version_ID");
        if (M_Product_ID == null || Util.getValueOfInt(M_Product_ID) == 0
            || M_PriceList_Version_ID == 0) {
            return "";
        }
        this.setCalloutActive(true);

        var C_BPartner_ID = ctx.getContextAsInt(windowNo, "C_BPartner_ID");
        var Qty = Util.getValueOfDecimal(mTab.getValue("PlannedQty"));
        var IsSOTrx = true;
        //MProductPricing pp = new MProductPricing(ctx.getAD_Client_ID(), ctx.getAD_Org_ID(),
        //    Util.getValueOfInt(M_Product_ID), C_BPartner_ID, Qty, IsSOTrx);
        //pp.SetM_PriceList_Version_ID(M_PriceList_Version_ID);
        var date = Util.getValueOfDateTime(mTab.getValue("PlannedDate"));
        //DateTime date = (DateTime)mTab.getValue("PlannedDate");
        if (date == null) {
            date = Util.getValueOfDateTime(mTab.getValue("DateContract"));
            if (date == null) {
                date = Util.getValueOfDateTime(mTab.getValue("DateFinish"));
                if (date == null) {
                    //date = new DateTime(System.currentTimeMillis());
                    date = new Date();
                    //date = new DateTime(CommonFunctions.CurrentTimeMillis());// (DateTime)(Util.getValueOfDateTime(CommonFunctions.CurrentTimeMillis()));
                }
            }
            //pp.SetPriceDate(date);
            ////
            //var PriceList = pp.GetPriceList();
            //mTab.setValue("PriceList", PriceList);
            //var PlannedPrice = pp.GetPriceStd();
            //mTab.setValue("PlannedPrice", PlannedPrice);
            //var Discount = pp.GetDiscount();
            //mTab.setValue("Discount", Discount);
            ////
            //var curPrecision = 2;
            //var PlannedAmt = pp.GetLineAmt(curPrecision);
            //mTab.setValue("PlannedAmt", PlannedAmt);


            var paramString = M_Product_ID.toString().concat(",", C_BPartner_ID.toString(), ",", //2
                Qty.toString(), ",", //3
                isSOTrx, ",", //4 
                M_PriceList_ID.toString(), ",", //5
                M_PriceList_Version_ID.toString(), ",", //6
                date.toString(), ",",//7
                null); //8

            //Get product price information
            var dr = null;
            dr = VIS.dataContext.getJSONRecord("MProductPricing/GetProductPricing", paramString);


            var rowDataDB = null;


            // MProductPricing pp = new MProductPricing(ctx.getAD_Client_ID(), ctx.getAD_Org_ID(),
            //     M_Product_ID, C_BPartner_ID, Qty, isSOTrx);


            //		
            mTab.setValue("PriceList", dr["PriceList"]);
            // mTab.setValue("PriceLimit", dr.PriceLimit);
            mTab.setValue("PlannedPrice", dr.PriceActual);
            //mTab.setValue("PriceEntered", dr.PriceEntered);
            //  mTab.setValue("C_Currency_ID", Util.getValueOfInt(dr.C_Currency_ID));
            mTab.setValue("Discount", dr.Discount);
            mTab.setValue("PlannedAmt", dr.LineAmt);


            //	
            //this.log.fine("PlannedQty=" + Qty + " * PlannedPrice=" + PlannedPrice + " -> PlannedAmt=" + PlannedAmt);
            return "";
        }	//	product
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };	//	CalloutProject

    VIS.Model.CalloutProject = CalloutProject;

    //************CalloutProjectLine Start***************
    function CalloutProjectLine() {
        VIS.CalloutEngine.call(this, "VIS.CalloutProjectLine");//must call
    };

    VIS.Utility.inheritPrototype(CalloutProjectLine, VIS.CalloutEngine); //inherit prototype

    CalloutProjectLine.prototype.Charge = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }

        var paramString = mTab.getValue("C_Charge_ID").toString();
        //X_C_Charge charge = new X_C_Charge(ctx, Cid, null);
        //var amt=charge.GetChargeAmt();
        var amt = VIS.dataContext.getJSONRecord("MCharge/GetCharge", paramString);
        mTab.setValue("PlannedQty", 1);
        mTab.setValue("PlannedPrice", amt);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    VIS.Model.CalloutProjectLine = CalloutProjectLine;
    //**************CalloutProjectLine End*************

    //************CalloutProjectLine Start***************
    function CalloutProjectLine() {
        VIS.CalloutEngine.call(this, "VIS.CalloutProjectLine");//must call
    };
    VIS.Utility.inheritPrototype(CalloutProjectLine, VIS.CalloutEngine); //inherit prototype
    CalloutProjectLine.prototype.Charge = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }

        var paramString = mTab.getValue("C_Charge_ID").toString();
        //X_C_Charge charge = new X_C_Charge(ctx, Cid, null);
        //var amt=charge.GetChargeAmt();
        var amt = VIS.dataContext.getJSONRecord("MCharge/GetCharge", paramString);
        mTab.setValue("PlannedQty", 1);
        mTab.setValue("PlannedPrice", amt);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutProjectLine = CalloutProjectLine;
    //**************CalloutProjectLine End*************


    //*************CalloutProjectCBParner Starts***********
    function CalloutProjectCBPartner() {
        VIS.CalloutEngine.call(this, "VIS.CalloutProjectCBPartner");//must call
    };
    VIS.Utility.inheritPrototype(CalloutProjectCBPartner, VIS.CalloutEngine); //inherit prototype


    CalloutProjectCBPartner.prototype.SetAddress = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if ((this.isCalloutActive()) || value == null || value.toString() == "") {
            mTab.setValue("C_BPartner_Location_ID", null);
            mTab.setValue("AD_User_ID", null);
            mTab.setValue("M_PriceList_Version_ID", null);
            return "";
        }
        try {
            this.setCalloutActive(true);
            if (mField.getColumnName() == "C_BPartner_ID") {
                if (mTab.getField("Ref_BPartner_ID") != null) {
                    mTab.setValue("Ref_BPartner_ID", value)
                }
                var dr = null;
                dr = VIS.dataContext.getJSONRecord("MBPartner/GetBPData", Util.getValueOfString(mTab.getValue("C_BPartner_ID")));
                if (dr != null) {
                    var _Location_ID = VIS.Utility.Util.getValueOfInt(dr["C_BPartner_Location_ID"]);
                    if (_Location_ID == 0)
                        mTab.setValue("C_BPartner_Location_ID", null);
                    else
                        mTab.setValue("C_BPartner_Location_ID", _Location_ID);
                    var _User_ID = VIS.Utility.Util.getValueOfInt(dr["AD_User_ID"]);
                    if (_User_ID == 0)
                        mTab.setValue("AD_User_ID", null);
                    else
                        mTab.setValue("AD_User_ID", _User_ID);

                    var M_PriceList_Version_ID = VIS.Utility.Util.getValueOfInt(dr["M_PriceList_Version_ID"]);
                    if (M_PriceList_Version_ID == 0)
                        mTab.setValue("M_PriceList_Version_ID", null);
                    else
                        mTab.setValue("M_PriceList_Version_ID", M_PriceList_Version_ID);
                }
            }
            else if (mField.getColumnName() == "C_BPartnerSR_ID") {
                if (mTab.getField("Ref_BPartner_ID") != null) {
                    mTab.setValue("Ref_BPartner_ID", value)
                }
                var dr = null;
                dr = VIS.dataContext.getJSONRecord("MBPartner/GetBPData", Util.getValueOfString(mTab.getValue("C_BPartnerSR_ID")));
                if (dr != null) {
                    var _Location_ID = VIS.Utility.Util.getValueOfInt(dr["C_BPartner_Location_ID"]);
                    if (_Location_ID == 0)
                        mTab.setValue("C_BPartner_Location_ID", null);
                    else
                        mTab.setValue("C_BPartner_Location_ID", _Location_ID);
                    var _User_ID = VIS.Utility.Util.getValueOfInt(dr["AD_User_ID"]);
                    if (_User_ID == 0)
                        mTab.setValue("AD_User_ID", null);
                    else
                        mTab.setValue("AD_User_ID", _User_ID);

                    var M_PriceList_Version_ID = VIS.Utility.Util.getValueOfInt(dr["M_PriceList_Version_ID"]);
                    if (M_PriceList_Version_ID == 0)
                        mTab.setValue("M_PriceList_Version_ID", null);
                    else
                        mTab.setValue("M_PriceList_Version_ID", M_PriceList_Version_ID);
                }
            }
            //var sql = "SELECT au.ad_user_id,  cl.c_bpartner_location_id FROM c_bpartner cp  INNER JOIN c_bpartner_location cl ON cl.c_bpartner_id=cp.c_bpartner_id INNER JOIN Ad_User au ON au.c_bpartner_id   =cp.c_bpartner_id WHERE cp.c_bpartner_id= " + VIS.Utility.Util.getValueOfString(mTab.getValue("C_BPartner_ID")) + " AND cp.isactive       ='Y'  ORDER BY cp.created";

            //var ds = CalloutDB.executeCalloutDataSet(sql, null);
            //for (var i = 0; i < ds.tables[0].rows.length; i++) {
            //    var dr = ds.tables[0].rows[i];
            //    if (dr != null) {
            //        var _Location_ID = VIS.Utility.Util.getValueOfInt(dr.getCell("C_BPartner_Location_ID"));
            //        if (_Location_ID == 0)
            //            mTab.setValue("C_BPartner_Location_ID", null);
            //        else
            //            mTab.setValue("C_BPartner_Location_ID", _Location_ID);

            //        var _User_ID = VIS.Utility.Util.getValueOfInt(dr.getCell("AD_User_ID"));
            //        if (_User_ID == 0)
            //            mTab.setValue("AD_User_ID", null);
            //        else
            //            mTab.setValue("AD_User_ID", _User_ID);
            //    }
            //}


            //var _locatio_id = Util.getValueOfString(CalloutDB.executeCalloutScalar(sql, null, null));
            //if (parseInt(_locatio_id)>0) {
            //     mTab.setValue("C_BPartner_Location_ID", parseInt(_locatio_id));
            //}

        }
        catch (err) {
            this.setCalloutActive(false);
            return err;
        }
        this.setCalloutActive(false);
        return "";
    }
    VIS.Model.CalloutProjectCBPartner = CalloutProjectCBPartner;
    //*************CalloutProjectCBParner Ends*************

})(VIS, jQuery);