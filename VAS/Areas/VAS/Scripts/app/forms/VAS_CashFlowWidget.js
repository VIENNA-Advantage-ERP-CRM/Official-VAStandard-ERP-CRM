/************************************************************
 * Module Name    : VAS
 * Purpose        : This Widget is used to showcase Cash Flow in system
 * chronological  : Development
 * Created Date   : 20 November 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_CashFlowWidget = function () {
        var widgetID = 0;
        this.frame;
        this.windowNo;
        this.widgetInfo;
        var $bsyDiv;
        var $self = this;
        var $maindiv = null;
        var precision = 0;
        var vDifferentYearDataList = null;
        var $root = $('<div class="h-100 w-100 vas-cashFlow-background">');

        /** Load Design on Load */
        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) != 0 ? this.widgetInfo.AD_UserHomeWidgetID : $self.windowNo);

            GetColumnID();

            createBusyIndicator();

            $maindiv = $('<div id="VAS-BarLine-ERP_' + widgetID + '" class="vas-barline-cashFlow-container">');
            var MainHeadingComboDiv = $('<div class="d-flex justify-content-between vas-cashFlow-heading">');
            var HeadingDiv = $('<div class= "">' + VIS.Msg.getMsg("VAS_CashFlowWidget") + '</div>');

            // Create Control Div
            $DifferentYearDataListDiv = $('<div class="input-group vis-input-wrap">');
            /* parameters are: context, windowno., coloumn id, display type, DB coloumn name, Reference key, Is parent, Validation Code*/
            $DifferentYearDataListLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), widgetID, 0, VIS.DisplayType.List, "VAS_ExpRevProfitWidget", ColumnIds.AD_Reference_ID, false, null);
            // Parameters are: columnName, mandatory, isReadOnly, isUpdateable, lookup,display length
            vDifferentYearDataList = new VIS.Controls.VComboBox("VAS_ExpRevProfitWidget", true, false, true, $DifferentYearDataListLookUp, 20);
            vDifferentYearDataList.setValue("01");
            var $DifferentYearDataListControlWrap = $('<div class="vis-control-wrap">');
            $DifferentYearDataListDiv.append($DifferentYearDataListControlWrap);
            $DifferentYearDataListControlWrap.append(vDifferentYearDataList.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' '));
            $DifferentYearDataListDiv.append($DifferentYearDataListControlWrap);

            // Add Control into Maine Heading Div
            MainHeadingComboDiv.append(HeadingDiv).append($DifferentYearDataListDiv);

            // Add Maine Hedaeding dive into main div
            $maindiv.append(MainHeadingComboDiv);

            // Add maine div into Root
            $root.append($maindiv)

            // Fire Value Change Evenet for the year Control
            vDifferentYearDataList.fireValueChanged = function () {
                vDifferentYearDataList.setValue(vDifferentYearDataList.getValue());
                $bsyDiv[0].style.visibility = "visible";
                $maindiv.find('#vas_norecordcont_' + widgetID).remove();
                $self.getCashFlowvDetails();
            }
        };

        /** Get data and load */
        this.getCashFlowvDetails = function () {
            // Show busy indicator while data is loading
            $bsyDiv[0].style.visibility = "visible";

            // Define your AJAX request
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetCashFlowData",
                { "ListValue": vDifferentYearDataList.getValue() }, function (dr) {
                    var CashFlowData = dr;
                    var cashFlowArr = [];

                    // Remove existing canvas if exists
                    $root.find('canvas').remove();
                    precision = CashFlowData[0].stdPrecision;
                    if (CashFlowData == null && CashFlowData.length == 0) {
                        // If there is an error in the response, show "No record found" message
                        $maindiv.append('<div class="vas-igwidg-notfounddiv" id="vas_norecordcont_' + widgetID + '">' + VIS.Msg.getMsg("VAS_RecordNotFound") + '</div>')
                        $root.append($maindiv);
                    } else {
                        if (CashFlowData != null) {
                            cashFlowArr.push(CashFlowData[0].CashOutAmt);
                            cashFlowArr.push(CashFlowData[0].CashInAmt);
                        }
                        // Define static labels and colors
                        const lstLabel = [VIS.Msg.getMsg("VAS_CashOut"), VIS.Msg.getMsg("VAS_CashIn")]
                        const backgroundColors = [
                            'rgba(255, 99, 132, 0.7)',  // Red
                            'rgba(0, 187, 0, 0.7)',      // Green,
                        ];

                        // Prepare the data object for the chart
                        const data = {
                            labels: lstLabel, // Dynamic labels
                            datasets: [
                                {
                                    label: lstLabel,
                                    data: cashFlowArr,  // This must be an array of numbers
                                    borderColor: 'rgba(0,0,0,0)', // Transparent border color
                                    borderWidth: 0, // No border
                                    backgroundColor: backgroundColors, // Red background color
                                }
                            ],
                        };

                        // Check if the labels and data are correct
                        // console.log("Chart Data:", data);

                        // This is used to set the padding for the legend
                        const plugin = {
                            beforeInit: function (chart) {
                                const originalFit = chart.legend.fit;
                                chart.legend.fit = function fit() {
                                    originalFit.bind(chart.legend)();
                                    this.height += -5; // Adjust legend height if needed
                                }
                            }
                        };

                        // Define the chart configuration for BAR chart
                        const config = {
                            type: 'bar',
                            data: data,
                            options: {
                                responsive: true,
                                layout: {
                                    padding: 0
                                },
                                scales: {
                                    x: {
                                        grid: {
                                            display: true // Hide the grid lines on the x-axis   
                                        }
                                    },
                                    y: {
                                        grid: {
                                            display: false, // Hide the grid lines on the y-axis
                                            //beginAtZero: true,
                                        }
                                    },
                                },
                                plugins: {
                                    legend: {
                                        display: true,
                                        position: 'bottom', // Positioning the legend on the right
                                        labels: {
                                            generateLabels: function (chart) {
                                                const data = chart.data;
                                                const labels = data.labels;
                                                return labels.map((label, i) => ({
                                                    text: label,
                                                    fillStyle: backgroundColors[i] || '#000000',
                                                    strokeStyle: 'transparent',
                                                    lineWidth: 0
                                                }));
                                            },
                                            boxWidth: 10
                                        },
                                    },
                                    tooltip: {
                                        enabled: true,
                                        mode: 'index',
                                        callbacks: {
                                            label: function (tooltipItem) {
                                                const dataIndex = tooltipItem.dataIndex;
                                                const datasetIndex = tooltipItem.datasetIndex;
                                                const dataset = tooltipItem.chart.data.datasets[datasetIndex];
                                                const labels = tooltipItem.chart.data.labels;
                                                const value = dataset.data[dataIndex];
                                                return labels[dataIndex] + ': ' + parseFloat(value).toLocaleString(window.navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision });
                                            }
                                        }
                                    },
                                }
                            },
                            plugins: [plugin]
                        };


                        // Create a new canvas element and append it to the root
                        const canvas = $('<canvas class="vas-barline-ExRePr-canvas"></canvas>').css({
                            width: '100%',   // Set width to 100% (or any fixed value)
                            height: '300px'  // Set height for the canvas
                        });
                        var polarChart = $root.find('#VAS-BarLine-ERP_' + widgetID);
                        polarChart.append(canvas);

                        // Initialize the chart with the new data
                        const ctx = canvas[0].getContext('2d');
                        const chart = new Chart(ctx, config);
                        console.log("Chart initialized:", chart);

                        // Check if the chart was initialized correctly
                        if (!chart) {
                            console.error("Failed to initialize the chart.");
                        }
                    }

                    // Hide busy indicator once data is loaded
                    $bsyDiv[0].style.visibility = "hidden";
                });
        };


        /** Get List Reference ID for filter Data */
        var GetColumnID = function () {
            ColumnIds = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetColumnID", { "refernceName": "VAS_ExpRevProfitWidget" }, null);
        }

        /** Add busy Indicator in Root */
        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = "visible";
            $root.append($bsyDiv);
        };

        /** Get Root */
        this.getRoot = function () {
            return $root;
        };

        /** Refresh Widget Data */
        this.refreshWidget = function () {
            $bsyDiv[0].style.visibility = "visible";
            $maindiv.find('#vas_norecordcont_' + widgetID).remove();
            $self.getCashFlowvDetails();
        };
    };

    /**
     * Int Function for Intialize Form
     * @param {any} windowNo
     * @param {any} frame
     */
    VAS.VAS_CashFlowWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () {
            self.getCashFlowvDetails();
        }, 50);
    };

    /**
     * Widget Size change
     * @param {any} widget
     */
    VAS.VAS_CashFlowWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    /** refresh Widget Function */
    VAS.VAS_CashFlowWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /** Dispose Function */
    VAS.VAS_CashFlowWidget.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);
