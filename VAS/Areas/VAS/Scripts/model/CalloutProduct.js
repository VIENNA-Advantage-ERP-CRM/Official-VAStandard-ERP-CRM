; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    /// <summary>
    /// Product Callouts
    /// </summary>
    function CalloutProduct() {
        VIS.CalloutEngine.call(this, "VIS.CalloutProduct"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutProduct, VIS.CalloutEngine);//inherit CalloutEngine
    /**
     *	Product Category
     *  @param ctx context
     *  @param windowNo current Window No
     *  @param mTab Grid Tab
     *  @param mField Grid Field
     *  @param value New Value
     *  @return null or error message
     */
    CalloutProduct.prototype.ProductCategory = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value.toString() == "") {
            return "";
        }
        var M_Product_Category_ID = Util.getValueOfInt(value);
        if (M_Product_Category_ID == null || Util.getValueOfInt(M_Product_Category_ID) == 0
            || M_Product_Category_ID == 0)
            return "";
        var paramString = Util.getValueOfInt(value);
        /**
         * Modified for update Book Qty on existing records.
         * Also checks the old asi and removes it if product has been change.
         */

        //Get MInventoryLine Information
        //Get product price information
        var dr = null;
        dr = VIS.dataContext.getJSONRecord("MProductCategory/GetProductCategory", paramString);

        //var M_Product_ID = dr.M_Product_ID;//getQtyAvailable(M_Warehouse_ID, M_Product_ID, M_AttributeSetI
        //var M_Locator_ID=dr.M_Locator_ID;     
        var IsPurchasedToOrder = dr.IsPurchasedToOrder;
        //  MProductCategory pc = new MProductCategory(ctx, M_Product_Category_ID, null);
        mTab.setValue("IsPurchasedToOrder", IsPurchasedToOrder);
        pc = null;
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    CalloutProduct.prototype.UOM = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }

        if (mTab.getValue("M_Product_id") == null) {
            return "";
        }
        this.setCalloutActive(true);

        // If values are in The Transaction Tab then restrict user so that UOM can't be changed or set to what it was previously.
        var fields = mTab.getValue("M_Product_ID").toString();
        var result = VIS.dataContext.getJSONRecord("MProduct/GetTransactionCount", fields);//Created by Nisha

        if (result > 0) {
            VIS.ADialog.info("Can't change UOM due to Transactions happens based on existing UOM");
            //var uom_ID = VIS.dataContext.getJSONRecord("MProduct/GetC_UOM_ID", mTab.getValue("M_Product_ID").toString());
            mTab.setValue("C_UOM_ID", oldValue);
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /**
     *	Resource Group
     *  @param ctx context
     *  @param windowNo current Window No
     *  @param mTab Grid Tab
     *  @param mField Grid Field
     *  @param value New Value
     *  @return null or error message
     */
    CalloutProduct.prototype.ResourceGroup = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        var resgrp = Util.getValueOfString(value);
        if (resgrp == null || resgrp.length == 0)
            return "";

        if ("O" == resgrp)
            mTab.setValue("BasisType", null);
        else
            mTab.setValue("BasisType", "I");
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    /**
     *  Organization
     *  @param ctx context
     *  @param windowNo current Window No
     *  @param mTab Grid Tab
     *  @param mField Grid Field
     *  @param value New Value
     *  @return null or error message
     */
    CalloutProduct.prototype.Organization = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value.toString() == "") {
            return "";
        }
        var AD_Org_ID = Util.getValueOfInt(value);

        if (AD_Org_ID == null) {
            return "";
        }
        var dr = null;
        dr = VIS.dataContext.getJSONRecord("MLocator/GetLocator", paramString);

        var Default_Locator_ID = dr["Default_Locator_ID"];//getQtyAvailable(M_Warehouse_ID, M_Product_ID, M_AttributeSetI


        // MLocator defaultLocator = MLocator.GetDefaultLocatorOfOrg(ctx, AD_Org_ID);
        // if (defaultLocator != null)
        // {
        mTab.setValue("M_Locator_ID", Default_Locator_ID);
        //}
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutProduct = CalloutProduct;


    function CalloutProdCatVal() {
        VIS.CalloutEngine.call(this, "VIS.CalloutProdCatVal");//must call
    };
    VIS.Utility.inheritPrototype(CalloutProdCatVal, VIS.CalloutEngine);//inherit prototype
    CalloutProdCatVal.prototype.SetProdCatValues = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        try {
            //var Sql = "SELECT IsActive "
            // Change By Mohit Amortization process 02/11/2016
            var countVA038 = false;
            //var isSOTrx = ctx.getWindowContext(windowNo, "IsSOTrx", true) == "Y";
            // End Change Amortization
            var countDTD001 = false;
            var countVA005 = false;
            //if (countVA005 > 0) {
            //    Sql += " , producttype ";
            //}
            //if (countVA005 > 0) {
            //    Sql += " , m_attributeset_id, c_taxcategory_id ";
            //}
            //Sql += " FROM M_Product_Category WHERE m_product_category_id=" + Util.getValueOfInt(value);
            //var result = CalloutDB.executeCalloutDataSet(Sql);
            // Added by Bharat on 11/May/2017
            var paramString = "VA038_,DTD001_,VA005_";
            var dr = null;
            dr = VIS.dataContext.getJSONRecord("ModulePrefix/GetModulePrefix", paramString);
            if (dr != null) {
                countVA038 = dr["VA038_"];
                countDTD001 = dr["DTD001_"];
                countVA005 = dr["VA005_"];
                var ds = null;
                paramString = value.toString();
                ds = VIS.dataContext.getJSONRecord("MProductCategory/GetCategoryData", paramString);
                if (ds != null) {
                    if (countDTD001) {
                        mTab.setValue("ProductType", ds["ProductType"]);
                    }
                    else {
                        mTab.setValue("ProductType", "");
                    }
                    if (countVA005) {
                        if (ds["M_AttributeSet_ID"] != null || ds["M_AttributeSet_ID"] != "") {
                            mTab.setValue("M_AttributeSet_ID", Util.getValueOfInt(ds["M_AttributeSet_ID"]));
                        }
                        else {
                            mTab.setValue("M_AttributeSet_ID", 0);
                        }
                        if (ds["C_TaxCategory_ID"] != null || ds["C_TaxCategory_ID"] != "") {
                            mTab.setValue("C_TaxCategory_ID", Util.getValueOfInt(ds["C_TaxCategory_ID"]));
                        }
                        else {
                            mTab.setValue("C_TaxCategory_ID", 0);
                        }
                    }
                    if (countVA038 > 0) {
                        if (ds["ProductType"] == "E" || ds["ProductType"] == "S") {
                            if (Util.getvalueofint(ds["A_Asset_Group_ID"]) > 0) {
                                if (ds["VA038_AmortizationTemplate_ID"] != null || ds["VA038_AmortizationTemplate_ID"] != "") {
                                    mTab.setValue("VA038_AmortizationTemplate_ID", Util.getvalueofint(ds["VA038_AmortizationTemplate_ID"]));
                                }
                                else {
                                    mTab.setValue("VA038_AmortizationTemplate_ID", 0);
                                }
                            }
                            else {
                                mTab.setValue("VA038_AmortizationTemplate_ID", 0);
                            }
                        }
                        else {
                            mTab.setValue("VA038_AmortizationTemplate_ID", 0);
                        }
                    }
                }
            }
            //if (result != null) {
            //    if (result.tables[0].rows.length > 0) {

            //        if (Util.getValueOfInt(CalloutDB.executeCalloutScalar("SELECT COUNT(AD_MODULEINFO_ID) FROM AD_MODULEINFO WHERE PREFIX='DTD001_' ")) > 0) {
            //            if (result.tables[0].rows[0].cells.producttype != null || Util.getValueOfString(result.tables[0].rows[0].cells.producttype) != "") {
            //                mTab.setValue("PRODUCTTYPE", result.tables[0].rows[0].cells.producttype);
            //            }
            //            else {
            //                mTab.setValue("PRODUCTTYPE", "");
            //            }
            //        }
            //        if (countVA005 > 0) {
            //            if (result.tables[0].rows[0].cells.m_attributeset_id != null || Util.getValueOfString(result.tables[0].rows[0].cells.m_attributeset_id) != "") {
            //                mTab.setValue("M_AttributeSet_ID", Util.getValueOfInt(result.tables[0].rows[0].cells.m_attributeset_id));
            //            }
            //            else {
            //                mTab.setValue("M_AttributeSet_ID", 0);
            //            }
            //            if (result.tables[0].rows[0].cells.c_taxcategory_id != null || Util.getValueOfString(result.tables[0].rows[0].cells.c_taxcategory_id) != "") {
            //                mTab.setValue("C_TaxCategory_ID", Util.getValueOfInt(result.tables[0].rows[0].cells.c_taxcategory_id));
            //            }
            //            else {
            //                mTab.setValue("C_TaxCategory_ID", 0);
            //            }
            //        }
            //    }
            //}
            // Change Done By Mohit Aortization Process 02/11/2016, Checkin By Sukhwinder on 06 March, 2017
            //if (countVA038 > 0) {
            //    if (Util.getValueOfString(result.tables[0].rows[0].cells.producttype) == "E" || Util.getValueOfString(result.tables[0].rows[0].cells.producttype) == "S") {
            //        var AssetGroup_ID = Util.getValueOfInt(CalloutDB.executeCalloutScalar("SELECT A_Asset_Group_ID FROM M_Product_Category WHERE M_Product_Category_ID=" + Util.getValueOfInt(value)));
            //        if (AssetGroup_ID > 0) {
            //            var AmortizTemp_ID = Util.getValueOfInt(CalloutDB.executeCalloutScalar("SELECT VA038_AmortizationTemplate_ID FROM A_Asset_Group WHERE A_Asset_Group_ID=" + AssetGroup_ID));
            //            if (AmortizTemp_ID > 0) {
            //                mTab.setValue("VA038_AmortizationTemplate_ID", AmortizTemp_ID);
            //            }
            //            else {
            //                mTab.setValue("VA038_AmortizationTemplate_ID", 0);
            //            }
            //        }
            //        else {
            //            mTab.setValue("VA038_AmortizationTemplate_ID", 0);
            //        }
            //    }
            //    else {
            //        mTab.setValue("VA038_AmortizationTemplate_ID", 0);
            //    }
            //}
            // End Change Amortization

        }
        catch (err) {
            this.setCalloutActive(false);
            this.log.severe(err.toString()); // SD
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    }
    VIS.Model.CalloutProdCatVal = CalloutProdCatVal;


    function CalloutProdTypeVal() {
        VIS.CalloutEngine.call(this, "VIS.CalloutProdTypeVal");
    }

    VIS.Utility.inheritPrototype(CalloutProdTypeVal, VIS.CalloutEngine);

    CalloutProdTypeVal.prototype.SetProdTypeValues = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        try {
            // If "Product Type" is Servies, Resource and Expense Type "Stocked" checkbox value will be "False". 
            if (value != 'I') {
                mTab.setValue("IsStocked", false);
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

    VIS.Model.CalloutProdTypeVal = CalloutProdTypeVal;

})(VIS, jQuery);