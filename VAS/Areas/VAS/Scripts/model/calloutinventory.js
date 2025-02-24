; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    function CalloutInventory() {
        VIS.CalloutEngine.call(this, "VIS.CalloutInventory"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutInventory, VIS.CalloutEngine); //inherit calloutengine
    /// <summary>
    /// Product/Locator/asi modified.
    /// Set Attribute Set Instance
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current window no</param>
    /// <param name="tab">model tab</param>
    /// <param name="field">model field</param>
    /// <param name="value">new value</param>
    /// <returns>error message or ""</returns>
    CalloutInventory.prototype.Product = function (ctx, windowNo, mTab, mField, value, oldValue) {
        // when method call from poduct container and set value of container as null
        if (!this.isCalloutActive() && mField.getColumnName() == "M_ProductContainer_ID" && (value == null || value.toString() == "")) {
            value = 0;
        }
        //JID_1181: On change of product on physical inventory line nad internal use system is giving error [TypeError: Cannot read property 'toString' of null]
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }

        //	New Line - Get Book Value
        var M_Product_ID = 0;
        var product = Util.getValueOfInt(mTab.getValue("M_Product_ID"));
        if (product != null)
            M_Product_ID = product;
        if (M_Product_ID == 0)
            return "";
        var M_Locator_ID = 0;
        var locator = Util.getValueOfInt(mTab.getValue("M_Locator_ID"));
        if (locator != null)
            M_Locator_ID = locator;
        if (M_Locator_ID == 0)
            return "";

        this.setCalloutActive(true);

        //	overkill - see new implementation
        var Attribute_ID = 0;
        var AttrCode = ctx.getContext(windowNo, "AttrCode");
        // not call this function when calling happen from ProductContainer reference
        if (AttrCode != null && AttrCode != "" && mField.getColumnName() != "M_ProductContainer_ID") {            
            var paramString = value.toString() + ',' + AttrCode.toString();
            Attribute_ID = VIS.dataContext.getJSONRecord("MInventoryLine/GetProductAttribute", paramString);

        }
        var inventoryLine = Util.getValueOfInt(mTab.getValue("M_InventoryLine_ID"));        
        var MovementDate = ctx.getContext(windowNo, "MovementDate");
        var AD_Org_ID = ctx.getContextAsInt(windowNo, "AD_Org_ID");

        // JID_0855 :On change of locator and Attribute set instance sytem is removing the UOM of product.-
        // Mohit - 21 May 2019.
        if (mField.getColumnName() == "M_Product_ID") {
            var paramString = "" + "," + value.toString();
            //VAI050-If Internal Use True than set UOM - give priority to Consumable UOM else set Base UOM
            var InternalUse = mTab.getValue("IsInternalUse");
            if (InternalUse) {
                var result = VIS.dataContext.getJSONRecord("MProduct/GetProductUOMs", paramString);
                if (result != null) {
                    if (result["VAS_ConsumableUOM_ID"] > 0)
                        mTab.setValue("C_UOM_ID", Util.getValueOfInt(result["VAS_ConsumableUOM_ID"]));
                    else
                        mTab.setValue("C_UOM_ID", Util.getValueOfInt(result["C_UOM_ID"]));
                }
            }
            else {
                var Uom = VIS.dataContext.getJSONRecord("MInventoryLine/GetProductUOM", value.toString());
                mTab.setValue("C_UOM_ID", Util.getValueOfInt(Uom));
            }
        }

        var bd = null;
        var paramString = inventoryLine.toString();
        /**
         * Modified for update Book Qty on existing records.
         * Also checks the old asi and removes it if product has been change.
         */
        if (inventoryLine != null && inventoryLine != 0) {
            //Get MInventoryLine Information
            //Get product price information
            var dr = null;
            dr = VIS.dataContext.getJSONRecord("MInventoryLine/GetMInventoryLine", paramString);

            var M_Product_ID = dr["M_Product_ID"];//dr.M_Product_ID;//getQtyAvailable(M_Warehouse_ID, M_Product_ID, M_AttributeSetI
            var M_Locator_ID = dr["M_Locator_ID"];//dr.M_Locator_ID;

            // MInventoryLine iLine = new MInventoryLine(ctx, inventoryLine.Value, null);  
            var M_Product_ID1 = Util.getValueOfInt(mTab.getValue("M_Product_ID"));
            var M_Locator_ID1 = Util.getValueOfInt(mTab.getValue("M_Locator_ID"));
            // get product Container
            var M_ProductContainer_ID = Util.getValueOfInt(mTab.getValue("M_ProductContainer_ID"));
            var M_AttributeSetInstance_ID1 = 0;
            // if product or locator has changed recalculate Book Qty
            //if (M_Product_ID1 != M_Product_ID || M_Locator_ID1 != M_Locator_ID) {
            if (M_Product_ID1 != M_Product_ID || M_Locator_ID1 != M_Locator_ID || (mField.getColumnName() == "M_ProductContainer_ID")) {
                // Check asi - if product has been changed remove old asi
                if (Attribute_ID > 0) {
                    mTab.setValue("M_AttributeSetInstance_ID", Attribute_ID);
                }
                if (M_Product_ID1 == M_Product_ID) {
                    M_AttributeSetInstance_ID1 = Util.getValueOfInt(mTab.getValue("M_AttributeSetInstance_ID"));
                }
                else {
                    mTab.setValue("M_AttributeSetInstance_ID", null);
                }
                try {
                    bd = this.SetQtyBook(AD_Org_ID, M_AttributeSetInstance_ID1, M_Product_ID1, M_Locator_ID1, MovementDate, M_ProductContainer_ID);
                    mTab.setValue("QtyBook", bd);
                    mTab.setValue("OpeningStock", bd);
                }
                catch (err) {
                    this.setCalloutActive(false);
                    this.log.severe(err.toString());
                    return mTab.setValue("QtyBook", bd);
                }
            }
            this.setCalloutActive(false);
            ctx = windowNo = mTab = mField = value = oldValue = null;
            return "";
        }
        
        //	Set Attribute
        if (Attribute_ID > 0) {
            mTab.setValue("M_AttributeSetInstance_ID", Attribute_ID);
        }
        var M_AttributeSetInstance_ID = 0;
        var asi = Util.getValueOfInt(mTab.getValue("M_AttributeSetInstance_ID"));
        if (asi != null)
            M_AttributeSetInstance_ID = asi;

        mTab.setValue("M_AttributeSetInstance_ID", null);
        var M_ProductContainer_ID = Util.getValueOfInt(mTab.getValue("M_ProductContainer_ID"));

        try {
            bd = this.SetQtyBook(AD_Org_ID, M_AttributeSetInstance_ID, M_Product_ID, M_Locator_ID, MovementDate, M_ProductContainer_ID);
            mTab.setValue("QtyBook", bd);
            mTab.setValue("OpeningStock", bd);
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
            return mTab.setValue("QtyBook", bd);
        }
        //
        this.log.info("M_Product_ID=" + M_Product_ID
            + ", M_Locator_ID=" + M_Locator_ID
            + ", M_AttributeSetInstance_ID=" + M_AttributeSetInstance_ID
            + " - QtyBook=" + bd);
        this.setCalloutActive(false);

        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// Product/Locator/asi modified.
    /// Set Attribute Set Instance
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current window no</param>
    /// <param name="tab">model tab</param>
    /// <param name="field">model field</param>
    /// <param name="value">new value</param>
    /// <returns>error message or ""</returns>
    CalloutInventory.prototype.AttributeSetInstance = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive())
            return "";

        //	overkill - see new implementation
        //var inventoryLine = Util.getValueOfInt(mTab.getValue("M_InventoryLine_ID"));
        //var M_Inventory_ID = Util.getValueOfInt(mTab.getValue("M_Inventory_ID"));
        //var dr1 = null;
        //var paramInventory = M_Inventory_ID.toString();
        //dr1 = VIS.dataContext.getJSONRecord("MInventory/GetMInventory", paramInventory);

        var MovementDate = ctx.getContext(windowNo, "MovementDate");
        var AD_Org_ID = ctx.getContextAsInt(windowNo, "AD_Org_ID");

        var bd = null;
        var M_Product_ID = 0;
        var product = Util.getValueOfInt(mTab.getValue("M_Product_ID"));
        if (product != null)
            M_Product_ID = product;
        if (M_Product_ID == 0)
            return "";
        var M_Locator_ID = 0;
        var locator = Util.getValueOfInt(mTab.getValue("M_Locator_ID"));
        if (locator != null)
            M_Locator_ID = locator;
        if (M_Locator_ID == 0)
            return "";

        this.setCalloutActive(true);
        //	Set Attribute
        var M_AttributeSetInstance_ID = 0;
        var asi = Util.getValueOfInt(mTab.getValue("M_AttributeSetInstance_ID"));
        if (asi != null)
            M_AttributeSetInstance_ID = asi;

        var M_ProductContainer_ID = Util.getValueOfInt(mTab.getValue("M_ProductContainer_ID"));

        try {
            bd = this.SetQtyBook(AD_Org_ID, M_AttributeSetInstance_ID, M_Product_ID, M_Locator_ID, MovementDate, M_ProductContainer_ID);
            mTab.setValue("QtyBook", bd);
            mTab.setValue("OpeningStock", bd);
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
            return mTab.setValue("QtyBook", bd);
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /// <summary>
    /// FifferenceQty/AsOnDateCount modified.
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current window no</param>
    /// <param name="tab">model tab</param>
    /// <param name="field">model field</param>
    /// <param name="value">new value</param>
    /// <returns>error message or ""</returns>
    CalloutInventory.prototype.SetDiff = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        var asOnDateQty = 0, openingStock = 0, diffQty = 0, qtyBook = 0;
        this.setCalloutActive(true);
        try {
            openingStock = Util.getValueOfDecimal(mTab.getValue("OpeningStock"));
            qtyBook = Util.getValueOfDecimal(mTab.getValue("QtyBook"));

            if (mField.getColumnName().equals("DifferenceQty")) {
                diffQty = Util.getValueOfDecimal(mTab.getValue("DifferenceQty"));
                asOnDateQty = openingStock - diffQty;
                mTab.setValue("AsOnDateCount", asOnDateQty);
            }
            else if (mField.getColumnName().equals("AsOnDateCount")) {
                asOnDateQty = Util.getValueOfDecimal(mTab.getValue("AsOnDateCount"));
                diffQty = openingStock - asOnDateQty;
                mTab.setValue("DifferenceQty", diffQty);
            }
            if (qtyBook - diffQty > 0) {
                mTab.setValue("QtyCount", qtyBook - diffQty);
            }
            else {
                mTab.setValue("QtyCount", 0);
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
            return err.message;
        }

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };


    CalloutInventory.prototype.SetQtyBook = function (AD_Org_ID, M_AttributeSetInstance_ID, M_Product_ID, M_Locator_ID, MovementDate, M_ProductContainer_ID) {
        
        var isContainerApplicable = false;
        if (VIS.context.ctx["#PRODUCT_CONTAINER_APPLICABLE"] != undefined) {
            isContainerApplicable = VIS.context.ctx["#PRODUCT_CONTAINER_APPLICABLE"].equals("Y", true);
        }

        //var tsDate = (Number(MovementDate.getMonth()) + 1) + "-" + MovementDate.getDate() + "-" + MovementDate.getFullYear();
        var params = isContainerApplicable.toString() + "," + MovementDate.toString() + "," + M_Product_ID.toString() + "," + M_Locator_ID.toString()
            + "," + M_AttributeSetInstance_ID.toString() + "," + M_ProductContainer_ID.toString() + "," + AD_Org_ID.toString();

        var bd = VIS.dataContext.getJSONRecord("MInventory/GetCurrentQty", params);
        return bd;
    };

    // Callout added by mohit to get UOM conversion on Physical inventory line and internal use inventory line against the selected UOM.- 12 June 20018
    /// <summary>
    /// convert the qty according to selected UOM
    /// </summary>
    /// <param name="ctx">context</param>
    /// <param name="windowNo">current window no</param>
    /// <param name="tab">model tab</param>
    /// <param name="field">model field</param>
    /// <param name="value">new value</param>
    /// <returns>Set qty or error message</returns>
    CalloutInventory.prototype.SetUOMQty = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        if (this.isCalloutActive())
            return "";
        var asOnDateQty = 0, openingStock = 0, diffQty = 0, QtyOrdered = 0;
        this.setCalloutActive(true);
        try {
            var M_Product_ID = Util.getValueOfDecimal(mTab.getValue("M_Product_ID"));
            if (M_Product_ID == 0) {

                this.setCalloutActive(false);
                return "";
            }

            // Check the source window
            //physical inventory call
            if (mTab.getValue("IsInternalUse") == false || mTab.getValue("IsInternalUse") == null) {
                if (mField.getColumnName() == "C_UOM_ID") {

                    var C_UOM_To_ID = Util.getValueOfInt(value);
                    QtyEntered = Util.getValueOfDecimal(mTab.getValue("QtyEntered"));
                    M_Product_ID = Util.getValueOfInt(mTab.getValue("M_Product_ID"));

                    //JID_0680 set quantity acc to percision
                    paramStr = C_UOM_To_ID.toString().concat(","); //1
                    var gp = VIS.dataContext.getJSONRecord("MUOM/GetPrecision", paramStr);
                    var QtyEntered1 = QtyEntered.toFixed(Util.getValueOfInt(gp));//, MidpointRounding.AwayFromZero);
                    if (QtyEntered != QtyEntered1) {
                        this.log.fine("Corrected QtyEntered Scale UOM=" + C_UOM_To_ID
                            + "; QtyEntered=" + QtyEntered + "->" + QtyEntered1);
                        QtyEntered = QtyEntered1;
                        mTab.setValue("QtyEntered", QtyEntered);
                    }

                    paramStr = M_Product_ID.toString().concat(',').concat(C_UOM_To_ID.toString()).concat(',').concat(QtyEntered.toString());
                    var pc = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramStr);
                    QtyOrdered = pc

                    //handle issue while conversion on Physical Inventory
                    //var conversion = false

                    if (QtyOrdered == null) {
                        QtyOrdered = QtyEntered;
                    }
                }
                else {
                    var C_UOM_To_ID = ctx.getContextAsInt(windowNo, "C_UOM_ID");
                    QtyEntered = Util.getValueOfDecimal(mTab.getValue("QtyEntered"));

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
                }

                openingStock = Util.getValueOfDecimal(mTab.getValue("OpeningStock"));
                qtyBook = Util.getValueOfDecimal(mTab.getValue("QtyBook"));
                if (mTab.getValue("AdjustmentType").toString() == "D") {                    
                    diffQty = QtyOrdered;
                    asOnDateQty = openingStock - diffQty;
                    mTab.setValue("DifferenceQty", QtyOrdered);
                    mTab.setValue("AsOnDateCount", asOnDateQty);
                }
                else if (mTab.getValue("AdjustmentType").toString() == "A") {                    
                    asOnDateQty = QtyOrdered;
                    diffQty = openingStock - asOnDateQty;
                    mTab.setValue("AsOnDateCount", QtyOrdered);
                    mTab.setValue("DifferenceQty", diffQty);
                }
                if (qtyBook - diffQty > 0) {
                    mTab.setValue("QtyCount", qtyBook - diffQty);
                }
                else {
                    mTab.setValue("QtyCount", 0);                    
                }

                // VIS0045: Reset Cost price when as on date count is less than Current qty
                if (qtyBook > qtyBook - diffQty) {
                    mTab.setValue("PriceCost", 0);
                }
            }
            // Internal use inventory call
            else if (mTab.getValue("IsInternalUse") == true) {
                if (mField.getColumnName() == "C_UOM_ID") {

                    var C_UOM_To_ID = Util.getValueOfInt(value);
                    QtyEntered = Util.getValueOfDecimal(mTab.getValue("QtyEntered"));
                    M_Product_ID = Util.getValueOfInt(mTab.getValue("M_Product_ID"));

                    //JID_0680 set quantity acc to percision
                    paramStr = C_UOM_To_ID.toString().concat(","); //1
                    var gp = VIS.dataContext.getJSONRecord("MUOM/GetPrecision", paramStr);
                    var QtyEntered1 = QtyEntered.toFixed(Util.getValueOfInt(gp));//, MidpointRounding.AwayFromZero);
                    if (QtyEntered != QtyEntered1) {
                        this.log.fine("Corrected QtyEntered Scale UOM=" + C_UOM_To_ID
                            + "; QtyEntered=" + QtyEntered + "->" + QtyEntered1);
                        QtyEntered = QtyEntered1;
                        mTab.setValue("QtyEntered", QtyEntered);
                    }

                    paramStr = M_Product_ID.toString().concat(',').concat(C_UOM_To_ID.toString()).concat(',').concat(QtyEntered.toString());
                    var pc = VIS.dataContext.getJSONRecord("MUOMConversion/ConvertProductFrom", paramStr);
                    QtyOrdered = pc;

                    //handle issue while conversion on Physical Inventory
                    if (QtyOrdered == null)
                        QtyOrdered = QtyEntered;

                    mTab.setValue("QtyInternalUse", QtyOrdered);

                    //var conversion = false
                    //if (QtyOrdered != null) {
                    //    conversion = QtyEntered != QtyOrdered;
                    //}
                    //if (QtyOrdered == null) {
                    //    conversion = false;
                    //    QtyOrdered = 1;
                    //}
                    //if (conversion) {
                    //    mTab.setValue("QtyInternalUse", QtyOrdered);
                    //}
                    //else {

                    //    mTab.setValue("QtyInternalUse", (QtyOrdered * QtyEntered));
                    //}
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

                    mTab.setValue("QtyInternalUse", QtyOrdered);
                }
            }
        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString());
            return err.message;
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutInventory = CalloutInventory;

})(VIS, jQuery);