; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;
    var steps = false;
    var countEd011 = 0;


    //*************CalloutOrderLine Start**************
    function CalloutOrderLine() {
        VIS.CalloutEngine.call(this, "VIS.CalloutOrderLine"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutOrderLine, VIS.CalloutEngine);//inherit CalloutEngine

    CalloutOrderLine.prototype.EndDate = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  

        if (value == null || value.toString() == "") {
            return ""; //Must sir but it was not the case
        }
        var SDate = new Date(mTab.getValue("StartDate"));
        //var frequency = Util.getValueOfInt(mTab.getValue("C_Frequency_ID"));
        //var Sql = "Select NoOfDays from C_Frequency where C_Frequency_ID=" + frequency; //By Sarab
        //var Sql = "Select NoOfMonths from C_Frequency where C_Frequency_ID=" + frequency;
        //var days = Util.getValueOfInt(VIS.DB.executeScalar(Sql, null, null));

        var months = VIS.dataContext.getJSONRecord("MOrderLine/GetNoOfMonths", Util.getValueOfString(mTab.getValue("C_Frequency_ID")));
        var invoice = Util.getValueOfInt(mTab.getValue("NoofCycle"));
        //var End = SDate.addDays(days * invoice);     //By sarab 
        //var End = SDate.setMonth(SDate.getMonth() + (days * invoice));        // By Karan
        var End = SDate.getDate() + "/" + (SDate.getMonth() + (months * invoice)) + "/" + SDate.getFullYear();
        if (End <= 0) {
            End = new Date();
        }
        else {
            End = new Date(End);
        }
        End = End.toISOString();
        mTab.setValue("EndDate", End);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    CalloutOrderLine.prototype.Qty = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  

        if (value == null || value.toString() == "") {
            return "";
        }
        var Cycles = Util.getValueOfDecimal(value);
        var cyclesCount = Util.getValueOfDecimal(mTab.getValue("QtyPerCycle"));
        var qty = Cycles * cyclesCount;
        mTab.setValue("QtyEntered", qty);
        mTab.setValue("QtyOrdered", qty);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    CalloutOrderLine.prototype.QtyPerCycle = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }
        var cyclesCount = Util.getValueOfDecimal(value);
        var Cycles = Util.getValueOfDecimal(mTab.getValue("NoofCycle"));
        var qty = Cycles * cyclesCount;
        mTab.setValue("QtyEntered", qty);
        mTab.setValue("QtyOrdered", qty);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutOrderLine = CalloutOrderLine;
    //**************CalloutOrderLine End*****************


    //***********CalloutOrderlineRecording Start********
    function CalloutOrderlineRecording() {
        VIS.CalloutEngine.call(this, "VIS.CalloutOrderlineRecording"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutOrderlineRecording, VIS.CalloutEngine);//inherit CalloutEngine

    CalloutOrderlineRecording.prototype.Orderline = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            return "";
        }
        if (this.isCalloutActive()) {
            return "";
        }
        try {
            this.setCalloutActive(true);
            var paramString = Util.getValueOfInt(value);
            var dr = VIS.dataContext.getJSONRecord("MOrderLine/GetOrderLine", paramString);

            //X_C_OrderLine ol = new X_C_OrderLine(Env.GetCtx(), Util.getValueOfInt(value), null);
            mTab.setValue("M_Product_ID", dr["M_Product_ID"]);
            mTab.setValue("Qty", dr["Qty"]);
            mTab.setValue("C_UOM_ID", dr["C_UOM_ID"]);
            mTab.setValue("C_BPartner_ID", dr["C_BPartner_ID"]);
            mTab.setValue("PlannedHours", dr["PlannedHours"]);
            this.setCalloutActive(false);
        }
        catch (err) {
            this.setCalloutActive(false);
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutOrderlineRecording = CalloutOrderlineRecording;
    //***********CalloutOrderlineRecording End********

    //VIS430:Set Product,Charge,Uom and Attribute set Instance when select contract line id
    //***********CalloutContractLineStart********
    function CalloutContractLine() {
        VIS.CalloutEngine.call(this, "VIS.CalloutContractLine"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutContractLine, VIS.CalloutEngine);//inherit CalloutEngine

    CalloutContractLine.prototype.Contractlines = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "" || Util.getValueOfInt(value) == 0) {
            return "";
        }
        if (this.isCalloutActive()) {
            return "";
        }
        try {
            this.setCalloutActive(true);
            var paramString = Util.getValueOfInt(value);
            var cl = VIS.dataContext.getJSONRecord("MVASContract/GetContractData", paramString);
            if (cl!= null) {
                if (Util.getValueOfInt(cl.M_Product) > 0) {
                    mTab.setValue("M_Product_ID", Util.getValueOfInt(cl.M_Product));
                }
                if (Util.getValueOfInt(cl.C_Charge) > 0) {
                    mTab.setValue("C_Charge_ID", Util.getValueOfInt(cl.C_Charge));
                }
                mTab.setValue("M_AttributeSetInstance_ID", cl["M_AttributeSetInstance"]);
                mTab.setValue("C_UOM_ID", cl["C_UOM"]);
                mTab.setValue("QtyEntered", 1);

                var M_Product_ID = Util.getValueOfInt(mTab.getValue("M_Product_ID"));
                var C_BPartner_ID = Util.getValueOfInt(mTab.getValue("C_BPartner_ID"));
                var isSOTrx = (ctx.getWindowContext(windowNo, "IsSOTrx", true) == "Y");
                var params = M_Product_ID.toString().concat(",", (mTab.getValue("C_Order_ID")).toString() +
                    "," + Util.getValueOfString(mTab.getValue("M_AttributeSetInstance_ID")) +
                    "," + Util.getValueOfString(mTab.getValue("C_UOM_ID")) + "," + ctx.getAD_Client_ID().toString() +
                    "," + C_BPartner_ID.toString() +
                    "," + (mTab.getValue("QtyEntered")).toString() +
                    "," + isSOTrx + "," + 1 + "," + 1);
                var prices = VIS.dataContext.getJSONRecord("MOrderLine/GetPricesOnChange", params);
                PriceListPrecision = Util.getValueOfInt(params["PriceListPrecision"]);
                PriceList = Util.getValueOfDecimal(prices["PriceList"]);
                mTab.setValue("PriceList", PriceList);
                PriceEntered = Util.getValueOfDecimal(prices["PriceEntered"]);
                mTab.setValue("PriceEntered", PriceEntered);
                PriceActual = Util.getValueOfDecimal(prices["PriceEntered"].toFixed(PriceListPrecision));
                mTab.setValue("PriceActual", PriceActual);
                
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            return err;
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutContractLine = CalloutContractLine;
    //***********CalloutContractLine End********


})(VIS, jQuery);