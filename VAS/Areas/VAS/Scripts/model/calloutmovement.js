; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    /// <summary>
    /// Inventory Movement Callouts
    /// </summary>
    function CalloutMovement() {
        VIS.CalloutEngine.call(this, "VIS.CalloutMovement"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutMovement, VIS.CalloutEngine);//inherit CalloutEngine

    /// <summary>
    /// Product modified
    /// Set Attribute Set Instance
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current window no</param>
    /// <param name="tab">model tab</param>
    /// <param name="field">model field</param>
    /// <param name="value">new value</param>
    /// <returns>Error message or ""</returns>
    CalloutMovement.prototype.Product = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        if (value == null || value.toString() == "") {
            //if (Util.getValueOfInt(VIS.DB.executeScalar("SELECT COUNT(*) FROM AD_column WHERE ad_table_id = " +
            //    " (SELECT ad_table_id   FROM ad_table   WHERE upper(tablename) = upper('M_MovementLine'))" +
            //    " and upper(columnname) = 'C_UOM_ID'", null, null)) > 0) {
            mTab.setValue("MovementQty", 1);
            mTab.setValue("QtyEntered", 1);
            //}
            return "";
        }

        if (this.isCalloutActive()) {
            return "";
        }

        this.setCalloutActive(true);
        try {
            var M_Product_ID = value;
            if (M_Product_ID == null || M_Product_ID == 0)
                return "";
            //	Set Attribute
            // JID_0910: On change of product on line system is not removing the ASI. if product is changed then also update the ASI field.

            //if (ctx.getContextAsInt(windowNo, "M_Product_ID") == M_Product_ID
            //    && ctx.getContextAsInt(windowNo, "M_AttributeSetInstance_ID") != 0)
            //    mTab.setValue("M_AttributeSetInstance_ID", ctx.getContextAsInt(windowNo, "M_AttributeSetInstance_ID"));
            //else
            mTab.setValue("M_AttributeSetInstance_ID", null);

            // change by Shivani 
            //var sql = "SELECT COUNT(*) FROM AD_column WHERE ad_table_id = " +
            //    " (SELECT ad_table_id   FROM ad_table   WHERE upper(tablename) = upper('M_MovementLine'))" +
            //    " and upper(columnname) = 'C_UOM_ID'";
            //if (Util.getValueOfInt(VIS.DB.executeScalar(sql, null, null)) > 0) {
            //sql = "select c_uom_id from m_product where m_product_id=" + M_Product_ID;
            //var c_uom_id = Util.getValueOfInt(VIS.DB.executeScalar(sql, null, null));

            var c_uom_id = VIS.dataContext.getJSONRecord("MProduct/GetC_UOM_ID", M_Product_ID.toString());
            mTab.setValue("C_UOM_ID", c_uom_id);
            if (value != oldValue) {

                mTab.setValue("MovementQty", 1);
                mTab.setValue("QtyEntered", 1);
            }

            // var c_uom_id = Util.getValueOfInt(value);

            c_uom_id = Util.getValueOfDecimal(mTab.getValue("C_UOM_ID"));
            var QtyEntered = Util.getValueOfDecimal(mTab.getValue("QtyEntered"));
            M_Product_ID = Util.getValueOfInt(mTab.getValue("M_Product_ID"));

            var paramStr = M_Product_ID.toString().concat(',').concat(c_uom_id.toString()).concat(',').concat(QtyEntered.toString());
            var pc = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramStr);
            QtyOrdered = pc;

            var conversion = false
            if (QtyOrdered != null) {
                conversion = QtyEntered != QtyOrdered;
            }
            if (QtyOrdered == null) {
                conversion = false;
                QtyOrdered = 1;
            }
            if (conversion) {
                mTab.setValue("MovementQty", QtyOrdered);
            }
            else {
                mTab.setValue("MovementQty", (QtyOrdered * QtyEntered));
            }
            // }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.log(Level.severe, sql, err);
            return err.message;
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";

    };

    VIS.Model.CalloutMovement = CalloutMovement;


    function CalloutInventoryMove() {
        VIS.CalloutEngine.call(this, "VIS.CalloutInventoryMove");
    };
    //#endregion
    VIS.Utility.inheritPrototype(CalloutInventoryMove, VIS.CalloutEngine); //inherit calloutengine

    CalloutInventoryMove.prototype.UOM = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == 0 || value == null || value.toString() == "") {
          //VAI051:- When Qty is zero then Movement Qty should be 0;
            if (value == 0) {
                mTab.setValue("MovementQty", 0)
            }
            return "";

        }
        if (this.isCalloutActive() || value == null)
            return "";
        this.setCalloutActive(true);
        try {
            var M_Product_ID = Util.getValueOfDecimal(mTab.getValue("M_Product_ID"));
            if (M_Product_ID == 0) {
                QtyEntered = 1;
                QtyOrdered = QtyEntered;
                mTab.setValue("QtyOrdered", QtyOrdered);
                mTab.setValue("QtyEntered", QtyEntered);

                this.setCalloutActive(false);
                ctx = windowNo = mTab = mField = value = oldValue = null;
                return "";
            }

            if (mField.getColumnName() == "C_UOM_ID") {
                var C_UOM_To_ID = Util.getValueOfInt(value);
                QtyEntered = Util.getValueOfDecimal(mTab.getValue("QtyEntered"));
                M_Product_ID = Util.getValueOfInt(mTab.getValue("M_Product_ID"));
                paramStr = M_Product_ID.toString().concat(',').concat(C_UOM_To_ID.toString()).concat(',').concat(QtyEntered.toString());
                var pc = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramStr);
                QtyOrdered = pc;

                var conversion = false
                if (QtyOrdered != null) {
                    conversion = QtyEntered != QtyOrdered;
                }
                if (QtyOrdered == null) {
                    conversion = false;
                    QtyOrdered = 1;
                }
                if (conversion) {
                    mTab.setValue("MovementQty", QtyOrdered);
                }
                else {

                    mTab.setValue("MovementQty", (QtyOrdered * QtyEntered));
                }


            }

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

                QtyOrdered = pc;//(Decimal?)MUOMConversion.ConvertProductFrom(ctx, M_Product_ID,

                if (QtyOrdered == null)
                    QtyOrdered = QtyEntered;


                var conversion = QtyEntered != QtyOrdered;



                this.log.fine("UOM=" + C_UOM_To_ID
                    + ", QtyEntered=" + QtyEntered
                    + " -> " + conversion
                    + " QtyOrdered=" + QtyOrdered);
                ctx.setContext(windowNo, "UOMConversion", conversion ? "Y" : "N");

                mTab.setValue("MovementQty", QtyOrdered);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    }
    VIS.Model.CalloutInventoryMove = CalloutInventoryMove;

    function CalloutshipLine() {
        VIS.CalloutEngine.call(this, "VIS.CalloutshipLine");
    };
    VIS.Utility.inheritPrototype(CalloutshipLine, VIS.CalloutEngine); //inherit calloutengine


    CalloutshipLine.prototype.DocType = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        if (this.isCalloutActive() || value == null)
            return "";
        this.setCalloutActive(true);
        try {
            //sql = "SELECT M_Product_ID , movementqty , M_AttributeSetInstance_ID FROM M_InOutLine WHERE M_InOutLine_ID=" + Util.getValueOfInt(value);
            // var M_Product_ID = Util.getValueOfInt(VIS.DB.executeScalar(sql, null, null));

            //var ds = VIS.DB.executeDataSet(sql);
            var paramString = value.toString() + "," + mTab.getValue("M_Package_ID").toString() + "," + mTab.getValue("M_InOutLine_ID").toString();
            var idr = VIS.dataContext.getJSONRecord("MInOut/GetProductDetails", paramString);
            var movementqty = 0;
            if (idr != null && Object.keys(idr).length > 0) {
                movementqty = Util.getValueOfDecimal(idr.Movementqty);
                //sql = "SELECT  SUM(ConfirmedQty + scrappedqty) FROM M_PackageLine WHERE  M_Package_ID=" + mTab.getValue("M_Package_ID") + " and m_inoutline_id =  " + mTab.getValue("M_InOutLine_ID");
                //var totalConfirmedAndScrapped = Util.getValueOfDecimal(VIS.DB.executeScalar(sql))
                mTab.setValue("M_Product_ID", Util.getValueOfInt(idr.M_Product_ID));
                mTab.setValue("Qty", movementqty - Util.getValueOfInt(idr.totalConfirmedAndScrapped));
                mTab.setValue("M_AttributeSetInstance_ID", Util.getValueOfInt(idr.M_AttributeSetInstance_ID));
                mTab.setValue("DTD001_IsConfirm", true);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString()); // SD
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    }
    VIS.Model.CalloutshipLine = CalloutshipLine;
    //***Callout CalloutshipLine End


    //*************CalloutMoveConfirmLineSetQty**********//
    function CalloutMoveConfirmLine() {
        VIS.CalloutEngine.call(this, "VIS.CalloutMoveConfirmLine");//must call
    };
    VIS.Utility.inheritPrototype(CalloutMoveConfirmLine, VIS.CalloutEngine); //inherit prototype

    CalloutMoveConfirmLine.prototype.SetQty = function (ctx, windowNo, mTab, mField, value, oldValue) {

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
        else if (mField.getColumnName() == "DifferenceQty") {
            if (Util.getValueOfDecimal(mTab.getValue("TargetQty")) < (Util.getValueOfDecimal(mTab.getValue("ConfirmedQty")) + Util.getValueOfDecimal(mTab.getValue("DifferenceQty")))) {
                mTab.setValue("ConfirmedQty", Util.getValueOfDecimal(mTab.getValue("TargetQty")));
                mTab.setValue("DifferenceQty", 0);
                mTab.setValue("ScrappedQty", 0);
                this.setCalloutActive(false);
                return "";
            }
            mTab.setValue("ScrappedQty", Util.getValueOfDecimal(mTab.getValue("TargetQty")) - (Util.getValueOfDecimal(mTab.getValue("ConfirmedQty")) + Util.getValueOfDecimal(mTab.getValue("DifferenceQty"))));

        }
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
    VIS.Model.CalloutMoveConfirmLine = CalloutMoveConfirmLine;
    //*****************MoveConfirmLineSetQty********************//

})(VIS, jQuery);