; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    //***************CalloutKPI Start********************

    function CalloutKPI() {
        VIS.CalloutEngine.call(this, "VIS.CalloutKPI");//must call
    };
    VIS.Utility.inheritPrototype(CalloutKPI, VIS.CalloutEngine); //inherit prototype

    CalloutKPI.prototype.CalculationSelection = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "" || value == false) {
            return "";
        }
        this.setCalloutActive(true);

        if (mField.getColumnName() == "IsSum") {
            //DisplayType.Integer;
            mTab.setValue("IsMaximum", false);
            mTab.setValue("IsCount", false);
            mTab.setValue("IsMinimum", false);
        }
        else if (mField.getColumnName() == "IsMaximum") {
            mTab.setValue("IsSum", false);
            mTab.setValue("IsMinimum", false);
            mTab.setValue("IsCount", false);

        }
        else if (mField.getColumnName() == "IsCount") {
            mTab.setValue("IsMaximum", false);
            mTab.setValue("IsSum", false);
            mTab.setValue("IsMinimum", false);

        }
        else if (mField.getColumnName() == "IsMinimum") {
            mTab.setValue("IsSum", false);
            mTab.setValue("IsCount", false);
            mTab.setValue("IsMaximum", false);

        }

        this.setCalloutActive(false);
        return "";
    };



    CalloutKPI.prototype.UpdateKPITableInContext = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);

        if (value == true) {
            mTab.setValue("AD_Table_ID", -1);
            mTab.setValue("AD_Tab_ID", -1);
            ctx.setContext(windowNo, "AD_Table_ID", -1);
            ctx.setContext(windowNo, "AD_Tab_ID", -1);
        }
        else {
            mTab.setValue("TableView_ID", -1);
            ctx.setContext(windowNo, "TableView_ID", -1);
        }

        this.setCalloutActive(false);
        return "";
    };



    CalloutKPI.prototype.UpdateTabIDContext = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive()) {
            return "";
        }
        this.setCalloutActive(true);
        if (value < 1 || value == null || value.toString() == "") {
            mTab.setValue("AD_Tab_ID", -1);
            ctx.setContext(windowNo, "AD_Tab_ID", -1);
        }
        else {
            ctx.setContext(windowNo, "AD_Tab_ID", value);
        }

        this.setCalloutActive(false);
        return "";
    };



    VIS.Model.CalloutKPI = CalloutKPI;

    //**************************CalloutKPI End******************************\\



    //***************CalloutDashboard Start********************

    function CalloutDashboard() {
        VIS.CalloutEngine.call(this, "VIS.CalloutDashboard");//must call
    };
    VIS.Utility.inheritPrototype(CalloutDashboard, VIS.CalloutEngine); //inherit prototype

    CalloutDashboard.prototype.UpdateTableInContext = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive()) {
            return "";
        }
        this.setCalloutActive(true);

        if (value == true) {
            mTab.setValue("AD_Table_ID", -1);
            mTab.setValue("AD_Tab_ID", -1);
            ctx.setContext(windowNo, "AD_Table_ID", -1);
            ctx.setContext(windowNo, "AD_Tab_ID", -1);
        }
        else {

            mTab.setValue("TableView_ID", -1);
            ctx.setContext(windowNo, "TableView_ID", -1);
        }

        this.setCalloutActive(false);
        return "";
    };

    CalloutDashboard.prototype.UpdateTabIDContext = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive()) {
            return "";
        }
        this.setCalloutActive(true);
        if (value < 1 || value == null || value.toString() == "") {
            mTab.setValue("AD_Tab_ID", -1);
            ctx.setContext(windowNo, "AD_Tab_ID", -1);
        }
        else {
            ctx.setContext(windowNo, "AD_Tab_ID", value);
            mTab.setValue("TableView_ID", -1);
            ctx.setContext(windowNo, "TableView_ID", -1);
        }

        this.setCalloutActive(false);
        return "";
    };

    CalloutDashboard.prototype.UpdateTableViewIDOnTableContext = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        if (value > 0) {
            mTab.setValue("TableView_ID", -1);
            ctx.setContext(windowNo, "TableView_ID", -1);
        }

        this.setCalloutActive(false);
        return "";
    };


    CalloutDashboard.prototype.SelectFunctionOnDashboard = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (this.isCalloutActive() || value == null || value.toString() == "" || value == false) {
            return "";
        }
        this.setCalloutActive(true);
        var bl = Util.getValueOfBoolean(value);

        if (mField.getColumnName() == "IsSum") {
            //DisplayType.Integer;
            mTab.setValue("IsAvg", false);
            mTab.setValue("IsCount", false);
            mTab.setValue("IsNone", false);
        }
        else if (mField.getColumnName() == "IsAvg") {
            mTab.setValue("IsSum", false);
            mTab.setValue("IsCount", false);
            mTab.setValue("IsNone", false);

        }
        else if (mField.getColumnName() == "IsCount") {
            mTab.setValue("IsSum", false);
            mTab.setValue("IsAvg", false);
            mTab.setValue("IsNone", false);

        }
        else if (mField.getColumnName() == "IsNone") {
            mTab.setValue("IsSum", false);
            mTab.setValue("IsCount", false);
            mTab.setValue("IsAvg", false);

        }
        this.setCalloutActive(false);


        return "";
    };

    //Manish 4/7/2016   For Maximum value check should not be greater then 60
    CalloutDashboard.prototype.ValueCheck = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive()) {
            return "";
        }
        this.setCalloutActive(true);


        var val = mTab.getValue("LastNValue");
        if (val > 60) {
            mTab.setValue("LastNValue", 1);
        }

        this.setCalloutActive(false);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";

    };
    //end 


    VIS.Model.CalloutDashboard = CalloutDashboard;

    //**************************CalloutDashboard End******************************\\


    //***************CalloutDashboard Start********************

    function CalloutDashboardView() {
        VIS.CalloutEngine.call(this, "VIS.CalloutDashboardView");//must call
    };
    VIS.Utility.inheritPrototype(CalloutDashboardView, VIS.CalloutEngine); //inherit prototype

    CalloutDashboardView.prototype.UpdateTabIDContext = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive()) {
            return "";
        }
        this.setCalloutActive(true);
        if (value < 1 || value == null || value.toString() == "") {
            mTab.setValue("AD_Tab_ID", -1);
            ctx.setContext(windowNo, "AD_Tab_ID", -1);
        }
        else {
            ctx.setContext(windowNo, "AD_Tab_ID", value);
        }

        this.setCalloutActive(false);
        return "";
    };

    CalloutDashboardView.prototype.GroupByChecked = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (this.isCalloutActive() || value == null) {
            return "";
        }
        this.setCalloutActive(true);
        //var sql = "UPDATE RC_ViewColumn SET IsGroupBy='N' WHERE RC_View_ID=" + mTab.getValue("RC_View_ID") + " AND RC_ViewColumn_ID NOT IN(" + mTab.getValue("RC_ViewColumn_ID") + ")";
        //var count = VIS.DB.executeQuery(sql);
        var paramstring = mTab.getValue("RC_View_ID").toString() + "," + mTab.getValue("RC_ViewColumn_ID").toString();
        VIS.dataContext.getJSONRecord("MFramework/UpdateGroupByChecked", paramstring);
        this.setCalloutActive(false);
        return "";

    };
    CalloutDashboardView.prototype.IsViewChecked = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (this.isCalloutActive() || value == null) {
            return "";
        }
        this.setCalloutActive(true);
        if (value) {
            mTab.setValue("AD_Tab_ID", -1);
            mTab.setValue("AD_Table_ID", -1);
            ctx.setContext(windowNo, "AD_Tab_ID", -1);
            ctx.setContext(windowNo, "AD_Table_ID", -1);
        }
        else {
            mTab.setValue("TableView_ID", -1);
            ctx.setContext(windowNo, "TableView_ID", -1);
        }
        this.setCalloutActive(false);
        return "";

    };
    VIS.Model.CalloutDashboardView = CalloutDashboardView;

    //**************************CalloutDashboard End******************************\\


    //****************CalloutService Start***********
    function CalloutService() {
        VIS.CalloutEngine.call(this, "VIS.CalloutService");//must call
    };
    VIS.Utility.inheritPrototype(CalloutService, VIS.CalloutEngine); //inherit prototype

    /**
     *  @param ctx      Context
     *  @param WindowNo current Window No
     *  @param mTab     Model Tab
     *  @param mField   Model Field
     *  @param value    The new value
     *  @return Error message or ""
     */
    CalloutService.prototype.StatisticGroup = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value.toString() == "") {
            return "";
        }
        //When we change the Statistic Group the Statistic subgroup 
        //value will be cleared
        mTab.setValue("FO_STATISTICSUBGROUP_ID", 0);
        return "";
    }
    //CalloutService.prototype.SelectFunctionOnDashboard = function (ctx, windowNo, mTab, mField, value, oldValue) {

    //    if (this.isCalloutActive() || value == null || value.toString() == "" || value == false) {
    //        return "";
    //    }
    //    this.setCalloutActive(true);
    //    var bl = Util.getValueOfBoolean(value);

    //    if (mField.getColumnName() == "IsSum") {
    //        //DisplayType.Integer;
    //        mTab.setValue("IsAvg", false);
    //        mTab.setValue("IsCount", false);
    //        mTab.setValue("IsNone", false);
    //    }
    //    else if (mField.getColumnName() == "IsAvg") {
    //        mTab.setValue("IsSum", false);
    //        mTab.setValue("IsCount", false);
    //        mTab.setValue("IsNone", false);

    //    }
    //    else if (mField.getColumnName() == "IsCount") {
    //        mTab.setValue("IsSum", false);
    //        mTab.setValue("IsAvg", false);
    //        mTab.setValue("IsNone", false);

    //    }
    //    else if (mField.getColumnName() == "IsNone") {
    //        mTab.setValue("IsSum", false);
    //        mTab.setValue("IsCount", false);
    //        mTab.setValue("IsAvg", false);

    //    }
    //    this.setCalloutActive(false);


    //    return "";
    //}

    VIS.Model.CalloutService = CalloutService;
    //*****************CalloutService Ends*******************************


    //**************CalloutWorkflow Starts********
    function CallOutWorkflow() {
        VIS.CalloutEngine.call(this, "VIS.CallOutWorkflow ");//must call
    };
    VIS.Utility.inheritPrototype(CallOutWorkflow, VIS.CalloutEngine); //inherit prototype


    CallOutWorkflow.prototype.WorkflowType = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var wfType = Util.getValueOfString(value.toString());
        if (wfType == "R") {
            //var ID = Util.getValueOfInt(VIS.DB.executeScalar("SELECT AD_Table_ID FROM AD_Table WHERE IsActive='Y' AND TableName= 'VADMS_MetaData'"));
            var ID = VIS.dataContext.getJSONRecord("MFramework/GetWorkflowType", null);
            if (ID == 0) {
                VIS.ADialog.info("No_VADMS", null, "", "");
                //ShowMessage.Error("No_VADMS", true);
                return VIS.Msg.getMsg("No_VADMS");
            }
            mTab.setValue("AD_Table_ID", ID);
        }
        this.setCalloutActive(false);
        return "";
    };
    CallOutWorkflow.prototype.SetSelectedColumn = function (ctx, windowNo, mTab, mField, value, oldValue) {

        if (value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);
        var ColumnName = VIS.dataContext.getJSONRecord("MFramework/GetIsGenericAttribute", Util.getValueOfInt(value));
        if (ColumnName.toLower() == "C_GenAttributeSetInstance_ID".toLower()) {
            ctx.setContext(windowNo, "IsGenericAttribute", "Y");
        }
        else {
            ctx.setContext(windowNo, "IsGenericAttribute", "N");
        }
        this.setCalloutActive(false);
        return "";
    };
    VIS.Model.CallOutWorkflow = CallOutWorkflow;
    //**************CalloutWorkflow Ends**********


    //*************CalloutDisplayButton Starts***********
    function CalloutDisplayButton() {
        VIS.CalloutEngine.call(this, "VIS.CalloutDisplayButton ");//must call
    };
    VIS.Utility.inheritPrototype(CalloutDisplayButton, VIS.CalloutEngine); //inherit prototype

    CalloutDisplayButton.prototype.DisplayButton = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";

        }
        if (Util.getValueOfInt(mTab.getValue("C_DocTypeTarget_ID")) != 0) {
            //var sql = "select dc.DocSubTypeSO from c_doctype dc inner join c_docbasetype db on(dc.DocBaseType=db.DocBaseType)"
            //    + "where c_doctype_id=" + Util.getValueOfInt(mTab.getValue("C_DocTypeTarget_ID")) + " and db.DocBaseType='SOO' and dc.DocSubTypeSO in ('WR','WI')";
            var _DocBaseType = VIS.dataContext.getJSONRecord("MDocType/GetDocSubType", value.toString());
            //var _DocBaseType = Util.getValueOfString(VIS.DB.executeScalar(sql, null, null));
            if (_DocBaseType == "WR" || _DocBaseType == "WI") {
                mTab.setValue("InvoicePrint", "Y");
            }
            else {
                mTab.setValue("InvoicePrint", "N");

            }

        }
        else {
            this.setCalloutActive(false);
            return "";
        }
        this.setCalloutActive(false);
        return "";
    }
    VIS.Model.CalloutDisplayButton = CalloutDisplayButton;
    //*************CalloutDisplayButton Ends*************


    function CalloutModuleMgmt() {
        VIS.CalloutEngine.call(this, "VIS.CalloutModuleMgmt");
    };
    //#endregion
    VIS.Utility.inheritPrototype(CalloutModuleMgmt, VIS.CalloutEngine); //inherit calloutengine

    CalloutModuleMgmt.prototype.GenerateMenuItemExportID = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        var modulePrefix = ctx.getContext(windowNo, "Prefix");


        if ((mTab.getValue("MenuItemExport_ID") == null && mTab.getValue("IsMenuItem").toString() == "true")) {
            var date = new Date();
            var ExportID = modulePrefix + date.getTime();
            mTab.setValue("MenuItemExport_ID", ExportID);
        }

        this.setCalloutActive(false);
        return "";
    }

    CalloutModuleMgmt.prototype.GenerateVersionID = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        var versionno = ctx.getContext(windowNo, "VersionNo");
        var versionid = versionno.replace(".", "");
        mTab.setValue("VersionID", versionid);
        this.setCalloutActive(false);
        return "";
    }

    CalloutModuleMgmt.prototype.CheckBoxValidation = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        var CheckBoxValue = Util.getValueOfBoolean(value);
        if (CheckBoxValue == true) {
            var columnName = mField.getColumnName();
            if (columnName == "IsProfessionalFree") {
                mTab.setValue("IsProfessionalPaid", false);
                mTab.setValue("IsFree", false);
                mTab.setValue("IsPaid", false);
                // mTab.SetValue("IsProfessionalPaid", false);
            }
            else if (columnName == "IsProfessionalPaid") {
                mTab.setValue("IsProfessionalFree", false);
                mTab.setValue("IsFree", false);
                mTab.setValue("IsPaid", false);
            }
            else if (columnName == "IsFree") {
                mTab.setValue("IsProfessionalPaid", false);
                mTab.setValue("IsProfessionalFree", false);
                mTab.setValue("IsPaid", false);
            }
            else if (columnName == "IsPaid") {
                mTab.setValue("IsProfessionalPaid", false);
                mTab.setValue("IsFree", false);
                mTab.setValue("IsProfessionalFree", false);
            }
        }
        this.setCalloutActive(false);
        return "";
    }
    VIS.Model.CalloutModuleMgmt = CalloutModuleMgmt;

    //VIS264*************CalloutAzureBlob Start**************
    function CalloutAzureBlob() {
        VIS.CalloutEngine.call(this, "VIS.CalloutAzureBlob");//must call
    };
    VIS.Utility.inheritPrototype(CalloutAzureBlob, VIS.CalloutEngine); //inherit prototype
    CalloutAzureBlob.prototype.CheckModule = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (this.isCalloutActive() || value == null || value.toString() == "") {
            return "";
        }
        this.setCalloutActive(true);

        var fileLocation = Util.getValueOfString(mTab.getValue("SaveAttachmentOn"));

        if (fileLocation == "AB") {

            //check if VA090 module is Installed
            var modulePrefix = VIS.dataContext.getJSONRecord("VIS/ModulePrefix/GetModulePrefix", "VA090_");
            if (!modulePrefix["VA090_"]) {
                mTab.setValue("SaveAttachmentOn", null);
                this.setCalloutActive(false);
                return VIS.Msg.getMsg("VIS_VA090NotInstalled");
            }
        }

        this.setCalloutActive(false);
        return "";
    };
    VIS.Model.CalloutAzureBlob = CalloutAzureBlob;
    //VIS264**************CalloutAzureBlob End*************

})(VIS, jQuery);