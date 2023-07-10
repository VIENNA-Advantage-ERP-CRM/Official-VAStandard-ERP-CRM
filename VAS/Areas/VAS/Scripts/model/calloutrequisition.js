; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;
    var steps = false;

    function CalloutRequisition() {
        VIS.CalloutEngine.call(this, "VIS.CalloutRequisition"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutRequisition, VIS.CalloutEngine);//inherit CalloutEngine

    /** Logger					*/


    /**
     *	Requisition Line - Product.
     *		- PriceStd
     *  @param ctx context
     *  @param WindowNo current Window No
     *  @param mTab Grid Tab
     *  @param mField Grid Field
     *  @param value New Value
     *  @return null or error message
     */
    CalloutRequisition.prototype.Product = function (ctx, windowNo, mTab, mField, value, oldValue) {

        try {
            if (this.isCalloutActive() || value == null || value.toString() == "") {
                return "";
            }
            var M_Product_ID = value;
            if (M_Product_ID == null || M_Product_ID == 0)
                return "";

            this.setCalloutActive(true);

            //
            /**	Set Attribute
            if (ctx.getContextAsInt( Env.WINDOW_INFO, Env.TAB_INFO, "M_Product_ID") == M_Product_ID.intValue()
                && ctx.getContextAsInt( Env.WINDOW_INFO, Env.TAB_INFO, "M_AttributeSetInstance_ID") != 0)
                mTab.setValue("M_AttributeSetInstance_ID", Integer.valueOf(ctx.getContextAsInt( Env.WINDOW_INFO, Env.TAB_INFO, "M_AttributeSetInstance_ID")));
            else
                mTab.setValue("M_AttributeSetInstance_ID", null);
            **/

            // JID_0910: On change of product on line system is not removing the ASI. if product is changed then also update the ASI field.
            mTab.setValue("M_AttributeSetInstance_ID", null);

            var C_BPartner_ID = ctx.getContextAsInt(windowNo, "C_BPartner_ID");
            var qty = mTab.getValue("Qty");
            var isSOTrx = false;
            var M_PriceList_ID = ctx.getContextAsInt(windowNo, "M_PriceList_ID");
            var M_PriceList_Version_ID = ctx.getContextAsInt(windowNo, "M_PriceList_Version_ID");
            var orderDate = ctx.getContext(windowNo, "DateDoc");

            var paramString;
            var C_UOM_ID = VIS.dataContext.getJSONRecord("MProduct/GetC_UOM_ID", M_Product_ID);

            //** Price List - ValidFrom date validation ** Dt:03/26/2021 ** Modified By: Kumar **//
            //var paramsPrice = Util.getValueOfString(M_PriceList_ID).concat(",", Util.getValueOfString(mTab.getValue("M_Requisition_ID")), ",",
            //    Util.getValueOfString(M_Product_ID), ",",
            //    Util.getValueOfString(C_UOM_ID), ",",
            //    Util.getValueOfString(mTab.getValue("M_AttributeSetInstance_ID")), ",",
            //    "3");

            //Get PriceListversion based on Pricelist
            //var M_PriceList_Version_ID = VIS.dataContext.getJSONRecord("MPriceListVersion/GetM_PriceList_Version_ID", paramsPrice);

            paramString = M_Product_ID.toString().concat(",", C_BPartner_ID, ",", //2
                qty, ",", //3
                isSOTrx, ",", //4 
                M_PriceList_ID, ",", //5
                M_PriceList_Version_ID, ",", //6
                orderDate, ",", null, ",", C_UOM_ID, ",", 1); //7

            //Get product price information
            var dr = null;
            dr = VIS.dataContext.getJSONRecord("MProductPricing/GetProductPricing", paramString);
            mTab.setValue("PriceActual", dr["PriceActual"]);
            mTab.setValue("PrintDescription", Util.getValueOfString(dr["DocumentNote"]));
            //		
            //mTab.setValue("PriceActual", pp.GetPriceStd());
            ctx.setContext(windowNo, "EnforcePriceLimit", dr["EnforcePriceLimit"] ? "Y" : "N");	//	not used
            ctx.setContext(windowNo, "DiscountSchema", dr["DiscountSchema"] ? "Y" : "N");

            // Set Product UOM 
            mTab.setValue("C_UOM_ID", C_UOM_ID);
            var param = mTab.getValue("M_Requisition_ID").toString() + "," + ctx.getAD_Client_ID().toString() + "," + value.toString();
            var OrderLine = VIS.dataContext.getJSONRecord("MOrderLine/GetReqOrderLine", param);

            //var sql = "SELECT C_OrderLine_ID FROM C_OrderLine"
            //    + " WHERE C_Order_ID ="
            //    + " (SELECT C_Order_ID "
            //    + " FROM C_Order "
            //    + " WHERE DocumentNo="
            //    + " (SELECT DocumentNo FROM M_Requisition WHERE M_Requisition.M_Requisition_id = " + mTab.getValue("M_Requisition_id") + ")"
            //    + " AND AD_Client_ID =" + ctx.getAD_Client_ID() + ")"
            //    + " AND M_Product_ID=" + value;

            //var OrderLine = Util.getValueOfInt(VIS.DB.executeScalar(sql));

            if (OrderLine > 0) {
                mTab.setValue("C_OrderLine_ID", OrderLine);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            return err.message;
            //MessageBox.Show("CalloutRequisition- Product");
        }

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// Requisition Line - Charge.
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current Window No</param>
    /// <param name="mTab">Grid Tab</param>
    /// <param name="mField">Grid Field</param>
    /// <param name="value"> New Value</param>
    /// <returns>null or error message</returns>

    CalloutRequisition.prototype.Charge = function (ctx, windowNo, mTab, mField, value, oldValue) {

        try {
            if (this.isCalloutActive() || value == null || value.toString() == "") {
                return "";
            }

            this.setCalloutActive(true);
            var C_Charge_ID = Util.getValueOfInt(value);
            if (C_Charge_ID == null || C_Charge_ID == 0) {
                this.setCalloutActive(false);
                return "";
            }

            //	No Product defined
            if (mTab.getValue("M_Product_ID") != null) {
                mTab.setValue("M_Product_ID", null);
            }
            mTab.setValue("M_AttributeSetInstance_ID", null);

            //set default UOM for charge.
            var c_uom_id = ctx.getContextAsInt("#C_UOM_ID");
            if (c_uom_id > 0) {
                mTab.setValue("C_UOM_ID", c_uom_id);	//	Default charge from context
            }
            else {
                mTab.setValue("C_UOM_ID", 100);	//	EA
            }

            //var chargeAmt = VIS.dataContext.getJSONRecord("MCharge/GetCharge", C_Charge_ID.toString());
            //mTab.setValue("PriceActual", Util.getValueOfDecimal(chargeAmt));

            //190 - Set PriceActual and Print Description
            var dr = VIS.dataContext.getJSONRecord("MCharge/GetChargeDetails", C_Charge_ID.toString());
            if (dr != null) {
                mTab.setValue("PriceActual", Util.getValueOfDecimal(dr["ChargeAmt"]));
                mTab.setValue("PrintDescription", Util.getValueOfString(dr["PrintDescription"]));
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            return err.message;
        }
        this.setCalloutActive(false);
        oldValue = null;
        return "";
    };

    /**
     *	Order Line - Amount.
     *		- called from Qty, PriceActual
     *		- calculates LineNetAmt
     *  @param ctx context
     *  @param WindowNo current Window No
     *  @param mTab Grid Tab
     *  @param mField Grid Field
     *  @param value New Value
     *  @return null or error message
     */
    CalloutRequisition.prototype.Amt = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            return "";
        }
        if (this.isCalloutActive() || value == null)
            return "";
        try {
            this.setCalloutActive(true);

            var StdPrecision = ctx.getStdPrecision();
            var PriceListPrecision = StdPrecision;
            var M_PriceList_ID = ctx.getContextAsInt(windowNo, "M_PriceList_ID");
            var dr;
            if (M_PriceList_ID != 0) {
                dr = VIS.dataContext.getJSONRecord("MPriceList/GetPriceList", M_PriceList_ID.toString());
                if (dr != null) {
                    StdPrecision = Util.getValueOfInt(dr["StdPrecision"]);
                    PriceListPrecision = Util.getValueOfInt(dr["PriceListPrecision"]);
                }
            }

            //	Qty changed - recalc price
            if (mField.getColumnName() == "Qty"
                && "Y" == ctx.getContext("DiscountSchema")) {
                var M_Product_ID = ctx.getContextAsInt(windowNo, "M_Product_ID");
                var C_BPartner_ID = ctx.getContextAsInt(windowNo, "C_BPartner_ID");
                var qty = value;
                var isSOTrx = false;
                //pp.SetM_PriceList_ID(M_PriceList_ID);
                var M_PriceList_Version_ID = ctx.getContextAsInt(windowNo, "M_PriceList_Version_ID");
                //pp.SetM_PriceList_Version_ID(M_PriceList_Version_ID);
                ///DateTime orderDate = (DateTime)mTab.getValue("DateInvoiced");
                var orderDate = mTab.getValue("DateInvoiced");
                // pp.SetPriceDate(orderDate);
                //

                //*******************
                var paramString = M_Product_ID.concat(",", C_BPartner_ID, ",", //2
                    qty, ",", //3
                    isSOTrx, ",", //4 
                    M_PriceList_ID, ",", //5
                    M_PriceList_Version_ID, ",", //6
                    orderDate, ",", null); //7


                //Get product price information
                var dr = null;
                dr = VIS.dataContext.getJSONRecord("MProductPricing/GetProductPricing", paramString);

                mTab.setValue("PriceActual", Util.getValueOfDecimal(dr["PriceActual"].toFixed(PriceListPrecision)));
            }

            var Qty = mTab.getValue("QtyEntered");
            //Decimal PriceActual = (Decimal)mTab.getValue("PriceActual");
            var PriceActual = mTab.getValue("PriceActual");

            //	get values
            this.log.fine("amt - Qty=" + Qty + ", Price=" + PriceActual + ", Precision=" + PriceListPrecision);

            //	Multiply
            var LineNetAmt = Qty * PriceActual;
            if (Util.scale(LineNetAmt) > StdPrecision)
                LineNetAmt = LineNetAmt.toFixed(StdPrecision);//, MidpointRounding.AwayFromZero);
            mTab.setValue("LineNetAmt", LineNetAmt);
            // JID_1744 the precision should be as per currency percision
            mTab.setValue("PriceActual", Util.getValueOfDecimal(PriceActual.toFixed(PriceListPrecision)));
            this.log.info("amt - LineNetAmt=" + LineNetAmt);
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.saveError("CalloutRequisation", err);
        }

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };


    /// <summary>
    /// Requisition Line - Quantity.
    /// - called from C_UOM_ID, QtyEntered
    /// - enforces qty UOM relationship
    /// </summary> JID_0996 - On entering the Entered qty callout will update the qty field in base UOM.
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current Window No</param>
    /// <param name="mTab">Grid Tab</param>
    /// <param name="mField">Grid Field</param>
    /// <param name="value">New Value</param>
    /// <returns>null or error message</returns>
    CalloutRequisition.prototype.Qty = function (ctx, windowNo, mTab, mField, value, oldValue) {

        var paramStr = "";
        if (this.isCalloutActive() || value == null || value.toString() == "")
            return "";
        console.log("Before Charge Or Product");
        if (Util.getValueOfInt(mTab.getValue("M_Product_ID")) == 0 && Util.getValueOfInt(mTab.getValue("C_Charge_ID")) == 0) {
            return "";
        }
        console.log("After Charge Or Product");
        this.setCalloutActive(true);
        try {
            var M_Product_ID = ctx.getContextAsInt(windowNo, "M_Product_ID");
            if (steps) {
                this.log.Warning("init - M_Product_ID=" + M_Product_ID + " - ");
            }
            var QtyEntered = VIS.Env.ZERO;
            var QtyRequired = VIS.Env.ZERO;
            var PriceActual, PriceEntered;

            var M_PriceList_ID = ctx.getContextAsInt(windowNo, "M_PriceList_ID");

            //	No Product
            if (M_Product_ID == 0) {
                QtyEntered = Util.getValueOfDecimal(mTab.getValue("QtyEntered"));
                QtyRequired = QtyEntered;
                mTab.setValue("Qty", QtyRequired);
            }
            //	UOM Changed - convert from Entered -> Product
            /** Price List - Ensuring valid Uom id ** Dt:01/02/2021 ** Modified By: Kumar **/
            else if (mField.getColumnName() == "C_UOM_ID" || mField.getColumnName() == "M_AttributeSetInstance_ID") {
                var C_UOM_To_ID = Util.getValueOfInt(mTab.getValue("C_UOM_ID"));
                QtyEntered = Util.getValueOfDecimal(mTab.getValue("QtyEntered"));
                var C_BPartner_ID = ctx.getContextAsInt(windowNo, "C_BPartner_ID");
                var M_AttributeSetInstance_ID;
                /** Price List - Ensuring valid Uom id ** Dt:01/02/2021 ** Modified By: Kumar **/
                if (mField.getColumnName() == "M_AttributeSetInstance_ID") {
                    M_AttributeSetInstance_ID = Util.getValueOfInt(mTab.getValue("M_AttributeSetInstance_ID"));
                }
                else {
                    M_AttributeSetInstance_ID = ctx.getContextAsInt(windowNo, "M_AttributeSetInstance_ID");
                }

                var isSOTrx = false;
                var orderDate = ctx.getContext(windowNo, "DateDoc");
                var gp = 2;

                var params = M_Product_ID.toString().concat(",", (mTab.getValue("M_Requisition_ID")).toString() +
                    "," + Util.getValueOfString(mTab.getValue("M_AttributeSetInstance_ID")) +
                    "," + Util.getValueOfString(mTab.getValue("C_UOM_ID")) + "," + ctx.getAD_Client_ID().toString() +
                    "," + Util.getValueOfString(C_BPartner_ID) + "," + QtyEntered.toString() + "," + M_PriceList_ID.toString());
                var productPrices = VIS.dataContext.getJSONRecord("MProductPricing/GetPricesOnChange", params);

                if (productPrices != null) {
                    mTab.setValue("PriceActual", Util.getValueOfDecimal(productPrices["PriceEntered"]));
                    gp = Util.getValueOfInt(productPrices["UOMPrecision"]);
                    QtyRequired = productPrices["QtyOrdered"];
                }

                //paramStr = M_Product_ID.toString().concat(",", C_BPartner_ID, ",", //2
                //    QtyEntered, ",", //3
                //    isSOTrx, ",", //4 
                //    M_PriceList_ID, ",", //5
                //    M_PriceList_Version_ID, ",", //6
                //    orderDate, ",", null, ",", M_AttributeSetInstance_ID.toString(), ",",  //7
                //    C_UOM_To_ID, ",", 1);
                ////Get product price information
                //dr = null;
                //dr = VIS.dataContext.getJSONRecord("MProductPricing/GetProductPricing", paramStr);
                //if (dr != null) {
                //    PriceActual = dr["PriceActual"];
                //    /** Price List - Ensuring valid Uom id ** Dt:01/02/2021 ** Modified By: Kumar **/
                //    if (mField.getColumnName() == "M_AttributeSetInstance_ID") {
                //        mTab.setValue("PriceActual", Util.getValueOfInt(PriceActual));
                //    }
                //}
                //Get precision from server side
                //paramStr = C_UOM_To_ID.toString().concat(",");
                //var gp = VIS.dataContext.getJSONRecord("MUOM/GetPrecision", paramStr);

                var QtyEntered1 = QtyEntered.toFixed(Util.getValueOfInt(gp));
                if (QtyEntered != QtyEntered1) {
                    this.log.fine("Corrected QtyEntered Scale UOM=" + C_UOM_To_ID
                        + "; QtyEntered=" + QtyEntered + "->" + QtyEntered1);
                    QtyEntered = QtyEntered1;
                    mTab.setValue("QtyEntered", QtyEntered);
                }

                //Conversion of Qty Entered
                //paramStr = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(), ","
                //    , QtyEntered.toString());
                //var pc = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramStr);
                //QtyRequired = pc;

                if (QtyRequired == null) {
                    QtyRequired = QtyEntered;
                }

                mTab.setValue("Qty", QtyRequired);

                //var conversion = QtyEntered != QtyRequired;

                //if (PriceActual == 0) {
                //    //Conversion of Price Entered
                //    PriceActual = Util.getValueOfDecimal(mTab.getValue("PriceActual"));
                //    paramStr = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(), ",", //2
                //        PriceActual.toString()); //3
                //    pc = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramStr);

                //    PriceEntered = pc;
                //    if (PriceEntered == null)
                //        PriceEntered = PriceActual;
                //    this.log.fine("UOM=" + C_UOM_To_ID
                //        + ", QtyEntered/PriceActual=" + QtyEntered + "/" + PriceActual
                //        + " -> " + conversion
                //        + " QtyRequired/PriceEntered=" + QtyRequired + "/" + PriceEntered);
                //    ctx.setContext(windowNo, "UOMConversion", conversion ? "Y" : "N");

                //    mTab.setValue("PriceActual", PriceEntered);
                //}
                //else {
                //    mTab.setValue("PriceActual", PriceActual);
                //}                
            }
            //	QtyEntered changed - calculate QtyRequired
            else if (mField.getColumnName() == "QtyEntered") {
                var C_UOM_To_ID = ctx.getContextAsInt(windowNo, "C_UOM_ID");
                QtyEntered = Util.getValueOfDecimal(value);
                //Get precision from server
                paramStr = C_UOM_To_ID.toString().concat(","); //1
                var gp = VIS.dataContext.getJSONRecord("MUOM/GetPrecision", paramStr);
                var QtyEntered1 = QtyEntered.toFixed(Util.getValueOfInt(gp));//, MidpointRounding.AwayFromZero);

                if (QtyEntered != QtyEntered1) {
                    this.log.fine("Corrected QtyEntered Scale UOM=" + C_UOM_To_ID
                        + "; QtyEntered=" + QtyEntered + "->" + QtyEntered1);
                    QtyEntered = QtyEntered1;
                    mTab.setValue("QtyEntered", QtyEntered);
                }

                paramStr = M_Product_ID.toString().concat(",", C_UOM_To_ID.toString(), ",", //2
                    QtyEntered.toString()); //3
                var pc = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramStr);

                QtyRequired = pc;
                if (QtyRequired == null)
                    QtyRequired = QtyEntered;

                var conversion = QtyEntered != QtyRequired;

                this.log.fine("UOM=" + C_UOM_To_ID
                    + ", QtyEntered=" + QtyEntered
                    + " -> " + conversion
                    + " Qty=" + QtyRequired);
                ctx.setContext(windowNo, "UOMConversion", conversion ? "Y" : "N");

                mTab.setValue("Qty", QtyRequired);
            }

            PriceActual = Util.getValueOfDecimal(mTab.getValue("PriceActual"));
            var StdPrecision = ctx.getStdPrecision();
            if (M_PriceList_ID > 0) {
                var dr = VIS.dataContext.getJSONRecord("MPriceList/GetPriceList", M_PriceList_ID.toString());
                if (dr != null) {
                    StdPrecision = Util.getValueOfInt(dr["StdPrecision"]);
                }
                dr = null;
            }
            var LineNetAmt = QtyEntered * PriceActual;
            if (Util.scale(LineNetAmt) > StdPrecision)
                LineNetAmt = LineNetAmt.toFixed(StdPrecision);//, MidpointRounding.AwayFromZero);
            mTab.setValue("LineNetAmt", LineNetAmt);
            this.log.info("amt - LineNetAmt=" + LineNetAmt);
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
*  //VIS0336:for setting the Projectline details on req line tab
* @param {any} ctx
* @param {any} windowNo
* @param {any} mTab
* @param {any} mField
* @param {any} value
* @param {any} oldValue
*/
    CalloutRequisition.prototype.ProjectLine = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null) {
            return "";
        }
        if (mTab.getValue("C_ProjectLine_ID") != null) {
            var paramString = mTab.getValue("C_ProjectLine_ID");
            var data = VIS.dataContext.getJSONRecord("MProject/GetReqLinesProjectDetail", paramString);
            if (data != null) {
                mTab.setValue("M_Product_ID", data["ProductID"]);
                mTab.setValue("M_AttributeSetInstance_ID", data["AtrrInstance"]);
                mTab.setValue("C_Charge_ID", data["ChargeID"]);
                mTab.setValue("C_UOM_ID", data["UOM"]);
                mTab.setValue("PriceActual", data["PriceActual"]);
                mTab.setValue("QtyEntered", data["QtyEntered"]);

            }
        }
        this.setCalloutActive(false);
        return "";
    };
    VIS.Model.CalloutRequisition = CalloutRequisition;

})(VIS, jQuery);