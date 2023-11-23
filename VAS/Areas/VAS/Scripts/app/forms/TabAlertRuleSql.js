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
        var pageNo = 0;
        var TableID = 0;
        var alertID = 0;
        var alertRuleID = 0;
        var tableName = null;
        var andFlag = true;
        var orderbyFlag = true;
        var sqlGenerateFlag = false;
        var sqlFlag = true;
        var WhereCondition;
        var totalPages;
        var loadTotalPages = true;
        var joinTable;
        var seletedJoinCloumn = [];
        var $query = $("<div>");
        var gridDiv = $("<div class='vas-grid-div1'>");
        var gridDiv2 = $("<div class='vas-grid-div2'>");
        var $joinMultiSelect = $("<div class='vas-join-multiselect'>");
        var dGrid = null;
        var dGrid2 = null;
        var $sqlBtn = $("<input class='VIS_Pref_btn-2 active' type='button' value=' " + VIS.Msg.getMsg("VAS_SQL") + "'>");
        var $sqlResultDiv = $("<div class='vas-sql-result-msg'>");
        var $sqlGeneratorBtn = $("<input class='VIS_Pref_btn-2 vas-sql-generator' type='button' value=' " + VIS.Msg.getMsg("VAS_SQLGenerator") + "'>");
        var $testSqlGeneratorBtn = $("<input style='display: none;' class='VIS_Pref_btn-2 vas-test-sql vas-test-sqlgenerator' type='button' value=' " + VIS.Msg.getMsg("TestSql") + "'>");
        var $testSqlBtn = $("<input class='VIS_Pref_btn-2 vas-test-sql' type='button' value=' " + VIS.Msg.getMsg("VAS_TestSql") + "'>");
        var $saveBtn = $("<input class='VIS_Pref_btn-2 vas-save-btn' type='button' value=' " + VIS.Msg.getMsg("VAS_Save") + "'>");
        var $updateGenerateBtn = $("<input class='VIS_Pref_btn-2 vas-update-btn' type='button' value=' " + VIS.Msg.getMsg("Update") + "'>");
        var $saveGeneratorBtn = $("<input class='VIS_Pref_btn-2 vas-save-btn' type='button' value=' " + VIS.Msg.getMsg("VAS_Save") + "'>");
        var $addJoinBtn = $("<input class='VIS_Pref_btn-2' type='button' value=' " + VIS.Msg.getMsg("VAS_AddJoin") + "'>");
        var $addFilterBtn = $("<input class='VIS_Pref_btn-2 vas-add-btn' type='button' value=' " + VIS.Msg.getMsg("VAS_AddFilter") + "'>");
        var $addSortBtn = $("<input class='VIS_Pref_btn-2 vas-add-btn' type='button' value=' " + VIS.Msg.getMsg("VAS_AddSort") + "'>");
        var $windowTabDiv = $("<div class='vas-windowtab'>");
        var $windowTabLabel = $("<label>");
        var $joinFieldColumnLabel = $("<label>");
        var $windowTabSelect = null;
        var $txtWindow = $("<div class='VIS-AMTD-formData VIS-AMTD-InputBtns input-group vas-input-wrap'>");
        var $windowFieldColumnLabel = $("<label>");
        var $windowFieldColumnSelect = $("<div class='vas-windowfieldcol-select vas-windowtab'></div>");
        var $joinsSelect = $("<div style='display: none;' class='vas-joins-select vas-common-style vas-windowtab'>");
        var $selectQuery = $("<div class='vas-query-input' contenteditable='true'>");
        var $selectGeneratorQuery = $("<textarea>");
        var $multiSelect = $("<div class='vas-multiselect'>");
        var $selectBox = $("<div class='vas-selectBox vas-windowtab'><select><option>" + VIS.Msg.getMsg("VAS_SelectAll") + "</option></select><div class='vas-overselect'></div></div>");
        var $checkBoxes = $("<div class='vas-checkboxes'>");
        var $joins = $("<div class='vas-joins'>");
        var $joinsDiv = $("<div class='vas-add-label'>");
        var $joinsLabel = $("<label>");
        var $addJoinsHeading = $("<h4>");
        var $addJoinsDiv = $("<div class='vas-add-label-content'>");
        var $joinsWindowTabSelect = null;
        var $txtJoinWindow = $("<div class='VIS-AMTD-formData VIS-AMTD-InputBtns input-group vas-input-wrap'>");
        var $joinsDropdown = $("<select class='vas-joins-collection'><option>INNER JOIN</option><option>LEFT JOIN</option><option>RIGHT JOIN</option><option>FULL JOIN</option></select>");
        var $joinOnFieldColumnName1Div = $("<div class='vas-windowtab'>");
        var $joinOnFieldColumnName1Label = $("<label>");
        var $joinOnFieldColumnName2Div = $("<div class='vas-windowtab'>");
        var $joinOnFieldColumnName2Label = $("<label>");
        var $joinOnFieldColumnName1 = $("<select><option>" + VIS.Msg.getMsg("VAS_MainTable_Column") + "</option></select>");
        var $joinOnFieldColumnName2 = $("<select><option>" + VIS.Msg.getMsg("VAS_JoinTable_Column") + "</option></select>");
        var $filters = $("<div class='vas-filters'>");
        var $joinsFieldColumnSelect = $("<select>");
        var $filterDiv = $("<div class='vas-add-label'>");
        var $filterLabel = $("<label>");
        var $addFilterText = $("<div>");
        var $addFilterSelectButton = $("<div class='vas-select-plus-btn'>");
        var $addFilterDiv = $("<div class='vas-add-label-content'>");
        var $filterPrice = $("<select class='vas-filter-text-input'>");
        var $filterCondition = $("<select><option>=</option><option>></option><option><</option><option><=</option><option>>=</option><option><></option><option>IS NULL</option><option>IS NOT NULL</option><option>IN</option><option>NOT IN</option></select>");
        var $filterPriceValue = $("<input type='textbox' class='vas-filter-text-input'>");
        var $filterConditionV2 = $("<select><option>AND</option><option>OR</option></select>");
        var $sortElements = $("<select><option>ASC</option><option>DESC</option></select>");
        var $sortByDiv = $("<div class='vas-add-label'>");
        var $sortByLabel = $("<label>");
        var $addSortByText = $("<div>");
        var $addSortByDiv = $("<div class='vas-add-label-content'>");
        var $sortByDropdown = $("<select class='vas-sortby-dropdown'>");
        var $addSortBySelectWithButton = $("<div class='vas-select-plus-btn'>");
        var $sqlBtns = $("<div class='vas-sql-btns'>");
        var $contentArea = $("<div class='vas-content-area'>");
        var $queryMessage = $("<div class='vas-query-message'>");
        var $sqlContent = $("<div class='vas-sql-content'></div>");
        var $queryResultGrid = $("<div style='display:none;' class='vas-queryresult-grid'></div>");
        var $sqlGeneratorContent = $("<div class='vas-sqlgenerator' style='display: none;'><div class='vas-sqlgenerator-column vas-sqlgenerator-column1'></div><div class='vas-sqlgenerator-column vas-sqlgenerator-column2'></div></div>");
        var $sqlGeneratorQueryResultGrid = $("<div style='display:none;' class='vas-queryresult-grid'></div>");     
        $sqlBtns.append($sqlBtn)
            .append($sqlGeneratorBtn)
             .append($sqlResultDiv)
             .append($testSqlBtn)
             .append($testSqlGeneratorBtn);
        var sqlQuery = VIS.Msg.getMsg("VAS_SQLQuery");
        var testSQL = VIS.Msg.getMsg("VAS_TestSql");
        var $root = $("<div class='vas-root'>");

        // Initialize UI Elements
        this.init = function () {
            $windowTabLabel.text(VIS.Msg.getMsg("VAS_WindowTab"));
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
            $selectGeneratorQuery.attr('disabled', true);
            $root.append($sqlBtns).append($contentArea).append(gridDiv).append(gridDiv2);
            $contentArea.append($sqlContent)
                 .append($sqlGeneratorContent)
                 .append($queryMessage);
            $queryResultGrid.append(gridDiv);
            $sqlGeneratorQueryResultGrid.append(gridDiv2);
            $sqlGeneratorContent.find(".vas-sqlgenerator-column1")
                .append($windowTabDiv)
                .append($multiSelect)
                .append($joinsDiv)
                .append($addJoinsDiv)
                .append($filterDiv)
                .append($addFilterDiv)
                .append($sortByDiv)
                .append($addSortByDiv);

            $windowTabDiv.append($windowTabLabel).append($txtWindow);
            $multiSelect.append($windowFieldColumnSelect).append($checkBoxes);
            $windowFieldColumnSelect.append($windowFieldColumnLabel);
            $joinsDiv.append($joinsLabel);

            $joinOnFieldColumnName1Div.append($joinOnFieldColumnName1Label).append($joinOnFieldColumnName1);
            $joinOnFieldColumnName2Div.append($joinOnFieldColumnName2Label).append($joinOnFieldColumnName2);
            $addJoinsDiv.append($addJoinsHeading)
                   .append($txtJoinWindow)
                   .append($joinsSelect)           
                   .append($joinMultiSelect)
                   .append($joinsDropdown)
                    .append($joinOnFieldColumnName1Div)
                    .append($joinOnFieldColumnName2Div)
                   .append($addJoinBtn)
                   .append($joins);
            $joinsSelect.append($joinFieldColumnLabel);
            $filterDiv.append($filterLabel);
            $addFilterDiv.append($addFilterText)
                .append($filterPrice)
                .append($filterCondition)
                .append($filterPriceValue)
                .append($addFilterSelectButton);
            $addFilterSelectButton.append($filterConditionV2).append($addFilterBtn);
            $sortByDiv.append($sortByLabel);
            $addSortByDiv.append($addSortByText).append($sortByDropdown).append($addSortBySelectWithButton);
            $addSortBySelectWithButton.append($sortElements).append($addSortBtn);
            $sqlContent.append($selectQuery).append($queryResultGrid);
            $sqlGeneratorContent.find('.vas-sqlgenerator-column2').append($selectGeneratorQuery).append(gridDiv2);

            /*
               Click event on Test SQL Button 
               to show the result in Grid with 
               validation on input textbox in which
               user will write the SQL query in 
               SQL Tab
            */
            $testSqlBtn.on(VIS.Events.onTouchStartOrClick, function () {
                if ($selectQuery && $selectQuery.text().length>0) {
                    if ($sqlBtn.hasClass('active')) {
                        $(this).toggleClass('vas-show-grid');
                        if (!$(this).hasClass('vas-show-grid')) {
                            $(this).val(testSQL);
                            $queryResultGrid.hide();
                            $query.show();
                            $selectQuery.show();
                            $sqlContent.find($saveBtn).hide();
                        }
                        else {
                            $(this).val(sqlQuery);
                            $sqlContent.append($saveBtn);
                            $sqlContent.find($saveBtn).show();
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
                var hideUpdateGeneratorBtn = sqlGeneratorColumn2.find($updateGenerateBtn);
                var hideSaveGeneratorBtn = sqlGeneratorColumn2.find($saveGeneratorBtn);
                if ($selectGeneratorQuery.val() != '') {
                    if ($sqlGeneratorBtn.hasClass('active')) {
                        $(this).toggleClass('vas-show-grid');
                        if (!$(this).hasClass('vas-show-grid')) {
                            $(this).val(testSQL);
                            $selectGeneratorQuery.show();
                            gridDiv2.hide();
                            $sqlGeneratorQueryResultGrid.hide();
                            hideUpdateGeneratorBtn.hide();
                            hideSaveGeneratorBtn.hide();
                        }
                        else {
                            $(this).val(sqlQuery);
                            $selectGeneratorQuery.hide();
                            gridDiv2.show();
                            $sqlGeneratorQueryResultGrid.show();
                            sqlGeneratorColumn2.append($saveGeneratorBtn).append($updateGenerateBtn);
                            sqlGeneratorColumn2.find($saveGeneratorBtn).show();
                            sqlGeneratorColumn2.find($updateGenerateBtn).show();
                            var query = $selectGeneratorQuery.val();
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
            let filterArray = [];
            let index = 0;
            function readFilterData() {
                var data = '';

                for (var i = 0; i < filterArray.length; i++) {
                     var dataType= filterArray[i].dataType;
                    data += '<div class=vas-filter-item>';
                    data += '<div class=vas-filter-whitebg>';
                    data += '<div class="vas-filters-block">';
                    data += '<div class="vas-filter-andor-value" style=display:none;>' + filterArray[i].filterAndOrValue + ' ' + '</div>';
                    data += '<div class="vas-selecttable">' + filterArray[i].filterPriceText + ' ' + '</div>';
                    data += '<div class="vas-filter-condition">' + filterArray[i].filterCondition + ' ' + '</div>';
                    if (VIS.DisplayType.IsID == dataType || VIS.DisplayType.IsNumeric == dataType || VIS.DisplayType.IsSearch == dataType) {
                        data += '<div class="vas-filter-price-value">' + filterArray[i].filterPriceValue + '</div>';
                    }
                    else if (VIS.DisplayType.YesNo == dataType || VIS.DisplayType.String == dataType || VIS.DisplayType.IsDate == dataType || VIS.DisplayType.IsText == dataType) {
                        data += '<div class="vas-filter-price-value">' + "'" + filterArray[i].filterPriceValue + "'" + '</div>';
                    }
                    else {
                        data += '<div class="vas-filter-price-value">' + filterArray[i].filterPriceValue + '</div>';
                    }
                    data += '</div>';
                    data += '<div class="vas-filters-editdelete-btns">';
                    data += '<div style=display:none;><span class="glyphicon glyphicon-edit"></span></div>';
                    data += '<div><span class="glyphicon glyphicon-trash"></span></div>';
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
                //var dataType =[];
                var filterPriceval = $filterPrice.find('option:selected').val();
                let filterPriceText = filterPriceval.slice(filterPriceval.indexOf(':') + 1);
                let filterCondition = $filterCondition.find('option:selected').val();
                let filterPriceValue = $filterPriceValue.val();
                 var dataType = filterPriceval.slice(0, filterPriceval.indexOf(':'));
                let filterAndOrValue = $filterConditionV2.find('option:selected').val();
                const filterObj = { filterPriceText, filterCondition, filterPriceValue, filterAndOrValue, dataType: dataType }
                filterArray.push(filterObj);
                index += 1;
                $filters.empty();
                readFilterData();
                if ($filters && $filters.length > 0) {
                    $filters.find('.vas-filter-item').addClass('vas-filters-row');
                }

                if ($('.vas-filters .vas-filter-item').length > 1) {
                    $('.vas-filters .vas-filter-item:first').addClass('vas-first-delete-icon');
                }
            }

            /*
               Click event on Edit and Delete Buttons 
               for Filters in Filter Accordion
            */
            $filters.on(VIS.Events.onTouchStartOrClick, function (event) {
                if ($(event.target).hasClass('glyphicon-trash')) {
                    filterArray.splice($(event.target), 1);
                    deleteFilter();
                    $(event.target).parents('.vas-filter-item').remove();
                    if ($('.vas-filters .vas-filter-item').length < 2) {
                        $('.vas-filters .vas-filter-item').removeClass('vas-first-delete-icon');
                    }
                }
                if ($(event.target).hasClass('glyphicon-edit')) {
                    let filterSelectTableVal = $(event.target).parents('.vas-filter-item').find(".vas-selecttable").text();
                    let filterPriceValue = $(event.target).parents('.vas-filter-item').find(".vas-filter-price-value").text();
                    let filterCondition = $(event.target).parents('.vas-filter-item').find(".vas-filter-condition").text();
                    $(event.target).parents('.vas-filter-item').siblings().find(".vas-filter-condition").removeClass('active');
                    $(event.target).parents('.vas-filter-item').siblings().find(".vas-filter-price-value").removeClass('active');
                    $(event.target).parents('.vas-filter-item').find(".vas-filter-condition").addClass('active');
                    $(event.target).parents('.vas-filter-item').find(".vas-filter-price-value").addClass('active');
                    $filterPrice.val(filterSelectTableVal);
                    $filterCondition.val(filterCondition);
                    $filterPriceValue.val(filterPriceValue);
                    $addFilterBtn.addClass('vas-edit-btn');
                }
            });

            /*
               Function to update the filter
               in Filter Accordion
            */
            function updateFilter() {
                $addFilterBtn.val('Add Filter');
                var updatedFilterPriceValue = $filterPriceValue.val();
                $('.vas-filter-price-value.active').text(updatedFilterPriceValue);
                var updatedFilterConditionValue = $filterCondition.find('option:selected').val();
                $('.vas-filter-condition.active').text(updatedFilterConditionValue);
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
                $(this).next().toggle();
                if ($(this).next().is(':empty')) {
                    $(this).next().css("display", "none");
                }
            });

            /*
               Click event on Window/Field Column in Joins 
               to display the data in form of multiple checkboxes 
            */
            $joinsSelect.on(VIS.Events.onTouchStartOrClick, function () {
                $(this).next().toggle();
            });

            /*
               Click event on Save Button 
               to save the query
            */
            $saveBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var query = $selectQuery.text();
                alertID = VIS.context.getContext(windowNo, 'AD_Alert_ID');
                if (alertID > 0) {
                    UpdateAlertRule(query);
                }

            });

            /*
              Click event on Save Generator Button 
              to save the query
           */
            $saveGeneratorBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var query = $selectGeneratorQuery.val();
                SaveAlertRule(query);
            });

            /*
               Click event on Update Button in SQL Generator
               to update the query
            */
            $updateGenerateBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var query = $selectGeneratorQuery.val();
                UpdateAlertRule(query);
            });

            /*
               Click event on Add Filter Button
               to add/update filters
            */
            $addFilterBtn.on(VIS.Events.onTouchStartOrClick, function (event) {
                let filterCondition = $filterCondition.find('option:selected').val();
                var filterPriceValue = $filterPriceValue.val();
                if ($filterPrice.val() != '' && filterPriceValue != '') {
                    if (andFlag) {
                        WhereCondition = "WHERE";
                    }
                    else {
                        WhereCondition = $filterConditionV2.val();
                    }
                    ApplyFilter(WhereCondition);
                    if ($(this).hasClass('vas-edit-btn')) {
                        updateFilter();
                        $(this).removeClass('vas-edit-btn');
                        ClearText();
                    }
                    else {
                        if (filterCondition) {
                            $addFilterDiv.append($filters);
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
            let joinsArray = [];
            let joinIndex = 0;
            function readJoinsData() {
                var data = '';
                for (var i = 0; i < joinsArray.length; i++) {
                    data += '<div class=vas-join-item>';
                    data += '<div class="vas-joins-bg">';
                    data += '<div class="vas-joins-block">';
                    data += '<div class="vas-selecttable">' + joinsArray[i].joinsDropDown + '</div>';
                    data += '</div>';
                    data += '<div class="vas-delete-join-btn">';
                    data += '<div><span class="glyphicon glyphicon-trash"></span></div>';
                    data += '</div>';
                    data += '</div>';
                    data += '</div>';
                }
                $joins.append(data);
            }

            // Click event on Edit and Delete Buttons for Joins
            $joins.on(VIS.Events.onTouchStartOrClick, function (event) {
                if ($(event.target).hasClass('glyphicon-trash')) {
                    var joinsDropDown = $(event.target).parents('.vas-join-item').find('.vas-selecttable').text();
                    joinsArray.splice($(event.target), 1);
                    $(event.target).parents('.vas-join-item').remove();
                    if ($selectGeneratorQuery.val().indexOf(joinsDropDown) > -1) {
                        var desiredResult = $selectGeneratorQuery.val().slice(0, $selectGeneratorQuery.val().lastIndexOf(joinsDropDown));
                        $selectGeneratorQuery.val(desiredResult);
                    }
                }
            });

            /*
               Click event on Add Join Button
               to add joins
            */
            $addJoinBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var joinQuery;
                var keyColumn1 = $joinOnFieldColumnName1.val();
                var keyColumn2 = $joinOnFieldColumnName2.val();
                var sql = $selectGeneratorQuery.val();
                var joinsDropDown = $joinsDropdown.find('option:selected').val();
                const joinsObj = { joinsDropDown }
                if (sql != '' && keyColumn1 != '' && keyColumn2 != '' && keyColumn1 != null && keyColumn2 != null) {
                    var fromIndex = sql.indexOf('FROM');
                    if (fromIndex != -1) {
                        var fromClause = sql.slice(fromIndex);
                        if (seletedJoinCloumn.length > 0) {
                            sql = sql.slice(0, fromIndex) + ", " + seletedJoinCloumn + " " + fromClause;
                        } else {
                            sql = sql.slice(0, fromIndex) + fromClause;
                        }
                        joinQuery = " " + $joinsDropdown.val() + " " + joinTable + ' ON (' + keyColumn1 + " = " + keyColumn2 + ")";
                    }
                    var whereIndex = sql.indexOf('WHERE');
                    if (whereIndex != -1) {
                        var whereClause = sql.slice(whereIndex);
                        sql = sql.slice(0, whereIndex) + joinQuery + " " + whereClause;
                    } else {
                        sql += joinQuery;
                    }
                    $selectGeneratorQuery.val('');
                    $selectGeneratorQuery.val(sql);
                    $joinMultiSelect.val('');
                    $joinOnFieldColumnName2.val('');
                    $joinOnFieldColumnName1.val('');
                    filterFlag = true;
                }
            });

            /*
               Click event on Add Sort Button
               to add sorts like ASC, DESC etc
            */
            $addSortBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var sortQuery;
                var sortColumn = $sortByDropdown.val();
                var sql = $selectGeneratorQuery.val();
                if (sql != '' && sortColumn != '') {
                    if (orderbyFlag) {
                        sortQuery = " ORDER BY " + sortColumn + " " + $sortElements.val();
                    }
                    else {
                        sortQuery = ", " + sortColumn + " " + $sortElements.val();
                    }
                }
                sql += sortQuery;
                $selectGeneratorQuery.val('');
                $selectGeneratorQuery.val(sql);
                orderbyFlag = false;
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
            getJoinWindow();
            eventHandling();
        }

        /*
            AJAX Call to get Alert Data
        */
        this.SqlQuery = function (ParentId) {
            alertRuleID = ParentId;
            alertID = $self.selectedRow.ad_alert_id;
            tableName = $self.selectedRow.name;
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
            var currentValue = $selectGeneratorQuery.val();
            var conditionToRemove = $(event.target).parents('.vas-filter-item').find(".vas-filters-block").text();
            var orIndexCR = conditionToRemove.indexOf("OR");
            var orIndexSql = currentValue.indexOf("OR");
            var andIndexCR = conditionToRemove.indexOf('AND');
            var andIndexSQl = currentValue.indexOf('AND');
            var whereIndex = currentValue.indexOf('WHERE');
            if (andIndexSQl == -1 && andIndexCR != -1) {
                conditionToRemove = conditionToRemove.replace("AND", "").trim();
            }
            if (whereIndex != -1 && andIndexSQl == -1) {
                currentValue = currentValue.replace("WHERE", "").trim();
                andFlag = true;
            }            
            var updatedQuery = currentValue.replace(conditionToRemove, "").trim();
            $selectGeneratorQuery.val('');
            $selectGeneratorQuery.val(updatedQuery);
        }

        /*
            Function of Event Handling
        */
        function eventHandling() {
            $windowTabSelect.fireValueChanged = OnChange;
            $joinsWindowTabSelect.fireValueChanged = joinsTableOnChange;
        }

        /*
            Onchange function on Joins Field Column
        */
        $selectBox.on("change", joinsFieldColumnSelect);

        /*
            Onchange function to get the table data
        */
        function OnChange() {
            TableID = 0;
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
                            TableID = result[0].TableID;
                            tableName = result[0].TableName;
                        }
                    },
                    error: function (error) {
                        console.log(error);
                    }
                });

            }
            $selectGeneratorQuery.val('');
            if (TableID > 0) {
                getColumns(TableID);
            }
            else {
                getColumns(0);
            }
            $selectGeneratorQuery.show();
            gridDiv2.hide();
            $sqlGeneratorQueryResultGrid.hide();
            andFlag = true;
            orderbyFlag = true;
            $sqlResultDiv.hide();
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
                getJoinsColumns(TableID2);
            }
            else {
                getJoinsColumns(0);
            }
        }

        /*
            Function to apply filters
        */
        function ApplyFilter(WhereCondition) {
            var sql = $selectGeneratorQuery.val();
            var orderIndex = sql.indexOf('ORDER BY');
            var whereSql = "";
            var filterval = $filterPrice.val();
            if (filterval != null) {
                var filterColumn = filterval.slice(filterval.indexOf(':') + 1);
                var dataType = filterval.slice(0, filterval.indexOf(':'));
                if (sql != '' && filterColumn != '') {
                    if (orderIndex == -1) {
                        sql += " " + WhereCondition;
                        if (VIS.DisplayType.IsID == dataType || VIS.DisplayType.IsNumeric == dataType || VIS.DisplayType.IsSearch == dataType) {
                            sql += " " + filterColumn + " " + $filterCondition.val() + " " + $filterPriceValue.val();
                        }
                        else if (VIS.DisplayType.YesNo == dataType || VIS.DisplayType.String == dataType || VIS.DisplayType.IsDate == dataType || VIS.DisplayType.IsText == dataType) {
                            sql += " " + filterColumn + " " + $filterCondition.val() + " '" + $filterPriceValue.val() + "'";
                        }
                        else {
                            sql += " " + filterColumn + " " + $filterCondition.val() + " " + $filterPriceValue.val();
                        }
                    }
                    else {
                        whereSql += " " + WhereCondition;
                        if (VIS.DisplayType.IsID == dataType || VIS.DisplayType.IsNumeric == dataType || VIS.DisplayType.IsSearch == dataType) {
                            whereSql += " " + filterColumn + " " + $filterCondition.val() + " " + $filterPriceValue.val();
                        }
                        else if (VIS.DisplayType.YesNo == dataType || VIS.DisplayType.String == dataType || VIS.DisplayType.IsDate == dataType || VIS.DisplayType.IsText == dataType) {
                            whereSql += " " + filterColumn + " " + $filterCondition.val() + " '" + $filterPriceValue.val() + "'";
                        }
                        else {
                            whereSql += " " + filterColumn + " " + $filterCondition.val() + " " + $filterPriceValue.val();
                        }
                        sql = sql.substring(0, orderIndex) + " " + whereSql + " " + sql.substring(orderIndex);
                    }
                    $selectGeneratorQuery.val('');
                    $selectGeneratorQuery.val(sql);
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
            $filterCondition.val('=');
        }

        /*
            Function to get the columns from Table in Window/Tab
        */
        function getColumns(TableID) {
            $selectBox.find('select').empty();
            var flag = true;
            var seletedCloumn = [];
            $checkBoxes.empty();
            $sortByDropdown.empty();
            $filterPrice.empty();
            $joinOnFieldColumnName1.empty();
            $.ajax({
                url: VIS.Application.contextUrl + "AlertSQLGenerate/GetColumns",
                data: { tableID: TableID },
                type: "POST",
                async: false,
                success: function (result) {
                    result = JSON.parse(result);
                    if (result && result.length > 0) {
                        tableName = result[0].TableName;
                        $selectBox.find('select').prepend("<option>Select</option>");
                        $joinOnFieldColumnName1.prepend("<option selected='select'></option>");
                        $sortByDropdown.prepend("<option selected='select'>Select</option>");
                        $filterPrice.prepend("<option selected='select'>Select</option>");
                        for (var i = 1; i < result.length; i++) {
                            $joinOnFieldColumnName1.append(" <option value=" + tableName + "." + result[i].DBColumn + ">" + result[i].DBColumn + "</option>");
                            $sortByDropdown.append(" <option value=" + tableName + "." + result[i].DBColumn + ">" + tableName + " > " + result[i].ColumnName + "</option>");
                            $filterPrice.append(" <option value=" + result[i].DataType + ":" + tableName + "." + result[i].DBColumn + ">" + tableName + " > " + result[i].ColumnName + "</option>");
                            $checkBoxes.append(" <div class='vas-column-list-item'>" + "<input type='checkbox' class='vas-column-checkbox'>" + result[i].FieldName + " - " + result[i].DBColumn + "</div>");
                        }
                    }

                    seletedCloumn = [];
                    $(".vas-column-list-item .vas-column-checkbox").on('click', function (eve) {
                        if (!flag) {
                            $(".vas-column-checkbox").prop("checked", false);
                            flag = true;
                        }
                        var selectedItem = $(this).parent('.vas-column-list-item').text();
                        var desiredResult = tableName + "." + selectedItem.substring(selectedItem.indexOf('-') + 1).trim() + " AS " + '"' + selectedItem.slice(0, selectedItem.indexOf('-') - 1) + '" ';
                        if (this.checked) {
                            seletedCloumn.push(desiredResult);
                        }
                        else {
                            seletedCloumn = seletedCloumn.filter(function (elem) {
                                return elem != desiredResult;
                            });
                        }
                        GetSQL(seletedCloumn);
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
        function getJoinsColumns(TableID2) {
            $selectBox.find('select').empty();
            $joinOnFieldColumnName2.empty();
            $joinMultiSelect.empty();
            $joinOnFieldColumnName2.prepend("<option selected='select'></option>");
            $joinsSelect.show();
            $joinsSelect.css("margin-bottom", "10px");
            $.ajax({
                url: VIS.Application.contextUrl + "AlertSQLGenerate/GetColumns",
                data: { tableID: TableID2 },
                type: "POST",
                async: false,
                success: function (result) {
                    result = JSON.parse(result);
                    if (result && result.length > 0) {
                        joinTable = result[0].TableName;
                        for (var i = 1; i < result.length; i++) {
                            $joinOnFieldColumnName2.append(" <option value=" + joinTable + "." + result[i].DBColumn + ">" + result[i].DBColumn + "</option>");
                            $joinMultiSelect.append(" <div class='vas-column-list-item'>" + "<input type='checkbox' class='vas-column-checkbox'>" + result[i].FieldName + " - " + result[i].DBColumn + "</div>");
                            $sortByDropdown.append(" <option value=" + joinTable + "." + result[i].DBColumn + ">" + joinTable + " > " + result[i].ColumnName + "</option>");
                            $filterPrice.append(" <option value=" + result[i].DataType + ":" + joinTable + "." + result[i].DBColumn + ">" + joinTable + " > " + result[i].ColumnName + "</option>");
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
        function getJoinWindow() {
            var lookups = new VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.Search, "AD_Tab_ID", 0, false, "");
            $joinsWindowTabSelect = new VIS.Controls.VTextBoxButton("AD_Tab_ID", true, false, true, VIS.DisplayType.Search, lookups);
            var locDep = $joinsWindowTabSelect.getControl().attr('placeholder', ' Search here').attr("id", "Ad_Tab_ID");
            var DivSearchCtrlWrap = $('<div class="vas-control-wrap">');
            var DivSearchBtnWrap = $('<div class="input-group-append">');
            $txtJoinWindow.css("width", "100%");
            $txtJoinWindow.append(DivSearchCtrlWrap).append(DivSearchBtnWrap);
            DivSearchCtrlWrap.append(locDep);
            DivSearchBtnWrap.append($joinsWindowTabSelect.getBtn(0));
            DivSearchBtnWrap.append($joinsWindowTabSelect.getBtn(1));
            $joinsWindowTabSelect.setCustomInfo('SQLGeneratorAlertTab');
        }

        /*
            Search functionality for Window/Tab
        */
        function getWindow() {
            var lookups = new VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.Search, "AD_Tab_ID", 0, false, "");
            $windowTabSelect = new VIS.Controls.VTextBoxButton("AD_Tab_ID", true, false, true, VIS.DisplayType.Search, lookups);
            // $windowTabSelect.append($windowTabLabel.getControl());
            var locDep = $windowTabSelect.getControl().attr('placeholder', ' Search here').attr("id", "Ad_Table_ID");
            var DivSearchCtrlWrap = $('<div class="vas-control-wrap">');
            var DivSearchBtnWrap = $('<div class="input-group-append">');
            $txtWindow.css("width", "100%");
            $txtWindow.append(DivSearchCtrlWrap).append(DivSearchBtnWrap);
            DivSearchCtrlWrap.append(locDep);
            DivSearchBtnWrap.append($windowTabSelect.getBtn(0));
            DivSearchBtnWrap.append($windowTabSelect.getBtn(1));
            $windowTabSelect.setCustomInfo('SQLGeneratorAlertTab');
        }

        /*
            Functionality to Save the Query in SQL Generator Tab
        */
        function SaveAlertRule(query) {
            if (query != null) {
                alertRuleID = 0;
                alertID = VIS.context.getContext(windowNo, 'AD_Alert_ID');
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/SaveQuery",
                    type: "POST",
                    data: { query: query, alertRuleID: alertRuleID, alertID: alertID, tableName: tableName, tableID: TableID },
                    async: false,
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result && result.length > 0) {
                            $sqlResultDiv.text(result);
                            $sqlResultDiv.removeClass('vas-sql-result-error');
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
            Functionality to Update the Query in SQL Generator Tab
        */
        function UpdateAlertRule(query) {
            if (query != null) {
                alertRuleID = $self.ParentId;
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/UpdateQuery",
                    type: "POST",
                    data: { query: query, alertRuleID: alertRuleID, alertID: alertID, tableID: TableID },
                    async: false,
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result && result.length > 0) {
                            $sqlResultDiv.text(result);
                            $sqlResultDiv.removeClass('vas-sql-result-error');
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
            Function to display SQL query for Joins in SQL Generator Tab
        */
        function joinsFieldColumnSelect() {
            var columnID = $joinsFieldColumnSelect.val();
            var selectBoxVal = $selectBox.find('select').find('option:selected').val();
            if (selectBoxVal != "*") {
                $selectGeneratorQuery.val("SELECT name FROM " + tableName);
            }
            else {
                $selectGeneratorQuery.val("SELECT * FROM " + tableName);
            }
        }

        /*
            Function to display SQL based on Column Values in SQL Generator Tab
        */
        function GetSQL(columnValue) {
            $selectGeneratorQuery.val('');
            if (columnValue.length > 0) {
                $selectGeneratorQuery.val("SELECT " + columnValue + " FROM " + tableName);
            }
        }

        /*
            AJAX Call to get the result in Grid 
            in SQL and SQL Generator Tab
        */
        function getResult(query) {
            if (query != null) {
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/GetResult",
                    data: { Query: query, pageNo: pageNo, pageSize: pageSize },
                    type: "POST",
                    async: false,
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result != null && result != []) {
                            if (result.length > 0) {                               
                                loadGrid(result);
                                $sqlResultDiv.hide();
                            }
                            else {
                                $sqlResultDiv.show();
                                $selectGeneratorQuery.show();
                                gridDiv2.hide();
                                $sqlGeneratorQueryResultGrid.hide();
                                $testSqlGeneratorBtn.val(testSQL);
                                $sqlResultDiv.text(VIS.Msg.translate(VIS.Env.getCtx(), "NoRecordFound"));
                                $sqlResultDiv.text(VIS.Msg.getMsg("VIS_NoRecordFound"));
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
                            else {
                                $selectGeneratorQuery.show();
                                gridDiv2.hide();
                                $sqlGeneratorQueryResultGrid.hide();
                                $testSqlGeneratorBtn.val(testSQL);
                            }
                            $testSqlBtn.removeClass('vas-show-grid');
                            $sqlResultDiv.show();
                            $sqlResultDiv.text(VIS.Msg.getMsg("VAS_ValidQuery"));
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
        function loadGrid(result) {
            if (loadTotalPages) {
                loadTotalPages = false;
                result = result.slice(0, 20);
            }
            var grdCols = [];
            for (var i = 0; i < Object.keys(result[0]).length; i++) {
                if (Object.keys(result[0])[i] != 'recid') {
                    grdCols.push({ field: Object.keys(result[0])[i], caption: Object.keys(result[0])[i], size: '130px' });
                }
            }
            if (sqlGenerateFlag) {
                dGrid2 = $(gridDiv2).w2grid({
                    name: "gridForm2" + $self.windowNo,
                    recordHeight: 35,
                    multiSelect: true,
                    columns: grdCols,
                    records: w2utils.encodeTags(result)
                });

            } else {
                dGrid = $(gridDiv).w2grid({
                    name: "gridForm" + $self.windowNo,
                    recordHeight: 35,
                    multiSelect: true,
                    columns: grdCols,
                    records: w2utils.encodeTags(result)
                });
                $selectQuery.hide();
                $query.hide();
                $queryResultGrid.show();
            }
        }

        this.getRoot = function () {
            return $root;
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
    }

})(VAS, jQuery);