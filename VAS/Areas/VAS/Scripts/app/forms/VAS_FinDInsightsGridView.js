/********************************************************
* Module Name    : VAS
* Purpose        : To show the homepage for finDInsightGW Form
* Class Used     : VAS.Gridinsight
* Chronological Development
* VAI-093         3 Oct 2024
******************************************************/

; VAS = window.VAS || {};
/* self-invoking function no need to call ,automatically call on pageload */
; (function (VAS, $) {
    /* Form Class function fullnamespace */
    VAS.VAS_FinDInsightsGridView = function () {
        var frame;
        this.windowNo;
        var $root;
        this.log = VIS.Logging.VLogger.getVLogger("finDInsightGW_Form");/* init log Class */
        var self = this;
        var gridDiv;
        var insightGrid = null;
        var orgId = null;
        var InsightGridCounter = 0;
        var pageSize = 50;
        var pageNo = 1;
        var TotalPageCount = 0;
        var Label = null;
        var frame = null;
        var tableName = null;
        var columnsData = null;
        var Data = null;
        var ulPaging = null;
        var liFirstPage = null;
        var liPrevPage = null;
        var cmbPage = null;
        var AD_Org_ID = null;
        var liCurrPage = null;
        var liNextPage = null;
        var liLastPage = null;
        var divPaging = null;





        /*
         for Initial design and message
        */
        function initializeComponent() {

            /* creating a root div */
            $root = $('<div style="height:100%">');

            /* creating busy indicator */
            createBusyIndicator();

            /* creating a new frame */
            frame = new VIS.CFrame();
            frame.setName(VIS.Utility.encodeText(Label));
            frame.setTitle(VIS.Utility.encodeText(Label));
            frame.setContent(self);
            frame.hideHeader(false);
            frame.show();

            setBusy(true);

            /* creating html structure */
            createinsighstructure();

            /*function used to the get the data for the grid */
            GetData(pageNo);

            /*create paging */
            createPageSettings();

            setTimeout(function () {
                setBusy(false);
            }, 1000)

        };

        /* function used to create basic HTML structure */
        function createinsighstructure() {

            mainContainerDiv = $('<div class="VAS-finDInsightGW-Grid-container h-100">');
            gridDiv = $('<div class="VAS-finDInsightGW-gridDiv "style="height:calc(100vh - 152px); width: 100%;">');
            divPaging = $('<div class="VAS-finDInsightGW-Paging mr-3" style="display:flex;align-items: center;justify-content:end;margin-top:10px">');
            mainContainerDiv.append(gridDiv).append(divPaging);
            $root.append(mainContainerDiv);


        };

        /* function used  to load grid */
        function loadGrid(colz, InsightData = []) {
            if (insightGrid != null) {
                insightGrid.destroy();
                insightGrid = null;
            }


            insightGrid = $(gridDiv).w2grid({
                name: "VAS_Grid_" + InsightGridCounter,
                recordHeight: 25,
                show: {
                    toolbar: false,  // indicates if toolbar is v isible
                    //columnHeaders: true,   // indicates if columns is visible
                    lineNumbers: true,  // indicates if line numbers column is visible
                    selectColumn: false,  // indicates if select column is visible
                    toolbarReload: false,   // indicates if toolbar reload button is visible
                    toolbarColumns: true,   // indicates if toolbar columns button is visible
                    toolbarSearch: false,   // indicates if toolbar search controls are visible
                    toolbarAdd: false,   // indicates if toolbar add new button is visible
                    toolbarDelete: true,   // indicates if toolbar delete button is visible
                    toolbarSave: true,   // indicates if toolbar save button is visible
                },
                multiSelect: false,
                columns: colz,
                records: InsightData,
                //onRefresh: function (event) {
                //    /* Set random colors for each row */
                //    var grid = this;
                //    grid.records.forEach(function (record) {
                //        var randomColor = getRandomColor();
                //        record.style = 'background-color: ' + randomColor + '; color: black;'; // Add text color for contrast
                //    });
                //},
            });
        }

        /* function to get random colors */
        //function getRandomColor() {
        //    var r = Math.floor(Math.random() * 156) + 200; // Values between 100 and 255
        //    var g = Math.floor(Math.random() * 156) + 200; // Values between 100 and 255
        //    var b = Math.floor(Math.random() * 156) + 200; // Values between 100 and 255
        //    return 'rgb(' + r + ',' + g + ',' + b + ')';
        //}

        /* function used to create column data */
        function createColumnData(data) {
            columnsData = data.map(name => ({
                field: name,
                caption: name,
                columnName: name,
                size: '10%',
                sortable: true,
                resizable: true

            }));
        }

        /**
         *  Ajax function used to get data for the grid
         * @param {any} pageNo
         */
        function GetData(pageNo) {
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetFinDataInsightGrid", { "tableName": tableName, "pageNo": pageNo, "pageSize": pageSize, "AD_Org_ID": orgId }, function (dr) {
                data = dr;
                if (data != null && data.length > 0) {
                    if (data.length > 1) {
                        columnsData = (data[0].ColName).split(',')
                        Data = data.shift();

                        /* function used to create fields for the columns for w2ui grid */
                        createColumnData(columnsData);
                        //Calculted Page count
                        TotalPageCount = Math.ceil(data[0].Count / pageSize);
                    } else {
                        columnsData = (data[0].ColName).split(',');
                        data.shift();
                        /* function used to create fields for the columns for w2ui grid */

                        createColumnData(columnsData);
                        VIS.ADialog.info("VAS_RecordNotFound");
                    }
                    
                }

                /* function used for loading the grid */
                loadGrid(columnsData, data);

                /*function used to reset the pagging after loading of grid */
                resetPageCtrls(TotalPageCount);
            });
        }


        /* function used for creating paging div */
        function createPageSettings() {

            ulPaging = $('<ul class="vis-statusbar-ul">');
            liFirstPage = $('<li style="opacity: 1;"><div><i class="vis vis-shiftleft" title="First Page" style="opacity: 0.6;"></i></div></li>');
            liPrevPage = $('<li style="opacity: 1;"><div><i class="vis vis-pageup" title="Page Up" style="opacity: 0.6;"></i></div></li>');
            cmbPage = $('<select>');
            liCurrPage = $('<li>').append(cmbPage);
            liNextPage = $('<li style="opacity: 1;"><div><i class="vis vis-pagedown" title="Page Down" style="opacity: 0.6;"></i></div></li>');
            liLastPage = $('<li style="opacity: 1;"><div><i class="vis vis-shiftright" title="Last Page" style="opacity: 0.6;"></i></div></li>');
            ulPaging.append(liFirstPage).append(liPrevPage).append(liCurrPage).append(liNextPage).append(liLastPage);

            divPaging.append(ulPaging);
            pageEvents();
        };

        /* function used to create click events for the paging arrows */
        function pageEvents() {
            liFirstPage.on("click", function () {
                if ($(this).css("opacity") == "1") {
                    pageNo = 1;
                    GetData(pageNo);
                }
            });
            liPrevPage.on("click", function () {
                if ($(this).css("opacity") == "1") {
                    pageNo--;
                    GetData(parseInt(pageNo));
                }
            });
            liNextPage.on("click", function () {
                if ($(this).css("opacity") == "1") {
                    pageNo++;
                    GetData(parseInt(pageNo));
                }
            });
            liLastPage.on("click", function () {
                if ($(this).css("opacity") == "1") {
                    pageNo = parseInt(cmbPage.find("Option:last").val());
                    GetData(pageNo);
                }
            });
            cmbPage.on("change", function () {
                pageNo = cmbPage.val();
                GetData(pageNo);
            });
        };

        /**
         *  function used to reset the paging according to the 
         *  current page count 
         * @param {any} TotalPageCount
         */
        function resetPageCtrls(TotalPageCount) {
            cmbPage.empty();
            if (TotalPageCount > 0) {
                for (var i = 0; i < TotalPageCount; i++) {
                    cmbPage.append($("<option value=" + (i + 1) + ">" + (i + 1) + "</option>"))
                }
                cmbPage.val(pageNo);


                if (TotalPageCount > pageNo) {
                    liNextPage.css("opacity", "1");
                    liLastPage.css("opacity", "1");
                }
                else {
                    liNextPage.css("opacity", "0.6");
                    liLastPage.css("opacity", "0.6");
                }

                if (pageNo > 1) {
                    liFirstPage.css("opacity", "1");
                    liPrevPage.css("opacity", "1");
                }
                else {
                    liFirstPage.css("opacity", "0.6");
                    liPrevPage.css("opacity", "0.6");
                }

                if (TotalPageCount == 1) {
                    liFirstPage.css("opacity", "0.6");
                    liPrevPage.css("opacity", "0.6");
                    liNextPage.css("opacity", "0.6");
                    liLastPage.css("opacity", "0.6");
                }
            }
            else {
                liFirstPage.css("opacity", "0.6");
                liPrevPage.css("opacity", "0.6");
                liNextPage.css("opacity", "0.6");
                liLastPage.css("opacity", "0.6");
            }
        };




        /* function used to create Busy Indicator */
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

        /* function used to set the properties passed */
        this.setProperties = function (TName, lbl, Dataobject, Counter, AD_Org_ID) {
            Label = lbl;
            tableName = TName;
            Dataobject = Dataobject;
            InsightGridCounter = Counter;
            orgId = VIS.Utility.Util.getValueOfInt(AD_Org_ID);
        }

        this.Initialize = function () {
            initializeComponent();
        };

        /* Privilized function */
        this.getRoot = function () {
            return $root;
        };

        /* function used to clear the variables and dispose the frame */
        this.disposeComponent = function () {
            self = null;
            if (insightGrid != null) {
                insightGrid.destroy();
                insightGrid = null;
            }
            if ($root)
                $root.remove();
            $root = null;
            gridDiv = null;
            $button1 = null;
            $button2 = null;
            insightGrid = null;
            orgId = null;
            Label = null;
            columnsData = null;
            Data = null;
            ulPaging = null;
            liFirstPage = null;
            liPrevPage = null;
            cmbPage = null;
            liCurrPage = null;
            liNextPage = null;
            liLastPage = null;
            divPaging = null;
            pageNo = null;
            TotalPageCount = null;
            this.getRoot = null;
            this.disposeComponent = null;
        };
    };



    //Must Implement with same parameter
    //self Invoking function
    VAS.VAS_FinDInsightsGridView.prototype.init = function (windowNo, frame) {
        //Assign to this Variable
        this.frame = frame;
        this.windowNo = windowNo;
        frame.hideHeader(true); //hide header
        var obj = this.Initialize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    //Must implement dispose
    VAS.VAS_FinDInsightsGridView.prototype.dispose = function () {
        /*CleanUp Code */
        //dispose this component
        this.disposeComponent();
        //call frame dispose function
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };


})(VAS, jQuery);