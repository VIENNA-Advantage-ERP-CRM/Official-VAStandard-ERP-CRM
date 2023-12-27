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
        var alertID = 0;
        var alertRuleID = 0;
        var tableName = null;
        var seletedCloumn = null;
        var andFlag = true;
        var orderbyFlag = true;
        var sqlGenerateFlag = false;
        var removeColumnJoins = null;
        var sqlFlag = true;
        var filterIndex = null;
        var WhereCondition;
        var oldFilterVal = null;
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
        var filterArray;
        this.dGrid = null;
        this.dGridGen = null;
        var $windowTabDiv = $("<div class='vas-windowtab'>");
        var $windowTabLabel = $("<label>");
        var $joinFieldColumnLabel = $("<label>");
        var $windowTabSelect = null;
        var $txtWindow = $("<div class='VIS-AMTD-formData VIS-AMTD-InputBtns input-group vas-input-wrap'>");
        var $windowFieldColumnLabel = $("<label>");
        var $windowFieldColumnSelect = $("<div class='vas-windowfieldcol-select vas-windowtab vis-ev-col-mandatory' style='display: none;'></div>");
        var downArrow = $('<span class="vis vis-arrow-down vas-arrow-down"></span>');
        var $joinsSelect = $("<div style='display: none;' class='vas-joins-select vas-common-style vas-windowtab'>");
        var $selectQuerySqlText = $("<div class='vas-sqlquery-text'>");
        var $selectGenQuerySqlText = $("<div class='vas-sqlquery-text'>");
        var $selectQuery = $("<div class='vas-query-input vas-sql-query-input' id='query-input' contenteditable='true'>");
        var $selectGeneratorQuery = $("<div class='vas-query-input' contenteditable='false'>");
        var $multiSelect = $("<div class='vas-multiselect'>");
        var $selectBox = $("<div class='vas-selectBox vas-windowtab'><select><option>" + VIS.Msg.getMsg("VAS_SelectAll") + "</option></select><div class='vas-overselect'></div></div>");
        var $checkBoxes = $("<div class='vas-checkboxes'>");
        var $joins = $("<div class='vas-joins'>");
        var $joinsDiv = $("<div class='vas-add-label'>");
        var $joinsLabel = $("<label>");
        var $column1Div = $("<div class='vas-column1'>");
        var $addJoinsHeading = $("<h4>");
        var $addJoinsDiv = $("<div class='vas-add-label-content'>");
        var $joinsWindowTabSelect = null;
        var $txtJoinWindow = $("<div class='VIS-AMTD-formData VIS-AMTD-InputBtns input-group vas-input-wrap'>");
        var $joinsDropdown = null;
        var $removeJoins = null;
        var $filterEditDiv = $("<div style='display:none;'>");
        var $joinOnFieldColumnName1Div = $("<div class='vas-windowtab vas-join-field'>");
        var $joinOnFieldColumnName1Label = $("<label>");
        var $joinOnFieldColumnName2Div = $("<div class='vas-windowtab vas-join-field'>");
        var $joinOnFieldColumnName2Label = $("<label>");
        var $joinOnFieldColumnMainTable = null;
        var $joinOnFieldColumnJoinTable = null;
        var $filters = $("<div class='vas-filters'>");
        var $filterDiv = $("<div class='vas-add-label'>");
        var $filterLabel = $("<label>");
        var $addFilterText = $("<div class='vas-addfiltertext'>");
        var $addFilterSelectButton = $("<div class='vas-select-plus-btn'>");
        var $addFilterDiv = $("<div class='vas-add-label-content'>");
        var $filterColumnNameLabel = $("<label>");
        var $filterColumnNameDiv = $("<div class='vas-windowtab vas-filter-field'>");
        var $filterOperatorLabel = $("<label>");
        var $filterOperatorDiv = $("<div class='vas-windowtab vas-operator'>");
        var $filterValueLabel = $("<label>");
        var $filterValueDiv = $("<div class='vas-windowtab vas-columnval'>");
        var $filterPrice = $("<select class='vas-filter-text-input'>");
        var $filterCondition = $("<select><option>=</option><option>></option><option><</option><option><=</option><option>>=</option><option><></option><option>IS NULL</option><option>IS NOT NULL</option><option>IN</option><option>NOT IN</option></select>");
        var $filterPriceValue = $("<input type='textbox' class='vas-filter-text-input'>");
        var $filterConditionV2 = null;
        var $sortElements = null;
        var $fieldColDropdown = $("<input class='vas-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $fieldJoinColDropdown = $("<input class='vas-join-select-col' type='textbox' placeholder='" + VIS.Msg.getMsg("VAS_TypeColumn") + "'>");
        var $sortByDiv = $("<div class='vas-add-label'>");
        var $sortByLabel = $("<label>");
        var $addSortByText = $("<h4>");
        var $addSortByDiv = $("<div class='vas-add-label-content'>");
        var $sortByDropdown = $("<select class='vas-sortby-dropdown'>");
        var $addSortBySelectWithButton = $("<div class='vas-select-plus-btn'>");
        var $sqlBtns = $("<div class='vas-sql-btns'>");
        var $contentArea = $("<div class='vas-content-area'>");
        var $queryMessage = $("<div class='vas-query-message'>");
        var $sqlContent = $("<div class='vas-sql-content'></div>");
        var $queryResultGrid = $("<div style='display:none;' class='vas-queryresult-grid'></div>");
        var $sqlGeneratorContent = null;
        var $sqlGeneratorQueryResultGrid = $("<div style='display:none;' class='vas-queryresult-grid'></div>");
        var sqlQuery = VIS.Msg.getMsg("VAS_SQLQuery");
        var testSQL = VIS.Msg.getMsg("VAS_TestSql");
        var $root = $("<div class='vas-root'>");

        // Initialize UI Elements
        this.init = function () {
            $sqlBtn = $("<input class='VIS_Pref_btn-2 active vas-sql-btn' id='vas-sql-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_SQL") + "'>");
            $sqlGeneratorBtn = $("<input class='VIS_Pref_btn-2 vas-sql-generator' id='vas-sql-generatorbtn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_SQLGenerator") + "'>");
            $testSqlGeneratorBtn = $("<input style='display: none;' class='VIS_Pref_btn-2 vas-test-sql vas-test-sqlgenerator' id='vas-testsql-generatorbtn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("TestSql") + "'>");
            $testSqlBtn = $("<input class='VIS_Pref_btn-2 vas-test-sql' id='vas-testsql-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_TestSql") + "'>");
            $saveBtn = $("<input class='VIS_Pref_btn-2 vas-save-btn' type='button' id='vas-save-btn" + $self.windowNo + "' value='" + VIS.Msg.getMsg("VAS_Save") + "'>");
            $saveGeneratorBtn = $("<input class='VIS_Pref_btn-2 vas-save-btn' id='vas-savegenerator-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_Save") + "'>");
            $addJoinBtn = $("<input class='VIS_Pref_btn-2' id='vas-addjoin-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_AddJoin") + "'>");
            $addFilterBtn = $("<input class='VIS_Pref_btn-2 vas-add-btn' id='vas-addfilter-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_AddFilter") + "'>");
            $addSortBtn = $("<input class='VIS_Pref_btn-2 vas-add-btn' id='vas-addsort-btn" + $self.windowNo + "' type='button' value='" + VIS.Msg.getMsg("VAS_AddSort") + "'>");
            $sqlGeneratorContent = $("<div class='vas-sqlgenerator' id='vas-sqlgeneratorcontent" + $self.windowNo + "' style='display: none;'><div class='vas-sqlgenerator-column vas-sqlgenerator-column1'></div><div class='vas-sqlgenerator-column vas-sqlgenerator-column2'></div></div>");
            $joinsDropdown = $("<select class='vas-joins-collection'><option>INNER JOIN</option><option>LEFT JOIN</option><option>RIGHT JOIN</option><option>FULL JOIN</option></select>");
            $joinOnFieldColumnMainTable = $("<select><option>" + VIS.Msg.getMsg("VAS_MainTable_Column") + "</option></select>");
            $joinOnFieldColumnJoinTable = $("<select><option>" + VIS.Msg.getMsg("VAS_JoinTable_Column") + "</option></select>");
            $filterConditionV2 = $("<select><option>AND</option><option>OR</option></select>");
            $sortElements = $("<select><option>ASC</option><option>DESC</option></select>");

            $sqlBtns.append($sqlBtn)
                .append($sqlGeneratorBtn)
                .append($sqlResultDiv)
                .append($testSqlBtn)
                .append($testSqlGeneratorBtn);
            $windowTabLabel.text(VIS.Msg.getMsg("VAS_WindowTab"));
            $selectQuerySqlText.text(VIS.Msg.getMsg("VAS_SQL"));
            $selectGenQuerySqlText.text(VIS.Msg.getMsg("VAS_SQLQuery"));
            $windowFieldColumnLabel.text(VIS.Msg.getMsg("VAS_FieldColumn"));
            $joinOnFieldColumnName1Label.text(VIS.Msg.getMsg("VAS_MainTable_Column"));
            $joinOnFieldColumnName2Label.text(VIS.Msg.getMsg("VAS_JoinTable_Column"));
            $joinFieldColumnLabel.text(VIS.Msg.getMsg("VAS_FieldColumn"));
            $joinsLabel.text(VIS.Msg.getMsg("VAS_Joins"));
            $addJoinsHeading.text(VIS.Msg.getMsg("VAS_AddJoin"));
            $filterLabel.text(VIS.Msg.getMsg("VAS_Filter"));
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
            $multiSelect.append($fieldColDropdown);
            $multiSelect.append($windowFieldColumnSelect).append($checkBoxes);
            $windowFieldColumnSelect.append($windowFieldColumnLabel);
            $windowFieldColumnSelect.append(downArrow);
            $joinsDiv.append($joinsLabel);
            $joinOnFieldColumnName1Div.append($joinOnFieldColumnName1Label).append($joinOnFieldColumnMainTable);
            $joinOnFieldColumnName2Div.append($joinOnFieldColumnName2Label).append($joinOnFieldColumnJoinTable);
            $addJoinsDiv.append($addJoinsHeading)
                .append($txtJoinWindow)
                .append($fieldJoinColDropdown)
                .append($joinsSelect)
                .append($joinMultiSelect)
                .append($joinsDropdown)
                .append($joinOnFieldColumnName1Div)
                .append($joinOnFieldColumnName2Div)
                .append($addJoinBtn)
                .append($joins);
            $joinsSelect.append($joinFieldColumnLabel);
            $filterDiv.append($filterLabel);
            $filterColumnNameDiv.append($filterColumnNameLabel).append($filterPrice);
            $filterOperatorDiv.append($filterOperatorLabel).append($filterCondition);
            $filterValueDiv.append($filterValueLabel).append($filterPriceValue);
            $addFilterDiv.append($addFilterText)
                .append($filterColumnNameDiv)
                .append($filterOperatorDiv)
                .append($filterValueDiv)
                .append($addFilterSelectButton)
                .append($filterEditDiv);
            $addFilterSelectButton.append($filterConditionV2).append($addFilterBtn);
            $sortByDiv.append($sortByLabel);
            $addSortByDiv.append($addSortByText).append($sortByDropdown).append($addSortBySelectWithButton);
            $addSortBySelectWithButton.append($sortElements).append($addSortBtn);
            $sqlContent.append($selectQuerySqlText).append($selectQuery).append($queryResultGrid);
            $sqlGeneratorContent.find('.vas-sqlgenerator-column2').append($selectGenQuerySqlText).append($selectGeneratorQuery).append(gridDiv2);

            $fieldColDropdown.on("keyup", function () {
                var $fieldColDropdownVal = $(this).val().toLowerCase();
                let windowFieldItem = $checkBoxes.children('.vas-column-list-item').length;
                if (windowFieldItem > 0) {
                    $checkBoxes.show();
                    $('.vas-checkboxes .vas-column-list-item').filter(function () {
                        $(this).toggle($(this).text().toLowerCase().indexOf($fieldColDropdownVal) > -1);                        
                    });
                }
                else {              
                    $checkBoxes.hide();
                }
            });

            $fieldJoinColDropdown.on("keyup", function () {
                var $joinColDropdownVal = $(this).val().toLowerCase();
                let joinFieldItem = $checkBoxes.children('.vas-column-list-item').length;
                if (joinFieldItem > 0) {
                    $joinMultiSelect.show();
                    $('.vas-join-multiselect .vas-column-list-item').filter(function () {
                        $(this).toggle($(this).text().toLowerCase().indexOf($joinColDropdownVal) > -1);
                    });
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

            });

            /*
               Function to collect filter data to display
               on UI when user select the
               dropdowns in Filter Accordion
            */
            filterArray = [];
            function readFilterData() {
                var data = '';
                for (var i = 0; i < filterArray.length; i++) {
                    var dataType = filterArray[i].dataType;
                    data += '<div class=vas-filter-item>';
                    data += '<div class=vas-filter-whitebg>';
                    data += '<div class="vas-filters-block">';
                    data += '<div class="vas-filter-andor-value" style=display:none;>' + filterArray[i].filterAndOrValue + '</div>';
                    if (VIS.DisplayType.Date == dataType || VIS.DisplayType.DateTime == dataType) {
                        data += '<div class="vas-selecttable">' + " TO_CHAR(" + filterArray[i].filterPriceval + ", 'yyyy-mm-dd')" + '</div>';

                    }
                    else {
                        data += '<div class="vas-selecttable">' + filterArray[i].filterPriceval + '</div>';
                    }
                    data += '<div class="vas-filter-condition">' + filterArray[i].filterCondition + '</div>';

                    if (VIS.DisplayType.Integer == dataType || VIS.DisplayType.ID == dataType || VIS.DisplayType.IsSearch == dataType) {
                        data += '<div class="vas-filter-price-value">' + filterArray[i].filterValue + '</div>';
                    }
                    else if (VIS.DisplayType.String == dataType || VIS.DisplayType.List == dataType || VIS.DisplayType.Text == dataType || VIS.DisplayType.TextLong == dataType
                        || VIS.DisplayType.DateTime == dataType || VIS.DisplayType.Date == dataType) {
                        data += '<div class="vas-filter-price-value">' + "'" + filterArray[i].filterValue + "'" + '</div>';
                    }
                    else if (VIS.DisplayType.YesNo == dataType) {
                        if (filterArray[i].IsChecked) {
                            data += '<div class="vas-filter-price-value">' + "'Y'" + '</div>';
                        } else {
                            data += '<div class="vas-filter-price-value">' + "'N'" + '</div>';
                        }
                    }
                    else {
                        data += '<div class="vas-filter-price-value">' + filterArray[i].filterValue + '</div>';
                    }
                    data += '</div>';
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
            function addFilter() {
                var filterPriceval = $filterPrice.find('option:selected').val();
                let filterCondition = $filterCondition.find('option:selected').val();
                var filterValue = $filterPriceValue.val();
                if (autoComValue != null) {
                    filterValue = autoComValue;
                    autoComValue = null;
                }
                var dataType = $filterPrice.find('option:selected').attr("datatype");
                var IsChecked = $filterPriceValue.is(':checked');
                let filterAndOrValue = $filterConditionV2.find('option:selected').val();
                const filterObj = { filterPriceval, filterCondition, filterValue, filterAndOrValue, dataType: dataType, IsChecked: IsChecked }
                filterArray.push(filterObj);
                $filters.empty();
                readFilterData();
                if ($filters && $filters.length > 0) {
                    $filters.find('.vas-filter-item').addClass('vas-filters-row');
                }
            }

            /*
               Click event on Edit and Delete Buttons 
               for Filters in Filter Accordion
            */
            $filters.on(VIS.Events.onTouchStartOrClick, function (event) {
                var filterItem = $(event.target).parents('.vas-filter-item');
                if ($(event.target).hasClass('vis-delete')) {
                    filterArray.splice($(event.target), 1);
                    filterItem.remove();
                    if ($('.vas-filters .vas-filter-item').length < 2) {
                        $('.vas-filters .vas-filter-item').removeClass('vas-first-delete-icon');
                    }

                    deleteFilter();
                    $sqlResultDiv.hide();
                }
                if ($(event.target).hasClass('vis-edit')) {
                    $addFilterBtn.val(VIS.Msg.getMsg("VAS_UpdateFilter"));
                    updateFilter();
                    var filterSelectTableVal = filterItem.find(".vas-selecttable").text();
                    var filterValue = filterItem.find(".vas-filter-price-value").text();
                    var filterCondition = filterItem.find(".vas-filter-condition").text();
                    filterItem.siblings().find(".vas-filter-condition").removeClass('active');
                    filterItem.siblings().find(".vas-filter-price-value").removeClass('active');
                    filterItem.find(".vas-filter-condition").addClass('active');
                    filterItem.find(".vas-filter-price-value").addClass('active');
                    $filterPrice.val(filterSelectTableVal);
                    $filterCondition.val(filterCondition);
                    $filterPriceValue.val(filterValue);
                    $addFilterBtn.addClass('vas-edit-btn');
                    $filterPrice.attr('disabled', true);
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
                oldFilterVal = filterCondition + " " + filterValue;
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
               Click event on Window/Field Column in Joins 
               to display the data in form of multiple checkboxes 
            */
            //$joinsSelect.on(VIS.Events.onTouchStartOrClick, function () {
            //    $(this).next().toggle();
            //});

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
                let filterCondition = $filterCondition.find('option:selected').val();
                if ($filterPrice.val() != '') {
                    if (andFlag) {
                        WhereCondition = "WHERE";
                    }
                    else {
                        WhereCondition = $filterConditionV2.val();
                    }
                    if ($(this).hasClass('vas-edit-btn')) {
                        var updatedFilterPriceValue = $filterPriceValue.val();
                        $('.vas-filter-price-value.active').text(updatedFilterPriceValue);
                        var updatedFilterConditionValue = $filterCondition.find('option:selected').val();
                        $('.vas-filter-condition.active').text(updatedFilterConditionValue);
                        var updatedFilterConditionAndOr = $filterConditionV2.find('option:selected').val();
                        var newQuery = updatedFilterConditionValue + " " + updatedFilterPriceValue;
                        $(this).removeClass('vas-edit-btn');
                        ClearText();
                        $addFilterBtn.val(VIS.Msg.getMsg("VAS_AddFilter"));
                        $filterPrice.removeAttr("disabled");
                        var oldQuery = $filterEditDiv.text();
                        var editedQuery = $selectGeneratorQuery.text().replace(oldQuery, newQuery);
                        $selectGeneratorQuery.text(editedQuery);
                        if (filterIndex > -1) {
                            filterArray[filterIndex].filterCondition = updatedFilterConditionValue;
                            filterArray[filterIndex].filterValue = updatedFilterPriceValue;
                        }
                    }
                    else {
                        if (filterCondition) {
                            $addFilterDiv.append($filters);
                            ApplyFilter(WhereCondition);
                            addFilter();
                        }

                    }
                    ClearText();
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
            joinsArray = [];
            function readJoinsData() {
                var data = '';
                for (var i = 0; i < joinsArray.length; i++) {
                    data += '<div class=vas-join-item>';
                    data += '<div class="vas-joins-bg">';
                    data += '<div class="vas-joins-block">';
                    data += '<div class="vas-selecttable join-title">' + joinsArray[i].joinsDropDown + '</div>';
                    data += '<div class="vas-selecttable join-base-table">' + joinsArray[i].keyColumn1 + '</div>';
                    data += '<div class="vas-selecttable join-tab">' + joinsArray[i].joinTableName + '</div>';
                    data += '<div class="vas-selecttable join-jointable">' + joinsArray[i].keyColumn2 + '</div>';
                    if (joinsArray[i].joinSelectedColumn.length > 0) {
                        data += '<div class="vas-selecttable join-joinselectedcolumn">' + joinsArray[i].joinSelectedColumn + '</div>';
                    }
                    data += '</div>';
                    data += '<div class="vas-delete-join-btn">';
                    data += '<div><i class="vis vis-delete"></i></div>';
                    data += '</div>';
                    data += '</div>';
                    data += '</div>';
                }
                $joins.append(data);
            }

            // Click event on Edit and Delete Buttons for Joins
            $joins.on(VIS.Events.onTouchStartOrClick, function (event) {
                if ($(event.target).hasClass('vis-delete')) {
                    var deleteJoin = $(event.target).parents('.vas-delete-join-btn');
                    var joinsTitle = deleteJoin.prev('.vas-joins-block').find('.join-title').text();
                    var joinsTab = deleteJoin.prev('.vas-joins-block').find('.join-tab').text();
                    var joinsBaseTable = deleteJoin.prev('.vas-joins-block').find('.join-base-table').text();
                    var joinsJoinTable = deleteJoin.prev('.vas-joins-block').find('.join-jointable').text();
                    var columnToRemove = ", " + deleteJoin.prev('.vas-joins-block').find('.join-joinselectedcolumn').text();
                    var reqJoinQuery = joinsTitle + " " + joinsTab + " " + 'ON (' + joinsBaseTable + " = " + joinsJoinTable + ")";
                    $removeJoins = $selectGeneratorQuery.text().replace(reqJoinQuery, '').trim();
                    $selectGeneratorQuery.text($removeJoins);
                    removeColumnJoins = $selectGeneratorQuery.text().replace(columnToRemove, '');
                    $selectGeneratorQuery.empty();
                    $selectGeneratorQuery.text(removeColumnJoins);
                    $(event.target).parents('.vas-join-item').remove();
                    joinsArray.splice($(event.target), 1);
                    $sqlResultDiv.hide();
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
                var keyColumn1 = $joinOnFieldColumnMainTable.val();
                var keyColumn2 = $joinOnFieldColumnJoinTable.val();
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
                        } else {
                            sql = sql.slice(0, fromIndex) + fromClause;
                        }
                        joinQuery = " " + $joinsDropdown.val() + " " + joinTable + ' ON (' + keyColumn1 + " = " + keyColumn2 + ")";
                        var addJoinQuery = $joinsDropdown.val() + " " + joinTable + ' ON (' + keyColumn1 + " = " + keyColumn2 + ")";
                        $selectGeneratorQuery.text(addJoinQuery);

                    }
                    var whereIndex = sql.indexOf('WHERE');
                    if (whereIndex != -1) {
                        var whereClause = sql.slice(whereIndex);
                        sql = sql.slice(0, whereIndex) + joinQuery + " " + whereClause;
                    } else {
                        sql += joinQuery;
                    }
                    if (joinData != null) {
                        if (joinData && joinData.length > 0) {
                            tableName = joinData[0].TableName;
                            for (var i = 0; i < joinData.length; i++) {
                                var optionValue = tableName + "." + joinData[i].DBColumn;
                                var optionText = tableName + " > " + joinData[i].DBColumn;
                                if (!addedOptions.includes(optionValue)) {
                                    $joinOnFieldColumnMainTable.append(" <option value=" + optionValue + ">" + optionText + "</option>");
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
                    seletedJoinCloumn = [];
                }
            });

            $filterPrice.change(function () {
                $filterPriceValue.prop('checked', false);
                var displayType = $filterPrice.find('option:selected').attr("datatype");
                $filterPriceValue.val('');
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
            });

            autoComValue = null;
            $filterPriceValue.vaautocomplete({
                minLength: 2,
                source: function (term, response) {
                    var displayType = $filterPrice.find('option:selected').attr("datatype");
                    if (displayType == VIS.DisplayType.TableDir || displayType == VIS.DisplayType.Table || displayType == VIS.DisplayType.Search) {
                        var filtervalue = $filterPrice.val();
                        var tabID = $filterPrice.find('option:selected').attr("tabid");
                        var referenceValueID = $filterPrice.find('option:selected').attr("refValId");
                        var columnName = filtervalue.slice(filtervalue.indexOf('.') + 1)
                        var columnID = $filterPrice.find('option:selected').attr("columnID");
                        var fieldID = $filterPrice.find('option:selected').attr("fieldID");
                        var windowId = $filterPrice.find('option:selected').attr("WindowID");
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
                                var res = [];
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
                var sortColumn = $sortByDropdown.val();
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
            alertID = $self.selectedRow.ad_alert_id;
            $.ajax({
                url: VIS.Application.contextUrl + "AlertSQLGenerate/GetAlertData",
                type: "POST",
                data: { alertRuleID: alertRuleID, alertID: alertID },
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
            $selectGeneratorQuery.text('');
            joinsArray = [];
            filterArray = [];
            $filters.empty();

            $windowFieldColumnSelect.addClass("vis-ev-col-mandatory");
            if (tableID > 0) {
                getColumns(tableID, tabID);
                $windowTabSelect.getControl().removeClass("vis-ev-col-mandatory");
            }
            else {
                getColumns(0, 0);
                $joinsDiv.css("pointer-events", "none");
                $filterDiv.css("pointer-events", "none");
                $sortByDiv.css("pointer-events", "none");
                $checkBoxes.hide();
                $windowTabSelect.getControl().addClass("vis-ev-col-mandatory");
            }
            $txtJoinWindow.empty();
            getJoinWindow(tabID, tableID);
            $selectGeneratorQuery.show();
            gridDiv2.hide();
            $sqlGeneratorQueryResultGrid.hide();
            andFlag = true;
            orderbyFlag = true;
            $joinsWindowTabSelect.fireValueChanged = joinsTableOnChange;
            addedOptions = [];
            joinData = null;
            $filterPriceValue.attr('type', 'Textbox');
            $filterPriceValue.val('');
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
                            JointableName = result[0].TableName;
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
            var filterval = $filterPrice.val();
            if (filterval != null) {
                var filterColumn = filterval.slice(filterval.indexOf(':') + 1);
                var dataType = $filterPrice.find('option:selected').attr("datatype");
                if (sql != '' && filterColumn != '') {
                    if (orderIndex == -1) {
                        var filterValue = $filterPriceValue.val();
                        if (autoComValue != null) {
                            filterValue = autoComValue;
                        }
                        sql += " " + WhereCondition;
                        if (VIS.DisplayType.Integer == dataType || VIS.DisplayType.ID == dataType || VIS.DisplayType.IsSearch == dataType) {
                            sql += " " + filterColumn + " " + $filterCondition.val() + " " + filterValue;
                        }
                        else if (VIS.DisplayType.String == dataType || VIS.DisplayType.List == dataType || VIS.DisplayType.Text == dataType || VIS.DisplayType.TextLong == dataType) {
                            sql += " " + filterColumn + " " + $filterCondition.val() + " '" + filterValue + "'";
                        }
                        else if (VIS.DisplayType.Date == dataType || VIS.DisplayType.DateTime == dataType) {
                            sql += " TO_CHAR(" + filterColumn + ", 'yyyy-mm-dd') " + $filterCondition.val() + " '" + filterValue + "'";
                        }
                        else if (VIS.DisplayType.YesNo == dataType) {
                            if ($filterPriceValue.is(':checked')) {
                                sql += " " + filterColumn + " " + $filterCondition.val() + " 'Y'";
                            } else {
                                sql += " " + filterColumn + " " + $filterCondition.val() + " 'N'";
                            }
                        }
                        else {
                            sql += " " + filterColumn + " " + $filterCondition.val() + " " + filterValue;
                        }
                    }
                    else {
                        whereSql += " " + WhereCondition;
                        if (VIS.DisplayType.Integer == dataType || VIS.DisplayType.ID == dataType || VIS.DisplayType.IsSearch == dataType) {
                            whereSql += " " + filterColumn + " " + $filterCondition.val() + " " + filterValue;
                        }
                        else if (VIS.DisplayType.String == dataType || VIS.DisplayType.IsDate == dataType || VIS.DisplayType.IsText == dataType || VIS.DisplayType.Text == dataType || VIS.DisplayType.TextLong == dataType) {
                            whereSql += " " + filterColumn + " " + $filterCondition.val() + " '" + filterValue + "'";
                        }
                        else if (VIS.DisplayType.Date == dataType || VIS.DisplayType.DateTime == dataType) {
                            whereSql += " TO_CHAR(" + filterColumn + ", 'yyyy-mm-dd') " + $filterCondition.val() + " '" + filterValue + "'";
                        }
                        else if (VIS.DisplayType.YesNo == dataType) {
                            if ($filterPriceValue.is(':checked')) {
                                whereSql += " " + filterColumn + " " + $filterCondition.val() + " 'Y'";
                            } else {
                                whereSql += " " + filterColumn + " " + $filterCondition.val() + " 'N'";
                            }
                        }
                        else {
                            whereSql += " " + filterColumn + " " + $filterCondition.val() + " " + filterValue;
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
            $.ajax({
                url: VIS.Application.contextUrl + "AlertSQLGenerate/GetColumns",
                data: { tableID: tableID, tabID: tabID },
                type: "POST",
                async: false,
                success: function (result) {
                    result = JSON.parse(result);
                    if (result && result.length > 0) {
                        tableName = result[0].TableName;
                        $selectBox.find('select').prepend("<option>Select</option>");
                        $joinOnFieldColumnMainTable.prepend("<option selected='select'></option>");
                        $sortByDropdown.prepend("<option selected='select'></option>");
                        $filterPrice.prepend("<option selected='select'>Select</option>");
                        for (var i = 0; i < result.length; i++) {
                            $joinOnFieldColumnMainTable.append(" <option value=" + tableName + "." + result[i].DBColumn + ">" + tableName + "> " + result[i].DBColumn + "</option>");
                            $sortByDropdown.append(" <option value=" + tableName + "." + result[i].DBColumn + ">" + tableName + " > " + result[i].ColumnName + "</option>");
                            $filterPrice.append(" <option refValId=" + result[i].ReferenceValueID + " fieldID=" + result[i].FieldID + " WindowID=" + result[i].WindowID + " tabID=" + tabID + " columnID=" + result[i].ColumnID + " datatype=" + result[i].DataType + " value=" + tableName + "." + result[i].DBColumn + ">" + tableName + " > " + result[i].ColumnName + "</option>");
                            $checkBoxes.append(" <div class='vas-column-list-item' title='" + result[i].FieldName + " - " + result[i].DBColumn + "'>" + "<input type='checkbox' class='vas-column-checkbox'>" + result[i].FieldName + " - " + result[i].DBColumn + "</div>");
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
                        GetSQL(seletedCloumn);
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
            $selectBox.find('select').empty();
            $joinOnFieldColumnJoinTable.empty();
            $joinMultiSelect.empty();
            $joinOnFieldColumnJoinTable.prepend("<option selected='select'></option>");
            // $joinsSelect.show();
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
                        for (var i = 0; i < result.length; i++) {
                            $joinOnFieldColumnJoinTable.append(" <option value=" + joinTable + "." + result[i].DBColumn + ">" + joinTable + "> " + result[i].DBColumn + "</option>");
                            $joinMultiSelect.append(" <div class='vas-column-list-item'>" + "<input type='checkbox' class='vas-column-checkbox'>" + result[i].FieldName + " - " + result[i].DBColumn + "</div>");
                            $sortByDropdown.append(" <option value=" + joinTable + "." + result[i].DBColumn + ">" + joinTable + " > " + result[i].ColumnName + "</option>");
                            $filterPrice.append(" <option refValId=" + result[i].ReferenceValueID + " fieldID=" + result[i].FieldID + " tabID=" + tabID + " columnID=" + result[i].ColumnID + " datatype=" + result[i].DataType + " value=" + joinTable + "." + result[i].DBColumn + ">" + joinTable + ">" + tableName + " > " + result[i].ColumnName + "</option>");
                        }
                        filterFlag = false;
                        seletedJoinCloumn = [];
                        $(".vas-join-multiselect .vas-column-checkbox").on('click', function (eve) {
                            var selectedItem = $(this).parent('.vas-column-list-item').text();
                            var desiredResult = joinTable + "." + selectedItem.substring(selectedItem.indexOf('-') + 1).trim() + " AS " + '"' + selectedItem.slice(0, selectedItem.indexOf('-') - 1) + '" ';
                            if (this.checked) {
                                seletedJoinCloumn.push(desiredResult);
                            }
                            else {
                                seletedCloumn = seletedCloumn.filter(function (elem) {
                                    return elem != desiredResult;
                                });
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
            var lookups = new VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.Search, "AD_Tab_ID", 0, false, " AD_Tab.AD_Tab_ID <> " + tabID + " AND AD_Tab.AD_Table_ID<>" + tableID);
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
            var lookups = new VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.Search, "AD_Tab_ID", 0, false, "");
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
            $selectGeneratorQuery.text('');
            if (fromIndex == -1) {
                if (columnValue.length > 0) {
                    $selectGeneratorQuery.text("SELECT " + columnValue + "FROM " + tableName);
                }
            }
            else {
                var fromSql = currentSql.slice(fromIndex).trim();
                $selectGeneratorQuery.text("SELECT " + columnValue + fromSql);
            }
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
            if ($sqlGeneratorBtn)
                $sqlGeneratorBtn.off(VIS.Events.onTouchStartOrClick);
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