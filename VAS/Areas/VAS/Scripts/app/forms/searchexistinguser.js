 
/********************************************************
 * Module Name: VAS Standard
 * Purpose : create existing user With Business Partner 
 * Employee code: VAI061
 * Created Date: 10-01-2024
 * Updated Date:
 ********************************************************/

; VAS = window.VAS || {};
//self-invoking function
; (function (VAS, $) {
    // Form Class function fullnamespace
    VAS.AttachUserToBP = function () {
        this.frame;
        this.windowNo;
        var dGrid = null;
        var $root;
        var $self = this;
        var arrListColumns = [];
        var $upperDiv = null;
        var lowerDiv = null;
        var bottomDiv = null;
        var recordID = null;
        var c_BPartnerID = 0;
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


        // Initialize UI Elements

        function initializeComponent() {
            $parentdiv = $("<div class='vas-parent'>");
            $inputDiv = $("<div class='vas-businessPartnerInput py-3'>");
            $buttonDiv = $("<div class='vas-businessPartnerButton'>");
            $root = $("<div class='vas-root'>");
            $upperDiv = $("<div>");
            lowerDiv = $("<div class='vas-lowerDiv'>");
            bottomDiv = $("<div class='vas-bottom'>");
            $searchButton = $('<button class="vas-businessPartnerSearch"><i class="fa fa-search" aria-hidden="true"></i></button>');
            $cancelBtn = $("<button class='vas-actionbtn'> "+ VIS.Msg.getMsg("Cancel") +" </button>");
            $okBtn = $("<button class='vas-actionbtn vas-disableBtn ' > " + VIS.Msg.getMsg("Ok") + " </button>");

           /* Employee textbox*/
            $employee = $('<div class="input-group vis-input-wrap mb-0" >');
            $textEmployee = $('<div class="vis-control-wrap"><input type="text" maxlength="80" disabled /><label>Employee</label></div>');
            $employee.append($textEmployee);

           /* SearchExistingUser textbox*/
            $searchEmployee = $('<div class="input-group vis-input-wrap mb-0" >');
            $textSearchEmployee = $('<div class="vis-control-wrap"><input type="text" maxlength="80" /><label>SearchExistingUser</label></div>');
            $searchEmployee.append($textSearchEmployee);
            $inputDiv.append($employee).append($searchEmployee)
            $buttonDiv.append($searchButton);
            $parentdiv.append($inputDiv).append($buttonDiv);
            $upperDiv.append($parentdiv);
            bottomDiv.append($okBtn).append($cancelBtn);
            $root.append($upperDiv).append(lowerDiv).append(bottomDiv);

            /* ok button use to call the getRecordID function */
            $okBtn.on(VIS.Events.onTouchStartOrClick, function () {
                getRecordID();

            });

          /*  
              search button use to 
              search the records from grid 
          */
            $searchButton.on(VIS.Events.onTouchStartOrClick, function () {
                arrListColumns = [];
                loadGrid();

            });

           // close the form
            $cancelBtn.on(VIS.Events.onTouchStartOrClick, function () {
                $self.frame.close();
            });

           /* keyup used to search the record after pressing enter */
            $parentdiv.on("keyup", function (e) {
                arrListColumns = [];
                if (e.keyCode === 13) {
                    loadGrid();
                }
            });
        }

       /*  function used to get the TableID and userID*/
        function getRecordID() {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/MEmployeeMaster/UpdateUser",
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
                        var obj = new VAS.AttachError($self.WindowNo,errorHtml);
                        obj.show();
                    }
                        
                    },
                
                error: function (eror) {
                    console.log(eror);
                }
            }); 
        }
       /* function used to get the records via ajax */
            function loadGrid() {
                var data = [];
                $.ajax({
                    url: VIS.Application.contextUrl + "VAS/MEmployeeMaster/GetUserList",
                    type: "GET",
                    data: {
                        searchKey: $textSearchEmployee.children('input').val(),
                        c_BPartnerID: c_BPartnerID
                   },
                    contentType: "application/json; charset=utf-8",
                    success: function (result) {
                        result = JSON.parse(result);
                        if (result != null && (result.userlist) !=null) {
                            $textEmployee.find('input').val(result.searchKey);
                            for (var i = 0; i < (result.userlist).length; i++) {                             
                                var line = {};
                                line['recid'] = result.userlist[i].recid;
                                line['Image'] = result.userlist[i].ImageUrl;
                                line['Name'] = result.userlist[i].Name;
                                line['Email'] = result.userlist[i].Email;
                                line['Mobile'] = result.userlist[i].Mobile;
                                line['SuperVisor'] = result.userlist[i].SupervisorName;
                                line['User_ID'] = result.userlist[i].UserID;
                                data.push(line);
                            }
                        }
                        dynInit(data);
                    },
                    error: function (eror) {
                        console.log(eror);
                    }
                });
                return data;
        };

      /* Function used to display the records on grid */
            function dynInit(data) {
                if (dGrid != null) {
                    dGrid.destroy();
                    dGrid = null;
                }
                if (arrListColumns.length == 0) {
                    arrListColumns.push({ field: "recid", caption:"recid", sortable: true, size: '10%', min: 100, hidden: true });
                    arrListColumns.push({
                        field: "Image", caption: VIS.Msg.getMsg("Image"), sortable: true, size: '5%', min: 70, hidden: false,
                        render: function (rec) {
                            var recImage = rec.Image;
                            if (recImage != null) {
                                return '<div class="text-center"><img class="vas-businessPartnerImg" alt=' + rec.Image + ' src="' + VIS.Application.contextUrl + rec.Image + '"></div>';
                            }
                            else  { 
                                return '<div class="vis-grid-row-td-icon-center vis-gridImageicon" ><div class="vis-app-user-img-wrap">  <i class="fa fa-user"></i><img src="" alt="profile image"> </div></div>';
                            }
                        }   
                    });
                    arrListColumns.push({ field: "Name", caption: VIS.Msg.getMsg("Name"), sortable: true, size: '10%', min: 130, hidden: false });
                    arrListColumns.push({ field: "Email", caption: VIS.Msg.getMsg("Email"), sortable: true, size: '10%', min: 130, hidden: false });
                    arrListColumns.push({ field: "Mobile", caption: VIS.Msg.getMsg("Mobile"), sortable: true, size: '10%', min: 130, hidden: false });
                    arrListColumns.push({ field: "SuperVisor", caption: VIS.Msg.getMsg("SuperVisor"), sortable: true, size: '9%', min: 130, hidden: false });
                    arrListColumns.push({ field: "User_ID", caption: VIS.Msg.getMsg("User_ID"), sortable: true, size: '7%', min: 130, hidden: false });

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


        };

        /* display function used to display the grid */
            this.display = function () {
                loadGrid();
            }


            this.Initialize = function () {
                // load by java script
                initializeComponent();
        }

        this.getRoot = function () {
            return $root;
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
            this.Initialize();
            this.frame.getContentGrid().append(this.getRoot());
            this.frame.getContentGrid().height(500);
            this.display();
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

        // Load form into VIS
        VAS.AttachUserToBP = VAS.AttachUserToBP
    }
})(VAS, jQuery);