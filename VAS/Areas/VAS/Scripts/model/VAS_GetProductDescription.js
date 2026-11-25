; VAS = window.VAS || {};
; (function (VAS, $) {

    var Util = VIS.Utility.Util;

    //*********** VAS_GetProductDescription Start ****
    /**
     * Module Name : VAS
     * Purpose     : Callout for the VAS_OppLines tab. Copies the Description of the selected product
     *               (M_Product.Description) into the Description field of the opportunity line.
     * Bind        : Table and Column -> VAS_OppLines -> M_Product_ID column -> tick Callout,
     *               Callout Code = VAS.Model.VAS_GetProductDescription.GetProductDescription
     */
    function VAS_GetProductDescription() {
        VIS.CalloutEngine.call(this, "VAS.VAS_GetProductDescription");//must call
    };
    VIS.Utility.inheritPrototype(VAS_GetProductDescription, VIS.CalloutEngine); //inherit prototype

    /**
     * Set the line Description from the description of the selected product.
     * Attach to the M_Product_ID column of VAS_OppLines.
     * @param {any} ctx
     * @param {any} windowNo
     * @param {any} mTab
     * @param {any} mField
     * @param {any} value  selected M_Product_ID
     * @param {any} oldValue
     * @returns "" on success
     */
    VAS_GetProductDescription.prototype.GetProductDescription = function (ctx, windowNo, mTab, mField, value, oldValue) {

        // Product cleared: nothing to fetch, keep whatever the user has typed in Description
        if (value == null || value.toString() == "") {
            return "";
        }
        var productID = Util.getValueOfInt(value);
        if (productID == 0) {
            return "";
        }

        // Synchronous lookup, same as the other VAS callouts: the description must be on the line
        // before the user moves on. The server applies MRole access on M_Product.
        var description = VIS.dataContext.getJSONRecord("MProduct/GetProductDescription", productID.toString());

        mTab.setValue("Description", Util.getValueOfString(description));

        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    VAS.Model = VAS.Model || {};
    VAS.Model.VAS_GetProductDescription = VAS_GetProductDescription;
    //*********** VAS_GetProductDescription End*************

})(VAS, jQuery);
