; VAS = window.VAS || {};
; VAS.Model = window.VAS.Model || {};

; (function (VAS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;



    //************************
    function CalloutTerm() {
        VIS.CalloutEngine.call(this, "CalloutTerm");//must call
    };

    VIS.Utility.inheritPrototype(CalloutTerm, VIS.CalloutEngine); //inherit prototype

         //to get term description data for term description field in 
        // term assignment tab in terms  window  from term details
        // field in term master window
    CalloutTerm.prototype.SetTermDescription = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if ((this.isCalloutActive()) || value == null || value.toString() == "") {
            return "";
        }

        try {
            this.setCalloutActive(true);
            var res = VIS.dataContext.getJSONRecord("VAS/MTerm/GetTermDescription", value);
            mTab.setValue("VAS_TermDescription", res);
        }
        catch (err) {
            this.setCalloutActive(false);
            return err;
        }

        this.setCalloutActive(false);
        return "";
    }



    VAS.Model.VAS_CalloutTerm = CalloutTerm;
    //**************CalloutTerm End*************


})(VAS, jQuery);
