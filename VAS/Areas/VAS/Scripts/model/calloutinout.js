; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    function CalloutInOut() {
        VIS.CalloutEngine.call(this, "VIS.CalloutInOut");//must call
    };
    VIS.Utility.inheritPrototype(CalloutInOut, VIS.CalloutEngine); //inherit prototype

    /// <summary>
    /// C_Order - Order Defaults.
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="windowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns>error message or ""</returns>
    CalloutInOut.prototype.Order = function (ctx, windowNo, mTab, mField, value, oldValue) {
        // 

        if (value == null || value.toString() == "") {
            return "";
        }
        try {
            var C_Order_ID = Util.getValueOfInt(value.toString());
            if (C_Order_ID == null || C_Order_ID == 0) {
                return "";
            }
            //	No Callout Active to fire dependent values
            if (this.isCalloutActive())	//	prevent recursive
            {
                return "";
            }
            var paramString = C_Order_ID.toString();
            var dr = VIS.dataContext.getJSONRecord("MOrder/GetOrder", paramString);
            var DataPrefix = VIS.dataContext.getJSONRecord("ModulePrefix/GetModulePrefix", "VA077_");
            if (Util.getValueOfInt(dr["ID"]) != 0) {
                mTab.setValue("DateOrdered", dr["DateOrdered"]);
                mTab.setValue("POReference", dr["POReference"]);
                if (mTab.getValue("AD_Org_ID") != Util.getValueOfInt(dr["AD_Org_ID"])) {    // If Org ID is same then no need to update the Org ID, else Document type gets refreshed
                    mTab.setValue("AD_Org_ID", Util.getValueOfInt(dr["AD_Org_ID"]));
                }
                //
                mTab.setValue("DeliveryRule", dr["DeliveryRule"]);
                mTab.setValue("DeliveryViaRule", dr["DeliveryViaRule"]);
                mTab.setValue("M_Shipper_ID", Util.getValueOfInt(dr["M_Shipper_ID"]));
                mTab.setValue("FreightCostRule", dr["FreightCostRule"]);
                mTab.setValue("FreightAmt", dr["FreightAmt"]);

                mTab.setValue("C_BPartner_ID", Util.getValueOfInt(dr["C_BPartner_ID"]));
                //sraval: source forge bug # 1503219 - added to default ship to location
                mTab.setValue("C_BPartner_Location_ID", Util.getValueOfInt(dr["C_BPartner_Location_ID"]));

                mTab.setValue("AD_OrgTrx_ID", Util.getValueOfInt(dr["AD_OrgTrx_ID"]));
                mTab.setValue("C_Activity_ID", Util.getValueOfInt(dr["C_Activity_ID"]));
                mTab.setValue("C_Campaign_ID", Util.getValueOfInt(dr["C_Campaign_ID"]));
                mTab.setValue("C_Project_ID", Util.getValueOfInt(dr["C_Project_ID"]));
                mTab.setValue("User1_ID", Util.getValueOfInt(dr["User1_ID"]));
                mTab.setValue("User2_ID", Util.getValueOfInt(dr["User2_ID"]));

                // Added by Bharat on 30 Jan 2018 to set Inco Term from Order
                if (mTab.getField("C_IncoTerm_ID") != null) {
                    mTab.setValue("C_IncoTerm_ID", Util.getValueOfInt(dr["C_IncoTerm_ID"]));
                }

                var isReturnTrx = mTab.getValue("IsReturnTrx");
                if (isReturnTrx) {
                    mTab.setValue("Orig_Order_ID", dr["Orig_Order_ID"]);
                    mTab.setValue("Orig_InOut_ID", dr["Orig_InOut_ID"]);
                    // added by vivek on 09/10/2017 advised by pradeep to set drop ship checkbox value
                    if (Util.getValueOfString(dr["IsDropShip"]) == "Y") {
                        mTab.setValue("IsDropShip", true);
                    }
                    else {
                        mTab.setValue("IsDropShip", false);
                    }
                }
                mTab.setValue("M_Warehouse_ID", Util.getValueOfInt(dr["M_Warehouse_ID"]));
                if (DataPrefix["VA077_"]) {
                    if (dr["VA077_HistoricContractDate"] != "") {
                        mTab.setValue("VA077_HistoricContractDate", Dateoffset(dr["VA077_HistoricContractDate"]));
                    }
                    if (dr["VA077_ChangeStartDate"] != "") {
                        mTab.setValue("VA077_ChangeStartDate", Dateoffset(dr["VA077_ChangeStartDate"]));
                    }
                    if (dr["VA077_ContractCPStartDate"] != "") {
                        mTab.setValue("VA077_ContractCPStartDate", Dateoffset(dr["VA077_ContractCPStartDate"]));
                    }
                    if (dr["VA077_ContractCPEndDate"] != "") {
                        mTab.setValue("VA077_ContractCPEndDate", Dateoffset(dr["VA077_ContractCPEndDate"]));
                    }
                    mTab.setValue("VA077_PartialAmtCatchUp", Util.getValueOfDecimal(dr["VA077_PartialAmtCatchUp"]));
                    mTab.setValue("VA077_OldAnnualContractTotal", Util.getValueOfDecimal(dr["VA077_OldAnnualContractTotal"]));
                    mTab.setValue("VA077_AdditionalAnnualCharge", Util.getValueOfDecimal(dr["VA077_AdditionalAnnualCharge"]));
                    mTab.setValue("VA077_NewAnnualContractTotal", Util.getValueOfDecimal(dr["VA077_NewAnnualContractTotal"]));
                    mTab.setValue("VA077_SalesCoWorker", Util.getValueOfDecimal(dr["VA077_SalesCoWorker"]));
                    mTab.setValue("VA077_SalesCoWorkerPer", Util.getValueOfDecimal(dr["VA077_SalesCoWorkerPer"]));
                    mTab.setValue("VA077_TotalMarginAmt", Util.getValueOfDecimal(dr["VA077_TotalMarginAmt"]));
                    mTab.setValue("VA077_TotalPurchaseAmt", Util.getValueOfDecimal(dr["VA077_TotalPurchaseAmt"]));
                    mTab.setValue("VA077_TotalSalesAmt", Util.getValueOfDecimal(dr["VA077_TotalSalesAmt"]));
                    mTab.setValue("VA077_MarginPercent", Util.getValueOfDecimal(dr["VA077_MarginPercent"]));
                }

            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
            //MessageBox.Show("CalloutInOut--Order Defaults");
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// InOut - DocType.
    /// - sets MovementType
    /// - gets DocNo
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="windowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns>error message or ""</returns>
    CalloutInOut.prototype.DocType = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }
        var C_DocType_ID = Util.getValueOfInt(value.toString());// (int)value;
        if (C_DocType_ID == null || C_DocType_ID == 0) {
            return "";
        }
        //var sql = "SELECT d.docBaseType, d.IsDocNoControlled, s.CurrentNext, d.IsReturnTrx "
        //    + "FROM C_DocType d, AD_Sequence s "
        //    + "WHERE C_DocType_ID=" + C_DocType_ID		//	1
        //    + " AND d.DocNoSequence_ID=s.AD_Sequence_ID(+)";
        // var dr = null;
        var paramString = C_DocType_ID.toString();
        var dr = VIS.dataContext.getJSONRecord("MInOut/GetDocTypeData", paramString);
        try {
            ctx.setContext(windowNo, "C_DocTypeTarget_ID", C_DocType_ID);
            //dr = VIS.DB.executeReader(sql, null, null);
            //if (dr.read()) {
            //	Set Movement Type
            if (dr != null) {
                var docBaseType = Util.getValueOfString(dr["docBaseType"]);
                var isReturnTrx = dr["IsReturnTrx"] == "Y";
                if (docBaseType.equals("MMS") && !isReturnTrx)					//	Material Shipments
                {
                    mTab.setValue("MovementType", "C-");				//	Customer Shipments
                }
                else if (docBaseType.equals("MMS") && isReturnTrx)				//	Material Shipments
                {
                    mTab.setValue("MovementType", "C+");				//	Customer Returns
                }
                else if (docBaseType.equals("MMR") && !isReturnTrx)				//	Material Receipts
                {
                    mTab.setValue("MovementType", "V+");				//	Vendor Receipts
                }
                else if (docBaseType.equals("MMR") && isReturnTrx)				//	Material Receipts
                {
                    mTab.setValue("MovementType", "V-");					//	Return to Vendor
                }

                //	DocumentNo
                if (dr["IsDocNoControlled"] == "Y") {
                    mTab.setValue("DocumentNo", "<" + Util.getValueOfString(dr["CurrentNext"]) + ">");
                }
            }
            //dr.close();
        }
        catch (err) {
            this.setCalloutActive(false);
            if (dr != null) {
                dr.close();
                dr = null;
            }
            //MessageBox.Show("CalloutInOut--DocType");
            this.log.log(Level.SEVERE, sql, err);
            return err.message;
            //return e.getLocalizedMessage();
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// M_InOut - Defaults for BPartner.
    /// - Location
    /// - Contact
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="windowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns>error message or ""</returns>
    CalloutInOut.prototype.BPartner = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  

        //var sql = "";
        var idr = null;
        var dr = null;
        if (value == null || value.toString() == "") {
            return "";
        }
        try {

            var C_BPartner_ID = Util.getValueOfInt(value.toString());// (int)value;
            if (C_BPartner_ID == null || C_BPartner_ID == 0) {
                return "";
            }

            var isReturnTrx = mTab.getValue("IsReturnTrx");
            var isSOTrx = mTab.getValue("IsSOTrx");


            //	sraval: source forge bug # 1503219
            var order = mTab.getValue("C_Order_ID");

            var paramString = true.toString() + "," + C_BPartner_ID;
            //var idr = VIS.dataContext.getJSONRecord("MInOut/GetBPartnerOrderData", paramString);
            var idr = VIS.dataContext.getJSONRecord("MBPartner/GetBPartnerOrderData", paramString);
            if (idr != null) {
                //	Location
                var ii = idr["C_BPartner_Location_ID"];
                // sraval: source forge bug # 1503219 - default location for material receipt
                if (order == null) {
                    if (ii == 0) {
                        mTab.setValue("C_BPartner_Location_ID", null);
                    }
                    else {
                        mTab.setValue("C_BPartner_Location_ID", ii);
                    }
                }
                //	Contact
                ii = idr["AD_User_ID"];
                //if (dr.wasNull())
                if (ii == 0) {
                    mTab.setValue("AD_User_ID", null);
                }
                else {
                    mTab.setValue("AD_User_ID", ii);
                }

                //Inco Term
                var IncoTerm = isSOTrx ? idr["C_IncoTerm_ID"] : idr["C_IncoTermPO_ID"];
                if (IncoTerm > 0) {
                    mTab.setValue("C_IncoTerm_ID", IncoTerm);
                }

                // Skip credit check for returns
                if (isSOTrx & !isReturnTrx) {
                    //	CreditAvailable
                    var CreditStatus = idr["CreditStatusSettingOn"];
                    var CreditAvailable = idr["CreditAvailable"];
                    if (CreditStatus == "CH") {
                        if (idr["SOCreditStatus"] != null) {
                            if (!idr["SOCreditStatus"].equals("X")) {// SD
                                if (CreditAvailable <= 0) {
                                    VIS.ADialog.info("CreditLimitOver");
                                }
                            }
                        }
                    }
                    else {
                        var locId = Util.getValueOfInt(mTab.getValue("C_BPartner_Location_ID"));
                        dr = VIS.dataContext.getJSONRecord("MBPartner/GetLocationData", locId.toString());
                        if (dr != null) {
                            CreditStatus = Util.getValueOfString(dr["CreditStatusSettingOn"]);
                            if (CreditStatus == "CL") {
                                var CreditLimit = Util.getValueOfDouble(dr["SO_CreditLimit"]);
                                if (CreditLimit != 0) {
                                    var CreditAvailable = Util.getValueOfDouble(dr["CreditAvailable"]);
                                    if (CreditAvailable <= 0) {
                                        VIS.ADialog.info("CreditOver");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            //if (idr != null) {
            //    idr.close();
            //    idr = null;
            //}
            //if (drl != null) {
            //    drl.close();
            //    drl = null;
            //}
            //MessageBox.Show("CalloutInOut--BPartner");
            //this.log.log(Level.SEVERE, sql, e);
            //return e.getLocalizedMessage();
            return err;
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// M_Warehouse.
    /// Set Organization and Default Locator
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="windowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns>error message or ""</returns>
    CalloutInOut.prototype.Warehouse = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }
        if (this.isCalloutActive()) {
            return "";
        }
        var M_Warehouse_ID = Util.getValueOfInt(value);// (int)value;
        if (M_Warehouse_ID == null || M_Warehouse_ID == 0) {
            return "";
        }
        this.setCalloutActive(true);

        //var sql = "SELECT w.AD_Org_ID, l.M_Locator_ID "
        //    + "FROM M_Warehouse w"
        //    + " LEFT OUTER JOIN M_Locator l ON (l.M_Warehouse_ID=w.M_Warehouse_ID AND l.IsDefault='Y') "
        //    + "WHERE w.M_Warehouse_ID=" + M_Warehouse_ID;		//	1
        //var dr = null;
        var dr = VIS.dataContext.getJSONRecord("MInOut/GetWarehouse", value.toString());
        try {
            //dr = VIS.DB.executeReader(sql, null, null);
            //if (dr.read()) {
            if (dr != null) {
                //	Org
                var ii = Util.getValueOfInt(dr["AD_Org_ID"]);//.getInt(1));
                var AD_Org_ID = ctx.getContextAsInt(windowNo, "AD_Org_ID");
                if (AD_Org_ID != ii) {
                    mTab.setValue("AD_Org_ID", ii);
                }
                //	Locator
                ii = Util.getValueOfInt(dr["M_Locator_ID"]);// new int(dr.getInt(2));
                //if (dr.wasNull())
                if (ii == 0) {
                    ctx.setContext(windowNo, 0, "M_Locator_ID", null);
                }
                else {
                    this.log.config("M_Locator_ID=" + ii);
                    ctx.setContext(windowNo, "M_Locator_ID", ii);
                }
            }
            //dr.close();
        }
        catch (err) {
            this.setCalloutActive(false);
            if (dr != null) {
                dr.close();
                dr = null;
            }
            //MessageBox.Show("CalloutInOut--Warehouse");
            this.log.log(Level.SEVERE, sql, err);
            this.setCalloutActive(false);
            //return e.getLocalizedMessage();
            return err.message;
        }

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };


    /// <summary>
    /// OrderLine Callout
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="windowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns>error message or ""</returns>
    CalloutInOut.prototype.OrderLine = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var C_OrderLine_ID = Util.getValueOfInt(value.toString());// (int)value;
        if (C_OrderLine_ID == null || C_OrderLine_ID == 0) {
            this.setCalloutActive(false);
            return "";
        }
        try {
            //	Get Details
            var paramString = C_OrderLine_ID.toString();
            var DataPrefix = VIS.dataContext.getJSONRecord("ModulePrefix/GetModulePrefix", "VA077_");
            var dr = VIS.dataContext.getJSONRecord("MOrderLine/GetOrderLine", paramString);
            // MOrderLine ol = new MOrderLine(ctx, C_OrderLine_ID, null);

            //	Get Details
            if (Util.getValueOfInt(dr["GetID"]) != 0) {

                // when order line contains charge, it will be selected on Shipment Line on selection of Order Line
                if (Util.getValueOfInt(dr["M_Product_ID"]) > 0) {
                    mTab.setValue("M_Product_ID", Util.getValueOfInt(dr["M_Product_ID"]));
                    mTab.setValue("C_Charge_ID", null);
                }
                else {
                    mTab.setValue("C_Charge_ID", Util.getValueOfInt(dr["C_Charge_ID"]));
                    mTab.setValue("M_Product_ID", null);
                }

                mTab.setValue("PrintDescription", dr["PrintDescription"].toString());
                mTab.setValue("M_AttributeSetInstance_ID", Util.getValueOfInt(dr["M_AttributeSetInstance_ID"]));
                mTab.setValue("C_UOM_ID", Util.getValueOfInt(dr["C_UOM_ID"]));
                //var movementQty = Decimal.Subtract(ol.GetQtyOrdered(), ol.GetQtyDelivered());
                var movementQty = (Util.getValueOfDecimal(dr["QtyOrdered"]) - Util.getValueOfDecimal(dr["QtyDelivered"]));
                mTab.setValue("MovementQty", movementQty);
                var qtyEntered = movementQty;
                if ((Util.getValueOfDecimal(dr["QtyEntered"])).toString().compareTo(Util.getValueOfDecimal(dr["QtyOrdered"])) != Util.getValueOfDecimal(dr["QtyOrdered"])) {
                    //qtyEntered = qtyEntered.multiply(ol.getQtyEntered()).divide(ol.getQtyOrdered(), 12, Decimal.ROUND_HALF_UP);
                    qtyEntered = ((qtyEntered * (Util.getValueOfDecimal(dr["QtyEntered"]))) / (Util.getValueOfDecimal(dr["QtyOrdered"]).toFixed(12)));//, MidpointRounding.AwayFromZero));
                }
                mTab.setValue("QtyEntered", qtyEntered);
                //
                mTab.setValue("C_Activity_ID", Util.getValueOfInt(dr["C_Activity_ID"]));
                mTab.setValue("C_Campaign_ID", Util.getValueOfInt(dr["C_Campaign_ID"]));
                mTab.setValue("C_Project_ID", Util.getValueOfInt(dr["C_Project_ID"]));
                mTab.setValue("C_ProjectPhase_ID", Util.getValueOfInt(dr["C_ProjectPhase_ID"]));
                mTab.setValue("C_ProjectTask_ID", Util.getValueOfInt(dr["C_ProjectTask_ID"]));
                mTab.setValue("AD_OrgTrx_ID", Util.getValueOfInt(dr["AD_OrgTrx_ID"]));
                mTab.setValue("User1_ID", Util.getValueOfInt(dr["User1_ID"]));
                mTab.setValue("User2_ID", Util.getValueOfInt(dr["User2_ID"]));
                //if (dr["IsReturnTrx"]=="true")
                if (Util.getValueOfBoolean(dr["IsReturnTrx"])) {
                    mTab.setValue("Orig_OrderLine_ID", Util.getValueOfInt(dr["Orig_OrderLine_ID"]));
                    var paramString = dr["Orig_InOutLine_ID"];
                    var line = VIS.dataContext.getJSONRecord("MInOutLine/GetMInOutLine", paramString);

                    // JID_1656: locator sholud select manually
                    //mTab.setValue("M_Locator_ID", line["M_Locator_ID"]);
                }
                if (Util.getValueOfString(dr["IsDropShip"]) == "Y") {
                    mTab.setValue("IsDropShip", true);
                }
                else {
                    mTab.setValue("IsDropShip", false);
                }

                if (DataPrefix["VA077_"]) {
                    mTab.setValue("VA077_CNAutodesk", Util.getValueOfString(dr["VA077_CNAutodesk"]));
                    mTab.setValue("VA077_Duration", Util.getValueOfString(dr["VA077_Duration"]));
                    mTab.setValue("VA077_MarginAmt", Util.getValueOfDecimal(dr["VA077_MarginAmt"]));
                    mTab.setValue("VA077_MarginPercent", Util.getValueOfDecimal(dr["VA077_MarginPercent"]));
                    mTab.setValue("VA077_OldSN", Util.getValueOfString(dr["VA077_OldSN"]));
                    mTab.setValue("VA077_ProductInfo", Util.getValueOfString(dr["VA077_ProductInfo"]));
                    mTab.setValue("VA077_PurchasePrice", Util.getValueOfDecimal(dr["VA077_PurchasePrice"]));
                    mTab.setValue("VA077_RegEmail", Util.getValueOfString(dr["VA077_RegEmail"]));
                    mTab.setValue("VA077_SerialNo", Util.getValueOfString(dr["VA077_SerialNo"]));
                    mTab.setValue("VA077_UpdateFromVersn", Util.getValueOfString(dr["VA077_UpdateFromVersn"]));
                    mTab.setValue("VA077_UserRef_ID", Util.getValueOfInt(dr["VA077_UserRef_ID"]));
                    mTab.setValue("VA077_ServiceContract_ID", Util.getValueOfInt(dr["VA077_ServiceContract_ID"]));
                    if (dr["VA077_StartDate"] != "") {
                        mTab.setValue("VA077_StartDate", Dateoffset(dr["VA077_StartDate"]));
                    }
                    if (dr["VA077_EndDate"] != "") {
                        mTab.setValue("VA077_EndDate", Dateoffset(dr["VA077_EndDate"]));
                    }

                    if (Util.getValueOfBoolean(dr["VA077_LicenceTracked"]))
                        mTab.setValue("VA077_LicenceTracked", true);
                    else
                        mTab.setValue("VA077_LicenceTracked", false);

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

    /// <summary>
    /// M_InOutLine - Default UOM/Locator for Product.
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="windowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns>error message or ""</returns>
    CalloutInOut.prototype.Product = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        var M_Product_ID = Util.getValueOfInt(value);// (int)value;
        if (M_Product_ID == 0) {
            return "";
        }
        this.setCalloutActive(true);
        try {
            //	Set Attribute & Locator
            // JID_0910: On change of product on line system is not removing the ASI. if product is changed then also update the ASI field.

            var M_Locator_ID = 0;
            //if (ctx.getContextAsInt(windowNo, "M_Product_ID") == M_Product_ID
            //    && ctx.getContextAsInt(windowNo, "M_AttributeSetInstance_ID") != 0) {
            //    mTab.setValue("M_AttributeSetInstance_ID",
            //        Util.getValueOfInt(ctx.getContextAsInt(windowNo, "M_AttributeSetInstance_ID").toString()));
            //	Locator from Info Window - ASI
            //M_Locator_ID = ctx.getContextAsInt(windowNo, "M_Locator_ID");
            //if (M_Locator_ID != 0) {
            //    mTab.setValue("M_Locator_ID", Util.getValueOfInt(M_Locator_ID.toString()));
            //}
            //}
            //else {
            mTab.setValue("M_AttributeSetInstance_ID", null);
            //}

            var isSOTrx = ctx.getWindowContext(windowNo, "IsSOTrx", true) == "Y";
            // var isSOTrx = ctx.getContext("IsSOTrx");
            if (isSOTrx) {
                this.setCalloutActive(false);
                return "";
            }

            //	PO - Set UOM/Locator/Qty

            //MProduct product = MProduct.Get(ctx, M_Product_ID);
            //mTab.setValue("C_UOM_ID", Util.getValueOfInt(product.GetC_UOM_ID().toString()));
            //var paramString = M_Product_ID.toString();
            //var C_UOM_ID = VIS.dataContext.getJSONRecord("MProduct/GetC_UOM_ID", paramString);
            //mTab.setValue("C_UOM_ID", C_UOM_ID);
            //var qryBP = "SELECT C_BPartner_ID FROM M_InOut WHERE M_InOut_ID = " + Util.getValueOfInt(mTab.getValue("M_InOut_ID"));
            //var bpartner = Util.getValueOfInt(VIS.DB.executeScalar(qryBP));
            //var qryUom = "SELECT vdr.C_UOM_ID FROM M_Product p LEFT JOIN M_Product_Po vdr ON p.M_Product_ID= vdr.M_Product_ID WHERE p.M_Product_ID=" + M_Product_ID + " AND vdr.C_BPartner_ID = " + bpartner;
            //var uom = Util.getValueOfInt(VIS.DB.executeScalar(qryUom));
            //if (C_UOM_ID != 0) {
            //    if (C_UOM_ID != uom && uom != 0) {
            //        var Res = Util.getValueOfDecimal(VIS.DB.executeScalar("SELECT trunc(multiplyrate,4) FROM C_UOM_Conversion WHERE C_UOM_ID = " + C_UOM_ID + " AND C_UOM_To_ID = " + uom + " AND M_Product_ID= " + M_Product_ID + " AND IsActive='Y'"));
            //        if (Res > 0) {
            //            mTab.setValue("QtyEntered", Util.getValueOfInt(mTab.getValue("QtyEntered")) * Res);
            //            //OrdQty = MUOMConversion.ConvertProductTo(GetCtx(), _M_Product_ID, UOM, OrdQty);
            //        }
            //        else {
            //            var res = Util.getValueOfDecimal(VIS.DB.executeScalar("SELECT trunc(multiplyrate,4) FROM C_UOM_Conversion WHERE C_UOM_ID = " + C_UOM_ID + " AND C_UOM_To_ID = " + uom + " AND IsActive='Y'"));
            //            if (res > 0) {
            //                mTab.setValue("QtyEntered", Util.getValueOfInt(mTab.getValue("QtyEntered")) * Res);
            //                //OrdQty = MUOMConversion.Convert(GetCtx(), prdUOM, UOM, OrdQty);
            //            }
            //        }
            //        mTab.setValue("C_UOM_ID", uom);
            //    }

            var qtyEntered = Util.getValueOfDecimal(mTab.getValue("QtyEntered"));
            mTab.setValue("MovementQty", qtyEntered);
            var params = mTab.getValue("M_InOut_ID").toString() + "," + M_Product_ID.toString() + "," + qtyEntered.toString() + ","
                + ctx.getContextAsInt(windowNo, "C_BPartner_ID").toString();
            var dr = VIS.dataContext.getJSONRecord("MInOut/GetUOMConv", params);
            if (dr != null) {
                if (dr["C_UOM_ID"] != dr["uom"] && dr["uom"] != 0) {
                    //mTab.setValue("QtyEntered", Util.getValueOfInt(mTab.getValue("QtyEntered")) * Util.getValueOfDecimal(dr["multiplyrate"]));

                    mTab.setValue("QtyEntered", dr["qtyentered"]);
                    mTab.setValue("C_UOM_ID", dr["uom"]);
                }
                else {
                    //mTab.setValue("C_UOM_ID", C_UOM_ID);
                    mTab.setValue("C_UOM_ID", dr["C_UOM_ID"]);
                }
            }


            //190 - Get Print description from product and set print desc
            var prod = VIS.dataContext.getJSONRecord("MProduct/GetProduct", M_Product_ID.toString());
            if (prod != null)
                mTab.setValue("PrintDescription", prod.DocumentNote);

            // Commented as not in use now
            //if (window.BTR002) {
            //    if (isSOTrx) {
            //        var prdWarehouse = 0, prdLocator = 0;
            //        var M_Warehouse_ID = ctx.getContextAsInt(windowNo, "M_Warehouse_ID");
            //        var qryPrd = "SELECT loc.M_Warehouse_ID FROM M_Product p INNER JOIN M_Locator loc ON p.M_Locator_ID= loc.M_Locator_ID WHERE p.M_Product_ID=" + M_Product_ID;
            //        var idr = VIS.DB.executeReader(qryPrd);
            //        while (idr.read()) {
            //            prdWarehouse = Util.getValueOfInt(idr.get("m_warehouse_id"));
            //            prdLocator = Util.getValueOfInt(idr.get("m_locator_id"));
            //        }
            //        idr.close();
            //        if (M_Warehouse_ID == prdWarehouse) {
            //            mTab.setValue("M_Locator_ID", prdLocator);
            //        }
            //    }
            //}
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
    /// InOut Line - Quantity.
    /// - called from C_UOM_ID, qtyEntered, movementQty
    /// - enforces qty UOM relationship
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="windowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns>error message or ""</returns>
    CalloutInOut.prototype.Qty = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        try {
            var M_Product_ID = ctx.getContextAsInt(windowNo, "M_Product_ID");
            this.log.log(Level.WARNING, "qty - init - M_Product_ID=" + M_Product_ID);
            var movementQty, qtyEntered;
            var paramString = "";
            var precision = 0;

            //	No Product
            if (M_Product_ID == 0) {
                qtyEntered = Util.getValueOfDecimal(mTab.getValue("QtyEntered"));
                mTab.setValue("MovementQty", qtyEntered);
            }
            //	UOM Changed - convert from Entered -> Product
            else if (mField.getColumnName().toString().equals("C_UOM_ID")) {
                var C_UOM_To_ID = value;
                qtyEntered = Util.getValueOfDecimal(mTab.getValue("QtyEntered"));
                paramString = C_UOM_To_ID.toString();
                precision = VIS.dataContext.getJSONRecord("MUOM/GetPrecision", paramString);
                var QtyEntered1 = Util.getValueOfDecimal(qtyEntered.toFixed(precision));//, MidpointRounding.AwayFromZero);
                if (qtyEntered.compareTo(QtyEntered1) != 0) {
                    this.log.fine("Corrected qtyEntered Scale UOM=" + C_UOM_To_ID
                        + "; qtyEntered=" + qtyEntered + "->" + QtyEntered1);
                    qtyEntered = QtyEntered1;
                    mTab.setValue("QtyEntered", qtyEntered);
                }

                paramString = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(),
                    ",", qtyEntered.toString());
                movementQty = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramString);
                //movementQty = MUOMConversion.ConvertProductFrom(ctx, M_Product_ID,
                //    C_UOM_To_ID, qtyEntered.Value);
                if (movementQty == null) {
                    movementQty = qtyEntered;
                }
                var conversion = qtyEntered.compareTo(movementQty) != 0;
                this.log.fine("UOM=" + C_UOM_To_ID
                    + ", qtyEntered=" + qtyEntered
                    + " -> " + conversion
                    + " movementQty=" + movementQty);
                ctx.setContext(windowNo, "UOMConversion", conversion ? "Y" : "N");
                mTab.setValue("MovementQty", movementQty);
            }
            //	No UOM defined
            else if (ctx.getContextAsInt(windowNo, "C_UOM_ID") == 0) {
                qtyEntered = Util.getValueOfDecimal(mTab.getValue("QtyEntered"));
                mTab.setValue("MovementQty", qtyEntered);
            }
            //	qtyEntered changed - calculate movementQty
            else if (mField.getColumnName().toString().equals("QtyEntered")) {

                // If Ship/Receipt Line is created with Reference of Inoice Line.
                // Can not change the Quantity on line.
                if (Util.getValueOfInt(mTab.getValue("M_InOutLine_ID")) > 0) {
                    var invLine_ID = VIS.dataContext.getJSONRecord("MInOutLine/GetInvoiceLine", mTab.getValue("M_InOutLine_ID").toString());
                    if (invLine_ID > 0) {
                        mTab.setValue("QtyEntered", oldValue);
                        this.setCalloutActive(false);
                        return "VIS_CantChangeQty";
                    }
                }
                var C_UOM_To_ID = ctx.getContextAsInt(windowNo, "C_UOM_ID");
                qtyEntered = Util.getValueOfDecimal(value);
                paramString = M_Product_ID.toString();
                precision = VIS.dataContext.getJSONRecord("MProduct/GetUOMPrecision", paramString);

                // JID_0681: If we copy the MR lines using copy from button system is only copy the Qty only before decimal.
                var QtyEntered1 = Util.getValueOfDecimal(qtyEntered.toFixed(precision));
                if (qtyEntered.compareTo(QtyEntered1) != 0) {
                    this.log.fine("Corrected qtyEntered Scale UOM=" + C_UOM_To_ID
                        + "; qtyEntered=" + qtyEntered + "->" + QtyEntered1);
                    qtyEntered = QtyEntered1;
                    mTab.setValue("QtyEntered", qtyEntered);
                }

                paramString = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(),
                    ",", qtyEntered.toString());
                movementQty = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramString);
                // movementQty = MUOMConversion.ConvertProductFrom(ctx, M_Product_ID,
                //     C_UOM_To_ID, qtyEntered.Value);
                if (movementQty == null) {
                    movementQty = qtyEntered;
                }
                var conversion = qtyEntered.compareTo(movementQty) != 0;
                this.log.fine("UOM=" + C_UOM_To_ID
                    + ", qtyEntered=" + qtyEntered
                    + " -> " + conversion
                    + " movementQty=" + movementQty);
                ctx.setContext(windowNo, "UOMConversion", conversion ? "Y" : "N");
                mTab.setValue("MovementQty", movementQty);
            }
            //	movementQty changed - calculate qtyEntered (should not happen)
            else if (mField.getColumnName().toString().equals("MovementQty")) {
                var C_UOM_To_ID = ctx.getContextAsInt(windowNo, "C_UOM_ID");
                movementQty = Util.getValueOfDecimal(value);

                paramString = M_Product_ID.toString();
                precision = VIS.dataContext.getJSONRecord("MProduct/GetUOMPrecision", paramString);

                // JID_0681: If we copy the MR lines using copy from button system is only copy the Qty only before decimal.
                var MovementQty1 = Util.getValueOfDecimal(movementQty.toFixed(precision));
                if (movementQty.compareTo(MovementQty1) != 0) {
                    this.log.fine("Corrected movementQty "
                        + movementQty + "->" + MovementQty1);
                    movementQty = MovementQty1;
                    mTab.setValue("MovementQty", movementQty);
                }

                paramString = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(),
                    ",", movementQty.toString());
                qtyEntered = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductTo", paramString);

                //qtyEntered = MUOMConversion.ConvertProductTo(ctx, M_Product_ID,
                //    C_UOM_To_ID, movementQty);
                if (qtyEntered == null) {
                    qtyEntered = movementQty;
                }
                var conversion = movementQty.compareTo(qtyEntered) != 0;
                this.log.fine("UOM=" + C_UOM_To_ID
                    + ", movementQty=" + movementQty
                    + " -> " + conversion
                    + " qtyEntered=" + qtyEntered);
                ctx.setContext(windowNo, "UOMConversion", conversion ? "Y" : "N");
                mTab.setValue("QtyEntered", qtyEntered);
            }

            // Check for RMA
            var isReturnTrx = "Y".equals(ctx.getContext("IsReturnTrx"));
            if (M_Product_ID != 0 && isReturnTrx) {
                var oLine_ID = Util.getValueOfInt(mTab.getValue("C_OrderLine_ID"));
                paramString = oLine_ID.toString();
                var oLine = VIS.dataContext.getJSONRecord("MOrderLine/GetOrderLine", paramString);
                //  MOrderLine oLine = new MOrderLine(ctx, oLine_ID, null);
                if (oLine.Get_ID() != 0) {
                    var orig_IOLine_ID = oLine["Orig_InOutLine_ID"];
                    if (orig_IOLine_ID != 0) {
                        var paramString = orig_IOLine_ID.toString();
                        var orig_IOLine = VIS.dataContext.getJSONRecord("MInOutLine/GetMInOutLine", paramString);

                        // MInOutLine orig_IOLine = new MInOutLine(ctx, orig_IOLine_ID, null);
                        var shippedQty = orig_IOLine["MovementQty"];
                        movementQty = Util.getValueOfDecimal(mTab.getValue("MovementQty"));
                        if (shippedQty.toString().compareTo(movementQty) < 0) {
                            if (ctx.getWindowContext(windowNo, "IsSOTrx", true) == "Y") {
                                // ShowMessage.Info("QtyShippedAndReturned", true, "", "");
                                VIS.ADialog.info("QtyShippedAndReturned");
                            }
                            else {
                                // ShowMessage.Info("QtyRecievedAndReturnd", true, "", "");
                                VIS.ADialog.info("QtyRecievedAndReturnd");
                            }
                            mTab.setValue("MovementQty", shippedQty);
                            movementQty = shippedQty;

                            var C_UOM_To_ID = ctx.getContextAsInt(windowNo, "C_UOM_ID");

                            paramString = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(),
                                ",", movementQty.toString());
                            qtyEntered = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductTo", paramString);
                            //qtyEntered = MUOMConversion.ConvertProductTo(ctx, M_Product_ID,
                            //        C_UOM_To_ID, movementQty);
                            if (qtyEntered == null) {
                                qtyEntered = movementQty;
                            }
                            mTab.setValue("QtyEntered", qtyEntered);
                            mTab.setValue("MovementQty", movementQty);
                            this.log.fine("qtyEntered : " + qtyEntered.toString() +
                                "movementQty : " + movementQty.toString());
                        }
                    }
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

    /// <summary>
    /// M_InOutLine - ASI.
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="windowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns>error message or ""</returns>
    CalloutInOut.prototype.Asi = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        var M_ASI_ID = Util.getValueOfInt(value);// (int)value;
        if (M_ASI_ID == null || M_ASI_ID == 0) {
            return "";
        }
        this.setCalloutActive(true);
        try {
            //POST 1509/JID_1752 :: when we changes ASI, system changing locator refernce with small ID, which is not reqiure
            if (Util.getValueOfInt(mTab.getValue("M_Locator_ID")) == 0) {
                var M_Product_ID = ctx.getContextAsInt(windowNo, "M_Product_ID");
                var M_Warehouse_ID = ctx.getContextAsInt(windowNo, "M_Warehouse_ID");

                var param = M_Warehouse_ID.toString();
                var M_Locator_ID = VIS.dataContext.getJSONRecord("MInOut/GetDefaultLocatorID", param);
                //var M_Locator_ID = VIS.DB.executeScalar("SELECT MIN(M_Locator_ID) FROM M_Locator WHERE IsActive = 'Y' AND M_Warehouse_ID = " + M_Warehouse_ID);
                ctx.setContext(windowNo, "M_Locator_ID", M_Locator_ID);
                this.log.fine("M_Product_ID=" + M_Product_ID
                    + ", M_ASI_ID=" + M_ASI_ID
                    + " - M_Warehouse_ID=" + M_Warehouse_ID
                    + ", M_Locator_ID=" + M_Locator_ID);
                //	Check Selection
                var M_AttributeSetInstance_ID = ctx.getContextAsInt(windowNo, "M_AttributeSetInstance_ID");
                if (M_ASI_ID == M_AttributeSetInstance_ID) {
                    var selectedM_Locator_ID = ctx.getContextAsInt(windowNo, "M_Locator_ID");
                    if (selectedM_Locator_ID != 0) {
                        this.log.fine("Selected M_Locator_ID=" + selectedM_Locator_ID);
                        mTab.setValue("M_Locator_ID", selectedM_Locator_ID);
                    }
                }
            }
        }
        catch (errx) {
            this.setCalloutActive(false);
            this.log.severe(errx.toString());
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary> 190
    /// M_InOutLine - Charge.
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="windowNo"></param>
    /// <param name="mTab"></param>
    /// <param name="mField"></param>
    /// <param name="value"></param>
    /// <returns>error message or ""</returns>
    CalloutInOut.prototype.Charge = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        var C_Charge_ID = Util.getValueOfInt(mTab.getValue("C_Charge_ID"));// (int)value;
        if (C_Charge_ID == null || C_Charge_ID == 0) {
            return "";
        }
        this.setCalloutActive(true);
        try {
            var charge = VIS.dataContext.getJSONRecord("MCharge/GetChargeDetails", C_Charge_ID.toString());
            if (charge != null)
                mTab.setValue("PrintDescription", charge.PrintDescription);
        }
        catch (errx) {
            this.setCalloutActive(false);
            this.log.severe(errx.toString());
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };


    /**
     *  VAI050:Set VehicleReg.No,Gross Weight and Tare Weight on select Vehicle No
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value
     * @param {any} oldValue
     */
    CalloutInOut.prototype.setFleetDetail = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            mTab.setValue("VAS_VehicleRegistrationNo", null);
            mTab.setValue("VAS_GrossWeight", null);
            mTab.setValue("VAS_TareWeight", null);
            return "";
        }
        this.setCalloutActive(true);
        var result = VIS.dataContext.getJSONRecord("MInOut/GetFleetDetail", Util.getValueOfInt(mTab.getValue("VAS_FleetDetail_ID")));
        if (result != null) {
            mTab.setValue("VAS_VehicleRegistrationNo", result.VAS_VehicleRegistrationNo);
            mTab.setValue("VAS_GrossWeight", result.VAS_GrossWeight);
            mTab.setValue("VAS_TareWeight", result.VAS_TareWeight);
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    VIS.Model.CalloutInOut = CalloutInOut;


    // JID_0236: On Ship/Receipt Confirm line Sum of [Confirmed Qty+Difference Qty+Scrap Qty] should not be greater than Target Qty. Same is working on Move confrimation window
    function CalloutShipReceiptConfirmLine() {
        VIS.CalloutEngine.call(this, "VIS.CalloutShipReceiptConfirmLine");//must call
    };
    VIS.Utility.inheritPrototype(CalloutShipReceiptConfirmLine, VIS.CalloutEngine); //inherit prototype

    CalloutShipReceiptConfirmLine.prototype.SetQty = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        if (Util.getValueOfDecimal(mTab.getValue("TargetQty")) < (Util.getValueOfDecimal(mTab.getValue("ConfirmedQty")))) {
            mTab.setValue("ConfirmedQty", Util.getValueOfDecimal(mTab.getValue("TargetQty")));
            mTab.setValue("DifferenceQty", 0);
            mTab.setValue("ScrappedQty", 0);
            this.setCalloutActive(false);
            return "";
        }
        if (mField.getColumnName() == "ConfirmedQty") {
            if (Util.getValueOfDecimal(mTab.getValue("ConfirmedQty")) < 0) {
                mTab.setValue("ConfirmedQty", Util.getValueOfDecimal(mTab.getValue("TargetQty")));
                mTab.setValue("DifferenceQty", 0);
                mTab.setValue("ScrappedQty", 0);
                this.setCalloutActive(false);
                return "";
            }
            mTab.setValue("ScrappedQty", 0);
            mTab.setValue("DifferenceQty", Util.getValueOfDecimal(mTab.getValue("TargetQty")) - (Util.getValueOfDecimal(mTab.getValue("ConfirmedQty")) + Util.getValueOfDecimal(mTab.getValue("ScrappedQty"))));

        }
        //else if (mField.getColumnName() == "DifferenceQty") {
        //    if (Util.getValueOfDecimal(mTab.getValue("TargetQty")) < (Util.getValueOfDecimal(mTab.getValue("ConfirmedQty")) + Util.getValueOfDecimal(mTab.getValue("DifferenceQty")))) {
        //        mTab.setValue("ConfirmedQty", Util.getValueOfDecimal(mTab.getValue("TargetQty")));
        //        mTab.setValue("DifferenceQty", 0);
        //        mTab.setValue("ScrappedQty", 0);
        //        this.setCalloutActive(false);
        //        return "";
        //    }
        //    mTab.setValue("ScrappedQty", Util.getValueOfDecimal(mTab.getValue("TargetQty")) - (Util.getValueOfDecimal(mTab.getValue("ConfirmedQty")) + Util.getValueOfDecimal(mTab.getValue("DifferenceQty"))));

        //}
        else if (mField.getColumnName() == "ScrappedQty") {
            if (Util.getValueOfDecimal(mTab.getValue("ScrappedQty")) < 0) {
                mTab.setValue("ConfirmedQty", Util.getValueOfDecimal(mTab.getValue("TargetQty")));
                mTab.setValue("DifferenceQty", 0);
                mTab.setValue("ScrappedQty", 0);
                this.setCalloutActive(false);
                return "";
            }
            if (Util.getValueOfDecimal(mTab.getValue("TargetQty")) < (Util.getValueOfDecimal(mTab.getValue("ConfirmedQty")) + Util.getValueOfDecimal(mTab.getValue("ScrappedQty")))) {
                mTab.setValue("ConfirmedQty", Util.getValueOfDecimal(mTab.getValue("TargetQty")));
                mTab.setValue("DifferenceQty", 0);
                mTab.setValue("ScrappedQty", 0);
                this.setCalloutActive(false);
                return "";
            }
            mTab.setValue("DifferenceQty", Util.getValueOfDecimal(mTab.getValue("TargetQty")) - (Util.getValueOfDecimal(mTab.getValue("ConfirmedQty")) + Util.getValueOfDecimal(mTab.getValue("ScrappedQty"))));
        }

        this.setCalloutActive(false);
        return "";
    };
    VIS.Model.CalloutShipReceiptConfirmLine = CalloutShipReceiptConfirmLine;

})(VIS, jQuery);