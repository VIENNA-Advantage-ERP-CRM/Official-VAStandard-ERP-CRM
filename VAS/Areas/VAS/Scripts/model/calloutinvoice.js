; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;
    var countEd011 = 0;
    var steps = false;

    function CalloutInvoice() {
        VIS.CalloutEngine.call(this, "VIS.CalloutInvoice"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutInvoice, VIS.CalloutEngine);//inherit CalloutEngine

    /**
     *	Invoice Header - DocType.
     *		- PaymentRule
     *		- temporary Document
     *  Context:
     *  	- DocSubTypeSO
     *		- HasCharges
     *	- (re-sets Business Partner info of required)
     *	@param ctx context
     *	@param windowNo window no
     *	@param mTab tab
     *	@param mField field
     *	@param value value
     *	@return null or error message
     */
    CalloutInvoice.prototype.DocType = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }

        var C_DocType_ID = value;//(int)value;
        if (C_DocType_ID == null || C_DocType_ID == 0)
            return "";

        //var sql = "SELECT d.HasCharges,'N',d.IsDocNoControlled,"
        //    + "s.CurrentNext, d.DocBaseType "
        //    /*//jz outer join
        //    + "FROM C_DocType d, AD_Sequence s "
        //    + "WHERE C_DocType_ID=?"		//	1
        //    + " AND d.DocNoSequence_ID=s.AD_Sequence_ID(+)";
        //    */
        //    + "FROM C_DocType d "
        //    + "LEFT OUTER JOIN AD_Sequence s ON (d.DocNoSequence_ID=s.AD_Sequence_ID) "
        //    + "WHERE C_DocType_ID=" + C_DocType_ID;		//	1

        var paramString = C_DocType_ID.toString();
        var dr = VIS.dataContext.getJSONRecord("MDocType/GetDocTypeData", paramString);

        try {
            if (dr != null) {
                //	Charges - Set Context
                ctx.setContext(windowNo, "HasCharges", Util.getValueOfString(dr["HasCharges"]));

                //	DocumentNo
                if (dr["IsDocNoControlled"] == "Y") {
                    mTab.setValue("DocumentNo", "<" + Util.getValueOfString(dr["CurrentNext"]) + ">");
                }

                //  DocBaseType - Set Context
                var s = Util.getValueOfString(dr["DocBaseType"]);
                ctx.setContext(windowNo, "DocBaseType", s);

                //  AP Check & AR Credit Memo
                if (s.startsWith("AP")) {
                    mTab.setValue("PaymentRule", "S");    //  Check
                }
                else if (s.endsWith("C")) {
                    mTab.setValue("PaymentRule", "P");    //  OnCredit
                }

                // set Value Of Treat As Discount - in case of AP Credit memo only
                if (mTab.getField("TreatAsDiscount") != null) {
                    if (s.equals("APC")) {
                        mTab.setValue("TreatAsDiscount", Util.getValueOfBoolean(dr["TreatAsDiscount"]));
                    }
                    else {
                        mTab.setValue("TreatAsDiscount", false);
                    }
                }

                //set isreturntrx
                if (mTab.getField("IsReturnTrx") != null) {
                    mTab.setValue("IsReturnTrx", Util.getValueOfBoolean(dr["IsReturnTrx"]));
                }
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            if (dr != null) {
                dr.close();
            }
            this.log.log(Level.SEVERE, sql, err);
            return err.message;
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };


    /**
     *	Invoice Header- BPartner.
     *		- M_PriceList_ID (+ Context)
     *		- C_BPartner_Location_ID
     *		- AD_User_ID
     *		- POReference
     *		- SO_Description
     *		- IsDiscountPrinted
     *		- PaymentRule
     *		- C_PaymentTerm_ID
     *	@param ctx context
     *	@param windowNo window no
     *	@param mTab tab
     *	@param mField field
     *	@param value value
     *	@return null or error message
     */
    CalloutInvoice.prototype.BPartner = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value.toString() == "") {
            return "";
        }
        try {
            var C_BPartner_ID = Util.getValueOfInt(value);//(int)value;
            if (C_BPartner_ID == null || C_BPartner_ID == 0) {
                return "";
            }
            var isvendor = 'N';
            var isCustomer = 'N';
            //var sql = "SELECT p.AD_Language,p.C_PaymentTerm_ID,"
            //    + " COALESCE(p.M_PriceList_ID,g.M_PriceList_ID) AS M_PriceList_ID, p.PaymentRule,p.POReference,"
            //    + " p.SO_Description,p.IsDiscountPrinted,"
            //    + " p.SO_CreditLimit, p.SO_CreditLimit-p.SO_CreditUsed AS CreditAvailable,"
            //    + " l.C_BPartner_Location_ID,c.AD_User_ID,"
            //    + " COALESCE(p.PO_PriceList_ID,g.PO_PriceList_ID) AS PO_PriceList_ID, p.PaymentRulePO,p.PO_PaymentTerm_ID "
            //    + "FROM C_BPartner p"
            //    + " INNER JOIN C_BP_Group g ON (p.C_BP_Group_ID=g.C_BP_Group_ID)"
            //    + " LEFT OUTER JOIN C_BPartner_Location l ON (p.C_BPartner_ID=l.C_BPartner_ID AND l.IsBillTo='Y' AND l.IsActive='Y')"
            //    + " LEFT OUTER JOIN AD_User c ON (p.C_BPartner_ID=c.C_BPartner_ID) "
            //    + "WHERE p.C_BPartner_ID=" + C_BPartner_ID + " AND p.IsActive='Y'";		//	#1

            //-----------------ANuj----Code----------
            //var sql = "SELECT p.AD_Language,p.C_PaymentTerm_ID,"
            //    + " COALESCE(p.M_PriceList_ID,g.M_PriceList_ID) AS M_PriceList_ID, p.PaymentRule,p.POReference,"
            //    + " p.SO_Description,p.IsDiscountPrinted, p.C_IncoTerm_ID,p.C_IncoTermPO_ID, ";

            //var _CountVA009 = Util.getValueOfInt(VIS.DB.executeScalar("SELECT COUNT(AD_MODULEINFO_ID) FROM AD_MODULEINFO WHERE PREFIX='VA009_'  AND IsActive = 'Y'"));
            //if (_CountVA009 > 0) {
            //    //VA009_PO_PaymentMethod_ID added new column for enhancement.. Google Sheet ID-- SI_0036
            //    sql += " p.VA009_PaymentMethod_ID, p.VA009_PO_PaymentMethod_ID, ";
            //    //p.VA009_PO_PaymentMethod_ID, ";
            //}

            //sql += "p.CreditStatusSettingOn,p.SO_CreditLimit, NVL(p.SO_CreditLimit,0) - NVL(p.SO_CreditUsed,0) AS CreditAvailable,"
            //    + " l.C_BPartner_Location_ID,c.AD_User_ID,"
            //    + " COALESCE(p.PO_PriceList_ID,g.PO_PriceList_ID) AS PO_PriceList_ID, p.PaymentRulePO,p.PO_PaymentTerm_ID, "
            //    + " p.SalesRep_ID,p.IsSalesRep, p.C_Withholding_ID "
            //    + " FROM C_BPartner p"
            //    + " INNER JOIN C_BP_Group g ON (p.C_BP_Group_ID=g.C_BP_Group_ID)"
            //    + " LEFT OUTER JOIN C_BPartner_Location l ON (p.C_BPartner_ID=l.C_BPartner_ID AND l.IsBillTo='Y' AND l.IsActive='Y')"
            //    + " LEFT OUTER JOIN AD_User c ON (p.C_BPartner_ID=c.C_BPartner_ID) "
            //    + "WHERE p.C_BPartner_ID=" + C_BPartner_ID + " AND p.IsActive='Y'";		//	#1
            var _CountVA009 = false;
            var module = VIS.dataContext.getJSONRecord("ModulePrefix/GetModulePrefix", "VA009_");
            if (module != null) {
                _CountVA009 = module["VA009_"];
            }

            var paramString = _CountVA009.toString() + "," + C_BPartner_ID.toString();
            var isSOTrx = ctx.isSOTrx(windowNo);
            var dr = null;
            var drl = null;
            try {
                dr = VIS.dataContext.getJSONRecord("MBPartner/GetBPartnerData", paramString);

                //dr = VIS.DB.executeReader(sql, null, null);
                //if (dr.read()) {
                //	PriceList & IsTaxIncluded & Currency
                if (dr != null) {
                    var ii = Util.getValueOfInt(dr[isSOTrx ? "M_PriceList_ID" : "PO_PriceList_ID"]);
                    if (ii > 0) {
                        mTab.setValue("M_PriceList_ID", ii);
                    }
                    // JID_0364: If price list not available at BP, user need to select it manually

                    //else {	//	get default PriceList
                    //    var i = ctx.getContextAsInt("#M_PriceList_ID");
                    //    if (i != 0) {
                    //        mTab.setValue("M_PriceList_ID", i);
                    //    }
                    //}

                    //	PaymentRule
                    //Inco Term
                    var IncoTerm = Util.getValueOfInt(dr[isSOTrx ? "C_IncoTerm_ID" : "C_IncoTermPO_ID"]);
                    if (IncoTerm > 0) {
                        mTab.setValue("C_IncoTerm_ID", IncoTerm);
                    }

                    var s = Util.getValueOfString(dr[isSOTrx ? "PaymentRule" : "PaymentRulePO"]);
                    if (s != null && s.length != 0) {
                        if (ctx.getContext("DocBaseType").toString().endsWith("C"))	//	Credits are Payment Term
                        {
                            s = "P";
                        }
                        else if (isSOTrx && (s.toString().equals("S") || s.toString().equals("U")))	//	No Check/Transfer for SO_Trx
                        {
                            s = "P";											//  Payment Term
                        }
                        mTab.setValue("PaymentRule", s);
                    }

                    //var _CountVA009 = Util.getValueOfInt(VIS.DB.executeScalar("SELECT COUNT(AD_MODULEINFO_ID) FROM AD_MODULEINFO WHERE PREFIX='VA009_'  AND IsActive = 'Y'"));
                    //if (result != null) {
                    //    _CountVA009 = result["VA009_"];
                    //}
                    //if (_CountVA009 > 0) {

                    var DataPrefix = VIS.dataContext.getJSONRecord("ModulePrefix/GetModulePrefix", "VA009_");
                    if (DataPrefix["VA009_"]) {
                        var _PaymentMethod_ID = Util.getValueOfInt(dr["VA009_PaymentMethod_ID"]);
                        var PaymentBasetype = Util.getValueOfString(dr["VA009_PaymentBaseType"]);
                        ////VA009_PO_PaymentMethod_ID added new column for enhancement.. Google Sheet ID-- SI_0036
                        var isvendor = 'N';
                        var bpdtl = VIS.dataContext.getJSONRecord("MBPartner/GetBPDetails", C_BPartner_ID);
                        if (bpdtl != null) {
                            isvendor = Util.getValueOfString(bpdtl["IsVendor"]);
                            isCustomer = Util.getValueOfString(bpdtl["IsCustomer"]);
                            if (!isSOTrx) { //In case of Purchase Order
                                if (isvendor == "Y") {
                                    _PaymentMethod_ID = Util.getValueOfInt(bpdtl["VA009_PO_PaymentMethod_ID"]);
                                    PaymentBasetype = Util.getValueOfString(bpdtl["VA009_PAYMENTBASETYPEPO"]);
                                }
                                else {
                                    _PaymentMethod_ID = 0;
                                    PaymentBasetype = null;
                                }
                            }
                            else {
                                if (isvendor == "Y") {
                                    _PaymentMethod_ID = 0;
                                    PaymentBasetype = null;
                                    if (isCustomer == "Y") {
                                        _PaymentMethod_ID = Util.getValueOfInt(bpdtl["VA009_PaymentMethod_ID"]);
                                        PaymentBasetype = Util.getValueOfString(bpdtl["VA009_PAYMENTBASETYPE"]);
                                    }
                                }
                                else {
                                    if (isCustomer == "Y") {
                                        _PaymentMethod_ID = Util.getValueOfInt(bpdtl["VA009_PaymentMethod_ID"]);
                                        PaymentBasetype = Util.getValueOfString(bpdtl["VA009_PAYMENTBASETYPE"]);
                                    }
                                }

                            }
                        }

                        if (_PaymentMethod_ID == 0)
                            mTab.setValue("VA009_PaymentMethod_ID", null);
                        else {
                            mTab.setValue("VA009_PaymentMethod_ID", _PaymentMethod_ID);
                            if (PaymentBasetype != null) {
                                //if (PaymentBasetype != null) {
                                mTab.setValue("PaymentMethod", PaymentBasetype);
                                if (isvendor == 'N')
                                    mTab.setValue("PaymentRule", PaymentBasetype);
                                else
                                    mTab.setValue("PaymentRule", PaymentBasetype);
                            }
                            else {
                                mTab.setValue("PaymentMethod", "T");
                                if (isvendor == 'N')
                                    mTab.setValue("PaymentRule", "T");
                                else
                                    mTab.setValue("PaymentRule", "T");
                            }
                        }
                    }

                    //  Payment Term
                    var PaymentTermPresent = Util.getValueOfInt(mTab.getValue("C_PaymentTerm_ID")); // from BSO/BPO window
                    var C_Order_Blanket = Util.getValueOfDecimal(mTab.getValue("C_Order_Blanket"))
                    if (PaymentTermPresent > 0 && C_Order_Blanket > 0) {
                    }
                    else {
                        ii = Util.getValueOfInt(dr[isSOTrx ? "C_PaymentTerm_ID" : "PO_PaymentTerm_ID"]);
                        if (ii > 0) {
                            mTab.setValue("C_PaymentTerm_ID", ii);
                        }
                    }
                    //	Location
                    var locID = Util.getValueOfInt(dr["C_BPartner_Location_ID"]);
                    /*VIS_0045: 04-May-2023 - DevOps Task ID: 2110*/
                    //	overwritten by InfoBP selection - works only if InfoWindow
                    //	was used otherwise creates error (uses last value, may bevar  to differnt BP)
                    if (C_BPartner_ID.toString().equals(ctx.getWindowTabContext(windowNo, VIS.EnvConstants.TAB_INFO, "C_BPARTNER_ID").toString())) {
                        var loc = ctx.getWindowTabContext(windowNo, VIS.EnvConstants.TAB_INFO, "C_BPARTNER_LOCATION_ID");
                        if (loc && loc.toString().length > 0) {
                            locID = parseInt(loc);
                        }
                    }
                    if (locID == 0) {
                        mTab.setValue("C_BPartner_Location_ID", null);
                    }
                    else {
                        mTab.setValue("C_BPartner_Location_ID", locID);
                    }

                    /*VIS_0045: 04-May-2023 - DevOps Task ID: 2110*/
                    //	Contact - overwritten by InfoBP selection
                    var contID = Util.getValueOfInt(dr["AD_User_ID"]);
                    if (C_BPartner_ID.toString().equals(ctx.getWindowTabContext(windowNo, VIS.EnvConstants.TAB_INFO, "C_BPARTNER_ID").toString())) {
                        var cont = ctx.getWindowTabContext(windowNo, VIS.EnvConstants.TAB_INFO, "AD_USER_ID");
                        if (cont && cont.toString().length > 0) {
                            contID = parseInt(cont);
                        }
                    }
                    if (contID == 0) {
                        mTab.setValue("AD_User_ID", null);
                    }
                    else {
                        mTab.setValue("AD_User_ID", contID);
                    }

                    //	CreditAvailable
                    if (isSOTrx) {
                        var CreditStatus = dr["CreditStatusSettingOn"];
                        if (CreditStatus == "CH") {
                            var CreditLimit = Util.getValueOfDouble(dr["SO_CreditLimit"]);
                            if (CreditLimit != 0) {
                                var CreditAvailable = Util.getValueOfDouble(dr["CreditAvailable"]);
                                if (dr != null && CreditAvailable <= 0) {
                                    VIS.ADialog.info("CreditOver");
                                }
                            }
                        }
                        else {
                            var locId = Util.getValueOfInt(mTab.getValue("C_BPartner_Location_ID"));
                            // JID_0161 // change here now will check credit settings on field only on Business Partner Header // Lokesh Chauhan 15 July 2019 
                            //sql = "SELECT bp.CreditStatusSettingOn,p.SO_CreditLimit, NVL(p.SO_CreditLimit,0) - NVL(p.SO_CreditUsed,0) AS CreditAvailable" +
                            //    " FROM C_BPartner_Location p INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID = p.C_BPartner_ID) WHERE p.C_BPartner_Location_ID = " + locId;
                            //drl = VIS.DB.executeReader(sql);

                            dr1 = VIS.dataContext.getJSONRecord("MBPartner/GetLocationData", locId.toString());
                            if (drl != null) {
                                CreditStatus = drl["CreditStatusSettingOn"];
                                if (CreditStatus == "CL") {
                                    var CreditLimit = Util.getValueOfDouble(drl["SO_CreditLimit"]);
                                    //	var SOCreditStatus = dr.getString("SOCreditStatus");
                                    if (CreditLimit != 0) {
                                        var CreditAvailable = Util.getValueOfDouble(drl["CreditAvailable"]);
                                        if (dr != null && CreditAvailable <= 0) {
                                            VIS.ADialog.info("CreditOver");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    //	PO Reference
                    s = Util.getValueOfString(dr["POReference"]);
                    if (s != null && s.length != 0) {
                        mTab.setValue("POReference", s);
                    }
                    else {
                        mTab.setValue("POReference", null);
                    }
                    //	SO Description
                    s = Util.getValueOfString(dr["SO_Description"]);
                    if (s != null && s.toString().trim().length != 0) {
                        mTab.setValue("Description", s);
                    }
                    //	IsDiscountPrinted
                    s = Util.getValueOfString(dr["IsDiscountPrinted"]);
                    if (s != null && s.toString().trim().length != 0) {
                        mTab.setValue("IsDiscountPrinted", s);
                    }
                    else {
                        mTab.setValue("IsDiscountPrinted", "N");
                    }
                    //if dr has supply type property then set its value
                    if (dr.hasOwnProperty("VA106_SupplyType")) {
                        mTab.setValue("VA106_SupplyType", dr["VA106_SupplyType"]);
                    }
                    // set withholding tax defined on vendor/customer
                    //mTab.setValue("C_Withholding_ID", Util.getValueOfInt(dr.get("C_Withholding_ID")));
                }
                //dr.close();
            }
            catch (err) {
                this.setCalloutActive(false);
                this.log.log(Level.SEVERE, "bPartner", err);
                return err.message;
            }
            finally {
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /**
     *	Set Payment Term.
     *	Payment Term has changed 
     *	@param ctx context
     *	@param windowNo window no
     *	@param mTab tab
     *	@param mField field
     *	@param value value
     *	@return null or error message
     */
    CalloutInvoice.prototype.PaymentTerm = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  

        if (value == null || value.toString() == "") {
            return "";
        }
        try {
            var C_PaymentTerm_ID = value;
            var C_Invoice_ID = ctx.getContextAsInt(windowNo, "C_Invoice_ID", false);
            var get_ID;
            var apply;
            if (C_PaymentTerm_ID == null || C_PaymentTerm_ID == 0
                || C_Invoice_ID == 0)	//	not saved yet
            {
                return "";
            }
            //
            var paramString = C_PaymentTerm_ID.toString().concat(",", C_Invoice_ID.toString());

            var dr = VIS.dataContext.getJSONRecord("MPaymentTerm/GetPaymentTerm", paramString);

            // Added by Bharat on 18 July 2017 as issue given by Ravikant on Payment Term selection giving Internal server error..
            if (dr != null) {
                get_ID = dr["Get_ID"];
                var valid = dr["Apply"];
                if (get_ID == 0) {
                    return "PaymentTerm not found";
                }

                // JID_0576: "Getting error ""Could not save changes - data was changed after query. IsPayScheduleValid"" while changing payment term after adding invoice line"                
                mTab.setValue("IsPayScheduleValid", valid ? true : false);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /***
     *	Invoice Line - Product.
     *		- reset C_Charge_ID / M_AttributeSetInstance_ID
     *		- PriceList, PriceStd, PriceLimit, C_Currency_ID, EnforcePriceLimit
     *		- UOM
     *	Calls Tax
     *	@param ctx context
     *	@param windowNo window no
     *	@param mTab tab
     *	@param mField field
     *	@param value value
     *	@return null or error message
     */
    CalloutInvoice.prototype.Product = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (this.isCalloutActive() || value == null || value.toString() == "") {
            mTab.setValue("VA038_AmortizationTemplate_ID", 0);
            return "";
        }

        var M_Product_ID = value;
        if (M_Product_ID == null || M_Product_ID == 0)
            return "";
        this.setCalloutActive(true);
        try {
            // Clear Charge
            mTab.setValue("C_Charge_ID", null);

            // JID_0910: On change of product on line system is not removing the ASI. if product is changed then also update the ASI field.
            mTab.setValue("M_AttributeSetInstance_ID", null);

            /*****	Price Calculation see also qty	****/
            var Qty = mTab.getValue("QtyInvoiced");
            var M_AttributeSetInstance_ID = ctx.getContextAsInt(windowNo, "M_AttributeSetInstance_ID");

            var paramString = mTab.getValue("C_Invoice_ID").toString().concat(",", M_Product_ID.toString(), ",",
                M_AttributeSetInstance_ID.toString(), ",", Qty.toString());
            var invoiceRecord = VIS.dataContext.getJSONRecord("MInvoice/GetInvoiceProductInfo", paramString);

            countEd011 = Util.getValueOfInt(invoiceRecord["countEd011"]);
            var isSOTrx = invoiceRecord["IsSOTrx"];

            var purchasingUom = 0;
            if (countEd011 > 0) {
                //VAI050- If isSoTrx false than Set Purchasing UOM from Product if Purchasing UOM found on Purchasing tab
                //If Purchasing UOM not found than give priority to PU unit of Product else set Base UOM of Product
                if (Util.getValueOfInt(invoiceRecord["purchasingUom"]) > 0 && !isSOTrx)
                    purchasingUom = Util.getValueOfInt(invoiceRecord["purchasingUom"]);
                else
                    purchasingUom = Util.getValueOfInt(invoiceRecord["VAS_PurchaseUOM_ID"]);
                if (purchasingUom > 0 && isSOTrx == false) {
                    mTab.setValue("C_UOM_ID", purchasingUom);
                }
                //VAI050- If isSoTrx true than Set Sales UOM from Product if found else set Base UOM
                else if (Util.getValueOfInt(invoiceRecord["VAS_SalesUOM_ID"]) > 0 && isSOTrx) {
                    mTab.setValue("C_UOM_ID", Util.getValueOfInt(invoiceRecord["VAS_SalesUOM_ID"]));
                }
                else {
                    mTab.setValue("C_UOM_ID", Util.getValueOfInt(invoiceRecord["headerUom"]));
                }
            }

            // set C_RevenueRecognition_ID if InvoiceLine Tab Contains C_RevenueRecognition_ID field
            if (mTab.findColumn("C_RevenueRecognition_ID") > -1) {
                mTab.setValue("C_RevenueRecognition_ID", Util.getValueOfInt(invoiceRecord["C_RevenueRecognition_ID"]));
            }

            // VIS_045: 01-July-2025, Check Tax Collected at Source is defined on Product Master, if defined then check it is sales trx, if yes then set value on InvoiceLine
            if (isSOTrx && mTab.findColumn("VA106_TaxCollectedAtSource_ID") > -1 && invoiceRecord.hasOwnProperty("VA106_TaxCollectedAtSource_ID")) {
                mTab.setValue("VA106_TaxCollectedAtSource_ID", Util.getValueOfInt(invoiceRecord["VA106_TaxCollectedAtSource_ID"]));
            }

            // VIS_045: 07-Jan-2026, Set HSN Code From Product
            if (mTab.findColumn("VAS_HSN_SACCode") > -1 && invoiceRecord.hasOwnProperty("VAS_HSN_SACCode")) {
                mTab.setValue("VAS_HSN_SACCode", Util.getValueOfString(invoiceRecord["VAS_HSN_SACCode"]));
            }

            //		
            mTab.setValue("PriceList", Util.getValueOfDecimal(invoiceRecord["PriceList"]));
            mTab.setValue("PriceLimit", Util.getValueOfDecimal(invoiceRecord["PriceLimit"]));
            mTab.setValue("PriceActual", Util.getValueOfDecimal(invoiceRecord["PriceActual"]));
            mTab.setValue("PriceEntered", Util.getValueOfDecimal(invoiceRecord["PriceEntered"]));
            mTab.setValue("C_Currency_ID", Util.getValueOfInt(invoiceRecord["C_Currency_ID"]));
            mTab.setValue("PrintDescription", Util.getValueOfString(invoiceRecord["DocumentNote"]));

            if (countEd011 > 0 && purchasingUom > 0 && isSOTrx == false) {
                // when record created with purchasing UOM, then set Qty Invoiced
                mTab.setValue("QtyInvoiced", Util.getValueOfDecimal(invoiceRecord["QtyInvoiced"]));
            }

            ctx.setContext(windowNo, "EnforcePriceLimit", Util.getValueOfString(invoiceRecord["EnforcePriceLimit"]) ? "Y" : "N");
            ctx.setContext(windowNo, "DiscountSchema", Util.getValueOfString(invoiceRecord["DiscountSchema"]) ? "Y" : "N");

        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        this.setCalloutActive(false);
        oldValue = null;
        return this.Tax(ctx, windowNo, mTab, mField, value);
    };

    /**
     * 	Calculate Discout based on Discount Schema selected on Business Partner
     *	@param ProductId Product ID
     *	@param ClientId Tenant
     *	@param amount Amount on which discount is to be calculated
     *	@param DiscountSchemaId Discount Schema of Business Partner
     *	@param FlatDiscount Flat Discount % on Business Partner
     *	@param QtyEntered Quantity on which discount is to be calculated
     *	@return Discount value
     */
    //CalloutInvoice.prototype.FlatDiscount = function (ProductId, ClientId, amount, DiscountSchemaId, FlatDiscount, QtyEntered) {
    //    var amountAfterBreak = amount;
    //    var sql = "SELECT UNIQUE M_Product_Category_ID FROM M_Product WHERE IsActive='Y' AND M_Product_ID = " + ProductId;
    //    var productCategoryId = Util.getValueOfInt(VIS.DB.executeScalar(sql));
    //    var isCalulate = false;
    //    var dsDiscountBreak = null;
    //    var discountType = "";
    //    // Is flat Discount
    //    // JID_0487: Not considering Business Partner Flat Discout Checkbox value while calculating the Discount
    //    sql = "SELECT  DiscountType, IsBPartnerFlatDiscount, FlatDiscount FROM M_DiscountSchema WHERE "
    //              + "M_DiscountSchema_ID = " + DiscountSchemaId + " AND IsActive='Y'  AND AD_Client_ID=" + ClientId;
    //    dsDiscountBreak = VIS.DB.executeDataSet(sql);

    //    if (dsDiscountBreak != null && dsDiscountBreak.getTables().length > 0) {
    //        discountType = Util.getValueOfString(dsDiscountBreak.getTables()[0].getRows()[0].getCell("DiscountType"));
    //        if (discountType == "F") {
    //            var discountBreakValue = 0;
    //            isCalulate = true;
    //            if (Util.getValueOfString(dsDiscountBreak.getTables()[0].getRows()[0].getCell("IsBPartnerFlatDiscount")) == "N") {
    //                discountBreakValue = (amount - ((amount * Util.getValueOfDecimal(dsDiscountBreak.getTables()[0].getRows()[0].getCell("FlatDiscount"))) / 100));
    //            }
    //            else {
    //                discountBreakValue = (amount - ((amount * FlatDiscount) / 100));
    //            }
    //            amountAfterBreak = discountBreakValue;
    //            return amountAfterBreak;
    //        }

    //        else if (discountType == "B") {

    //            // Product Based
    //            sql = "SELECT M_Product_Category_ID , M_Product_ID , BreakValue , IsBPartnerFlatDiscount , BreakDiscount FROM M_DiscountSchemaBreak WHERE "
    //                       + "M_DiscountSchema_ID = " + DiscountSchemaId + " AND M_Product_ID = " + ProductId
    //                       + " AND IsActive='Y'  AND AD_Client_ID=" + ClientId + "Order BY BreakValue DESC";
    //            dsDiscountBreak = VIS.DB.executeDataSet(sql);
    //            if (dsDiscountBreak.getTables().length > 0) {
    //                var m = 0;
    //                var discountBreakValue = 0;

    //                for (m = 0; m < dsDiscountBreak.getTables()[0].getRows().length; m++) {
    //                    if (QtyEntered < Util.getValueOfDecimal(dsDiscountBreak.getTables()[0].getRows()[m].getCell("BreakValue"))) {
    //                        continue;
    //                    }
    //                    if (Util.getValueOfString(dsDiscountBreak.getTables()[0].getRows()[m].getCell("IsBPartnerFlatDiscount")) == "N") {
    //                        isCalulate = true;
    //                        discountBreakValue = (amount - (amount * Util.getValueOfDecimal(dsDiscountBreak.getTables()[0].getRows()[m].getCell("BreakDiscount")) / 100));
    //                        break;
    //                    }
    //                    else {
    //                        isCalulate = true;
    //                        discountBreakValue = (amount - ((amount * FlatDiscount) / 100));
    //                        break;
    //                    }
    //                }
    //                if (isCalulate) {
    //                    amountAfterBreak = discountBreakValue;
    //                    return amountAfterBreak;
    //                }
    //            }
    //            //

    //            // Product Category Based
    //            sql = "SELECT M_Product_Category_ID , M_Product_ID , BreakValue , IsBPartnerFlatDiscount , BreakDiscount FROM M_DiscountSchemaBreak WHERE "
    //                       + " M_DiscountSchema_ID = " + DiscountSchemaId + " AND M_Product_Category_ID = " + productCategoryId
    //                       + " AND IsActive='Y'  AND AD_Client_ID=" + ClientId + "Order BY BreakValue DESC";
    //            dsDiscountBreak = VIS.DB.executeDataSet(sql);
    //            if (dsDiscountBreak.getTables().length > 0) {
    //                var m = 0;
    //                var discountBreakValue = 0;

    //                for (m = 0; m < dsDiscountBreak.getTables()[0].getRows().length; m++) {
    //                    if (QtyEntered < Util.getValueOfDecimal(dsDiscountBreak.getTables()[0].getRows()[m].getCell("BreakValue"))) {
    //                        continue;
    //                    }
    //                    if (Util.getValueOfString(dsDiscountBreak.getTables()[0].getRows()[m].getCell("IsBPartnerFlatDiscount")) == "N") {
    //                        isCalulate = true;
    //                        discountBreakValue = (amount - (amount * Util.getValueOfDecimal(dsDiscountBreak.getTables()[0].getRows()[m].getCell("BreakDiscount")) / 100));
    //                        break;
    //                    }
    //                    else {
    //                        isCalulate = true;
    //                        discountBreakValue = (amount - ((amount * FlatDiscount) / 100));
    //                        break;
    //                    }
    //                }
    //                if (isCalulate) {
    //                    amountAfterBreak = discountBreakValue;
    //                    return amountAfterBreak;
    //                }
    //            }
    //            //

    //            // Otherwise
    //            sql = "SELECT M_Product_Category_ID , M_Product_ID , BreakValue , IsBPartnerFlatDiscount , BreakDiscount FROM M_DiscountSchemaBreak WHERE "
    //                       + " M_DiscountSchema_ID = " + DiscountSchemaId + " AND M_Product_Category_ID IS NULL AND m_product_id IS NULL "
    //                       + " AND IsActive='Y'  AND AD_Client_ID=" + ClientId + "Order BY BreakValue DESC";
    //            dsDiscountBreak = VIS.DB.executeDataSet(sql);
    //            if (dsDiscountBreak.getTables().length > 0) {
    //                var m = 0;
    //                var discountBreakValue = 0;

    //                for (m = 0; m < dsDiscountBreak.getTables()[0].getRows().length; m++) {
    //                    if (QtyEntered < Util.getValueOfDecimal(dsDiscountBreak.getTables()[0].getRows()[m].getCell("BreakValue"))) {
    //                        continue;
    //                    }
    //                    if (Util.getValueOfString(dsDiscountBreak.getTables()[0].getRows()[m].getCell("IsBPartnerFlatDiscount")) == "N") {
    //                        isCalulate = true;
    //                        discountBreakValue = (amount - (amount * Util.getValueOfDecimal(dsDiscountBreak.getTables()[0].getRows()[m].getCell("BreakDiscount")) / 100));
    //                        break;
    //                    }
    //                    else {
    //                        isCalulate = true;
    //                        discountBreakValue = (amount - ((amount * FlatDiscount) / 100));
    //                        break;
    //                    }
    //                }
    //                if (isCalulate) {
    //                    amountAfterBreak = discountBreakValue;
    //                    return amountAfterBreak;
    //                }
    //            }
    //        }
    //    }

    //    return amountAfterBreak;
    //};

    /**
     *	Invoice Line - Charge.
     * 		- updates PriceActual from Charge
     * 		- sets PriceLimit, PriceList to zero
     * 	Calles tax
     *	@param ctx context
     *	@param windowNo window no
     *	@param mTab tab
     *	@param mField field
     *	@param value value
     *	@return null or error message
     */
    CalloutInvoice.prototype.Charge = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value.toString() == "") {
            mTab.setValue("VA038_AmortizationTemplate_ID", 0);
            return "";
        }
        var dr = null;
        try {
            var C_Charge_ID = value;
            if (C_Charge_ID == null || C_Charge_ID == 0)
                return "";

            //	No Product defined
            if (mTab.getValue("M_Product_ID") != null) {
                mTab.setValue("C_Charge_ID", null);
                return "ChargeExclusively";
            }
            mTab.setValue("M_AttributeSetInstance_ID", null);
            mTab.setValue("S_ResourceAssignment_ID", null);

            //JID_0054: Currently System setting the Each as UOM after selecting the cahrge. Need to set default UOM for charge.
            var c_uom_id = ctx.getContextAsInt("#C_UOM_ID");
            if (c_uom_id > 0) {
                mTab.setValue("C_UOM_ID", c_uom_id);	//	Default UOM from context.
            }
            else {
                mTab.setValue("C_UOM_ID", 100);	//	EA
            }
            ctx.setContext(windowNo, "DiscountSchema", "N");

            //JID_1744 The precision should be as per Currenct Precision
            //JID_1744 The precision should be as per Currenct Precision
            var stdPrecision = VIS.dataContext.getJSONRecord("MInvoice/GetPercision", mTab.getValue("C_Invoice_ID").toString());

            //190 - Remove client side query and set print description
            var dr = VIS.dataContext.getJSONRecord("MCharge/GetChargeDetails", C_Charge_ID.toString());
            if (dr != null) {
                mTab.setValue("PriceEntered", Util.getValueOfDecimal(dr["ChargeAmt"]).toFixed(stdPrecision));
                mTab.setValue("PrintDescription", Util.getValueOfString(dr["PrintDescription"]));
                mTab.setValue("PriceActual", Util.getValueOfDecimal(dr["ChargeAmt"]).toFixed(stdPrecision));
                mTab.setValue("PriceLimit", 0);
                mTab.setValue("PriceList", 0);
                mTab.setValue("Discount", 0);
            }

            mTab.setValue("PriceLimit", VIS.Env.ZERO);
            mTab.setValue("PriceList", VIS.Env.ZERO);
            mTab.setValue("Discount", VIS.Env.ZERO);

        }
        catch (err) {
            this.setCalloutActive(false);
            return err.message;
        }
        oldValue = null;
        return this.Tax(ctx, windowNo, mTab, mField, value);
    };

    /**
     *	Invoice Line - Tax.
     *		- basis: Product, Charge, BPartner Location
     *		- sets C_Tax_ID
     *  Calles Amount
     *	@param ctx context
     *	@param windowNo window no
     *	@param mTab tab
     *	@param mField field
     *	@param value value
     *	@return null or error message
     */
    CalloutInvoice.prototype.Tax = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value.toString() == "") {
            return "";
        }
        if (Util.getValueOfInt(mTab.getValue("C_Charge_ID")) == 0 && Util.getValueOfInt(mTab.getValue("M_Product_ID")) == 0) {
            return "";
        }
        var column = mField.getColumnName();
        try {
            /**** Start Amit For Tax Type Module ****/
            var taxRule = "";
            var sql = "";
            // JID_1449: Tax is not working in case of charge on invoice line
            var params = Util.getValueOfString(mTab.getValue("C_Invoice_ID")).concat(",", Util.getValueOfString(mTab.getValue("M_Product_ID")) +
                "," + Util.getValueOfString(mTab.getValue("C_Charge_ID")));
            var recDic = VIS.dataContext.getJSONRecord("MInvoice/GetTax", params);

            var _CountVATAX = Util.getValueOfInt(recDic["_CountVATAX"]);

            var isSOTrx = ctx.getWindowContext(windowNo, "IsSOTrx", true) == "Y";

            if (_CountVATAX > 0) {
                taxRule = Util.getValueOfString(recDic["taxRule"]);
            }
            if (taxRule == "T") {
                var taxid = Util.getValueOfInt(recDic["taxId"]);

                if (taxid > 0) {
                    mTab.setValue("C_Tax_ID", taxid);
                }
                else {
                    if (Util.getValueOfInt(mTab.getValue("M_Product_ID")) > 0) {
                        mTab.setValue("M_Product_ID", "");
                        this.setCalloutActive(false);
                        if (recDic["TaxExempt"] == "Y") {
                            return VIS.ADialog.info("TaxNoExemptFound");
                        }
                        else {
                            return VIS.ADialog.info("VATAX_DefineTax");
                        }
                    }
                    else if (Util.getValueOfInt(mTab.getValue("C_Charge_ID")) > 0) {
                        mTab.setValue("C_Charge_ID", "");
                        this.setCalloutActive(false);
                        if (recDic["TaxExempt"] == "Y") {
                            return VIS.ADialog.info("TaxNoExemptFound");
                        }
                        else {
                            return VIS.ADialog.info("VATAX_DefineChargeTax");
                        }
                    }
                }
            }
            /**** end Amit For Tax Type Module ****/
            else {
                //	Check Product
                var M_Product_ID = 0;
                if (column.toString() == "M_Product_ID") {
                    M_Product_ID = value;
                }
                else {
                    M_Product_ID = ctx.getContextAsInt(windowNo, "M_Product_ID");
                }
                var C_Charge_ID = 0;
                if (column.toString() == "C_Charge_ID") {
                    C_Charge_ID = value;
                }
                else {
                    C_Charge_ID = ctx.getContextAsInt(windowNo, "C_Charge_ID");
                }
                this.log.fine("Product=" + M_Product_ID + ", C_Charge_ID=" + C_Charge_ID);
                if (M_Product_ID == 0 && C_Charge_ID == 0) {
                    return this.Amt(ctx, windowNo, mTab, mField, value);
                }

                //	Check Partner Location
                var shipC_BPartner_Location_ID = ctx.getContextAsInt(windowNo, "C_BPartner_Location_ID");
                if (shipC_BPartner_Location_ID == 0) {
                    return this.Amt(ctx, windowNo, mTab, mField, value);
                }
                this.log.fine("Ship BP_Location=" + shipC_BPartner_Location_ID);
                var billC_BPartner_Location_ID = shipC_BPartner_Location_ID;
                this.log.fine("Bill BP_Location=" + billC_BPartner_Location_ID);

                //	Dates 
                var billDate = (ctx.getContext("DateInvoiced"));
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
                    billC_BPartner_Location_ID.toString(), ",",
                    shipC_BPartner_Location_ID.toString(), ",",
                    ctx.getWindowContext(windowNo, "IsSOTrx", true).equals("Y"));

                var C_Tax_ID = Util.getValueOfInt(VIS.dataContext.getJSONRecord("MTax/Get", paramString));

                this.log.info("Tax ID=" + C_Tax_ID);
                //
                if (C_Tax_ID == 0) {
                    // VIS.ADialog.info("");
                }
                else {
                    mTab.setValue("C_Tax_ID", C_Tax_ID);
                }
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        oldValue = null;
        return this.Amt(ctx, windowNo, mTab, mField, value);
    };

    /**
     *	Invoice - Amount.
     *		- called from QtyInvoiced, PriceActual
     *		- calculates LineNetAmt
     *	@param ctx context
     *	@param windowNo window no
     *	@param mTab tab
     *	@param mField field
     *	@param value value
     *	@return null or error message
     */
    CalloutInvoice.prototype.Amt = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        if (Util.getValueOfInt(mTab.getValue("C_Charge_ID")) == 0 && Util.getValueOfInt(mTab.getValue("M_Product_ID")) == 0) {
            return "";
        }

        this.setCalloutActive(true);

        try {
            this.log.log(Level.WARNING, "amt - init");
            var C_UOM_To_ID = ctx.getContextAsInt(windowNo, "C_UOM_ID");
            var M_Product_ID = ctx.getContextAsInt(windowNo, "M_Product_ID");
            var M_PriceList_ID = ctx.getContextAsInt(windowNo, "M_PriceList_ID");
            var isSOTrx = ctx.getWindowContext(windowNo, "IsSOTrx", true) == "Y";

            var paramStr = M_PriceList_ID.toString(); //1

            //Get product price information
            var dr;
            dr = VIS.dataContext.getJSONRecord("MPriceList/GetPriceList", paramStr);
            var PriceListPrecision = 2;
            var StdPrecision = 2;
            var epl = "Y";
            var IsTaxIncluded = "N";
            if (dr != null) {
                StdPrecision = dr["StdPrecision"];
                PriceListPrecision = dr["PriceListPrecision"];
                epl = Util.getValueOfString(dr["EnforcePriceLimit"]);
                IsTaxIncluded = Util.getValueOfString(dr["IsTaxIncluded"]);
            }

            var QtyEntered, QtyInvoiced, PriceEntered, PriceActual, PriceLimit, Discount, PriceList, DiscountSchema;
            //	get values
            //added by bharat Mantis id : 1230
            var orderline_ID = Util.getValueOfInt(mTab.getValue("C_OrderLine_ID"));
            //VAI082:-DevOps ID:-4092-On Change of quantity, price should be set based on discount schema.  
            DiscountApplied = Util.getValueOfBoolean(mTab.getValue("VAS_IsDiscountApplied"));

            QtyEntered = mTab.getValue("QtyEntered");
            QtyInvoiced = mTab.getValue("QtyInvoiced");
            this.log.fine("QtyEntered=" + QtyEntered + ", Invoiced=" + QtyInvoiced + ", UOM=" + C_UOM_To_ID);
            //
            PriceEntered = mTab.getValue("PriceEntered");
            PriceActual = mTab.getValue("PriceActual");
            PriceLimit = mTab.getValue("PriceLimit");
            PriceList = mTab.getValue("PriceList");

            this.log.fine("PriceList=" + PriceList + ", Limit=" + PriceLimit + ", Precision=" + PriceListPrecision);
            this.log.fine("PriceEntered=" + PriceEntered + ", Actual=" + PriceActual);// + ", Discount=" + Discount);

            //Start Amit UOM
            if (mField.getColumnName() == "QtyEntered") {

                //var C_BPartner_ID = ctx.getContextAsInt(windowNo, "C_BPartner_ID", false);

                //if (orderline_ID == 0) {
                //    paramStr = M_Product_ID.toString().concat(",", (mTab.getValue("C_Invoice_ID")).toString() +
                //        "," + Util.getValueOfString(mTab.getValue("M_AttributeSetInstance_ID")) +
                //        "," + C_UOM_To_ID.toString() + "," + ctx.getAD_Client_ID().toString() + "," + C_BPartner_ID.toString() +
                //        "," + QtyEntered.toString());
                //    var prices = VIS.dataContext.getJSONRecord("MInvoice/GetPrices", paramStr);

                //    if (prices != null) {
                //        PriceList = prices["PriceList"];
                //        mTab.setValue("PriceList", PriceList);
                //        PriceEntered = prices["PriceEntered"];
                //        mTab.setValue("PriceLimit", prices["PriceLimit"]);
                //        PriceActual = PriceEntered;
                //        mTab.setValue("PriceActual", PriceActual);
                //        mTab.setValue("PriceEntered", PriceEntered);
                //    }
                //}
            }


            //	Qty changed - recalc price
            if ((mField.getColumnName() == "QtyInvoiced"
                || mField.getColumnName() == "QtyEntered"
                || mField.getColumnName() == "M_Product_ID")
                && !"N" == ctx.getContext("DiscountSchema")) {
                var C_BPartner_ID = ctx.getContextAsInt(windowNo, "C_BPartner_ID");
                //if (mField.getColumnName() == "QtyEntered") {
                //    var paramStr = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(), ",", //2
                //        QtyEntered.toString()); //3
                //    QtyInvoiced = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductTo", paramStr);
                //}

                if (QtyInvoiced == null)
                    QtyInvoiced = QtyEntered;


                //var date = mTab.getValue("DateInvoiced");

                //if (M_PriceList_ID <= 0) {
                //    var invoiceRecord = VIS.dataContext.getJSONRecord("MInvoice/GetInvoice", (mTab.getValue("C_Invoice_ID")).toString());
                //    M_PriceList_ID = invoiceRecord["M_PriceList_ID"];
                //}

                //** Price List - ValidFrom date validation ** Dt:01/02/2021 ** Modified By: Kumar **//
                //var paramsPrice;
                //paramsPrice = M_PriceList_ID.toString().concat(",", mTab.getValue("C_Invoice_ID").toString(), ",",
                //    M_Product_ID.toString(), ",",
                //    C_UOM_To_ID.toString(), ",",
                //    Util.getValueOfString(mTab.getValue("M_AttributeSetInstance_ID")), ",",
                //    "2");

                //Get PriceListversion based on Pricelist
                //var _priceListVersion_ID = 0;
                //if (date == null) {
                //    // when we have date, system will getting pricelist version id in the model - GetProductPricing
                //    _priceListVersion_ID = VIS.dataContext.getJSONRecord("MPriceListVersion/GetM_PriceList_Version_ID", paramsPrice);
                //}

                if (orderline_ID == 0) {

                    //var paramString;
                    //if (date == null) {
                    //    paramString = M_Product_ID.toString().concat(",", C_BPartner_ID.toString(), ",", //2
                    //        QtyInvoiced.toString(), ",", //3
                    //        isSOTrx, ",", //4 
                    //        M_PriceList_ID.toString(), ",", //5
                    //        _priceListVersion_ID.toString(), ",", //6
                    //        date, ",", null, ",",  //7
                    //        C_UOM_To_ID.toString(), ",", 1);
                    //}
                    //else {
                    //    paramString = M_Product_ID.toString().concat(",", C_BPartner_ID.toString(), ",", //2
                    //        QtyInvoiced.toString(), ",", //3
                    //        isSOTrx, ",", //4 
                    //        M_PriceList_ID.toString(), ",", //5
                    //        _priceListVersion_ID.toString(), ",", //6
                    //        date.toString(), ",", null, ",",  //7
                    //        C_UOM_To_ID.toString(), ",", 1);
                    //}

                    //Get product price information
                    //var dr = null;
                    //dr = VIS.dataContext.getJSONRecord("MProductPricing/GetProductPricing", paramString);

                    paramString = M_Product_ID.toString().concat(",", (mTab.getValue("C_Invoice_ID")).toString() +
                        "," + Util.getValueOfString(mTab.getValue("M_AttributeSetInstance_ID")) +
                        "," + C_UOM_To_ID.toString() + "," + ctx.getAD_Client_ID().toString() + "," + C_BPartner_ID.toString() +
                        "," + QtyEntered.toString());
                    var prices = VIS.dataContext.getJSONRecord("MInvoice/GetPrices", paramString);
                    var isDiscountApplied = "N";

                    if (prices != null) {
                        // VIS_0045: Handle zero price issue on quantity change.
                        if (mField.getColumnName() == "M_Product_ID" ||
                            (Util.getValueOfDecimal(prices["PriceList"]) != 0 && mTab.getValue("PriceList") == 0)) {
                            PriceList = prices["PriceList"];
                            mTab.setValue("PriceList", PriceList);
                        }

                        PriceEntered = prices["PriceEntered"];
                        DiscountSchema = Util.getValueOfString(prices["DiscountSchema"]);
                        isDiscountApplied = Util.getValueOfString(prices["IsDiscountApplied"]);

                        // VIS_0045: Handle zero price issue on quantity change.
                        if (mField.getColumnName() == "M_Product_ID" ||
                            (Util.getValueOfDecimal(prices["PriceLimit"]) != 0 && mTab.getValue("PriceLimit") == 0)) {
                            mTab.setValue("PriceLimit", prices["PriceLimit"]);
                        }
                    }

                    //if (PriceEntered == null) {
                    //    PriceEntered = dr.PriceStd;
                    //}
                    this.log.fine("amt - QtyChanged -> PriceActual=" + PriceEntered
                        + ", PriceEntered=" + PriceEntered);//+ ", Discount=" + dr.Discount
                    PriceActual = PriceEntered;

                    // VIS_0045: Handle zero price issue on quantity change.
                    // if Discount applied with Discount Schema  and price entered not ZERO then change price
                    //VAI082:-DevOps ID:-4092-On change of quantity, price should be set based on discount schema.               
                    if (mField.getColumnName() == "M_Product_ID" ||
                        ((isDiscountApplied.equals("Y") || DiscountApplied)
                            && PriceEntered != 0) || (PriceEntered != 0 && mTab.getValue("PriceEntered") == 0)) {
                        mTab.setValue("VAS_IsDiscountApplied", isDiscountApplied.equals("Y"));
                        mTab.setValue("PriceActual", PriceActual);
                        mTab.setValue("PriceEntered", PriceEntered);
                    }
                }
                ctx.setContext(windowNo, "DiscountSchema", DiscountSchema ? "Y" : "N");
            }

            else if (mField.getColumnName() == "PriceActual") {
                PriceActual = Util.getValueOfDecimal(value.toFixed(PriceListPrecision));
                PriceEntered = PriceActual;
                //
                this.log.fine("amt - PriceActual=" + PriceActual
                    + " -> PriceEntered=" + PriceEntered);
                mTab.setValue("PriceEntered", PriceEntered);
                mTab.SetValue("PriceActual", PriceActual);
            }
            else if (mField.getColumnName() == "PriceEntered") {
                PriceEntered = Util.getValueOfDecimal(value.toFixed(PriceListPrecision));
                PriceActual = PriceEntered;
                if (PriceActual == null) {
                    PriceActual = PriceEntered;
                }
                //
                this.log.fine("amt - PriceEntered=" + PriceEntered
                    + "-> PriceActual=" + PriceActual);
                mTab.setValue("PriceActual", PriceActual);
                mTab.setValue("PriceEntered", PriceEntered);
            }

            //	Check PriceLimit
            var OverwritePriceLimit = false;
            //var epl = ctx.getContext("EnforcePriceLimit");
            var enforce = isSOTrx && epl != null && epl == "Y";
            //if (epl == "") {
            //    var paramString = Util.getValueOfInt(mTab.getValue("C_Invoice_ID")).toString();
            //    var C_Invoice = VIS.dataContext.getJSONRecord("MInvoice/GetInvoice", paramString);
            //    //var sql = "SELECT EnforcePriceLimit FROM M_PriceList WHERE IsActive = 'Y' AND M_PriceList_ID = " + C_Invoice["M_PriceList_ID"];
            //    //epl = VIS.DB.executeScalar(sql);
            //    enforce = (C_Invoice["IsSOTrx"] && epl != null && epl == "Y");
            //}

            OverwritePriceLimit = VIS.MRole.getDefault().getIsOverwritePriceLimit();
            if (enforce && OverwritePriceLimit) {
                enforce = false;
            }
            //	Check Price Limit?
            if (enforce && Util.getValueOfDouble(PriceLimit) != 0.0
                && PriceActual.compareTo(PriceLimit) < 0) {
                PriceActual = PriceLimit;

                PriceEntered = PriceActual;
                if (PriceEntered == null) {
                    PriceEntered = PriceLimit;
                }
                this.log.fine("amt =(under) PriceEntered=" + PriceEntered + ", Actual" + PriceLimit);
                mTab.setValue("PriceActual", PriceLimit);
                mTab.setValue("PriceEntered", PriceEntered);

                VIS.ADialog.info("UnderLimitPrice");
                //	Repeat Discount calc
                if (Util.getValueOfInt(PriceList) != 0) {
                    Discount = (PriceList - PriceActual) / PriceList * 100.0;
                    if (Util.scale(Discount) > 2) {
                        Discount = Discount.toFixed(2);
                    }
                }
            }

            //	Line Net Amt
            PriceEntered = Util.getValueOfDecimal(mTab.getValue("PriceEntered"));
            var lineNetAmt = QtyEntered * PriceEntered;

            if (Util.scale(lineNetAmt) > StdPrecision) {
                lineNetAmt = Util.getValueOfDecimal(lineNetAmt.toFixed(StdPrecision));
            }
            this.log.info("amt = LineNetAmt=" + lineNetAmt);
            mTab.setValue("LineNetAmt", lineNetAmt);

            //	Calculate Tax Amount for PO / SO (SI_0339)
            var taxAmt = VIS.Env.ZERO;
            if (mField.getColumnName() == "TaxAmt") {
                taxAmt = mTab.getValue("TaxAmt");
            }
            else {
                var taxID = mTab.getValue("C_Tax_ID");
                if (taxID != null) {
                    var C_Tax_ID = taxID;//.intValue();
                    var IsTaxIncluded = this.IsTaxIncluded(windowNo, ctx);
                    var paramString = C_Tax_ID.toString().concat(",", lineNetAmt.toString(), ",", //2
                        IsTaxIncluded, ",", //3
                        StdPrecision.toString() //4 
                    ); //7          
                    var dr = null;
                    taxAmt = VIS.dataContext.getJSONRecord("MTax/CalculateTax", paramString);
                    mTab.setValue("TaxAmt", taxAmt);

                    // Set Surcharge Amount to zero
                    if (mTab.getField("SurchargeAmt") != null) {
                        mTab.setValue("SurchargeAmt", 0);
                    }
                }
            }

            if (IsTaxIncluded) {
                mTab.setValue("LineTotalAmt", (Util.getValueOfDecimal(lineNetAmt)));
            }
            else {
                mTab.setValue("LineTotalAmt", Util.getValueOfDecimal((lineNetAmt + taxAmt).toFixed(StdPrecision)));
            }

            /*VIS_045: 01-July-2025, Calculate TCS tax Amount */
            if (isSOTrx && mTab.findColumn("VA106_TaxCollectedAtSource_ID") > -1 && Util.getValueOfInt(mTab.getValue("VA106_TaxCollectedAtSource_ID")) > 0) {
                if (Util.getValueOfDecimal(mTab.getValue("LineTotalAmt")) != 0) {
                    paramStr = Util.getValueOfInt(mTab.getValue("VA106_TaxCollectedAtSource_ID")).toString() + "," +
                        Util.getValueOfDecimal(mTab.getValue("LineTotalAmt")).toString();

                    var tcsTaxDetails = VIS.dataContext.getJSONRecord("MInvoice/VA106_CalculateTCSTax", paramStr);

                    if (tcsTaxDetails != null) {
                        mTab.setValue("VA106_TCSAmount", Util.getValueOfDecimal(tcsTaxDetails["VA106_TCSTaxValue"]));
                    }
                }
                else {
                    mTab.setValue("VA106_TCSAmount", 0);
                }
            }


        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /**
     * This function is used to calculate the TCS tax 
     * Calculate on Value -- Taxable amt + Tax Amt + Surcharge Amt = Line Total Amt
     * Rate will be picked from Tax Collected at source screen
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutInvoice.prototype.VA106_CalculateTCSTax = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive()) {
            return "";
        }
        if (value == null || value.toString() == "") {
            mTab.setValue("VA106_TCSAmount", 0);
        }
        try {
            this.setCalloutActive(true);

            if (Util.getValueOfDecimal(mTab.getValue("LineTotalAmt")) != 0) {
                var paramStr = Util.getValueOfInt(mTab.getValue("VA106_TaxCollectedAtSource_ID")).toString() + "," +
                    Util.getValueOfDecimal(mTab.getValue("LineTotalAmt")).toString();

                var tcsTaxDetails = VIS.dataContext.getJSONRecord("MInvoice/VA106_CalculateTCSTax", paramStr);

                if (tcsTaxDetails != null) {
                    mTab.setValue("VA106_TCSAmount", Util.getValueOfDecimal(tcsTaxDetails["VA106_TCSTaxValue"]));
                }
            }
            else {
                mTab.setValue("VA106_TCSAmount", 0);
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


    /**
     * 	Is Tax Included
     *	@param windowNo window no
     *	@return tax included (default: false)
     */
    CalloutInvoice.prototype.IsTaxIncluded = function (windowNo, ctx) {
        //  

        //var ctx = Env.getContext();
        var ss = ctx.getContext("IsTaxIncluded");
        try {
            //	Not Set Yet
            if (ss.toString().length == 0) {
                var M_PriceList_ID = ctx.getContextAsInt(windowNo, "M_PriceList_ID");
                if (M_PriceList_ID == 0) {
                    return false;
                }
                //ss = VIS.DB.executeScalar("SELECT IsTaxIncluded FROM M_PriceList WHERE M_PriceList_ID=" + M_PriceList_ID, null, null).toString();

                ss = VIS.dataContext.getJSONRecord("MPriceList/GetTaxIncluded", M_PriceList_ID.toString());
                if (ss == null) {
                    ss = "N";
                }
                ctx.setContext(windowNo, "IsTaxIncluded", ss);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "Y" == ss;
    };

    /**
     *	Invoice Line - Quantity.
     *		- called from C_UOM_ID, QtyEntered, QtyInvoiced
     *		- enforces qty UOM relationship
     *	@param ctx context
     *	@param windowNo window no
     *	@param mTab tab
     *	@param mField field
     *	@param value value
     *	@return null or error message
     */
    CalloutInvoice.prototype.Qty = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }

        if (Util.getValueOfInt(mTab.getValue("C_Charge_ID")) == 0 && Util.getValueOfInt(mTab.getValue("M_Product_ID")) == 0) {
            return "";
        }
        this.setCalloutActive(true);
        try {
            var paramStr = "";
            var M_Product_ID = ctx.getContextAsInt(windowNo, "M_Product_ID");
            this.log.log(Level.WARNING, "qty - init - M_Product_ID=" + M_Product_ID);
            var QtyInvoiced, QtyEntered, PriceActual, PriceEntered;

            //	No Product
            if (M_Product_ID == 0) {
                QtyEntered = mTab.getValue("QtyEntered");
                mTab.setValue("QtyInvoiced", QtyEntered);
            }
            //	UOM Changed - convert from Entered -> Product
            // JID_0540: System should check the Price in price list for selected Attribute set instance and UOM,If price is not there is will multiple the base UOM price with UOM conversion multipy value and set that price.
            else if (mField.getColumnName().toString().equals("C_UOM_ID") || mField.getColumnName().toString().equals("M_AttributeSetInstance_ID") || mField.getColumnName().toString().equals("VAS_ContractLine_ID")) {
                var C_UOM_To_ID = Util.getValueOfInt(mTab.getValue("C_UOM_ID"));
                QtyEntered = mTab.getValue("QtyEntered");

                /*** Start Amit ***/

                var C_BPartner_ID = ctx.getContextAsInt(windowNo, "C_BPartner_ID", false);
                var bpartner = VIS.dataContext.getJSONRecord("MBPartner/GetBPartner", C_BPartner_ID.toString());

                paramStr = M_Product_ID.toString().concat(",", (mTab.getValue("C_Invoice_ID")).toString() +
                    "," + Util.getValueOfString(mTab.getValue("M_AttributeSetInstance_ID")) +
                    "," + C_UOM_To_ID.toString() + "," + ctx.getAD_Client_ID().toString() + "," + C_BPartner_ID.toString() +
                    "," + QtyEntered.toString());
                var prices = VIS.dataContext.getJSONRecord("MInvoice/GetPrices", paramStr);

                if (prices != null) {
                    countEd011 = prices["countEd011"];
                    var countVAPRC = prices["countVAPRC"];

                    // No UOM and Advance Pricing Modules
                    if (countEd011 <= 0 && countVAPRC <= 0) {
                        var QtyEntered1 = null;

                        if (QtyEntered != null) {
                            paramStr = C_UOM_To_ID.toString();
                            var precision = VIS.dataContext.getJSONRecord("MUOM/GetPrecision", paramStr);
                            QtyEntered1 = QtyEntered.toFixed(precision);
                        }

                        if (QtyEntered != QtyEntered1) {
                            this.log.fine("Corrected QtyEntered Scale UOM=" + C_UOM_To_ID
                                + "; QtyEntered=" + QtyEntered + "->" + QtyEntered1);
                            QtyEntered = QtyEntered1;
                            mTab.setValue("QtyEntered", QtyEntered);
                        }

                        paramStr = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(), ",", QtyEntered.toString());
                        QtyInvoiced = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramStr);

                        if (QtyInvoiced == null) {
                            QtyInvoiced = QtyEntered;
                        }

                        var conversion = QtyEntered != QtyInvoiced;
                        PriceActual = mTab.getValue("PriceActual");

                        paramStr = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(), ",", PriceActual.toString());
                        PriceEntered = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramStr);

                        if (PriceEntered == null) {
                            PriceEntered = PriceActual;
                        }
                        this.log.fine("qty - UOM=" + C_UOM_To_ID
                            + ", QtyEntered/PriceActual=" + QtyEntered + "/" + PriceActual
                            + " -> " + conversion
                            + " QtyInvoiced/PriceEntered=" + QtyInvoiced + "/" + PriceEntered);
                        ctx.setContext(windowNo, "UOMConversion", conversion ? "Y" : "N");
                        mTab.setValue("QtyInvoiced", QtyInvoiced);
                        mTab.setValue("PriceEntered", PriceEntered);
                    }
                    else {
                        var PriceList = prices["PriceList"];
                        mTab.setValue("PriceList", PriceList);
                        PriceEntered = prices["PriceEntered"];
                        mTab.setValue("PriceLimit", prices["PriceLimit"]);
                        mTab.setValue("PriceActual", PriceEntered);
                        mTab.setValue("PriceEntered", PriceEntered);

                        //Get precision from server side
                        paramStr = C_UOM_To_ID.toString().concat(",");
                        var gp = VIS.dataContext.getJSONRecord("MUOM/GetPrecision", paramStr);

                        var QtyEntered1 = QtyEntered.toFixed(Util.getValueOfInt(gp));

                        if (QtyEntered != QtyEntered1) {
                            this.log.fine("Corrected QtyEntered Scale UOM=" + C_UOM_To_ID
                                + "; QtyEntered=" + QtyEntered + "->" + QtyEntered1);
                            QtyEntered = QtyEntered1;
                            mTab.setValue("QtyEntered", QtyEntered);
                        }

                        //Conversion of Qty Ordered
                        paramStr = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(), ","
                            , QtyEntered.toString());
                        var pc = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramStr);
                        QtyInvoiced = pc;

                        var conversion = false
                        if (QtyInvoiced != null) {
                            conversion = QtyEntered != QtyInvoiced;
                        }
                        if (QtyInvoiced == null) {
                            conversion = false;
                            QtyInvoiced = 1;
                        }
                        if (conversion) {
                            mTab.setValue("QtyInvoiced", QtyInvoiced);
                        }
                        else {
                            mTab.setValue("QtyInvoiced", (QtyInvoiced * QtyEntered1));
                        }
                    }
                }
            }
            //	QtyEntered changed - calculate QtyInvoiced
            else if (mField.getColumnName().equals("QtyEntered")) {
                var C_UOM_To_ID = ctx.getContextAsInt(windowNo, "C_UOM_ID");
                QtyEntered = value;

                var QtyEntered1 = null;

                if (QtyEntered != null) {
                    var precision = VIS.dataContext.getJSONRecord("MUOM/GetPrecision", C_UOM_To_ID.toString());
                    QtyEntered1 = QtyEntered.toFixed(precision);
                }

                //if (QtyEntered.Value.compareTo(QtyEntered1.Value) != 0)
                if (QtyEntered != QtyEntered1) {
                    this.log.fine("Corrected QtyEntered Scale UOM=" + C_UOM_To_ID
                        + "; QtyEntered=" + QtyEntered + "->" + QtyEntered1);
                    QtyEntered = QtyEntered1;
                    mTab.setValue("QtyEntered", QtyEntered);
                }

                paramString = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(),
                    ",", QtyEntered.toString());
                QtyInvoiced = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramString);

                if (QtyInvoiced == null) {
                    QtyInvoiced = QtyEntered;
                }

                var conversion = QtyEntered != QtyInvoiced;

                this.log.fine("qty - UOM=" + C_UOM_To_ID
                    + ", QtyEntered=" + QtyEntered
                    + " -> " + conversion
                    + " QtyInvoiced=" + QtyInvoiced);
                ctx.setContext(windowNo, "UOMConversion", conversion ? "Y" : "N");
                mTab.setValue("QtyInvoiced", QtyInvoiced);
            }
            //	QtyInvoiced changed - calculate QtyEntered (should not happen)
            else if (mField.getColumnName().equals("QtyInvoiced")) {
                var C_UOM_To_ID = ctx.getContextAsInt(windowNo, "C_UOM_ID");

                QtyInvoiced = value;

                var paramString = M_Product_ID.toString();
                var precision = VIS.dataContext.getJSONRecord("MProduct/GetUOMPrecision", paramString);

                var QtyInvoiced1 = null;

                if (QtyInvoiced != null) {
                    QtyInvoiced1 = Util.getValueOfDecimal(QtyInvoiced.toFixed(precision));
                }

                if (QtyInvoiced != QtyInvoiced1) {
                    this.log.fine("Corrected QtyInvoiced Scale "
                        + QtyInvoiced + "->" + QtyInvoiced1);
                    QtyInvoiced = QtyInvoiced1;
                    mTab.setValue("QtyInvoiced", QtyInvoiced);
                }
                paramString = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(),
                    ",", QtyInvoiced.toString());
                QtyEntered = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductTo", paramString);
                if (QtyEntered == null) {
                    QtyEntered = QtyInvoiced;
                }

                var conversion = QtyInvoiced != QtyEntered;

                this.log.fine("qty - UOM=" + C_UOM_To_ID
                    + ", QtyInvoiced=" + QtyInvoiced
                    + " -> " + conversion
                    + " QtyEntered=" + QtyEntered);
                ctx.setContext(windowNo, "UOMConversion", conversion ? "Y" : "N");
                mTab.setValue("QtyEntered", QtyEntered);
            }
            //

        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        finally {

            this.setCalloutActive(false);
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// Order Header - PriceList.
    /// (used also in Invoice)
    /// - C_Currency_ID
    /// 	- IsTaxIncluded
    /// 	Window Context:
    /// 	- EnforcePriceLimit
    /// 	- StdPrecision
    /// 	- M_PriceList_Version_ID
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current Window No</param>
    /// <param name="mTab">Grid Tab</param>
    /// <param name="mField">Grid Field</param>
    /// <param name="value">New Value</param>
    /// <returns>null or error message</returns>
    CalloutInvoice.prototype.PriceList = function (ctx, windowNo, mTab, mField, value, oldValue) {

        var sql = "";
        var dr = null;
        try {
            if (value == null || value.toString() == "") {
                return "";
            }
            var M_PriceList_ID = Util.getValueOfInt(value.toString());
            if (M_PriceList_ID == null || M_PriceList_ID == 0)
                return "";

            //sql = "SELECT pl.IsTaxIncluded,pl.EnforcePriceLimit,pl.C_Currency_ID,c.StdPrecision,"
            //    + "plv.M_PriceList_Version_ID,plv.ValidFrom "
            //    + "FROM M_PriceList pl,C_Currency c,M_PriceList_Version plv "
            //    + "WHERE pl.C_Currency_ID=c.C_Currency_ID"
            //    + " AND pl.M_PriceList_ID=plv.M_PriceList_ID"
            //    + " AND pl.M_PriceList_ID=" + M_PriceList_ID						//	1
            //    + "ORDER BY plv.ValidFrom DESC";
            //	Use newest price list - may not be future

            //DataSet ds = VIS.DB..executeDataset(sql, null);
            //for (int i = 0; i < ds.Tables[0].Rows.Count; i++)

            //dr = VIS.DB.executeReader(sql);
            dr = VIS.dataContext.getJSONRecord("MPriceList/GetPriceListData", value.toString());
            //if (dr.read()) {
            if (dr != null) {
                //DataRow dr = ds.Tables[0].Rows[i];
                //	Tax Included
                mTab.setValue("IsTaxIncluded", (Boolean)("Y".equals(dr["IsTaxIncluded"])));
                //	Price Limit Enforce
                ctx.setContext(windowNo, "EnforcePriceLimit", dr["EnforcePriceLimit"]);
                //	Currency
                var ii = Util.getValueOfInt(dr["C_Currency_ID"]);
                mTab.setValue("C_Currency_ID", ii);
                var prislst = dr["M_PriceList_Version_ID"];
                //	PriceList Version
                ctx.setContext(windowNo, "M_PriceList_Version_ID", prislst);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.log(Level.severe, null, err);
            return err.message;
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /** Adhoc Payment - Validating DueDate ** Dt: 18/01/2021 ** Modified By: Kumar **/
    CalloutInvoice.prototype.CheckDueDate = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }

        if (mTab.getValue("DateInvoiced") == null || mTab.getValue("DateInvoiced") == "") {
            this.setCalloutActive(false);
            return "";
        }
        else {
            try {
                this.setCalloutActive(true);

                if (mTab.getValue("DateInvoiced") != null && mTab.getValue("DueDate") != null) {
                    var invDate = new Date(mTab.getValue("DateInvoiced"));
                    var dueDate = new Date(mTab.getValue("DueDate"));
                    if (dueDate < invDate) {
                        VIS.ADialog.error("DueDateLessThanInvoiceDate");
                        mTab.setValue("DueDate", "");
                    }
                }
                ctx = windowNo = mTab = mField = value = oldValue = null;
                this.setCalloutActive(false);
                //---------------------End---------------------------------------
            }
            catch (err) {
                this.setCalloutActive(false);
                return err;
            }
        }
        return "";
    };

    /**
 * This function is used to get the detail of Invoice line
 * @param {any} ctx
 * @param {any} windowNo
 * @param {any} mTab
 * @param {any} mField
 * @param {any} value
 * @param {any} oldValue
 */
    CalloutInvoice.prototype.GetInvoiceLineDetail = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        try {
            this.setCalloutActive(true);

            var paramString = value.toString();
            var invoiceRecord = VIS.dataContext.getJSONRecord("MInvoice/GetInvoiceLineDetail", paramString);

            if (invoiceRecord != null) {
                mTab.setValue("M_Product_ID", invoiceRecord["M_Product_ID"]);
                mTab.setValue("M_AttributeSetInstance_ID", invoiceRecord["M_AttributeSetInstance_ID"]);
                mTab.setValue("C_UOM_ID", invoiceRecord["C_UOM_ID"]);
                mTab.setValue("QtyEntered", invoiceRecord["QtyEntered"]);
                mTab.setValue("QtyInvoiced", invoiceRecord["QtyInvoiced"]);
            }
            ctx = windowNo = mTab = mField = value = oldValue = null;
            this.setCalloutActive(false);
        }
        catch (err) {
            this.setCalloutActive(false);
            return err;
        }
        return "";
    };


    VIS.Model.CalloutInvoice = CalloutInvoice;


    //**************ProvisionalInvoice Start*********
    function ProvisionalInvoice() {
        VIS.CalloutEngine.call(this, "VIS.ProvisionalInvoice"); //must call
    };
    VIS.Utility.inheritPrototype(ProvisionalInvoice, VIS.CalloutEngine);//inherit CalloutEngine

    //call when change the DateInvoiced field on Provisional Invoice window
    ProvisionalInvoice.prototype.DateAcct = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "" || this.isCalloutActive()) {
            return "";
        }

        this.setCalloutActive(true);
        try {
            //Can be change according to DateInvoiced incase of Provisional Invoice window
            mTab.setValue("DateAcct", value);
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// Order Header - PriceList.
    /// (used also in Invoice)
    /// - C_Currency_ID
    /// 	- IsTaxIncluded
    /// 	Window Context:
    /// 	- EnforcePriceLimit
    /// 	- M_PriceList_Version_ID
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current Window No</param>
    /// <param name="mTab">Grid Tab</param>
    /// <param name="mField">Grid Field</param>
    /// <param name="value">New Value</param>
    /// <returns>null or error message</returns>
    ProvisionalInvoice.prototype.PriceList = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        try {

            var M_PriceList_ID = value.toString();
            if (Util.getValueOfInt(M_PriceList_ID) == 0)
                return "";
            this.setCalloutActive(true);
            if (steps) {
                this.log.warning("init");
            }

            //	Use net price list - may not be future
            var dr = VIS.dataContext.getJSONRecord("MPriceList/GetPriceListDataForProvisionalInvoice", M_PriceList_ID);
            if (dr != null) {
                //	Tax Included
                mTab.setValue("IsTaxIncluded", "Y" == dr["IsTaxIncluded"]);

                //	Price Limit Enforce
                ctx.setContext(windowNo, "EnforcePriceLimit", dr["EnforcePriceLimit"]);

                //	Currency
                var ii = Util.getValueOfInt(dr["C_Currency_ID"]);
                mTab.setValue("C_Currency_ID", ii);

                //	PriceList Version
                var prislst = Util.getValueOfInt(dr["M_PriceList_Version_ID"]);
                ctx.setContext(windowNo, "M_PriceList_Version_ID", prislst);

            }
        }
        catch (err) {
            this.setCalloutActive(false);
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
     *	Invoice Header- BPartner.
     *		- M_PriceList_ID (+ Context)
     *		- C_BPartner_Location_ID
     *		- AD_User_ID
     *		- PaymentMethod
     *		- C_PaymentTerm_ID
     *	@param ctx context
     *	@param windowNo window no
     *	@param mTab tab
     *	@param mField field
     *	@param value value
     *	@return null or error message
     */
    ProvisionalInvoice.prototype.BPartner = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        try {

            var C_BPartner_ID = Util.getValueOfInt(value);
            if (C_BPartner_ID == null || C_BPartner_ID == 0) {
                return "";
            }

            this.setCalloutActive(true);

            var isSOTrx = ctx.isSOTrx(windowNo);

            var dr = VIS.dataContext.getJSONRecord("MBPartner/GetBPDataForProvisionalInvoice", C_BPartner_ID);
            if (dr != null) {

                //	PriceList 
                var ii = Util.getValueOfInt(dr[isSOTrx ? "M_PriceList_ID" : "PO_PriceList_ID"]);
                if (ii > 0) {
                    mTab.setValue("M_PriceList_ID", ii);
                }

                // Payment Method
                var _CountVA009 = dr["countVA009"];
                if (_CountVA009 > 0) {
                    var _PaymentMethod_ID = 0

                    if (!isSOTrx && Util.getValueOfString(dr["IsVendor"]) == "Y") {
                        //In case of Purchase Order and vendor
                        _PaymentMethod_ID = Util.getValueOfInt(dr["VA009_PO_PaymentMethod_ID"]);
                    }
                    else if (isSOTrx && Util.getValueOfString(dr["IsCustomer"]) == "Y") {
                        //In case of Sales Order and customer
                        _PaymentMethod_ID = Util.getValueOfInt(dr["VA009_PaymentMethod_ID"]);
                    }
                    else {
                        _PaymentMethod_ID = 0;
                    }

                    if (_PaymentMethod_ID == 0)
                        mTab.setValue("VA009_PaymentMethod_ID", null);
                    else {
                        mTab.setValue("VA009_PaymentMethod_ID", _PaymentMethod_ID);
                    }
                }

                //  Payment Term
                ii = Util.getValueOfInt(dr[isSOTrx ? "C_PaymentTerm_ID" : "PO_PaymentTerm_ID"]);
                if (ii > 0) {
                    mTab.setValue("C_PaymentTerm_ID", ii);
                }

                //	Location
                var locID = Util.getValueOfInt(dr["C_BPartner_Location_ID"]);
                /*VIS_0045: 04-May-2023 - DevOps Task ID: 2110*/
                //	overwritten by InfoBP selection - works only if InfoWindow
                //	was used otherwise creates error (uses last value, may bevar to differnt BP)
                if (C_BPartner_ID.toString().equals(ctx.getWindowTabContext(windowNo, VIS.EnvConstants.TAB_INFO, "C_BPARTNER_ID").toString())) {
                    var loc = ctx.getWindowTabContext(windowNo, VIS.EnvConstants.TAB_INFO, "C_BPARTNER_LOCATION_ID");
                    if (loc && loc.toString().length > 0) {
                        locID = parseInt(loc);
                    }
                }
                if (locID == 0) {
                    mTab.setValue("C_BPartner_Location_ID", null);
                }
                else {
                    mTab.setValue("C_BPartner_Location_ID", locID);
                }

                /*VIS_0045: 04-May-2023 - DevOps Task ID: 2110*/
                //	Contact - overwritten by InfoBP selection
                var contID = Util.getValueOfInt(dr["AD_User_ID"]);
                if (C_BPartner_ID.toString().equals(ctx.getWindowTabContext(windowNo, VIS.EnvConstants.TAB_INFO, "C_BPARTNER_ID").toString())) {
                    var cont = ctx.getWindowTabContext(windowNo, VIS.EnvConstants.TAB_INFO, "AD_USER_ID");
                    if (cont && cont.toString().length > 0) {
                        contID = parseInt(cont);
                    }
                }
                if (contID == 0) {
                    mTab.setValue("AD_User_ID", null);
                }
                else {
                    mTab.setValue("AD_User_ID", contID);
                }
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
        }
        this.setCalloutActive(false);
        return this.PriceList(ctx, windowNo, mTab, mField, mTab.getValue("M_PriceList_ID"));
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    VIS.Model.ProvisionalInvoice = ProvisionalInvoice;
    //**************ProvisionalInvoice End*********


})(VIS, jQuery);