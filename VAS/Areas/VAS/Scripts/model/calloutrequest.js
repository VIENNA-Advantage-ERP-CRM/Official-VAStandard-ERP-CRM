; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;
    function CalloutRequest() {
        VIS.CalloutEngine.call(this, "VIS.CalloutRequest"); //must call
    }
    VIS.Utility.inheritPrototype(CalloutRequest, VIS.CalloutEngine);//inherit CalloutEngine
    /// <summary>
    /// Request - Copy Mail Text - <b>Callout</b>
    /// </summary>
    /// <param name="ctx">Context</param>
    /// <param name="WindowNo">current Window No</param>
    /// <param name="mTab">Model Tab</param>
    /// <param name="mField">Model Field</param>
    /// <param name="value">The new value</param>
    /// <returns>Error message or ""</returns>
    CalloutRequest.prototype.CopyMail = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        var colName = mField.getColumnName();
        this.log.info(colName + "=" + value);
        if (value == null || value.toString() == "") {
            return "";
        }
       
        try {
            var txt = VIS.dataContext.getJSONRecord("MRequestType/GetMailText", value.toString());
            txt = VIS.Env.parseContext(ctx, windowNo, txt, false, true);
            mTab.setValue("Result", txt);
            //}
            //idr.close();

        }
        catch (err) {
            this.setCalloutActive(false);
            //if (idr != null) {
            //    idr.close();
            //}
            this.log.log(Level.SEVERE, sql, err);
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };  //  copyText


    /// <summary>
    /// Request - Copy Response Text - <b>Callout</b>
    /// </summary>
    /// <param name="ctx">Context</param>
    /// <param name="WindowNo">current Window No</param>
    /// <param name="mTab">Model Tab</param>
    /// <param name="mField">Model Field</param>
    /// <param name="value">The new value</param>
    /// <returns>message or ""</returns>
    CalloutRequest.prototype.CopyResponse = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        var colName = mField.getColumnName();
        this.log.info(colName + "=" + value);
        if (value == null || value.toString() == "") {
            return "";
        }
        
        try {
            var txt = VIS.dataContext.getJSONRecord("MRequestType/GetResponseText", value.toString());
            txt = VIS.Env.parseContext(ctx, windowNo, txt, false, true);
            mTab.setValue("Result", txt);
            //}
            // idr.close();

        }
        catch (err) {
            this.setCalloutActive(false);
            //if (idr != null) {
            //    idr.close();
            //}
            this.log.log(Level.SEVERE, sql, err);
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };  //  copyResponse

    /// <summary>
    /// Request - Chane of Request Type - <b>Callout</b>
    /// </summary>
    /// <param name="ctx">Context</param>
    /// <param name="WindowNo">current Window No</param>
    /// <param name="mTab">Model Tab</param>
    /// <param name="mField">Model Field</param>
    /// <param name="value"> The new value</param>
    /// <returns>Error message or ""</returns>
    CalloutRequest.prototype.Type = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        var colName = mField.getColumnName();
        this.log.info(colName + "=" + value);
        mTab.setValue("R_Status_ID", null);
        if (value == null || value.toString() == "") {
            return "";
        }
        //var R_RequestType_ID = ((Integer)value).intValue();
        var R_RequestType_ID = Util.getValueOfInt(value);
        if (R_RequestType_ID == 0) {
            return "";
        }
        var paramString = R_RequestType_ID.toString();


        //Get BankAccount information

        var R_Status_ID = VIS.dataContext.getJSONRecord("MRequestType/GetDefaultR_Status_ID", paramString);
        //MRequestType rt = MRequestType.Get(ctx, R_RequestType_ID);
        // var R_Status_ID = rt.GetDefaultR_Status_ID();
        if (R_Status_ID != 0) {
            //mTab.setValue("R_Status_ID", new Integer(R_Status_ID));
            mTab.setValue("R_Status_ID", Util.getValueOfInt(R_Status_ID));

        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };//	type


    //CalloutRequest.prototype.BPartner = function (ctx, windowNo, mTab, mField, value, oldValue) {
    //    if (value == null || value.toString() == "" || this.isCalloutActive()) {
    //        return "";
    //    }

    //    this.setCalloutActive(true);

    //    var sql = "Select AD_User_ID FROM AD_User WHERE IsActive='Y' AND C_BPartner_ID=" + value;
    //    var AD_User_ID = Util.getValueOfInt(VIS.DB.executeScalar(sql));

    //    if (AD_User_ID) {
    //        mTab.setValue("AD_User_ID", 0);
    //        mTab.setValue("AD_User_ID", AD_User_ID);
    //    }

    //    this.setCalloutActive(false);
    //    return "";
    //};

    VIS.Model.CalloutRequest = CalloutRequest;

    //*************CalloutMRequest Starts***********
    function CalloutMRequest() {
        VIS.CalloutEngine.call(this, "VIS.CalloutMRequest");//must call
    };
    VIS.Utility.inheritPrototype(CalloutMRequest, VIS.CalloutEngine); //inherit prototype


    CalloutMRequest.prototype.DateRequired = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }

        this.setCalloutActive(true);
        try {
            var DateDoc, DateReq;
            DateDoc = new Date(mTab.getValue("StartDate"));
            DateReq = new Date(mTab.getValue("CloseDate"));
            if (DateReq.toISOString() < DateDoc.toISOString()) {
                mTab.setValue("CloseDate", "");
                this.setCalloutActive(false);
                VIS.ADialog.info("CloseDateInvalid", null, "", "");
            }
            this.log.fine("CloseDate=" + DateReq);
        }
        catch (err) {
            VIS.ADialog.info("error in Date" + err, null, "", "");
            this.setCalloutActive(false);
            return err;
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    CalloutMRequest.prototype.PlanDateRequired = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }

        this.setCalloutActive(true);
        try {
            var DateDoc, DateReq;
            DateDoc = new Date(mTab.getValue("DateStartPlan"));
            DateReq = new Date(mTab.getValue("DateCompletePlan"));

            if (DateReq.toISOString() < DateDoc.toISOString()) {
                mTab.setValue("DateCompletePlan", "");
                this.setCalloutActive(false);
                VIS.ADialog.info("CmpDateInvalid", null, "", "");
            }
            this.log.fine("DateCompletePlan=" + DateReq);
        }
        catch (err) {
            VIS.ADialog.info("error in Date" + err, null, "", "");
            this.setCalloutActive(false);
            return err;
        }
        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    VIS.Model.CalloutMRequest = CalloutMRequest;
    //*************CalloutMRequest Ends*************

})(VIS, jQuery);