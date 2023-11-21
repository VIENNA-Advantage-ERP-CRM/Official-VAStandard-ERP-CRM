; VIS = window.VIS || {};
; (function (VIS, $) {
    VIS.SQL = function () {
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        this.ParentId = 0;
        var $self = this;
        var pageSize = 20;
        var pageNo = 0;
        var $statusBar;
        var TableID = 0;
        var alertID = 0;
        var alertRuleID = 0;
        var tableName = null;
        var andFlag = true;
        var orderbyFlag = true;
        var sortFlag = true;
        var andOrFlag = false;
        var filterFlag = true;
        var sqlGenerateFlag = false;
        var sqlFlag = true;
        var WhereCondition;
        var totalPages;
        var loadTotalPages = true;
        var TABLE2;
        var windowResult;
        var seletedJoinCloumn = [];
        var $query = $("<div>");
        var gridDiv = $("<div style='width: 100%; height: 90%;  margin-right: 15px; margin-top: 1px; border: 1px solid darkgray;'>");
        var gridDiv2 = $("<div style='width: 100%; height: calc(100% - 21%);  margin-right: 15px; margin-top: 1px; border: 1px solid darkgray; display: none;'>");
        var $cmbPaging;
        var $joinMultiSelect = $("<div class='vis-join-multiselect'>");
        var dGrid = null;
        var $sqlBtn = $("<input class='VIS_Pref_btn-2 active' type='button' value='SQL'>");
        var $sqlResultDiv = $("<div class='vas-sql-result'>");
        var $sqlGeneratorBtn = $("<input class='VIS_Pref_btn-2 vas-sql-generator' type='button' value='SQL Generator'>");
        var $testSqlGeneratorBtn = $("<input style='display: none;' class='VIS_Pref_btn-2 vas-test-sql vas-test-sqlgenerator' type='button' value='Test SQL'>");
        var $testSqlBtn = $("<input class='VIS_Pref_btn-2 vas-test-sql' type='button' value='Test SQL'>");
        var $saveBtn = $("<input class='VIS_Pref_btn-2 vas-save-btn' type='button' value='Save'>");
        var $updateGenerateBtn = $("<input class='VIS_Pref_btn-2 vas-update-btn' type='button' value='Update'>");
        var $saveGeneratorBtn = $("<input class='VIS_Pref_btn-2 vas-save-btn' type='button' value='Save'>");
        var $addJoinBtn = $("<input class='VIS_Pref_btn-2' type='button' value='Add Join'>");
        var $addFilterBtn = $("<input class='VIS_Pref_btn-2 vis-add-btn' type='button' value='Add Filter'>");
        var $addSortBtn = $("<input class='VIS_Pref_btn-2 vis-add-btn' type='button' value='Add Sort'>");
        var $windowTabDiv = $("<div class='vas-windowtab'>");
        var $windowTabLabel = $("<label>");
        var $joinFieldColumnLabel = $("<label>");
        var $windowTabSelect = null;
        var $txtWindow = $("<div class='VIS-AMTD-formData VIS-AMTD-InputBtns input-group vis-input-wrap'>");
        var $windowFieldColumnLabel = $("<label>");
        var $windowFieldColumnSelect = $("<div class='vas-windowfieldcol-select vas-windowtab'></div>");
        var $joinFieldMultiSelect = $("<div class='vas-joinfieldcol-select'>");
        var $joinsSelect = $("<div style='display: none;' class='vas-joins-select vas-common-style vas-windowtab'>");
        var $selectQuery = $("<div class='vas-query-input' contenteditable='true'>");
        var highLightedWord = ["select", "where", "from"];

        var $selectGeneratorQuery = $("<textarea>");
        var $multiSelect = $("<div class='vas-multiselect'>");
        var $selectBox = $("<div class='vas-selectBox vas-windowtab'><select><option>Select All</option></select><div class='vas-overselect'></div></div>");
        var $checkBoxes = $("<div class='vas-checkboxes'>");
        var $joins = $("<div class='vas-joins'>");
        var $joinsDiv = $("<div class='vas-add-label'>");
        var $joinsLabel = $("<label>");
        var $addJoinsHeading = $("<h4>");
        var $addJoinsDiv = $("<div class='vas-add-label-content'>");
        var $joinsWindowTabSelect = null;
        var $txtJoinWindow = $("<div class='VIS-AMTD-formData VIS-AMTD-InputBtns input-group vis-input-wrap'>");
        var $joinsDropdown = $("<select class='vas-joins-collection'><option>Inner Join</option><option>Left Join</option><option>Right Join</option><option>Full Join</option></select>");
        var $joinOnFieldColumnName1 = $("<select><option>Join on Field/Column Name1</option></select>");
        var $joinOnFieldColumnName2 = $("<select><option>Join on Field/Column Name2</option></select>");
        var $filters = $("<div class='vas-filters'>");
        var $joinsFieldColumnSelect = $("<select>");
        var $listOfContent = $("<div class='vas-content-list'>")
        var $filterDiv = $("<div class='vas-add-label'>");
        var $filterLabel = $("<label>");
        var $addFilterText = $("<div>");
        var $addFilterSelectButton = $("<div class='vas-select-plus-btn'>");
        var $addFilterDiv = $("<div class='vas-add-label-content'>");
        var $filterPrice = $("<select class='vas-filter-text-input'>");
        var $filterCondition = $("<select><option>Select an option</option><option>=</option><option>></option><option><</option><option><=</option><option>>=</option><option><></option><option>IS NULL</option><option>IS NOT NULL</option><option>IN</option><option>NOT IN</option></select>");
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
        var $queryGeneratorResultGrid = $("<div style='display:none;' class='vas-queryresult-grid'></div>");
        var $sqlGeneratorContent = $("<div class='vas-sqlgenerator' style='display: none;'><div class='vas-sqlgenerator-column vas-sqlgenerator-column1'></div><div class='vas-sqlgenerator-column vas-sqlgenerator-column2'></div></div>");
        var $sqlGeneratorQueryResultGrid = $("<div style='display:none;' class='vas-queryresult-grid'></div>");
        var $statusBar = $('<div class="VA093-compPagging">');
        var html = $('<div class="vis-ad-w-p-s-pages">'
            + '<ul class="vis-ad-w-p-s-plst"><li></li>'
            + '<li class="VA093_cmpPageUp" style="opacity: 0.6;"><div><i class="vis vis-pageup" title="Page Down" })="" style="opacity: 0.6;"></i></div></li>'
            + '<li class="VA093-status-page-cmb"></li>'
            + '<li class="VA093_cmpPageDown" style="opacity: 0.6;"><div><i class="vis vis-pagedown" title="Page Up" })="" style="opacity: 0.6;"></i></div></li>'
            + '</ul></div>');
        $sqlBtns.append($sqlBtn);
        $sqlBtns.append($sqlGeneratorBtn);
        $sqlBtns.append($sqlResultDiv);
        $sqlBtns.append($testSqlBtn);
        $sqlBtns.append($testSqlGeneratorBtn);
        var sqlQuery = VIS.Msg.translate(VIS.Env.getCtx(), "SQLQuery");
        var testSQL = VIS.Msg.translate(VIS.Env.getCtx(), "TestSQL");
        var $root = $('<div style="height:100%;width:100%;"></div>');
        /*
            * Intialize UI Elements
        */
        this.init = function () {
            $windowTabLabel.text(VIS.Msg.translate(VIS.Env.getCtx(), "windowTab"));
            $windowFieldColumnLabel.text(VIS.Msg.translate(VIS.Env.getCtx(), "fieldColumn"));
            $joinFieldColumnLabel.text(VIS.Msg.translate(VIS.Env.getCtx(), "fieldColumn"));
            $joinsLabel.text(VIS.Msg.translate(VIS.Env.getCtx(), "joins"));
            $addJoinsHeading.text(VIS.Msg.translate(VIS.Env.getCtx(), "Add Joins"));
            $filterLabel.text(VIS.Msg.translate(VIS.Env.getCtx(), "Filter"));
            $sortByLabel.text(VIS.Msg.translate(VIS.Env.getCtx(), "Sort"));
            $addFilterText.text(VIS.Msg.translate(VIS.Env.getCtx(), "addFilter"));
            $addSortByText.text(VIS.Msg.translate(VIS.Env.getCtx(), "Add Sort"));
            $selectGeneratorQuery.attr('disabled', true);
            $root.append($sqlBtns).append($contentArea).append(gridDiv).append(gridDiv2);
            $contentArea.append($sqlContent);
            $contentArea.append($sqlGeneratorContent);
            $contentArea.append($queryMessage);
            $statusBar.append(html);
            $queryResultGrid.append(gridDiv).append($statusBar);
            $sqlGeneratorQueryResultGrid.append(gridDiv2).append($statusBar);
            $sqlGeneratorContent.find(".vas-sqlgenerator-column1").append($windowTabDiv).append($multiSelect)
                .append($joinsDiv).append($addJoinsDiv).append($filterDiv).append($addFilterDiv).append($sortByDiv).append($addSortByDiv);
            $windowTabDiv.append($windowTabLabel);
            $windowTabDiv.append($txtWindow);
            $multiSelect.append($windowFieldColumnSelect);
            $windowFieldColumnSelect.append($windowFieldColumnLabel);
            $multiSelect.append($checkBoxes);
            $joinsDiv.append($joinsLabel);
            $addJoinsDiv.append($addJoinsHeading);
            $addJoinsDiv.append($txtJoinWindow);
            $addJoinsDiv.append($joinsSelect);
            $joinsSelect.append($joinFieldColumnLabel);
            $addJoinsDiv.append($joinMultiSelect);
            $addJoinsDiv.append($joinsDropdown);
            $addJoinsDiv.append($joinOnFieldColumnName1);
            $addJoinsDiv.append($joinOnFieldColumnName2);
            $addJoinsDiv.append($addJoinBtn);
            $addJoinsDiv.append($joins);
            $filterDiv.append($filterLabel);
            $addFilterDiv.append($addFilterText);
            $addFilterDiv.append($filterPrice);
            $addFilterDiv.append($filterCondition);
            $addFilterDiv.append($filterPriceValue);
            $addFilterDiv.append($addFilterSelectButton);
            $addFilterSelectButton.append($filterConditionV2);
            $addFilterSelectButton.append($addFilterBtn);
            $sortByDiv.append($sortByLabel);
            $addSortByDiv.append($addSortByText);
            $addSortByDiv.append($sortByDropdown);
            $addSortByDiv.append($addSortBySelectWithButton);
            $addSortBySelectWithButton.append($sortElements);
            $addSortBySelectWithButton.append($addSortBtn);
            $sqlContent.append($selectQuery);
            $sqlContent.append($queryResultGrid);
            $sqlGeneratorContent.find('.vas-sqlgenerator-column2').append($selectGeneratorQuery).append(gridDiv2);       
            $testSqlBtn.on(VIS.Events.onTouchStartOrClick, function () {
                if ($selectQuery.text() != '') {
                    if ($sqlBtn.hasClass('active')) {
                        $(this).toggleClass('vis-show-grid');
                        if (!$(this).hasClass('vis-show-grid')) {
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
                            getResult();
                        }
                    }
                }
                else {
                    $sqlResultDiv.text(VIS.Msg.translate(VIS.Env.getCtx(), "WriteQuery"));
                }
            });
            $testSqlGeneratorBtn.on(VIS.Events.onTouchStartOrClick, function () {
                if ($selectGeneratorQuery.val() != '') {
                    if ($sqlGeneratorBtn.hasClass('active')) {
                        $(this).toggleClass('vis-show-grid');
                        if (!$(this).hasClass('vis-show-grid')) {
                            $(this).val(testSQL);
                            $selectGeneratorQuery.show();
                            gridDiv2.hide();
                            $sqlGeneratorQueryResultGrid.hide();
                            $sqlGeneratorContent.find('.vas-sqlgenerator-column2').find($updateGenerateBtn).hide();
                            $sqlGeneratorContent.find('.vas-sqlgenerator-column2').find($saveGeneratorBtn).hide();
                        }
                        else {
                            $(this).val(sqlQuery);
                            $selectGeneratorQuery.hide();
                            gridDiv2.show();
                            $sqlGeneratorQueryResultGrid.show();
                            $sqlGeneratorContent.find('.vas-sqlgenerator-column2').append($saveGeneratorBtn).append($updateGenerateBtn);;
                            $sqlGeneratorContent.find('.vas-sqlgenerator-column2').find($saveGeneratorBtn).show();
                            $sqlGeneratorContent.find('.vas-sqlgenerator-column2').find($updateGenerateBtn).show();
                            getResult();
                        }
                    }
                }
                else {
                    $sqlResultDiv.text(VIS.Msg.translate(VIS.Env.getCtx(), "DisplayQuery"));
                }

            });

            let filterArray = [];
            let index = 0;
            function readFilterData(dataType) {
                var data = '';               
                for (var i = 0; i < filterArray.length; i++) {
                    data += '<div class=vis-filter-item>';
                    data += '<div class=vas-filter-whitebg>';
                    data += '<div class="vis-filters-block">';
                    if (andOrFlag) {
                        data += '<div class="vis-filter-andor-value" style=display:none;>' + filterArray[i].filterAndOrValue +' '+ '</div>';
                    }
                    data += '<div class="vas-selecttable">' + filterArray[i].filterPriceText + ' ' +'</div>';
                    data += '<div class="vis-filter-condition">' + filterArray[i].filterCondition + ' ' + '</div>';
                    if (VIS.DisplayType.IsID == dataType || VIS.DisplayType.IsNumeric == dataType) {
                        data += '<div class="vis-filter-price-value">' + filterArray[i].filterPriceValue + '</div>';
                    }
                    else if (VIS.DisplayType.YesNo == dataType || VIS.DisplayType.String == dataType || VIS.DisplayType.IsDate == dataType || VIS.DisplayType.IsText == dataType) {
                        data += '<div class="vis-filter-price-value">' +"'"+ filterArray[i].filterPriceValue + "'"+'</div>';
                    }
                    else {
                        data += '<div class="vis-filter-price-value">' + filterArray[i].filterPriceValue + '</div>';
                    }
                    data += '</div>';
                    data += '<div class="vis-filters-editdelete-btns">';
                    data += '<div style=display:none;><span class="glyphicon glyphicon-edit"></span></div>';
                    data += '<div><span class="glyphicon glyphicon-trash"></span></div>';
                    data += '</div>';
                    data += '</div>';
                    data += '</div>';
                }
                $filters.append(data);
            }

            function addFilter() {              
                var filterPriceval = $filterPrice.find('option:selected').val();
                let filterPriceText = filterPriceval.slice(filterPriceval.indexOf(':') + 1);
                let filterCondition = $filterCondition.find('option:selected').val();
                let filterPriceValue = $filterPriceValue.val();
                var dataType = filterPriceval.slice(0, filterPriceval.indexOf(':'));
                let filterAndOrValue = $filterConditionV2.find('option:selected').val();
                const filterObj = { filterPriceText, filterCondition, filterPriceValue, filterAndOrValue }
                filterArray.push(filterObj);
                index += 1;
                $filters.empty();
                readFilterData(dataType);
                if ($filters.length) {
                    $filters.find('.vis-filter-item').addClass('vas-filters-row');
                }
            }
            $filters.on(VIS.Events.onTouchStartOrClick, function (event) {
                console.log(event)
                if ($(event.target).hasClass('glyphicon-trash')) {
                    filterArray.splice($(event.target), 1);
                    deleteFilter();
                    $(event.target).parents('.vis-filter-item').remove();
                }
                if ($(event.target).hasClass('glyphicon-edit')) {
                    let filterSelectTableVal = $(event.target).parents('.vis-filter-item').find(".vas-selecttable").text();
                    let filterPriceValue = $(event.target).parents('.vis-filter-item').find(".vis-filter-price-value").text();
                    let filterCondition = $(event.target).parents('.vis-filter-item').find(".vis-filter-condition").text();
                    $(event.target).parents('.vis-filter-item').siblings().find(".vis-filter-condition").removeClass('active');
                    $(event.target).parents('.vis-filter-item').siblings().find(".vis-filter-price-value").removeClass('active');
                    $(event.target).parents('.vis-filter-item').find(".vis-filter-condition").addClass('active');
                    $(event.target).parents('.vis-filter-item').find(".vis-filter-price-value").addClass('active');
                    $filterPrice.val(filterSelectTableVal);
                    $filterCondition.val(filterCondition);
                    $filterPriceValue.val(filterPriceValue);
                    $addFilterBtn.addClass('vis-edit-btn');
                    $addFilterBtn.val('Update Filter');                    
                }
            });

            function updateFilter() {
                $addFilterBtn.val('Add Filter');
                var updatedFilterPriceValue = $filterPriceValue.val();
                $('.vis-filter-price-value.active').text(updatedFilterPriceValue);
                var updatedFilterConditionValue = $filterCondition.find('option:selected').val();
                $('.vis-filter-condition.active').text(updatedFilterConditionValue);               
            }

            $sqlBtn.on(VIS.Events.onTouchStartOrClick, function () {
                if ($testSqlBtn.hasClass('vis-show-grid')) {
                    $query.hide();
                    $selectQuery.hide();                  
                }
                else {
                    $query.show();
                    $selectQuery.show();                 
                }
                $sqlContent.show();
                $testSqlBtn.show();
                $testSqlGeneratorBtn.hide();
                Active(this);
                $sqlGeneratorContent.hide();
                sqlGenerateFlag = false;
                sqlFlag = true;
            });

            $sqlGeneratorBtn.on(VIS.Events.onTouchStartOrClick, function () {
                $sqlGeneratorQueryResultGrid.hide();
                $sqlContent.hide();
                $testSqlGeneratorBtn.show();
                $testSqlBtn.hide();
                Active(this);
                $sqlGeneratorContent.show();
                sqlGenerateFlag = true;
                sqlFlag = false;
            });
            $joinsDiv.on(VIS.Events.onTouchStartOrClick, function () {
                $(this).siblings().removeClass('active');
                $(this).toggleClass('active');
            });
            $filterDiv.on(VIS.Events.onTouchStartOrClick, function () {
                $(this).siblings().removeClass('active');
                $(this).toggleClass('active');
            });
            $sortByDiv.on(VIS.Events.onTouchStartOrClick, function () {
                $(this).siblings().removeClass('active');
                $(this).toggleClass('active');
            });
            $windowFieldColumnSelect.on(VIS.Events.onTouchStartOrClick, function () {
                $(this).next().toggle();
                if ($(this).next().is(':empty')) {
                    $(this).next().css("display", "none");
                }
            });
            $joinsSelect.on(VIS.Events.onTouchStartOrClick, function () {
                $(this).next().toggle();
            });

            $saveBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var query = $selectQuery.text();
                alertID = VIS.context.getContext(windowNo, 'AD_Alert_ID');
                if (alertID > 0) {
                    UpdateAlertRule(query);
                }

            });

            $saveGeneratorBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var query = $selectGeneratorQuery.val();
                SaveAlertRule(query);
            });
            $updateGenerateBtn.on(VIS.Events.onTouchStartOrClick, function () {
                var query = $selectGeneratorQuery.val();
                UpdateAlertRule(query);
            });
            $addFilterBtn.on(VIS.Events.onTouchStartOrClick, function () {
                if (andFlag) {
                    WhereCondition = "WHERE";
                }
                else {
                    WhereCondition = $filterConditionV2.val();
                    andOrFlag = true;
                   
                }
                ApplyFilter(WhereCondition);
                if ($(this).hasClass('vis-edit-btn')) {
                    updateFilter();
                    $(this).removeClass('vis-edit-btn');
                    ClearText();
                }
                else {
                    let filterCondition = $filterCondition.find('option:selected').val();
                    let filterPriceValue = $filterPriceValue.val();
                    if (filterCondition && filterPriceValue) {
                        $addFilterDiv.append($filters);
                        addFilter();
                       
                    }
                    else {
                        $sqlResultDiv.text(VIS.Msg.translate(VIS.Env.getCtx(), "Please Add Filter Values"));
                    }
                }           
                ClearText();

            });

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
            $joins.on(VIS.Events.onTouchStartOrClick, function (event) {
                if ($(event.target).hasClass('glyphicon-trash')) {
                    var joinsDropDown = $(event.target).parents('.vas-join-item').find('.vas-selecttable').text();
                    joinsArray.splice($(event.target), 1);
                    $(event.target).parents('.vas-join-item').remove();
                    if ($selectGeneratorQuery.val().indexOf(joinsDropDown) > -1) {
                        var desiredResult = $selectGeneratorQuery.val().slice(0, $selectGeneratorQuery.val().lastIndexOf(joinsDropDown));
                        $selectGeneratorQuery.text(desiredResult);
                    }
                }
            });
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
                        sql = sql.slice(0, fromIndex) + seletedJoinCloumn + " " + fromClause;
                        joinQuery = " " + $joinsDropdown.val() + " " + TABLE2 + ' ON (' + keyColumn1 + " = " + keyColumn2 + ")";
                    }
                    var whereIndex = sql.indexOf('WHERE');
                    if (whereIndex != -1) {
                        var whereClause = sql.slice(whereIndex);
                        sql = sql.slice(0, whereIndex) + joinQuery + " " + whereClause;
                    } else {
                        sql += joinQuery;
                    }
                    $selectGeneratorQuery.empty();
                    $selectGeneratorQuery.text(sql);
                    $joinMultiSelect.val('');
                    $joinOnFieldColumnName2.val('');
                    $joinOnFieldColumnName1.val('');
                    sortFlag = true;
                    filterFlag = true;
                }             
            });
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
                $selectGeneratorQuery.empty();
                $selectGeneratorQuery.text(sql);
                orderbyFlag = false;
            });

            function Active(e) {
                $sqlBtn.removeClass('active');
                $sqlGeneratorBtn.removeClass('active');
                $(e).addClass('active');
            }
            getWindow();
            getJoinWindow();
            createPagingData();
            eventHandling();
        }

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
        function deleteFilter() {
            var filterColumnName = $(event.target).parents('.vis-filter-item').find(".vas-selecttable").text();
            var filterOperation = $(event.target).parents('.vis-filter-item').find(".vis-filter-condition").text();
            var filterValue = $(event.target).parents('.vis-filter-item').find(".vis-filter-price-value").text();;
            var currentValue = $selectGeneratorQuery.val();
            var conditionToRemove = $(event.target).parents('.vis-filter-item').find(".vis-filters-block").text();
            var orIndex = conditionToRemove.indexOf('OR');
            var andIndex = conditionToRemove.indexOf('AND');
            const wherePattern = /WHERE\s+(.+)/i;
            var whereIndex = currentValue.indexOf('WHERE');
            if (whereIndex != -1 && andIndex == -1 && orIndex == -1) {
                currentValue = currentValue.replace("WHERE", "");
                andFlag = true;
            }
            if (andIndex != -1 && orIndex != -1) {

            }
            var updatedQuery = currentValue.replace(conditionToRemove, "").trim();
            $selectGeneratorQuery.val('');
            $selectGeneratorQuery.val(updatedQuery);
        }

        function eventHandling() {
            $windowTabSelect.fireValueChanged = OnChange;
            $joinsWindowTabSelect.fireValueChanged = joinsTableOnChange;
        }

        $selectBox.on("change", joinsFieldColumnSelect);

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

            if (TableID > 0) {
                getColumns(TableID);
                $selectGeneratorQuery.empty();              
            }
            else {
                $selectGeneratorQuery.empty();
                getColumns(0);
            }
            $selectGeneratorQuery.show();
            gridDiv2.hide();
            $sqlGeneratorQueryResultGrid.hide();
            andFlag = true;
            sortFlag = true;

        }

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

        function ApplyFilter(WhereCondition) {
            var sql = $selectGeneratorQuery.val();
            var filterval = $filterPrice.val();
            if (filterval != null) {
                var filterColumn = filterval.slice(filterval.indexOf(':') + 1);
                var dataType = filterval.slice(0, filterval.indexOf(':'));
                if (sql != '' && filterColumn != '' && $filterPriceValue.val() != '') {
                    sql += " " + WhereCondition;
                    if (VIS.DisplayType.IsID == dataType || VIS.DisplayType.IsNumeric == dataType) {
                        sql += " " + filterColumn + " " + $filterCondition.val() + " " + $filterPriceValue.val();
                    }
                    else if (VIS.DisplayType.YesNo == dataType || VIS.DisplayType.String == dataType || VIS.DisplayType.IsDate == dataType || VIS.DisplayType.IsText == dataType) {
                        sql += " " + filterColumn + " " + $filterCondition.val() + " '" + $filterPriceValue.val() + "'";
                    }
                    else {
                        sql += " " + filterColumn + " " + $filterCondition.val() + " " + $filterPriceValue.val();
                    }
                    $selectGeneratorQuery.val('');
                    $selectGeneratorQuery.val(sql);
                    andFlag = false;
                }
            }
        }

        function ClearText() {
            $filterPrice.val('');
            $filterPriceValue.val('');
            $filterCondition.val('Select an option');
        }

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
                        $joinOnFieldColumnName1.prepend("<option selected='select'>Join on Field/Column Name1</option>");
                        $sortByDropdown.prepend("<option selected='select'>Select</option>");
                        $filterPrice.prepend("<option selected='select'>Select</option>");
                        for (var i = 1; i < result.length; i++) {
                            console.log(result[i])
                            $joinOnFieldColumnName1.append(" <option value=" + tableName + "." + result[i].DBColumn + ">" + result[i].ColumnName + "</option>");
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

        function getJoinsColumns(TableID2) {
            $selectBox.find('select').empty();
            $joinOnFieldColumnName2.empty();
            $joinMultiSelect.empty();
            $joinOnFieldColumnName2.prepend("<option value='*' selected='select'>Join on Field/Column Name2</option>");
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
                        TABLE2 = result[0].TableName;
                        for (var i = 1; i < result.length; i++) {
                            console.log(result[i]);
                            $joinOnFieldColumnName2.append(" <option value=" + TABLE2 + "." + result[i].DBColumn + ">" + result[i].ColumnName + "</option>");
                            $joinMultiSelect.append(" <div class='vas-column-list-item'>" + "<input type='checkbox' class='vas-column-checkbox'>" + result[i].FieldName + " - " + result[i].DBColumn + "</div>");
                            $sortByDropdown.append(" <option value=" + TABLE2 + "." + result[i].DBColumn + ">" + TABLE2 + " > " + result[i].ColumnName + "</option>");
                            $filterPrice.append(" <option value=" + result[i].DataType + ":" + TABLE2 + "." + result[i].DBColumn + ">" + TABLE2 + " > " + result[i].ColumnName + "</option>");
                        }
                        sortFlag = false;
                        filterFlag = false;
                        seletedJoinCloumn = [];
                        $(".vis-join-multiselect .vas-column-checkbox").on('click', function (eve) {
                            var selectedItem = $(this).parent('.vas-column-list-item').text();
                            var desiredResult = TABLE2 + "." + selectedItem.substring(selectedItem.indexOf('-') + 1).trim() + " AS " + '"' + selectedItem.slice(0, selectedItem.indexOf('-') - 1) + '" ';
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

        function getJoinWindow() {
            var lookups = new VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.Search, "AD_Tab_ID", 0, false, "");
            $joinsWindowTabSelect = new VIS.Controls.VTextBoxButton("AD_Tab_ID", true, false, true, VIS.DisplayType.Search, lookups);
            var locDep = $joinsWindowTabSelect.getControl().attr('placeholder', ' Search here').attr("id", "Ad_Tab_ID");
            var DivSearchCtrlWrap = $('<div class="vis-control-wrap">');
            var DivSearchBtnWrap = $('<div class="input-group-append">');
            $txtJoinWindow.css("width", "100%");
            $txtJoinWindow.append(DivSearchCtrlWrap).append(DivSearchBtnWrap);
            DivSearchCtrlWrap.append(locDep);
            DivSearchBtnWrap.append($joinsWindowTabSelect.getBtn(0));
            DivSearchBtnWrap.append($joinsWindowTabSelect.getBtn(1));
            $joinsWindowTabSelect.setCustomInfo('Alert_Window');
        }

        function getWindow() {
            var lookups = new VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.Search, "AD_Tab_ID", 0, false, "");
            $windowTabSelect = new VIS.Controls.VTextBoxButton("AD_Tab_ID", true, false, true, VIS.DisplayType.Search, lookups);
            // $windowTabSelect.append($windowTabLabel.getControl());
            var locDep = $windowTabSelect.getControl().attr('placeholder', ' Search here').attr("id", "Ad_Table_ID");
            var DivSearchCtrlWrap = $('<div class="vis-control-wrap">');
            var DivSearchBtnWrap = $('<div class="input-group-append">');
            $txtWindow.css("width", "100%");
            $txtWindow.append(DivSearchCtrlWrap).append(DivSearchBtnWrap);
            DivSearchCtrlWrap.append(locDep);
            DivSearchBtnWrap.append($windowTabSelect.getBtn(0));
            DivSearchBtnWrap.append($windowTabSelect.getBtn(1));
            $windowTabSelect.setCustomInfo('Alert_Window');
        }

        function SaveAlertRule(query) {
            if (query != null) {
                alertRuleID = 0;
                alertID = VIS.context.getContext(windowNo, 'AD_Alert_ID');
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/SaveQuery",
                    type: "POST",
                    data: { query: query, alertRuleID: alertRuleID, alertID: alertID, tableName: tableName, TableID: TableID },
                    async: false,
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result && result.length > 0) {
                            $sqlResultDiv.text(VIS.Msg.translate(VIS.Env.getCtx(), result));
                        }
                    },
                    error: function (error) {
                        console.log(error);
                    }
                });
            }
        }

        function UpdateAlertRule(query) {
            if (query != null) {
                alertRuleID = $self.ParentId;
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/UpdateQuery",
                    type: "POST",
                    data: { query: query, alertRuleID: alertRuleID, alertID: alertID, TableID: TableID },
                    async: false,
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result && result.length > 0) {
                            $sqlResultDiv.text(VIS.Msg.translate(VIS.Env.getCtx(), result));
                        }
                    },
                    error: function (error) {
                        console.log(error);
                    }
                });
            }
        }

        function joinsFieldColumnSelect() {
            var columnID = $joinsFieldColumnSelect.val();
            console.log("colunid" + columnID);
            var selectBoxVal = $selectBox.find('select').find('option:selected').val();
            console.log(selectBoxVal);
            if (selectBoxVal != "*") {
                $selectGeneratorQuery.text("SELECT name FROM " + tableName);
            }
            else {
                $selectGeneratorQuery.text("SELECT * FROM " + tableName);
            }
        }

        function GetSQL(columnValue) {
            $selectGeneratorQuery.empty();
            if (columnValue.length > 0) {
                $selectGeneratorQuery.text("SELECT " + columnValue + " FROM " + tableName);
            }
        }

        function getResult() {
            var query;
            if (sqlFlag)
                query = $selectQuery.text();
            if (sqlGenerateFlag) {
                query = $selectGeneratorQuery.val();
            }
            if (query != null) {
                $.ajax({
                    url: VIS.Application.contextUrl + "AlertSQLGenerate/GetResult",
                    data: { Query: query, pageNo: pageNo, pageSize: pageSize },
                    type: "POST",
                    async: false,
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result != null) {
                            if (result.length > 0) {
                               
                                if (loadTotalPages) {
                                    var totalReocrds = result.length;
                                    if (totalReocrds < 1 || totalReocrds < pageSize) {
                                        totalPages = 1;
                                    }
                                    else if (pageSize == 0) {
                                        totalPages = 1;
                                    }
                                    else {
                                        totalPages = Math.ceil(totalReocrds / pageSize);// + ((totalRows % pageSize) != 0 ? 1 : 0);
                                        if (totalPages > 1) {
                                            createPagingData();
                                        }
                                    }
                                    fillPagesCombo(true);
                                    createPagingData();
                                }
                                loadGrid(result);
                            }
                            else {
                                $sqlResultDiv.text(VIS.Msg.translate(VIS.Env.getCtx(), "NoRecordFound"));
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
                            $testSqlBtn.removeClass('vis-show-grid');

                            $sqlResultDiv.text(VIS.Msg.translate(VIS.Env.getCtx(), "ValidQuery"));

                        }
                    },
                    error: function (error) {
                        console.log(error);
                    }
                });
            }
        }

        function loadGrid(result) {
            if (dGrid != null) {
                dGrid.destroy();
                dGrid = null;
            }
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
                dGrid = $(gridDiv2).w2grid({
                    name: "gridForm" + $self.windowNo,
                    recordHeight: 35,
                    multiSelect: true,
                    columns: grdCols,
                    records: w2utils.encodeTags(result)
                });
               // dGrid.refresh();

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
                //dGrid.refresh();
            }
        }

        function createPagingData() {
            if (sqlFlag) {
                if (gridDiv == null) {
                    html.hide();
                }
                else {
                    html.show();
                }
            }
            else {
                if (gridDiv2 == null) {
                    html.hide();
                }
                else {
                    html.show();
                }
            }
            $statusBar.find('.vis-pageup').css("opacity", "0.6");
            $statusBar.find('.VA093_cmpPageUp').css("opacity", "0.6");
            $statusBar.find('.vis-pagedown').css("opacity", "0.6");
            $statusBar.find('.VA093_cmpPageDown').css("opacity", "0.6");
            if (totalPages == 1 || totalPages == 0) {
                $statusBar.find('.vis-pageup').css("opacity", "0.6");
                $statusBar.find('.VA093_cmpPageUp').css("opacity", "0.6");
            }
            if (totalPages > 1) {
                $statusBar.find('.vis-pagedown').css("opacity", "1.0");
                $statusBar.find('.VA093_cmpPageDown').css("opacity", "1.0");
            } else {
                $statusBar.find('.vis-pagedown').css("opacity", "0.6");
                $statusBar.find('.VA093_cmpPageDown').css("opacity", "0.6");
            }
            $liPaging = $statusBar.find('.VA093-status-page-cmb');
            $btnPageDown = $statusBar.find('.VA093_cmpPageDown');
            $btnPageUp = $statusBar.find('.VA093_cmpPageUp');
            $btnPageDown.off('click', onBtnPageDown);
            $btnPageDown.on('click', onBtnPageDown);
            $btnPageUp.off('click', onBtnPageUp);
            $btnPageUp.on('click', onBtnPageUp);
        };

        function onBtnPageDown() {
            pageNo = VIS.Utility.Util.getValueOfInt($cmbPaging.val());
            if (pageNo > 0) {
                if (pageNo < totalPages) {
                    pageNo = pageNo + 1;
                    $cmbPaging.val(pageNo);
                    getResult();
                }
            }
            if (pageNo == totalPages) {
                $statusBar.find('.vis-pagedown').css("opacity", "0.6");
                $statusBar.find('.VA093_cmpPageDown').css("opacity", "0.6");
            }
            if (totalPages > 1) {
                $statusBar.find('.vis-pageup').css("opacity", "1.0");
                $statusBar.find('.VA093_cmpPageUp').css("opacity", "1.0");
            }
            else {
                $statusBar.find('.vis-pageup').css("opacity", "0.6");
                $statusBar.find('.VA093_cmpPageUp').css("opacity", "0.6");
            }
        };

        function onBtnPageUp() {
            pageNo = VIS.Utility.Util.getValueOfInt($cmbPaging.val());
            if (pageNo > 1) {
                $statusBar.find('.vis-pagedown').css("opacity", "1.0");
                $statusBar.find('.VA093_cmpPageDown').css("opacity", "1.0");
                pageNo = pageNo - 1;
                $cmbPaging.val(pageNo);
                if (pageNo == 1 || pageNo == 0) {
                    $statusBar.find('.vis-pageup').css("opacity", "0.6");
                    $statusBar.find('.VA093_cmpPageUp').css("opacity", "0.6");
                }
                getResult();
            }
        };

        function fillPagesCombo() {
            $cmbPaging = $('<select class="vis-statusbar-combo"></select>');
            for (var i = 1; i <= totalPages; i++) {
                $cmbPaging.append('<option value="' + i + '">' + i + '</option>');
            }
            $liPaging.empty();
            $liPaging.append($cmbPaging);
            $cmbPaging.off("change");
            $cmbPaging.on("change", function () {
                pageNo = $cmbPaging.val();
                getResult();
            });
        };

        this.getRoot = function () {
            return $root;
        };
    };
    VIS.SQL.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        this.init();
    };

    /*This function to update tab panel based on selected record*/
    VIS.SQL.prototype.refreshPanelData = function (ParentId, selectedRow) {
        this.ParentId = ParentId;
        this.selectedRow = selectedRow;
        this.SqlQuery(ParentId);
    };
    /*
     This will set width as per window width in dafault case it is 75%
     */
    VIS.SQL.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };
    /*
    Memory dealocation
    */
    VIS.SQL.prototype.dispose = function () {
        this.windowNo = 0;
        this.curTab = null;
        this.rowSource = null;
        this.panelWidth = null;
    }

})(VIS, jQuery);