; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;
    var steps = false;

    //*************** VAS_CalloutContract ************  
    function VAS_CalloutContract() {
        VIS.CalloutEngine.call(this, "VIS.VAS_CalloutContract");//must call
    };
    VIS.Utility.inheritPrototype(VAS_CalloutContract, VIS.CalloutEngine); //inherit prototype
    VAS_CalloutContract.prototype.CalculateContDuration = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var SDate = Util.getValueOfDate(mTab.getValue("StartDate"));
        var Edate = Util.getValueOfDate(mTab.getValue("EndDate"));


        if (SDate != 0 && Edate != 0) {

            var totalMonths = (Edate.getDate() - SDate.getDate()) / 30 +
                (Edate.getMonth() - SDate.getMonth() +
                    (12 * (Edate.getFullYear() - SDate.getFullYear())));

            var totalYears = (Edate.getMonth() - SDate.getMonth()) / 12 +
                (Edate.getFullYear() - SDate.getFullYear());
            var Month = (totalMonths % 12);

            mTab.setValue("VAS_ContractMonths", Math.round(Month));
            mTab.setValue("VAS_ContractDuration", Math.round(totalYears));
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";

    };


    /**
      * *************************Callout check End date must be greater than Start date****************************
      * @param {any} ctx
      * @param {any} windowNo
      * @param {any} mTab
      * @param {any} mField
      * @param {any} value
      * @param {any} oldValue
      */
    VAS_CalloutContract.prototype.EndDate = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var startDate = new Date(mTab.getValue("StartDate"));
        var endDate = new Date(value);
        endDate = endDate.toISOString();
        startDate = startDate.toISOString();
        if (mTab.getValue("StartDate") != null) {
            if (endDate < startDate) {
                mTab.setValue("EndDate", null);
                this.setCalloutActive(false);
                return "VAS_EndDateMustGreater";
            }
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    /**
     * *****************Callout check Start Date must be less than End Date********************
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    VAS_CalloutContract.prototype.StartDate = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var startDate = new Date(value);
        var endDate = new Date(mTab.getValue("EndDate"));
        endDate = endDate.toISOString();
        startDate = startDate.toISOString();
        if (mTab.getValue("EndDate") != null) {
            if (endDate < startDate) {
                mTab.setValue("StartDate", null);
                this.setCalloutActive(false);
                return "VAS_EndDateMustGreater";
            }
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    VAS_CalloutContract.prototype.DateDoc = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);

        var DocDate = mTab.getValue("DateDoc");
        var StartDate = mTab.getValue("StartDate");
        if (DocDate != null && StartDate != null) {
            if (DocDate > StartDate && mField.getColumnName() == "DateDoc") {
                mTab.setValue("DateDoc", null);
                this.setCalloutActive(false);
                return "VAS_ContractDateMustGreater";
            }
            if (DocDate > StartDate && mField.getColumnName() == "StartDate") {
                mTab.setValue("StartDate", null);
                this.setCalloutActive(false);
                return "VAS_ContractDateMustGreater";
            }
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };


    VAS_CalloutContract.prototype.ContractRef = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var result = VIS.dataContext.getJSONRecord("MVASContract/GetContractDetails", Util.getValueOfInt(value));
        if (result) {
            if (result["IsExpiredContracts"] == 'Y') {          //Check Contract Expired or not
                this.setCalloutActive(false);
                return "VAS_ContractExpired";
            }
            mTab.setValue("ContractType", result["ContractType"]);
            mTab.setValue("C_BPartner_ID", result["C_BPartner_ID"]);
            mTab.setValue("Bill_Location_ID", result["Bill_Location_ID"]);
            mTab.setValue("Bill_User_ID", result["Bill_User_ID"]);
            mTab.setValue("VAS_Jurisdiction", result["VAS_Jurisdiction"]);
            mTab.setValue("C_Currency_ID", result["C_Currency_ID"]);
            mTab.setValue("C_IncoTerm_ID", result["C_IncoTerm_ID"]);
            mTab.setValue("C_PaymentTerm_ID", result["C_PaymentTerm_ID"]);
            mTab.setValue("C_Project_ID", result["C_Project_ID"]);
            mTab.setValue("M_PriceList_ID", result["M_PriceList_ID"]);
            mTab.setValue("VA009_PaymentMethod_ID", result["VA009_PaymentMethod_ID"]);
            mTab.setValue("C_BPartner_Location_ID", result["Bill_Location_ID"]);
            mTab.setValue("VAS_ContractCategory_ID", result["VAS_ContractCategory_ID"]);

            if (mTab.getField("VA097_VendorDetails_ID") != null) {  //VIS0336_changes done for setting the vendor details id on purchase order window
                mTab.setValue("VA097_VendorDetails_ID", result["VA097_VendorDetails_ID"]);
            }

        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VAS_CalloutContract.prototype.PriceList = function (ctx, windowNo, mTab, mField, value, oldValue) {

        var dr = null;
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        try {

            var M_PriceList_ID = Util.getValueOfInt(value.toString());
            if (M_PriceList_ID == null || M_PriceList_ID == 0)
                return "";
            this.setCalloutActive(true);
            if (steps) {
                this.log.warning("init");
            }
            dr = VIS.dataContext.getJSONRecord("MPriceList/GetPriceListData", value.toString());
            if (dr != null) {
                //	Currency
                mTab.setValue("C_Currency_ID", dr["C_Currency_ID"]);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            if (dr != null) {
                dr.close();
            }
            this.log(Level.SEVERE, "", err);
            return err;
        }
        if (steps) {
            this.log.warning("finish");
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    /**
     * On Business Partner and ContractType according Field should be Updated
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    VAS_CalloutContract.prototype.BPartner = function (ctx, windowNo, mTab, mField, value, oldValue) {

        var dr = null;

        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        try {
            var C_BPartner_ID = 0;
            var isvendor = 'N';
            var isCustomer = 'N';
            if (value != null)
                C_BPartner_ID = Util.getValueOfInt(value.toString());
            if (C_BPartner_ID == 0)
                return "";

            var isReturnTrx = Util.getValueOfBoolean(mTab.getValue("IsReturnTrx"));
            if (isReturnTrx)
                return "";

            this.setCalloutActive(true);

            var _CountVA009 = false;
            var paramString = "VA009_";
            var isSOTrx = ctx.isSOTrx(windowNo);
            var dr = VIS.dataContext.getJSONRecord("ModulePrefix/GetModulePrefix", paramString);
            if (dr != null) {
                _CountVA009 = dr["VA009_"];
            }

            paramString = _CountVA009.toString() + "," + C_BPartner_ID;
            dr = VIS.dataContext.getJSONRecord("MVASContract/GetBPartnerData", paramString);
            if (dr != null) {

                // jurisdiction Tax
                var contractType = Util.getValueOfString(mTab.getValue("ContractType"));
                if (contractType == "ASP")
                    mTab.setValue("VAS_Jurisdiction", dr["VAS_TaxJurisdiction"]);

                // Price List

                var PriceList = Util.getValueOfInt(contractType == "ASR" ? dr["M_PriceList_ID"] : dr["PO_PriceList_ID"]);
                if (PriceList != 0) {
                    mTab.setValue("M_PriceList_ID", PriceList);
                }
                //Inco Term
                var IncoTerm = Util.getValueOfInt(contractType == "ASR" ? dr["C_IncoTerm_ID"] : dr["C_IncoTermPO_ID"]);
                if (IncoTerm > 0) {
                    mTab.setValue("C_IncoTerm_ID", IncoTerm);
                }

                //	Bill-To BPartner
                mTab.setValue("Bill_BPartner_ID", C_BPartner_ID);
                var bill_Location_ID = Util.getValueOfInt(dr["Bill_Location_ID"]);
                if (bill_Location_ID == 0) {
                    var bill_Partner_ID = Util.getValueOfInt(dr["Bill_BPartner_ID"]);
                    if (bill_Partner_ID > 0)
                        mTab.setValue("Bill_BPartner_ID", bill_Partner_ID);
                }
                else
                    mTab.setValue("Bill_Location_ID", bill_Location_ID);

                // Ship-To Location
                var shipTo_ID = Util.getValueOfInt(dr["C_BPartner_Location_ID"]);
                if (C_BPartner_ID.toString() == ctx.getContext("C_BPartner_ID")) {
                    var loc = ctx.getContext("C_BPartner_Location_ID");
                    if (loc.length > 0)
                        shipTo_ID = Util.getValueOfInt(loc);
                }
                if (shipTo_ID == 0)
                    mTab.setValue("C_BPartner_Location_ID", null);
                else {
                    mTab.setValue("C_BPartner_Location_ID", shipTo_ID);
                    if ("Y" == Util.getValueOfString(dr["IsShipTo"]))
                        mTab.setValue("Bill_Location_ID", shipTo_ID);
                }

                //Payment Method
                if (_CountVA009) {
                    var _PaymentMethod_ID = Util.getValueOfInt(dr["VA009_PaymentMethod_ID"]);
                    var _PO_PaymentMethod_ID = 0;
                    if (C_Order_Blanket <= 0) {

                        var bpdtl = VIS.dataContext.getJSONRecord("MBPartner/GetBPDetails", C_BPartner_ID);
                        if (bpdtl != null) {
                            isvendor = Util.getValueOfString(bpdtl["IsVendor"]);
                            isCustomer = Util.getValueOfString(bpdtl["IsCustomer"]);
                            if (!isSOTrx) {                   //In case of Purchase Order
                                if (isvendor == "Y") {
                                    _PaymentMethod_ID = Util.getValueOfInt(bpdtl["VA009_PO_PaymentMethod_ID"]);
                                }
                                else {
                                    _PaymentMethod_ID = 0;
                                }
                            }
                            else {
                                if (isvendor == "Y") {
                                    _PaymentMethod_ID = 0;
                                    PaymentBasetype = null;
                                    if (isCustomer == "Y") {
                                        _PaymentMethod_ID = Util.getValueOfInt(bpdtl["VA009_PaymentMethod_ID"]);
                                    }
                                }
                                else {
                                    if (isCustomer == "Y") {
                                        _PaymentMethod_ID = Util.getValueOfInt(bpdtl["VA009_PaymentMethod_ID"]);
                                    }
                                }

                            }
                        }
                    }
                }

                if (_PaymentMethod_ID == 0)
                    mTab.setValue("VA009_PaymentMethod_ID", null);
                else
                    mTab.setValue("VA009_PaymentMethod_ID", _PaymentMethod_ID);

                // Invoice Contact
                var contID = Util.getValueOfInt(dr["AD_User_ID"]);
                if (C_BPartner_ID.toString() == ctx.getContext("C_BPartner_ID")) {
                    var cont = ctx.getContext("AD_User_ID");
                    if (cont.length > 0)
                        contID = Util.getValueOfInt(cont);
                }
                if (contID == 0)
                    mTab.setValue("AD_User_ID", null);
                else {
                    mTab.setValue("AD_User_ID", contID);
                    mTab.setValue("Bill_User_ID", contID);
                }

                //	Payment Term
                var PaymentTermPresent = Util.getValueOfInt(mTab.getValue("C_PaymentTerm_ID")); // from BSO/BPO window
                var C_Order_Blanket = Util.getValueOfDecimal(mTab.getValue("C_Order_Blanket"));
                if (PaymentTermPresent > 0 && C_Order_Blanket > 0) {
                }
                else {
                    ii = Util.getValueOfInt(contractType == "ASR" ? dr["C_PaymentTerm_ID"] : dr["PO_PaymentTerm_ID"]);
                    var isPaymentTermUpdate = this.checkAdvancePaymentTerm(Util.getValueOfInt(mTab.getValue("C_DocTypeTarget_ID")), ii);
                    if (isPaymentTermUpdate) {
                        if (ii != 0)//ii=0 when dr return ""
                        {
                            mTab.setValue("C_PaymentTerm_ID", ii);
                        }
                    }
                    else {
                        mTab.setValue("C_PaymentTerm_ID", null);
                    }
                }
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            return err;
        }
        this.setCalloutActive(false);
        oldValue = null;
    };

    VAS_CalloutContract.prototype.checkAdvancePaymentTerm = function (documnetType_Id, PaymentTerm_Id) {
        var isAdvancePayTerm = true;
        var dr = VIS.dataContext.getJSONRecord("ModulePrefix/GetModulePrefix", "VA009_");
        if (dr != null && !dr["VA009_"]) {
            return isAdvancePayTerm;
        }
        if (Util.getValueOfInt(documnetType_Id) != 0 && Util.getValueOfInt(PaymentTerm_Id) != 0) {
            var paramString = documnetType_Id.toString() + "," + PaymentTerm_Id.toString();
            isAdvancePayTerm = VIS.dataContext.getJSONRecord("MOrder/checkAdvancePaymentTerm", paramString);
        }
        return isAdvancePayTerm;
    }
    /**
     * VIS404 Set UOM on product selection on contract line tab of Contract Master window
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    VAS_CalloutContract.prototype.SetProductUOM = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null) {
            return "";
        }
        this.setCalloutActive(true);
        var data = VIS.dataContext.getJSONRecord("MVASContract/GetProductUOM", value.toString());
        if (data != null) {
            mTab.setValue("C_UOM_ID", data);
            this.setCalloutActive(false);
            return "";
        }
    };
    /**
     * VIS404 Set fields blank on change of Contract Type
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    VAS_CalloutContract.prototype.UpdateIsSOTrx = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "" || value == false) {
            return "";
        }
        this.setCalloutActive(true);

        ctx.setIsSOTrx(windowNo, value == "ASR" ? true : false)
        if (value == "ASR") {
            mTab.setValue("IsSOTrx", value == "ASR" ? true : false);
            mTab.setValue("VAS_Jurisdiction", null);
        }
        this.setCalloutActive(false);
    };
    function dateDiffInYears(dateold, datenew) {
        var ynew = datenew.getFullYear();
        var mnew = datenew.getMonth();
        var dnew = datenew.getDate();
        var yold = dateold.getFullYear();
        var mold = dateold.getMonth();
        var dold = dateold.getDate();
        var diff = ynew - yold;
        if (mold > mnew) diff--;
        else {
            if (mold == mnew) {
                if (dold > dnew) diff--;
            }
        }
        return diff;
    };
    VIS.Model.VAS_CalloutContract = VAS_CalloutContract;
    //***************VAS_CalloutContract End ************

})(VIS, jQuery);