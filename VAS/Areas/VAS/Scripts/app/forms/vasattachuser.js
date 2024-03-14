
/********************************************************
 * Module Name: VAS Standard
 * Purpose : create existing user With Business Partner 
 * Employee code: VAI061
 * Created Date: 10-01-2024
 * Updated Date: 14-02-2024
 ********************************************************/

; VAS = window.VAS || {};
//self-invoking function
; (function (VAS, $) {
    // Form Class function fullnamespace
    VAS.AttachUserToBP = function () {
        this.frame;
        this.windowNo;
        var _windowNo;
        var dGrid = null;
        var pageNo = 1;
        var pageSize = 10;    
        var ulPaging, liPrevPage, liCurrPage, liNextPage,  selectPage;
        var recordCount = null;
        var $root;
        var $self = this;
        var arrListColumns = [];
        var $upperDiv = null;
        var lowerDiv = null;
        var bottomDiv = null;
        var $bsyDiv = null;
        var c_BPartnerID = 0;
        var userName = [];
        var $parentdiv = null;
        var $inputDiv = null;
        var $okBtn, $cancelBtn, $searchButton;
        var $employee = null;
        var $textEmployee = null;
        var $searchEmployee = null;
        var $textSearchEmployee = null;
        var $btnDiv = null;
        var search = null;
        var selectedRecord = [];
        var totalPages = null;
        var countRecords = 0;
        // Initialize UI Elements

        function initializeComponent() {
        
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis-busyindicatordiv"></i></div></div>');
            $parentdiv = $("<div class='vas-parent'>");
            recordCount = $("<div id='vas-recordCount'><span class='vas-recordCount'></span></div>");
            $inputDiv = $("<div class='vas-businessPartnerInput py-2'>");
            $root = $("<div class='vas-root'>");
            $upperDiv = $("<div>");
            $btnDiv = $("<div class= 'd-flex'>");
            lowerDiv = $("<div class='vas-lowerDiv'>");
            bottomDiv = $("<div class='vas-bottom'>");
            $searchButton = $('<div class="input-group-append vas-businessPartnerButton"><button class="input-group-text vas-businessPartnerSearch"><i class="fa fa-search" aria-hidden="true"></i></button></div>');
            $cancelBtn = $("<button class='vas-actionbtn'> " + VIS.Msg.getMsg("Cancel") + " </button>");
            $okBtn = $("<button class='vas-actionbtn vas-disableBtn ' > " + VIS.Msg.getMsg("Ok") + " </button>");
          
            ulPaging = $('<ul class="vis-ad-w-p-s-plst"">');
            liPage = $('<li></li>');

            liPrevPage = $('<li style="opacity: 0.6;" class ="vas-disablePage" ><div><i class="vis vis-pageup" title="' + VIS.Msg.getMsg("PageDown") + '" style="opacity: 0.6;"></i></div></li>');

            selectPage = $("<select class = 'vis-statusbar-combo'></select>");

            liCurrPage = $('<li>').append(selectPage);

            liNextPage = $('<li style="opacity: 1"><div><i class="vis vis-pagedown " title="' + VIS.Msg.getMsg(" PageUp") + '" style = "opacity: 0.6;" ></i ></div ></li> ');
            ulPaging.append(liPage).append(liPrevPage).append(liCurrPage).append(liNextPage);
          
            /* Employee textbox*/
            $employee = $('<div class="input-group vis-input-wrap mb-0 vas-inputboxPadding" >');
            $textEmployee = $('<div class="vis-control-wrap"><input type="text" maxlength="80" disabled /><label>' + VIS.Msg.getMsg("Employee") + ' </label></div>');
            $employee.append($textEmployee);
            $textEmployee.find('input').val(VIS.context.getContext(_windowNo, "Value"));
            /* SearchExistingUser textbox*/
            $searchEmployee = $('<div class="input-group vis-input-wrap mb-0 vas-inputboxPadding" >');
            $textSearchEmployee = $('<div class="vis-control-wrap"><input type="text" maxlength="80"  data-hasbtn=" " /><label>' + VIS.Msg.getMsg("VAS_SearchExistingUser") + '</label></div>');
            $searchEmployee.append($textSearchEmployee).append($searchButton);
            $inputDiv.append($employee).append($searchEmployee);       
            $parentdiv.append($inputDiv);
            $upperDiv.append($parentdiv);
            $btnDiv.append(ulPaging).append($okBtn).append($cancelBtn);
            bottomDiv.append(recordCount).append($btnDiv);
            $root.append($upperDiv).append(lowerDiv).append(bottomDiv)
            $root.append($bsyDiv);
            dropDown();

            /* ok button use to call the getRecordID function */
            $okBtn.on(VIS.Events.onTouchStartOrClick, function () {
                linkPartnerID();

            });
          /* click function on previous div used to call the previous page record*/
            liPrevPage.on("click", function () {
                liNextPage.css("opacity", "1");
                liNextPage.removeClass("vas-disablePage");
            
                pageNo = selectPage.val() - 1;
                if (pageNo >= 1) {                 
                    loadGrid(pageNo, pageSize, true);
                    selectPage.val(pageNo);
                  
                }

                if (pageNo <= 1) {
                    liPrevPage.css("opacity", "0.6");
                    $(liPrevPage).addClass("vas-disablePage");
                }
            });
            /*click function on nextpage div used to call the next page record */

            liNextPage.on("click", function () {
                liPrevPage.css("opacity", "1");
                $(liPrevPage).removeClass("vas-disablePage");
                 pageNo = parseInt(selectPage.val()) + 1;
                if (pageNo <= totalPages) {
                    loadGrid(pageNo, pageSize, true);
                    selectPage.val(pageNo);
                }
                if (pageNo >= totalPages) {
                    liNextPage.css("opacity", "0.6");
                    liNextPage.addClass("vas-disablePage");
                }
            });


        /* function used to change the value of option when we click on the values on dropdown*/
            function dropDown() {
                selectPage.on("change", function () {
                    pageNo = selectPage.val();
                  
                    loadGrid(pageNo, pageSize, true);
                    selectPage.find("option[value='" + pageNo + "']").prop("selected", true);
                    if (pageNo >= totalPages) {
                        liNextPage.css("opacity", "0.6");
                        liNextPage.addClass("vas-disablePage");
                        liPrevPage.css("opacity", "1");
                        $(liPrevPage).removeClass("vas-disablePage");
                    }
                    else {
                        liNextPage.css("opacity", "1");
                        liNextPage.removeClass("vas-disablePage");
                    }
                    if (pageNo <= 1) {
                        liPrevPage.css("opacity", "0.6");
                        $(liPrevPage).addClass("vas-disablePage");
                        liNextPage.css("opacity", "1");
                        liNextPage.removeClass("vas-disablePage");

                    }
                    else {
                        liPrevPage.css("opacity", "1");
                        $(liPrevPage).removeClass("vas-disablePage");
                    }
                
                });
                
            };
           
                //search button use to 
                //search the records from grid 
            
            $searchButton.on(VIS.Events.onTouchStartOrClick, function () {
                selectedRecord = [];
                $($okBtn).addClass("vas-disableBtn");

                search = $textSearchEmployee.children('input').val();
                pageNo = 1;
                countRecords = 0;
                arrListColumns = [];
                busyDiv(true);
                loadGrid(pageNo, pageSize, true);
                liNextPage.css("opacity", "1");
                liNextPage.removeClass("vas-disablePage");
                $textSearchEmployee.children('input').val('');
            });

            // close the form
            $cancelBtn.on(VIS.Events.onTouchStartOrClick, function () {
                $self.frame.close();
            });

            /* keyup used to search the record after pressing enter */
            $parentdiv.on("keyup", function (e) {
                search = $textSearchEmployee.children('input').val();
                pageNo = 1;
                countRecords = 0;
                 arrListColumns = [];
                if (e.keyCode === 13) {
                    selectedRecord = [];
                    $($okBtn).addClass("vas-disableBtn");
                    busyDiv(true);
                    loadGrid(pageNo, pageSize, true);
                    liNextPage.css("opacity", "1");
                    liNextPage.removeClass("vas-disablePage");
                    $textSearchEmployee.children('input').val('');
                }

            });
          
        }
      
       /* function used to get the dropdown values acoroding to number of total pages*/
        function PageCtrls() {
            selectPage.empty();
            if (totalPages > 0) {
                for (var i = 0; i < totalPages; i++) {
                    selectPage.append($("<option>").val(i + 1).text(i + 1));
                }
                selectPage.val(pageNo);
            }
        };    
    
        /*  function used to link the userID against PartnerID*/
        function linkPartnerID() {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/VASAttachUserToBP/UpdateUser",
                type: "POST",
                data: {
                    userNames: userName,
                    userIds: selectedRecord,
                    c_BPartnerID: c_BPartnerID
                },
                success: function (result) {
                    result = JSON.parse(result);
                    if (result.length == 0) {
                        $self.frame.close();
                    }
                    else {
                        var errorHtml = "<ul>";
                        for (var i = 0; i < result.length; i++) {
                            errorHtml += "<li>" + result[i].userName + ":" + result[i].error + "</li>";
                        }
                        errorHtml += "</ul>";
                        var obj = new VAS.AttachError($self.WindowNo, errorHtml);
                        obj.show();
                    }

                },

                error: function (eror) {
                    console.log(eror);
                }
            });
        }
     /* selected row function used to get the selected record data*/
        function onSelectRow(event) {
            if (event.recid == undefined) {
                var countRecords = w2ui['GridDiv' + $self.windowNo].records.length;
                var selectedRecordSet = new Set(selectedRecord);

                // Iterate over each record in the grid
                for (var i = 0; i < countRecords; i++) {
                    var recid = w2ui['GridDiv' + $self.windowNo].records[i].recid;

                    // Check if the recid is not already in the selectedRecord array
                    if (!selectedRecordSet.has(recid)) {
                        selectedRecord.push(recid);
                    }
                }
            
                // Remove the disabled class from the ok button
                $($okBtn).removeClass("vas-disableBtn");
            }
            else {
                var recordId = event.recid;
                if (recordId != undefined && selectedRecord.indexOf(recordId) === -1) {
                    selectedRecord.push(recordId);
                    for (i = 0; i < selectedRecord.length; i++) {
                        selectedRecord[i] = parseInt(selectedRecord[i], 10)
                    }
                }
              
                $($okBtn).removeClass("vas-disableBtn");
            }
        };
       /* function used to deselect the record data and also enable and disbale the ok button*/
        function onUnselectedRow(event) {
            if (event.recid == undefined) {
                var currentRecid = [];
                countRecords = w2ui['GridDiv' + $self.windowNo].records.length;
                for (i = 0; i < countRecords; i++) {
                    recid = w2ui['GridDiv' + $self.windowNo].records[i].recid
                    currentRecid.push(recid);
                    selectedRecord = selectedRecord.filter(function (value) {
                        return value != currentRecid[i];
                    });
                }
                if (selectedRecord.length == 0) {
                    $($okBtn).addClass("vas-disableBtn");
                }
            }
            else {
                recordId = event.recid;
                selectedRecord = selectedRecord.filter(function (value) {
                    return value != recordId;
                   
                });
                if (selectedRecord.length == 0) {
                    $($okBtn).addClass("vas-disableBtn");
                }   
            }
        }
        /* function used to get the records via ajax */
        function loadGrid(pageNo, pageSize, isReload) {
            var data = [];
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/VASAttachUserToBP/GetUserList",
                type: "GET",
                data: {
                    searchKey: search,
                    pageNo: pageNo,
                    pageSize: pageSize
                },
                contentType: "application/json; charset=utf-8",
                success: function (result) {
                    result = JSON.parse(result);
                    recordCount.find('span.vas-recordCount').text(VIS.Msg.getMsg("VAS_totalRecords") + ': ' + result.RecordCount);
                    totalPages = Math.ceil(result.RecordCount /pageSize);
                    if (result != null && (result.UserList) != null) {
                        countRecords = 0;
                        countRecords = countRecords + (result.UserList).length;
                        totalRecords = result.RecordCount;
                        for (var i = 0; i < (result.UserList).length; i++) {

                            var line = {};
                            line['recid'] = result.UserList[i].recid;
                            line['Image'] = result.UserList[i].ImageUrl;
                            line['Name'] = result.UserList[i].Name;
                            line['Email'] = result.UserList[i].Email;
                            line['Mobile'] = result.UserList[i].Mobile;
                            line['SuperVisor'] = result.UserList[i].SupervisorName;
                            line['User_ID'] = result.UserList[i].UserID;
                            data.push(line);


                        }
                    }
                 
                    dynInit(data, isReload);
                    busyDiv(false);

                  
                },
                error: function (eror) {
                    busyDiv(false);
                    console.log(eror);

                }
            });
          
        };

        /* Function used to display the records on grid */
        function dynInit(data, isReload) {
            if (dGrid == null) {
            arrListColumns = [];
            if (arrListColumns.length == 0) {
                arrListColumns.push({ field: "recid", caption: "recid", sortable: false, size: '0%', hidden: true });
                arrListColumns.push({
                    field: "Image", caption: VIS.Msg.getMsg("VAS_Image"), sortable: false, size: '7%', min: 35, hidden: false,
                    render: function (rec) {
                        var recImage = rec.Image;
                        if (recImage != null) {
                            return '<div class="vis-grid-row-td-icon-center vis-gridImageicon"><img class="vas-businessPartnerImg" alt=' + rec.Image + ' src="' + VIS.Application.contextUrl + rec.Image + '"></div>';
                        }
                        else {
                            return '<div class="vis-grid-row-td-icon-center vis-gridImageicon" ><div class="vis-app-user-img-wrap">  <i class="fa fa-user"></i><img src="" alt="profile image"> </div></div>';
                        }
                    }
                });
                arrListColumns.push({ field: "User_ID", caption: VIS.Msg.getMsg("VAS_UserID"), sortable: false, size: '14%', hidden: false });
                arrListColumns.push({ field: "Name", caption: VIS.Msg.getMsg("Name"), sortable: false, size: '21%', hidden: false });
                arrListColumns.push({ field: "Email", caption: VIS.Msg.getMsg("VAS_Email"), sortable: false, size: '24%', hidden: false });
                arrListColumns.push({ field: "Mobile", caption: VIS.Msg.getMsg("Mobile"), sortable: false, size: '17%',  hidden: false });
                arrListColumns.push({ field: "SuperVisor", caption: VIS.Msg.getMsg("VAS_SuperVisor"), sortable: false, size: '17%', hidden: false });


                /* encode the tags */
                w2utils.encodeTags(data);

                dGrid = $(lowerDiv).w2grid({
                    name: "GridDiv" + $self.windowNo,
                    recordHeight: 35,
                    show: { selectColumn: true },
                    multiSelect: true,
                    columns: arrListColumns,
                    records: data,
                    onSelect: onSelectRow ,
                    onUnselect: onUnselectedRow

                });
            }
                
            }
       /* check applied if reload is true than it clear the dgrid and the updated grid records */
            if (isReload) {
                dGrid.clear();
                dGrid.add(data);
            }
            else if (pageNo > 1) {
                dGrid.add(data);
            }
            PageCtrls();
            reapplySelection();
        };

       // function used to select the already records after calling the grid again 

        function reapplySelection() {
            if (selectedRecord.length > 0 ) {
                dGrid.select.apply(dGrid,selectedRecord);
            }
        }

       
        /* display function used to display the grid */
        this.display = function () {
            busyDiv(true);
            loadGrid(pageNo, pageSize, false);

        }


        this.Initialize = function () {
            // load by java script
            initializeComponent();
        }

        this.getRoot = function () {
            return $root;
        };
       /* busy Indicator*/
        function busyDiv(Value) {
            if (Value) {
                $bsyDiv[0].style.visibility = 'visible';
            }
            else {
                $bsyDiv[0].style.visibility = 'hidden';
            }
        };
        /*function used to deallocate the memory*/
        this.disposeComponent = function () {
            if ($root)
                $root.remove();
            if (dGrid != null) {
                dGrid.destroy();
            }
            dGrid = null;
            $root = null;
            $bsyDiv = null;
            arrListColumns = null;
            this.getRoot = null;
            this.disposeComponent = null;
            this.lowerDiv = null;
            this.upperDiv = null;
            this.frame = null;
            this.bottomDiv = null;
            this.parentDiv = null;
            this.$employee = null;
            this.$textEmployee = null;
            this.$searchEmployee = null;
            this.$textSearchEmployee = null;
            this.inputDiv = null;
            userName = null;

            if ($okBtn)
                $okBtn.off(VIS.Events.onTouchStartOrClick);
            if ($cancelBtn)
                $cancelBtn.off(VIS.Events.onTouchStartOrClick);
            $okBtn = $cancelBtn = null;
            selectedRecordIds = null;
            selectedRecord = null;

        };


        VAS.AttachUserToBP.prototype.init = function (windowNo, frame) {
            //Assign to this Varable
            this.frame = frame;//
            c_BPartnerID = frame.getRecord_ID();
            this.windowNo = windowNo;
            _windowNo = windowNo;
            this.Initialize();
            this.frame.getContentGrid().append(this.getRoot());
            this.frame.getContentGrid().height();
            var sself = this         
            sself.display();
        };

        // Must implement dispose
        VAS.AttachUserToBP.prototype.dispose = function () {
            this.disposeComponent();
            // call frame dispose function
            if (this.frame)
                this.frame.dispose();
            this.frame = null;
        };

        /* To set the width of frame */
        VAS.AttachUserToBP.prototype.setWidth = function () {
            return 900;
        };
      /*  TO set the height of frame*/
        VAS.AttachUserToBP.prototype.setHeight = function () {
            return 620;
        };
        // Load form into VIS
        VAS.AttachUserToBP = VAS.AttachUserToBP
    }
})(VAS, jQuery);