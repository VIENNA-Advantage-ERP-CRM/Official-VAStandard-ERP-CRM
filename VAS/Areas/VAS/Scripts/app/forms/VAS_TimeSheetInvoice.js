
; (function (VAS, $) {
    function VAS_TimeSheetInvoice() {
        this.frame;
        this.windowNo;
        var $self = this;
        this.arrListColumns = [];
        this.PreviewColumns = [];
        var ctx = VIS.Env.getCtx();

        this.dGrid = null;
        var AD_Org_ID = null;
        var precision = 2;
        var pageNo = 1;
        var pageSize = 13;
        var TotalRecords = 0;
        var toggleside = false;
        var gridPgnoInvoice = 1;
        this.lblOrg = new VIS.Controls.VLabel();
        this.lblCustomer = new VIS.Controls.VLabel();
        this.lblResource = new VIS.Controls.VLabel();
        this.tabRecordSelection = new VIS.Controls.VLabel();
        this.cmbOrg = new VIS.Controls.VComboBox('', false, false, true);
        this.vSearchCustomer = null;
        this.vSearchResource = null;
        this.vSearchRequest = null;
        this.vSearchTask = null;
        this.vSearchTimeAndExpense = null;
        this.vSearchProject = null;
        var culture = new VIS.CultureSeparator();
        var format = VIS.DisplayType.GetNumberFormat(VIS.DisplayType.Amount);
        var dotFormatter = VIS.Env.isDecimalPoint();
        this.$root = $("<div class='vis-formouterwrpdiv vas-arinvroot'>");
        this.$busyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis-busyindicatordiv"></i></div></div>');
        this.topDiv = null;
        this.sideDiv = null;
        this.gridSelectDiv = null;
        this.bottumDiv = null;
        this.PreviewGridDiv = null;
        var $bsyDiv = null
        var SubTotal = 0;
        var $FromDate = null;
        var $ToDate = null;
        var $CustomerSelected = null;
        var $ResourceSelected = null;
        var $TimeAndExpenseSelected = null;
        var $TaskSelected = null;
        var $ProjectSelected = null;
        var $RequestSelected = null;
        var $OrgDiv = null;
        var $CustomerDiv = null;
        var $ResourceDiv = null;
        var TimeDiv = null;
        var ProjectDiv = null;
        var RequestDiv = null;
        var TaskDiv = null;
        this.btnToggel = null;
        this.spnSelect = null;
        this.btnSpaceDiv = null;
        this.lblGenrate = null;
        this.lblSelect = null;
        var ModulePrefix = null;
        var LeftSideFields = null;
        var IsFilterBtnClicked = false;
        var gridDataArray = [];
        var LeftGridData = [];
        var FilteredData = [];
        var TimeExpenseId = [];
        var RequestId = [];
        var ResourceId = [];
        var TaskId = [];
        var CustomerId = [];
        var ProjectId = [];
        var sideDivWidth = 260;
        var minSideWidth = 50;
        var selectDivWidth = $(window).width() - (sideDivWidth + 20 + 5);
        var selectDivFullWidth = $(window).width() - (20 + minSideWidth);
        //VIS_427 Created array to store column name and tablename to get column id at single request
        var ColumnData = [
            { ColumnName: "AD_Org_ID", TableName: "S_TimeExpense" },
            { ColumnName: "C_BPartner_ID", TableName: "S_TimeExpenseLine" },
            { ColumnName: "S_Resource_ID", TableName: "S_Resource" },
            { ColumnName: "C_ProjectRef_ID", TableName: "C_Order" },
            { ColumnName: "R_Request_ID", TableName: "S_TimeExpenseLine" },
            { ColumnName: "S_TimeExpense_ID", TableName: "S_TimeExpense" },
            { ColumnName: "VA075_Task_ID", TableName: "VA075_Task" }
        ];
        //It will store value of column ids
        var ColumnIds = null;

        //Stored messages which is to be translated
        var elements = [
            "VAS_TimeRecordDoc",
            "VAS_Customer",
            "S_Resource_ID",
            "M_Product_ID",
            "Qty",
            "VAS_TotalBilableAmount",
            "Price",
            "C_BPartner_ID",
            "VA009_PaymentMethod_ID",
            "M_PriceList_ID",
            "C_Charge_ID",
            "C_Currency_ID",
            "C_Uom_ID",
            "VAS_LocationName",
            "VAS_recordCounted",
            "VAS_RecordedDate",
            "VAS_EstimatedTime",
            "VAS_ProjReq_Name",
            "VAS_ProjReq_ID",
            "VAS_PhaseName",
            "PriceStd",
            "PriceLimit",
            "StdPrecision",
            "EnforcePriceLimit",
            "VA075_WorkOrderOperation_ID",
            "S_TimeExpenseLine_ID",
            "AD_Org_ID",
            "VAS_FromDate",
            "VAS_toDate",
            "VAS_RecordSelection",
            "Search",
            "VAS_RecordNo",
            "VAS_Preview",
            "VAS_GenInvoice",
            "S_TimeExpense_ID",
            "C_Project_ID",
            "R_Request_ID",
            "C_Task_ID",
            "VAS_RecordedTime",
            "VAS_EstimatedTime",
            "VAS_RequestOrProject",
            "VAS_RecordingDate",
            "VAS_Filter",
            "VAS_ProductOrCharge",
            "VAS_CustImage",
            "VAS_RecordCount",
            "VAS_Start",
            "VA075_Task_ID",
            "VAS_Apply",
            "VAS_UomName",
            "VAS_PreviewDialog",
            "VAS_RequestInfoDialog",
            "VAS_SubTotal",
            "VAS_ISO_Code"
             
        ];
        VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);

        //This function is called when form is loaded
        function initializeComponent() {
            ModulePrefix = VIS.dataContext.getJSONRecord("VIS/ModulePrefix/GetModulePrefix", "VA075_");
            //this function will get the column id for lookups
            GetColumnID(ColumnData);
            /*this function is used to create controls on left side pannel div*/
            LeftSideControls();
            /*this function is used to create design of form */
            loadDesign();
            createBusyIndicator();
        };
        /*this function is used to create controls on left side pannel div*/
        function LeftSideControls() {
            LeftSideFields = $('<div class="vas-tis-top-fields">');
            //Created organization dropdown control
            $OrgDiv = $('<div class="input-group vis-input-wrap">');
            var Orglookup = VIS.MLookupFactory.getMLookUp(VIS.Env.getCtx(), $self.windowNo, ColumnIds.AD_Org_ID, VIS.DisplayType.TableDir);
            $self.cmbOrg = new VIS.Controls.VComboBox("AD_Org_ID", true, false, true, Orglookup, 150, VIS.DisplayType.TableDir);
            $self.cmbOrg.setMandatory(true);
            var $OrgControlWrap = $('<div class="vis-control-wrap">');
            var $OrgButtonWrap = $('<div class="input-group-append">');
            $OrgDiv.append($OrgControlWrap);
            $OrgControlWrap.append($self.cmbOrg.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VAS.translatedTexts.AD_Org_ID + '</label>');
            $OrgDiv.append($OrgControlWrap);
            $OrgButtonWrap.append($self.cmbOrg.getBtn(0));
            $OrgDiv.append($OrgButtonWrap);
            //Created customer search control to filter out data
            var CustomerDiv = $('<div class="vas-CustomerDiv">');
            var CustomerValidation = "C_BPartner.IsActive='Y' AND C_BPartner.IsCustomer = 'Y'";
            $CustomerDiv = $('<div class="input-group vis-input-wrap">');
            var CustomerLookUp = VIS.MLookupFactory.getMLookUp(VIS.Env.getCtx(), $self.windowNo, ColumnIds.C_BPartner_ID, VIS.DisplayType.Search, "C_BPartner_ID", 0, false, CustomerValidation);
            $self.vSearchCustomer = new VIS.Controls.VTextBoxButton("C_BPartner_ID", true, false, true, VIS.DisplayType.Search, CustomerLookUp);
            var $CustomerControlWrap = $('<div class="vis-control-wrap">');
            var $CustomerButtonWrap = $('<div class="input-group-append">');
            $CustomerDiv.append($CustomerControlWrap);
            $CustomerControlWrap.append($self.vSearchCustomer.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VAS.translatedTexts.C_BPartner_ID + '</label>');
            $CustomerDiv.append($CustomerControlWrap);
            $CustomerButtonWrap.append($self.vSearchCustomer.getBtn(0));
            $CustomerDiv.append($CustomerButtonWrap);
            CustomerDiv.append($CustomerDiv);
            //Created Resource control to filter out data
            var ResourceDiv = $('<div class="vas-ResourceDiv">');
            $ResourceDiv = $('<div class="input-group vis-input-wrap">');
            var ResourceValidationCode = "S_Resource.IsActive='Y' AND S_Resource.AD_Org_ID IN(0,@AD_Org_ID@)";
            var ResuorceLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, ColumnIds.S_Resource_ID, VIS.DisplayType.Search, "S_Resource_ID", 0, false, ResourceValidationCode);
            $self.vSearchResource = new VIS.Controls.VTextBoxButton("S_Resource_ID", true, false, true, VIS.DisplayType.Search, ResuorceLookUp, 0);
            var $ResourceControlWrap = $('<div class="vis-control-wrap">');
            var $ResourceButtonWrap = $('<div class="input-group-append">');
            $ResourceDiv.append($ResourceControlWrap);
            $ResourceControlWrap.append($self.vSearchResource.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VAS.translatedTexts.S_Resource_ID + '</label>');
            $ResourceDiv.append($ResourceButtonWrap);
            $ResourceButtonWrap.append($self.vSearchResource.getBtn(0));
            $ResourceDiv.append($ResourceButtonWrap);
            ResourceDiv.append($ResourceDiv);
            //Created from and to date control to filter out data
            var FromDatediv = $('<div class="vas-FromdateDiv">');
            $FromDatewrapDiv = $('<div class="input-group vis-input-wrap">');
            $FromDate = new VIS.Controls.VDate("DateReport", true, false, true, VIS.DisplayType.Date, "DateReport");
            var $FromDateWrap = $('<div class="vis-control-wrap">');
            $FromDatewrapDiv.append($FromDateWrap);
            $FromDateWrap.append($FromDate.getControl().attr('placeholder', ' ').attr('data-placeholder', '')).append('<label>' + VAS.translatedTexts.VAS_FromDate + '</label>');
            FromDatediv.append($FromDatewrapDiv);

            var toDatediv = $('<div class="vas-todateDiv">');
            $toDatewrapDiv = $('<div class="input-group vis-input-wrap">');
            $ToDate = new VIS.Controls.VDate("DateReport", true, false, true, VIS.DisplayType.Date, "DateReport");
            var $toDateWrap = $('<div class="vis-control-wrap">');
            $toDatewrapDiv.append($toDateWrap);
            $toDateWrap.append($ToDate.getControl().attr('placeholder', ' ').attr('data-placeholder', '')).append('<label>' + VAS.translatedTexts.VAS_toDate + '</label>');
            toDatediv.append($toDatewrapDiv);
            LeftSideFields.append($OrgDiv).append(CustomerDiv).append(ResourceDiv).append(FromDatediv).append(toDatediv);
            $CustomerSelected = $('<div class="vas-tis-dropdown-lbl">');
            $ResourceSelected = $('<div class="vas-tis-dropdown-lbl">');
            /*when user change the value of customer it will check whether record exsist or not if exist then will not append on div else it will be 
            appneded to div*/
            $self.vSearchCustomer.fireValueChanged = function () {
                if (CustomerId.indexOf($self.vSearchCustomer.value) == -1) {
                    if ($self.vSearchCustomer.value != null) {
                        $CustomerSelected.append('<div class="vas-selected-lbl">' + $self.vSearchCustomer.ctrl.val() + '<span class="vis vis-cross" data-custId="' + $self.vSearchCustomer.value + '"></span></div>');
                        CustomerDiv.append($CustomerSelected);
                        CustomerId.push($self.vSearchCustomer.value);

                        $CustomerSelected.find('.vis-cross').on('click', function () {
                            var Customer_ID = $(this).attr('data-custId')
                            CustomerId = jQuery.grep(CustomerId, function (value) {
                                return VIS.Utility.Util.getValueOfInt(value) != VIS.Utility.Util.getValueOfInt(Customer_ID)
                            });
                            $(this).parent().remove();
                            if ($CustomerSelected.find('.vis-cross').length == 0) {
                                $CustomerSelected.remove();
                            }
                        });
                    }
                }
                $self.vSearchCustomer.setValue(null);
            };
            /*when user change the value of resource it will check whether record exist or not if exsist then will not append on div else it will be
             appneded to div*/
            $self.vSearchResource.fireValueChanged = function () {
                if (ResourceId.indexOf($self.vSearchResource.value) == -1) {
                    if ($self.vSearchResource.value != null) {
                        $ResourceSelected.append('<div class="vas-selected-lbl">' + $self.vSearchResource.ctrl.val() + '<span class="vis vis-cross" data-resourceid="' + $self.vSearchResource.value + '"></span></div>')
                        ResourceDiv.append($ResourceSelected);
                        ResourceId.push($self.vSearchResource.value);
                        $ResourceSelected.find('.vis-cross').on('click', function () {
                            var Resource_ID = $(this).attr('data-resourceid')
                            ResourceId = jQuery.grep(ResourceId, function (value) {
                                return VIS.Utility.Util.getValueOfInt(value) != VIS.Utility.Util.getValueOfInt(Resource_ID)
                            });
                            $(this).parent().remove();
                            if ($ResourceSelected.find('.vis-cross').length == 0) {
                                $ResourceSelected.remove();
                            }
                        })
                    }
                }
                $self.vSearchResource.setValue(null);
            };
            $self.cmbOrg.fireValueChanged = function () {
                ctx.setContext($self.windowNo, "AD_Org_ID", $self.cmbOrg.getValue());
            }
        }

        /*this function is used to create design of form */
        function loadDesign() {
            //this design will have all the controls like arrow btn,filter btn
            $self.topDiv = $("<div id='topDiv_" + $self.windowNo + "' class='vis-archive-l-s-head vis-frm-ls-top' style='padding: 0;'>" +
                "<div id='btnSpaceDiv_" + $self.windowNo + "' class='vas-spacediv'>" +
                "<button id='btnSpace_" + $self.windowNo + "' class='vis-archive-sb-t-button' ><i class='vis vis-arrow-left'></i></button></div>" +
                "<div id='spnSelect_" + $self.windowNo + "' class='vas-RecordSelection'>" +
                "<div class='vas-SelectDiv'>" +
                "<label id='spnSelect_" + $self.windowNo + "' class='VIS_Pref_Label_Font vas-SpnSelect'>" + VAS.translatedTexts.VAS_RecordSelection + "</label></div>" +
                "<div class='vas-tis-filter dropdown'>" +
                "<span class='vas-tis-filterspn btn d-flex position-relative' type='button' id='vas_tis_dropdownMenu_" + $self.windowNo + "'>" +
                "<i class='fa fa-filter vas-tis-filterIcon'></i>" +
                "</span>" +
                "<div class='vas-tis-filterPopupWrap' id='vas_tis_FilterPopupWrap_" + $self.windowNo + "'>" +
                "</div>" +
                "</div>");

            $self.btnSpaceDiv = $self.topDiv.find("#btnSpaceDiv_" + $self.windowNo);

            $self.LeftsideDiv = $("<div id='sideDiv_" + $self.windowNo + "' class='vis-archive-l-s-content vis-leftsidebarouterwrap px-3'>");
            $self.LeftsideDiv.css("height", "100%");
            $self.SearchBtn = $("<input id='SearchBtn_" + $self.windowNo + "' class='VIS_Pref_btn-2 vas-searchbtn' type='button' value='" + VAS.translatedTexts.Search + "'>");
            $self.bottumDiv = $("<div class='vis-info-btmcnt-wrap vis-p-t-10 vas-BottomBtnDiv'>");
            $self.PreviewBtn = $("<button id='PreviewBtn_" + $self.windowNo + "' class='VIS_Pref_btn-2 mr-2'>" + VAS.translatedTexts.VAS_Preview + "</button>");
            $self.GenerateInvBtn = $("<button id='VAS_GenInvoice_" + $self.windowNo + "' class='VIS_Pref_btn-2'>" + VAS.translatedTexts.VAS_GenInvoice + "</button>");
            $self.LeftsideDiv.append(LeftSideFields).append($self.SearchBtn);
            $self.bottumDiv.append($self.GenerateInvBtn).append($self.PreviewBtn);

            $self.gridSelectDiv = $("<div id='gridSelectDiv_" + $self.windowNo + "' class='vis-frm-grid-outerwrp'>");
            //appended all the design to root
            $self.$root.append($self.topDiv).append($self.LeftsideDiv).append($self.gridSelectDiv).append($self.bottumDiv);
        }

        /* this function is used to load the column and data on grid*/
        function dynInit(data) {

            if ($self.dGrid != null) {
                $self.dGrid.destroy();
                $self.dGrid = null;
            }
            if ($self.arrListColumns.length == 0) {
                $self.arrListColumns.push({ field: "recid", caption: VAS.translatedTexts.VAS_RecordNo, sortable: true, size: '13%', min: 50, hidden: true });
                $self.arrListColumns.push({

                    field: "DocumentNo", caption: VAS.translatedTexts.VAS_TimeRecordDoc, sortable: true, size: '38%', min: 150, hidden: false, render: function (record, index, col_index) {
                        return '<a href="#" class="vas-decoration-style">' + record['DocumentNo'] + '</a>';
                    }, editable: { type: 'text' }
                });
                $self.arrListColumns.push({
                    field: "C_BPartner", caption: VAS.translatedTexts.VAS_Customer, sortable: true, size: '30%', min: 150, hidden: false, render: function (record) {
                        var div;
                        if (record["ImageUrl"] != null) {
                            div = '<div class="vas-tis-gridimg vis-gridImageicon">' +
                                '<img class="vas-businessPartnerImg" alt="' + record["ImageUrl"] + '" src="' + VIS.Application.contextUrl + record["ImageUrl"] + '">' + " " + record["C_BPartner"]
                            '</div>';
                        } else {
                            div = '<div class="vas-tis-gridimg vis-gridImageicon">' +
                                '<div class="vis-app-user-img-wrap"><i class="fa fa-user"></i><img src="" alt="' + VAS.translatedTexts.VAS_CustImage + '"></div>' + " " + record["C_BPartner"]
                            '</div>';
                        }
                        return div;
                    }
                });
                $self.arrListColumns.push({ field: "S_Resource_ID", caption: VAS.translatedTexts.S_Resource_ID, sortable: true, size: '16%', min: 150, hidden: false });
                $self.arrListColumns.push({ field: "M_Product", caption: VAS.translatedTexts.VAS_ProductOrCharge, sortable: true, size: '25%', min: 150, hidden: false });
                $self.arrListColumns.push({
                    field: 'Qty', caption: VAS.translatedTexts.Qty,sortable: true, size: '8%', min: 150, hidden: false, render: function (record, index, col_index) {
                        var val = record["Qty"] + " " + record["UomName"];
                        return val;
                    }
                })
                $self.arrListColumns.push({
                    field: "Price", caption: VAS.translatedTexts.Price, sortable: true, size: '16%', min: 150, hidden: false, style: 'text-align:right;', editable: { type: 'number' }, render: function (record, index, col_index) {
                        var val = record["Price"];
                        precision = record["StdPrecision"];
                        return '<a href="#" class="vas-decoration-style right-align">' + parseFloat(val).toLocaleString(window.navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision }) + '</a>';
                    }
                });
                $self.arrListColumns.push({
                    field: 'Amount', caption: VAS.translatedTexts.VAS_TotalBilableAmount, sortable: true, size: '25%', min: 150, hidden: false, style: 'text-align:right;', render: function (record, index, col_index) {
                        var val = record["Amount"];
                        precision = record["StdPrecision"]
                        return parseFloat(val).toLocaleString(window.navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision }) +  " " + record["ISO_Code"];
                    }

                });
                $self.arrListColumns.push({ field: "C_BPartner_ID", caption: VAS.translatedTexts.C_BPartner_ID, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "VA009_PaymentMethod_ID", caption: VAS.translatedTexts.VA009_PaymentMethod_ID, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "M_PriceList_ID", caption: VAS.translatedTexts.M_PriceList_ID, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "C_BPartner_Location_ID", caption: VAS.translatedTexts.C_BPartner_Location_ID, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "M_Product_ID", caption: VAS.translatedTexts.M_Product_ID, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "C_Charge_ID", caption: VAS.translatedTexts.C_Charge_ID, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "C_Currency_ID", caption: VAS.translatedTexts.C_Currency_ID, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "C_PaymentTerm_ID", caption: VAS.translatedTexts.C_PaymentTerm_ID, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "C_Uom_ID", caption: VAS.translatedTexts.C_Uom_ID, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "LocationName", caption: VAS.translatedTexts.VAS_LocationName, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "recordCount", caption: VAS.translatedTexts.VAS_recordCounted, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "RecordedDate", caption: VAS.translatedTexts.VAS_RecordedDate, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "EstimatedTime", caption: VAS.translatedTexts.VAS_EstimatedTime, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "ProjReq_Name", caption: VAS.translatedTexts.VAS_ProjReq_Name, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "ProjReq_ID", caption: VAS.translatedTexts.VAS_ProjReq_ID, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "PhaseName", caption: VAS.translatedTexts.VAS_PhaseName, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "PriceStd", caption: VAS.translatedTexts.PriceStd, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "PriceLimit", caption: VAS.translatedTexts.PriceLimit, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "StdPrecision", caption: VAS.translatedTexts.StdPrecision, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "EnforcePriceLimit", caption: VAS.translatedTexts.EnforcePriceLimit, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "VA075_WorkOrderOperation_ID", caption: VAS.translatedTexts.VA075_WorkOrderOperation_ID, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "S_TimeExpenseLine_ID", caption: VAS.translatedTexts.S_TimeExpenseLine_ID, sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "ISO_Code", caption: VAS.translatedTexts.VAS_ISO_Code, sortable: true, size: '16%', min: 150, hidden: true });
            }

            w2utils.encodeTags(data);

            $self.dGrid = $($self.gridSelectDiv).w2grid({
                name: "gridGenForm_" + $self.windowNo,
                recordHeight: 40,
                show: { selectColumn: true },
                multiSelect: true,
                columns: $self.arrListColumns,
                records: data,
                scorll: true,
                onSelect: function (event) {
                    if ($self.dGrid.records.length > 0) {
                        ArrayOfGrid(event, $self.dGrid, false);
                    }
                },
                onUnselect: OnUnseletRow,
                onEditField: function (event) {
                    if (event.column == 1) {
                        // Prevent the default behavior of the grid's cell click event
                        event.stopPropagation();
                        event.preventDefault();

                        // Open the popup dialog
                        // Initialize an array to store values
                        var arry = [];

                        // Split the PhaseName string into an array if it's not null
                        if ($self.dGrid.records[event.index].PhaseName != null) {
                            arry = ($self.dGrid.records[event.index].PhaseName).split(',');
                        }
                        //Converting date to local date format
                        var recordingDate = new Date(($self.dGrid.records[event.index].RecordedDate))
                        // Initialize HTML elements for displaying task details
                        var htmlmain = $('<div>');
                        var htmlString = '<div class="vas-taskDescDailog">                                                                       ' +
                            ' <div class="vas-taskDesc_feildWrap mb-2 ">                                                                         ' +
                            '   <div class="vas-taskDesc_feild">                                                                                 ' +
                            '     <span class="vas-taskdescTtl">' + VAS.translatedTexts.VAS_RecordingDate + '</span>                                                            ' +
                            '     <span class="vas-taskdescValue">' + (recordingDate.toLocaleDateString() || '') + '</span>                                                              ' +
                            '   </div>                                                                                                           ' +
                            '   <div class="vas-taskDesc_feild">                                                                                 ' +
                            '     <span class="vas-taskdescTtl text-right">' + VAS.translatedTexts.VAS_RequestOrProject + '</span>                                       ' +
                            '     <span class="vas-taskdescValue text-right">' + ($self.dGrid.records[event.index].ProjReq_Name || '') + '</span>                                    ' +
                            '   </div>                                                                                                           ' +
                            ' </div>                                                                                                             ' +
                            ' <div class="vas-projectTimeline mb-2">                                                                             ' +
                            '   <span class="vas-taskdescTtl">' + VAS.translatedTexts.VAS_RequestOrProject + '</span>                                                    ' +
                            '   <div class="vas-projectTimelineConatainer d-flex align-items-center justify-content-center">                     ' +
                            '     <div id="pipeline" class="pipeline">';

                        /* Project Phase*/
                        if ($self.dGrid.records[event.index].PhaseName != null) {
                            for (var i = 0; i < arry.length; i++) {
                                htmlString += '<div class="stage" id="stage' + i + '">' + arry[i] + '</div>';
                                if (i < arry.length - 1) {
                                    htmlString += '<div class="connector"></div>';
                                }
                            }
                        }
                        /* Summary*/
                        else if ($self.dGrid.records[event.index].Summary != null) {
                            htmlString += '<div>' + ($self.dGrid.records[event.index].PhaseName || '') + '</div>';
                        }

                        htmlString += '</div>' +
                            '   </div>                                                                                                           ' +
                            ' </div>                                                                                                             ' +
                            ' <div class="vas-taskDesc_feildWrap mb-2 ">                                                                         ' +
                            '   <div class="vas-taskDesc_feild">                                                                                 ' +
                            '     <span class="vas-taskdescTtl">' + VAS.translatedTexts.VAS_EstimatedTime + '</span>                                                            ' +
                            '     <span class="vas-taskdescValue">' + ($self.dGrid.records[event.index].EstimatedTime || '') + '</span>                                                                ' +
                            '   </div>                                                                                                           ' +
                            '   <div class="vas-taskDesc_feild">                                                                                 ' +
                            '     <span class="vas-taskdescTtl text-right">' + VAS.translatedTexts.VAS_RecordedTime + '</span>                                                  ' +
                            '     <span class="vas-taskdescValue text-right">' + ($self.dGrid.records[event.index].Qty || '') + '</span>                                                     ' +
                            '   </div>                                                                                                           ' +
                            ' </div>                                                                                                             ' +
                            '</div>';

                        htmlmain.append(htmlString);
                        // Create and configure the dialog for displaying task details
                        var RequestInfoDialog = new VIS.ChildDialog();
                        RequestInfoDialog.setContent(htmlmain);
                        RequestInfoDialog.setTitle(VAS.translatedTexts.VAS_RequestInfoDialog);
                        RequestInfoDialog.setWidth("50%");
                        RequestInfoDialog.setEnableResize(true);
                        RequestInfoDialog.setModal(true);
                        RequestInfoDialog.show();
                        RequestInfoDialog.hideButtons();
                    }
                    else if (event.column == 6) {
                        event.onComplete = function (event) {
                            id = event.recid;
                            $('#grid_gridGenForm_' + $self.windowNo + '_rec_' + id).keydown(function (event) {
                                var isDotSeparator = culture.isDecimalSeparatorDot(window.navigator.language);

                                if (!isDotSeparator && (event.keyCode == 190 || event.keyCode == 110)) {// , separator
                                    return false;
                                }
                                else if (isDotSeparator && event.keyCode == 188) { // . separator
                                    return false;
                                }
                                if (event.target.value.contains(".") && (event.which == 110 || event.which == 190 || event.which == 188)) {
                                    this.value = this.value.replace('.', '');
                                }
                                if (event.target.value.contains(",") && (event.which == 110 || event.which == 190 || event.which == 188)) {
                                    this.value = this.value.replace(',', '');
                                }
                                if (event.keyCode != 8 && event.keyCode != 9 && (event.keyCode < 37 || event.keyCode > 40) &&
                                    (event.keyCode < 48 || event.keyCode > 57) && (event.keyCode < 96 || event.keyCode > 105)
                                    && event.keyCode != 109 && event.keyCode != 189 && event.keyCode != 110
                                    && event.keyCode != 144 && event.keyCode != 188 && event.keyCode != 190) {
                                    return false;
                                }
                            });
                        };

                    }
                },
                onChange: function (event) {
                    if ($self.dGrid.getChanges(event.recid) != undefined) {
                        if (event.column == 6) {
                            if (event.value_new == "") {
                                event.value_new = 0;
                            }
                            else {
                                //Changed New value into number
                                event.value_new = format.GetConvertedNumber(event.value_new, dotFormatter);//Converted new value because the new value is in string
                                event.value_new = event.value_new.toLocaleString(window.navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision });
                                event.value_new = format.GetConvertedNumber(event.value_new, dotFormatter);
                            }
                            /*Applied Check if Enforce Price limit checkbox true on Price list and user enter value less than price limit it will show error*/
                            if ($self.dGrid.records[event.index]['EnforcePriceLimit'] == "Y" && parseFloat(event.value_new) < $self.dGrid.records[event.index]['PriceLimit']) {
                                VIS.ADialog.info('VAS_PriceLimitExceeds');
                                event.value_new = event.value_original;
                                $self.dGrid.getChanges(event.recid).Price = event.value_original
                                $self.dGrid.records[event.index]['Price'] = event.value_original
                                $self.dGrid.refreshCell(event.recid, "Price");
                                return;
                            }
                            $self.dGrid.records[event.index]['Price'] = event.value_new
                            $self.dGrid.records[event.index]['Amount'] = event.value_new * $self.dGrid.records[event.index]['Qty'];
                            $self.dGrid.refreshCell(event.recid, "Amount");
                            $self.dGrid.refreshCell(event.recid, "Price");
                        }
                    }
                }

            });
            $self.gridSelectDiv.find('#grid_gridGenForm_' + ($self.windowNo) + '_records').off("scroll", enablingscrollEvent);
            $self.gridSelectDiv.find('#grid_gridGenForm_' + ($self.windowNo) + '_records').on("scroll", enablingscrollEvent);
        }
        /**
        * this fucntion is called when user unselects records
        * @param {any} e
        */
        function OnUnseletRow(e) {
            ArrayOfGrid(e, $self.dGrid, true);
        }
        /**
        * this function used to append busy indicator to root
        */
        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap" style="visibility: hidden;"></div>');
            $bsyDiv.append($('<div class="vis-busyindicatorinnerwrap"><i class="vis-busyindicatordiv"></i></div>'));
            $self.setBusy(false);
            $self.$root.append($bsyDiv);
        };
        /**
        * this function used to enable the value of busy indicator by passing the value true or false
        */
        this.setBusy = function (isBusy) {
            if (isBusy) {
                $bsyDiv[0].style.visibility = "visible";
            }
            else {
                $bsyDiv[0].style.visibility = "hidden";
            }
        }
        /**
         * this fucntion is used to store data on grids on selection and unselection of records
         * @param {any} event
         * @param {any} dGrid
         * @param {any} IsRecordUnselected
         */
        function ArrayOfGrid(event, dGrid, IsRecordUnselected) {
            // Check if all records are selected
            if (event.recid == undefined) {
                // Clear the arrays
                gridDataArray = [];
                LeftGridData = [];
                // Loop through all records in the grid
                for (var i = 0; i < dGrid.records.length; i++) {
                    // Check if records are unselected
                    if (IsRecordUnselected) {
                        // Reset the arrays if records are unselected
                        gridDataArray = [];
                        LeftGridData = [];
                    } else {
                        // Otherwise, populate gridDataArray with all records
                        gridDataArray.push(dGrid.records[i]);
                    }
                }
                // Add data for preview based on the selection
                AddForPreviewDataOnAllSelection(gridDataArray, true);
            } else {
                // If a record is unselected
                if (IsRecordUnselected) {
                    // Remove the selected record from gridDataArray
                    AddedDataForPreviewOnSelection(event, dGrid, false);
                    gridDataArray = jQuery.grep(gridDataArray, function (value) {
                        // Filter out the unselected record
                        return VIS.Utility.Util.getValueOfInt(value.recid) != VIS.Utility.Util.getValueOfInt(event.recid)
                    });
                } else {
                    // Add the selected record to gridDataArray
                    gridDataArray.push(dGrid.records[event.index]);
                    // Add data for preview with the selected record
                    AddedDataForPreviewOnSelection(event, dGrid, true);
                }
            }


        }

        //this fucntion is used to get remaining columns  id
        var GetColumnID = function (ColumnData) {
            ColumnIds = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS_TimeSheetInvoices/GetColumnID", { "ColumnData": JSON.stringify(ColumnData) }, null);
        }

        //This function is called when grid is scrolled to maintain paging
        function enablingscrollEvent() {

            if ($(this).scrollTop() + $(this).innerHeight() >= this.scrollHeight) {
                if (pageNo < gridPgnoInvoice) {
                    $self.setBusy(true);
                    pageNo++;
                    LoadTimeSheetData(pageNo, pageSize,true);
                }
            }
        }


        // Function to add data for preview based on all selections
        function AddForPreviewDataOnAllSelection(gridDataArray, shouldAdd) {
            // If gridDataArray is empty, reset LeftGridData and return
            if (gridDataArray.length === 0) {
                LeftGridData = [];
                return;
            }

            // Loop through each record in gridDataArray
            for (var i = 0; i < gridDataArray.length; i++) {
                var record = gridDataArray[i];
                var isUnique = true;

                // Loop through each record in LeftGridData
                for (var j = 0; j < LeftGridData.length; j++) {
                    // Check if the record already exists in LeftGridData based on specific conditions
                    if (record.C_BPartner_ID === LeftGridData[j].C_BPartner_ID &&
                        record.C_BPartner_Location_ID === LeftGridData[j].C_BPartner_Location_ID &&
                        record.VA009_PaymentMethod_ID === LeftGridData[j].VA009_PaymentMethod_ID &&
                        record.M_PriceList_ID === LeftGridData[j].M_PriceList_ID) {
                        // If the record exists, increment the record count and mark it as not unique
                        LeftGridData[j].recordCount = LeftGridData[j].recordCount + 1;
                        isUnique = false;
                        break;
                    }
                }

                // If the record is unique and should be added
                if (isUnique && shouldAdd) {
                    // Set the record count to 1 and add it to LeftGridData
                    record.recordCount = 1;
                    LeftGridData.push(record);
                }
            }
        }

        // Function called when a user selects or unselects a single record
        function AddedDataForPreviewOnSelection(event, dGrid, shouldAdd) {
            var isUnique = true;

            // Loop through each record in LeftGridData
            for (var j = 0; j < LeftGridData.length; j++) {
                // Check if the record matches specific conditions and if it should be added
                if (shouldAdd && (dGrid.records[event.index].C_BPartner_ID === LeftGridData[j].C_BPartner_ID &&
                    dGrid.records[event.index].C_BPartner_Location_ID === LeftGridData[j].C_BPartner_Location_ID &&
                    dGrid.records[event.index].VA009_PaymentMethod_ID === LeftGridData[j].VA009_PaymentMethod_ID &&
                    dGrid.records[event.index].M_PriceList_ID === LeftGridData[j].M_PriceList_ID)) {
                    // If the record matches and should be added, increment the record count and mark it as not unique
                    LeftGridData[j].recordCount = LeftGridData[j].recordCount + 1;
                    isUnique = false;
                    break;
                }
                // Check if the record matches specific conditions and if it should be removed
                else if (!shouldAdd && (dGrid.records[event.index].C_BPartner_ID === LeftGridData[j].C_BPartner_ID &&
                    dGrid.records[event.index].C_BPartner_Location_ID === LeftGridData[j].C_BPartner_Location_ID &&
                    dGrid.records[event.index].VA009_PaymentMethod_ID === LeftGridData[j].VA009_PaymentMethod_ID &&
                    dGrid.records[event.index].M_PriceList_ID === LeftGridData[j].M_PriceList_ID)) {
                    // If the record matches and should be removed, decrement the record count
                    LeftGridData[j].recordCount = LeftGridData[j].recordCount - 1;
                    // If the record count becomes zero or less, remove the record from LeftGridData
                    if (LeftGridData[j].recordCount <= 0) {
                        LeftGridData = jQuery.grep(LeftGridData, function (record) {
                            return record.recordCount > 0;
                        });
                    }
                }
            }

            // If the record is unique and should be added
            if (isUnique && shouldAdd) {
                // Set the record count to 1 and add it to LeftGridData
                dGrid.records[event.index].recordCount = 1;
                LeftGridData.push(dGrid.records[event.index]);
            }
        }



        /**
        * this function is responsible for loading data into grid
        * @param {any} pageNo
        * @param {any} pageSize
        * @param {any} IsOnScroll
        */
        function LoadTimeSheetData(pageNo, pageSize,IsOnScroll) {
            var data = [];
            gridDataArray = [];
            LeftGridData = [];
            FilteredData = [];
            if (VIS.Utility.Util.getValueOfInt(AD_Org_ID) == 0) {
                VIS.ADialog.info('VAS_PlzSelectOrganization');
                return;
            }
            var AD_Client_ID = VIS.Env.getCtx().getAD_Client_ID();
            $self.setBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_TimeSheetInvoices/LoadGridData",
                type: 'POST',
                data: {
                    AD_Client_ID: VIS.Utility.Util.getValueOfInt(AD_Client_ID),
                    AD_Org_ID: VIS.Utility.Util.getValueOfInt(AD_Org_ID),
                    C_BPartner_ID: CustomerId.toString(),
                    S_Resource_ID: ResourceId.toString(),
                    TimExpenSeDoc: TimeExpenseId.toString(),
                    C_Project_ID: ProjectId.toString(),
                    R_Request_ID: RequestId.toString(),
                    C_Task_ID: TaskId.toString(),
                    FromDate: $FromDate.getValue(),
                    toDate: $ToDate.getValue(),
                    pageNo: pageNo,
                    pageSize: pageSize
                },
                success: function (res) {
                    var gridDataResult = JSON.parse(res);
                    if (gridDataResult && gridDataResult.length > 0) {
                        var count = 0;
                        if (!IsOnScroll) {
                            count = 1;
                        }
                        else {
                            count = $self.dGrid.records.length + 1
                        }
                        
                        if (pageNo == 1 && gridDataResult.length > 0) {
                            TotalRecords = gridDataResult[0].countRecords
                            gridPgnoInvoice = Math.ceil(TotalRecords / pageSize);
                        }
                        for (var i = 0; i < gridDataResult.length; i++) {

                            var line = {};
                            line['DocumentNo'] = gridDataResult[i].DocumentNo;
                                line['C_BPartner'] = gridDataResult[i].CustomerName;
                                line['S_Resource_ID'] = gridDataResult[i].ResourceName;
                                line['M_Product'] = gridDataResult[i].ProductName;
                                line['Qty'] = gridDataResult[i].Qty;
                                line['Price'] = gridDataResult[i].PriceList;
                                line['PriceStd'] = gridDataResult[i].PriceStd;
                                line['PriceLimit'] = gridDataResult[i].PriceLimit;
                                line['Amount'] = gridDataResult[i].PriceList * gridDataResult[i].Qty;
                                line['C_BPartner_ID'] = gridDataResult[i].CustomerId;
                                line['StdPrecision'] = gridDataResult[i].stdPrecision;
                                line['VA009_PaymentMethod_ID'] = gridDataResult[i].VA009_PaymentMethod_ID;
                                line['M_PriceList_ID'] = gridDataResult[i].M_PriceList_ID;
                                line['C_BPartner_Location_ID'] = gridDataResult[i].C_Location_ID;
                                line['M_Product_ID'] = gridDataResult[i].M_Product_ID;
                                line['C_Charge_ID'] = gridDataResult[i].C_Charge_ID;
                                line['C_Uom_ID'] = gridDataResult[i].C_Uom_ID;
                                line['C_PaymentTerm_ID'] = gridDataResult[i].C_PaymentTerm_ID;
                                line['C_Currency_ID'] = gridDataResult[i].C_Currency_ID;
                                line['UomName'] = gridDataResult[i].UomName;
                                line['ISO_Code'] = gridDataResult[i].ISO_Code;
                                line['ImageUrl'] = gridDataResult[i].ImageUrl;
                                line['LocationName'] = gridDataResult[i].LocationName;
                                line['ProjReq_ID'] = gridDataResult[i].ProjReq_ID;
                                line['ProjReq_Name'] = gridDataResult[i].ProjReq_Name;
                                line['RecordedDate'] = gridDataResult[i].RecordedDate;
                                line['EstimatedTime'] = gridDataResult[i].EstimatedTime;
                            line['EnforcePriceLimit'] = gridDataResult[i].EnforcePriceLimit;
                            line['S_TimeExpenseLine_ID'] = gridDataResult[i].S_TimeExpenseLine_ID;
                            line['VA075_WorkOrderOperation_ID'] = gridDataResult[i].VA075_WorkOrderOperation_ID;
                            if (gridDataResult[i].PhaseInfo != null) {
                                line['PhaseName'] = VAS.translatedTexts.VAS_Start;
                                for (var j = 0; j < gridDataResult[i].PhaseInfo.length; j++) {
                                    line['PhaseName'] = line['PhaseName'] + "," + (gridDataResult[i].PhaseInfo[j].PhaseName);
                                }
                            }
                            else if (gridDataResult[i].RequestSummary != null) {
                                line['PhaseName'] = gridDataResult[i].RequestSummary;
                            }

                            line['recordCount'] = 0;
                            line['recid'] = count;
                            count++;
                            data.push(line);
                        }

                    }
                    else {
                        VIS.ADialog.info("NoDataFound");
                    }
                    //Handled condition for scroll
                    if (!IsOnScroll) {
                        dynInit(data);
                    }
                    else {
                        $self.dGrid.add(data);
                    }
                    $self.setBusy(false);
                },
                error: function (e) {
                },
            });

            return data;
        }


        /**
        * this grid is used for loading preview data
        * @param {any} data
        */
        function PreviewDyinit(data) {
            $self.PreviewColumns = [];
            if ($self.dGridPreview != null) {
                $self.dGridPreview.destroy();
                $self.dGridPreview = null;
            }
            if ($self.PreviewColumns.length == 0) {
                $self.PreviewColumns.push({ field: "M_Product", caption: VAS.translatedTexts.VAS_ProductOrCharge, sortable: true, size: '16%', min: 150, hidden: false });
                $self.PreviewColumns.push({
                    field: 'Price', caption: VAS.translatedTexts.Price, size: '16%', sortable: true, size: '16%', min: 150, hidden: false, render: function (record, index, col_index) {
                        var val = record["Price"];
                        precision = record["StdPrecision"]
                        return parseFloat(val).toLocaleString(window.navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision });
                    }
                });
                $self.PreviewColumns.push({ field: "UomName", caption: VAS.translatedTexts.VAS_UomName, sortable: true, size: '16%', min: 150, hidden: false });
                $self.PreviewColumns.push({
                    field: 'Qty', caption: VAS.translatedTexts.Qty, size: '16%', sortable: true, size: '16%', min: 150, hidden: false, render: function (record, index, col_index) {
                        var val = record["Qty"] + " " + record["UomName"];
                        return val;
                    }
                })
                $self.PreviewColumns.push({
                    field: 'Amount', caption: VAS.translatedTexts.VAS_TotalBilableAmount, size: '16%', sortable: true, size: '16%', min: 150, hidden: false, render: function (record, index, col_index) {
                        SubTotal = SubTotal + record["Amount"];
                        var val = record["Amount"];
                        precision = record["StdPrecision"]
                        return parseFloat(val).toLocaleString(window.navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision }) + " " + record["ISO_Code"];
                    }
                })
            }
            w2utils.encodeTags(data);
            $self.dGridPreview = $($self.PreviewGridDiv).w2grid({
                name: "GridPreview_" + $self.windowNo,
                recordHeight: 29,
                columns: $self.PreviewColumns,
                records: data
            });
        }

        /**
        * this function is used to show preview of records selected by user on clcik of preview button
        * @param {any} gridDataArray
        */
        function LoadPreviewDialog(gridDataArray) {
            // Create HTML elements for the preview dialog layout
            var PreviewWrap = $('<div class="vas-tis-arInvViewer d-flex">');
            var PreviLeftWrap = $('<div class= "vas-tis-arInvLeftSide"><div class="vas-tis-arInvListGroup">');
            var rightPreviewWrap = $('<div class="vas-tis-arInvRightSide">');
            var PreviewMainDiv = $("<div class='vas-poptis-content vis-formouterwrpdiv'>");

            // Loop through LeftGridData to populate the left side of the dialog
            for (var i = 0; i < LeftGridData.length; i++) {
                PreviLeftWrap.append(
                    '<div class="vas-tis-arInvlistItem mb-2" id="C_BpartnerListDiv_' + $self.windowNo + '">' +
                    '<div class="vas-tis-arInvoiceDetails px-2">' +
                    '<span class="vas-tis-arInvDetailsDesc" id="C_BPartnerDiv_' + $self.windowNo + '" data-bpid="' + LeftGridData[i].C_BPartner_ID + '" data-paymethodid="' + LeftGridData[i].VA009_PaymentMethod_ID + '" data-pricelistid="' + LeftGridData[i].M_PriceList_ID + '"> ' + LeftGridData[i].C_BPartner + '</span > ' +
                    '<span class="vas-tis-arInvDetailsDesc">' + VAS.translatedTexts.VAS_RecordCount + ":" + LeftGridData[i].recordCount + '</span>' +
                    '<span class="vas-tis-arInvDetailsDesLoc" id="C_BPartnerLocationDiv_' + $self.windowNo + '" data-bplocationid="' + LeftGridData[i].C_BPartner_Location_ID + '">' + LeftGridData[i].LocationName + '</span>' +
                    '</div>');
            }

            // Create HTML elements for the right side of the dialog
            var PreviewId = "gridSelectDiv_" + $self.windowNo;
            var headTagForBPartner = $('<h4 id="HeadBPTag_' + $self.windowNo + '">' + LeftGridData[0].C_BPartner + '</h4>')
            $self.PreviewGridDiv = $("<div id='" + PreviewId + "' class='vas-pointdiv'>");
            rightPreviewWrap.append(headTagForBPartner).append($self.PreviewGridDiv);

            // Combine all elements and append them to the main dialog div
            PreviewWrap.append(PreviLeftWrap).append(rightPreviewWrap);
            PreviewMainDiv.append(PreviewWrap);

            // Create and configure the preview dialog
            var PreviewDialog = new VIS.ChildDialog();
            PreviewDialog.setContent(PreviewMainDiv);
            PreviewDialog.setTitle(VAS.translatedTexts.VAS_PreviewDialog);
            PreviewDialog.setWidth("80%");
            PreviewDialog.setHeight(517);
            PreviewDialog.setEnableResize(true);
            PreviewDialog.setModal(true);
            PreviewDialog.show();
            PreviewDialog.hideButtons();

            // Handle click events on items in the left side of the dialog
            var BPartnerInfoClickDiv = PreviLeftWrap.find('.vas-tis-arInvlistItem');
            BPartnerInfoClickDiv.first().addClass('vas-tis-arInvActive');
            var locid = BPartnerInfoClickDiv.find("#C_BPartnerLocationDiv_" + $self.windowNo).attr('data-bplocationid');
            var bpid = BPartnerInfoClickDiv.find("#C_BPartnerDiv_" + $self.windowNo).attr('data-bpid');
            var priceListId = BPartnerInfoClickDiv.find("#C_BPartnerDiv_" + $self.windowNo).attr('data-pricelistid');
            var payMethodId = BPartnerInfoClickDiv.find("#C_BPartnerDiv_" + $self.windowNo).attr('data-paymethodid');
            FilteredData = jQuery.grep(gridDataArray, function (value) {
                return VIS.Utility.Util.getValueOfInt(value.C_BPartner_ID) === VIS.Utility.Util.getValueOfInt(bpid)
                    && VIS.Utility.Util.getValueOfInt(value.C_BPartner_Location_ID) === VIS.Utility.Util.getValueOfInt(locid)
                    && VIS.Utility.Util.getValueOfInt(value.VA009_PaymentMethod_ID) === VIS.Utility.Util.getValueOfInt(payMethodId)
                    && VIS.Utility.Util.getValueOfInt(value.M_PriceList_ID) === VIS.Utility.Util.getValueOfInt(priceListId)
            });

            // Initialize the preview grid with filtered data
            PreviewDyinit(FilteredData);

            // Append subtotal span to the right side of the dialog
            var SubTotalSpan = $('<p class="text-right pt-2" style="clear:both;" id="SubTotalSpan_' + $self.windowNo + '">' + VAS.translatedTexts.VAS_SubTotal + ": " + SubTotal + '<p>');
            rightPreviewWrap.append(SubTotalSpan);

            // Handle click events on items in the left side of the dialog
            PreviLeftWrap.find('.vas-tis-arInvlistItem').on('click', function () {
                locid = $(this).find('.vas-tis-arInvDetailsDesLoc').attr('data-bplocationid');
                bpid = $(this).find('.vas-tis-arInvDetailsDesc').attr('data-bpid');
                priceListId = $(this).find('.vas-tis-arInvDetailsDesc').attr('data-pricelistid');
                payMethodId = $(this).find('.vas-tis-arInvDetailsDesc').attr('data-paymethodid');
                PreviLeftWrap.find('.vas-tis-arInvlistItem').removeClass('vas-tis-arInvActive');
                $(this).addClass('vas-tis-arInvActive');
                FilteredData = jQuery.grep(gridDataArray, function (value) {
                    return VIS.Utility.Util.getValueOfInt(value.C_BPartner_ID) === VIS.Utility.Util.getValueOfInt(bpid)
                        && VIS.Utility.Util.getValueOfInt(value.C_BPartner_Location_ID) === VIS.Utility.Util.getValueOfInt(locid)
                        && VIS.Utility.Util.getValueOfInt(value.VA009_PaymentMethod_ID) === VIS.Utility.Util.getValueOfInt(payMethodId)
                        && VIS.Utility.Util.getValueOfInt(value.M_PriceList_ID) === VIS.Utility.Util.getValueOfInt(priceListId)
                });

                // Initialize the preview grid with filtered data
                PreviewDyinit(FilteredData);
                rightPreviewWrap.find("#HeadBPTag_" + $self.windowNo).text(FilteredData[0].C_BPartner);
                rightPreviewWrap.find("#SubTotalSpan_" + $self.windowNo).text(VAS.translatedTexts.VAS_SubTotal + ": " + SubTotal)
                SubTotal = 0;
            });

            SubTotal = 0;
        }

        /**
        * this function is responsible for generating invoice
        * @param {any} gridDataArray
        */
        function GenerateInvoice(gridDataArray) {
            if (gridDataArray.length == 0) {
                VIS.ADialog.info('VAS_PlzSelectRecord');
                return;
            }
            var AD_Client_ID = VIS.Env.getCtx().getAD_Client_ID();
            $self.setBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + "VAS_TimeSheetInvoices/GenerateInvoice",
                type: 'POST',
                data: {
                    DataTobeInvoice: JSON.stringify(gridDataArray),
                    AD_Client_ID: VIS.Utility.Util.getValueOfInt(AD_Client_ID),
                    AD_Org_ID: VIS.Utility.Util.getValueOfInt(AD_Org_ID)
                },
                success: function (result) {
                    if (result != null) {

                        VIS.ADialog.info("", null, JSON.parse(result), null);
                        //Cleared the value of grid after generating the invoice
                        dynInit(null);
                        clearVariables();
                        clearControls();
                        removeControlsSelectedDiv()
                    }
                    else {
                        VIS.ADialog.info("VAS_InvoiceNotGenerated");
                    }

                    $self.setBusy(false);
                },
                error: function (e) {
                    VIS.ADialog.info("VAS_InvoiceNotGenerated");
                    $self.setBusy(false);
                },
            });

        }
        /*this function display the filter popup on click of filter button*/
        function FilterDisplay() {
            var $FilterDiv = $self.$root.find("#vas_tis_FilterPopupWrap_" + $self.windowNo);
            var $FilterHeader = $(
                '  <div class="vas-filter-flyout">                                                              ' +
                '    <div class="vas-flyout-header">                                                            ' +
                '      <h1>' + VAS.translatedTexts.VAS_Filter + '</h1><span class="vis vis-cross"></span> ' +
                '    </div>                                                                                     ' +
                '    <div class="vas-from-row">');
            //Created the time and expense control 
            $TimeAndExpenseSelected = $('<div class="vas-tis-dropdown-lbl d-flex flex-wrap">');
            $TaskSelected = $('<div class="vas-tis-dropdown-lbl">');
            $ProjectSelected = $('<div class="vas-tis-dropdown-lbl">');
            $RequestSelected = $('<div class="vas-tis-dropdown-lbl">');
            TimeDiv = $('<div class="VAS-Time">');
            $TimeAndExpenseDiv = $('<div class="input-group vis-input-wrap">');
            var TimeAndExpenseLookUp = VIS.MLookupFactory.getMLookUp(VIS.Env.getCtx(), $self.windowNo, ColumnIds.S_TimeExpense_ID, VIS.DisplayType.Search);
            $self.vSearchTimeAndExpense = new VIS.Controls.VTextBoxButton("S_TimeExpense_ID", true, false, true, VIS.DisplayType.Search, TimeAndExpenseLookUp);
            var $TimeAndExpenseControlWrap = $('<div class="vis-control-wrap">');
            var $TimeAndExpenseButtonWrap = $('<div class="input-group-append">');
            $TimeAndExpenseDiv.append($TimeAndExpenseControlWrap);
            $TimeAndExpenseControlWrap.append($self.vSearchTimeAndExpense.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VAS.translatedTexts.S_TimeExpense_ID + '</label>');
            $TimeAndExpenseDiv.append($TimeAndExpenseControlWrap);
            $TimeAndExpenseButtonWrap.append($self.vSearchTimeAndExpense.getBtn(0));
            $TimeAndExpenseDiv.append($TimeAndExpenseButtonWrap);
            TimeDiv.append($TimeAndExpenseDiv);
            // created the Project control 
            ProjectDiv = $('<div class="VAS-projectdiv">');
            $ProjectDiv = $('<div class="input-group vis-input-wrap">');
            var validationcode = "C_Project.AD_Org_ID IN(0,@AD_Org_ID@) AND C_Project.IsActive='Y' AND C_Project.IsOpportunity='N' AND C_Project.IsCampaign='N'";
            var ProjectLookUp = VIS.MLookupFactory.getMLookUp(VIS.Env.getCtx(), $self.windowNo, ColumnIds.C_ProjectRef_ID, VIS.DisplayType.Search, "C_ProjectRef_ID", 0, false, validationcode);
            $self.vSearchProject = new VIS.Controls.VTextBoxButton("C_Project_ID", true, false, true, VIS.DisplayType.Search, ProjectLookUp);
            var $ProjectControlWrap = $('<div class="vis-control-wrap">');
            var $ProjectButtonWrap = $('<div class="input-group-append">');
            //Set the info window As project
            $self.vSearchProject.setCustomInfo('Project');
            $ProjectDiv.append($ProjectControlWrap);
            $ProjectControlWrap.append($self.vSearchProject.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VAS.translatedTexts.C_Project_ID + '</label>');
            $ProjectDiv.append($ProjectButtonWrap);
            $ProjectButtonWrap.append($self.vSearchProject.getBtn(0));
            $ProjectDiv.append($ProjectButtonWrap);
            ProjectDiv.append($ProjectDiv);
            // created the Request control 
            RequestDiv = $('<div class="VAS-RequestDiv">');
            $RequestDiv = $('<div class="input-group vis-input-wrap">');
            var RequestLookUp = VIS.MLookupFactory.getMLookUp(VIS.Env.getCtx(), $self.windowNo, ColumnIds.R_Request_ID, VIS.DisplayType.Search);
            $self.vSearchRequest = new VIS.Controls.VTextBoxButton("R_Request_ID", true, false, true, VIS.DisplayType.Search, RequestLookUp);
            var $RequestControlWrap = $('<div class="vis-control-wrap">');
            var $RequestButtonWrap = $('<div class="input-group-append">');
            $RequestDiv.append($RequestControlWrap);
            $RequestControlWrap.append($self.vSearchRequest.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VAS.translatedTexts.R_Request_ID + '</label>');
            $RequestDiv.append($RequestButtonWrap);
            $RequestButtonWrap.append($self.vSearchRequest.getBtn(0));
            $RequestDiv.append($RequestButtonWrap);
            RequestDiv.append($RequestDiv);

            if (ModulePrefix["VA075_"]) {
                TaskDiv = $('<div class="VAS-TaskDiv">');
                $TaskDiv = $('<div class="input-group vis-input-wrap">');
                var TaskLookUp = VIS.MLookupFactory.getMLookUp(VIS.Env.getCtx(), $self.windowNo, ColumnIds.VA075_Task_ID, VIS.DisplayType.Search);
                $self.vSearchTask = new VIS.Controls.VTextBoxButton("VA075_Task_ID", true, false, true, VIS.DisplayType.Search, TaskLookUp);
                var $TaskControlWrap = $('<div class="vis-control-wrap">');
                var $TaskButtonWrap = $('<div class="input-group-append">');
                $TaskDiv.append($TaskControlWrap);
                $TaskControlWrap.append($self.vSearchTask.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VAS.translatedTexts.VA075_Task_ID + '</label>');
                $TaskDiv.append($TaskControlWrap);
                $TaskButtonWrap.append($self.vSearchTask.getBtn(0));
                $TaskDiv.append($TaskButtonWrap);
                TaskDiv.append($TaskDiv);

                $FilterHeader.append(TimeDiv).append(TaskDiv).append(ProjectDiv).append(RequestDiv);
            }
            else {
                $FilterHeader.append(TimeDiv).append(ProjectDiv).append(RequestDiv);
            }

            var ApplyBtn = $('<div class="vas-flyout-footer">' +
                '<button class="VIS_Pref_btn-2 w-100 mt-0" id="VAS_Apply_' + $self.windowNo + '">' + VAS.translatedTexts.VAS_Apply + '</button>' +
                '</div>'
            );
            $FilterHeader.append(ApplyBtn);
            var $ApplyButton = ApplyBtn.find("#VAS_Apply_" + $self.windowNo);

            $FilterDiv.append($FilterHeader);
            //On click of apply button The data will be filtered according to selected filters
            $ApplyButton.on("click", function () {
                AD_Org_ID = $self.cmbOrg.getControl().find('option:selected').val();
                gridPgnoInvoice = 1; pageNo = 1;
                LoadTimeSheetData(pageNo, pageSize,false);
            });

            $self.vSearchTimeAndExpense.fireValueChanged = function () {
                if (TimeExpenseId.indexOf($self.vSearchTimeAndExpense.value) == -1) {
                    if ($self.vSearchTimeAndExpense.value != null) {
                        $TimeAndExpenseSelected.append('<div class="vas-selected-lbl">' + $self.vSearchTimeAndExpense.ctrl.val() + '<span class="vis vis-cross" data-timeandexpenceid="' + $self.vSearchTimeAndExpense.value + '"></span></div>');
                        TimeDiv.append($TimeAndExpenseSelected);
                        TimeExpenseId.push($self.vSearchTimeAndExpense.value);
                        $TimeAndExpenseSelected.find('.vis-cross').on('click', function () {
                            var TimeAndExpense_ID = $(this).attr('data-timeandexpenceid')
                            TimeExpenseId = jQuery.grep(TimeExpenseId, function (value) {
                                return VIS.Utility.Util.getValueOfInt(value) != VIS.Utility.Util.getValueOfInt(TimeAndExpense_ID);
                            });
                            $(this).parent().remove();
                            if ($TimeAndExpenseSelected.find('.vis-cross').length == 0) {
                                $TimeAndExpenseSelected.remove();
                            }
                        });
                    }
                }
                $self.vSearchTimeAndExpense.setValue(null);
            }
            $self.vSearchTask.fireValueChanged = function () {
                if (TaskId.indexOf($self.vSearchTask.value) == -1) {
                    if ($self.vSearchTask.value != null) {
                        $TaskSelected.append('<div class="vas-selected-lbl">' + $self.vSearchTask.ctrl.val() + '<span class="vis vis-cross" data-taskid="' + $self.vSearchTask.value + '"></span></div>');
                        TaskDiv.append($TaskSelected);
                        TaskId.push($self.vSearchTask.value);
                        $TaskSelected.find('.vis-cross').on('click', function () {
                            var Task_ID = $(this).attr('data-taskid')
                            TaskId = jQuery.grep(TaskId, function (value) {
                                return VIS.Utility.Util.getValueOfInt(value) != VIS.Utility.Util.getValueOfInt(Task_ID);
                            });
                            $(this).parent().remove();
                            if ($TaskSelected.find('.vis-cross').length == 0) {
                                $TaskSelected.remove();
                            }
                        });
                    }
                }
                $self.vSearchTask.setValue(null);
            }
            $self.vSearchProject.fireValueChanged = function () {
                if (ProjectId.indexOf($self.vSearchProject.value) == -1) {
                    if ($self.vSearchProject.value != null) {
                        $ProjectSelected.append('<div class="vas-selected-lbl">' + $self.vSearchProject.ctrl.val() + '<span class="vis vis-cross" data-projectid="' + $self.vSearchProject.value + '"></span></div>')
                        ProjectDiv.append($ProjectSelected);
                        ProjectId.push($self.vSearchProject.value);
                        $ProjectSelected.find('.vis-cross').on('click', function () {
                            var Project_ID = $(this).attr('data-projectid')
                            ProjectId = jQuery.grep(ProjectId, function (value) {
                                return VIS.Utility.Util.getValueOfInt(value) != VIS.Utility.Util.getValueOfInt(Project_ID);
                            });
                            $(this).parent().remove();
                            if ($ProjectSelected.find('.vis-cross').length == 0) {
                                $ProjectSelected.remove();
                            }
                        });
                    }
                }
                $self.vSearchProject.setValue(null);
            }
            $self.vSearchRequest.fireValueChanged = function () {
                if (RequestId.indexOf($self.vSearchRequest.value) == -1) {
                    if ($self.vSearchRequest.value != null) {
                        $RequestSelected.append('<div class="vas-selected-lbl">' + $self.vSearchRequest.ctrl.val() + '<span class="vis vis-cross" data-requestid="' + $self.vSearchRequest.value + '"></span></div>')
                        RequestDiv.append($RequestSelected);
                        RequestId.push($self.vSearchRequest.value);
                        $RequestSelected.find('.vis-cross').on('click', function () {
                            var Request_ID = $(this).attr('data-requestid')
                            RequestId = jQuery.grep(RequestId, function (value) {
                                return VIS.Utility.Util.getValueOfInt(value) != VIS.Utility.Util.getValueOfInt(Request_ID);
                            });
                            $(this).parent().remove();
                            if ($RequestSelected.find('.vis-cross').length == 0) {
                                $RequestSelected.remove();
                            }
                        });
                    }
                }
                $self.vSearchRequest.setValue(null);
            }

            $FilterHeader.find('.vis-cross').on('click', function () {
                $FilterHeader.remove();
                IsFilterBtnClicked = false;
                TaskId = [];
                TimeExpenseId = [];
                RequestId = [];
                ProjectId = [];
            });

        }
        //This function is used to clear the controls on generating the invoice
        function clearControls() {
            $self.cmbOrg.setValue(null);
            $FromDate.setValue(null);
            $ToDate.setValue(null);
        }
        //This function is used to initialize the design onload and handling various click events
        this.Initialize = function () {
            initializeComponent();
            this.btnToggel = this.$root.find("#btnSpace_" + $self.windowNo);
            this.spnRecordSelection = this.$root.find("#spnSelect_" + $self.windowNo);
            this.PreviewBtn = this.$root.find("#PreviewBtn_" + $self.windowNo);
            var btnFilter = this.$root.find("#vas_tis_dropdownMenu_" + $self.windowNo);
            //On click of filter button added a popup to filter the data
            if (btnFilter != null) {
                btnFilter.on("click", function () {
                    if (!IsFilterBtnClicked) {
                        FilterDisplay();
                    }
                    IsFilterBtnClicked = true;

                });
            }
            //On clcik of search button the data will beloaded on grid
            if (this.SearchBtn != null)
                this.SearchBtn.on(VIS.Events.onTouchStartOrClick, function () {
                    if ($FromDate.getValue() == null || $ToDate.getValue() == null) {
                        VIS.ADialog.info('VAS_DateFieldAreMandatory');
                        $FromDate.setValue(null);
                        $ToDate.setValue(null);
                        return;
                    }
                    if ($FromDate.getValue() > $ToDate.getValue()) {
                        VIS.ADialog.info('VAS_PlzEnterCorrectDate');
                        $FromDate.setValue(null);
                        $ToDate.setValue(null);
                        return;
                    }
                    AD_Org_ID = $self.cmbOrg.getControl().find('option:selected').val();
                    pageNo = 1;
                    LoadTimeSheetData(pageNo, pageSize,false);

                });
            //On clcik of Preview button the data will beloaded on Prview data grid
            if (this.PreviewBtn != null) {
                this.PreviewBtn.on(VIS.Events.onTouchStartOrClick, function () {
                    if (LeftGridData.length == 0) {
                        VIS.ADialog.info('VAS_PlzSelectRecord');
                        return;
                    }
                    LoadPreviewDialog(gridDataArray);
                });
            }
            //On clcik of Generate invoice button the invoice will be generated
            if (this.GenerateInvBtn != null) {
                this.GenerateInvBtn.on(VIS.Events.onTouchStartOrClick, function () {
                    if (LeftGridData.length == 0) {
                        VIS.ADialog.info('VAS_PlzSelectRecord');
                        return;
                    }
                    GenerateInvoice(gridDataArray);
                });
            }
            /*This event is used to handle the Togle event of left side i.e.
              once this button is clicked by user the left field get disabled*/
            if (this.btnToggel != null)
                var borderspace = 0;
            this.btnToggel.on(VIS.Events.onTouchStartOrClick, function () {
                if (toggleside) {
                    if (VIS.Application.isRTL) {
                        borderspace = 180;
                    }
                    else {
                        borderspace = 0;

                    }
                    $self.btnToggel.animate({ borderSpacing: borderspace }, {
                        step: function (now, fx) {
                            $(this).css('-webkit-transform', 'rotate(' + now + 'deg)');
                            $(this).css('-moz-transform', 'rotate(' + now + 'deg)');
                            $(this).css('transform', 'rotate(' + now + 'deg)');
                        },
                        duration: 'slow'
                    }, 'linear');

                    toggleside = false;
                    $self.btnSpaceDiv.animate({ width: 310 }, "slow");
                    $self.gridSelectDiv.animate({ width: selectDivWidth }, "slow");
                    LeftSideFields.css("display", "block");
                    $self.SearchBtn.css("display", "block");
                    $self.LeftsideDiv.animate({ width: sideDivWidth }, "slow", null, function () {
                        $self.dGrid.resize();
                    });
                }
                else {
                    if (VIS.Application.isRTL) {
                        borderspace = 0;
                    }
                    else {
                        borderspace = 180;

                    }
                    $self.btnToggel.animate({ borderSpacing: borderspace }, {
                        step: function (now, fx) {
                            $(this).css('-webkit-transform', 'rotate(' + now + 'deg)');
                            $(this).css('-moz-transform', 'rotate(' + now + 'deg)');
                            $(this).css('transform', 'rotate(' + now + 'deg)');
                        },
                        duration: 'slow'
                    }, 'linear');

                    toggleside = true;
                    $self.btnSpaceDiv.animate({ width: minSideWidth }, "slow");
                    $self.LeftsideDiv.animate({ width: minSideWidth }, "slow");
                    LeftSideFields.css("display", "none");
                    $self.SearchBtn.css("display", "none");
                    $self.gridSelectDiv.animate({ width: selectDivFullWidth }, "slow", null, function () {
                        $self.dGrid.resize();
                    });
                }
            });

        }
        /* this function is used to remove the div after generating invoice*/
        function removeControlsSelectedDiv() {
            $CustomerSelected.remove();
            $ResourceSelected.remove();
        }
        /* This function is responsible for clearing the variable*/
        function clearVariables() {
            gridDataArray = [];
            LeftGridData = [];
            FilteredData = [];
            TimeExpenseId = [];
            RequestId = [];
            ResourceId = [];
            TaskId = [];
            CustomerId = [];
            ProjectId = [];
        }

        this.display = function () {
            dynInit(null);
        }

        this.getRoot = function () {
            return this.$root;
        };

        this.refreshUI = function () {
            /*Refresh Grid on Focus*/
            this.dGrid.resize();
        };

        this.disposeComponent = function () {

            $self = null;
            this.frame = null;
            this.windowNo = null;
            this.arrListColumns = null;
            this.dGrid = null;
            whereClause = null;
            AD_Org_ID = null;
            C_BPartner_ID = null;

            toggle = null;
            toggleGen = null;
            toggleside = null;

            this.lblOrg = null;
            this.lblBPartner = null;
            this.lblStatusInfo = null;
            this.tabSelect = null;
            this.tabGenrate = null;
            this.cmbOrg = null;
            this.vSearchBPartner = null;

            this.$root = null;
            this.$busyDiv = null;

            this.topDiv = null;
            this.sideDiv = null;
            this.gridSelectDiv = null;
            this.gridGenerateDiv = null;
            this.bottumDiv = null;
            this.okBtn = null;
            this.cancelBtn = null;
            this.btnRefresh = null;
            this.btnToggel = null;
            this.spnSelect = null;
            this.spnGenerate = null;
            ColumnIds = null;
            ColumnData = null;
            gridDataArray = [];
            LeftGridData = [];
            FilteredData = [];
            TimeExpenseId = [];
            RequestId = [];
            ResourceId = [];
            TaskId = [];
            CustomerId = [];
            ProjectId = [];
            SubTotal = 0;
            $FromDate = null;
            $ToDate = null;
            $CustomerSelected = null;
            $ResourceSelected = null;
            $TimeAndExpenseSelected = null;
            $TaskSelected = null;
            $ProjectSelected = null;
            $RequestSelected = null;

            sideDivWidth = null;
            selectDivWidth = null;
            selectDivFullWidth = null;
            selectDivToggelWidth = null;
            sideDivHeight = null;

            this.getRoot = null;
            this.disposeComponent = null;
        };
    };

    //Must Implement with same parameter
    VAS_TimeSheetInvoice.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        VIS.Env.getCtx().setContext(this.windowNo, "IsSOTrx", "Y");

        try {
            var obj = this.Initialize();
        }
        catch (ex) {
            //log.Log(Level.SEVERE, "init", ex);
        }

        this.frame.getContentGrid().append(this.getRoot());
        this.display();
        this.cmbOrg.getControl().focus();
    };

    VAS_TimeSheetInvoice.prototype.refresh = function () {
        this.refreshUI();
    };

    //Must implement dispose
    VAS_TimeSheetInvoice.prototype.dispose = function () {
        /*CleanUp Code */
        //dispose this component
        this.disposeComponent();

        //call frame dispose function
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };
    VAS.Apps = VAS.Apps || {};
    VAS.Apps.AForms = VIS.Apps.AForms || {};
    VAS.Apps.AForms.VAS_TimeSheetInvoice = VAS_TimeSheetInvoice;


})(VAS, jQuery);