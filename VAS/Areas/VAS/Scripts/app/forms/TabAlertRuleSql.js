; VAS = window.VAS || {};
; (function (VAS, $) {
    VAS.TabAlertRuleSql = function () {
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        this.ParentId = 0;
        var $self = this;
        var pageSize = 20;
        var autoComValue = null;
        var pageNo = 0;
        var tableID = 0;
        var tabID = 0;
        var joinData = null;
        var addedOptions = [];
        var joinColumnName = [];
        var alertID = 0;
        var alertRuleID = 0;
        var tableName = null;
        var filterArrayIndex = 0;
        var andFlag = true;
        var orderbyFlag = true;
        var sqlGenerateFlag = false;
        var removeColumnJoins = null;
        var sqlFlag = true;
        var filterIndex = null;
        var WhereCondition;
        var joinsArray;
        var joinTable;
        var seletedJoinCloumn = [];
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
        //var $inDropdownVal = [];
        var filterArray;
        this.dGrid = null;
        this.dGridGen = null;
        var $windowTabDiv = $("<div class='vas-windowtab'>");
        var $joinSearch = $("<div class='vas-windowtab vas-searchinput-block'>");
        var $multiSelectArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $multiSelectJoinArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        //var $inOperatorArrow = $('<span class="vis vis-plus vas-arrow-down"></span>');
        var $windowTabLabel = $("<label>");
        var $joinFieldColumnLabel = $("<label>");
        var $windowTabSelect = null;
        var $txtWindow = $("<div class='VIS-AMTD-formData VIS-AMTD-InputBtns input-group vas-input-wrap'>");
        var $windowFieldColumnLabel = $("<label>");
        var $windowFieldColumnSelect = $("<div class='vas-windowfieldcol-select vas-windowtab vis-ev-col-mandatory' style='display: none;'></div>");
        var $joinsSelect = $("<div style='display: none;' class='vas-joins-select vas-common-style vas-windowtab'>");
        var $selectQuerySqlText = $("<div class='vas-sqlquery-text'>");
        var $selectGenQuerySqlText = $("<div class='vas-sqlquery-text'>")
        var $sqlGenDeleteIcon = $("<i class='vis vis-delete vas-filters-editdelete-btns'></i>");
        var $selectQuery = $("<div class='vas-query-input vas-sql-query-input' id='query-input' contenteditable='true'>");
        var $selectGeneratorQuery = $("<div class='vas-query-input' contenteditable='false'>");
        var $multiSelect = $("<div class='vas-multiselect'>");
        var $selectBox = $("<div class='vas-selectBox vas-windowtab'><select><option>" + VIS.Msg.getMsg("VAS_SelectAll") + "</option></select><div class='vas-overselect'></div></div>");
        var $checkBoxes = $("<div class='vas-checkboxes'>");
        var $joins = $("<div class='vas-joins'>");
        var $joinsDiv = $("<div class='vas-add-label'>");
        var $joinsLabel = $("<label>");
        var $filterColLabel = $("<label>");
        var $sortColLabel = $("<label>");
        var $column1Div = $("<div class='vas-column1'>");
        var $addJoinsHeading = $("<h4>");
        var $addJoinsDiv = $("<div class='vas-add-label-content'>");
        var $joinsWindowTabSelect = null;
        var $txtJoinWindow = $("<div class='VIS-AMTD-formData VIS-AMTD-InputBtns input-group vas-input-wrap'>");
        var $joinsDropdown = null;
        var $filterEditDiv = $("<div style='display:none;'>");
        var $fieldColLabel = $("<label>");
        var $joinWindowColLabel = $("<label>");
        var $baseTableColLabel = $("<label>");
        var $joinSearchLabel = $("<label>");
        var $joiningTableColLabel = $("<label>");
        var $joinOnFieldColumnMainTable = null;
        var $joinOnFieldColumnJoinTable = null;
        var $filters = $("<div class='vas-filters'>");
       // var $inOperatorValues = $("<div class='vas-inValues'>");
        var $filterDiv = $("<div class='vas-add-label'>");
        var $filterLabel = $("<label>");
        var $addFilterText = $("<div class='vas-addfiltertext'>");
        var $addFilterSelectButton = $("<div class='vas-select-plus-btn'>");
        var $addFilterDiv = $("<div class='vas-add-label-content'>");
        var $filterColumnNameLabel = $("<label>");
        var $filterOperatorLabel = $("<label>");
        var $filterOperatorDiv = $("<div class='vas-windowtab vas-operator'>");
        var $filterValueLabel = $("<label>");
        var $filterValueDiv = $("<div class='vas-windowtab vas-columnval vas-single-input'>");
        var $filterPrice = $("<div class='vas-filter-text-input vas-single-selection-dropdown'>");
        var $filterCondition = $("<select class='vas-filtercondn'><option>=</option><option><></option><option>></option><option><</option><option><=</option><option>>=</option><option>LIKE</option><option>NOT LIKE</option><option>IS NULL</option><option>IS NOT NULL</option></select>");
        var $filterPriceValue = $("<input type='textbox' class='vas-filter-text-input'>");
        const inOperatorObj = null;
        var $fieldColDropdownBlock = $("<div class='vas-fielddropdown vas-windowtab'>");
        var $fieldJoinColDropdownBlock = $("<div class='vas-joinfielddropdown vas-windowtab'>");
        var $filterConditionV2 = null;
        var $sortElements = null;
        var $sortSelectArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $filterSelectArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $baseTableSelectArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $joiningTableSelectArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $sortColumnInput = $("<input class='vas-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $filterColumnInput = $("<input class='vas-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $baseTableJoinInput = $("<input class='vas-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $joiningTableInput = $("<input class='vas-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $fieldColDropdown = $("<input class='vas-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $fieldJoinColDropdown = $("<input class='vas-join-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $sortByDiv = $("<div class='vas-add-label'>");
        var $sortByLabel = $("<label>");
        var $addSortByText = $("<h4>");
        var $addSortByDiv = $("<div class='vas-add-label-content'>");
        var $sortByDropdown = $("<div class='vas-sortby-dropdown'>");
        var $sortInputBlock = $("<div class='vas-single-input vas-windowtab vas-sort-block'>");
        var $filterInputBlock = $("<div class='vas-single-input vas-windowtab vas-searchinput-block'>");
        var $baseTableInputBlock = $("<div class='vas-single-input vas-windowtab'>");
        var $joiningTableInputBlock = $("<div class='vas-single-input vas-joiningtable-input vas-windowtab'>");
        var $addSortBySelectWithButton = $("<div class='vas-select-plus-btn vas-addsort-btn'>");
        var $sqlBtns = $("<div class='vas-sql-btns'>");
        var $contentArea = $("<div class='vas-content-area'>");
        var $queryMessage = $("<div class='vas-query-message'>");
        var $sqlContent = $("<div class='vas-sql-content'></div>");
        var $queryResultGrid = $("<div style='display:none;' class='vas-queryresult-grid'></div>");
        var $sqlGeneratorContent = null;
        var $sqlGeneratorQueryResultGrid = $("<div style='display:none;' class='vas-queryresult-grid'></div>");
        var sqlQuery = VIS.Msg.getMsg("VAS_SQLQuery");
        var testSQL = VIS.Msg.getMsg("VAS_TestSql");
        var joinCommonColumn = null;
        var $root = $("<div class='vas-root'>");

        // Initialize UI Elements
        this.init = function () {
            $sqlBtn = $("<input class='VIS_Pref_btn-2 vas-sql-btn' id='vas-sql-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_SQL") + "'>");
            $sqlGeneratorBtn = $("<input class='VIS_Pref_btn-2 vas-sql-generator' id='vas-sql-generatorbtn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_SQLGenerator") + "'>");
            $testSqlGeneratorBtn = $("<input style='display: none;' class='VIS_Pref_btn-2 vas-test-sql vas-test-sqlgenerator' id='vas-testsql-generatorbtn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_TestSql") + "'>");
            $testSqlBtn = $("<input class='VIS_Pref_btn-2 vas-test-sql' id='vas-testsql-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_TestSql") + "'>");
            $saveBtn = $("<input class='VIS_Pref_btn-2 vas-save-btn' type='button' id='vas-save-btn" + $self.windowNo + "' value='" + VIS.Msg.getMsg("VAS_Save") + "'>");
            $saveGeneratorBtn = $("<input class='VIS_Pref_btn-2 vas-save-btn' id='vas-savegenerator-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_Save") + "'>");
            $addJoinBtn = $("<input class='VIS_Pref_btn-2 vas-add-join-btn' id='vas-addjoin-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_AddJoin") + "'>");
            $addFilterBtn = $("<input class='VIS_Pref_btn-2 vas-add-btn' id='vas-addfilter-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_AddFilter") + "'>");
            $addSortBtn = $("<input class='VIS_Pref_btn-2 vas-add-btn' id='vas-addsort-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_AddSort") + "'>");
            $sqlGeneratorContent = $("<div class='vas-sqlgenerator' id='vas-sqlgeneratorcontent" + $self.windowNo + "' style='display: none;'><div class='vas-sqlgenerator-column vas-sqlgenerator-column1'></div><div class='vas-sqlgenerator-column vas-sqlgenerator-column2'></div></div>");
            $joinsDropdown = $("<select class='vas-joins-collection'><option>INNER JOIN</option><option>LEFT OUTER JOIN</option><option>RIGHT OUTER JOIN</option><option>FULL JOIN</option></select>");
            $joinOnFieldColumnMainTable = $("<div class='vas-single-selection-dropdown'>");
            $joinOnFieldColumnJoinTable = $("<div class='vas-single-selection-dropdown'>");
            $filterConditionV2 = $("<select><option>AND</option><option>OR</option></select>");
            $sortElements = $("<select><option>ASC</option><option>DESC</option></select>");

            $sqlBtns.append($sqlGeneratorBtn)
                .append($sqlBtn)
                .append($sqlResultDiv)
                .append($testSqlBtn)
                .append($testSqlGeneratorBtn);
            $windowTabLabel.text(VIS.Msg.getMsg("VAS_WindowTab"));
            $joinSearchLabel.text(VIS.Msg.getMsg("VAS_WindowTabAddition"));
            $selectQuerySqlText.text(VIS.Msg.getMsg("VAS_SQL"));
            $selectGenQuerySqlText.text(VIS.Msg.getMsg("VAS_SQLQuery"));
            $windowFieldColumnLabel.text(VIS.Msg.getMsg("VAS_FieldColumn"));
            $filterColLabel.text(VIS.Msg.getMsg("VAS_FieldColumn"));
            $fieldColLabel.text(VIS.Msg.getMsg("VAS_FieldColumn"));
            $sortColLabel.text(VIS.Msg.getMsg("VAS_FieldColumn"));
            $joinWindowColLabel.text(VIS.Msg.getMsg("VAS_FieldColumn"));
            $baseTableColLabel.text(VIS.Msg.getMsg("VAS_MainTable_Column"));
            $joiningTableColLabel.text(VIS.Msg.getMsg("VAS_JoinTable_Column"));
            $joinFieldColumnLabel.text(VIS.Msg.getMsg("VAS_FieldColumn"));
            $joinsLabel.text(VIS.Msg.getMsg("VAS_Joins"));
            $addJoinsHeading.text(VIS.Msg.getMsg("VAS_AddJoin"));
            $filterLabel.text(VIS.Msg.getMsg("VAS_Condition"));
            $sortByLabel.text(VIS.Msg.getMsg("VAS_Sort"));
            $addFilterText.text(VIS.Msg.getMsg("VAS_AddFilter"));
            $addSortByText.text(VIS.Msg.getMsg("VAS_AddSort"));
            $filterColumnNameLabel.text(VIS.Msg.getMsg("VAS_FieldColumn"));
            $filterOperatorLabel.text(VIS.Msg.getMsg("Operator"));
            $filterValueLabel.text(VIS.Msg.getMsg("VAS_ColumnValue"));
            $selectGeneratorQuery.attr('disabled', true);
            $root.append($sqlBtns).append($contentArea).append(gridDiv).append(gridDiv2);
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

            $windowTabDiv.append($windowTabLabel).append($txtWindow);
            $multiSelect.append($fieldColDropdownBlock);
            $fieldColDropdownBlock.append($fieldColLabel);
            $fieldColDropdownBlock.append($fieldColDropdown);
            $fieldColDropdownBlock.append($multiSelectArrow);
            $multiSelect.append($windowFieldColumnSelect).append($checkBoxes);
            $windowFieldColumnSelect.append($windowFieldColumnLabel);
            $joinsDiv.append($joinsLabel);
            $addJoinsDiv.append($addJoinsHeading)
                .append($joinSearch)
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
            $joinSearch.append($joinSearchLabel).append($txtJoinWindow);
            $fieldJoinColDropdownBlock.append($joinWindowColLabel);
            $fieldJoinColDropdownBlock.append($fieldJoinColDropdown);
            $fieldJoinColDropdownBlock.append($multiSelectJoinArrow);
            $joinsSelect.append($joinFieldColumnLabel);
            $filterDiv.append($filterLabel);
            $filterOperatorDiv.append($filterOperatorLabel).append($filterCondition);
            $filterValueDiv.append($filterValueLabel).append($filterPriceValue);
            $addFilterDiv.append($addFilterText)
                .append($filterInputBlock)
                .append($filterPrice)
                .append($filterOperatorDiv)
                .append($filterValueDiv)
                .append($addFilterSelectButton)
                .append($filterEditDiv);
            $addFilterSelectButton.append($filterConditionV2).append($addFilterBtn);
            $sortByDiv.append($sortByLabel);
            $addSortByDiv.append($addSortByText).append($sortInputBlock).append($sortByDropdown).append($addSortBySelectWithButton);
            $sortInputBlock.append($sortColLabel).append($sortColumnInput).append($sortSelectArrow);
            $filterInputBlock.append($filterColLabel).append($filterColumnInput).append($filterSelectArrow);
            $baseTableInputBlock.append($baseTableColLabel).append($baseTableJoinInput).append($baseTableSelectArrow);
            $joiningTableInputBlock.append($joiningTableColLabel).append($joiningTableInput).append($joiningTableSelectArrow);
            $addSortBySelectWithButton.append($sortElements).append($addSortBtn);
            $sqlContent.append($selectQuerySqlText).append($selectQuery).append($queryResultGrid);
            $sqlGeneratorContent.find('.vas-sqlgenerator-column2').append($selectGenQuerySqlText.append($sqlGenDeleteIcon)).append($selectGeneratorQuery).append(gridDiv2);

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
                        $sortByDropdown.hide();
                    });
                }
            });

            /* Hiding filter Value on basis of filter Operator */

            $filterCondition.on("change", function () {
                if ($filterCondition.val() == 'IS NULL') {
                    $filterValueDiv.hide();
                }
                else {
                    $filterValueDiv.show();
                }
                /*if ($filterCondition.val() == 'IN') {
                    $filterValueDiv.addClass('vas-reduceColValWidth');
                    $filterValueDiv.append($inOperatorArrow);
                    $inOperatorArrow.show();
                }
                else {
                    $filterValueDiv.removeClass('vas-reduceColValWidth');
                    $inOperatorArrow.hide();
                }*/
            });

            /*searching the filter column when user start typing*/

            $filterColumnInput.on("keyup", function () {
                var $filterColDropdownVal = $(this).val().toLowerCase();
                var $filterSelectedItem = $filterPrice.children('.vas-column-list-item');
                if ($filterSelectedItem.length > 0) {
                    $filterPrice.show();
                    $filterSelectedItem.filter(function () {
                        $(this).toggle($(this).text().toLowerCase().indexOf($filterColDropdownVal) > -1);
                    });
                }
                if (!$filterSelectedItem.is(':visible')) {
                    $filterPrice.hide();
                }
                else {
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
                        $(this).parent($filterPrice).prev($baseTableInputBlock).find($baseTableJoinInput).val($join1ItemVal);
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
                            getResult(query);
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
                        }
                        else {
                            $(this).val(sqlQuery);
                            $selectGeneratorQuery.hide();
                            gridDiv2.show();
                            $sqlGeneratorQueryResultGrid.show();
                            sqlGeneratorColumn2.append($saveGeneratorBtn);
                            sqlGeneratorColumn2.find($saveGeneratorBtn).show();
                            var query = $selectGeneratorQuery.text();
                            getResult(query);
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
                    $sortByDropdown.toggle();
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
                    $joinOnFieldColumnJoinTable.toggle();
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


            $filterSelectArrow.on(VIS.Events.onTouchStartOrClick, function () {
                var $filterSelectedItem = $filterPrice.children('.vas-column-list-item');
                let $filterItem = $filterPrice.children('.vas-column-list-item');
                if ($filterSelectedItem.length > 0) {
                    $filterPrice.toggle();
                    $filterItem.show();
                }
                if (!$filterItem.is(':visible')) {
                    $filterPrice.hide();
                }
                else {
                    $filterItem.on(VIS.Events.onTouchStartOrClick, function () {
                        filterOnChange($(this), $filterSelectedItem);
                    });
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

            filterArray = [];
            function readFilterData() {
                var data = '';
                for (let obj of filterArray) {
                    if (obj.filterValue.startsWith("'") && obj.filterValue.endsWith("'")) {
                        obj.filterValue = obj.filterValue.slice(1, -1);
                    }
                }
                for (var i = 0; i < filterArray.length; i++) {
                    var dataType = filterArray[i].dataType;
                    data += '<div class=vas-filter-item index=' + i + '>';
                    data += '<div class="vas-filter-whitebg" style="background-color:' + randomColor() +'">';
                    data += '<div class="vas-filters-block">';
                    data += '<div class="vas-filter-andor-value" style=display:none;>' + filterArray[i].filterAndOrValue + '</div>';
                    data += '<div class="vas-filter-whereExit" style=display:none;>' + filterArray[i].whereExist + '</div>';
                    data += '<div class="vas-filter-datatype" style=display:none;>' + dataType + '</div>';
                    if (VIS.DisplayType.Date == dataType || VIS.DisplayType.DateTime == dataType) {
                        data += '<div class="vas-selecttable" title=' + " TO_CHAR(" + filterArray[i].filterPriceval + ", 'yyyy-mm-dd')" + '>' + " TO_CHAR(" + filterArray[i].filterPriceval + ", 'yyyy-mm-dd')" + '</div>';
                    }
                    else {
                        data += '<div class="vas-selecttable" title=' + filterArray[i].filterPriceval + '>' + filterArray[i].filterPriceval + '</div>';
                    }
                    data += '<span class="vas-filter-condition">' + " " + filterArray[i].filterCondition + " " + '</span>';

                    if (filterArray[i].filterCondition == 'IS NULL' || filterArray[i].filterCondition == 'IS NOT NULL') {
                        data += '<span class="vas-filter-price-value"></span>';
                    }
                    else if (VIS.DisplayType.Integer == dataType || VIS.DisplayType.ID == dataType || VIS.DisplayType.IsSearch == dataType) {
                        data += '<span class="vas-filter-price-value">' + filterArray[i].filterValue + '</span>';
                    }
                    else if (VIS.DisplayType.String == dataType || VIS.DisplayType.List == dataType || VIS.DisplayType.Text == dataType || VIS.DisplayType.TextLong == dataType
                        || VIS.DisplayType.DateTime == dataType || VIS.DisplayType.Date == dataType) {
                        data += '<span class="vas-filter-price-value">' + "'" + filterArray[i].filterValue + "'" + '</span>';
                    }
                    else if (VIS.DisplayType.YesNo == dataType) {
                        data += '<span class="vas-filter-price-value">' + "'" + filterArray[i].filterValue.substring(1, filterArray[i].filterValue.length - 1) + "'" + '</span>';
                    }
                    else {
                        data += '<span class="vas-filter-price-value">' + filterArray[i].filterValue + '</span>';
                    }
                    data += '</div>';
                    data += '<div class="vas-filters-editdelete-btns">';
                    //if (VIS.DisplayType.Date != dataType && VIS.DisplayType.DateTime != dataType && filterArray[i].filterCondition!='IN') {
                    if (VIS.DisplayType.Date != dataType && VIS.DisplayType.DateTime != dataType) {
                        data += '<div><i class="vis vis-edit"></i></div>';
                    }
                    else {
                        data += '<div><i class="vis vis-edit vas-disabled-icon"></i></div>';
                    }
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

            function addFilter() {
                // var filterPriceval = $filterPrice.children('.vas-column-list-item.active').attr('value');
                var filterPriceval = $filterColumnInput.val();
                let filterCondition = $filterCondition.find('option:selected').val();
                var filterValue = $filterPriceValue.val();
                if (autoComValue != null) {
                    filterValue = autoComValue;
                    autoComValue = null;
                }               
                var columnIndex = $selectGeneratorQuery.text().indexOf(filterPriceval);
                var beforeCondition = $selectGeneratorQuery.text().slice(columnIndex - 6, columnIndex);
                var whereExist = false;
                if (beforeCondition.indexOf('WHERE') > -1) {
                    whereExist = true;
                }
                var dataType = $filterPrice.children('.vas-column-list-item.active').attr("datatype");
                if (VIS.DisplayType.YesNo == dataType) {
                    if ($filterPriceValue.is(':checked')) {
                        filterValue = "Y";
                    } else {
                        filterValue = "N";
                    }
                }
                /*if (filterCondition == 'IN') {
                    filterValue = "("+$inDropdownVal+")";
                }*/
                let filterAndOrValue = $filterConditionV2.find('option:selected').val();
                const filterObj = {
                    filterPriceval, filterCondition, filterValue, filterAndOrValue,
                    dataType, whereExist
                }
                filterArray.push(filterObj);
                $filters.empty();
                readFilterData();
                if ($filters && $filters.length > 0) {
                    $filters.find('.vas-filter-item').addClass('vas-filters-row');
                }
                $filterValueDiv.show();
            }

            /*inOperatorArray = [];

            function readInOperatorData() {                
                $inDropdownVal.push($filterPriceValue.val());
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
                    filterArray.splice(filterItem.index(), 1);
                    filterItem.remove();
                    if ($('.vas-filters .vas-filter-item').length < 2) {
                        $('.vas-filters .vas-filter-item').removeClass('vas-first-delete-icon');
                    }
                    deleteFilter();
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
                    var filterCondition = filterItem.find(".vas-filter-condition").text();
                    var updatedword = filterValue.replace(/^'|'$/g, '');
                    filterItem.siblings().find(".vas-filter-condition").removeClass('active');
                    filterItem.siblings().find(".vas-filter-price-value").removeClass('active');
                    filterItem.siblings().find(".vas-filter-andor-value").removeClass('active');
                    filterItem.siblings().find(".vas-selecttable").removeClass('active');
                    filterItem.find(".vas-filter-condition").addClass('active');
                    filterItem.find(".vas-filter-price-value").addClass('active');
                    filterItem.find(".vas-filter-andor-value").addClass('active');
                    filterItem.find(".vas-selecttable").addClass('active');
                    $filterColumnInput.val(filterSelectTableVal);
                    $filterCondition.val(filterCondition);
                    $filterPriceValue.val(updatedword);
                    $addFilterBtn.addClass('vas-edit-btn');
                    $filterSelectArrow.css('pointer-events', 'none');
                    let $filterPriceItem = $filterPrice.children('.vas-column-list-item');
                    $filterPriceItem.each(function () {
                        let filterPriceEditedVal = $(this).attr('value');
                        if (filterPriceEditedVal === filterSelectTableVal) {
                            $(this).trigger('click');
                        }
                    });
                    $sqlResultDiv.hide();
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
                    $filterPriceValue.attr('type', 'date');
                    $filterPriceValue.prev('label').removeClass('vas-label-space');
                }
                else if (displayType == VIS.DisplayType.Integer || displayType == VIS.DisplayType.ID || displayType == VIS.DisplayType.Amount) {
                    $filterPriceValue.attr('type', 'number');
                    $filterPriceValue.prev('label').removeClass('vas-label-space');
                }
                else if (displayType == VIS.DisplayType.YesNo) {
                    $filterPriceValue.attr('type', 'checkbox');
                    $filterPriceValue.prev('label').addClass('vas-label-space');
                }
                else {
                    $filterPriceValue.attr('type', 'textbox');
                    $filterPriceValue.prev('label').removeClass('vas-label-space');
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

            $addFilterBtn.on(VIS.Events.onTouchStartOrClick, function (event) {
                var filterCondition = $filterCondition.val();
                var filterColumn = $filterColumnInput.val();              
                var updatedFilterPriceValue = $filterPriceValue.val();
                if (autoComValue != null) {
                    updatedFilterPriceValue = autoComValue;
                }
                var displayType = $filterPrice.children('.vas-column-list-item.active').attr("datatype");
                if (VIS.DisplayType.Date == displayType || VIS.DisplayType.DateTime == displayType) {
                    filterColumn += "TO_CHAR(" + filterColumn + ", 'yyyy-mm-dd')";
                    updatedFilterPriceValue = "'" + updatedFilterPriceValue + "'";
                }
                if (displayType == VIS.DisplayType.YesNo) {
                    if ($filterPriceValue.is(':checked')) {
                        updatedFilterPriceValue = "'Y'";
                    } else {
                        updatedFilterPriceValue = "'N'";
                    }
                }
                if (VIS.DisplayType.String == displayType || VIS.DisplayType.List == displayType
                    || VIS.DisplayType.Text == displayType || VIS.DisplayType.TextLong == displayType) {
                    updatedFilterPriceValue = "'" + updatedFilterPriceValue + "'";
                }
                if (filterColumn != '' && filterCondition != '' && filterCondition != undefined) {
                    if (andFlag) {
                        WhereCondition = "WHERE";
                    }
                    else {
                        WhereCondition = $filterConditionV2.val();
                    }
                    /*if (filterCondition == 'IN') {
                        filterColumn = "(" + $inDropdownVal + ")";
                    }*/
                    if ($(this).hasClass('vas-edit-btn')) {
                        $filters.find('.vas-filter-price-value.active').text(updatedFilterPriceValue);
                        var updatedFilterConditionValue = $filterCondition.find('option:selected').val();
                        $filters.find('.vas-filter-condition.active').text(updatedFilterConditionValue);
                        var andOrOperator = $filterConditionV2.find('option:selected').val();
                        $filters.find('.vas-filter-andor-value.active').text(andOrOperator);
                        $filters.find('.vas-selecttable.active').text(filterColumn);
                        var oldQuery = $filterEditDiv.text();
                        var sqlGenQuery = $selectGeneratorQuery.text();
                        if (filterArrayIndex == 0) {
                            var newQuery = filterColumn + " " + updatedFilterConditionValue + " " + updatedFilterPriceValue;
                        }
                        else {
                            var newQuery = andOrOperator + " " + filterColumn + " " + updatedFilterConditionValue + " " + updatedFilterPriceValue;
                        }

                        $(this).removeClass('vas-edit-btn');
                        ClearText();
                        autoComValue = null;
                        $addFilterBtn.val(VIS.Msg.getMsg("VAS_AddFilter"));
                        $filterSelectArrow.css('pointer-events', 'all');
                        newQuery = newQuery.replace(/\s{2,}/g, ' ');
                        sqlGenQuery = sqlGenQuery.replace(/\s{2,}/g, ' ');
                        var editedQuery = sqlGenQuery.replace(oldQuery, newQuery);
                        $selectGeneratorQuery.text(editedQuery);
                        if (filterIndex > -1) {
                            filterArray[filterIndex].filterCondition = updatedFilterConditionValue;
                            filterArray[filterIndex].filterValue = updatedFilterPriceValue;
                            filterArray[filterIndex].filterAndOrValue = andOrOperator;
                        }
                    }
                    else {
                        $addFilterDiv.append($filters);
                        ApplyFilter(WhereCondition);
                        addFilter();

                    }
                    $filterColumnInput.val('');
                    ClearText();
                    //$inDropdownVal = [];
                   // $inOperatorValues.empty();
                }
                else {
                    $sqlResultDiv.text(VIS.Msg.getMsg("VAS_AddFilterValues"));
                    $sqlResultDiv.addClass('vas-sql-result-error');
                }
            });

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
                    data += '<div class="vas-selecttable join-title" style="background-color:' + randomColor() +'">' + joinsArray[i].joinsDropDown + '</div>';
                    data += '<div class="vas-selecttable join-base-table" style="background-color:' + randomColor() +'">' + joinsArray[i].keyColumn1 + '</div>';
                    data += '<div class="vas-selecttable join-tab" style="background-color:' + randomColor() +'">' + joinsArray[i].joinTableName + '</div>';
                    data += '<div class="vas-selecttable join-jointable" style="background-color:' + randomColor() +'">' + joinsArray[i].keyColumn2 + '</div>';
                    if (joinsArray[i].joinSelectedColumn.length > 0) {
                        data += '<div class="vas-selecttable join-joinselectedcolumn" style="background-color:' + randomColor() +'">' + joinsArray[i].joinSelectedColumn + '</div>';
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
                if (!target.is($filterSelectArrow) && !target.is($filterColumnInput) && !target.is($filterPrice)) {
                    $filterPrice.hide();
                }
                if (!target.is($baseTableSelectArrow) && !target.is($baseTableJoinInput) && !target.is($joinOnFieldColumnMainTable)) {
                    $joinOnFieldColumnMainTable.hide();
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
                var joinsDropDown = $joinsDropdown.find('option:selected').val();
                const joinsObj = { joinsDropDown, joinTableName, keyColumn1, keyColumn2, joinSelectedColumn };
                if (joinsDropDown && joinTableName && keyColumn1 && keyColumn2 && joinSelectedColumn) {
                    joinsArray.push(joinsObj);
                }
                if (sql != '' && keyColumn1 != '' && keyColumn2 != '' && keyColumn1 != null && keyColumn2 != null) {
                    var fromIndex = sql.indexOf('FROM');
                    if (fromIndex != -1) {
                        var fromClause = sql.slice(fromIndex);
                        if (seletedJoinCloumn.length > 0) {
                            sql = sql.slice(0, fromIndex) + ", " + seletedJoinCloumn + fromClause;
                            joinColumnName = seletedJoinCloumn;

                        } else {
                            sql = sql.slice(0, fromIndex) + fromClause;
                        }
                        joinQuery = " " + $joinsDropdown.val() + " " + joinTable + ' ON (' + keyColumn1 + " = " + keyColumn2 + ")";
                        var addJoinQuery = $joinsDropdown.val() + " " + joinTable + ' ON (' + keyColumn1 + " = " + keyColumn2 + ")";
                        $selectGeneratorQuery.text(addJoinQuery);

                    }
                    var whereIndex = sql.indexOf('WHERE');
                    var orderIndex = sql.indexOf('ORDER BY');
                    if (whereIndex != -1 || orderIndex != -1) {
                        if (whereIndex != -1) {
                            var whereClause = sql.slice(whereIndex);
                            sql = sql.slice(0, whereIndex) + joinQuery + " " + whereClause;
                        }
                        else {
                            var whereClause = sql.slice(orderIndex);
                            sql = sql.slice(0, orderIndex) + joinQuery + " " + whereClause;
                        }

                    } else {
                        sql += joinQuery;
                    }                   
                    if (joinData != null) {
                        if (joinData && joinData.length > 0) {
                            joinTableName = joinData[0].TableName;
                            joinCommonColumn = joinCommonColumn.concat(joinData);
                            for (var i = 0; i < joinData.length; i++) {
                                var optionValue = joinTableName + "." + joinData[i].DBColumn;
                                var optionText = joinTableName + " > " + joinData[i].ColumnName + " (" + joinData[i].DBColumn + ")";
                                if (!addedOptions.includes(optionValue)) {
                                    $sortByDropdown.append(" <div class='vas-column-list-item' title='" + optionText + "' value=" + optionValue + ">" + optionText + "</div>");
                                    $filterPrice.append(" <div class='vas-column-list-item' title='" + optionText + "' refValId=" + joinData[i].ReferenceValueID + " fieldID=" + joinData[i].FieldID + " tabID=" + tabID + " columnID=" + joinData[i].ColumnID +
                                        " datatype=" + joinData[i].DataType + " value=" + optionValue + ">" + optionText + "</div>");
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
                    getJoinsColumns(0, 0)
                }
            });

            /* Auto complete suggestion for lookups to fileration*/

            autoComValue = null;
            $filterPriceValue.vaautocomplete({
                minLength: 1,
                source: function (term, response) {
                    var displayType = $filterPrice.children('.vas-column-list-item.active').attr("datatype");
                    var res = [];
                    if (displayType == VIS.DisplayType.TableDir || displayType == VIS.DisplayType.Table || displayType == VIS.DisplayType.Search) {
                        var filtervalue = $filterPrice.children('.vas-column-list-item.active').attr('value');
                        var tabID = $filterPrice.children('.vas-column-list-item.active').attr("tabid");
                        var referenceValueID = $filterPrice.children('.vas-column-list-item.active').attr("refValId");
                        var columnName = filtervalue.slice(filtervalue.indexOf('.') + 1)
                        var columnID = $filterPrice.children('.vas-column-list-item.active').attr("columnID");
                        var fieldID = $filterPrice.children('.vas-column-list-item.active').attr("fieldID");
                        var windowId = $filterPrice.children('.vas-column-list-item.active').attr("WindowID");
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
                    else if ($filterCondition.val() == 'LIKE' || $filterCondition.val() == 'NOT LIKE') {
                        var searchValue = $filterPriceValue.val();
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

            $addSortBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var sortQuery = "";
                var sortColumn = $sortColumnInput.val();
                var sql = $selectGeneratorQuery.text();
                if (sql != '' && sortColumn != '') {
                    if (orderbyFlag) {
                        sortQuery = " ORDER BY " + sortColumn + " " + $sortElements.val();
                        orderbyFlag = false;
                    }
                    else {
                        sortQuery = ", " + sortColumn + " " + $sortElements.val();
                    }
                }
                sql += sortQuery;
                $selectGeneratorQuery.text('');
                $selectGeneratorQuery.text(sql);
            });

            // Clear/refresh sql query
            $sqlGenDeleteIcon.on(VIS.Events.onTouchStartOrClick, function () {
                clear();
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
        }

        /*
            AJAX Call to get Alert Data
        */
        this.SqlQuery = function (ParentId) {
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
                        if (result && result.length > 0) {
                            $selectQuery.text(result);
                        }
                    },
                    error: function (error) {
                        console.log(error);
                    }
                });
                $sqlBtn.trigger('click');
                $sqlResultDiv.show();
            }
            else {
                $windowTabSelect.setValue(null);
                OnChange();
                $selectQuery.text('');
                $sqlGeneratorBtn.attr('disabled', false);
                $sqlGeneratorBtn.css("opacity", 1);
                $testSqlBtn.val(testSQL);
                $queryResultGrid.hide();
                $query.show();
                $selectQuery.show();
                $saveBtn.hide();
            }
        }

        /*
            Function to delete the particular filter in Filter Accordion
        */

        function deleteFilter() {
            var filterItem = $(event.target).parents('.vas-filter-item');
            var andOrVal = filterItem.find(".vas-filter-andor-value").text();
            var filterSelectTableVal = filterItem.find(".vas-selecttable").text();
            var filterValue = filterItem.find(".vas-filter-price-value").text();
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
        }

        /*
            Function of Event Handling
        */
        function eventHandling() {
            $windowTabSelect.fireValueChanged = OnChange;
        }

        /*
            Onchange function to get the table data
        */

        function OnChange() {
            tableID = 0;
            var tabID = $windowTabSelect.getValue();
            if (tabID > 0) {
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/GetTable",
                    type: "POST",
                    async: false,
                    data: { tabID: tabID },
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result && result.length > 0) {
                            tableID = result[0].TableID;
                            tableName = result[0].TableName;
                            tabID = result[0].TabID;
                        }
                    },
                    error: function (error) {
                        console.log(error);
                    }
                });
            }
            if (tableID > 0) {
                getColumns(tableID, tabID);
                $joinsWindowTabSelect.fireValueChanged = joinsTableOnChange;
                $windowTabSelect.getControl().removeClass("vis-ev-col-mandatory");
            }
            else {
                clear();
            }
           
        }

        /*
            Function to get Window/Tab data in Joins
        */

        function joinsTableOnChange() {
            var tabID = $joinsWindowTabSelect.getValue();
            var TableID2 = 0;
            if (tabID > 0) {
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/GetTable",
                    type: "POST",
                    async: false,
                    data: { tabID: tabID },
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result && result.length > 0) {
                            TableID2 = result[0].TableID;
                        }
                    },
                    error: function (error) {
                        console.log(error);
                    }
                });
            }
            if (TableID2 != null && TableID2 != "") {
                getJoinsColumns(TableID2, tabID);
            }
            else {
                getJoinsColumns(0, 0);
            }
        }

        /*
            Function to apply filters
        */

        function ApplyFilter(WhereCondition) {
            var sql = $selectGeneratorQuery.text();
            var orderIndex = sql.indexOf('ORDER BY');
            var whereSql = "";
            var filterval = $filterColumnInput.val();
            var filterValue = $filterPriceValue.val();
            var filterCondition = $filterCondition.val();
            if (filterval != null) {
                var filterColumn = filterval.slice(filterval.indexOf(':') + 1);
                var dataType = $filterPrice.children('.vas-column-list-item.active').attr("datatype");
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
                        else if (VIS.DisplayType.Integer == dataType || VIS.DisplayType.ID == dataType || VIS.DisplayType.IsSearch == dataType) {
                            sql += " " + filterColumn + " " + filterCondition + " " + filterValue;
                        }
                        else if (VIS.DisplayType.String == dataType || VIS.DisplayType.List == dataType || VIS.DisplayType.Text == dataType || VIS.DisplayType.TextLong == dataType) {
                            sql += " " + filterColumn + " " + filterCondition + " '" + filterValue + "'";
                        }
                        else if (VIS.DisplayType.Date == dataType || VIS.DisplayType.DateTime == dataType) {
                            sql += " TO_CHAR(" + filterColumn + ", 'yyyy-mm-dd') " + filterCondition + " '" + filterValue + "'";
                        }
                        else if (VIS.DisplayType.YesNo == dataType) {
                            if ($filterPriceValue.is(':checked')) {
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
                        else if (VIS.DisplayType.YesNo == dataType) {
                            if ($filterPriceValue.is(':checked')) {
                                whereSql += " " + filterColumn + " " + filterCondition + " 'Y'";
                            } else {
                                whereSql += " " + filterColumn + " " + filterCondition + " 'N'";
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
            $filterPrice.val('');
            $filterPriceValue.val('');
            $filterCondition.val('');
        }


        /*
            Function to get the columns from Table in Window/Tab
        */
        function getColumns(tableID, tabID) {
            $selectBox.find('select').empty();
            var flag = true;
            var seletedCloumn = [];
            $checkBoxes.empty();
            $sortByDropdown.empty();
            $filterPrice.empty();
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
                        tableName = result[0].TableName;
                        joinCommonColumn = result;
                        for (var i = 0; i < result.length; i++) {
                            $sortByDropdown.append("<div class='vas-column-list-item' title='" + tableName + " > " + result[i].ColumnName + " (" + result[i].DBColumn + ")" + "' value=" + tableName + "." + result[i].DBColumn + ">" + tableName + " > " + result[i].ColumnName + " (" + result[i].DBColumn + ")" + "</div>");
                            $filterPrice.append("<div class='vas-column-list-item' title='" + tableName + " > " + result[i].ColumnName + " (" + result[i].DBColumn + ")" + "' refValId=" + result[i].ReferenceValueID + " fieldID=" + result[i].FieldID + " WindowID=" + result[i].WindowID + " tabID=" + tabID + " columnID="
                                + result[i].ColumnID + " datatype=" + result[i].DataType + " value=" + tableName + "." + result[i].DBColumn + ">" + tableName + " > " + result[i].ColumnName + " (" + result[i].DBColumn + ")" + "</div>");
                            $checkBoxes.append(" <div class='vas-column-list-item' title='" + result[i].FieldName + " - " + result[i].DBColumn + "'>" + "<input type='checkbox' class='vas-column-checkbox' data-oldIndex = "+i+">" + result[i].FieldName + " - " + result[i].DBColumn + "</div>");
                        }
                    }
                    seletedCloumn = [];
                    $(".vas-column-list-item .vas-column-checkbox").on('click', function (eve) {
                        if (!flag) {
                            $(".vas-column-checkbox").prop("checked", false);
                            flag = true;
                        }
                        var selectedItem = $(this).parent('.vas-column-list-item').text();
                        var fieldName = selectedItem.slice(0, selectedItem.indexOf('-') - 1);
                        fieldName = fieldName.replace(/[^a-zA-Z0-9\s]+/g, '');
                        if (fieldName != "") {
                            var desiredResult = tableName + "." + selectedItem.substring(selectedItem.indexOf('-') + 1).trim() + " AS " + '"' + fieldName + '" ';
                        }
                        else {
                            var desiredResult = tableName + "." + selectedItem.substring(selectedItem.indexOf('-') + 1).trim() + " ";
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
                            GetSQL(seletedCloumn);
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
                        } else {
                            // Get the index of the current item
                            var currentIndex = $(this).data('oldindex');
                            $currentItem.insertAfter($checkBoxes.children().eq(currentIndex));
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
        function getJoinsColumns(TableID2, tabID2) {
            var joinColumns = [];
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
                        var primaryKeyMTable=null;
                        var primaryKeyjTable=null;
                        for (var i = 0; i < result.length; i++) {
                            for (var j = 0; j < joinCommonColumn.length; j++) {
                                var optionMTableValue = joinCommonColumn[j].TableName + "." + joinCommonColumn[j].DBColumn;
                                var optionMTableText = joinCommonColumn[j].DBColumn + " (" + joinCommonColumn[j].TableName + ")";
                                var optionJTableValue = joinTable + "." + result[i].DBColumn;
                                var optionJTableText = result[i].DBColumn + " (" + joinTable + ")";
                                if (joinCommonColumn[j].DBColumn == result[i].DBColumn) {
                                    if (!joinColumns.includes(result[i].DBColumn)) {
                                        $joinOnFieldColumnJoinTable.append("<div class='vas-column-list-item' title='" + optionJTableText + "'value=" + optionJTableValue + ">" + optionJTableText+ "</div>");
                                        joinColumns.push(result[i].DBColumn);
                                    }
                                    $joinOnFieldColumnMainTable.append(" <div class='vas-column-list-item' title='" + optionMTableText + "' value=" + optionMTableValue + ">" + optionMTableText + "</div>");
                                }
                                if (joinCommonColumn[j].IsKey=='Y' && $baseTableJoinInput.val()!=null) {
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
                            $joinMultiSelect.append(" <div class='vas-column-list-item' title='" + result[i].FieldName + " - " + result[i].DBColumn + "'>" + "<input type='checkbox' class='vas-column-checkbox' data-oldIndex = " + i +">" + result[i].FieldName + " - " + result[i].DBColumn + "</div>");
                        }
                        if ($joiningTableInput.val() == '' && primaryKeyjTable != null) {
                            $joiningTableInput.val(joinTable + "." + primaryKeyjTable);
                        }
                        filterFlag = false;
                        seletedJoinCloumn = [];
                        $(".vas-join-multiselect .vas-column-checkbox").on('click', function (eve) {
                            var selectedItem = $(this).parent('.vas-column-list-item').text();
                            var fieldName = selectedItem.slice(0, selectedItem.indexOf('-') - 1);
                            fieldName = fieldName.replace(/[^a-zA-Z0-9\s]+/g, '');
                            if (fieldName != "") {
                                var desiredResult = joinTable + "." + selectedItem.substring(selectedItem.indexOf('-') + 1).trim() + " AS " + '"' + fieldName + '" ';
                            }
                            else {
                                var desiredResult = joinTable + "." + selectedItem.substring(selectedItem.indexOf('-') + 1).trim() + " ";
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
            $joinsWindowTabSelect = new VIS.Controls.VTextBoxButton("AD_Tab_ID", true, false, true, VIS.DisplayType.Search, lookups);
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
            var locDep = $windowTabSelect.getControl().attr('placeholder', ' Search here').attr("id", "Ad_Table_ID");
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
                alertRuleID = $self.ParentId;
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/UpdateQuery",
                    type: "POST",
                    data: { query: VIS.secureEngine.encrypt(query), alertRuleID: alertRuleID, alertID: alertID, tableID: tableID },
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

        function GetSQL(columnValue) {
            var currentSql = $selectGeneratorQuery.text();
            var fromIndex = currentSql.indexOf('FROM');
            var joinIndex = currentSql.indexOf('JOIN');
            $selectGeneratorQuery.text('');
            if (fromIndex == -1) {
                sql = "SELECT " + columnValue + "FROM " + tableName;
            }
            else if (joinIndex != -1 && joinColumnName.length > 0) {
                sql = "SELECT " + columnValue + ", " + joinColumnName + currentSql.slice(fromIndex);
            }
            else {
                sql = "SELECT " + columnValue + currentSql.slice(fromIndex);
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
                    data: { Query: VIS.secureEngine.encrypt(query), pageNo: pageNo, pageSize: pageSize },
                    type: "POST",
                    async: false,
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result != null && result != []) {

                            if (result.length > 0) {
                                if (sqlFlag) {
                                    sqlGrid(result);
                                    $sqlResultDiv.hide();
                                }
                                if (sqlGenerateFlag) {
                                    sqlGeneratorGrid(result);
                                    $sqlResultDiv.hide();
                                }
                            }
                            else {
                                if (sqlFlag) {
                                    $testSqlBtn.val(testSQL);
                                    $queryResultGrid.hide();
                                    $query.show();
                                    $selectQuery.show();
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
            self.parent($filterPrice).prev($filterInputBlock).find($filterColumnInput).val($filterItemVal);
            $filterSelectedItem.removeClass('active');
            self.addClass('active');
            var activeItemDataType = $filterPrice.children('.vas-column-list-item.active').attr('datatype');
            self.parent($filterPrice).prev($filterInputBlock).find($filterColumnInput).attr('datatype', activeItemDataType);
            $filterPrice.hide();
            var displayType = self.attr("datatype");
            $filterCondition.val('');
            $filterCondition.removeClass('vas-checkboxoption-hidden');
            $filterCondition.removeClass('vas-remove-isnulloption');
            $filterCondition.removeClass('vas-add-isnulloption');
            $filterCondition.removeClass('vas-remove-likeoption');
            if (displayType == VIS.DisplayType.Date || displayType == VIS.DisplayType.DateTime) {
                $filterPriceValue.attr('type', 'date');
                $filterPriceValue.prev('label').removeClass('vas-label-space');
                $filterCondition.addClass('vas-add-isnulloption');
                $filterCondition.addClass('vas-remove-likeoption');
            }
            else if (displayType == VIS.DisplayType.TableDir || displayType == VIS.DisplayType.Table || displayType == VIS.DisplayType.Search ||
                displayType == VIS.DisplayType.Integer || displayType == VIS.DisplayType.ID || displayType == VIS.DisplayType.Amount) {
                $filterPriceValue.attr('type', 'number');
                $filterPriceValue.prev('label').removeClass('vas-label-space');
                $filterCondition.addClass('vas-remove-likeoption');
            }
            else if (displayType == VIS.DisplayType.String || VIS.DisplayType.List == displayType || VIS.DisplayType.Text == displayType || VIS.DisplayType.TextLong == displayType) {
                $filterPriceValue.attr('type', 'textbox');
                $filterPriceValue.prev('label').removeClass('vas-label-space');
                $filterCondition.addClass('vas-add-likeoption');
                $filterCondition.addClass('vas-add-isnulloption');
            }
            else if (displayType == VIS.DisplayType.YesNo) {
                $filterPriceValue.attr('type', 'checkbox');
                $filterPriceValue.prev('label').addClass('vas-label-space');
                $filterCondition.addClass('vas-checkboxoption-hidden');
                $filterCondition.addClass('vas-remove-isnulloption');
            }
            else {
                $filterPriceValue.attr('type', 'textbox');
                $filterPriceValue.prev('label').removeClass('vas-label-space');
            }
        }

        function clear() {
            tableID = 0;
            tableName = "";
            tabID = 0;
            $selectGeneratorQuery.text('');
            joinsArray = [];
            $joins.empty();
            filterArray = [];
            $filters.empty();
            $windowFieldColumnSelect.addClass("vis-ev-col-mandatory");
            getColumns(0, 0);
            getJoinsColumns(0, 0)
            $joinsDiv.css("pointer-events", "none");
            $filterDiv.css("pointer-events", "none");
            $sortByDiv.css("pointer-events", "none");
            $checkBoxes.hide();
            $checkBoxes.children('.vas-column-list-item').find('input[type="checkbox"]').prop('checked', false);
            $(".vas-column-checkbox").prop("checked", false);
            $sortColumnInput.val('');
            $filterColumnInput.val('');
            $windowTabSelect.getControl().addClass("vis-ev-col-mandatory");
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
            andFlag = true;
            orderbyFlag = true;
            $windowTabSelect.setValue(null)
            addedOptions = [];
            joinColumnName = [];
            seletedJoinCloumn = [];
            joinData = null;
            $filterPriceValue.attr('type', 'Textbox');
            $filterPriceValue.val('');
            joinCommonColumn = [];
            $baseTableJoinInput.val('');
            $joiningTableInput.val('');
            $joinOnFieldColumnMainTable.empty();
            $joinOnFieldColumnJoinTable.empty();

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
            $filterCondition = null;
            $filterConditionV2 = null;
            $filterPriceValue = null;
            $sortElements = null;
            removeColumnJoins = null;
            oldFilterVal = null;
            filterIndex = null;
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