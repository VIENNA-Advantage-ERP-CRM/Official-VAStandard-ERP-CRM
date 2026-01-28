; VAS = window.VAS || {};
; (function (VAS, $) {
    VAS.TabAlertRuleSql = function () {
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        this.ParentId = 0;
        var $self = this;
        var pageSize = 100;
        var autoComValue = null;
        var pageNo = 1;
        var tableID, EventTable = 0;
        var tabID = 0;
        var joinData = null;
        var addedOptions = [];
        var joinColumnName = [];
        var alertID = 0;
        var alertRuleID = 0;
        var mainTableName = null;
        var filterArrayIndex = 0;
        var andFlag = true;
        var sqlGenerateFlag, filterArrayReset = false;
        var sqlFlag = true;
        var filterIndex = null;
        var WhereCondition;
        var joinsArray, $filterCurrentDate, $filterDateList, isDynamic, txtYear, txtMonth, txtDay, txtIsInsert,
            txtIsUpdate, txtIsDelete, windowTabCtrl, FiledColumnCtrl, okBtn, lblBottomMsg;
        var joinTable;
        var seletedJoinCloumn = [];
        var sortValuesArray = [];
        var whereJoinClause = '';
        var $query = $("<div>");
        var gridDiv = $("<div class='vas-grid-div1'>");
        var gridDiv2 = $("<div class='vas-grid-div2'>");
        var $joinMultiSelect = $("<div class='vas-join-multiselect'>");
        var $sqlResultDiv = $("<div class='vas-sql-result-msg'>");
        var $sqlGeneratorBtn = null;
        var $testSqlGeneratorBtn = null;
        var $testSqlBtn = null;
        var $saveBtn = null;
        var $saveGeneratorBtn = null;
        var $addJoinBtn = null;
        var $addFilterBtn = null;
        var $addSortBtn = null;
        var sqlGridCols = [];
        var sqlGeneratorGridCols = [];
        var filterArray, isEmailCtrl, emailColNameCtrl, txtIsEmail, txtEmailColName, emailContentDiv,
            isInserCtrl, isUpdateCtrl, isDeleteCtrl, CRUDContentCtrl, UpdateColumnCtrl, txtWindowTab, txtFiledColumn;
        var sortedIndex = -1;
        var pagePrev, pageNext = null;
        var totalPages = 1;
        var recordCount= 0;
        this.dGrid = null;
        this.dGridGen = null;
        this.BasedOn = "";
        var $windowTabDiv = $("<div class='vas-windowtab'><label>" + VIS.Msg.getMsg("VAS_WindowTab") + "</label>");
        var $joinSearch = $("<div class='vas-windowtab vas-searchinput-block'><label>" + VIS.Msg.getMsg("VAS_WindowTabAddition") + "</label>");
        var $multiSelectArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $multiSelectJoinArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $windowTabSelect = null;
        var $txtWindow = $("<div class='VIS-AMTD-formData VIS-AMTD-InputBtns input-group vas-input-wrap'>");
        var $windowFieldColumnSelect = $("<div class='vas-windowfieldcol-select vas-windowtab vis-ev-col-mandatory' style='display: none;'><label>" + VIS.Msg.getMsg("VAS_FieldColumn") + "</label></div>");
        var $joinsSelect = $("<div style='display: none;' class='vas-joins-select vas-common-style vas-windowtab'><label>" + VIS.Msg.getMsg("VAS_FieldColumn") + "</label>");
        var $selectGenQuerySqlText = $("<div class='vas-sqlquery-text'>")
        var $sqlGenDeleteIcon = $("<i class='vis vis-delete vas-filters-editdelete-btns'></i>");
        var $selectQuery = $("<div class='vas-query-input vas-sql-query-input' id='query-input' contenteditable='true'>");
        var $selectGeneratorQuery = $("<div class='vas-query-input' contenteditable='false'>");
        var $multiSelect = $("<div class='vas-multiselect'>");
        var $selectBox = $("<div class='vas-selectBox vas-windowtab'><select><option>" + VIS.Msg.getMsg("VAS_SelectAll") + "</option></select><div class='vas-overselect'></div></div>");
        var $checkBoxes = $("<div class='vas-checkboxes'>");
        var $joins = $("<div class='vas-joins'>");
        var $joinsDiv = $("<div class='vas-add-label'><label>" + VIS.Msg.getMsg("VAS_Joins") + "</label>");
        var $column1Div = $("<div class='vas-column1'>");
        var $addJoinsDiv = $("<div class='vas-add-label-content'><h4>" + VIS.Msg.getMsg("VAS_AddJoin") + "</h4>");
        var $joinsWindowTabSelect = null;
        var $txtJoinWindow = $("<div class='VIS-AMTD-formData VIS-AMTD-InputBtns input-group vas-input-wrap'>");
        var $joinsDropdown = null;
        var $filterEditDiv = $("<div style='display:none;'>");
        var $joinOnFieldColumnMainTable = null;
        var $joinOnFieldColumnJoinTable = null;
        var $filters = $("<div class='vas-filters'>");
        var $filterDiv = $("<div class='vas-add-label'><label>" + VIS.Msg.getMsg("VAS_Condition") + "</label>");
        var $addFilterSelectButton = $("<div class='vas-select-plus-btn'>");
        var addConditionLabel = $("<div class='vas-addfiltertext'>" + VIS.Msg.getMsg('VAS_AddFilter') + "</div>");
        var $addFilterDiv = $("<div class='vas-add-label-content'>");
        var $filterOperatorDiv = $("<div class='vas-windowtab vas-operator'><label>" + VIS.Msg.getMsg("Operator") + "</label>");
        var $filterValueDiv = $("<div class='vas-windowtab vas-columnval vas-single-input vas-showHideColumn'><label>" + VIS.Msg.getMsg("VAS_ColumnValue") + "</label>");
        var $filterColumnName = $("<div class='vas-filter-text-input vas-single-selection-dropdown'>");
        var $filterValDropdown = $("<div class='vas-filter-val'>");
        var $filterOperator = $("<select class='vas-filtercondn'><option>=</option><option><></option><option>></option><option><</option><option><=</option><option>>=</option><option>LIKE</option><option>NOT LIKE</option><option>IS NULL</option><option>IS NOT NULL</option></select>");
        var $filterValue = $("<input type='textbox' class='vas-filter-text-input'>");
        var $fieldColDropdownBlock = $("<div class='vas-fielddropdown vas-windowtab'><label>" + VIS.Msg.getMsg("VAS_FieldColumn") + "</label>");
        var $fieldJoinColDropdownBlock = $("<div class='vas-joinfielddropdown vas-windowtab'><label>" + VIS.Msg.getMsg("VAS_FieldColumn") + "</label>");
        var $filterConditionV2 = null;
        var switchValueBtn = $('<i data-info="Y" class="fa fa-exchange" title="' + VIS.Msg.getMsg('SwapColumnValue') + '"></i>');
        var $sortElements = null;
        var $sortSelectArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $filterSelectArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $filterValSelectArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $baseTableSelectArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $joiningTableSelectArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $sortColumnInput = $("<input class='vas-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $filterColumnInput = $("<input class='vas-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $filterValInput = $("<input class='vas-select-col' data-input='oldColumn' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $baseTableJoinInput = $("<input class='vas-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $joiningTableInput = $("<input class='vas-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $fieldColDropdown = $("<input class='vas-select-col vis-ev-col-mandatory' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $fieldJoinColDropdown = $("<input class='vas-join-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $sortByDiv = $("<div class='vas-add-label'><label>" + VIS.Msg.getMsg("VAS_Sort") + "</label>");
        var $filterofMultipleColumns = $("<div class='vas-filterofMultipleCols'>");
        var $filterNewColumn = $("<div class='vas-single-input vas-windowtab vas-searchinput-block vas-filterNewCol' style='display: none;'><label>" + VIS.Msg.getMsg("VAS_CompareColumn") + "</label>");
        var $filterNewColumnInput = $("<input class='vas-select-col' data-input='newColumn' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $filterNewColumnArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $addSortByDiv = $("<div class='vas-add-label-content'><h4>" + VIS.Msg.getMsg("VAS_AddSort") + "</h4>");
        var $sortByDropdown = $("<div class='vas-sortby-dropdown'>");
        var $sortInputBlock = $("<div class='vas-single-input vas-windowtab vas-sort-block'><label>" + VIS.Msg.getMsg("VAS_FieldColumn") + "</label>");
        var $filterInputBlock = $("<div class='vas-single-input vas-windowtab vas-searchinput-block'><label>" + VIS.Msg.getMsg("VAS_FieldColumn") + "</label>");
        var $filterValExchangeIconBlock = $("<div class='vas-filterValExchangeIconBlock vas-showHideColumn' style='display: none;'>");
        var $filterValBlock = $("<div class='vas-single-input vas-windowtab vas-searchinput-block vas-filterValBlock'><label>" + VIS.Msg.getMsg("VAS_FieldColumn") + "</label>");
        var $filterCol2Block = $("<div class='vas-single-input vas-windowtab vas-searchinput-block vas-filterCol2Block vas-single-selection-dropdown'>");
        var $baseTableInputBlock = $("<div class='vas-single-input vas-windowtab'><label>" + VIS.Msg.getMsg("VAS_MainTable_Column") + "</label>");
        var $joiningTableInputBlock = $("<div class='vas-single-input vas-joiningtable-input vas-windowtab'><label>" + VIS.Msg.getMsg("VAS_JoinTable_Column") + "</label>");
        var $addSortBySelectWithButton = $("<div class='vas-select-plus-btn vas-addsort-btn'>");
        var $sqlBtns = $("<div class='vas-sql-btns'>");
        var $contentArea = $("<div class='vas-content-area'>");
        var $queryMessage = $("<div class='vas-query-message'>");
        var $sqlContent = $("<div class='vas-sql-content'><div class='vas-sqlquery-text'>" + VIS.Msg.getMsg("VAS_SQL") + "</div></div>");
        var $queryResultGrid = $("<div style='display:none;' class='vas-queryresult-grid'></div>");
        var $sqlGeneratorContent = null;
        var $sqlGeneratorQueryResultGrid = $("<div style='display:none;' class='vas-queryresult-grid'></div>");

        var $filterDateDiv = $('<div class="vis-advanedSearch-InputsWrap vis-advancedSearchMrgin vas-dateBlock">'
            + '<div id="currentDate" class= "vis-form-group vis-advancedSearchInput1" ><input type="checkbox" '
            + 'id="chkDynamic" name="IsDynamic" class="vis-pull-left"><label for="IsDynamic">' + VIS.Msg.getMsg("IsDynamic") + '</label></div>'
            + '<div class="vis-form-group vis-advancedSearchInput">'
            + '<select id="drpDynamicOp" disabled>'
            + '<option>' + VIS.Msg.getMsg("Today") + '</option>'
            + '<option>' + VIS.Msg.getMsg("lastxDays") + '</option>'
            + '<option>' + VIS.Msg.getMsg("lastxMonth") + '</option>'
            + '<option>' + VIS.Msg.getMsg("lastxYears") + '</option>'
            + '</select></div>'
            + '<div class= "vis-form-group vis-advancedSearchHorigontal vis-pull-left"id = "divYear" style = "display: none;" >'
            + '<input id = "txtYear" type = "number" min = "1" max = "99" > <label for="Year">' + VIS.Msg.getMsg("Year") + '</label>'
            + '</div>'
            + '<div class="vis-form-group vis-advancedSearchHorigontal vis-pull-left" id="divMonth" style="display: none;">'
            + ' <input id="txtMonth" type="number" min="1" max="12">'
            + '<label for="Month">' + VIS.Msg.getMsg("Month") + '</label></div><div class="vis-form-group vis-advancedSearchHorigontal vis-pull-left" id="divDay" style="">'
            + '<input id="txtDay" type="number" min="0" max="31">'
            + '<label for="Day">' + VIS.Msg.getMsg("Day") + '</label></div></div>');
        var sqlQuery = VIS.Msg.getMsg("VAS_SQLQuery");
        var $sortCollection = $("<div class='vas-sortCollection'>");
        var testSQL = VIS.Msg.getMsg("VAS_TestSql");
        var joinCommonColumn, $bsyDiv = null;
        var record = [];
        var Joinrecord = [];
        var $root = $("<div class='vas-root'>");
        var $SQLMainDiv = $("<div class='vas-root-main'>");
        var $EventMainDiv = $("<div style='display:none;' class='vas-root-eventmain'>");

        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap" style="visibility: hidden;"></div>');
            $bsyDiv.append($('<div class="vis-busyindicatorinnerwrap"><i class="vis-busyindicatordiv"></i></div>'));
            setBusy(false);
            $root.append($bsyDiv);
        };

        /**
         * function used to show/hide busy indicator
         * @param {any} isBusy
         */
        function setBusy(isBusy) {
            if (isBusy) {
                $bsyDiv[0].style.visibility = "visible";
            }
            else {
                $bsyDiv[0].style.visibility = "hidden";
            }
        };

        // Initialize UI Elements
        this.init = function () {
            $sqlBtn = $("<input class='VIS_Pref_btn-2 vas-sql-btn' id='vas-sql-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_SQL") + "'>");
            $sqlGeneratorBtn = $("<input class='VIS_Pref_btn-2 vas-sql-generator' id='vas-sql-generatorbtn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_SQLGenerator") + "'>");
            $testSqlGeneratorBtn = $("<input style='display: none;' class='VIS_Pref_btn-2 vas-test-sql vas-test-sqlgenerator' id='vas-testsql-generatorbtn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_TestSql") + "'>");
            $testSqlBtn = $("<input class='VIS_Pref_btn-2 vas-test-sql' id='vas-testsql-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_TestSql") + "'>");
            $saveBtn = $("<input class='VIS_Pref_btn-2 vas-save-btn' type='button' id='vas-save-btn" + $self.windowNo + "' value='" + VIS.Msg.getMsg("VAS_Save") + "'>");
            $saveGeneratorBtn = $("<input class='VIS_Pref_btn-2 vas-save-btn mt-0' id='vas-savegenerator-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_Save") + "'>");
            pagingDiv = $('<div>' +
                '<ul class="vis-ad-w-p-s-plst">' +
                '<li style="opacity: 0.6;"><div><i class="vis vis-pageup" id="VAS_pageUp' + $self.windowNo + '"></i></div></li>' +
                '<li>' +
                '<select class="vis-statusbar-combo" id= "VAS_dropDownSelect' + $self.windowNo + '"><option>1</option></select>' +
                '</li>' +
                '<li style="opacity: 0.6;"><div><i class="vis vis-pagedown"  id= "VAS_pageDown' + $self.windowNo + '"></i></div></li>' +
                '</ul>' +
                '<div>');

            pagingPlusBtnDiv = $('<div class="VAS-pagingDiv d-none">');
            $addJoinBtn = $("<input class='VIS_Pref_btn-2 vas-add-join-btn' id='vas-addjoin-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_AddJoin") + "'>");
            $addFilterBtn = $("<input class='VIS_Pref_btn-2 vas-add-btn' id='vas-addfilter-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_AddFilter") + "'>");
            $addSortBtn = $("<input class='VIS_Pref_btn-2 vas-add-btn' id='vas-addsort-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_AddSort") + "'>");
            $sqlGeneratorContent = $("<div class='vas-sqlgenerator' id='vas-sqlgeneratorcontent" + $self.windowNo + "' style='display: none;'><div class='vas-sqlgenerator-column vas-sqlgenerator-column1'></div><div class='vas-sqlgenerator-column vas-sqlgenerator-column2'></div></div>");
            $joinsDropdown = $("<select class='vas-joins-collection'><option>INNER JOIN</option><option>LEFT OUTER JOIN</option><option>RIGHT OUTER JOIN</option><option>FULL JOIN</option></select>");
            $joinOnFieldColumnMainTable = $("<div class='vas-single-selection-dropdown'>");
            $joinOnFieldColumnJoinTable = $("<div class='vas-single-selection-dropdown'>");
            $filterConditionV2 = $("<select><option>AND</option><option>OR</option></select>");
            $sortElements = $("<select><option>ASC</option><option>DESC</option></select>");
            $filterCurrentDate = $filterDateDiv.find('#currentDate');
            $filterDateList = $filterDateDiv.find('#drpDynamicOp');
            isDynamic = $filterDateDiv.find('#chkDynamic');
            txtYear = $filterDateDiv.find('#txtYear');
            txtMonth = $filterDateDiv.find('#txtMonth');
            txtDay = $filterDateDiv.find('#txtDay');
            divDay = $filterDateDiv.find('#divDay');
            divMonth = $filterDateDiv.find('#divMonth');
            divYear = $filterDateDiv.find('#divYear');

            $sqlBtns.append($sqlGeneratorBtn)
                .append($sqlBtn)
                .append($sqlResultDiv)
                .append($testSqlBtn)
                .append($testSqlGeneratorBtn);
            $selectGenQuerySqlText.text(VIS.Msg.getMsg("VAS_SQLQuery"));
            $selectGeneratorQuery.attr('disabled', true);
            $SQLMainDiv.append($sqlBtns).append($contentArea).append(gridDiv).append(gridDiv2);
            $root.append($SQLMainDiv).append($EventMainDiv);
            $contentArea.append($sqlContent)
                .append($sqlGeneratorContent)
                .append($queryMessage);
            $queryResultGrid.append(gridDiv);
            $sqlGeneratorQueryResultGrid.append(gridDiv2);
            $sqlGeneratorContent.find(".vas-sqlgenerator-column1").append($column1Div);
            $column1Div
                .append($windowTabDiv)
                .append($multiSelect)
                .append($joinsDiv)
                .append($addJoinsDiv)
                .append($filterDiv)
                .append($addFilterDiv)
                .append($sortByDiv)
                .append($addSortByDiv);

            $windowTabDiv.append($txtWindow);
            $multiSelect.append($fieldColDropdownBlock);
            $fieldColDropdownBlock.append($fieldColDropdown);
            $fieldColDropdownBlock.append($multiSelectArrow);
            $multiSelect.append($windowFieldColumnSelect).append($checkBoxes);
            $addJoinsDiv.append($joinSearch)
                .append($fieldJoinColDropdownBlock)
                .append($joinsSelect)
                .append($joinMultiSelect)
                .append($joinsDropdown)
                .append($baseTableInputBlock)
                .append($joinOnFieldColumnMainTable)
                .append($joiningTableInputBlock)
                .append($joinOnFieldColumnJoinTable)
                .append($addJoinBtn)
                .append($joins);
            $joinSearch.append($txtJoinWindow);
            $fieldJoinColDropdownBlock.append($fieldJoinColDropdown);
            $fieldJoinColDropdownBlock.append($multiSelectJoinArrow);
            $filterOperatorDiv.append($filterOperator);
            $filterValueDiv.append($filterValue);
            $addFilterDiv.append(addConditionLabel).append($filterInputBlock)
                .append($filterColumnName)
                .append($filterOperatorDiv)
                .append($filterofMultipleColumns)
                .append($filterCol2Block)
                .append($filterValDropdown)
                .append($addFilterSelectButton)
                .append($filterEditDiv);
            $addFilterSelectButton.append($filterConditionV2).append($addFilterBtn);
            $addSortByDiv.append($sortInputBlock).append($sortByDropdown).append($addSortBySelectWithButton).append($sortCollection);
            $sortInputBlock.append($sortColumnInput).append($sortSelectArrow);
            $filterInputBlock.append($filterColumnInput).append($filterSelectArrow);
            $filterValExchangeIconBlock.append($filterValBlock);
            $filterofMultipleColumns.append(switchValueBtn)
                .append($filterValueDiv)
                .append($filterValExchangeIconBlock)
                .append($filterNewColumn)
                .append($filterDateDiv);
            $filterNewColumn.append($filterNewColumnInput).append($filterNewColumnArrow);
            $filterValBlock.append($filterValInput).append($filterValSelectArrow);
            $baseTableInputBlock.append($baseTableJoinInput).append($baseTableSelectArrow);
            $joiningTableInputBlock.append($joiningTableInput).append($joiningTableSelectArrow);
            $addSortBySelectWithButton.append($sortElements).append($addSortBtn);
            $sqlContent.append($selectQuery).append($queryResultGrid);
            $sqlContent.append('<div id="emailContent_' + $self.windowNo + '" class="vas-alert-emailmsg VIS_Pref_show vis-formouterwrpdiv">'
                + '<div class= "VIS_Pref_dd"><div class="input-group vis-input-wrap" id="Is_Email_' + $self.windowNo + '"></div></div>'
                + '<div class= "VIS_Pref_dd"><div style="display:none;" class="input-group vis-input-wrap" id="EmailColName_' + $self.windowNo + '"></div></div>'
                + '</div>');


            $EventMainDiv.append('<div class="vas-windowtab"><label>' + VIS.Msg.getMsg("VAS_WindowTab") + '</label>'
                + '<div id="WindowTab_' + $self.windowNo + '"  class="VIS-AMTD-formData VIS-AMTD-InputBtns input-group vas-input-wrap"></div>'
                + '</div><div id="CRUDContent_' + $self.windowNo + '" class="vas-alert-emailmsg VIS_Pref_show vis-formouterwrpdiv">'
                + '<div class= "VIS_Pref_dd"><div class="input-group vis-input-wrap" id="Is_Insert_' + $self.windowNo + '"></div></div>'
                + '<div class= "VIS_Pref_dd"><div class="input-group vis-input-wrap" id="IsUpdate_' + $self.windowNo + '"></div></div>'
                + '<div class= "VIS_Pref_dd"><div class="input-group vis-input-wrap" id="IsDelete_' + $self.windowNo + '"></div></div>'
                + '</div><div style="display:none;" class="VAS-Alert-window-column" id="updateColumn_' + $self.windowNo + '">'
                + '<div class= "vas-windowtab"><label>' + VIS.Msg.getMsg("VAS_FieldColumn") + '</label>'
                + '<div id="FieldColumn_' + $self.windowNo + '"  class="VIS-AMTD-formData VIS-AMTD-InputBtns input-group vas-input-wrap"></div>'
                + '</div></div>'
                + '<div class="vis-ctrfrm-btnwrp">'
                + '<input id="okBtn_' + $self.windowNo + '" class="VIS_Pref_btn-2" type="button" value="' + VIS.Msg.getMsg("OK") + '">'
                + '<div class="vis-ad-w-p-s-main pull-left"><div class="vis-ad-w-p-s-infoline"></div><div class="vis-ad-w-p-s-msg" style="align-items:flex-end;" id="lblBottomMsg_' + $self.windowNo + '"></div></div>'
                + '</div>');

            createBusyIndicator();

            isEmailCtrl = $sqlContent.find("#Is_Email_" + $self.windowNo);
            emailColNameCtrl = $sqlContent.find("#EmailColName_" + $self.windowNo);
            emailContentDiv = $sqlContent.find("#emailContent_" + $self.windowNo);
            var isEmailCtrlWrap = $('<div class="vis-control-wrap">');
            var emailColCtrlWrap = $('<div class="vis-control-wrap">');
            var isInsertCtrlWrap = $('<div class="vis-control-wrap">');
            var isUpdateCtrlWrap = $('<div class="vis-control-wrap">');
            var isDeleteCtrlWrap = $('<div class="vis-control-wrap">');


            isInserCtrl = $EventMainDiv.find("#Is_Insert_" + $self.windowNo);
            isUpdateCtrl = $EventMainDiv.find("#IsUpdate_" + $self.windowNo);
            isDeleteCtrl = $EventMainDiv.find("#IsDelete_" + $self.windowNo);
            CRUDContentCtrl = $EventMainDiv.find("#CRUDContent_" + $self.windowNo);
            UpdateColumnCtrl = $EventMainDiv.find("#updateColumn_" + $self.windowNo);
            windowTabCtrl = $EventMainDiv.find("#WindowTab_" + $self.windowNo);
            FiledColumnCtrl = $EventMainDiv.find("#FieldColumn_" + $self.windowNo);
            okBtn = $EventMainDiv.find("#okBtn_" + $self.windowNo);
            lblBottomMsg = $EventMainDiv.find("#lblBottomMsg_" + $self.windowNo);

            $sqlGeneratorContent.find('.vas-sqlgenerator-column2').append($selectGenQuerySqlText.append($sqlGenDeleteIcon)).append($selectGeneratorQuery).append(gridDiv2);

            txtIsEmail = new VIS.Controls.VCheckBox("IsEmail", false, false, true, VIS.Msg.getMsg("VAS_SendMail"), null, false);
            isEmailCtrl.append(isEmailCtrlWrap);
            isEmailCtrlWrap.append(txtIsEmail.getControl().addClass("vis-ec-col-lblchkbox"));

            txtEmailColName = new VIS.Controls.VTextBox("EMail", true, false, true);
            emailColNameCtrl.append(emailColCtrlWrap);
            emailColCtrlWrap.append(txtEmailColName.getControl().attr('title', VIS.Msg.getMsg("VAS_TypeEmailColumn")).attr('placeholder', ' ').attr('data-placeholder', '').attr("autocomplete", "off")).append('<label>' + VIS.Msg.getMsg("VAS_EmailColumn")+'</label>');


            txtIsInsert = new VIS.Controls.VCheckBox("IsInsert", false, false, true, VIS.Msg.getMsg("VAS_OnInsert"), null, false);
            isInserCtrl.append(isInsertCtrlWrap);
            isInsertCtrlWrap.append(txtIsInsert.getControl().addClass("vis-ec-col-lblchkbox"));

            txtIsUpdate = new VIS.Controls.VCheckBox("IsUpdate", false, false, true, VIS.Msg.getMsg("VAS_OnUpdate"), null, false);
            isUpdateCtrl.append(isUpdateCtrlWrap);
            isUpdateCtrlWrap.append(txtIsUpdate.getControl().addClass("vis-ec-col-lblchkbox"));

            txtIsDelete = new VIS.Controls.VCheckBox("IsDeleted", false, false, true, VIS.Msg.getMsg("VAS_OnDelete"), null, false);
            isDeleteCtrl.append(isDeleteCtrlWrap);
            isDeleteCtrlWrap.append(txtIsDelete.getControl().addClass("vis-ec-col-lblchkbox"));

            var lookups = new VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.Search, "AD_Tab_ID", 0, false, "AD_Tab.AD_Window_ID IN (SELECT AD_Window_ID FROM AD_Window_Access WHERE AD_Role_ID = " + VIS.Env.getCtx().getAD_Role_ID() + ")");
            txtWindowTab = new VIS.Controls.VTextBoxButton("AD_Tab_ID", true, false, true, VIS.DisplayType.Search, lookups);
            var locDep = txtWindowTab.getControl().attr('placeholder', ' Search here').attr("id", "Ad_Table_ID").css("width", "100%").css("height", "100%");
            var DivSearchCtrlWrap = $('<div class="vas-control-wrap">');
            var DivSearchBtnWrap = $('<div class="input-group-append">');
            windowTabCtrl.css("width", "100%");
            windowTabCtrl.append(DivSearchCtrlWrap).append(DivSearchBtnWrap);
            DivSearchCtrlWrap.append(locDep);
            DivSearchBtnWrap.append(txtWindowTab.getBtn(0));
            DivSearchBtnWrap.append(txtWindowTab.getBtn(1));
            txtWindowTab.setCustomInfo('VAS_AlertSQLGenerator');
            txtWindowTab.getControl().addClass("vis-ev-col-mandatory");

            var fieldlookup = new VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.MultiKey, "AD_Column_ID", 0, false, "");
            txtFiledColumn = new VIS.Controls.VTextBoxButton("AD_Column_ID", true, false, true, VIS.DisplayType.MultiKey, fieldlookup);
            var locDep = txtFiledColumn.getControl().attr('placeholder', ' Search here').attr("id", "AD_Column_ID").css("width", "100%").css("height", "100%");
            var DivSearchCtrlWrap = $('<div class="vas-control-wrap">');
            var DivSearchBtnWrap = $('<div class="input-group-append">');
            FiledColumnCtrl.css("width", "100%");
            FiledColumnCtrl.append(DivSearchCtrlWrap).append(DivSearchBtnWrap);
            DivSearchCtrlWrap.append(locDep);
            DivSearchBtnWrap.append(txtFiledColumn.getBtn(0));
            DivSearchBtnWrap.append(txtFiledColumn.getBtn(1));
            txtFiledColumn.setCustomInfo('VAS_AlertFieldColumn');
            txtFiledColumn.getControl().addClass("vis-ev-col-mandatory");


            // Empty the Condition's Operator value
            $filterOperator.val('');


            /*Multiple column checbox handling*/

            $fieldColDropdown.on("keyup", function () {
                var $fieldColDropdownVal = $(this).val().toLowerCase();
                var $windowMultiSelectItem = $checkBoxes.children('.vas-column-list-item');
                var $windowMultiSelectItemLength = $windowMultiSelectItem.length;
                if ($windowMultiSelectItemLength > 0) {
                    $checkBoxes.show();
                    $windowMultiSelectItem.filter(function () {
                        $(this).toggle($(this).text().toLowerCase().indexOf($fieldColDropdownVal) > -1);
                    });
                }
                else {
                    $checkBoxes.hide();
                }
                if (!$windowMultiSelectItem.is(':visible')) {
                    $checkBoxes.hide();
                }
            });


            isDynamic.on("change", function () {
                var enable = isDynamic.prop("checked");
                $filterDateList.prop("disabled", !enable);
                if (enable) {
                    setDynamicQryControls();
                    $filterOperator.val('>=');
                    $filterOperator.addClass('vas-disabled-icon');
                }
                else {
                    $filterOperator.removeClass('vas-disabled-icon');
                    divYear.hide();
                    divMonth.hide();
                    divDay.hide();
                }
            });

            $filterDateList.on("change", function () {

                setDynamicQryControls();
            });

            $filterNewColumn.find('input').on("keyup", function () {
                var $filterNewColumnVal = $(this).val().toLowerCase();
                var $filterCol2BlockItem = $filterCol2Block.children('.vas-column-list-item');
                var $filterCol2BlockItemLength = $filterCol2BlockItem.length;
                if ($filterCol2BlockItemLength > 0) {
                    $filterCol2Block.show();
                    $filterCol2BlockItem.filter(function () {
                        $(this).toggle($(this).text().toLowerCase().indexOf($filterNewColumnVal) > -1);
                    });
                }
                else {
                    $filterCol2Block.hide();
                }
                if (!$filterCol2BlockItem.is(':visible')) {
                    $filterCol2Block.hide();
                }
            });

            $filterofMultipleColumns.find(switchValueBtn).off(VIS.Events.onTouchStartOrClick);
            $filterofMultipleColumns.find(switchValueBtn).on(VIS.Events.onTouchStartOrClick, function () {
                $filterCol2Block.hide();
                if ($(this).parents('.vas-add-label-content').find('input[type="textbox"]').val() != '') {
                    $filterNewColumn.toggle();
                    if ($filterNewColumn.css('display') != 'none') {
                        $filterValExchangeIconBlock.hide();
                        $filterValueDiv.hide();
                        $filterOperator.removeClass('vas-checkboxoption-hidden');
                        $filterOperator.removeClass('vas-remove-isnulloption');
                        $filterOperator.removeClass('vas-add-isnulloption');
                        $filterOperator.removeClass('vas-remove-likeoption');
                        $filterOperator.addClass('vas-comparision-opeartor');
                        $filterDateDiv.hide();
                        $filterColumnInput.attr('datatype', 13);
                    }
                    else {
                        var columnId = $filterInputBlock.find('input').attr('columnid');
                        $filterColumnName.find('.vas-column-list-item[columnid=' + columnId + ']').trigger('click');
                        var ctrlDataType = $filterInputBlock.find('input').attr('datatype');
                        if (ctrlDataType == VIS.DisplayType.Date || ctrlDataType == VIS.DisplayType.DateTime) {
                            $filterDateDiv.show();
                            $filterColumnName.find('.vas-column-list-item.active').trigger('click');
                        } else {
                            $filterDateDiv.hide();
                        }

                    }
                    //$filterOperator.val('=');
                }
            });


            /*Multiple join column checbox handling*/

            $fieldJoinColDropdown.on("keyup", function () {
                var $joinColDropdownVal = $(this).val().toLowerCase();
                var $joinMultiSelectItem = $joinMultiSelect.children('.vas-column-list-item');
                var $joinMultiSelectItemLength = $joinMultiSelectItem.length;
                if ($joinMultiSelectItemLength > 0) {
                    $joinMultiSelect.show();
                    $joinMultiSelectItem.filter(function () {
                        $(this).toggle($(this).text().toLowerCase().indexOf($joinColDropdownVal) > -1);
                    });
                }
                if (!$joinMultiSelectItem.is(':visible')) {
                    $joinMultiSelect.hide();
                }
            });

            /*searching the sort column when user start typing*/

            $sortColumnInput.on("keyup", function () {
                var $sortByColDropdownVal = $(this).val().toLowerCase();
                var $sortBySelectedItem = $sortByDropdown.children('.vas-column-list-item');
                var $sortBySelectedItemLength = $sortBySelectedItem.length;
                if ($sortBySelectedItemLength > 0) {
                    $sortByDropdown.show();
                    $sortBySelectedItem.filter(function () {
                        $(this).toggle($(this).text().toLowerCase().indexOf($sortByColDropdownVal) > -1);
                    });
                }
                if (!$sortBySelectedItem.is(':visible')) {
                    $sortByDropdown.hide();
                }
                else {
                    $sortBySelectedItem.on(VIS.Events.onTouchStartOrClick, function () {
                        let $sortByItemVal = $(this).attr('value');
                        $(this).parent($sortByDropdown).prev($sortInputBlock).find($sortColumnInput).val($sortByItemVal);
                        sortOnChange($(this), $sortBySelectedItem);
                        $sortByDropdown.hide();
                    });
                }
            });

            /* Hiding filter Value on basis of filter Operator */

            $filterOperator.on("change", function () {
                if ($filterOperator.val() == 'IS NULL') {
                    $filterValExchangeIconBlock.find('input').prop('disabled', true);
                    $filterValueDiv.find('input').prop('disabled', true);
                    $filterValExchangeIconBlock.find('.vas-arrow-down').css('pointer-events', 'none');
                }
                else if ($filterOperator.val() == 'LIKE') {
                    $filterValueDiv.find('input').prop('disabled', false);
                    $filterValueDiv.show();
                    $filterValExchangeIconBlock.hide();
                }
                else {
                    $filterValExchangeIconBlock.find('input').prop('disabled', false);
                    $filterValueDiv.find('input').prop('disabled', false);
                    $filterValExchangeIconBlock.find('.vas-arrow-down').css('pointer-events', 'all');
                }
            });

            /*searching the filter column when user start typing*/
            $filterColumnInput.off("keyup");
            $filterColumnInput.on("keyup", function () {
                var $filterColDropdownVal = $(this).val().toLowerCase();
                var $filterSelectedItem = $filterColumnName.children('.vas-column-list-item');
                if ($filterSelectedItem.length > 0) {
                    $filterColumnName.show();
                    $filterSelectedItem.filter(function () {
                        $(this).toggle($(this).text().toLowerCase().indexOf($filterColDropdownVal) > -1);
                    });
                }
                if (!$filterSelectedItem.is(':visible')) {
                    $filterColumnName.hide();
                }
                else {
                    $filterSelectedItem.off(VIS.Events.onTouchStartOrClick);
                    $filterSelectedItem.on(VIS.Events.onTouchStartOrClick, function () {
                        filterOnChange($(this), $filterSelectedItem);
                    });
                }
            });


            /*searching the  main base table column when user start typing*/

            $baseTableJoinInput.on("keyup", function () {
                var $join1DropdownVal = $(this).val().toLowerCase();
                var $join1SelectedItem = $joinOnFieldColumnMainTable.children('.vas-column-list-item');
                var $join1SelectedItemLength = $join1SelectedItem.length;
                if ($join1SelectedItemLength > 0) {
                    $joinOnFieldColumnMainTable.show();
                    $join1SelectedItem.filter(function () {
                        $(this).toggle($(this).text().toLowerCase().indexOf($join1DropdownVal) > -1);
                    });
                }

                if (!$join1SelectedItem.is(':visible')) {
                    $joinOnFieldColumnMainTable.hide();
                }
                else {
                    $join1SelectedItem.on(VIS.Events.onTouchStartOrClick, function () {
                        let $join1ItemVal = $(this).attr('value');
                        $(this).parent($filterColumnName).prev($baseTableInputBlock).find($baseTableJoinInput).val($join1ItemVal);
                        $join1SelectedItem.removeClass('active');
                        $(this).addClass('active');
                        var activeItemDataType = $joinOnFieldColumnMainTable.children('.vas-column-list-item.active').attr('datatype');
                        $(this).parent($joinOnFieldColumnMainTable).prev($baseTableInputBlock).find($baseTableJoinInput).attr('datatype', activeItemDataType);
                        $joinOnFieldColumnMainTable.hide();
                    });
                    //onTopSelCheckbox($joinOnFieldColumnMainTable);
                }
            });

            /*searching the column of join table when user start typing*/

            $joiningTableInput.on("keyup", function () {
                var $join2DropdownVal = $(this).val().toLowerCase();
                var $join2SelectedItem = $joinOnFieldColumnJoinTable.children('.vas-column-list-item');
                var $join2SelectedItemLength = $join2SelectedItem.length;
                if ($join2SelectedItemLength > 0) {
                    $joinOnFieldColumnJoinTable.show();
                    $join2SelectedItem.filter(function () {
                        $(this).toggle($(this).text().toLowerCase().indexOf($join2DropdownVal) > -1);
                    });
                }
                if (!$join2SelectedItem.is(':visible')) {
                    $joinOnFieldColumnJoinTable.hide();
                }
                else {
                    $join2SelectedItem.on(VIS.Events.onTouchStartOrClick, function () {
                        let $join2ItemVal = $(this).attr('value');
                        $(this).parent($joinOnFieldColumnJoinTable).prev($joiningTableInputBlock).find($joiningTableInput).val($join2ItemVal);
                        $join2SelectedItem.removeClass('active');
                        $(this).addClass('active');
                        var activeItemDataType = $joinOnFieldColumnJoinTable.children('.vas-column-list-item.active').attr('datatype');
                        $(this).parent($joinOnFieldColumnJoinTable).prev($joiningTableInputBlock).find($joiningTableInput).attr('datatype', activeItemDataType);
                        $joinOnFieldColumnJoinTable.hide();
                    });
                    //onTopSelCheckbox($joinOnFieldColumnJoinTable);
                }
            });

            /*
              Click event on Test SQL Button 
              to show the result in Grid with 
              validation on input textbox in which
              user will write the SQL query in 
              SQL Tab
           */

            $testSqlBtn.on(VIS.Events.onTouchStartOrClick, function () {
                if ($selectQuery && $selectQuery.text().length > 0) {
                    if ($sqlBtn.hasClass('active')) {
                        $(this).toggleClass('vas-show-grid');
                        if (!$(this).hasClass('vas-show-grid')) {
                            $(this).val(testSQL);
                            $queryResultGrid.hide();
                            $query.show();
                            $selectQuery.show();
                            emailContentDiv.show();
                            $sqlContent.find($saveBtn).hide();
                            $sqlResultDiv.hide();
                            $sqlContent.removeClass('vas-grid-height');
                        }
                        else {
                            $(this).val(sqlQuery);
                            $sqlContent.append($saveBtn);
                            $sqlContent.find($saveBtn).show();
                            $sqlContent.addClass('vas-grid-height');
                            var query = $selectQuery.text();
                            var match = query.match(/\bFROM\b\s+([a-zA-Z0-9_."]+)/);
                            if (match && match[1]) {
                                mainTableName = match[1].replace(/"/g, ''); // remove quotes if any
                                getResult(query);
                            } else {
                                VIS.ADialog.error("", "", VIS.Msg.getMsg("No table found"));
                            }
                        }
                    }
                }
                else {
                    $sqlResultDiv.text(VIS.Msg.getMsg("VAS_WriteQuery"));
                    $sqlResultDiv.addClass('vas-sql-result-error');
                    $sqlResultDiv.show();
                }
            });

            /*
               Click event on Test SQL Generator Button 
               to show the result in Grid with 
               validation in which user will select 
               the Window/Tab and Column in 
               SQL Generator Tab
            */

            $testSqlGeneratorBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var sqlGeneratorColumn2 = $sqlGeneratorContent.find('.vas-sqlgenerator-column2');
                var hideSaveGeneratorBtn = sqlGeneratorColumn2.find($saveGeneratorBtn);
                if ($selectGeneratorQuery.text() != '') {
                    if ($sqlGeneratorBtn.hasClass('active')) {
                        $(this).toggleClass('vas-show-grid');
                        if (!$(this).hasClass('vas-show-grid')) {
                            $(this).val(testSQL);
                            $selectGeneratorQuery.show();
                            gridDiv2.hide();
                            $sqlGeneratorQueryResultGrid.hide();
                            hideSaveGeneratorBtn.hide();
                            $sqlResultDiv.hide();
                            pagingPlusBtnDiv.addClass('d-none');
                            pagingPlusBtnDiv.removeClass('d-flex justify-content-between align-items-center');

                        }
                        else {
                            $(this).val(sqlQuery);
                            $selectGeneratorQuery.hide();
                            gridDiv2.show();
                            $sqlGeneratorQueryResultGrid.show();
                            sqlGeneratorColumn2.append(pagingPlusBtnDiv);
                            pagingPlusBtnDiv.append(pagingDiv).append($saveGeneratorBtn)
                            sqlGeneratorColumn2.find($saveGeneratorBtn).show();
                            var query = $selectGeneratorQuery.text();
                            getResult(query);
                            pagePrev = $root.find('#VAS_pageUp' + $self.windowNo);
                            pageNext = $root.find('#VAS_pageDown' + $self.windowNo);
                            pagePrev.addClass('VA107-disablePage');
                            pagePrev.parents('li').css('pointer-events', 'none');
                            pagePrev.off(VIS.Events.onTouchStartOrClick);
                            pagePrev.on(VIS.Events.onTouchStartOrClick, function () {
                                pageNo = pagingDiv.find("select").val() - 1;
                                if (pageNo <= 1) {
                                    pagePrev.css("opacity", "0.6");
                                    pageNext.removeClass('VA107-disablePage');
                                    pageNext.css("opacity", "1");
                                    if (pageNo < 1) {
                                        return;
                                    }
                                }
                                else if (pageNo > 1) {
                                    pagePrev.removeClass('VA107-disablePage');
                                    pagePrev.parents('li').css('pointer-events', 'all');
                                    pageNext.removeClass('VA107-disablePage');
                                    pagePrev.css("opacity", "1");
                                    pageNext.css("opacity", "1");
                                }
                                pagingDiv.find("select").val(pageNo);
                                var query = $selectGeneratorQuery.text();
                                getResult(query);
                            });

                            pageNext.off(VIS.Events.onTouchStartOrClick);
                            pageNext.on(VIS.Events.onTouchStartOrClick, function () {
                                pageNo = parseInt(pagingDiv.find("select").val()) + 1;
                                pagePrev.removeClass('VA107-disablePage');
                                pagePrev.parents('li').css('pointer-events', 'all');
                                pagePrev.css("opacity", "1");
                                if (pageNo >= totalPages) {
                                    pageNext.addClass('VA107-disablePage');
                                    pageNext.css("opacity", "0.6");
                                    if (pageNo > totalPages) {
                                        return;
                                    }
                                }
                                pagingDiv.find("select").val(pageNo);
                                var query = $selectGeneratorQuery.text();
                                getResult(query);
                            });
                        }
                    }
                }
                else {
                    $sqlResultDiv.show();
                    $sqlResultDiv.text(VIS.Msg.getMsg("VAS_DisplayQuery"));
                    $sqlResultDiv.addClass('vas-sql-result-error');
                }
                $fieldColDropdown.val('');
                $fieldJoinColDropdown.val('');
            });

            pagingDiv.find("select").on("change", function () {
                pageNo = parseInt($(this).val());
                if (pageNo >= totalPages) {
                    pageNext.css("opacity", "0.6");
                    pageNext.addClass("VA107-disablePage");
                    pagePrev.css("opacity", "1");
                    pagePrev.removeClass("VA107-disablePage");
                }
                else {
                    pageNext.css("opacity", "1");
                    pageNext.removeClass("VA107-disablePage");
                }
                if (pageNo <= 1) {
                    pagePrev.css("opacity", "0.6");
                    pagePrev.addClass("VA107-disablePage");
                    pageNext.css("opacity", "1");
                    pageNext.removeClass("VA107-disablePage");
                }
                else {
                    pagePrev.css("opacity", "1");
                    pagePrev.removeClass("VA107-disablePage");
                }
                var query = $selectGeneratorQuery.text();
                getResult(query);
                /*   selectPage.find("option[value='" + pageNo + "']").prop("selected", true);*/
            });


            /*
             Click event on Arrow
             to display the data in form of multiple checkboxes 
          */
            $multiSelectArrow.on(VIS.Events.onTouchStartOrClick, function () {
                let windowFieldItem = $checkBoxes.children('.vas-column-list-item').length;
                let $windowMultiSelectItem = $checkBoxes.children('.vas-column-list-item');
                if (windowFieldItem > 0) {
                    $checkBoxes.toggle();
                    $checkBoxes.toggleClass('vas-open-col');
                    $windowMultiSelectItem.show();
                }
                if (!$windowMultiSelectItem.is(':visible')) {
                    $fieldColDropdown.val('');
                    $checkBoxes.hide();
                }
                //onTopSelCheckbox($checkBoxes);
            });

            $sortSelectArrow.on(VIS.Events.onTouchStartOrClick, function () {
                let $sortByItemLength = $sortByDropdown.children('.vas-column-list-item').length;
                let $sortByItem = $sortByDropdown.children('.vas-column-list-item');
                if ($sortByItemLength > 0) {
                    if ($sortByDropdown.css('display') === 'none') {
                        $sortByDropdown.css({
                            'display': 'flex',
                            'flex-wrap': 'wrap'
                        });
                    } else {
                        $sortByDropdown.css('display', 'none');
                    }
                    $sortByItem.show();
                }
                if (!$sortByItem.is(':visible')) {
                    $sortByDropdown.hide();
                }
                else {
                    $sortByItem.on(VIS.Events.onTouchStartOrClick, function () {
                        let $sortByItemVal = $(this).attr('value');
                        $(this).parent($sortByDropdown).prev($sortInputBlock).find($sortColumnInput).val($sortByItemVal);
                        $sortByDropdown.hide();
                    });
                }
            });

            $baseTableSelectArrow.on(VIS.Events.onTouchStartOrClick, function () {
                let $join1Length = $joinOnFieldColumnMainTable.children('.vas-column-list-item').length;
                let $join1Item = $joinOnFieldColumnMainTable.children('.vas-column-list-item');
                if ($join1Length > 0) {
                    $joinOnFieldColumnMainTable.toggle();
                    $join1Item.show();
                }
                if (!$join1Item.is(':visible')) {
                    $joinOnFieldColumnMainTable.hide();
                }
                else {
                    $join1Item.on(VIS.Events.onTouchStartOrClick, function () {
                        let $join1ItemVal = $(this).attr('value');
                        $(this).parent($joinOnFieldColumnMainTable).prev($baseTableInputBlock).find($baseTableJoinInput).val($join1ItemVal);
                        $joinOnFieldColumnMainTable.hide();
                    });
                }
            });

            $joiningTableSelectArrow.on(VIS.Events.onTouchStartOrClick, function () {
                let $join2Length = $joinOnFieldColumnJoinTable.children('.vas-column-list-item').length;
                let $join2Item = $joinOnFieldColumnJoinTable.children('.vas-column-list-item');
                if ($join2Length > 0) {
                    //$joinOnFieldColumnJoinTable.toggle();
                    if ($joinOnFieldColumnJoinTable.css('display') === 'none') {
                        $joinOnFieldColumnJoinTable.css({
                            'display': 'flex',
                            'flex-wrap': 'wrap',
                            'float': 'none'
                        });
                    }
                    else {
                        $joinOnFieldColumnJoinTable.css('display', 'none');
                    }
                    $join2Item.show();
                }
                if (!$join2Item.is(':visible')) {
                    $joinOnFieldColumnJoinTable.hide();
                }
                else {
                    $join2Item.on(VIS.Events.onTouchStartOrClick, function () {
                        let $join2ItemVal = $(this).attr('value');
                        $(this).parent($joinOnFieldColumnJoinTable).prev($joiningTableInputBlock).find($joiningTableInput).val($join2ItemVal);
                        $joinOnFieldColumnJoinTable.hide();
                    });
                }
            });

            $filterSelectArrow.off(VIS.Events.onTouchStartOrClick);
            $filterSelectArrow.on(VIS.Events.onTouchStartOrClick, function () {
                var $filterSelectedItem = $filterColumnName.children('.vas-column-list-item');
                let $filterItem = $filterColumnName.children('.vas-column-list-item');
                if ($filterSelectedItem.length > 0) {
                    $filterColumnName.toggle();
                    $filterItem.show();
                }
                if (!$filterItem.is(':visible')) {
                    $filterColumnName.hide();
                }
                else {
                    $filterItem.off(VIS.Events.onTouchStartOrClick);
                    $filterItem.on(VIS.Events.onTouchStartOrClick, function () {
                        filterOnChange($(this), $filterSelectedItem);
                    });
                }
            });

            $filterNewColumnArrow.on(VIS.Events.onTouchStartOrClick, function () {
                var $filterNewColumnSelectedItem = $filterCol2Block.children('.vas-column-list-item');
                let $filterItem = $filterCol2Block.children('.vas-column-list-item');
                if ($filterNewColumnSelectedItem.length > 0) {
                    $filterCol2Block.toggle();
                    $filterItem.show();
                }
                if (!$filterItem.is(':visible')) {
                    $filterCol2Block.hide();
                }
            });

            /* Click Event for Filter Value Select Arrow */
            $filterValSelectArrow.on(VIS.Events.onTouchStartOrClick, function () {
                var $filterValSelectedItem = $filterValDropdown.children('.vas-filterValItem');
                $(this).toggleClass('vas-showFilterVal');
                if ($filterValSelectedItem.length > 0) {
                    $filterValDropdown.toggle();
                    if (!$(this).hasClass('vas-showFilterVal')) {
                        // $filterValBlock.find('input').val('');
                        $filterValSelectedItem.removeClass('vas-selected-filterVal');
                    }
                }
                if (!$filterValSelectedItem.is(':visible')) {
                    $filterValDropdown.hide();
                }
            });

            /*Change the control on basic of column datatype*/
            $multiSelectJoinArrow.on(VIS.Events.onTouchStartOrClick, function () {
                let joinFieldItem = $joinMultiSelect.children('.vas-column-list-item').length;
                let $joinMultiSelectItem = $joinMultiSelect.children('.vas-column-list-item');
                if (joinFieldItem > 0) {
                    $joinMultiSelect.toggle();
                    $joinMultiSelectItem.show();
                }
                if (!$joinMultiSelectItem.is(':visible')) {
                    $fieldJoinColDropdown.val('');
                    $joinMultiSelect.hide();
                }
                //onTopSelCheckbox($joinMultiSelect);
            });

            /*
               Function to collect filter data to display
               on UI when user select the
               dropdowns in Filter Accordion
            */

            let filterArray = [];
            function readFilterData() {
                var data = '';

                for (var i = 0; i < filterArray.length; i++) {

                    var obj = filterArray[i];
                    var dataType = obj.dataType;

                    data += '<div class="vas-filter-item" value="' + obj.filterVal +
                        '" colVersion="' + obj.columnVersion +
                        '" dataType="' + dataType +
                        '" filterId="' + obj.filterValue +
                        '" index="' + i + '">';

                    data += '<div class="vas-filter-whitebg" style="background-color:' + randomColor() + '">';
                    data += '<div class="vas-filters-block">';

                    data += '<div class="vas-filter-andor-value" style="display:none;">' + obj.filterAndOrValue + '</div>';
                    data += '<div class="vas-filter-whereExit" style="display:none;">' + obj.whereExist + '</div>';
                    data += '<div class="vas-filter-datatype" style="display:none;">' + dataType + '</div>';
                    data += '<div class="vas-filter-isDynamic" style="display:none;">' + obj.chkDynamic + '</div>';
                    data += '<div class="vas-filter-day" style="display:none;">' + obj.day + '</div>';
                    data += '<div class="vas-filter-month" style="display:none;">' + obj.month + '</div>';
                    data += '<div class="vas-filter-year" style="display:none;">' + obj.year + '</div>';
                    data += '<div class="vas-filter-dynamicIndex" style="display:none;">' + obj.dynamicIndex + '</div>';

                    // Column name display
                    if ((VIS.DisplayType.Date == dataType || VIS.DisplayType.DateTime == dataType) && obj.chkDynamic == 'N') {
                        data += '<div class="vas-selecttable" title="TO_CHAR(' + obj.filterVal + ', \'yyyy-mm-dd\')">' +
                            " TO_CHAR(" + obj.filterVal + ", 'yyyy-mm-dd')" + '</div>';
                    }
                    else {
                        data += '<div class="vas-selecttable" title="' + obj.filterVal + '">' + obj.filterVal + '</div>';
                    }

                    // Condition
                    data += '<span class="vas-filter-condition">' + " " + obj.filterCondition + " " + '</span>';

                    // VALUE
                    if (obj.filterCondition == 'IS NULL' || obj.filterCondition == 'IS NOT NULL') {
                        data += '<span class="vas-filter-price-value"></span>';
                    }
                    else if (obj.chkDynamic === 'Y') {
                        data += '<span class="vas-filter-price-value">' + obj.filterValue + '</span>';
                    }
                    else if (VIS.DisplayType.String == dataType || VIS.DisplayType.YesNo == dataType || VIS.DisplayType.List == dataType ||
                        VIS.DisplayType.Text == dataType || VIS.DisplayType.TextLong == dataType) {
                        data += '<span class="vas-filter-price-value">\'' + obj.filterValue + '\'</span>';
                    }
                    else if (VIS.DisplayType.DateTime == dataType || VIS.DisplayType.Date == dataType) {
                        data += '<span class="vas-filter-price-value">' + obj.filterValue + '</span>';
                    }
                    else {
                        data += '<span class="vas-filter-price-value">' + obj.filterValue + '</span>';
                    }

                    data += '</div>';

                    // Edit/Delete buttons
                    data += '<div class="vas-filters-editdelete-btns">';
                    data += '<div><i class="vis vis-edit"></i></div>';
                    data += '<div><i class="vis vis-delete"></i></div>';
                    data += '</div>';

                    data += '</div>';
                    data += '</div>';
                }

                $filters.append(data);
            }
            /*
               Function to add filters to display
               on UI when user select the
               dropdowns in Filter Accordion
            */

            function addFilter(value, dataType) {
                // var filterVal = $filterColumnName.children('.vas-column-list-item.active').attr('value');
                var filterVal = $filterColumnInput.val();
                var columnVal = $filterValInput.val();
                var columnVersion = $filterValInput.data('input');
                if ($filterNewColumn.css('display') != 'none') {
                    columnVal = $filterNewColumn.find('input').val();
                    columnVersion = $filterNewColumn.find('input').data('input');
                }
                var chkDynamic = "N";
                let filterCondition = $filterOperator.find('option:selected').val();
                var filterValue = value;
                if (autoComValue != null) {
                    filterValue = autoComValue;
                    autoComValue = null;
                }
                var columnIndex = $selectGeneratorQuery.text().indexOf(filterVal);
                var beforeCondition = $selectGeneratorQuery.text().slice(columnIndex - 6, columnIndex);
                var whereExist = false;
                if (beforeCondition.indexOf('WHERE') > -1) {
                    whereExist = true;
                }
                //var dataType = $filterColumnName.children('.vas-column-list-item.active').attr("datatype");
                if (VIS.DisplayType.YesNo == dataType) {
                    if ($filterValue.is(':checked')) {
                        filterValue = "Y";
                    } else {
                        filterValue = "N";
                    }
                }
                if (isDynamic.is(':checked')) {
                    chkDynamic = "Y";
                    filterValue = getDynamicValue($filterDateList[0].selectedIndex);
                }
                /*if (filterCondition == 'IN') {
                    filterValue = "("+$inDropdownVal+")";
                }*/
                let filterAndOrValue = $filterConditionV2.find('option:selected').val();
                if (filterArrayReset) {
                    filterArray = [];
                    filterArrayReset = false;
                }
                    var dynamicIndex = $filterDateList[0].selectedIndex;
                    var year = txtYear.val();
                    var month = txtMonth.val();
                    var day = txtDay.val();
  
                const filterObj = {
                    filterVal, filterCondition, filterValue, filterAndOrValue,
                    dataType, whereExist, chkDynamic, columnVal, columnVersion, dynamicIndex, year, month, day
                }
                filterArray.push(filterObj);
                $filters.empty();
                readFilterData();
                if ($filters && $filters.length > 0) {
                    $filters.find('.vas-filter-item').addClass('vas-filters-row');
                }
            }

            /*inOperatorArray = [];

            function readInOperatorData() {                
                $inDropdownVal.push($filterValue.val());
                $inOperatorValues.empty();

                for (var i = 0; i < $inDropdownVal.length; i++) {
                    if (inOperatorArray[i]) {
                        inOperatorData += '<div class="vas-selecttable">' + $inDropdownVal[i] +" "+ '</div>';
                        inOperatorData += '</div>';
                    }
                }
                $inOperatorValues.append($inDropdownVal);
            }
            $inOperatorArrow.on(VIS.Events.onTouchStartOrClick, function (event) {
                readInOperatorData();
            });*/

            /*
               Click event on Edit and Delete Buttons 
               for Filters in Filter Accordion
            */

            $filters.on(VIS.Events.onTouchStartOrClick, function (event) {
                var filterItem = $(event.target).parents('.vas-filter-item');

                if ($(event.target).hasClass('vis-delete')) {
                    deleteFilter();
                    filterArray.splice(filterItem.index(), 1);
                    filterItem.remove();
                    if ($('.vas-filters .vas-filter-item').length < 2) {
                        $('.vas-filters .vas-filter-item').removeClass('vas-first-delete-icon');
                    }
                    $filters.empty();
                    readFilterData();
                    if ($filters && $filters.length > 0) {
                        $filters.find('.vas-filter-item').addClass('vas-filters-row');
                    }
                    $sqlResultDiv.hide();
                }
                if ($(event.target).hasClass('vis-edit')) {
                    $addFilterBtn.val(VIS.Msg.getMsg("VAS_UpdateFilter"));
                    updateFilter();
                    var filterSelectTableVal = filterItem.find(".vas-selecttable").text();
                    var filterValue = filterItem.find(".vas-filter-price-value").text();

                    var updatedword = filterValue.replace(/^'|'$/g, '');
                    filterItem.siblings().find(".vas-filter-condition").removeClass('active');
                    filterItem.siblings().find(".vas-filter-price-value").removeClass('active');
                    filterItem.siblings().find(".vas-filter-andor-value").removeClass('active');
                    filterItem.siblings().find(".vas-filter-price-name").removeClass('active');
                    filterItem.siblings().find(".vas-filter-isDynamic").removeClass('active');
                    filterItem.siblings().find(".vas-filter-day").removeClass('active');
                    filterItem.siblings().find(".vas-filter-month").removeClass('active');
                    filterItem.siblings().find(".vas-filter-year").removeClass('active');
                    filterItem.siblings().find(".vas-filter-dynamicIndex").removeClass('active');
                    filterItem.siblings().find(".vas-selecttable").removeClass('active');
                    filterItem.find(".vas-filter-condition").addClass('active');
                    filterItem.find(".vas-filter-price-value").addClass('active');
                    filterItem.find(".vas-filter-andor-value").addClass('active');
                    filterItem.find(".vas-filter-price-name").addClass('active');
                    filterItem.find(".vas-filter-isDynamic").addClass('active');
                    filterItem.find(".vas-filter-day").addClass('active');
                    filterItem.find(".vas-filter-month").addClass('active');
                    filterItem.find(".vas-filter-year").addClass('active');
                    filterItem.find(".vas-filter-dynamicIndex").addClass('active');
                    filterItem.find(".vas-selecttable").addClass('active');
                    filterItem.siblings().removeClass('active');
                    filterItem.addClass('active');

                    if (filterItem.attr('datatype') != 15 && filterItem.attr('datatype') != 16) {
                        $filterColumnInput.val(filterSelectTableVal);
                    }
                    else {
                        var itemValue = $filters.children('.vas-filter-item.active').attr('value');
                        $filterColumnInput.val(itemValue);
                    }
                    var filterCondition = filterItem.find(".vas-filter-condition.active").text().trim();
                    $filterOperator.val(filterCondition);
                    $filterValue.val(updatedword);

                    if (filterValue == "'Y'") {
                        $filterValue.prop('checked', true);
                    }

                    $addFilterBtn.addClass('vas-edit-btn');
                    // $filterSelectArrow.css('pointer-events', 'none');

                    let $filterPriceItem = $filterColumnName.children('.vas-column-list-item');

                    $filterPriceItem.each(function () {
                        let filterPriceEditedVal = $(this).attr('value');
                        if (filterPriceEditedVal === filterSelectTableVal) {
                            $(this).trigger('click');
                        }
                    });
                    var obj = filterArray[filterItem.index()];

                    if (obj.chkDynamic=='Y') {
                        isDynamic.prop("checked", true);
                        $filterOperator.val(">=");
                        $filterOperator.addClass('vas-disabled-icon');

                        // restore dropdown selection
                        $filterDateList.prop("disabled", false);
                        $filterDateList.prop("selectedIndex", obj.dynamicIndex);
                        // show controls properly
                        setDynamicQryControls();;
                        // restore values
                        txtYear.val(obj.year);
                        txtMonth.val(obj.month);
                        txtDay.val(obj.day);

                       
                    }
                    else {
                      //  isDynamic.prop("checked", false);
                       // $filterOperator.removeClass('vas-disabled-icon');
                       // $filterDateList.prop("disabled", true);
                        resetDynamicControls();
                    }



                    $sqlResultDiv.hide();

                    // Edit the paricular value based on specific datatype
                    var versionInfo = filterItem.attr('colversion');
                    var nameText = filterItem.find(".vas-filter-price-name.active").text();
                    var colId = filterItem.find(".vas-filter-price-value.active").text();

                    // Show the Column textbox if data types are 12, 29 etc..
                    var activeItemDataType = $filterColumnName.children('.vas-column-list-item.active').attr('datatype');
                    if (activeItemDataType == 12 || activeItemDataType == 29 || activeItemDataType == 22 || activeItemDataType == 47) {
                        $filterValueDiv.find('input').val(colId);
                    }

                    if (filterItem.hasClass('active')) {
                        var editDataType = filterItem.find(".vas-filter-datatype").text();
                        $filterColumnInput.attr('datatype', editDataType);
                    }

                    // Set the input value & attribute value based on old/new columns
                    if (versionInfo == "newColumn") {
                        $('.vas-select-col[data-input=' + versionInfo + ']').parents('.vas-filterofMultipleCols').find(switchValueBtn).trigger('click');
                        //$filterNewColumnInput.val(oldColText);
                        $filterNewColumnInput.val(colId);
                        $filterNewColumnInput.attr('columnid', colId);
                    }
                    else if (versionInfo == "oldColumn") {
                        if (nameText) {
                            $filterValInput.val(nameText);
                        }
                        else {
                            $filterValInput.val(colId);
                        }
                        $filterValInput.attr('columnid', colId);
                    }
                }
            });

            /*
               Function to update the filter
               in Filter Accordion
            */

            function updateFilter() {
                var filterItem = $(event.target).parents('.vas-filter-item');
                var filterValue = filterItem.find(".vas-filter-price-value").text();
                var filterCondition = filterItem.find(".vas-filter-condition").text();
                var andOrOperator = filterItem.find(".vas-filter-andor-value").text();
                var displayType = filterItem.find(".vas-filter-datatype").text();
                var filterColumn = filterItem.find(".vas-selecttable").text();
                filterArrayIndex = $(event.target).parents('.vas-filter-item').attr('index');
                if (displayType == VIS.DisplayType.Date || displayType == VIS.DisplayType.DateTime) {
                    $filterValue.attr('type', 'date');
                    $filterValue.prev('label').removeClass('vas-label-space');
                }
                else if (displayType == VIS.DisplayType.Integer || displayType == VIS.DisplayType.ID || displayType == VIS.DisplayType.Amount) {
                    $filterValue.attr('type', 'number');
                    $filterValue.prev('label').removeClass('vas-label-space');
                }
                else if (displayType == VIS.DisplayType.YesNo) {
                    $filterValue.attr('type', 'checkbox');
                    $filterValue.prev('label').addClass('vas-label-space');
                }
                else {
                    $filterValue.attr('type', 'textbox');
                    $filterValue.prev('label').removeClass('vas-label-space');
                }
                if (filterArrayIndex == 0) {
                    var oldFilterVal = filterColumn + " " + filterCondition + " " + filterValue;
                } else {
                    var oldFilterVal = andOrOperator + " " + filterColumn + " " + filterCondition + " " + filterValue;
                }
                oldFilterVal = oldFilterVal.replace(/\s{2,}/g, ' ');
                $filterEditDiv.text(oldFilterVal);
                filterIndex = filterItem.attr('index');
            }

            /*
               Click event on SQL Button
               to display UI as per design
            */

            $sqlBtn.on(VIS.Events.onTouchStartOrClick, function () {
                $sqlResultDiv.hide();
                $sqlContent.show();
                $testSqlBtn.show();
                $testSqlGeneratorBtn.hide();
                Active(this);
                $sqlGeneratorContent.hide();
                sqlGenerateFlag = false;
                sqlFlag = true;
            });

            /*
              Click event on SQL Generator Button
              to display UI as per design
           */

            $sqlGeneratorBtn.on(VIS.Events.onTouchStartOrClick, function () {
                let contentHeight = $contentArea.parents('.vis-ad-w-p-ap-tp-o-b-content').height();
                $contentArea.parents('.vis-ad-w-p-ap-tp-o-b-content').css({ "height": contentHeight, "overflow-y": "auto" });
                $sqlResultDiv.hide();
                $sqlGeneratorQueryResultGrid.hide();
                $sqlContent.hide();
                $testSqlGeneratorBtn.show();
                $testSqlBtn.hide();
                Active(this);
                $sqlGeneratorContent.show();
                sqlGenerateFlag = true;
                sqlFlag = false;
            });

            $sqlGeneratorBtn.trigger('click');

            /*
               Joins Accordion
            */

            $joinsDiv.on(VIS.Events.onTouchStartOrClick, function () {
                $(this).siblings().removeClass('active');
                $(this).toggleClass('active');
            });

            /*
               Filters Accordion
            */
            $filterDiv.on(VIS.Events.onTouchStartOrClick, function () {
                $(this).siblings().removeClass('active');
                $(this).toggleClass('active');
            });

            /*
               Sort Accordion
            */

            $sortByDiv.on(VIS.Events.onTouchStartOrClick, function () {
                $(this).siblings().removeClass('active');
                $(this).toggleClass('active');
            });

            /*
               Click event on Window/Field Column 
               to display the data in form of multiple checkboxes 
            */

            $windowFieldColumnSelect.on(VIS.Events.onTouchStartOrClick, function () {
                let windowFieldItem = $checkBoxes.children('.vas-column-list-item').length;
                if (windowFieldItem > 0) {
                    $(this).next().toggle();
                }
            });

            /*
               Click event on Save Button 
               to save the query
            */

            $saveBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var query = $selectQuery.text().trim();
                alertID = VIS.context.getContext($self.windowNo, 'AD_Alert_ID');
                if (alertID > 0) {                    
                    UpdateAlertRule(query);
                }
            });

            /*
              Click event on Save Generator Button 
              to save the query
           */

            $saveGeneratorBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var query = $selectGeneratorQuery.text().trim();
                alertID = VIS.context.getContext($self.windowNo, 'AD_Alert_ID');
                if (alertID > 0) {
                    UpdateAlertRule(query);
                }
            });

            /*
               Click event on Add Filter Button
               to add/update filters
            */

            // Event handler for adding or editing filter
            $addFilterBtn.on(VIS.Events.onTouchStartOrClick, function (event) {
                var filterCondition = $filterOperator.val();
                var filterColumn = $filterColumnInput.val();
                var updatedFilterValue = $filterValue.val();
                var displayType = $filterColumnInput.attr("datatype");

                // Check if the filter value div is visible
                if ($filterValueDiv.css('display') != 'none') {
                    if (autoComValue != null) {
                        updatedFilterValue = autoComValue;
                    }

                    // Check if the display type is Date or DateTime
                    if (VIS.DisplayType.Date == displayType || VIS.DisplayType.DateTime == displayType) {
                        if (!isDynamic.is(':checked') && updatedFilterValue.length > 0) {
                            filterColumn += "TO_CHAR(" + filterColumn + ", 'yyyy-mm-dd')";
                            updatedFilterValue = "'" + updatedFilterValue + "'";
                        }
                        if (isDynamic.is(':checked')) {
                            updatedFilterValue = getDynamicValue($filterDateList[0].selectedIndex);                           
                            filterCondition = $filterOperator.val();
                        }
                    }

                    // Handle Yes/No condition
                    if (displayType == VIS.DisplayType.YesNo) {
                        updatedFilterValue = $filterValue.is(':checked') ? "'Y'" : "'N'";
                    }

                    // Handle other types like String, List, Text
                    if (VIS.DisplayType.String == displayType || VIS.DisplayType.List == displayType
                        || VIS.DisplayType.Text == displayType || VIS.DisplayType.TextLong == displayType) {
                        updatedFilterValue = "'" + updatedFilterValue + "'";
                    }
                }
                // Handling if filter value exchange icon block is visible
                else if ($filterValExchangeIconBlock.css('display') != 'none' && $filterValExchangeIconBlock.find('input').val() != '') {
                    updatedFilterValue = $filterValExchangeIconBlock.find('input').attr('columnid');
                }
                // Handling if a new column is displayed
                else if ($filterNewColumn.css('display') != 'none') {
                    updatedFilterValue = $filterNewColumn.find('input').val();
                    displayType = 13;
                }

                // Make sure all necessary filter values are provided before adding filter
                if (filterColumn != '' && filterCondition != '' && filterCondition != undefined && updatedFilterValue
                    && updatedFilterValue.length > 0) {

                    var WhereCondition = '';
                    // Set WHERE if it hasn't been set yet
                    if (andFlag && $selectGeneratorQuery.text().indexOf('WHERE') == -1) {
                        WhereCondition = "WHERE";
                    }
                    else {
                        WhereCondition = $filterConditionV2.val();
                    }

                    // Edit button logic
                    if ($(this).hasClass('vas-edit-btn')) {
                        $filters.empty();

                        // Get the updated filter condition and operator
                        var updatedFilterConditionValue = $filterOperator.find('option:selected').val();
                        $filters.find('.vas-filter-price-value.active').parents('.vas-filter-item').attr("filterId", updatedFilterValue);

                        var andOrOperator = $filterConditionV2.find('option:selected').val();
                        var oldQuery = $filterEditDiv.text();
                        var sqlGenQuery = $selectGeneratorQuery.text();


                        // Disable dynamic filter inputs while editing
                        txtDay.prop("readonly", true);
                        txtMonth.prop("readonly", true);
                        txtYear.prop("readonly", true);
                        isDynamic.prop("disabled", true);
                        $filterDateList.prop("disabled", true);

                        // Set dynamic value if dynamic checkbox is checked
                        var chkDynamic = isDynamic.is(':checked') ? "Y" : "N";

                        // Enable dynamic checkbox after edit
                        isDynamic.prop("disabled", false);

                        // Handle SQL query generation logic
                        var columnVersion = $filterValInput.data('input');
                        if ($filterNewColumn.css('display') != 'none') {
                            reqFilterVal = $filterNewColumn.find('input').val();
                            columnVersion = $filterNewColumn.find('input').data('input');
                        }

                        var dateType = $filterColumnInput.attr('datatype');
                        var newQuery = generateNewQuery(dateType, filterColumn, updatedFilterValue, updatedFilterConditionValue, andOrOperator, filterArrayIndex);

                        // Update SQL generator query
                        sqlGenQuery = sqlGenQuery.replace(oldQuery, newQuery);
                        $selectGeneratorQuery.text(sqlGenQuery);


                        // Update filterArray with new values
                        var dataTypeReq = $filterColumnInput.attr('datatype');

                        var reqFilterVal = $filterValInput.val();
                        var regex = /(.+)TO_CHAR/;
                        var match = filterColumn.match(regex);
                        if (filterIndex > -1 && filterArray[filterIndex] && typeof filterArray[filterIndex] === 'object') {
                            filterArray[filterIndex].filterCondition = updatedFilterConditionValue;
                            filterArray[filterIndex].columnVal = reqFilterVal;
                            filterArray[filterIndex].filterValue = updatedFilterValue;
                            // Update the value in case of date format
                            if (match != null && (dateType == 15 || dateType == 16)) {
                                filterArray[filterIndex].filterVal = match[1];
                            }
                            else {
                                filterArray[filterIndex].filterVal = filterColumn;
                            }
                            filterArray[filterIndex].columnVersion = columnVersion;
                            filterArray[filterIndex].dataType = dataTypeReq;
                            filterArray[filterIndex].filterAndOrValue = andOrOperator;
                            filterArray[filterIndex].chkDynamic = chkDynamic;
                            if (isDynamic.is(':checked')) {
                                filterArray[filterIndex].isDynamic = true;
                                filterArray[filterIndex].dynamicIndex = $filterDateList[0].selectedIndex;
                                filterArray[filterIndex].year = txtYear.val();
                                filterArray[filterIndex].month = txtMonth.val();
                                filterArray[filterIndex].day = txtDay.val();
                            } else {
                                filterArray[filterIndex].isDynamic = false;
                            }

                        }

                        // Reset UI state after editing
                        $(this).removeClass('vas-edit-btn');
                        ClearText();
                        autoComValue = null;
                        $addFilterBtn.val(VIS.Msg.getMsg("VAS_AddFilter"));
                        $filterSelectArrow.css('pointer-events', 'all');

                        readFilterData();  // Refresh the filter data UI

                    } else {
                        // Add a new filter to the list if edit button wasn't clicked
                        $addFilterDiv.append($filters);
                        ApplyFilter(WhereCondition, updatedFilterValue, displayType);
                        addFilter(updatedFilterValue, displayType);

                        // Reset filter input type and hide date div
                        $filterValue.attr('type', 'text');
                        $filterDateDiv.hide();
                    }

                    // Reset filter column input after adding or editing filter
                    $filterColumnInput.val('');
                    ClearText();

                } else {
                    $sqlResultDiv.text(VIS.Msg.getMsg("VAS_AddFilterValues"));
                    $sqlResultDiv.addClass('vas-sql-result-error');
                }
            });

            /**
             * Function to generate new SQL query based on filter values
             * @param {any} dateType
             * @param {any} filterColumn
             * @param {any} updatedFilterValue
             * @param {any} updatedFilterConditionValue
             * @param {any} andOrOperator
             * @param {any} filterArrayIndex
             */
            function generateNewQuery(dateType, filterColumn, updatedFilterValue, updatedFilterConditionValue, andOrOperator, filterArrayIndex) {
                // Wrap values in quotes based on dateType or other conditions
                var startIndex = filterColumn.indexOf("TO_CHAR(");

                var extracted = "";

                var endIndex = "";

                if (startIndex !== -1) {
                    endIndex = filterColumn.indexOf(")", startIndex) + 1;
                    extracted = filterColumn.slice(startIndex, endIndex);
                }
                var newQuery = "";
                var regex = /(.+)TO_CHAR/;
                var match = filterColumn.match(regex);


                // Function to handle dateType 17 logic
                function wrapInQuotes(value, dateType) {
                    return dateType == 17 ? "'" + value + "'" : value;
                }

                if (filterArrayIndex == 0 && dateType != 15 && dateType != 16) {
                    newQuery = " " + filterColumn + " " + updatedFilterConditionValue + " " + wrapInQuotes(updatedFilterValue, dateType);
                } else if (match != null && (dateType == 15 || dateType == 16)) {
                    if (filterArrayIndex >= 1) {
                        newQuery = andOrOperator + " " + extracted + " " + updatedFilterConditionValue + " " + wrapInQuotes(updatedFilterValue, dateType);
                    } else {
                        newQuery = " " + extracted + " " + updatedFilterConditionValue + " " + wrapInQuotes(updatedFilterValue, dateType);
                    }
                } else if (match == null && (dateType == 15 || dateType == 16)) {
                    if (filterArrayIndex < 1) {
                        newQuery = " " + filterColumn + " " + updatedFilterConditionValue + " " + wrapInQuotes(updatedFilterValue, dateType);
                    }
                } else {
                    newQuery = andOrOperator + " " + filterColumn + " " + updatedFilterConditionValue + " " + wrapInQuotes(updatedFilterValue, dateType);
                }

                return newQuery.replace(/\s{2,}/g, ' '); // Clean up extra spaces
            }

            /*
               Function to collect joins data to display
               on UI when user select the
               dropdowns in Joins Accordion
            */
            function randomColor() {
                var hue = Math.floor(Math.random() * 360);
                var v = Math.floor(Math.random() * 16) + 75;
                var pastel = 'hsl(' + hue + ', 100%, ' + v + '%)';
                return pastel;
            }

            joinsArray = [];
            function readJoinsData() {
                var data = '';
                for (var i = 0; i < joinsArray.length; i++) {
                    data += '<div class=vas-join-item>';
                    data += '<div class="vas-joins-bg">';
                    data += '<div class="vas-joins-block">';
                    data += '<div class="vas-selecttable join-title" style="background-color:' + randomColor() + '">' + joinsArray[i].joinsDropDown + '</div>';
                    data += '<div class="vas-selecttable join-base-table" style="background-color:' + randomColor() + '">' + joinsArray[i].keyColumn1 + '</div>';
                    data += '<div class="vas-selecttable join-tab" style="background-color:' + randomColor() + '">' + joinsArray[i].joinTableName + '</div>';
                    data += '<div class="vas-selecttable join-jointable" style="background-color:' + randomColor() + '">' + joinsArray[i].keyColumn2 + '</div>';
                    if (joinsArray[i].joinSelectedColumn.length > 0) {
                        data += '<div class="vas-selecttable join-joinselectedcolumn" style="background-color:' + randomColor() + '">' + joinsArray[i].joinSelectedColumn + '</div>';
                    }
                    data += '</div>';
                    // data += '<div class="vas-delete-join-btn">';
                    // data += '<div><i class="vis vis-delete"></i></div>';
                    data += '</div>';
                    data += '</div>';
                    data += '</div>';
                }
                $joins.append(data);
            }

            // Click event on Edit and Delete Buttons for Joins

            /* $joins.on(VIS.Events.onTouchStartOrClick, function (event) {
                 if ($(event.target).hasClass('vis-delete')) {
                     var deleteJoin = $(event.target).parents('.vas-delete-join-btn');
                     var joinsTitle = deleteJoin.prev('.vas-joins-block').find('.join-title').text();
                     var joinsTab = deleteJoin.prev('.vas-joins-block').find('.join-tab').text();
                     var joinsBaseTable = deleteJoin.prev('.vas-joins-block').find('.join-base-table').text();
                     var joinsJoinTable = deleteJoin.prev('.vas-joins-block').find('.join-jointable').text();
                     var columnToRemove = ", " + deleteJoin.prev('.vas-joins-block').find('.join-jo
                     
                     inselectedcolumn').text();
                     var reqJoinQuery = joinsTitle + " " + joinsTab + " " + 'ON (' + joinsBaseTable + " = " + joinsJoinTable + ")";
                     if (joinCommonColumn.length > 0 && joinsJoinTable.length > 0) {
                         var joinTableIndex = joinsJoinTable.indexOf('.');
                         if (joinTableIndex > 0) {
                             for (var i = joinCommonColumn.length - 1; i >= 0; i--) {
                                 if (joinCommonColumn[i].TableName === joinsJoinTable.slice(0, joinTableIndex)) {
                                     joinCommonColumn.splice(i, 1);
                                 }
                             }
                         }
                     }
                     $removeJoins = $selectGeneratorQuery.text().replace(reqJoinQuery, '').trim();
                     $selectGeneratorQuery.text($removeJoins);
                     removeColumnJoins = $selectGeneratorQuery.text().replace(columnToRemove, '');
                     $selectGeneratorQuery.empty();
                     $selectGeneratorQuery.text(removeColumnJoins);
                     $(event.target).parents('.vas-join-item').remove();
                     joinsArray.splice($(event.target), 1);
                     $sqlResultDiv.hide();
                 }
             });*/

            $("body").on(VIS.Events.onTouchStartOrClick, function (e) {
                var target = $(e.target);
                var $windowMultiSelectItem = $checkBoxes.children('.vas-column-list-item');
                var $windowMultiSelectItemCheckbox = $checkBoxes.children('.vas-column-list-item').children('.vas-column-checkbox');
                if (!target.is($multiSelectArrow) && !target.is($fieldColDropdown) && !target.is($checkBoxes) && !target.is($windowMultiSelectItem) && !target.is($windowMultiSelectItemCheckbox)) {
                    $checkBoxes.hide();
                    $fieldColDropdown.val('');
                }
                if (!target.is($filterSelectArrow) && !target.is($filterColumnInput) && !target.is($filterColumnName)) {
                    $filterColumnName.hide();
                }
                if (!target.is($baseTableSelectArrow) && !target.is($baseTableJoinInput) && !target.is($joinOnFieldColumnMainTable)) {
                    // $joinOnFieldColumnMainTable.hide();
                    if ($joinOnFieldColumnMainTable == null) {
                        return;
                    }
                }
                if (!target.is($joiningTableSelectArrow) && !target.is($joiningTableInput) && !target.is($joinOnFieldColumnJoinTable)) {
                    $joinOnFieldColumnJoinTable.hide();
                }
                var $joinMultiSelectItem = $joinMultiSelect.children('.vas-column-list-item');
                var $joinMultiSelectItemCheckbox = $joinMultiSelect.children('.vas-column-list-item').children('.vas-column-checkbox');
                if (!target.is($multiSelectJoinArrow) && !target.is($fieldJoinColDropdown) && !target.is($joinMultiSelect) && !target.is($joinMultiSelectItem) && !target.is($joinMultiSelectItemCheckbox)) {
                    $joinMultiSelect.hide();
                    $fieldJoinColDropdown.val('');
                }
                var $filterValItem = $filterValDropdown.children('.vas-filterValItem');
                var $filterValSelectedItemInput = $filterValBlock.find('input');
                if (!target.is($filterValItem) && !target.is($filterValSelectArrow) && !target.is($filterValDropdown) && !target.is($filterValSelectedItemInput)) {
                    $filterValDropdown.hide();
                }
                var $filterCol2Item = $filterCol2Block.children('.vas-column-list-item');
                if (!target.is($filterCol2Item) && !target.is($filterNewColumnArrow) && !target.is($filterCol2Block) && !target.is($filterNewColumnInput)) {
                    $filterCol2Block.hide();
                }
                var $sortByItem = $sortByDropdown.children('.vas-column-list-item');
                if (!target.is($sortByItem) && !target.is($sortSelectArrow) && !target.is($sortByDropdown) && !target.is($sortColumnInput)) {
                    $sortByDropdown.hide();
                }
            });

            /*
               Click event on Add Join Button
               to add joins
            */

            $addJoinBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var joinQuery;
                var joinTableName = joinTable;
                var joinSelectedColumn = seletedJoinCloumn;
                var keyColumn1 = $baseTableJoinInput.val();
                var keyColumn2 = $joiningTableInput.val();
                var sql = $selectGeneratorQuery.text();
                record = record.concat(Joinrecord);
                var joinsDropDown = $joinsDropdown.find('option:selected').val();
                const joinsObj = { joinsDropDown, joinTableName, keyColumn1, keyColumn2, joinSelectedColumn };
                if (joinsDropDown && joinTableName && keyColumn1 && keyColumn2 && joinSelectedColumn) {
                    joinsArray.push(joinsObj);
                }
                if (sql != '' && keyColumn1 != '' && keyColumn2 != '' && keyColumn1 != null && keyColumn2 != null) {
                    var fromIndex = sql.indexOf('FROM');
                    var whereIndex = sql.indexOf('WHERE');
                    var orderIndex = sql.indexOf('ORDER BY');
                    var fromClause = '';
                    var whereClause = '';
                    var orderClause = '';
                    if (whereIndex == -1 && orderIndex == -1) {
                        fromClause = sql.slice(fromIndex);
                    } else if (whereIndex != -1 && orderIndex == -1) {
                        fromClause = sql.slice(fromIndex, whereIndex);
                        whereClause = sql.slice(whereIndex);
                    } else if (whereIndex == -1 && orderIndex != -1) {
                        fromClause = sql.slice(fromIndex, orderIndex);
                        orderClause = sql.slice(orderIndex);
                    } else if (whereIndex != -1 && orderIndex != -1) {
                        fromClause = sql.slice(fromIndex, whereIndex);
                        whereClause = sql.slice(whereIndex, orderIndex);
                        orderClause = sql.slice(orderIndex);
                    }

                    if (fromClause != '') {
                        joinQuery = " " + $joinsDropdown.val() + " " + joinTable + ' ON (' + keyColumn1 + " = " + keyColumn2 + ")";
                        var addJoinQuery = $joinsDropdown.val() + " " + joinTable + ' ON (' + keyColumn1 + " = " + keyColumn2 + ")";
                        $selectGeneratorQuery.text(addJoinQuery);
                        if (seletedJoinCloumn.length > 0) {
                            sql = sql.slice(0, fromIndex) + ", " + seletedJoinCloumn + fromClause + joinQuery;
                            joinColumnName = seletedJoinCloumn;

                        } else {
                            sql = sql.slice(0, fromIndex) + fromClause + joinQuery;
                        }


                        if (whereClause != '') {
                            sql += " " + whereClause
                        }
                        if (whereJoinClause != '') {
                            if (whereIndex != -1) {
                                sql += " AND " + whereJoinClause;
                            } else {
                                sql += " WHERE " + whereJoinClause;
                            }
                        }
                        if (orderClause != '') {
                            sql += " " + orderClause;
                        }
                    }

                    if (joinData != null) {
                        if (joinData && joinData.length > 0) {
                            joinTableName = joinData[0].TableName;
                            joinCommonColumn = joinCommonColumn.concat(joinData);
                            for (var i = 0; i < joinData.length; i++) {
                                var optionValue = joinTableName + "." + joinData[i].DBColumn;
                                var optionText = joinTableName + " - " + joinData[i].ColumnName + " (" + joinData[i].DBColumn + ")";
                                if (!addedOptions.includes(optionValue)) {
                                    $sortByDropdown.append(" <div class='vas-column-list-item' title='" + optionText + "' value=" + optionValue + ">" + optionText + "</div>");
                                    $filterColumnName.append(" <div class='vas-column-list-item' title='" + optionText + "' refValId=" + joinData[i].ReferenceValueID + " fieldID=" + joinData[i].FieldID + " WindowID=" + joinData[i].WindowID + " tabID=" + tabID + " DBColumnName=" + joinData[i].DBColumn + " TableName=" + joinTableName + " columnID=" + joinData[i].ColumnID +
                                        " datatype=" + joinData[i].DataType + " value=" + optionValue + ">" + optionText + "</div>");
                                    $filterCol2Block.append("<div class='vas-column-list-item' title='" + optionText + "' refValId=" + joinData[i].ReferenceValueID + " fieldID=" + joinData[i].FieldID + " WindowID=" + joinData[i].WindowID + " tabID=" + tabID + " DBColumnName=" + joinData[i].DBColumn + " TableName=" + joinTableName + " columnID="
                                        + joinData[i].ColumnID + " datatype=" + joinData[i].DataType + " value=" + optionValue + ">" + optionText + "</div>");
                                    addedOptions.push(optionValue);
                                }
                            }
                        }
                    }

                    $joins.empty();
                    readJoinsData();
                    $selectGeneratorQuery.text('');
                    $selectGeneratorQuery.text(sql);
                    $joinMultiSelect.val('');
                    $joinOnFieldColumnJoinTable.val('');
                    $joinOnFieldColumnMainTable.val('');
                    filterFlag = true;
                    $fieldJoinColDropdown.val('');
                    $baseTableJoinInput.val('');
                    $joiningTableInput.val('');
                    seletedJoinCloumn = [];
                    $joinsWindowTabSelect.setValue(null);
                    getJoinsColumns(0, 0);
                }
            });

            /* Auto complete suggestion for lookups to fileration*/

            autoComValue = null;
            $filterValue.vaautocomplete({
                minLength: 1,
                source: function (term, response) {
                    var displayType = $filterColumnName.children('.vas-column-list-item.active').attr("datatype");
                    var res = [];
                    if (displayType == VIS.DisplayType.TableDir || displayType == VIS.DisplayType.Table || displayType == VIS.DisplayType.Search) {
                        var filtervalue = $filterColumnName.children('.vas-column-list-item.active').attr('value');
                        var tabID = $filterColumnName.children('.vas-column-list-item.active').attr("tabid");
                        var referenceValueID = $filterColumnName.children('.vas-column-list-item.active').attr("refValId");
                        var columnName = filtervalue.slice(filtervalue.indexOf('.') + 1)
                        var columnID = $filterColumnName.children('.vas-column-list-item.active').attr("columnID");
                        var fieldID = $filterColumnName.children('.vas-column-list-item.active').attr("fieldID");
                        var windowId = $filterColumnName.children('.vas-column-list-item.active').attr("WindowID");
                        var validation = "";
                        var d = {
                            'ctx': VIS.Env.getCtx(),
                            'windowNo': VIS.Env.getWindowNo(),
                            'column_ID': columnID,
                            'AD_Reference_ID': displayType,
                            'columnName': columnName,
                            'AD_Reference_Value_ID': referenceValueID,
                            'isParent': true,
                            'validationCode': validation
                        };
                        autoComValue = null;
                        $.ajax({
                            type: 'Post',
                            url: VIS.Application.contextUrl + "Form/GetAccessSqlAutoComplete",
                            data: {
                                columnName: columnName,
                                text: term,
                                WindowNo: $self.windowNo,
                                AD_Window_ID: windowId,
                                AD_Tab_ID: tabID,
                                AD_Field_ID: fieldID,
                                Validation: JSON.stringify(validation),
                                LookupData: JSON.stringify(d)
                            },
                            success: function (data) {
                                res = [];
                                if (JSON.parse(data) != null) {
                                    result = JSON.parse(data).Table;
                                    for (var i = 0; i < result.length; i++) {
                                        var parseObj = {};
                                        parseObj[Object.keys(result[i])[0].toLowerCase()] = result[i][Object.keys(result[i])[0]];
                                        parseObj[Object.keys(result[i])[1].toLowerCase()] = result[i][Object.keys(result[i])[1]];
                                        parseObj[Object.keys(result[i])[2].toLowerCase()] = result[i][Object.keys(result[i])[2]];
                                        res.push({
                                            id: parseObj.id,
                                            value: VIS.Utility.Util.getIdentifierDisplayVal(parseObj.finalvalue)
                                        });
                                    }
                                    response(res);
                                }
                            },
                        });
                    }
                    else if ($filterOperator.val() == 'LIKE' || $filterOperator.val() == 'NOT LIKE') {
                        var searchValue = $filterValue.val();
                        var id = [searchValue + '%',
                        '%' + searchValue,
                        '%' + searchValue + '%'
                        ];
                        var finalValue = [
                            'StartWith_' + searchValue,
                            'EndWith_' + searchValue,
                            'InBetween_' + searchValue
                        ];
                        res = [];
                        for (var i = 0; i < id.length; i++) {
                            res.push({
                                id: id[i],
                                value: VIS.Utility.Util.getIdentifierDisplayVal(finalValue[i])
                            });
                        }
                        response(res);
                    }
                    else {
                        return;
                    }
                },

                onSelect: function (e, item) {
                    autoComValue = item.id;
                }
            });

            /*   Click event on Add Sort Button
               to add sorts like ASC, DESC etc*/
            sortValuesArray = [];
            sortedIndex = -1;

            $addSortBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var sortQuery = "";
                var sortColumn = $sortColumnInput.val();
                var sql = $selectGeneratorQuery.text();
                var direction = $sortElements.val();
                var sortColValue = $sortColumnInput.val();

                // Append subquery in sorting in Add Sort section
                var sortdatatype = $sortColumnInput.attr('sortdatatype');
                var sortcolumnID = $sortColumnInput.attr('sortcolumnID');
                var sortDBColumnName = $sortColumnInput.attr('sortDBColumnName');
                var sortrefValId = $sortColumnInput.attr('sortrefValId');
                var sortisParent = $sortColumnInput.attr('sortisParent');
                var sortTableName = $sortColumnInput.attr('sortTableName');

                if (VIS.DisplayType.IsLookup(sortdatatype)) {
                    var subQuery = GetLookup(sortdatatype, sortcolumnID, sortDBColumnName, sortrefValId, sortisParent, sortTableName);
                    if (subQuery.indexOf(') as') > 0) {
                        sortColumn = subQuery.substring(0, subQuery.indexOf(') as') + 1).trim();
                    }
                }

                var sortObject = {
                    column: sortColumn,
                    reqSortVal: sortColValue,
                    direction: direction,
                    sortdatatype: sortdatatype,
                    sortcolumnID: sortcolumnID,
                    sortDBColumnName: sortDBColumnName,
                    sortrefValId: sortrefValId,
                    sortisParent: sortisParent,
                    sortTableName: sortTableName
                };

                if (!$(this).hasClass('vas-edit-sort')) {
                    if (sql !== '' && sortColumn !== '') {
                        // Push the current sort object to the array
                        sortValuesArray.push(sortObject);
                        $sortColumnInput.val('');
                        displaySortValues();
                        rebuildSQLQuery();
                    }
                } else {
                    $(this).removeClass('vas-edit-sort');
                    $addSortBtn.val(VIS.Msg.getMsg("VAS_AddSort"));
                    $sortColumnInput.val('');

                    if (sortedIndex >= 0 && sortedIndex < sortValuesArray.length) {
                        sortValuesArray[sortedIndex] = sortObject;
                    }

                    displaySortValues();
                    rebuildSQLQuery();
                }
            });

            // Function to display the sort values on the UI
            function displaySortValues() {
                $sortCollection.empty();

                sortValuesArray.forEach(function (sortObject, index) {
                    $sortCollection.append(
                        '<div class="VAS-sortedItem d-flex justify-content-between" data-index=' + index +
                        ' style="background-color: ' + randomColor() + '">' +
                        '<div>' + sortObject.column + ' ' + sortObject.direction + '</div>' +
                        '<div class="vas-sortcolval d-none">' + sortObject.reqSortVal + '</div>' +
                        '<div class="vas-sortColumnName d-none">' + sortObject.column + '</div>' +
                        '<div class="vas-sortDirection d-none">' + sortObject.direction + '</div>' +
                        '<div class="vas-sort-datatype d-none">' + sortObject.sortdatatype + '</div>' +
                        '<div class="vas-sort-columnID d-none">' + sortObject.sortcolumnID + '</div>' +
                        '<div class="vas-sort-DBColumName d-none">' + sortObject.sortDBColumnName + '</div>' +
                        '<div class="vas-sort-refValId d-none">' + sortObject.sortrefValId + '</div>' +
                        '<div class="vas-sort-isParent d-none">' + sortObject.sortisParent + '</div>' +
                        '<div class="vas-sort-TableName d-none">' + sortObject.sortTableName + '</div>' +
                        '<div class="vas-sortBtns">' +
                        '<i class="vis vis-edit" data-index="' + index + '"></i>' +
                        '<i class="vis vis-delete" data-index="' + index + '"></i>' +
                        '</div>' +
                        '</div>'
                    );
                });

                // Delete handler
                $sortCollection.find('.vas-sortBtns i.vis-delete').off(VIS.Events.onTouchStartOrClick).on(VIS.Events.onTouchStartOrClick, function () {
                    var indexToRemove = parseInt($(this).data('index'));
                    sortValuesArray.splice(indexToRemove, 1);
                    displaySortValues();
                    rebuildSQLQuery();
                });

                // Edit handler
                $sortCollection.find('.vas-sortBtns i.vis-edit').off(VIS.Events.onTouchStartOrClick).on(VIS.Events.onTouchStartOrClick, function () {
                    var sortedItem = $(this).closest('.VAS-sortedItem');
                    sortedIndex = parseInt(sortedItem.data('index'));

                    sortedItem.addClass('active').siblings().removeClass('active');

                    $sortColumnInput.val(sortedItem.find('.vas-sortcolval').text());
                    $sortElements.val(sortedItem.find('.vas-sortDirection').text());
                    $sortColumnInput.attr('sortdatatype', sortedItem.find('.vas-sort-datatype').text());
                    $sortColumnInput.attr('sortcolumnID', sortedItem.find('.vas-sort-columnID').text());
                    $sortColumnInput.attr('sortDBColumnName', sortedItem.find('.vas-sort-DBColumName').text());
                    $sortColumnInput.attr('sortrefValId', sortedItem.find('.vas-sort-refValId').text());
                    $sortColumnInput.attr('sortisParent', sortedItem.find('.vas-sort-isParent').text());
                    $sortColumnInput.attr('sortTableName', sortedItem.find('.vas-sort-TableName').text());

                    $addSortBtn.val(VIS.Msg.getMsg("VAS_UpdateSort")).addClass('vas-edit-sort');
                });
            }

            // Rebuilds the SQL query
            function rebuildSQLQuery() {
                var sql = $selectGeneratorQuery.text().replace(/ORDER BY[\s\S]*$/i, '').trim();
                if (sortValuesArray.length > 0) {
                    var orderByClauses = sortValuesArray.map(function (sortObject) {
                        return "LOWER(TRIM(" + sortObject.column + ")) " + sortObject.direction;
                    }).join(", ");
                    sql += " ORDER BY " + orderByClauses;
                }
                $selectGeneratorQuery.text(sql);
            }


            // Clear/refresh sql query
            $sqlGenDeleteIcon.on(VIS.Events.onTouchStartOrClick, function () {
                clear();
                //resetSorting();
            });

            //show query 
            $column1Div.on(VIS.Events.onTouchStartOrClick, function () {
                $saveGeneratorBtn.hide();
                $selectGeneratorQuery.show();
                gridDiv2.hide();
                $sqlGeneratorQueryResultGrid.hide();
                $testSqlGeneratorBtn.val(testSQL);
                $testSqlGeneratorBtn.removeClass('vas-show-grid');
            });

            /*
                Function of add/remove active class on SQL & SQL Generator Buttons
            */
            function Active(e) {
                $sqlBtn.removeClass('active');
                $sqlGeneratorBtn.removeClass('active');
                $(e).addClass('active');
            }
            getWindow();
            getJoinWindow(0, 0);
            eventHandling();
            $filterDateDiv.hide();
        }

        /*
            AJAX Call to get Alert Data
        */
        this.SqlQuery = function (ParentId) {
            setBusy(true);
            var event = VIS.context.getContext(windowNo, 'BasedOn');
            UpdateColumnCtrl.hide()
            $sqlResultDiv.text("");
            $SQLMainDiv.show();
            $EventMainDiv.hide();
            if (event == 'E') {
                $SQLMainDiv.hide();
                $EventMainDiv.show();
            }
            emailContentDiv.hide();
            alertRuleID = ParentId;
            if (alertRuleID > 0) {
                $sqlGeneratorBtn.attr('disabled', true);
                $sqlGeneratorBtn.css("opacity", .3);
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/GetAlertData",
                    type: "POST",
                    data: { alertRuleID: alertRuleID },
                    async: false,
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result) {
                            $self.BasedOn = result.BasedOn;
                            if (result.BasedOn && result.BasedOn == 'S') {
                                emailContentDiv.show();
                                txtEmailColName.setValue(result.EmailColumnName);
                                txtIsEmail.setValue(result.IsEmail);
                                if (result.IsEmail) {
                                    emailColNameCtrl.show();
                                } else {
                                    emailColNameCtrl.hide();
                                }
                            }
                            $selectQuery.text(result.Query);
                            if (!result.Query) {
                                $windowTabSelect.setValue(null);
                                $selectQuery.text('');
                                $sqlGeneratorBtn.attr('disabled', false);
                                $sqlGeneratorBtn.css("opacity", 1);
                                $testSqlBtn.val(testSQL);
                                $queryResultGrid.hide();
                                $query.show();
                                $selectQuery.show();
                                emailContentDiv.show();
                            }
                            if (result.BasedOn && result.BasedOn == 'E') {
                                txtWindowTab.setValue(result.TabID);
                                txtIsInsert.setValue(result.IsInsert);
                                txtIsUpdate.setValue(result.IsUpdate);
                                txtIsDelete.setValue(result.IsDelete);
                                if (result.IsUpdate && result.TabID>0) {
                                    getEventColumn(result.TabID);
                                    txtFiledColumn.setValue(result.ColumnID);
                                    UpdateColumnCtrl.show();
                                } else {
                                    UpdateColumnCtrl.hide();
                                }
                                EventTable = result.TableID;
                                $SQLMainDiv.hide();
                                $EventMainDiv.show();
                            }
                        }
                        setBusy(false);
                    },
                    error: function (error) {
                        console.log(error);
                        setBusy(false);
                    }
                });
                $sqlBtn.trigger('click');
                $sqlResultDiv.show();
            
            } else if (event == 'E') {
                $SQLMainDiv.hide();
                $EventMainDiv.show();
                txtWindowTab.setValue(null);
                txtIsInsert.setValue(false);
                txtIsUpdate.setValue(false);
                txtIsDelete.setValue(false);
                txtFiledColumn.setValue(null);
                EventTable = 0;
                setBusy(false);
            }
            else {
                $windowTabSelect.setValue(null);
                OnChange(0, 'O');
                $selectQuery.text('');
                $sqlGeneratorBtn.attr('disabled', false);
                $sqlGeneratorBtn.css("opacity", 1);
                $testSqlBtn.val(testSQL);
                $queryResultGrid.hide();
                $query.show();
                $selectQuery.show();
                emailContentDiv.show();
                $saveBtn.hide();
                setBusy(false);
            }
        }

        function pageCtrls() {
            if (totalPages > 0) {
                pagingDiv.find("select").empty();
                for (var i = 0; i < totalPages; i++) {
                    pagingDiv.find("select").append($("<option>").val(i + 1).text(i + 1));
                }
                pagingDiv.find("select").val(pageNo);
            }
        };

        /*Getting last n days query*/

        function getDynamicValue(index) {
            var text = "";
            text = " adddays(sysdate, - " + getTotalDays(index) + ") ";
            return text;
        };

        /*Calucating total days*/

        function getTotalDays(index) {
            let y = parseInt(txtYear.val() || 0, 10);
            let m = parseInt(txtMonth.val() || 0, 10);
            let d = parseInt(txtDay.val() || 0, 10);

            let today = new Date();

            if (index == 3 || index == 6) {
                // years + months + days
                let targetDate = new Date(today.getFullYear() - y, today.getMonth() - m, today.getDate() - d);
                return Math.floor((today - targetDate) / (1000 * 60 * 60 * 24));
            }

            else if (index == 2 || index == 5) {
                // months + days
                let targetDate = new Date(today.getFullYear(), today.getMonth() - m, today.getDate() - d);
                return Math.floor((today - targetDate) / (1000 * 60 * 60 * 24));
            }

            else {
                // only days
                return d;
            }
        }



        /* Displaying year month and days on basis of seletion */

        function setDynamicQryControls(isUser) {
            var index = $filterDateList[0].selectedIndex;
            if (isUser) {
                divYear.hide();
                divMonth.hide();
                divDay.hide();
                return;
            }
            divYear.show();
            divMonth.show();
            if (isDynamic.is(':checked')) {
                divDay.show();
            }
            else {
                divDay.hide();
            }
            txtDay.prop("readonly", false);
            txtMonth.prop("readonly", false);
            txtYear.prop("readonly", false);
            txtMonth.prop("min", 1);
            if (index == 3 || index == 6) {
                txtMonth.prop("min", 0);
                txtDay.val(0);
                txtMonth.val(0);
                txtYear.val(1);
            }

            else if (index == 2 || index == 5) {
                divYear.hide();
                txtYear.val("");
                txtMonth.val(1);
                txtDay.val(0);
            }
            else if (index == 1 || index == 4) {
                divYear.hide();
                divMonth.hide();
                txtDay.val(0);
            }
            else if (index == 0) {
                txtDay.prop("readonly", true);
                divYear.hide();
                divMonth.hide();
                txtDay.val(0);
                //divDay.hide();
            }
        };
        /*  Function to delete the particular filter in Filter Accordion*/

        function deleteFilter() {
            var filterItem = $(event.target).parents('.vas-filter-item');
            var andOrVal = filterItem.find(".vas-filter-andor-value").text();
            var filterSelectTableVal = filterItem.find(".vas-selecttable").text();
            /*var filterValue = filterItem.find(".vas-filter-price-value").text();*/
            var filterValue = filterItem.attr("filterId");
            if (typeof filterValue == "string") {
                filterValue = filterItem.find(".vas-filter-price-value").text();
            }
            var filterCondition = filterItem.find(".vas-filter-condition").text();
            var currentValue = $selectGeneratorQuery.text();
            currentValue = currentValue.replace(/\s{2,}/g, ' ');
            var orderByIndex = currentValue.indexOf('ORDER BY');
            var whereIndex = currentValue.indexOf('WHERE');
            var conditionToRemove = andOrVal + " " + filterSelectTableVal + " " + filterCondition + " " + filterValue;
            var orderBySQL = "";
            if (orderByIndex > -1) {
                orderBySQL = currentValue.slice(orderByIndex);
                currentValue = currentValue.slice(0, orderByIndex).trim();
            }
            var orIndexCR = conditionToRemove.indexOf('OR');
            var orIndexSql = currentValue.indexOf('OR');
            var andIndexCR = conditionToRemove.indexOf('AND');
            var andIndexSql = currentValue.indexOf('AND');
            if (whereIndex > -1 && andIndexSql == -1 && orIndexSql == -1) {
                if (orIndexCR !== -1) {
                    conditionToRemove = conditionToRemove.replace("OR", "").trim();
                }
                if (andIndexCR !== -1) {
                    conditionToRemove = conditionToRemove.replace("AND", "").trim();
                }
                currentValue = currentValue.replace("WHERE", "").trim();
                andFlag = true;
            }
            var handleWhereSql = conditionToRemove;
            if (andIndexSql > -1 || orIndexSql > -1) {
                if (andIndexCR > -1) {
                    handleWhereSql = conditionToRemove.replace("AND", "").trim();
                }
                if (orIndexCR > -1) {
                    handleWhereSql = conditionToRemove.replace("OR", "").trim();
                }
                handleWhereSql = handleWhereSql.replace(/\s{2,}/g, ' ');
                var conditionIndex = currentValue.indexOf(handleWhereSql);
                var conditionLength = handleWhereSql.length;
                var beforeCondition = currentValue.slice(conditionIndex - 6, conditionIndex);
                var whereExistIndex = beforeCondition.indexOf('WHERE');
                if (whereExistIndex > -1) {
                    var afterCondition = currentValue.slice(conditionIndex, conditionIndex + conditionLength + 4).trim();
                    var orExistIndexCR = afterCondition.indexOf('OR');
                    var andExistIndexSql = afterCondition.indexOf('AND');

                    if (orExistIndexCR > -1) {
                        handleWhereSql = currentValue.slice(conditionIndex, conditionIndex + conditionLength + 3).trim();
                    }
                    if (andExistIndexSql > -1) {
                        handleWhereSql = currentValue.slice(conditionIndex, conditionIndex + conditionLength + 4).trim();
                    }
                    conditionToRemove = handleWhereSql;
                }
            }
            conditionToRemove = conditionToRemove.replace(/\s{2,}/g, ' ');
            var updatedQuery = currentValue.replace(conditionToRemove, "").trim();
            updatedQuery += " " + orderBySQL;
            $selectGeneratorQuery.text('');
            updatedQuery = updatedQuery.replace(/\s{2,}/g, ' ');
            $selectGeneratorQuery.text(updatedQuery);
            ClearText();
        }

        /*
            Function of Event Handling
        */
        function eventHandling() {
            $windowTabSelect.fireValueChanged = function (e) {
                lblBottomMsg.text("");
                OnChange(e.newValue, 'S');
            };
            txtWindowTab.fireValueChanged = function (e) {
                isProcessing = false;
                OnChange(e.newValue, 'E');
                UpdateColumnCtrl.hide();
                txtFiledColumn.setValue(null);
                getEventColumn(e.newValue);
                txtIsUpdate.setReadOnly(false);
                txtIsInsert.setReadOnly(false);
                txtIsDelete.setReadOnly(false);
                txtIsUpdate.setValue(false);
                txtIsInsert.setValue(false);
                txtIsDelete.setValue(false);
                lblBottomMsg.text("");
            };

            txtIsInsert.fireValueChanged = function (e) {
                isProcessing = false;
                UpdateColumnCtrl.hide();
                if (e.newValue) {
                    txtIsDelete.setValue(false);
                    txtIsUpdate.setValue(false);
                    txtIsDelete.setReadOnly(true);
                    txtIsUpdate.setReadOnly(true);
                } else {
                    txtIsDelete.setReadOnly(false);
                    txtIsUpdate.setReadOnly(false);
                }
                lblBottomMsg.text("");
            };

            txtIsUpdate.fireValueChanged = function (e) {
                isProcessing = false;
                UpdateColumnCtrl.hide();
                if (e.newValue) {
                    var tabID = txtWindowTab.getValue();
                    if (tabID == 0) {
                        VIS.ADialog.error("", "", VIS.Msg.getMsg("VAS_EnterWindowTab"));
                    }
                    UpdateColumnCtrl.show();
                    txtIsInsert.setValue(false);
                    txtIsDelete.setValue(false);
                    txtIsDelete.setReadOnly(true);
                    txtIsInsert.setReadOnly(true);
                } else {
                    txtIsDelete.setReadOnly(false);
                    txtIsInsert.setReadOnly(false);
                }
                lblBottomMsg.text("");
            };

            txtIsDelete.fireValueChanged = function (e) {
                lblBottomMsg.text("");
                isProcessing = false;
                UpdateColumnCtrl.hide();
                if (e.newValue) {
                    txtIsInsert.setValue(false);
                    txtIsUpdate.setValue(false);
                    txtIsUpdate.setReadOnly(true);
                    txtIsInsert.setReadOnly(true);
                } else {
                    txtIsUpdate.setReadOnly(false);
                    txtIsInsert.setReadOnly(false);
                }
            };

            let isProcessing = false;
            okBtn.on(VIS.Events.onClick, function () {
                if (isProcessing) return; 
                setBusy(true);
                isProcessing = true; 
                saveAlertRule();
            });

            txtIsEmail.fireValueChanged = function (e) {
                if (e.newValue) {
                    emailColNameCtrl.show();
                } else {
                    emailColNameCtrl.hide();
                }
            };
        }


        function getEventColumn(tabID) {
            FiledColumnCtrl.empty();
            txtFiledColumn.setValue(null);
            var fieldlookup = new VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.MultiKey, "AD_Column_ID", 0, false, " AD_Column.IsActive='Y' AND FD.AD_Tab_ID= " + tabID);
            txtFiledColumn = new VIS.Controls.VTextBoxButton("AD_Column_ID", true, false, true, VIS.DisplayType.MultiKey, fieldlookup);
            var locDep = txtFiledColumn.getControl().attr('placeholder', ' Search here').attr("id", "AD_Column_ID").css("width", "100%").css("height", "100%");
            var DivSearchCtrlWrap = $('<div class="vas-control-wrap">');
            var DivSearchBtnWrap = $('<div class="input-group-append">');
            FiledColumnCtrl.css("width", "100%");
            FiledColumnCtrl.append(DivSearchCtrlWrap).append(DivSearchBtnWrap);
            DivSearchCtrlWrap.append(locDep);
            DivSearchBtnWrap.append(txtFiledColumn.getBtn(0));
            DivSearchBtnWrap.append(txtFiledColumn.getBtn(1));
            txtFiledColumn.setCustomInfo('VAS_AlertFieldColumn');
            txtFiledColumn.getControl().addClass("vis-ev-col-mandatory");
           // UpdateColumnCtrl.show();
        }

        function saveAlertRule() {
            lblBottomMsg.text("");
            if (txtWindowTab.getValue() == 0) {
                setBusy(false);
                VIS.ADialog.error("", "", VIS.Msg.getMsg("VAS_EnterWindowTab"));
                isProcessing = false;
                return false;
            }
            var ColumnIds = '';
            if (txtIsUpdate.getValue()) {

                if (txtFiledColumn.getValue() && txtFiledColumn.getValue().trim().length == 0) {
                    setBusy(false);
                    VIS.ADialog.error("", "", VIS.Msg.getMsg("VAS_EnterFieldColumn"));
                    isProcessing = false;
                    return false;
                }
                ColumnIds = txtFiledColumn.getValue();
            }

            var obj = {
                alertRuleID: $self.ParentId,
                alertID: VIS.context.getContext($self.windowNo, 'AD_Alert_ID'),
                tableID: EventTable,
                TabID: txtWindowTab.getValue(),
                ColumnID: ColumnIds,
                IsInsert: txtIsInsert.getValue(),
                IsUpdate: txtIsUpdate.getValue(),
                IsDelete: txtIsDelete.getValue()
            }

            $.ajax({
                url: VIS.Application.contextUrl + "AlertSQLGenerate/SaveAlertRule",
                type: "POST",
                data: obj,
                async: false,
                success: function (result) {
                    result = JSON.parse(result);
                    if (result == 'Saved Successfully') {
                        setBusy(false);
                        lblBottomMsg.text('Saved Successfully');
                        isProcessing = false;
                    }
                },
                error: function (error) {
                    setBusy(false);
                    console.log(error);
                    isProcessing = false;
                }
            });
        }

        /*
            Onchange function to get the table data
        */

        function OnChange(tabID, type) {
            tableID = 0;
            pageNo = 1;
            recordCount = 0;
            //  var tabID = $windowTabSelect.getValue();
            var whereClause = '';
            record = [];
            if (tabID > 0) {
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/GetTable",
                    type: "POST",
                    async: false,
                    data: { tabID: tabID, windowNo: self.windowNo },
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result && result.length > 0) {
                            tableID = result[0].TableID;
                            EventTable = tableID;
                            mainTableName = result[0].TableName;
                            tabID = result[0].TabID;
                            whereClause = result[0].WhereClause
                            record = result[0].dr.Table;
                        }
                    },
                    error: function (error) {
                        console.log(error);
                    }
                });             
            }
            if (tableID > 0) {
                if (type == 'S') {
                    getColumns(tableID, tabID, whereClause);
                    $joinsWindowTabSelect.fireValueChanged = joinsTableOnChange;
                    $windowTabSelect.getControl().removeClass("vis-ev-col-mandatory");
                }
            } else {
                clear();
            }
        }

        /*
            Function to get Window/Tab data in Joins
        */

        function joinsTableOnChange() {
            var whereClause = '';
            var tabID = $joinsWindowTabSelect.getValue();
            var TableID2 = 0;
            Joinrecord = [];
            if (tabID > 0) {
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/GetTable",
                    type: "POST",
                    async: false,
                    data: { tabID: tabID, windowNo: self.windowNo },
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result && result.length > 0) {
                            TableID2 = result[0].TableID;
                            whereClause = result[0].WhereClause;
                            Joinrecord = result[0].dr.Table;
                        }
                    },
                    error: function (error) {
                        console.log(error);
                    }
                });
            }
            if (TableID2 != null && TableID2 != "") {
                getJoinsColumns(TableID2, tabID, whereClause);
            }
            else {
                getJoinsColumns(0, 0, null);
            }
        }

        /*
            Function to apply filters
        */

        function ApplyFilter(WhereCondition, value, dataType) {
            var sql = $selectGeneratorQuery.text();
            var orderIndex = sql.indexOf('ORDER BY');
            var whereSql = "";
            var filterval = $filterColumnInput.val();
            var filterValue = value;
            var filterCondition = $filterOperator.val();
            if (filterval != null) {
                var filterColumn = filterval.slice(filterval.indexOf(':') + 1);
                //  var dataType = $filterColumnName.children('.vas-column-list-item.active').attr("datatype");
                if (sql != '' && filterColumn != '' && filterCondition != '') {
                    if (orderIndex == -1) {
                        if (autoComValue != null) {
                            filterValue = autoComValue;
                        }
                        /* if (filterCondition == 'IN') {
                             filterValue = "(" + $inDropdownVal + ")";
                         }*/
                        sql += " " + WhereCondition;
                        if (filterCondition == 'IS NULL' || filterCondition == 'IS NOT NULL') {
                            sql += " " + filterColumn + " " + filterCondition;
                        }
                        /* else if (VIS.DisplayType.Integer == dataType || VIS.DisplayType.ID == dataType || VIS.DisplayType.IsSearch == dataType) {
                             sql += " " + filterColumn + " " + filterCondition + " " + filterValue;
                         }*/
                        else if (VIS.DisplayType.String == dataType || VIS.DisplayType.List == dataType || VIS.DisplayType.Text == dataType || VIS.DisplayType.TextLong == dataType) {
                            sql += " " + filterColumn + " " + filterCondition + " '" + filterValue + "'";
                        }
                        else if (VIS.DisplayType.Date == dataType || VIS.DisplayType.DateTime == dataType) {
                            if (isDynamic.is(':checked')) {
                                $filterOperator.val('>=');
                                sql += " " + filterColumn + " >=" + getDynamicValue($filterDateList[0].selectedIndex);
                            } else {
                                sql += " TO_CHAR(" + filterColumn + ", 'yyyy-mm-dd') " + filterCondition + " " + filterValue;
                            }
                        }
                        else if (VIS.DisplayType.YesNo == dataType) {
                            if ($filterValue.is(':checked')) {
                                sql += " " + filterColumn + " " + filterCondition + " 'Y'";
                            } else {
                                sql += " " + filterColumn + " " + filterCondition + " 'N'";
                            }
                        }
                        else {
                            sql += " " + filterColumn + " " + filterCondition + " " + filterValue;
                        }
                    }
                    else {
                        whereSql += " " + WhereCondition;
                        if (filterCondition == 'IS NULL' || filterCondition == 'IS NOT NULL') {
                            whereSql += " " + filterColumn + " " + filterCondition;
                        }
                        else if (VIS.DisplayType.Integer == dataType || VIS.DisplayType.ID == dataType || VIS.DisplayType.IsSearch == dataType) {
                            whereSql += " " + filterColumn + " " + filterCondition + " " + filterValue;
                        }
                        else if (VIS.DisplayType.String == dataType || VIS.DisplayType.IsDate == dataType || VIS.DisplayType.IsText == dataType || VIS.DisplayType.Text == dataType || VIS.DisplayType.TextLong == dataType) {
                            whereSql += " " + filterColumn + " " + filterCondition + " '" + filterValue + "'";
                        }
                        else if (VIS.DisplayType.Date == dataType || VIS.DisplayType.DateTime == dataType) {
                            whereSql += " TO_CHAR(" + filterColumn + ", 'yyyy-mm-dd') " + filterCondition + " '" + filterValue + "'";
                        }
                        else if (VIS.DisplayType.Date == dataType || VIS.DisplayType.DateTime == dataType) {
                            if (isDynamic.is(':checked')) {
                                $filterOperator.val('>=');
                                whereSql += " " + filterColumn + " >= " + getDynamicValue($filterDateList[0].selectedIndex);
                            } else {
                                whereSql += " TO_CHAR(" + filterColumn + ", 'yyyy-mm-dd') " + filterCondition + " " + filterValue;
                            }
                        }
                        else {
                            whereSql += " " + filterColumn + " " + filterCondition + " " + filterValue;
                        }
                        sql = sql.substring(0, orderIndex) + " " + whereSql + " " + sql.substring(orderIndex);
                    }
                    $selectGeneratorQuery.text('');
                    $selectGeneratorQuery.text(sql);
                    andFlag = false;
                }
            }
        }

        /*
            Function to clear the filter dropdowns
        */
        function ClearText() {
            $filterColumnName.val('');
            $filterValue.val('');
            $filterOperator.val('');
            $filterOperator.removeClass('vas-disabled-icon');
            $filterValExchangeIconBlock.hide();
            $filterNewColumn.hide();
            $filterValInput.val('');
            $filterColumnInput.val('');
            $addFilterBtn.val(VIS.Msg.getMsg("VAS_AddFilter"));
            $filterNewColumn.find('input').val('');
            $filterValExchangeIconBlock.find('input').val('');
            $filterCurrentDate.find('input').prop('checked', false);
            $filterValue.prop('checked', false);
            $filterValueDiv.show();
            resetDynamicControls();
        }

        //reset the dynamic controls
        function resetDynamicControls() {
            isDynamic.prop("checked", false);
            $filterOperator.val(''); // or its default
            $filterOperator.removeClass('vas-disabled-icon');

            $filterDateList.prop("disabled", true);
            $filterDateList.prop("selectedIndex", 0);

            txtYear.val('');
            txtMonth.val('');
            txtDay.val('');

            divYear.hide();
            divMonth.hide();
            divDay.hide();
        }

        function GetLookup(columnDatatype, columnID, columnName, refrenceID, isParent, tableName) {
            var result = "";
            $.ajax({
                url: VIS.Application.contextUrl + "AlertSQLGenerate/GetLookup",
                type: "POST",
                data: {
                    windowNo: $self.windowNo,
                    columnDatatype: columnDatatype,
                    columnID: columnID,
                    columnName: columnName,
                    refrenceID: refrenceID,
                    isParent: (isParent === 'Y'),
                    tableName: tableName
                },
                async: false,
                success: function (data) {
                    result = JSON.parse(data);

                },
                error: function (error) {
                    console.log(error);
                }

            });
            return result;
        }


        /*
            Function to get the columns from Table in Window/Tab
        */
        function getColumns(tableID, tabID, whereClause) {
            $selectBox.find('select').empty();
            var flag = true;
            var seletedCloumn = [];
            $checkBoxes.empty();
            $sortByDropdown.empty();
            $filterColumnName.empty();
            $filterCol2Block.empty();
            $joinOnFieldColumnMainTable.empty();
            joinCommonColumn = [];
            $.ajax({
                url: VIS.Application.contextUrl + "AlertSQLGenerate/GetColumns",
                data: { tableID: tableID, tabID: tabID },
                type: "POST",
                async: false,
                success: function (result) {
                    result = JSON.parse(result);
                    if (result && result.length > 0) {
                        mainTableName = result[0].TableName;
                        joinCommonColumn = result;
                        for (var i = 0; i < result.length; i++) {
                            // Append the dynamic values of Sort/Joins/Conditions and displayed on UI
                            $sortByDropdown.append(
                                "<div class='vas-column-list-item' " +
                                "title='" + mainTableName + " - " + result[i].ColumnName + " (" + result[i].DBColumn + ")' " +
                                "value='" + mainTableName + "." + result[i].DBColumn + "' " +
                                "refValId='" + result[i].ReferenceValueID + "' " +
                                "fieldID='" + result[i].FieldID + "' " +
                                "WindowID='" + result[i].WindowID + "' " +
                                "isParent='" + result[i].IsParent + "' " +
                                "tabID='" + tabID + "' " +
                                "DBColumnName='" + result[i].DBColumn + "' " +
                                "fieldName='" + result[i].FieldName + "' " +
                                "TableName='" + mainTableName + "' " +
                                "columnID='" + result[i].ColumnID + "' " +
                                "datatype='" + result[i].DataType + "'>" +
                                result[i].ColumnName + " (" + result[i].DBColumn + ")" +
                                "</div>"
                            );

                            $filterColumnName.append(
                                "<div class='vas-column-list-item' " +
                                "columnID='" + result[i].ColumnID + "' " +
                                "TableName='" + mainTableName + "' " +
                                "title='" + mainTableName + " - " + result[i].ColumnName + " (" + result[i].DBColumn + ")' " +
                                "refValId='" + result[i].ReferenceValueID + "' " +
                                "fieldID='" + result[i].FieldID + "' " +
                                "WindowID='" + result[i].WindowID + "' " +
                                "isParent='" + result[i].IsParent + "' " +
                                "tabID='" + tabID + "' " +
                                "DBColumnName='" + result[i].DBColumn + "' " +
                                "fieldName='" + result[i].FieldName + "' " +
                                "datatype='" + result[i].DataType + "' " +
                                "value='" + mainTableName + "." + result[i].DBColumn + "'>" +
                                mainTableName + " - " + result[i].ColumnName + " (" + result[i].DBColumn + ")" +
                                "</div>"
                            );

                            $filterCol2Block.append(
                                "<div class='vas-column-list-item' " +
                                "title='" + mainTableName + " - " + result[i].ColumnName + " (" + result[i].DBColumn + ")' " +
                                "refValId='" + result[i].ReferenceValueID + "' " +
                                "fieldID='" + result[i].FieldID + "' " +
                                "WindowID='" + result[i].WindowID + "' " +
                                "tabID='" + tabID + "' " +
                                "DBColumnName='" + result[i].DBColumn + "' " +
                                "TableName='" + mainTableName + "' " +
                                "fieldName='" + result[i].FieldName + "' " +
                                "columnID='" + result[i].ColumnID + "' " +
                                "datatype='" + result[i].DataType + "' " +
                                "value='" + mainTableName + "." + result[i].DBColumn + "'>" +
                                mainTableName + " - " + result[i].ColumnName + " (" + result[i].DBColumn + ")" +
                                "</div>"
                            );

                            $checkBoxes.append(
                                "<div class='vas-column-list-item' " +
                                "refValId='" + result[i].ReferenceValueID + "' " +
                                "fieldID='" + result[i].FieldID + "' " +
                                "WindowID='" + result[i].WindowID + "' " +
                                "tabID='" + tabID + "' " +
                                "DBColumnName='" + result[i].DBColumn + "' " +
                                "TableName='" + mainTableName + "' " +
                                "fieldName='" + result[i].FieldName + "' " +
                                "columnID='" + result[i].ColumnID + "' " +
                                "datatype='" + result[i].DataType + "' " +
                                "isParent='" + result[i].IsParent + "' " +
                                "title='" + result[i].FieldName + " - " + result[i].DBColumn + "'>" +
                                "<input type='checkbox' class='vas-column-checkbox' data-oldIndex='" + i + "'>" +
                                result[i].FieldName + " - " + result[i].DBColumn +
                                "</div>"
                            );
                        }
                    }
                    seletedCloumn = [];
                    $(".vas-column-list-item .vas-column-checkbox").on('click', function (eve) {
                        if (!flag) {
                            $(".vas-column-checkbox").prop("checked", false);
                            flag = true;
                        }
                        var selectedItem = $(this).parent('.vas-column-list-item');
                        var columnDatatype = selectedItem.attr('datatype');
                        var desiredResult = "";
                        var fieldName = selectedItem.attr('fieldName');
                        fieldName = fieldName.replace(/[^a-zA-Z0-9\s]+/g, '');
                        if (VIS.DisplayType.IsLookup(columnDatatype)) {
                            desiredResult = GetLookup(columnDatatype, $(this).parent('.vas-column-list-item').attr('columnID'),
                                $(this).parent('.vas-column-list-item').attr('DBColumnName'),
                                $(this).parent('.vas-column-list-item').attr('refValId'),
                                $(this).parent('.vas-column-list-item').attr('isParent'),
                                $(this).parent('.vas-column-list-item').attr('TableName'));
                        }
                        if (desiredResult == "") {
                            if (fieldName != "") {
                                desiredResult = mainTableName + "." + selectedItem.attr('dbcolumnname').trim() + " AS " + '"' + fieldName + '" ';
                            }
                            else {
                                desiredResult = mainTableName + "." + selectedItem.attr('dbcolumnname').trim() + " ";
                            }
                        }

                        if (this.checked) {
                            seletedCloumn.push(desiredResult);
                        }
                        else {
                            seletedCloumn = seletedCloumn.filter(function (elem) {
                                return elem != desiredResult;
                            });
                        }
                        if (seletedCloumn.length > 0) {
                            GetSQL(seletedCloumn, whereClause);
                        }
                        if (seletedCloumn.length > 0) {
                            $joinsDiv.css("pointer-events", "all");
                            $filterDiv.css("pointer-events", "all");
                            $sortByDiv.css("pointer-events", "all");
                            $windowFieldColumnSelect.removeClass("vis-ev-col-mandatory");
                            $sqlResultDiv.hide();
                        }
                        else {
                            $joinsDiv.css("pointer-events", "none");
                            $filterDiv.css("pointer-events", "none");
                            $sortByDiv.css("pointer-events", "none");
                            $selectGeneratorQuery.text('');
                            $windowFieldColumnSelect.addClass("vis-ev-col-mandatory");
                        }
                    });


                    $checkBoxes.on('change', '.vas-column-list-item .vas-column-checkbox', function () {
                        var $currentItem = $(this).closest('.vas-column-list-item');
                        var isChecked = this.checked;

                        if (isChecked) {
                            // Move checked item to top
                            $currentItem.prependTo($checkBoxes);
                            $fieldColDropdown.removeClass("vis-ev-col-mandatory");
                        } else {
                            // Get the index of the current item
                            var currentIndex = $(this).data('oldindex');
                            $currentItem.insertAfter($checkBoxes.children().eq(currentIndex));
                        }
                        // Check if more than one checkbox is checked
                        var checkedCount = $checkBoxes.find('.vas-column-checkbox:checked').length;
                        // Toggle the 'vis-ev-col-mandatory' class based on checked count
                        if (checkedCount > 0) {
                            $fieldColDropdown.removeClass("vis-ev-col-mandatory");
                        } else {
                            $fieldColDropdown.addClass("vis-ev-col-mandatory");
                        }
                    });

                },
                error: function (error) {
                    console.log(error);
                }
            });
        };

        /*
            Function to get the columns from Table in Joins Dropdowns
        */
        function getJoinsColumns(TableID2, tabID2, whereClause) {
            whereJoinClause = '';
            if (whereClause != '' && whereClause) {
                whereJoinClause = whereClause;
            }
            var joinColumns = [];
            $baseTableJoinInput.val('');
            $joiningTableInput.val('');
            $selectBox.find('select').empty();
            $joinMultiSelect.empty();
            $joinOnFieldColumnJoinTable.empty();
            $joinsSelect.css("margin-bottom", "10px");
            $.ajax({
                url: VIS.Application.contextUrl + "AlertSQLGenerate/GetColumns",
                data: { tableID: TableID2, tabID: tabID2 },
                type: "POST",
                async: false,
                success: function (result) {
                    result = JSON.parse(result);
                    joinData = result;
                    if (result && result.length > 0) {
                        joinTable = result[0].TableName;
                        var primaryKeyMTable = null;
                        var primaryKeyjTable = null;
                        for (var i = 0; i < result.length; i++) {
                            for (var j = 0; j < joinCommonColumn.length; j++) {
                                var optionMTableValue = joinCommonColumn[j].TableName + "." + joinCommonColumn[j].DBColumn;
                                var optionMTableText = joinCommonColumn[j].DBColumn + " (" + joinCommonColumn[j].TableName + ")";
                                var optionJTableValue = joinTable + "." + result[i].DBColumn;
                                var optionJTableText = result[i].DBColumn + " (" + joinTable + ")";
                                if (joinCommonColumn[j].DBColumn == result[i].DBColumn) {
                                    if (!joinColumns.includes(result[i].DBColumn)) {
                                        $joinOnFieldColumnJoinTable.append("<div class='vas-column-list-item' title='" + optionJTableText + "'value=" + optionJTableValue + ">" + optionJTableText + "</div>");
                                        joinColumns.push(result[i].DBColumn);
                                    }
                                    $joinOnFieldColumnMainTable.append(" <div class='vas-column-list-item' title='" + optionMTableText + "' value=" + optionMTableValue + ">" + optionMTableText + "</div>");
                                }
                                if (joinCommonColumn[j].IsKey == 'Y' && !$baseTableJoinInput.val().length > 0) {
                                    $baseTableJoinInput.val(optionMTableValue);
                                    primaryKeyMTable = joinCommonColumn[j].DBColumn;
                                }
                            }
                            if (result[i].DBColumn == primaryKeyMTable && $joiningTableInput.val() != null) {
                                $joiningTableInput.val(joinTable + "." + primaryKeyMTable);
                            }
                            if (result[i].IsKey == 'Y') {
                                primaryKeyjTable = result[i].DBColumn;
                            }                         
                            $joinMultiSelect.append("<div class='vas-column-list-item' " +
                                "refValId='" + result[i].ReferenceValueID + "' " +
                                "fieldID='" + result[i].FieldID + "' " +
                                "WindowID='" + result[i].WindowID + "' " +
                                "tabID='" + tabID + "' " +
                                "DBColumnName='" + result[i].DBColumn + "' " +
                                "TableName='" + joinTable + "' " +
                                "fieldName='" + result[i].FieldName + "' " +
                                "columnID='" + result[i].ColumnID + "' " +
                                "datatype='" + result[i].DataType + "' " +
                                "isParent='" + result[i].IsParent + "' " +
                                "title='" + result[i].FieldName + " - " + result[i].DBColumn + "'>" +
                                "<input type='checkbox' class='vas-column-checkbox' data-oldIndex='" + i + "'>" +
                                result[i].FieldName + " - " + result[i].DBColumn +
                                "</div>"
                            );
                        }
                        if ($joiningTableInput.val() == '' && primaryKeyjTable != null) {
                            $joiningTableInput.val(joinTable + "." + primaryKeyjTable);
                        }
                        filterFlag = false;
                        seletedJoinCloumn = [];
                        $(".vas-join-multiselect .vas-column-checkbox").on('click', function (eve) {
                            var selectedItem = $(this).parent('.vas-column-list-item');
                            var columnDatatype = selectedItem.attr('datatype');
                            var desiredResult = "";
                            var fieldName = selectedItem.attr('fieldName');
                            fieldName = fieldName.replace(/[^a-zA-Z0-9\s]+/g, '');
                            if (VIS.DisplayType.IsLookup(columnDatatype)) {
                                desiredResult = GetLookup(columnDatatype, $(this).parent('.vas-column-list-item').attr('columnID'),
                                    $(this).parent('.vas-column-list-item').attr('DBColumnName'),
                                    $(this).parent('.vas-column-list-item').attr('refValId'),
                                    $(this).parent('.vas-column-list-item').attr('isParent'),
                                    $(this).parent('.vas-column-list-item').attr('TableName'));
                            }
                            if (desiredResult == "") {
                                if (fieldName != "") {
                                    desiredResult = joinTable + "." + selectedItem.attr('dbcolumnname').trim() + " AS " + '"' + fieldName + '" ';
                                }
                                else {
                                    desiredResult = joinTable + "." + selectedItem.attr('dbcolumnname').trim() + " ";
                                }
                            }
                            if (this.checked) {
                                seletedJoinCloumn.push(desiredResult);
                            }
                            else {
                                seletedJoinCloumn = seletedJoinCloumn.filter(function (elem) {
                                    return elem != desiredResult;
                                });
                            }
                        });

                        $joinMultiSelect.on('change', '.vas-column-list-item .vas-column-checkbox', function () {
                            var $currentItem = $(this).closest('.vas-column-list-item');
                            var isChecked = this.checked;

                            if (isChecked) {
                                // Move checked item to top
                                $currentItem.prependTo($joinMultiSelect);
                            } else {
                                // Get the index of the current item
                                var currentIndex = $(this).data('oldindex');
                                $currentItem.insertAfter($joinMultiSelect.children().eq(currentIndex));
                            }
                        });

                    }
                },
                error: function (error) {
                    console.log(error);
                }
            });
        };

        /*
            Search functionality for Joins
        */
        function getJoinWindow(tabID, tableID) {
            var lookups = new VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.Search, "AD_Tab_ID", 0, false,
                "AD_Tab.AD_Window_ID IN (SELECT AD_Window_ID FROM AD_Window_Access WHERE AD_Role_ID = " + VIS.Env.getCtx().getAD_Role_ID() + ") AND AD_Tab.AD_Tab_ID <> " + tabID + " AND AD_Tab.AD_Table_ID<>" + tableID);
            $joinsWindowTabSelect = new VIS.Controls.VTextBoxButton("AD_Tab_ID", false, false, true, VIS.DisplayType.Search, lookups);
            var locDep = $joinsWindowTabSelect.getControl().attr('placeholder', ' Search here').attr("id", "Ad_Tab_ID");
            var DivSearchCtrlWrap = $('<div class="vas-control-wrap">');
            var DivSearchBtnWrap = $('<div class="input-group-append">');
            $txtJoinWindow.css("width", "100%");
            $txtJoinWindow.append(DivSearchCtrlWrap).append(DivSearchBtnWrap);
            DivSearchCtrlWrap.append(locDep);
            DivSearchBtnWrap.append($joinsWindowTabSelect.getBtn(0));
            DivSearchBtnWrap.append($joinsWindowTabSelect.getBtn(1));
            $joinsWindowTabSelect.setCustomInfo('VAS_AlertSQLGenerator');
        }

        /*
            Search functionality for Window/Tab
        */
        function getWindow() {
            var lookups = new VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.Search, "AD_Tab_ID", 0, false, "AD_Tab.AD_Window_ID IN (SELECT AD_Window_ID FROM AD_Window_Access WHERE AD_Role_ID = " + VIS.Env.getCtx().getAD_Role_ID() + ")");
            $windowTabSelect = new VIS.Controls.VTextBoxButton("AD_Tab_ID", true, false, true, VIS.DisplayType.Search, lookups);
            var locDep = $windowTabSelect.getControl().attr('placeholder', ' Search here').attr("id", "Ad_Table_ID").css("width", "100%").css("height", "100%");
            var DivSearchCtrlWrap = $('<div class="vas-control-wrap">');
            var DivSearchBtnWrap = $('<div class="input-group-append">');
            $txtWindow.css("width", "100%");
            $txtWindow.append(DivSearchCtrlWrap).append(DivSearchBtnWrap);
            DivSearchCtrlWrap.append(locDep);
            DivSearchBtnWrap.append($windowTabSelect.getBtn(0));
            DivSearchBtnWrap.append($windowTabSelect.getBtn(1));
            $windowTabSelect.setCustomInfo('VAS_AlertSQLGenerator');
            $windowTabSelect.getControl().addClass("vis-ev-col-mandatory");
        }

        /*
            Functionality to Update the Query in SQL Generator Tab
        */
        function UpdateAlertRule(query) {
            if (query != null) {             
                var obj = {
                    query: VIS.secureEngine.encrypt(query),
                    tableID: tableID || 0,
                    alertID: alertID,
                    alertRuleID: alertRuleID,
                    isEmail: txtIsEmail.getValue(),
                    emailColumn: txtEmailColName.getValue()
                }
                alertRuleID = $self.ParentId;
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/UpdateQuery",
                    type: "POST",
                    data: obj,
                    async: false,
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result && result.length > 0) {
                            $sqlResultDiv.text(result);
                            $sqlResultDiv.removeClass('vas-sql-result-error');
                            $sqlResultDiv.show();
                            $self.curTab.dataRefreshAll();
                        }
                        if (result == 'Saved Successfully') {
                            $sqlBtn.trigger('click');
                            $sqlResultDiv.text(result);
                            $sqlResultDiv.show();
                        }
                    },
                    error: function (error) {
                        console.log(error);
                    }
                });
            }
        }

        /*
            Function to display SQL based on Column Values in SQL Generator Tab
        */

        function GetSQL(columnValue, whereClause) {
            var currentSql = $selectGeneratorQuery.text();
            var fromIndex = currentSql.indexOf('FROM');
            var joinIndex = currentSql.indexOf('JOIN');
            var whereIndex = currentSql.indexOf('WHERE');
            $selectGeneratorQuery.text('');
            if (fromIndex == -1) {
                sql = "SELECT " + columnValue + "FROM " + mainTableName;
            }
            else if (joinIndex != -1 && joinColumnName.length > 0) {
                sql = "SELECT " + columnValue + ", " + joinColumnName + currentSql.slice(fromIndex);
            }
            else {
                sql = "SELECT " + columnValue + currentSql.slice(fromIndex);
            }
            if (whereClause.length > 0 && sql.length > 0 && whereIndex == -1) {
                sql += " WHERE " + whereClause;
            }
            $selectGeneratorQuery.text('');
            $selectGeneratorQuery.text(sql);
        }

        /*
            AJAX Call to get the result in Grid 
            in SQL and SQL Generator Tab
        */

        function getResult(query) {
            if (query != null) {
                query = query.replace(/\s{2,}/g, ' ').trim();
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/GetResult",
                    data: { Query: VIS.secureEngine.encrypt(query), pageNo: pageNo, pageSize: pageSize, tableName: mainTableName, recordCount: recordCount },
                    type: "POST",
                    async: false,
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result != null && result != []) {
                            if (result.RecordList.length > 0) {
                                recordCount = result.TotalRecord;
                                totalPages = Math.ceil(recordCount / pageSize)
                                pageCtrls();
                                if (sqlFlag) {
                                    sqlGrid(result.RecordList);
                                    $sqlResultDiv.hide();
                                }
                                if (sqlGenerateFlag) {
                                    sqlGeneratorGrid(result.RecordList);
                                    $sqlResultDiv.hide();
                                }
                                pagingPlusBtnDiv.removeClass('d-none');
                                pagingPlusBtnDiv.addClass('d-flex justify-content-between align-items-center');
                            }
                            else {
                                if (sqlFlag) {
                                    $testSqlBtn.val(testSQL);
                                    $queryResultGrid.hide();
                                    $query.show();
                                    $selectQuery.show();
                                    emailContentDiv.show();
                                    $saveBtn.hide();
                                }
                                if (sqlGenerateFlag) {
                                    $selectGeneratorQuery.show();
                                    gridDiv2.hide();
                                    $sqlGeneratorQueryResultGrid.hide();
                                    $testSqlGeneratorBtn.val(testSQL);
                                }
                                $sqlResultDiv.show();
                                $sqlResultDiv.text(VIS.Msg.getMsg("NoRecordFound"));
                                $saveGeneratorBtn.hide();
                                // Removed the paging if result is null
                                pagingPlusBtnDiv.addClass('d-none');
                                pagingPlusBtnDiv.removeClass('d-flex justify-content-between align-items-center');
                                $testSqlGeneratorBtn.removeClass('vas-show-grid');
                                $sqlResultDiv.addClass('vas-sql-result-error');
                            }
                        }
                        else {
                            if (sqlFlag) {
                                $testSqlBtn.val(testSQL);
                                $queryResultGrid.hide();
                                $query.show();
                                $selectQuery.show();
                                emailContentDiv.show();
                                $saveBtn.hide();
                            }
                            if (sqlGenerateFlag) {
                                $selectGeneratorQuery.show();
                                gridDiv2.hide();
                                $sqlGeneratorQueryResultGrid.hide();
                                $testSqlGeneratorBtn.val(testSQL);
                            }
                            $testSqlBtn.removeClass('vas-show-grid');
                            $sqlResultDiv.show();
                            $sqlResultDiv.text(VIS.Msg.getMsg("VAS_ValidQuery"));
                            $saveGeneratorBtn.hide();
                            $testSqlGeneratorBtn.removeClass('vas-show-grid');
                            $sqlResultDiv.addClass('vas-sql-result-error');
                            // Removed the paging if result is null
                            pagingPlusBtnDiv.addClass('d-none');
                            pagingPlusBtnDiv.removeClass('d-flex justify-content-between align-items-center');
                        }
                    },
                    error: function (error) {
                        console.log(error);
                    }
                });
            }
        }

        /*
          Showing selected checkbox on top position
        */
        //function onTopSelCheckbox(checkBoxes) {
        //    var origOrder = checkBoxes.children();
        //    checkBoxes.on("click", ":checkbox", function () {
        //        var i, checked = document.createDocumentFragment(),
        //            unchecked = document.createDocumentFragment();
        //        for (i = 0; i < origOrder.length; i++) {
        //            if (origOrder[i].getElementsByTagName("input")[0].checked) {
        //                checked.appendChild(origOrder[i]);
        //            } else {
        //                unchecked.appendChild(origOrder[i]);
        //            }
        //        }
        //        checkBoxes.append(checked).append(unchecked);
        //    });
        //}

        /*
            Function to load the grids with result
            in SQL & SQL Generator Tab
        */

        function sqlGrid(result) {
            sqlGridCols = [];
            for (var i = 0; i < Object.keys(result[0]).length; i++) {
                if (Object.keys(result[0])[i] != 'recid') {
                    sqlGridCols.push({ field: Object.keys(result[0])[i], caption: Object.keys(result[0])[i], size: '130px' });
                }
            }
            if (this.dGrid != null) {
                this.dGrid.destroy();
                this.dGrid = null;
            }
            this.dGrid = $(gridDiv).w2grid({
                name: "gridForm" + $self.windowNo,
                recordHeight: 35,
                multiSelect: true,
                columns: sqlGridCols,
                records: w2utils.encodeTags(result)
            });
            $selectQuery.hide();
            emailContentDiv.hide();
            $query.hide();
            $queryResultGrid.show();
        }


        /*Grid Generation for SQL generator*/

        function sqlGeneratorGrid(result) {
            sqlGeneratorGridCols = [];
            for (var i = 0; i < Object.keys(result[0]).length; i++) {
                if (Object.keys(result[0])[i] != 'recid') {
                    sqlGeneratorGridCols.push({ field: Object.keys(result[0])[i], caption: Object.keys(result[0])[i], size: '130px' });
                }
            }
            if (this.dGridGen != null) {
                this.dGridGen.destroy();
                this.dGridGen = null;
            }
            this.dGridGen = $(gridDiv2).w2grid({
                name: "gridForm2" + $self.windowNo,
                recordHeight: 35,
                multiSelect: true,
                columns: sqlGeneratorGridCols,
                records: w2utils.encodeTags(result)
            });
            $selectGeneratorQuery.hide();
            gridDiv2.show();
            $sqlGeneratorQueryResultGrid.show();
        }

        function filterOnChange(self, $filterSelectedItem) {
            let $filterItemVal = self.attr('value');
            var htmlString = '';
            //$filterValBlock.find('input').val('');
            // $filterNewColumnInput.val('');

            // $filterValueDiv.hide();
            // $filterValExchangeIconBlock.show();
            $filterValueDiv.find('input').val('');

            $filterValInput.val('');
            self.parent($filterColumnName).prev($filterInputBlock).find($filterColumnInput).val($filterItemVal);
            $filterSelectedItem.removeClass('active');
            self.addClass('active');
            var activeItemDataType = $filterColumnName.children('.vas-column-list-item.active').attr('datatype');
            // Show the Column textbox if data types are 12, 29 etc..
            if (activeItemDataType == 12 || activeItemDataType == 29 || activeItemDataType == 22 || activeItemDataType == 47) {
                $filterValueDiv.show();
                $filterValExchangeIconBlock.hide();
            }
            else {
                $filterValueDiv.hide();
                $filterValExchangeIconBlock.show();
            }

            self.parent($filterColumnName).prev($filterInputBlock).find($filterColumnInput).attr('datatype', activeItemDataType);
            var activeItemColId = $filterColumnName.children('.vas-column-list-item.active').attr('columnid');
            self.parent($filterColumnName).prev($filterInputBlock).find($filterColumnInput).attr('columnid', activeItemColId);
            $filterColumnName.hide();
            $filterCurrentDate.find('input').prop('checked', false);
            $filterDateDiv.hide();
            $filterValDropdown.empty();
            var displayType = self.attr("datatype");
            var columnName = self.attr("DBColumnName");
            var tableName = self.attr("TableName");
            var refrenceValue = self.attr('refvalid');
            /*if (!VIS.DisplayType.IsLookup(displayType)) {
                for (var i = 0; i < record.length; i++) {
                    if (record[i][columnName]) {
                        htmlString += '<div class="vas-filterValItem" isNameExist="' + false + '"DBColumnName="' + columnName + '" tableName="' + tableName + '" value="' + record[i][columnName] + '">' + record[i][columnName] + '</div>';
                    }
                }
            }*/

            if (!VIS.DisplayType.IsLookup(displayType)) {
                for (var i = 0; i < record.length; i++) {
                    var keys = Object.keys(record[i]);
                    var matchKey = null;
                    for (var k = 0; k < keys.length; k++) {
                        if (keys[k].toLowerCase() === columnName.toLowerCase()) {
                            matchKey = keys[k];
                            break;
                        }
                    }
                    if (matchKey == null)
                        continue;

                    var value = record[i][matchKey];
                    if (value != null && value !== "") {
                        htmlString +=
                            '<div class="vas-filterValItem" isNameExist="' +
                            false +
                            '" DBColumnName="' + columnName +
                            '" tableName="' + tableName +
                            '" value="' + value +
                            '">' + value + '</div>';
                    }
                }
            }
            $filterValDropdown.append(htmlString);
            $filterofMultipleColumns.find('i').removeClass('vas-showAnotherColControl');
            $filterNewColumn.hide();

            $filterValExchangeIconBlock.find('input').prop('disabled', false);
            $filterValueDiv.find('input').prop('disabled', false);
            $filterValExchangeIconBlock.find('.vas-arrow-down').css('pointer-events', 'all');

            /*Click event on New Column Dropdown Item */
            $filterCol2Block.children('.vas-column-list-item').off(VIS.Events.onTouchStartOrClick);
            $filterCol2Block.children('.vas-column-list-item').on(VIS.Events.onTouchStartOrClick, function () {
                var itemText = $(this).attr('value');
                $filterNewColumnInput.val(itemText);
                $filterCol2Block.children('.vas-column-list-item').removeClass('vas-selected-filterVal');
                $(this).addClass('vas-selected-filterVal');
            });


            /* Keyup Event for Filter Value Items in Dropdown */
            $filterValBlock.find('input').off("keyup");
            $filterValBlock.find('input').on("keyup", function () {
                var $filterValLowerCase = $(this).val();
                var $filterValSelectItem = $filterValDropdown.children('.vas-filterValItem');

                if ($filterValLowerCase.length > 1) {
                    var tname = $filterValSelectItem.attr('tablename');
                    var isNameExist = $filterValSelectItem.attr('isNameExist');
                    var data = getIdsName(columnName, tname, displayType, $filterValLowerCase, isNameExist, activeItemColId, refrenceValue);

                    if (data && data.length > 0) {
                        var htmlString = '';
                        $filterValDropdown.empty();
                        for (var k = 0; k < data.length; k++) {
                            if (data[k].Value) {
                                htmlString += '<div class="vas-filterValItem" isNameExist="' + data[k].isNameExist + '" DBColumnName="' + columnName + '" tableName="' + data[k].tableName + '" value="' + data[k].Value + '">' + data[k].Name + '</div>';
                            }
                        }
                        $filterValDropdown.append(htmlString);
                        $filterValSelectItem = $filterValDropdown.children('.vas-filterValItem');

                        $filterValSelectItem.off(VIS.Events.onTouchStartOrClick);
                        $filterValSelectItem.on(VIS.Events.onTouchStartOrClick, function () {
                            $filterValDropdown.find('.vas-filterValItem').removeClass('vas-selected-filterVal');
                            $(this).addClass('vas-selected-filterVal');
                            var $filterValDropdownText = $(this).text();
                            $filterValBlock.find('input').val($filterValDropdownText);
                        });
                        $filterValDropdown.show();
                        return;
                    }
                }
                var $filterValSelectItemLength = $filterValSelectItem.length;
                $filterValSelectItem.removeClass('vas-selected-filterVal');
                if ($filterValSelectItemLength > 0) {
                    $filterValDropdown.show();
                    $filterValSelectItem.filter(function () {
                        $(this).toggle($(this).text().toLowerCase().indexOf($filterValLowerCase) > -1);
                    });
                } else {
                    $filterValDropdown.hide();
                }
                if (!$filterValSelectItem.is(':visible')) {
                    $filterValDropdown.hide();
                }
            });

            switchValueBtn.css("pointer-events", "all");
            $filterOperator.removeClass('vas-checkboxoption-hidden');
            $filterOperator.removeClass('vas-remove-isnulloption');
            $filterOperator.removeClass('vas-add-isnulloption');
            $filterOperator.removeClass('vas-remove-likeoption');
            $filterOperator.removeClass('vas-comparision-opeartor');
            $filterOperator.removeClass('vas-disabled-icon');


            if (displayType == VIS.DisplayType.Date || displayType == VIS.DisplayType.DateTime) {
                $filterValue.attr('type', 'date');
                $filterValue.prev('label').removeClass('vas-label-space');
                $filterOperator.addClass('vas-add-isnulloption');
                $filterOperator.addClass('vas-remove-likeoption');
                $filterOperator.addClass('vas-comparision-opeartor');
                $filterValExchangeIconBlock.hide();
                $filterValueDiv.show();
                $filterDateDiv.show();
                txtDay.prop("readonly", true);
                isDynamic.prop("disabled", false);
                $filterDateList.prop("disabled", true);
                $filterCurrentDate.on('change', 'input[type="checkbox"]', function () {
                    if ($(this).is(':checked')) {
                        $filterValueDiv.find('input').prop('disabled', true);
                        switchValueBtn.css("pointer-events", "none");
                    } else {
                        $filterValueDiv.find('input').prop('disabled', false);
                        switchValueBtn.css("pointer-events", "all");
                    }
                });

                $filterValueDiv.on('change', 'input[type="date"]', function () {
                    if ($(this).val()) {
                        $filterDateDiv.find('input[type="checkbox"]').attr('disabled', true);
                    }
                    else {
                        $filterDateDiv.find('input[type="checkbox"]').removeAttr('disabled');
                    }
                });
            }

            else if (VIS.DisplayType.IsLookup(displayType)) {
                $filterValue.attr('type', 'number');
                $filterValue.prev('label').removeClass('vas-label-space');
                $filterOperator.addClass('vas-remove-likeoption');
                var data = getIdsName(columnName, tableName, displayType, null, false, activeItemColId, refrenceValue);
                if (data && data.length > 0) {
                    $filterValDropdown.empty();
                    htmlString = '';
                    for (var i = 0; i < data.length; i++) {
                        if (data[i].Value) {
                            htmlString += '<div class="vas-filterValItem" isNameExist="' + data[i].isNameExist + '" DBColumnName="' + columnName + '" tableName="' + data[i].tableName + '" value="' + data[i].Value + '">' + data[i].Name + '</div>';
                        }
                    }
                    $filterValDropdown.append(htmlString);
                }
            }
            else if (displayType == VIS.DisplayType.Integer || displayType == VIS.DisplayType.Amount || displayType == VIS.DisplayType.ID) {
                $filterValue.attr('type', 'number');
                $filterValue.prev('label').removeClass('vas-label-space');
                $filterOperator.addClass('vas-remove-likeoption');
            }
            else if (displayType == VIS.DisplayType.String || VIS.DisplayType.List == displayType || VIS.DisplayType.Text == displayType || VIS.DisplayType.TextLong == displayType) {
                $filterValue.attr('type', 'textbox');
                $filterValue.prev('label').removeClass('vas-label-space');
                $filterOperator.addClass('vas-add-likeoption');
                $filterOperator.addClass('vas-add-isnulloption');
            }
            else if (displayType == VIS.DisplayType.YesNo) {
                $filterValue.attr('type', 'checkbox');
                $filterValue.prev('label').addClass('vas-label-space');
                $filterOperator.addClass('vas-checkboxoption-hidden');
                $filterOperator.addClass('vas-remove-isnulloption');
                $filterValExchangeIconBlock.hide();
                $filterValueDiv.show();
            }
            else {
                $filterValue.attr('type', 'textbox');
                $filterValue.prev('label').removeClass('vas-label-space');
            }
            /* Click Event for Filter Value Items in Dropdown */
            $filterValDropdown.find('.vas-filterValItem').off(VIS.Events.onTouchStartOrClick);
            $filterValDropdown.find('.vas-filterValItem').on(VIS.Events.onTouchStartOrClick, function () {
                $filterValDropdown.find('.vas-filterValItem').removeClass('vas-selected-filterVal');
                $(this).addClass('vas-selected-filterVal');
                var $filterValDropdownText = $(this).attr('value');
                $filterValBlock.find('input').val($(this).text());
                $filterValBlock.find('input').attr('columnId', $filterValDropdownText);
            });
        }

        /**
         * Added the attributes when user change the dropdown value in Add Sort Section
         * @param {any} self
         * @param {any} $sortSelectedItem
         */
        function sortOnChange(self, $sortSelectedItem) {
            let $sortByItemVal = self.attr('value');
            self.parent($sortByDropdown).prev($sortInputBlock).find($sortColumnInput).val($sortByItemVal);
            $sortSelectedItem.removeClass('active');
            self.addClass('active');
            var activeItemDataType = $sortByDropdown.children('.vas-column-list-item.active').attr('datatype');
            self.parent($sortByDropdown).prev($sortInputBlock).find($sortColumnInput).attr('sortdatatype', activeItemDataType);
            var activeItemColId = $sortByDropdown.children('.vas-column-list-item.active').attr('columnID');
            self.parent($sortByDropdown).prev($sortInputBlock).find($sortColumnInput).attr('sortcolumnID', activeItemColId);
            var activeItemColDB = $sortByDropdown.children('.vas-column-list-item.active').attr('DBColumnName');
            self.parent($sortByDropdown).prev($sortInputBlock).find($sortColumnInput).attr('sortDBColumnName', activeItemColDB);
            var activeItemrefID = $sortByDropdown.children('.vas-column-list-item.active').attr('refValId');
            self.parent($sortByDropdown).prev($sortInputBlock).find($sortColumnInput).attr('sortrefValId', activeItemrefID);
            var activeItemChkParent = $sortByDropdown.children('.vas-column-list-item.active').attr('isParent');
            self.parent($sortByDropdown).prev($sortInputBlock).find($sortColumnInput).attr('sortisParent', activeItemChkParent);
            var activeItemtable = $sortByDropdown.children('.vas-column-list-item.active').attr('TableName');
            self.parent($sortByDropdown).prev($sortInputBlock).find($sortColumnInput).attr('sortTableName', activeItemtable);
        }

        function getIdsName(columnName, tableName, displayType, whereClause, isNameExist, columnID, refrenceValueID) {
            var results = null;
            $.ajax({
                url: VIS.Application.contextUrl + "AlertSQLGenerate/GetIdsName",
                type: "POST",
                async: false,
                data: {
                    columnName: columnName,
                    tableName: tableName,
                    displayType: displayType,
                    whereClause: whereClause,
                    isNameExist: isNameExist,
                    columnID: columnID,
                    refrenceValueID: refrenceValueID,
                    windowNo: $self.windowNo

                },
                success: function (result) {
                    result = JSON.parse(result);
                    if (result && result.length > 0) {
                        results = result;
                    }
                },
                error: function (error) {
                    console.log(error);
                }
            });
            return results;
        }

        /*
           Clears the values of controls used
        */
        function clear() {
            $self.BasedOn = "";
            lblBottomMsg.text("");
            tableID = 0;
            EventTable = 0;
            mainTableName = "";
            tabID = 0;
            $selectGeneratorQuery.text('');
            joinsArray = [];
            $joins.empty();      
            resetFilters();
            Joinrecord = [];
            record = [];
            $sortCollection.empty();
            $filterCol2Block.empty();
            $windowFieldColumnSelect.addClass("vis-ev-col-mandatory");
            getColumns(0, 0);
            getJoinsColumns(0, 0);
            $filterofMultipleColumns.find('i').removeClass('vas-showAnotherColControl');
            $filterValueDiv.show();
            $filterValExchangeIconBlock.hide();
            $filterNewColumn.hide();
            $filterValDropdown.hide();
            $filterValDropdown.empty();
            $joinsDiv.css("pointer-events", "none");
            $filterDiv.css("pointer-events", "none");
            $sortByDiv.css("pointer-events", "none");
            $checkBoxes.hide();
            $checkBoxes.children('.vas-column-list-item').find('input[type="checkbox"]').prop('checked', false);
            $(".vas-column-checkbox").prop("checked", false);
            $sortColumnInput.val('');
            $filterColumnInput.val('');
            $windowTabSelect.getControl().addClass("vis-ev-col-mandatory");
            $fieldColDropdown.addClass("vis-ev-col-mandatory");
            $filterSelectArrow.css('pointer-events', 'all');
            $addFilterBtn.val(VIS.Msg.getMsg("VAS_AddFilter"));
            $sortByDiv.removeClass('active');
            $joinsDiv.removeClass('active');
            $filterDiv.removeClass('active');
            $txtJoinWindow.empty();
            getJoinWindow(0, 0);
            $selectGeneratorQuery.show();
            gridDiv2.hide();
            $sqlGeneratorQueryResultGrid.hide();
            $saveGeneratorBtn.hide();
            $testSqlGeneratorBtn.val(testSQL);
            $sqlResultDiv.hide();
            $filterDateDiv.hide();
            andFlag = true;
            $windowTabSelect.setValue(null)
            addedOptions = [];
            joinColumnName = [];
            seletedJoinCloumn = [];
            joinData = null;
            $filterValue.attr('type', 'Textbox');
            $filterValue.val('');
            joinCommonColumn = [];
            $baseTableJoinInput.val('');
            $joiningTableInput.val('');
            $joinOnFieldColumnMainTable.empty();
            $joinOnFieldColumnJoinTable.empty();
            $filterValBlock.find('input').val('');
            $filterNewColumnInput.val('');
            whereJoinClause = '';
            pagingPlusBtnDiv.addClass('d-none');
            pagingPlusBtnDiv.removeClass('d-flex justify-content-between align-items-center');
            sortValuesArray = [];
            sortedIndex = -1;
        }

        function resetFilters() {
            filterArrayReset = true;
            filterArray = [];
            $filters.empty();
            $('.vas-filter-item').remove();
            autoComValue = null;
            filterIndex = -1;
            filterArrayIndex = -1;
        }

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if ($testSqlBtn)
                $testSqlBtn.off(VIS.Events.onTouchStartOrClick);
            if ($testSqlGeneratorBtn)
                $testSqlGeneratorBtn.off(VIS.Events.onTouchStartOrClick);
            if ($filters)
                $filters.off(VIS.Events.onTouchStartOrClick);
            if ($sqlBtn)
                $sqlBtn.off(VIS.Events.onTouchStartOrClick);
            //if ($sqlGeneratorBtn)
            //    $sqlGeneratorBtn.off(VIS.Events.onTouchStartOrClick);
            if ($joinsDiv)
                $joinsDiv.off(VIS.Events.onTouchStartOrClick);
            if ($filterDiv)
                $filterDiv.off(VIS.Events.onTouchStartOrClick);
            if ($sortByDiv)
                $sortByDiv.off(VIS.Events.onTouchStartOrClick);
            if ($windowFieldColumnSelect)
                $windowFieldColumnSelect.off(VIS.Events.onTouchStartOrClick);
            if ($joinsSelect)
                $joinsSelect.off(VIS.Events.onTouchStartOrClick);
            if ($saveBtn)
                $saveBtn.off(VIS.Events.onTouchStartOrClick);
            if ($saveGeneratorBtn)
                $saveGeneratorBtn.off(VIS.Events.onTouchStartOrClick);
            if ($addFilterBtn)
                $addFilterBtn.off(VIS.Events.onTouchStartOrClick);
            if ($joins)
                $joins.off(VIS.Events.onTouchStartOrClick);
            if ($addJoinBtn)
                $addJoinBtn.off(VIS.Events.onTouchStartOrClick);
            if ($addSortBtn)
                $addSortBtn.off(VIS.Events.onTouchStartOrClick);
            if ($root != null) {
                $root.remove();
            }
            $root = null;
            $self = null;
            $sqlBtn = null;
            $sqlGeneratorBtn = null;
            $testSqlGeneratorBtn = null;
            $testSqlBtn = null;
            $saveBtn = null;
            $saveGeneratorBtn = null;
            $addJoinBtn = null;
            $addFilterBtn = null;
            $addSortBtn = null;
            $sqlGeneratorContent = null;
            $joinsDropdown = null;
            $joinOnFieldColumnMainTable = null;
            $joinOnFieldColumnJoinTable = null;
            $filterOperator = null;
            $filterConditionV2 = null;
            $filterValue = null;
            $sortElements = null;
            removeColumnJoins = null;
            oldFilterVal = null;
            pageNo = 1;
            recordCount = 0;
            filterIndex = null;
            sortedIndex = -1;
            this.getRoot = null;
            this.dGrid = null;
            this.disposeComponent = null;
        };
    };

    /*
        Function to start the Tab Panel
    */
    VAS.TabAlertRuleSql.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        this.init();
    };

    /*
        Function to update tab panel based on selected record
    */
    VAS.TabAlertRuleSql.prototype.refreshPanelData = function (ParentId, selectedRow) {
        this.ParentId = ParentId;
        this.selectedRow = selectedRow;
        this.SqlQuery(ParentId);
    };

    /*
        Function to resize tab panel based on selected record
    */

    VAS.TabAlertRuleSql.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    // Function of Memory Dealocation
    VAS.TabAlertRuleSql.prototype.dispose = function () {
        this.windowNo = 0;
        this.curTab = null;
        this.rowSource = null;
        this.panelWidth = null;
        this.disposeComponent();
    }

})(VAS, jQuery);