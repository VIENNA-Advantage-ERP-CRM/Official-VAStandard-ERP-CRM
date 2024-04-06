
; (function (VIS, $) {
    function VAS_TimeSheetInvoice() {
        this.frame;
        this.windowNo;
        var $self = this;
        this.arrListColumns = [];
        this.PreviewColumns = [];
        var IsRecordUnselected = false;

        this.dGrid = null;
        var whereClause = null;
        var AD_Org_ID = null;
        var C_BPartner_ID = null;

        var toggle = false;
        var toggleGen = false;
        var toggleside = false;

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


        this.$root = $("<div class='vis-formouterwrpdiv' style='width: 100%; height: 100%; background-color: white;'>");
        this.$busyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis-busyindicatordiv"></i></div></div>');
        this.topDiv = null;
        this.sideDiv = null;
        this.gridSelectDiv = null;
        this.bottumDiv = null;
        this.PreviewGridDiv = null;

        this.div = null;
        var SubTotal = 0;
        var $FromDate = null;
        var $ToDate = null;

        this.okBtn = null;
        this.cancelBtn = null;
        this.btnRefresh = null;
        this.btnToggel = null;
        this.spnSelect = null;
        this.btnSpaceDiv = null;
        this.lblGenrate = null;
        this.lblSelect = null;
        var S_Resource_ID = null;
        var TimExpenSeDoc = null;
        var C_Task_ID = null;
        var C_Project_ID = null;
        var R_Request_ID

        var btnClearBP = null;
        var btnClearResource = null;
        var gridDataArray = [];
        var LeftGridData = [];
        var FilteredData = [];

        var sideDivWidth = 260;
        var minSideWidth = 50;
        //window with-(sidediv with_margin from left+ space)
        var selectDivWidth = $(window).width() - (sideDivWidth + 20 + 5);
        var selectDivFullWidth = $(window).width() - (20 + minSideWidth);
        var selectDivToggelWidth = selectDivWidth + sideDivWidth + 5;
        var sideDivHeight = $(window).height() - 210;


        function initializeComponent() {

            var LeftSideFields = $('<div style="width:100%; height:calc(100% - 90px);" class="VA003-form-top-fields ">');
            $OrgDiv = $('<div class="input-group vis-input-wrap">');
            var Orglookup = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, GetColumnID("AD_Org_ID"), VIS.DisplayType.TableDir, "AD_Org_ID", 0, false, "AD_Org.AD_Org_ID <> 0 AND AD_Org.IsSummary='N' AND AD_Org.IsActive='Y' AND AD_Org.IsCostCenter='N' AND AD_Org.IsProfitCenter='N' ");
            $self.cmbOrg = new VIS.Controls.VComboBox("AD_Org_ID", true, false, true, Orglookup, 150, VIS.DisplayType.TableDir, 0);
            $self.cmbOrg.setMandatory(true);
            var $OrgControlWrap = $('<div class="vis-control-wrap">');
            var $OrgButtonWrap = $('<div class="input-group-append">');
            $OrgDiv.append($OrgControlWrap);
            $OrgControlWrap.append($self.cmbOrg.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("AD_Org_ID") + '</label>');
            $OrgDiv.append($OrgControlWrap);
            $OrgButtonWrap.append($self.cmbOrg.getBtn(0));
            $OrgDiv.append($OrgButtonWrap);

            $CustomerDiv = $('<div class="input-group vis-input-wrap">');
            var CustomerLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, GetColumnID("C_BPartner_ID"), VIS.DisplayType.Search, "C_BPartner_ID", 0, false, "C_BPartner.IsActive ='Y' And C_Bpartner.Issummary ='N'");
            $self.vSearchCustomer = new VIS.Controls.VTextBoxButton("C_BPartner_ID", true, false, true, VIS.DisplayType.Search, CustomerLookUp, 0);
            var $CustomerControlWrap = $('<div class="vis-control-wrap">');
            var $CustomerButtonWrap = $('<div class="input-group-append">');
            $CustomerDiv.append($CustomerControlWrap);
            $CustomerControlWrap.append($self.vSearchCustomer.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("C_BPartner_ID") + '</label>');
            $CustomerDiv.append($CustomerControlWrap);
            $CustomerButtonWrap.append($self.vSearchCustomer.getBtn(0));
            $CustomerDiv.append($CustomerButtonWrap);

            $ResourceDiv = $('<div class="input-group vis-input-wrap">');
            var ResuorceLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, GetColumnID("S_Resource_ID"), VIS.DisplayType.Search, "S_Resource_ID", 0, false, "S_Resource.AD_Org_ID IN(0,@AD_Org_ID@) AND S_Resource.IsActive='Y'");
            $self.vSearchResource = new VIS.Controls.VTextBoxButton("S_Resource_ID", true, false, true, VIS.DisplayType.Search, ResuorceLookUp, 0);
            var $ResourceControlWrap = $('<div class="vis-control-wrap">');
            var $ResourceButtonWrap = $('<div class="input-group-append">');
            $ResourceDiv.append($ResourceControlWrap);
            $ResourceControlWrap.append($self.vSearchResource.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("S_Resource_ID") + '</label>');
            $ResourceDiv.append($ResourceButtonWrap);
            $ResourceButtonWrap.append($self.vSearchResource.getBtn(0));
            $ResourceDiv.append($ResourceButtonWrap);
            var fromate = $('<div class="VAS-left-data"><div class="vis-input-wrap"><div class="vis-control-wrap"><input type="date" max="9999-12-31" id="VAS_FromDate_' + $self.windowNo + '"><label>' + VIS.Msg.getMsg("VA009_FromDate") + '</label></div></div> </div>');

            var toDate = $('<div class="VAS-left-data"><div class="vis-input-wrap"><div class="vis-control-wrap"><input type="date" max="9999-12-31" id="VAS_ToDate_' + $self.windowNo + '"> <label>' + VIS.Msg.getMsg("VA009_ToDate") + '</label></div></div></div>');


            var topDivId = "topDiv_" + $self.windowNo;
            var btnSpaceDivId = "btnSpaceDiv_" + $self.windowNo;
            var btnSpaceId = "btnSpace_" + $self.windowNo;
            var spnRecordSelection = "spnSelect_" + $self.windowNo;
            var lblRecordSelection = "lblSelect_" + $self.windowNo;

            $self.topDiv = $("<div id='" + topDivId + "' class='vis-archive-l-s-head vis-frm-ls-top' style='padding: 0;'>" +
                "<div id='" + btnSpaceDivId + "' class='vis-l-s-headwrp'>" +
                "<button id='" + btnSpaceId + "' class='vis-archive-sb-t-button' ><i class='vis vis-arrow-left'></i></button></div>" +
                "<div id='" + spnRecordSelection + "' class='VAS-RecordSelection'>" +
                "<div style='display: flex; width: 100%; align-items:center; justify-content:space-between' >" +
                "<label id='" + lblRecordSelection + "' class='VIS_Pref_Label_Font' style='vertical-align: middle;font-size: 28px;color: rgba(var(--v-c-primary), 1);'>" + VIS.Msg.translate(VIS.Env.getCtx(), "VAS_RecordSelection") + "</label></div>" +
                "<span class='VA075-srm-resourceSelect btn d-flex position-relative' type='button'> " +
                "<i class='fa fa-filter VAS-srm-filterIcon' id='VAS_FilterBtn_" + $self.windowNo + "'></i>" +
                "</div>")

            var sideDivId = "sideDiv_" + $self.windowNo;
            var parameterDivId = "parameterDiv_" + $self.windowNo;

            $self.btnSpaceDiv = $self.topDiv.find("#" + btnSpaceDivId);

            $self.sideDiv = $("<div id='" + sideDivId + "' class='vis-archive-l-s-content vis-leftsidebarouterwrap px-3'>");
            $self.sideDiv.css("height", "100%");
            var SearchBtnName = "SearchBtn_" + $self.windowNo;
            $self.SearchBtn = $("<input id='" + SearchBtnName + "' class='VIS_Pref_btn-2' style='margin-top: 0px; float: right;width: 70px;height: 38px;' type='button' value='" + VIS.Msg.translate(VIS.Env.getCtx(), "Search") + "'>");
            $self.bottumDiv = $("<div class='vis-info-btmcnt-wrap vis-p-t-10 vas-BottomBtnDiv'>");
            var PreviewBtnName = "PreviewBtn_" + $self.windowNo;
            $self.PreviewBtn = $("<button id='" + PreviewBtnName + "' class='VIS_Pref_btn-2 mr-2'>" + VIS.Msg.translate(VIS.Env.getCtx(), "VAS_Preview") + "</button>");
            var GenerateInvName = "VAS_GenInvoice_" + $self.windowNo;
            $self.GenerateInvBtn = $("<button id='" + GenerateInvName + "' class='VIS_Pref_btn-2'>" + VIS.Msg.translate(VIS.Env.getCtx(), "VAS_GenInvoice") + "</button>");

            LeftSideFields.append($OrgDiv).append($CustomerDiv).append($ResourceDiv).append(fromate).append(toDate);
            $self.div = $("<div class='vis-archive-l-s-content-inner py-3 px-2' id='" + parameterDivId + "'>");
            $self.sideDiv.append(LeftSideFields).append($self.SearchBtn);
            $self.bottumDiv.append($self.GenerateInvBtn).append($self.PreviewBtn);



            var gridSelectDivId = "gridSelectDiv_" + $self.windowNo;

            $self.gridSelectDiv = $("<div id='" + gridSelectDivId + "' class='vis-frm-grid-outerwrp'>");

            //Add to root
            $self.$root.append($self.$busyDiv);
            $self.$busyDiv[0].style.visibility = "hidden";
            $self.$root.append($self.topDiv).append($self.sideDiv).append($self.gridSelectDiv).append($self.bottumDiv);
        }

        function dynInit(data) {

            if ($self.dGrid != null) {
                $self.dGrid.destroy();
                $self.dGrid = null;
            }
            if ($self.arrListColumns.length == 0) {
                $self.arrListColumns.push({
                    field: "DocumentNo", caption: VIS.Msg.translate(VIS.Env.getCtx(), "VAS_TimeRecordDoc"), sortable: true, size: '16%', min: 150, hidden: false, editable: { type: 'number' }, render: function (record, index, col_index) {
                        return '<a href="#" class="vas-decoration-style">' + record['DocumentNo'] + '</a>';
                    }, editable: { type: 'text' }
                });

                $self.arrListColumns.push({ field: "C_BPartner", caption: VIS.Msg.translate(VIS.Env.getCtx(), "VAS_Customer"), sortable: true, size: '16%', min: 150, hidden: false });
                $self.arrListColumns.push({ field: "S_Resource_ID", caption: VIS.Msg.translate(VIS.Env.getCtx(), "S_Resource_ID"), sortable: true, size: '16%', min: 150, hidden: false });
                $self.arrListColumns.push({ field: "M_Product", caption: VIS.Msg.translate(VIS.Env.getCtx(), "M_Product_ID"), sortable: true, size: '16%', min: 150, hidden: false });
                $self.arrListColumns.push({
                    field: 'Qty', caption: VIS.Msg.getElement(VIS.Env.getCtx(), "Qty"), size: '16%', sortable: true, size: '16%', min: 150, hidden: false, render: function (record, index, col_index) {
                        var val = record["Qty"] + " " + record["UomName"];
                        return '<a href="#" class="vas-decoration-style">' + val + '</a>';;
                    }
                })
                $self.arrListColumns.push({ field: "Price", caption: VIS.Msg.getElement(VIS.Env.getCtx(), "Price"), sortable: true, size: '16%', min: 150, hidden: false, editable: { type: 'number' } });
                $self.arrListColumns.push({
                    field: 'Amount', caption: VIS.Msg.translate(VIS.Env.getCtx(), "VAS_TotalBilableAmount"), size: '16%', sortable: true, size: '16%', min: 150, hidden: false
                })
                $self.arrListColumns.push({ field: "C_BPartner_ID", caption: VIS.Msg.translate(VIS.Env.getCtx(), "C_BPartner_ID"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "VA009_PaymentMethod_ID", caption: VIS.Msg.translate(VIS.Env.getCtx(), "VA009_PaymentMethod_ID"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "M_PriceList_ID", caption: VIS.Msg.translate(VIS.Env.getCtx(), "M_PriceList_ID"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "C_BPartner_Location_ID", caption: VIS.Msg.translate(VIS.Env.getCtx(), "C_BPartner_Location_ID"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "M_Product_ID", caption: VIS.Msg.translate(VIS.Env.getCtx(), "M_Product_ID"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "C_Charge_ID", caption: VIS.Msg.translate(VIS.Env.getCtx(), "C_Charge_ID"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "C_Currency_ID", caption: VIS.Msg.translate(VIS.Env.getCtx(), "C_Currency_ID"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "C_PaymentTerm_ID", caption: VIS.Msg.translate(VIS.Env.getCtx(), "C_PaymentTerm_ID"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "C_Uom_ID", caption: VIS.Msg.translate(VIS.Env.getCtx(), "C_Uom_ID"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "LocationName", caption: VIS.Msg.translate(VIS.Env.getCtx(), "VAS_LocationName"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "recordCount", caption: VIS.Msg.translate(VIS.Env.getCtx(), "VAS_recordCounted"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "RecordedDate", caption: VIS.Msg.translate(VIS.Env.getCtx(), "VAS_RecordedDate"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "EstimatedTime", caption: VIS.Msg.translate(VIS.Env.getCtx(), "VAS_EstimatedTime"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "ProjReq_Name", caption: VIS.Msg.translate(VIS.Env.getCtx(), "VAS_ProjReq_Name"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "ProjReq_ID", caption: VIS.Msg.translate(VIS.Env.getCtx(), "VAS_ProjReq_ID"), sortable: true, size: '16%', min: 150, hidden: true });
                $self.arrListColumns.push({ field: "PhaseName", caption: VIS.Msg.translate(VIS.Env.getCtx(), "VAS_PhaseName"), sortable: true, size: '16%', min: 150, hidden: true });
            }
            w2utils.encodeTags(data);
            $self.dGrid = $($self.gridSelectDiv).w2grid({
                name: "gridGenForm" + $self.windowNo,
                recordHeight: 40,
                show: { selectColumn: true },
                multiSelect: true,
                columns: $self.arrListColumns,
                records: data,
                onSelect: function (event) {
                    if ($self.dGrid.records.length > 0) {
                        ArrayOfGrid(event, $self.dGrid);
                    }
                },
                onUnselect: OnUnseletRow,
                onEditField: OpenInfoDialog

            });
        }
        function OpenInfoDialog(e) {
            if (e.column == 0) {
                var arry = []
                if ($self.dGrid.records[e.index].PhaseName != null) {
                    arry = ($self.dGrid.records[e.index].PhaseName).split(',')
                }
                var htmlmain = $('<div>');
                var htmlString = '<div class="vas-taskDescDailog">                                                              ' +
                    ' <div class="vas-taskDesc_feildWrap mb-2 ">                                                                         ' +
                    '   <div class="vas-taskDesc_feild">                                                                                 ' +
                    '     <span class="vas-taskdescTtl">' + VIS.Msg.getMsg("VAS_RecordingDate") + '</span>                                                            ' +
                    '     <span class="vas-taskdescValue">' + (Globalize.format(new Date($self.dGrid.records[e.index].RecordedDate), "yyyy-MM-dd")) + '</span>                                                              ' +
                    '   </div>                                                                                                           ' +
                    '   <div class="vas-taskDesc_feild">                                                                                 ' +
                    '     <span class="vas-taskdescTtl text-right">' + VIS.Msg.getMsg("VAS_RequestOrProject") + '</span>                                       ' +
                    '     <span class="vas-taskdescValue text-right">' + $self.dGrid.records[e.index].ProjReq_Name + '</span>                                    ' +
                    '   </div>                                                                                                           ' +
                    ' </div>                                                                                                             ' +
                    ' <div class="vas-projectTimeline mb-2">                                                                             ' +
                    '   <span class="vas-taskdescTtl">' + VIS.Msg.getMsg("VAS_RequestOrProject") + '</span>                                                    ' +
                    '   <div class="vas-projectTimelineConatainer d-flex align-items-center justify-content-center">                     ' +
                    '     <div id="pipeline" class="pipeline">';
                if ($self.dGrid.records[e.index].PhaseName != null) {
                    for (var i = 0; i < arry.length; i++) {
                        htmlString += '<div class="stage" id="stage' + i + '">' + arry[i] + '</div>';
                        if (i < arry.length - 1) {
                            htmlString += '<div class="connector"></div>';
                        }
                    }
                }
                else if ($self.dGrid.records[e.index].Summary != null) {
                    htmlString += '<div>' + $self.dGrid.records[e.index].PhaseName + '</div>';
                }

                htmlString += '</div>' +
                    '   </div>                                                                                                           ' +
                    ' </div>                                                                                                             ' +
                    ' <div class="vas-taskDesc_feildWrap mb-2 ">                                                                         ' +
                    '   <div class="vas-taskDesc_feild">                                                                                 ' +
                    '     <span class="vas-taskdescTtl">' + VIS.Msg.getMsg("VAS_EstimatedTime") + '</span>                                                            ' +
                    '     <span class="vas-taskdescValue">' + $self.dGrid.records[e.index].EstimatedTime + '</span>                                                                ' +
                    '   </div>                                                                                                           ' +
                    '   <div class="vas-taskDesc_feild">                                                                                 ' +
                    '     <span class="vas-taskdescTtl text-right">' + VIS.Msg.getMsg("VAS_RecordtedTime") + '</span>                                                  ' +
                    '     <span class="vas-taskdescValue text-right">' + $self.dGrid.records[e.index].Qty + '</span>                                                     ' +
                    '   </div>                                                                                                           ' +
                    ' </div>                                                                                                             ' +
                    '</div>';
                htmlmain.append(htmlString);
                var RequestInfoDialog = new VIS.ChildDialog();
                RequestInfoDialog.setContent(htmlmain);
                RequestInfoDialog.setTitle(VIS.Msg.getMsg("VAS_RequestInfoDialog"));
                RequestInfoDialog.setWidth("50%");
                RequestInfoDialog.setEnableResize(true);
                RequestInfoDialog.setModal(true);
                RequestInfoDialog.show();
                RequestInfoDialog.hideButtons();
            }
        }
        function OnUnseletRow(e) {
            IsRecordUnselected = true;
            ArrayOfGrid(e, $self.dGrid);
        }
        function ArrayOfGrid(event, dGrid) {
            if (event.recid == undefined) {
                gridDataArray = [];
                LeftGridData = [];
                for (var i = 0; i < dGrid.records.length; i++) {
                    if (IsRecordUnselected) {
                        gridDataArray = [];
                        LeftGridData = [];
                    }
                    else {
                        gridDataArray.push(dGrid.records[i]);
                    }
                }
                AddForPreviewDataOnAllSelection(gridDataArray, true);

            }
            else {
                if (IsRecordUnselected) {

                    AddedDataForPreviewOnSingleSelection(event, dGrid, false);
                    gridDataArray = jQuery.grep(gridDataArray, function (value) {
                        return VIS.Utility.Util.getValueOfInt(value.recid) != VIS.Utility.Util.getValueOfInt(event.recid)
                    });
                }
                else {
                    SelectedAll = false;
                    gridDataArray.push(dGrid.records[event.index]);
                    AddedDataForPreviewOnSingleSelection(event, dGrid, true);
                }
            }
            IsRecordUnselected = false;

        }
        var GetColumnID = function (ColumnName) {
            var Column_ID = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS_TimeSheetInvoices/GetColumnID", { "ColumnName": ColumnName }, null);
            return Column_ID;
        }

        function AddForPreviewDataOnAllSelection(gridDataArray, shouldAdd) {
            if (gridDataArray.length === 0) {
                LeftGridData = [];
                return;
            }
            for (var i = 0; i < gridDataArray.length; i++) {
                var record = gridDataArray[i];
                var isUnique = true;

                for (var j = 0; j < LeftGridData.length; j++) {
                    if (record.C_BPartner_ID === LeftGridData[j].C_BPartner_ID &&
                        record.C_BPartner_Location_ID === LeftGridData[j].C_BPartner_Location_ID &&
                        record.VA009_PaymentMethod_ID === LeftGridData[j].VA009_PaymentMethod_ID &&
                        record.M_PriceList_ID === LeftGridData[j].M_PriceList_ID) {
                        LeftGridData[j].recordCount = LeftGridData[j].recordCount + 1;
                        isUnique = false;
                        break;
                    }
                }

                if (isUnique && shouldAdd) {
                    record.recordCount = 1;
                    LeftGridData.push(record);
                }
            }
        }

        function AddedDataForPreviewOnSingleSelection(event, dGrid, shouldAdd) {
            var isUnique = true;
            for (var j = 0; j < LeftGridData.length; j++) {
                if (shouldAdd && (dGrid.records[event.index].C_BPartner_ID === LeftGridData[j].C_BPartner_ID &&
                    dGrid.records[event.index].C_BPartner_Location_ID === LeftGridData[j].C_BPartner_Location_ID &&
                    dGrid.records[event.index].VA009_PaymentMethod_ID === LeftGridData[j].VA009_PaymentMethod_ID &&
                    dGrid.records[event.index].M_PriceList_ID === LeftGridData[j].M_PriceList_ID)) {
                    LeftGridData[j].recordCount = LeftGridData[j].recordCount + 1;
                    isUnique = false;
                    break;
                }
                else if (!shouldAdd && (dGrid.records[event.index].C_BPartner_ID === LeftGridData[j].C_BPartner_ID &&
                    dGrid.records[event.index].C_BPartner_Location_ID === LeftGridData[j].C_BPartner_Location_ID &&
                    dGrid.records[event.index].VA009_PaymentMethod_ID === LeftGridData[j].VA009_PaymentMethod_ID &&
                    dGrid.records[event.index].M_PriceList_ID === LeftGridData[j].M_PriceList_ID)) {

                    LeftGridData[j].recordCount = LeftGridData[j].recordCount - 1;
                    if (LeftGridData[j].recordCount <= 0) {
                        LeftGridData = jQuery.grep(LeftGridData, function (record) {
                            return record.recordCount > 0;
                        });
                    }

                }
            }
            if (isUnique && shouldAdd) {
                dGrid.records[event.index].recordCount = 1;
                LeftGridData.push(dGrid.records[event.index]);
            }
        }

        // search data
        function executeQuery() {
            var data = [];
            LeftGridData = [];
            gridDataArray = [];
            FilteredData = [];
            if (VIS.Utility.Util.getValueOfInt(AD_Org_ID) == 0) {
                VIS.ADialog.info('VAS_PlzSelectOrganization');
                return;
            }
            var AD_Cleint_ID = VIS.Env.getCtx().getAD_Client_ID();
            // set busy indegator
            $($self.$root[0]).addClass("vis-apanel-busyVInOutGenRoot");
            $($self.$busyDiv[0]).addClass("vis-apanel-busyVInOutGenBusyDiv");
            $self.$busyDiv[0].style.visibility = "visible";
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_TimeSheetInvoices/LoadGridData",
                type: 'POST',
                //async: false,
                data: {
                    AD_Cleint_ID: VIS.Utility.Util.getValueOfInt(AD_Cleint_ID),
                    AD_Org_ID: VIS.Utility.Util.getValueOfInt(AD_Org_ID),
                    C_BPartner_ID: VIS.Utility.Util.getValueOfInt(C_BPartner_ID),
                    S_Resource_ID: VIS.Utility.Util.getValueOfInt(S_Resource_ID),
                    TimExpenSeDoc: VIS.Utility.Util.getValueOfInt(TimExpenSeDoc),
                    C_Project_ID: VIS.Utility.Util.getValueOfInt(C_Project_ID),
                    R_Request_ID: VIS.Utility.Util.getValueOfInt(R_Request_ID),
                    C_Task_ID: VIS.Utility.Util.getValueOfInt(C_Task_ID),
                    FromDate: $FromDate.val(),
                    toDate: $toDate.val()
                },
                success: function (res) {
                    var ress = JSON.parse(res);
                    if (ress && ress.length > 0) {
                        var count = 1;
                        for (var i = 0; i < ress.length; i++) {
                            var line = {};
                            line['DocumentNo'] = ress[i].DocumentNo,
                                line['C_BPartner'] = ress[i].CustomerName,
                                line['S_Resource_ID'] = ress[i].ResourceName,
                                line['M_Product'] = ress[i].ProductName,
                                line['Qty'] = ress[i].Qty,
                                line['Price'] = ress[i].PriceList,
                                line['Amount'] = ress[i].PriceList * ress[i].Qty
                            line['C_BPartner_ID'] = ress[i].CustomerId,
                                line['VA009_PaymentMethod_ID'] = ress[i].VA009_PaymentMethod_ID,
                                line['M_PriceList_ID'] = ress[i].M_PriceList_ID,
                                line['C_BPartner_Location_ID'] = ress[i].C_Location_ID,
                                line['M_Product_ID'] = ress[i].M_Product_ID,
                                line['C_Charge_ID'] = ress[i].C_Charge_ID,
                                line['C_Uom_ID'] = ress[i].C_Uom_ID,
                                line['C_PaymentTerm_ID'] = ress[i].C_PaymentTerm_ID,
                                line['C_Currency_ID'] = ress[i].C_Currency_ID,
                                line['UomName'] = ress[i].UomName,
                                line['LocationName'] = ress[i].LocationName,
                                line['ProjReq_ID'] = ress[i].ProjReq_ID,
                                line['ProjReq_Name'] = ress[i].ProjReq_Name,
                                line['RecordedDate'] = ress[i].RecordedDate,
                                line['EstimatedTime'] = ress[i].EstimatedTime
                            if (ress[i].PhaseInfo != null) {
                                line['PhaseName'] = "Start"
                                for (var j = 0; j < ress[i].PhaseInfo.length; j++) {
                                    line['PhaseName'] = line['PhaseName'] + "," + (ress[i].PhaseInfo[j].PhaseName);
                                }
                            }
                            else if (ress[i].RequestSummary != null) {
                                line['PhaseName'] = ress[i].RequestSummary;
                            }

                            line['recordCount'] = 0
                            line['recid'] = count;
                            count++;
                            data.push(line);
                        }

                    }
                    else {
                        VIS.ADialog.info("NoDataFound");
                        //    $self.okBtn.attr('disabled', 'disabled');
                    }
                    dynInit(data);
                    // set busy indegator
                    $($self.$root[0]).removeClass("vis-apanel-busyVInOutGenRoot");
                    $($self.$busyDiv[0]).removeClass("vis-apanel-busyVInOutGenBusyDiv");
                    $self.$busyDiv[0].style.visibility = "hidden";
                },
                error: function (e) {
                    //$self.log.info(e);
                    // set busy indegator
                    $($self.$root[0]).removeClass("vis-apanel-busyVInOutGenRoot");
                    $($self.$busyDiv[0]).removeClass("vis-apanel-busyVInOutGenBusyDiv");
                    $self.$busyDiv[0].style.visibility = "hidden";
                },
            });

            return data;
        }
        function PreviewDyinit(data) {
            $self.PreviewColumns = [];
            if ($self.dGridPreview != null) {
                $self.dGridPreview.destroy();
                $self.dGridPreview = null;
            }
            if ($self.PreviewColumns.length == 0) {
                $self.PreviewColumns.push({ field: "M_Product", caption: VIS.Msg.translate(VIS.Env.getCtx(), "VAS_Product"), sortable: true, size: '16%', min: 150, hidden: false });
                $self.PreviewColumns.push({ field: "Price", caption: VIS.Msg.translate(VIS.Env.getCtx(), "C_Charge_ID"), sortable: true, size: '16%', min: 150, hidden: false });
                $self.PreviewColumns.push({ field: "UomName", caption: VIS.Msg.translate(VIS.Env.getCtx(), "C_UOM_ID"), sortable: true, size: '16%', min: 150, hidden: false });
                $self.PreviewColumns.push({
                    field: 'Qty', caption: VIS.Msg.getElement(VIS.Env.getCtx(), "Qty"), size: '16%', sortable: true, size: '16%', min: 150, hidden: false, render: function (record, index, col_index) {
                        var val = record["Qty"] + " " + record["UomName"];
                        return val;
                    }
                })
                $self.PreviewColumns.push({
                    field: 'Amount', caption: VIS.Msg.translate(VIS.Env.getCtx(), "VAS_TotalAmount"), size: '16%', sortable: true, size: '16%', min: 150, hidden: false, render: function (record, index, col_index) {
                        SubTotal = SubTotal + record["Amount"];
                        return record["Amount"];
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
        function LoadPreviewDialog(gridDataArray) {
            var PreviewWrap = $('<div class="vas-apInvViewer d-flex">')
            var PreviLeftWrap = $('<div class= "vas-apInvLeftSide" >' +
                '<div class="vas-apInvListGroup ">');
            var rightPreviewWrap = $('<div class="vas-apInvRightSide">');
            var PreviewMainDiv = $("<div class='VA009-popform-content vis-formouterwrpdiv'>");
            for (var i = 0; i < LeftGridData.length; i++) {
                PreviLeftWrap.append(
                    '<div class="vas-apInvlistItem mb-2" id="C_BpartnerListDiv_' + $self.windowNo + '">' +
                    '<div class="vas-apInvCompanyLogo">' +
                    '<span>BO</span>' +
                    '</div>' +
                    '<div class="vas-apInvoiceDetails pr-1">' +
                    '<span class="vas-apInvDetailsDesc" id="C_BPartnerDiv_' + $self.windowNo + '" data-bpid="' + LeftGridData[i].C_BPartner_ID + '" data-paymethodid="' + LeftGridData[i].VA009_PaymentMethod_ID + '" data-pricelistid="' + LeftGridData[i].M_PriceList_ID + '"> '
                    + LeftGridData[i].C_BPartner + '</span > ' +
                    '<span class="vas-apInvDetailsDesc">' + VIS.Msg.getMsg("VAS_RecordCount") + ":" + LeftGridData[i].recordCount + '</span>' +
                    '<span class="vas-apInvDetailsDesLoc" id="C_BPartnerLocationDiv_' + $self.windowNo + '" data-bplocationid="' + LeftGridData[i].C_BPartner_Location_ID + '">' + LeftGridData[i].LocationName + '</span>' +
                    '</div>');
            }

            var PreviewId = "gridSelectDiv_" + $self.windowNo;
            var headTagForBPartner = $('<h4 id="HeadBPTag_' + $self.windowNo + '">' + LeftGridData[0].C_BPartner + '</h4>')
            $self.PreviewGridDiv = $("<div id='" + PreviewId + "' class='VAS-PointDiv'>");
            rightPreviewWrap.append(headTagForBPartner).append($self.PreviewGridDiv);
            PreviewWrap.append(PreviLeftWrap).append(rightPreviewWrap);
            PreviewMainDiv.append(PreviewWrap);
            var PreviewDialog = new VIS.ChildDialog();
            PreviewDialog.setContent(PreviewMainDiv);
            PreviewDialog.setTitle(VIS.Msg.getMsg("VAS_PreviewDialog"));
            PreviewDialog.setWidth("70%");
            PreviewDialog.setHeight(517);
            PreviewDialog.setEnableResize(true);
            PreviewDialog.setModal(true);
            PreviewDialog.show();
            PreviewDialog.hideButtons();
            var BPartnerInfoClickDiv = PreviLeftWrap.find('.vas-apInvlistItem');
            BPartnerInfoClickDiv.first().addClass('VA075-apInvActive');
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
            PreviewDyinit(FilteredData);
            var SubTotalSpan = $('<p class="text-right" id="SubTotalSpan_' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_SubTotal") + ": " + SubTotal + '<p>');
            rightPreviewWrap.append(SubTotalSpan);
            PreviLeftWrap.find('.vas-apInvlistItem').on('click', function () {
                locid = $(this).find('.vas-apInvDetailsDesLoc').attr('data-bplocationid');
                bpid = $(this).find('.vas-apInvDetailsDesc').attr('data-bpid');
                priceListId = $(this).find('.vas-apInvDetailsDesc').attr('data-pricelistid');
                payMethodId = $(this).find('.vas-apInvDetailsDesc').attr('data-paymethodid');
                PreviLeftWrap.find('.vas-apInvlistItem').removeClass('VA075-apInvActive');
                $(this).addClass('VA075-apInvActive');
                FilteredData = jQuery.grep(gridDataArray, function (value) {
                    return VIS.Utility.Util.getValueOfInt(value.C_BPartner_ID) === VIS.Utility.Util.getValueOfInt(bpid)
                        && VIS.Utility.Util.getValueOfInt(value.C_BPartner_Location_ID) === VIS.Utility.Util.getValueOfInt(locid)
                        && VIS.Utility.Util.getValueOfInt(value.VA009_PaymentMethod_ID) === VIS.Utility.Util.getValueOfInt(payMethodId)
                        && VIS.Utility.Util.getValueOfInt(value.M_PriceList_ID) === VIS.Utility.Util.getValueOfInt(priceListId)
                });

                PreviewDyinit(FilteredData);
                rightPreviewWrap.find("#HeadBPTag_" + $self.windowNo).text(FilteredData[0].C_BPartner);
                rightPreviewWrap.find("#SubTotalSpan_" + $self.windowNo).text(VIS.Msg.getMsg("VAS_SubTotal") + ": " + SubTotal)
                SubTotal = 0;
            });
            SubTotal = 0;
        }

        function GenerateInvoice(gridDataArray) {
            var AD_Cleint_ID = VIS.Env.getCtx().getAD_Client_ID();
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_TimeSheetInvoices/GenerateInvoice",
                type: 'POST',
                //async: false,
                data: {
                    DataTobeInvoice: JSON.stringify(gridDataArray),
                    AD_Cleint_ID: VIS.Utility.Util.getValueOfInt(AD_Cleint_ID),
                    AD_Org_ID: VIS.Utility.Util.getValueOfInt(AD_Org_ID)
                },
                success: function (res) {
                },
                error: function (e) {
                },
            });

        }

        function generatetab(toggleside) {
            if (toggleside) {
                $self.okBtn.hide();
                $self.btnToggel.animate({ borderSpacing: 0 }, {
                    step: function (now, fx) {
                        $(this).css('-webkit-transform', 'rotate(' + now + 'deg)');
                        $(this).css('-moz-transform', 'rotate(' + now + 'deg)');
                        $(this).css('transform', 'rotate(' + now + 'deg)');
                    },
                    duration: 'slow'
                }, 'linear');

                toggleside = false;

                $self.gridSelectDiv.animate({ width: selectDivWidth }, "fast");
                $self.sideDiv.animate({ width: 'toggle', height: 'toggle' }, "slow");
            }
            $self.gridSelectDiv.css("display", "none");
            $self.sideDiv.css("display", "none");
            $self.gridGenerateDiv.css("display", "block");
            //$self.btnToggel.attr('disabled', 'disabled');
            $self.btnToggel.hide();

            lblSelect.css("font-size", "17px").css("color", "rgba(var(--v-c-on-secondary),1)");
            lblGenrate.css("font-size", "28px").css("color", "rgba(var(--v-c-primary), 1)");
        }

        //this.vetoablechange = function (evt) {
        //    C_BPartner_ID = $self.vSearchBPartner.getValue();
        //    $self.SearchBtn.removeAttr('disabled');
        //    executeQuery();
        //};

        //size chnage 
        this.sizeChanged = function (h, w) {
            selectDivWidth = w - (sideDivWidth + 20);
            selectDivFullWidth = w - (20 + minSideWidth);
            if (toggleside == true) {
                $self.btnSpaceDiv.animate({ width: minSideWidth }, "slow");
                $self.sideDiv.animate({ width: minSideWidth }, "slow");
                $self.div.css("display", "none");
                $self.gridSelectDiv.animate({ width: selectDivFullWidth }, "slow", null, function () {
                    $self.dGrid.resize();
                });
            }
            else {
                $self.btnSpaceDiv.animate({ width: sideDivWidth }, "slow");
                $self.gridSelectDiv.animate({ width: selectDivWidth }, "slow");
                $self.div.css("display", "block");
                $self.sideDiv.animate({ width: sideDivWidth }, "slow", null, function () {
                    $self.dGrid.resize();
                });
            }
        }

        this.Initialize = function () {
            initializeComponent();
            this.btnToggel = this.$root.find("#btnSpace_" + $self.windowNo);
            this.spnRecordSelection = this.$root.find("#spnSelect_" + $self.windowNo);
            this.PreviewBtn = this.$root.find("#PreviewBtn_" + $self.windowNo);
            var btnFilter = this.$root.find("#VAS_FilterBtn_" + $self.windowNo);
            $FromDate = this.$root.find("#VAS_FromDate_" + $self.windowNo);
            $ToDate = this.$root.find("#VAS_ToDate_" + $self.windowNo);

            if (btnFilter != null) {
                btnFilter.on("click", function () {
                    var $FilterHesd=$('<div class="position-relative">                                                                ' +
                        '  <div class="vas-filter-flyout">                                                              ' +
                        '    <div class="vas-flyout-header">                                                            ' +
                        '      <h1>Filter</h1><span class="vis vis-cross"></span>                                       ' +
                        '    </div>                                                                                     ' +
                        '    <div class="vas-from-row">');
                    //    '      <div class="input-group vis-input-wrap">                                                 ' +
                    //    '        <div class="vis-control-wrap VA068_TaxJurisdiction">                                   ' +
                    //    '          <select name="VA068_TaxJurisdiction_ID" class="" placeholder=" " data-placeholder="">' +
                    //    '            <option value=""></option>                                                         ' +
                    //    '          </select>                                                                            ' +
                    //    '          <label for="Name">Customer</label>                                                   ' +
                    //    '        </div>                                                                                 ' +
                    //    '        <!-- vis-control-wrap -->                                                              ' +
                    //    '      </div>                                                                                   ' +
                    //    '      <div class="vas-dropdown-lbl">                                                           ' +
                    //    '        <div class="vas-selected-lbl">Marian<span class="vis vis-cross"></span></div>          ' +
                    //    '        <div class="vas-selected-lbl">Brando<span class="vis vis-cross"></span></div>          ' +
                    //    '        <div class="vas-selected-lbl">Cary Grant<span class="vis vis-cross"></span></div>      ' +
                    //    '        <div class="vas-selected-lbl">Cary Grant<span class="vis vis-cross"></span></div>      ' +
                    //    '        <div class="vas-selected-lbl">Cary Grant<span class="vis vis-cross"></span></div>      ' +
                    //    '        <div class="vas-selected-lbl">Cary Grant<span class="vis vis-cross"></span></div>      ' +
                    //    '        <div class="vas-selected-lbl">Cary Grant<span class="vis vis-cross"></span></div>      ' +
                    //    '      </div>                                                                                   ' +
                    //    '      <div class="input-group vis-input-wrap">                                                 ' +
                    //    '        <div class="vis-control-wrap VA068_TaxJurisdiction">                                   ' +
                    //    '          <select name="VA068_TaxJurisdiction_ID" class="" placeholder=" " data-placeholder="">' +
                    //    '            <option value=""></option>                                                         ' +
                    //    '          </select>                                                                            ' +
                    //    '          <label for="Name">Location</label>                                                   ' +
                    //    '        </div>                                                                                 ' +
                    //    '        <!-- vis-control-wrap -->                                                              ' +
                    //    '      </div>                                                                                   ' +
                    //    '      <div class="vas-dropdown-lbl">                                                           ' +
                    //    '        <div class="vas-selected-lbl">New York, USA<span class="vis vis-cross"></span></div>   ' +
                    //    '        <div class="vas-selected-lbl">Alabama, USA<span class="vis vis-cross"></span></div>    ' +
                    //    '      </div>                                                                                   ' +
                    //    '      <div class="input-group vis-input-wrap">                                                 ' +
                    //    '        <div class="vis-control-wrap VA068_TaxJurisdiction">                                   ' +
                    //    '          <select name="VA068_TaxJurisdiction_ID" class="" placeholder=" " data-placeholder="">' +
                    //    '            <option value=""></option>                                                         ' +
                    //    '          </select>                                                                            ' +
                    //    '          <label for="Name">Project</label>                                                    ' +
                    //    '        </div>                                                                                 ' +
                    //    '        <!-- vis-control-wrap -->                                                              ' +
                    //    '      </div>                                                                                   ' +
                    //    '      <div class="input-group vis-input-wrap">                                                 ' +
                    //    '        <div class="vis-control-wrap VA068_TaxJurisdiction">                                   ' +
                    //    '          <select name="VA068_TaxJurisdiction_ID" class="" placeholder=" " data-placeholder="">' +
                    //    '            <option value=""></option>                                                         ' +
                    //    '          </select>                                                                            ' +
                    //    '          <label for="Name">Request</label>                                                    ' +
                    //    '        </div>                                                                                 ' +
                    //    '        <!-- vis-control-wrap -->                                                              ' +
                    //    '      </div>                                                                                   ' +
                    //    '    </div>                                                                                     ' +
                    //    '  </div>                                                                                        '+
                    //'</div>');
                   // $opnFilter = $("<div class='VA009-popform-content'>");

                    $TimeAndExpenseDiv = $('<div class="input-group vis-input-wrap">');
                    var TimeAndExpenseLookUp = VIS.MLookupFactory.getMLookUp(VIS.Env.getCtx(), $self.windowNo, GetColumnID("S_TimeExpense_ID"), VIS.DisplayType.Search);
                    $self.vSearchTimeAndExpense = new VIS.Controls.VTextBoxButton("S_TimeExpense_ID", true, false, true, VIS.DisplayType.Search, TimeAndExpenseLookUp);
                    var $TimeAndExpenseControlWrap = $('<div class="vis-control-wrap">');
                    var $TimeAndExpenseButtonWrap = $('<div class="input-group-append">');
                    $TimeAndExpenseDiv.append($TimeAndExpenseControlWrap);
                    $TimeAndExpenseControlWrap.append($self.vSearchTimeAndExpense.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("S_TimeExpense_ID") + '</label>');
                    $TimeAndExpenseDiv.append($TimeAndExpenseControlWrap);
                    $TimeAndExpenseButtonWrap.append($self.vSearchTimeAndExpense.getBtn(0));
                    $TimeAndExpenseDiv.append($TimeAndExpenseButtonWrap);

                    $TaskDiv = $('<div class="input-group vis-input-wrap">');
                    var TaskLookUp = VIS.MLookupFactory.getMLookUp(VIS.Env.getCtx(), $self.windowNo, GetColumnID("VA075_Task_ID"), VIS.DisplayType.Search);
                    $self.vSearchTask = new VIS.Controls.VTextBoxButton("VA075_Task_ID", true, false, true, VIS.DisplayType.Search, TaskLookUp);
                    var $TaskControlWrap = $('<div class="vis-control-wrap">');
                    var $TaskButtonWrap = $('<div class="input-group-append">');
                    $TaskDiv.append($TaskControlWrap);
                    $TaskControlWrap.append($self.vSearchTask.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("VA075_Task_ID") + '</label>');
                    $TaskDiv.append($TaskControlWrap);
                    $TaskButtonWrap.append($self.vSearchTask.getBtn(0));
                    $TaskDiv.append($TaskButtonWrap);

                    $ProjectDiv = $('<div class="input-group vis-input-wrap">');
                    var ProjectLookUp = VIS.MLookupFactory.getMLookUp(VIS.Env.getCtx(), $self.windowNo, GetColumnID("C_Project_ID"), VIS.DisplayType.Search);
                    $self.vSearchProject = new VIS.Controls.VTextBoxButton("C_Project_ID", true, false, true, VIS.DisplayType.Search, ProjectLookUp);
                    var $ProjectControlWrap = $('<div class="vis-control-wrap">');
                    var $ProjectButtonWrap = $('<div class="input-group-append">');
                    $ProjectDiv.append($ProjectControlWrap);
                    $ProjectControlWrap.append($self.vSearchProject.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("C_Project_ID") + '</label>');
                    $ProjectDiv.append($ProjectButtonWrap);
                    $ProjectButtonWrap.append($self.vSearchProject.getBtn(0));
                    $ProjectDiv.append($ProjectButtonWrap);

                    $RequestDiv = $('<div class="input-group vis-input-wrap">');
                    var RequestLookUp = VIS.MLookupFactory.getMLookUp(VIS.Env.getCtx(), $self.windowNo, GetColumnID("R_Request_ID"), VIS.DisplayType.Search);
                    $self.vSearchRequest = new VIS.Controls.VTextBoxButton("R_Request_ID", true, false, true, VIS.DisplayType.Search, RequestLookUp);
                    var $RequestControlWrap = $('<div class="vis-control-wrap">');
                    var $RequestButtonWrap = $('<div class="input-group-append">');
                    $RequestDiv.append($RequestControlWrap);
                    $RequestControlWrap.append($self.vSearchRequest.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("R_Request_ID") + '</label>');
                    $RequestDiv.append($RequestButtonWrap);
                    $RequestButtonWrap.append($self.vSearchRequest.getBtn(0));
                    $RequestDiv.append($RequestButtonWrap);

                    $FilterHesd.append($TimeAndExpenseDiv).append($TaskDiv).append($ProjectDiv).append($RequestDiv);

                    //var SelectionDialog = new VIS.ChildDialog();
                    //SelectionDialog.setContent($opnFilter);
                    //SelectionDialog.setTitle(VIS.Msg.getMsg("VA009_LoadBatchPayment"));
                    //SelectionDialog.setWidth("30%");
                    //SelectionDialog.setEnableResize(true);
                    //SelectionDialog.setModal(true);
                    //SelectionDialog.show();
                    //SelectionDialog.hideButtons();
                    // var ApplyBtn = $("<div class='VAS-ButtonDivStyle'>" +
                    //    "<div class='d-flex align-items-center justify-content-end'>" +
                    //    "<button class='ui-button mr-3' id="VAS_Apply_" + $self.windowNo + '">" + VIS.Msg.getMsg("VAS_Apply") + "</button>" +
                    //    "</div >" +
                    //    "</div>"
                    //);
                    var ApplyBtn = $('<div class="vas-flyout-footer">' +
                        '<button class="btn VIS_Pref_btn-2" id="VAS_Apply_' + $self.windowNo + '">'+ VIS.Msg.getMsg("VAS_Apply") +'</button>' +
                        '</div>'
                    );
                    $FilterHesd.append(ApplyBtn);
                    var $ApplyButton = ApplyBtn.find("#VAS_Apply_" + $self.windowNo)
                    this.$root.append($FilterHesd);
                    $ApplyButton.on("click", function () {
                        TimExpenSeDoc = $self.vSearchTimeAndExpense.getValue();
                        C_Task_ID = $self.vSearchTask.getValue();
                        C_Project_ID = $self.vSearchProject.getValue();
                        R_Request_ID = $self.vSearchRequest.getValue();
                        executeQuery();
                    });

                    //SelectionDialog.onClose = function () {
                    //    SelectionDispose();

                    //};

                    //function SelectionDispose() {
                    //    TableDir = null;
                    //    TimExpenSeDoc = null;
                    //    C_Task_ID = null;
                    //    C_Project_ID = null;
                    //    R_Request_ID = null;
                    //}

                });
            }
            if (this.SearchBtn != null)
                this.SearchBtn.on(VIS.Events.onTouchStartOrClick, function () {
                    $self.SearchBtn.removeAttr('disabled');

                    AD_Org_ID = $self.cmbOrg.getControl().find('option:selected').val();
                    C_BPartner_ID = $self.vSearchCustomer.getValue();
                    S_Resource_ID = $self.vSearchResource.getValue();
                    executeQuery();

                });
            if (this.PreviewBtn != null) {
                this.PreviewBtn.on(VIS.Events.onTouchStartOrClick, function () {
                    if (LeftGridData.length == 0) {
                        VIS.ADialog.info('VAS_PlzSelectRecord');
                    }
                    LoadPreviewDialog(gridDataArray);
                });
            }
            if (this.GenerateInvBtn != null) {
                this.GenerateInvBtn.on(VIS.Events.onTouchStartOrClick, function () {
                    if (LeftGridData.length == 0) {
                        VIS.ADialog.info('VAS_PlzSelectRecord');
                    }
                    GenerateInvoice(gridDataArray);
                });
            }

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
                    $self.btnSpaceDiv.animate({ width: sideDivWidth }, "slow");
                    $self.gridSelectDiv.animate({ width: selectDivWidth }, "slow");
                    $self.div.css("display", "block");
                    $self.sideDiv.animate({ width: sideDivWidth }, "slow", null, function () {
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
                    $self.sideDiv.animate({ width: minSideWidth }, "slow");
                    $self.div.css("display", "none");
                    $self.gridSelectDiv.animate({ width: selectDivFullWidth }, "slow", null, function () {
                        $self.dGrid.resize();
                    });
                }
            });


            if (this.spnSelect != null)
                this.spnSelect.on(VIS.Events.onTouchStartOrClick, function () {
                    if (!toggleside) {
                        $self.btnRefresh.show();
                        $self.okBtn.show();
                        $self.gridGenerateDiv.css("display", "none");
                        $self.gridSelectDiv.css("display", "block");
                        $self.sideDiv.css("display", "block");
                        $self.div.css("display", "block");

                        $self.gridSelectDiv.animate({ width: selectDivWidth }, "fast");

                        //$self.btnToggel.removeAttr('disabled');
                        $self.btnToggel.show();

                        lblSelect.css("font-size", "28px").css("color", "rgba(var(--v-c-primary), 1)");
                        lblGenrate.css("font-size", "17px").css("color", "rgba(var(--v-c-on-secondary),1)");
                    }
                });

        }

        this.display = function () {
            dynInit(null);
        }

        //Privilized function
        this.getRoot = function () {
            return this.$root;
        };

        this.refreshUI = function () {
            /*Refresh Grid on Focus*/
            this.dGrid.resize();
        };

        this.disposeComponent = function () {

            if (this.SearchBtn)
                this.SearchBtn.off(VIS.Events.onTouchStartOrClick);
            if (btnClearBP)
                btnClearBP.off(VIS.Events.onTouchStartOrClick);
            if (btnClearResource)
                btnClearResource.off(VIS.Events.onTouchStartOrClick);
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
    VIS.Apps = VIS.Apps || {};
    VIS.Apps.AForms = VIS.Apps.AForms || {};
    VIS.Apps.AForms.VAS_TimeSheetInvoice = VAS_TimeSheetInvoice;


})(VIS, jQuery);