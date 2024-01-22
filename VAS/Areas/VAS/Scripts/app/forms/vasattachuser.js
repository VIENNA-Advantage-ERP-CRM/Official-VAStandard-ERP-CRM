
/********************************************************
 * Module Name: VAS Standard
 * Purpose : create existing user With Business Partner 
 * Employee code: VAI061
 * Created Date: 10-01-2024
 * Updated Date: 22-01-2024
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
        var recordCount = null;
        var $root;
        var $self = this;
        var arrListColumns = [];
        var $upperDiv = null;
        var lowerDiv = null;
        var bottomDiv = null;
        var $bsyDiv = null;
        var recordID = null;
        var c_BPartnerID = 0;
        var countRecords = 0;
        var UserIds = [];
        var userName = [];
        var $parentdiv = null;
        var $inputDiv = null;
        var $buttonDiv = null;
        var $okBtn, $cancelBtn, $searchButton;
        var $employee = null;
        var $textEmployee = null;
        var $searchEmployee = null;
        var $textSearchEmployee = null;
        var totalRecords = 0;



        // Initialize UI Elements

        function initializeComponent() {
        
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis-busyindicatordiv"></i></div></div>');
            $parentdiv = $("<div class='vas-parent'>");
            recordCount = $("<div id='vas-recordCount'><span class='vas-recordCount'></span></div>");
            $inputDiv = $("<div class='vas-businessPartnerInput py-3'>");
            $buttonDiv = $("<div class='vas-businessPartnerButton'>");
            $root = $("<div class='vas-root'>");
            $upperDiv = $("<div>");
            lowerDiv = $("<div class='vas-lowerDiv'>");
            bottomDiv = $("<div class='vas-bottom'>");
            $searchButton = $('<button class="vas-businessPartnerSearch"><i class="fa fa-search" aria-hidden="true"></i></button>');
            $cancelBtn = $("<button class='vas-actionbtn'> " + VIS.Msg.getMsg("Cancel") + " </button>");
            $okBtn = $("<button class='vas-actionbtn vas-disableBtn ' > " + VIS.Msg.getMsg("Ok") + " </button>");

            /* Employee textbox*/
            $employee = $('<div class="input-group vis-input-wrap mb-0" >');
            $textEmployee = $('<div class="vis-control-wrap"><input type="text" maxlength="80" disabled /><label>' + VIS.Msg.getMsg("Employee") + ' </label></div>');
            $employee.append($textEmployee);
            $textEmployee.find('input').val(VIS.context.getContext(_windowNo, "Value"));
            /* SearchExistingUser textbox*/
            $searchEmployee = $('<div class="input-group vis-input-wrap mb-0" >');
            $textSearchEmployee = $('<div class="vis-control-wrap"><input type="text" maxlength="80" /><label>' + VIS.Msg.getMsg("VAS_SearchExistingUser") + '</label></div>');
            $searchEmployee.append($textSearchEmployee);
            $inputDiv.append($employee).append($searchEmployee)
            $buttonDiv.append($searchButton);
            $parentdiv.append($inputDiv).append($buttonDiv);
            $upperDiv.append($parentdiv);
            bottomDiv.append(recordCount).append($okBtn).append($cancelBtn);
            $root.append($upperDiv).append(lowerDiv).append(bottomDiv)
            $root.append($bsyDiv);

            /* ok button use to call the getRecordID function */
            $okBtn.on(VIS.Events.onTouchStartOrClick, function () {
                linkPartnerID();

            });

            /*  
                search button use to 
                search the records from grid 
            */
            $searchButton.on(VIS.Events.onTouchStartOrClick, function () {
                pageNo = 1;
                countRecords = 0;
                arrListColumns = [];
                loadGrid(pageNo, pageSize, true);

            });

            // close the form
            $cancelBtn.on(VIS.Events.onTouchStartOrClick, function () {
                $self.frame.close();
            });

            /* keyup used to search the record after pressing enter */
            $parentdiv.on("keyup", function (e) {
                pageNo = 1;
                countRecords = 0;
                 arrListColumns = [];
                if (e.keyCode === 13) {
                    loadGrid(pageNo, pageSize,true);
                }

            });
       
        }

        /*  function used to link the userID against PartnerID*/
        function linkPartnerID() {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/VASAttachUserToBP/UpdateUser",
                type: "POST",
                data: {
                    userNames: userName,
                    userIds: UserIds,
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
        /* function used to get the records via ajax */
        function loadGrid(pageNo, pageSize, isReload) {
            var data = [];

            $.ajax({
                url: VIS.Application.contextUrl + "VAS/VASAttachUserToBP/GetUserList",
                type: "GET",
                data: {
                    searchKey: $textSearchEmployee.children('input').val(),
                    pageNo: pageNo,
                    pageSize: pageSize
                },
                contentType: "application/json; charset=utf-8",
                success: function (result) {
                    result = JSON.parse(result);
                    recordCount.find('span.vas-recordCount').text(VIS.Msg.getMsg("VAS_totalRecords") + ':' + result.RecordCount);
                    totalRecords = result.RecordCount;
                    if (result != null && (result.UserList) != null) {
                        countRecords = countRecords + (result.UserList).length;
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
                    arrListColumns.push({ field: "recid", caption: "recid", sortable: true, size: '10%', min: 100, hidden: true });
                    arrListColumns.push({
                        field: "Image", caption: VIS.Msg.getMsg("VAS_Image"), sortable: true, size: '5%', min: 70, hidden: false,
                        render: function (rec) {
                            var recImage = rec.Image;
                            if (recImage != null) {
                                return '<div class="text-center"><img class="vas-businessPartnerImg" alt=' + rec.Image + ' src="' + VIS.Application.contextUrl + rec.Image + '"></div>';
                            }
                            else {
                                return '<div class="vis-grid-row-td-icon-center vis-gridImageicon" ><div class="vis-app-user-img-wrap">  <i class="fa fa-user"></i><img src="" alt="profile image"> </div></div>';
                            }
                        }
                    });
                    arrListColumns.push({ field: "Name", caption: VIS.Msg.getMsg("Name"), sortable: true, size: '9%', min: 130, hidden: false });
                    arrListColumns.push({ field: "Email", caption: VIS.Msg.getMsg("VAS_Email"), sortable: true, size: '9%', min: 130, hidden: false });
                    arrListColumns.push({ field: "Mobile", caption: VIS.Msg.getMsg("Mobile"), sortable: true, size: '9%', min: 110, hidden: false });
                    arrListColumns.push({ field: "SuperVisor", caption: VIS.Msg.getMsg("VAS_SuperVisor"), sortable: true, size: '9%', min: 130, hidden: false });
                    arrListColumns.push({ field: "User_ID", caption: VIS.Msg.getMsg("VAS_UserID"), sortable: true, size: '7%', min: 130, hidden: false });

                    /* encode the tags */
                    w2utils.encodeTags(data);

                    dGrid = $(lowerDiv).w2grid({
                        name: "GridDiv" + $self.windowNo,
                        recordHeight: 35,
                        show: { selectColumn: true },
                        multiSelect: true,
                        columns: arrListColumns,
                        records: data,
                        onSelect: function (event) {

                            if (dGrid.records.length > 0)
                                recordID = dGrid.get(event.recid).recid;
                            Name = dGrid.get(event.recid).Name;
                            $($okBtn).removeClass("vas-disableBtn");
                            UserIds.push(recordID);
                            userName.push(Name);
                        },
                        onUnselect: function (event) {

                            if (dGrid.records.length > 0) {
                                recordID = dGrid.get(event.recid).recid;
                                Name = dGrid.get(event.recid).Name;
                                UserIds = jQuery.grep(UserIds, function (value) {
                                    return value != recordID;
                                });
                                userName = jQuery.grep(userName, function (value) {
                                    return value != Name;
                                });
                            }
                            if (UserIds.length == 0) {
                                $($okBtn).addClass("vas-disableBtn");
                            }
                        }
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
            /*  onScroll event applied to execute paging on scroll grid */

            lowerDiv.find('#grid_GridDiv' + ($self.windowNo) + '_records').off("scroll");
            lowerDiv.find('#grid_GridDiv' + ($self.windowNo) + '_records').on("scroll", function () {
                if ($(this).scrollTop() + $(this).innerHeight() + 5 >= $(this)[0].scrollHeight) {
                    pageNo += 1;

                    if (totalRecords == countRecords) {
                        return;
                    }
                    busyDiv(true);
                    loadGrid(pageNo, pageSize, false);

                }
            });
        };



        /* display function used to display the grid */
        this.display = function () {
            busyDiv(true);
            loadGrid(pageNo, pageSize,false);
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

            if ($okBtn)
                $okBtn.off(VIS.Events.onTouchStartOrClick);
            if ($cancelBtn)
                $cancelBtn.off(VIS.Events.onTouchStartOrClick);
            $okBtn = $cancelBtn = null;
            UserIds = null;
        };


        VAS.AttachUserToBP.prototype.init = function (windowNo, frame) {
            //Assign to this Varable
            this.frame = frame;//
            c_BPartnerID = frame.getRecord_ID();
            this.windowNo = windowNo;
            _windowNo = windowNo;
            this.Initialize();
            this.frame.getContentGrid().append(this.getRoot());
            this.frame.getContentGrid().height(500);
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