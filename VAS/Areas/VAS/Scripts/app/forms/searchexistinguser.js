; VAS = window.VAS || {};

//self-invoking function
; (function (VAS, $) {
    // VAI061 08/12/2024 Form Class function fullnamespace
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
        var $parentdiv = null;
        var $inputDiv = null;
        var $buttonDiv = null;
        var $okBtn, $cancelBtn, $searchButton;
        var $employee = null;
        var $textEmployee = null;
        var $searchEmployee = null;
        var $textSearchEmployee = null;


        // VAI061 08/12/2024 Initialize UI Elements

        function initializeComponent() {
            $parentdiv = $("<div class='vis-parent'>");
            $inputDiv = $("<div class='vis-businessPartnerInput py-3'>");
            $buttonDiv = $("<div class='vis-businessPartnerButton'>");
            $root = $("<div class='vis-root'>");
            $upperDiv = $("<div>");
            lowerDiv = $("<div class='vis-lowerDiv'>");
            bottomDiv = $("<div class='vis-bottom'>");
            $searchButton = $('<button class="vis-businessPartnerSearch"><i class="fa fa-search" aria-hidden="true"></i></button>');
            $cancelBtn = $("<button class='vis-actionbtn'> "+ VIS.Msg.getMsg("Cancel") +" </button>");
            $okBtn = $("<button class='vis-actionbtn vis-disableBtn ' > " + VIS.Msg.getMsg("Ok") + " </button>");

           /* VAI061 08/12/2024 Employee textbox*/
            $employee = $('<div class="input-group vis-input-wrap mb-0" >');
            $textEmployee = $('<div class="vis-control-wrap"><input type="text" maxlength="80" disabled /><label>Employee</label></div>');
            $employee.append($textEmployee);

           /* VAI061 08/12/2024 SearchExistingUser textbox*/
            $searchEmployee = $('<div class="input-group vis-input-wrap mb-0" >');
            $textSearchEmployee = $('<div class="vis-control-wrap"><input type="text" maxlength="80" /><label>SearchExistingUser</label></div>');
            $searchEmployee.append($textSearchEmployee);
            $inputDiv.append($employee).append($searchEmployee)
            $buttonDiv.append($searchButton);
            $parentdiv.append($inputDiv).append($buttonDiv);
            $upperDiv.append($parentdiv);
            bottomDiv.append($okBtn).append($cancelBtn);
            $root.append($upperDiv).append(lowerDiv).append(bottomDiv);

            /*VAI061 08/12/2024 ok button use to call the getRecordID function */
            $okBtn.on(VIS.Events.onTouchStartOrClick, function () {
                getRecordID();

            });

          /*  VAI061 08/12/2024
              search button use to 
              search the records from grid 
          */
            $searchButton.on(VIS.Events.onTouchStartOrClick, function () {
                arrListColumns = [];
                loadGrid();

            });

           //VAI061 08/12/2024 close the form
            $cancelBtn.on(VIS.Events.onTouchStartOrClick, function () {
                $self.frame.close();
            });

           /*VAI061 08/12/2024 keyup used to search the record after pressing enter */
            $parentdiv.on("keyup", function (e) {
                arrListColumns = [];
                if (e.keyCode === 13) {
                    loadGrid();
                }
            });
        }

       /* VAI061 08/12/2024 function used to get the TableID and userID*/
        function getRecordID() {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/MEmployeeMaster/UpdateUser",
                type: "POST",
                data: {
                    userIds: UserIds,
                   c_BPartnerID: c_BPartnerID 
                },
                success: function (result) {
                    result = JSON.parse(result);
                    if (result.length == 0) {
                        $self.frame.close();
                    }
                    else {
                        var errorName = result[0].error;
                        VIS.ADialog.error(errorName);
                    }
                },
                error: function (eror) {
                    console.log(eror);
                }
            }); 
        }
       /*VAI061 08/12/2024 function used to get the records via ajax */
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

      /* VAI061 08/12/2024 Function used to display the records on grid */
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
                                return '<div class="text-center"><img class="vis-businessPartnerImg" alt=' + rec.Image + ' src="' + VIS.Application.contextUrl + rec.Image + '"></div>';
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

                    /*VAI061 08/12/2024 encode the tags */
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
                            $($okBtn).removeClass("vis-disableBtn");
                            UserIds.push(recordID)
                        },
                        onUnselect: function (event) {

                            if (dGrid.records.length > 0)
                                recordID = dGrid.get(event.recid).recid;
                            $($okBtn).addClass("vis-disableBtn");
                            UserIds = jQuery.grep(UserIds, function (value) {
                                return value != recordID;
                            });
                        }   
                    });
                }


        };

        /* VAI061 08/12/2024 display function used to display the grid */
            this.display = function () {
                loadGrid();
            }


            this.Initialize = function () {
                //VAI061 08/12/2024 load by java script
                initializeComponent();
        }

        this.getRoot = function () {
            return $root;
        };

        /*VAI061 08/12/2024 function used to deallocate the memory*/
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
            //VAI061 08/12/2024 Assign to this Varable
            this.frame = frame;//
            c_BPartnerID = frame.getRecord_ID();
            this.windowNo = windowNo;
            this.Initialize();
            this.frame.getContentGrid().append(this.getRoot());
            this.frame.getContentGrid().height(500);
            this.display();
        };
      
        //VAI061 08/12/2024 Must implement dispose
        VAS.AttachUserToBP.prototype.dispose = function () {
            this.disposeComponent();
           // VAI061 08/12/2024 call frame dispose function
            if (this.frame)
                this.frame.dispose();
            this.frame = null;
        };

      /*VAI061 08/12/2024 To set the width of frame */
        VAS.AttachUserToBP.prototype.setWidth = function () {
            return 900;
        };

        //VAI061 08/12/2024 Load form into VIS
        VAS.AttachUserToBP = VAS.AttachUserToBP
    }
})(VAS, jQuery);